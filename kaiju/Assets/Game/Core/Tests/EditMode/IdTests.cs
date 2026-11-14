using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public class IdTests
    {
        // C18
        [Test]
        public void Ids_NoImplicitConversions_NoPublicConstructors()
        {
            foreach (var t in new[] { typeof(EntityId), typeof(BuildingId), typeof(PlayerSlot) })
            {
                Assert.That(t.IsValueType, Is.True, t.Name);
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    Assert.That(m.Name, Is.Not.EqualTo("op_Implicit").And.Not.EqualTo("op_Explicit"), t.Name + " must not convert");
                Assert.That(t.GetConstructors(BindingFlags.Public | BindingFlags.Instance), Is.Empty, t.Name + " is built only by named factories");
            }
        }

        [Test]
        public void Ids_DefaultIsInvalid()
        {
            Assert.That(default(EntityId).IsValid, Is.False);
            Assert.That(default(BuildingId).IsValid, Is.False);
            Assert.That(default(PlayerSlot).IsValid, Is.False);
            var alloc = new EntityIdAllocator();
            Assert.That(alloc.Allocate(), Is.Not.EqualTo(default(EntityId)));
            Assert.That(PlayerSlot.FromIndex(0), Is.Not.EqualTo(default(PlayerSlot)));
            Assert.That(BuildingId.From(BuildingSource.OsmWay, 0), Is.Not.EqualTo(default(BuildingId)));
            Assert.Throws<ArgumentOutOfRangeException>(() => BuildingId.From(BuildingSource.None, 1));
            Assert.Throws<InvalidOperationException>(() => { var _ = default(PlayerSlot).Index; });
        }

        [Test]
        public void Ids_ValueSemantics_AllocationFree()
        {
            var e1 = EntityId.FromValue(7); var e2 = EntityId.FromValue(7); var e3 = EntityId.FromValue(8);
            var b1 = BuildingId.From(BuildingSource.OsmWay, 5); var b2 = BuildingId.From(BuildingSource.OsmWay, 5);
            var p1 = PlayerSlot.FromIndex(3); var p2 = PlayerSlot.FromIndex(3);
            Assert.That(e1 == e2 && e1.Equals(e2) && e1.GetHashCode() == e2.GetHashCode() && e1 != e3, Is.True);
            Assert.That(b1 == b2 && b1.GetHashCode() == b2.GetHashCode(), Is.True);
            Assert.That(p1 == p2 && p1.GetHashCode() == p2.GetHashCode(), Is.True);
            Assert.That(e1.CompareTo(e3), Is.LessThan(0));
            Assert.That(BuildingId.From(BuildingSource.OsmWay, 9).CompareTo(BuildingId.From(BuildingSource.OsmRelation, 1)), Is.LessThan(0));
            int acc = 0;
            Action ops = () =>
            {
                for (int i = 0; i < 10000; i++)
                {
                    acc += (e1 == e2 ? 1 : 0) + (e1.Equals(e3) ? 1 : 0) + e1.GetHashCode() + e1.CompareTo(e3);
                    acc += (b1 == b2 ? 1 : 0) + b1.GetHashCode() + b1.CompareTo(b2) + (b1.Equals(b2) ? 1 : 0);
                    acc += (p1 == p2 ? 1 : 0) + p1.GetHashCode() + p1.CompareTo(p2) + (p1.Equals(p2) ? 1 : 0);
                }
            };
            ops();
            var r = AllocMeter.Measure(ops);
            Evidence.Line("alloc.txt", "id equality/hash/compare x10000 -> " + r);
            if (r.Bytes >= 0) Assert.That(r.Bytes, Is.EqualTo(0));
            if (r.Count >= 0) Assert.That(r.Count, Is.EqualTo(0));
        }

        // C19: golden values, identical in every process and runner.
        public static string ComputeGolden()
        {
            var sb = new StringBuilder();
            foreach (var v in new ulong[] { 1, 42, (1UL << 32) + 7, ulong.MaxValue - 1 })
            {
                var id = EntityId.FromValue(v);
                sb.Append("entity|").Append(v).Append('|').Append(id).Append('|').Append(id.GetHashCode().ToString("X8")).Append('\n');
            }
            var builds = new[]
            {
                BuildingId.From(BuildingSource.OsmWay, 1), BuildingId.From(BuildingSource.OsmWay, 4294967297UL),
                BuildingId.From(BuildingSource.OsmRelation, 4294967297UL), BuildingId.From(BuildingSource.Landmark, 0),
                BuildingId.From(BuildingSource.OsmWay, 12345678901234UL),
            };
            foreach (var b in builds)
                sb.Append("building|").Append((int)b.Source).Append(':').Append(b.SourceId).Append('|').Append(b).Append('|').Append(b.GetHashCode().ToString("X8")).Append('\n');
            foreach (var i in new[] { 0, 1, 63 })
            {
                var p = PlayerSlot.FromIndex(i);
                sb.Append("slot|").Append(i).Append('|').Append(p).Append('|').Append(p.GetHashCode().ToString("X8")).Append('\n');
            }
            return sb.ToString();
        }

        [Test]
        public void Ids_SerialiseRoundTrip_AndCrossProcessStable()
        {
            string now = ComputeGolden();
            if (Environment.GetEnvironmentVariable("KAIJU_WRITE_GOLDEN") == "1") File.WriteAllText(ProjectPaths.Fixture("ids_golden.txt"), now);
            string golden = File.ReadAllText(ProjectPaths.Fixture("ids_golden.txt")).Replace("\r\n", "\n");
            Evidence.Line("trace-hashes.txt", "ids_golden " + StableHash.Hex(StableHash.Fnv1a64(now)));
            Assert.That(now, Is.EqualTo(golden), "ID string/hash forms changed: saves and replicas would break");

            // Lossless round trip through the string form and the save form.
            var doc = new SaveDocument("test");
            var sec = doc.AddSection("ids", 1);
            var rng = new SplitMix64(7);
            var entities = new List<EntityId>(); var buildings = new List<BuildingId>(); var slots = new List<PlayerSlot>();
            for (int i = 0; i < 1000; i++)
            {
                var e = EntityId.FromValue(rng.Next() | 1); entities.Add(e);
                var b = BuildingId.From((BuildingSource)(1 + rng.NextInt(3)), rng.Next()); buildings.Add(b);
                var p = PlayerSlot.FromIndex(rng.NextInt(1 << 20)); slots.Add(p);
                Assert.That(EntityId.TryParse(e.ToString(), out var e2) && e2 == e, Is.True, e.ToString());
                Assert.That(BuildingId.TryParse(b.ToString(), out var b2) && b2 == b, Is.True, b.ToString());
                Assert.That(PlayerSlot.TryParse(p.ToString(), out var p2) && p2 == p, Is.True, p.ToString());
                if (!sec.TryGet(b.ToString(), out _)) sec.Add(b.ToString()).Set("entity", SaveValue.Of(e.ToString())).Set("slot", SaveValue.Of(p.ToString()));
            }
            var back = SaveCodec.Deserialize(SaveCodec.Serialize(doc), "mem");
            Assert.That(back.Ok, Is.True, back.Message);
            Assert.That(back.Document.TryGetSection("ids", out var sec2), Is.True);
            foreach (var r in sec2.Records)
            {
                Assert.That(BuildingId.TryParse(r.Key, out _), Is.True);
                Assert.That(EntityId.TryParse(r.Get("entity").AsString, out _), Is.True);
                Assert.That(PlayerSlot.TryParse(r.Get("slot").AsString, out _), Is.True);
            }
            foreach (var bad in new[] { "", "ent:", "ent:-1", "ent:1x", "bld:osm-way:", "bld:unknown:1", "bld:none:1", "slot:a", "ENT:1", "ent:18446744073709551616" })
            {
                Assert.That(EntityId.TryParse(bad, out _) || BuildingId.TryParse(bad, out _) || PlayerSlot.TryParse(bad, out _), Is.False, bad);
            }
        }

        // C20
        [Test]
        public void BuildingId_FromSourceKey_OrderIndependent()
        {
            var keys = new List<KeyValuePair<BuildingSource, ulong>>();
            var rng = new SplitMix64(3);
            for (int i = 0; i < 2000; i++) keys.Add(new KeyValuePair<BuildingSource, ulong>((BuildingSource)(1 + rng.NextInt(3)), rng.Next() >> rng.NextInt(40)));
            var damageA = new Dictionary<BuildingId, int>();
            for (int i = 0; i < keys.Count; i++) damageA[BuildingId.From(keys[i].Key, keys[i].Value)] = i;
            // Re-import: reversed tile order plus 500 new buildings mixed in.
            var damageB = new Dictionary<BuildingId, int>();
            for (int i = 0; i < 500; i++) damageB[BuildingId.From(BuildingSource.Landmark, 1000000000000UL + (ulong)i)] = -1;
            for (int i = keys.Count - 1; i >= 0; i--) damageB[BuildingId.From(keys[i].Key, keys[i].Value)] = i;
            foreach (var kv in damageA) Assert.That(damageB[kv.Key], Is.EqualTo(kv.Value));
        }

        [Test]
        public void BuildingId_LargeValues_NoCrossSourceCollision()
        {
            foreach (var raw in new[] { 0UL, 1UL, (1UL << 32) - 1, 1UL << 32, (1UL << 32) + 1, 11000000000UL, ulong.MaxValue })
            {
                var way = BuildingId.From(BuildingSource.OsmWay, raw);
                var rel = BuildingId.From(BuildingSource.OsmRelation, raw);
                var lm = BuildingId.From(BuildingSource.Landmark, raw);
                Assert.That(way != rel && rel != lm && way != lm, Is.True);
                Assert.That(way.ToString() != rel.ToString() && rel.ToString() != lm.ToString(), Is.True);
                Assert.That(BuildingId.TryParse(way.ToString(), out var back) && back.SourceId == raw, Is.True);
            }
        }

        [Test]
        public void BuildingId_SameRawIdOtherSource_UnequalHashAndSortApart()
        {
            var sources = new[] { BuildingSource.OsmWay, BuildingSource.OsmRelation, BuildingSource.Landmark };
            foreach (var raw in new[] { 1UL, 42UL, 1UL << 32, 11000000000UL, ulong.MaxValue })
                for (int a = 0; a < sources.Length; a++)
                    for (int b = a + 1; b < sources.Length; b++)
                    {
                        var x = BuildingId.From(sources[a], raw); var y = BuildingId.From(sources[b], raw);
                        Assert.That(x.Equals(y), Is.False, x + " vs " + y);
                        Assert.That(x.GetHashCode(), Is.Not.EqualTo(y.GetHashCode()), x + " vs " + y + " hash");
                        Assert.That(x.CompareTo(y), Is.LessThan(0), "ordered by source first");
                        Assert.That(y.CompareTo(x), Is.GreaterThan(0));
                    }
        }

        [Test]
        public void BuildingId_RawIdsDifferingOnlyAboveBit32_StayDistinct()
        {
            foreach (int bit in new[] { 32, 33, 40, 63 })
                foreach (ulong low in new[] { 0UL, 7UL, 0xFFFFFFFFUL })
                {
                    var x = BuildingId.From(BuildingSource.OsmWay, low);
                    var y = BuildingId.From(BuildingSource.OsmWay, low | (1UL << bit));
                    Assert.That(x == y, Is.False, "bit " + bit);
                    Assert.That(x.GetHashCode(), Is.Not.EqualTo(y.GetHashCode()), "bit " + bit + " hash");
                    Assert.That(x.CompareTo(y), Is.LessThan(0), "bit " + bit + " order");
                    Assert.That(x.ToString(), Is.Not.EqualTo(y.ToString()));
                }
        }

        [Test, Category("Slow")]
        public void BuildingId_NoCollisions_10M()
        {
            const int perSource = 3333334; // x3 sources = 10,000,002 IDs
            var ids = new BuildingId[perSource * 3];
            int n = 0;
            for (int s = 1; s <= 3; s++)
                for (ulong i = 0; i < perSource; i++)
                    ids[n++] = BuildingId.From((BuildingSource)s, unchecked(i * 0x9E3779B97F4A7C15UL)); // same raw ids in every source
            Array.Sort(ids);
            int collisions = 0;
            for (int i = 1; i < ids.Length; i++) if (ids[i] == ids[i - 1]) collisions++;
            Evidence.Line("grep-counts.txt", "BuildingId_NoCollisions_10M: ids=" + ids.Length + " collisions=" + collisions);
            Assert.That(ids.Length, Is.GreaterThanOrEqualTo(10000000));
            Assert.That(collisions, Is.EqualTo(0));
        }

        // C21
        [Test]
        public void EntityId_NoReuseWithinSession()
        {
            var alloc = new EntityIdAllocator();
            var stale = alloc.Allocate();
            alloc.Release(stale);
            ulong highest = stale.Value;
            for (int i = 0; i < 1000000; i++)
            {
                var id = alloc.Allocate();
                if (id.Value <= highest || id == stale) Assert.Fail("ID " + id + " reissued or not increasing");
                highest = id.Value;
                alloc.Release(id);
            }
            Assert.That(alloc.IsLive(stale), Is.False);
            Assert.Throws<InvalidOperationException>(() => alloc.Release(stale));
            Assert.That(alloc.LiveCount, Is.EqualTo(0));
            var resumed = new EntityIdAllocator(alloc.NextValue);
            Assert.That(resumed.Allocate().Value, Is.GreaterThan(highest), "numbering continues after a load");
        }

        // C22
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(16)]
        [TestCase(64)]
        public void PlayerSlots_SupportN(int n)
        {
            var slots = PlayerSlot.Range(n);
            Assert.That(slots.Length, Is.EqualTo(n));
            var set = new HashSet<PlayerSlot>(slots);
            Assert.That(set.Count, Is.EqualTo(n));
            var doc = new SaveDocument("test");
            var sec = doc.AddSection("slots", 1);
            for (int i = 0; i < n; i++)
            {
                Assert.That(slots[i].Index, Is.EqualTo(i));
                Assert.That(PlayerSlot.TryParse(slots[i].ToString(), out var back) && back == slots[i], Is.True);
                if (i > 0) Assert.That(slots[i - 1].CompareTo(slots[i]), Is.LessThan(0));
                sec.Add(slots[i].ToString()).Set("ready", SaveValue.Of(i % 2 == 0));
            }
            var loaded = SaveCodec.Deserialize(SaveCodec.Serialize(doc), "mem");
            loaded.Document.TryGetSection("slots", out var sec2);
            Assert.That(sec2.Records.Count, Is.EqualTo(n));
        }
    }
}
