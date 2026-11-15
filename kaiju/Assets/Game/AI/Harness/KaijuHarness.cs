using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Game.Core;

namespace Game.AI.Harness
{
    /// <summary>Scenario for the 2D kinematic harness (R-024 1.2 perception, no occlusion). Harness-only constants are marked.</summary>
    public sealed class HarnessScenario
    {
        public string Name = "3v4";
        public ulong Seed = 1;
        public int KaijuCount = 3;
        public int MechCount = 4;
        public double DurationSeconds = 600.0;
        public float StepSeconds = 0.02f;
        public float FieldHalfM = 2000f;          // 4 km x 4 km field
        public float FovRad = 270f * 0.017453292519943295f;
        public float TurnRateRadPerS = 0.5f;      // harness constant (BrainConfig has none)
        public float AccelMps2 = 2.0f;            // harness constant (BrainConfig has none)
        public float MeleeDamage = 25f;           // harness constant
        public float MechHp = 100f;               // harness constant
        public float MeleeCooldownSeconds = 2f;   // harness constant
        public float MechWalkMps = 3f;            // harness constant
        public float MechFleeMps = 4.5f;            // harness constant
        public float MechFleeRadiusM = 400f;      // harness constant
        public float MechRespawnSeconds = 0f;     // 0 = dead mechs stay dead
        // Combat (T-033b). All Assumed config, harness-only (the runtime brain never sees them). MechDps = 0 keeps the legacy invulnerable kaiju.
        public float KaijuMaxHp = 400f;           // harness constant; HpFrac in the observation = hp / KaijuMaxHp
        public float MechDps = 0f;                // declared damage per second of one mech inside MechRangeM
        public float MechRangeM = 300f;           // harness constant
        public float MechShotIntervalSeconds = 1f; // damage is applied (and logged as a hit) once per interval to the nearest kaiju in range
        public bool MeasureTiming;

        /// <summary>M-007@v1 scenario (R-024 5 rev 2): 3 kaiju vs 2 slow decoy mechs, 300 s (90 s cannot fit approach 600-900 m + assembly + flank arc).</summary>
        public static HarnessScenario M007(ulong seed, bool directCharge)
        {
            var sc = new HarnessScenario
            {
                Name = "m007", Seed = seed, KaijuCount = 3, MechCount = 2, DurationSeconds = 300.0,
                MechWalkMps = 1.5f, MechFleeRadiusM = 0f, MechRespawnSeconds = 0f,
            };
            sc.Brain.DirectCharge = directCharge;
            return sc;
        }

        /// <summary>M007-3v4 (R-024 5 rev 3): 4 kaiju vs 3 slow mechs that shoot back (declared DPS in range); kaiju die, Retreat is reachable.</summary>
        public static HarnessScenario M007ThreeVsFour(ulong seed, bool directCharge)
        {
            var sc = new HarnessScenario
            {
                Name = "m007_3v4", Seed = seed, KaijuCount = 4, MechCount = 3, DurationSeconds = 300.0,
                MechWalkMps = 1.5f, MechFleeRadiusM = 0f, MechRespawnSeconds = 0f, MechDps = 6f,
            };
            sc.Brain.DirectCharge = directCharge;
            return sc;
        }

        /// <summary>M007-churn (R-024 rev 3 addendum): the 3v4 scenario with fleeing, respawning mechs so targets churn (C3 evaluation set).</summary>
        public static HarnessScenario M007Churn(ulong seed, bool directCharge)
        {
            HarnessScenario sc = M007ThreeVsFour(seed, directCharge);
            sc.Name = "m007_churn"; sc.MechFleeRadiusM = 400f; sc.MechFleeMps = 4.5f; sc.MechRespawnSeconds = 30f;
            return sc;
        }
        public BrainConfig Brain = BrainConfig.Default();
    }

    /// <summary>Timing and counters of one harness run.</summary>
    public sealed class HarnessResult
    {
        public long Steps, Decisions, Hits, Deaths, KaijuDeaths, KaijuHits;
        public int KaijuRetreats, KaijuWithdrawn;
        public int MechsAliveAtEnd;
        public long[] DecideTicks;   // Stopwatch ticks per due Decide call (MeasureTiming)
        public long[] TickTotals;    // Stopwatch ticks per step: sum of Decide calls in that step (all steps)
        public long AllocatedBytesInDecide;
    }

    public sealed class SplitMix64
    {
        private ulong _s;
        public SplitMix64(ulong seed) { _s = seed; }
        public ulong Next()
        {
            ulong z = (_s += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
        public float NextFloat() => (Next() >> 40) / 16777216f; // [0,1)
        public float Range(float a, float b) => a + (b - a) * NextFloat();
    }

    /// <summary>Deterministic 2D kinematic harness: N scripted kaiju versus M scripted mech dummies, fixed step.</summary>
    public sealed class KaijuHarness
    {
        private sealed class Kaiju
        {
            public EntityId Id; public ScriptedKaijuBrain Brain = new ScriptedKaijuBrain();
            public float X, Y, Heading, Speed, Hp = 1f, HpAbs, Cooldown; public bool EverRetreated, EverWithdrawn;
            public BrainIntent Intent; public BrainState State; public bool Alive = true;
            public double[] LastSeen; public float[] LastX, LastY, LastHead, LastHp, LastSpeed; public bool[] EverSeen;
        }

        private sealed class Mech
        {
            public EntityId Id; public float X, Y, Heading, Hp; public bool Alive = true;
            public float WX, WY; public double RespawnAt, NextShot;
        }

        private readonly HarnessScenario _sc;
        private readonly BrainLogWriter _log;
        private readonly Kaiju[] _k;
        private readonly Mech[] _m;
        private readonly SplitMix64 _rng;
        private readonly BrainObservation _obs = new BrainObservation();

        public KaijuHarness(HarnessScenario sc, BrainLogWriter log)
        {
            _sc = sc; _log = log; _rng = new SplitMix64(sc.Seed * 0x9E3779B97F4A7C15UL + 12345UL);
            int period = (int)Math.Round(0.2 / sc.StepSeconds); if (period < 1) period = 1;
            sc.Brain.BrainPeriodSteps = period;
            if (sc.KaijuCount > BrainContracts.MaxAllies + 1 || sc.MechCount > BrainContracts.MaxTargets)
                throw new ArgumentException("scenario exceeds contract limits");
            _k = new Kaiju[sc.KaijuCount]; _m = new Mech[sc.MechCount];
            for (int i = 0; i < _k.Length; i++)
            {
                var k = new Kaiju { Id = EntityId.FromValue((ulong)(i + 1)), HpAbs = sc.KaijuMaxHp };
                k.X = -1000f + _rng.Range(-100f, 100f); k.Y = (i - (_k.Length - 1) * 0.5f) * 200f + _rng.Range(-50f, 50f);
                k.Heading = _rng.Range(-0.2f, 0.2f);
                k.Brain.Reset(sc.Seed + (ulong)i, sc.Brain);
                int n = _m.Length;
                k.LastSeen = new double[n]; k.LastX = new float[n]; k.LastY = new float[n]; k.LastHead = new float[n];
                k.LastHp = new float[n]; k.LastSpeed = new float[n]; k.EverSeen = new bool[n];
                _k[i] = k;
            }
            for (int j = 0; j < _m.Length; j++)
            {
                var m = new Mech { Id = EntityId.FromValue((ulong)(101 + j)), Hp = sc.MechHp };
                m.X = _rng.Range(-800f, 1200f); m.Y = _rng.Range(-1000f, 1000f); NewWaypoint(m);
                m.Heading = MathF.Atan2(m.WY - m.Y, m.WX - m.X);
                _m[j] = m;
            }
        }

        private void NewWaypoint(Mech m) { m.WX = _rng.Range(-_sc.FieldHalfM, _sc.FieldHalfM); m.WY = _rng.Range(-_sc.FieldHalfM, _sc.FieldHalfM); }
        private static float Wrap(float a)
        {
            const float TwoPi = 6.2831853071795864f;
            while (a > MathF.PI) a -= TwoPi;
            while (a <= -MathF.PI) a += TwoPi;
            return a;
        }
        private float Clamp(float v) { float h = _sc.FieldHalfM; return v < -h ? -h : (v > h ? h : v); }

        public HarnessResult Run()
        {
            var res = new HarnessResult();
            long total = (long)Math.Round(_sc.DurationSeconds / _sc.StepSeconds);
            int period = _sc.Brain.BrainPeriodSteps;
            float dt = _sc.StepSeconds;
            if (_sc.MeasureTiming) { res.TickTotals = new long[total]; res.DecideTicks = new long[total * Math.Max(1, _k.Length) / period + _k.Length + 8]; }
            int ncall = 0;
            _log.WriteHeader(_sc);

            for (long step = 0; step < total; step++)
            {
                double now = step * (double)dt;
                // 1. mechs
                for (int j = 0; j < _m.Length; j++) StepMech(_m[j], now, dt);
                // 2. perception memory update (every step)
                for (int i = 0; i < _k.Length; i++) Perceive(_k[i], now);
                // 3. decisions on due steps
                bool sampled = false; long tickSum = 0;
                for (int i = 0; i < _k.Length; i++)
                {
                    Kaiju k = _k[i];
                    if (!k.Alive) continue;
                    if ((step % period) != (long)(k.Id.Value % (ulong)period)) continue;
                    FillObs(k, step, now);
                    if (!sampled) { WriteSamples(step, now); sampled = true; }
                    BrainState prev = k.State;
                    long a0 = _sc.MeasureTiming ? GC.GetAllocatedBytesForCurrentThread() : 0;
                    long t0 = _sc.MeasureTiming ? Stopwatch.GetTimestamp() : 0;
                    k.Brain.Decide(_obs, ref k.Intent);
                    if (_sc.MeasureTiming)
                    {
                        long dtk = Stopwatch.GetTimestamp() - t0; tickSum += dtk;
                        if (ncall < res.DecideTicks.Length) res.DecideTicks[ncall++] = dtk;
                        res.AllocatedBytesInDecide += GC.GetAllocatedBytesForCurrentThread() - a0;
                    }
                    k.State = k.Intent.State;
                    if (k.State == BrainState.Retreat && !k.EverRetreated) { k.EverRetreated = true; res.KaijuRetreats++; }
                    if (k.State == BrainState.Withdrawn && !k.EverWithdrawn) { k.EverWithdrawn = true; res.KaijuWithdrawn++; }
                    _log.WriteDecision(step, now, k.Id.Value, 1, RoleOf(k), k.Alive, k.X, k.Y, k.Heading, k.Speed, k.Hp,
                        k.Intent, prev, _obs);
                    res.Decisions++;
                }
                if (_sc.MeasureTiming) res.TickTotals[step] = tickSum;
                // 4. kinematics + melee
                for (int i = 0; i < _k.Length; i++)
                {
                    Kaiju k = _k[i];
                    if (!k.Alive) continue;
                    float dh = Wrap(k.Intent.DesiredHeadingRad - k.Heading), maxTurn = _sc.TurnRateRadPerS * dt;
                    k.Heading = Wrap(k.Heading + (dh > maxTurn ? maxTurn : (dh < -maxTurn ? -maxTurn : dh)));
                    float sp = k.Intent.SpeedFrac; if (sp < 0f) sp = 0f; if (sp > 1f) sp = 1f;
                    float ts = sp * _sc.Brain.CruiseSpeedMps, ds = ts - k.Speed, ma = _sc.AccelMps2 * dt;
                    k.Speed += ds > ma ? ma : (ds < -ma ? -ma : ds);
                    k.X = Clamp(k.X + MathF.Cos(k.Heading) * k.Speed * dt); k.Y = Clamp(k.Y + MathF.Sin(k.Heading) * k.Speed * dt);
                    if (k.Cooldown > 0f) k.Cooldown -= dt;
                    if (k.Intent.Action == BrainAction.Melee && k.Cooldown <= 0f)
                    {
                        for (int j = 0; j < _m.Length; j++)
                        {
                            Mech m = _m[j];
                            if (!m.Alive || m.Id.Value != k.Intent.TargetId.Value) continue;
                            float dx = m.X - k.X, dy = m.Y - k.Y;
                            if (MathF.Sqrt(dx * dx + dy * dy) > _sc.Brain.MeleeReachM) break;
                            m.Hp -= _sc.MeleeDamage; k.Cooldown = _sc.MeleeCooldownSeconds; res.Hits++;
                            _log.WriteHit(step, k.Id.Value, m.Id.Value, _sc.MeleeDamage, m.Hp < 0f ? 0f : m.Hp);
                            if (m.Hp <= 0f)
                            {
                                m.Alive = false; m.RespawnAt = now + _sc.MechRespawnSeconds; res.Deaths++;
                                _log.WriteDeath(step, m.Id.Value);
                            }
                            break;
                        }
                    }
                }
                // 5. mech fire (after kinematics): each mech shoots the nearest live kaiju inside MechRangeM once per interval
                if (_sc.MechDps > 0f) MechFire(step, now, res);
                res.Steps++;
            }
            for (int j = 0; j < _m.Length; j++) if (_m[j].Alive) res.MechsAliveAtEnd++;
            if (res.DecideTicks != null) Array.Resize(ref res.DecideTicks, ncall);
            _log.Flush();
            return res;
        }

        private void MechFire(long step, double now, HarnessResult res)
        {
            float dmg = _sc.MechDps * _sc.MechShotIntervalSeconds, r2 = _sc.MechRangeM * _sc.MechRangeM;
            for (int j = 0; j < _m.Length; j++)
            {
                Mech m = _m[j];
                if (!m.Alive || now < m.NextShot) continue;
                Kaiju tk = null; float bd = float.PositiveInfinity;
                for (int i = 0; i < _k.Length; i++)
                {
                    Kaiju k = _k[i]; if (!k.Alive) continue;
                    float dx = k.X - m.X, dy = k.Y - m.Y, d2 = dx * dx + dy * dy;
                    if (d2 <= r2 && d2 < bd) { bd = d2; tk = k; }
                }
                if (tk == null) continue;
                m.NextShot = now + _sc.MechShotIntervalSeconds;
                tk.HpAbs -= dmg; res.KaijuHits++;
                tk.Hp = tk.HpAbs > 0f ? tk.HpAbs / _sc.KaijuMaxHp : 0f;
                _log.WriteHit(step, m.Id.Value, tk.Id.Value, dmg, tk.HpAbs > 0f ? tk.HpAbs : 0f);
                if (tk.HpAbs <= 0f)
                {
                    tk.Alive = false; tk.Speed = 0f; res.KaijuDeaths++;
                    _log.WriteDeath(step, tk.Id.Value);
                    // closing decision row (alive 0, no target) so row-replaying metrics stop counting this kaiju
                    var stop = new BrainIntent { State = tk.State, Reason = ReasonCode.None };
                    _log.WriteDecision(step, now, tk.Id.Value, 1, "solo", false, tk.X, tk.Y, tk.Heading, 0f, 0f, stop, tk.State, _obs);
                }
            }
        }

        private void StepMech(Mech m, double now, float dt)
        {
            if (!m.Alive)
            {
                if (_sc.MechRespawnSeconds > 0f && now >= m.RespawnAt)
                {
                    m.Alive = true; m.Hp = _sc.MechHp; m.X = _rng.Range(-800f, 1200f); m.Y = _rng.Range(-1000f, 1000f); NewWaypoint(m);
                }
                return;
            }
            float nd = float.PositiveInfinity, nx = 0, ny = 0;
            for (int i = 0; i < _k.Length; i++)
            {
                if (!_k[i].Alive) continue;
                float dx = m.X - _k[i].X, dy = m.Y - _k[i].Y, d = MathF.Sqrt(dx * dx + dy * dy);
                if (d < nd) { nd = d; nx = dx; ny = dy; }
            }
            if (nd < _sc.MechFleeRadiusM)
            {
                m.Heading = MathF.Atan2(ny, nx);
                m.X = Clamp(m.X + MathF.Cos(m.Heading) * _sc.MechFleeMps * dt); m.Y = Clamp(m.Y + MathF.Sin(m.Heading) * _sc.MechFleeMps * dt);
            }
            else
            {
                float dx = m.WX - m.X, dy = m.WY - m.Y;
                if (dx * dx + dy * dy < 400f) NewWaypoint(m);
                m.Heading = MathF.Atan2(m.WY - m.Y, m.WX - m.X);
                m.X = Clamp(m.X + MathF.Cos(m.Heading) * _sc.MechWalkMps * dt); m.Y = Clamp(m.Y + MathF.Sin(m.Heading) * _sc.MechWalkMps * dt);
            }
        }

        private bool IsVisible(Kaiju k, Mech m)
        {
            if (!m.Alive) return false;
            float dx = m.X - k.X, dy = m.Y - k.Y;
            if (dx * dx + dy * dy > _sc.Brain.SightRangeM * _sc.Brain.SightRangeM) return false;
            return MathF.Abs(Wrap(MathF.Atan2(dy, dx) - k.Heading)) <= _sc.FovRad * 0.5f;
        }

        private void Perceive(Kaiju k, double now)
        {
            for (int j = 0; j < _m.Length; j++)
            {
                Mech m = _m[j];
                if (!IsVisible(k, m)) continue;
                k.LastSeen[j] = now; k.EverSeen[j] = true; k.LastX[j] = m.X; k.LastY[j] = m.Y; k.LastHead[j] = m.Heading;
                k.LastHp[j] = m.Hp / _sc.MechHp; k.LastSpeed[j] = 0f;
            }
        }

        private void FillObs(Kaiju k, long step, double now)
        {
            BrainObservation o = _obs;
            o.Step = step; o.TimeSeconds = now;
            o.Self = new SelfObs
            {
                Id = k.Id, PackId = 1, Pos = new Float2(k.X, k.Y), HeadingRad = k.Heading, Speed = k.Speed, HpFrac = k.Hp,
                State = (byte)k.State, StateAgeSteps = 0, Cooldowns = k.Cooldown > 0f ? k.Cooldown : 0f,
            };
            int nt = 0;
            for (int j = 0; j < _m.Length; j++)
            {
                Mech m = _m[j];
                bool vis = IsVisible(k, m);
                if (!k.EverSeen[j]) continue;
                double age = vis ? 0.0 : now - k.LastSeen[j];
                if (age > _sc.Brain.MemorySeconds) continue; // dropped from memory
                o.Targets[nt++] = new TargetObs
                {
                    Id = m.Id, Pos = vis ? new Float2(m.X, m.Y) : new Float2(k.LastX[j], k.LastY[j]),
                    HeadingRad = vis ? m.Heading : k.LastHead[j], Speed = 0f, HpFrac = vis ? m.Hp / _sc.MechHp : k.LastHp[j],
                    Alive = m.Alive, Visible = vis, MemoryAge = (float)age, // death is public: Alive reflects truth inside the memory window
                };
            }
            o.TargetCount = nt;
            int na = 0;
            for (int i = 0; i < _k.Length; i++)
            {
                Kaiju a = _k[i]; if (a == k) continue;
                { float ax = a.X - k.X, ay = a.Y - k.Y; if (ax * ax + ay * ay > _sc.Brain.AllyAwarenessM * _sc.Brain.AllyAwarenessM) continue; } // DEF-M1ai-4: range-filter in the observation
                o.Allies[na++] = new AllyObs
                {
                    Id = a.Id, Pos = new Float2(a.X, a.Y), HeadingRad = a.Heading, HpFrac = a.Hp, Alive = a.Alive,
                    State = (byte)a.State, TargetId = a.Intent.TargetId, Role = 0,
                };
            }
            o.AllyCount = na;
        }

        // Role as the brain derives it (R-024 1.4 / step A note): solo = no live ally in range; even rank among live allies+self = wing.
        private string RoleOf(Kaiju k)
        {
            int live = 0, rank = 0;
            for (int i = 0; i < _obs.AllyCount; i++)
            {
                AllyObs a = _obs.Allies[i];
                if (!a.Alive) continue;
                float dx = a.Pos.X - k.X, dy = a.Pos.Y - k.Y;
                if (MathF.Sqrt(dx * dx + dy * dy) > _sc.Brain.AllyAwarenessM) continue;
                live++; if (a.Id.Value < k.Id.Value) rank++;
            }
            return live == 0 ? "solo" : ((rank & 1) == 0 ? "wing" : "centre");
        }

        private void WriteSamples(long step, double now)
        {
            for (int j = 0; j < _m.Length; j++)
            {
                Mech m = _m[j]; if (!m.Alive) continue;
                _log.WriteTargetSample(step, now, m.Id.Value, "mech", m.Alive, m.X, m.Y, m.Heading, m.Hp);
            }
        }
    }
}
