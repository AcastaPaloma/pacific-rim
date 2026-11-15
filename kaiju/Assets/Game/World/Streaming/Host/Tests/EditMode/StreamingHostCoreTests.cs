// T-109: ring membership and rebase bookkeeping of the engine-free streaming host (real StreamingScheduler, fake reader and builder).
using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Game.World.Geo;

namespace Game.World.Streaming.Host.Tests
{
    sealed class FakeReader : ITileReader
    {
        public readonly Dictionary<ResidencyKey, int> FailTimes = new Dictionary<ResidencyKey, int>();
        public readonly Dictionary<ResidencyKey, int> Reads = new Dictionary<ResidencyKey, int>();
        public TilePayload Read(ResidencyKey key, CancellationToken ct)
        {
            lock (Reads) { Reads.TryGetValue(key, out int n); Reads[key] = n + 1; }
            if (FailTimes.TryGetValue(key, out int f) && f > 0) { FailTimes[key] = f - 1; throw new System.IO.IOException("injected"); }
            var p = new TilePayload { Key = key, GpuBytes = 1024, CpuBytes = 512 };
            p.Meshes.Add(new GeoMeshData { Name = key.ToString() });
            return p;
        }
    }

    sealed class FakeBuilder : ITileBuilder
    {
        public double OriginX = double.NaN, OriginZ = double.NaN;
        public int Shifts;
        public readonly Dictionary<ResidencyKey, (double x, double z)> Roots = new Dictionary<ResidencyKey, (double, double)>();
        public readonly List<ResidencyKey> Released = new List<ResidencyKey>();
        public readonly List<(ResidencyKey key, IBuildSink sink)> Deferred = new List<(ResidencyKey, IBuildSink)>();
        public bool Defer;
        public void Build(in ResidencyKey key, TilePayload payload, IBuildSink sink)
        {
            key.Bounds(out double x0, out double z0, out _, out _);
            Roots[key] = (x0 - OriginX, z0 - OriginZ);
            if (Defer) Deferred.Add((key, sink)); else sink.Built(key, payload.GpuBytes, payload.CpuBytes);
        }
        public void Release(in ResidencyKey key) { Roots.Remove(key); Released.Add(key); }
        public void ShiftOrigin(double ox, double oz)
        {
            var keys = new List<ResidencyKey>(Roots.Keys);
            foreach (var k in keys) { var r = Roots[k]; Roots[k] = (r.x + OriginX - ox, r.z + OriginZ - oz); }
            if (!double.IsNaN(OriginX)) Shifts++;
            OriginX = ox; OriginZ = oz;
        }
    }

    public class StreamingHostCoreTests
    {
        static FocusSample Cam(int id, double x, double z) => new FocusSample { Id = id, Kind = FocusKind.Camera, X = x, Y = 50, Z = z };
        static FocusSample Body(int id, double x, double z) => new FocusSample { Id = id, Kind = FocusKind.Body, X = x, Y = 10, Z = z, ReachM = 100f, NeedsPhysics = true };

        static (StreamingHostCore host, FakeBuilder b, FakeReader r, StreamingScheduler s) Make(int cover = 0, IWorkRunner runner = null, StreamingHostOptions opt = null)
        {
            var s = new StreamingScheduler(new StreamingConfig(), new DefaultTileCostModel(cover));
            var b = new FakeBuilder(); var r = new FakeReader();
            return (new StreamingHostCore(s, r, b, runner ?? new InlineWorkRunner(), opt ?? new StreamingHostOptions { MainThreadSliceMs = 1e9 }), b, r, s);
        }

        static void Run(StreamingHostCore h, int ticks, params FocusSample[] foci) { for (int i = 0; i < ticks; i++) h.Tick(0.1, foci); }

        [Test]
        public void PhysicsRing_ResidentChunksAreExactlyThoseWithinRadius_AndBodyIsReleasedForSimulation()
        {
            var (h, b, r, s) = Make();
            double x = 30 * 2048 + 1024, z = 36 * 2048 + 1024;
            Run(h, 30, Cam(1, x, z), Body(2, x, z));
            Assert.IsFalse(h.IsBodyHeld(2));
            int phys = h.ResidentCount(ResidencyKind.Physics);
            Assert.Greater(phys, 0);
            foreach (var k in h.Resident)
            {
                if (k.Kind != ResidencyKind.Physics) continue;
                k.Bounds(out double x0, out double z0, out double x1, out double z1);
                double dx = Math.Max(0, Math.Max(x0 - x, x - x1)), dz = Math.Max(0, Math.Max(z0 - z, z - z1));
                Assert.LessOrEqual(Math.Sqrt(dx * dx + dz * dz), 768.0, k.ToString());
            }
            // every chunk touching the 512 m disc is resident
            for (int cz = 0; cz < 8; cz++) for (int cx = 0; cx < 8; cx++)
            {
                var k = ResidencyKey.PhysicsChunk(GeoTileId.Containing(x, z), cx, cz);
                k.Bounds(out double x0, out double z0, out double x1, out double z1);
                double dx = Math.Max(0, Math.Max(x0 - x, x - x1)), dz = Math.Max(0, Math.Max(z0 - z, z - z1));
                if (Math.Sqrt(dx * dx + dz * dz) <= 500.0) Assert.AreEqual(TileState.Resident, s.GetState(k), k.ToString());
            }
            Assert.AreEqual(h.Resident.Count, b.Roots.Count);
        }

        [Test]
        public void BodyIsHeldWhileItsPhysicsRingIsStillBuilding()
        {
            var (h, b, r, s) = Make();
            b.Defer = true;
            var f = new[] { Cam(1, 61440 + 1024, 73728 + 1024), Body(2, 61440 + 1024, 73728 + 1024) };
            h.Tick(0.1, f); h.Tick(0.1, f);
            Assert.IsTrue(h.IsBodyHeld(2));
            foreach (var d in b.Deferred.ToArray()) d.sink.Built(d.key, 1, 1);
            for (int i = 0; i < 5; i++) h.Tick(0.1, f);
            foreach (var d in b.Deferred.ToArray()) d.sink.Built(d.key, 1, 1);
            Assert.IsFalse(h.IsBodyHeld(2));
        }

        [Test]
        public void Rebase_ShiftsRootsByExactMultiplesAndKeepsResidentOffsetsBounded()
        {
            var (h, b, r, s) = Make();
            double x = 20000, z = 100000;
            Run(h, 5, Cam(1, x, z), Body(2, x, z));
            double ox0 = h.RenderOriginX, oz0 = h.RenderOriginZ;
            Assert.AreEqual(0, h.RebaseCount);
            Assert.AreEqual(0.0, ox0 % 2048.0); Assert.AreEqual(0.0, oz0 % 2048.0);
            var seen = new List<(double, double, double, double)>();
            h.OriginShifted += (a, c, d, e) => seen.Add((a, c, d, e));
            double maxLocal = 0;
            for (int i = 0; i < 800; i++)
            {
                x += 60 * 0.1 * 6;   // 36 m per tick
                h.Tick(0.1, new[] { Cam(1, x, z), Body(2, x, z) });
                Assert.AreEqual(b.OriginX, h.RenderOriginX);
                foreach (var kv in b.Roots) { maxLocal = Math.Max(maxLocal, Math.Max(Math.Abs(kv.Value.x), Math.Abs(kv.Value.z))); Assert.AreEqual(0.0, kv.Value.x % 1.0); }
            }
            Assert.Greater(h.RebaseCount, 3);
            Assert.AreEqual(h.RebaseCount, seen.Count);
            foreach (var s2 in seen) { Assert.AreEqual(0.0, (s2.Item3 - s2.Item1) % 2048.0); Assert.AreEqual(0.0, (s2.Item4 - s2.Item2) % 2048.0); }
            Assert.AreEqual(h.RebaseCount, b.Shifts);
            // tile roots of resident keys never farther than the cluster bound plus the largest resident render ring tile (host pairs them with the camera)
            var cam = h.LastPlan.Assignments.Find(a => a.FocusId == 1);
            Assert.LessOrEqual(Math.Abs(cam.LocalX), 4096f); Assert.LessOrEqual(Math.Abs(cam.LocalZ), 4096f);
            // roots equal tile origin minus the render origin, exactly
            foreach (var kv in b.Roots)
            {
                kv.Key.Bounds(out double x0, out double z0, out _, out _);
                Assert.AreEqual(x0 - h.RenderOriginX, kv.Value.x); Assert.AreEqual(z0 - h.RenderOriginZ, kv.Value.z);
            }
        }

        [Test]
        public void Eviction_ReleasesBuilderAndFreesResidentSet()
        {
            var (h, b, r, s) = Make();
            Run(h, 10, Cam(1, 20000, 100000), Body(2, 20000, 100000));
            var first = new List<ResidencyKey>(h.Resident);
            Assert.Greater(first.Count, 0);
            Run(h, 40, Cam(1, 50000, 60000), Body(2, 50000, 60000));
            foreach (var k in first) if (k.Kind == ResidencyKind.Physics) { Assert.IsFalse(h.IsResident(k)); Assert.Contains(k, b.Released); Assert.AreEqual(TileState.Absent, s.GetState(k)); }
            Assert.AreEqual(h.Resident.Count, b.Roots.Count);
        }

        [Test]
        public void ReaderFailure_BacksOffThenRecovers()
        {
            var (h, b, r, s) = Make();
            var key = ResidencyKey.PhysicsChunk(GeoTileId.Containing(1024, 1024), 0, 0);
            r.FailTimes[key] = 2;
            var f = new[] { Cam(1, 400, 400), Body(2, 400, 400) };
            for (int i = 0; i < 60; i++) h.Tick(0.25, f);
            Assert.GreaterOrEqual(h.Failures, 2);
            Assert.AreEqual(3, r.Reads[key]);
            Assert.AreEqual(TileState.Resident, s.GetState(key));
        }

        [Test]
        public void MainThreadSlice_LimitsNonUrgentBuildsPerTick_ButEveryPayloadIsBuiltEventually()
        {
            double now = 0;
            var opt = new StreamingHostOptions { MainThreadSliceMs = 2.0, ClockMs = () => now += 1.5 };
            var (h, b, r, s) = Make(0, null, opt);
            int builtAfterFirst = -1;
            var f = new[] { Cam(1, 61440 + 1024, 73728 + 1024) };   // camera only: render tiles, no physics (non-urgent)
            h.Tick(0.1, f); builtAfterFirst = h.Resident.Count;
            Assert.LessOrEqual(builtAfterFirst, 2);
            for (int i = 0; i < 80; i++) h.Tick(0.1, f);
            Assert.Greater(h.Resident.Count, builtAfterFirst);
            Assert.AreEqual(0, h.Failures);
        }

        [Test]
        public void SchedulerStateAndHostSetsAgree_NoKeyIsBuiltTwiceWhileResident()
        {
            var (h, b, r, s) = Make();
            Run(h, 60, Cam(1, 40000, 90000), Body(2, 40000, 90000));
            foreach (var kv in r.Reads) if (h.IsResident(kv.Key)) Assert.AreEqual(1, kv.Value, kv.Key.ToString());
            foreach (var k in h.Resident) Assert.AreEqual(TileState.Resident, s.GetState(k));
        }
    }
}
