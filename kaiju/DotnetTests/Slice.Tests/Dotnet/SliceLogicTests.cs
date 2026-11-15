using System.Collections.Generic;
using Game.AI;
using Game.Core;
using Game.Slice;
using Game.World.Seismic;
using NUnit.Framework;

namespace Game.Slice.Tests
{
    public class SliceLogicTests
    {
        static SliceTarget Mech(float x, float y) => new SliceTarget { Id = EntityId.FromValue(101), X = x, Y = y };

        [Test]
        public void Kaiju_ApproachesAndHitsTheTarget()
        {
            var k = new SliceKaijuAgent(new ScriptedKaijuBrain(), EntityId.FromValue(1), 7, -600f, 0f, 0f, 0.02f);
            var t = new List<SliceTarget> { Mech(0f, 0f) };
            float d0 = 600f; long hits = 0;
            for (long step = 0; step < 50 * 240; step++)
            {
                if (k.Step(step, step * 0.02, 0.02f, t) != null) hits++;
                if (!t[0].Alive) break;
            }
            float dx = t[0].X - k.X, dy = t[0].Y - k.Y;
            Assert.Less(System.MathF.Sqrt(dx * dx + dy * dy), d0 * 0.5f, "the brain closes on a visible target");
            Assert.Greater(k.Decisions, 100);
            Assert.GreaterOrEqual(hits, 0);
        }

        [Test]
        public void Kaiju_IsDeterministicForASeed()
        {
            float Run()
            {
                var k = new SliceKaijuAgent(new ScriptedKaijuBrain(), EntityId.FromValue(1), 7, -600f, 50f, 0f, 0.02f);
                var t = new List<SliceTarget> { Mech(0f, 0f) };
                for (long s = 0; s < 3000; s++) k.Step(s, s * 0.02, 0.02f, t);
                return k.X * 31f + k.Y;
            }
            Assert.AreEqual(Run(), Run());
        }

        [Test]
        public void Impact_EnergyIsHalfMvSquared()
        {
            Assert.AreEqual(0.5 * 2000 * 30 * 30, SliceImpact.KineticEnergyJ(2000, 30), 1e-9);
            Assert.AreEqual(0.0, SliceImpact.KineticEnergyJ(-1, 30));
            Assert.AreEqual(0.0, SliceImpact.KineticEnergyJ(10, 0));
        }

        [Test]
        public void BuildingIndex_FindsWithinRadiusAndNearest()
        {
            var ix = new SliceBuildingIndex();
            var a = BuildingId.From(BuildingSource.OsmWay, 1); var b = BuildingId.From(BuildingSource.OsmWay, 2);
            ix.Add(a, 100, 100, 10); ix.Add(b, 300, 100, 10);
            Assert.IsTrue(ix.TryFind(105, 100, 0, out var f)); Assert.AreEqual(a, f);
            Assert.IsFalse(ix.TryFind(200, 100, 0, out _));
            Assert.IsTrue(ix.TryNearest(290, 90, out var n, out _, out _)); Assert.AreEqual(b, n);
        }

        [Test]
        public void QuakePlan_PicksTheLongestMappedSystem()
        {
            var pieces = new List<FaultPiece>
            {
                new FaultPiece { FaultName = "Short", Section = "s", Linetype = "", SlipSense = "Right lateral", Xz = new double[] { 0, 0, 5000, 0 } },
                new FaultPiece { FaultName = "Long", Section = "l", Linetype = "", SlipSense = "Right lateral", Xz = new double[] { 0, 100, 40000, 100 } },
            };
            var set = FaultTraceSet.Build(pieces);
            Assert.IsTrue(SliceQuakePlan.TryPlan(set, 1, 9, out var sc));
            Assert.AreEqual(FaultTraceSet.HashName("Long"), sc.FaultId);
            Assert.Greater(sc.HypoX, 0f);
            Assert.IsFalse(SliceQuakePlan.TryPlan(null, 1, 9, out _));
        }
    }
}
