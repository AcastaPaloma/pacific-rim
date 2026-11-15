using System;
using System.Numerics;
using Game.Physics.Body.Contracts;
using Game.Units.Mech.Contracts;

namespace Game.Units.Mech
{
    /// <summary>
    /// T-005 assisted walk (walk_mode = 1): a virtual hand of god on the pelvis. Pure physics stays the default (walk_mode = 0, no force is ever written).
    /// Three engine external forces per step (they act for the next physics step): vertical support (fraction of body weight), a horizontal CoM velocity servo toward the commanded velocity,
    /// and an upright / heading torque on the pelvis written as a pure couple (two opposite forces, zero net force). Every magnitude is capped and logged.
    /// </summary>
    public sealed class WalkAssist
    {
        readonly IArticulatedBody _b; readonly WalkTuning _t; readonly double _dt, _g, _l, _mass, _w; readonly int _pelvis;
        double _fade;                       // 0..1 ramp
        public Vector3 Force;               // N, net force on the body this step (support + servo)
        public Vector3 Torque;              // N m, couple on the pelvis
        public double SupportN, ServoN, TiltNm, YawNm;
        public bool On => _t.walk_mode > 0.5;
        public double Fade => _fade;
        public double PeakForceW, PeakTorqueWl;

        public WalkAssist(IArticulatedBody body, WalkTuning t, double dt, double gravity, double legLength, int pelvis)
        {
            _b = body; _t = t; _dt = dt; _g = gravity; _l = legLength; _pelvis = pelvis; _mass = body.TotalMassKg; _w = _mass * gravity;
        }

        /// <param name="walking">the walk is on (not Stand); the assist fades in / out over as_ramp_s</param>
        public void Update(bool walking, in LocoCommand cmd, in LinkState pelvis, Vector3 com, Vector3 comVel, float groundY, float comRest)
        {
            Force = Vector3.Zero; Torque = Vector3.Zero; SupportN = ServoN = TiltNm = YawNm = 0;
            bool active = walking || _t.as_stand > 0.5;
            double step = _dt / Math.Max(_t.as_ramp_s, 1e-3);
            _fade = Math.Max(0, Math.Min(1, _fade + (active ? step : -step)));
            if (_fade <= 0) return;
            double fade = _fade, tstar = Math.Sqrt(_l / _g);
            // vertical support: constant fraction of W plus an optional height spring on the CoM (as_z_om rad/s, capped at as_z_cap W)
            double fz = _t.as_up * _w;
            if (_t.as_z_om > 0)
            {
                double h = com.Y - groundY, e = comRest - h;
                double z = _mass * (_t.as_z_om * _t.as_z_om * e - 2 * _t.as_z_om * comVel.Y);
                fz += Math.Max(-_t.as_z_cap * _w, Math.Min(_t.as_z_cap * _w, z));
            }
            // horizontal velocity servo
            double fx = 0, fzh = 0;
            if (_t.as_v_cap > 0)
            {
                double vx = cmd.Speed * Math.Sin(cmd.Heading), vz = cmd.Speed * Math.Cos(cmd.Heading);
                double ax = (vx - comVel.X) / Math.Max(_t.as_v_tau, 1e-3), az = (vz - comVel.Z) / Math.Max(_t.as_v_tau, 1e-3);
                fx = _mass * ax; fzh = _mass * az; double mag = Math.Sqrt(fx * fx + fzh * fzh), cap = _t.as_v_cap * _w;
                if (mag > cap) { fx *= cap / mag; fzh *= cap / mag; }
            }
            Force = new Vector3((float)(fx * fade), (float)(fz * fade), (float)(fzh * fade));
            SupportN = fz * fade; ServoN = Math.Sqrt(fx * fx + fzh * fzh) * fade;
            if (Force.LengthSquared() > 0) _b.AddExternalForce(_pelvis, Force, pelvis.Com);
            // upright torque: tilt axis = up x worldY (torque rotating the pelvis up vector back to vertical), damping on the horizontal angular velocity
            Vector3 T = Vector3.Zero; double wl = _w * _l;
            if (_t.as_tilt_k > 0 || _t.as_tilt_d > 0)
            {
                Vector3 up = Vector3.Transform(Vector3.UnitY, pelvis.Rotation), axis = Vector3.Cross(up, Vector3.UnitY);
                double sin = axis.Length(), ang = Math.Atan2(sin, Math.Max(-1f, Math.Min(1f, up.Y))); Vector3 ax2 = sin > 1e-9 ? axis / (float)sin : Vector3.Zero;
                Vector3 om = pelvis.AngularVelocity; om.Y = 0;
                T = ax2 * (float)(_t.as_tilt_k * wl * ang) - om * (float)(_t.as_tilt_d * wl * tstar);
                double m = T.Length(), cap = _t.as_tilt_cap * wl; if (cap > 0 && m > cap) T *= (float)(cap / m);
                TiltNm = T.Length() * fade;
            }
            // yaw torque toward the commanded heading (only while walking forward or turning; the pelvis yaw is its forward vector's heading)
            if (_t.as_yaw_k > 0 || _t.as_yaw_d > 0)
            {
                Vector3 fwd = Vector3.Transform(Vector3.UnitZ, pelvis.Rotation); double yaw = Math.Atan2(fwd.X, fwd.Z);
                double e = IntentLimiter.Wrap(cmd.Heading - yaw), wy = pelvis.AngularVelocity.Y - cmd.YawRate;
                double ty = _t.as_yaw_k * wl * e - _t.as_yaw_d * wl * tstar * wy, cap = _t.as_yaw_cap * wl; if (cap > 0) ty = Math.Max(-cap, Math.Min(cap, ty));
                T += new Vector3(0, (float)ty, 0); YawNm = Math.Abs(ty) * fade;
            }
            T *= (float)fade;
            if (T.LengthSquared() > 1e-6f)
            {
                // pure couple: forces +-f u at +-R a, a perpendicular to That, u = That x a, so r x F = R f That for each of the two (2 R f = |T|)
                double tm = T.Length(); Vector3 th = T / (float)tm, a0 = Math.Abs(th.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
                Vector3 a = Vector3.Normalize(a0 - th * Vector3.Dot(a0, th)), u = Vector3.Cross(th, a); double R = Math.Max(0.5, _t.as_lever_m), f = tm / (2 * R);
                Vector3 p = pelvis.Com;
                _b.AddExternalForce(_pelvis, u * (float)f, p + a * (float)R);
                _b.AddExternalForce(_pelvis, -u * (float)f, p - a * (float)R);
                Torque = T;
            }
            PeakForceW = Math.Max(PeakForceW, Math.Sqrt(Force.X * Force.X + Force.Y * Force.Y + Force.Z * Force.Z) / _w);
            PeakTorqueWl = Math.Max(PeakTorqueWl, Torque.Length() / wl);
        }

        /// <summary>walk_mode 2: PD servo of the pelvis toward a planned pose (position of the link origin, attitude, velocities), plus kin_support x W of vertical support. Forces and couple are capped (kin_f_cap, kin_fz_cap in W; kin_rot_cap in W l) and fade in over as_ramp_s.</summary>
        public void Servo(in LinkState pelvis, Vector3 pT, Quaternion qT, Vector3 vT, Vector3 wT)
        {
            Force = Vector3.Zero; Torque = Vector3.Zero; SupportN = ServoN = TiltNm = YawNm = 0;
            _fade = Math.Min(1, _fade + _dt / Math.Max(_t.as_ramp_s, 1e-3));
            double fade = _fade, om = _t.kin_om, ze = _t.kin_zeta, tstar = Math.Sqrt(_l / _g), wl = _w * _l;
            Vector3 e = pT - pelvis.Position, ev = vT - pelvis.Velocity;
            Vector3 F = (float)_mass * ((float)(om * om) * e + (float)(2 * ze * om) * ev);
            double hm = Math.Sqrt(F.X * F.X + F.Z * F.Z), hcap = _t.kin_f_cap * _w;
            if (hm > hcap) { F.X *= (float)(hcap / hm); F.Z *= (float)(hcap / hm); }
            double vcap = _t.kin_fz_cap * _w; F.Y = (float)Math.Max(-vcap, Math.Min(vcap, F.Y));
            ServoN = Math.Sqrt(F.X * F.X + F.Y * F.Y + F.Z * F.Z);
            SupportN = _t.kin_support * _w; F.Y += (float)SupportN;
            Force = F * (float)fade; SupportN *= fade; ServoN *= fade;
            _b.AddExternalForce(_pelvis, Force, pelvis.Com);
            Quaternion dq = qT * Quaternion.Conjugate(pelvis.Rotation); if (dq.W < 0) dq = new Quaternion(-dq.X, -dq.Y, -dq.Z, -dq.W);
            var qv = new Vector3(dq.X, dq.Y, dq.Z); double s = qv.Length(), ang = 2 * Math.Atan2(s, dq.W); Vector3 r = s > 1e-9 ? qv * (float)(ang / s) : qv * 2f;
            Vector3 T = (float)(_t.kin_rot_k * wl) * r + (float)(_t.kin_rot_d * wl * tstar) * (wT - pelvis.AngularVelocity);
            double tm = T.Length(), cap = _t.kin_rot_cap * wl; if (cap > 0 && tm > cap) T *= (float)(cap / tm);
            T *= (float)fade; TiltNm = new Vector2(T.X, T.Z).Length(); YawNm = Math.Abs(T.Y);
            ApplyCouple(T, pelvis.Com);
            PeakForceW = Math.Max(PeakForceW, Force.Length() / _w); PeakTorqueWl = Math.Max(PeakTorqueWl, Torque.Length() / wl);
        }

        void ApplyCouple(Vector3 T, Vector3 point)
        {
            if (T.LengthSquared() < 1e-6f) return;
            double tm = T.Length(); Vector3 th = T / (float)tm, a0 = Math.Abs(th.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
            Vector3 a = Vector3.Normalize(a0 - th * Vector3.Dot(a0, th)), u = Vector3.Cross(th, a); double R = Math.Max(0.5, _t.as_lever_m), f = tm / (2 * R);
            _b.AddExternalForce(_pelvis, u * (float)f, point + a * (float)R);
            _b.AddExternalForce(_pelvis, -u * (float)f, point - a * (float)R);
            Torque = T;
        }
    }
}
