// T-024 step 3: debris increments, mass conservation and the M-052 energy account bounds.
using System;
using Game.Core;
using NUnit.Framework;

namespace Game.World.Destruction.Tests
{
    public class DebrisAccountTests
    {
        static BuildingId Id(ulong n) { return BuildingId.From(BuildingSource.OsmWay, n); }

        // M-052 archetypes: house, mid-rise, tower, wide low warehouse, slender tower.
        static readonly BuildingAttributes[] Archetypes =
        {
            new BuildingAttributes { HeightM = 7, Levels = 2, FootprintM2 = 100, Residential = true },
            new BuildingAttributes { HeightM = 25, Levels = 6, FootprintM2 = 600 },
            new BuildingAttributes { HeightM = 160, Levels = 40, FootprintM2 = 1500 },
            new BuildingAttributes { HeightM = 8, Levels = 2, FootprintM2 = 10000 },
            new BuildingAttributes { HeightM = 120, Levels = 30, FootprintM2 = 150 },
        };

        static double[] Thresholds(DamageModel m, BuildingId id)
        {
            m.TryGetThresholds(id, out var a, out var b, out var c, out var d);
            return new[] { a, b, c, d };
        }

        // Drives a building to every state in `path` (strictly increasing) by cumulative energy.
        static void Drive(DamageModel m, BuildingId id, params int[] path)
        {
            var t = Thresholds(m, id);
            foreach (int k in path)
            {
                m.TryGetState(id, out var s);
                m.Apply(id, t[k - 1] * 1.0001 - s.CumulativeEnergyJ, DamageCause.Impact, default, 1);
            }
        }

        static DebrisAccount Account(DamageModel m, BuildingId id)
        {
            Assert.IsTrue(m.TryGetDebrisAccount(id, out var a));
            return a;
        }

        static readonly int[][] Paths =
        {
            new int[0], new[] { 1 }, new[] { 2 }, new[] { 3 }, new[] { 4 }, new[] { 1, 2 }, new[] { 1, 3 }, new[] { 1, 4 },
            new[] { 2, 3 }, new[] { 2, 4 }, new[] { 3, 4 }, new[] { 1, 2, 3, 4 }, new[] { 1, 3, 4 },
        };

        [Test]
        public void MassConserved_AllStatePaths_AllArchetypes()
        {
            for (int i = 0; i < Archetypes.Length; i++)
                foreach (var path in Paths)
                {
                    var m = new DamageModel();
                    var id = Id((ulong)(100 + i));
                    m.Register(id, Archetypes[i]);
                    Drive(m, id, path);
                    var a = Account(m, id);
                    Assert.LessOrEqual(Math.Abs(a.MassKg - (a.StandingKg + a.DebrisKg)) / a.MassKg, 1e-6);
                    Assert.LessOrEqual(Math.Abs(a.PieceMassSumKg - a.DebrisKg), 1e-6 * Math.Max(a.DebrisKg, 1.0));
                    Assert.LessOrEqual(a.PieceCount, 64);
                    m.TryGetState(id, out var s);
                    Assert.AreEqual(s.DebrisKg, a.DebrisKg, 0.0);
                    Assert.GreaterOrEqual(a.StandingKg, 0.0);
                }
        }

        [Test]
        public void TwoStep_And_Direct_Totals_Equal()
        {
            for (int i = 0; i < Archetypes.Length; i++)
            {
                var m = new DamageModel();
                var a1 = Id(1); var a2 = Id(2);
                m.Register(a1, Archetypes[i]); m.Register(a2, Archetypes[i]);
                Drive(m, a1, 1, 3, 4);
                var t = Thresholds(m, a2);
                m.Apply(a2, t[3] * 1.0001, DamageCause.Impact, default, 1);
                var x = Account(m, a1); var y = Account(m, a2);
                Assert.AreEqual(y.DebrisKg, x.DebrisKg, 1e-6 * y.DebrisKg);
                Assert.AreEqual(y.PotentialEnergyJ, x.PotentialEnergyJ, 1e-6 * y.PotentialEnergyJ);
            }
        }

        [Test]
        public void Debris_IsMonotone_AndIncrementsSumToTotal()
        {
            var bus = new RecordingBus();
            var m = new DamageModel(bus);
            var id = Id(7);
            m.Register(id, Archetypes[1]);
            double last = 0, sumInc = 0;
            var t = Thresholds(m, id);
            for (int k = 1; k <= 4; k++)
            {
                m.Apply(id, t[k - 1] * 1.0001 - (k > 1 ? t[k - 2] * 1.0001 : 0), DamageCause.Impact, default, k);
                m.TryGetState(id, out var s);
                Assert.GreaterOrEqual(s.DebrisKg, last);
                Assert.Greater(s.DebrisKg, last, "each state change adds debris");
                last = s.DebrisKg;
            }
            foreach (var e in bus.Events) sumInc += e.DebrisKg;
            Assert.AreEqual(4, bus.Events.Count);
            Assert.AreEqual(last, sumInc, 1e-5 * last);
            // Sub-threshold and repeated energy add no debris.
            m.Apply(id, 1.0, DamageCause.Impact, default, 9);
            m.TryGetState(id, out var f);
            Assert.AreEqual(last, f.DebrisKg, 0.0);
        }

        [Test]
        public void Event_CarriesDebrisIncrement_ForImpactAndShaking()
        {
            var bus = new RecordingBus();
            var m = new DamageModel(bus);
            var id = Id(9);
            m.Register(id, Archetypes[1]);
            var cap = CapacityModel.Compute(id, Archetypes[1]);
            var t = Thresholds(m, id);
            m.Apply(id, t[0] * 1.0001, DamageCause.Impact, default, 1);   // 0 -> 1
            Assert.AreEqual((float)(0.01 * cap.MassKg), bus.Events[0].DebrisKg, 1e-3f * (float)cap.MassKg * 0.01f);
            m.Apply(id, t[2] * 1.0001 - t[0] * 1.0001, DamageCause.Impact, default, 2);   // 1 -> 3
            Assert.AreEqual(1, bus.Events[1].PrevState - 0);
            Assert.AreEqual((float)(0.24 * cap.MassKg), bus.Events[1].DebrisKg, 1e-3f * (float)cap.MassKg * 0.24f);

            var m2 = new DamageModel(bus);
            var sid = Id(10);
            m2.Register(sid, Archetypes[1]);
            int before = bus.Events.Count;
            m2.ApplyShaking(sid, 3.0, 1, 5);
            m2.TryGetState(sid, out var s);
            Assert.Greater(s.State, 0);
            Assert.AreEqual(before + 1, bus.Events.Count);
            Assert.AreEqual((float)s.DebrisKg, bus.Events[before].DebrisKg, 1e-4f * (float)s.DebrisKg);
            Assert.Greater(bus.Events[before].DebrisKg, 0f);
        }

        [Test]
        public void EnergyAccount_WithinM052Bounds_EveryClassAndState()
        {
            for (int i = 0; i < Archetypes.Length; i++)
                for (int k = 1; k <= 4; k++)
                {
                    var m = new DamageModel();
                    var id = Id((ulong)(300 + i));
                    m.Register(id, Archetypes[i]);
                    Drive(m, id, k);
                    var a = Account(m, id);
                    Assert.Greater(a.PotentialEnergyJ, 0.0, $"arch {i} state {k}");
                    Assert.GreaterOrEqual(a.ResidualJ, 0.0, $"arch {i} state {k}");
                    Assert.LessOrEqual(a.ResidualJ / a.PotentialEnergyJ, 0.05, $"arch {i} state {k}");
                    Assert.LessOrEqual(a.EjectaJ, 0.10 * a.PotentialEnergyJ);
                    Assert.LessOrEqual(a.FractureJ + a.GroundJ + a.EjectaJ, a.PotentialEnergyJ * (1 + 1e-12));
                    double bal = a.PotentialEnergyJ - (a.FractureJ + a.GroundJ + a.EjectaJ + a.ResidualJ);
                    Assert.LessOrEqual(Math.Abs(bal), 1e-9 * a.PotentialEnergyJ);
                }
        }

        [Test]
        public void MidRiseComplete_MatchesR021Example_Order()
        {
            // Complete mid-rise (C2.M, phi 0.10): M_d = 0.325 M0; dPE is of the order M_d g H/2 (R-021 §3 example).
            var m = new DamageModel();
            var id = Id(11);
            m.Register(id, Archetypes[1]);
            Drive(m, id, 4);
            var a = Account(m, id);
            Assert.AreEqual(0.325 * a.MassKg, a.DebrisKg, 1e-9 * a.MassKg);
            double upper = a.DebrisKg * DamageParameters.Gravity * 0.5 * 25.0;
            Assert.Less(a.PotentialEnergyJ, upper);
            Assert.Greater(a.PotentialEnergyJ, 0.9 * upper);
        }

        [Test]
        public void UnknownBuilding_And_ReRegister_KeepConsistentDebris()
        {
            var m = new DamageModel();
            Assert.IsFalse(m.TryGetDebrisAccount(Id(404), out _));
            var id = Id(12);
            m.Register(id, Archetypes[0]);
            Assert.AreEqual(0.0, Account(m, id).DebrisKg);
            Drive(m, id, 3);
            m.Register(id, Archetypes[1]);   // new capacity, damage kept
            var a = Account(m, id);
            m.TryGetState(id, out var s);
            Assert.AreEqual(3, s.State);
            Assert.AreEqual(0.25 * a.MassKg, s.DebrisKg, 1e-9 * a.MassKg);
        }
    }
}
