using System;
using System.Collections.Generic;
using Game.AI;
using Game.AI.Pack;
using Game.Core;
using NUnit.Framework;

namespace Game.AI.Pack.Tests
{
    /// <summary>Tiny 2D kinematic harness for the pack layer (T-113). Consumes Game.AI.Contracts and Game.AI.Pack only.</summary>
    internal sealed class SimMech
    {
        public EntityId Id; public Float2 Pos; public float Vx = 0f, Vy = 0f, Hp = 1f; public bool Alive = true;
    }

    internal sealed class SimInfra : IInfrastructureSource
    {
        public readonly List<InfraTarget> Items = new List<InfraTarget>();
        public int Count => Items.Count;
        public InfraTarget Get(int i) => Items[i];
        public void Destroy(EntityId id) { for (int i = 0; i < Items.Count; i++) if (Items[i].Id == id) { var t = Items[i]; t.Alive = false; Items[i] = t; } }
    }

    /// <summary>Stand-in for the scripted brain: closes to its standoff on the nearest target, holds there, melees inside reach.</summary>
    internal sealed class HoldBrain : IKaijuBrain
    {
        private BrainConfig _cfg;
        public int ContractVersion => BrainContracts.ContractVersion;
        public void Reset(ulong seed, in BrainConfig cfg) { _cfg = cfg; }
        public void Decide(in BrainObservation obs, ref BrainIntent intent)
        {
            int best = -1; float bd = float.MaxValue;
            for (int i = 0; i < obs.TargetCount; i++)
            {
                if (!obs.Targets[i].Alive) continue;
                float dx = obs.Targets[i].Pos.X - obs.Self.Pos.X, dy = obs.Targets[i].Pos.Y - obs.Self.Pos.Y;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                if (d < bd) { bd = d; best = i; }
            }
            intent = default;
            if (best < 0) { intent.State = BrainState.Idle; return; }
            intent.TargetId = obs.Targets[best].Id;
            intent.DesiredHeadingRad = MathF.Atan2(obs.Targets[best].Pos.Y - obs.Self.Pos.Y, obs.Targets[best].Pos.X - obs.Self.Pos.X);
            if (bd > _cfg.StandoffM) { intent.State = BrainState.Approach; intent.SpeedFrac = _cfg.SpeedApproach; }
            else intent.State = BrainState.Idle;
        }
    }

    /// <summary>Aggressive stand-in: always charges the nearest target (the "no bait discipline" control).</summary>
    internal sealed class ChargeBrain : IKaijuBrain
    {
        private BrainConfig _cfg;
        public int ContractVersion => BrainContracts.ContractVersion;
        public void Reset(ulong seed, in BrainConfig cfg) { _cfg = cfg; }
        public void Decide(in BrainObservation obs, ref BrainIntent intent)
        {
            intent = default;
            if (obs.TargetCount == 0) return;
            ref readonly TargetObs t = ref obs.Targets[0];
            float dx = t.Pos.X - obs.Self.Pos.X, dy = t.Pos.Y - obs.Self.Pos.Y;
            float d = MathF.Sqrt(dx * dx + dy * dy);
            intent.TargetId = t.Id; intent.DesiredHeadingRad = MathF.Atan2(dy, dx);
            intent.State = BrainState.Charge; intent.SpeedFrac = _cfg.SpeedCharge;
            intent.Action = d <= _cfg.MeleeReachM ? BrainAction.Melee : BrainAction.None;
        }
    }

    internal sealed class SimMember
    {
        public PackMemberState S;
        public IKaijuBrain Brain;
        public BrainIntent Intent;
        public bool Melee;
    }

    internal sealed class PackSim
    {
        public readonly List<SimMember> Members = new List<SimMember>();
        public readonly List<SimMech> Mechs = new List<SimMech>();
        public readonly PackCoordinator Coord;
        public double Time;
        public const float Dt = 0.2f;
        private readonly BrainObservation _obs = new BrainObservation();
        private readonly PackMemberState[] _states = new PackMemberState[16];
        private int _step;
        public PackConfig Cfg = PackConfig.Default();

        public PackSim(SimInfra infra, ulong seed = 7)
        {
            Coord = new PackCoordinator(infra);
            Coord.Reset(seed, Cfg);
        }

        public SimMember Add(ulong id, in SpeciesRole sp, float x, float y, Func<IKaijuBrain> inner, bool wrapped = true)
        {
            var cfg = BrainConfig.Default();
            cfg.StandoffM = sp.StandoffM; cfg.MeleeReachM = sp.MeleeReachM; cfg.CruiseSpeedMps = sp.CruiseMps;
            IKaijuBrain b = wrapped ? new PackMemberBrain(inner(), Coord, Cfg) : inner();
            b.Reset(id, cfg);
            var m = new SimMember { Brain = b };
            m.S = new PackMemberState { Id = EntityId.FromValue(id), Species = sp, Pos = new Float2(x, y), HpFrac = 1f, Alive = true, State = BrainState.Idle };
            Members.Add(m);
            return m;
        }

        public SimMech AddMech(ulong id, float x, float y)
        {
            var m = new SimMech { Id = EntityId.FromValue(id), Pos = new Float2(x, y) };
            Mechs.Add(m); return m;
        }

        public void Kill(ulong id) { foreach (var m in Members) if (m.S.Id.Value == id) { m.S.Alive = false; m.S.HpFrac = 0f; } }

        private void Fill(SimMember me)
        {
            _obs.Step = _step; _obs.TimeSeconds = Time;
            _obs.Self = new SelfObs { Id = me.S.Id, Pos = me.S.Pos, HpFrac = me.S.HpFrac, State = (byte)me.S.State };
            int n = 0;
            foreach (var t in Mechs)
            {
                if (n >= BrainContracts.MaxTargets) break;
                _obs.Targets[n++] = new TargetObs { Id = t.Id, Pos = t.Pos, HpFrac = t.Hp, Alive = t.Alive, Visible = true };
            }
            _obs.TargetCount = n;
            int a = 0;
            foreach (var o in Members)
                if (o != me && o.S.Alive && a < BrainContracts.MaxAllies)
                    _obs.Allies[a++] = new AllyObs { Id = o.S.Id, Pos = o.S.Pos, HpFrac = o.S.HpFrac, Alive = true, TargetId = o.S.TargetId };
            _obs.AllyCount = a;
        }

        /// <summary>Advance by seconds; the coordinator ticks at 1 Hz, brains at 5 Hz.</summary>
        public void Run(float seconds)
        {
            int steps = (int)Math.Round(seconds / Dt);
            for (int k = 0; k < steps; k++)
            {
                if (_step % 5 == 0) TickCoordinator();
                foreach (var m in Members)
                {
                    if (!m.S.Alive) continue;
                    Fill(m);
                    m.Brain.Decide(_obs, ref m.Intent);
                    m.Melee = m.Intent.Action == BrainAction.Melee;
                    m.S.State = m.Intent.State; m.S.TargetId = m.Intent.TargetId;
                    float v = m.Intent.SpeedFrac * m.S.Species.CruiseMps * (m.Intent.State == BrainState.Charge ? 1.6f : 1f);
                    m.S.Pos = new Float2(m.S.Pos.X + MathF.Cos(m.Intent.DesiredHeadingRad) * v * Dt, m.S.Pos.Y + MathF.Sin(m.Intent.DesiredHeadingRad) * v * Dt);
                }
                foreach (var t in Mechs) if (t.Alive) t.Pos = new Float2(t.Pos.X + t.Vx * Dt, t.Pos.Y + t.Vy * Dt);
                Time += Dt; _step++;
            }
        }

        public void TickCoordinator()
        {
            int n = 0;
            foreach (var m in Members)
            {
                _states[n++] = m.S;
                if (!m.S.Alive) continue;
                Fill(m);
                Coord.Board.Ingest(_obs);
            }
            Coord.Update(Time, _states, n);
        }

        public static float Dist(Float2 a, Float2 b) { float dx = b.X - a.X, dy = b.Y - a.Y; return MathF.Sqrt(dx * dx + dy * dy); }
    }

    [TestFixture]
    public sealed class PackScenarioTests
    {
        private static readonly SpeciesRole S = SpeciesRole.Strider, M = SpeciesRole.Breacher, H = SpeciesRole.Bastion;
        private static Func<IKaijuBrain> Hold => () => new HoldBrain();

        private static float AngleAround(Float2 centre, Float2 p) => MathF.Atan2(p.Y - centre.Y, p.X - centre.X);
        private static float AngDiff(float a, float b)
        {
            float d = a - b;
            while (d > MathF.PI) d -= 2 * MathF.PI;
            while (d < -MathF.PI) d += 2 * MathF.PI;
            return MathF.Abs(d);
        }

        // ---------------------------------------------------------------- 1. flank
        [Test]
        public void Flank_TwoFlankersTakeOppositeSlotsAndReachNinetyDegreeSpread()
        {
            var sim = new PackSim(null);
            sim.Add(1, H, -3000, 0, Hold); sim.Add(2, S, -3100, 200, Hold);
            sim.Add(3, S, -3100, -200, Hold); sim.Add(4, M, -3200, 0, Hold);
            var mech = sim.AddMech(100, 0, 0);
            sim.Run(600f);

            Assert.AreEqual(PackGoal.Hunt, sim.Coord.Goal);
            var flankers = new List<SimMember>();
            foreach (var m in sim.Members) if (sim.Coord.RoleOf(m.S.Id) == PackRole.Flanker) flankers.Add(m);
            Assert.AreEqual(2, flankers.Count, "n=4 hunt fields two flankers");
            Assert.GreaterOrEqual(sim.Coord.EngagedSpreadRad, PackConfig.Default().FlankSpreadMinRad, "slot spread reaches the commit angle");

            // the members physically reached the slots: opposite sides of the approach line, achieved spread >= 90 deg
            float a0 = AngleAround(mech.Pos, flankers[0].S.Pos), a1 = AngleAround(mech.Pos, flankers[1].S.Pos);
            Assert.GreaterOrEqual(AngDiff(a0, a1), 90f * MathF.PI / 180f, "achieved flank spread");
            foreach (var f in flankers)
            {
                Assert.True(sim.Coord.TryGetDirective(f.S.Id, out var d));
                Assert.Less(PackSim.Dist(f.S.Pos, d.SlotPos), 80f, "flanker sits in its slot");
            }
            // flankers are on opposite sides of the pack-to-target axis
            float s0 = MathF.Sign(flankers[0].S.Pos.Y), s1 = MathF.Sign(flankers[1].S.Pos.Y);
            Assert.AreNotEqual(s0, s1);
        }

        // ---------------------------------------------------------------- 2. bait
        [Test]
        public void Bait_LightKaijuHoldsTheBandAndNeverMeleesWhileTheControlCharges()
        {
            float band = PackConfig.Default().BaitBandFrac * PackConfig.Default().MechWeaponRangeM;
            float MinDist(bool wrapped, out bool melee)
            {
                var sim = new PackSim(null);
                sim.Add(1, H, -3000, 0, Hold); sim.Add(2, M, -3000, 300, Hold);
                var bait = sim.Add(3, S, -2500, -300, () => new ChargeBrain(), wrapped);
                sim.AddMech(100, 0, 0);
                float min = float.MaxValue; melee = false;
                for (int i = 0; i < 400; i++)
                {
                    sim.Run(1f);
                    if (i > 20) { min = Math.Min(min, PackSim.Dist(bait.S.Pos, new Float2(0, 0))); melee |= bait.Melee; }
                    if (wrapped && i == 25) Assert.AreEqual(PackRole.Bait, sim.Coord.RoleOf(bait.S.Id), "the light species takes the bait slot");
                }
                return min;
            }
            float withPack = MinDist(true, out bool meleeWrapped);
            float control = MinDist(false, out bool meleeControl);
            Assert.False(meleeWrapped, "bait never melees");
            Assert.GreaterOrEqual(withPack, band * 0.8f, "bait stays near the weapon-range band");
            Assert.True(meleeControl, "control charges into melee");
            Assert.Less(control, 60f);
        }

        private static BrainObservation ObsAt(float sx, float sy, float hp, float tx, float ty)
        {
            var o = new BrainObservation();
            o.Self = new SelfObs { Id = EntityId.FromValue(2), Pos = new Float2(sx, sy), HpFrac = hp };
            o.TargetCount = 1; o.Targets[0] = new TargetObs { Id = EntityId.FromValue(100), Pos = new Float2(tx, ty), HpFrac = 1f, Alive = true, Visible = true };
            return o;
        }

        private sealed class FixedDirective : IPackDirectiveSource
        {
            public PackDirective D;
            public bool TryGetDirective(EntityId id, out PackDirective d) { d = D; return true; }
        }

        [Test]
        public void Bait_WoundedBaitBreaksOffEarlyAndHealthyBaitKitesTangentially()
        {
            var src = new FixedDirective { D = new PackDirective { Valid = true, Role = PackRole.Bait, Goal = PackGoal.Hunt, FocusTargetId = EntityId.FromValue(100), SlotPos = new Float2(-270, 0) } };
            var brain = new PackMemberBrain(new ChargeBrain(), src, PackConfig.Default());
            brain.Reset(1, BrainConfig.Default());
            var intent = new BrainIntent();

            brain.Decide(ObsAt(-250, 0, 1f, 0, 0), ref intent); // inside the band, healthy
            Assert.AreEqual(BrainAction.None, intent.Action);
            Assert.AreEqual(1f, MathF.Abs(MathF.Sin(intent.DesiredHeadingRad)), 0.2f, "heading is tangential (north or south) when the target is due east");

            brain.Decide(ObsAt(-250, 0, 0.5f, 0, 0), ref intent); // wounded
            Assert.AreEqual(BrainState.Retreat, intent.State);
            Assert.AreEqual(MathF.PI, MathF.Abs(intent.DesiredHeadingRad), 0.01f, "retreats directly away from the target");
        }

        // ---------------------------------------------------------------- 3. anchor
        [Test]
        public void Anchor_HeaviestFitHoldsStandoffAtHalfSpeedAcrossAllMixedPacks()
        {
            SpeciesRole[] all = { S, M, H };
            int packs = 0;
            for (int n = 2; n <= 5; n++)
            {
                int total = 1; for (int i = 0; i < n; i++) total *= 3;
                for (int code = 0; code < total; code++)
                {
                    var sim = new PackSim(null, (ulong)(100 + code));
                    int c = code; int bestSpecies = -1;
                    for (int i = 0; i < n; i++)
                    {
                        int sp = c % 3; c /= 3;
                        sim.Add((ulong)(i + 1), all[sp], -4000 - 50 * i, 100 * i, Hold, false);
                        if (sp > bestSpecies) bestSpecies = sp;
                    }
                    sim.AddMech(100, 0, 0);
                    sim.TickCoordinator();
                    packs++;
                    int anchors = 0, free = 0; SimMember anchor = null;
                    foreach (var m in sim.Members)
                    {
                        PackRole r = sim.Coord.RoleOf(m.S.Id);
                        if (r == PackRole.Free) free++;
                        if (r == PackRole.Anchor) { anchors++; if (anchor == null) anchor = m; }
                    }
                    Assert.AreEqual(0, free, $"pack {code}/{n}: every member has a role");
                    if (n >= 3)
                    {
                        Assert.GreaterOrEqual(anchors, 1, $"pack {code}/{n} has an anchor");
                        Assert.AreEqual(all[bestSpecies].SpeciesId, anchor.S.Species.SpeciesId, $"pack {code}/{n}: the best anchor fit holds the anchor slot");
                        Assert.True(sim.Coord.TryGetDirective(anchor.S.Id, out var d));
                        Assert.AreEqual(anchor.S.Species.StandoffM, PackSim.Dist(d.SlotPos, new Float2(0, 0)), 1f, "anchor slot at the species standoff");
                    }
                    // determinism: a second coordinator with the same seed and inputs gives the same roles
                    var sim2 = new PackSim(null, (ulong)(100 + code));
                    c = code;
                    for (int i = 0; i < n; i++) { int sp = c % 3; c /= 3; sim2.Add((ulong)(i + 1), all[sp], -4000 - 50 * i, 100 * i, Hold, false); }
                    sim2.AddMech(100, 0, 0); sim2.TickCoordinator();
                    foreach (var m in sim.Members) Assert.AreEqual(sim.Coord.RoleOf(m.S.Id), sim2.Coord.RoleOf(m.S.Id));
                }
            }
            Assert.AreEqual(9 + 27 + 81 + 243, packs);
        }

        [Test]
        public void Anchor_HalvesSpeedInsideItsSlotAndSteersToItOutside()
        {
            var src = new FixedDirective { D = new PackDirective { Valid = true, Role = PackRole.Anchor, Goal = PackGoal.Hunt, FocusTargetId = EntityId.FromValue(100), SlotPos = new Float2(-560, 0) } };
            var brain = new PackMemberBrain(new FixedSpeedBrain(0.6f), src, PackConfig.Default());
            brain.Reset(1, BrainConfig.Default());
            var intent = new BrainIntent();
            brain.Decide(ObsAt(-560, 10, 1f, 0, 0), ref intent);
            Assert.AreEqual(0.3f, intent.SpeedFrac, 1e-5f);
            brain.Decide(ObsAt(-2000, 0, 1f, 0, 0), ref intent);
            Assert.AreEqual(0f, MathF.Abs(intent.DesiredHeadingRad), 0.01f, "outside the slot it walks to it");
        }

        private sealed class FixedSpeedBrain : IKaijuBrain
        {
            private readonly float _v;
            public FixedSpeedBrain(float v) { _v = v; }
            public int ContractVersion => 1;
            public void Reset(ulong seed, in BrainConfig cfg) { }
            public void Decide(in BrainObservation obs, ref BrainIntent intent) { intent = default; intent.SpeedFrac = _v; intent.State = BrainState.Idle; }
        }

        [Test]
        public void Anchor_SlowsAfterReachingSlot()
        {
            var sim = new PackSim(null);
            var anchor = sim.Add(1, H, -3000, 0, Hold);
            sim.Add(2, S, -3000, 300, Hold);
            sim.AddMech(100, 0, 0);
            sim.Run(800f);
            Assert.AreEqual(PackRole.Anchor, sim.Coord.RoleOf(anchor.S.Id));
            Assert.Less(PackSim.Dist(anchor.S.Pos, new Float2(0, 0)), H.StandoffM + 70f);
            Assert.GreaterOrEqual(PackSim.Dist(anchor.S.Pos, new Float2(0, 0)), H.StandoffM - 70f);
        }

        // ---------------------------------------------------------------- 4. raid vs hunt
        private static SimInfra OneBridge(float x, float y, float hp = 100f)
        {
            var infra = new SimInfra();
            infra.Items.Add(new InfraTarget { Id = EntityId.FromValue(900), Pos = new Float2(x, y), Class = 2, Hp = hp, Alive = true });
            return infra;
        }

        [Test]
        public void RaidVsHunt_FarMechRaidsThenSwitchesToHuntWhenItApproachesAndNeverBeforeDwell()
        {
            var infra = OneBridge(8000, 0);
            var sim = new PackSim(infra);
            sim.Add(1, H, 0, 0, Hold); sim.Add(2, M, 0, 100, Hold); sim.Add(3, S, 0, -100, Hold);
            var mech = sim.AddMech(100, 40000, 5000);
            sim.TickCoordinator();
            Assert.AreEqual(PackGoal.Raid, sim.Coord.Goal, "distant mech: infrastructure raid has the higher utility");
            Assert.Greater(sim.Coord.RaidUtility, sim.Coord.HuntUtility);
            Assert.AreEqual(EntityId.FromValue(900), sim.Coord.RaidTargetId);
            Assert.AreEqual(PackRole.Raider, sim.Coord.RoleOf(EntityId.FromValue(2)), "Breacher raids (0.7), Bastion escorts as anchor, Strider baits: best summed bid");
            Assert.AreEqual(PackRole.Anchor, sim.Coord.RoleOf(EntityId.FromValue(1)));

            // the mech jumps close 10 s in: dwell (30 s) holds the goal
            sim.Run(10f);
            mech.Pos = new Float2(1500, 0);
            sim.TickCoordinator();
            Assert.AreEqual(PackGoal.Raid, sim.Coord.Goal, "no voluntary goal switch before the dwell");
            int switchesBefore = sim.Coord.Stats.GoalSwitches;
            sim.Run(30f);
            Assert.AreEqual(PackGoal.Hunt, sim.Coord.Goal, "after the dwell the much better hunt wins");
            Assert.AreEqual(switchesBefore + 1, sim.Coord.Stats.GoalSwitches);
            Assert.Greater(sim.Coord.HuntUtility, sim.Coord.RaidUtility * PackConfig.Default().GoalSwitchRatio);

            // stable afterwards (no thrash)
            sim.Run(120f);
            Assert.AreEqual(switchesBefore + 1, sim.Coord.Stats.GoalSwitches);
        }

        [Test]
        public void RaidVsHunt_DestroyedTargetForcesAnImmediateSwitchAndMechKillBackToRaid()
        {
            var infra = OneBridge(8000, 0);
            var sim = new PackSim(infra);
            sim.Add(1, H, 0, 0, Hold); sim.Add(2, M, 0, 100, Hold);
            var mech = sim.AddMech(100, 40000, 0);
            sim.TickCoordinator();
            Assert.AreEqual(PackGoal.Raid, sim.Coord.Goal);
            infra.Destroy(EntityId.FromValue(900));
            sim.Run(1f); sim.TickCoordinator();
            Assert.AreEqual(PackGoal.Hunt, sim.Coord.Goal, "raid target gone: forced switch, no dwell wait");
            Assert.AreEqual(1, sim.Coord.Stats.GoalSwitches);
            // a second bridge appears and the mech dies
            infra.Items.Add(new InfraTarget { Id = EntityId.FromValue(901), Pos = new Float2(5000, 0), Class = 2, Hp = 100f, Alive = true });
            mech.Alive = false;
            sim.Run(2f); sim.TickCoordinator();
            Assert.AreEqual(PackGoal.Raid, sim.Coord.Goal);
            Assert.AreEqual(EntityId.FromValue(901), sim.Coord.RaidTargetId);
        }

        [Test]
        public void RaidVsHunt_DefendedTargetIsLessAttractiveThanAnUndefendedOne()
        {
            var infra = OneBridge(8000, 0);
            infra.Items.Add(new InfraTarget { Id = EntityId.FromValue(901), Pos = new Float2(-9000, 0), Class = 2, Hp = 100f, Alive = true });
            var sim = new PackSim(infra);
            sim.Add(1, M, 0, 0, Hold); sim.Add(2, S, 0, 100, Hold);
            sim.AddMech(100, 8500, 0); // a mech guards the nearer bridge
            sim.TickCoordinator();
            Assert.AreEqual(PackGoal.Raid, sim.Coord.Goal);
            Assert.AreEqual(EntityId.FromValue(901), sim.Coord.RaidTargetId, "the undefended, slightly farther bridge wins");
        }

        // ---------------------------------------------------------------- 5. member death
        [Test]
        public void MemberDeath_AnchorLossReassignsForcedWithoutThrashThenPackWithoutAnchorStopsHunting()
        {
            var infra = OneBridge(80000, 0); // far: hunt first
            var sim = new PackSim(infra);
            sim.Add(1, H, -3000, 0, Hold); sim.Add(2, M, -3100, 200, Hold);
            sim.Add(3, S, -3100, -200, Hold); sim.Add(4, S, -3200, 100, Hold); sim.Add(5, S, -3200, -100, Hold);
            sim.AddMech(100, 0, 0);
            sim.Run(60f);
            Assert.AreEqual(PackGoal.Hunt, sim.Coord.Goal);
            Assert.AreEqual(PackRole.Anchor, sim.Coord.RoleOf(EntityId.FromValue(1)));
            var statsBefore = sim.Coord.Stats;

            sim.Kill(1);
            sim.Run(2f);
            Assert.False(sim.Coord.TryGetDirective(EntityId.FromValue(1), out _), "dead member has no directive");
            Assert.AreEqual(PackGoal.Hunt, sim.Coord.Goal, "pack with a Breacher still anchors and hunts");
            Assert.AreEqual(PackRole.Anchor, sim.Coord.RoleOf(EntityId.FromValue(2)), "next best fit takes over the anchor");
            Assert.Greater(sim.Coord.Stats.ForcedRoleChanges, statsBefore.ForcedRoleChanges);
            int voluntaryAfterLoss = sim.Coord.Stats.RoleChanges - sim.Coord.Stats.ForcedRoleChanges;

            sim.Run(60f); // stable: no voluntary thrash afterwards
            Assert.AreEqual(voluntaryAfterLoss, sim.Coord.Stats.RoleChanges - sim.Coord.Stats.ForcedRoleChanges);
            Assert.AreEqual(PackRole.Anchor, sim.Coord.RoleOf(EntityId.FromValue(2)));

            // the last anchor-capable member dies: three Striders cannot cover the anchor (fit 0.1 < 0.3) -> no hunt
            sim.Kill(2);
            sim.Run(2f);
            Assert.AreNotEqual(PackGoal.Hunt, sim.Coord.Goal, "covering rule: no member fits the anchor, the pack stops hunting");
            Assert.AreEqual(PackGoal.Raid, sim.Coord.Goal, "a far bridge is the remaining viable goal");
            Assert.AreEqual(3, sim.Coord.EligibleCount);
        }

        [Test]
        public void MemberDeath_RetreatingMemberLeavesTheAllocationAndRegroupHasARallyPoint()
        {
            var sim = new PackSim(null);
            sim.Add(1, M, -3000, 0, Hold); sim.Add(2, S, -3000, 200, Hold); sim.Add(3, S, -3000, -200, Hold);
            sim.AddMech(100, 0, 0);
            sim.Run(30f);
            Assert.AreEqual(3, sim.Coord.EligibleCount);
            sim.Members[1].S.State = BrainState.Retreat; // brain withdrew: leaves allocation
            sim.TickCoordinator();
            Assert.AreEqual(2, sim.Coord.EligibleCount);
            Assert.False(sim.Coord.TryGetDirective(EntityId.FromValue(2), out _));
            sim.Kill(1); sim.Kill(3);
            sim.TickCoordinator();
            Assert.AreEqual(PackGoal.None, sim.Coord.Goal, "no live eligible member: nothing to coordinate");
        }

        // ---------------------------------------------------------------- determinism and allocation
        [Test]
        public void Determinism_SameSeedSameTrajectoryDifferentSeedMayDifferInFlankSide()
        {
            string Run(ulong seed)
            {
                var sim = new PackSim(OneBridge(8000, 0), seed);
                sim.Add(1, H, -3000, 0, Hold); sim.Add(2, S, -3000, 200, Hold); sim.Add(3, M, -3000, -200, Hold); sim.Add(4, S, -3100, 0, Hold);
                sim.AddMech(100, 0, 0);
                sim.Run(120f);
                var sb = new System.Text.StringBuilder();
                foreach (var m in sim.Members) sb.Append(sim.Coord.RoleOf(m.S.Id)).Append(':').Append(m.S.Pos.X.ToString("R")).Append(',').Append(m.S.Pos.Y.ToString("R")).Append(';');
                return sb.ToString();
            }
            Assert.AreEqual(Run(11), Run(11));
            bool anyDiffer = false;
            for (ulong s = 1; s < 8 && !anyDiffer; s++) anyDiffer = Run(s) != Run(s + 100);
            Assert.True(anyDiffer, "the seed picks the first flank side");
        }

        [Test]
        public void SteadyStateUpdateDoesNotAllocate()
        {
            var sim = new PackSim(OneBridge(8000, 0));
            sim.Add(1, H, -3000, 0, Hold); sim.Add(2, S, -3000, 200, Hold); sim.Add(3, M, -3000, -200, Hold);
            sim.AddMech(100, 0, 0);
            sim.Run(20f);
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++) sim.TickCoordinator();
            long a1 = GC.GetAllocatedBytesForCurrentThread();
            Assert.LessOrEqual(a1 - a0, 0, "PackCoordinator.Update + PackBoard.Ingest: 0 B steady state");
        }
    }
}
