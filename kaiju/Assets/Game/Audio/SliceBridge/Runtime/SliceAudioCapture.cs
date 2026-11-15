// T-188: headless capture. Renders a scripted event sequence through AudioPlanner + SliceAudioEngine (+ Limiter) to 16-bit stereo WAV bytes and a metrics record. Pure C#, deterministic.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Game.Audio.SliceBridge
{
    public sealed class SliceCaptureResult
    {
        public byte[] Wav;
        public float PeakOut, PeakPreLimiter;
        public int PeakActive, MaxVoices, Started, Submitted, Stolen, RejectedByPlanner, LimiterBlocks, Frames;
        public double LimiterCeiling;
        public readonly Dictionary<AudioEventKind, double> SoloRms = new Dictionary<AudioEventKind, double>();
        public readonly Dictionary<AudioEventKind, double> SoloPeak = new Dictionary<AudioEventKind, double>();
        public readonly Dictionary<AudioEventKind, int> StartedByKind = new Dictionary<AudioEventKind, int>();
        public string Sha256Hex;

        public string ToJson()
        {
            var ci = CultureInfo.InvariantCulture; var sb = new StringBuilder();
            sb.Append("{\n  \"task\": \"T-188\", \"frames\": ").Append(Frames).Append(", \"seconds\": ").Append((Frames / 48000.0).ToString("F2", ci)).Append(",\n");
            sb.Append("  \"peak_out\": ").Append(PeakOut.ToString("F4", ci)).Append(", \"peak_pre_limiter\": ").Append(PeakPreLimiter.ToString("F4", ci))
              .Append(", \"limiter_ceiling\": ").Append(LimiterCeiling.ToString("F4", ci)).Append(", \"limiter_blocks\": ").Append(LimiterBlocks).Append(",\n");
            sb.Append("  \"voices\": { \"max\": ").Append(MaxVoices).Append(", \"peak_active\": ").Append(PeakActive).Append(", \"submitted\": ").Append(Submitted)
              .Append(", \"started\": ").Append(Started).Append(", \"stolen\": ").Append(Stolen).Append(", \"rejected\": ").Append(RejectedByPlanner).Append(" },\n");
            sb.Append("  \"wav_sha256\": \"").Append(Sha256Hex).Append("\",\n  \"per_kind\": [");
            bool first = true;
            foreach (var kv in SoloRms)
            {
                if (!first) sb.Append(","); first = false;
                SoloPeak.TryGetValue(kv.Key, out double pk);
                sb.Append("\n    { \"kind\": \"").Append(kv.Key).Append("\", \"solo_rms\": ").Append(kv.Value.ToString("E3", ci)).Append(", \"solo_peak\": ").Append(pk.ToString("F4", ci)).Append(" }");
            }
            sb.Append("\n  ]\n}\n");
            return sb.ToString();
        }
    }

    public static class SliceAudioCapture
    {
        public const int Block = 512;

        public sealed class ScriptItem
        {
            public double TimeS; public AudioEvent Event;
            public ScriptItem(double t, AudioEvent e) { TimeS = t; Event = e; }
        }

        static AudioEvent E(long id, AudioEventKind k, double x, double y, double z, double energy, double mass, AudioMaterial m, AudioSurface s, uint seed = 7)
            => new AudioEvent(id, k, 0, x, y, z, energy, 0, mass, m, s, seed);

        /// <summary>One event of each class, 100-300 m from a listener at the origin facing +z (quakes 15 km).</summary>
        public static List<ScriptItem> SoloScript()
        {
            var a = new SliceAudioAssumptions();
            double footE = 0.5 * a.MechMassKg * a.FootEffectiveMassFraction * a.FootLandingSpeedMs * a.FootLandingSpeedMs;
            var l = new List<ScriptItem>
            {
                new ScriptItem(0, E(1, AudioEventKind.FootContact, 30, 0, 60, footE, a.FootMassKg, AudioMaterial.Asphalt, AudioSurface.Pavement)),
                new ScriptItem(0, E(2, AudioEventKind.Impact, -80, 5, 150, 4e8, 5e4, AudioMaterial.Concrete, AudioSurface.Building)),
                new ScriptItem(0, E(3, AudioEventKind.StructureFailure, 60, 20, 200, 2e9, 1e6, AudioMaterial.Concrete, AudioSurface.Building)),
                new ScriptItem(0, E(4, AudioEventKind.DebrisRain, 60, 0, 200, 2e8, 1e4, AudioMaterial.Concrete, AudioSurface.Pavement)),
                new ScriptItem(0, E(5, AudioEventKind.QuakeP, 9000, -8000, 12000, 1e13, 0, AudioMaterial.Rock, AudioSurface.Ground)),
                new ScriptItem(0, E(6, AudioEventKind.QuakeS, 9000, -8000, 12000, 4e13, 0, AudioMaterial.Rock, AudioSurface.Ground)),
                new ScriptItem(0, E(7, AudioEventKind.QuakeRumble, 9000, -8000, 12000, 5e13, 0, AudioMaterial.Rock, AudioSurface.Ground)),
                new ScriptItem(0, E(8, AudioEventKind.MuzzleBlast, 20, 30, 250, 5e8, 100, AudioMaterial.Steel, AudioSurface.Ground)),
                new ScriptItem(0, E(9, AudioEventKind.SupersonicCrack, 10, 10, 120, 1e7, 100, AudioMaterial.Steel, AudioSurface.Ground)),
            };
            return l;
        }

        /// <summary>The full scripted sequence: walk, shots, impacts, a failing building, a quake, a kaiju rise, then a 120-event burst that must be capped by the voice pool and the limiter.</summary>
        public static List<ScriptItem> FullScript()
        {
            var a = new SliceAudioAssumptions(); var l = new List<ScriptItem>(); long id = 100;
            double footE = 0.5 * a.MechMassKg * a.FootEffectiveMassFraction * a.FootLandingSpeedMs * a.FootLandingSpeedMs;
            for (int i = 0; i < 6; i++) l.Add(new ScriptItem(0.5 + i * 1.2, new AudioEvent(id++, AudioEventKind.FootContact, i, i % 2 == 0 ? -10 : 10, 0, 40 + i * 20, footE, a.MechMassKg * a.FootEffectiveMassFraction * a.FootLandingSpeedMs, a.FootMassKg, AudioMaterial.Asphalt, AudioSurface.Pavement, (uint)(i + 1))));
            l.Add(new ScriptItem(2.0, E(id++, AudioEventKind.MuzzleBlast, 20, 30, 250, 5e8, 100, AudioMaterial.Steel, AudioSurface.Ground)));
            l.Add(new ScriptItem(2.8, E(id++, AudioEventKind.Impact, -80, 5, 150, 4e8, 5e4, AudioMaterial.Concrete, AudioSurface.Building)));
            l.Add(new ScriptItem(3.0, E(id++, AudioEventKind.StructureFailure, -80, 20, 150, 2e9, 1e6, AudioMaterial.Concrete, AudioSurface.Building)));
            l.Add(new ScriptItem(3.1, E(id++, AudioEventKind.DebrisRain, -80, 0, 150, 2e8, 1e4, AudioMaterial.Concrete, AudioSurface.Pavement)));
            l.Add(new ScriptItem(6.0, E(id++, AudioEventKind.QuakeP, 9000, -8000, 12000, 1e13, 0, AudioMaterial.Rock, AudioSurface.Ground)));
            l.Add(new ScriptItem(6.0, E(id++, AudioEventKind.QuakeS, 9000, -8000, 12000, 4e13, 0, AudioMaterial.Rock, AudioSurface.Ground)));
            l.Add(new ScriptItem(6.0, E(id++, AudioEventKind.QuakeRumble, 9000, -8000, 12000, 5e13, 0, AudioMaterial.Rock, AudioSurface.Ground)));
            l.Add(new ScriptItem(11.0, E(id++, AudioEventKind.Impact, 400, 0, 900, a.KaijuRiseEnergyJ, a.KaijuRiseMassKg, AudioMaterial.Rock, AudioSurface.Ground)));
            // burst: 120 loud mixed events in the same block (simultaneous collapse during the quake)
            for (int i = 0; i < 120; i++)
            {
                var k = i % 3 == 0 ? AudioEventKind.StructureFailure : i % 3 == 1 ? AudioEventKind.DebrisRain : AudioEventKind.Impact;
                l.Add(new ScriptItem(14.0, E(id++, k, -100 + i * 3, 10, 80 + (i % 7) * 15, 1e9 + i * 1e7, 2e5, AudioMaterial.Concrete, AudioSurface.Building, (uint)(i + 11))));
            }
            l.Sort((p, q) => p.TimeS.CompareTo(q.TimeS));
            return l;
        }

        public static SliceCaptureResult Run(List<ScriptItem> script, double seconds, AudioProfile profile = null, bool writeWav = true)
        {
            var p = profile ?? new AudioProfile();
            var eng = new SliceAudioEngine(p);
            eng.SetListener(new Listener(0, 0, 0, 0.0));
            int total = (int)Math.Ceiling(seconds * p.SampleRate); total = (total + Block - 1) / Block * Block;
            var l = new float[Block]; var r = new float[Block];
            var pcm = new short[total * 2];
            int si = 0; var batch = new List<AudioEvent>();
            for (int pos = 0; pos < total; pos += Block)
            {
                double tNow = pos / p.SampleRate; batch.Clear();
                while (si < script.Count && script[si].TimeS <= tNow + 1e-9) batch.Add(script[si++].Event);
                if (batch.Count > 0) eng.Submit(batch);
                eng.Render(l, r, Block);
                for (int i = 0; i < Block; i++) { pcm[(pos + i) * 2] = To16(l[i]); pcm[(pos + i) * 2 + 1] = To16(r[i]); }
            }
            var st = eng.Stats;
            var res = new SliceCaptureResult
            {
                PeakOut = st.PeakOut, PeakPreLimiter = st.PeakPreLimiter, PeakActive = st.PeakActive, MaxVoices = p.MaxVoices, Started = st.Started, Submitted = st.Submitted,
                Stolen = st.Stolen, RejectedByPlanner = st.RejectedByPlanner, LimiterBlocks = st.LimiterBlocks, Frames = total, LimiterCeiling = p.LimiterCeiling
            };
            for (int k = 0; k <= (int)AudioEventKind.Ambience; k++) { int n = eng.StartedOfKind((AudioEventKind)k); if (n > 0) res.StartedByKind[(AudioEventKind)k] = n; }
            if (writeWav)
            {
                res.Wav = Wav16(pcm, (int)p.SampleRate);
                using (var sha = System.Security.Cryptography.SHA256.Create()) res.Sha256Hex = BitConverter.ToString(sha.ComputeHash(res.Wav)).Replace("-", "").ToLowerInvariant();
            }
            return res;
        }

        /// <summary>Renders each solo event alone in a fresh engine for its own length (quakes: until after the seismic arrival) and records RMS and peak of the mix.</summary>
        public static void SoloMetrics(SliceCaptureResult into, AudioProfile profile = null)
        {
            var p = profile ?? new AudioProfile();
            foreach (var item in SoloScript())
            {
                var eng = new SliceAudioEngine(p); eng.SetListener(new Listener(0, 0, 0, 0.0));
                eng.Submit(item.Event);
                var k = item.Event.Kind;
                double arrive = (k == AudioEventKind.QuakeP || k == AudioEventKind.QuakeS || k == AudioEventKind.QuakeRumble)
                    ? AudioMath.QuakeArrivalS(Math.Sqrt(item.Event.X * item.Event.X + item.Event.Y * item.Event.Y + item.Event.Z * item.Event.Z), k == AudioEventKind.QuakeS) : 0.0;
                double seconds = arrive + 13.0;
                int frames = (int)(seconds * p.SampleRate) / Block * Block;
                var l = new float[Block]; var r = new float[Block]; double ss = 0; double pk = 0; long n = 0;
                for (int pos = 0; pos < frames; pos += Block)
                {
                    eng.Render(l, r, Block);
                    for (int i = 0; i < Block; i++) { ss += (double)l[i] * l[i] + (double)r[i] * r[i]; pk = Math.Max(pk, Math.Max(Math.Abs(l[i]), Math.Abs(r[i]))); n += 2; }
                }
                into.SoloRms[k] = Math.Sqrt(ss / Math.Max(n, 1)); into.SoloPeak[k] = pk;
            }
        }

        static short To16(float x) { double v = Math.Round(x * 32767.0); return (short)Math.Max(-32768, Math.Min(32767, v)); }

        public static byte[] Wav16(short[] interleaved, int sampleRate)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                int dataBytes = interleaved.Length * 2;
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + dataBytes); w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(sampleRate); w.Write(sampleRate * 4); w.Write((short)4); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(dataBytes);
                for (int i = 0; i < interleaved.Length; i++) w.Write(interleaved[i]);
                w.Flush(); return ms.ToArray();
            }
        }
    }
}
