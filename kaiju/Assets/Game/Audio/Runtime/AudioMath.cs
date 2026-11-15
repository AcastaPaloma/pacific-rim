using System;
namespace Game.Audio
{
    /// <summary>Pure acoustics functions. No state, no engine, no clocks, no RNG: same inputs give bit-identical outputs. R-132.</summary>
    public static class AudioMath
    {
        public const double RefPressurePa = 20e-6;
        public const double RefPowerW = 1e-12;
        /// <summary>10 log10(2 pi): hemispherical radiation (sources on the ground), Lp(1 m) = Lw - 7.98.</summary>
        public const double HemisphereDb = 7.9818;

        /// <summary>Speed of sound in dry air, m/s: 343.2 sqrt(T/293.15 K) (ideal gas; humidity effect below 0.5 percent, ignored).</summary>
        public static double SpeedOfSound(double tempC) => 343.2 * Math.Sqrt((tempC + 273.15) / 293.15);

        /// <summary>Fraction of source energy radiated as sound and the burst duration, per kind. Assumed (R-132 section 7.2).</summary>
        public static void KindAcoustics(AudioEventKind kind, out double efficiency, out double durationS)
        {
            switch (kind)
            {
                case AudioEventKind.FootContact: efficiency = 2e-3; durationS = 0.08; break;
                case AudioEventKind.Impact: efficiency = 1e-2; durationS = 0.05; break;
                case AudioEventKind.StructureFailure: efficiency = 5e-3; durationS = 0.6; break;
                case AudioEventKind.DebrisRain: efficiency = 1e-3; durationS = 1.5; break;
                case AudioEventKind.QuakeP: case AudioEventKind.QuakeS: case AudioEventKind.QuakeRumble: efficiency = 1e-4; durationS = 8.0; break;
                case AudioEventKind.MuzzleBlast: efficiency = 5e-2; durationS = 0.02; break;
                case AudioEventKind.SupersonicCrack: efficiency = 1e-3; durationS = 0.005; break;
                case AudioEventKind.ProjectileWhistle: efficiency = 1e-6; durationS = 1.0; break;
                case AudioEventKind.WaterSplash: efficiency = 3e-3; durationS = 0.5; break;
                case AudioEventKind.Wade: efficiency = 1e-3; durationS = 0.5; break;
                default: efficiency = 1e-6; durationS = 1.0; break;
            }
        }

        /// <summary>Sound pressure level at 1 m (dB re 20 uPa) for an energy release: Lw = 10 log10(eta E / T / 1 pW), Lp = Lw - 7.98 (hemisphere); clamped to [0, maxDb].</summary>
        public static double SplAt1m(double energyJ, double efficiency, double durationS, double maxDb = 194.0)
        {
            if (!(energyJ > 0) || !(efficiency > 0) || !(durationS > 0)) return 0.0;
            double lw = 10.0 * Math.Log10(energyJ * efficiency / durationS / RefPowerW);
            double lp = lw - HemisphereDb;
            return Math.Min(Math.Max(lp, 0.0), maxDb);
        }

        public static double SplAt1m(in AudioEvent ev, AudioProfile p)
        {
            KindAcoustics(ev.Kind, out double eta, out double dur);
            return SplAt1m(ev.EnergyJ, eta, dur, p.MaxSplDb);
        }

        /// <summary>Spherical spreading relative to 1 m: 20 log10 r (r clamped at 1 m). Inverse square.</summary>
        public static double DistanceAttenuationDb(double distanceM) => 20.0 * Math.Log10(Math.Max(distanceM, 1.0));

        /// <summary>ISO 9613-1:1993 pure-tone attenuation coefficient, dB per metre. Valid 50 Hz to 10 kHz; used outside as a smooth extrapolation.</summary>
        public static double AtmosphericAbsorptionDbPerM(double freqHz, double tempC, double relHumidityPct, double pressureKPa)
        {
            const double T0 = 293.15, T01 = 273.16, pr = 101.325;
            double T = tempC + 273.15;
            double c = -6.8346 * Math.Pow(T01 / T, 1.261) + 4.6151;
            double h = relHumidityPct * Math.Pow(10.0, c) * (pr / pressureKPa);
            double frO = (pressureKPa / pr) * (24.0 + 4.04e4 * h * (0.02 + h) / (0.391 + h));
            double frN = (pressureKPa / pr) * Math.Pow(T / T0, -0.5) * (9.0 + 280.0 * h * Math.Exp(-4.170 * (Math.Pow(T / T0, -1.0 / 3.0) - 1.0)));
            double f2 = freqHz * freqHz;
            return 8.686 * f2 * (1.84e-11 * (pr / pressureKPa) * Math.Sqrt(T / T0)
                + Math.Pow(T / T0, -2.5) * (0.01275 * Math.Exp(-2239.1 / T) / (frO + f2 / frO) + 0.1068 * Math.Exp(-3352.0 / T) / (frN + f2 / frN)));
        }

        public static double AtmosphericAbsorptionDb(double freqHz, double distanceM, AudioProfile p)
            => AtmosphericAbsorptionDbPerM(freqHz, p.TemperatureC, p.RelativeHumidityPct, p.PressureKPa) * Math.Max(distanceM, 0.0);

        /// <summary>Octave band centres used by the mixer, Hz (16 Hz covers infrasound content).</summary>
        public static readonly double[] BandCentresHz = { 16, 31.5, 63, 125, 250, 500, 1000, 2000, 4000, 8000 };

        /// <summary>Lowest 1/3-octave frequency (20 Hz to 20 kHz) at which air absorption over the path reaches 6 dB: the low-pass corner for the voice.</summary>
        public static double AtmosphericLowpassHz(double distanceM, AudioProfile p)
        {
            for (int k = 0; k <= 30; k++)
            {
                double f = 20.0 * Math.Pow(2.0, k / 3.0);
                if (f > 20000.0) break;
                if (AtmosphericAbsorptionDb(f, distanceM, p) >= 6.0) return f;
            }
            return 20000.0;
        }

        public static double PropagationDelayS(double distanceM, double speedOfSound) => Math.Max(distanceM, 0.0) / speedOfSound;

        /// <summary>Received / emitted frequency ratio, source and listener moving, velocities projected on the line source to listener: (c - vL_toward_source...) form
        /// f_obs/f_src = (c + vL_r) / (c + vS_r) with r pointing from source to listener, v_r = dot(v, r_hat). Clamped so a supersonic source gives a finite ratio.</summary>
        public static double DopplerRatio(in AudioEvent src, in Listener l, double speedOfSound)
        {
            double dx = l.X - src.X, dy = l.Y - src.Y, dz = l.Z - src.Z;
            double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (d < 1e-9) return 1.0;
            double rx = dx / d, ry = dy / d, rz = dz / d;
            double vs = src.VelX * rx + src.VelY * ry + src.VelZ * rz; // source toward listener positive
            double vl = l.VelX * rx + l.VelY * ry + l.VelZ * rz;       // listener away from source positive
            double num = speedOfSound + vl;
            double den = speedOfSound - vs;
            if (den < 0.1 * speedOfSound) den = 0.1 * speedOfSound; // supersonic approach: bounded (the crack, not pitch, carries that case)
            return Math.Min(Math.Max(num / den, 0.25), 4.0);
        }

        /// <summary>Cheap building occlusion: count of blocking buildings on the straight segment (from the adapter's line-of-sight test) -> dB and low-pass cutoff factor.</summary>
        public static double OcclusionDb(int blockers, AudioProfile p) => Math.Min(Math.Max(blockers, 0) * p.OcclusionDbPerBlocker, p.OcclusionMaxDb);
        public static double OcclusionLowpassFactor(int blockers) => Math.Pow(0.5, Math.Min(Math.Max(blockers, 0), 4));

        /// <summary>Perceptual compression of physical level to mix level: linear in dB between (floor SPL, floor dBFS) and (ceil SPL, ceil dBFS); below floor returns -infinity.</summary>
        public static double SplToDbfs(double splDb, AudioProfile p)
        {
            if (splDb <= p.MapFloorSplDb) return double.NegativeInfinity;
            double t = Math.Min((splDb - p.MapFloorSplDb) / (p.MapCeilSplDb - p.MapFloorSplDb), 1.0);
            return p.MapFloorDbfs + t * (p.MapCeilDbfs - p.MapFloorDbfs);
        }

        public static double DbfsToLinear(double dbfs) => double.IsNegativeInfinity(dbfs) ? 0.0 : Math.Pow(10.0, dbfs / 20.0);

        /// <summary>Constant-power pan from source azimuth relative to listener heading; returns pan in [-1,1] (+ = right) and Woodworth ITD in s.</summary>
        public static void PanAndItd(in AudioEvent src, in Listener l, double speedOfSound, out double pan, out double itdS)
        {
            double dx = src.X - l.X, dz = src.Z - l.Z;
            double az = Math.Atan2(dx, dz) - l.HeadingRad; // 0 = ahead, +pi/2 = right
            double sinAz = Math.Sin(az);
            double hyp = Math.Sqrt(dx * dx + dz * dz);
            pan = hyp < 1e-6 ? 0.0 : Math.Min(Math.Max(sinAz, -1.0), 1.0);
            double th = Math.Asin(pan);
            itdS = 0.0875 / speedOfSound * (th + Math.Sin(th)); // positive = source on the right, left ear later (sign convention: + means source right)
        }

        public static void PanGains(double pan, out double left, out double right)
        {
            double a = (pan + 1.0) * Math.PI * 0.25;
            left = Math.Cos(a); right = Math.Sin(a);
        }

        // ---- Seismic ----
        /// <summary>Seismic moment, N m, from Mw (Hanks and Kanamori 1979).</summary>
        public static double SeismicMomentNm(double mw) => Math.Pow(10.0, 1.5 * mw + 9.1);

        /// <summary>Brune corner frequency, Hz: fc = 0.49 beta (dSigma / M0)^(1/3) (Brune 1970; beta 3500 m/s, stress drop 3 MPa assumed).</summary>
        public static double QuakeCornerFrequencyHz(double mw, double stressDropPa = 3e6, double betaMs = 3500.0)
            => 0.49 * betaMs * Math.Pow(stressDropPa / SeismicMomentNm(mw), 1.0 / 3.0);

        /// <summary>Seconds after origin at which the P / S wave reaches a point at hypocentral distance r (vp 6000, vs 3500 m/s upper-crust assumed).</summary>
        public static double QuakeArrivalS(double hypocentralDistanceM, bool sWave) => hypocentralDistanceM / (sWave ? 3500.0 : 6000.0);

        /// <summary>Audible rumble level, dB SPL, from peak ground acceleration in g (Assumed mapping: 60 dB at 0.01 g, 20 dB per decade, clamp 0..120).</summary>
        public static double QuakeRumbleDb(double pgaG)
            => Math.Min(Math.Max(60.0 + 20.0 * Math.Log10(Math.Max(pgaG, 1e-6) / 0.01), 0.0), 120.0);
    }
}
