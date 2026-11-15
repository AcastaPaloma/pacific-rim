// Audio contracts (layer L3, engine-free): physical audio events, listener, profile, sink / mixer interfaces. F-070 (proposed), F-009 (footfall sync), M-009, R-132, T-190.
// Positions are world metres in double (same frame as the producers); energy in joules, impulse in N s, mass in kg.
namespace Game.Audio
{
    public static class AudioContractVersions
    {
        public const int EventApi = 1;
        public const int ProfileRevision = 1;
    }

    public static class AudioFlags
    {
        /// <summary>Physical (energy driven, procedural) audio. Default off. Flag file Config/FeatureFlags/Audio.json is added by the Unity adapter task (T-195).</summary>
        public const string Physical = "audio.physical";
    }

    /// <summary>What produced the sound. Append only (values are persisted in capture logs).</summary>
    public enum AudioEventKind : byte
    {
        FootContact = 0,       // Physics.Body FootContact / FootLoaded
        Impact = 1,            // Combat ImpactImpulse (shell, melee)
        StructureFailure = 2,  // Destruction BuildingDamaged state change
        DebrisRain = 3,        // Destruction DebrisKg increment
        QuakeP = 4,            // Seismic QuakeBegan + P travel time
        QuakeS = 5,            // S arrival
        QuakeRumble = 6,       // continuous infrasonic / low rumble while shaking
        MuzzleBlast = 7,       // Combat WeaponFired
        ProjectileWhistle = 8, // Combat shell in flight (Doppler source)
        SupersonicCrack = 9,   // Combat shell above Mach 1 (N wave)
        WaterSplash = 10,
        Wade = 11,
        Ambience = 12
    }

    public enum AudioMaterial : byte { Steel = 0, Concrete = 1, Glass = 2, Soil = 3, Rock = 4, Water = 5, Asphalt = 6, Hide = 7 }

    public enum AudioSurface : byte { Ground = 0, Pavement = 1, Water = 2, Mud = 3, Rock = 4, Building = 5 }

    /// <summary>One sound-producing physical event. Producers (Unity adapter) build these from bus events; the audio runtime never sees engine types.</summary>
    public readonly struct AudioEvent
    {
        public readonly long Id;            // unique, monotone (bus ContactId or adapter counter)
        public readonly AudioEventKind Kind;
        public readonly long Tick;          // simulation step of the physical event (exact contact frame, M-009)
        public readonly double X, Y, Z;     // world, m
        public readonly double VelX, VelY, VelZ; // source velocity m/s (Doppler); 0 for static
        public readonly double EnergyJ;     // energy delivered / released at the source
        public readonly double ImpulseNs;
        public readonly double MassKg;      // mass of the sounding body (foot/mech, shell, building)
        public readonly AudioMaterial Material;
        public readonly AudioSurface Surface;
        public readonly uint Seed;          // for deterministic detune / grain placement

        public AudioEvent(long id, AudioEventKind kind, long tick, double x, double y, double z, double energyJ, double impulseNs, double massKg,
            AudioMaterial material, AudioSurface surface, uint seed = 0, double velX = 0, double velY = 0, double velZ = 0)
        {
            Id = id; Kind = kind; Tick = tick; X = x; Y = y; Z = z; EnergyJ = energyJ; ImpulseNs = impulseNs; MassKg = massKg;
            Material = material; Surface = surface; Seed = seed; VelX = velX; VelY = velY; VelZ = velZ;
        }
    }

    public readonly struct Listener
    {
        public readonly double X, Y, Z;
        public readonly double VelX, VelY, VelZ;
        public readonly double HeadingRad; // atan2(x, z), same convention as Combat
        public Listener(double x, double y, double z, double headingRad, double velX = 0, double velY = 0, double velZ = 0)
        { X = x; Y = y; Z = z; HeadingRad = headingRad; VelX = velX; VelY = velY; VelZ = velZ; }
    }

    /// <summary>Everything the sink needs to start one voice. The delay is already the speed-of-sound delay.</summary>
    public readonly struct VoiceCommand
    {
        public readonly int VoiceId;
        public readonly AudioEvent Event;
        public readonly double StartDelayS;  // from event tick time, s (propagation delay)
        public readonly float SplAtListenerDb;
        public readonly float LowpassHz;
        public readonly float PitchRatio;    // Doppler
        public readonly float Pan;           // -1 left .. +1 right
        public readonly float ItdS;          // interaural time difference, s; + = source on the right (left ear delayed)
        public VoiceCommand(int voiceId, in AudioEvent ev, double startDelayS, float spl, float lowpassHz, float pitch, float pan, float itdS)
        { VoiceId = voiceId; Event = ev; StartDelayS = startDelayS; SplAtListenerDb = spl; LowpassHz = lowpassHz; PitchRatio = pitch; Pan = pan; ItdS = itdS; }
    }

    /// <summary>Producer side: anything that can hand over events produced since the last drain (the bus adapter in Unity, a script in tests).</summary>
    public interface IAudioEmitter
    {
        /// <summary>Appends pending events to <paramref name="into"/> in publication order and clears them. Returns the count appended.</summary>
        int Drain(System.Collections.Generic.List<AudioEvent> into);
    }

    /// <summary>Consumer side: the engine adapter (AudioSource pool, OnAudioFilterRead synth).</summary>
    public interface IAudioSink
    {
        void Play(in VoiceCommand command);
        void Stop(int voiceId);
    }

    /// <summary>Final stereo mix. The deterministic limiter lives behind this (Game.Audio.Limiter).</summary>
    public interface IAudioMixer
    {
        float MasterGain { get; set; }
        /// <summary>Fills <paramref name="count"/> frames. Output must satisfy |sample| less than or equal to the profile limiter ceiling.</summary>
        void Render(float[] left, float[] right, int count);
    }

    /// <summary>Tunables. All Assumed unless R-132 says Sourced; changing a default bumps AudioContractVersions.ProfileRevision.</summary>
    public sealed class AudioProfile
    {
        public double TemperatureC = 15.0;
        public double RelativeHumidityPct = 75.0;   // Bay marine layer
        public double PressureKPa = 101.325;
        public int MaxVoices = 32;
        public int ReservedCriticalVoices = 6;      // quake, muzzle blast, big structure failures may use these when full
        public double AudibleFloorDb = 25.0;        // cull below this at the listener (after masking by ambience)
        public double MaxSplDb = 194.0;             // linear acoustics limit at 1 atm (1 atm peak ~ 194 dB)
        public double StealHysteresisDb = 3.0;
        public double OcclusionDbPerBlocker = 8.0;
        public double OcclusionMaxDb = 30.0;
        public double LimiterCeiling = 0.89;        // linear, about -1 dBFS
        public double LimiterReleaseS = 0.25;
        public double SampleRate = 48000.0;
        // Perceptual compression: physical SPL at the listener -> mix level. R-132 section 7.
        public double MapCeilSplDb = 140.0, MapFloorSplDb = 40.0, MapCeilDbfs = -3.0, MapFloorDbfs = -60.0;
    }
}
