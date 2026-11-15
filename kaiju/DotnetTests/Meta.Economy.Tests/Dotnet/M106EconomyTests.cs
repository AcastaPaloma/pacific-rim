// M-106@v1, economy part (R-104 section 11): economy@v1 restores bit-exactly across 3 cycles x 3 seeds, with a file save and a simulated process
// restart at every phase change; a restarted run is compared with an un-restarted twin fed the same operations. Corrupt sections give
// SaveLoadStatus.Corrupt and leave the service untouched. Run with KAIJU_EVIDENCE_DIR set to write the evidence json.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Game.Core;
using Game.Meta.Economy;

namespace Game.Meta.Economy.Tests
{
    public class M106EconomyTests
    {
        static readonly PlayerSlot S0 = PlayerSlot.FromIndex(0), S1 = PlayerSlot.FromIndex(1);

        sealed class Session
        {
            public readonly SaveSystem Sys = new SaveSystem("m106");
            public readonly EconomyService Svc;
            public readonly JsonFileSaveStore Store;
            public Session(string path, PriceTable t) { Svc = new EconomyService(t); Store = new JsonFileSaveStore(path); Sys.Register(Svc); }
            public void Save() => Store.Save(Sys.Capture());
            public void Load() { var r = Store.Load(); Assert.IsTrue(r.Ok, r.Message); var rr = Sys.Restore(r.Document); Assert.IsTrue(rr.Ok, rr.Message); }
        }

        // Flattened bit image of everything the service exposes.
        static List<long> Flatten(EconomyService e, long maxContractId)
        {
            var o = new List<long> { e.Funds, e.Arrears, e.TotalCredited, e.TotalDebited, e.FloorGrantCount(S0), e.FloorGrantCount(S1) };
            for (long id = 1; id <= maxContractId; id++)
            {
                var c = e.GetContract(id);
                if (c == null) { o.Add(-99); continue; }
                o.Add(c.Id); o.Add((long)c.State); o.Add(c.ValueUsd); o.Add(c.AdvanceUsd); o.Add(c.PayoutUsd); o.Add(c.LossUsd); o.Add(c.AcceptedBySlot); o.Add(c.Step);
                foreach (var a in c.Assets) { foreach (char ch in a.AssetId) o.Add(ch); o.Add(-1); o.Add(a.ReplacementUsd); }
            }
            var tx = new List<KeyValuePair<long, long>>(); var rs = new List<TransactionReason>(); e.CopyTransactions(tx, rs);
            for (int i = 0; i < tx.Count; i++) { o.Add(tx[i].Key); o.Add(tx[i].Value); o.Add((long)rs[i]); }
            return o;
        }

        static long Diff(List<long> a, List<long> b)
        {
            long n = Math.Abs(a.Count - b.Count);
            for (int i = 0; i < Math.Min(a.Count, b.Count); i++) if (a[i] != b[i]) n++;
            return n;
        }

        // One scripted sortie: offer, accept, (maybe) buy a repair, complete, upkeep, maybe lose the mech and take the floor chassis. Identical on every driver.
        sealed class Driver
        {
            public EconomyService Svc; public long MaxId; readonly Kit.Rng _r; readonly PriceTable _t; readonly List<ContractAsset> _pool; public int Flyable = 1; long _step;
            public Driver(EconomyService svc, ulong seed, PriceTable t) { Svc = svc; _r = new Kit.Rng(seed); _t = t; _pool = Kit.Pool(t); }
            public long Open = 0;      // accepted, not completed
            public void Offer()
            {
                var offers = Svc.OfferContracts((long)_r.Next(), ++_step, _pool, 3);
                MaxId = offers[offers.Count - 1].Id;
                var c = offers[_r.Below(offers.Count)]; Svc.Accept(c.Id, S0); Open = c.Id;
                Svc.TryPurchase(Svc.Funds / 2, TransactionReason.Repair, _step);
            }
            public void Resolve()
            {
                var c = Svc.GetContract(Open); var st = new AssetDamageState[c.Assets.Length];
                for (int j = 0; j < st.Length; j++) st[j] = (AssetDamageState)_r.Below(4);
                Svc.Complete(Open, new ContractOutcome(st, ++_step)); Open = 0;
                Svc.PayUpkeep(_t.UpkeepPerSortieUsd(_t.ChassisPriceUsd("balanced")), _step);
                if (_r.Unit() < 0.4) Flyable = 0;
                if (Svc.TryGrantFloorChassis(S0, Flyable, _step).Granted) Flyable = 1;
            }
            public void Rebind(EconomyService s) { Svc = s; }
        }

        sealed class Result { public long Differences, Comparisons; public List<long> Final; public long MaxId; }

        static Result RunSeed(ulong seed, string dir)
        {
            var t = Kit.Prices(); var res = new Result();
            string path = Path.Combine(dir, "econ_" + seed + ".json");
            var twin = new Driver(new EconomyService(t, null, 25_000_000L), seed, t);
            var live = new Session(path, t); var drv = new Driver(live.Svc, seed, t);
            // seed the live service identically through its own starting funds, then restart once so even the first state crosses a file
            live = new Session(path, t); live.Svc.Credit(25_000_000L, TransactionReason.Other, 0);
            drv.Rebind(live.Svc);
            // the twin was built with startingFunds which also records the same first tx; align by recording the same on live
            for (int cycle = 0; cycle < 3; cycle++)
            {
                for (int sortie = 0; sortie < 4; sortie++)
                {
                    twin.Offer(); drv.Offer();
                    // phase change: contract accepted -> save, restart, load
                    live.Save(); live = new Session(path, t); live.Load(); drv.Rebind(live.Svc);
                    var a = Flatten(twin.Svc, twin.MaxId); var b = Flatten(live.Svc, drv.MaxId);
                    res.Differences += Diff(a, b); res.Comparisons++;
                    twin.Resolve(); drv.Resolve();
                    // phase change: contract settled -> save, restart, load
                    live.Save(); live = new Session(path, t); live.Load(); drv.Rebind(live.Svc);
                    a = Flatten(twin.Svc, twin.MaxId); b = Flatten(live.Svc, drv.MaxId);
                    res.Differences += Diff(a, b); res.Comparisons++;
                    Assert.AreEqual(twin.Svc.Funds, live.Svc.Funds);
                    Assert.GreaterOrEqual(live.Svc.Funds, 0);
                }
            }
            res.Final = Flatten(live.Svc, drv.MaxId); res.MaxId = drv.MaxId;
            return res;
        }

        [Test] public void M106_Economy_ZeroBitwiseDifferences_ThreeCycles_ThreeSeeds()
        {
            string dir = Path.Combine(Path.GetTempPath(), "m106_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
            try
            {
                long diffs = 0, cmp = 0; var seeds = new ulong[] { 1, 20261115, 0xDEADBEEFUL }; var sb = new StringBuilder("{\n  \"metric\": \"M-106@v1\",\n  \"part\": \"economy\",\n  \"cycles\": 3,\n  \"seeds\": [");
                for (int i = 0; i < seeds.Length; i++)
                {
                    var r = RunSeed(seeds[i], dir);
                    Assert.AreEqual(0, r.Differences, "seed " + seeds[i]);
                    Assert.AreEqual(24, r.Comparisons);
                    Assert.Greater(r.MaxId, 20, "the run created contracts");
                    diffs += r.Differences; cmp += r.Comparisons;
                    sb.Append(i > 0 ? "," : "").Append("\n    {\"seed\": ").Append(seeds[i]).Append(", \"comparisons\": ").Append(r.Comparisons).Append(", \"differences\": ").Append(r.Differences).Append(", \"contracts\": ").Append(r.MaxId).Append('}');
                }
                sb.Append("\n  ],\n  \"total_comparisons\": ").Append(cmp).Append(",\n  \"total_differences\": ").Append(diffs).Append("\n}\n");
                Assert.AreEqual(0, diffs); Assert.AreEqual(72, cmp);
                string ev = Environment.GetEnvironmentVariable("KAIJU_EVIDENCE_DIR");
                if (!string.IsNullOrEmpty(ev)) { Directory.CreateDirectory(ev); File.WriteAllText(Path.Combine(ev, "m106_economy.json"), sb.ToString()); }
            }
            finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
        }

        [Test] public void M106_FlippedBitIsCounted()
        {
            var t = Kit.Prices(); var e = new EconomyService(t, null, 1000);
            var a = Flatten(e, 0); var b = new List<long>(a); b[0] ^= 1L << 17;
            Assert.AreEqual(1, Diff(a, b));
            b = new List<long>(a); b.Add(0); Assert.AreEqual(1, Diff(a, b));
        }

        [Test] public void Save_CorruptSectionsAreRejectedAndServiceUntouched()
        {
            var t = Kit.Prices();
            var live = new Session(Path.Combine(Path.GetTempPath(), "m106c_" + Guid.NewGuid().ToString("N") + ".json"), t);
            var d = new Driver(live.Svc, 5, t); live.Svc.Credit(9_000_000L, TransactionReason.Other, 0);
            d.Offer(); d.Resolve();
            long id = d.MaxId; var before = Flatten(live.Svc, id);
            var doc = live.Sys.Capture();
            Func<Action<SaveSection>, SaveLoadResult> tamper = edit =>
            {
                var good = live.Sys.Capture(); edit(good.Sections.First(s => s.Name == "economy"));
                return live.Sys.Restore(good);
            };
            var cases = new List<Action<SaveSection>>
            {
                s => s.Remove("treasury"),
                s => { s.TryGet("treasury", out var r); r.Set("funds", SaveValue.Of(r.Get("funds").AsInt64 + 1)); },            // breaks conservation
                s => { s.TryGet("treasury", out var r); r.Set("funds", SaveValue.Of(-1L)); },
                s => { s.TryGet("treasury", out var r); r.Set("funds", SaveValue.Of(1.5)); },
                s => { s.TryGet("treasury", out var r); r.Set("next_contract_id", SaveValue.Of(1L)); },                           // contracts at or above next id
                s => { var c = s.Records.First(x => x.Key.StartsWith("contract:")); c.Set("state", SaveValue.Of(9L)); },
                s => { var c = s.Records.First(x => x.Key.StartsWith("contract:")); c.Set("value_usd", SaveValue.Of(c.Get("value_usd").AsInt64 + 1)); }, // assets no longer sum
                s => { var c = s.Records.First(x => x.Key.StartsWith("asset:")); c.Set("value_usd", SaveValue.Of(0L)); },
                s => { var c = s.Records.First(x => x.Key.StartsWith("tx:")); c.Set("reason", SaveValue.Of(99L)); },
                s => s.Add("mystery:1"),
                s => s.Add("floor:slot:x").Set("granted", SaveValue.Of(1L)),
                s => s.Add("asset:" + new string('0', 11) + "9:0000").Set("id", SaveValue.Of("a")).Set("value_usd", SaveValue.Of(5L)),
            };
            int n = 0;
            foreach (var edit in cases)
            {
                n++;
                var res = tamper(edit);
                Assert.AreEqual(SaveLoadStatus.Corrupt, res.Status, "case " + (n - 1) + ": " + res.Message);
                Assert.AreEqual(0, Diff(before, Flatten(live.Svc, id)), "service untouched after case " + (n - 1));
            }
            Assert.IsNotNull(doc);
        }

        [Test] public void Save_UnrelatedSectionsKeptAndNewerSectionRejected()
        {
            var t = Kit.Prices(); var sys = new SaveSystem("m106"); var svc = new EconomyService(t, null, 77); sys.Register(svc);
            var other = new SaveDocument("x"); other.AddSection("hangar", 1).Add("slot:0").Set("phase", SaveValue.Of(1L));
            var doc = sys.Capture(other);
            Assert.IsTrue(doc.TryGetSection("hangar", out _)); Assert.IsTrue(doc.TryGetSection("economy", out var es)); Assert.AreEqual(1, es.Version);
            es.Version = 2;
            Assert.AreEqual(SaveLoadStatus.SectionTooNew, sys.Restore(doc).Status);
            Assert.AreEqual(77L, svc.Funds);
        }

        [Test] public void Save_EmptyServiceRoundTripsAndFloorCountsSurvive()
        {
            var t = Kit.Prices(); var a = new EconomyService(t); var sysA = new SaveSystem("v"); sysA.Register(a);
            a.TryGrantFloorChassis(S1, 0, 3); a.TryGrantFloorChassis(S1, 0, 4);
            var b = new EconomyService(t); var sysB = new SaveSystem("v"); sysB.Register(b);
            Assert.IsTrue(sysB.Restore(sysA.Capture()).Ok);
            Assert.AreEqual(2L, b.FloorGrantCount(S1)); Assert.AreEqual(0L, b.FloorGrantCount(S0));
            Assert.AreEqual(0, Diff(Flatten(a, 0), Flatten(b, 0)));
        }
    }
}
