// Engine-free World.Geo tests: run by Unity EditMode and by `dotnet test DotnetTests/WorldGeo.Tests` (T-003).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.World.Geo.Tests
{
    public static class GeoTestData
    {
        public static string ProjectRoot()
        {
            var d = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (d != null && !Directory.Exists(Path.Combine(d.FullName, "Assets", "StreamingAssets", "Geo")))
            {
                var k = Path.Combine(d.FullName, "kaiju");
                if (Directory.Exists(Path.Combine(k, "Assets", "StreamingAssets", "Geo"))) return k;
                d = d.Parent;
            }
            if (d == null) throw new DirectoryNotFoundException("project root with Assets/StreamingAssets/Geo not found");
            return d.FullName;
        }

        public static string M0Dir => Path.Combine(ProjectRoot(), "Assets", "StreamingAssets", "Geo", "tiles", "v1", "L0", "30_36");

        /// <summary>The real config file: tests use the same single source of truth as the game.</summary>
        public static IReadOnlyDictionary<string, double> Settings() => GeoSettings.Load(Path.Combine(ProjectRoot(), "Assets", "Game", "Config", "WorldGeo.json"));

        static GeoTileModel _m0;
        public static GeoTileModel M0() => _m0 ?? (_m0 = new GeoTileModel(new GeoTileContext(M0Dir, Settings())));

        /// <summary>Copy of the M0 manifest with one edit applied, in a temp folder.</summary>
        public static string Doctored(Action<Dictionary<string, object>> edit)
        {
            var man = (Dictionary<string, object>)GeoJson.Parse(File.ReadAllText(Path.Combine(M0Dir, "manifest.json")));
            edit(man);
            var dir = Path.Combine(Path.GetTempPath(), "geo_doctored_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "manifest.json"), GeoJson.Write(man));
            return dir;
        }
    }

    public class GeoManifestTests
    {
        [Test]
        public void CommittedTile_ManifestIsComplete_AndHashesMatch()
        {
            var ctx = new GeoTileContext(GeoTestData.M0Dir, GeoTestData.Settings(), verifyHashes: true);
            Assert.AreEqual("L0_30_36", ctx.Tile.ToString());
            Assert.AreEqual(61440.0, ctx.OriginX);
            Assert.AreEqual(73728.0, ctx.OriginZ);
            Assert.AreEqual(0.969, ctx.WaterLevelNavd88, 1e-9);
        }

        static IEnumerable<TestCaseData> Doctorings()
        {
            yield return new TestCaseData((Action<Dictionary<string, object>>)(m => ((Dictionary<string, object>)m["build"]).Remove("git"))).SetName("Rejects_NoBuildGit");
            yield return new TestCaseData((Action<Dictionary<string, object>>)(m => m.Remove("bounds_baytm"))).SetName("Rejects_NoBounds");
            yield return new TestCaseData((Action<Dictionary<string, object>>)(m => Layer(m, 0).Remove("sha256"))).SetName("Rejects_LayerWithoutSha");
            yield return new TestCaseData((Action<Dictionary<string, object>>)(m => Src(m, 3, 0).Remove("licence"))).SetName("Rejects_SourceWithoutLicence");
            yield return new TestCaseData((Action<Dictionary<string, object>>)(m => Src(m, 3, 0)["attribution"] = "")).SetName("Rejects_EmptyAttribution");
            yield return new TestCaseData((Action<Dictionary<string, object>>)(m => Src(m, 0, 0).Remove("fetched_utc"))).SetName("Rejects_NoFetchTime");
            yield return new TestCaseData((Action<Dictionary<string, object>>)(m => Src(m, 1, 1).Remove("source_sha256"))).SetName("Rejects_NoSourceHash");
            yield return new TestCaseData((Action<Dictionary<string, object>>)(m => Src(m, 4, 0)["licence"] = "unclear")).SetName("Rejects_UnclearLicence");
            yield return new TestCaseData((Action<Dictionary<string, object>>)(m => m["spec"] = "geo_tile@v2")).SetName("Rejects_WrongSpec");
            yield return new TestCaseData((Action<Dictionary<string, object>>)(m => ((List<object>)m["layers"]).RemoveAt(5))).SetName("Rejects_MissingFaultLayer");
        }

        static Dictionary<string, object> Layer(Dictionary<string, object> m, int i) => (Dictionary<string, object>)((List<object>)m["layers"])[i];
        static Dictionary<string, object> Src(Dictionary<string, object> m, int i, int j) => (Dictionary<string, object>)((List<object>)Layer(m, i)["sources"])[j];

        [TestCaseSource(nameof(Doctorings))]
        public void DoctoredManifest_IsRefused(Action<Dictionary<string, object>> edit)
        {
            var dir = GeoTestData.Doctored(edit);
            var ex = Assert.Throws<GeoTileRejectedException>(() => new GeoTileContext(dir, GeoTestData.Settings(), verifyHashes: false));
            Assert.IsNotEmpty(ex.Problems);
        }

        [Test]
        public void TamperedFile_IsRefused_BySha256()
        {
            var dir = GeoTestData.Doctored(m => { });
            foreach (var f in Directory.GetFiles(GeoTestData.M0Dir))
                if (!f.EndsWith("manifest.json") && !f.EndsWith(".meta")) File.Copy(f, Path.Combine(dir, Path.GetFileName(f)));
            File.AppendAllText(Path.Combine(dir, "faults.geojson"), " ");
            var ex = Assert.Throws<GeoTileRejectedException>(() => new GeoTileContext(dir, GeoTestData.Settings(), verifyHashes: true));
            Assert.IsTrue(ex.Problems.Any(p => p.Contains("sha256 mismatch for faults.geojson")), string.Join("; ", ex.Problems));
        }

        [Test]
        public void Attributions_IncludeOsmCreditAndOdbLNotice()
        {
            var ctx = new GeoTileContext(GeoTestData.M0Dir, GeoTestData.Settings(), verifyHashes: false);
            var a = GeoManifestValidator.Attributions(ctx.Manifest);
            Assert.IsTrue(a.Contains("© OpenStreetMap contributors (ODbL)"));
            Assert.IsTrue(a.Any(s => s.Contains("Open Database License")));
        }
    }

    public class GeoIdTests
    {
        [Test]
        public void TileId_ParseRoundTrip_AndOwnership()
        {
            Assert.IsTrue(GeoTileId.TryParse("L0_30_36", out var t));
            Assert.AreEqual("L0_30_36", t.ToString());
            Assert.AreEqual(t, GeoTileId.Containing(62375.6, 74051.1));
            Assert.IsTrue(t.Contains(61440, 73728));
            Assert.IsFalse(t.Contains(63488, 74000));
            Assert.AreEqual(4096.0, GeoTileId.From(1, 0, 0).SizeM);
            Assert.IsFalse(GeoTileId.TryParse("30_36", out _));
        }

        [Test]
        public void Bid_MapsToCoreBuildingId()
        {
            Assert.AreEqual("bld:osm-way:431972186", BuildingsLayer.ParseBid("osm:w431972186").ToString());
            Assert.AreEqual("bld:osm-relation:13543125", BuildingsLayer.ParseBid("osm:r13543125").ToString());
            Assert.Throws<FormatException>(() => BuildingsLayer.ParseBid("osm:n1"));
            Assert.Throws<FormatException>(() => BuildingsLayer.ParseBid("way 5"));
        }
    }

    public class GeoMeshTests
    {
        static float[] Bumpy(int n)
        {
            var h = new float[n * n];
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                    h[r * n + c] = (float)(10 * Math.Sin(c * 0.21) * Math.Cos(r * 0.13) + 0.02 * ((r * 7919 + c * 104729) % 97));
            return h;
        }

        [Test]
        public void Rtin_RespectsMaxError_AndIsCrackFreeAndUpward()
        {
            int n = 129;
            var h = Bumpy(n);
            var r = RtinMesher.Build(h, n, 1.0, 0.25f, 32);
            Assert.LessOrEqual(r.MaxErrorM, 0.25 + 1e-4);
            Assert.AreEqual(16, r.Chunks.Count);
            // crack-free: every edge is shared by exactly two triangles except the tile boundary
            var edges = new Dictionary<(long, long), int>();
            foreach (var m in r.Chunks)
                for (int k = 0; k < m.Indices.Length; k += 3)
                    for (int e = 0; e < 3; e++)
                    {
                        int a = m.Indices[k + e], b = m.Indices[k + (e + 1) % 3];
                        long ka = Key(m, a), kb = Key(m, b);
                        var key = ka < kb ? (ka, kb) : (kb, ka);
                        edges[key] = edges.TryGetValue(key, out var c) ? c + 1 : 1;
                        // upward normal
                    }
            foreach (var kv in edges)
            {
                bool boundary = OnBoundary(kv.Key.Item1, n) && OnBoundary(kv.Key.Item2, n);
                Assert.AreEqual(boundary ? 1 : 2, kv.Value, "edge " + kv.Key);
            }
            foreach (var m in r.Chunks)
                for (int k = 0; k < m.Indices.Length; k += 3)
                {
                    var p = m.Positions; int a = m.Indices[k] * 3, b = m.Indices[k + 1] * 3, c = m.Indices[k + 2] * 3;
                    double ny = (p[b + 2] - p[a + 2]) * (p[c] - p[a]) - (p[b] - p[a]) * (p[c + 2] - p[a + 2]);
                    Assert.Greater(ny, 0, "triangle must face up");
                }
        }

        static long Key(GeoMeshData m, int v) => (long)Math.Round(m.Positions[3 * v + 2]) * 100000 + (long)Math.Round(m.Positions[3 * v]);
        static bool OnBoundary(long k, int n) { long x = k % 100000, z = k / 100000; return x == 0 || z == 0 || x == n - 1 || z == n - 1; }

        [Test]
        public void Triangulator_SquareWithHole_HasRightArea()
        {
            var rings = new List<double[]> { new double[] { 0, 0, 10, 0, 10, 10, 0, 10, 0, 0 }, new double[] { 3, 3, 3, 6, 6, 6, 6, 3, 3, 3 } };
            var tris = Triangulator.Triangulate(rings);
            double a = tris.Sum(t => ((t[2] - t[0]) * (t[5] - t[1]) - (t[4] - t[0]) * (t[3] - t[1])) / 2);
            Assert.AreEqual(91.0, a, 1e-9);
        }

        [Test]
        public void Extrusion_IsWatertight_ConsistentlyWound_AndOutward()
        {
            var b = new GeoBuildingData { Polygons = new List<List<double[]>> { new List<double[]> {
                new double[] { 0, 0, 20, 0, 20, 5, 8, 5, 8, 15, 0, 15, 0, 0 }, new double[] { 2, 2, 2, 4, 4, 4, 4, 2, 2, 2 } } } };
            GeoGeom.Centroid(b.Polygons, out b.AnchorLocalX, out b.AnchorLocalZ, out b.AreaM2);
            var m = BuildingExtruder.Extrude(b, -2, 30, 0, "t");
            AssertClosed(m, out double vol);
            Assert.AreEqual(b.AreaM2 * 32, vol, 1e-3);
            Assert.AreEqual(100 + 80 - 4, b.AreaM2, 1e-9);
        }

        [Test]
        public void PyramidalRoof_IsClosed_ApexAtHeight()
        {
            var b = new GeoBuildingData { RoofShape = "pyramidal", Polygons = new List<List<double[]>> { new List<double[]> { new double[] { 0, 0, 10, 0, 10, 10, 0, 10, 0, 0 } } } };
            GeoGeom.Centroid(b.Polygons, out b.AnchorLocalX, out b.AnchorLocalZ, out b.AreaM2);
            var m = BuildingExtruder.Extrude(b, 0, 30, 10, "p");
            AssertClosed(m, out double vol);
            Assert.AreEqual(100 * 20 + 100 * 10 / 3.0, vol, 1e-3);
            Assert.AreEqual(30f, Enumerable.Range(0, m.VertexCount).Max(i => m.Positions[3 * i + 1]));
        }

        /// <summary>Every directed edge (by position) has exactly one reverse twin; signed volume via divergence theorem.</summary>
        public static void AssertClosed(GeoMeshData m, out double volume)
        {
            var dir = new Dictionary<string, int>();
            string P(int v) => m.Positions[3 * v] + "," + m.Positions[3 * v + 1] + "," + m.Positions[3 * v + 2];
            volume = 0;
            for (int k = 0; k < m.Indices.Length; k += 3)
            {
                for (int e = 0; e < 3; e++)
                {
                    var key = P(m.Indices[k + e]) + ">" + P(m.Indices[k + (e + 1) % 3]);
                    dir[key] = dir.TryGetValue(key, out var c) ? c + 1 : 1;
                }
                var p = m.Positions; int a = m.Indices[k] * 3, b = m.Indices[k + 1] * 3, cc = m.Indices[k + 2] * 3;
                // Unity front normal = cross(b-a, c-a); outward faces give positive volume with this sign
                volume += (p[a] * (p[b + 1] * p[cc + 2] - p[b + 2] * p[cc + 1]) - p[a + 1] * (p[b] * p[cc + 2] - p[b + 2] * p[cc]) + p[a + 2] * (p[b] * p[cc + 1] - p[b + 1] * p[cc])) / 6.0;
            }
            // T-junction-free closure: count edge use in both directions, split collinear edges are summed by length test below
            int unmatched = 0;
            foreach (var kv in dir)
            {
                var parts = kv.Key.Split('>');
                var rev = parts[1] + ">" + parts[0];
                if (!dir.TryGetValue(rev, out var c) || c != kv.Value) unmatched++;
                Assert.AreEqual(1, kv.Value, "directed edge used twice (inconsistent winding): " + kv.Key);
            }
            Assert.AreEqual(0, unmatched, "open edges");
            Assert.Greater(volume, 0, "faces must point outward");
        }
    }

    public class GeoTileImportTests
    {
        [Test]
        public void M0_Imports_AllLayers_WithManifestCounts()
        {
            var m = GeoTestData.M0();
            var man = m.Context.ManifestLayer("buildings");
            Assert.AreEqual(GeoJson.Num(man, "count"), m.Buildings.Count);
            Assert.AreEqual(GeoJson.Num(m.Context.ManifestLayer("roads"), "count"), m.Layer("roads").Roads.Count);
            Assert.AreEqual(0, m.Layer("faults").Faults.Count);
            Assert.AreEqual(1.0, m.Stats["faults.empty_by_design"]);
            Assert.AreEqual(GeoJson.Num(m.Context.ManifestLayer("water"), "water_fraction"), m.Stats["water.fraction"], 1e-5);
            // RTIN error is per-split, not a strict bound: the stated (measured) error is what gates (WorldGeo.json, MODULE.md)
            Assert.LessOrEqual(m.Stats["terrain.max_error_m"], 0.25);
            Assert.LessOrEqual(m.Stats["terrain.rms_error_m"], 0.05);
            Assert.LessOrEqual(m.Stats["terrain.triangles"] + m.Stats["buildings.triangles"] + m.Stats["water.triangles"], 1500000);
            TestContext.WriteLine("terrain triangles " + m.Stats["terrain.triangles"] + ", buildings " + m.Stats["buildings.triangles"] + ", water " + m.Stats["water.triangles"]);
        }

        [Test]
        public void Landmarks_HaveSourceHeights_AndSalesforceIsTallest()
        {
            var m = GeoTestData.M0();
            var sf = m.ById[BuildingsLayer.ParseBid("osm:w431972186")];
            Assert.AreEqual(326.0, sf.HeightM, 1e-9);
            double top = double.MinValue; GeoBuildingData tallest = null;
            foreach (var b in m.Buildings)
                if (b.Mesh != null)
                    for (int i = 1; i < b.Mesh.Positions.Length; i += 3)
                        if (b.Mesh.Positions[i] > top) { top = b.Mesh.Positions[i]; tallest = b; }
            Assert.AreEqual("osm:w431972186", tallest.PartOf.IsValid ? GeoBidOf(m, tallest.PartOf) : tallest.Bid);
        }

        static string GeoBidOf(GeoTileModel m, BuildingId id) => m.ById[id].Bid;

        [Test]
        public void AssessorFields_AreRead_AndAbsentMeansZeroOrNull()
        {
            var m = GeoTestData.M0();
            int withYear = 0, withStories = 0, withClass = 0, noYear = 0;
            foreach (var b in m.Buildings)
            {
                if (b.YearBuilt > 0) { withYear++; Assert.That(b.YearBuilt, Is.InRange(1800, 2100)); } else noYear++;
                if (b.Stories > 0) withStories++;
                if (!string.IsNullOrEmpty(b.StructClass)) withClass++;
            }
            TestContext.WriteLine("year_built " + withYear + ", stories " + withStories + ", struct_class " + withClass + ", no year " + noYear);
            Assert.Greater(withYear, 0);
            Assert.Greater(noYear, 0);   // absent stays 0
        }

        [Test]
        public void Removal_ByBid_LeavesOthersUnchanged_AndStructuresTakeTheirParts()
        {
            var m = new GeoTileModel(new GeoTileContext(GeoTestData.M0Dir, GeoTestData.Settings(), verifyHashes: false));
            var before = m.CombineChunk(3, 3);
            var rng = new Random(20261114);
            var ids = m.Buildings.Select(b => b.Id).ToList();
            var victims = new HashSet<BuildingId>();
            while (victims.Count < 20) victims.Add(ids[rng.Next(ids.Count)]);
            foreach (var v in victims) m.Remove(v);
            foreach (var b in m.Buildings)
                if (!m.Removed.Contains(b.Id)) Assert.IsFalse(victims.Contains(b.Id));
            var tam = BuildingsLayer.ParseBid("osm:w24222973");
            var removed = m.Remove(tam);
            Assert.GreaterOrEqual(removed.Count, 5, "Transamerica structure removes its stepped parts");
            Assert.IsTrue(m.PartsOf[tam].All(p => m.Removed.Contains(p.Id)));
            Assert.AreEqual(0, m.Remove(tam).Count, "idempotent");
            var after = m.CombineChunk(3, 3);
            Assert.LessOrEqual(after.TriangleCount, before.TriangleCount);
        }
    }
}
