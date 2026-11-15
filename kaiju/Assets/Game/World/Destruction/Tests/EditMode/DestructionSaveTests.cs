// destruction@v1 save section tests (T-025 step 1, M-050 shape). Engine-free.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Game.Core;
using NUnit.Framework;

namespace Game.World.Destruction.Tests
{
    public class DestructionSaveTests
    {
        static readonly BuildingSource[] Sources = { BuildingSource.OsmWay, BuildingSource.OsmRelation, BuildingSource.Landmark };

        internal static BuildingId Id(int i) { return BuildingId.From(Sources[i % 3], (ulong)(1000 + i * 7919)); }

        internal static BuildingAttributes Attr(int i)
        {
            return new BuildingAttributes { HeightM = 5 + (i * 13) % 150, Levels = 0, FootprintM2 = 80 + (i * 31) % 1500, Residential = i % 3 == 0 };
        }

        // Seeded damaged world: n buildings registered, a fraction of them hit with random energies.
        internal static DamageModel Build(int n, int seed)
        {
            var m = new DamageModel();
            for (int i = 0; i < n; i++) m.Register(Id(i), Attr(i));
            var rng = new Random(seed);
            for (int i = 0; i < n; i++)
            {
                if (rng.Next(3) == 0) continue;
                m.TryGetThresholds(Id(i), out _, out _, out _, out double s4);
                int hits = 1 + rng.Next(3);
                for (int h = 0; h < hits; h++)
                    m.Apply(Id(i), rng.NextDouble() * s4 * 0.8, (DamageCause)rng.Next(4), default, 100 + rng.Next(1000));
            }
            return m;
        }

        internal static SaveSystem Sys(DamageModel m, out DestructionSaveParticipant p)
        {
            var sys = new SaveSystem("test");
            p = new DestructionSaveParticipant(m);
            sys.Register(p);
            return sys;
        }

        internal static string Bytes(DamageModel m)
        {
            var sys = Sys(m, out _);
            return SaveCodec.Serialize(sys.Capture());
        }

        internal static SaveDocument RoundTrip(string text)
        {
            var r = SaveCodec.Deserialize(text, "mem");
            Assert.IsTrue(r.Ok, r.Message);
            return r.Document;
        }

        internal static List<BuildingDamageState> All(DamageModel m)
        {
            var l = new List<BuildingDamageState>();
            m.CopyDamage(l);
            l.Sort((a, b) => string.CompareOrdinal(a.Id.ToString(), b.Id.ToString()));
            return l;
        }

        internal static void AssertSameBits(IList<BuildingDamageState> a, IList<BuildingDamageState> b)
        {
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].Id, b[i].Id);
                Assert.AreEqual(a[i].State, b[i].State);
                Assert.AreEqual(BitConverter.DoubleToInt64Bits(a[i].CumulativeEnergyJ), BitConverter.DoubleToInt64Bits(b[i].CumulativeEnergyJ));
                Assert.AreEqual(BitConverter.DoubleToInt64Bits(a[i].DebrisKg), BitConverter.DoubleToInt64Bits(b[i].DebrisKg));
                Assert.AreEqual(a[i].LastStep, b[i].LastStep);
                Assert.AreEqual(a[i].Cause, b[i].Cause);
            }
        }

        // Fresh model with the same buildings registered but no damage, restored from the saved text.
        internal static DamageModel Reload(string text, int n, out DestructionSaveParticipant p)
        {
            var m = new DamageModel();
            for (int i = 0; i < n; i++) m.Register(Id(i), Attr(i));
            var sys = Sys(m, out p);
            var r = sys.Restore(RoundTrip(text));
            Assert.IsTrue(r.Ok, r.Message);
            return m;
        }

        [Test]
        public void SaveLoadSave_SectionBytesIdentical()
        {
            var a = Build(2000, 11);
            string t1 = Bytes(a);
            var b = Reload(t1, 2000, out _);
            Assert.AreEqual(t1, Bytes(b));
            Assert.Greater(All(a).Count, 100);
        }

        [Test]
        public void Restored_StatesEnergyDebris_EqualBitForBit()
        {
            var a = Build(2000, 12);
            var b = Reload(Bytes(a), 2000, out var p);
            AssertSameBits(All(a), All(b));
            Assert.AreEqual(DestructionContractVersions.ModelRevision, p.LoadedModelRevision);
            Assert.IsTrue(All(a).Exists(s => s.DebrisKg > 0), "fixture must include debris");
        }

        [Test]
        public void Sparse_UndamagedBuildingsAreNotWritten()
        {
            var m = Build(300, 13);
            var doc = Sys(m, out _).Capture();
            Assert.IsTrue(doc.TryGetSection("destruction", out var sec));
            Assert.AreEqual(1, sec.Version);
            Assert.AreEqual(All(m).Count + 1, sec.Records.Count); // + header
            Assert.IsTrue(All(m).Count < 300);
            for (int i = 1; i < sec.Records.Count; i++) Assert.Less(string.CompareOrdinal(sec.Records[i - 1].Key, sec.Records[i].Key), 0);
        }

        [Test]
        public void UnknownIds_SurviveAndReapplyAtRegister()
        {
            var a = Build(500, 14);
            string t1 = Bytes(a);
            // Reload into a model where only the first 100 buildings are loaded.
            var b = Reload(t1, 100, out _);
            Assert.Greater(b.PendingCount, 0);
            Assert.AreEqual(t1, Bytes(b), "records of unloaded buildings are written back unchanged");
            for (int i = 100; i < 500; i++) b.Register(Id(i), Attr(i));
            Assert.AreEqual(0, b.PendingCount);
            AssertSameBits(All(a), All(b));
            Assert.AreEqual(t1, Bytes(b));
        }

        [Test]
        public void Restore_ReplacesExistingDamage()
        {
            var a = Build(50, 15);
            var b = Build(50, 99);
            var sys = Sys(b, out _);
            Assert.IsTrue(sys.Restore(RoundTrip(Bytes(a))).Ok);
            AssertSameBits(All(a), All(b));
        }

        [Test]
        public void Restore_PublishesNothing()
        {
            var a = Build(100, 16);
            var bus = new RecordingBus();
            var b = new DamageModel(bus);
            for (int i = 0; i < 100; i++) b.Register(Id(i), Attr(i));
            Assert.IsTrue(Sys(b, out _).Restore(RoundTrip(Bytes(a))).Ok);
            Assert.AreEqual(0, bus.Events.Count);
        }

        [Test]
        public void SameBytesUnderOtherCultures()
        {
            var m = Build(300, 17);
            string baseline = Bytes(m);
            var saved = Thread.CurrentThread.CurrentCulture;
            try
            {
                foreach (var name in new[] { "de-DE", "tr-TR" })
                {
                    Thread.CurrentThread.CurrentCulture = new CultureInfo(name);
                    Assert.AreEqual(baseline, Bytes(m), name);
                    Assert.AreEqual(baseline, Bytes(Reload(baseline, 300, out _)), name);
                }
            }
            finally { Thread.CurrentThread.CurrentCulture = saved; }
        }

        [Test]
        public void UnrelatedSections_KeptByteIdentical()
        {
            var m = Build(50, 18);
            var doc = new SaveDocument("other");
            var other = doc.AddSection("zzz.other", 3);
            other.Add("ent:1").Set("x", SaveValue.Of(1.5));
            var sys = Sys(m, out _);
            var cap = sys.Capture(doc);
            Assert.IsTrue(cap.TryGetSection("zzz.other", out var kept));
            Assert.AreEqual(3, kept.Version);
            Assert.AreEqual(1.5, kept.Records[0].Get("x").AsFloat64);
        }

        [Test]
        public void NewerSectionVersion_RefusedAndModelUntouched()
        {
            var a = Build(50, 19);
            var doc = Sys(a, out _).Capture();
            doc.TryGetSection("destruction", out var sec);
            sec.Version = 2;
            var b = Build(50, 20);
            var before = All(b);
            var r = Sys(b, out _).Restore(doc);
            Assert.AreEqual(SaveLoadStatus.SectionTooNew, r.Status);
            AssertSameBits(before, All(b));
        }

        [Test]
        public void BadRecord_ThrowsAndModelUntouched()
        {
            var a = Build(50, 21);
            var doc = Sys(a, out _).Capture();
            doc.TryGetSection("destruction", out var sec);
            sec.Records[sec.Records.Count - 1].Set("state", SaveValue.Of(9L));
            var b = Build(50, 22);
            var before = All(b);
            Assert.Throws<FormatException>(() => Sys(b, out _).Restore(doc));
            AssertSameBits(before, All(b));
        }

        [Test]
        public void BadKey_Throws()
        {
            var m = new DamageModel();
            var doc = new SaveDocument("t");
            var sec = doc.AddSection("destruction", 1);
            sec.Add("bld:osm-way:notanumber").Set("state", SaveValue.Of(1L));
            Assert.Throws<FormatException>(() => Sys(m, out _).Restore(doc));
        }

        [Test]
        public void CorruptedByte_IsDetectedOrChangesTheHash()
        {
            var a = Build(300, 23);
            string t1 = Bytes(a);
            var rng = new Random(5);
            int changed = 0, tried = 0;
            // Flip one character inside records (skip the envelope header line).
            int start = t1.IndexOf("\"records\"", StringComparison.Ordinal);
            for (int k = 0; k < 200; k++)
            {
                int pos = start + rng.Next(t1.Length - start);
                char c = t1[pos];
                if (c == '\n') continue;
                char repl = c == '0' ? '1' : '0';
                string bad = t1.Substring(0, pos) + repl + t1.Substring(pos + 1);
                tried++;
                var r = SaveCodec.Deserialize(bad, "bad");
                bool detected = !r.Ok;
                if (!detected)
                {
                    var m = new DamageModel();
                    for (int i = 0; i < 300; i++) m.Register(Id(i), Attr(i));
                    try
                    {
                        var rr = Sys(m, out _).Restore(r.Document);
                        detected = !rr.Ok || Bytes(m) != t1;
                    }
                    catch (FormatException) { detected = true; }
                }
                if (detected) changed++;
            }
            Assert.Greater(tried, 150);
            Assert.AreEqual(tried, changed, "every single-byte corruption must be detected or change the section bytes");
        }

        [Test]
        public void Model_Restore_RejectsInvalidInput_Atomically()
        {
            var m = Build(20, 24);
            var before = All(m);
            var bad = new List<BuildingDamageState>
            {
                new BuildingDamageState { Id = Id(1), State = 1, CumulativeEnergyJ = 5, DebrisKg = 0 },
                new BuildingDamageState { Id = Id(2), State = 1, CumulativeEnergyJ = double.NaN },
            };
            Assert.Throws<ArgumentException>(() => m.Restore(bad));
            AssertSameBits(before, All(m));
            var dup = new List<BuildingDamageState>
            {
                new BuildingDamageState { Id = Id(1), State = 1, CumulativeEnergyJ = 5 },
                new BuildingDamageState { Id = Id(1), State = 2, CumulativeEnergyJ = 6 },
            };
            Assert.Throws<ArgumentException>(() => m.Restore(dup));
            AssertSameBits(before, All(m));
        }

        [Test]
        public void ReregisterAfterRestore_KeepsSavedDamage()
        {
            var a = Build(100, 25);
            var b = Reload(Bytes(a), 100, out _);
            for (int i = 0; i < 100; i++) b.Register(Id(i), Attr(i)); // idempotent re-register
            AssertSameBits(All(a), All(b));
        }
    }
}
