// T-188: Unity glue for the slice audio adapter. One GameObject with a looping silent AudioSource whose OnAudioFilterRead pulls the engine mix. Main thread: drain the bridge, feed the listener, run the placeholder footfall cadence.
using System;
using Game.Core;
using UnityEngine;

namespace Game.Audio.SliceBridge.Unity
{
    public sealed class SliceAudioBehaviour : MonoBehaviour
    {
        SliceAudioBridge _bridge;
        SliceAudioEngine _engine;
        PlaceholderStepCadence _cadence;
        readonly System.Collections.Generic.List<AudioEvent> _tmp = new System.Collections.Generic.List<AudioEvent>(32);
        double _ox, _oz;
        public bool PlaceholderFootfalls = true;   // TEMPORARY: off once Physics.Body FootContact is forwarded (T-005)
        public int FootfallCount => _cadence?.Footfalls ?? 0;
        public SliceAudioBridge Bridge => _bridge;
        public SliceAudioEngine Engine => _engine;

        public void Init(IEventBus bus, SliceAudioBridge.BuildingLocator locator)
        {
            var profile = new AudioProfile { SampleRate = AudioSettings.outputSampleRate };
            _engine = new SliceAudioEngine(profile);
            _bridge = new SliceAudioBridge(locator);
            _cadence = new PlaceholderStepCadence(new SliceAudioAssumptions());
            _bridge.Subscribe(bus);
            var src = gameObject.AddComponent<AudioSource>();
            src.spatialBlend = 0f; src.loop = true; src.playOnAwake = false;
            var clip = AudioClip.Create("slice_audio_carrier", 1024, 1, AudioSettings.outputSampleRate, false);
            clip.SetData(new float[1024], 0);
            src.clip = clip; src.Play();
        }

        /// <summary>Per frame. Positions are Unity-frame; <paramref name="originX"/>/<paramref name="originZ"/> convert to the world frame the events use.</summary>
        public void Tick(Vector3 mech, Transform listener, long step, double originX, double originZ, AudioSurface surface = AudioSurface.Pavement)
        {
            if (_engine == null) return;
            _ox = originX; _oz = originZ;
            var f = listener.forward;
            _engine.SetListener(new Listener(listener.position.x + originX, listener.position.y, listener.position.z + originZ, Math.Atan2(f.x, f.z)));
            _bridge.TickNow = step;
            if (PlaceholderFootfalls && _cadence.Step(mech.x + originX, mech.z + originZ, Math.Max(Time.deltaTime, 1e-4), out double fx, out double fz, out int foot))
                _bridge.OnPlaceholderFootfall(step, fx, mech.y, fz, foot, surface);
            _tmp.Clear();
            if (_bridge.Drain(_tmp) > 0) _engine.Submit(_tmp);
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            var e = _engine;
            if (e == null || channels < 1) return;
            e.RenderInterleavedAdd(data, channels);
        }

        void OnDestroy() { _bridge?.Unsubscribe(); }
    }

    /// <summary>The slice scene calls this once; it installs nothing unless <c>audio.slice</c> is on.</summary>
    public static class SliceAudioInstaller
    {
        public static SliceAudioBehaviour TryInstall(IFeatureFlags flags, IEventBus bus, SliceAudioBridge.BuildingLocator locator)
        {
            if (flags == null || bus == null || !flags.IsOn(SliceAudioFlags.Slice)) return null;
            var go = new GameObject("SliceAudio");
            var b = go.AddComponent<SliceAudioBehaviour>();
            b.Init(bus, locator);
            return b;
        }
    }
}
