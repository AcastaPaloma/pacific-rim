// T-023 R-020 §3: static landmarks layer (Golden Gate Bridge tower legs, deck, cables). Engine-free; real-tile tests skip when tiles are absent.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Game.World.Geo.Tests
{
    public class GeoLandmarksTests
    {
        static readonly IGeoLayer[] Only = { new LandmarksLayer() };

        static GeoTileModel Load(int i, int j)
        {
            var dir = Path.Combine(GeoTestData.ProjectRoot(), "Assets", "StreamingAssets", "Geo", "tiles", "v1", "L0", i.ToString("00") + "_" + j.ToString("00"));
            if (!File.Exists(Path.Combine(dir, "landmarks.geojson"))) Assert.Ignore("tile " + dir + " has no landmarks (bake with Pipelines/geo, T-023)");
            return new GeoTileModel(new GeoTileContext(dir, GeoTestData.Settings(), verifyHashes: false), Only);
        }

        static List<GeoLandmarkData> Lm(GeoTileModel m) => m.Layer("landmarks").Landmarks;

        [Test]
        public void DefaultLayers_OrderIsTerrainWaterBuildingsLandmarksRoadsFaults()
        {
            Assert.AreEqual(new[] { "terrain", "water", "buildings", "landmarks", "roads", "faults" }, GeoTileModel.DefaultLayers().Select(l => l.Name).ToArray());
        }

        [Test]
        public void ManifestWithoutLayer_StaysValid_NoLandmarks()
        {
            var dir = GeoTestData.Doctored(man =>
            {
                var layers = (List<object>)man["layers"];
                layers.RemoveAll(l => ((Dictionary<string, object>)l)["layer"] as string == "landmarks");
            });
            var ctx = new GeoTileContext(dir, GeoTestData.Settings(), verifyHashes: false);
            Assert.IsNull(ctx.ManifestLayer("landmarks"));
            GeoTileModel m = null;
            Assert.DoesNotThrow(() => m = new GeoTileModel(ctx, Only));
            Assert.AreEqual(0, Lm(m).Count);
            Assert.AreEqual(0, m.Attributions.Count);
        }

        [Test]
        public void SoMaTile_LayerPresentWithZeroFeatures()
        {
            var m = new GeoTileModel(new GeoTileContext(GeoTestData.M0Dir, GeoTestData.Settings(), verifyHashes: false), Only);
            Assert.AreEqual(0, Lm(m).Count);
            Assert.AreEqual(0, m.Layer("landmarks").Stats["landmarks.count"]);
        }

        [Test]
        public void GoldenGate_ColliderCountsAndShapes()
        {
            var a = Lm(Load(26, 37)); var b = Lm(Load(26, 38));
            Assert.AreEqual(5, a.Count); Assert.AreEqual(5, b.Count);
            var towers = a.Concat(b).Where(l => l.Kind == "bridge_tower").ToList();
            Assert.AreEqual(4, towers.Sum(t => t.Colliders.Count(c => c.Shape == GeoColliderShape.Box)));
            foreach (var t in towers)
            {
                var c = t.Colliders.Single();
                Assert.AreEqual(t.BaseNavd88M, c.P0Y, 1e-9); Assert.AreEqual(t.TopNavd88M, c.P1Y, 1e-9);
                Assert.Greater(c.Width, 5); Assert.Greater(c.Depth, 5);
                Assert.IsNotNull(t.Mesh); Assert.Greater(t.Mesh.TriangleCount, 0);
            }
            foreach (var d in a.Concat(b).Where(l => l.Kind == "bridge_deck"))
            {
                Assert.Greater(d.Colliders.Count, 1);
                foreach (var c in d.Colliders)
                {
                    Assert.AreEqual(GeoColliderShape.Box, c.Shape);
                    Assert.AreEqual(18.9, c.Width, 1e-9);
                    Assert.AreEqual(LandmarksLayer.DeckDepthM, c.Depth, 1e-9);
                    Assert.LessOrEqual(Horiz(c), 40.0 + 1e-9);
                }
                Assert.Greater(d.Mesh.TriangleCount, 0);
            }
            foreach (var cab in a.Concat(b).Where(l => l.Kind == "bridge_cable"))
                foreach (var c in cab.Colliders)
                {
                    Assert.AreEqual(GeoColliderShape.Capsule, c.Shape);
                    Assert.AreEqual(0.46, c.Radius, 0.005);
                    Assert.LessOrEqual(Horiz(c), 40.0 + 1e-9);
                }
            Assert.AreEqual(Lm(Load(26, 37)).Sum(l => l.Colliders.Count), Load(26, 37).Stats["landmarks.colliders"]);
        }

        static double Horiz(GeoColliderData c) => Math.Sqrt((c.P1X - c.P0X) * (c.P1X - c.P0X) + (c.P1Z - c.P0Z) * (c.P1Z - c.P0Z));

        [Test]
        public void Deck_EndpointsMatchOriginalVertices_BoxTopFollowsRoadSurface()
        {
            foreach (var (i, j) in new[] { (26, 37), (26, 38) })
            {
                var d = Lm(Load(i, j)).Single(l => l.Kind == "bridge_deck");
                var first = d.Colliders.First(); var last = d.Colliders.Last();
                Assert.AreEqual(d.Y[0], first.P0Y + first.Depth / 2, 1e-6);
                Assert.AreEqual(d.Y[d.Y.Length - 1], last.P1Y + last.Depth / 2, 1e-6);
                Assert.AreEqual(d.Line[0], first.P0X); Assert.AreEqual(d.Line[1], first.P0Z);
                Assert.AreEqual(d.Line[d.Line.Length - 2], last.P1X); Assert.AreEqual(d.Line[d.Line.Length - 1], last.P1Z);
                for (int k = 1; k < d.Colliders.Count; k++)   // chain: each segment starts where the previous ends
                {
                    Assert.AreEqual(d.Colliders[k - 1].P1X, d.Colliders[k].P0X); Assert.AreEqual(d.Colliders[k - 1].P1Y, d.Colliders[k].P0Y); Assert.AreEqual(d.Colliders[k - 1].P1Z, d.Colliders[k].P0Z);
                }
            }
        }

        [Test]
        public void Continuity_AtTileCut_DeckAndBothCables()
        {
            var m37 = Load(26, 37); var m38 = Load(26, 38);
            double maxErr = 0;
            foreach (var slug in new[] { "ggb_deck", "ggb_cable_a", "ggb_cable_b" })
            {
                var a = Lm(m37).Single(l => l.Lid == "lm:" + slug + ":26_37").Colliders.Last();
                var b = Lm(m38).Single(l => l.Lid == "lm:" + slug + ":26_38").Colliders.First();
                double ex = Math.Abs(a.P1X + m37.Context.OriginX - (b.P0X + m38.Context.OriginX));
                double ey = Math.Abs(a.P1Y - b.P0Y);
                double ez = Math.Abs(a.P1Z + m37.Context.OriginZ - (b.P0Z + m38.Context.OriginZ));
                Assert.Less(ex, 1e-6, slug); Assert.Less(ey, 1e-6, slug); Assert.Less(ez, 1e-6, slug);
                Assert.AreEqual(77824.0, a.P1Z + m37.Context.OriginZ, 1e-6);
                maxErr = Math.Max(maxErr, Math.Max(ex, Math.Max(ey, ez)));
            }
            TestContext.WriteLine("max continuity error at z=77824: " + maxErr);
        }

        [Test]
        public void Landmarks_NeverEnterBuildingIndex_SortedByLid()
        {
            var m = Load(26, 37);
            Assert.AreEqual(0, m.Buildings.Count);
            Assert.AreEqual(0, m.ById.Count);
            Assert.AreEqual(0, m.Layer("landmarks").Buildings.Count);
            var lids = Lm(m).Select(l => l.Lid).ToList();
            var sorted = lids.OrderBy(s => s, StringComparer.Ordinal).ToList();
            Assert.AreEqual(sorted, lids);
            Assert.IsTrue(m.Attributions.Count > 0);
        }
    }
}
