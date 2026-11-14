using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>Test-only module section, v2. v1 stored "hp" as an integer percentage; v2 stores "health" in [0,1].</summary>
    public sealed class InventoryParticipant : ISaveParticipant
    {
        public readonly Dictionary<string, double> Health = new Dictionary<string, double>();
        public int ReadCalls;
        public int Version = 2;
        public string SectionName => "sample.inventory";
        public int SectionVersion => Version;
        public void Write(SaveSection s) { foreach (var kv in Health) s.Add(kv.Key).Set("health", SaveValue.Of(kv.Value)); }
        public void Read(SaveSection s) { ReadCalls++; foreach (var r in s.Records) Health[r.Key] = r.Get("health").AsFloat64; }
    }

    public sealed class InventoryV1ToV2 : ISaveSectionMigration
    {
        public string SectionName => "sample.inventory";
        public int FromVersion => 1;
        public void Migrate(SaveSection s)
        {
            foreach (var r in s.Records) { r.Set("health", SaveValue.Of(r.Get("hp").AsInt64 / 100.0)); r.Remove("hp"); }
            s.Version = 2;
        }
    }

    public class SaveTests
    {
        private string _dir;
        [SetUp] public void SetUp() { _dir = ProjectPaths.NewTempDir(); }
        [TearDown] public void TearDown() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

        public static SaveDocument BuildGoldenV1()
        {
            var doc = new SaveDocument("0.1.0-dev");
            var inv = doc.AddSection("sample.inventory", 1);
            inv.Add(BuildingId.From(BuildingSource.OsmWay, 4294967297UL).ToString()).Set("hp", SaveValue.Of(75L));
            inv.Add(BuildingId.From(BuildingSource.Landmark, 3).ToString()).Set("hp", SaveValue.Of(100L));
            var misc = doc.AddSection("test.values", 1);
            misc.Add(EntityId.FromValue(1).ToString())
                .Set("pi", SaveValue.Of(Math.PI)).Set("negZero", SaveValue.Of(-0.0)).Set("tiny", SaveValue.Of(double.Epsilon))
                .Set("big", SaveValue.Of(long.MinValue)).Set("on", SaveValue.Of(true)).Set("name", SaveValue.Of("Ünïcode \"quoted\"\n\u0001"));
            misc.Add(PlayerSlot.FromIndex(0).ToString()).Set("ready", SaveValue.Of(false));
            return doc;
        }

        private static string Golden => ProjectPaths.Fixture("save_v1.json");

        [Test]
        public void Save_GoldenV1_Loads()
        {
            // Regenerating this fixture is a deliberate act: it must keep loading in every future build.
            if (Environment.GetEnvironmentVariable("KAIJU_WRITE_GOLDEN") == "1" && !File.Exists(Golden)) new JsonFileSaveStore(Golden).Save(BuildGoldenV1());
            var bytes = File.ReadAllBytes(Golden);
            var result = new JsonFileSaveStore(Golden).Load();
            Assert.That(result.Ok, Is.True, result.Message);
            var doc = result.Document;
            Assert.That(doc.Schema, Is.EqualTo(1));
            Assert.That(doc.BuildVersion, Is.EqualTo("0.1.0-dev"));
            Assert.That(doc.TryGetSection("test.values", out var misc), Is.True);
            Assert.That(misc.TryGet("ent:1", out var r), Is.True);
            Assert.That(r.Get("pi").AsFloat64, Is.EqualTo(Math.PI));
            Assert.That(BitConverter.DoubleToInt64Bits(r.Get("negZero").AsFloat64), Is.EqualTo(BitConverter.DoubleToInt64Bits(-0.0)));
            Assert.That(r.Get("big").AsInt64, Is.EqualTo(long.MinValue));
            Assert.That(r.Get("name").AsString, Is.EqualTo("Ünïcode \"quoted\"\n\u0001"));
            Assert.That(Encoding.UTF8.GetBytes(SaveCodec.Serialize(doc)), Is.EqualTo(bytes), "canonical bytes are stable");
            Assert.That(SaveCodec.Serialize(BuildGoldenV1()), Is.EqualTo(Encoding.UTF8.GetString(bytes)));
        }

        // C29
        [Test]
        public void Save_RoundTrip_100k()
        {
            const int n = 100000;
            var rng = new SplitMix64(2026);
            var doc = new SaveDocument("0.1.0-dev");
            var sec = doc.AddSection("bench.buildings", 1);
            var expected = new Dictionary<string, long[]>(n);
            for (int i = 0; i < n; i++)
            {
                ulong raw = i % 3 == 0 ? (1UL << 32) + (ulong)i * 977UL : rng.Next();
                var key = BuildingId.From((BuildingSource)(1 + i % 3), raw).ToString();
                if (expected.ContainsKey(key)) continue;
                long bits = (long)rng.Next();               // any bit pattern: NaN payloads, denormals, -0.0
                long state = (long)rng.Next();
                sec.Add(key).Set("damage", SaveValue.Of(BitConverter.Int64BitsToDouble(bits))).Set("state", SaveValue.Of(state)).Set("collapsed", SaveValue.Of(i % 5 == 0));
                expected.Add(key, new[] { bits, state });
            }
            var store = new JsonFileSaveStore(Path.Combine(_dir, "world.save.json"));
            var sw = Stopwatch.StartNew();
            store.Save(doc);
            double writeMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            var loaded = store.Load();
            double readMs = sw.Elapsed.TotalMilliseconds;
            Assert.That(loaded.Ok, Is.True, loaded.Message);
            Assert.That(loaded.Document.TryGetSection("bench.buildings", out var back), Is.True);
            Assert.That(back.Records.Count, Is.EqualTo(expected.Count));
            int mismatches = 0;
            foreach (var r in back.Records)
            {
                var e = expected[r.Key];
                if (r.Get("damage").Float64Bits != e[0] || r.Get("state").AsInt64 != e[1]) mismatches++;
            }
            Assert.That(mismatches, Is.EqualTo(0));
            long size = new FileInfo(store.Path).Length;
            Evidence.Line("save-baseline.txt", "records=" + expected.Count + " write_ms=" + writeMs.ToString("F1", CultureInfo.InvariantCulture)
                + " read_ms=" + readMs.ToString("F1", CultureInfo.InvariantCulture) + " bytes=" + size);
            var evidenceDir = Environment.GetEnvironmentVariable("KAIJU_EVIDENCE_DIR");
            if (!string.IsNullOrEmpty(evidenceDir))
                new JsonFileSaveStore(Path.Combine(evidenceDir, AllocMeter.Runner + "-sample-save.json")).Save(BuildGoldenV1());
        }

        // C30
        [Test]
        public void Save_NewerVersion_RefusedUntouched()
        {
            string path = Path.Combine(_dir, "future.json");
            string text = SaveCodec.Serialize(BuildGoldenV1()).Replace("\"schema\":1", "\"schema\":2");
            File.WriteAllText(path, text);
            var stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(path, stamp);
            byte[] before = File.ReadAllBytes(path);
            var result = new JsonFileSaveStore(path).Load();
            Assert.That(result.Status, Is.EqualTo(SaveLoadStatus.NewerSchema));
            Assert.That(result.Document, Is.Null);
            StringAssert.Contains("newer than this build supports", result.Message);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
            Assert.That(File.GetLastWriteTimeUtc(path), Is.EqualTo(stamp));
        }

        [TestCase("\"schema\":1", "", SaveLoadStatus.UnknownSchema)]
        [TestCase("\"schema\":1", "\"schema\":0", SaveLoadStatus.UnknownSchema)]
        [TestCase("\"schema\":1", "\"schema\":\"1\"", SaveLoadStatus.UnknownSchema)]
        [TestCase("\"schema\":1", "\"schema\":1.5", SaveLoadStatus.UnknownSchema)]
        [TestCase("\"format\":\"kaiju-save\"", "\"format\":\"other\"", SaveLoadStatus.Corrupt)]
        [TestCase("\"version\":1", "\"version\":\"x\"", SaveLoadStatus.Corrupt)]
        [TestCase("]}\n]}", "]}", SaveLoadStatus.Corrupt)]
        public void Save_UnknownVersion_NoSilentLoad(string find, string replace, SaveLoadStatus expected)
        {
            string text = SaveCodec.Serialize(BuildGoldenV1());
            string bad = replace == "" ? text.Replace(find + ",", "") : text.Replace(find, replace);
            Assert.That(bad, Is.Not.EqualTo(text));
            var result = SaveCodec.Deserialize(bad, "case");
            Assert.That(result.Status, Is.EqualTo(expected), result.Message);
            Assert.That(result.Document, Is.Null);
        }

        [Test]
        public void Save_Migration_V1ToV2_Sample()
        {
            var loaded = new JsonFileSaveStore(Golden).Load();
            var system = new SaveSystem("0.2.0-dev");
            var inv = new InventoryParticipant();
            system.Register(inv);
            system.Register(new InventoryV1ToV2());
            var result = system.Restore(loaded.Document);
            Assert.That(result.Ok, Is.True, result.Message);
            Assert.That(inv.Health["bld:osm-way:4294967297"], Is.EqualTo(0.75));
            Assert.That(inv.Health["bld:landmark:3"], Is.EqualTo(1.0));
            var resaved = system.Capture(loaded.Document);
            Assert.That(resaved.TryGetSection("sample.inventory", out var s) && s.Version == 2, Is.True);
            Assert.That(resaved.BuildVersion, Is.EqualTo("0.2.0-dev"));
        }

        [Test]
        public void Save_SectionTooNewOrUnmigratable_Refused()
        {
            var inv = new InventoryParticipant { Version = 1 };
            var system = new SaveSystem("old-build");
            system.Register(inv);
            var doc = new SaveDocument("new-build");
            doc.AddSection("sample.inventory", 2).Add("bld:landmark:1").Set("health", SaveValue.Of(0.5));
            var r = system.Restore(doc);
            Assert.That(r.Status, Is.EqualTo(SaveLoadStatus.SectionTooNew));
            Assert.That(inv.ReadCalls, Is.EqualTo(0), "nothing applied");

            var v3 = new InventoryParticipant { Version = 3 };
            var sys3 = new SaveSystem("b");
            sys3.Register(v3);
            sys3.Register(new InventoryV1ToV2());
            var golden = new JsonFileSaveStore(Golden).Load().Document;
            Assert.That(sys3.Restore(golden).Status, Is.EqualTo(SaveLoadStatus.MissingMigration));
            Assert.That(v3.ReadCalls, Is.EqualTo(0));
        }

        // C31
        private sealed class FailingStream : MemoryStream
        {
            private readonly Stream _inner; private readonly long _failAt;
            public FailingStream(Stream inner, long failAt) { _inner = inner; _failAt = failAt; }
            public override void Write(byte[] buffer, int offset, int count)
            {
                int allowed = (int)Math.Max(0, Math.Min(count, _failAt - _inner.Position));
                _inner.Write(buffer, offset, allowed);
                _inner.Flush();
                if (allowed < count) throw new IOException("simulated power loss after " + _inner.Position + " bytes");
            }
            protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
        }

        [Test]
        public void Save_InterruptedWrite_PreviousIntact()
        {
            string path = Path.Combine(_dir, "world.save.json");
            var good = BuildGoldenV1();
            new JsonFileSaveStore(path).Save(good);
            byte[] goodBytes = File.ReadAllBytes(path);

            var next = BuildGoldenV1();
            next.TryGetSection("sample.inventory", out var inv);
            inv.Add("bld:landmark:99").Set("hp", SaveValue.Of(1L));
            var failing = new JsonFileSaveStore(path, p => new FailingStream(new FileStream(p, FileMode.Create), goodBytes.Length / 2));
            Assert.Throws<SaveWriteException>(() => failing.Save(next));

            Assert.That(File.ReadAllBytes(path), Is.EqualTo(goodBytes));
            Assert.That(File.Exists(failing.TempPath), Is.False, "partial temp file removed");
            File.WriteAllText(failing.TempPath, "{\"half-written"); // leftover from a real crash
            var loaded = new JsonFileSaveStore(path).Load();
            Assert.That(loaded.Ok, Is.True, loaded.Message);
            Assert.That(SaveCodec.Serialize(loaded.Document), Is.EqualTo(SaveCodec.Serialize(good)));
            new JsonFileSaveStore(path).Save(next);   // a later good save replaces both
            Assert.That(new JsonFileSaveStore(path).Load().Document.TryGetSection("sample.inventory", out var inv2) && inv2.TryGet("bld:landmark:99", out _), Is.True);
        }

        // C32
        [TestCase("de-DE")]
        [TestCase("tr-TR")]
        public void Save_ByteIdentical_UnderForeignCulture(string culture)
        {
            var original = Thread.CurrentThread.CurrentCulture;
            string invariant;
            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
                invariant = SaveCodec.Serialize(BuildGoldenV1());
                string p1 = Path.Combine(_dir, "a.json");
                new JsonFileSaveStore(p1).Save(BuildGoldenV1());
                Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
                Assert.That(1.5.ToString(), Is.Not.EqualTo("1.5"), "culture really uses a decimal comma");
                string p2 = Path.Combine(_dir, "b.json");
                new JsonFileSaveStore(p2).Save(BuildGoldenV1());
                Assert.That(SaveCodec.Serialize(BuildGoldenV1()), Is.EqualTo(invariant));
                Assert.That(File.ReadAllBytes(p2), Is.EqualTo(File.ReadAllBytes(p1)));
                var back = new JsonFileSaveStore(p2).Load();
                Assert.That(back.Ok, Is.True, back.Message);
                Assert.That(SaveCodec.Serialize(back.Document), Is.EqualTo(invariant));
                var settings = CoreSettings.Parse("{\"schema\":1,\"fixedStepSeconds\":0.02,\"maxEventsPerCascade\":10}", "c");
                Assert.That(settings.FixedStepSeconds, Is.EqualTo(0.02));
            }
            finally { Thread.CurrentThread.CurrentCulture = original; }
        }

        // C33
        [Test]
        public void Save_UnknownSections_PreservedThroughLoadSave()
        {
            string text = SaveCodec.Serialize(BuildGoldenV1());
            // A future module's section with value shapes this build does not understand.
            string future = "{\"name\":\"future.module\",\"version\":7,\"records\":[\n{\"key\":\"x:1\",\"fields\":{\"arr\":[1,2.5,{\"k\":null}],\"frac\":1.25e-3,\"huge\":123456789012345678901234567890,\"obj\":{\"f64\":\"nothex\"}}}\n]},\n";
            text = text.Replace("\"sections\":[\n", "\"sections\":[\n" + future);
            var loaded = SaveCodec.Deserialize(text, "future");
            Assert.That(loaded.Ok, Is.True, loaded.Message);
            var system = new SaveSystem("0.1.0-dev");
            var inv = new InventoryParticipant();
            system.Register(inv);
            system.Register(new InventoryV1ToV2());
            Assert.That(system.Restore(loaded.Document).Ok, Is.True);
            string resaved = SaveCodec.Serialize(system.Capture(loaded.Document));
            StringAssert.Contains(future.TrimEnd('\n', ','), resaved);
            var again = SaveCodec.Deserialize(resaved, "again");
            Assert.That(SaveCodec.Serialize(again.Document), Is.EqualTo(resaved));
        }
    }
}
