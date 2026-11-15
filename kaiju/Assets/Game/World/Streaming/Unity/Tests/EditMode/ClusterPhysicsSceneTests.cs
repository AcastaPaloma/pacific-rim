// T-111: per-cluster PhysicsScene (EditMode). Colliders live only in the scene of their cluster; a cluster rebase moves them by exact integers and
// the raycast height does not change; a short flythrough on real Unity physics meets the M-015@v2 clauses. The 75 km run is FlythroughBatch.
using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Game.World.Geo;
using Game.World.Geo.Unity;
using Game.World.Streaming.Host;

namespace Game.World.Streaming.Unity.Tests
{
    public class ClusterPhysicsSceneTests
    {
        sealed class Sink : IBuildSink
        {
            public int Built;
            void IBuildSink.Built(in ResidencyKey key, long g, long c) { Built++; }
            void IBuildSink.BuildFailed(in ResidencyKey key, string reason) { Assert.Fail(reason); }
        }

        GameObject _parent; UnityTileBuilder _b; ClusterPhysicsScenes _scenes;
        [SetUp] public void Up()
        {
            _parent = new GameObject("cluster_test");
            _scenes = new ClusterPhysicsScenes("TestCluster");
            _b = new UnityTileBuilder(_parent.transform, new InlineWorkRunner(), GeoTileBuilder.LoadSettings(Application.dataPath)) { ClusterScenes = _scenes };
        }
        [TearDown] public void Down() { _b.ReleaseAll(); _scenes.Dispose(); UnityEngine.Object.DestroyImmediate(_parent); }

        static TilePayload Chunk(ResidencyKey key) => new SyntheticTileReader().Read(key, System.Threading.CancellationToken.None);

        [Test]
        public void ChunkLivesOnlyInItsClusterScene_AndRaycastFindsTheSyntheticSurface()
        {
            var key = ResidencyKey.PhysicsChunk(GeoTileId.From(0, 10, 20), 3, 4);
            _b.ShiftClusterOrigin(1, 10 * 2048.0, 20 * 2048.0);
            _b.ShiftClusterOrigin(2, 40 * 2048.0, 40 * 2048.0);
            _b.AssignCluster(key, 1);
            var sink = new Sink();
            _b.Build(key, Chunk(key), sink);
            _b.Tick();
            Assert.AreEqual(1, sink.Built);
            float lx = 3 * 256 + 100.5f, lz = 4 * 256 + 60.25f;          // inside the chunk, in cluster 1's frame
            Assert.IsTrue(_scenes.TryGet(1, out var c1)); Assert.IsTrue(_scenes.TryGet(2, out var c2));
            Assert.IsTrue(c1.Physics.Raycast(new Vector3(lx, 300, lz), Vector3.down, out var hit, 600f), "cluster 1 sees the chunk");
            double wx = 10 * 2048.0 + lx, wz = 20 * 2048.0 + lz;
            Assert.AreEqual(SyntheticBay.Height(wx, wz), hit.point.y, 0.01);
            Assert.IsFalse(c2.Physics.Raycast(new Vector3(lx, 300, lz), Vector3.down, out _, 600f), "cluster 2 has no collider there");
            Assert.IsFalse(Physics.defaultPhysicsScene.Raycast(new Vector3(lx, 300, lz), Vector3.down, out _, 600f), "the default scene is untouched");
        }

        [Test]
        public void ClusterRebase_MovesTheChunkByExactIntegers_AndTheHeightIsUnchanged()
        {
            var key = ResidencyKey.PhysicsChunk(GeoTileId.From(0, 10, 20), 3, 4);
            _b.ShiftClusterOrigin(1, 10 * 2048.0, 20 * 2048.0);
            _b.AssignCluster(key, 1);
            var sink = new Sink();
            _b.Build(key, Chunk(key), sink); _b.Tick();
            double wx = 10 * 2048.0 + 3 * 256 + 100.5, wz = 20 * 2048.0 + 4 * 256 + 60.25;
            _scenes.TryGet(1, out var c);
            double Height() { Physics.SyncTransforms(); return c.Physics.Raycast(new Vector3((float)(wx - c.OriginX), 300, (float)(wz - c.OriginZ)), Vector3.down, out var h, 600f) ? h.point.y : double.NaN; }
            double h0 = Height();
            _b.ShiftClusterOrigin(1, 12 * 2048.0, 19 * 2048.0);
            double h1 = Height();
            Assert.AreEqual(h0, h1, 1e-4);
            _b.ShiftClusterOrigin(1, 8 * 2048.0, 22 * 2048.0);
            Assert.AreEqual(h0, Height(), 1e-4);
        }

        [Test]
        public void ReassignedChunk_MovesToTheOtherClustersScene()
        {
            var key = ResidencyKey.PhysicsChunk(GeoTileId.From(0, 10, 20), 0, 0);
            _b.ShiftClusterOrigin(1, 10 * 2048.0, 20 * 2048.0);
            _b.ShiftClusterOrigin(2, 10 * 2048.0, 20 * 2048.0);
            _b.AssignCluster(key, 1);
            _b.Build(key, Chunk(key), new Sink()); _b.Tick();
            _scenes.TryGet(1, out var c1); _scenes.TryGet(2, out var c2);
            Assert.IsTrue(c1.Physics.Raycast(new Vector3(100, 300, 100), Vector3.down, out _, 600f));
            _b.AssignCluster(key, 2);
            Physics.SyncTransforms();
            Assert.IsFalse(c1.Physics.Raycast(new Vector3(100, 300, 100), Vector3.down, out _, 600f));
            Assert.IsTrue(c2.Physics.Raycast(new Vector3(100, 300, 100), Vector3.down, out _, 600f));
        }

        [Test]
        public void Flythrough_6km_OnRealPhysicsScenes_MeetsM015v2()
        {
            var sc = new FlythroughScenario { RouteLengthM = 6000.0, MechSpeedMps = 30.0, Debris = 20, WarmUpTicks = 6, DrainTicks = 6, Label = "unity_edit_6km" };
            var r = UnityFlythrough.Run(sc);
            TestContext.WriteLine(r.ToJson());
            Assert.LessOrEqual(r.MaxAbsLocalM, 4096.0);
            Assert.LessOrEqual(r.MaxReconstructionErrM, 1e-3);
            Assert.LessOrEqual(r.MaxPairChangeM, 1e-3);
            Assert.LessOrEqual(r.MaxTerrainDeltaM, 0.1);
            Assert.LessOrEqual(r.MaxRebaseTerrainDeltaM, 0.1);
            Assert.AreEqual(0, r.HoldEvents);
            Assert.AreEqual(0, r.FramesBelowSurface);
            Assert.AreEqual(0, r.FramesWithoutGround);
            Assert.AreEqual(0, r.WorldOriginMismatches);
            Assert.Greater(r.Rebases, 0);
        }
    }

    /// <summary>`unity run <project> -- -executeMethod Game.World.Streaming.Unity.Tests.FlythroughBatch.Run -m015Out DIR [-m015Speed 30,60] [-m015Route 0]` writes m015_unity_<speed>mps.json (full route when -m015Route is 0 or missing).</summary>
    public static class FlythroughBatch
    {
        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string n, string d) { for (int i = 0; i < args.Length - 1; i++) if (args[i] == n) return args[i + 1]; return d; }
            string dir = Arg("-m015Out", Path.Combine(Directory.GetCurrentDirectory(), "evidence", "T-111"));
            double route = double.Parse(Arg("-m015Route", "0"), System.Globalization.CultureInfo.InvariantCulture);
            foreach (var sp in Arg("-m015Speed", "30").Split(','))
            {
                double speed = double.Parse(sp, System.Globalization.CultureInfo.InvariantCulture);
                var sc = new FlythroughScenario { MechSpeedMps = speed, Label = "unity_" + (int)speed + "mps" };
                if (route > 0) sc.RouteLengthM = route;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var r = UnityFlythrough.Run(sc);
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "m015_unity_" + (int)speed + "mps.json");
                File.WriteAllText(path, r.ToJson());
                File.WriteAllText(Path.Combine(dir, "streaming_frames_unity_" + (int)speed + "mps.json"), r.ToFramesJson(speed));
                UnityEngine.Debug.Log("FlythroughBatch: wrote " + path + " in " + sw.Elapsed.TotalSeconds.ToString("F1") + " s, steps " + r.Steps + ", holds " + r.HoldEvents);
            }
            UnityEditor.EditorApplication.Exit(0);
        }
    }
}
