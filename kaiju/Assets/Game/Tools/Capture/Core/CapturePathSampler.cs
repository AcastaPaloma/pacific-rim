// Pure maths of capture_path@v1 (T-147): time is the frame index (n / fps), never a wall clock; shake is a stateless hash of (seed, frame, axis).
using System;

namespace Game.Tools.Capture
{
    public struct CaptureSample
    {
        public double T, E, N, Alt, YawDeg, PitchDeg, Hfov;
        /// <summary>Shake offset in internal texels, +x right, +y up.</summary>
        public double ShakeXPx, ShakeYPx;
    }

    public static class CapturePathSampler
    {
        public static CaptureSample Sample(CapturePath p, int frame)
        {
            double t = Math.Min(frame / (double)p.Fps, p.DurationS);
            var k = p.Keyframes;
            int seg = 0;
            while (seg < k.Count - 2 && t >= k[seg + 1].T) seg++;
            double u = (t - k[seg].T) / (k[seg + 1].T - k[seg].T);
            u = Math.Max(0.0, Math.Min(1.0, u));
            double ue = p.Interpolation == "ease_in_out" ? u * u * (3.0 - 2.0 * u) : u;
            var s = new CaptureSample { T = t };
            if (p.Interpolation == "catmull_rom")
            {
                var p0 = k[Math.Max(seg - 1, 0)]; var p1 = k[seg]; var p2 = k[seg + 1]; var p3 = k[Math.Min(seg + 2, k.Count - 1)];
                s.E = CatmullRom(p0.E, p1.E, p2.E, p3.E, u); s.N = CatmullRom(p0.N, p1.N, p2.N, p3.N, u); s.Alt = CatmullRom(p0.Alt, p1.Alt, p2.Alt, p3.Alt, u);
            }
            else
            {
                s.E = Lerp(k[seg].E, k[seg + 1].E, ue); s.N = Lerp(k[seg].N, k[seg + 1].N, ue); s.Alt = Lerp(k[seg].Alt, k[seg + 1].Alt, ue);
            }
            // angles: shortest arc between consecutive keyframes (a single-axis slerp), eased like position unless catmull_rom (then linear)
            double ua = p.Interpolation == "catmull_rom" ? u : ue;
            s.YawDeg = k[seg].Yaw + ShortestArc(k[seg].Yaw, k[seg + 1].Yaw) * ua;
            s.PitchDeg = Lerp(k[seg].Pitch, k[seg + 1].Pitch, ua);
            s.Hfov = Lerp(double.IsNaN(k[seg].Hfov) ? 0 : k[seg].Hfov, double.IsNaN(k[seg + 1].Hfov) ? 0 : k[seg + 1].Hfov, ua);
            double amp = 0;
            foreach (var sh in p.Shake) if (t >= sh.T) amp += sh.AmpPx * Math.Exp(-(t - sh.T) / sh.DecayS);
            if (amp > 1e-9)
            {
                s.ShakeXPx = amp * Noise(p.Seed, frame, 0); s.ShakeYPx = amp * Noise(p.Seed, frame, 1);
            }
            return s;
        }

        public static double Lerp(double a, double b, double u) => a + (b - a) * u;

        public static double ShortestArc(double fromDeg, double toDeg)
        {
            double d = (toDeg - fromDeg) % 360.0;
            if (d > 180.0) d -= 360.0; else if (d < -180.0) d += 360.0;
            return d;
        }

        static double CatmullRom(double a, double b, double c, double d, double u)
        {
            double u2 = u * u, u3 = u2 * u;
            return 0.5 * ((2.0 * b) + (-a + c) * u + (2.0 * a - 5.0 * b + 4.0 * c - d) * u2 + (-a + 3.0 * b - 3.0 * c + d) * u3);
        }

        /// <summary>Stateless xorshift32 hash in [-1, 1]: the same (seed, frame, axis) always gives the same value, in any frame order.</summary>
        public static double Noise(long seed, int frame, int axis)
        {
            uint x = unchecked((uint)(seed * 2654435761L) ^ (uint)(frame * 40503 + 1) ^ (uint)(axis * 977 + 13));
            if (x == 0) x = 0x9E3779B9u;
            for (int i = 0; i < 4; i++) { x ^= x << 13; x ^= x >> 17; x ^= x << 5; }
            return x / (double)uint.MaxValue * 2.0 - 1.0;
        }
    }
}
