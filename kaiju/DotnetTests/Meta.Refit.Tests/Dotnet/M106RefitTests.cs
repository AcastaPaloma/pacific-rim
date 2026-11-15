// M-106@v1, refit part (R-104 section 11): refit@v1 restores bit-exactly across 3 cycles x 3 seeds, with a file save and a simulated process restart after the
// sorties (wear) and after the hangar refits of every phase; a restarted run is compared with an un-restarted twin fed the same operations. Floats are compared as raw
// bits. Run with KAIJU_EVIDENCE_DIR set to write the evidence json.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Game.Core;
using Game.Meta.Economy;
using Game.Meta.Refit;

namespace Game.Meta.Refit.Tests
{
    public class M106RefitTests
    {
        sealed class Session
        {
            public readonly SaveSystem Sys = new SaveSystem("m106f");
            public readonly Kit.Rig Rig; public readonly JsonFileSaveStore Store;
            public Session(string path)
            {
                Rig = Kit.NewRig(50_000_000_000L, false); Store = new JsonFileSaveStore(path); Sys.Register(Rig.Svc);
                Rig.Salvage.Sets["osteoderm_plate"] = 1000; Rig.Salvage.Sets["actuator_fibre"] = 1000;
            }
            public RefitService Svc => Rig.Svc;
            public void Save() => Store.Save(Sys.Capture());
            public void Load() { var r = Store.Load(); Assert.IsTrue(r.Ok, r.Message); var rr = Sys.Restore(r.Document); Assert.IsTrue(rr.Ok, rr.Message); }
        }

        static readonly EntityId[] Mechs = { EntityId.FromValue(21), EntityId.FromValue(22), EntityId.FromValue(23) };

        static List<long> Flatten(Session s)
        {
            var o = new List<long>();
            var doc = s.Sys.Capture(); doc.TryGetSection("refit", out var sec);
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
            foreach (var id in Mechs)
            {
                var b = s.Svc.Get(id); o.Add(b.SpecHash); o.Add(b.Refits);
                foreach (var kv in b.Wear) o.Add(BitConverter.DoubleToInt64Bits(kv.Value));
                // the spec rebuilt from the loaded parts equals the stored hash
                o.Add(s.Svc.Validate(b.Chassis, b.Parts).Spec.Hash);
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
            public Session S; readonly Kit.Rng _r; long _step; public long Applied, Refused;
            public Driver(Session s, ulong seed)
            {
                S = s; _r = new Kit.Rng(seed);
                s.Svc.Register(Mechs[0], Kit.S0, "balanced", s.Svc.DefaultBuild("balanced"), false, 0);
                s.Svc.Register(Mechs[1], Kit.S1, "heavy_brawler", s.Svc.DefaultBuild("heavy_brawler"), false, 0);
                s.Svc.Register(Mechs[2], Kit.S1, "basic_balanced", s.Svc.DefaultBuild("basic_balanced"), true, 0);
            }
            public void Rebind(Session s) { S = s; }
            public void Sorties()
            {
                for (int k = 0; k < 6; k++)
                {
                    var id = Mechs[_r.Below(3)]; var b = S.Svc.Get(id); var slots = b.Parts.Keys.ToList();
                    S.Svc.AddWear(id, slots[_r.Below(slots.Count)], _r.Unit() * 0.37);
                }
            }
            public void Hangar()
            {
                for (int k = 0; k < 4; k++)
                {
                    _step += 1; var id = Mechs[_r.Below(3)]; var b = S.Svc.Get(id); var ch = S.Rig.Chassis.Get(b.Chassis);
                    var a = new Dictionary<string, string>(b.Parts);
                    int swaps = 1 + _r.Below(2);
                    for (int i = 0; i < swaps; i++)
                    {
                        var slot = ch.Slots[_r.Below(ch.Slots.Length)];
                        var fit = S.Rig.Parts.All.Where(p => p.SlotType == slot.Type && p.Chassis.Contains(ch.Id)).ToList();
                        a[slot.Name] = fit[_r.Below(fit.Count)].Id;
                    }
                    var res = S.Svc.Apply(id, a, _step);
                    if (res.Status == RefitStatus.Applied) Applied++; else Refused++;
                }
            }
        }

        sealed class Result { public long Differences, Comparisons, Applied, Refused; }

        static Result RunSeed(ulong seed, string dir)
        {
            var res = new Result();
            var twin = new Session(Path.Combine(dir, "twin_" + seed + ".json")); var td = new Driver(twin, seed);
            var live = new Session(Path.Combine(dir, "refit_" + seed + ".json")); var ld = new Driver(live, seed);
            for (int cycle = 0; cycle < 3; cycle++)
            {
                for (int phase = 0; phase < 2; phase++)
                {
                    td.Sorties(); ld.Sorties();
                    live.Save(); var path = Path.Combine(dir, "refit_" + seed + ".json"); live = new Session(path); live.Load(); ld.Rebind(live);   // restart after the sorties
                    res.Differences += Diff(Flatten(twin), Flatten(live)); res.Comparisons++;
                    td.Hangar(); ld.Hangar();
                    live.Save(); live = new Session(path); live.Load(); ld.Rebind(live);                                                                                          // restart after the refits
                    res.Differences += Diff(Flatten(twin), Flatten(live)); res.Comparisons++;
                }
            }
            res.Applied = ld.Applied; res.Refused = ld.Refused;
            Assert.AreEqual(td.Applied, ld.Applied, "the restarted run took the same decisions as the twin");
            return res;
        }

        [Test] public void M106_Refit_ZeroBitwiseDifferences_ThreeCycles_ThreeSeeds()
        {
            string dir = Path.Combine(Path.GetTempPath(), "m106f_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
            try
            {
                long diffs = 0, cmp = 0, applied = 0; var seeds = new ulong[] { 1, 20261115, 0xDEADBEEFUL };
                var sb = new StringBuilder("{\n  \"metric\": \"M-106@v1\",\n  \"part\": \"refit\",\n  \"cycles\": 3,\n  \"seeds\": [");
                for (int i = 0; i < seeds.Length; i++)
                {
                    var r = RunSeed(seeds[i], dir);
                    Assert.AreEqual(0, r.Differences, "seed " + seeds[i]);
                    Assert.AreEqual(12, r.Comparisons);
                    diffs += r.Differences; cmp += r.Comparisons; applied += r.Applied;
                    sb.Append(i > 0 ? "," : "").Append("\n    {\"seed\": ").Append(seeds[i]).Append(", \"comparisons\": ").Append(r.Comparisons).Append(", \"differences\": ").Append(r.Differences)
                      .Append(", \"refits_applied\": ").Append(r.Applied).Append(", \"refits_refused\": ").Append(r.Refused).Append('}');
                }
                sb.Append("\n  ],\n  \"total_comparisons\": ").Append(cmp).Append(",\n  \"total_differences\": ").Append(diffs).Append("\n}\n");
                Assert.AreEqual(0, diffs); Assert.AreEqual(36, cmp); Assert.Greater(applied, 10, "the scripted runs apply refits, so the saved assignments change");
                string ev = Environment.GetEnvironmentVariable("KAIJU_EVIDENCE_DIR");
                if (!string.IsNullOrEmpty(ev)) { Directory.CreateDirectory(ev); File.WriteAllText(Path.Combine(ev, "m106_refit.json"), sb.ToString()); }
            }
            finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
        }

        [Test] public void M106_FlippedBitIsCounted()
        {
            var s = new Session(Path.Combine(Path.GetTempPath(), "m106ff_" + Guid.NewGuid().ToString("N") + ".json"));
            var d = new Driver(s, 3); d.Sorties();
            var a = Flatten(s); var b = new List<long>(a); b[2] ^= 1L << 17;
            Assert.AreEqual(1, Diff(a, b));
            b = new List<long>(a); b.Add(0); Assert.AreEqual(1, Diff(a, b));
        }
    }
}
