using System;
using Game.AI.Pack;
using Game.Core;
using NUnit.Framework;

namespace Game.AI.Pack.Tests
{
    /// <summary>Bay-scale hunt tactics (T-115b, <see cref="PackConfig.Bay"/>): ring-up, commit gate, Bait staging, Bait retreat latch, D_eng-bounded slots.</summary>
    [TestFixture]
    public sealed class PackBayTests
    {
        private static readonly SpeciesRole S = SpeciesRole.Strider, M = SpeciesRole.Breacher, H = SpeciesRole.Bastion;

        private static PackSim BaySim()
        {
            var sim = new PackSim(null);
            sim.Cfg = PackConfig.Bay();
            sim.Coord.Reset(7, sim.Cfg);
            return sim;
        }

        [Test]
        public void Bay_GateStaysClosedInTransitThenOpensWithAWideFlankSpread()
        {
            var sim = BaySim();
            sim.Add(1, S, -3000, 100, () => new HoldBrain()); sim.Add(2, M, -3050, -100, () => new HoldBrain()); sim.Add(3, H, -3100, 0, () => new HoldBrain());
            sim.AddMech(100, 0, 0);
            sim.Run(5f);
            foreach (var m in sim.Members)
            {
                Assert.IsTrue(sim.Coord.TryGetDirective(m.S.Id, out var d));
                Assert.IsFalse(d.CommitReady, "no commit while the pack is still 3 km out");
            }
            bool opened = false; float spread = 0f; var centre = new Float2(0, 0);
            for (int t = 0; t < 900 && !opened; t++)
            {
                sim.Run(1f);
                if (sim.Coord.TryGetDirective(sim.Members[0].S.Id, out var d0) && d0.CommitReady)
                {
                    opened = true;
                    // bearings of the members that are inside their engagement radius (5 x height) at that moment
                    var b = new System.Collections.Generic.List<float>();
                    foreach (var m in sim.Members)
                        if (PackSim.Dist(centre, m.S.Pos) <= 5f * m.S.Species.HeightM) b.Add(MathF.Atan2(m.S.Pos.Y, m.S.Pos.X));
                    for (int i = 0; i < b.Count; i++)
                        for (int j = i + 1; j < b.Count; j++)
                        {
                            float a = MathF.Abs(b[i] - b[j]); if (a > MathF.PI) a = 2 * MathF.PI - a;
                            if (a > spread) spread = a;
                        }
                }
            }
            Assert.IsTrue(opened, "the gate opens once the pack is ringed up, the flankers hold and the Bait has drawn the lock");
            Assert.GreaterOrEqual(spread, 90f * MathF.PI / 180f, "the engaged members stand at least 90 degrees apart when the gate opens");
        }

        [Test]
        public void Bay_SlotsStayInsideTheEngagementRadiusOfEverySpecies()
        {
            var sim = BaySim();
            sim.Add(1, S, -3000, 0, () => new HoldBrain()); sim.Add(2, M, -3000, 100, () => new HoldBrain()); sim.Add(3, H, -3000, -100, () => new HoldBrain());
            sim.AddMech(100, 0, 0);
            sim.Run(900f);
            foreach (var m in sim.Members)
            {
                Assert.IsTrue(sim.Coord.TryGetDirective(m.S.Id, out var d));
                float r = PackSim.Dist(new Float2(0, 0), d.SlotPos);
                Assert.LessOrEqual(r, 5f * m.S.Species.HeightM + 1f, "engagement slot of " + m.S.Id.Value + " is inside D_eng so the member counts as engaged");
            }
        }

        [Test]
        public void Bay_BaitFirst_StrikersHoldOutsideWeaponRangeAtLeast90DegreesFromTheBait()
        {
            // T-113b: Strider Bait, Strider + Breacher Flankers, Bastion Anchor; every striker slot is >= 90 deg from the Bait slot (P4, C1) and outside weapon range
            var sim = BaySim();
            sim.Cfg.BaitFirst = true; sim.Coord.Reset(7, sim.Cfg);   // flag off in PackConfig.Bay() (T-113b evidence), on here
            sim.Add(1, S, -3000, 0, () => new HoldBrain()); sim.Add(2, S, -3000, 150, () => new HoldBrain());
            sim.Add(3, M, -3000, -150, () => new HoldBrain()); sim.Add(4, H, -3100, 0, () => new HoldBrain());
            sim.AddMech(100, 0, 0);
            int checkedTicks = 0;
            for (int t = 0; t < 600; t++)
            {
                sim.Run(1f);
                float bait = float.NaN; var strikers = new System.Collections.Generic.List<(float ang, float r)>();
                foreach (var m in sim.Members)
                {
                    Assert.IsTrue(sim.Coord.TryGetDirective(m.S.Id, out var d));
                    if (d.Goal != PackGoal.Hunt) continue;
                    float a = MathF.Atan2(d.SlotPos.Y, d.SlotPos.X), r = PackSim.Dist(new Float2(0, 0), d.SlotPos);
                    if (d.Role == PackRole.Bait) bait = a;
                    else if (d.Role == PackRole.Anchor || d.Role == PackRole.Flanker) strikers.Add((a, r));
                }
                if (float.IsNaN(bait) || sim.Coord.TryGetDirective(sim.Members[0].S.Id, out var d0) && d0.CommitReady) continue;
                if (strikers.Count == 3) checkedTicks++;
                foreach (var (a, r) in strikers)
                {
                    float diff = MathF.Abs(a - bait); if (diff > MathF.PI) diff = 2 * MathF.PI - diff;
                    Assert.GreaterOrEqual(diff, 90f * MathF.PI / 180f - 1e-3f, "striker slot vs Bait slot at t=" + t);
                    Assert.GreaterOrEqual(r, sim.Cfg.MechWeaponRangeM + sim.Cfg.HoldOutsideMarginM - 1f, "striker holds outside weapon range");
                }
            }
            Assert.Greater(checkedTicks, 30, "the Bait + three strikers formation was checked before the gate opened");
        }

        [Test]
        public void Bay_ABaitBelowItsRetreatHpLeavesForGoodEvenWithoutADirective()
        {
            var cfg = PackConfig.Bay();
            var brain = new PackMemberBrain(new HoldBrain(), null, cfg);
            var bc = BrainConfig.Default(); brain.Reset(1, bc);
            var obs = new BrainObservation();
            obs.Self = new SelfObs { Id = EntityId.FromValue(1), Pos = new Float2(250, 0), HpFrac = 0.5f };
            obs.Targets[0] = new TargetObs { Id = EntityId.FromValue(100), Pos = new Float2(0, 0), HpFrac = 1f, Alive = true, Visible = true };
            obs.TargetCount = 1;
            var d = new PackDirective { Valid = true, Role = PackRole.Bait, Goal = PackGoal.Hunt, FocusTargetId = EntityId.FromValue(100), SlotPos = new Float2(250, 0) };
            var intent = new BrainIntent();
            brain.Decide(obs, d, ref intent);
            Assert.AreEqual(BrainState.Retreat, intent.State, "Bait below the retreat hp retreats");
            // healed or not, it is latched: the next decision without any directive still retreats (no flapping in and out of the allocation)
            obs.Self.HpFrac = 1f; obs.Self.Pos = new Float2(900, 0);
            brain.Decide(obs, ref intent);
            Assert.IsTrue(intent.State == BrainState.Withdrawn || intent.State == BrainState.Retreat, "latched: " + intent.State);
        }
    }
}
