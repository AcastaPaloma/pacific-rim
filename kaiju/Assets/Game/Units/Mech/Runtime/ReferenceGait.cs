using System;
using System.Numerics;
using Game.Physics.Body.Contracts;
using Game.Units.Mech.Contracts;

namespace Game.Units.Mech
{
    /// <summary>What the generator needs from mech_frame@v1 (lengths in m, masses in kg). Built once per mech from the frame data.</summary>
    public sealed class GaitGeometry
    {
        public double PelvisYawInertia, UpperYawInertia;   // kg m2 about the vertical: the pelvis; torso, head and both arms about the waist axis (arm masses at their lateral offset), the assembly the waist turns
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
            foreach (var s in f.segments)
            {
                if (s.kind == "pelvis") g.PelvisYawInertia += s.inertia_principal_kgm2[1];
                else if (s.kind == "torso" || s.kind == "head") g.UpperYawInertia += s.inertia_principal_kgm2[1];
                else if (s.kind == "upper_arm" || s.kind == "forearm" || s.kind == "hand") g.UpperYawInertia += s.inertia_principal_kgm2[1] + s.mass_kg * s.com[0] * s.com[0];
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
            _legMax = LegLength((_t.imp > 0 ? Math.Min(_t.knee_min_flex_deg, _t.imp_knee_min_deg) : _t.knee_min_flex_deg) * Math.PI / 180);
            _toeOffExtra = 0; _waist = null;
            Tilt = 0; HipTarget = _t.imp_hip_height_frac * l; _hsPitch = 0; _toPitch = 0; _sched = null;
            if (_t.imp > 0 && Speed > 1e-6) ApplySchedule(fr, l);
            if (Speed > 1e-6 && _t.ref_ankle_heel_rise > 0) { var end = Sample(Duty - 1e-6); _toeOffExtra = Math.Max(0, end.L.FootPitch); }
            // R-007 change 4: the trunk turns against the limbs. Waist yaw rate = -gain x (angular momentum of legs and arms about the vertical) / trunk yaw inertia, zero mean, bounded.
            const int K = 480; var table = new double[K + 1]; double T = 2 * StepTime, mean = 0, max = (_sched != null ? _t.imp_waist_max_deg : _t.ref_waist_max_deg) * Math.PI / 180;
            if (_sched != null && _t.imp_waist_ff > 0 && _g.UpperYawInertia > 0)
            {
                // round 2: the declared trunk share k = imp_waist_ff. The waist turns the upper body (torso, head, arms) so that the vertical angular momentum of the whole body, with the upper body
                // riding on the pelvis, is cut to (1 - k) of itself, over the first eight stride harmonics (R-008 yaw.py): upper inertia x d(waist)/dt = -k x L0, zero mean.
                const int H = 8; var L0 = new double[K]; for (int i = 0; i < K; i++) L0[i] = AngularMomentumY((double)i / K);
                for (int i = 0; i <= K; i++) table[i] = 0;
                for (int h = 1; h <= H; h++)
                {
                    double a = 0, b = 0; for (int i = 0; i < K; i++) { double w = 2 * Math.PI * h * i / K; a += L0[i] * Math.Cos(w); b += L0[i] * Math.Sin(w); }
                    a *= 2.0 / K; b *= 2.0 / K; double om = 2 * Math.PI * h / T;
                    for (int i = 0; i <= K; i++) { double w = 2 * Math.PI * h * i / K; table[i] += -_t.imp_waist_ff / _g.UpperYawInertia * (a * Math.Sin(w) - b * Math.Cos(w)) / om; }
                }
                double peak = 0; for (int i = 0; i <= K; i++) peak = Math.Max(peak, Math.Abs(table[i]));
                if (peak > max) for (int i = 0; i <= K; i++) table[i] *= max / peak;
                _waist = table;
            }
            else if (Speed > 1e-6 && _t.ref_waist_ff > 0 && _g.TrunkYawInertia > 0)
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

        // ---- R-008 section 1 corrected whole-stance schedule (imp = 1) ----
        // One row per Froude number, from research/R-008/model.py (S2 bounds: heel strike <= 25 deg, toe-off <= 40 deg, forward pelvic tilt <= 10 deg, pelvis yaw <= 8 deg;
        // knee >= 5 deg, hip excursion <= 0.05 l, single-support LIPM acceleration <= 0.1 g, foot flat >= 75 % of single support; leg length 42 m, duty 0.63) maximising the whole-stance joint margin.
        // Columns: Froude, foot placement at heel strike as a fraction of the step, hip height (m at l = 42, scaled) and the four Fourier terms of its two-per-stride variation (m),
        // heel-strike pitch (deg, toes up), foot flat from (fraction of stance), heel rise at (fraction of stance), toe-off pitch (deg, heel up), pelvic tilt (deg), pelvis yaw amplitude (deg). Linear in Froude; the end rows hold outside.
        static readonly double[][] Schedule =
        {
            new[] { 0.100, 0.6401, 41.147, 0.7026, -0.2374, -0.4100, 0.2271, 23.71, 0.2776, 0.7240, 10.81, 10.00, 8.0 },
            new[] { 0.125, 0.6145, 41.053, 0.6866, -0.3409, -0.3309, 0.3318, 24.98, 0.2503, 0.6967, 12.87, 10.00, 8.0 },
            new[] { 0.150, 0.5777, 40.983, 0.5941, -0.4104, -0.1699, 0.3948, 25.00, 0.2161, 0.6625, 15.84, 10.00, 8.0 },
            new[] { 0.175, 0.5732, 40.875, 0.6021, -0.4769, -0.1361, 0.4114, 25.00, 0.2101, 0.6565, 17.31, 10.00, 8.0 },
            new[] { 0.200, 0.5614, 41.060, 0.1521, -0.5277, 0.0488, 0.4258, 25.00, 0.2063, 0.6527, 20.37, 10.00, 8.0 },
        };
        double[] _sched;   // [1..12] of the interpolated row, angles in rad
        static double Cos01(double u) => 0.5 * (1 - Math.Cos(Math.PI * Clamp01(u)));
        void ApplySchedule(double fr, double l)
        {
            var row = new double[13]; int n = Schedule.Length;
            int i = 0; while (i < n - 2 && fr > Schedule[i + 1][0]) i++;
            double f = Math.Max(0, Math.Min(1, (fr - Schedule[i][0]) / (Schedule[i + 1][0] - Schedule[i][0])));
            for (int k = 1; k < 13; k++) row[k] = Schedule[i][k] + f * (Schedule[i + 1][k] - Schedule[i][k]);
            double scale = l / 42.0;
            for (int k = 2; k <= 6; k++) row[k] *= scale;
            row[7] *= Math.PI / 180; row[10] *= Math.PI / 180; row[11] *= Math.PI / 180; row[12] *= Math.PI / 180;
            _sched = row; _aStart = _aEnd = 0; FrontReach = row[1] * StepLength; HipTarget = row[2]; Tilt = row[11]; _hsPitch = row[7]; _toPitch = row[10];
            // ankle angles the swing runs between: at toe-off (toe line on the ground, pitch = toe-off pitch) and at heel strike (heel line on the ground)
            double to = row[10], hsp = -row[7], stride = 2 * StepLength; ToePivot(FrontReach - stride * Duty, to, out double z0, out double y0); HeelPivot(FrontReach, hsp, out double z1, out double y1);
            _aStart = to + ShankAngle(SchedHip(Duty), HipZOf(Duty), z0, y0); _aEnd = hsp + ShankAngle(SchedHip(1.0), HipZOf(1.0), z1, y1);
        }
        double _aStart, _aEnd;
        double HipZOf(double psi) => _g.HipHalfWidth * Math.Sin(_sched[12] * Math.Cos(2 * Math.PI * psi));
        /// <summary>Stance foot pitch at a fraction of the stance (0 heel strike .. 1 toe-off): toes up about the heel, flat, heel up about the toe.</summary>
        double SchedPitch(double tau) => tau < _sched[8] ? -_sched[7] * (1 - Cos01(tau / Math.Max(_sched[8], 1e-6))) : tau >= _sched[9] ? _sched[10] * Cos01((tau - _sched[9]) / Math.Max(1 - _sched[9], 1e-6)) : 0;
        double SchedHip(double phase) { double w = 4 * Math.PI * phase; return _sched[2] + _sched[3] * Math.Cos(w) + _sched[4] * Math.Sin(w) + _sched[5] * Math.Cos(2 * w) + _sched[6] * Math.Sin(2 * w); }
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
            if (psi < Duty && _sched != null)
            {
                // corrected reference (R-008): the pitch schedule that keeps every joint inside its range over the whole stance; the ankle follows from the heel or toe line that stays on the ground
                double flatZ = FrontReach - stride * psi;
                pitch = SchedPitch(psi / Duty); support = pitch < -1e-9 ? FootSupport.Heel : pitch > 1e-9 ? FootSupport.Toe : FootSupport.Flat;
                StancePosture.AnklePosition(_g, pitch, out double ax, out double ay); zeta = flatZ + ax; y = ay;
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
            if (_sched != null) { to = _toPitch; hs = _hsPitch; }
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
            if (_sched != null)
            {
                // the swing foot must stay within the leg's reach (knee not locked) at the hip height of the pattern: right after toe-off the hip is still moving forward over a foot that recedes
                double reach = LegLength((_t.imp_knee_min_deg + 0.3) * Math.PI / 180), dz = zeta - hipZ;
                y = Math.Max(y, SchedHip(psi) - Math.Sqrt(Math.Max(reach * reach - dz * dz, 0)));
            }
            pitch = to + (-hs - to) * Smooth(u / uh);
            if (_sched != null)
            {
                // the swing ankle angle runs smoothly from its toe-off value to its heel-strike value (both inside the range, so every value between is), the foot pitch follows from the shank,
                // and the ankle rides as high as the sole clearance needs for that pitch (a trailing shank turns the foot toes-down)
                double hipY = SchedHip(psi), clr = _t.swing_clearance_frac * _g.HipHeight * bump, target = _aStart + (_aEnd - _aStart) * Smooth(u);
                for (int it = 0; it < 10; it++)
                {
                    double pf = target - ShankAngle(hipY, hipZ, zeta, y), c = Math.Cos(pf), sn = Math.Sin(pf);
                    double toeLow = _g.AnkleHeight * c + _g.ToeZ * sn, heelLow = _g.AnkleHeight * c + _g.HeelZ * sn;   // ankle height above the toe and above the heel
                    y = Math.Max(y, Math.Max(toeLow, heelLow) + clr);
                }
                pitch = target - ShankAngle(hipY, hipZ, zeta, y);
            }
        }
        double ShankAngle(double hipY, double hipZ, double zeta, double y)
        {
            double dz = zeta - hipZ, dy = hipY - y, len = Math.Min(Math.Sqrt(dz * dz + dy * dy), _legMax), a = _g.Thigh, c = _g.Shank;
            double knee = Math.PI - Math.Acos(Math.Max(-1, Math.Min(1, (a * a + c * c - len * len) / (2 * a * c))));
            double alpha = Math.Acos(Math.Max(-1, Math.Min(1, (a * a + len * len - c * c) / (2 * a * len))));
            return Math.Atan2(dz, dy) + alpha - knee;
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
            double yawAmp = _sched != null ? _sched[12] : _t.ref_pelvis_yaw_deg * Rollover * Math.PI / 180;
            // the pelvis turns so that the hip of the leg that is about to land is forward: left lands at phase 0 (left hip forward = nose to the right = positive yaw)
            s.PelvisYaw = yawAmp * Math.Cos(2 * Math.PI * phase);
            s.WaistYaw = WaistTable(phase) - (_sched != null ? 0 : s.PelvisYaw * _t.ref_waist_cancels_pelvis);
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
            s.HipHeight = _sched != null ? SchedHip(phase) : SoftMin(height, SoftMin(reachL, reachR));
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
            if (support == FootSupport.Swing && _sched == null)
            {
                // in the air the foot follows the shank as far as needed to keep the ankle inside its range (on the ground the pitch is set by the contact)
                double am = _sched != null ? _t.imp_ankle_margin_deg : _t.ref_ankle_margin_deg, lo = (_g.AnkleRom[0] + am) * Math.PI / 180, hi = (_g.AnkleRom[1] - am) * Math.PI / 180;
                double blend = _sched != null ? Smooth(Math.Min(progress, 1 - progress) / _t.ref_ankle_free_span) : Smooth((Math.Min(progress, 1 - progress) - _t.ref_ankle_free_span) / _t.ref_ankle_free_span);
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
        /// <summary>
        /// The same moment as <see cref="YawMoment"/>, over a whole stride, with the angular momentum limited to the first <paramref name="harmonics"/> stride harmonics (R-008 yaw.py: 8, about 1.2 Hz,
        /// the drive bandwidth of R-005 section 3.2): what a joint drive can follow and what the stance foot must supply. An instantaneous velocity jump of the swing foot at heel strike or toe-off
        /// is an impact that the body's compliance absorbs, not a sustained free moment. Sample i is at phase i / n.
        /// </summary>
        public double[] YawMomentProfile(int n = 240, int harmonics = 8)
        {
            var L = new double[n]; for (int i = 0; i < n; i++) L[i] = AngularMomentumY((double)i / n);
            var M = new double[n]; double T = 2 * StepTime;
            for (int k = 1; k <= harmonics; k++)
            {
                double a = 0, b = 0; for (int i = 0; i < n; i++) { double w = 2 * Math.PI * k * i / n; a += L[i] * Math.Cos(w); b += L[i] * Math.Sin(w); }
                a *= 2.0 / n; b *= 2.0 / n; double om = 2 * Math.PI * k / T;
                for (int i = 0; i < n; i++) { double w = 2 * Math.PI * k * i / n; M[i] += om * (-a * Math.Sin(w) + b * Math.Cos(w)); }
            }
            return M;
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
            if (_sched != null) L += (_g.PelvisYawInertia * (b.PelvisYaw - a.PelvisYaw) + _g.UpperYawInertia * ((b.PelvisYaw + b.WaistYaw) - (a.PelvisYaw + a.WaistYaw))) / (2 * h * T);
            else L += _g.TrunkYawInertia * ((b.PelvisYaw + b.WaistYaw) - (a.PelvisYaw + a.WaistYaw)) / (2 * h * T);
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
