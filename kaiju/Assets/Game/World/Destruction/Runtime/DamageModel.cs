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
            public double HeightM, PeriodS;
        }

        readonly Dictionary<BuildingId, Record> _records = new Dictionary<BuildingId, Record>();
        readonly IEventBus _bus;

        /// <param name="bus">Optional; when set, a state change publishes <see cref="BuildingDamaged"/>.</param>
        public DamageModel(IEventBus bus = null) { _bus = bus; }

        public int Count => _records.Count;

        public void Register(BuildingId id, in BuildingAttributes attributes)
        {
            if (!id.IsValid) throw new ArgumentException("Invalid BuildingId", nameof(id));
            var cap = CapacityModel.Compute(id, attributes);
            _records.TryGetValue(id, out var r);
            r.State.Id = id;
            r.Type = cap.Type; r.HeightM = a_h(attributes); r.PeriodS = cap.PeriodS;
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
            double u = CapacityModel.Uniform(id, eventIndex);
            byte k = ShakingFragility.StateFor(r.Type, r.HeightM, r.PeriodS, pgaG, u);
            if (k > r.State.State)
            {
                double s = k == 4 ? r.S4 : k == 3 ? r.S3 : k == 2 ? r.S2 : r.S1;
                if (s > r.State.CumulativeEnergyJ) r.State.CumulativeEnergyJ = s;
                r.State.State = k; r.State.LastStep = step; r.State.Cause = (byte)DamageCause.Shaking;
                result.Changed = true;
                _records[id] = r;
            }
            result.Current = r.State;
            if (result.Changed && _bus != null)
            {
                _bus.Publish(new BuildingDamaged
                {
                    BuildingId = id, State = r.State.State, EnergyJ = (float)(r.State.CumulativeEnergyJ - prevE),
                    PrevState = result.PrevState, Cause = (byte)DamageCause.Shaking, Source = default, DebrisKg = 0f,
                });
            }
            return result;
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
            if (energyJ > 0 && !double.IsInfinity(energyJ) && !double.IsNaN(energyJ))
            {
                r.State.CumulativeEnergyJ += energyJ;
                double e = r.State.CumulativeEnergyJ;
                byte k = e >= r.S4 ? (byte)4 : e >= r.S3 ? (byte)3 : e >= r.S2 ? (byte)2 : e >= r.S1 ? (byte)1 : (byte)0;
                if (k > r.State.State)
                {
                    r.State.State = k; r.State.LastStep = step; r.State.Cause = (byte)cause;
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
                    PrevState = result.PrevState, Cause = (byte)cause, Source = source, DebrisKg = 0f,
                };
                _bus.Publish(evt);
            }
            return result;
        }
    }
}
