// M-106@v1, salvage part (R-104 section 11): salvage@v1 restores bit-exactly across 3 cycles x 3 seeds, with a file save and a simulated process
// restart at every phase change (after claim, after rare-part consumption); a restarted run is compared with an un-restarted twin fed the same
// operations. Floats are compared as raw bits. Corrupt sections give SaveLoadStatus.Corrupt and leave the service untouched.
// Run with KAIJU_EVIDENCE_DIR set to write the evidence json.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Game.Core;
using Game.Meta.Economy;
using Game.Meta.Salvage;

namespace Game.Meta.Salvage.Tests
{
    public class M106SalvageTests
    {
        static readonly string[] Classes = { "structure", "armour", "drive", "reactor", "titanium", "composite", "weapons", "bone", "hide", "tendon", "debris_steel", "debris_other" };
        static readonly string[] Rares = { "osteoderm_plate", "keratin_liner", "actuator_fibre" };

        sealed class Session
        {
            public readonly SaveSystem Sys = new SaveSystem("m106s");
            public readonly SalvageService Svc;
            public readonly JsonFileSaveStore Store;
            public Session(string path) { Svc = new SalvageService(Kit.Yields(), new EconomyService(Kit.Prices()), null); Store = new JsonFileSaveStore(path); Sys.Register(Svc); }
            public void Save() => Store.Save(Sys.Capture());
            public void Load() { var r = Store.Load(); Assert.IsTrue(r.Ok, r.Message); var rr = Sys.Restore(r.Document); Assert.IsTrue(rr.Ok, rr.Message); }
        }

        // Bit image of everything the service exposes plus its whole saved section.
        static List<long> Flatten(Session s)
        {
            var o = new List<long> { s.Svc.TotalCreditUsd, s.Svc.WreckCount };
            foreach (var c in Classes) o.Add(BitConverter.DoubleToInt64Bits(s.Svc.InventoryKg(c)));
            foreach (var r in Rares) o.Add(s.Svc.RareAvailable(r));
            var doc = s.Sys.Capture(); doc.TryGetSection("salvage", out var sec);
            foreach (var rec in sec.Records)
            {
                foreach (char ch in rec.Key) o.Add(ch); o.Add(-1);
                foreach (var f in rec.FieldNames.OrderBy(x => x, StringComparer.Ordinal))
                {
                    foreach (char ch in f) o.Add(ch);
                    var v = rec.Get(f); o.Add((long)v.Kind);
                    o.Add(v.Kind == SaveValueKind.Float64 ? v.Float64Bits : v.Kind == SaveValueKind.Int64 ? v.AsInt64 : 0);
                }
            }
            return o;
        }

        static long Diff(List<long> a, List<long> b)
        {
            long n = Math.Abs(a.Count - b.Count);
            for (int i = 0; i < Math.Min(a.Count, b.Count); i++) if (a[i] != b[i]) n++;
            return n;
        }

        sealed class Driver
        {
            public SalvageService Svc; readonly Kit.Rng _r; readonly PriceTable _t; readonly YieldsTable _y; long _id, _step;
            public long MaxId => _id;
            public Driver(SalvageService svc, ulong seed) { Svc = svc; _r = new Kit.Rng(seed); _t = Kit.Prices(); _y = Kit.Yields(); }
            public void Rebind(SalvageService s) { Svc = s; }
            public void ClaimSome()
            {
                for (int k = 0; k < 3; k++)
                {
                    var loc = (SalvageLocation)_r.Below(3); SalvageInput inp; _id++; _step++;
                    switch (_r.Below(3))
                    {
                        case 0: inp = Kit.Mech(_id, loc, _r, _t, _r.Unit() * 1500); break;
                        case 1: inp = Kit.Kaiju(_id, loc, _y, 30 + _r.Unit() * 140, _r.Unit() * 20_000); break;
                        default: inp = Kit.Building(_id, loc, 1e5 + _r.Unit() * 5e7, _r.Unit()); break;
                    }
                    Svc.Claim(inp, _step);
                    if (_r.Unit() < 0.3) Svc.Claim(inp, _step);   // a replayed handoff changes nothing
                }
            }
            public void ConsumeSome()
            {
                foreach (var r in Rares) { long a = Svc.RareAvailable(r); if (a > 0) Svc.TryConsumeRare(r, 1 + _r.Below((int)Math.Min(a, 5))); }
            }
        }

        sealed class Result { public long Differences, Comparisons, Wrecks; }

        static Result RunSeed(ulong seed, string dir)
        {
            var res = new Result(); string path = Path.Combine(dir, "salv_" + seed + ".json");
            var twinSession = new Session(Path.Combine(dir, "twin_" + seed + ".json")); var twin = new Driver(twinSession.Svc, seed);
            var live = new Session(path); var drv = new Driver(live.Svc, seed);
            for (int cycle = 0; cycle < 3; cycle++)
            {
                for (int phase = 0; phase < 2; phase++)
                {
                    twin.ClaimSome(); drv.ClaimSome();
                    live.Save(); live = new Session(path); live.Load(); drv.Rebind(live.Svc);       // restart after claims
                    res.Differences += Diff(Flatten(twinSession), Flatten(live)); res.Comparisons++;
                    twin.ConsumeSome(); drv.ConsumeSome();
                    live.Save(); live = new Session(path); live.Load(); drv.Rebind(live.Svc);       // restart after rare consumption
                    res.Differences += Diff(Flatten(twinSession), Flatten(live)); res.Comparisons++;
                }
            }
            res.Wrecks = drv.MaxId;
            return res;
        }

        [Test] public void M106_Salvage_ZeroBitwiseDifferences_ThreeCycles_ThreeSeeds()
        {
            string dir = Path.Combine(Path.GetTempPath(), "m106s_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
            try
            {
                long diffs = 0, cmp = 0; var seeds = new ulong[] { 1, 20261115, 0xDEADBEEFUL };
                var sb = new StringBuilder("{\n  \"metric\": \"M-106@v1\",\n  \"part\": \"salvage\",\n  \"cycles\": 3,\n  \"seeds\": [");
                for (int i = 0; i < seeds.Length; i++)
                {
                    var r = RunSeed(seeds[i], dir);
                    Assert.AreEqual(0, r.Differences, "seed " + seeds[i]);
                    Assert.AreEqual(12, r.Comparisons);
                    Assert.AreEqual(18, r.Wrecks);
                    diffs += r.Differences; cmp += r.Comparisons;
                    sb.Append(i > 0 ? "," : "").Append("\n    {\"seed\": ").Append(seeds[i]).Append(", \"comparisons\": ").Append(r.Comparisons).Append(", \"differences\": ").Append(r.Differences).Append(", \"wrecks\": ").Append(r.Wrecks).Append('}');
                }
                sb.Append("\n  ],\n  \"total_comparisons\": ").Append(cmp).Append(",\n  \"total_differences\": ").Append(diffs).Append("\n}\n");
                Assert.AreEqual(0, diffs); Assert.AreEqual(36, cmp);
                string ev = Environment.GetEnvironmentVariable("KAIJU_EVIDENCE_DIR");
                if (!string.IsNullOrEmpty(ev)) { Directory.CreateDirectory(ev); File.WriteAllText(Path.Combine(ev, "m106_salvage.json"), sb.ToString()); }
            }
            finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
        }

        [Test] public void M106_FlippedBitIsCounted()
        {
            var s = new Session(Path.Combine(Path.GetTempPath(), "m106sf_" + Guid.NewGuid().ToString("N") + ".json"));
            new Driver(s.Svc, 3).ClaimSome();
            var a = Flatten(s); var b = new List<long>(a); b[2] ^= 1L << 17;
            Assert.AreEqual(1, Diff(a, b));
            b = new List<long>(a); b.Add(0); Assert.AreEqual(1, Diff(a, b));
        }

        [Test] public void Replayed_AfterReload_DoesNotSalvageTwice()
        {
            string path = Path.Combine(Path.GetTempPath(), "m106sr_" + Guid.NewGuid().ToString("N") + ".json");
            var eco = new EconomyService(Kit.Prices()); var s1 = new SalvageService(Kit.Yields(), eco);
            var sys1 = new SaveSystem("v"); sys1.Register(s1);
            var inp = Kit.Mech(5, SalvageLocation.Land, new Kit.Rng(1), Kit.Prices());
            var first = s1.Claim(inp, 1);
            var eco2 = new EconomyService(Kit.Prices()); var s2 = new SalvageService(Kit.Yields(), eco2);
            var sys2 = new SaveSystem("v"); sys2.Register(s2);
            Assert.IsTrue(sys2.Restore(sys1.Capture()).Ok);
            var again = s2.Claim(inp, 2);
            Assert.IsTrue(again.Replay); Assert.AreEqual(first.CreditUsd, again.CreditUsd); Assert.AreEqual(0, eco2.Funds);
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(first.RecoveredKg), BitConverter.DoubleToInt64Bits(again.RecoveredKg));
        }

        [Test] public void Save_CorruptSectionsAreRejectedAndServiceUntouched()
        {
            var live = new Session(Path.Combine(Path.GetTempPath(), "m106sc_" + Guid.NewGuid().ToString("N") + ".json"));
            var d = new Driver(live.Svc, 11); d.ClaimSome(); d.ClaimSome(); d.ConsumeSome();
            var before = Flatten(live);
            SaveLoadResult Tamper(Action<SaveSection> edit)
            {
                var good = live.Sys.Capture(); edit(good.Sections.First(s => s.Name == "salvage"));
                return live.Sys.Restore(good);
            }
            var cases = new List<Action<SaveSection>>
            {
                s => s.Remove("totals"),
                s => { s.TryGet("totals", out var r); r.Set("credit_usd", SaveValue.Of(r.Get("credit_usd").AsInt64 + 1)); },          // total no longer equals the sum
                s => { s.TryGet("totals", out var r); r.Set("credit_usd", SaveValue.Of(-1L)); },
                s => { s.TryGet("totals", out var r); r.Set("wrecks", SaveValue.Of(r.Get("wrecks").AsInt64 + 1)); },
                s => { s.TryGet("totals", out var r); r.Set("wrecks", SaveValue.Of(1.5)); },
                s => { var c = s.Records.First(x => x.Key.StartsWith("wreck:")); c.Set("source", SaveValue.Of(9L)); },
                s => { var c = s.Records.First(x => x.Key.StartsWith("wreck:")); c.Set("location", SaveValue.Of(-1L)); },
                s => { var c = s.Records.First(x => x.Key.StartsWith("wreck:")); c.Set("recovered_kg", SaveValue.Of(c.Get("available_kg").AsFloat64 * 2 + 1)); },
                s => { var c = s.Records.First(x => x.Key.StartsWith("wreck:")); c.Set("recovered_kg", SaveValue.Of(double.NaN)); },
                s => { var c = s.Records.First(x => x.Key.StartsWith("wreck:")); c.Set("recovered_kg", SaveValue.Of(5L)); },              // wrong kind
                s => { var c = s.Records.First(x => x.Key.StartsWith("wreck:")); c.Set("raw_credit_usd", SaveValue.Of(c.Get("credit_usd").AsInt64 - 1)); },
                s => { var c = s.Records.First(x => x.Key.StartsWith("inv:")); c.Set("mass_kg", SaveValue.Of(c.Get("mass_kg").AsFloat64 + 1e6)); },   // inventory != recovered
                s => { var c = s.Records.First(x => x.Key.StartsWith("inv:")); c.Set("mass_kg", SaveValue.Of(-1.0)); },
                s => s.Add("inv:unobtainium").Set("mass_kg", SaveValue.Of(1.0)),
                s => s.Add("rare:mystery_part").Set("earned", SaveValue.Of(1L)).Set("consumed", SaveValue.Of(0L)),
                s => { var c = s.Records.First(x => x.Key.StartsWith("rare:")); c.Set("consumed", SaveValue.Of(c.Get("earned").AsInt64 + 1)); },
                s => { var c = s.Records.First(x => x.Key.StartsWith("rare:")); c.Set("earned", SaveValue.Of(c.Get("earned").AsInt64 + 1)); },       // does not match inventory
                s => s.Add("wreck:000000000000").Set("source", SaveValue.Of(0L)),
                s => s.Add("mystery:1"),
            };
            Assert.IsTrue(live.Svc.RareAvailable("osteoderm_plate") + live.Svc.RareAvailable("keratin_liner") + live.Svc.RareAvailable("actuator_fibre") >= 0);
            Assert.IsTrue(live.Sys.Capture().Sections.First(s => s.Name == "salvage").Records.Any(x => x.Key.StartsWith("rare:")), "the scripted run earned a rare part");
            int n = 0;
            foreach (var edit in cases)
            {
                var res = Tamper(edit);
                Assert.AreEqual(SaveLoadStatus.Corrupt, res.Status, "case " + n + ": " + res.Message);
                Assert.AreEqual(0, Diff(before, Flatten(live)), "service untouched after case " + n);
                n++;
            }
        }

        [Test] public void Save_NewerSectionRejected_AndEmptyServiceRoundTrips()
        {
            var a = new Session(Path.Combine(Path.GetTempPath(), "m106se_" + Guid.NewGuid().ToString("N") + ".json"));
            var doc = a.Sys.Capture(); Assert.IsTrue(doc.TryGetSection("salvage", out var es)); Assert.AreEqual(1, es.Version);
            var b = new Session(Path.Combine(Path.GetTempPath(), "m106se2_" + Guid.NewGuid().ToString("N") + ".json"));
            Assert.IsTrue(b.Sys.Restore(doc).Ok); Assert.AreEqual(0, Diff(Flatten(a), Flatten(b)));
            es.Version = 2;
            Assert.AreEqual(SaveLoadStatus.SectionTooNew, b.Sys.Restore(doc).Status);
        }
    }
}
