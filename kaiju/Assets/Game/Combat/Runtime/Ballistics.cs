// Fixed-step deterministic point-mass ballistics in SI units (research/R-027.md section 1).
// Integrator: classical RK4 on (position, velocity) using only + - * / and sqrt, so results are bit-reproducible in any process.
using System;

namespace Game.Combat
{
    /// <summary>Drag coefficient against Mach number, piecewise linear, clamped at both ends. One point = constant.</summary>
    public sealed class DragCurve
    {
        private readonly double[] _mach;
        private readonly double[] _cd;

        public DragCurve(double[] mach, double[] cd)
        {
            if (mach == null || cd == null || mach.Length == 0 || mach.Length != cd.Length) throw new ArgumentException("mach and cd must be equal-length and non-empty");
            for (int i = 1; i < mach.Length; i++) if (!(mach[i] > mach[i - 1])) throw new ArgumentException("mach must be strictly increasing");
            for (int i = 0; i < cd.Length; i++) if (!(cd[i] >= 0) || double.IsInfinity(cd[i])) throw new ArgumentException("cd must be finite and non-negative");
            _mach = (double[])mach.Clone(); _cd = (double[])cd.Clone();
        }

        public static DragCurve Constant(double cd) => new DragCurve(new[] { 0.0 }, new[] { cd });

        /// <summary>ASSUMED shape standing in for a G7-like boat-tail shell curve (R-027 section 1): 0.15 subsonic, peak 0.38 at Mach 1.1, 0.25 at Mach 3.</summary>
        public static DragCurve ShellAssumed() =>
            new DragCurve(new[] { 0.0, 0.8, 0.95, 1.1, 1.5, 2.0, 3.0 }, new[] { 0.15, 0.15, 0.25, 0.38, 0.34, 0.30, 0.25 });

        public double Cd(double mach)
        {
            int n = _mach.Length;
            if (n == 1 || !(mach > _mach[0])) return _cd[0];
            if (mach >= _mach[n - 1]) return _cd[n - 1];
            int i = 1;
            while (mach > _mach[i]) i++;
            double f = (mach - _mach[i - 1]) / (_mach[i] - _mach[i - 1]);
            return _cd[i - 1] + f * (_cd[i] - _cd[i - 1]);
        }
    }

    /// <summary>Air and gravity. Defaults: ISA sea level (R-027).</summary>
    public struct BallisticEnvironment
    {
        public double Gravity;      // m/s^2, positive down
        public double AirDensity;   // kg/m^3 (constant with altitude: Assumed)
        public double SoundSpeed;   // m/s
        public CVec3 Wind;          // m/s
        /// <summary>Terrain height for the built-in ground plane, m. Shells landing on it report a Ground impact. NaN = no plane.</summary>
        public double GroundY;

        public static BallisticEnvironment Standard() => new BallisticEnvironment
        {
            Gravity = 9.80665, AirDensity = 1.225, SoundSpeed = 340.29, Wind = CVec3.Zero, GroundY = 0,
        };
    }

    public static class Ballistics
    {
        /// <summary>Acceleration at velocity <paramref name="v"/>: gravity plus quadratic drag -0.5 rho Cd A |v_rel| v_rel / m.</summary>
        public static CVec3 Acceleration(in CVec3 v, double massKg, double areaM2, DragCurve drag, in BallisticEnvironment env)
        {
            CVec3 vr = v - env.Wind;
            double sp = vr.Length;
            double k = 0;
            if (sp > 0 && drag != null) k = 0.5 * env.AirDensity * drag.Cd(sp / env.SoundSpeed) * areaM2 / massKg * sp;
            return new CVec3(-k * vr.X, -env.Gravity - k * vr.Y, -k * vr.Z);
        }

        /// <summary>One RK4 step of <paramref name="dt"/> seconds. Drag depends on velocity only, so the position update is the velocity RK4 average.</summary>
        public static void Step(ref CVec3 pos, ref CVec3 vel, double massKg, double areaM2, DragCurve drag, in BallisticEnvironment env, double dt)
        {
            CVec3 v1 = vel;                      CVec3 a1 = Acceleration(v1, massKg, areaM2, drag, env);
            CVec3 v2 = vel + a1 * (0.5 * dt);    CVec3 a2 = Acceleration(v2, massKg, areaM2, drag, env);
            CVec3 v3 = vel + a2 * (0.5 * dt);    CVec3 a3 = Acceleration(v3, massKg, areaM2, drag, env);
            CVec3 v4 = vel + a3 * dt;            CVec3 a4 = Acceleration(v4, massKg, areaM2, drag, env);
            pos = pos + (v1 + (v2 + v3) * 2.0 + v4) * (dt / 6.0);
            vel = vel + (a1 + (a2 + a3) * 2.0 + a4) * (dt / 6.0);
        }

        /// <summary>Flies a shell to the ground plane (no targets); returns the landing point (linear interpolation inside the last step) and flight time. False if it never lands within maxSeconds.</summary>
        public static bool FlyToGround(CVec3 pos, CVec3 vel, double massKg, double areaM2, DragCurve drag, in BallisticEnvironment env, double dt, double maxSeconds, out CVec3 landing, out double flightSeconds, out CVec3 landingVelocity)
        {
            double t = 0; landing = pos; landingVelocity = vel; flightSeconds = 0;
            while (t < maxSeconds)
            {
                CVec3 p0 = pos, v0 = vel;
                Step(ref pos, ref vel, massKg, areaM2, drag, env, dt);
                t += dt;
                if (pos.Y <= env.GroundY && p0.Y > env.GroundY)
                {
                    double f = (p0.Y - env.GroundY) / (p0.Y - pos.Y);
                    landing = p0 + (pos - p0) * f; landingVelocity = v0 + (vel - v0) * f; flightSeconds = t - dt + f * dt; return true;
                }
            }
            return false;
        }
    }
}
