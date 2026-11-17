// T-124f: the allocation reductions in RtinMesher / PngGray8 / the geo_lod@v1 reader keep every output bit-identical. Each case hashes every array of the
// built tile data (positions, normals, uvs, indices, chunk ids, names, RTIN render heights and error metrics) and compares it with the hash the
// pre-T-124f code produced (golden constants; KAIJU_T124F_PRINT=1 prints the current hashes). The real pyramid tile case runs when a baked pyramid is reachable.
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Game.World.Geo;
using NUnit.Framework;

namespace Game.World.Streaming.Host.Tests
{
    public class TileBuildEqualityTests
    {
        static void Put(IncrementalHash h, float[] a) { if (a == null) { h.AppendData(new byte[] { 0xFF }); return; } var b = new byte[a.Length * 4]; Buffer.BlockCopy(a, 0, b, 0, b.Length); h.AppendData(BitConverter.GetBytes(a.Length)); h.AppendData(b); }
        static void Put(IncrementalHash h, int[] a) { var b = new byte[a.Length * 4]; Buffer.BlockCopy(a, 0, b, 0, b.Length); h.AppendData(BitConverter.GetBytes(a.Length)); h.AppendData(b); }
        static void Put(IncrementalHash h, string s) { h.AppendData(Encoding.UTF8.GetBytes(s ?? "<null>")); h.AppendData(new byte[] { 0 }); }
        static void Put(IncrementalHash h, GeoMeshData m) { Put(h, m.Layer); Put(h, m.Material); Put(h, m.Name); Put(h, m.Positions); Put(h, m.Normals); Put(h, m.Uv); Put(h, m.Indices); h.AppendData(BitConverter.GetBytes(m.ChunkX)); h.AppendData(BitConverter.GetBytes(m.ChunkZ)); }
        static string Done(IncrementalHash h) { var d = h.GetHashAndReset(); var sb = new StringBuilder(); foreach (var x in d) sb.Append(x.ToString("x2")); return sb.ToString().Substring(0, 24); }
        static IncrementalHash NewHash() => IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        internal static string HashPayload(TilePayload p) { var h = NewHash(); foreach (var m in p.Meshes) Put(h, m); h.AppendData(BitConverter.GetBytes(p.GpuBytes)); h.AppendData(BitConverter.GetBytes(p.CpuBytes)); return Done(h); }
        internal static string HashLod(LodTileResult r)
        {
            var h = NewHash(); foreach (var m in r.Meshes) Put(h, m); Put(h, r.TerrainRenderHeights);
            h.AppendData(BitConverter.GetBytes(r.TerrainMaxErrorM)); h.AppendData(BitConverter.GetBytes(r.TerrainRmsErrorM)); h.AppendData(BitConverter.GetBytes(r.TerrainThresholdM));
            foreach (int v in new[] { r.TerrainTriangles, r.B1Triangles, r.B2Triangles, r.WaterTriangles, r.RemovedBuildingsHidden, r.TerrainOpsApplied, r.TerrainNodesChanged }) h.AppendData(BitConverter.GetBytes(v));
            return Done(h);
        }

        static void Check(string name, string actual, string golden)
        {
            if (Environment.GetEnvironmentVariable("KAIJU_T124F_PRINT") == "1") { Console.WriteLine("T124F_HASH " + name + " " + actual); return; }
            Assert.AreEqual(golden, actual, name + " must be bit-identical to the pre-T-124f output");
        }

        static string ProjectRoot()
        {
            var d = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (d != null)
            {
                if (Directory.Exists(Path.Combine(d.FullName, "Assets", "StreamingAssets", "Geo"))) return d.FullName;
                var k = Path.Combine(d.FullName, "kaiju"); if (Directory.Exists(Path.Combine(k, "Assets", "StreamingAssets", "Geo"))) return k;
                d = d.Parent;
            }
            throw new DirectoryNotFoundException("project root not found");
        }

        [Test]
        public void Rtin_SyntheticField_WithAndWithoutForcedEdges()
        {
            const int n = 513; var hgt = new float[n * n]; var rnd = new Random(7);
            for (int r = 0; r < n; r++) for (int c = 0; c < n; c++) hgt[r * n + c] = (float)(8 * Math.Sin(c * 0.05) * Math.Cos(r * 0.037) + (rnd.NextDouble() - 0.5) * (c > 300 ? 1.5 : 0.05) + (r > 400 ? 20 : 0));
            foreach (var force in new bool[][] { null, new[] { true, false, true, false } })
            {
                var res = RtinMesher.Build(hgt, n, 4.0, 0.25f, 128, "terrain", force);
                var h = NewHash(); foreach (var m in res.Chunks) Put(h, m); Put(h, res.RenderHeights);
                h.AppendData(BitConverter.GetBytes(res.MaxErrorM)); h.AppendData(BitConverter.GetBytes(res.RmsErrorM)); h.AppendData(BitConverter.GetBytes(res.Triangles)); h.AppendData(BitConverter.GetBytes(res.Vertices));
                Check("rtin_synth_force_" + (force == null ? "none" : "NS"), Done(h), force == null ? Golden.RtinNone : Golden.RtinNS);
            }
        }

        [Test]
        public void Rtin_RepeatedBuildsOnOneThread_AreIdentical_NoStateLeaksBetweenBuilds()
        {
            const int n = 513; var a = new float[n * n]; var b = new float[n * n]; var rnd = new Random(3);
            for (int i = 0; i < a.Length; i++) { a[i] = (float)(rnd.NextDouble() * 9); b[i] = (float)(i % n * 0.01 + rnd.NextDouble() * 0.2); }
            string Hash(float[] hf, bool[] f) { var res = RtinMesher.Build(hf, n, 4.0, 0.3f, 128, "terrain", f); var h = NewHash(); foreach (var m in res.Chunks) Put(h, m); Put(h, res.RenderHeights); h.AppendData(BitConverter.GetBytes(res.MaxErrorM)); return Done(h); }
            var ha = Hash(a, new[] { true, true, true, true }); var hb = Hash(b, null);
            Assert.AreEqual(ha, Hash(a, new[] { true, true, true, true })); Assert.AreEqual(hb, Hash(b, null)); Assert.AreEqual(ha, Hash(a, new[] { true, true, true, true }));
        }

        [Test]
        public void LodReader_SyntheticTile_BitIdentical()
        {
            var id = GeoTileId.From(1, 1, 1); var root = Path.Combine(Path.GetTempPath(), "kaiju_t124f_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(LodSpec.TileFolder(root, id)); GeoLodReaderTests.WriteSyntheticTile(LodSpec.TileFolder(root, id), id);
                var plain = new GeoLodTileReader(root, null, new GeoLodOptions { KeepTerrainHeights = true }).Load(id);
                Check("lod_synth_keepheights", HashLod(plain), Golden.LodSynthKeep);
                var noKeep = new GeoLodTileReader(root, null, new GeoLodOptions { KeepTerrainHeights = false }).Load(id);
                var plainNoHeights = HashLod(noKeep);
                Check("lod_synth_default", plainNoHeights, Golden.LodSynthDefault);
                var p = new GeoLodTileReader(root).Read(ResidencyKey.Render(id), CancellationToken.None);
                Check("lod_synth_payload", HashPayload(p), Golden.LodSynthPayload);
            }
            finally { try { Directory.Delete(root, true); } catch (IOException) { } }
        }

        [Test]
        public void L0Reader_CommittedTile_RenderAndPhysicsPayloadsBitIdentical()
        {
            var settings = GeoSettings.Load(Path.Combine(ProjectRoot(), "Assets", "Game", "Config", "WorldGeo.json"));
            var tiles = Path.Combine(ProjectRoot(), "Assets", "StreamingAssets", "Geo", "tiles", "v1"); var t = GeoTileId.From(0, 30, 36);
            var rd = new GeoL0TileReader(tiles, settings, 3);
            Check("l0_render", HashPayload(rd.Read(ResidencyKey.Render(t), CancellationToken.None)), Golden.L0Render);
            Check("l0_phys_3_3", HashPayload(rd.Read(ResidencyKey.PhysicsChunk(t, 3, 3), CancellationToken.None)), Golden.L0Phys33);
            Check("l0_phys_5_2", HashPayload(rd.Read(ResidencyKey.PhysicsChunk(t, 5, 2), CancellationToken.None)), Golden.L0Phys52);
        }

        [Test]
        public void LodReader_RealPyramidTiles_BitIdentical_WhenReachable()
        {
            var root = StreamingAllocBenchTests.RealLodRoot();
            if (root == null) Assert.Ignore("no baked geo_lod@v1 pyramid (set KAIJU_LOD_ROOT)");
            var sb = new StringBuilder();
            foreach (var (l, i, j) in new[] { (1, 17, 17), (2, 8, 8), (3, 4, 4), (4, 2, 2) })
            {
                var id = GeoTileId.From(l, i, j);
                if (!new GeoLodTileReader(root).Exists(id)) continue;
                sb.Append(id).Append('=').Append(HashLod(new GeoLodTileReader(root, null, new GeoLodOptions { KeepTerrainHeights = true }).Load(id))).Append(';');
            }
            Console.WriteLine("T124F_REAL " + sb);
            var outp = Environment.GetEnvironmentVariable("KAIJU_T124F_REAL_OUT");
            if (!string.IsNullOrEmpty(outp)) File.WriteAllText(outp, sb.ToString());
            var gold = Environment.GetEnvironmentVariable("KAIJU_T124F_REAL_GOLDEN");
            if (!string.IsNullOrEmpty(gold) && File.Exists(gold)) Assert.AreEqual(File.ReadAllText(gold), sb.ToString(), "real pyramid tiles must equal the pre-T-124f hashes");
        }

        // hashes of the output of the code before T-124f (RtinMesher, PngGray8, GeoLodTileReader as of 9395b9e)
        static class Golden
        {
            public const string RtinNone = "eff11a956e728acb18050b8e", RtinNS = "f8ac1a036dd650e09fe1134d", LodSynthKeep = "6a9ef29c691499f97da48f6e", LodSynthDefault = "84dd0019a11784a02df2edb4",
                LodSynthPayload = "5678daaa6feec341619d52a2", L0Render = "84f215ffd210ce00ae1a9eb8", L0Phys33 = "b9e3bef53afe183d9d13b721", L0Phys52 = "e58103e25ef62b039c387ace";
        }
    }
}
