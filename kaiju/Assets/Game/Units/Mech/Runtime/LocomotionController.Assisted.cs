using System;
using System.Numerics;
using Game.Control.Contracts;
using Game.Physics.Body.Contracts;
using Game.Units.Mech.Contracts;

namespace Game.Units.Mech
{
    /// <summary>
    /// T-005 walk_mode 2, assisted gait (default off; walk_mode 0 runs none of this). A procedural step generator plans where the feet go (planted feet stay fixed in the world, the swing foot flies a min-jerk path,
    /// the base point moves with the limited command), the leg joints are position drives toward the IK of those foot targets, and a capped hand-of-god servo (WalkAssist.Servo, engine external forces)
    /// holds the pelvis on the planned base path. Intent still only enters as the limited command (speed, heading, yaw rate), so walk, turn and stop answer the pilot.
    /// </summary>
    public sealed partial class LocomotionController
    {
        bool _kInit;
        Vector2 _kB;                                  // base point: ground projection of the pelvis plan
        readonly Vector2[] _kFoot = new Vector2[2];   // planted / current sole-centre target of each foot (xz)
        readonly float[] _kFootYaw = new float[2];
        readonly double[] _kFootH = new double[2], _kFootPitch = new double[2];   // sole height above ground, pitch (+ toe down)
        int _kSwing = -1, _kNext, _kLastSw = -1;
        double _kT, _kTstep, _kTsw, _kDs;
        Vector2 _kSwStart, _kSwTarget; float _kSwYaw0, _kSwYaw1;
        double _kHipY, _kLat;
        Vector3 _kPPrev; bool _kHavePPrev;
        readonly double[] _kDbg = new double[16];
        readonly double[] _kHipCmd = new double[2];

        public Func<float, float, float> GroundHeight;   // terrain height under a world xz (a ray cast in the Unity assembly); null = flat ground at y = 0

        float KGround(Vector2 xz) => GroundHeight != null ? GroundHeight(xz.X, xz.Y) : 0f;

        void KinInit()
        {
            _kInit = true;
            Vector2 mid = Vector2.Zero;
            for (int i = 0; i < 2; i++)
            {
                _kFoot[i] = _legs[i].Sole2; _kFootYaw[i] = _legs[i].Yaw; mid += _kFoot[i] * 0.5f;
            }
            _b.GetLink(_pelvis, out var pel0); _kB = new Vector2(pel0.Position.X, pel0.Position.Z);
            _b.GetLink(_legs[0].Thigh, out var th0); _b.GetLink(_legs[1].Thigh, out var th1);
            _kHipY = 0.5 * (th0.Position.Y + th1.Position.Y);
            for (int i = 0; i < 2; i++) _kAnkleW[i] = new Vector3(_kFoot[i].X, _ankleHeight, _kFoot[i].Y) - new Vector3(Fwd(_kFootYaw[i]).X, 0, Fwd(_kFootYaw[i]).Y) * _ankleToSoleCentre;
            var conj = Quaternion.Conjugate(_pelvisState.Rotation);
            _kHipLocal[0] = Vector3.Transform(th0.Position - _pelvisState.Position, conj); _kHipLocal[1] = Vector3.Transform(th1.Position - _pelvisState.Position, conj);
            _kRng = (uint)(2654435761u * (uint)(_t.kin_seed + 1) + 12345u);
        }

        readonly Vector3[] _kHipLocal = new Vector3[2], _kAnkleW = new Vector3[2];
        readonly double[] _kExtra = new double[2];
        Vector2 _kJit; bool _kHitNow; Vector3 _kSwA0, _kSwAnkle; double _kSwP0, _kSwPitch; readonly double[] _kPitchNow = new double[2];
        uint _kRng;
        /// <summary>Deterministic jitter in [-1, 1] (LCG), for kin_noise robustness runs.</summary>
        double KRand() { _kRng = _kRng * 1664525u + 1013904223u; return ((_kRng >> 8) & 0xFFFF) / 32767.5 - 1.0; }

        double KMaxLen(Leg leg)
        {
            double a = leg.ThighLength, b = leg.ShankLength, km = _t.knee_min_flex_deg * Math.PI / 180;
            return Math.Sqrt(a * a + b * b + 2 * a * b * Math.Cos(km));
        }

        Vector2 KNominal(int leg, Vector2 b, float yaw) => b + Fwd(yaw) * _ankleToSoleCentre + Right(yaw) * (float)(_legs[leg].Side * 0.5 * _stanceWidth * _t.kin_width);   // sole centre target: the ankle under the hip, the sole centre ahead of it

        double KFootErr(int leg, float psi) => Vector2.Distance(_kFoot[leg], KNominal(leg, _kB, psi)) + 0.5 * _l * Math.Abs(Wrap(_kFootYaw[leg] - psi));

        void KinUpdate(long step)
        {
            if (!_kInit) KinInit();
            double dt = _dt, l = _l, v = _cmd.Speed, wy = _cmd.YawRate; float psi = _cmd.Heading;
            Vector2 dir = Fwd(psi), right = Right(psi);
            _kB += dir * (float)(v * dt);
            // hit reaction: a shove that moves the pelvis farther than kin_hit_m from its plan frees the plan (the base follows the pelvis, the servo only pulls at its caps) and the next foot lands on the capture point of the measured motion
            Vector2 pelXz = XZ(_pelvisState.Position), planXz = _kB + right * (float)_kLat, errXz = pelXz - planXz; double errLen = errXz.Length();
            bool hit = _t.kin_hit_m > 0 && errLen > _t.kin_hit_m;
            if (hit) _kB += errXz * (float)(1 - _t.kin_hit_m / errLen);
            _kHitNow = hit;
            bool want = v > 0.03 * _lim.VCruise || Math.Abs(wy) > 1e-3 || hit;
            double ds = Math.Max(0, Math.Min(0.6, _t.kin_ds));

            // ---- step machine ----
            if (_kSwing < 0)
            {
                _kDs -= dt;
                int sw = -1;
                if (_kDs <= 0)
                {
                    if (want)
                    {
                        double p0 = Vector2.Dot(_kFoot[0] - _kB, dir), p1 = Vector2.Dot(_kFoot[1] - _kB, dir);
                        sw = Math.Abs(p0 - p1) > 0.5 ? (p0 < p1 ? 0 : 1) : _kNext;
                        if (hit) { Vector2 vr = XZ(_pelvisState.Velocity); double q0 = Vector2.Dot(_kFoot[0] - pelXz, vr), q1 = Vector2.Dot(_kFoot[1] - pelXz, vr); sw = q0 < q1 ? 0 : 1; }   // the foot behind the motion steps
                    }
                    else
                    {
                        double e0 = KFootErr(0, psi), e1 = KFootErr(1, psi);
                        if (Math.Max(e0, e1) > _t.kin_tol_m) sw = e0 >= e1 ? 0 : 1;
                    }
                }
                if (sw >= 0)
                {
                    _kSwing = sw; _kNext = 1 - sw; _kT = 0;
                    _kTstep = _t.kin_tscale * _lim.StepTime(Math.Max(v, _t.reach_speed_floor_frac * _lim.VCruise)); if (_t.kin_noise > 0) _kTstep *= 1 + _t.kin_noise * KRand();
                    _kTsw = _kTstep * (1 - ds); _kJit = _t.kin_noise > 0 ? new Vector2((float)(_t.kin_noise * l * 0.1 * KRand()), (float)(_t.kin_noise * l * 0.1 * KRand())) : Vector2.Zero;
                    _kSwStart = _kFoot[sw]; _kSwYaw0 = _kFootYaw[sw]; _kSwTarget = _kSwStart; _kSwYaw1 = _kSwYaw0; _kSwA0 = _kAnkleW[sw]; _kSwP0 = _kPitchNow[sw];
                }
            }
            if (_kSwing >= 0)
            {
                int sw = _kSwing;
                _kT += dt; double u = _kT / _kTsw;
                if (u < 0.6)
                {
                    double tRem = Math.Max(0, _kTsw * 0.92 - _kT);
                    Vector2 btd = _kB + dir * (float)(v * tRem); float psiTd = (float)(psi + wy * tRem);
                    double tStance = (1 + ds) * _kTstep, f = Math.Min(0.5 * v * tStance, _t.kin_fmax_frac * l);
                    Vector2 bTarget = btd + Fwd(psiTd) * (float)f;
                    if (_kHitNow) bTarget = pelXz + (XZ(_pelvisState.Velocity) - dir * (float)v) * (float)(1.0 / Lipm.Omega(_g, Math.Max(1f, _com.Y - _groundY)));   // capture point of the measured motion relative to the commanded one
                    _kSwTarget = KNominal(sw, bTarget, psiTd) + _kJit; _kSwYaw1 = psiTd;
                }
                double hd = _t.kin_hdelay, s = Lipm.MinJerk((u - hd) / (0.92 - hd));
                _kFoot[sw] = _kSwStart + (_kSwTarget - _kSwStart) * (float)s;
                _kFootYaw[sw] = _kSwYaw0 + Wrap(_kSwYaw1 - _kSwYaw0) * (float)s;
                // swing height: quick rise (peak at u = kin_peak), slow min-jerk descent so the foot lands with almost no vertical speed
                double up = _t.kin_peak, hn = u < up ? Math.Pow(Math.Sin(0.5 * Math.PI * u / up), 2) : 1 - Lipm.MinJerk((u - up) / (1 - up)), sn = Math.Sqrt(Math.Max(0, hn));
                _kFootH[sw] = _t.kin_clear * l * hn; _kFootPitch[sw] = -_t.kin_toe_deg * Math.PI / 180 * sn;
                // swing ankle path: continues from the lift-off ankle (heel-risen foot pivoted on its toe) to the flat-foot landing ankle, clearance on top; the pitch eases from the lift-off pitch to toe up
                Vector2 fe = Fwd(_kSwYaw1); float gnd = KGround(_kSwTarget);
                Vector3 aEnd = new Vector3(_kSwTarget.X - fe.X * _ankleToSoleCentre, gnd + _ankleHeight, _kSwTarget.Y - fe.Y * _ankleToSoleCentre);
                double ys = Lipm.SmoothStep(u / 0.5), m = Lipm.SmoothStep(u / 0.35);
                _kSwAnkle = new Vector3(_kSwA0.X + (aEnd.X - _kSwA0.X) * (float)s, (float)(_kSwA0.Y + (aEnd.Y - _kSwA0.Y) * ys + _t.kin_clear * l * hn), _kSwA0.Z + (aEnd.Z - _kSwA0.Z) * (float)s);
                _kSwPitch = _kSwP0 * (1 - m) + _kFootPitch[sw] * m;
                {
                    // the lowest sole end (toe or heel, pitch about the ankle) stays kin_lift_m above the ground until the last 20 % of the swing
                    double sp = Math.Sin(_kSwPitch), cp = Math.Cos(_kSwPitch), fade = 1 - Lipm.SmoothStep((u - 0.8) / 0.2);
                    double need = gnd + _t.kin_lift_m * fade + _ankleHeight * cp + Math.Max(_toeOff * sp, _heelOff * sp);
                    if (_kSwAnkle.Y < need) _kSwAnkle.Y = (float)need;
                }
                if (u >= 1)
                {
                    _kFoot[sw] = _kSwTarget; _kFootYaw[sw] = _kSwYaw1; _kFootH[sw] = -_t.kin_preload_m; _kFootPitch[sw] = 0;
                    _kLastSw = sw; _kSwing = -1; _kDs = _kTstep * ds;
                }
            }
            for (int i = 0; i < 2; i++) if (i != _kSwing) { _kFootH[i] = -_t.kin_preload_m; _kFootPitch[i] = 0; }

            // ---- pelvis plan ----
            double p = _kSwing >= 0 ? _kT / _kTstep : (_kLastSw >= 0 && _kTstep > 0 ? 1 - Math.Max(0, _kDs) / _kTstep : 0);
            double latT = 0;
            int swNow = _kSwing >= 0 ? _kSwing : _kLastSw;
            if (swNow >= 0 && (want || _kSwing >= 0 || _kDs > 0)) latT = -_legs[swNow].Side * _t.kin_sway_m * (1 - 2 * Lipm.SmoothStep((p - _t.kin_sway_from) / (1 - _t.kin_sway_from)));
            _kLat += (latT - _kLat) * (1 - Math.Exp(-_t.kin_hz * 4 * dt));
            Vector2 hipXz = _kB + right * (float)_kLat;
            // hip height: the planted (and flying) feet bound it by the leg reach (their ankle targets of the last tick, heel rise included)
            double yMin = double.MaxValue, rr = _t.kin_reach * KMaxLen(_legs[0]);
            for (int i = 0; i < 2; i++)
            {
                Vector2 hip = hipXz + right * (float)(_legs[i].Side * 0.5 * _stanceWidth);
                Vector3 aw = _kAnkleW[i];
                if (i == _kSwing) { Vector2 fe = Fwd(_kSwYaw1); aw = new Vector3(_kSwTarget.X - fe.X * _ankleToSoleCentre, KGround(_kSwTarget) + _ankleHeight, _kSwTarget.Y - fe.Y * _ankleToSoleCentre); }   // the flying foot only bounds the height by its landing spot
                double d = Vector2.Distance(new Vector2(aw.X, aw.Z), hip), dy = Math.Sqrt(Math.Max(1.0, rr * rr - d * d));
                yMin = Math.Min(yMin, aw.Y + dy);
            }
            _kHipY += (yMin - _kHipY) * (1 - Math.Exp(-_t.kin_hz * dt));
            double lean = _t.kin_lean * Math.Min(1.5, v / Math.Max(1e-6, _lim.VCruise));
            Quaternion qT = JointMath2.YawRotation(psi) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)lean);
            Vector3 pT = new Vector3(hipXz.X, (float)(_kHipY - (_l - _pelvisRest)), hipXz.Y);
            Vector3 vT = _kHavePPrev ? (pT - _kPPrev) / (float)dt : Vector3.Zero; _kPPrev = pT; _kHavePPrev = true;
            _assist.Servo(_pelvisState, pT, qT, vT, new Vector3(0, (float)wy, 0));
            _dbg.AssistOn = _assist.Fade > 0; _dbg.AssistF = _assist.Force; _dbg.AssistT = _assist.Torque; _dbg.AssistFade = _assist.Fade;

            // ---- joint drives: IK of the foot targets from the measured pelvis ----
            double kmin = _t.knee_min_flex_deg * Math.PI / 180, kmax = _t.knee_max_flex_deg * Math.PI / 180;
            for (int i = 0; i < 2; i++)
            {
                var leg = _legs[i];
                _b.GetLink(leg.Thigh, out var thigh);
                Vector3 hipPos = thigh.Position; Quaternion pelRot = _pelvisState.Rotation;
                if (_t.kin_ik_plan > 1.5) { hipPos = pT + Vector3.Transform(_kHipLocal[i], qT); pelRot = qT; }   // fully planned pelvis: an attitude error of the pelvis moves the feet by leg length x error
                else if (_t.kin_ik_plan > 0.5) { if (_t.kin_att_w > 0) pelRot = Quaternion.Slerp(pelRot, qT, (float)_t.kin_att_w); hipPos = pT + Vector3.Transform(_kHipLocal[i], pelRot); }   // kin_att_w: share of the planned attitude in the leg IK (the legs then also hold the pelvis attitude, the feet move by leg length x w x error)          // planned position (the legs are stiff to the plan), measured attitude (the feet stay planted)
                // foot pitch: the base pitch of the plan (toe up in the swing) plus the extra pitch that keeps the ankle inside its range (heel rise at push-off pivots about the toe, toe up about the heel)
                var ainfo = _b.GetDof(leg.Ankle[0]); double lo = ainfo.LowerRad + _t.kin_ankle_margin_deg * Math.PI / 180, hi = ainfo.UpperRad - _t.kin_ankle_margin_deg * Math.PI / 180, extra = 0;
                Quaternion footRot = default; Vector3 ankle = default; LegIk.Solution ik = default; Vector3 hip = default, ank = default;
                double pitchNow = 0;
                for (int it = 0; it < 4; it++)
                {
                    double pitch = _kFootPitch[i] + extra; if (i == _kSwing) pitch = _kSwPitch; float yw = _kFootYaw[i];
                    footRot = JointMath2.YawRotation(yw) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)pitch);
                    Vector3 sole = new Vector3(_kFoot[i].X, KGround(_kFoot[i]) + (float)_kFootH[i], _kFoot[i].Y), f3 = new Vector3(Fwd(yw).X, 0, Fwd(yw).Y);
                    Vector3 a0 = sole + new Vector3(0, _ankleHeight, 0) - f3 * _ankleToSoleCentre;   // ankle of the flat foot
                    ankle = a0; pitchNow = pitch;
                    if (i == _kSwing) ankle = _kSwAnkle;
                    else if (Math.Abs(extra) > 1e-9)
                    {
                        double piv = extra > 0 ? _toeOff : _heelOff;   // pivot point z relative to the ankle (toe for heel rise)
                        Vector3 pivot = a0 - new Vector3(0, _ankleHeight, 0) + f3 * (float)piv;
                        Vector3 rel = Vector3.Transform(new Vector3(0, _ankleHeight, (float)-piv), Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)extra));
                        ankle = pivot + Vector3.Transform(rel, JointMath2.YawRotation(yw));
                    }
                    ik = LegIk.Solve(hipPos, pelRot, ankle, footRot, leg.ThighLength, leg.ShankLength, kmin, kmax);
                    hip = JointMath2.RotVec(ik.HipRelative); ank = JointMath2.RotVec(ik.AnkleRelative);
                    double an = Component(ank, 0) * ainfo.Sign * 1.0;
                    if (i == _kSwing) break;
                    if (an < lo) extra += (lo - an) * ainfo.Sign; else if (an > hi) extra -= (an - hi) * ainfo.Sign; else break;
                    extra = Math.Max(-0.5, Math.Min(0.5, extra));
                }
                _kAnkleW[i] = ankle; _kExtra[i] = extra; _kPitchNow[i] = pitchNow;
                for (int a = 0; a < 3; a++)
                {
                    if (leg.Hip[a] >= 0) KinDrive(leg.Hip[a], Component(hip, a) * _b.GetDof(leg.Hip[a]).Sign);
                    if (leg.Ankle[a] >= 0) KinDrive(leg.Ankle[a], Component(ank, a) * _b.GetDof(leg.Ankle[a]).Sign);
                }
                KinDrive(leg.Knee, ik.Knee * _b.GetDof(leg.Knee).Sign);
                _kHipCmd[i] = Component(hip, 0) * _b.GetDof(leg.Hip[0]).Sign;
            }
            for (int d = 0; d < _b.DofCount; d++)
            {
                if (_isLegDof[d]) continue;
                double tgt = 0;
                for (int arm = 0; arm < 2; arm++) if (d == _armShoulderFlex[arm]) { int leg = (_legs[0].Side < 0) == (arm == 0) ? 0 : 1; tgt = -_t.kin_arm * _kHipCmd[leg]; }
                KinDrive(d, tgt);
            }

            // ---- phase and diagnostics ----
            _phase = _kSwing >= 0 ? LocoPhase.SingleSupport : want || _kDs > 0 ? LocoPhase.DoubleSupport : LocoPhase.Stand;
            _stance = _kSwing >= 0 ? 1 - _kSwing : (_kLastSw >= 0 ? _kLastSw : 0);
            _time = _kSwing >= 0 ? _kT : 0; _stepT = _kTstep;
            var dbg = _kDbg; dbg[0] = _kSwing; dbg[1] = _kFoot[0].X; dbg[2] = _kFoot[0].Y; dbg[3] = _kFoot[1].X; dbg[4] = _kFoot[1].Y; dbg[5] = _kHipY; dbg[6] = _kB.X; dbg[7] = _kB.Y; dbg[8] = psi; dbg[9] = _kTstep;
            dbg[10] = pT.X - _pelvisState.Position.X; dbg[11] = pT.Y - _pelvisState.Position.Y; dbg[12] = pT.Z - _pelvisState.Position.Z; dbg[13] = _kExtra[0]; dbg[14] = _kExtra[1]; dbg[15] = _kLat;
            _dbg.Kin = _kDbg;
        }

        void KinDrive(int dof, double target)
        {
            var info = _b.GetDof(dof);
            target = Math.Max(info.LowerRad, Math.Min(info.UpperRad, target));
            double vel = _hasPrev[dof] ? (target - _prevTarget[dof]) / _dt : 0;
            vel = Math.Max(-_t.target_velocity_max_rps, Math.Min(_t.target_velocity_max_rps, vel));
            _prevTarget[dof] = target; _hasPrev[dof] = true;
            _b.GetDofGains(dof, false, out double k, out double c);
            double gs = _t.kin_gain;
            _b.SetDofDrive(dof, target, vel, k * gs, c * Math.Sqrt(gs));
            _b.SetDofFeedForward(dof, SupportTorque(dof, Vector3.Zero, default, false));
        }
    }
}
