// T-188: adapter from the slice scene's bus events (Combat, Destruction, Seismic) and the placeholder mech step cadence to Game.Audio events. Engine-free; the Unity glue is in ../Unity.
// Frame: all positions are WORLD metres (floating-origin world), the same frame as Seismic, Destruction building anchors and the listener fed to the engine. Assumed constants are in SliceAudioAssumptions.
using System;
using System.Collections.Generic;
using Game.Combat;
using Game.Core;
using Game.World.Destruction;
using Game.World.Seismic;

namespace Game.Audio.SliceBridge
{
    public static class SliceAudioFlags
    {
        /// <summary>Slice audio adapter. Default off in Config/FeatureFlags/AudioSlice.json; the slice scene installs the adapter only when on.</summary>
        public const string Slice = "audio.slice";
    }

    /// <summary>Assumed numbers (R-132 style: all Assumed until sourced). Changing one is a decision to log.</summary>
    public sealed class SliceAudioAssumptions
    {
        public double MechMassKg = 6.0e6;              // 6,000 t mech (Audio MODULE.md)
        public double FootMassKg = 3.0e5;              // sounding mass for the modal footfall
        public double FootLandingSpeedMs = 2.0;        // placeholder approach speed; the real body supplies VerticalImpulseNs (T-005)
        public double FootEffectiveMassFraction = 0.15;// share of the mech mass that stops at the foot
        public double StrideM = 40.0;                  // placeholder stride (half a cycle per footfall)
        public double MinStepSpeedMs = 0.5;
        public double MuzzleVelocityMs = 1000.0;       // WeaponFired carries only the projectile mass
        public double DebrisDropM = 20.0;              // mean fall height of debris, for E = m g h
        public double KaijuRiseEnergyJ = 5.0e10;       // EmergencePoint ground burst
        public double KaijuRiseMassKg = 1.0e7;
        public int MaxEventsPerDrain = 12;             // quake: hundreds of buildings change state in one frame; keep the strongest
        public byte MinFailureState = 2;               // Moderate and above sound; Slight only by direct impact
    }

    /// <summary>Counters for tests and the capture.</summary>
    public struct SliceBridgeStats
    {
        public int Received, Emitted, DroppedNoPosition, DroppedFiltered, DroppedCap;
    }

    /// <summary>Placeholder footfall source: emits a FootContact AudioEvent each half stride walked, alternating feet. TEMPORARY until Physics.Body publishes FootContact (T-005): then feed those through <see cref="SliceAudioBridge.OnFootContact"/> and turn this off.</summary>
    public sealed class PlaceholderStepCadence
    {
        readonly SliceAudioAssumptions _a; double _lx, _lz; bool _have; double _acc; int _foot;
        public PlaceholderStepCadence(SliceAudioAssumptions a) { _a = a ?? new SliceAudioAssumptions(); }
        public int Footfalls { get; private set; }

        /// <summary>Feed the mech pose once per frame. Returns true when a footfall happened this frame; <paramref name="speedMs"/> is the planar speed over <paramref name="dtS"/>.</summary>
        public bool Step(double x, double z, double dtS, out double footX, out double footZ, out int foot)
        {
            footX = x; footZ = z; foot = _foot;
            if (!_have) { _lx = x; _lz = z; _have = true; return false; }
            double d = Math.Sqrt((x - _lx) * (x - _lx) + (z - _lz) * (z - _lz)); _lx = x; _lz = z;
            double speed = dtS > 1e-6 ? d / dtS : 0.0;
            if (speed < _a.MinStepSpeedMs) { _acc = Math.Min(_acc, _a.StrideM * 0.25); return false; }
            _acc += d;
            double half = _a.StrideM * 0.5;
            if (_acc < half) return false;
            _acc -= half; foot = _foot; _foot ^= 1; Footfalls++;
            return true;
        }
    }

    /// <summary>
    /// Subscribes to the slice bus, builds <see cref="AudioEvent"/>s, and hands them over through <see cref="IAudioEmitter.Drain"/>. Main-thread only (the bus is). The audio thread never touches it.
    /// </summary>
    public sealed class SliceAudioBridge : IAudioEmitter,
        IEventHandler<BuildingDamaged>, IEventHandler<QuakeBegan>, IEventHandler<EmergencePoint>, IEventHandler<WeaponFired>, IEventHandler<ImpactImpulse>
    {
        public delegate bool BuildingLocator(BuildingId id, out double x, out double y, out double z);

        readonly SliceAudioAssumptions _a;
        readonly BuildingLocator _locate;
        readonly List<AudioEvent> _pending = new List<AudioEvent>(64);
        readonly List<double> _pendingEnergy = new List<double>(64);
        long _nextId = 1;
        SliceBridgeStats _stats;
        IEventBus _bus;
        public long TickNow;                      // sim step stamped on events that carry none (set by the host per frame)
        public SliceBridgeStats Stats => _stats;
        public int Pending => _pending.Count;

        public SliceAudioBridge(BuildingLocator locate, SliceAudioAssumptions assumptions = null)
        { _locate = locate; _a = assumptions ?? new SliceAudioAssumptions(); }

        public void Subscribe(IEventBus bus)
        {
            _bus = bus;
            bus.Subscribe<BuildingDamaged>(this); bus.Subscribe<QuakeBegan>(this); bus.Subscribe<EmergencePoint>(this);
            bus.Subscribe<WeaponFired>(this); bus.Subscribe<ImpactImpulse>(this);
        }

        public void Unsubscribe()
        {
            if (_bus == null) return;
            _bus.Unsubscribe<BuildingDamaged>(this); _bus.Unsubscribe<QuakeBegan>(this); _bus.Unsubscribe<EmergencePoint>(this);
            _bus.Unsubscribe<WeaponFired>(this); _bus.Unsubscribe<ImpactImpulse>(this); _bus = null;
        }

        void Add(in AudioEvent ev) { _pending.Add(ev); _pendingEnergy.Add(ev.EnergyJ); _stats.Emitted++; }
        uint SeedOf(ulong v) { return (uint)(v ^ (v >> 32)) * 2654435761u; }

        public void Handle(in BuildingDamaged e)
        {
            _stats.Received++;
            bool direct = e.Cause == (byte)DamageCause.Impact;
            if (e.State <= e.PrevState || (!direct && e.State < _a.MinFailureState)) { _stats.DroppedFiltered++; return; }
            if (_locate == null || !_locate(e.BuildingId, out double x, out double y, out double z)) { _stats.DroppedNoPosition++; return; }
            double mass = Math.Min(Math.Max(e.DebrisKg, 1.0e3), 1.0e7);
            double energy = Math.Max(e.EnergyJ, e.DebrisKg * 9.81 * _a.DebrisDropM);
            uint seed = SeedOf(e.BuildingId.SourceId + e.State);
            Add(new AudioEvent(_nextId++, AudioEventKind.StructureFailure, TickNow, x, y, z, energy, 0, mass, AudioMaterial.Concrete, AudioSurface.Building, seed));
            if (e.DebrisKg > 0f)
                Add(new AudioEvent(_nextId++, AudioEventKind.DebrisRain, TickNow, x, 0, z, e.DebrisKg * 9.81 * _a.DebrisDropM, 0, Math.Min(e.DebrisKg, 1.0e4), AudioMaterial.Concrete, AudioSurface.Pavement, seed ^ 0x5bd1e995u));
        }

        public void Handle(in QuakeBegan e)
        {
            _stats.Received++;
            // radiated seismic energy log10 Es = 1.5 Mw + 4.8 (Gutenberg and Richter, Es in J); the three phases share the source.
            double es = Math.Pow(10.0, 1.5 * e.Mw + 4.8);
            double x = e.HypoX, y = -e.HypoDepthM, z = e.HypoZ;
            Add(new AudioEvent(_nextId++, AudioEventKind.QuakeP, TickNow, x, y, z, es * 0.2, 0, 0, AudioMaterial.Rock, AudioSurface.Ground, e.Seed));
            Add(new AudioEvent(_nextId++, AudioEventKind.QuakeS, TickNow, x, y, z, es * 0.8, 0, 0, AudioMaterial.Rock, AudioSurface.Ground, e.Seed + 1));
            Add(new AudioEvent(_nextId++, AudioEventKind.QuakeRumble, TickNow, x, y, z, es, 0, 0, AudioMaterial.Rock, AudioSurface.Ground, e.Seed + 2));
        }

        public void Handle(in EmergencePoint e)
        {
            _stats.Received++;
            Add(new AudioEvent(_nextId++, AudioEventKind.Impact, TickNow, e.X, 0, e.Z, _a.KaijuRiseEnergyJ, 0, _a.KaijuRiseMassKg, AudioMaterial.Rock, AudioSurface.Ground, e.EventId));
        }

        public void Handle(in WeaponFired e)
        {
            _stats.Received++;
            double m = e.ProjectileMassKg > 0 ? e.ProjectileMassKg : 100.0;
            Add(new AudioEvent(_nextId++, AudioEventKind.MuzzleBlast, e.Step, e.MuzzleX, e.MuzzleY, e.MuzzleZ, 0.5 * m * _a.MuzzleVelocityMs * _a.MuzzleVelocityMs, 0, m, AudioMaterial.Steel, AudioSurface.Ground, SeedOf((ulong)e.Step)));
        }

        public void Handle(in ImpactImpulse e)
        {
            _stats.Received++;
            if (!(e.EnergyJ > 0)) { _stats.DroppedFiltered++; return; }
            double mass = e.ImpulseNs > 0 ? Math.Min(Math.Max(e.ImpulseNs * e.ImpulseNs / (2.0 * e.EnergyJ), 1.0), 1.0e8) : 1.0e3;   // E = p^2 / 2m
            var kind = (TargetKind)e.TargetKind;
            var mat = kind == TargetKind.Building ? AudioMaterial.Concrete : kind == TargetKind.Ground ? AudioMaterial.Soil : kind == TargetKind.Kaiju ? AudioMaterial.Hide : AudioMaterial.Steel;
            Add(new AudioEvent(_nextId++, AudioEventKind.Impact, e.Step, e.PointX, e.PointY, e.PointZ, e.EnergyJ, e.ImpulseNs, mass, mat, kind == TargetKind.Building ? AudioSurface.Building : AudioSurface.Ground, SeedOf((ulong)e.Step)));
        }

        /// <summary>Physics.Body FootContact hookup (T-005): the host subscribes to the Body event and forwards its fields. Same event shape as the placeholder cadence.</summary>
        public void OnFootContact(long step, long contactId, double x, double y, double z, double verticalImpulseNs, double approachSpeedMs, double footMassKg, AudioSurface surface)
        {
            _stats.Received++;
            double v = Math.Max(approachSpeedMs, 0.1);
            double e = verticalImpulseNs > 0 ? verticalImpulseNs * v * 0.5 : 0.5 * footMassKg * v * v;
            Add(new AudioEvent(_nextId++ + (contactId << 20), AudioEventKind.FootContact, step, x, y, z, e, verticalImpulseNs, footMassKg,
                surface == AudioSurface.Pavement ? AudioMaterial.Asphalt : AudioMaterial.Soil, surface, SeedOf((ulong)contactId)));
        }

        /// <summary>Placeholder footfall (TEMPORARY, see <see cref="PlaceholderStepCadence"/>).</summary>
        public void OnPlaceholderFootfall(long step, double x, double y, double z, int foot, AudioSurface surface)
        {
            _stats.Received++;
            double m = _a.MechMassKg * _a.FootEffectiveMassFraction, v = _a.FootLandingSpeedMs;
            Add(new AudioEvent(_nextId++, AudioEventKind.FootContact, step, x, y, z, 0.5 * m * v * v, m * v, _a.FootMassKg,
                surface == AudioSurface.Pavement ? AudioMaterial.Asphalt : AudioMaterial.Soil, surface, (uint)(foot + 1) * 2654435761u + (uint)step));
        }

        /// <summary>Appends at most <see cref="SliceAudioAssumptions.MaxEventsPerDrain"/> events (the highest energy; quake phases and footfalls are always kept) in publication order, drops the rest.</summary>
        public int Drain(List<AudioEvent> into)
        {
            int n = _pending.Count; if (n == 0) return 0;
            int cap = _a.MaxEventsPerDrain;
            if (n <= cap) { into.AddRange(_pending); _pending.Clear(); _pendingEnergy.Clear(); return n; }
            // keep: all "always" kinds, then the strongest of the rest up to the cap
            var keep = new bool[n]; int kept = 0;
            for (int i = 0; i < n; i++) if (Always(_pending[i].Kind)) { keep[i] = true; kept++; }
            var order = new List<int>(n); for (int i = 0; i < n; i++) if (!keep[i]) order.Add(i);
            order.Sort((p, q) => { int c = _pendingEnergy[q].CompareTo(_pendingEnergy[p]); return c != 0 ? c : p.CompareTo(q); });
            for (int k = 0; k < order.Count && kept < cap; k++) { keep[order[k]] = true; kept++; }
            int outCount = 0;
            for (int i = 0; i < n; i++) if (keep[i]) { into.Add(_pending[i]); outCount++; } else _stats.DroppedCap++;
            _pending.Clear(); _pendingEnergy.Clear();
            return outCount;
        }

        static bool Always(AudioEventKind k) => k == AudioEventKind.QuakeP || k == AudioEventKind.QuakeS || k == AudioEventKind.QuakeRumble || k == AudioEventKind.FootContact || k == AudioEventKind.MuzzleBlast;
    }
}
