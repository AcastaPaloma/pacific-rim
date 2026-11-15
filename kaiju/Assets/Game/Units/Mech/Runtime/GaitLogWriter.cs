using System;
using System.IO;
using System.Numerics;
using System.Text;
using Game.Control.Contracts;
using Game.Physics.Body.Contracts;
using Game.Units.Mech.Contracts;

namespace Game.Units.Mech
{
    /// <summary>
    /// Writes <c>gait_log@v1</c> (research/R-001 section 8): one header, one step record per physics step with no gaps, events right after the step they belong to.
    /// The intent object shows the intent as received and the command as limited, with the limiting causes (C14).
    /// </summary>
    public sealed class GaitLogWriter : IDisposable
    {
        readonly TextWriter _w;
        readonly IArticulatedBody _b;
        readonly double _dt;
        readonly JsonLine _j = new JsonLine();
        readonly StringBuilder _sb = new StringBuilder(1024);
        float _prevYaw; bool _hasYaw; double _yawRate;
        MoveId _lastMove = (MoveId)(-1); LimitCause _lastCauses;

        public GaitLogWriter(TextWriter w, IArticulatedBody body, double dt) { _w = w; _b = body; _dt = dt; }

        public void WriteHeader(string capture, string commit, MechLimits limits, double cruiseFroude, string cameraRig, string notes)
        {
            var f = _b.Frame; var sole = f.foot_sole;
            double hx = 0.45 * sole.width_m, hz = 0.45 * sole.length_m;
            _sb.Clear().Append('[');
            for (int i = 0; i < _b.FootCount; i++)
            {
                if (i > 0) _sb.Append(',');
                _sb.Append("{\"id\":\"").Append(_b.FootId(i)).Append("\",\"length_m\":"); JsonLine.Num(_sb, sole.length_m); _sb.Append(",\"width_m\":"); JsonLine.Num(_sb, sole.width_m);
                _sb.Append(",\"sole_points_local\":[[0,0,0],[").Append(F(hx)).Append(",0,").Append(F(hz)).Append("],[").Append(F(-hx)).Append(",0,").Append(F(hz)).Append("],[").Append(F(hx)).Append(",0,").Append(F(-hz)).Append("],[").Append(F(-hx)).Append(",0,").Append(F(-hz)).Append("]]}");
            }
            _sb.Append(']');
            string feet = _sb.ToString();
            _sb.Clear().Append('[');
            for (int i = 0; i < _b.FootCount; i++) { if (i > 0) _sb.Append(','); _sb.Append('"').Append(_b.FootId(i)).Append('"'); }
            _sb.Append(']');
            double mg = _b.TotalMassKg * _b.Tuning.gravity_mps2;
            double iyaw = 0;
            for (int i = 0; i < f.segments.Length; i++) { var s = f.segments[i]; iyaw += s.inertia_principal_kgm2[1] + s.mass_kg * ((s.com[0] - f.com_rest[0]) * (s.com[0] - f.com_rest[0]) + (s.com[2] - f.com_rest[2]) * (s.com[2] - f.com_rest[2])); }
            double vCruise = Math.Sqrt(cruiseFroude * limits.G * limits.LegLength);
            _j.Begin().Str("rec", "header").Str("schema", MechContractVersions.GaitLog).Str("capture", capture).Str("commit", commit).Str("unit_id", _b.UnitId.ToString()).Str("unit_kind", "mech")
              .Num("H_m", f.total_height_m).Num("leg_length_m", limits.LegLength).Num("com_height_stand_m", f.com_rest[1]).Num("mass_kg", _b.TotalMassKg).Num("I_yaw_kgm2", iyaw)
              .Raw("foot_ids", _sb.ToString()).Raw("feet", feet).Num("fixed_dt_s", _dt).Num("physics_hz", 1.0 / _dt).Num("render_hz_target", 60).Num("g_mps2", _b.Tuning.gravity_mps2)
              .Num("mu_assumed", _b.Tuning.friction_dynamic).Num("reactor_power_W", _b.Tuning.reactor_power_w).Num("T_stride_nom_s", cruiseFroude > 0 ? 2 * limits.StepTime(vCruise) : 0).Num("fr_cruise_nom", cruiseFroude)
              .Raw("thresholds", "{\"F_on_N\":" + F(_b.Tuning.contact_on_bw * mg) + ",\"F_off_N\":" + F(_b.Tuning.contact_off_bw * mg) + "}").Str("camera_rig", cameraRig).Str("notes", notes);
            _w.WriteLine(_j.End());
        }

        static string F(double v) => JsonLine.Text(v);
        static string Arr(double[] a) => "[" + F(a[0]) + "," + F(a[1]) + "," + F(a[2]) + "]";
        static string D(double v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

        public void WriteStep(long n, in IntentFrame intent, in LocoCommand cmd, in LocomotionController.Debug dbg)
        {
            int pelvis = 0;
            for (int i = 0; i < _b.LinkCount; i++) if (_b.LinkParent(i) < 0) pelvis = i;
            _b.GetLink(pelvis, out var root);
            Vector3 fwd = Vector3.Transform(Vector3.UnitZ, root.Rotation);
            float yaw = (float)Math.Atan2(fwd.X, fwd.Z);
            if (_hasYaw) _yawRate = IntentLimiter.Wrap(yaw - _prevYaw) / _dt;
            _prevYaw = yaw; _hasYaw = true;
            // positions in float64: the centre of mass and the sole centres are computed here in double precision from the engine's link states (origin offset is zero at M0)
            double mx = 0, my = 0, mz = 0, mass = 0;
            for (int i = 0; i < _b.LinkCount; i++) { _b.GetLink(i, out var ls); double m = _b.LinkMassKg(i); mass += m; mx += m * (double)ls.Com.X; my += m * (double)ls.Com.Y; mz += m * (double)ls.Com.Z; }
            var sole = _b.Frame.foot_sole; double sy = -sole.ankle_height_m, sz = (sole.heel_z_m + sole.toe_z_m) * 0.5 - sole.ankle_z_m;
            _sb.Clear().Append('[');
            for (int i = 0; i < _b.FootCount; i++)
            {
                _b.GetFoot(i, out var fs); _b.GetLink(fs.Link, out var fl);
                double qx = fl.Rotation.X, qy = fl.Rotation.Y, qz = fl.Rotation.Z, qw = fl.Rotation.W;
                // rotate (0, sy, sz) by the foot rotation: v' = v + 2 w (q x v) + 2 q x (q x v)
                double cx = qy * sz - qz * sy, cy = -qx * sz, cz = qx * sy;
                double px = fl.Position.X + 2 * (qw * cx + qy * cz - qz * cy), py = fl.Position.Y + sy + 2 * (qw * cy + qz * cx - qx * cz), pz = fl.Position.Z + sz + 2 * (qw * cz + qx * cy - qy * cx);
                if (i > 0) _sb.Append(',');
                _sb.Append("{\"p\":[").Append(D(px)).Append(',').Append(D(py)).Append(',').Append(D(pz)).Append(']').Append(",\"q\":[").Append(F(fs.Rotation.X)).Append(',').Append(F(fs.Rotation.Y)).Append(',').Append(F(fs.Rotation.Z)).Append(',').Append(F(fs.Rotation.W))
                   .Append("],\"v\":").Append(JsonLine.VecOf(fs.Velocity)).Append(",\"contact\":").Append(fs.Contact ? '1' : '0').Append(",\"fn\":").Append(F(fs.NormalForceN))
                   .Append(",\"ft\":").Append(JsonLine.VecOf(fs.TangentialForceN)).Append(",\"cn\":").Append(fs.NormalForceN > 0 ? JsonLine.VecOf(fs.Normal) : "null").Append(",\"cop\":").Append(JsonLine.VecOf(fs.Cop)).Append('}');
            }
            _sb.Append(']');
            string feet = _sb.ToString();
            string intentJson = "{\"move\":\"" + intent.move.ToString().ToLowerInvariant() + "\",\"target\":" + JsonLine.VecOf(intent.target) + ",\"effort\":" + F(intent.effort) + ",\"cmd_speed_mps\":" + F(cmd.Speed) + ",\"cmd_yaw_rate_rps\":" + F(cmd.YawRate)
                + ",\"cmd_heading_rad\":" + F(cmd.Heading) + ",\"requested_speed_mps\":" + F(cmd.RequestedSpeed) + ",\"limit_causes\":\"" + cmd.Causes + "\",\"pilots\":" + intent.pilots + "}";
            double power = 0;
            for (int d = 0; d < _b.DofCount; d++) { double p = _b.DofAppliedTorque(d) * _b.DofVelocity(d); if (p > 0) power += p; }
            _j.Begin().Str("rec", "step").Int("n", n).Num("t", n * _dt).Raw("com_p", "[" + D(mx / mass) + "," + D(my / mass) + "," + D(mz / mass) + "]").Vec("com_v", _b.ComVelocity).Vec("root_p", root.Position).Quat("root_q", root.Rotation)
              .Num("yaw", yaw).Num("yaw_rate", _yawRate).Raw("feet", feet).Num("ext_f", _b.ExternalForceMagnitudeN).Raw("intent", intentJson).Num("p_loco_W", power)
              .Raw("ctl", "{\"phase\":\"" + dbg.Phase + "\",\"stance\":" + dbg.Stance + ",\"dcm\":[" + F(dbg.Dcm.X) + "," + F(dbg.Dcm.Y) + "],\"cop_cmd\":[" + F(dbg.CopCommand.X) + "," + F(dbg.CopCommand.Y) + "],\"swing_target\":[" + F(dbg.SwingTarget.X) + "," + F(dbg.SwingTarget.Y) + "],\"ref\":[" + F(dbg.Reference.X) + "," + F(dbg.Reference.Y) + "],\"com_h_target\":" + F(dbg.ComHeightTarget) + ",\"swing_clamped\":" + (dbg.SwingClamped ? "true" : "false") + "}")
              // optional (round 2 step 3): vertical free moment the stance foot(s) must supply, the friction budget, their ratio and the trunk's share of the legs' yaw momentum; null when not available
              .Num("yaw_moment_nm", dbg.YawMomentNm).Num("yaw_budget_nm", dbg.YawBudgetNm).Num("yaw_util", dbg.YawUtil).Num("trunk_share", dbg.TrunkShare)
              // optional (round 2 step 6a, imp > 0): reference and measured hip flexion, knee, ankle plantar angle (deg) of the stance and swing leg, and the Froude of the reference in use
              .Raw("angles", dbg.AngStRef == null ? "null" : "{\"fr_ref\":" + F(dbg.RefFroude) + ",\"st_ref\":" + Arr(dbg.AngStRef) + ",\"st_meas\":" + Arr(dbg.AngStMeas) + ",\"sw_ref\":" + Arr(dbg.AngSwRef) + ",\"sw_meas\":" + Arr(dbg.AngSwMeas) + "}")
              // optional (round 3 step 8.5 c2, imp = 2): optimised feed-forward torque (N m, hip knee ankle) of the stance and swing leg, and the reference stance phase; null when not available
              .Raw("tau_ff", dbg.TauFfSt == null ? "null" : "{\"st\":" + Arr(dbg.TauFfSt) + ",\"sw\":" + Arr(dbg.TauFfSw) + "}").Raw("ref_phase", dbg.TauFfSt == null ? "null" : F(dbg.RefPhase));
            _w.WriteLine(_j.End());
            if (intent.move != _lastMove || cmd.Causes != _lastCauses)
            {
                _lastMove = intent.move; _lastCauses = cmd.Causes;
                Event(n, "intent_change", "\"move\":\"" + intent.move.ToString().ToLowerInvariant() + "\",\"cmd_speed_mps\":" + F(cmd.Speed) + ",\"cmd_yaw_rate_rps\":" + F(cmd.YawRate) + ",\"requested_speed_mps\":" + F(cmd.RequestedSpeed) + ",\"limit_causes\":\"" + cmd.Causes + "\"");
            }
        }

        public void ContactBegin(in FootContact e) => Event(e.Step, "contact_begin", "\"foot\":\"" + _b.FootId(e.Foot) + "\",\"contact_id\":" + e.ContactId + ",\"fn\":" + F(e.NormalForceN) + ",\"foot_v\":" + JsonLine.VecOf(e.ApproachVelocity) + ",\"cop\":" + JsonLine.VecOf(e.Position) + ",\"jn\":" + F(e.VerticalImpulseNs) + ",\"pressure_pa\":" + F(e.PressurePa));
        public void ContactEnd(in FootLiftOff e) => Event(e.Step, "contact_end", "\"foot\":\"" + _b.FootId(e.Foot) + "\",\"contact_id\":" + e.ContactId);
        public void CameraImpulse(long n, int foot, long contactId, double amplitudeM, long sourceN) => Event(n, "cam_impulse", "\"foot\":\"" + _b.FootId(foot) + "\",\"contact_id\":" + contactId + ",\"amplitude_m\":" + F(amplitudeM) + ",\"dir\":[0,-1,0],\"source_n\":" + sourceN);

        public void Event(long n, string type, string body) => _w.WriteLine("{\"rec\":\"event\",\"n\":" + n + ",\"t\":" + (n * _dt).ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ",\"type\":\"" + type + "\"" + (string.IsNullOrEmpty(body) ? "" : "," + body) + "}");
        public void Dispose() => _w.Dispose();
    }
}
