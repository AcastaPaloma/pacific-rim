// T-111b: geo_lod@v1 reader (L1..L4), validator mirror and journal replay for the LOD forms. The synthetic tile is written by the test (a real
// pyramid tile is 13 MB and git-ignored); when a baked pyramid is reachable (KAIJU_LOD_ROOT or Pipelines/geo/cache/tiles/v1) the real-tile
// tests run too, otherwise they are ignored with the reason.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using Game.Core;
using Game.World.Geo;
using NUnit.Framework;

namespace Game.World.Streaming.Host.Tests
{
    public class GeoLodReaderTests
    {
        static readonly GeoTileId T = GeoTileId.From(1, 1, 1);          // L0 (2,2) (3,2) (2,3) (3,3); origin 4096, 4096
        string _root, _dir;
        static BuildingId Bld(ulong n) => BuildingId.From(BuildingSource.OsmWay, n);

        [OneTimeSetUp]
        public void Setup()
        {
            _root = Path.Combine(Path.GetTempPath(), "kaiju_lod_" + Guid.NewGuid().ToString("N"));
            _dir = LodSpec.TileFolder(_root, T);
            Directory.CreateDirectory(_dir);
            WriteSyntheticTile(_dir, T);
        }

        [OneTimeTearDown]
        public void Teardown() { try { Directory.Delete(_root, true); } catch (IOException) { } }

        // ---- synthetic tile ----

        static double[] Box(double x, double z, double w, double d, double y0, double y1) => new[]
        {
            x, y0, z,  x + w, y0, z,  x + w, y0, z + d,  x, y0, z + d,
            x, y1, z,  x + w, y1, z,  x + w, y1, z + d,  x, y1, z + d,
        };
        static readonly ushort[] BoxIdx =
        {
            0, 2, 1, 0, 3, 2,  4, 5, 6, 4, 6, 7,
            0, 1, 5, 0, 5, 4,  1, 2, 6, 1, 6, 5,  2, 3, 7, 2, 7, 6,  3, 0, 4, 3, 4, 7,
        };
        static float[] Ups(int vertices) { var n = new float[vertices * 3]; for (int k = 1; k < n.Length; k += 3) n[k] = 1f; return n; }

        static void WriteSyntheticTile(string dir, GeoTileId id)
        {
            double ox = id.OriginX, oz = id.OriginZ;
            // terrain: gentle slope plus a rough bump so the RTIN mesh is non-trivial; heights 0..~41 m
            const int N = 2049;
            double hMin = 0, hMax = 50;
            var r16 = new byte[N * N * 2];
            for (int r = 0; r < N; r++)
                for (int c = 0; c < N; c++)
                {
                    double h = 0.01 * c * 2 + 3.0 * Math.Sin(c * 0.01) * Math.Cos(r * 0.013) + 10;
                    ushort q = (ushort)Math.Round((h - hMin) / (hMax - hMin) * 65535.0);
                    int o = 2 * (r * N + c); r16[o] = (byte)(q & 255); r16[o + 1] = (byte)(q >> 8);
                }
            // b1: chunk (0,0) holds buildings 101 and 102, chunk (1,0) the part 103 of structure 101
            var a = Box(ox + 10, oz + 10, 20, 20, 5, 25);
            var b = Box(ox + 60, oz + 10, 20, 20, 5, 35);
            var cpos = Box(ox + 300, oz + 10, 20, 20, 5, 15);
            var chunk0 = a.Concat(b).ToArray();
            var idx0 = BoxIdx.Concat(BoxIdx.Select(i => (ushort)(i + 8))).ToArray();
            var mesh = LodMesh.Encode(new List<(int, int, double[], float[], ushort[])>
            {
                (0, 0, chunk0, Ups(16), idx0), (1, 0, cpos, Ups(8), (ushort[])BoxIdx.Clone()),
            }, new List<LodRange>
            {
                new LodRange { BidIndex = 0, ChunkIndex = 0, FirstIndex = 0, IndexCount = 36 },
                new LodRange { BidIndex = 1, ChunkIndex = 0, FirstIndex = 36, IndexCount = 36 },
                new LodRange { BidIndex = 2, ChunkIndex = 1, FirstIndex = 0, IndexCount = 36 },
            });
            string index = "{\"spec\":\"geo_lod@v1\",\"form\":\"b1\",\"tile\":\"L1_01_01\",\"chunk_m\":256.0,\"buildings\":["
                + "{\"bid\":\"osm:w101\",\"chunk\":[0,0],\"h_min\":5.0,\"h_max\":25.0,\"landmark\":false},"
                + "{\"bid\":\"osm:w102\",\"chunk\":[0,0],\"h_min\":5.0,\"h_max\":35.0,\"landmark\":false},"
                + "{\"bid\":\"osm:w103\",\"chunk\":[1,0],\"h_min\":5.0,\"h_max\":15.0,\"landmark\":false,\"part_of\":\"osm:w101\"}]}";
            // water: the south-west quarter of the tile
            var w = new byte[2048 * 2048];
            for (int row = 1024; row < 2048; row++) for (int col = 0; col < 1024; col++) w[row * 2048 + col] = 255;   // png row 0 = north
            var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["terrain.r16"] = r16, ["b1.lodmesh"] = mesh, ["b1.index.json"] = Encoding.UTF8.GetBytes(index), ["water.png"] = Png(w, 2048, 2048),
            };
            foreach (var kv in files) File.WriteAllBytes(Path.Combine(dir, kv.Key), kv.Value);
            var sb = new StringBuilder();
            string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
            sb.Append("{\"spec\":\"geo_lod@v1\",\"tile\":\"" + id + "\",\"level\":" + id.Level + ",\"i\":" + id.I + ",\"j\":" + id.J);
            sb.Append(",\"bounds_baytm\":[" + F(ox) + "," + F(oz) + "," + F(ox + id.SizeM) + "," + F(oz + id.SizeM) + "],\"source_level\":0,\"source_fingerprint\":\"synthetic\"");
            sb.Append(",\"terrain\":{\"file\":\"terrain.r16\",\"samples\":2049,\"spacing_m\":2.0,\"h_min\":" + F(hMin) + ",\"h_max\":" + F(hMax) + ",\"rtin_threshold_m\":0.2,\"rtin_threshold_for_cap_m\":0.5}");
            sb.Append(",\"buildings\":{\"form_b1\":{\"file\":\"b1.lodmesh\",\"index\":\"b1.index.json\"}}");
            sb.Append(",\"water\":{\"file\":\"water.png\"}");
            sb.Append(",\"errors\":{\"terrain\":{\"max_m\":1.0,\"rms_m\":0.1,\"p99_m\":0.5},\"buildings\":{\"footprint_max_m\":1.0,\"footprint_rms_m\":0.5,\"height_max_m\":0.0}}");
            sb.Append(",\"stats\":{\"triangles\":1},\"chunk_m\":256.0,\"attribution\":[],\"licence_ok\":true,\"files\":{");
            sb.Append(string.Join(",", files.Select(kv => "\"" + kv.Key + "\":\"" + LodSpec.Sha256Hex(kv.Value) + "\"")));
            sb.Append("}}");
            File.WriteAllText(Path.Combine(dir, "manifest.json"), sb.ToString());
        }

        static byte[] Png(byte[] gray, int w, int h)
        {
            var raw = new byte[(w + 1) * h];
            for (int y = 0; y < h; y++) Buffer.BlockCopy(gray, y * w, raw, y * (w + 1) + 1, w);
            var z = new MemoryStream();
            z.WriteByte(0x78); z.WriteByte(0x9C);
            using (var ds = new DeflateStream(z, CompressionLevel.Fastest, true)) ds.Write(raw, 0, raw.Length);
            uint a = 1, b = 0;
            foreach (var v in raw) { a = (a + v) % 65521; b = (b + a) % 65521; }
            z.Write(new[] { (byte)(b >> 8), (byte)b, (byte)(a >> 8), (byte)a }, 0, 4);
            var o = new MemoryStream();
            o.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
            void Chunk(string type, byte[] data)
            {
                var len = new[] { (byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length };
                o.Write(len, 0, 4);
                var td = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
                o.Write(td, 0, td.Length);
                uint crc = 0xFFFFFFFF;
                foreach (var v in td) { crc ^= v; for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1; }
                crc ^= 0xFFFFFFFF;
                o.Write(new[] { (byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc }, 0, 4);
            }
            Chunk("IHDR", new byte[] { (byte)(w >> 24), (byte)(w >> 16), (byte)(w >> 8), (byte)w, (byte)(h >> 24), (byte)(h >> 16), (byte)(h >> 8), (byte)h, 8, 0, 0, 0, 0 });
            Chunk("IDAT", z.ToArray());
            Chunk("IEND", new byte[0]);
            return o.ToArray();
        }

        static int Tris(LodTileResult r, string layer) => r.Meshes.Where(m => m.Layer == layer).Sum(m => m.TriangleCount);

        // ---- validator ----

        [Test]
        public void ValidatorAcceptsTheSyntheticTileAndNamesEveryDefect()
        {
            Assert.IsEmpty(LodSpec.Validate(_dir, T));
            var man = File.ReadAllText(Path.Combine(_dir, "manifest.json"));
            var tmp = Path.Combine(_root, "bad"); Directory.CreateDirectory(tmp);
            foreach (var f in Directory.GetFiles(_dir)) File.Copy(f, Path.Combine(tmp, Path.GetFileName(f)), true);
            File.WriteAllText(Path.Combine(tmp, "manifest.json"), man.Replace("geo_lod@v1\",\"tile", "geo_lod@v2\",\"tile").Replace("\"licence_ok\":true,", ""));
            File.WriteAllBytes(Path.Combine(tmp, "b1.index.json"), new byte[] { 32 });
            File.Delete(Path.Combine(tmp, "water.png"));
            var p = string.Join(" | ", LodSpec.Validate(tmp, T));
            StringAssert.Contains("spec is 'geo_lod@v2'", p);
            StringAssert.Contains("missing top-level field 'licence_ok'", p);
            StringAssert.Contains("sha256 mismatch for b1.index.json", p);
            StringAssert.Contains("file water.png missing", p);
            Assert.IsNotEmpty(LodSpec.Validate(_dir, GeoTileId.From(1, 0, 0)), "manifest of another tile");
            Assert.AreEqual("manifest.json missing", LodSpec.Validate(Path.Combine(_root, "nowhere"))[0]);
        }

        [Test]
        public void LodMeshRoundTripsWithinTheQuantisationStep()
        {
            var pos = Box(5000.25, 4100.5, 12, 9, 3, 17);
            var bytes = LodMesh.Encode(new List<(int, int, double[], float[], ushort[])> { (3, 4, pos, Ups(8), (ushort[])BoxIdx.Clone()) },
                new List<LodRange> { new LodRange { BidIndex = 7, ChunkIndex = 0, FirstIndex = 0, IndexCount = 36 } });
            var m = LodMesh.Decode(bytes);
            Assert.AreEqual(8, m.VertexCount); Assert.AreEqual(36, m.IndexCount);
            Assert.AreEqual(3, m.Chunks[0].Cx); Assert.AreEqual(4, m.Chunks[0].Cz); Assert.AreEqual(7, m.Ranges[0].BidIndex);
            for (int k = 0; k < pos.Length; k++) Assert.AreEqual(pos[k], m.Positions[k], 12.0 / 65535 + 1e-3);
            Assert.AreEqual(1f, m.Normals[1], 0.01f);
            Assert.Throws<InvalidDataException>(() => LodMesh.Decode(new byte[40]));
            Assert.Throws<InvalidDataException>(() => LodMesh.Decode(bytes.Take(bytes.Length - 8).ToArray()));
        }

        // ---- reading ----

        [Test]
        public void ReadsTerrainBuildingsAndWaterInTileLocalMetres()
        {
            var rd = new GeoLodTileReader(_root);
            var r = rd.Load(T);
            Assert.AreEqual(0.2, r.TerrainThresholdM, "nominal threshold, no cap relaxation");
            Assert.Greater(r.TerrainTriangles, 1000);
            Assert.AreEqual(36, r.B1Triangles, "three boxes of 12 triangles");
            Assert.Greater(r.WaterTriangles, 0);
            var terr = r.Meshes.Where(m => m.Layer == "terrain").ToList();
            Assert.AreEqual(64, terr.Count, "8 x 8 chunks of 256 cells");
            foreach (var m in terr) for (int v = 0; v < m.VertexCount; v++) { Assert.GreaterOrEqual(m.Positions[3 * v], 0f); Assert.LessOrEqual(m.Positions[3 * v], 4096f); }
            var bm = r.Meshes.Where(m => m.Layer == "buildings").ToList();
            Assert.AreEqual(2, bm.Count);
            var c0 = bm.First(m => m.ChunkX == 0);
            Assert.AreEqual(10.0, c0.Positions.Where((v, k) => k % 3 == 0).Min(), 0.01, "tile-local x of building 101");
            Assert.AreEqual(35.0, c0.Positions.Where((v, k) => k % 3 == 1).Max(), 0.01);
            var water = r.Meshes.Where(m => m.Layer == "water").ToList();
            foreach (var m in water) for (int v = 0; v < m.VertexCount; v++) { Assert.AreEqual(0.969f, m.Positions[3 * v + 1], 1e-6f); Assert.LessOrEqual(m.Positions[3 * v], 2048.01f); Assert.LessOrEqual(m.Positions[3 * v + 2], 2048.01f); }
            Assert.AreEqual(2 * 16, Tris(r, "water"), "the wet quarter is 4 x 4 chunks of one merged rectangle (2 triangles) each");
        }

        [Test]
        public void ThresholdOptionsNeverRelaxByThemselves()
        {
            var nominal = new GeoLodTileReader(_root).Load(T);
            var cap = new GeoLodTileReader(_root, null, new GeoLodOptions { UseCapThreshold = true }).Load(T);
            var forced = new GeoLodTileReader(_root, null, new GeoLodOptions { TerrainThresholdM = 1.0 }).Load(T);
            Assert.AreEqual(0.5, cap.TerrainThresholdM); Assert.AreEqual(1.0, forced.TerrainThresholdM);
            Assert.Less(cap.TerrainTriangles, nominal.TerrainTriangles);
            Assert.Less(forced.TerrainTriangles, cap.TerrainTriangles);
        }

        [Test]
        public void BordersAreForcedOnlyWhereANeighbourIsBaked()
        {
            var alone = new GeoLodTileReader(_root).Load(T);
            var nb = GeoTileId.From(1, 2, 1);
            var d = LodSpec.TileFolder(_root, nb);
            Directory.CreateDirectory(d);
            File.WriteAllText(Path.Combine(d, "manifest.json"), "{}");              // only existence is consulted for the border rule
            try
            {
                var withEast = new GeoLodTileReader(_root, null, new GeoLodOptions { Validate = true }).Load(T);
                Assert.Greater(withEast.TerrainTriangles, alone.TerrainTriangles, "east border nodes are all vertices now");
            }
            finally { Directory.Delete(d, true); }
        }

        [Test]
        public void RouterServesLevelsOneToFourAndRefusesPhysics()
        {
            var router = new LevelRoutingReader();
            var rd = GeoLodTileReader.RegisterAll(router, _root);
            for (int n = 1; n <= 4; n++) Assert.IsTrue(router.Has(n));
            Assert.IsFalse(router.Has(0));
            var p = router.Read(ResidencyKey.Render(T), CancellationToken.None);
            Assert.Greater(p.Meshes.Count, 60); Assert.Greater(p.GpuBytes, 0);
            Assert.Throws<NotSupportedException>(() => router.Read(ResidencyKey.Render(GeoTileId.From(0, 1, 1)), CancellationToken.None));
            Assert.Throws<FileNotFoundException>(() => router.Read(ResidencyKey.Render(GeoTileId.From(2, 0, 0)), CancellationToken.None) );
            Assert.IsTrue(rd.Exists(T)); Assert.IsFalse(rd.Exists(GeoTileId.From(1, 0, 0)));
            var cts = new CancellationTokenSource(); cts.Cancel();
            Assert.Throws<OperationCanceledException>(() => rd.Read(ResidencyKey.Render(T), cts.Token));
        }

        // ---- journal replay ----

        [Test]
        public void RemovedBuildingsLeaveTheirIndexRangesAndTakeTheirPartsWithThem()
        {
            var j = new StreamingJournal();
            var rd = new GeoLodTileReader(_root, j);
            var before = rd.Load(T);
            Assert.AreEqual(36, before.B1Triangles);
            // building 102 lives in L0 tile (2,2) (any L0 tile of the L1 tile may hold the record)
            j.RecordRemoval(GeoTileId.From(0, 2, 2), Bld(102));
            var r1 = rd.Load(T);
            Assert.AreEqual(24, r1.B1Triangles); Assert.AreEqual(1, r1.RemovedBuildingsHidden);
            Assert.AreEqual(before.TerrainTriangles, r1.TerrainTriangles, "terrain untouched by a removal");
            // removing structure 101 drops its own box and the part 103 (listed in the other chunk, part_of = 101)
            j.RecordRemoval(GeoTileId.From(0, 3, 3), Bld(101));
            var r2 = rd.Load(T);
            Assert.AreEqual(0, r2.B1Triangles); Assert.AreEqual(3, r2.RemovedBuildingsHidden);
            Assert.AreEqual(0, r2.Meshes.Count(m => m.Layer == "buildings"), "chunks with nothing left are omitted");
            // an id that is not in the tile changes nothing
            var j2 = new StreamingJournal(); j2.RecordRemoval(GeoTileId.From(0, 2, 2), Bld(999));
            Assert.AreEqual(36, new GeoLodTileReader(_root, j2).Load(T).B1Triangles);
            // vertices of a partly cut chunk are compacted, the rest still draws and references only valid vertices
            var j3 = new StreamingJournal(); j3.RecordRemoval(GeoTileId.From(0, 2, 2), Bld(102));
            var c0 = new GeoLodTileReader(_root, j3).Load(T).Meshes.First(m => m.Layer == "buildings" && m.ChunkX == 0);
            Assert.AreEqual(8, c0.VertexCount); Assert.AreEqual(12, c0.TriangleCount);
            Assert.LessOrEqual(c0.Indices.Max(), 7);
        }

        [Test]
        public void CratersReplayOnTheLevelOnlyWhenDeepEnough()
        {
            double cx = T.OriginX + 2000, cz = T.OriginZ + 2000;
            Func<double, LodTileResult> run = depth =>
            {
                var j = new StreamingJournal();
                j.RecordTerrainOp(new TerrainOp(cx, cz, 120, depth, 7));
                return new GeoLodTileReader(_root, j, new GeoLodOptions { KeepTerrainHeights = true }).Load(T);
            };
            var none = new GeoLodTileReader(_root, null, new GeoLodOptions { KeepTerrainHeights = true }).Load(T);
            var shallow = run(0.15);          // AffectsLevel(1): depth >= 0.2 m
            var deep = run(2.0);
            Assert.AreEqual(0, shallow.TerrainOpsApplied); Assert.AreEqual(none.TerrainTriangles, shallow.TerrainTriangles);
            Assert.AreEqual(1, deep.TerrainOpsApplied, "one crater recorded in several L0 tiles counts once");
            Assert.Greater(deep.TerrainNodesChanged, 1000);
            int centre = 1000 * 2049 + 1000;          // node at (2000 m, 2000 m) of the 2 m lattice
            Assert.Less(deep.TerrainRenderHeights[centre], none.TerrainRenderHeights[centre] - 1.0f, "the crater floor is in the mesh");
            Assert.AreEqual(none.TerrainRenderHeights[10], deep.TerrainRenderHeights[10], "far from the crater nothing changed");
            // deterministic: the same journal gives the same mesh
            Assert.AreEqual(deep.TerrainTriangles, run(2.0).TerrainTriangles);
        }

        // ---- real baked pyramid (optional) ----

        static string RealRoot()
        {
            var env = Environment.GetEnvironmentVariable("KAIJU_LOD_ROOT");
            if (!string.IsNullOrEmpty(env) && Directory.Exists(env)) return env;
            var d = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (d != null)
            {
                foreach (var c in new[] { Path.Combine(d.FullName, "Pipelines", "geo", "cache", "tiles", "v1"), Path.Combine(d.FullName, "kaiju", "Pipelines", "geo", "cache", "tiles", "v1") })
                    if (Directory.Exists(Path.Combine(c, "L1"))) return c;
                d = d.Parent;
            }
            return null;
        }

        [Test]
        public void RealBakedTilesValidateAndMatchTheirManifests()
        {
            var root = RealRoot();
            if (root == null) Assert.Ignore("no baked geo_lod@v1 pyramid (Pipelines/geo/bake_lod.py --out ...; set KAIJU_LOD_ROOT)");
            int checkedTiles = 0;
            for (int level = 1; level <= 4; level++)
            {
                var ld = Path.Combine(root, "L" + level);
                if (!Directory.Exists(ld)) continue;
                foreach (var td in Directory.GetDirectories(ld).OrderBy(x => x).Take(2))
                {
                    var name = Path.GetFileName(td).Split('_');
                    var id = GeoTileId.From(level, int.Parse(name[0]), int.Parse(name[1]));
                    Assert.IsEmpty(LodSpec.Validate(td, id), id.ToString());
                    var man = GeoJson.Obj(GeoJson.Parse(File.ReadAllText(Path.Combine(td, "manifest.json"))));
                    var forms = GeoJson.Obj(GeoJson.Get(man, "buildings"));
                    var r = new GeoLodTileReader(root, null, new GeoLodOptions { ForceSharedBorders = false }).Load(id);
                    var stats = GeoJson.Obj(GeoJson.Get(man, "stats"));
                    var t = GeoJson.Obj(GeoJson.Get(man, "terrain"));
                    double nominal = GeoJson.Num(t, "rtin_triangles");
                    // the 256-cell chunk limit adds a few hundred triangles to the baker's unchunked count
                    Assert.AreEqual(nominal, r.TerrainTriangles, nominal * 0.03 + 600, id + " terrain triangles vs manifest");
                    Assert.AreEqual(GeoJson.Num(stats, "building_triangles"), r.B1Triangles + r.B2Triangles, id + " building triangles vs manifest");
                    Assert.Greater(r.WaterTriangles + r.TerrainTriangles, 0);
                    // the decoder's winding convention: the side cross(b - a, c - a) points to is the stored normal's side
                    foreach (var form in LodSpec.FormsOfLevel(level))
                    {
                        var lm = LodMesh.Decode(File.ReadAllBytes(Path.Combine(td, form + ".lodmesh")));
                        int good = 0, total = 0;
                        foreach (var c in lm.Chunks)
                            for (int k = 0; k < c.IndexCount; k += 3)
                            {
                                int a = c.VertexFirst + lm.Indices[c.IndexFirst + k], b = c.VertexFirst + lm.Indices[c.IndexFirst + k + 1], cc = c.VertexFirst + lm.Indices[c.IndexFirst + k + 2];
                                double ux = lm.Positions[3 * b] - lm.Positions[3 * a], uy = lm.Positions[3 * b + 1] - lm.Positions[3 * a + 1], uz = lm.Positions[3 * b + 2] - lm.Positions[3 * a + 2];
                                double vx = lm.Positions[3 * cc] - lm.Positions[3 * a], vy = lm.Positions[3 * cc + 1] - lm.Positions[3 * a + 1], vz = lm.Positions[3 * cc + 2] - lm.Positions[3 * a + 2];
                                double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                                double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                                if (len < 1e-9) continue;
                                total++;
                                if ((nx * lm.Normals[3 * a] + ny * lm.Normals[3 * a + 1] + nz * lm.Normals[3 * a + 2]) / len > 0.5) good++;
                            }
                        Assert.Greater(total, 0, id + " " + form);
                        Assert.GreaterOrEqual(good, total * 0.98, id + " " + form + " winding vs normals");
                    }
                    checkedTiles++;
                }
            }
            Assert.Greater(checkedTiles, 0);
        }

        [Test]
        public void RealTileRemovalByBidDropsExactlyThatBuildingsTriangles()
        {
            var root = RealRoot();
            if (root == null) Assert.Ignore("no baked geo_lod@v1 pyramid");
            var l1 = Path.Combine(root, "L1");
            var td = Directory.GetDirectories(l1).OrderBy(x => x).FirstOrDefault(d => File.Exists(Path.Combine(d, "b1.index.json")));
            if (td == null) Assert.Ignore("no L1 tile with B1");
            var name = Path.GetFileName(td).Split('_');
            var id = GeoTileId.From(1, int.Parse(name[0]), int.Parse(name[1]));
            var idx = LodBuildingIndex.Parse(File.ReadAllText(Path.Combine(td, "b1.index.json")));
            var mesh = LodMesh.Decode(File.ReadAllBytes(Path.Combine(td, "b1.lodmesh")));
            int pick = -1;
            for (int k = 0; k < idx.Bids.Count && pick < 0; k++) if (idx.Bids[k].StartsWith("osm:") && idx.PartOf[k] == null && mesh.Ranges.Any(r => r.BidIndex == k)) pick = k;
            Assert.GreaterOrEqual(pick, 0);
            int expected = mesh.Ranges.Where(r => r.BidIndex == pick).Sum(r => r.IndexCount) / 3;
            var opt = new GeoLodOptions { ForceSharedBorders = false, TerrainThresholdM = 50 };       // coarse terrain: this test is about buildings
            var before = new GeoLodTileReader(root, null, opt).Load(id);
            var j = new StreamingJournal();
            j.RecordRemoval(GeoTileId.From(0, id.I * 2, id.J * 2), BuildingsLayer.ParseBid(idx.Bids[pick]));
            var after = new GeoLodTileReader(root, j, opt).Load(id);
            Assert.AreEqual(before.B1Triangles - expected, after.B1Triangles);
        }
    }
}
