using System;
using System.IO;
using System.Numerics;
using System.Text;
using Game.Physics.Body.Contracts;

namespace Game.Physics.Body
{
    /// <summary>Whole-body mechanical state computed from link states (engine-free; M-006 energy and momentum checks).</summary>
    public struct BodyEnergetics
    {
        public double KeLin, KeRot, Pe, Total;
        public Vector3 Momentum, AngularMomentumCom;
        public double MaxLinkSpeed;
        public bool NaN;

        public static BodyEnergetics Compute(IArticulatedBody b)
        {
            var e = new BodyEnergetics();
            double g = b.Tuning.gravity_mps2;
            Vector3 com = b.Com, vcom = b.ComVelocity;
            double px = 0, py = 0, pz = 0, lx = 0, ly = 0, lz = 0;
            for (int i = 0; i < b.LinkCount; i++)
            {
                b.GetLink(i, out var s);
                double m = b.LinkMassKg(i);
                if (m <= 0) continue;
                var I = b.Frame.segments[i].inertia_principal_kgm2;
                double v2 = s.Velocity.LengthSquared();
                e.KeLin += 0.5 * m * v2;
                Vector3 wl = JointMath.Rotate(JointMath.Inverse(s.Rotation), s.AngularVelocity);
                e.KeRot += 0.5 * (I[0] * wl.X * wl.X + I[1] * wl.Y * wl.Y + I[2] * wl.Z * wl.Z);
                e.Pe += m * g * s.Com.Y;
                px += m * s.Velocity.X; py += m * s.Velocity.Y; pz += m * s.Velocity.Z;
                Vector3 r = s.Com - com, dv = s.Velocity - vcom;
                Vector3 c = Vector3.Cross(r, dv);
                Vector3 lw = JointMath.Rotate(s.Rotation, new Vector3((float)(I[0] * wl.X / 1e6), (float)(I[1] * wl.Y / 1e6), (float)(I[2] * wl.Z / 1e6)));
                lx += m * c.X + lw.X * 1e6; ly += m * c.Y + lw.Y * 1e6; lz += m * c.Z + lw.Z * 1e6;
                double sp = Math.Sqrt(v2);
                if (sp > e.MaxLinkSpeed) e.MaxLinkSpeed = sp;
                if (float.IsNaN(s.Com.X + s.Com.Y + s.Com.Z + s.Velocity.X + s.Velocity.Y + s.Velocity.Z + s.Rotation.W)) e.NaN = true;
            }
            e.Momentum = new Vector3((float)(px / 1e6), (float)(py / 1e6), (float)(pz / 1e6)) * 1e6f;
            e.AngularMomentumCom = new Vector3((float)(lx / 1e6), (float)(ly / 1e6), (float)(lz / 1e6)) * 1e6f;
            e.Total = e.KeLin + e.KeRot + e.Pe;
            return e;
        }
    }

    /// <summary>Writes <c>physics_log@v1</c> (research/R-005 section 8.9): one header, one record per physics step, event records. Joins gait_log@v1 on run_id/step/t.</summary>
    public sealed class PhysicsLogWriter : IDisposable
    {
        readonly TextWriter _w;
        readonly IArticulatedBody _b;
        readonly string _runId;
        readonly double _dt;
        readonly JsonLine _j = new JsonLine();
        readonly StringBuilder _sb = new StringBuilder(4096);
        double _lastExternalWork;

        public PhysicsLogWriter(TextWriter w, IArticulatedBody body, string runId, double dt) { _w = w; _b = body; _runId = runId; _dt = dt; }

        /// <param name="settingsJson">Engine read-back as a JSON object body (solver type, iterations, per-link damping, drives): supplied by the adapter that owns the engine.</param>
        public void WriteHeader(string scene, string commit, string engineVersion, string settingsJson)
        {
            _j.Begin().Str("schema", BodyContractVersions.PhysicsLog).Str("run_id", _runId).Str("scene", scene).Str("commit", commit).Str("unity_version", engineVersion)
              .Num("fixed_dt", _dt).Vec("gravity", 0, -_b.Tuning.gravity_mps2, 0).Str("frame_id", _b.Frame.id).Str("frame_schema", _b.Frame.schema)
              .Num("total_mass_kg", _b.TotalMassKg).Num("sim_mass_unit_kg", _b.Tuning.sim_mass_unit_kg).Vec("origin_offset", 0, 0, 0).Str("unit_id", _b.UnitId.ToString());
            if (!string.IsNullOrEmpty(settingsJson)) _j.Raw("engine", settingsJson);
            _w.WriteLine(_j.End());
        }

        public BodyEnergetics WriteStep(long step)
        {
            var e = BodyEnergetics.Compute(_b);
            double maxJoint = 0, pPos = 0, pAbs = 0;
            _sb.Clear().Append('{');
            string lastJoint = null;
            for (int d = 0; d < _b.DofCount; d++)
            {
                var info = _b.GetDof(d);
                double q = _b.DofPosition(d), qd = _b.DofVelocity(d), td = _b.DofDriveTorque(d), tt = _b.DofAppliedTorque(d), p = tt * qd;
                if (Math.Abs(qd) > maxJoint) maxJoint = Math.Abs(qd);
                if (p > 0) pPos += p;
                pAbs += Math.Abs(p);
                if (info.JointId != lastJoint) { if (lastJoint != null) _sb.Append("},"); _sb.Append('"').Append(info.JointId).Append("\":{"); lastJoint = info.JointId; } else _sb.Append(',');
                _sb.Append('"').Append(info.Name).Append("\":[");
                JsonLine.Num(_sb, Math.Round(q * 180 / Math.PI, 4)); _sb.Append(','); JsonLine.Num(_sb, Math.Round(qd, 5)); _sb.Append(','); JsonLine.Num(_sb, Math.Round(td, 0)); _sb.Append(','); JsonLine.Num(_sb, Math.Round(tt, 0)); _sb.Append(',');
                JsonLine.Num(_sb, Math.Round(p, 0)); _sb.Append(','); _sb.Append(Math.Abs(tt) >= 0.999 * info.ForceLimitNm ? '1' : '0'); _sb.Append(']');
            }
            if (lastJoint != null) _sb.Append('}');
            _sb.Append('}');
            string joints = _sb.ToString();
            _sb.Clear().Append('[');
            for (int d = 0, n = 0; d < _b.DofCount; d++) if (_b.DofSpeedClamped(d)) { var ci = _b.GetDof(d); if (n++ > 0) _sb.Append(','); _sb.Append('"').Append(ci.JointId).Append('.').Append(ci.Name).Append('"'); }
            string clamped = _sb.Append(']').ToString();
            _sb.Clear().Append('{');
            for (int f = 0; f < _b.FootCount; f++)
            {
                _b.GetFoot(f, out var fs);
                if (f > 0) _sb.Append(',');
                double ft = fs.TangentialForceN.Length();
                _sb.Append('"').Append(_b.FootId(f)).Append("\":{\"contact\":").Append(fs.Contact ? '1' : '0').Append(",\"Jn\":"); JsonLine.Num(_sb, fs.NormalForceN * _dt);
                _sb.Append(",\"Jt\":["); JsonLine.Num(_sb, fs.TangentialForceN.X * _dt); _sb.Append(','); JsonLine.Num(_sb, fs.TangentialForceN.Z * _dt);
                _sb.Append("],\"cop\":").Append(JsonLine.VecOf(fs.Cop)).Append(",\"n_points\":").Append(fs.Points).Append(",\"mu_util\":"); JsonLine.Num(_sb, fs.NormalForceN > 1 ? ft / fs.NormalForceN : 0);
                _sb.Append(",\"pressure_kpa\":"); JsonLine.Num(_sb, fs.NormalForceN / _b.SoleAreaM2 / 1000.0); _sb.Append('}');
            }
            _sb.Append('}');
            double ew = _b.ExternalWorkJ - _lastExternalWork; _lastExternalWork = _b.ExternalWorkJ;
            _j.Begin().Str("run_id", _runId).Int("step", step).Num("t", step * _dt).Vec("com", _b.Com).Vec("com_vel", _b.ComVelocity)
              .Num("ke_lin", e.KeLin).Num("ke_rot", e.KeRot).Num("pe", e.Pe).Num("e_total", e.Total).Vec("p", e.Momentum).Vec("L_com", e.AngularMomentumCom)
              .Num("max_link_speed", e.MaxLinkSpeed).Num("max_joint_speed", maxJoint).Int("clamps", _b.JointSpeedClampCount).Raw("clamped", clamped).Bool("nan", e.NaN)
              .Raw("joint_fields", "[\"q_deg\",\"qd\",\"tau_drive\",\"tau_total\",\"power\",\"saturated\"]").Raw("joints", joints).Raw("feet", _sb.ToString())
              .Num("power_total_pos", pPos).Num("power_total_abs", pAbs).Num("external_work", ew).Num("ext_f", _b.ExternalForceMagnitudeN).Int("non_foot_contacts", _b.NonFootContacts);
            _w.WriteLine(_j.End());
            return e;
        }

        public void WriteEvent(long step, string type, string extraJsonBody)
        {
            _j.Begin().Str("run_id", _runId).Str("event", type).Int("step", step).Num("t", step * _dt);
            string s = _j.End();
            _w.WriteLine(string.IsNullOrEmpty(extraJsonBody) ? s : s.Substring(0, s.Length - 1) + "," + extraJsonBody + "}");
        }

        public void Dispose() => _w.Dispose();
    }
}
