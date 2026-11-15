// destruction@v1 golden file, one test-only section migration (v1 -> v2), stream failure and 20,000-id scale (T-025 step 2, M-050).
// Engine-free. The shipped section stays at version 1; the v2 here exists only to prove the migration path end to end.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Game.Core;
using NUnit.Framework;

namespace Game.World.Destruction.Tests
{
    public class DestructionSaveMigrationTests
    {
        const string GoldenName = "destruction_v1.json";
        const string AddedField = "armor";   // test-only v2 field, default 0

        static string GoldenPath([CallerFilePath] string here = "")
        {
            return Path.Combine(Path.GetDirectoryName(here), "Golden", GoldenName);
        }

        // Test-only participant at section version 2: same data as v1 plus one defaulted field on every building record.
        sealed class V2Participant : ISaveParticipant
        {
            readonly DestructionSaveParticipant _inner;
            public V2Participant(DamageModel m) { _inner = new DestructionSaveParticipant(m); }
            public string SectionName => DestructionSaveParticipant.Section;
            public int SectionVersion => 2;
            public void Write(SaveSection section)
            {
                _inner.Write(section);
                foreach (var r in section.Records) if (r.Key != DestructionSaveParticipant.MetaKey) r.Set(AddedField, SaveValue.Of(0L));
            }
            public void Read(SaveSection section)
            {
                foreach (var r in section.Records)
                {
                    if (r.Key == DestructionSaveParticipant.MetaKey) continue;
                    if (!r.TryGet(AddedField, out var v) || v.Kind != SaveValueKind.Int64 || v.AsInt64 != 0)
                        throw new FormatException("v2 record " + r.Key + " lacks defaulted '" + AddedField + "'");
                }
                _inner.Read(section);
            }
        }

        sealed class V1ToV2 : ISaveSectionMigration
        {
            public string SectionName => DestructionSaveParticipant.Section;
            public int FromVersion => 1;
            public void Migrate(SaveSection section)
            {
                foreach (var r in section.Records)
                    if (r.Key != DestructionSaveParticipant.MetaKey && !r.TryGet(AddedField, out _)) r.Set(AddedField, SaveValue.Of(0L));
                section.Version = 2;
            }
        }

        static SaveSystem SysV2(DamageModel m)
        {
            var sys = new SaveSystem("test");
            sys.Register(new V2Participant(m));
            sys.Register(new V1ToV2());
            return sys;
        }

        static DamageModel Registered(int n)
        {
            var m = new DamageModel();
            for (int i = 0; i < n; i++) m.Register(DestructionSaveTests.Id(i), DestructionSaveTests.Attr(i));
            return m;
        }

        static Dictionary<string, (long state, long energyBits)> Snapshot(DamageModel m)
        {
            var d = new Dictionary<string, (long, long)>();
            foreach (var s in DestructionSaveTests.All(m)) d[s.Id.ToString()] = (s.State, BitConverter.DoubleToInt64Bits(s.CumulativeEnergyJ));
            return d;
        }

        // The ground truth read straight from the section records, independent of DamageModel.
        static Dictionary<string, (long state, long energyBits)> FromSection(SaveSection sec)
        {
            var d = new Dictionary<string, (long, long)>();
            foreach (var r in sec.Records)
                if (r.Key != DestructionSaveParticipant.MetaKey)
                    d[r.Key] = (r.Get("state").AsInt64, r.Get("e_cum_j").Float64Bits);
            return d;
        }

        static void AssertSame(Dictionary<string, (long state, long energyBits)> expect, Dictionary<string, (long state, long energyBits)> got)
        {
            Assert.AreEqual(expect.Count, got.Count);
            foreach (var kv in expect)
            {
                Assert.IsTrue(got.TryGetValue(kv.Key, out var g), kv.Key);
                Assert.AreEqual(kv.Value.state, g.state, "state " + kv.Key);
                Assert.AreEqual(kv.Value.energyBits, g.energyBits, "e_cum_j bits " + kv.Key);
            }
        }

        [Test]
        public void Golden_V1_File_IsCurrentOrWritten()
        {
            string path = GoldenPath();
            string current = DestructionSaveTests.Bytes(DestructionSaveTests.Build(300, 31));
            if (Environment.GetEnvironmentVariable("KAIJU_WRITE_GOLDEN") == "1")
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, current, new UTF8Encoding(false));
                Assert.Pass("golden written: " + path);
            }
            Assert.IsTrue(File.Exists(path), "golden missing; run once with KAIJU_WRITE_GOLDEN=1");
            var r = SaveCodec.Deserialize(File.ReadAllText(path, Encoding.UTF8), path);
            Assert.IsTrue(r.Ok, r.Message);
            Assert.IsTrue(r.Document.TryGetSection("destruction", out var sec));
            Assert.AreEqual(1, sec.Version);
            // The shipped writer still produces the golden bytes (any format drift is a deliberate, versioned change).
            Assert.AreEqual(File.ReadAllText(path, Encoding.UTF8), current);
        }

        [Test]
        public void Golden_V1_LoadsAtV1_ByteIdenticalResave()
        {
            string text = File.ReadAllText(GoldenPath(), Encoding.UTF8);
            var m = Registered(300);
            var sys = new SaveSystem("test");
            sys.Register(new DestructionSaveParticipant(m));
            Assert.IsTrue(sys.Restore(DestructionSaveTests.RoundTrip(text)).Ok);
            Assert.AreEqual(text, SaveCodec.Serialize(sys.Capture()));
        }

        [Test]
        public void Migration_V1_To_V2_KeepsStateAndEnergyBitExact_ThenResaveAndReload()
        {
            string text = File.ReadAllText(GoldenPath(), Encoding.UTF8);
            var doc = DestructionSaveTests.RoundTrip(text);
            doc.TryGetSection("destruction", out var v1);
            var truth = FromSection(v1);
            Assert.Greater(truth.Count, 50);

            var m = Registered(300);
            var sys = SysV2(m);
            var res = sys.Restore(doc);
            Assert.IsTrue(res.Ok, res.Message);
            AssertSame(truth, Snapshot(m));

            // Re-save as v2.
            var saved = sys.Capture();
            Assert.IsTrue(saved.TryGetSection("destruction", out var v2));
            Assert.AreEqual(2, v2.Version);
            foreach (var r in v2.Records)
                if (r.Key != DestructionSaveParticipant.MetaKey) Assert.AreEqual(0L, r.Get(AddedField).AsInt64);
            string v2Text = SaveCodec.Serialize(saved);
            Assert.AreNotEqual(text, v2Text);

            // Reload v2 into a fresh model (no migration needed any more).
            var m2 = Registered(300);
            var res2 = SysV2(m2).Restore(DestructionSaveTests.RoundTrip(v2Text));
            Assert.IsTrue(res2.Ok, res2.Message);
            AssertSame(truth, Snapshot(m2));
            DestructionSaveTests.AssertSameBits(DestructionSaveTests.All(m), DestructionSaveTests.All(m2));
            Assert.AreEqual(v2Text, SaveCodec.Serialize(SysV2(m2).Capture()));
        }

        [Test]
        public void Migration_Missing_IsRefusedWithoutTouchingTheModel()
        {
            var doc = DestructionSaveTests.RoundTrip(File.ReadAllText(GoldenPath(), Encoding.UTF8));
            var m = DestructionSaveTests.Build(30, 32);
            var before = Snapshot(m);
            var sys = new SaveSystem("test");
            sys.Register(new V2Participant(m)); // no migration registered
            Assert.AreEqual(SaveLoadStatus.MissingMigration, sys.Restore(doc).Status);
            AssertSame(before, Snapshot(m));
        }

        sealed class TruncatingStream : Stream
        {
            readonly Stream _inner; readonly int _limit; int _written;
            public TruncatingStream(Stream inner, int limit) { _inner = inner; _limit = limit; }
            public override void Write(byte[] buffer, int offset, int count)
            {
                int room = Math.Max(0, _limit - _written);
                int n = Math.Min(room, count);
                if (n > 0) { _inner.Write(buffer, offset, n); _written += n; }
                if (n < count) { _inner.Flush(); throw new IOException("simulated disk failure after " + _written + " bytes"); }
            }
            public override void Flush() { _inner.Flush(); }
            protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override int Read(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
        }

        static string TempDir()
        {
            string d = Path.Combine(Path.GetTempPath(), "kaiju-destr-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        [Test]
        public void StreamFailure_TruncatedWrite_PreviousSaveStaysLoadable()
        {
            string dir = TempDir();
            try
            {
                string file = Path.Combine(dir, "save.json");
                var a = DestructionSaveTests.Build(500, 33);
                var good = new JsonFileSaveStore(file);
                var sysA = new SaveSystem("test");
                sysA.Register(new DestructionSaveParticipant(a));
                good.Save(sysA.Capture());
                string before = File.ReadAllText(file, Encoding.UTF8);

                // More damage, then a save whose stream fails halfway through.
                var b = DestructionSaveTests.Build(500, 34);
                var sysB = new SaveSystem("test");
                sysB.Register(new DestructionSaveParticipant(b));
                int cut = before.Length / 2;
                var bad = new JsonFileSaveStore(file, p => new TruncatingStream(new FileStream(p, FileMode.Create, FileAccess.Write, FileShare.None), cut));
                Assert.Throws<SaveWriteException>(() => bad.Save(sysB.Capture()));

                Assert.AreEqual(before, File.ReadAllText(file, Encoding.UTF8), "previous save file untouched");
                Assert.IsFalse(File.Exists(bad.TempPath), "partial temp file removed");
                var r = good.Load();
                Assert.IsTrue(r.Ok, r.Message);
                var c = DestructionSaveTests.Reload(before, 500, out _);
                var sysC = new SaveSystem("test");
                sysC.Register(new DestructionSaveParticipant(c));
                Assert.IsTrue(sysC.Restore(r.Document).Ok);
                DestructionSaveTests.AssertSameBits(DestructionSaveTests.All(a), DestructionSaveTests.All(c));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Test]
        public void StreamFailure_OnFirstSave_LeavesNoSaveFile()
        {
            string dir = TempDir();
            try
            {
                string file = Path.Combine(dir, "save.json");
                var m = DestructionSaveTests.Build(100, 35);
                var sys = new SaveSystem("test");
                sys.Register(new DestructionSaveParticipant(m));
                var bad = new JsonFileSaveStore(file, p => new TruncatingStream(new FileStream(p, FileMode.Create, FileAccess.Write, FileShare.None), 40));
                Assert.Throws<SaveWriteException>(() => bad.Save(sys.Capture()));
                Assert.IsFalse(File.Exists(file));
                Assert.IsFalse(File.Exists(bad.TempPath));
                Assert.AreEqual(SaveLoadStatus.Missing, bad.Load().Status);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Test]
        public void Scale_20000Buildings_SaveLoadBitExact_Timed()
        {
            const int N = 20000;
            string dir = TempDir();
            try
            {
                var a = DestructionSaveTests.Build(N, 41);
                var expect = DestructionSaveTests.All(a);
                Assert.Greater(expect.Count, N / 3);
                var store = new JsonFileSaveStore(Path.Combine(dir, "big.json"));
                var sysA = new SaveSystem("test");
                sysA.Register(new DestructionSaveParticipant(a));

                var sw = Stopwatch.StartNew();
                store.Save(sysA.Capture());
                long saveMs = sw.ElapsedMilliseconds;

                var b = Registered(N);
                var sysB = new SaveSystem("test");
                sysB.Register(new DestructionSaveParticipant(b));
                sw.Restart();
                var r = store.Load();
                Assert.IsTrue(r.Ok, r.Message);
                var rr = sysB.Restore(r.Document);
                long loadMs = sw.ElapsedMilliseconds;
                Assert.IsTrue(rr.Ok, rr.Message);

                long bytes = new FileInfo(store.Path).Length;
                TestContext.Out.WriteLine("SCALE20K damaged=" + expect.Count + " file_bytes=" + bytes + " save_ms=" + saveMs + " load_ms=" + loadMs);
                DestructionSaveTests.AssertSameBits(expect, DestructionSaveTests.All(b));
                Assert.Less(saveMs + loadMs, 10000, "20,000-id save+load budget");
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
