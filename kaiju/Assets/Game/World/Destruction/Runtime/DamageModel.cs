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
            r.S1 = cap.S1; r.S2 = cap.S2; r.S3 = cap.S3; r.S4 = cap.S4;
            _records[id] = r;
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
