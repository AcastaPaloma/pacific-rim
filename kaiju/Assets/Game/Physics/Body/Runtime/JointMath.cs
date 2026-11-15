using System;
using System.Numerics;

namespace Game.Physics.Body
{
    /// <summary>Rotation-vector helpers. Measured in evidence/T-005/probes: a PhysX spherical joint's reduced coordinates are the rotation vector of the child relative to the parent.</summary>
    public static class JointMath
    {
        public static Vector3 RotVec(Quaternion q)
        {
            if (q.W < 0) q = new Quaternion(-q.X, -q.Y, -q.Z, -q.W);
            var v = new Vector3(q.X, q.Y, q.Z);
            float s = v.Length();
            if (s < 1e-9f) return v * 2f;
            float angle = 2f * (float)Math.Atan2(s, q.W);
            return v * (angle / s);
        }
        public static Quaternion FromRotVec(Vector3 r)
        {
            float a = r.Length();
            if (a < 1e-9f) return Quaternion.Identity;
            return Quaternion.CreateFromAxisAngle(r / a, a);
        }
        public static Vector3 Rotate(Quaternion q, Vector3 v) => Vector3.Transform(v, q);
        public static Quaternion Inverse(Quaternion q) => Quaternion.Conjugate(q);
        /// <summary>Heading of a rotation: atan2(fwd.x, fwd.z) of its forward (+z) axis.</summary>
        public static float Yaw(Quaternion q) { var f = Rotate(q, Vector3.UnitZ); return (float)Math.Atan2(f.X, f.Z); }
        public static Quaternion YawRotation(float yaw) => Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
        public static float WrapAngle(float a) { while (a > Math.PI) a -= 2f * (float)Math.PI; while (a <= -Math.PI) a += 2f * (float)Math.PI; return a; }
    }
}
