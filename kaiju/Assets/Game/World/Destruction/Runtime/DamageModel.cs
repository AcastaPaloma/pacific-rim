// IDamageModel implementation: cumulative energy against per-building capacity, monotone states (R-021 §2.3).
using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.World.Destruction
{
    public sealed class DamageModel : IDamageModel
    {
        struct Record
        {
            public BuildingDamageState State;
            public double S1, S2, S3, S4;
            public HazusType Type;
            public double HeightM, PeriodS, MassKg, FootprintM2;
        }

        readonly Dictionary<BuildingId, Record> _records = new Dictionary<BuildingId, Record>();
        // Saved damage for buildings that are not registered (tile not loaded). Re-applied at Register; written back by CopyDamage.
        readonly Dictionary<BuildingId, BuildingDamageState> _pending = new Dictionary<BuildingId, BuildingDamageState>();
        readonly IEventBus _bus;

        /// <param name="bus">Optional; when set, a state change publishes <see cref="BuildingDamaged"/>.</param>
        public DamageModel(IEventBus bus = null) { _bus = bus; }

        public int Count => _records.Count;
        /// <summary>Restored records waiting for their building to be registered.</summary>
        public int PendingCount => _pending.Count;

        /// <summary>
        /// Appends every damaged building (state > 0 or energy > 0), registered or pending, to <paramref name="into"/>, in no
        /// particular order. The save sorts by key. Debris is the stored value.
        /// </summary>
        public void CopyDamage(List<BuildingDamageState> into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            foreach (var kv in _records)
            {
                var s = kv.Value.State;
                if (s.State > 0 || s.CumulativeEnergyJ > 0) into.Add(s);
            }
            foreach (var kv in _pending) into.Add(kv.Value);
        }

        /// <summary>
        /// Replaces all damage with <paramref name="states"/>. Validates everything first and throws ArgumentException with
        /// nothing applied. Registered buildings take the saved state, energy, debris, step and cause; the rest are kept
        /// pending and applied when <see cref="Register"/> sees them. Buildings not listed are reset to undamaged.
        /// Publishes nothing (a load is not a damage event).
        /// </summary>
        public void Restore(IReadOnlyList<BuildingDamageState> states)
        {
            if (states == null) throw new ArgumentNullException(nameof(states));
            var seen = new HashSet<BuildingId>();
            for (int i = 0; i < states.Count; i++)
            {
                var s = states[i];
                if (!s.Id.IsValid) throw new ArgumentException("Invalid BuildingId at " + i, nameof(states));
                if (!seen.Add(s.Id)) throw new ArgumentException("Duplicate " + s.Id, nameof(states));
                if (s.State > (byte)DamageState.Complete) throw new ArgumentException("State " + s.State + " out of range for " + s.Id, nameof(states));
                if (s.Cause > (byte)DamageCause.Debris) throw new ArgumentException("Cause " + s.Cause + " out of range for " + s.Id, nameof(states));
                if (!IsFiniteNonNegative(s.CumulativeEnergyJ) || !IsFiniteNonNegative(s.DebrisKg))
                    throw new ArgumentException("Energy or debris is negative or not finite for " + s.Id, nameof(states));
            }
            _pending.Clear();
            var ids = new List<BuildingId>(_records.Keys);
            foreach (var id in ids)
            {
                var r = _records[id];
                r.State = new BuildingDamageState { Id = id };
                _records[id] = r;
            }
            for (int i = 0; i < states.Count; i++)
            {
                var s = states[i];
                if (_records.TryGetValue(s.Id, out var r)) { r.State = s; _records[s.Id] = r; }
                else _pending[s.Id] = s;
            }
        }

        static bool IsFiniteNonNegative(double v) { return v >= 0 && !double.IsInfinity(v) && !double.IsNaN(v); }

        public void Register(BuildingId id, in BuildingAttributes attributes)
        {
            if (!id.IsValid) throw new ArgumentException("Invalid BuildingId", nameof(id));
            var cap = CapacityModel.Compute(id, attributes);
            _records.TryGetValue(id, out var r);
            r.State.Id = id;
            bool restored = _pending.TryGetValue(id, out var saved);
            if (restored)
            {
                r.State = saved;
                _pending.Remove(id);
            }
            r.Type = cap.Type; r.HeightM = a_h(attributes); r.PeriodS = cap.PeriodS;
            r.MassKg = cap.MassKg; r.FootprintM2 = attributes.FootprintM2;
            if (!restored) r.State.DebrisKg = DebrisAccounting.DebrisKgAt(r.Type, r.MassKg, r.State.State);
            r.S1 = cap.S1; r.S2 = cap.S2; r.S3 = cap.S3; r.S4 = cap.S4;
            _records[id] = r;
        }

        static double a_h(in BuildingAttributes a) { return a.HeightM > 0 ? a.HeightM : DamageParameters.MetresPerLevel; }

        /// <summary>
        /// Ground-shaking route (R-021 §2.4): draws u from (BuildingId, eventIndex >= 1), state = max k with u &lt; P_k(PGA),
        /// and raises cumulative energy to max(E_cum, S_state) so the energy route shares the ladder. Never lowers a state.
        /// </summary>
        public DamageChange ApplyShaking(BuildingId id, double pgaG, int eventIndex, long step)
        {
            if (eventIndex < DamageParameters.FirstEventIndex) throw new ArgumentOutOfRangeException(nameof(eventIndex));
            var result = new DamageChange();
            if (!_records.TryGetValue(id, out var r)) return result;
            result.Known = true;
            result.PrevState = r.State.State;
            double prevE = r.State.CumulativeEnergyJ;
            double debrisInc = 0;
            double u = CapacityModel.Uniform(id, eventIndex);
            byte k = ShakingFragility.StateFor(r.Type, r.HeightM, r.PeriodS, pgaG, u);
            if (k > r.State.State)
            {
                double s = k == 4 ? r.S4 : k == 3 ? r.S3 : k == 2 ? r.S2 : r.S1;
                if (s > r.State.CumulativeEnergyJ) r.State.CumulativeEnergyJ = s;
                r.State.State = k; r.State.LastStep = step; r.State.Cause = (byte)DamageCause.Shaking;
                debrisInc = SetDebris(ref r);
                result.Changed = true;
                _records[id] = r;
            }
            result.Current = r.State;
            if (result.Changed && _bus != null)
            {
                _bus.Publish(new BuildingDamaged
                {
                    BuildingId = id, State = r.State.State, EnergyJ = (float)(r.State.CumulativeEnergyJ - prevE),
                    PrevState = result.PrevState, Cause = (byte)DamageCause.Shaking, Source = default, DebrisKg = (float)debrisInc,
                });
            }
            return result;
        }

        /// <summary>Sets the debris mass for the record's current state; returns the increment, kg (never negative).</summary>
        static double SetDebris(ref Record r)
        {
            double total = DebrisAccounting.DebrisKgAt(r.Type, r.MassKg, r.State.State);
            double inc = total - r.State.DebrisKg;
            r.State.DebrisKg = total;
            return inc > 0 ? inc : 0;
        }

        public bool TryGetDebrisAccount(BuildingId id, out DebrisAccount account)
        {
            if (!_records.TryGetValue(id, out var r)) { account = default; return false; }
            account = DebrisAccounting.Compute(id, r.Type, r.MassKg, r.HeightM, r.FootprintM2, r.State.State, r.State.CumulativeEnergyJ);
            return true;
        }

        public bool TryGetState(BuildingId id, out BuildingDamageState state)
        {
            if (_records.TryGetValue(id, out var r)) { state = r.State; return true; }
            state = default; return false;
        }

        public bool TryGetThresholds(BuildingId id, out double slightJ, out double moderateJ, out double extensiveJ, out double completeJ)
        {
            if (_records.TryGetValue(id, out var r)) { slightJ = r.S1; moderateJ = r.S2; extensiveJ = r.S3; completeJ = r.S4; return true; }
            slightJ = moderateJ = extensiveJ = completeJ = 0; return false;
        }

        public DamageChange Apply(BuildingId id, double energyJ, DamageCause cause, EntityId source, long step)
        {
            var result = new DamageChange();
            if (!_records.TryGetValue(id, out var r)) return result;
            result.Known = true;
            result.PrevState = r.State.State;
            double debrisInc = 0;
            if (energyJ > 0 && !double.IsInfinity(energyJ) && !double.IsNaN(energyJ))
            {
                r.State.CumulativeEnergyJ += energyJ;
                double e = r.State.CumulativeEnergyJ;
                byte k = e >= r.S4 ? (byte)4 : e >= r.S3 ? (byte)3 : e >= r.S2 ? (byte)2 : e >= r.S1 ? (byte)1 : (byte)0;
                if (k > r.State.State)
                {
                    r.State.State = k; r.State.LastStep = step; r.State.Cause = (byte)cause;
                    debrisInc = SetDebris(ref r);
                    result.Changed = true;
                }
                _records[id] = r;
            }
            result.Current = r.State;
            if (result.Changed && _bus != null)
            {
                var evt = new BuildingDamaged
                {
                    BuildingId = id, State = r.State.State, EnergyJ = (float)energyJ,
                    PrevState = result.PrevState, Cause = (byte)cause, Source = source, DebrisKg = (float)debrisInc,
                };
                _bus.Publish(evt);
            }
            return result;
        }
    }
}
