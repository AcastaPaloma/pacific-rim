// Engine-free logic of the M1 slice scene (T-039b). Everything here is TEMPORARY scaffolding for the first playable:
// the kinematic kaiju stand-in integrates ScriptedKaijuBrain intents with the 2D harness constants, the impactor is a
// kinetic-energy calculation, the quake plan picks a mapped fault from the baked faults layer.
using System;
using System.Collections.Generic;
using Game.AI;
using Game.Core;
using Game.World.Seismic;

namespace Game.Slice
{
    /// <summary>Flag names of the slice. `slice.m1` gates the whole scene bootstrap; committed config keeps it off.</summary>
    public static class SliceFlags { public const string M1 = "slice.m1"; }

    /// <summary>A mech (or placeholder body) the kaiju can see. Plane coordinates: x = BayTM easting, y = BayTM northing (Unity z).</summary>
    public sealed class SliceTarget
    {
        public EntityId Id;
        public float X, Y, HeadingRad, Speed, Hp = 1f;
        public bool Alive = true;
    }

    /// <summary>Kinematic kaiju stand-in (TEMPORARY, like Units.Placeholder for the mech): a brain, a point body, constants from the 2D harness.</summary>
    public sealed class SliceKaijuAgent
    {
        public const float TurnRateRadPerS = 0.5f, AccelMps2 = 2.0f;   // KaijuHarness constants (BrainConfig has none)
        public const float MeleeDamage = 0.1f, MeleeCooldownS = 2.0f;

        readonly IKaijuBrain _brain;
        readonly BrainObservation _obs = new BrainObservation();
        BrainIntent _intent;
        float _cooldown;
        public BrainConfig Config;
        public EntityId Id;
        public float X, Y, HeadingRad, Speed, Hp = 1f;
        public BrainState State;
        public long Decisions, Hits;

        public SliceKaijuAgent(IKaijuBrain brain, EntityId id, ulong seed, float x, float y, float heading, float stepSeconds)
        {
            _brain = brain ?? throw new ArgumentNullException(nameof(brain));
            Id = id; X = x; Y = y; HeadingRad = heading;
            Config = BrainConfig.Default();
            Config.BrainPeriodSteps = Math.Max(1, (int)Math.Round(0.2 / stepSeconds));
            _brain.Reset(seed, Config);
        }

        static float Wrap(float a)
        {
            const float TwoPi = 6.2831853071795864f;
            while (a > MathF.PI) a -= TwoPi;
            while (a <= -MathF.PI) a += TwoPi;
            return a;
        }

        /// <summary>One fixed step. <paramref name="step"/> is the Core clock step. Returns the target hit this step (or null).</summary>
        public SliceTarget Step(long step, double timeS, float dt, IReadOnlyList<SliceTarget> targets)
        {
            int period = Config.BrainPeriodSteps;
            if ((step % period) == (long)(Id.Value % (ulong)period))
            {
                Fill(step, timeS, targets);
                _brain.Decide(_obs, ref _intent);
                State = _intent.State; Decisions++;
            }
            float dh = Wrap(_intent.DesiredHeadingRad - HeadingRad), maxTurn = TurnRateRadPerS * dt;
            HeadingRad = Wrap(HeadingRad + (dh > maxTurn ? maxTurn : (dh < -maxTurn ? -maxTurn : dh)));
            float sp = Math.Max(0f, Math.Min(1f, _intent.SpeedFrac));
            float ds = sp * Config.CruiseSpeedMps - Speed, ma = AccelMps2 * dt;
            Speed += ds > ma ? ma : (ds < -ma ? -ma : ds);
            X += MathF.Cos(HeadingRad) * Speed * dt; Y += MathF.Sin(HeadingRad) * Speed * dt;
            if (_cooldown > 0f) _cooldown -= dt;
            if (_intent.Action == BrainAction.Melee && _cooldown <= 0f)
                for (int i = 0; i < targets.Count; i++)
                {
                    var t = targets[i];
                    if (!t.Alive || t.Id.Value != _intent.TargetId.Value) continue;
                    float dx = t.X - X, dy = t.Y - Y;
                    if (MathF.Sqrt(dx * dx + dy * dy) > Config.MeleeReachM) break;
                    t.Hp -= MeleeDamage; if (t.Hp <= 0f) { t.Hp = 0f; t.Alive = false; }
                    _cooldown = MeleeCooldownS; Hits++;
                    return t;
                }
            return null;
        }

        void Fill(long step, double timeS, IReadOnlyList<SliceTarget> targets)
        {
            _obs.Step = step; _obs.TimeSeconds = timeS;
            _obs.Self = new SelfObs { Id = Id, PackId = 1, Pos = new Float2(X, Y), HeadingRad = HeadingRad, Speed = Speed, HpFrac = Hp, State = (byte)State, StateAgeSteps = 0 };
            int n = Math.Min(targets.Count, BrainContracts.MaxTargets);
            _obs.TargetCount = n; _obs.AllyCount = 0;
            for (int i = 0; i < n; i++)
            {
                var t = targets[i];
                float dx = t.X - X, dy = t.Y - Y;
                bool vis = t.Alive && dx * dx + dy * dy <= Config.SightRangeM * Config.SightRangeM;
                _obs.Targets[i] = new TargetObs { Id = t.Id, Pos = new Float2(t.X, t.Y), HeadingRad = t.HeadingRad, Speed = t.Speed, HpFrac = t.Hp, Alive = t.Alive, Visible = vis, MemoryAge = 0f };
            }
        }
    }

    /// <summary>Placeholder impactor: a rigid mass at speed. Energy is what Destruction.Apply takes (J).</summary>
    public static class SliceImpact
    {
        public static double KineticEnergyJ(double massKg, double speedMs) { return massKg > 0 && speedMs > 0 ? 0.5 * massKg * speedMs * speedMs : 0.0; }
    }

    /// <summary>Grid index over building anchors for "which building did this hit land on".</summary>
    public sealed class SliceBuildingIndex
    {
        const double Cell = 64.0;
        struct Entry { public BuildingId Id; public double X, Z, Radius; }
        readonly Dictionary<long, List<Entry>> _cells = new Dictionary<long, List<Entry>>();
        public int Count { get; private set; }

        static long Key(int cx, int cz) { return ((long)cx << 32) ^ (uint)cz; }
        static int C(double v) { return (int)Math.Floor(v / Cell); }

        public void Add(BuildingId id, double x, double z, double radiusM)
        {
            long k = Key(C(x), C(z));
            if (!_cells.TryGetValue(k, out var l)) _cells[k] = l = new List<Entry>();
            l.Add(new Entry { Id = id, X = x, Z = z, Radius = Math.Max(radiusM, 3.0) });
            Count++;
        }

        /// <summary>The building whose anchor is nearest (x,z) and within its radius + slackM; false when none.</summary>
        public bool TryFind(double x, double z, double slackM, out BuildingId id)
        {
            id = default; double best = double.MaxValue; bool found = false;
            int cx = C(x), cz = C(z), reach = 1 + (int)(slackM / Cell);
            for (int i = cx - reach; i <= cx + reach; i++)
                for (int j = cz - reach; j <= cz + reach; j++)
                    if (_cells.TryGetValue(Key(i, j), out var l))
                        foreach (var e in l)
                        {
                            double d = Math.Sqrt((e.X - x) * (e.X - x) + (e.Z - z) * (e.Z - z));
                            if (d <= e.Radius + slackM && d < best) { best = d; id = e.Id; found = true; }
                        }
            return found;
        }

        /// <summary>Nearest building anchor to (x,z) regardless of radius (scripted impactor target); false when empty.</summary>
        public bool TryNearest(double x, double z, out BuildingId id, out double ax, out double az)
        {
            id = default; ax = az = 0; double best = double.MaxValue; bool found = false;
            foreach (var l in _cells.Values)
                foreach (var e in l)
                {
                    double d = (e.X - x) * (e.X - x) + (e.Z - z) * (e.Z - z);
                    if (d < best) { best = d; id = e.Id; ax = e.X; az = e.Z; found = true; }
                }
            return found;
        }
    }

    /// <summary>Chooses the quake from the baked faults: the mapped system with the longest mapped trace, hypocentre at its middle vertex.</summary>
    public static class SliceQuakePlan
    {
        public const float DefaultMw = 6.9f;   // below the 7.2 default so a short mapped trace is rarely shortened; Assumed (slice feel)

        public static bool TryPlan(FaultTraceSet faults, uint eventId, uint seed, out QuakeScenario scenario)
        {
            scenario = default;
            if (faults == null) return false;
            FaultSystem best = null;
            foreach (var s in faults.Systems) if (s.VertexCount > 1 && (best == null || s.MappedLengthM > best.MappedLengthM)) best = s;
            if (best == null) return false;
            int v = best.VertexCount / 2;
            scenario = new QuakeScenario
            {
                EventId = eventId, Mw = DefaultMw, FaultId = best.FaultId, SlipSense = (byte)best.SlipSense,
                HypoX = (float)best.X[v], HypoZ = (float)best.Z[v], HypoDepthM = SeismicAssumptions.HypoDepthM,
                RuptureSpeedMs = SeismicAssumptions.RuptureSpeedMs, Seed = seed,
            };
            return true;
        }
    }
}
