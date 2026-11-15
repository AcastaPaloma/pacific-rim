using System;
namespace Game.Audio
{
    public readonly struct Mode
    {
        public readonly double FreqHz, T60S, Amplitude;
        public Mode(double f, double t60, double a) { FreqHz = f; T60S = t60; Amplitude = a; }
    }

    /// <summary>
    /// Modal sets for struck bodies: sum of damped sinusoids a_k exp(-6.9 t / T60_k) sin(2 pi f_k t) (van den Doel and Pai 1998). Frequencies scale with 1/length = m^(-1/3) (geometric similarity),
    /// so the spectral centroid falls with mass. Material ratios, damping and contact time are Assumed (R-132 section 8). Deterministic: detune comes from a hash of the event seed.
    /// </summary>
    public static class ModalSynth
    {
        public const int MaxModes = 8;

        struct MatParams { public double F1At1Kg, T60At1Kg, Tilt, Rolloff, ContactS1Kg; public double[] Ratios; }

        static MatParams For(AudioMaterial m)
        {
            switch (m)
            {   // F1 at 1 kg [Hz], T60 [s] at 1 kg, damping tilt (per mode), amplitude exponent, contact time at 1 kg [s], partial ratios
                case AudioMaterial.Steel: return new MatParams { F1At1Kg = 1800, T60At1Kg = 3.0, Tilt = 0.6, Rolloff = 0.9, ContactS1Kg = 4e-4, Ratios = new[] { 1, 2.756, 5.404, 8.933, 13.34, 18.64, 24.81, 31.87 } };
                case AudioMaterial.Concrete: return new MatParams { F1At1Kg = 1100, T60At1Kg = 0.5, Tilt = 2.5, Rolloff = 1.1, ContactS1Kg = 1.2e-3, Ratios = new[] { 1, 1.59, 2.14, 2.30, 2.65, 2.92, 3.16, 3.5 } };
                case AudioMaterial.Glass: return new MatParams { F1At1Kg = 3200, T60At1Kg = 0.9, Tilt = 1.2, Rolloff = 0.7, ContactS1Kg = 2.5e-4, Ratios = new[] { 1, 2.32, 4.25, 6.63, 9.38, 12.5, 16.0, 19.8 } };
                case AudioMaterial.Rock: return new MatParams { F1At1Kg = 1400, T60At1Kg = 0.8, Tilt = 2.0, Rolloff = 1.0, ContactS1Kg = 1e-3, Ratios = new[] { 1, 1.7, 2.4, 3.3, 4.1, 5.2, 6.4, 7.7 } };
                case AudioMaterial.Water: return new MatParams { F1At1Kg = 400, T60At1Kg = 0.25, Tilt = 3.0, Rolloff = 1.4, ContactS1Kg = 4e-3, Ratios = new[] { 1, 1.5, 2.1, 2.9, 3.8, 4.8, 6.0, 7.3 } };
                default:
                    return new MatParams { F1At1Kg = 700, T60At1Kg = 0.3, Tilt = 3.0, Rolloff = 1.3, ContactS1Kg = 3e-3, Ratios = new[] { 1, 1.4, 1.9, 2.5, 3.2, 4.0, 4.9, 5.9 } };
            }
        }

        /// <summary>Hash to [-1,1], deterministic (splitmix64-style).</summary>
        public static double Hash01(uint seed, int k)
        {
            ulong z = (((ulong)seed << 8) | (uint)k) + 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL; z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL; z ^= z >> 31;
            return (z >> 11) * (1.0 / 9007199254740992.0) * 2.0 - 1.0;
        }

        /// <summary>Contact duration in s: grows with size, m^(1/3); contact low-passes the excitation at about 1/(pi tc).</summary>
        public static double ContactTimeS(AudioMaterial mat, double massKg) => For(mat).ContactS1Kg * Math.Pow(Math.Max(massKg, 1e-3), 1.0 / 3.0);

        public static int Build(AudioMaterial mat, double massKg, uint seed, Mode[] into)
        {
            if (into == null || into.Length < MaxModes) throw new ArgumentException("buffer too small", nameof(into));
            var p = For(mat);
            double mm = Math.Max(massKg, 1e-3);
            double scale = Math.Pow(mm, -1.0 / 3.0);
            double tc = ContactTimeS(mat, mm);
            double t60Scale = Math.Pow(mm, 1.0 / 3.0); // larger bodies ring longer
            double fc = 1.0 / (Math.PI * tc);
            for (int k = 0; k < MaxModes; k++)
            {
                double detune = 1.0 + 0.01 * Hash01(seed, k);
                double f = p.F1At1Kg * scale * p.Ratios[k] * detune;
                double t60 = p.T60At1Kg * t60Scale / (1.0 + p.Tilt * k);
                double a = Math.Pow(k + 1, -p.Rolloff) / (1.0 + (f / fc) * (f / fc));
                into[k] = new Mode(f, t60, a);
            }
            return MaxModes;
        }

        /// <summary>Amplitude-weighted mean frequency, Hz.</summary>
        public static double SpectralCentroidHz(Mode[] modes, int count)
        {
            double sa = 0, sf = 0;
            for (int i = 0; i < count; i++) { sa += modes[i].Amplitude; sf += modes[i].Amplitude * modes[i].FreqHz; }
            return sa > 0 ? sf / sa : 0.0;
        }

        /// <summary>Renders the modal sum into dst (peak-normalised to peak); pure and deterministic. The engine adapter runs the same recurrence per audio block.</summary>
        public static void Render(Mode[] modes, int count, double sampleRate, float[] dst, int n, float peak)
        {
            Array.Clear(dst, 0, n);
            for (int k = 0; k < count; k++)
            {
                if (modes[k].FreqHz >= 0.5 * sampleRate) continue;
                double w = 2.0 * Math.PI * modes[k].FreqHz / sampleRate;
                double decay = Math.Exp(-6.9078 / (Math.Max(modes[k].T60S, 1e-4) * sampleRate));
                double env = modes[k].Amplitude;
                for (int i = 0; i < n; i++) { dst[i] += (float)(env * Math.Sin(w * i)); env *= decay; }
            }
            float mx = 0f;
            for (int i = 0; i < n; i++) mx = Math.Max(mx, Math.Abs(dst[i]));
            if (mx > 0f) { float g = peak / mx; for (int i = 0; i < n; i++) dst[i] *= g; }
        }
    }
}
