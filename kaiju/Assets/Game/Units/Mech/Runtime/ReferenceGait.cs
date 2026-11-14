using System;
using System.Numerics;
using Game.Physics.Body.Contracts;
using Game.Units.Mech.Contracts;

namespace Game.Units.Mech
{
    /// <summary>What the generator needs from mech_frame@v1 (lengths in m, masses in kg). Built once per mech from the frame data.</summary>
    public sealed class GaitGeometry
    {
        public double Thigh, Shank, HipHeight, HipHalfWidth, AnkleHeight, HeelZ, ToeZ, LegMass, ThighMass, ShankMass, FootMass, ArmMass, ArmLength, ShoulderHalfWidth, TrunkYawInertia, TotalMass;
        public double ThighComFrac, ShankComFrac, ArmComFrac;   // centre of mass along the segment (thigh, shank) or along the whole hanging arm, from the frame's segment data
        public double[] HipFlexRom, KneeRom, AnkleRom;   // degrees, anatomical

        public static GaitGeometry From(MechFrameData f)
        {
            var g = new GaitGeometry();
            double[] hip = null, knee = null, ankle = null, shoulder = null;
            foreach (var j in f.joints)
            {
                if (j.id == "hip_L") { hip = j.position; g.HipFlexRom = Rom(j, "flexion"); }
                else if (j.id == "knee_L") { knee = j.position; g.KneeRom = Rom(j, "flexion"); }
                else if (j.id == "ankle_L") { ankle = j.position; g.AnkleRom = Rom(j, "plantarflexion"); }
                else if (j.id == "shoulder_L") shoulder = j.position;
            }
            if (hip == null || knee == null || ankle == null) throw new InvalidOperationException("the reference gait needs hip_L, knee_L and ankle_L joints in the frame");
            g.Thigh = hip[1] - knee[1]; g.Shank = knee[1] - ankle[1]; g.HipHeight = hip[1]; g.HipHalfWidth = Math.Abs(hip[0]);
            g.AnkleHeight = f.foot_sole.ankle_height_m; g.HeelZ = f.foot_sole.heel_z_m - f.foot_sole.ankle_z_m; g.ToeZ = f.foot_sole.toe_z_m - f.foot_sole.ankle_z_m;
            g.ShoulderHalfWidth = shoulder != null ? Math.Abs(shoulder[0]) : g.HipHalfWidth;
            foreach (var s in f.segments)
            {
                g.TotalMass += s.mass_kg;
                if (s.side == "L")
                {
                    if (s.kind == "thigh") g.ThighMass = s.mass_kg; else if (s.kind == "shank") g.ShankMass = s.mass_kg; else if (s.kind == "foot") g.FootMass = s.mass_kg;
                    else { g.ArmMass += s.mass_kg; if (shoulder != null) g.ArmLength = Math.Max(g.ArmLength, shoulder[1] - s.distal_end[1]); }
                }
                else if (s.side != "R") g.TrunkYawInertia += s.inertia_principal_kgm2[1];
            }
            g.LegMass = g.ThighMass + g.ShankMass + g.FootMass;
            double armMoment = 0;
            foreach (var s in f.segments)
            {
                if (s.side != "L") continue;
                if (s.kind == "thigh") g.ThighComFrac = (hip[1] - s.com[1]) / g.Thigh;
                else if (s.kind == "shank") g.ShankComFrac = (knee[1] - s.com[1]) / g.Shank;
                else if (s.kind != "foot" && shoulder != null) armMoment += s.mass_kg * (shoulder[1] - s.com[1]);
            }
            g.ArmComFrac = g.ArmMass > 0 && g.ArmLength > 0 ? armMoment / (g.ArmMass * g.ArmLength) : 0.5;
            return g;
        }
        static double[] Rom(JointData j, string dof) { foreach (var d in j.dofs) if (d.name == dof) return d.rom_deg; throw new InvalidOperationException(j.id + " has no DOF " + dof); }
    }

    public enum FootSupport { Swing = 0, Heel = 1, Flat = 2, Toe = 3 }

    public struct LegSample
    {
        public Vector3 Ankle;            // stride frame: x right, y up, z forward relative to the point that moves with the walking speed
        public double FootPitch;         // rad, positive = toes down
        public FootSupport Support;
        public double HipFlexion, Knee, AnklePlantar;   // rad, anatomical (ankle: positive = plantarflexion)
        public double SwingProgress;     // 0..1 during swing, else 0
        public bool Reachable;
    }

    public struct GaitSample
    {
        public double Phase, HipHeight, PelvisSway, PelvisYaw, PelvisTilt, WaistYaw, ArmFlexionL, ArmFlexionR, LeadShare;
        public LegSample L, R;
    }

    /// <summary>
    /// R-007 reference gait, engine-free: one stride of a kinematically consistent walking pattern for the pelvis and both legs at a given speed.
    /// Heel strike with the toes up, foot flat, heel rise and toe-off about the toe (rollover), a stance knee that is bent for loading, extends through mid-stance and bends again in pre-swing,
    /// a swing that ends with the foot at rest in the world (retraction) and a bounded descent, pelvic rotation, arm and trunk counter-rotation.
    /// Phase 0 is left heel strike, 0.5 right heel strike. Coordinates are in a frame that moves forward with the walking speed, origin under the hip centre.
    /// Everything is a function of speed, frame geometry and walk_tuning@v1 values (prefix ref_). The controller tracks this pattern; it does not move the body.
    /// </summary>
    public sealed class ReferenceGait
    {
        readonly GaitGeometry _g;
        readonly WalkTuning _t;
        readonly double _gravity;
        public double Speed { get; private set; }
        public double StepLength { get; private set; }
        public double StepTime { get; private set; }
        public double Duty { get; private set; }
        public double Rollover { get; private set; }     // 0 flat-footed .. 1 full heel-to-toe
        public double FrontReach { get; private set; }   // ankle ahead of the hip at heel strike
        public double StepWidth { get; private set; }
        double _legMax;

        public ReferenceGait(GaitGeometry geometry, WalkTuning tuning, double gravity) { _g = geometry; _t = tuning; _gravity = gravity; Configure(0, 0); }

        /// <param name="stepLength">the step the controller is going to take (already limited); 0 = from the speed by the R-001 stride rule</param>
        public void Configure(double speed, double stepLength)
        {
            double l = _g.HipHeight, tStar = Math.Sqrt(l / _gravity);
            Speed = Math.Max(0, speed);
            double fr = Speed * Speed / (_gravity * l);
            double s = stepLength > 0 ? stepLength : 0.5 * _t.stride_k * Math.Pow(Math.Max(fr, 1e-9), _t.stride_exp) * l;
            StepTime = Speed > 1e-6 ? Math.Min(_t.step_time_max_tstar * tStar, s / Speed) : _t.step_time_max_tstar * tStar;
            StepLength = Speed * StepTime;
            Duty = _t.duty_factor;
            Rollover = Clamp01((StepLength / l - _t.ref_rollover_start_frac) / Math.Max(1e-9, _t.ref_rollover_full_frac - _t.ref_rollover_start_frac));
            FrontReach = 0.5 * Duty * 2 * StepLength + _t.ref_front_bias_frac * StepLength;
            StepWidth = _t.step_width_walk_frac * l;
            _legMax = LegLength(_t.knee_min_flex_deg * Math.PI / 180);
            _toeOffExtra = 0; _waist = null;
            Tilt = _t.imp > 0 ? _t.imp_pelvis_tilt_deg * Math.PI / 180 * Rollover : 0;
            HipTarget = _t.imp_hip_height_frac * l;
            _hsPitch = 0; _toPitch = 0;
            if (_t.imp > 0 && Speed > 1e-6) { _hsPitch = -Posture(-FrontReach).FootPitch; _toPitch = Posture(2 * StepLength * Duty - FrontReach).FootPitch; }
            if (Speed > 1e-6 && _t.ref_ankle_heel_rise > 0) { var end = Sample(Duty - 1e-6); _toeOffExtra = Math.Max(0, end.L.FootPitch); }
            // R-007 change 4: the trunk turns against the limbs. Waist yaw rate = -gain x (angular momentum of legs and arms about the vertical) / trunk yaw inertia, zero mean, bounded.
            const int K = 240; var table = new double[K + 1]; double T = 2 * StepTime, mean = 0, max = _t.ref_waist_max_deg * Math.PI / 180;
            if (Speed > 1e-6 && _t.ref_waist_ff > 0 && _g.TrunkYawInertia > 0)
            {
                for (int i = 1; i <= K; i++) table[i] = table[i - 1] - _t.ref_waist_ff * LimbMomentumY((i - 0.5) / K) / _g.TrunkYawInertia * (T / K);
                double drift = table[K] / K;
                for (int i = 0; i <= K; i++) { table[i] -= drift * i; mean += table[i] / (K + 1); }
                double peak = 0; for (int i = 0; i <= K; i++) { table[i] -= mean; peak = Math.Max(peak, Math.Abs(table[i])); }
                if (peak > max) for (int i = 0; i <= K; i++) table[i] *= max / peak;
                _waist = table;
            }
        }
        double[] _waist;
        double _hsPitch, _toPitch;
        /// <summary>Forward pelvic tilt of the pattern, rad (R-008 section 1: about 10 degrees at full rollover; the waist extends by the same angle so the trunk stays upright).</summary>
        public double Tilt { get; private set; }
        /// <summary>Hip height the stance posture is solved for, m (walk tall: the rockers, not a crouch, give the reach).</summary>
        public double HipTarget { get; private set; }
        /// <summary>R-008 corrected stance: the state-indexed posture for a hip <paramref name="x"/> m ahead of the foot's flat-ankle anchor, with this pattern's margins.</summary>
        public StanceSolution Posture(double x, double pitchFloor = 0) => StancePosture.Solve(_g, x, HipTarget, _t.imp_knee_min_deg * Math.PI / 180, (_g.AnkleRom[0] + _t.imp_ankle_margin_deg) * Math.PI / 180, (_g.AnkleRom[1] - _t.imp_ankle_margin_deg) * Math.PI / 180, _t.imp_heel_strike_max_deg * Math.PI / 180, _t.imp_toe_off_max_deg * Math.PI / 180, pitchFloor);
        double WaistTable(double phase)
        {
            if (_waist == null) return 0;
            double x = phase * (_waist.Length - 1); int i = Math.Min(_waist.Length - 2, (int)x); double f = x - i;
            // cubic through the neighbours keeps the rate continuous
            double p0 = _waist[(i - 1 + _waist.Length - 1) % (_waist.Length - 1)], p1 = _waist[i], p2 = _waist[i + 1], p3 = _waist[(i + 2) % (_waist.Length - 1)];
            return p1 + 0.5 * f * (p2 - p0 + f * (2 * p0 - 5 * p1 + 4 * p2 - p3 + f * (3 * (p1 - p2) + p3 - p0)));
        }

        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
        static double Smooth(double u) { u = Clamp01(u); return u * u * u * (10 - 15 * u + 6 * u * u); }
        double LegLength(double knee) => Math.Sqrt(_g.Thigh * _g.Thigh + _g.Shank * _g.Shank + 2 * _g.Thigh * _g.Shank * Math.Cos(knee));

        // ---- foot ----
        void Foot(double psi, double extraHeelRise, double hipZ, out double zeta, out double y, out double pitch, out FootSupport support, out double progress)
        {
            double stride = 2 * StepLength, T = 2 * StepTime, hs = _t.ref_heel_strike_deg * Rollover * Math.PI / 180, to = _t.ref_toe_off_deg * Math.Max(Rollover, _t.ref_toe_off_floor) * Math.PI / 180;
            progress = 0;
            if (psi < Duty && _t.imp > 0)
            {
                // corrected reference (R-008): the foot pitch is the one the stance posture needs for the hip's position over the foot, so the ankle demand holds over the whole stance
                double flatZ = FrontReach - stride * psi;
                var p = Posture(hipZ - flatZ);
                pitch = p.FootPitch; support = p.Support; zeta = flatZ + p.AnkleX; y = p.AnkleY;
                return;
            }
            if (psi < Duty)
            {
                double ps = psi / Duty, flatZ = FrontReach - stride * psi;   // where the ankle is while the foot is flat
                if (ps < _t.ref_foot_flat_at && hs > 0) { pitch = -hs * (1 - Smooth(ps / _t.ref_foot_flat_at)); support = FootSupport.Heel; HeelPivot(flatZ, pitch, out zeta, out y); }
                else if ((ps >= _t.ref_heel_rise_start && to > 0) || extraHeelRise > 1e-9) { pitch = Math.Max(extraHeelRise, ps >= _t.ref_heel_rise_start ? to * Smooth((ps - _t.ref_heel_rise_start) / (1 - _t.ref_heel_rise_start)) : 0); support = FootSupport.Toe; ToePivot(flatZ, pitch, out zeta, out y); }
                else { pitch = 0; support = FootSupport.Flat; zeta = flatZ; y = _g.AnkleHeight; }
                return;
            }
            // swing: from the toe-off pose to the next heel-strike pose. In this moving frame a foot at rest in the world moves backward at the walking speed, so both ends have that velocity (retraction).
            support = FootSupport.Swing;
            double u = (psi - Duty) / (1 - Duty), tSwing = (1 - Duty) * T;
            progress = u;
            to = Math.Max(to, _toeOffExtra);
            if (_t.imp > 0) { to = _toPitch; hs = _hsPitch; }
            ToePivot(FrontReach - stride * Duty, to, out double z0, out double y0);
            HeelPivot(FrontReach, -hs, out double z1, out double y1);
            double v = -Speed * tSwing;   // d(zeta)/du at both ends
            double h00 = 1 - 10 * u * u * u + 15 * u * u * u * u - 6 * u * u * u * u * u, h01 = 1 - h00;
            double h10 = u - 6 * u * u * u + 8 * u * u * u * u - 3 * u * u * u * u * u, h11 = -4 * u * u * u + 7 * u * u * u * u - 3 * u * u * u * u * u;
            zeta = h00 * z0 + h01 * z1 + (h10 + h11) * v;
            // vertical: lift to the clearance (twice-differentiable bump: no jump in acceleration at lift-off), be at a low hover over the landing spot before the final approach,
            // then come down so that the descent never exceeds the touchdown speed
            double tStar = Math.Sqrt(_g.HipHeight / _gravity), vtd = _t.touchdown_speed_tstar * _g.HipHeight / tStar;
            double tFinal = Math.Min(0.4 * tSwing, _t.ref_final_approach_tstar * tStar), uh = 1 - tFinal / tSwing, hover = vtd * tFinal / 1.875, up = _t.ref_swing_peak_at * uh;
            double bump = u < up ? Smooth(u / up) : u < uh ? 1 - Smooth((u - up) / (uh - up)) : 0;
            double baseY = u < uh ? y0 + (y1 + hover - y0) * Smooth(u / uh) : y1 + hover * (1 - Smooth((u - uh) / (1 - uh)));
            y = baseY + _t.swing_clearance_frac * _g.HipHeight * bump;
            pitch = to + (-hs - to) * Smooth(u / uh);
        }
        void HeelPivot(double flatZ, double pitch, out double zeta, out double y)
        {
            double dz = -_g.HeelZ, dy = _g.AnkleHeight, c = Math.Cos(pitch), s = Math.Sin(pitch);
            zeta = flatZ + _g.HeelZ + dz * c + dy * s; y = -dz * s + dy * c;
        }
        void ToePivot(double flatZ, double pitch, out double zeta, out double y)
        {
            double dz = -_g.ToeZ, dy = _g.AnkleHeight, c = Math.Cos(pitch), s = Math.Sin(pitch);
            zeta = flatZ + _g.ToeZ + dz * c + dy * s; y = -dz * s + dy * c;
        }

        double StanceKnee(double psi)
        {
            double load = _t.knee_load_deg, mid = _t.knee_mid_deg + (1 - Rollover) * (_t.ref_knee_mid_flat_deg - _t.knee_mid_deg), pre = mid + Rollover * (_t.ref_preswing_knee_deg - mid);
            double ps = psi / Duty;
            double k = ps < 0.5 ? load + (mid - load) * Smooth(ps / _t.ref_knee_extend_by) : mid + (pre - mid) * Smooth((ps - _t.ref_heel_rise_start) / (1 - _t.ref_heel_rise_start));
            return k * Math.PI / 180;
        }

        double _toeOffExtra;   // heel rise the ankle range forces by the end of stance (found in Configure), carried into the swing start

        /// <summary>The whole-body reference at a stride phase in [0, 1).</summary>
        public GaitSample Sample(double phase)
        {
            // pass 1 with the scheduled foot pitch; pass 2 lets a stance heel rise by what the ankle's dorsiflexion range requires (what the joint stop would do passively)
            var first = Build(phase, 0, 0);
            double lo = (_g.AnkleRom[0] + _t.ref_ankle_margin_deg) * Math.PI / 180, k = _t.ref_ankle_soft_deg * Math.PI / 180;
            double needL = first.L.Support == FootSupport.Swing || first.L.Support == FootSupport.Heel ? 0 : k * Math.Log(1 + Math.Exp((lo - (first.L.AnklePlantar - first.L.FootPitch)) / k));
            double needR = first.R.Support == FootSupport.Swing || first.R.Support == FootSupport.Heel ? 0 : k * Math.Log(1 + Math.Exp((lo - (first.R.AnklePlantar - first.R.FootPitch)) / k));
            return _t.ref_ankle_heel_rise <= 0 || (needL < 1e-6 && needR < 1e-6) ? first : Build(phase, needL, needR);
        }

        GaitSample Build(double phase, double heelRiseL, double heelRiseR)
        {
            phase -= Math.Floor(phase);
            var s = new GaitSample { Phase = phase };
            double yawAmp = _t.ref_pelvis_yaw_deg * Rollover * Math.PI / 180;
            // the pelvis turns so that the hip of the leg that is about to land is forward: left lands at phase 0 (left hip forward = nose to the right = positive yaw)
            s.PelvisYaw = yawAmp * Math.Cos(2 * Math.PI * phase);
            s.WaistYaw = WaistTable(phase) - s.PelvisYaw * _t.ref_waist_cancels_pelvis;
            double arm = _t.ref_arm_amp_deg * Math.PI / 180 * Math.Min(1, StepLength / (_t.ref_rollover_full_frac * _g.HipHeight));
            s.ArmFlexionL = -arm * Math.Cos(2 * Math.PI * phase); s.ArmFlexionR = arm * Math.Cos(2 * Math.PI * phase);   // each arm against its own leg
            double w0 = Math.Sqrt(_gravity / _g.HipHeight), half = 0.5 * StepWidth;
            double swayAmp = half * (1 - 1 / Math.Cosh(0.5 * w0 * StepTime)) * _t.ref_sway_scale;
            s.PelvisSway = -swayAmp * Math.Sin(2 * Math.PI * (phase - 0.25 * (2 * Duty - 1)));   // toward the left foot (-x) during left stance

            double hipZL = -_g.HipHalfWidth * Math.Sin(s.PelvisYaw) * -1, hipZR = -hipZL;   // left hip forward for positive yaw
            s.PelvisTilt = Tilt;
            Foot(phase, heelRiseL, hipZL, out double zl, out double yl, out double pl, out FootSupport sl, out double ul);
            double pr0 = phase - 0.5; pr0 -= Math.Floor(pr0);
            Foot(pr0, heelRiseR, hipZR, out double zr, out double yr, out double prr, out FootSupport sr, out double ur);
            // share of the support taken by the leg that landed last, ramped over double support
            double ds = Duty - 0.5;
            double leadL = sl != FootSupport.Swing ? (sr == FootSupport.Swing ? 1 : Smooth(phase < 0.5 ? phase / ds : 1)) : 0;
            double leadR = sr != FootSupport.Swing ? (sl == FootSupport.Swing ? 1 : Smooth(pr0 < 0.5 ? pr0 / ds : 1)) : 0;
            if (sl != FootSupport.Swing && sr != FootSupport.Swing) { if (phase < ds) leadR = 1 - leadL; else leadL = 1 - leadR; }
            s.LeadShare = phase < 0.5 ? leadL : leadR;
            // hip height: each supporting leg at its knee profile, blended by the shares, never above what a supporting leg can reach
            double height = 0;
            if (sl != FootSupport.Swing) height += leadL * Height(yl, zl - hipZL, LegLength(StanceKnee(phase)));
            if (sr != FootSupport.Swing) height += leadR * Height(yr, zr - hipZR, LegLength(StanceKnee(pr0)));
            // both legs must reach their feet at every phase (the swing leg too: the hips are already low when its heel arrives). Smooth minimum: no kink in the hip height.
            double reachL = Height(yl, zl - hipZL, _legMax) - _t.ref_reach_margin_frac * _g.HipHeight, reachR = Height(yr, zr - hipZR, _legMax) - _t.ref_reach_margin_frac * _g.HipHeight;
            if (_t.imp > 0) height = HipTarget;
            s.HipHeight = SoftMin(height, SoftMin(reachL, reachR));
            s.L = Leg(-1, zl, yl, pl, sl, ul, s, hipZL); s.R = Leg(1, zr, yr, prr, sr, ur, s, hipZR);
            return s;
        }

        double SoftMin(double a, double b) { double k = _t.ref_softmin_frac * _g.HipHeight, m = Math.Min(a, b); return m - k * Math.Log(Math.Exp(-(a - m) / k) + Math.Exp(-(b - m) / k)); }
        static double Height(double ankleY, double dz, double leg) => ankleY + Math.Sqrt(Math.Max(leg * leg - dz * dz, 0.25 * leg * leg));

        LegSample Leg(int side, double zeta, double y, double pitch, FootSupport support, double progress, in GaitSample s, double hipZ)
        {
            var leg = new LegSample { Ankle = new Vector3((float)(side * 0.5 * StepWidth), (float)y, (float)zeta), FootPitch = pitch, Support = support, SwingProgress = progress };
            // sagittal two-link solution (the lateral offset between hip and foot is taken by hip abduction and does not change the sagittal angles to first order)
            double dz = zeta - hipZ, dy = s.HipHeight - y, len = Math.Sqrt(dz * dz + dy * dy);
            leg.Reachable = len <= _legMax + 1e-6;
            len = Math.Min(len, _legMax);
            double a = _g.Thigh, c = _g.Shank;
            double knee = Math.PI - Math.Acos(Math.Max(-1, Math.Min(1, (a * a + c * c - len * len) / (2 * a * c))));
            double alpha = Math.Acos(Math.Max(-1, Math.Min(1, (a * a + len * len - c * c) / (2 * a * len))));
            double lineAngle = Math.Atan2(dz, dy);                 // hip-to-ankle line from the downward vertical, positive forward
            leg.Knee = knee; leg.HipFlexion = lineAngle + alpha + s.PelvisTilt;   // thigh forward of the line by alpha; the joint also takes up the pelvic tilt
            double shank = lineAngle + alpha - knee;                  // shank angle from the vertical, positive = foot end forward
            leg.AnklePlantar = pitch + shank;                      // foot pitch relative to the shank
            if (support == FootSupport.Swing)
            {
                // in the air the foot follows the shank as far as needed to keep the ankle inside its range (on the ground the pitch is set by the contact)
                double lo = (_g.AnkleRom[0] + _t.ref_ankle_margin_deg) * Math.PI / 180, hi = (_g.AnkleRom[1] - _t.ref_ankle_margin_deg) * Math.PI / 180;
                double blend = Smooth((Math.Min(progress, 1 - progress) - _t.ref_ankle_free_span) / _t.ref_ankle_free_span);
                double k = _t.ref_ankle_soft_deg * Math.PI / 180, x = leg.AnklePlantar;
                double clamped = x + k * Math.Log(1 + Math.Exp((lo - x) / k)) - k * Math.Log(1 + Math.Exp((x - hi) / k));   // smooth clamp: no kink where the limit takes over
                leg.AnklePlantar += (clamped - leg.AnklePlantar) * blend; leg.FootPitch = leg.AnklePlantar - shank;
            }
            return leg;
        }

        /// <summary>Vertical moment (N m) the reference asks of the ground about the stance foot: the rate of change of the legs', arms' and trunk's angular momentum about the vertical through the hip centre.</summary>
        public double YawMoment(double phase)
        {
            double h = 1e-3;
            return (AngularMomentumY(phase + h) - AngularMomentumY(phase - h)) / (2 * h * 2 * StepTime);
        }
        double AngularMomentumY(double phase)
        {
            double h = 1e-3, T = 2 * StepTime;
            var a = Sample(phase - h); var b = Sample(phase + h); var m = Sample(phase);
            double L = 0;
            // legs: thigh, shank and foot as point masses on the hip-knee-ankle chain; z velocity in the moving frame; lever arm = lateral offset
            L += LegMomentum(m.L, a.L, b.L, -1, h * T) + LegMomentum(m.R, a.R, b.R, 1, h * T);
            // arms: a pendulum from the shoulder, centre of mass at 0.45 of its length
            double r = _g.ArmComFrac * _g.ArmLength;
            L += -_g.ShoulderHalfWidth * _g.ArmMass * r * Math.Cos(m.ArmFlexionL) * (b.ArmFlexionL - a.ArmFlexionL) / (2 * h * T) * -1;
            L += _g.ShoulderHalfWidth * _g.ArmMass * r * Math.Cos(m.ArmFlexionR) * (b.ArmFlexionR - a.ArmFlexionR) / (2 * h * T) * -1;
            // trunk: yaw of the pelvis plus the waist
            L += _g.TrunkYawInertia * ((b.PelvisYaw + b.WaistYaw) - (a.PelvisYaw + a.WaistYaw)) / (2 * h * T);
            return L;
        }
        double LimbMomentumY(double phase)
        {
            double h = 1e-3, T = 2 * StepTime, r = _g.ArmComFrac * _g.ArmLength;
            var a = Sample(phase - h); var b = Sample(phase + h); var m = Sample(phase);
            double L = LegMomentum(m.L, a.L, b.L, -1, h * T) + LegMomentum(m.R, a.R, b.R, 1, h * T);
            L += _g.ShoulderHalfWidth * _g.ArmMass * r * (Math.Cos(m.ArmFlexionL) * (b.ArmFlexionL - a.ArmFlexionL) - Math.Cos(m.ArmFlexionR) * (b.ArmFlexionR - a.ArmFlexionR)) / (2 * h * T);
            return L + _g.TrunkYawInertia * (1 - _t.ref_waist_cancels_pelvis) * (b.PelvisYaw - a.PelvisYaw) / (2 * h * T);
        }
        double LegMomentum(in LegSample m, in LegSample a, in LegSample b, int side, double dt)
        {
            // angular momentum about +y of a mass at lateral offset x moving with forward velocity vz is  -x m vz  (y x: r x v = (x, ., z) x (0, ., vz) -> y component = z*0 - x*vz)
            double x = side * _g.HipHalfWidth;
            Func<LegSample, double> kneeZ = q => _g.Thigh * Math.Sin(q.HipFlexion);
            double vAnkle = (b.Ankle.Z - a.Ankle.Z) / (2 * dt), vKnee = (kneeZ(b) - kneeZ(a)) / (2 * dt);
            double vThigh = _g.ThighComFrac * vKnee, vShank = vKnee + _g.ShankComFrac * (vAnkle - vKnee), vFoot = vAnkle;
            return -x * (_g.ThighMass * vThigh + _g.ShankMass * vShank + _g.FootMass * vFoot);
        }
    }
}
