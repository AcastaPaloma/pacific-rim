// M-106@v1, roster part (R-104 section 11): roster@v1 restores bit-exactly across 3 cycles x 3 seeds, with a file save and a simulated process restart
// after the fights and after the hangar actions of every phase; a restarted run is compared with an un-restarted twin fed the same operations.
// Floats are compared as raw bits. Run with KAIJU_EVIDENCE_DIR set to write the evidence json.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Game.Core;
using Game.Meta.Economy;
using Game.Meta.Roster;

namespace Game.Meta.Roster.Tests
{
    public class M106RosterTests
    {
        sealed class Session
        {
            public readonly SaveSystem Sys = new SaveSystem("m106r");
            public readonly RosterService Svc; public readonly EntityIdAllocator Alloc = new EntityIdAllocator(); public readonly JsonFileSaveStore Store;
            public Session(string path) { Svc = new RosterService(new EconomyService(Kit.Prices()), Alloc); Store = new JsonFileSaveStore(path); Sys.Register(Svc); }
            public void Save() => Store.Save(Sys.Capture());
            public void Load() { var r = Store.Load(); Assert.IsTrue(r.Ok, r.Message); var rr = Sys.Restore(r.Document); Assert.IsTrue(rr.Ok, rr.Message); }
        }

        static List<long> Flatten(Session s)
        {
            var o = new List<long> { (long)s.Svc.NextEntityValue };
            var doc = s.Sys.Capture(); doc.TryGetSection("roster", out var sec);
            foreach (var rec in sec.Records)
            {
                foreach (char ch in rec.Key) o.Add(ch); o.Add(-1);
                foreach (var f in rec.FieldNames.OrderBy(x => x, StringComparer.Ordinal))
                {
                    foreach (char ch in f) o.Add(ch);
                    var v = rec.Get(f); o.Add((long)v.Kind);
                    if (v.Kind == SaveValueKind.String) foreach (char ch in v.AsString) o.Add(ch);
                    else o.Add(v.Kind == SaveValueKind.Float64 ? v.Float64Bits : v.Kind == SaveValueKind.Int64 ? v.AsInt64 : v.AsBool ? 1 : 0);
                }
            }
            foreach (var slot in new[] { Kit.S0, Kit.S1 })
            {
                o.Add(s.Svc.FlyableCount(slot)); o.Add((long)s.Svc.ActiveMech(slot).Value); o.Add(s.Svc.FloorGrants(slot));
                var p = s.Svc.Pilot(slot); o.Add(BitConverter.DoubleToInt64Bits(p.Strain)); o.Add(p.Sorties); o.Add(p.RecoverySorties); o.Add(p.MechsLost);
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
            public RosterService Svc; readonly Kit.Rng _r; long _step; public long Losses, Fights;
            public Driver(RosterService svc, ulong seed) { Svc = svc; _r = new Kit.Rng(seed); svc.Create(Kit.S0, "balanced", (long)_r.Next(), 0); svc.Create(Kit.S1, "heavy_brawler", (long)_r.Next(), 0); }
            public void Rebind(RosterService s) { Svc = s; }
            public void Fights3()
            {
                for (int k = 0; k < 3; k++)
                {
                    _step += 1 + _r.Below(50); var slot = _r.Below(2) == 0 ? Kit.S0 : Kit.S1;
                    var rec = _r.Below(2) == 0 ? Kit.Body(slot, _r.Unit() < 0.35 ? 0 : _r.Unit() * 100) : Kit.Full(slot, _r.Unit() < 0.35 ? 0 : 1 + _r.Unit() * 99, _r.Unit() < 0.5 ? 0 : 50);
                    var res = Svc.OnFightEnd(rec, _step, _r.Unit()); Fights++; if (res.Lost) Losses++;
                }
            }
            public void Hangar()
            {
                _step += 1; var slot = _r.Below(2) == 0 ? Kit.S0 : Kit.S1;
                if (_r.Below(3) == 0) Svc.Create(slot, _r.Below(2) == 0 ? "balanced" : "heavy_brawler", (long)_r.Next(), _step);
                var l = new List<MechInstance>(); Svc.Mechs(slot, l); var fly = l.Where(x => x.State == MechState.Flyable).ToList();
                if (fly.Count > 0) Svc.SetActive(slot, fly[_r.Below(fly.Count)].MechId);
                var scrappable = fly.Where(x => !x.IsFloor).ToList();
                if (scrappable.Count > 0 && _r.Below(4) == 0) Svc.Retire(scrappable[_r.Below(scrappable.Count)].MechId, _step);
            }
        }

        sealed class Result { public long Differences, Comparisons, Fights, Losses; }

        static Result RunSeed(ulong seed, string dir)
        {
            var res = new Result(); string path = Path.Combine(dir, "ros_" + seed + ".json");
            var twinSession = new Session(Path.Combine(dir, "twin_" + seed + ".json")); var twin = new Driver(twinSession.Svc, seed);
            var live = new Session(path); var drv = new Driver(live.Svc, seed);
            for (int cycle = 0; cycle < 3; cycle++)
            {
                for (int phase = 0; phase < 2; phase++)
                {
                    twin.Fights3(); drv.Fights3();
                    live.Save(); live = new Session(path); live.Load(); drv.Rebind(live.Svc);          // restart after the fights
                    res.Differences += Diff(Flatten(twinSession), Flatten(live)); res.Comparisons++;
                    twin.Hangar(); drv.Hangar();
                    live.Save(); live = new Session(path); live.Load(); drv.Rebind(live.Svc);          // restart after the hangar actions
                    res.Differences += Diff(Flatten(twinSession), Flatten(live)); res.Comparisons++;
                }
            }
            res.Fights = drv.Fights; res.Losses = drv.Losses;
            return res;
        }

        [Test] public void M106_Roster_ZeroBitwiseDifferences_ThreeCycles_ThreeSeeds()
        {
            string dir = Path.Combine(Path.GetTempPath(), "m106r_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
            try
            {
                long diffs = 0, cmp = 0, losses = 0; var seeds = new ulong[] { 1, 20261115, 0xDEADBEEFUL };
                var sb = new StringBuilder("{\n  \"metric\": \"M-106@v1\",\n  \"part\": \"roster\",\n  \"cycles\": 3,\n  \"seeds\": [");
                for (int i = 0; i < seeds.Length; i++)
                {
                    var r = RunSeed(seeds[i], dir);
                    Assert.AreEqual(0, r.Differences, "seed " + seeds[i]);
                    Assert.AreEqual(12, r.Comparisons);
                    diffs += r.Differences; cmp += r.Comparisons; losses += r.Losses;
                    sb.Append(i > 0 ? "," : "").Append("\n    {\"seed\": ").Append(seeds[i]).Append(", \"comparisons\": ").Append(r.Comparisons).Append(", \"differences\": ").Append(r.Differences)
                      .Append(", \"fights\": ").Append(r.Fights).Append(", \"losses\": ").Append(r.Losses).Append('}');
                }
                sb.Append("\n  ],\n  \"total_comparisons\": ").Append(cmp).Append(",\n  \"total_differences\": ").Append(diffs).Append("\n}\n");
                Assert.AreEqual(0, diffs); Assert.AreEqual(36, cmp); Assert.Greater(losses, 0, "the scripted runs include losses and floor grants");
                string ev = Environment.GetEnvironmentVariable("KAIJU_EVIDENCE_DIR");
                if (!string.IsNullOrEmpty(ev)) { Directory.CreateDirectory(ev); File.WriteAllText(Path.Combine(ev, "m106_roster.json"), sb.ToString()); }
            }
            finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
        }

        [Test] public void M106_FlippedBitIsCounted()
        {
            var s = new Session(Path.Combine(Path.GetTempPath(), "m106rf_" + Guid.NewGuid().ToString("N") + ".json"));
            new Driver(s.Svc, 3).Fights3();
            var a = Flatten(s); var b = new List<long>(a); b[2] ^= 1L << 17;
            Assert.AreEqual(1, Diff(a, b));
            b = new List<long>(a); b.Add(0); Assert.AreEqual(1, Diff(a, b));
        }
    }
}
