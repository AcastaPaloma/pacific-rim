// Engine-free composition hook of World.Seismic (MODULARITY rule 5): registers services only when seismic.enabled is on.
using Game.Core;

namespace Game.World.Seismic
{
    /// <summary>
    /// Adapts the Core bus to <see cref="ISeismicEventSink"/>. Events are buffered and flushed in order (QuakeBegan, FaultRupture,
    /// EmergencePoint) in one burst, so the shaking field already holds the rupture when subscribers see FaultRupture.
    /// </summary>
    public sealed class CoreBusSeismicSink : ISeismicEventSink
    {
        readonly IEventBus _bus;
        bool _hasBegan, _hasRupture, _hasEmergence;
        QuakeBegan _began; FaultRupture _rupture; EmergencePoint _emergence;

        public CoreBusSeismicSink(IEventBus bus) { _bus = bus; }

        public void Publish<T>(T e) where T : unmanaged, IEvent
        {
            if (e is QuakeBegan b) { _began = b; _hasBegan = true; }
            else if (e is FaultRupture f) { _rupture = f; _hasRupture = true; }
            else if (e is EmergencePoint p) { _emergence = p; _hasEmergence = true; }
            else if (_bus != null) _bus.Publish(e);
        }

        /// <summary>Publishes the buffered events in sequence order and clears the buffer. No bus: events are dropped.</summary>
        public void Flush()
        {
            if (_bus != null)
            {
                if (_hasBegan) _bus.Publish(_began);
                if (_hasRupture) _bus.Publish(_rupture);
                if (_hasEmergence) _bus.Publish(_emergence);
            }
            _hasBegan = _hasRupture = _hasEmergence = false;
        }
    }

    /// <summary>Runs a <see cref="QuakeScenario"/>: rupture on the mapped fault, shaking field, then the three events on the Core bus.</summary>
    public sealed class QuakeDirector
    {
        readonly QuakeSequence _sequence;
        readonly CoreBusSeismicSink _sink;
        readonly DistrictRefs _districts;
        public ShakingField Field { get; }
        public Vs30Provider Vs30 { get; }
        public FaultTraceSet Faults { get; }

        public QuakeDirector(FaultTraceSet faults, DistrictRefs districts, IEventBus bus, Vs30Provider vs30 = null)
        {
            Faults = faults ?? throw new System.ArgumentNullException(nameof(faults));
            _districts = districts;
            _sequence = new QuakeSequence(faults);
            _sink = new CoreBusSeismicSink(bus);
            Field = new ShakingField();
            Vs30 = vs30 ?? new Vs30Provider();
        }

        /// <summary>Returns the rupture, or null when the EventId already ran or the FaultId is unknown (nothing published then).</summary>
        public RuptureResult Run(QuakeScenario scenario, double originTimeS = 0)
        {
            RuptureResult r = _sequence.Run(scenario, _districts, _sink, originTimeS);
            if (r == null) return null;
            Field.Add(r);
            _sink.Flush();
            return r;
        }
    }

    public static class SeismicModule
    {
        /// <summary>
        /// Returns the director it created, or null when the flag is off (nothing registered, bus untouched). When on it registers
        /// <see cref="IShakingField"/> (the director's field), <see cref="Vs30Provider"/> and <see cref="QuakeDirector"/>; the Core bus
        /// is taken from the registry if present.
        /// </summary>
        public static QuakeDirector Install(IServiceRegistry registry, IFeatureFlags flags, FaultTraceSet faults, DistrictRefs districts, Vs30Provider vs30 = null)
        {
            if (registry == null || flags == null || faults == null || !flags.IsOn(SeismicFlags.Enabled)) return null;
            IEventBus bus = null;
            registry.TryResolve(out bus);
            var director = new QuakeDirector(faults, districts, bus, vs30);
            registry.Register<IShakingField>(director.Field);
            registry.Register<Vs30Provider>(director.Vs30);
            registry.Register<QuakeDirector>(director);
            return director;
        }
    }
}
