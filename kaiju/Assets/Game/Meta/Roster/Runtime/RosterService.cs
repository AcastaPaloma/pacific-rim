using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Core;
using Game.Hangar;
using Game.Meta.Economy;

namespace Game.Meta.Roster
{
    /// <summary>Deterministic wreck identity (R-104 section 6 rule 3): a pure function of mech id and loss step, positive and below 10^12 (fits the 12-digit salvage keys).</summary>
    public static class WreckIdentity
    {
        public const long Modulus = 999_999_999_999L;

        public static long Derive(EntityId mechId, long step, int probe = 0)
        {
            unchecked
            {
                ulong z = mechId.Value * 0x9E3779B97F4A7C15UL + (ulong)step * 0xBF58476D1CE4E5B9UL + 0x94D049BB133111EBUL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL; z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL; z ^= z >> 31;
                return (long)((z % (ulong)Modulus + (ulong)probe) % (ulong)Modulus) + 1;
            }
        }
    }

    /// <summary>
    /// Roster and permadeath (R-104 section 6, 10.3, D-020, Q-206). A mech is Lost when a core part is at integrity 0 at fight end or the player scraps it;
    /// Lost is absorbing (no transition leaves it) and the tombstone stays. A slot with no flyable mech after a loss gets the city-funded floor chassis
    /// through Meta.Economy (never stacked). The pilot survives with a strain penalty (no pilot death in v1). Save section roster@v1; Read validates
    /// everything first and throws FormatException, leaving the service untouched.
    /// </summary>
    public sealed class RosterService : IRosterService, ISaveParticipant
    {
        /// <summary>Default core parts: the placeholder body ("body", one part, Destroyed means Lost), the cockpit-torso block and the reactor plant.</summary>
        public static readonly string[] DefaultCoreParts = { "body", "cockpit_torso", "reactor" };

        const string KeyTotals = "totals";

        sealed class Mech
        {
            public EntityId Id; public int Slot; public string Chassis; public MechState State; public long SpecHash, WreckId, Cycles, CreatedStep, LossStep;
            public bool IsFloor; public LossCause Cause;
        }
        sealed class PilotState { public double Strain; public long Sorties, Recovery, Lost; public ulong Active; }
        sealed class State
        {
            public SortedDictionary<ulong, Mech> Mechs = new SortedDictionary<ulong, Mech>();
            public SortedDictionary<int, PilotState> Pilots = new SortedDictionary<int, PilotState>();
            public SortedDictionary<int, long> Floor = new SortedDictionary<int, long>();
            public HashSet<long> WreckIds = new HashSet<long>();
        }

        readonly IEconomyService _economy;
        readonly EntityIdAllocator _alloc;
        readonly IEventBus _bus;
        readonly HashSet<string> _core;
        State _s = new State();

        public RosterService(IEconomyService economy, EntityIdAllocator allocator, IEventBus bus = null, IEnumerable<string> coreParts = null)
        {
            _economy = economy ?? throw new ArgumentNullException(nameof(economy));
            _alloc = allocator ?? throw new ArgumentNullException(nameof(allocator));
            _bus = bus;
            _core = new HashSet<string>(coreParts ?? DefaultCoreParts, StringComparer.Ordinal);
            if (_core.Count == 0) throw new ArgumentException("at least one core part is required");
        }

        public ulong NextEntityValue => _alloc.NextValue;

        // ---------------------------------------------------------------- queries

        static MechInstance Snap(Mech m, PlayerSlot slot) => new MechInstance
        {
            MechId = m.Id, Slot = slot, Chassis = m.Chassis, State = m.State, SpecHash = m.SpecHash, WreckId = m.WreckId, Cycles = m.Cycles,
            IsFloor = m.IsFloor, Cause = m.Cause, CreatedStep = m.CreatedStep, LossStep = m.LossStep,
        };

        public MechInstance Get(EntityId mechId) => _s.Mechs.TryGetValue(mechId.Value, out var m) ? Snap(m, PlayerSlot.FromIndex(m.Slot)) : null;

        public void Mechs(PlayerSlot slot, List<MechInstance> into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            into.Clear();
            if (!slot.IsValid) throw new ArgumentException("invalid slot");
            foreach (var m in _s.Mechs.Values) if (m.Slot == slot.Index) into.Add(Snap(m, slot));
        }

        public int FlyableCount(PlayerSlot slot)
        {
            if (!slot.IsValid) throw new ArgumentException("invalid slot");
            int n = 0;
            foreach (var m in _s.Mechs.Values) if (m.Slot == slot.Index && m.State == MechState.Flyable) n++;
            return n;
        }

        public EntityId ActiveMech(PlayerSlot slot)
        {
            if (!slot.IsValid) throw new ArgumentException("invalid slot");
            if (_s.Pilots.TryGetValue(slot.Index, out var p) && p.Active != 0 && _s.Mechs.TryGetValue(p.Active, out var a) && a.State == MechState.Flyable) return a.Id;
            foreach (var m in _s.Mechs.Values) if (m.Slot == slot.Index && m.State == MechState.Flyable) return m.Id;
            return default;
        }

        public void SetActive(PlayerSlot slot, EntityId mechId)
        {
            if (!slot.IsValid) throw new ArgumentException("invalid slot");
            if (!_s.Mechs.TryGetValue(mechId.Value, out var m) || m.Slot != slot.Index || m.State != MechState.Flyable)
                throw new InvalidOperationException(mechId + " is not a flyable mech of " + slot);
            EnsurePilot(slot.Index).Active = mechId.Value;
        }

        public PilotRecord Pilot(PlayerSlot slot)
        {
            if (!slot.IsValid) throw new ArgumentException("invalid slot");
            var p = EnsurePilot(slot.Index);
            return new PilotRecord { Slot = slot, Strain = p.Strain, Sorties = p.Sorties, RecoverySorties = p.Recovery, MechsLost = p.Lost };
        }

        public long FloorGrants(PlayerSlot slot) => slot.IsValid && _s.Floor.TryGetValue(slot.Index, out long n) ? n : 0;

        PilotState EnsurePilot(int slot)
        {
            if (!_s.Pilots.TryGetValue(slot, out var p)) _s.Pilots.Add(slot, p = new PilotState());
            return p;
        }

        // ---------------------------------------------------------------- commands

        public MechInstance Create(PlayerSlot slot, string chassis, long specHash, long step) => Snap(CreateCore(slot, chassis, specHash, step, false), slot);

        Mech CreateCore(PlayerSlot slot, string chassis, long specHash, long step, bool floor)
        {
            if (!slot.IsValid) throw new ArgumentException("invalid slot");
            if (string.IsNullOrEmpty(chassis)) throw new ArgumentException("chassis is required");
            if (step < 0) throw new ArgumentOutOfRangeException(nameof(step));
            var m = new Mech { Id = _alloc.Allocate(), Slot = slot.Index, Chassis = chassis, State = MechState.Flyable, SpecHash = specHash, CreatedStep = step, IsFloor = floor, Cause = LossCause.None };
            _s.Mechs.Add(m.Id.Value, m);
            var p = EnsurePilot(slot.Index);
            if (p.Active == 0) p.Active = m.Id.Value;
            return m;
        }

        public void SetSpecHash(EntityId mechId, long specHash)
        {
            if (!_s.Mechs.TryGetValue(mechId.Value, out var m)) throw new ArgumentException(mechId + " is not in the roster");
            if (m.State == MechState.Lost) return;
            m.SpecHash = specHash;
        }

        public MechInstance Retire(EntityId mechId, long step)
        {
            if (!_s.Mechs.TryGetValue(mechId.Value, out var m)) throw new ArgumentException(mechId + " is not in the roster");
            var slot = PlayerSlot.FromIndex(m.Slot);
            if (m.State == MechState.Lost) return Snap(m, slot);
            if (m.IsFloor) throw new InvalidOperationException("the floor chassis cannot be scrapped (non-resellable, Q-206)");
            MarkLost(m, LossCause.Scrapped, step);
            GrantFloor(slot, step);   // scrapping the last flyable mech is a loss like any other: the slot is never left without one (Q-206)
            return Snap(m, slot);
        }

        public FightEndResult OnFightEnd(IBodyDamageRecord record, long step, double pilotStrain = 0.0)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            var id = ActiveMech(record.Slot);
            if (!id.IsValid) throw new InvalidOperationException(record.Slot + " has no flyable mech");
            return OnFightEnd(id, record, step, pilotStrain);
        }

        public FightEndResult OnFightEnd(EntityId mechId, IBodyDamageRecord record, long step, double pilotStrain = 0.0)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (step < 0) throw new ArgumentOutOfRangeException(nameof(step));
            if (double.IsNaN(pilotStrain) || double.IsInfinity(pilotStrain)) throw new ArgumentException("pilot strain must be finite");
            if (!_s.Mechs.TryGetValue(mechId.Value, out var m)) throw new ArgumentException(mechId + " is not in the roster");
            if (m.Slot != record.Slot.Index) throw new ArgumentException(mechId + " does not belong to " + record.Slot);
            if (m.State == MechState.Lost) return new FightEndResult { MechId = m.Id, Lost = true, Replay = true, WreckId = m.WreckId, Cause = m.Cause };

            var parts = new List<PartDamage>();
            record.CopyParts(parts);
            bool anyCore = false, coreDead = false;
            foreach (var part in parts)
            {
                if (double.IsNaN(part.Integrity)) throw new ArgumentException("part " + part.PartId + " has NaN integrity");
                if (!_core.Contains(part.PartId)) continue;
                anyCore = true;
                if (part.Integrity <= 0.0) coreDead = true;
            }
            if (!anyCore) throw new ArgumentException("the damage record has no core part (" + string.Join(", ", _core) + ")");

            var p = EnsurePilot(m.Slot);
            p.Sorties = checked(p.Sorties + 1);
            double strain = Math.Min(1.0, Math.Max(0.0, pilotStrain));
            var result = new FightEndResult { MechId = m.Id };
            if (!coreDead)
            {
                m.Cycles = checked(m.Cycles + 1);
                p.Strain = strain;
                if (p.Recovery > 0) p.Recovery--;
                return result;
            }
            // Loss: strain recovery of one sortie, the pilot flies the next chassis at full strain 0.5 at least (R-104 section 6 rule 4).
            p.Strain = Math.Max(strain, 0.5);
            p.Recovery = 1;
            MarkLost(m, LossCause.CoreDestroyed, step);
            result.Lost = true; result.WreckId = m.WreckId; result.Cause = m.Cause;
            var floor = GrantFloor(record.Slot, step);
            if (floor != null) { result.FloorGranted = true; result.FloorMechId = floor.MechId; }
            return result;
        }

        void MarkLost(Mech m, LossCause cause, long step)
        {
            if (step < 0) throw new ArgumentOutOfRangeException(nameof(step));
            long wreck = 0;
            for (int probe = 0; probe < 1000; probe++)
            {
                long cand = WreckIdentity.Derive(m.Id, step, probe);
                if (!_s.WreckIds.Contains(cand)) { wreck = cand; break; }
            }
            if (wreck == 0) throw new InvalidOperationException("no free wreck id");
            m.State = MechState.Lost; m.Cause = cause; m.WreckId = wreck; m.LossStep = step;
            _s.WreckIds.Add(wreck);
            var p = EnsurePilot(m.Slot);
            p.Lost = checked(p.Lost + 1);
            if (p.Active == m.Id.Value) p.Active = 0;
            var evt = new MechLost { MechId = m.Id, Slot = PlayerSlot.FromIndex(m.Slot), WreckId = wreck, Cause = (byte)cause, Step = step };
            _bus?.Publish(in evt);
        }

        public MechInstance GrantFloor(PlayerSlot slot, long step)
        {
            if (!slot.IsValid) throw new ArgumentException("invalid slot");
            var g = _economy.TryGrantFloorChassis(slot, FlyableCount(slot), step);
            if (!g.Granted) return null;
            _s.Floor.TryGetValue(slot.Index, out long n);
            _s.Floor[slot.Index] = n + 1;
            return Snap(CreateCore(slot, g.ChassisId, 0, step, true), slot);
        }

        // ---------------------------------------------------------------- save: roster@v1

        public string SectionName => RosterContractVersions.SectionName;
        public int SectionVersion => RosterContractVersions.SaveSection;

        static string Pad(long v) => v.ToString("D12", CultureInfo.InvariantCulture);

        public void Write(SaveSection section)
        {
            section.Add(KeyTotals).Set("next_entity", SaveValue.Of(unchecked((long)_alloc.NextValue))).Set("mechs", SaveValue.Of((long)_s.Mechs.Count));
            foreach (var m in _s.Mechs.Values)
                section.Add("mech:" + Pad(unchecked((long)m.Id.Value))).Set("slot", SaveValue.Of((long)m.Slot)).Set("chassis", SaveValue.Of(m.Chassis))
                    .Set("state", SaveValue.Of((long)m.State)).Set("spec_hash", SaveValue.Of(m.SpecHash)).Set("wreck_id", SaveValue.Of(m.WreckId))
                    .Set("cycles", SaveValue.Of(m.Cycles)).Set("floor", SaveValue.Of(m.IsFloor)).Set("cause", SaveValue.Of((long)m.Cause))
                    .Set("created_step", SaveValue.Of(m.CreatedStep)).Set("loss_step", SaveValue.Of(m.LossStep));
            foreach (var kv in _s.Pilots)
            {
                var p = kv.Value;
                section.Add("pilot:slot:" + kv.Key.ToString(CultureInfo.InvariantCulture)).Set("strain", SaveValue.Of(p.Strain)).Set("sorties", SaveValue.Of(p.Sorties))
                    .Set("recovery", SaveValue.Of(p.Recovery)).Set("lost", SaveValue.Of(p.Lost)).Set("active", SaveValue.Of(unchecked((long)p.Active)));
            }
            foreach (var kv in _s.Floor) section.Add("floor:slot:" + kv.Key.ToString(CultureInfo.InvariantCulture)).Set("granted_count", SaveValue.Of(kv.Value));
        }

        public void Read(SaveSection section)
        {
            var n = new State();
            if (!section.TryGet(KeyTotals, out var tr)) throw new FormatException("roster: no totals record");
            long nextEntity = Int(tr, "next_entity"), mechCount = Int(tr, "mechs");
            if (nextEntity < 1 || mechCount < 0) throw new FormatException("roster: bad totals");
            foreach (var rec in section.Records)
            {
                string k = rec.Key;
                if (k == KeyTotals) continue;
                if (k.StartsWith("mech:", StringComparison.Ordinal))
                {
                    long id = ParseId(k, 5, k.Length);
                    if (id < 1 || id >= nextEntity) throw new FormatException("roster: bad mech id " + k);
                    var m = new Mech { Id = EntityId.FromValue((ulong)id) };
                    long slot = Int(rec, "slot"), state = Int(rec, "state"), cause = Int(rec, "cause");
                    if (slot < 0 || slot >= int.MaxValue - 1) throw new FormatException("roster: bad slot in " + k);
                    if (state < 0 || state > (long)MechState.Lost || cause < 0 || cause > (long)LossCause.Scrapped) throw new FormatException("roster: bad enum in " + k);
                    m.Slot = (int)slot; m.State = (MechState)state; m.Cause = (LossCause)cause;
                    m.Chassis = Str(rec, "chassis");
                    if (m.Chassis.Length == 0) throw new FormatException("roster: empty chassis in " + k);
                    m.SpecHash = Int(rec, "spec_hash"); m.WreckId = Int(rec, "wreck_id"); m.Cycles = Int(rec, "cycles");
                    m.CreatedStep = Int(rec, "created_step"); m.LossStep = Int(rec, "loss_step");
                    m.IsFloor = Bool(rec, "floor");
                    if (m.Cycles < 0 || m.CreatedStep < 0 || m.LossStep < 0) throw new FormatException("roster: negative value in " + k);
                    if (m.State == MechState.Flyable)
                    {
                        if (m.Cause != LossCause.None || m.WreckId != 0 || m.LossStep != 0) throw new FormatException("roster: a flyable mech carries loss data (" + k + ")");
                    }
                    else
                    {
                        if (m.Cause == LossCause.None) throw new FormatException("roster: lost mech without a cause (" + k + ")");
                        if (m.WreckId < 1 || m.WreckId > WreckIdentity.Modulus || !n.WreckIds.Add(m.WreckId)) throw new FormatException("roster: bad or duplicate wreck id in " + k);
                        if (m.IsFloor && m.Cause == LossCause.Scrapped) throw new FormatException("roster: the floor chassis cannot be scrapped (" + k + ")");
                        if (m.LossStep < m.CreatedStep) throw new FormatException("roster: loss before creation in " + k);
                    }
                    n.Mechs.Add((ulong)id, m);
                }
                else if (k.StartsWith("pilot:slot:", StringComparison.Ordinal))
                {
                    int slot = ParseSlot(k, 11);
                    var p = new PilotState { Strain = Real(rec, "strain"), Sorties = Int(rec, "sorties"), Recovery = Int(rec, "recovery"), Lost = Int(rec, "lost"), Active = unchecked((ulong)Int(rec, "active")) };
                    if (p.Strain > 1.0 || p.Sorties < 0 || p.Recovery < 0 || p.Recovery > 1 || p.Lost < 0) throw new FormatException("roster: bad pilot values in " + k);
                    n.Pilots.Add(slot, p);
                }
                else if (k.StartsWith("floor:slot:", StringComparison.Ordinal))
                {
                    int slot = ParseSlot(k, 11);
                    long g = Int(rec, "granted_count");
                    if (g < 0) throw new FormatException("roster: negative floor count in " + k);
                    n.Floor.Add(slot, g);
                }
                else throw new FormatException("roster: unknown record " + k);
            }
            if (mechCount != n.Mechs.Count) throw new FormatException("roster: mech count does not match the records");
            var lostBySlot = new Dictionary<int, long>(); var floorBySlot = new Dictionary<int, long>(); var cyclesBySlot = new Dictionary<int, long>();
            foreach (var m in n.Mechs.Values)
            {
                if (!n.Pilots.ContainsKey(m.Slot)) throw new FormatException("roster: slot " + m.Slot + " has mechs but no pilot record");
                lostBySlot.TryGetValue(m.Slot, out long l); if (m.State == MechState.Lost) lostBySlot[m.Slot] = l + 1;
                floorBySlot.TryGetValue(m.Slot, out long f); if (m.IsFloor) floorBySlot[m.Slot] = f + 1;
                cyclesBySlot.TryGetValue(m.Slot, out long c); cyclesBySlot[m.Slot] = c + m.Cycles;
            }
            foreach (var kv in n.Pilots)
            {
                var p = kv.Value;
                lostBySlot.TryGetValue(kv.Key, out long lost); cyclesBySlot.TryGetValue(kv.Key, out long cycles);
                if (p.Lost != lost) throw new FormatException("roster: pilot loss count does not match the lost mechs of slot " + kv.Key);
                if (p.Sorties < cycles + lost - ScrappedCount(n, kv.Key)) throw new FormatException("roster: sorties fewer than the fights the slot's mechs survived or died in");
                if (p.Active != 0 && (!n.Mechs.TryGetValue(p.Active, out var a) || a.Slot != kv.Key || a.State != MechState.Flyable)) throw new FormatException("roster: active mech of slot " + kv.Key + " is not a flyable mech of the slot");
                if (p.Recovery > 0 && p.Lost == 0) throw new FormatException("roster: recovery owed without a loss");
            }
            foreach (var kv in n.Floor)
            {
                floorBySlot.TryGetValue(kv.Key, out long f);
                if (kv.Value != f) throw new FormatException("roster: floor grant count of slot " + kv.Key + " does not match its floor mechs");
            }
            foreach (var kv in floorBySlot) if (!n.Floor.ContainsKey(kv.Key)) throw new FormatException("roster: floor mechs without a floor record in slot " + kv.Key);
            // Commit: advance the allocator past every loaded id so a new mech never reuses one.
            if ((ulong)nextEntity > _alloc.NextValue)
            {
                if ((ulong)nextEntity - _alloc.NextValue > 1_000_000UL) throw new FormatException("roster: next_entity is implausibly far ahead");
                while (_alloc.NextValue < (ulong)nextEntity) _alloc.Release(_alloc.Allocate());
            }
            _s = n;
        }

        static long ScrappedCount(State s, int slot)
        {
            long c = 0;
            foreach (var m in s.Mechs.Values) if (m.Slot == slot && m.Cause == LossCause.Scrapped) c++;
            return c;
        }

        static int ParseSlot(string key, int from)
        {
            long v = ParseId(key, from, key.Length);
            if (v >= int.MaxValue - 1) throw new FormatException("roster: bad slot key " + key);
            return (int)v;
        }

        static long ParseId(string key, int from, int to)
        {
            if (to - from < 1 || to - from > 18) throw new FormatException("roster: bad key " + key);
            long v = 0;
            for (int i = from; i < to; i++) { char ch = key[i]; if (ch < '0' || ch > '9') throw new FormatException("roster: bad key " + key); v = v * 10 + (ch - '0'); }
            return v;
        }

        static long Int(SaveRecord r, string f)
        {
            if (!r.TryGet(f, out var v) || v.Kind != SaveValueKind.Int64) throw new FormatException("roster: " + r.Key + " lacks Int64 '" + f + "'");
            return v.AsInt64;
        }

        static bool Bool(SaveRecord r, string f)
        {
            if (!r.TryGet(f, out var v) || v.Kind != SaveValueKind.Bool) throw new FormatException("roster: " + r.Key + " lacks Bool '" + f + "'");
            return v.AsBool;
        }

        static string Str(SaveRecord r, string f)
        {
            if (!r.TryGet(f, out var v) || v.Kind != SaveValueKind.String) throw new FormatException("roster: " + r.Key + " lacks String '" + f + "'");
            return v.AsString;
        }

        static double Real(SaveRecord r, string f)
        {
            if (!r.TryGet(f, out var v) || v.Kind != SaveValueKind.Float64) throw new FormatException("roster: " + r.Key + " lacks Float64 '" + f + "'");
            double d = v.AsFloat64;
            if (double.IsNaN(d) || double.IsInfinity(d) || d < 0) throw new FormatException("roster: " + r.Key + " '" + f + "' must be finite and >= 0");
            return d;
        }
    }

    public static class RosterModule
    {
        /// <summary>Returns the service it registered, or null when meta.permadeath is off or an argument is missing (nothing registered). The composition root registers it with SaveSystem.</summary>
        public static RosterService Install(IServiceRegistry registry, IFeatureFlags flags, IEconomyService economy, EntityIdAllocator allocator, IEventBus bus = null)
        {
            if (registry == null || flags == null || economy == null || allocator == null || !flags.IsOn(RosterFlags.Permadeath)) return null;
            var service = new RosterService(economy, allocator, bus);
            registry.Register<IRosterService>(service);
            return service;
        }
    }
}
