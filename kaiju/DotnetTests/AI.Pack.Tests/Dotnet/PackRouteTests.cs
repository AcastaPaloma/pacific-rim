using System;
using Game.AI.Pack;
using Game.Core;
using NUnit.Framework;

namespace Game.AI.Pack.Tests
{
    /// <summary>T-115c: GridRoutePlanner on a cost field (deep lake, soft band), flag gating of the pack layer.</summary>
    [TestFixture]
    public sealed class PackRouteTests
    {
        // 40 x 40 cells of 250 m; deep lake x 4000..6000, y 1000..9000 (depth 100 m), soft band x 6000..7000 (all y)
        private sealed class Field : IBayCostField
        {
            public float OriginX => 0f; public float OriginY => 0f; public float CellM => 250f; public int Nx => 40; public int Ny => 40;
            public Func<int, int, float> Depth = (x, y) => x >= 16 && x < 24 && y >= 4 && y < 36 ? 100f : 0f;
            public Func<int, int, float> Soft = (x, y) => x >= 24 && x < 28 ? 1f : 0f;
            public float DepthM(int ix, int iy) => Depth(ix, iy);
            public float Softness(int ix, int iy) => Soft(ix, iy);
        }

        private static readonly SpeciesRole S = SpeciesRole.Strider, H = SpeciesRole.Bastion;
        private static float Len(Float2 a, Float2 b) { float dx = b.X - a.X, dy = b.Y - a.Y; return MathF.Sqrt(dx * dx + dy * dy); }

        [Test]
        public void ClearLine_ReturnsTheGoalAndTheStraightLength()
        {
            var p = new GridRoutePlanner(new Field { Soft = (x, y) => 0f });
            var a = new Float2(500, 500); var b = new Float2(3000, 500);
            Assert.AreEqual(b, p.NextWaypoint(a, b, S));
            Assert.AreEqual(2500f, p.PathLength(a, b, S), 5f);
            Assert.AreEqual(0, p.Searches);
        }

        [Test]
        public void DeepLakeBetween_DetoursAroundTheEnd()
        {
            var p = new GridRoutePlanner(new Field { Soft = (x, y) => 0f });
            var a = new Float2(2000, 5000); var b = new Float2(8500, 5000);
            Float2 w = p.NextWaypoint(a, b, S);
            Assert.AreNotEqual(b, w, "straight line crosses 100 m of water");
            float len = p.PathLength(a, b, S);
            Assert.Greater(len, Len(a, b) * 1.25f);
            Assert.Less(len, Len(a, b) * 2.2f);
            // walk the waypoints: never inside a blocked cell, arrives at the goal
            var f = new Field(); Float2 pos = a; int guard = 0;
            while (Len(pos, b) > 100f && guard++ < 400)
            {
                Float2 next = p.NextWaypoint(pos, b, S); float d = Len(pos, next); float stepLen = Math.Min(d, 100f);
                pos = new Float2(pos.X + (next.X - pos.X) / d * stepLen, pos.Y + (next.Y - pos.Y) / d * stepLen);
                Assert.AreEqual(0f, f.Depth((int)(pos.X / 250f), (int)(pos.Y / 250f)), "walked into the lake at " + pos.X + "," + pos.Y);
            }
            Assert.Less(guard, 400);
        }

        [Test]
        public void ShallowSpeciesLimit_TheTallSpeciesWadesWhereTheShortOneCannot()
        {
            var shelf = new Field { Depth = (x, y) => x >= 16 && x < 24 ? 30f : 0f, Soft = (x, y) => 0f };   // 30 m: Strider (24 m limit) blocked, Bastion (64 m) wades
            var p = new GridRoutePlanner(shelf);
            var a = new Float2(1000, 5000); var b = new Float2(9000, 5000);
            Assert.AreEqual(b, p.NextWaypoint(a, b, H));
            Assert.AreEqual(8000f, p.PathLength(a, b, H), 5f);
            Assert.AreEqual(float.PositiveInfinity, p.PathLength(a, b, S), "a wall of deep water across the whole map has no way round");
        }

        [Test]
        public void SoftGround_CostsMoreThanItsLengthAndRoutesPreferFirmGround()
        {
            var p = new GridRoutePlanner(new Field { Depth = (x, y) => 0f });
            var a = new Float2(5000, 500); var b = new Float2(9000, 500);   // straight across the 1 km soft band
            float eff = p.PathLength(a, b, S);
            Assert.Greater(eff, 4000f + 1300f, "1 km of soft ground at factor 2.5 adds 1.5 km");
            Assert.Less(eff, 4000f + 1700f);
        }

        [Test]
        public void GoalInsideDeepWater_RoutesToTheNearestShore()
        {
            var p = new GridRoutePlanner(new Field { Soft = (x, y) => 0f });
            var a = new Float2(2000, 5000); var b = new Float2(5000, 5000);   // goal 1 km inside the lake
            float len = p.PathLength(a, b, S);
            Assert.Less(len, 2200f);
            Assert.AreNotEqual(float.PositiveInfinity, len);
        }

        [Test]
        public void SameQuery_IsCachedAndDeterministic()
        {
            var p = new GridRoutePlanner(new Field { Soft = (x, y) => 0f });
            var a = new Float2(2000, 5000); var b = new Float2(8500, 5000);
            Float2 w1 = p.NextWaypoint(a, b, S); int s1 = p.Searches;
            Float2 w2 = p.NextWaypoint(new Float2(2010, 5010), b, S);
            Assert.AreEqual(s1, p.Searches, "same start cell and goal cell: no second search");
            Assert.AreEqual(w1, w2);
            Assert.AreEqual(w1, new GridRoutePlanner(new Field { Soft = (x, y) => 0f }).NextWaypoint(a, b, S));
        }

        [Test]
        public void OutsideTheGrid_FallsBackToTheStraightLine()
        {
            var p = new GridRoutePlanner(new Field());
            var a = new Float2(-500, 500); var b = new Float2(3000, 500);
            Assert.AreEqual(b, p.NextWaypoint(a, b, S));
            Assert.AreEqual(3500f, p.PathLength(a, b, S), 1f);
        }

        [Test]
        public void BayConfigEnablesRoutePlanning_DefaultStaysTheT113Behaviour()
        {
            Assert.IsTrue(PackConfig.Bay().RoutePlanning);
            Assert.IsFalse(PackConfig.Default().RoutePlanning);
        }
    }
}
