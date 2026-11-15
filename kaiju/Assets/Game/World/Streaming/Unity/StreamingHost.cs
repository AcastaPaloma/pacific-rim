// Unity driver and installer of the streaming host (T-109). Behind flag world.streaming (default off): StreamingModule.Install returns
// null when the flag is off, and nothing else in the project creates a StreamingHost, so M1 GeoTileSetBuilder behaviour is unchanged.
using System;
using System.Collections.Generic;
using UnityEngine;
using Game.Core;
using Game.World.Geo;
using Game.World.Geo.Unity;
using Game.World.Streaming.Host;

namespace Game.World.Streaming.Unity
{
    public sealed class StreamingHost : MonoBehaviour
    {
        public const int MaxFoci = 64;
        StreamingHostCore _core;
        UnityTileBuilder _builder;
        IStreamingFocus _focus;
        readonly FocusSample[] _buf = new FocusSample[MaxFoci];

        public StreamingHostCore Core => _core;
        public UnityTileBuilder Builder => _builder;
        public StreamingScheduler Scheduler { get; private set; }

        public void Initialize(StreamingConfig cfg, ITileCostModel cost, ITileReader reader, IStreamingFocus focus, IReadOnlyDictionary<string, double> geoSettings, IEventBus bus = null, IWorkRunner runner = null)
        {
            _focus = focus ?? throw new ArgumentNullException(nameof(focus));
            runner = runner ?? new ThreadPoolWorkRunner();
            _builder = new UnityTileBuilder(transform, runner, geoSettings);
            Scheduler = new StreamingScheduler(cfg ?? new StreamingConfig(), cost ?? new DefaultTileCostModel(), bus);
            var b = StreamingBudget.Default;
            _core = new StreamingHostCore(Scheduler, reader, _builder, runner, new StreamingHostOptions { MainThreadSliceMs = b.MainThreadSliceMs, MaxReads = b.MaxInFlightReads });
        }

        void Update()
        {
            if (_core == null) return;
            int n = _focus.Fill(_buf);
            _core.Tick(Time.deltaTime, new ReadOnlySpan<FocusSample>(_buf, 0, n));
            _builder.Tick();
        }

        void OnDestroy() { _core?.Dispose(); _builder?.ReleaseAll(); }
    }

    public static class StreamingModule
    {
        /// <summary>Creates the host object when world.streaming is on; null (nothing created, nothing registered) otherwise.</summary>
        public static StreamingHost Install(IFeatureFlags flags, Func<StreamingHost, bool> configure = null)
        {
            if (flags == null || !flags.IsOn(StreamingFlags.Enabled)) return null;
            var go = new GameObject("StreamingHost");
            var host = go.AddComponent<StreamingHost>();
            if (configure != null && !configure(host)) { { if (Application.isPlaying) UnityEngine.Object.Destroy(go); else UnityEngine.Object.DestroyImmediate(go); }; return null; }
            return host;
        }

        public static string DefaultTilesRoot => GeoModule.DefaultTilesRoot;
    }
}
