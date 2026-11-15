using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Core;
using Game.Meta.Economy;
using Game.Meta.Salvage;

namespace Game.Meta.Refit
{
    /// <summary>Part price = sum over price classes of mass x class installed price per tonne (R-104 section 2), the same rule and rounding as Meta.Economy; sell-back in bps.</summary>
    public sealed class ClassPricing : IPartPricing
    {
        readonly SortedDictionary<string, long> _usdPerTonne;
        readonly int _sellBackBps;

        public ClassPricing(IDictionary<string, long> usdPerTonneByClass, int sellBackBps)
        {
            if (usdPerTonneByClass == null || usdPerTonneByClass.Count == 0) throw new ArgumentException("no price classes");
            if (sellBackBps < 0 || sellBackBps > 10000) throw new ArgumentOutOfRangeException(nameof(sellBackBps));
            _usdPerTonne = new SortedDictionary<string, long>(usdPerTonneByClass, StringComparer.Ordinal);
            foreach (var kv in _usdPerTonne) if (kv.Value <= 0) throw new ArgumentException("price of class " + kv.Key + " must be positive");
            _sellBackBps = sellBackBps;
        }

        public long PartPriceUsd(PartDef part)
        {
            if (part == null) throw new ArgumentNullException(nameof(part));
            long sum = 0;
            foreach (var kv in part.CompositionKg)
            {
                if (!_usdPerTonne.TryGetValue(kv.Key, out long perTonne)) throw new ArgumentException("part " + part.Id + " uses unpriced class " + kv.Key);
                sum = checked(sum + (checked(kv.Value * perTonne) * 2 + 1000) / 2000);   // round half up, as PriceTable.MulDivRound(kg, usd/t, 1000)
            }
            return sum;
        }

        public long SellBackUsd(long priceUsd) => checked(priceUsd * _sellBackBps) / 10000;
    }

    /// <summary>
    /// Meta.Refit (R-104 sections 9, 10.4; D-017, D-019, D-025, F-062). Validates builds from physics (mass, power, heat, actuator torque, CoM, tilt margin), builds
    /// MechBuildSpec@v1, and applies hangar refits through Meta.Economy (new parts at price, taken-out parts at the 40% sell-back, city-issued parts at nothing) and
    /// Meta.Salvage (rare part sets consumed on fitting). Save section refit@v1: assignments, wear, ownership; Read validates everything first and throws FormatException.
    /// </summary>
    public sealed class RefitService : IRefitService, ISaveParticipant
    {
        const string KeyTotals = "totals";

        sealed class Mech
        {
            public EntityId Id; public int Slot; public string Chassis; public long SpecHash, Refits, RegisteredStep, AppliedStep;
            public string[] Parts; public double[] Wear; public bool[] Issued;
        }
        sealed class State { public SortedDictionary<ulong, Mech> Mechs = new SortedDictionary<ulong, Mech>(); }

        readonly IPartCatalog _parts;
        readonly IChassisCatalog _chassis;
        readonly IPartPricing _pricing;
        readonly IEconomyService _economy;
        readonly IEventBus _bus;
        readonly ISalvageService _salvage;
        readonly IRefitGate _gate;
        State _s = new State();

        public RefitService(IPartCatalog parts, IChassisCatalog chassis, IPartPricing pricing, IEconomyService economy, IEventBus bus = null, ISalvageService salvage = null, IRefitGate gate = null)
        {
            _parts = parts ?? throw new ArgumentNullException(nameof(parts));
            _chassis = chassis ?? throw new ArgumentNullException(nameof(chassis));
            _pricing = pricing ?? throw new ArgumentNullException(nameof(pricing));
            _economy = economy ?? throw new ArgumentNullException(nameof(economy));
            _bus = bus; _salvage = salvage; _gate = gate;
        }

        public IPartCatalog Parts => _parts;
        public IChassisCatalog Chassis => _chassis;

        // ---------------------------------------------------------------- pure queries

        public IReadOnlyDictionary<string, string> DefaultBuild(string chassisId)
        {
            var ch = _chassis.Get(chassisId) ?? throw new ArgumentException("Unknown chassis '" + chassisId + "'");
            var d = new SortedDictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < ch.Slots.Length; i++) d[ch.Slots[i].Name] = ch.DefaultBuild[i];
            return d;
        }

        public RefitReport Validate(string chassisId, IReadOnlyDictionary<string, string> assignment)
        {
            var ch = _chassis.Get(chassisId);
            if (ch == null) return Fail(RefitViolation.UnknownChassis, chassisId ?? "", "unknown chassis");
            return ValidateCore(ch, assignment, out _);
        }

        public bool TryBuild(string chassisId, IReadOnlyDictionary<string, string> assignment, out MechBuildSpec spec, out RefitReport report)
        {
            report = Validate(chassisId, assignment);
            spec = report.Ok ? report.Spec : null;
            return report.Ok;
        }

        public long PriceOf(string chassisId, IReadOnlyDictionary<string, string> assignment)
        {
            var ch = _chassis.Get(chassisId) ?? throw new ArgumentException("Unknown chassis '" + chassisId + "'");
            var rep = ValidateStructure(ch, assignment, out var parts);
            if (rep != null) throw new ArgumentException("build is structurally invalid: " + rep.Detail);
            long sum = 0;
            foreach (var p in parts) sum = checked(sum + _pricing.PartPriceUsd(p));
            return sum;
        }

        static RefitReport Fail(RefitViolation v, string subject, string detail)
            => new RefitReport { Ok = false, FirstViolation = v, Subject = subject, Detail = detail };

        RefitReport ValidateStructure(ChassisDef ch, IReadOnlyDictionary<string, string> a, out PartDef[] parts)
        {
            parts = null;
            if (a == null) return Fail(RefitViolation.SlotMissing, "", "no assignment");
            var keys = new List<string>(a.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var k in keys) if (ch.SlotIndex(k) < 0) return Fail(RefitViolation.UnknownSlot, k, "the " + ch.Id + " frame has no slot '" + k + "'");
            var arr = new PartDef[ch.Slots.Length];
            for (int i = 0; i < ch.Slots.Length; i++)
            {
                var s = ch.Slots[i];
                if (!a.TryGetValue(s.Name, out string pid) || pid == null) return Fail(RefitViolation.SlotMissing, s.Name, "slot '" + s.Name + "' has no part");
                var p = _parts.Get(pid);
                if (p == null) return Fail(RefitViolation.UnknownPart, pid, "no part '" + pid + "' in the catalogue");
                if (!string.Equals(p.SlotType, s.Type, StringComparison.Ordinal)) return Fail(RefitViolation.WrongSlotType, s.Name, "part '" + pid + "' is a " + p.SlotType + ", slot '" + s.Name + "' takes a " + s.Type);
                if (Array.IndexOf(p.Chassis, ch.Id) < 0) return Fail(RefitViolation.WrongChassis, s.Name, "part '" + pid + "' does not fit the " + ch.Id + " chassis");
                arr[i] = p;
            }
            parts = arr;
            return null;
        }

        RefitReport ValidateCore(ChassisDef ch, IReadOnlyDictionary<string, string> a, out PartDef[] parts)
        {
            var bad = ValidateStructure(ch, a, out parts);
            if (bad != null) return bad;
            var ev = RefitPhysics.Evaluate(ch, parts);
            var rep = new RefitReport { Ok = true, FirstViolation = RefitViolation.None, Spec = RefitPhysics.ToSpec(ch, parts, ev) };
            rep.Margins.AddRange(ev.Margins);
            foreach (var m in ev.Margins)
            {
                if (m.Ok) continue;
                rep.Ok = false; rep.FirstViolation = m.Kind; rep.Subject = m.Subject;
                rep.Detail = m.Kind + ": value " + m.Value.ToString("R", CultureInfo.InvariantCulture) + " against limit " + m.Limit.ToString("R", CultureInfo.InvariantCulture);
                break;
            }
            return rep;
        }

        // ---------------------------------------------------------------- registered mechs

        public MechBuildSpec Register(EntityId mech, PlayerSlot slot, string chassisId, IReadOnlyDictionary<string, string> assignment, bool cityIssued, long step)
        {
            if (!mech.IsValid) throw new ArgumentException("invalid mech id");
            if (!slot.IsValid) throw new ArgumentException("invalid slot");
            if (step < 0) throw new ArgumentOutOfRangeException(nameof(step));
            if (_s.Mechs.ContainsKey(mech.Value)) throw new ArgumentException(mech + " is already registered");
            var ch = _chassis.Get(chassisId) ?? throw new ArgumentException("Unknown chassis '" + chassisId + "'");
            var rep = ValidateCore(ch, assignment, out var parts);
            if (!rep.Ok) throw new ArgumentException("build is not valid: " + rep.FirstViolation + " " + rep.Subject + " " + rep.Detail);
            foreach (var p in parts) if (p.IsRare) throw new ArgumentException("rare part " + p.Id + " is fitted through Apply, not Register");
            var m = new Mech
            {
                Id = mech, Slot = slot.Index, Chassis = ch.Id, SpecHash = rep.Spec.Hash, RegisteredStep = step, AppliedStep = step,
                Parts = new string[ch.Slots.Length], Wear = new double[ch.Slots.Length], Issued = new bool[ch.Slots.Length],
            };
            for (int i = 0; i < parts.Length; i++) { m.Parts[i] = parts[i].Id; m.Issued[i] = cityIssued; }
            _s.Mechs.Add(mech.Value, m);
            return rep.Spec;
        }

        public bool IsRegistered(EntityId mech) => _s.Mechs.ContainsKey(mech.Value);

        public MechBuild Get(EntityId mech)
        {
            if (!_s.Mechs.TryGetValue(mech.Value, out var m)) return null;
            var ch = _chassis.Get(m.Chassis);
            var b = new MechBuild
            {
                MechId = m.Id, Slot = PlayerSlot.FromIndex(m.Slot), Chassis = m.Chassis, SpecHash = m.SpecHash, Refits = m.Refits,
                Parts = new SortedDictionary<string, string>(StringComparer.Ordinal), Wear = new SortedDictionary<string, double>(StringComparer.Ordinal),
                Issued = new SortedDictionary<string, bool>(StringComparer.Ordinal),
            };
            for (int i = 0; i < ch.Slots.Length; i++) { b.Parts[ch.Slots[i].Name] = m.Parts[i]; b.Wear[ch.Slots[i].Name] = m.Wear[i]; b.Issued[ch.Slots[i].Name] = m.Issued[i]; }
            return b;
        }

        public RefitResult Quote(EntityId mech, IReadOnlyDictionary<string, string> assignment) => Plan(mech, assignment, 0, false);

        public RefitResult Apply(EntityId mech, IReadOnlyDictionary<string, string> assignment, long step)
        {
            if (step < 0) throw new ArgumentOutOfRangeException(nameof(step));
            return Plan(mech, assignment, step, true);
        }

        RefitResult Plan(EntityId mechId, IReadOnlyDictionary<string, string> assignment, long step, bool commit)
        {
            if (!_s.Mechs.TryGetValue(mechId.Value, out var m)) return new RefitResult { Status = RefitStatus.UnknownMech };
            var slot = PlayerSlot.FromIndex(m.Slot);
            if (_gate != null && !_gate.CanRefit(slot, mechId)) return new RefitResult { Status = RefitStatus.NotAllowed };
            var ch = _chassis.Get(m.Chassis);
            var rep = ValidateCore(ch, assignment, out var parts);
            if (!rep.Ok) return new RefitResult { Status = RefitStatus.Invalid, Report = rep, Spec = rep.Spec };

            long net = 0; int changed = 0;
            var needRare = new SortedDictionary<string, long>(StringComparer.Ordinal);
            for (int i = 0; i < parts.Length; i++)
            {
                if (string.Equals(parts[i].Id, m.Parts[i], StringComparison.Ordinal)) continue;
                changed++;
                net = checked(net + _pricing.PartPriceUsd(parts[i]));
                if (!m.Issued[i]) net = checked(net - _pricing.SellBackUsd(_pricing.PartPriceUsd(_parts.Get(m.Parts[i]))));
                if (parts[i].IsRare) { needRare.TryGetValue(parts[i].RarePart, out long c); needRare[parts[i].RarePart] = c + 1; }
            }
            var res = new RefitResult { Report = rep, Spec = rep.Spec, CostUsd = net, SlotsChanged = changed };
            if (changed == 0) { res.Status = RefitStatus.NoChange; return res; }
            long rareCount = 0;
            foreach (var kv in needRare)
            {
                rareCount += kv.Value;
                if (_salvage == null || _salvage.RareAvailable(kv.Key) < kv.Value) { res.Status = RefitStatus.RareUnavailable; return res; }
            }
            res.RareConsumed = (int)rareCount;
            if (net > 0 && _economy.Funds < net) { res.Status = RefitStatus.InsufficientFunds; return res; }
            res.Status = RefitStatus.Applied;
            if (!commit) return res;

            // Atomic from here: the purchase is the last thing that can refuse.
            if (net > 0 && !_economy.TryPurchase(net, TransactionReason.Purchase, step)) { res.Status = RefitStatus.InsufficientFunds; return res; }
            if (net < 0) _economy.Credit(-net, TransactionReason.Sale, step);
            foreach (var kv in needRare) _salvage.TryConsumeRare(kv.Key, kv.Value);
            for (int i = 0; i < parts.Length; i++)
            {
                if (string.Equals(parts[i].Id, m.Parts[i], StringComparison.Ordinal)) continue;
                m.Parts[i] = parts[i].Id; m.Wear[i] = 0.0; m.Issued[i] = false;
            }
            m.SpecHash = rep.Spec.Hash; m.Refits = checked(m.Refits + 1); m.AppliedStep = step;
            var evt = new RefitApplied { Mech = mechId, Slot = slot, CostUsd = net, SpecHash = m.SpecHash, Step = step };
            _bus?.Publish(in evt);
            return res;
        }

        public void AddWear(EntityId mech, string slotName, double amount)
        {
            if (double.IsNaN(amount) || double.IsInfinity(amount) || amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (!_s.Mechs.TryGetValue(mech.Value, out var m)) throw new ArgumentException(mech + " is not registered");
            int i = _chassis.Get(m.Chassis).SlotIndex(slotName);
            if (i < 0) throw new ArgumentException("unknown slot '" + slotName + "'");
            m.Wear[i] = Math.Min(1.0, m.Wear[i] + amount);
        }

        // ---------------------------------------------------------------- save: refit@v1

        public string SectionName => RefitContractVersions.SectionName;
        public int SectionVersion => RefitContractVersions.SaveSection;

        static string Pad(ulong v) => v.ToString("D12", CultureInfo.InvariantCulture);

        public void Write(SaveSection section)
        {
            section.Add(KeyTotals).Set("mechs", SaveValue.Of((long)_s.Mechs.Count));
            foreach (var m in _s.Mechs.Values)
            {
                var ch = _chassis.Get(m.Chassis);
                string key = "mech:" + Pad(m.Id.Value);
                section.Add(key).Set("slot", SaveValue.Of((long)m.Slot)).Set("chassis", SaveValue.Of(m.Chassis)).Set("spec_hash", SaveValue.Of(m.SpecHash))
                    .Set("refits", SaveValue.Of(m.Refits)).Set("registered_step", SaveValue.Of(m.RegisteredStep)).Set("applied_step", SaveValue.Of(m.AppliedStep));
                for (int i = 0; i < ch.Slots.Length; i++)
                    section.Add(key + "/slot:" + ch.Slots[i].Name).Set("part_id", SaveValue.Of(m.Parts[i])).Set("wear", SaveValue.Of(m.Wear[i])).Set("issued", SaveValue.Of(m.Issued[i]));
            }
        }

        public void Read(SaveSection section)
        {
            var n = new State();
            if (!section.TryGet(KeyTotals, out var tr)) throw new FormatException("refit: no totals record");
            long mechCount = Int(tr, "mechs");
            if (mechCount < 0) throw new FormatException("refit: bad totals");
            var slotRecs = new Dictionary<ulong, Dictionary<string, SaveRecord>>();
            foreach (var rec in section.Records)
            {
                string k = rec.Key;
                if (k == KeyTotals) continue;
                if (!k.StartsWith("mech:", StringComparison.Ordinal)) throw new FormatException("refit: unknown record " + k);
                int slash = k.IndexOf('/');
                if (slash < 0)
                {
                    ulong id = ParseId(k, 5, k.Length);
                    if (id == 0) throw new FormatException("refit: bad mech id " + k);
                    long slot = Int(rec, "slot");
                    if (slot < 0 || slot >= int.MaxValue - 1) throw new FormatException("refit: bad slot in " + k);
                    var m = new Mech { Id = EntityId.FromValue(id), Slot = (int)slot, Chassis = Str(rec, "chassis"), SpecHash = Int(rec, "spec_hash"), Refits = Int(rec, "refits"),
                                       RegisteredStep = Int(rec, "registered_step"), AppliedStep = Int(rec, "applied_step") };
                    if (m.Refits < 0 || m.RegisteredStep < 0 || m.AppliedStep < m.RegisteredStep) throw new FormatException("refit: bad counters in " + k);
                    n.Mechs.Add(id, m);
                }
                else
                {
                    ulong id = ParseId(k, 5, slash);
                    if (string.CompareOrdinal(k, slash, "/slot:", 0, 6) != 0) throw new FormatException("refit: bad slot record key " + k);
                    string name = k.Substring(slash + 6);
                    if (!slotRecs.TryGetValue(id, out var d)) { d = new Dictionary<string, SaveRecord>(StringComparer.Ordinal); slotRecs[id] = d; }
                    d.Add(name, rec);
                }
            }
            if (mechCount != n.Mechs.Count) throw new FormatException("refit: mech count does not match the records");
            foreach (var kv in n.Mechs)
            {
                var m = kv.Value;
                var ch = _chassis.Get(m.Chassis) ?? throw new FormatException("refit: mech " + Pad(kv.Key) + " has unknown chassis " + m.Chassis);
                if (!slotRecs.TryGetValue(kv.Key, out var d) || d.Count != ch.Slots.Length) throw new FormatException("refit: mech " + Pad(kv.Key) + " lacks slot records");
                m.Parts = new string[ch.Slots.Length]; m.Wear = new double[ch.Slots.Length]; m.Issued = new bool[ch.Slots.Length];
                var arr = new PartDef[ch.Slots.Length];
                for (int i = 0; i < ch.Slots.Length; i++)
                {
                    if (!d.TryGetValue(ch.Slots[i].Name, out var rec)) throw new FormatException("refit: mech " + Pad(kv.Key) + " lacks slot " + ch.Slots[i].Name);
                    string pid = Str(rec, "part_id");
                    var p = _parts.Get(pid);
                    if (p == null || p.SlotType != ch.Slots[i].Type || Array.IndexOf(p.Chassis, ch.Id) < 0) throw new FormatException("refit: part " + pid + " does not fit slot " + ch.Slots[i].Name);
                    double w = Real(rec, "wear");
                    if (w > 1.0) throw new FormatException("refit: wear above 1 in " + rec.Key);
                    m.Parts[i] = pid; m.Wear[i] = w; m.Issued[i] = Bool(rec, "issued"); arr[i] = p;
                }
                var spec = RefitPhysics.ToSpec(ch, arr, RefitPhysics.Evaluate(ch, arr));
                if (spec.Hash != m.SpecHash) throw new FormatException("refit: spec hash of mech " + Pad(kv.Key) + " does not match its parts (data changed or save corrupt)");
            }
            foreach (var id in slotRecs.Keys) if (!n.Mechs.ContainsKey(id)) throw new FormatException("refit: slot records without a mech header (" + Pad(id) + ")");
            _s = n;
        }

        static ulong ParseId(string key, int from, int to)
        {
            if (to - from < 1 || to - from > 19) throw new FormatException("refit: bad key " + key);
            ulong v = 0;
            for (int i = from; i < to; i++) { char ch = key[i]; if (ch < '0' || ch > '9') throw new FormatException("refit: bad key " + key); v = checked(v * 10 + (ulong)(ch - '0')); }
            return v;
        }

        static long Int(SaveRecord r, string f)
        {
            if (!r.TryGet(f, out var v) || v.Kind != SaveValueKind.Int64) throw new FormatException("refit: " + r.Key + " lacks Int64 '" + f + "'");
            return v.AsInt64;
        }

        static bool Bool(SaveRecord r, string f)
        {
            if (!r.TryGet(f, out var v) || v.Kind != SaveValueKind.Bool) throw new FormatException("refit: " + r.Key + " lacks Bool '" + f + "'");
            return v.AsBool;
        }

        static string Str(SaveRecord r, string f)
        {
            if (!r.TryGet(f, out var v) || v.Kind != SaveValueKind.String) throw new FormatException("refit: " + r.Key + " lacks String '" + f + "'");
            return v.AsString;
        }

        static double Real(SaveRecord r, string f)
        {
            if (!r.TryGet(f, out var v) || v.Kind != SaveValueKind.Float64) throw new FormatException("refit: " + r.Key + " lacks Float64 '" + f + "'");
            double d = v.AsFloat64;
            if (double.IsNaN(d) || double.IsInfinity(d) || d < 0) throw new FormatException("refit: " + r.Key + " '" + f + "' must be finite and >= 0");
            return d;
        }
    }

    public static class RefitModule
    {
        /// <summary>Returns the service it registered, or null when meta.refit is off or an argument is missing (nothing registered). The composition root registers it with SaveSystem.</summary>
        public static RefitService Install(IServiceRegistry registry, IFeatureFlags flags, IPartCatalog parts, IChassisCatalog chassis, IPartPricing pricing, IEconomyService economy,
                                           IEventBus bus = null, ISalvageService salvage = null, IRefitGate gate = null)
        {
            if (registry == null || flags == null || parts == null || chassis == null || pricing == null || economy == null || !flags.IsOn(RefitFlags.Refit)) return null;
            var service = new RefitService(parts, chassis, pricing, economy, bus, salvage, gate);
            registry.Register<IRefitService>(service);
            return service;
        }
    }
}
