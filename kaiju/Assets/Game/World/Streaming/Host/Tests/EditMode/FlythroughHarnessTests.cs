// T-111: the flythrough harness on the engine-free logic world (real scheduler, host core, per-cluster physics chunk ownership).
// The Unity run of the same harness (real PhysicsScene per cluster) is in Game.World.Streaming.Unity.Tests / FlythroughBatch.
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Game.World.Geo;

namespace Game.World.Streaming.Host.Tests
{
    public class FlythroughHarnessTests
    {
        static FlythroughReport Run(double mechSpeed, int debris = 100, string label = "logic")
        {
            var sched = new StreamingScheduler(new StreamingConfig(), new DefaultTileCostModel());
            var world = new LogicFlythroughWorld();
            var host = new StreamingHostCore(sched, new SyntheticTileReader(), world, new InlineWorkRunner(), new StreamingHostOptions { MainThreadSliceMs = 1e9 });
            var sc = new FlythroughScenario { MechSpeedMps = mechSpeed, Debris = debris, Label = label };
            return FlythroughHarness.Run(sc, host, world, "logic");
        }

        [Test]
        public void Route_IsAtLeast75Km() { Assert.GreaterOrEqual(SyntheticBay.RouteLengthM, 75000.0); }

        [TestCase(30.0)]
        [TestCase(60.0)]
        public void M015v2_AllClauses_HoldAtDesignSpeedAndAtTheCap(double mechSpeed)
        {
            var r = Run(mechSpeed, label: "logic_" + (int)mechSpeed + "mps");
            TestContext.WriteLine(r.ToJson());
            var outDir = Environment.GetEnvironmentVariable("KAIJU_M015_OUT");   // evidence / Python fixtures: m015_logic_<speed>mps.json
            if (!string.IsNullOrEmpty(outDir)) { Directory.CreateDirectory(outDir); File.WriteAllText(Path.Combine(outDir, "m015_logic_" + (int)mechSpeed + "mps.json"), r.ToJson()); }
            Assert.GreaterOrEqual(r.Steps, 8000, "the slowest kaiju needs more than 8000 s for 75 km");
            Assert.AreEqual(2, r.Mechs); Assert.AreEqual(3, r.Kaiju); Assert.AreEqual(100, r.Debris);
            Assert.LessOrEqual(r.MaxAbsLocalM, 4096.0, "(a)");
            Assert.LessOrEqual(r.MaxReconstructionErrM, 1e-3, "(b)");
            Assert.LessOrEqual(r.RoundTripErrM, 1e-3, "(c)");
            Assert.LessOrEqual(r.ProbeMaxErrM, 1e-3, "(c) every probe step");
            Assert.LessOrEqual(r.MaxPairChangeM, 1e-3, "(d) pairwise distance");
            Assert.LessOrEqual(r.MaxRebasePairChangeM, 1e-3, "(d) pairwise distance across rebases");
            Assert.LessOrEqual(r.MaxRebaseTerrainDeltaM, 0.1, "(d) terrain height across rebases");
            Assert.LessOrEqual(r.MaxTerrainDeltaM, 0.1);
            Assert.AreEqual(0, r.HoldEvents, "(e) holds");
            Assert.AreEqual(0, r.FramesBelowSurface, "(e) below the surface");
            Assert.AreEqual(0, r.FramesWithoutGround, "a body without a collider under it");
            Assert.AreEqual(0, r.WorldOriginMismatches);
            Assert.AreEqual(0, r.UnclaimedChunks);
            Assert.Greater(r.Rebases, 20);
            Assert.GreaterOrEqual(r.ClustersMax, 2);
            Assert.Greater(r.ClusterShifts, 20);
        }

        [Test]
        public void Report_Json_IsStableAndNamesEveryClause()
        {
            var r = Run(30.0, debris: 4);
            var a = r.ToJson(); var b = r.ToJson();
            Assert.AreEqual(a, b);
            foreach (var k in new[] { "\"format\": \"m015_dump\"", "\"schema\": 2", "max_abs_local_m", "max_reconstruction_err_m", "round_trip_err_m", "pair_change_max_m", "terrain_delta_max_m", "hold_events", "frames_below_surface", "path_length_m" })
                StringAssert.Contains(k, a);
        }

        [Test]
        public void PhysicsChunks_LiveInTheFrameOfTheClusterThatNeedsThem()
        {
            // two bodies 40 km apart: two clusters, each with its own resident chunks; the ground under each is found only in its own cluster
            var sched = new StreamingScheduler(new StreamingConfig(), new DefaultTileCostModel());
            var world = new LogicFlythroughWorld();
            var host = new StreamingHostCore(sched, new SyntheticTileReader(), world, new InlineWorkRunner(), new StreamingHostOptions { MainThreadSliceMs = 1e9 });
            var f = new[]
            {
                new FocusSample { Id = 10, Kind = FocusKind.Body, X = 22000, Y = 20, Z = 100000, NeedsPhysics = true, ReachM = 100f },
                new FocusSample { Id = 11, Kind = FocusKind.Body, X = 62000, Y = 20, Z = 60000, NeedsPhysics = true, ReachM = 100f },
            };
            for (int i = 0; i < 12; i++) host.Tick(1.0, f);
            int c10 = host.ClusterOf(10), c11 = host.ClusterOf(11);
            Assert.AreNotEqual(c10, c11);
            Assert.IsFalse(host.IsBodyHeld(10)); Assert.IsFalse(host.IsBodyHeld(11));
            var a = host.LastPlan.Assignments;
            foreach (var asg in a)
            {
                double g = world.GroundHeightLocal(asg.ClusterId, asg.LocalX, asg.LocalZ);
                Assert.IsFalse(double.IsNaN(g), "ground under focus " + asg.FocusId);
                int other = asg.ClusterId == c10 ? c11 : c10;
                Assert.IsTrue(double.IsNaN(world.GroundHeightLocal(other, asg.LocalX, asg.LocalZ)), "the other cluster has no collider there");
            }
            Assert.AreEqual(0, host.UnclaimedChunks);
        }

        [Test]
        public void ClusterBuilder_IsToldWhenAClusterAppearsMovesAndEnds()
        {
            var sched = new StreamingScheduler(new StreamingConfig(), new DefaultTileCostModel());
            var rec = new RecordingClusterBuilder();
            var host = new StreamingHostCore(sched, new SyntheticTileReader(), rec, new InlineWorkRunner(), new StreamingHostOptions { MainThreadSliceMs = 1e9 });
            var both = new[]
            {
                new FocusSample { Id = 1, Kind = FocusKind.Body, X = 10000, Y = 20, Z = 10000, NeedsPhysics = true },
                new FocusSample { Id = 2, Kind = FocusKind.Body, X = 40000, Y = 20, Z = 40000, NeedsPhysics = true },
            };
            for (int i = 0; i < 3; i++) host.Tick(1.0, both);
            Assert.AreEqual(2, rec.Origins.Count);
            host.Tick(1.0, new[] { both[0] });
            Assert.AreEqual(1, rec.Removed.Count);
            Assert.AreEqual(1, rec.Origins.Count);
        }

        sealed class RecordingClusterBuilder : ITileBuilder, IClusterBuilder
        {
            public readonly Dictionary<int, (double, double)> Origins = new Dictionary<int, (double, double)>();
            public readonly List<int> Removed = new List<int>();
            public void AssignCluster(in ResidencyKey key, int clusterId) { }
            public void ShiftClusterOrigin(int clusterId, double ox, double oz) { Origins[clusterId] = (ox, oz); }
            public void RemoveCluster(int clusterId) { Origins.Remove(clusterId); Removed.Add(clusterId); }
            public void Build(in ResidencyKey key, TilePayload payload, IBuildSink sink) { sink.Built(key, payload.GpuBytes, payload.CpuBytes); }
            public void Release(in ResidencyKey key) { }
            public void ShiftOrigin(double ox, double oz) { }
        }
    }
}
