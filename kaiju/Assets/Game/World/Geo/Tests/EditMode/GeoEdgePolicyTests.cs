// T-021 step 3 (engine-free): shared edge heights and forced edge vertices between neighbouring tiles. Synthetic data only.
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Game.World.Geo.Tests
{
    public class GeoEdgePolicyTests
    {
        const int N = 65;

        static float[] Surface(int seed)
        {
            var h = new float[N * N];
            for (int r = 0; r < N; r++)
                for (int c = 0; c < N; c++)
                    h[r * N + c] = (float)(8 * Math.Sin(c * 0.23 + seed) * Math.Cos(r * 0.17 - seed) + 0.01 * ((r * 7919 + c * 104729 + seed * 31) % 101));
            return h;
        }

        static Dictionary<GeoTileId, float[]> Data(params GeoTileId[] ids) => ids.ToDictionary(t => t, t => Surface(t.I * 7 + t.J * 13));

        static GeoEdgeProfiles Prof(Dictionary<GeoTileId, float[]> d, GeoTileId t) => GeoEdgeProfiles.From(new GeoHeightField(d[t], N, 1.0));

        static float[] Policed(GeoTileSet set, Dictionary<GeoTileId, float[]> d, GeoTileId t, out GeoEdgePolicy p)
        {
            p = GeoEdgePolicy.For(set, t, x => Prof(d, x));
            var h = (float[])d[t].Clone();
            p?.Apply(h, N);
            return h;
        }

        // vertex heights of one border of the meshed tile: node index along the border -> y (all chunks)
        static Dictionary<int, float> BorderVerts(RtinResult r, GeoDirection side)
        {
            var res = new Dictionary<int, float>();
            foreach (var m in r.Chunks)
                for (int v = 0; v < m.Positions.Length / 3; v++)
                {
                    int x = (int)Math.Round(m.Positions[3 * v]), z = (int)Math.Round(m.Positions[3 * v + 2]);
                    bool on = side == GeoDirection.North ? z == N - 1 : side == GeoDirection.South ? z == 0 : side == GeoDirection.East ? x == N - 1 : x == 0;
                    if (!on) continue;
                    res[side == GeoDirection.North || side == GeoDirection.South ? x : z] = m.Positions[3 * v + 1];
                }
            return res;
        }

        [Test]
        public void NoNeighbour_PolicyIsNull()
        {
            var a = GeoTileId.From(0, 3, 3);
            var set = new GeoTileSet(new[] { a });
            Assert.IsNull(GeoEdgePolicy.For(set, a, x => Prof(Data(a), x)));
        }

        [Test]
        public void NoForcedEdges_OutputIsIdenticalToToday()
        {
            var h = Surface(5);
            var r0 = RtinMesher.Build(h, N, 1.0, 0.25f, 32);
            var r1 = RtinMesher.Build(h, N, 1.0, 0.25f, 32, "terrain", new bool[4]);
            Assert.AreEqual(r0.Chunks.Count, r1.Chunks.Count);
            for (int c = 0; c < r0.Chunks.Count; c++)
            {
                CollectionAssert.AreEqual(r0.Chunks[c].Positions, r1.Chunks[c].Positions);
                CollectionAssert.AreEqual(r0.Chunks[c].Indices, r1.Chunks[c].Indices);
            }
        }

        [Test]
        public void TwoTiles_SharedEdge_SameMeanHeights_AndSameVertices()
        {
            var a = GeoTileId.From(0, 3, 3); var b = GeoTileId.From(0, 4, 3);   // b is east of a
            var set = new GeoTileSet(new[] { a, b });
            var d = Data(a, b);
            var ha = Policed(set, d, a, out var pa); var hb = Policed(set, d, b, out var pb);
            Assert.IsTrue(pa.Forced[(int)GeoDirection.East]); Assert.IsTrue(pb.Forced[(int)GeoDirection.West]);
            Assert.IsFalse(pa.Forced[(int)GeoDirection.North] || pa.Forced[(int)GeoDirection.West] || pa.Forced[(int)GeoDirection.South]);
            for (int r = 0; r < N; r++)
            {
                float expect = (float)(0.5 * ((double)d[a][r * N + N - 1] + d[b][r * N]));
                Assert.AreEqual(expect, ha[r * N + N - 1], 1e-6f, "row " + r);
                Assert.AreEqual(ha[r * N + N - 1], hb[r * N], 0f, "A east == B west exactly, row " + r);
            }
            // untouched interior and untouched borders
            Assert.AreEqual(d[a][10 * N + 10], ha[10 * N + 10]);
            Assert.AreEqual(d[a][10 * N], ha[10 * N]);

            var ra = RtinMesher.Build(ha, N, 1.0, 0.25f, 32, "terrain", pa.Forced);
            var rb = RtinMesher.Build(hb, N, 1.0, 0.25f, 32, "terrain", pb.Forced);
            var ea = BorderVerts(ra, GeoDirection.East); var eb = BorderVerts(rb, GeoDirection.West);
            Assert.AreEqual(N, ea.Count, "every border node is a vertex (A)");
            Assert.AreEqual(N, eb.Count, "every border node is a vertex (B)");
            foreach (var kv in ea) Assert.AreEqual(kv.Value, eb[kv.Key], 0f, "node " + kv.Key);
            // without forcing the unforced mesh of a rough surface drops border nodes
            var unforced = BorderVerts(RtinMesher.Build(ha, N, 1.0, 0.25f, 32), GeoDirection.East);
            Assert.LessOrEqual(unforced.Count, N);
        }

        [Test]
        public void FourTiles_SharedCorner_IsOneValue()
        {
            var sw = GeoTileId.From(0, 3, 3); var se = GeoTileId.From(0, 4, 3); var nw = GeoTileId.From(0, 3, 4); var ne = GeoTileId.From(0, 4, 4);
            var set = new GeoTileSet(new[] { sw, se, nw, ne });
            var d = Data(sw, se, nw, ne);
            var hsw = Policed(set, d, sw, out _); var hse = Policed(set, d, se, out _); var hnw = Policed(set, d, nw, out _); var hne = Policed(set, d, ne, out _);
            float c = hsw[(N - 1) * N + N - 1];                       // sw's NE corner
            Assert.AreEqual(c, hse[(N - 1) * N], 0f);                 // se's NW
            Assert.AreEqual(c, hnw[N - 1], 0f);                       // nw's SE
            Assert.AreEqual(c, hne[0], 0f);                           // ne's SW
            double mean = (d[sw][(N - 1) * N + N - 1] + (double)d[se][(N - 1) * N] + d[nw][N - 1] + d[ne][0]) / 4;
            Assert.AreEqual(mean, c, 1e-6);
        }

        [Test]
        public void DiagonalOnly_CornerShared_NoForcedEdge()
        {
            var a = GeoTileId.From(0, 3, 3); var b = GeoTileId.From(0, 4, 4);
            var set = new GeoTileSet(new[] { a, b });
            var d = Data(a, b);
            var ha = Policed(set, d, a, out var pa); var hb = Policed(set, d, b, out var pb);
            Assert.IsFalse(pa.Forced.Any(x => x)); Assert.IsFalse(pb.Forced.Any(x => x));
            Assert.AreEqual(ha[(N - 1) * N + N - 1], hb[0], 0f);
        }
    }
}
