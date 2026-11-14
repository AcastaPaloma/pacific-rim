using System;
using System.Numerics;

namespace Game.Units.Mech
{
    /// <summary>
    /// Linear inverted pendulum and divergent component of motion (capture point) mathematics, R-005 section 5.2 steps 2 and 4.
    /// Horizontal plane only: Vector2 = (world x, world z). xi = x + v / w0, d(xi)/dt = w0 (xi - p).
    /// </summary>
    public static class Lipm
    {
        public static double Omega(double g, double comHeight) => Math.Sqrt(g / Math.Max(comHeight, 1e-3));
        public static Vector2 Dcm(Vector2 x, Vector2 v, double w) => x + v / (float)w;

        /// <summary>Steady-gait DCM offset ahead of the stance CoP at the start of a step of effective length s (step minus CoP travel): b = s / (e^{wT} - 1).</summary>
        public static double OffsetForward(double effectiveStep, double w, double T) => effectiveStep / (Math.Exp(w * T) - 1.0);
        /// <summary>Steady-gait lateral DCM offset toward the swing side for CoPs alternating by <paramref name="width"/>: b = width / (1 + e^{wT}).</summary>
        public static double OffsetLateral(double width, double w, double T) => width / (1.0 + Math.Exp(w * T));

        /// <summary>DCM after <paramref name="tr"/> seconds when the CoP moves as p(tau) = p + pdot tau.</summary>
        public static Vector2 DcmAtEnd(Vector2 xi, Vector2 p, Vector2 pdot, double w, double tr)
        {
            float e = (float)Math.Exp(w * tr);
            Vector2 lead = pdot / (float)w;
            return p + pdot * (float)tr + lead + (xi - p - lead) * e;
        }

        /// <summary>The constant CoP offset from the nominal (moving) CoP that brings the DCM to <paramref name="xiEndDesired"/> after <paramref name="tr"/> seconds.</summary>
        public static Vector2 DeadbeatOffset(Vector2 xi, Vector2 pNominalNow, Vector2 pdot, Vector2 xiEndDesired, double w, double tr)
        {
            float e = (float)Math.Exp(w * tr);
            Vector2 lead = pdot / (float)w;
            Vector2 freeEnd = pNominalNow + pdot * (float)tr + lead + (xi - pNominalNow - lead) * e;
            return (xiEndDesired - freeEnd) / (1f - e);
        }

        public static double MinJerk(double t) { t = t < 0 ? 0 : t > 1 ? 1 : t; return t * t * t * (10 - 15 * t + 6 * t * t); }
        public static double SmoothStep(double t) { t = t < 0 ? 0 : t > 1 ? 1 : t; return t * t * (3 - 2 * t); }

        /// <summary>Alexander &amp; Jayes: stride length = k Fr^exp l. Returns the step time for a walking speed, capped for slow speeds.</summary>
        public static double StepTime(double speed, double legLength, double g, double k, double exp, double maxStepTime)
        {
            if (speed < 1e-3) return maxStepTime;
            double fr = speed * speed / (g * legLength);
            double stride = k * Math.Pow(fr, exp) * legLength;
            return Math.Min(maxStepTime, 0.5 * stride / speed);
        }
    }

    /// <summary>Analytic two-link leg inverse kinematics (R-005 section 5.2 step 5). All rotations are world rotations of links whose rest axes equal the world axes.</summary>
    public static class LegIk
    {
        public struct Solution { public Quaternion HipRelative, AnkleRelative, Thigh, Shank; public double Knee; public bool Clamped; }

        /// <param name="kneeMin">smallest knee flexion allowed, rad (keeps the leg off its singular straight pose)</param>
        public static Solution Solve(Vector3 hip, Quaternion pelvis, Vector3 ankle, Quaternion foot, double thighLength, double shankLength, double kneeMin, double kneeMax)
        {
            var s = new Solution();
            Vector3 d = ankle - hip;
            double len = d.Length();
            double maxLen = Math.Sqrt(thighLength * thighLength + shankLength * shankLength + 2 * thighLength * shankLength * Math.Cos(kneeMin));
            double minLen = Math.Sqrt(thighLength * thighLength + shankLength * shankLength + 2 * thighLength * shankLength * Math.Cos(kneeMax));
            if (len < 1e-6) { d = new Vector3(0, -1, 0); len = 1; }
            Vector3 dir = d / (float)len;
            if (len > maxLen) { len = maxLen; s.Clamped = true; } else if (len < minLen) { len = minLen; s.Clamped = true; }
            double cosInterior = (thighLength * thighLength + shankLength * shankLength - len * len) / (2 * thighLength * shankLength);
            s.Knee = Math.PI - Math.Acos(Math.Max(-1, Math.Min(1, cosInterior)));
            double alpha = Math.Acos(Math.Max(-1, Math.Min(1, (thighLength * thighLength + len * len - shankLength * shankLength) / (2 * thighLength * len))));
            Vector3 x = Vector3.Transform(Vector3.UnitX, foot);
            x -= dir * Vector3.Dot(x, dir);
            if (x.LengthSquared() < 1e-8f) x = Vector3.UnitX;
            x = Vector3.Normalize(x);
            Vector3 thighDir = Vector3.Transform(dir, Quaternion.CreateFromAxisAngle(x, -(float)alpha)); // knee forward
            Vector3 y = -thighDir, z = Vector3.Cross(x, y);
            s.Thigh = Quaternion.CreateFromRotationMatrix(new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1));
            s.Shank = s.Thigh * Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)s.Knee);
            s.HipRelative = Quaternion.Conjugate(pelvis) * s.Thigh;
            s.AnkleRelative = Quaternion.Conjugate(s.Shank) * foot;
            return s;
        }
    }
}
