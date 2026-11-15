// M-059@v1: damage and repair carry-over over 3 fight -> hangar -> fight cycles x 3 seeds (R-025 section 4).
// Producer side of the metric: every comparison counts bitwise differences (floats as raw bits); the gate is 0 everywhere.
// Each save is followed by a simulated process restart: a new SaveSystem, HangarService and body, loaded from the file on disk.
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using Game.Core;
using Game.Hangar;

namespace Game.Hangar.Tests
{
    public class M059Tests
    {
        static readonly string[] Ids = { "arm_l", "arm_r", "core", "head", "leg_l", "leg_r", "pelvis", "torso" };
        static readonly double[] Max = { 80, 80, 200, 60, 120, 120, 140, 180 };
        static readonly PlayerSlot S0 = PlayerSlot.FromIndex(0);

        sealed class Rng
        {
            ulong _s; public Rng(ulong seed) { _s = seed; }
            public ulong Next() { unchecked { _s += 0x9E3779B97F4A7C15UL; ulong z = _s; z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL; z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL; return z ^ (z >> 31); } }
            public double Unit() => (Next() >> 11) * (1.0 / 9007199254740992.0);
            public int Below(int n) => (int)(Next() % (ulong)n);
        }

        sealed class Session
        {
            public readonly SaveSystem Sys = new SaveSystem("m059");
            public readonly HangarService Svc = new HangarService(HangarSites.CrissyField);
            public readonly JsonFileSaveStore Store;
            public Session(string path) { Store = new JsonFileSaveStore(path); Sys.Register(Svc); }
            public void Save() => Store.Save(Sys.Capture());
            public void Load()
            {
                var r = Store.Load(); Assert.IsTrue(r.Ok, r.Message);
                var rr = Sys.Restore(r.Document); Assert.IsTrue(rr.Ok, rr.Message);
            }
        }

        // Flattened bit image of the hangar's view of one body.
        static List<long> Flatten(HangarService svc)
        {
            var o = new List<long> { (long)svc.GetPhase(S0), svc.GetCycle(S0) };
            var parts = new List<PartRepairState>();
            Assert.IsTrue(svc.TryGetParts(S0, parts));
            foreach (var p in parts)
            {
                foreach (char c in p.Damage.PartId) o.Add(c);
                o.Add(-1);
                o.Add(BitConverter.DoubleToInt64Bits(p.Damage.Integrity)); o.Add(BitConverter.DoubleToInt64Bits(p.Damage.MaxIntegrity));
                o.Add(p.Damage.LastHitStep); o.Add(p.Damage.Hits); o.Add(p.Repaired ? 1 : 0); o.Add(p.QueueIndex);
            }
            return o;
        }

        static List<long> Flatten(FakeBody b)
        {
            var o = new List<long>();
            var sorted = new List<PartDamage>(b.Parts); sorted.Sort((x, y) => string.CompareOrdinal(x.PartId, y.PartId));
            foreach (var p in sorted)
            {
                foreach (char c in p.PartId) o.Add(c);
                o.Add(-1);
                o.Add(BitConverter.DoubleToInt64Bits(p.Integrity)); o.Add(BitConverter.DoubleToInt64Bits(p.MaxIntegrity)); o.Add(p.LastHitStep); o.Add(p.Hits);
            }
            return o;
        }

        static long Diff(List<long> a, List<long> b)
        {
            long n = Math.Abs(a.Count - b.Count);
            for (int i = 0; i < Math.Min(a.Count, b.Count); i++) if (a[i] != b[i]) n++;
            return n;
        }

        static string Sha(List<long> v)
        {
            var bytes = new byte[v.Count * 8]; for (int i = 0; i < v.Count; i++) BitConverter.GetBytes(v[i]).CopyTo(bytes, i * 8);
            return BitConverter.ToString(SHA256.Create().ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        // independent model of what the integrity of each part must be
        sealed class Shadow
        {
            public readonly double[] Integ = (double[])Max.Clone();
            public readonly long[] Hits = new long[8];
            public void Hit(int i, double dmg) { Integ[i] = Math.Max(0.0, Integ[i] - dmg); Hits[i]++; }
        }

        public sealed class Result
        {
            public long Differences, Comparisons, Hits, MinPartsHit, ReplayPartsHit;
            public bool ZeroPart, ContinuedFromCarried = true, RepairedExact = true, UnrepairedUnchanged = true;
            public List<string> Hashes = new List<string>();
        }

        static void Fight(FakeBody body, Shadow sh, Rng rng, int cycle, ref long step, Result res, bool firstFight)
        {
            int hits = 20 + rng.Below(8);
            var touched = new HashSet<int>();
            for (int h = 0; h < hits; h++)
            {
                int i = h < 8 ? (h + cycle) % 8 : rng.Below(8);         // every part is hit at least once per fight
                double d = 0.5 + rng.Unit() * 9.0;
                if (firstFight && h == 3) i = 4;                         // drive leg_l to 0 in fight 1
                if (firstFight && Ids[i] == "leg_l") d = Max[4] / 3.0 + rng.Unit();
                double before = body.Parts[i].Integrity;
                body.Hit(Ids[i], d, ++step); sh.Hit(i, d); res.Hits++;
                if (Math.Max(0.0, before - d) != body.Parts[i].Integrity) res.ContinuedFromCarried = false;
                touched.Add(i);
            }
            if (firstFight) { body.Hit("leg_l", 1e6, ++step); sh.Hit(4, 1e6); res.ZeroPart |= body.Parts[4].Integrity == 0.0; }
            res.MinPartsHit = touched.Count;
        }

        static Result RunSeed(ulong seed, string dir)
        {
            var res = new Result(); var rng = new Rng(seed);
            string path = Path.Combine(dir, "save_" + seed + ".json");
            var body = FakeBody.Fresh(S0, Ids, Max); var sh = new Shadow(); long step = 0;
            Fight(body, sh, rng, 0, ref step, res, true);
            Assert.GreaterOrEqual(res.Hits, 20); Assert.GreaterOrEqual(res.MinPartsHit, 6); Assert.IsTrue(res.ZeroPart);
            var live = new Session(path);                                    // lives through the whole run, replaced after each "restart"
            for (int cycle = 0; cycle < 3; cycle++)
            {
                // --- fight ends: enter the hangar, save, restart, load
                live.Svc.EnterHangar(body);
                var endFight = Flatten(live.Svc); res.Hashes.Add(Sha(endFight));
                live.Save();
                live = new Session(path); live.Load();
                res.Differences += Diff(endFight, Flatten(live.Svc)); res.Comparisons++;

                // --- hangar: repair 2 named parts (after the restart), leave the rest
                string a = Ids[(cycle * 3) % 8], b2 = Ids[(cycle * 3 + 4) % 8];
                var beforeRepair = new List<PartRepairState>(); live.Svc.TryGetParts(S0, beforeRepair);
                live.Svc.QueueRepair(S0, a); live.Svc.QueueRepair(S0, b2);
                Assert.AreEqual(2, live.Svc.ProcessRepairs(S0));
                var afterRepair = new List<PartRepairState>(); live.Svc.TryGetParts(S0, afterRepair);
                for (int i = 0; i < afterRepair.Count; i++)
                {
                    var p = afterRepair[i]; var q = beforeRepair[i];
                    if (p.Damage.PartId == a || p.Damage.PartId == b2)
                    { if (BitConverter.DoubleToInt64Bits(p.Damage.Integrity) != BitConverter.DoubleToInt64Bits(p.Damage.MaxIntegrity) || !p.Repaired) res.RepairedExact = false; }
                    else if (!q.Damage.Equals(p.Damage) || p.Repaired != q.Repaired) res.UnrepairedUnchanged = false;
                }
                var endHangar = Flatten(live.Svc); res.Hashes.Add(Sha(endHangar));
                live.Save();
                live = new Session(path); live.Load();
                res.Differences += Diff(endHangar, Flatten(live.Svc)); res.Comparisons++;

                // --- leave the hangar into a fresh body (the next fight): carried state must equal the hangar's
                var next = new FakeBody(S0);
                live.Svc.LeaveHangar(next);
                for (int i = 0; i < 8; i++)
                {
                    int k = Array.IndexOf(Ids, next.Parts[i].PartId);
                    // shadow: repaired parts back to max, others carried
                    if (next.Parts[i].PartId == a || next.Parts[i].PartId == b2) sh.Integ[k] = Max[k];
                }
                var expected = new List<long>();
                for (int i = 0; i < 8; i++)
                {
                    foreach (char c in Ids[i]) expected.Add(c); expected.Add(-1);
                    expected.Add(BitConverter.DoubleToInt64Bits(sh.Integ[i])); expected.Add(BitConverter.DoubleToInt64Bits(Max[i]));
                    expected.Add(next.Parts[i].LastHitStep); expected.Add(sh.Hits[i]);
                }
                res.Differences += Diff(expected, Flatten(next)); res.Comparisons++;
                body = next;

                // --- next fight: re-hit the repaired part and an unrepaired one, plus a seeded list
                Fight(body, sh, rng, cycle + 1, ref step, res, false);
                Assert.GreaterOrEqual(res.MinPartsHit, 6);
            }
            // final fight state is a valid hangar input too
            live.Svc.EnterHangar(body);
            var last = Flatten(live.Svc); res.Hashes.Add(Sha(last)); live.Save(); live = new Session(path); live.Load();
            res.Differences += Diff(last, Flatten(live.Svc)); res.Comparisons++;
            Assert.AreEqual(4, live.Svc.GetCycle(S0) + 1, "three completed visits");
            return res;
        }

        [Test] public void M059_CarryOver_ZeroBitwiseDifferences_ThreeCycles_ThreeSeeds()
        {
            string dir = Path.Combine(Path.GetTempPath(), "m059_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
            try
            {
                var sb = new StringBuilder("{\n  \"metric\": \"M-059@v1\",\n  \"cycles\": 3,\n  \"seeds\": [");
                long diffs = 0, cmp = 0; var seeds = new ulong[] { 1, 20261115, 0xDEADBEEFUL };
                for (int i = 0; i < seeds.Length; i++)
                {
                    var r = RunSeed(seeds[i], dir);
                    Assert.IsTrue(r.RepairedExact, "repaired parts reach max exactly"); Assert.IsTrue(r.UnrepairedUnchanged, "unrepaired parts bitwise unchanged");
                    Assert.IsTrue(r.ContinuedFromCarried, "hits continue from the carried integrity, never from full");
                    Assert.AreEqual(0, r.Differences, "seed " + seeds[i]);
                    diffs += r.Differences; cmp += r.Comparisons;
                    sb.Append(i > 0 ? "," : "").Append("\n    {\"seed\": ").Append(seeds[i]).Append(", \"comparisons\": ").Append(r.Comparisons)
                      .Append(", \"differences\": ").Append(r.Differences).Append(", \"hits\": ").Append(r.Hits).Append(", \"state_sha256\": [\"").Append(string.Join("\", \"", r.Hashes)).Append("\"]}");
                }
                sb.Append("\n  ],\n  \"comparisons\": ").Append(cmp).Append(",\n  \"bitwise_differences\": ").Append(diffs).Append(",\n  \"negative_control_differences\": ").Append(NegativeControl(dir)).Append("\n}\n");
                Assert.AreEqual(0, diffs);
                string ev = Environment.GetEnvironmentVariable("KAIJU_EVIDENCE_DIR");
                if (!string.IsNullOrEmpty(ev)) { Directory.CreateDirectory(ev); File.WriteAllText(Path.Combine(ev, "m059.json"), sb.ToString()); }
            }
            finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
        }

        // One damage bit flipped in the saved file must be counted (the metric can fail).
        static long NegativeControl(string dir)
        {
            string path = Path.Combine(dir, "neg.json");
            var s = new Session(path); var body = FakeBody.Fresh(S0, Ids, Max); body.Hit("torso", 33.3, 1); body.Hit("core", 4.4, 2);
            s.Svc.EnterHangar(body); var truth = Flatten(s.Svc); s.Save();
            var doc = s.Store.Load().Document;
            Assert.IsTrue(doc.TryGetSection("hangar", out var sec)); Assert.IsTrue(sec.TryGet("slot:0/torso", out var rec));
            rec.Set(HangarService.FieldIntegrity, SaveValue.Of(BitConverter.Int64BitsToDouble(rec.Get(HangarService.FieldIntegrity).Float64Bits ^ 1)));
            s.Store.Save(doc);
            var s2 = new Session(path); s2.Load();
            long d = Diff(truth, Flatten(s2.Svc)); Assert.GreaterOrEqual(d, 1); return d;
        }

        [Test] public void M059_NegativeControl_OneFlippedBitIsCounted()
        {
            string dir = Path.Combine(Path.GetTempPath(), "m059n_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
            try { Assert.AreEqual(1, NegativeControl(dir)); } finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
        }

        [Test] public void M059_ResetToFull_IsCounted()
        {
            var a = FakeBody.Fresh(S0, Ids, Max); a.Hit("torso", 50, 1);
            var svc = new HangarService(HangarSites.CrissyField); svc.EnterHangar(a);
            var truth = Flatten(svc);
            a.Parts[a.Index("torso")] = new PartDamage { PartId = "torso", Integrity = Max[7], MaxIntegrity = Max[7] };   // a bug that resets to full
            var svc2 = new HangarService(HangarSites.CrissyField); svc2.EnterHangar(a);
            Assert.Greater(Diff(truth, Flatten(svc2)), 0);
        }
    }
}
