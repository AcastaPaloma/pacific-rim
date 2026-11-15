// T-021 step 3: render mesh and collider use one edge-height rule (shared edges and corners).
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Game.World.Geo.Tests
{
    public class GeoEdgeHeightTests
    {
        const int N = 33;

        static float[] Field(double baseM, double slope, int seed)
        {
            var d = new float[N * N];
            var rnd = new Random(seed);
            for (int r = 0; r < N; r++)
                for (int c = 0; c < N; c++) d[r * N + c] = (float)(baseM + slope * c + 0.0173 * r + rnd.NextDouble() * 0.9);
            return d;
        }

        [Test]
        public void RenderEdge_And_ColliderEdge_AreIdentical_Corners_Included()
        {
            // B (j=35) is south of A (j=36); A's interior is ~100 m higher so the two colliders get different lattice origins.
            var a = Field(118.0, 0.31, 1);
            var b = Field(18.0, 0.27, 2);
            var ida = GeoTileId.From(0, 30, 36); var idb = GeoTileId.From(0, 30, 35);
            var set = new GeoTileSet(new[] { ida, idb });
            var data = new Dictionary<GeoTileId, float[]> { { ida, a }, { idb, b } };
            GeoEdgeProfiles Prof(GeoTileId t)
            {
                var d = data[t]; var p = new GeoEdgeProfiles { North = new double[N], East = new double[N], South = new double[N], West = new double[N] };
                for (int k = 0; k < N; k++) { p.South[k] = d[k]; p.North[k] = d[(N - 1) * N + k]; p.West[k] = d[k * N]; p.East[k] = d[k * N + N - 1]; }
                return p;
            }
            // profiles are the unmodified data of both tiles
            var pa = GeoEdgePolicy.For(set, ida, Prof); var pb = GeoEdgePolicy.For(set, idb, Prof);
            var da = (float[])a.Clone(); var db = (float[])b.Clone();
            pa.Apply(da, N); pb.Apply(db, N);

            GeoHeightLattice.Plan(da, out var stepA, out var kA);
            GeoHeightLattice.Plan(db, out var stepB, out var kB);
            Assert.AreEqual(GeoHeightLattice.StepM, stepA); Assert.AreEqual(GeoHeightLattice.StepM, stepB);
            Assert.AreNotEqual(kA, kB, "fixture must give the two colliders different lattice origins");

            for (int x = 0; x < N; x++)
            {
                float renderA = da[(N - 1 - (N - 1)) * N + x];   // A's south row (z = 0)
                float renderB = db[(N - 1) * N + x];              // B's north row
                double colA = GeoHeightLattice.ColliderHeight(GeoHeightLattice.Level(renderA, stepA, kA), stepA, kA);
                double colB = GeoHeightLattice.ColliderHeight(GeoHeightLattice.Level(renderB, stepB, kB), stepB, kB);
                Assert.AreEqual(renderA, renderB, "render edge A vs B, x=" + x);
                Assert.AreEqual(renderA, colA, 1e-5, "render vs collider A, x=" + x + (x == 0 || x == N - 1 ? " (corner)" : ""));
                Assert.AreEqual(renderB, colB, 1e-5, "render vs collider B, x=" + x + (x == 0 || x == N - 1 ? " (corner)" : ""));
                Assert.AreEqual(colA, colB, 1e-5, "collider A vs B, x=" + x);
            }
        }
    }
}
