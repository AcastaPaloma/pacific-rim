using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Core;
using Game.Meta.Economy;

namespace Game.Meta.Salvage
{
    /// <summary>
    /// Wreck salvage (R-104 section 5, 7, 10.2): recoverable mass by part from fragmentation and location, material classes incl. kaiju tissue
    /// (throughput-limited, rare part sets as unlocks), building debris as scrap, credit into Meta.Economy under the wreck-credit cap
    /// (12% of the build price for a mech). Claim is idempotent by WreckId. Save section salvage@v1; Read validates everything first and throws
    /// FormatException, leaving the service untouched. Mass is float64 (saved as raw bits); money is whole-USD long.
    /// </summary>
    public sealed class SalvageService : ISalvageService, ISaveParticipant
    {
        const string KeyTotals = "totals";
        sealed class Wreck { public byte Source, Location; public double Available, Recovered; public long Credit, Raw, Step; }
        sealed class Rare { public long Earned, Consumed; }
        sealed class State
        {
            public long TotalCredit;
            public SortedDictionary<long, Wreck> Wrecks = new SortedDictionary<long, Wreck>();
            public SortedDictionary<string, double> Inventory = new SortedDictionary<string, double>(StringComparer.Ordinal);
            public SortedDictionary<string, Rare> Rares = new SortedDictionary<string, Rare>(StringComparer.Ordinal);
        }

        readonly YieldsTable _y;
        readonly IEconomyService _economy;
        readonly IEventBus _bus;
        readonly Dictionary<string, string> _classOfRare = new Dictionary<string, string>(StringComparer.Ordinal);
        State _s = new State();

        public SalvageService(YieldsTable yields, IEconomyService economy, IEventBus bus = null)
        {
            _y = yields ?? throw new ArgumentNullException(nameof(yields));
            _economy = economy ?? throw new ArgumentNullException(nameof(economy));
            _bus = bus;
            foreach (var c in _y.Classes.Values) if (c.RarePart.Length > 0) _classOfRare[c.RarePart] = c.Id;
        }

        public YieldsTable Yields => _y;
        public long TotalCreditUsd => _s.TotalCredit;
        public int WreckCount => _s.Wrecks.Count;
        public bool IsClaimed(long wreckId) => _s.Wrecks.ContainsKey(wreckId);
        public double InventoryKg(string classId) => classId != null && _s.Inventory.TryGetValue(classId, out double m) ? m : 0.0;
        public long RareAvailable(string rarePartId) => rarePartId != null && _s.Rares.TryGetValue(rarePartId, out var r) ? r.Earned - r.Consumed : 0;

        public bool TryConsumeRare(string rarePartId, long count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (rarePartId == null || !_s.Rares.TryGetValue(rarePartId, out var r) || r.Earned - r.Consumed < count) return false;
            r.Consumed += count;
            return true;
        }

        // ---------------------------------------------------------------- assess (pure)

        static string KindOf(SalvageSourceKind k) => k == SalvageSourceKind.Mech ? "mech" : k == SalvageSourceKind.Kaiju ? "tissue" : "debris";

        static long ToUsd(double v)
        {
            if (!(v > 0)) return 0;
            if (v > 4.0e18) throw new OverflowException("salvage credit overflow");
            return (long)Math.Floor(v);
        }

        public SalvageResult Assess(SalvageInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (input.WreckId < 1) throw new ArgumentException("WreckId must be positive");
            if (input.SourceKind < SalvageSourceKind.Mech || input.SourceKind > SalvageSourceKind.Building) throw new ArgumentException("unknown source kind " + input.SourceKind);
            if (input.Location < SalvageLocation.Land || input.Location > SalvageLocation.DeepChannel) throw new ArgumentException("unknown location " + input.Location);
            if (input.Parts == null || input.Parts.Length == 0) throw new ArgumentException("a wreck needs at least one part");
            if (!(input.FallEnergyJPerKg >= 0) || double.IsInfinity(input.FallEnergyJPerKg)) throw new ArgumentException("FallEnergyJPerKg must be finite and >= 0");
            if (input.SourceKind == SalvageSourceKind.Mech && input.BuildPriceUsd <= 0) throw new ArgumentException("a mech wreck needs BuildPriceUsd > 0 (credit cap)");
            if (input.BuildPriceUsd < 0) throw new ArgumentException("BuildPriceUsd must be >= 0");

            string kind = KindOf(input.SourceKind);
            double eta = _y.EtaLocation[(int)input.Location];
            int n = input.Parts.Length;
            var cls = new MaterialClass[n];
            var rec = new double[n];
            var frag = new double[n];
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < n; i++)
            {
                var p = input.Parts[i];
                if (string.IsNullOrEmpty(p.PartId) || !seen.Add(p.PartId)) throw new ArgumentException("empty or duplicate part id '" + p.PartId + "'");
                if (p.ClassId == null || !_y.Classes.TryGetValue(p.ClassId, out cls[i])) throw new ArgumentException("unknown class '" + p.ClassId + "' in part " + p.PartId);
                if (cls[i].Kind != kind) throw new ArgumentException("class " + p.ClassId + " (" + cls[i].Kind + ") does not fit a " + input.SourceKind + " wreck");
                if (!(p.MassKg > 0) || double.IsInfinity(p.MassKg)) throw new ArgumentException("part " + p.PartId + ": mass must be finite and > 0");
                if (!(p.AbsorbedEnergyJ >= 0) || double.IsInfinity(p.AbsorbedEnergyJ)) throw new ArgumentException("part " + p.PartId + ": absorbed energy must be finite and >= 0");
                if (p.PriceUsd < 0) throw new ArgumentException("part " + p.PartId + ": negative price");
                if (kind == "mech" && (!(p.MaxIntegrity > 0) || !(p.Integrity >= 0) || double.IsInfinity(p.MaxIntegrity) || double.IsInfinity(p.Integrity)))
                    throw new ArgumentException("part " + p.PartId + ": bad integrity");
                frag[i] = cls[i].Fragments ? _y.FragmentedFraction(input.FallEnergyJPerKg + p.AbsorbedEnergyJ / p.MassKg) : 0.0;
                rec[i] = p.MassKg * (1.0 - frag[i]) * eta;
            }

            // Kaiju tissue is throughput-limited (R-104 5.4): each class takes at most its priority share of its mass, and the classes draw from
            // one shared tonnage budget in priority order.
            if (kind == "tissue")
            {
                var order = new List<int>(n); for (int i = 0; i < n; i++) order.Add(i);
                order.Sort((a, b) =>
                {
                    int c = cls[a].Priority.CompareTo(cls[b].Priority);
                    if (c != 0) return c;
                    c = string.CompareOrdinal(input.Parts[a].PartId, input.Parts[b].PartId);
                    return c != 0 ? c : a.CompareTo(b);
                });
                double pool = _y.ThroughputBudgetKg;
                foreach (int i in order)
                {
                    double take = Math.Min(rec[i], input.Parts[i].MassKg * (cls[i].PriorityShareBps / 10000.0));
                    take = Math.Min(take, pool);
                    rec[i] = take; pool -= take;
                }
            }

            var res = new SalvageResult { WreckId = input.WreckId, SourceKind = input.SourceKind, Location = input.Location, Parts = new PartRecovery[n] };
            long raw = 0; double avail = 0, got = 0;
            for (int i = 0; i < n; i++)
            {
                var p = input.Parts[i]; var c = cls[i];
                long usd = 0; bool reused = false;
                if (kind == "mech")
                {
                    double integ = Math.Min(1.0, p.Integrity / p.MaxIntegrity);
                    if (integ * 10000.0 > _y.ReuseMinIntegrityBps && p.PriceUsd > 0)
                    {
                        usd = ToUsd((double)p.PriceUsd * (_y.ReuseBps / 10000.0) * integ * (rec[i] / p.MassKg)); reused = true;
                    }
                    else if (!c.ReuseOnly) usd = ToUsd(rec[i] / 1000.0 * c.ScrapUsdPerTonne);
                }
                else usd = ToUsd(rec[i] / 1000.0 * c.ScrapUsdPerTonne);
                raw = checked(raw + usd);
                avail += p.MassKg; got += rec[i];
                res.Parts[i] = new PartRecovery(p.PartId, p.ClassId, p.MassKg, frag[i], rec[i], usd, reused);
            }
            res.AvailableKg = avail; res.RecoveredKg = got; res.RawCreditUsd = raw; res.CreditUsd = raw;
            if (input.SourceKind == SalvageSourceKind.Mech)
            {
                long cap = checked(input.BuildPriceUsd * _y.CreditCapBps) / 10000;
                if (raw > cap) { res.CreditUsd = cap; res.CapApplied = true; }
            }
            return res;
        }

        // ---------------------------------------------------------------- claim

        public SalvageClaim Claim(SalvageInput input, long step)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (input.WreckId >= 1 && _s.Wrecks.TryGetValue(input.WreckId, out var old))
                return new SalvageClaim { WreckId = input.WreckId, Replay = true, SourceKind = (SalvageSourceKind)old.Source, Location = (SalvageLocation)old.Location, RecoveredKg = old.Recovered, CreditUsd = old.Credit };
            var r = Assess(input);
            if (r.CreditUsd > 0) _economy.Credit(r.CreditUsd, TransactionReason.SalvageCredit, step);   // may throw: nothing of ours is changed yet
            _s.TotalCredit = checked(_s.TotalCredit + r.CreditUsd);
            _s.Wrecks.Add(r.WreckId, new Wreck { Source = (byte)r.SourceKind, Location = (byte)r.Location, Available = r.AvailableKg, Recovered = r.RecoveredKg, Credit = r.CreditUsd, Raw = r.RawCreditUsd, Step = step });
            foreach (var p in r.Parts)
            {
                _s.Inventory.TryGetValue(p.ClassId, out double m);
                _s.Inventory[p.ClassId] = m + p.RecoveredKg;
            }
            foreach (var c in _y.Classes.Values)
            {
                if (c.RarePart.Length == 0 || !_s.Inventory.TryGetValue(c.Id, out double m)) continue;
                long earned = (long)Math.Floor(m / c.RareMinMassKg);
                if (earned <= 0) continue;
                if (!_s.Rares.TryGetValue(c.RarePart, out var rr)) _s.Rares.Add(c.RarePart, rr = new Rare());
                rr.Earned = earned;
            }
            var evt = new SalvageRecovered { WreckId = r.WreckId, SourceKind = (byte)r.SourceKind, Location = (byte)r.Location, RecoveredKg = r.RecoveredKg, CreditUsd = r.CreditUsd };
            _bus?.Publish(in evt);
            return new SalvageClaim { WreckId = r.WreckId, Replay = false, SourceKind = r.SourceKind, Location = r.Location, RecoveredKg = r.RecoveredKg, CreditUsd = r.CreditUsd, Result = r };
        }

        // ---------------------------------------------------------------- save: salvage@v1

        public string SectionName => SalvageContractVersions.SectionName;
        public int SectionVersion => SalvageContractVersions.SaveSection;

        static string Pad(long v) => v.ToString("D12", CultureInfo.InvariantCulture);

        public void Write(SaveSection section)
        {
            section.Add(KeyTotals).Set("credit_usd", SaveValue.Of(_s.TotalCredit)).Set("wrecks", SaveValue.Of((long)_s.Wrecks.Count));
            foreach (var kv in _s.Wrecks)
            {
                var w = kv.Value;
                section.Add("wreck:" + Pad(kv.Key)).Set("source", SaveValue.Of((long)w.Source)).Set("location", SaveValue.Of((long)w.Location))
                    .Set("available_kg", SaveValue.Of(w.Available)).Set("recovered_kg", SaveValue.Of(w.Recovered))
                    .Set("credit_usd", SaveValue.Of(w.Credit)).Set("raw_credit_usd", SaveValue.Of(w.Raw)).Set("step", SaveValue.Of(w.Step));
            }
            foreach (var kv in _s.Inventory) section.Add("inv:" + kv.Key).Set("mass_kg", SaveValue.Of(kv.Value));
            foreach (var kv in _s.Rares) section.Add("rare:" + kv.Key).Set("earned", SaveValue.Of(kv.Value.Earned)).Set("consumed", SaveValue.Of(kv.Value.Consumed));
        }

        public void Read(SaveSection section)
        {
            var n = new State();
            if (!section.TryGet(KeyTotals, out var tr)) throw new FormatException("salvage: no totals record");
            n.TotalCredit = Int(tr, "credit_usd"); long wreckCount = Int(tr, "wrecks");
            if (n.TotalCredit < 0 || wreckCount < 0) throw new FormatException("salvage: negative totals");
            long sumCredit = 0; double sumRec = 0;
            foreach (var rec in section.Records)
            {
                string k = rec.Key;
                if (k == KeyTotals) continue;
                if (k.StartsWith("wreck:", StringComparison.Ordinal))
                {
                    long id = ParseId(k, 6, k.Length);
                    if (id < 1) throw new FormatException("salvage: bad wreck id " + k);
                    var w = new Wreck();
                    long src = Int(rec, "source"), loc = Int(rec, "location");
                    if (src < 0 || src > (long)SalvageSourceKind.Building || loc < 0 || loc > (long)SalvageLocation.DeepChannel) throw new FormatException("salvage: bad enum in " + k);
                    w.Source = (byte)src; w.Location = (byte)loc;
                    w.Available = Real(rec, "available_kg"); w.Recovered = Real(rec, "recovered_kg");
                    w.Credit = Int(rec, "credit_usd"); w.Raw = Int(rec, "raw_credit_usd"); w.Step = Int(rec, "step");
                    if (w.Recovered > w.Available) throw new FormatException("salvage: recovered exceeds available in " + k);
                    if (w.Credit < 0 || w.Raw < w.Credit) throw new FormatException("salvage: bad credit in " + k);
                    if (w.Credit != w.Raw && w.Source != (byte)SalvageSourceKind.Mech) throw new FormatException("salvage: only a mech credit is capped (" + k + ")");
                    sumCredit = checked(sumCredit + w.Credit); sumRec += w.Recovered;
                    n.Wrecks.Add(id, w);
                }
                else if (k.StartsWith("inv:", StringComparison.Ordinal))
                {
                    string cid = k.Substring(4);
                    if (!_y.Classes.ContainsKey(cid)) throw new FormatException("salvage: unknown class in " + k);
                    n.Inventory.Add(cid, Real(rec, "mass_kg"));
                }
                else if (k.StartsWith("rare:", StringComparison.Ordinal))
                {
                    string pid = k.Substring(5);
                    if (!_classOfRare.ContainsKey(pid)) throw new FormatException("salvage: unknown rare part in " + k);
                    var r = new Rare { Earned = Int(rec, "earned"), Consumed = Int(rec, "consumed") };
                    if (r.Earned < 0 || r.Consumed < 0 || r.Consumed > r.Earned) throw new FormatException("salvage: bad rare counts in " + k);
                    n.Rares.Add(pid, r);
                }
                else throw new FormatException("salvage: unknown record " + k);
            }
            if (wreckCount != n.Wrecks.Count) throw new FormatException("salvage: wreck count does not match the records");
            if (sumCredit != n.TotalCredit) throw new FormatException("salvage: total credit does not equal the sum over wrecks");
            double sumInv = 0; foreach (var m in n.Inventory.Values) sumInv += m;
            if (Math.Abs(sumInv - sumRec) > 1e-9 * Math.Max(1.0, Math.Max(sumInv, sumRec))) throw new FormatException("salvage: inventory does not equal the mass recovered over wrecks");
            foreach (var c in _y.Classes.Values)
            {
                if (c.RarePart.Length == 0) continue;
                long expected = n.Inventory.TryGetValue(c.Id, out double m) ? (long)Math.Floor(m / c.RareMinMassKg) : 0;
                long stored = n.Rares.TryGetValue(c.RarePart, out var r) ? r.Earned : 0;
                if (expected != stored) throw new FormatException("salvage: rare sets of " + c.RarePart + " do not match the inventory");
            }
            _s = n;
        }

        static long ParseId(string key, int from, int to)
        {
            if (to - from < 1 || to - from > 18) throw new FormatException("salvage: bad key " + key);
            long v = 0;
            for (int i = from; i < to; i++) { char ch = key[i]; if (ch < '0' || ch > '9') throw new FormatException("salvage: bad key " + key); v = v * 10 + (ch - '0'); }
            return v;
        }

        static long Int(SaveRecord r, string f)
        {
            if (!r.TryGet(f, out var v) || v.Kind != SaveValueKind.Int64) throw new FormatException("salvage: " + r.Key + " lacks Int64 '" + f + "'");
            return v.AsInt64;
        }

        static double Real(SaveRecord r, string f)
        {
            if (!r.TryGet(f, out var v) || v.Kind != SaveValueKind.Float64) throw new FormatException("salvage: " + r.Key + " lacks Float64 '" + f + "'");
            double d = v.AsFloat64;
            if (double.IsNaN(d) || double.IsInfinity(d) || d < 0) throw new FormatException("salvage: " + r.Key + " '" + f + "' must be finite and >= 0");
            return d;
        }
    }

    public static class SalvageModule
    {
        /// <summary>Returns the service it registered, or null when meta.salvage is off or there is no economy (nothing registered). The composition root registers it with SaveSystem.</summary>
        public static SalvageService Install(IServiceRegistry registry, IFeatureFlags flags, YieldsTable yields, IEconomyService economy, IEventBus bus = null)
        {
            if (registry == null || flags == null || yields == null || economy == null || !flags.IsOn(SalvageFlags.Salvage)) return null;
            var service = new SalvageService(yields, economy, bus);
            registry.Register<ISalvageService>(service);
            return service;
        }
    }
}
