using System;
namespace Game.Audio
{
    public readonly struct PropagationResult
    {
        public readonly double DistanceM, DelayS, SplAt1mDb, SplAtListenerDb, LowpassHz, Doppler, Pan, ItdS, Score;
        public PropagationResult(double d, double delay, double spl1, double splL, double lp, double dop, double pan, double itd, double score)
        { DistanceM = d; DelayS = delay; SplAt1mDb = spl1; SplAtListenerDb = splL; LowpassHz = lp; Doppler = dop; Pan = pan; ItdS = itd; Score = score; }
    }

    /// <summary>Glue of the pure functions: event + listener + profile (+ blockers from the adapter's line-of-sight test) to a plan, then voice grant. Deterministic.</summary>
    public static class AudioPlanner
    {
        public static PropagationResult Plan(in AudioEvent ev, in Listener l, AudioProfile p, int blockers = 0)
        {
            double dx = ev.X - l.X, dy = ev.Y - l.Y, dz = ev.Z - l.Z;
            double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            double c = AudioMath.SpeedOfSound(p.TemperatureC);
            double spl1 = AudioMath.SplAt1m(ev, p);
            double bandDb = AudioMath.AtmosphericAbsorptionDb(DominantHz(ev), d, p); // absorption in the dominant band
            double splL = Math.Max(0.0, spl1 - AudioMath.DistanceAttenuationDb(d) - bandDb - AudioMath.OcclusionDb(blockers, p));
            double lp = Math.Min(AudioMath.AtmosphericLowpassHz(d, p), 20000.0 * AudioMath.OcclusionLowpassFactor(blockers));
            AudioMath.PanAndItd(ev, l, c, out double pan, out double itd);
            double score = VoiceAllocator.Score(splL, p.AudibleFloorDb, ev.Kind);
            return new PropagationResult(d, AudioMath.PropagationDelayS(d, c), spl1, splL, lp, AudioMath.DopplerRatio(ev, l, c), pan, itd, score);
        }

        /// <summary>Dominant frequency of the event for band absorption: first modal frequency of the body (kinds with a modal source), else a fixed per-kind value. Assumed.</summary>
        public static double DominantHz(in AudioEvent ev)
        {
            switch (ev.Kind)
            {
                case AudioEventKind.QuakeP: case AudioEventKind.QuakeS: case AudioEventKind.QuakeRumble: return 16.0;
                case AudioEventKind.MuzzleBlast: return 100.0;
                case AudioEventKind.SupersonicCrack: return 2000.0;
                case AudioEventKind.ProjectileWhistle: return 1500.0;
                case AudioEventKind.Ambience: return 500.0;
                default:
                    var m = new Mode[ModalSynth.MaxModes];
                    ModalSynth.Build(ev.Material, ev.MassKg > 0 ? ev.MassKg : 1.0, ev.Seed, m);
                    return Math.Min(Math.Max(m[0].FreqHz, 16.0), 8000.0);
            }
        }

        /// <summary>Plan then allocate. Returns the command and whether it was accepted; <paramref name="stolenId"/> is the event id whose voice was stolen (-1 none).</summary>
        public static bool TryStart(in AudioEvent ev, in Listener l, AudioProfile p, VoiceAllocator alloc, int blockers, out VoiceCommand cmd, out long stolenId)
        {
            var r = Plan(ev, l, p, blockers);
            var g = alloc.Request(ev.Id, ev.Kind, r.Score, ev.Tick);
            stolenId = g.StolenEventId;
            if (!g.Accepted) { cmd = default; return false; }
            cmd = new VoiceCommand(g.VoiceId, ev, r.DelayS, (float)r.SplAtListenerDb, (float)r.LowpassHz, (float)r.Doppler, (float)r.Pan, (float)r.ItdS);
            return true;
        }
    }
}
