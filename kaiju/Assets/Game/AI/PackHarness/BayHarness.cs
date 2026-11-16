// m007_bay harness (R-103 6, T-115 step 2): deterministic 2D kinematic Bay-scale simulation of mixed-species packs against patrolling or
// guarding mechs and F-044 infrastructure, writing brain_log@v2. Pure C#; separate assembly from M1's Game.AI.Harness (never edits it).
// Phase 1 map `m007_bay_synth@v1`: 60 km x 60 km. Map "flat" (default): no water, straight-line routes (the T-115/T-115b baselines). Map "water" (T-115c): synthetic depth/softness field,
// kaiju cannot enter cells deeper than 0.4 x height and move at 1/(1+1.5 soft) on soft ground; the pack layer and the solo arm route on it with GridRoutePlanner.
using System;
using System.Collections.Generic;
using System.IO;
using Game.AI.Pack;
using Game.Core;
using Game.World.Emergence;

namespace Game.AI.PackHarness
{
    /// <summary>Control and candidate ids of R-103 5.3 (the brain_id written to the log).</summary>
    public static class BayBrains
    {
        public const string Candidate = "scriptedpack@1", DirectCharge = "directcharge@1", NoFocus = "nofocus@1", Solo = "solo@1",
            NoRoles = "noroles@1", NoBait = "nobait@1", NearestInfra = "nearestinfra@1", RandomRole = "randomrole@1", Omniscient = "omniscient@1";
        public static readonly string[] All = { Candidate, DirectCharge, NoFocus, Solo, NoRoles, NoBait, NearestInfra, RandomRole, Omniscient };
    }

    public sealed class BayScenario
    {
        public string Name = "hunt";               // hunt | churn | raid | mixed | emerge  (logged as m007_bay_<name>); cross = T-115c channel-crossing check on the water map
        public string Composition = "LLH";         // L Strider, M Breacher, H Bastion (emerge: label only)
        public ulong Seed = 1;
        public string BrainId = BayBrains.Candidate;
        public SpeciesRole[] Species;              // index = SpeciesId (1..3); defaults to the R-103 0.2 numbers
        public float StepSeconds = 0.1f;
        public int DecisionEverySteps = 2;         // brain 5 Hz
        public int CoordinatorEverySteps = 10;     // pack layer 1 Hz
        public int LogEveryDecisions = 0;          // 0 = by scenario (1 for hunt/churn, 5 otherwise)
        public double DurationSeconds = 0;         // 0 = by scenario
        public float CallRangeM = 5000f;
        public float HearingRangeM = 4000f;        // mech footsteps: the board hears them (source "heard"); sight stays 1200 m
        public float MechSpeed = 3f, MechCruise = 9.12f, MechRange = 300f, MechDps = 6f, MechTraverse = 0.6f, MechLockSeconds = 3f;
        public float MeleeDamageToMech = 25f, MechHpAbs = 100f, MeleeCooldown = 2f, KaijuHpRef = 400f;
        public float InfraDamageRate = 1f;         // hp/s per kaiju at the target (the coordinator plans with PackConfig.RaidDamageRate)
        public string Variant = "";               // T-115f: "" = the T-115..T-115e scenarios (unchanged). "mv1" = hunt_mv@v1: mechs patrol as one squad that starts inside kaiju sight, pack spread 500 m (see MODULE note in BuildMechs)
        public string Map = "flat";               // flat | water (T-115c synthetic Bay with a depth / soft-ground field) | real (T-115d m007_bay_real@v1: Field = the Composition adapter over the T-102 raster)
        public Game.AI.Pack.IBayCostField Field;   // required for Map = real
        public BayLayout Layout;                   // null = by map (Synthetic, or Real for Map = real)
        public int MechCountOverride = 0;          // T-124: 0 = by scenario (unchanged); > 0 = exactly that many mechs (M-008 harness: 2 bay, 1 reference)
        public bool PlanRoutes = true;             // water map: pack layer and solo arm route with GridRoutePlanner (false = straight-line control)
        public PackConfig Pack = PackConfig.Bay();
        public EmergenceOptions Emergence = new EmergenceOptions { SiteCount = 5 };

        public double Duration => DurationSeconds > 0 ? DurationSeconds : Name == "hunt" ? 600 : Name == "churn" ? 900 : Name == "raid" || Name == "cross" ? 6000 : Name == "mixed" ? 3600 : 1200;
        public int LogEvery => LogEveryDecisions > 0 ? LogEveryDecisions : (Name == "hunt" || Name == "churn" ? 1 : 5);
        public string ScenarioId => "m007_bay_" + Name;
        public float Mv1SquadDistM = 1000f;
        public float[] MixedSpawn;                // T-113c: mixed scenario spawn override (BayTM metres, x,y); Variant "mx2" uses the Layout's Mixed2 spawn, "" the original MixedX/MixedY
        public float Mv2KaijuSpacingM = 1500f, Mv2PicketAheadM = 900f, Mv2MechSpacingM = 1400f, Mv2PatrolM = 500f;
        public bool Mv2 => Variant == "mv2";
        public bool Mv1 => Variant == "mv1";
    }

    /// <summary>T-115e: a cost field seen from a local frame whose origin is (ox, oy) BayTM metres (integer), so the planner works on small coordinates.</summary>
    public sealed class LocalFrameField : IBayCostField
    {
        readonly IBayCostField _f;
        public LocalFrameField(IBayCostField f, double ox, double oy) { _f = f; OriginX = (float)(f.OriginX - ox); OriginY = (float)(f.OriginY - oy); }
        public float OriginX { get; } public float OriginY { get; }
        public float CellM => _f.CellM; public int Nx => _f.Nx; public int Ny => _f.Ny;
        public float DepthM(int ix, int iy) => _f.DepthM(ix, iy);
        public float Softness(int ix, int iy) => _f.Softness(ix, iy);
    }

    public sealed class BayResult
    {
        public long Steps, Decisions, Hits, MechDeaths, KaijuDeaths, InfraDestroyed, BlockedSteps;
        public int Kaiju, Packs;
    }

    public sealed class BayHarness
    {
        sealed class K
        {
            public EntityId Id; public int Sp; public char Letter; public SpeciesRole S; public IKaijuBrain Brain; public BrainConfig Cfg;
            public float X, Y, Heading, Speed, HpAbs, HpMax, Cooldown; public bool Alive, Emerged; public double EmergeAt;
            public BrainIntent Intent; public BrainState State; public int PackIdx = -1; public double JoinWaitUntil;
            public double[] LastSeen; public float[] LX, LY, LH, LHp; public bool[] Ever;
            public PackRole Role = PackRole.Free; public int Infra = -1; public float InfraDmg;
            public bool WasEligible;
        }

        sealed class M
        {
            public EntityId Id; public float X, Y, Heading, Hp; public bool Alive = true; public float WX, WY, HX, HY, Aim;
            public float OX, OY; public int Aiming = -1; public double Lock; public double RespawnAt; public int Cluster = -1; public bool Guard;
        }

        sealed class I : IInfrastructureSource
        {
            public readonly List<Node> N = new List<Node>();
            public struct Node { public EntityId Id; public float X, Y, Hp, HpMax; public byte Class; public bool Alive, Defended; public string ClassName; }
            public int Count => N.Count;
            public InfraTarget Get(int i) => new InfraTarget { Id = N[i].Id, Pos = new Float2(N[i].X, N[i].Y), Class = N[i].Class, Hp = N[i].Hp, Alive = N[i].Alive };
        }

        sealed class Pk { public PackCoordinator C; public readonly List<K> Members = new List<K>(); public int Id; public PackGoal LastGoal = PackGoal.None; public EntityId LastRaid; public PackRole[] Prev = new PackRole[8]; public EntityId[] PrevId = new EntityId[8]; public int PrevN; public bool Active; }

        readonly BayScenario _sc;
        readonly TextWriter _w;
        readonly BayLog _log;
        readonly List<K> _k = new List<K>();
        readonly List<M> _m = new List<M>();
        readonly I _infra = new I();
        readonly List<Pk> _packs = new List<Pk>();
        readonly PackDirectory _dir = new PackDirectory();
        readonly BrainObservation _obs = new BrainObservation();
        readonly PackMemberState[] _states = new PackMemberState[16];
        readonly Rng _rng;
        BayResult _res = new BayResult();
        bool _packLayer, _omniscient, _noFormation;
        IBayCostField _field;
        BayLayout _lay;
        GridRoutePlanner _planner;
        double _now;
        // T-115e local frame: every simulated position is kept relative to an integer-metre scenario origin (the spawn), so float32 arithmetic always works on small numbers and
        // a pure translation of the scenario is bit-identical. Only the cost-field lookups and the log writer see absolute BayTM coordinates (added back in double).
        readonly double _ox, _oy;
        float AX(float x) => (float)(x + _ox);
        float AY(float y) => (float)(y + _oy);

        /// <summary>T-124: called at the end of every simulated step (step index, simulated seconds). Null = M1..T-115 behaviour, bit for bit. The M-008 Bay performance harness uses it to interleave the Unity frame loop.</summary>
        public Action<BayHarness, long, double> AfterStep;
        /// <summary>T-124c (default off, null): called after each of the four step phases (0 world+perceive, 1 pack coordinator, 2 brain decisions, 3 kaiju motion+fire) with
        /// (harness, step, phase). Lets a frame loop interleave frames between the phases so the 10 Hz brain step is spread over the 60 Hz frames (amortisation).
        /// No effect on the simulation: the hook only observes.</summary>
        public Action<BayHarness, long, int> PhaseHook;
        /// <summary>T-124c profile: wall ms of the phases of the last step (0 world+perceive, 1 pack coordinator, 2 decisions, 3 motion+fire).</summary>
        public readonly double[] PhaseMsLast = new double[4];
        public GridRoutePlanner Planner => _planner;
        public BayResult Result => _res;
        public int KaijuCount => _k.Count;
        public int MechCount => _m.Count;
        public int InfraCount => _infra.N.Count;
        /// <summary>Absolute BayTM position (x east, y north as the harness names it; z in World terms), double. Read-only view for drivers.</summary>
        public bool TryGetKaiju(int i, out ulong id, out int species, out double x, out double y, out float heading, out bool alive)
        {
            id = 0; species = 0; x = y = 0; heading = 0; alive = false;
            if (i < 0 || i >= _k.Count) return false;
            var k = _k[i]; id = k.Id.Value; species = k.Sp; x = k.X + _ox; y = k.Y + _oy; heading = k.Heading; alive = k.Alive && k.Emerged; return true;
        }
        public bool TryGetMech(int i, out ulong id, out double x, out double y, out float heading, out bool alive)
        {
            id = 0; x = y = 0; heading = 0; alive = false;
            if (i < 0 || i >= _m.Count) return false;
            var m = _m[i]; id = m.Id.Value; x = m.X + _ox; y = m.Y + _oy; heading = m.Heading; alive = m.Alive; return true;
        }
        public bool TryGetInfra(int i, out double x, out double y, out float hp, out bool alive)
        {
            x = y = 0; hp = 0; alive = false;
            if (i < 0 || i >= _infra.N.Count) return false;
            var n = _infra.N[i]; x = n.X + _ox; y = n.Y + _oy; hp = n.Hp; alive = n.Alive; return true;
        }

        public BayHarness(BayScenario sc, TextWriter w)
        {
            _sc = sc; _w = w; _log = new BayLog(w); _rng = new Rng(sc.Seed * 0x9E3779B97F4A7C15UL + 777UL);
            if (sc.Species == null) sc.Species = new[] { default(SpeciesRole), SpeciesRole.Strider, SpeciesRole.Breacher, SpeciesRole.Bastion };
            string b = sc.BrainId;
            _packLayer = b == BayBrains.Candidate || b == BayBrains.NoRoles || b == BayBrains.NoBait || b == BayBrains.NearestInfra || b == BayBrains.RandomRole || b == BayBrains.Omniscient;
            _omniscient = b == BayBrains.Omniscient;
            _lay = sc.Layout ?? BayLayout.For(sc.Map);
            if (sc.Name == "emerge") { _ox = Math.Round(_lay.TraceX[30]); _oy = Math.Round(_lay.TraceZ[30]); }
            else if (sc.Name == "cross") { _ox = Math.Round((double)_lay.CrossX); _oy = Math.Round((double)_lay.CrossY); }
            else if (sc.Name == "raid") { _ox = Math.Round((double)_lay.RaidX); _oy = Math.Round((double)_lay.RaidY); }
            else if (sc.Name == "mixed")
            {
                _ox = Math.Round((double)_lay.MixedX); _oy = Math.Round((double)_lay.MixedY);
                if (sc.MixedSpawn != null) { _ox = sc.MixedSpawn[0]; _oy = sc.MixedSpawn[1]; }
                else if (sc.Variant == "mx2" && _lay.Mixed2X != 0f) { _ox = Math.Round((double)_lay.Mixed2X); _oy = Math.Round((double)_lay.Mixed2Y); }
            }
            else { _ox = Math.Round((double)_lay.HuntX); _oy = Math.Round((double)_lay.HuntY); }
            if (sc.Map == "real" && sc.Field == null) throw new ArgumentException("Map real needs Field (BayRasterCostField)");
            if (sc.Map == "water" || sc.Map == "real") { _field = sc.Field ?? new SyntheticBayField(); if (sc.PlanRoutes) _planner = new GridRoutePlanner(new LocalFrameField(_field, _ox, _oy)); }
            BuildInfra();
            BuildScenario();
        }

        // ------------------------------------------------------------------------------------------ setup

        void BuildInfra()
        {
            // synthetic Bay (R-103 6.2): 2 bridges, 3 power nodes, 4 port cranes (one cluster), 1 airport; Port-SFO about 21 km. `cross` = T-115c route check (one raid target beyond the channel).
            foreach (var n in _sc.Name == "cross" ? _lay.CrossInfra : _lay.Infra) Add(n.Class, n.C, (float)(n.X - _ox), (float)(n.Y - _oy), n.Hp);
        }

        void Add(string cls, byte c, float x, float y, float hp)
        {
            _infra.N.Add(new I.Node { Id = EntityId.FromValue((ulong)(201 + _infra.N.Count)), X = x, Y = y, Hp = hp, HpMax = hp, Class = c, ClassName = cls, Alive = true });
        }

        void BuildScenario()
        {
            string comp = _sc.Composition;
            var list = new List<char>();
            if (_sc.Name == "emerge") { PlanEmergence(); }
            else
            {
                foreach (char ch in comp) list.Add(ch);
                float sx = 0f, sy = 0f;   // the scenario origin is the spawn (local frame)

                for (int i = 0; i < list.Count; i++)
                    NewKaiju(list[i], sx + _rng.Range(-150f, 150f), sy + (i - (list.Count - 1) * 0.5f) * (_sc.Mv1 ? 500f : _sc.Mv2 ? _sc.Mv2KaijuSpacingM : 200f) + _rng.Range(-50f, 50f), 0.0, 0.3f);
                if (_sc.Name != "raid" && _sc.Name != "cross") BuildMechs();
                var pk = NewPack();
                foreach (var k in _k) Join(k, pk);
            }
            _res.Kaiju = _k.Count;
        }

        void PlanEmergence()
        {
            int n = 61; var x = new double[n]; var z = new double[n]; var arc = new double[n]; var br = new bool[n - 1];
            for (int i = 0; i < n; i++) { x[i] = _lay.TraceX[i] - _ox; z[i] = _lay.TraceZ[i] - _oy; arc[i] = i == 0 ? 0 : arc[i - 1] + Math.Sqrt(500.0 * 500.0 + Math.Pow(z[i] - z[i - 1], 2)); }
            var trace = new EmergenceTrace(1, x, z, arc, br);
            var tg = new TargetList(); foreach (var nd in _infra.N) tg.Items.Add(new EmergenceTarget(nd.X, nd.Y, 1f));
            var req = new EmergenceRequest { EventId = 1, Mw = 7.0f, Seed = (uint)(_sc.Seed & 0xFFFFFFFF), VertexStart = 0, VertexEnd = n - 1, RuptureEndS = 0f };
            var plan = EmergencePlanner.Plan(req, trace, tg, _sc.Emergence);
            if (plan == null) return;
            char[] letter = { ' ', 'L', 'M', 'H' };
            foreach (var s in plan.Sites)
                foreach (var kk in s.Kaiju)
                    NewKaiju(letter[kk.SpeciesId], (float)kk.X, (float)kk.Z, kk.TimeS, kk.HeadingRad);
        }

        sealed class TargetList : IEmergenceTargets
        {
            public readonly List<EmergenceTarget> Items = new List<EmergenceTarget>();
            public int Count => Items.Count; public EmergenceTarget Get(int i) => Items[i];
        }

        void NewKaiju(char letter, float x, float y, double at, float heading)
        {
            int sp = letter == 'L' ? 1 : letter == 'M' ? 2 : 3;
            SpeciesRole s = _sc.Species[sp];
            var cfg = BrainConfig.Default();
            cfg.CruiseSpeedMps = s.CruiseMps; cfg.MeleeReachM = s.MeleeReachM; cfg.StandoffM = s.StandoffM; cfg.EngageRadiusM = s.StandoffM * 1.2f;
            cfg.AllyAwarenessM = _sc.CallRangeM; cfg.AssemblyRadiusM = Math.Max(375f, s.StandoffM * 1.5f);
            cfg.BrainPeriodSteps = _sc.DecisionEverySteps;
            if (_sc.BrainId == BayBrains.DirectCharge) cfg.DirectCharge = true;
            if (_sc.BrainId == BayBrains.NoFocus) cfg.WeightFocus = 0f;
            var k = new K
            {
                Id = EntityId.FromValue((ulong)(_k.Count + 1)), Sp = sp, Letter = letter, S = s, Cfg = cfg, X = x, Y = y, Heading = heading, Alive = true,
                HpMax = _sc.KaijuHpRef * Math.Max(0.5f, s.HpRel), EmergeAt = at, Emerged = at <= 0.0,
            };
            k.HpAbs = k.HpMax;
            k.Brain = new Game.AI.ScriptedKaijuBrain();
            k.Brain.Reset(_sc.Seed * 31UL + (ulong)_k.Count, cfg);
            if (_packLayer) k.Brain = new PackMemberBrain(k.Brain, _dir, _sc.Pack);
            _k.Add(k);
        }

        void BuildMechs()
        {
            int n = (_sc.Seed % 2UL == 1UL) ? 3 : 4;
            bool guard = _sc.Name == "mixed";
            if (guard) n = 3;
            if (_sc.MechCountOverride > 0) n = _sc.MechCountOverride;
            if (_sc.Mv2 && !guard)
            {
                // hunt_mv@v2 (T-115f): a picket line facing the pack. Each mech holds a post (500 m patrol) 900 m ahead of the pack axis and 1400 m from its neighbour, the pack members are
                // 1500 m apart, so each kaiju has a different mech in its own 1200 m sight, while the pack board (4 km hearing, shared) sees them all.
                for (int j = 0; j < n; j++)
                {
                    var m = new M { Id = EntityId.FromValue((ulong)(101 + j)), Hp = 1f };
                    m.X = _sc.Mv2PicketAheadM + _rng.Range(-100f, 100f); m.Y = (j - (n - 1) * 0.5f) * _sc.Mv2MechSpacingM + _rng.Range(-150f, 150f); m.HX = m.X; m.HY = m.Y; NewWaypoint(m);
                    m.Heading = (float)Math.Atan2(m.WY - m.Y, m.WX - m.X); m.Aim = m.Heading;
                    _m.Add(m);
                }
                return;
            }
            if (_sc.Mv1 && !guard)
            {
                // hunt_mv@v1 (T-115f): the mechs are one patrol squad (R-103 6.3 mutual support: guards answer calls within 10 km, so mechs of one force are within a few hundred metres of each other),
                // squad centre 1000 m from the pack centre (inside the 1200 m sight of every member), members within 350 m of the centre and following a shared waypoint at patrol speed.
                float ang0 = (float)(_rng.NextDouble() * 2 * Math.PI);
                _sqHX = (float)Math.Cos(ang0) * _sc.Mv1SquadDistM; _sqHY = (float)Math.Sin(ang0) * _sc.Mv1SquadDistM;
                for (int j = 0; j < n; j++)
                {
                    var m = new M { Id = EntityId.FromValue((ulong)(101 + j)), Hp = 1f };
                    float a = (float)(_rng.NextDouble() * 2 * Math.PI), r = 350f * (float)Math.Sqrt(_rng.NextDouble());
                    m.OX = (float)Math.Cos(a) * r; m.OY = (float)Math.Sin(a) * r; m.X = _sqHX + m.OX; m.Y = _sqHY + m.OY; m.HX = _sqHX; m.HY = _sqHY;
                    _m.Add(m);
                }
                SquadWaypoint();
                foreach (var m in _m) { m.Heading = (float)Math.Atan2(m.WY - m.Y, m.WX - m.X); m.Aim = m.Heading; }
                return;
            }
            for (int j = 0; j < n; j++)
            {
                var m = new M { Id = EntityId.FromValue((ulong)(101 + j)), Hp = 1f };
                if (guard)
                {
                    // 3 guards split across 2 defended clusters 7 km apart: Port cranes (2 mechs) and the east power node (1 mech)
                    int c = j < 2 ? 0 : 1; m.Cluster = c; m.Guard = true;
                    var node = c == 0 ? _infra.N[_lay.GuardCluster0[0]] : _infra.N[_lay.GuardCluster1[0]];
                    m.HX = node.X + (j == 1 ? 200f : -150f); m.HY = node.Y + 150f * (j - 1);
                    m.X = m.HX; m.Y = m.HY;
                    Mark(c == 0 ? _lay.GuardCluster0 : _lay.GuardCluster1);
                }
                else
                {
                    float ang = (float)(_rng.NextDouble() * 2 * Math.PI), r = 3000f + _rng.Range(-300f, 300f);
                    m.X = (float)(_lay.HuntX - _ox) + (float)Math.Cos(ang) * r; m.Y = (float)(_lay.HuntY - _oy) + (float)Math.Sin(ang) * r; m.HX = m.X; m.HY = m.Y; NewWaypoint(m);
                }
                m.Heading = (float)Math.Atan2(m.WY - m.Y, m.WX - m.X); m.Aim = m.Heading;
                _m.Add(m);
            }
        }

        float DepthAt(float lx, float ly) { double x = lx + _ox, y = ly + _oy; int ix = (int)((x - _field.OriginX) / _field.CellM), iy = (int)((y - _field.OriginY) / _field.CellM); return x < _field.OriginX || y < _field.OriginY || ix >= _field.Nx || iy >= _field.Ny ? 0f : _field.DepthM(ix, iy); }
        float SoftAt(float lx, float ly) { double x = lx + _ox, y = ly + _oy; int ix = (int)((x - _field.OriginX) / _field.CellM), iy = (int)((y - _field.OriginY) / _field.CellM); return x < _field.OriginX || y < _field.OriginY || ix >= _field.Nx || iy >= _field.Ny ? 0f : _field.Softness(ix, iy); }

        void Mark(int[] idx) { foreach (int i in idx) { var n = _infra.N[i]; n.Defended = true; _infra.N[i] = n; } }
        float _sqHX, _sqHY, _sqWX, _sqWY;
        void SquadWaypoint()
        {
            float a = (float)(_rng.NextDouble() * 2 * Math.PI), r = 2000f * (float)Math.Sqrt(_rng.NextDouble());
            _sqWX = _sqHX + (float)Math.Cos(a) * r; _sqWY = _sqHY + (float)Math.Sin(a) * r;
            foreach (var m in _m) { m.WX = _sqWX + m.OX; m.WY = _sqWY + m.OY; }
        }
        void NewWaypoint(M m) { float a = (float)(_rng.NextDouble() * 2 * Math.PI), r = (_sc.Mv2 ? _sc.Mv2PatrolM : 2000f) * (float)Math.Sqrt(_rng.NextDouble()); m.WX = m.HX + (float)Math.Cos(a) * r; m.WY = m.HY + (float)Math.Sin(a) * r; }

        Pk NewPack()
        {
            var p = new Pk { Id = _packs.Count + 1, C = new PackCoordinator(_infra, _planner) };
            p.C.Reset(_sc.Seed * 17UL + (ulong)p.Id, _sc.Pack);
            switch (_sc.BrainId)
            {
                case BayBrains.NoRoles: p.C.Control = PackControl.NoRoles; break;
                case BayBrains.NoBait: p.C.Control = PackControl.NoBait; break;
                case BayBrains.NearestInfra: p.C.Control = PackControl.NearestInfra; break;
                case BayBrains.RandomRole: p.C.Control = PackControl.RandomRole; break;
            }
            _packs.Add(p); _res.Packs = _packs.Count;
            return p;
        }

        void Join(K k, Pk p) { if (k.PackIdx >= 0) { _packs[k.PackIdx].Members.Remove(k); } k.PackIdx = p.Id - 1; p.Members.Add(k); if (_packLayer) _dir.Assign(k.Id, p.C); }

        // ------------------------------------------------------------------------------------------ run

        public BayResult Run()
        {
            float dt = _sc.StepSeconds; long total = (long)Math.Round(_sc.Duration / dt);
            int logPeriodSteps = _sc.DecisionEverySteps * _sc.LogEvery;
            _log.Header(_sc, logPeriodSteps, _packLayer);
            for (long step = 0; step < total; step++)
            {
                _now = step * (double)dt;
                long pt0 = System.Diagnostics.Stopwatch.GetTimestamp();
                EmergenceTick(step);
                for (int j = 0; j < _m.Count; j++) StepMech(_m[j], dt);
                for (int i = 0; i < _k.Count; i++) if (_k[i].Alive && _k[i].Emerged) Perceive(_k[i]);
                PhaseEnd(0, ref pt0, step);
                if (step % _sc.CoordinatorEverySteps == 0) { if (_sc.Name == "emerge") Formation(step); if (_packLayer) TickPacks(step); }
                PhaseEnd(1, ref pt0, step);
                bool decStep = step % _sc.DecisionEverySteps == 0;
                bool logStep = step % logPeriodSteps == 0;
                if (logStep) LogWorld(step);
                if (decStep)
                {
                    for (int i = 0; i < _k.Count; i++)
                    {
                        K k = _k[i]; if (!k.Alive || !k.Emerged) continue;
                        FillObs(k, step);
                        BrainState prev = k.State;
                        if (!_packLayer) SoloDecide(k); else k.Brain.Decide(_obs, ref k.Intent);
                        k.State = k.Intent.State;
                        _res.Decisions++;
                        if (logStep) _log.Decision(step, _now, k.Id.Value, k.Sp, _packLayer ? PackIdOf(k) : 0, RoleOf(k), GoalOf(k), true, AX(k.X), AY(k.Y), k.Heading, k.Speed, k.HpAbs > 0 ? k.HpAbs / k.HpMax : 0f, k.Intent, prev, _obs, BoardAge(k), BoardSourceName(k));
                    }
                }
                PhaseEnd(2, ref pt0, step);
                for (int i = 0; i < _k.Count; i++) if (_k[i].Alive && _k[i].Emerged) StepKaiju(_k[i], dt, step);
                MechFire(step, dt);
                PhaseEnd(3, ref pt0, step);
                _res.Steps++;
                AfterStep?.Invoke(this, step, _now);
            }
            _w.Flush();
            return _res;
        }

        void PhaseEnd(int phase, ref long t0, long step)
        {
            long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            PhaseMsLast[phase] = (t1 - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            PhaseHook?.Invoke(this, step, phase);
            t0 = System.Diagnostics.Stopwatch.GetTimestamp();   // the hook's own time (frames) is not charged to the next phase
        }

        int PackIdOf(K k) => k.PackIdx >= 0 ? k.PackIdx + 1 : 0;

        // ------------------------------------------------------------------------------------------ emergence and formation (R-103 4.5)

        void EmergenceTick(long step)
        {
            if (_sc.Name != "emerge") return;
            for (int i = 0; i < _k.Count; i++)
            {
                K k = _k[i];
                if (k.Emerged || _now < k.EmergeAt) continue;
                k.Emerged = true; k.JoinWaitUntil = _now + 180.0;
                // call at emergence: heard by every already-emerged kaiju within call range, delay = distance / 343 (R-103 4.1)
                var heard = new List<ulong>(); float far = 0f;
                for (int j = 0; j < _k.Count; j++)
                {
                    K o = _k[j]; if (o == k || !o.Emerged || !o.Alive) continue;
                    float d = Dist(k.X, k.Y, o.X, o.Y);
                    if (d <= _sc.CallRangeM) { heard.Add(o.Id.Value); if (d > far) far = d; }
                }
                _log.Call(step, _now, k.Id.Value, heard, far / 343f, far, _sc.CallRangeM);
            }
        }

        // Co-emerged members within call range auto-form a valid pack (size 2-5, a species with fit >= 0.6 for Anchor/Flanker/Raider); singletons wait
        // up to 180 s, may be adopted by any pack within range with size < 5, and otherwise fly solo (R-103 4.5 step 2).
        void Formation(long step)
        {
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < _k.Count; i++)
                {
                    K k = _k[i]; if (!k.Alive || !k.Emerged) continue;
                    if (k.PackIdx >= 0 && _packs[k.PackIdx].Members.Count >= 2) continue;
                    // adoption by an existing pack within call range
                    Pk host = null; float best = float.MaxValue;
                    foreach (var p in _packs)
                    {
                        if (p == (k.PackIdx >= 0 ? _packs[k.PackIdx] : null)) continue;
                        int live = 0; float dmin = float.MaxValue;
                        foreach (var m in p.Members) if (m.Alive && m.Emerged) { live++; float d = Dist(k.X, k.Y, m.X, m.Y); if (d < dmin) dmin = d; }
                        if (live >= 2 && live < 5 && dmin <= _sc.CallRangeM && dmin < best) { best = dmin; host = p; }
                    }
                    if (host != null) { if (k.PackIdx >= 0) { var old = _packs[k.PackIdx]; old.Members.Remove(k); } Join(k, host); continue; }
                    // form with unassigned neighbours within call range
                    if (k.PackIdx >= 0) continue;   // already a (singleton) pack of its own
                    var group = new List<K> { k };
                    foreach (var o in _k) if (o != k && o.Alive && o.Emerged && o.PackIdx < 0 && Dist(k.X, k.Y, o.X, o.Y) <= _sc.CallRangeM && group.Count < 5) group.Add(o);
                    if (group.Count >= 2) { var np = NewPack(); foreach (var g in group) Join(g, np); }
                    else if (_now >= k.JoinWaitUntil) { var np = NewPack(); Join(k, np); }   // gave up waiting: solo pack of one
                }
        }

        // ------------------------------------------------------------------------------------------ pack tick

        void TickPacks(long step)
        {
            foreach (var p in _packs)
            {
                int n = 0;
                foreach (var m in p.Members)
                {
                    if (!m.Emerged) continue;
                    _states[n++] = new PackMemberState { Id = m.Id, Species = m.S, Pos = new Float2(m.X, m.Y), HpFrac = m.HpAbs > 0 ? m.HpAbs / m.HpMax : 0f, Alive = m.Alive, State = m.State, TargetId = m.Intent.TargetId };
                }
                // board: members' own sight, plus the heard mechs inside hearing range of any member
                foreach (var m in p.Members)
                {
                    if (!m.Alive || !m.Emerged) continue;
                    FillObs(m, step);
                    p.C.Board.Ingest(_obs);
                    for (int j = 0; j < _m.Count; j++)
                    {
                        M me = _m[j];
                        float d = Dist(m.X, m.Y, me.X, me.Y);
                        bool alive = me.Alive;
                        if (_omniscient) { p.C.Board.Observe(me.Id, new Float2(me.X, me.Y), default, me.Hp, alive, _now, m.Id, BoardSource.Map); }
                        else if (d <= _sc.HearingRangeM) p.C.Board.Observe(me.Id, new Float2(me.X, me.Y), default, me.Hp, alive, _now, m.Id, d <= m.Cfg.SightRangeM ? BoardSource.Own : BoardSource.Heard);
                    }
                }
                if (n == 0) continue;
                p.C.Update(_now, _states, n);
                LogPackEvents(p, step);
            }
        }

        void LogPackEvents(Pk p, long step)
        {
            if (p.C.Goal != p.LastGoal || (p.C.Goal == PackGoal.Raid && p.C.RaidTargetId != p.LastRaid))
            {
                _log.GoalChange(step, _now, p.Id, GoalName(p.C.Goal), p.C.Goal == PackGoal.Raid ? p.C.RaidTargetId.Value : (p.C.Goal == PackGoal.Hunt ? p.C.FocusTargetId.Value : 0));
                p.LastGoal = p.C.Goal; p.LastRaid = p.C.RaidTargetId;
            }
            var members = p.Members;
            int died = 0, retreated = 0;
            foreach (var m in members) { if (!m.Alive && m.WasEligible) died++; if (m.State == BrainState.Retreat || m.State == BrainState.Withdrawn) retreated += m.WasEligible ? 1 : 0; }
            foreach (var m in members)
            {
                if (!m.Emerged) continue;
                PackRole now = m.Alive ? p.C.RoleOf(m.Id) : PackRole.Free;
                if (now != m.Role)
                {
                    bool eligible = m.Alive && m.State != BrainState.Retreat && m.State != BrainState.Withdrawn;
                    string reason = !eligible ? (m.Alive ? "retreat" : "death") : (died > 0 ? "death" : (retreated > 0 ? "retreat" : (p.C.Stats.GoalSwitches != _goalSwitchesSeen(p) ? "goal_change" : "auction")));
                    _log.RoleChange(step, _now, m.Id.Value, RoleName(m.Role), RoleName(now), reason);
                    m.Role = now;
                }
                m.WasEligible = m.Alive && m.State != BrainState.Retreat && m.State != BrainState.Withdrawn;
            }
            _goalSeen[p.Id] = p.C.Stats.GoalSwitches;
        }

        readonly Dictionary<int, int> _goalSeen = new Dictionary<int, int>();
        int _goalSwitchesSeen(Pk p) => _goalSeen.TryGetValue(p.Id, out int v) ? v : 0;

        // ------------------------------------------------------------------------------------------ solo (no pack layer): own sight only, otherwise walk to the nearest target

        void SoloDecide(K k)
        {
            k.Brain.Decide(_obs, ref k.Intent);
            bool seeing = false;
            for (int i = 0; i < _obs.TargetCount; i++) if (_obs.Targets[i].Alive && _obs.Targets[i].Visible) { seeing = true; break; }
            if (seeing) { k.Infra = -1; return; }
            // nothing visible: transit to the nearest living infrastructure target and stand on it (the solo kaiju has the map but no board or roles)
            int best = -1; float bd = float.MaxValue;
            for (int i = 0; i < _infra.N.Count; i++) { var n = _infra.N[i]; if (!n.Alive) continue; float d = Dist(k.X, k.Y, n.X, n.Y); if (d < bd) { bd = d; best = i; } }
            k.Infra = best;
            if (best < 0) return;
            Float2 aim = new Float2(_infra.N[best].X, _infra.N[best].Y);
            if (_planner != null) aim = _planner.NextWaypoint(new Float2(k.X, k.Y), aim, k.S);   // the solo kaiju has the map: it routes, it does not pack
            k.Intent.DesiredHeadingRad = (float)Math.Atan2(aim.Y - k.Y, aim.X - k.X);
            k.Intent.SpeedFrac = 1f; k.Intent.State = BrainState.Approach; k.Intent.Action = BrainAction.None; k.Intent.TargetId = default;
        }

        // ------------------------------------------------------------------------------------------ mechs

        void StepMech(M m, float dt)
        {
            if (!m.Alive)
            {
                if (_sc.Name == "churn" && _now >= m.RespawnAt) { m.Alive = true; m.Hp = 1f; m.X = m.HX + _rng.Range(-500f, 500f); m.Y = m.HY + _rng.Range(-500f, 500f); NewWaypoint(m); }
                return;
            }
            // nearest live kaiju
            float nd = float.MaxValue; int ni = -1;
            for (int i = 0; i < _k.Count; i++) { K k = _k[i]; if (!k.Alive || !k.Emerged) continue; float d = Dist(m.X, m.Y, k.X, k.Y); if (d < nd) { nd = d; ni = i; } }
            float tx, ty, v;
            if (m.Guard)
            {
                // respond to a kaiju within 4 km of the post, return afterwards
                if (ni >= 0 && Dist(m.HX, m.HY, _k[ni].X, _k[ni].Y) < 4000f) { tx = _k[ni].X; ty = _k[ni].Y; v = nd > _sc.MechRange * 0.8f ? _sc.MechCruise : 0f; }
                else { tx = m.HX; ty = m.HY; v = Dist(m.X, m.Y, tx, ty) > 30f ? _sc.MechCruise : 0f; }
            }
            else if (_sc.Name == "churn" && nd < 400f) { tx = m.X + (m.X - _k[ni].X); ty = m.Y + (m.Y - _k[ni].Y); v = 4.5f; }
            else { if (Dist(m.X, m.Y, m.WX, m.WY) < 20f) { if (_sc.Mv1) SquadWaypoint(); else NewWaypoint(m); } tx = m.WX; ty = m.WY; v = _sc.MechSpeed; }
            if (v > 0f) { m.Heading = (float)Math.Atan2(ty - m.Y, tx - m.X); m.X += (float)Math.Cos(m.Heading) * v * dt; m.Y += (float)Math.Sin(m.Heading) * v * dt; }
        }

        void MechFire(long step, float dt)
        {
            for (int j = 0; j < _m.Count; j++)
            {
                M m = _m[j]; if (!m.Alive) { m.Aiming = -1; continue; }
                int t = -1; float bd = _sc.MechRange * _sc.MechRange;
                for (int i = 0; i < _k.Count; i++) { K k = _k[i]; if (!k.Alive || !k.Emerged) continue; float dx = k.X - m.X, dy = k.Y - m.Y, d2 = dx * dx + dy * dy; if (d2 <= bd) { bd = d2; t = i; } }
                if (t != m.Aiming) { m.Aiming = t; m.Lock = 0; }
                if (t < 0) continue;
                K tk = _k[t];
                float want = (float)Math.Atan2(tk.Y - m.Y, tk.X - m.X), diff = Wrap(want - m.Aim), max = _sc.MechTraverse * dt;
                m.Aim = Wrap(m.Aim + (diff > max ? max : diff < -max ? -max : diff));
                if (Math.Abs(Wrap(want - m.Aim)) < 0.05f) m.Lock += dt; else m.Lock = Math.Max(0.0, m.Lock - dt);
                if (m.Lock >= _sc.MechLockSeconds)
                {
                    float dmg = _sc.MechDps * dt;
                    tk.HpAbs -= dmg; _res.Hits++;
                    if (step % 10 == 0) _log.Hit(step, m.Id.Value, tk.Id.Value, _sc.MechDps, Math.Max(0f, tk.HpAbs));
                    if (tk.HpAbs <= 0f) { tk.Alive = false; tk.Speed = 0f; _res.KaijuDeaths++; _log.Death(step, tk.Id.Value); _log.Decision(step, _now, tk.Id.Value, tk.Sp, _packLayer ? PackIdOf(tk) : 0, "Free", "free", false, AX(tk.X), AY(tk.Y), tk.Heading, 0f, 0f, new BrainIntent { State = tk.State }, tk.State, _obs, 0f, "own"); }
                }
            }
        }

        // ------------------------------------------------------------------------------------------ kaiju kinematics, melee, raiding

        void StepKaiju(K k, float dt, long step)
        {
            float dh = Wrap(k.Intent.DesiredHeadingRad - k.Heading), turn = 0.5f * dt;
            k.Heading = Wrap(k.Heading + (dh > turn ? turn : dh < -turn ? -turn : dh));
            float sp = Math.Max(0f, Math.Min(1f, k.Intent.SpeedFrac));
            float top = k.Intent.State == BrainState.Charge ? k.S.ChargeMps : k.S.CruiseMps;
            float ts = sp * top, ds = ts - k.Speed, ma = 2f * dt;
            k.Speed += ds > ma ? ma : ds < -ma ? -ma : ds;
            float mv = k.Speed * dt;
            if (_field != null)
            {
                float nx = k.X + (float)Math.Cos(k.Heading) * mv, ny = k.Y + (float)Math.Sin(k.Heading) * mv;
                if (DepthAt(nx, ny) > 0.4f * k.S.HeightM) { k.Speed = 0f; nx = k.X; ny = k.Y; _res.BlockedSteps++; }   // the shore of a channel too deep to wade
                else if (SoftAt(nx, ny) > 0f) { float dv = 1f + GridRoutePlanner.DefaultSoftCost * SoftAt(nx, ny); nx = k.X + (nx - k.X) / dv; ny = k.Y + (ny - k.Y) / dv; }   // soft ground: speed / (1 + soft cost), the planner's own cost
                k.X = nx; k.Y = ny;
            }
            else { k.X += (float)Math.Cos(k.Heading) * k.Speed * dt; k.Y += (float)Math.Sin(k.Heading) * k.Speed * dt; }   // exact original arithmetic: the flat map must reproduce the T-115/T-115b logs bit for bit
            if (k.Cooldown > 0f) k.Cooldown -= dt;
            if (k.Intent.Action == BrainAction.Melee && k.Cooldown <= 0f)
                for (int j = 0; j < _m.Count; j++)
                {
                    M m = _m[j]; if (!m.Alive || m.Id.Value != k.Intent.TargetId.Value) continue;
                    if (Dist(k.X, k.Y, m.X, m.Y) > k.Cfg.MeleeReachM) break;
                    m.Hp -= _sc.MeleeDamageToMech / _sc.MechHpAbs; k.Cooldown = _sc.MeleeCooldown; _res.Hits++;
                    _log.Hit(step, k.Id.Value, m.Id.Value, _sc.MeleeDamageToMech, Math.Max(0f, m.Hp * _sc.MechHpAbs));
                    if (m.Hp <= 0f) { m.Alive = false; m.RespawnAt = _now + 30.0; _res.MechDeaths++; _log.Death(step, m.Id.Value); }
                    break;
                }
            // infrastructure damage: a kaiju standing at its raid target (pack directive) or at the solo target
            int target = -1;
            if (_packLayer && _dir.TryGetDirective(k.Id, out PackDirective d) && d.Goal == PackGoal.Raid && d.FocusTargetId.IsValid)
            { for (int i = 0; i < _infra.N.Count; i++) if (_infra.N[i].Id == d.FocusTargetId) { target = i; break; } }
            else if (!_packLayer) target = k.Infra;
            if (target >= 0)
            {
                var n = _infra.N[target];
                if (n.Alive && Dist(k.X, k.Y, n.X, n.Y) <= Math.Max(k.Cfg.MeleeReachM * 1.5f, 60f))
                {
                    n.Hp -= _sc.InfraDamageRate * dt;
                    if (n.Hp <= 0f) { n.Hp = 0f; n.Alive = false; _res.InfraDestroyed++; }
                    _infra.N[target] = n;
                }
            }
        }

        // ------------------------------------------------------------------------------------------ perception (R-024 1.2): sight range, 270 degree view, 3 s memory

        bool Visible(K k, M m)
        {
            if (!m.Alive) return false;
            float dx = m.X - k.X, dy = m.Y - k.Y, r = k.Cfg.SightRangeM;
            if (dx * dx + dy * dy > r * r) return false;
            return Math.Abs(Wrap((float)Math.Atan2(dy, dx) - k.Heading)) <= 270f * 0.017453292f * 0.5f;
        }

        void Perceive(K k)
        {
            if (k.LastSeen == null) { int n = _m.Count; k.LastSeen = new double[n]; k.LX = new float[n]; k.LY = new float[n]; k.LH = new float[n]; k.LHp = new float[n]; k.Ever = new bool[n]; }
            for (int j = 0; j < _m.Count; j++)
            {
                M m = _m[j]; if (!Visible(k, m)) continue;
                k.LastSeen[j] = _now; k.Ever[j] = true; k.LX[j] = m.X; k.LY[j] = m.Y; k.LH[j] = m.Heading; k.LHp[j] = m.Hp;
            }
        }

        void FillObs(K k, long step)
        {
            BrainObservation o = _obs; o.Step = step; o.TimeSeconds = _now;
            o.Self = new SelfObs { Id = k.Id, PackId = (uint)(k.PackIdx + 1), Pos = new Float2(k.X, k.Y), HeadingRad = k.Heading, Speed = k.Speed, HpFrac = k.HpAbs > 0 ? k.HpAbs / k.HpMax : 0f, State = (byte)k.State, Cooldowns = Math.Max(0f, k.Cooldown) };
            int nt = 0;
            if (k.Ever != null)
                for (int j = 0; j < _m.Count && nt < BrainContracts.MaxTargets; j++)
                {
                    M m = _m[j]; bool vis = Visible(k, m);
                    if (!k.Ever[j]) continue;
                    double age = vis ? 0.0 : _now - k.LastSeen[j];
                    if (age > k.Cfg.MemorySeconds) continue;
                    o.Targets[nt++] = new TargetObs { Id = m.Id, Pos = vis ? new Float2(m.X, m.Y) : new Float2(k.LX[j], k.LY[j]), HeadingRad = vis ? m.Heading : k.LH[j], HpFrac = vis ? m.Hp : k.LHp[j], Alive = m.Alive, Visible = vis, MemoryAge = (float)age };
                }
            o.TargetCount = nt;
            int na = 0;
            foreach (K a in _k)
            {
                if (a == k || !a.Emerged || na >= BrainContracts.MaxAllies) continue;
                if (_packLayer && a.PackIdx != k.PackIdx) continue;
                if (!_packLayer && Dist(a.X, a.Y, k.X, k.Y) > k.Cfg.AllyAwarenessM) continue;
                o.Allies[na++] = new AllyObs { Id = a.Id, Pos = new Float2(a.X, a.Y), HeadingRad = a.Heading, HpFrac = a.HpAbs > 0 ? a.HpAbs / a.HpMax : 0f, Alive = a.Alive, State = (byte)a.State, TargetId = a.Intent.TargetId };
            }
            o.AllyCount = na;
        }

        // ------------------------------------------------------------------------------------------ log helpers

        string RoleOf(K k) => _packLayer && k.PackIdx >= 0 ? RoleName(_packs[k.PackIdx].C.RoleOf(k.Id)) : "Free";
        string GoalOf(K k)
        {
            if (_packLayer && k.PackIdx >= 0) return GoalName(_packs[k.PackIdx].C.Goal);
            for (int i = 0; i < _obs.TargetCount; i++) if (_obs.Targets[i].Alive && _obs.Targets[i].Visible) return "hunt";
            return k.Infra >= 0 ? "raid" : "free";
        }
        float BoardAge(K k)
        {
            if (!_packLayer || k.PackIdx < 0 || !k.Intent.TargetId.IsValid) return 0f;
            var b = _packs[k.PackIdx].C.Board; int ix = b.IndexOf(k.Intent.TargetId);
            return ix < 0 ? 0f : (float)Math.Max(0.0, _now - b[ix].TObserved);
        }
        string BoardSourceName(K k)
        {
            if (_omniscient) return "oracle";
            if (!_packLayer || k.PackIdx < 0 || !k.Intent.TargetId.IsValid) return "own";
            var b = _packs[k.PackIdx].C.Board; int ix = b.IndexOf(k.Intent.TargetId);
            if (ix < 0) return "own";
            switch (b[ix].Source) { case Game.AI.Pack.BoardSource.Heard: return "heard"; case Game.AI.Pack.BoardSource.Called: return "called"; case Game.AI.Pack.BoardSource.Map: return "map"; default: return "own"; }
        }
        static string RoleName(PackRole r) => r == PackRole.Anchor ? "Anchor" : r == PackRole.Flanker ? "Flanker" : r == PackRole.Bait ? "Bait" : r == PackRole.Raider ? "Raider" : "Free";
        static string GoalName(PackGoal g) => g == PackGoal.Hunt ? "hunt" : g == PackGoal.Raid ? "raid" : g == PackGoal.Regroup ? "regroup" : "free";

        void LogWorld(long step)
        {
            for (int j = 0; j < _m.Count; j++)
            {
                M m = _m[j];
                _log.Sample(step, _now, m.Id.Value, m.Alive, AX(m.X), AY(m.Y), m.Heading, m.Hp);
                if (m.Alive && m.Aiming >= 0 && m.Lock > 0.0) _log.MechTarget(step, _now, m.Id.Value, _k[m.Aiming].Id.Value);
            }
            if (step % 100 == 0)
                for (int i = 0; i < _infra.N.Count; i++) { var n = _infra.N[i]; _log.InfraRow(step, _now, n.Id.Value, n.ClassName, n.Hp, n.HpMax, AX(n.X), AY(n.Y)); }
        }

        static float Dist(float ax, float ay, float bx, float by) { float dx = bx - ax, dy = by - ay; return (float)Math.Sqrt(dx * dx + dy * dy); }
        static float Wrap(float a) { while (a > Math.PI) a -= 6.2831855f; while (a <= -Math.PI) a += 6.2831855f; return a; }

        sealed class Rng
        {
            ulong _s; public Rng(ulong s) { _s = s; }
            public ulong Next() { unchecked { ulong z = (_s += 0x9E3779B97F4A7C15UL); z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL; z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL; return z ^ (z >> 31); } }
            public double NextDouble() => (Next() >> 11) * (1.0 / 9007199254740992.0);
            public float Range(float a, float b) => a + (b - a) * (float)NextDouble();
        }
    }
}
