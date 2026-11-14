using System.Collections.Generic;
using NUnit.Framework;
using Game.Core;

// C4: a sample module outside Core, written only against Core's public contracts.
// It adds its own event, service contract + implementation, feature flag and save section. Core is not edited.
namespace Game.Samples.Weather
{
    public struct WeatherChanged : IEvent
    {
        public int Step; public float WindMetresPerSecond; public BuildingId Sheltering;
    }

    public interface IWeatherService { float Wind { get; } void Tick(); }

    public sealed class WeatherService : IWeatherService, ISaveParticipant, IEventHandler<WeatherChanged>, System.IDisposable
    {
        public const string Flag = "sample.weather.gusts";
        private readonly IEventBus _bus; private readonly ISimulationClock _clock; private readonly IFeatureFlags _flags;
        public WeatherService(IServiceRegistry services)
        {
            _bus = services.Resolve<IEventBus>(); _clock = services.Resolve<ISimulationClock>(); _flags = services.Resolve<IFeatureFlags>();
            _bus.Subscribe(this);
        }
        public float Wind { get; private set; }
        public int Changes;
        public void Tick()
        {
            if (!_flags.IsOn(Flag)) return;   // new features start off
            _bus.Publish(new WeatherChanged { Step = (int)_clock.Step, WindMetresPerSecond = 3f + _clock.Step, Sheltering = BuildingId.From(BuildingSource.Landmark, 1) });
        }
        public void Handle(in WeatherChanged e) { Wind = e.WindMetresPerSecond; Changes++; }
        public string SectionName => "sample.weather";
        public int SectionVersion => 1;
        public void Write(SaveSection s) => s.Add("weather:global").Set("wind", SaveValue.Of((double)Wind)).Set("changes", SaveValue.Of((long)Changes));
        public void Read(SaveSection s)
        {
            if (!s.TryGet("weather:global", out var r)) return;
            Wind = (float)r.Get("wind").AsFloat64; Changes = (int)r.Get("changes").AsInt64;
        }
        public void Dispose() => _bus.Unsubscribe(this);
    }
}

namespace Game.Core.Tests
{
    using Game.Samples.Weather;

    public class ExtensionProofTests
    {
        private static CoreServices Boot(bool gustsOn, CollectingErrorSink errors)
        {
            // The module adds its flag by adding its own file next to the committed ones; no Core file is edited.
            string folder = ProjectPaths.NewTempDir();
            foreach (var f in System.IO.Directory.GetFiles(FlagTests.CommittedFlagsFolder, "*.json"))
                System.IO.File.Copy(f, System.IO.Path.Combine(folder, System.IO.Path.GetFileName(f)));
            System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "SampleWeather.json"),
                "{\"schema\":1,\"flags\":{\"" + WeatherService.Flag + "\":" + (gustsOn ? "true" : "false") + "}}");
            var flags = FeatureFlagLoader.LoadFolder(folder, errors);
            Assert.That(flags.IsOn("core.verboseBoot"), Is.False);
            return new CoreServices(new CoreSettings(0.02, 1000), flags, errors);
        }

        [Test]
        public void ExtensionProof_SampleModule_NoCoreChanges()
        {
            var errors = new CollectingErrorSink();
            string saved;
            using (var core = Boot(true, errors))
            {
                var weather = new WeatherService(core.Registry);
                core.Registry.Register<IWeatherService>(weather);
                var observed = new List<float>();
                core.Bus.Subscribe(new Game.Core.Tests.Handler<WeatherChanged>(e => observed.Add(e.WindMetresPerSecond)));
                for (int i = 0; i < 3; i++) { core.Clock.Advance(); core.Registry.Resolve<IWeatherService>().Tick(); }
                Assert.That(observed, Is.EqualTo(new[] { 4f, 5f, 6f }));
                Assert.That(weather.Wind, Is.EqualTo(6f));
                var saves = new SaveSystem("0.1.0-dev");
                saves.Register(weather);
                saved = SaveCodec.Serialize(saves.Capture());
            }
            using (var core = Boot(false, errors))
            {
                var weather = new WeatherService(core.Registry);
                var saves = new SaveSystem("0.1.0-dev");
                saves.Register(weather);
                Assert.That(saves.Restore(SaveCodec.Deserialize(saved, "mem").Document).Ok, Is.True);
                Assert.That(weather.Wind, Is.EqualTo(6f));
                Assert.That(weather.Changes, Is.EqualTo(3));
                weather.Tick();
                Assert.That(weather.Changes, Is.EqualTo(3), "flag off: the feature does nothing");
                core.Registry.Register<IWeatherService>(weather);
            }
            Assert.That(errors.Count, Is.EqualTo(0));
        }
    }
}
