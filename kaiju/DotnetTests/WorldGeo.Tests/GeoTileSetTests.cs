// T-021 step 1: tile set, neighbours, shared edges, version refusal, seam report, building ownership.
// Real-tile tests skip with a message when a tile folder is absent. Synthetic cases always run.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Game.World.Geo.Tests
{
    static class TileSetData
    {
        public static string TilesRoot => Path.Combine(GeoTestData.ProjectRoot(), "Assets", "StreamingAssets", "Geo", "tiles", "v1");

        public static List<GeoTileId> M1Tiles()
        {
            var l = GeoTileSet.Range(0, 25, 27, 36, 38);      // Golden Gate 3 x 3
            l.AddRange(GeoTileSet.Range(0, 29, 31, 35, 36));  // SoMa 3 x 2
            return l;
        }

        public static void RequireTiles(IEnumerable<GeoTileId> ids)
        {
            var absent = GeoTileSetLoader.Absent(TilesRoot, ids);
            if (absent.Count > 0) Assert.Ignore("tile folder(s) absent under " + TilesRoot + ": " + string.Join(", ", absent) + " (bake them with Pipelines/geo, T-020)");
        }

        static GeoTileSet _set;
        public static GeoTileSet M1Set() { RequireTiles(M1Tiles()); return _set ?? (_set = GeoTileSetLoader.Load(TilesRoot, M1Tiles())); }

        static List<GeoSeamReport> _seams;
        public static List<GeoSeamReport> Seams() => _seams ?? (_seams = GeoSeams.Report(M1Set()));

        /// <summary>Copy of one tile's manifest in a temp tiles root (only manifest.json, no hashes verified).</summary>
        public static string DoctoredRoot(IEnumerable<GeoTileId> ids, Action<GeoTileId, Dictionary<string, object>> edit)
        {
            var root = Path.Combine(Path.GetTempPath(), "geo_set_" + Guid.NewGuid().ToString("N"));
            foreach (var id in ids)
            {
                var dir = GeoTileSetLoader.TileDirectory(root, id);
                Directory.CreateDirectory(dir);
                var man = (Dictionary<string, object>)GeoJson.Parse(File.ReadAllText(Path.Combine(GeoTestData.M0Dir, "manifest.json")));
                man["tile"] = id.ToString(); man["i"] = (double)id.I; man["j"] = (double)id.J;
                man["origin_baytm_m"] = new List<object> { id.OriginX, id.OriginZ };
                edit?.Invoke(id, man);
                File.WriteAllText(Path.Combine(dir, "manifest.json"), GeoJson.Write(man));
            }
            return root;
        }
    }

    public class GeoTileSetSyntheticTests
    {
        static GeoTileId T(int i, int j) => GeoTileId.From(0, i, j);

        [Test]
        public void M1Set_Has15Tiles_And19SharedEdges()
        {
            var set = new GeoTileSet(TileSetData.M1Tiles());
            Assert.AreEqual(15, set.Tiles.Count);
            Assert.IsTrue(set.Contains(T(30, 36)));
            Assert.AreEqual(19, set.SharedEdges.Count);   // R-020 §4: GG 12 + SoMa 7
            Assert.AreEqual(12, set.SharedEdges.Count(e => e.A.I <= 27 && e.B.I <= 27));
            Assert.AreEqual(7, set.SharedEdges.Count(e => e.A.I >= 29));
            Assert.AreEqual(set.SharedEdges.Count, set.SharedEdges.Distinct().Count());
        }

        [Test]
        public void Neighbours_AreFoundInsideAndAbsentAtGaps()
        {
            var set = new GeoTileSet(TileSetData.M1Tiles());
            Assert.IsTrue(set.TryGetNeighbour(T(26, 37), GeoDirection.North, out var n)); Assert.AreEqual(T(26, 38), n);
            Assert.IsTrue(set.TryGetNeighbour(T(26, 37), GeoDirection.East, out n)); Assert.AreEqual(T(27, 37), n);
            Assert.IsTrue(set.TryGetNeighbour(T(26, 37), GeoDirection.South, out n)); Assert.AreEqual(T(26, 36), n);
            Assert.IsTrue(set.TryGetNeighbour(T(26, 37), GeoDirection.West, out n)); Assert.AreEqual(T(25, 37), n);
            Assert.IsFalse(set.TryGetNeighbour(T(27, 36), GeoDirection.East, out _), "column i28 is between GG and SoMa");
            Assert.IsFalse(set.TryGetNeighbour(T(29, 35), GeoDirection.West, out _));
            Assert.IsFalse(set.TryGetNeighbour(T(30, 36), GeoDirection.North, out _));
            Assert.IsTrue(set.TryGetNeighbour(T(30, 36), GeoDirection.South, out n)); Assert.AreEqual(T(30, 35), n);
        }

        [Test]
        public void Offsets_AreExactMultiplesOf2048_AndGgToSomaExceeds4096()
        {
            GeoTileSet.OffsetFrom(T(26, 37), T(30, 36), out var dx, out var dz);
            Assert.AreEqual(4 * 2048.0, dx); Assert.AreEqual(-2048.0, dz);
            GeoTileSet.OriginOf(T(30, 36), out var x, out var z);
            Assert.AreEqual(61440.0, x); Assert.AreEqual(73728.0, z);
            Assert.Greater(Math.Sqrt(dx * dx + dz * dz), 4096.0);
        }

        [Test]
        public void SharedEdge_GeometryIsOnTheGrid()
        {
            var set = new GeoTileSet(new[] { T(1, 1), T(2, 1), T(1, 2) });
            Assert.AreEqual(2, set.SharedEdges.Count);
            var ew = set.SharedEdges.Single(e => e.FromA == GeoDirection.East);
            Assert.AreEqual(new[] { 4096.0, 2048.0, 4096.0, 4096.0 }, new[] { ew.X0, ew.Z0, ew.X1, ew.Z1 });
            var ns = set.SharedEdges.Single(e => e.FromA == GeoDirection.North);
            Assert.AreEqual(new[] { 2048.0, 4096.0, 4096.0, 4096.0 }, new[] { ns.X0, ns.Z0, ns.X1, ns.Z1 });
            Assert.AreEqual(2048.0, ew.LengthM);
        }

        [Test]
        public void MixedLevels_AreRefused() =>
            Assert.Throws<GeoTileSetRejectedException>(() => new GeoTileSet(new[] { T(1, 1), GeoTileId.From(1, 1, 1) }));

        [Test]
        public void ParseKeys_AcceptsKeysAndRefusesJunk()
        {
            Assert.AreEqual(T(30, 36), GeoTileSet.ParseKeys(new[] { "L0_30_36" })[0]);
            Assert.Throws<GeoTileSetRejectedException>(() => GeoTileSet.ParseKeys(new[] { "30_36" }));
        }

        static GeoEdgeProfiles Flat(double h, int n = 5) =>
            new GeoEdgeProfiles { North = Enumerable.Repeat(h, n).ToArray(), East = Enumerable.Repeat(h, n).ToArray(), South = Enumerable.Repeat(h, n).ToArray(), West = Enumerable.Repeat(h, n).ToArray() };

        [Test]
        public void SeamMeasure_ReportsMeanP99AndMax()
        {
            var set = new GeoTileSet(new[] { T(1, 1), T(2, 1) });
            var edge = set.SharedEdges.Single();
            var a = Flat(10, 101); var b = Flat(10, 101);
            b.West[7] = 10.4; b.West[50] = 10.1; b.West[99] = 9.8;   // 3 of 101 samples differ
            var r = GeoSeams.Measure(edge, a, b);
            Assert.AreEqual(101, r.Samples);
            Assert.AreEqual(0.4, r.MaxAbsM, 1e-9);
            Assert.AreEqual(0.2, r.P99AbsM, 1e-9);       // nearest rank 100 of 101
            Assert.AreEqual(0.7 / 101, r.MeanAbsM, 1e-9);
            Assert.IsTrue(r.MaxWithinLimit); Assert.IsTrue(r.P99WithinLimit);
        }

        [Test]
        public void SeamMeasure_UsesTheMatchingSides_ForNorthSouth()
        {
            var set = new GeoTileSet(new[] { T(1, 1), T(1, 2) });
            var a = Flat(5); var b = Flat(5);
            a.North[2] = 7; b.South[2] = 6.5;   // matching pair
            a.South[2] = 99; b.North[2] = -99;  // must be ignored
            var r = GeoSeams.Measure(set.SharedEdges.Single(), a, b);
            Assert.AreEqual(0.5, r.MaxAbsM, 1e-12);
        }

        [Test]
        public void EdgeProfiles_FollowTheR16Layout()
        {
            int n = 4; var d = new float[n * n];
            for (int r = 0; r < n; r++) for (int c = 0; c < n; c++) d[r * n + c] = r * 10 + c;   // row 0 = south
            var p = GeoEdgeProfiles.From(new GeoHeightField(d, n, 1.0));
            Assert.AreEqual(new double[] { 0, 1, 2, 3 }, p.South);
            Assert.AreEqual(new double[] { 30, 31, 32, 33 }, p.North);
            Assert.AreEqual(new double[] { 0, 10, 20, 30 }, p.West);
            Assert.AreEqual(new double[] { 3, 13, 23, 33 }, p.East);
        }

        [Test]
        public void Ownership_DuplicateBidAcrossTilesIsRefused()
        {
            var a = new KeyValuePair<GeoTileId, IEnumerable<string>>(T(1, 1), new[] { "osm:w1", "osm:w2" });
            var b = new KeyValuePair<GeoTileId, IEnumerable<string>>(T(2, 1), new[] { "osm:w3" });
            Assert.AreEqual(3, GeoSeams.AssertSingleOwner(new[] { a, b }));
            var c = new KeyValuePair<GeoTileId, IEnumerable<string>>(T(2, 1), new[] { "osm:w3", "osm:w2" });
            var ex = Assert.Throws<GeoTileSetRejectedException>(() => GeoSeams.AssertSingleOwner(new[] { a, c }));
            StringAssert.Contains("osm:w2", ex.Message);
            StringAssert.Contains("L0_01_01", ex.Message);
            StringAssert.Contains("L0_02_01", ex.Message);
        }

        [Test]
        public void Ownership_SameBidTwiceInOneTileIsNotADuplicate()
        {
            var a = new KeyValuePair<GeoTileId, IEnumerable<string>>(T(1, 1), new[] { "osm:w1", "osm:w1" });
            Assert.AreEqual(0, GeoSeams.DuplicateOwners(new[] { a }).Count);
        }

        // ---- loader refusals on doctored copies of the real L0_30_36 manifest ----

        static List<GeoTileId> Pair() => new List<GeoTileId> { T(30, 36), T(31, 36) };

        [Test]
        public void Loader_AcceptsConsistentManifests_AndExposesOffsets()
        {
            if (!File.Exists(Path.Combine(GeoTestData.M0Dir, "manifest.json"))) Assert.Ignore("L0_30_36 absent");
            var root = TileSetData.DoctoredRoot(Pair(), null);
            var set = GeoTileSetLoader.Load(root, Pair());
            Assert.AreEqual(2, set.Tiles.Count); Assert.AreEqual(1, set.SharedEdges.Count);
            Assert.IsNotNull(set.Manifest(T(31, 36)));
        }

        static void Refused(Action<GeoTileId, Dictionary<string, object>> edit, string expect)
        {
            if (!File.Exists(Path.Combine(GeoTestData.M0Dir, "manifest.json"))) Assert.Ignore("L0_30_36 absent");
            var root = TileSetData.DoctoredRoot(Pair(), edit);
            var ex = Assert.Throws<GeoTileSetRejectedException>(() => GeoTileSetLoader.Load(root, Pair()));
            StringAssert.Contains(expect, ex.Message);
        }

        [Test] public void Loader_RefusesMixedSpecVersions() =>
            Refused((id, m) => { if (id.I == 31) m["spec"] = "geo_tile@v2"; }, "mixed spec");
        [Test] public void Loader_RefusesMixedProjection() =>
            Refused((id, m) => { if (id.I == 31) m["crs"] = "kaiju:BayTM@v2 (WKT2 in crs/baytm_v2.wkt)"; }, "mixed crs");
        [Test] public void Loader_RefusesForeignProjection() =>
            Refused((id, m) => m["crs"] = "EPSG:3857", "projection 'EPSG:3857'");
        [Test] public void Loader_RefusesWrongTileSize() =>
            Refused((id, m) => { if (id.I == 31) m["tile_size_m"] = 1024.0; }, "tile_size_m 1024");
        [Test] public void Loader_RefusesMixedVertical() =>
            Refused((id, m) => { if (id.I == 31) m["vertical"] = "NGVD29"; }, "mixed vertical");
        [Test] public void Loader_RefusesMixedWaterLevel() =>
            Refused((id, m) => { if (id.I == 31) m["water_level_navd88_m"] = 1.2; }, "mixed water_level");
        [Test] public void Loader_RefusesIndexMismatch() =>
            Refused((id, m) => { if (id.I == 31) m["i"] = 30.0; }, "do not match the tile key");

        [Test]
        public void Loader_RefusesUnsupportedLayerApi()
        {
            if (!File.Exists(Path.Combine(GeoTestData.M0Dir, "manifest.json"))) Assert.Ignore("L0_30_36 absent");
            var root = TileSetData.DoctoredRoot(Pair(), null);
            var ex = Assert.Throws<GeoTileSetRejectedException>(() => GeoTileSetLoader.Load(root, Pair(), layerApi: 2));
            StringAssert.Contains("LayerApi 2", ex.Message);
            Assert.AreEqual(1, GeoContractVersions.LayerApi, "LayerApi stays 1");
        }

        [Test]
        public void Loader_ListsEveryProblem_IncludingAbsentFolders()
        {
            var ex = Assert.Throws<GeoTileSetRejectedException>(() => GeoTileSetLoader.Load(Path.Combine(Path.GetTempPath(), "geo_none_" + Guid.NewGuid().ToString("N")), Pair()));
            Assert.AreEqual(2, ex.Problems.Count(p => p.Contains("tile folder absent")));
        }
    }

    public class GeoTileSetRealTilesTests
    {
        [Test]
        public void Real15Tiles_LoadValidatedWithHashes()
        {
            TileSetData.RequireTiles(TileSetData.M1Tiles());
            var set = GeoTileSetLoader.Load(TileSetData.TilesRoot, TileSetData.M1Tiles(), verifyHashes: true);
            Assert.AreEqual(15, set.Tiles.Count);
            Assert.AreEqual(19, set.SharedEdges.Count);
            Assert.AreEqual(0, GeoTileSetLoader.Absent(TileSetData.TilesRoot, set.Tiles).Count);
            foreach (var t in set.Tiles) Assert.AreEqual(2048.0, GeoJson.Num(set.Manifest(t), "tile_size_m"), t.ToString());
        }

        [Test]
        public void Real15Tiles_SeamReport_Prints19Edges_AndEdgeVerticesAgree()
        {
            var reports = TileSetData.Seams();
            Assert.AreEqual(19, reports.Count);
            foreach (var r in reports)
            {
                Assert.AreEqual(2049, r.Samples, r.Edge.ToString());
                Assert.IsFalse(double.IsNaN(r.MaxAbsM), r.Edge.ToString());
                TestContext.Progress.WriteLine("SEAM " + r);
            }
            TestContext.Progress.WriteLine("SEAM worst p99 = " + reports.Max(r => r.P99AbsM).ToString("0.0000") + " m, worst max = " + reports.Max(r => r.MaxAbsM).ToString("0.0000") + " m");
            // reporting only: no limit asserted here (R-020 limits 0.20 / 0.50 m are gated by Pipelines/geo, T-035)
        }

        [Test]
        public void Real15Tiles_BuildingIdsAreOwnedByAtMostOneTile()
        {
            var set = TileSetData.M1Set();
            int distinct = GeoSeams.AssertSingleOwner(set);
            Assert.Greater(distinct, 0);
            TestContext.Progress.WriteLine("OWNERSHIP distinct bids across 15 tiles = " + distinct);
        }
    }
}
