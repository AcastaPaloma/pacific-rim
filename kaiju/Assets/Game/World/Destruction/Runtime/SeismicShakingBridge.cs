// Bridge from World.Seismic to World.Destruction (T-027). Depends only on Game.World.Seismic.Contracts (events + IShakingField),
// never on the Seismic runtime. Active only when both destruction.enabled and seismic.enabled are on.
using System;
using System.Collections.Generic;
using Game.Core;
using Game.World.Seismic;

namespace Game.World.Destruction
{
    /// <summary>
    /// On <see cref="FaultRupture"/> of an EventId (once per EventId): for each registered building, in ordinal BuildingId order,
    /// reads PGA at its position from <see cref="IShakingField"/> and calls <see cref="DamageModel.ApplyShaking"/> with
    /// eventIndex = 1, 2, ... in order of events handled. The Seismic director adds the rupture to the field before it publishes, so
    /// the field answers at FaultRupture time. Known limit: the S-wave arrival delay is not applied (damage lands at rupture publication).
    /// </summary>
    public sealed class SeismicShakingBridge : IEventHandler<FaultRupture>
    {
        /// <summary>Rock Vs30 used when no provider is given; equals SeismicAssumptions.Vs30RockMs (Assumed, Q-NEW-c).</summary>
        public const double DefaultVs30Ms = 760.0;

        readonly DamageModel _model;
        readonly IShakingField _field;
        readonly Func<double, double, double> _vs30At;
        readonly Func<long> _step;
        readonly IEventBus _bus;
        readonly List<Site> _sites = new List<Site>();
        readonly HashSet<uint> _handled = new HashSet<uint>();
        int _eventIndex = DamageParameters.FirstEventIndex;

        struct Site { public BuildingId Id; public double X, Z; }

        SeismicShakingBridge(DamageModel model, IShakingField field, IEventBus bus, Func<double, double, double> vs30At, Func<long> step)
        {
            _model = model; _field = field; _bus = bus;
            _vs30At = vs30At ?? ((x, z) => DefaultVs30Ms);
            _step = step ?? (() => 0L);
        }

        /// <summary>Number of buildings the bridge knows positions of.</summary>
        public int SiteCount => _sites.Count;

        /// <summary>
        /// Returns null (and subscribes nothing) unless both flags are on and all arguments are present. Otherwise subscribes to
        /// <see cref="FaultRupture"/> on the bus and returns the bridge.
        /// </summary>
        public static SeismicShakingBridge TryCreate(IFeatureFlags flags, IEventBus bus, DamageModel model, IShakingField field,
            Func<double, double, double> vs30At = null, Func<long> step = null)
        {
            if (flags == null || bus == null || model == null || field == null) return null;
            if (!flags.IsOn(DestructionFlags.Enabled) || !flags.IsOn(SeismicFlags.Enabled)) return null;
            var b = new SeismicShakingBridge(model, field, bus, vs30At, step);
            bus.Subscribe<FaultRupture>(b);
            return b;
        }

        public void Detach() { _bus.Unsubscribe<FaultRupture>(this); }

        /// <summary>Position (BayTM metres) of a building the model knows. Re-adding an id replaces its position.</summary>
        public void AddBuilding(BuildingId id, double x, double z)
        {
            int lo = 0, hi = _sites.Count;
            while (lo < hi) { int mid = (lo + hi) >> 1; if (_sites[mid].Id.CompareTo(id) < 0) lo = mid + 1; else hi = mid; }
            var s = new Site { Id = id, X = x, Z = z };
            if (lo < _sites.Count && _sites[lo].Id.Equals(id)) _sites[lo] = s; else _sites.Insert(lo, s);
        }

        public void Handle(in FaultRupture evt)
        {
            if (!_handled.Add(evt.EventId)) return;
            int index = _eventIndex++;
            long step = _step();
            for (int i = 0; i < _sites.Count; i++)
            {
                Site s = _sites[i];
                if (!_field.TryGetShaking(evt.EventId, s.X, s.Z, _vs30At(s.X, s.Z), out ShakingSample sample)) continue;
                _model.ApplyShaking(s.Id, sample.PgaG, index, step);
            }
        }
    }
}
