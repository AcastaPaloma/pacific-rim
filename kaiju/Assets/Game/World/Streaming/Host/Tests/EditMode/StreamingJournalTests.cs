// T-110: destruction persistence across evict/reload. Engine-free; the reload tests use the committed M1 tile L0_30_36 through the real GeoL0TileReader.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Game.Core;
using Game.World.Geo;
using NUnit.Framework;

namespace Game.World.Streaming.Host.Tests
{
    sealed class JErrors : ICoreErrorSink { public void Report(CoreError error) { } }

    /// <summary>Builder that records what the journal looked like at the moment each key was released.</summary>
    sealed class ReleaseProbeBuilder : ITileBuilder
    {
        public StreamingJournal Journal;
        public readonly List<(ResidencyKey key, bool dirtyAtRelease)> Releases = new List<(ResidencyKey, bool)>();
        public void Build(in ResidencyKey key, TilePayload payload, IBuildSink sink) { sink.Built(key, payload.GpuBytes, payload.CpuBytes); }
        public void Release(in ResidencyKey key) { Releases.Add((key, Journal.IsDirty(key.Tile))); }
        public void ShiftOrigin(double ox, double oz) { }
    }

    public class StreamingJournalTests
    {
        static BuildingId Bld(ulong n) => BuildingId.From(BuildingSource.OsmWay, n);
        static readonly GeoTileId T = GeoTileId.From(0, 30, 36);

        static string ProjectRoot()
        {
            var d = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (d != null)
            {
                if (Directory.Exists(Path.Combine(d.FullName, "Assets", "StreamingAssets", "Geo"))) return d.FullName;
                var k = Path.Combine(d.FullName, "kaiju");
                if (Directory.Exists(Path.Combine(k, "Assets", "StreamingAssets", "Geo"))) return k;
                d = d.Parent;
            }
            throw new DirectoryNotFoundException("project root not found");
        }
        static string TilesRoot => Path.Combine(ProjectRoot(), "Assets", "StreamingAssets", "Geo", "tiles", "v1");
        static IReadOnlyDictionary<string, double> Settings() => GeoSettings.Load(Path.Combine(ProjectRoot(), "Assets", "Game", "Config", "WorldGeo.json"));

        // ---- terrain ops ----

        [Test]
        public void TerrainOps_AreDeterministicOrderedAndSeamConsistent()
        {
            // two neighbouring 2048 m tiles on a 33-sample lattice (spacing 64 m); a crater straddles the border x = 2048
            const int n = 33; const double sp = 64.0;
            var op = new TerrainOp(2048 - 30, 1000, 150, 20, 7);
            float[] A() { var h = new float[n * n]; for (int i = 0; i < h.Length; i++) h[i] = 100f; return h; }
            var a = A(); var b = A(); var a2 = A();
            int ca = TerrainOps.Apply(a, n, sp, 0, 0, new[] { op });
            TerrainOps.Apply(b, n, sp, 2048, 0, new[] { op });
            TerrainOps.Apply(a2, n, sp, 0, 0, new[] { op });
            Assert.Greater(ca, 0);
            CollectionAssert.AreEqual(a, a2);
            for (int r = 0; r < n; r++) Assert.AreEqual(a[r * n + (n - 1)], b[r * n], 0f, "border row " + r);   // shared border node: same world position, same height
            Assert.Less(a.Min(), 100f - 15f);
            Assert.AreEqual(100f, a[0], 0f);   // far corner untouched
            // order matters only through float subtraction; two ops in a fixed order replay identically
            var op2 = new TerrainOp(500, 500, 100, 5, 9);
            var x = A(); var y = A();
            TerrainOps.Apply(x, n, sp, 0, 0, new[] { op, op2 }); TerrainOps.Apply(y, n, sp, 0, 0, new[] { op, op2 });
            CollectionAssert.AreEqual(x, y);
        }

        [Test]
        public void TerrainOp_AffectsLevelFollowsErrorLadder()
        {
            var op = new TerrainOp(0, 0, 50, 0.5, 1);
            Assert.IsTrue(op.AffectsLevel(0)); Assert.IsTrue(op.AffectsLevel(2));    // err_max 0.4
            Assert.IsFalse(op.AffectsLevel(3));                                      // err_max 0.8
        }

        // ---- journal ----

        [Test]
        public void Journal_RecordsIdempotentlyAndSnapshotsAreImmutable()
        {
            var j = new StreamingJournal();
            Assert.IsTrue(j.RecordRemoval(T, Bld(5)));
            Assert.IsFalse(j.RecordRemoval(T, Bld(5)));
            var s1 = j.Snapshot(T);
            j.RecordRemoval(T, Bld(2));
            var s2 = j.Snapshot(T);
            Assert.AreEqual(1, s1.Removed.Count);
            Assert.AreEqual(2, s2.Removed.Count);
            Assert.IsTrue(j.Snapshot(GeoTileId.From(0, 1, 1)).IsEmpty);
            Assert.Throws<ArgumentException>(() => j.RecordRemoval(GeoTileId.From(1, 0, 0), Bld(1)));
        }

        [Test]
        public void Journal_CraterIsRecordedInEveryOverlappedTile()
        {
            var j = new StreamingJournal();
            int n = j.RecordTerrainOp(new TerrainOp(2048 * 3, 2048 * 3, 100, 10, 1));   // on a tile corner: four tiles
            Assert.AreEqual(4, n);
            Assert.AreEqual(1, j.Snapshot(GeoTileId.From(0, 2, 2)).Ops.Count);
            Assert.AreEqual(1, j.Snapshot(GeoTileId.From(0, 3, 3)).Ops.Count);
            Assert.AreEqual(0, j.Snapshot(GeoTileId.From(0, 4, 4)).Ops.Count);
        }

        [Test]
        public void Codec_RoundTripsExactlyAndRejectsBadInput()
        {
            var ids = new List<BuildingId> { Bld(9), Bld(3), BuildingId.From(BuildingSource.Landmark, 4) };
            var ops = new[] { new TerrainOp(1234.5678901234567, -0.0000001 + 99, 12.34, 0.0625, 4000000000u), new TerrainOp(1, 2, 3, 4, 5) };
            var text = JournalCodec.Encode(ids, ops);
            JournalCodec.Decode(text, out var r2, out var o2);
            CollectionAssert.AreEquivalent(ids, r2);
            CollectionAssert.AreEqual(ops, o2);
            Assert.AreEqual(text, JournalCodec.Encode(r2, o2));   // canonical: sorted ids
            Assert.Throws<FormatException>(() => JournalCodec.Decode("journal@2\nremoved:\n", out _, out _));
            Assert.Throws<FormatException>(() => JournalCodec.Decode("journal@1\nremoved:bld:osmw:x\n", out _, out _));
            Assert.Throws<FormatException>(() => JournalCodec.Decode("journal@1\nremoved:\nop:1,2,3,4,5\n", out _, out _));
            Assert.Throws<FormatException>(() => JournalCodec.Decode("journal@1\n", out _, out _));
        }

        [Test]
        public void Flush_WritesOnlyDirtyTilesAndReloadsFromStore()
        {
            var store = new MemoryJournalStore();
            var j = new StreamingJournal(store);
            j.RecordRemoval(T, Bld(1)); j.RecordTerrainOp(new TerrainOp(T.OriginX + 1000, T.OriginZ + 1000, 40, 3, 11));
            Assert.IsTrue(j.IsDirty(T));
            Assert.IsTrue(j.Flush(T));
            Assert.IsFalse(j.IsDirty(T));
            Assert.IsFalse(j.Flush(T));                 // nothing new: no second write
            Assert.AreEqual(1, store.Writes);
            var j2 = new StreamingJournal(store);
            Assert.AreEqual(1, j2.LoadFromStore());
            var a = j.Snapshot(T); var b = j2.Snapshot(T);
            CollectionAssert.AreEquivalent(a.Removed, b.Removed);
            CollectionAssert.AreEqual(a.Ops, b.Ops);
            Assert.AreEqual(a.OpsKey, b.OpsKey);
        }

        [Test]
        public void FileStore_RoundTripsAndSurvivesRewrite()
        {
            var dir = Path.Combine(Path.GetTempPath(), "kaiju_journal_" + Guid.NewGuid().ToString("N"));
            try
            {
                var fs = new FileJournalStore(dir);
                var j = new StreamingJournal(fs);
                j.RecordRemoval(T, Bld(1)); j.Flush(T);
                j.RecordRemoval(T, Bld(2)); j.Flush(T);   // overwrite
                Assert.AreEqual(0, Directory.GetFiles(dir, "*.tmp").Length);
                var j2 = new StreamingJournal(new FileJournalStore(dir));
                j2.LoadFromStore();
                Assert.AreEqual(2, j2.Snapshot(T).Removed.Count);
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        [Test]
        public void SaveSection_RoundTripsAndValidatesBeforeChanging()
        {
            var j = new StreamingJournal();
            j.RecordRemoval(T, Bld(8)); j.RecordRemoval(GeoTileId.From(0, 2, 3), Bld(1)); j.RecordTerrainOp(new TerrainOp(T.OriginX + 5, T.OriginZ + 5, 30, 2, 3));
            var part = new StreamingSaveParticipant(j);
            Assert.AreEqual("streaming", part.SectionName); Assert.AreEqual(1, part.SectionVersion);
            var sec = new SaveSection(part.SectionName, part.SectionVersion);
            part.Write(sec);
            var j2 = new StreamingJournal(); j2.RecordRemoval(GeoTileId.From(0, 9, 9), Bld(99));   // replaced by Read
            new StreamingSaveParticipant(j2).Read(sec);
            Assert.AreEqual(j.RemovedCount, j2.RemovedCount);
            Assert.IsTrue(j2.Snapshot(GeoTileId.From(0, 9, 9)).IsEmpty);
            CollectionAssert.AreEqual(j.Snapshot(T).Ops, j2.Snapshot(T).Ops);
            var sec2 = new SaveSection("streaming", 1); sec2.Add("L0_01_01").Set("journal", SaveValue.Of("junk"));
            var keep = new StreamingJournal(); keep.RecordRemoval(T, Bld(1));
            Assert.Throws<FormatException>(() => new StreamingSaveParticipant(keep).Read(sec2));
            Assert.IsTrue(keep.IsRemoved(T, Bld(1)));
        }

        // ---- flush on TileEvicted through the real scheduler and host ----

        [Test]
        public void Eviction_FlushesTheJournalBeforeTheBuilderReleases()
        {
            var bus = new EventBus(new JErrors(), 100000);
            var store = new MemoryJournalStore();
            var j = new StreamingJournal(store);
            j.Attach(bus);
            var sched = new StreamingScheduler(new StreamingConfig(), new DefaultTileCostModel(0), bus);
            var b = new ReleaseProbeBuilder { Journal = j };
            var host = new StreamingHostCore(sched, new FakeReader(), b, new InlineWorkRunner(), new StreamingHostOptions { MainThreadSliceMs = 1e9 });
            var home = GeoTileId.Containing(20000, 100000);
            j.RecordRemoval(home, Bld(77));          // recorded while resident or not: the journal does not care
            var near = new[] { new FocusSample { Id = 1, Kind = FocusKind.Camera, X = 20000, Y = 50, Z = 100000 }, new FocusSample { Id = 2, Kind = FocusKind.Body, X = 20000, Y = 10, Z = 100000, ReachM = 100f, NeedsPhysics = true } };
            for (int i = 0; i < 10; i++) host.Tick(0.1, near);
            Assert.IsTrue(host.Resident.Any(k => k.Tile.Equals(home)), "home tile resident");
            Assert.AreEqual(0, store.Writes);
            var far = new[] { new FocusSample { Id = 1, Kind = FocusKind.Camera, X = 50000, Y = 50, Z = 60000 }, new FocusSample { Id = 2, Kind = FocusKind.Body, X = 50000, Y = 10, Z = 60000, ReachM = 100f, NeedsPhysics = true } };
            for (int i = 0; i < 40; i++) host.Tick(0.1, far);
            Assert.IsTrue(b.Releases.Any(r => r.key.Tile.Equals(home)), "home tile released");
            Assert.AreEqual(1, store.Writes, "flushed once");
            foreach (var r in b.Releases.Where(r => r.key.Tile.Equals(home))) Assert.IsFalse(r.dirtyAtRelease, "journal clean when " + r.key + " was released");
            Assert.IsTrue(store.TryRead(home, out var payload)); StringAssert.Contains(Bld(77).ToString(), payload);
        }

        [Test]
        public void BusRemovalEvent_IsRecordedInTheLocatedTile()
        {
            var bus = new EventBus(new JErrors(), 1000);
            var j = new StreamingJournal();
            j.Attach(bus, id => id.SourceId == 1 ? T : (GeoTileId?)null);
            bus.Publish(new GeoBuildingRemoved { Building = Bld(1), Structure = Bld(1) });
            bus.Publish(new GeoBuildingRemoved { Building = Bld(2), Structure = Bld(2) });
            Assert.IsTrue(j.IsRemoved(T, Bld(1)));
            Assert.AreEqual(1, j.UnlocatedRemovals);
            j.Detach();
            bus.Publish(new GeoBuildingRemoved { Building = Bld(1), Structure = Bld(1) });
            Assert.AreEqual(1, j.UnlocatedRemovals);
        }

        // ---- reload through the real L0 reader ----

        static long Tris(TilePayload p, string layer) { long n = 0; foreach (var m in p.Meshes) if (m.Layer == layer) n += m.TriangleCount; return n; }

        [Test]
        public void Reload_OmitsRemovedBuildingsBeforeMeshing_RenderAndPhysics()
        {
            var j = new StreamingJournal();
            var reader = new GeoL0TileReader(TilesRoot, Settings(), 3, j);
            var key = ResidencyKey.Render(T);
            var before = reader.Read(key, CancellationToken.None);
            long trisBefore = Tris(before, "buildings");
            Assert.Greater(trisBefore, 0);
            // pick the building with the most triangles of the first populated chunk
            var model = new GeoTileModel(new GeoTileContext(GeoTileSetLoader.TileDirectory(TilesRoot, T), Settings(), false));
            var victim = model.Buildings.Where(b => b.Mesh != null).OrderByDescending(b => b.Mesh.Indices.Length).ThenBy(b => b.Id).First();
            j.RecordRemoval(T, victim.Id);
            var after = reader.Read(key, CancellationToken.None);
            Assert.LessOrEqual(Tris(after, "buildings"), trisBefore - victim.Mesh.Indices.Length / 3);   // a structure also hides its parts
            // equals what the live model does on removal (the M1 path)
            model.Remove(victim.Id);
            long expected = 0; for (int cz = 0; cz < model.BuildingChunks; cz++) for (int cx = 0; cx < model.BuildingChunks; cx++) expected += model.CombineChunk(cx, cz).TriangleCount;
            Assert.AreEqual(expected, Tris(after, "buildings"));
            // the physics chunk that held it also drops it
            int span = model.BuildingChunks / ResidencyKey.ChunksPerEdge;
            var pk = ResidencyKey.PhysicsChunk(T, victim.ChunkX / span, victim.ChunkZ / span);
            var pj = new StreamingJournal();
            var preader = new GeoL0TileReader(TilesRoot, Settings(), 3, pj);
            long pBefore = Tris(preader.Read(pk, CancellationToken.None), "buildings");
            pj.RecordRemoval(T, victim.Id);
            Assert.LessOrEqual(Tris(preader.Read(pk, CancellationToken.None), "buildings"), pBefore - victim.Mesh.Indices.Length / 3);
        }

        [Test]
        public void Reload_ReplaysCratersDeterministically_AndAfterAStoreRoundTrip()
        {
            var store = new MemoryJournalStore();
            var j = new StreamingJournal(store);
            var key = ResidencyKey.Render(T);
            var plain = new GeoL0TileReader(TilesRoot, Settings(), 3, j).Read(key, CancellationToken.None);
            var op = new TerrainOp(T.OriginX + 1024, T.OriginZ + 1024, 80, 12, 42);
            j.RecordTerrainOp(op);
            var r1 = new GeoL0TileReader(TilesRoot, Settings(), 3, j).Read(key, CancellationToken.None);
            // mean height of terrain vertices within the crater's inner 50 m (tile-local x, z = 1024, 1024)
            double MeanY(TilePayload p, out int n)
            {
                double s = 0; n = 0;
                foreach (var mesh in p.Meshes) if (mesh.Layer == "terrain")
                    for (int i = 0; i + 2 < mesh.Positions.Length; i += 3)
                    { double dx = mesh.Positions[i] - 1024, dz = mesh.Positions[i + 2] - 1024; if (dx * dx + dz * dz < 2500) { s += mesh.Positions[i + 1]; n++; } }
                return n > 0 ? s / n : double.NaN;
            }
            double m0 = MeanY(plain, out int n0), m1 = MeanY(r1, out int n1);
            Assert.Greater(n0, 0); Assert.Greater(n1, 0);
            Assert.Less(m1, m0 - 3.0, "crater lowers the mesh near its centre");
            j.Flush(T);
            var j2 = new StreamingJournal(store); j2.LoadFromStore();
            var r2 = new GeoL0TileReader(TilesRoot, Settings(), 3, j2).Read(key, CancellationToken.None);
            Assert.AreEqual(r1.Meshes.Count, r2.Meshes.Count);
            for (int i = 0; i < r1.Meshes.Count; i++) { CollectionAssert.AreEqual(r1.Meshes[i].Positions, r2.Meshes[i].Positions, r1.Meshes[i].Name); CollectionAssert.AreEqual(r1.Meshes[i].Indices, r2.Meshes[i].Indices); }
        }

        [Test]
        public void Reader_ModelCacheDropsWhenACraterIsAdded()
        {
            var j = new StreamingJournal();
            var reader = new GeoL0TileReader(TilesRoot, Settings(), 3, j);
            var key = ResidencyKey.Render(T);
            var a = reader.Read(key, CancellationToken.None);
            j.RecordTerrainOp(new TerrainOp(T.OriginX + 700, T.OriginZ + 700, 60, 9, 1));
            var b = reader.Read(key, CancellationToken.None);
            Assert.IsFalse(a.Meshes.Where(m => m.Layer == "terrain").SelectMany(m => m.Positions).SequenceEqual(b.Meshes.Where(m => m.Layer == "terrain").SelectMany(m => m.Positions)));
        }
    }
}
