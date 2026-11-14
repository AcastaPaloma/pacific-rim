using System;
using System.Collections.Generic;
using System.Numerics;
using Game.Control.Contracts;
using Game.Core;
using Game.Physics.Body.Contracts;
using Game.Units.Mech.Contracts;

namespace Game.Units.Mech
{
    /// <summary>
    /// F-003 (M0 part): biped walking under physics, the R-005 section 5 loop. Finite-state walk, capture-point (DCM) foot placement, virtual-force
    /// (Jacobian-transpose) stance legs with gravity feed-forward, PD joint drives toward inverse-kinematics targets for the swing leg and upper body.
    /// The controller's only outputs are joint drive settings and joint torques through <see cref="IArticulatedBody"/>: it cannot move, pin or push a body.
    /// Engine-free. One instance per mech; needs exactly two legs (other body plans get their own controller).
    /// </summary>
    public sealed class LocomotionController
    {
        sealed class Leg
        {
            public int Foot, Thigh, Shank, FootLink;
            public int[] Hip = { -1, -1, -1 }; public int Knee = -1; public int[] Ankle = { -1, -1, -1 };
            public float Side;                 // -1 left (-x), +1 right
            public double ThighLength, ShankLength;
            public double Load;                // measured normal force, lightly filtered
            public float Weight;               // share of the total ground force this step (0 = swing)
            public Vector3 Force, Cop;         // commanded ground reaction and its point of application
            public FootState State; public LinkState FootLinkState;
            public float Yaw; public Vector2 Ankle2, Sole2;
        }

        public struct Debug
        {
            public LocoPhase Phase; public int Stance; public Vector2 Dcm, CopCommand, SwingTarget, Reference; public float ComHeightTarget, StepTime, TimeInStep; public bool SwingClamped;
        }

        readonly IArticulatedBody _b;
        readonly WalkTuning _t;
        readonly MechLimits _lim;
        readonly IntentLimiter _limiter;
        readonly IEventBus _bus;
        readonly double _dt, _g, _mass, _l;
        readonly Leg[] _legs;
        readonly int _pelvis;
        readonly int[][] _descendants;      // per DOF: links on the distal side
        readonly double[] _prevTarget; readonly bool[] _hasPrev; readonly bool[] _isLegDof;
        readonly int[] _armShoulderFlex = { -1, -1 }, _elbow = { -1, -1 };
        readonly float _soleHalfLength, _soleHalfWidth, _ankleToSoleCentre, _ankleHeight, _footWidth, _stanceWidth, _comRest, _pelvisRest;
        readonly double _hipLimit, _kTilt, _dTilt, _kYaw, _dYaw;

        // state
        LocoPhase _phase = LocoPhase.Stand;
        int _stance;                         // leg index of the stance (or new stance) leg
        double _time, _stepT, _tds, _tss;    // time since the step began (touchdown), step timings
        Vector2 _copPrev, _copAtTouchdown, _offsetHeld;
        Vector3 _swingStart; float _swingStartYaw, _swingYawTarget; Vector2 _swingTarget; double _swingTime;
        bool _stomp, _stompDone, _firstStep, _capture;
        double _comHeightTarget;
        LimitCause _lastCauses; double _restTime;
        LocoCommand _cmd; Debug _dbg;
        // per-step measurements
        LinkState _pelvisState; Vector3 _com, _comVel; Vector2 _com2, _vel2, _dcm; float _groundY, _zc, _omega, _pelvisYaw;

        public LocoPhase Phase => _phase;
        public LocoCommand Command => _cmd;
        public Debug DebugState => _dbg;
        public MechLimits Limits => _lim;

        public LocomotionController(IArticulatedBody body, WalkTuning tuning, double dt, IEventBus bus)
        {
            if (tuning == null || tuning.schema != WalkTuning.Schema) throw new InvalidOperationException("walk tuning schema is not " + WalkTuning.Schema);
            _b = body; _t = tuning; _dt = dt; _bus = bus; _g = body.Tuning.gravity_mps2; _mass = body.TotalMassKg;
            var f = body.Frame;
            if (body.FootCount != 2) throw new InvalidOperationException("the biped locomotion controller needs exactly 2 feet; this frame has " + body.FootCount);
            _legs = new Leg[2];
            for (int i = 0; i < 2; i++)
            {
                body.GetFoot(i, out var fs);
                var leg = new Leg { Foot = i, FootLink = fs.Link, Shank = body.LinkParent(fs.Link) };
                leg.Thigh = body.LinkParent(leg.Shank);
                _legs[i] = leg;
            }
            _pelvis = body.LinkParent(_legs[0].Thigh);
            int nd = body.DofCount;
            _prevTarget = new double[nd]; _hasPrev = new bool[nd]; _isLegDof = new bool[nd]; _descendants = new int[nd][];
            for (int d = 0; d < nd; d++)
            {
                var info = body.GetDof(d);
                var list = new List<int>();
                for (int i = 0; i < body.LinkCount; i++) { for (int a = i; a >= 0; a = body.LinkParent(a)) if (a == info.ChildLink) { list.Add(i); break; } }
                _descendants[d] = list.ToArray();
                foreach (var leg in _legs)
                {
                    if (info.ChildLink == leg.Thigh) { leg.Hip[info.AxisIndex] = d; _isLegDof[d] = true; }
                    else if (info.ChildLink == leg.Shank) { leg.Knee = d; _isLegDof[d] = true; }
                    else if (info.ChildLink == leg.FootLink) { leg.Ankle[info.AxisIndex] = d; _isLegDof[d] = true; }
                }
            }
            foreach (var leg in _legs)
            {
                double[] hip = JointPos(f, body.LinkId(leg.Thigh)), knee = JointPos(f, body.LinkId(leg.Shank)), ankle = JointPos(f, body.LinkId(leg.FootLink));
                leg.Side = hip[0] < 0 ? -1 : 1; leg.ThighLength = Dist(hip, knee); leg.ShankLength = Dist(knee, ankle);
                if (leg.Hip[0] < 0 || leg.Knee < 0 || leg.Ankle[0] < 0) throw new InvalidOperationException("leg of foot " + body.FootId(leg.Foot) + " lacks an active hip, knee or ankle flexion DOF");
                for (int d = 0; d < nd; d++)
                {
                    var info = body.GetDof(d);
                    string seg = body.LinkId(info.ChildLink);
                    int arm = leg.Side < 0 ? 0 : 1;
                    if (info.Kind == "shoulder" && info.AxisIndex == 0 && seg.EndsWith(leg.Side < 0 ? "_L" : "_R", StringComparison.Ordinal)) _armShoulderFlex[arm] = d;
                    if (info.Kind == "elbow" && seg.EndsWith(leg.Side < 0 ? "_L" : "_R", StringComparison.Ordinal)) _elbow[arm] = d;
                }
            }
            _l = JointPos(f, body.LinkId(_legs[0].Thigh))[1];
            _lim = new MechLimits(tuning, _l, _g);
            _limiter = new IntentLimiter(_lim, dt);
            var sole = f.foot_sole;
            _soleHalfLength = (float)(sole.length_m * 0.5 * (1 - tuning.sole_margin)); _soleHalfWidth = (float)(sole.width_m * 0.5 * (1 - tuning.sole_margin));
            _ankleToSoleCentre = (float)((sole.heel_z_m + sole.toe_z_m) * 0.5 - sole.ankle_z_m); _ankleHeight = (float)sole.ankle_height_m; _footWidth = (float)sole.width_m;
            _stanceWidth = (float)f.stance.neutral_stance_width_m; _comRest = (float)f.com_rest[1]; _pelvisRest = (float)f.segments[_pelvis].proximal_joint[1];
            _hipLimit = body.GetDof(_legs[0].Hip[0]).ForceLimitNm;
            double iTilt = body.DofInertia(_legs[0].Hip[0], true), iYaw = _legs[0].Hip[1] >= 0 ? body.DofInertia(_legs[0].Hip[1], true) : iTilt;
            double wa = 2 * Math.PI * tuning.attitude_hz / Math.Sqrt(_l / 42.0), wy = 2 * Math.PI * tuning.yaw_hz / Math.Sqrt(_l / 42.0);
            _kTilt = iTilt * wa * wa; _dTilt = 2 * iTilt * wa; _kYaw = iYaw * wy * wy; _dYaw = 2 * iYaw * wy;
            _comHeightTarget = _pelvisRest;
        }

        static double[] JointPos(MechFrameData f, string child) { foreach (var j in f.joints) if (j.child == child) return j.position; throw new InvalidOperationException("no joint for " + child); }
        static double Dist(double[] a, double[] b) => Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]) + (a[2] - b[2]) * (a[2] - b[2]));
        static Vector2 Fwd(float yaw) => new Vector2((float)Math.Sin(yaw), (float)Math.Cos(yaw));
        static Vector2 Right(float yaw) => new Vector2((float)Math.Cos(yaw), -(float)Math.Sin(yaw));
        static Vector2 XZ(Vector3 v) => new Vector2(v.X, v.Z);
        static float Wrap(float a) => (float)IntentLimiter.Wrap(a);
        static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;

        /// <summary>One control step. Call once per physics step, before the step is simulated.</summary>
        public void Update(long step, in IntentFrame intent)
        {
            Measure();
            if (_phase == LocoPhase.Falling || _phase == LocoPhase.Fallen) { _cmd = new LocoCommand { Move = intent.move, Heading = _pelvisYaw, Causes = LimitCause.Fallen }; FallControl(step); Report(step); return; }
            if (FallDetected()) { _phase = LocoPhase.Falling; _restTime = 0; _bus?.Publish(new MechFallStarted { Unit = _b.UnitId, Step = step }); _limiter.Reset(_pelvisYaw); FallControl(step); Report(step); return; }
            double stepTime = _lim.StepTime(Math.Max(_cmd.Speed, 0.1 * _lim.VCruise));
            _cmd = _limiter.Update(intent, _com2, _pelvisYaw, _vel2.Length(), _t.step_length_max_frac * _l / stepTime);
            StateMachine(intent);
            WriteDrives();
            Report(step);
        }

        void Report(long step)
        {
            _dbg.Phase = _phase; _dbg.Stance = _stance; _dbg.Dcm = _dcm; _dbg.ComHeightTarget = (float)_comHeightTarget; _dbg.StepTime = (float)_stepT; _dbg.TimeInStep = (float)_time;
            if (_cmd.Causes != _lastCauses) { _lastCauses = _cmd.Causes; _bus?.Publish(new IntentLimited { Unit = _b.UnitId, Step = step, Causes = (int)_cmd.Causes, RequestedSpeed = _cmd.RequestedSpeed, Speed = _cmd.Speed }); }
        }

        void Measure()
        {
            _b.GetLink(_pelvis, out _pelvisState);
            _com = _b.Com; _comVel = _b.ComVelocity; _com2 = XZ(_com); _vel2 = XZ(_comVel);
            _pelvisYaw = JointMath2.Yaw(_pelvisState.Rotation);
            float ground = 0; int n = 0;
            foreach (var leg in _legs)
            {
                _b.GetFoot(leg.Foot, out leg.State); _b.GetLink(leg.FootLink, out leg.FootLinkState);
                leg.Load += 0.3 * (leg.State.NormalForceN - leg.Load);
                leg.Yaw = JointMath2.Yaw(leg.State.Rotation); leg.Ankle2 = XZ(leg.FootLinkState.Position); leg.Sole2 = XZ(leg.State.SoleCentre);
                if (leg.Weight > 0 || _phase == LocoPhase.Stand) { ground += leg.State.SoleCentre.Y; n++; }
            }
            _groundY = n > 0 ? ground / n : _groundY;
            _zc = Math.Max(1f, _com.Y - _groundY);
            _omega = (float)Lipm.Omega(_g, _zc);
            _dcm = _com2 + _vel2 / _omega;
        }

        bool FallDetected()
        {
            Vector3 up = Vector3.Transform(Vector3.UnitY, _pelvisState.Rotation);
            double tilt = Math.Acos(Clamp(up.Y, -1, 1));
            return tilt > _t.fall_tilt_deg * Math.PI / 180 || _com.Y - _groundY < _t.fall_height_frac * _comRest || _b.NonFootContacts > 0;
        }

        /// <summary>A fall is physics only: no recovery, no stabilising force. Drives go limp with a declared fraction of their critical damping.</summary>
        void FallControl(long step)
        {
            for (int d = 0; d < _b.DofCount; d++)
            {
                _b.GetDofGains(d, false, out _, out double damping);
                _b.SetDofDrive(d, _b.DofPosition(d), 0, 0, damping * _t.fall_damping_ratio);
                _b.SetDofFeedForward(d, 0);
                _hasPrev[d] = false;
            }
            foreach (var leg in _legs) leg.Weight = 0;
            if (_phase == LocoPhase.Falling)
            {
                double rest = Math.Sqrt(_t.rest_speed_fr * _g * _l);
                _restTime = _comVel.Length() < rest && _b.NonFootContacts > 0 ? _restTime + _dt : 0;
                if (_restTime > 1.0) { _phase = LocoPhase.Fallen; _bus?.Publish(new MechFallEnded { Unit = _b.UnitId, Step = step }); }
            }
        }

        // ---------------------------------------------------------------- state machine and support planning
        Vector2 Nominal(Leg leg, double u, double travelScale, bool inboard)
        {
            double z = _t.cop_heel_frac + (_t.cop_toe_frac - _t.cop_heel_frac) * u;
            if (!inboard) z = _t.cop_stand_frac;
            else z = _t.cop_stand_frac + (z - _t.cop_stand_frac) * travelScale;
            return leg.Ankle2 + Fwd(leg.Yaw) * (float)(z * _l) + (inboard ? Right(leg.Yaw) * (float)(-leg.Side * _t.cop_inboard_frac * _l) : Vector2.Zero);
        }

        Vector2 ClampToSole(Leg leg, Vector2 p)
        {
            Vector2 r = p - leg.Sole2, f = Fwd(leg.Yaw), s = Right(leg.Yaw);
            return leg.Sole2 + s * Clamp(Vector2.Dot(r, s), -_soleHalfWidth, _soleHalfWidth) + f * Clamp(Vector2.Dot(r, f), -_soleHalfLength, _soleHalfLength);
        }

        void StateMachine(in IntentFrame intent)
        {
            Leg a = _legs[0], b = _legs[1];
            bool wantsWalk = _cmd.Speed > 1e-3f;
            float feetYaw = a.Yaw + 0.5f * Wrap(b.Yaw - a.Yaw);
            bool turnMove = intent.move == MoveId.Turn || intent.move == MoveId.Walk;
            bool wantsTurn = turnMove && Math.Abs(Wrap(_cmd.Heading - feetYaw)) > (_phase == LocoPhase.Stand ? 0.07f : 0.025f);
            bool wantsStomp = intent.move == MoveId.Stomp && !_stompDone;
            if (intent.move != MoveId.Stomp) _stompDone = false;
            double targetHeight = _pelvisRest - _t.com_drop_stand_frac * _l;

            switch (_phase)
            {
                case LocoPhase.Stand:
                {
                    if (wantsWalk || wantsTurn || wantsStomp)
                    {
                        // first swing leg: the one on the side the mech turns toward; for a stomp the foot nearer the target
                        float turn = Wrap(_cmd.Heading - feetYaw);
                        int rightLeg = _legs[0].Side > 0 ? 0 : 1;
                        int swing = turn < -0.03f ? 1 - rightLeg : rightLeg;
                        if (wantsStomp) swing = Vector2.DistanceSquared(a.Sole2, XZ(intent.target)) <= Vector2.DistanceSquared(b.Sole2, XZ(intent.target)) ? 0 : 1;
                        _stance = 1 - swing; _stomp = wantsStomp; _phase = LocoPhase.Shift; _time = 0; _firstStep = true;
                        PlanTimings();
                    }
                    Vector2 reference = 0.5f * (Nominal(a, 0, 0, false) + Nominal(b, 0, 0, false));
                    Regulate(reference, a, b, (float)_t.stand_min_share); _dbg.Reference = reference;
                    Vector2 na = Nominal(a, 0, 0, false), nb = Nominal(b, 0, 0, false), ab = nb - na;
                    Vector2 nearest = na + ab * Clamp(Vector2.Dot(_dcm - na, ab) / Math.Max(1e-3f, ab.LengthSquared()), 0, 1);
                    if (_phase == LocoPhase.Stand && (_dcm - nearest).Length() > _t.capture_trigger_frac * _l)
                    {
                        // balance is leaving the ankle strategy's reach: take a capture step with the less loaded foot (R-005 5.4 step 4)
                        _stance = a.State.NormalForceN >= b.State.NormalForceN ? 0 : 1; _stomp = false; _firstStep = true;
                        _capture = true; PlanTimings(); _time = _tds; BeginSwing();
                    }
                    break;
                }
                case LocoPhase.Shift:
                {
                    Leg st = _legs[_stance], sw = _legs[1 - _stance];
                    if (!wantsWalk && !wantsTurn && !wantsStomp) { _phase = LocoPhase.Stand; goto case LocoPhase.Stand; }
                    _time += _dt;
                    float wCop = Vector2.Dot(Nominal(sw, 0, 1, true) - Nominal(st, 0, 1, true), Right(st.Yaw)) * sw.Side;
                    float by = _stomp ? 0f : (float)Lipm.OffsetLateral(wCop, _omega, _stepT);
                    Vector2 reference = Nominal(st, 0, 1, true) + Right(st.Yaw) * (sw.Side * by);
                    Regulate(reference, st, sw, 0f); _dbg.Reference = reference;
                    bool there = (_dcm - reference).Length() < 0.025f * _l && (sw.State.NormalForceN < _t.liftoff_load_bw * _mass * _g || _time > _t.shift_time_tstar * _lim.TStar);
                    if (there || _time > 2.5 * _t.shift_time_tstar * _lim.TStar)
                    { _time = _tds; BeginSwing(); }
                    targetHeight = _pelvisRest - _t.com_drop_walk_frac * _l;
                    break;
                }
                case LocoPhase.DoubleSupport:
                case LocoPhase.SingleSupport:
                {
                    _time += _dt;
                    Leg st = _legs[_stance], sw = _legs[1 - _stance];
                    float heading = _cmd.Heading;
                    double T = _stepT, tr = Math.Max(T - _time, 0.12 * T);
                    float speed = _stomp ? 0 : Math.Min(_cmd.Speed, _vel2.Length() + (float)(_lim.AccelStart * T));
                    speed = Math.Min(speed, (float)(_t.step_length_max_frac * _l / T));
                    float s = (float)(speed * T);
                    float travelScale = Clamp(s / (float)((_t.cop_toe_frac - _t.cop_heel_frac) * _l * 2), 0, 1);
                    float travel = (float)((_t.cop_toe_frac - _t.cop_heel_frac) * _l) * travelScale;
                    Vector2 nStart = Nominal(st, 0, travelScale, true), nNow = Nominal(st, Math.Min(1, _time / T), travelScale, true);
                    Vector2 pdot = _time < T ? Fwd(st.Yaw) * (float)(travel / T) : Vector2.Zero;
                    bool moving = speed > 1e-3f || wantsTurn;
                    float width = moving ? (float)(_t.step_width_walk_frac * _l) : _stanceWidth;
                    float wCop = width - 2f * (float)(_t.cop_inboard_frac * _l);
                    float bx = (float)Lipm.OffsetForward(s - travel, _omega, T), by = (float)Lipm.OffsetLateral(wCop, _omega, T);
                    Vector2 fwd = Fwd(heading), right = Right(heading);
                    Vector2 nextNominal = nStart + fwd * s + right * (sw.Side * wCop);
                    Vector2 dcmEndDesired = nextNominal + fwd * bx + right * (st.Side * by);
                    if (_time < T * (1 - 0.12)) _offsetHeld = Lipm.DeadbeatOffset(_dcm, nNow, pdot, dcmEndDesired, _omega, tr);
                    Vector2 copStance = ClampToSole(st, nNow + _offsetHeld);
                    if (_stomp) { Vector2 hold = Nominal(st, 0, 1, true); copStance = ClampToSole(st, _dcm + (float)_t.dcm_gain * (_dcm - hold)); _dbg.Reference = hold; }
                    Vector2 dcmEnd = Lipm.DcmAtEnd(_dcm, copStance, pdot, _omega, tr);

                    if (_phase == LocoPhase.DoubleSupport)
                    {
                        Vector2 copTrail = ClampToSole(sw, _copAtTouchdown), want = nNow + _offsetHeld, seg = copStance - copTrail;
                        float wNew = Clamp(Vector2.Dot(want - copTrail, seg) / Math.Max(1e-3f, seg.LengthSquared()), 0, 1);
                        wNew = Math.Max(wNew, (float)Lipm.SmoothStep((_time - 0.5 * _tds) / _tds));
                        // the trailing leg keeps carrying the body until the landed foot really bears load: the planned share may lead the measured one only by a margin
                        double loads = st.Load + sw.Load;
                        if (loads > 0.2 * _mass * _g) wNew = Math.Min(wNew, (float)(st.Load / loads + _t.transfer_lead));
                        _transfer = Math.Max(_transfer * 0.9f + wNew * 0.1f, (float)_t.touchdown_share);
                        wNew = _transfer;
                        Assign(st, wNew, copStance); Assign(sw, 1 - wNew, copTrail);
                        if ((_time >= _tds && wNew > 0.95f) || (_time >= 1.6 * _tds && wNew > 0.8f) || _time >= 3 * _tds) BeginSwing();
                    }
                    else
                    {
                        Assign(st, 1, copStance); Assign(sw, 0, copStance);
                        _swingTime += _dt;
                        double tau = _swingTime / _tss;
                        if (tau < _t.swing_freeze && !_stomp)
                        {
                            // capture-point foot placement: the next stance CoP sits behind the predicted DCM by the steady-gait offsets
                            float yawNext = st.Yaw + Clamp(Wrap(heading - st.Yaw), -(float)(_t.max_step_yaw_deg * Math.PI / 180), (float)(_t.max_step_yaw_deg * Math.PI / 180));
                            Vector2 nNext = dcmEnd - fwd * bx - right * (st.Side * by);
                            Vector2 ankleNext = nNext - Right(yawNext) * (float)(-sw.Side * _t.cop_inboard_frac * _l) - Fwd(yawNext) * (float)((_t.cop_stand_frac + (_t.cop_heel_frac - _t.cop_stand_frac) * travelScale) * _l);
                            // reachable region relative to the stance foot
                            Vector2 rel = ankleNext - st.Ankle2, sf = Fwd(st.Yaw), sr = Right(st.Yaw);
                            float along = Vector2.Dot(rel, sf), side = Vector2.Dot(rel, sr) * sw.Side;
                            float along2 = Clamp(along, -(float)(_t.reach_back_frac * _l), (float)(_t.reach_forward_frac * _l)), side2 = Clamp(side, _footWidth + (float)(_t.foot_gap_frac * _l), (float)(_t.reach_side_frac * _l));
                            _dbg.SwingClamped = along2 != along || side2 != side;
                            Vector2 wanted = st.Ankle2 + sf * along2 + sr * (side2 * sw.Side) + Fwd(yawNext) * _ankleToSoleCentre, change = wanted - _swingTarget;
                            float maxChange = (float)(_t.swing_retarget_tstar * _l / _lim.TStar * _dt);
                            _swingTarget += _swingTime <= _dt * 1.5 || change.Length() <= maxChange ? change : change * (maxChange / change.Length());
                            _swingYawTarget = yawNext;
                        }
                        bool landed = sw.State.Contact && tau > 0.5;
                        if (landed) Touchdown(wantsWalk, wantsTurn);
                    }
                    _dbg.CopCommand = copStance; _dbg.SwingTarget = _swingTarget;
                    // the hips ride low enough for both legs to reach a step of this length with flat feet (geometry, not tuning)
                    double legMax = Math.Sqrt(st.ThighLength * st.ThighLength + st.ShankLength * st.ShankLength + 2 * st.ThighLength * st.ShankLength * Math.Cos(_t.knee_min_flex_deg * Math.PI / 180));
                    double half = 0.5 * s + _t.step_reach_margin_frac * _l, legTotal = st.ThighLength + st.ShankLength;
                    double stepDrop = legTotal - Math.Sqrt(Math.Max(legMax * legMax - half * half, 0.64 * legTotal * legTotal));
                    targetHeight = _pelvisRest - Math.Max(_t.com_drop_walk_frac * _l, stepDrop) - _extraDrop;
                    break;
                }
            }
            double rate = 0.02 * _l / _lim.TStar * _dt; // the height target moves slowly (about 0.4 m/s at reference)
            _comHeightTarget += Math.Max(-rate, Math.Min(rate, targetHeight - _comHeightTarget));
            Forces();
        }

        void PlanTimings()
        {
            _stepT = _stomp ? _t.step_time_max_tstar * _lim.TStar : _capture ? _t.capture_step_time_tstar * _lim.TStar : _lim.StepTime(_cmd.Speed);
            _capture = false;
            _tds = (2 * _t.duty_factor - 1) * _stepT; _tss = _stepT - _tds;
        }

        void BeginSwing()
        {
            Leg sw = _legs[1 - _stance];
            _phase = LocoPhase.SingleSupport; _swingTime = 0;
            _swingStart = sw.State.SoleCentre; _swingStartYaw = sw.Yaw; _swingTarget = sw.Sole2; _swingYawTarget = sw.Yaw;
            foreach (int d in sw.Hip) if (d >= 0) _hasPrev[d] = false;
            _hasPrev[sw.Knee] = false;
            foreach (int d in sw.Ankle) if (d >= 0) _hasPrev[d] = false;
        }

        void Touchdown(bool wantsWalk, bool wantsTurn)
        {
            Leg st = _legs[_stance], sw = _legs[1 - _stance];
            _firstStep = false;
            float along = Math.Abs(Vector2.Dot(sw.Ankle2 - st.Ankle2, Fwd(st.Yaw)));
            Vector2 mid = 0.5f * (st.Ankle2 + sw.Ankle2);
            bool calm = Math.Abs(Vector2.Dot(_vel2, Fwd(st.Yaw))) < _lim.StopSpeed && (_dcm - mid).Length() < 0.5f * (st.Ankle2 - sw.Ankle2).Length() && along < 0.06f * _l && Math.Abs(Wrap(sw.Yaw - st.Yaw)) < 0.06f;
            if (_stomp || (!wantsWalk && !wantsTurn && calm))
            {
                if (_stomp) _stompDone = true;
                _stomp = false; _phase = LocoPhase.Stand; return;
            }
            _copAtTouchdown = _copPrev;
            _stance = 1 - _stance; _phase = LocoPhase.DoubleSupport; _time = 0; _transfer = (float)_t.touchdown_share;
            PlanTimings();
        }

        /// <summary>Standing balance: the CoP command steers the DCM to a reference point (ankle strategy), then the load is shared between the two feet.</summary>
        void Regulate(Vector2 reference, Leg first, Leg second, float minSecond)
        {
            Vector2 cop = _dcm + (float)_t.dcm_gain * (_dcm - reference);
            Vector2 n1 = Nominal(first, 0, 0, false), n2 = Nominal(second, 0, 0, false), seg = n2 - n1;
            float w2 = Clamp(Vector2.Dot(cop - n1, seg) / Math.Max(1e-3f, seg.LengthSquared()), minSecond, 1 - (float)_t.stand_min_share);
            Vector2 residual = cop - (n1 + seg * w2);
            Assign(first, 1 - w2, ClampToSole(first, n1 + residual)); Assign(second, w2, ClampToSole(second, n2 + residual));
            _dbg.CopCommand = cop;
        }

        void Assign(Leg leg, float weight, Vector2 cop) { leg.Weight = weight; leg.Cop = new Vector3(cop.X, leg.State.SoleCentre.Y, cop.Y); }

        /// <summary>R-005 5.2 step 6: the virtual force on the centre of mass (height spring-damper plus the pendulum force through the realised CoP), split by the load shares.</summary>
        void Forces()
        {
            double wc = 2 * Math.PI * _t.height_hz / Math.Sqrt(_l / 42.0);
            double fy = _mass * (_g + wc * wc * (_comHeightTarget - (_pelvisState.Position.Y - _groundY)) - 2 * wc * _pelvisState.Velocity.Y);
            fy = Math.Max(_t.force_min_bw * _mass * _g, Math.Min(_t.force_max_bw * _mass * _g, fy));
            Vector2 cop = Vector2.Zero; float wsum = 0;
            foreach (var leg in _legs) { cop += XZ(leg.Cop) * leg.Weight; wsum += leg.Weight; }
            if (wsum <= 0) return;
            cop /= wsum; _copPrev = cop;
            Vector2 fh = (_com2 - cop) * (float)(fy / _zc);
            // never ask the ground for more friction than two thirds of what it has (R-005 5.3: mu_avail >= 1.5 mu_req)
            float fhMax = (float)(fy * _b.Tuning.friction_dynamic / 1.5);
            if (fh.Length() > fhMax) fh *= fhMax / fh.Length();
            // vertical force by the planned share; horizontal force by the load each foot really carries, so a lightly loaded foot is never asked for friction it does not have
            double loadSum = 0; foreach (var leg in _legs) if (leg.Weight > 0) loadSum += leg.Load;
            foreach (var leg in _legs)
            {
                float share = leg.Weight / wsum, grip = loadSum > 0.2 * _mass * _g && leg.Weight > 0 ? (float)(leg.Load / loadSum) : share;
                leg.Force = new Vector3(fh.X * grip, (float)fy * share, fh.Y * grip);
            }
        }

        // ---------------------------------------------------------------- joint level
        void WriteDrives()
        {
            Leg st = _legs[_stance], sw = _legs[1 - _stance];
            // trunk attitude through the supporting hips (SIMBICON: stance hip torque = -torso torque - swing hip torque)
            float yawDesired = _pelvisYaw;
            if (_phase != LocoPhase.Stand) yawDesired = st.Yaw + 0.5f * Wrap((_phase == LocoPhase.SingleSupport ? _swingYawTargetNow : sw.Yaw) - st.Yaw);
            else yawDesired = _legs[0].Yaw + 0.5f * Wrap(_legs[1].Yaw - _legs[0].Yaw);
            Quaternion desired = JointMath2.YawRotation(yawDesired);
            Vector3 e = JointMath2.RotVec(desired * Quaternion.Conjugate(_pelvisState.Rotation)), w = _pelvisState.AngularVelocity;
            Vector3 tilt = new Vector3(e.X, 0, e.Z) * (float)_kTilt - new Vector3(w.X, 0, w.Z) * (float)_dTilt;
            float tiltMax = (float)(_t.attitude_torque_frac * _hipLimit), yawMax = (float)(_t.yaw_torque_frac * _hipLimit);
            if (tilt.Length() > tiltMax) tilt *= tiltMax / tilt.Length();
            float yawTorque = Clamp((float)(_kYaw * e.Y - _dYaw * w.Y), -yawMax, yawMax);
            Vector3 trunkTorque = tilt + new Vector3(0, yawTorque, 0);
            Vector3 swingReaction = Vector3.Zero;
            foreach (var leg in _legs) if (IsSwinging(leg)) foreach (int d in leg.Hip) if (d >= 0) swingReaction += _b.DofAxisWorld(d) * (float)_b.DofAppliedTorque(d);
            float wsum = _legs[0].Weight + _legs[1].Weight;

            foreach (var leg in _legs)
            {
                if (!IsSwinging(leg))
                {
                    Vector3 hipExtra = wsum > 0 ? -(trunkTorque + swingReaction) * (leg.Weight / wsum) : Vector3.Zero;
                    // a planted foot must not be twisted on the ground: yaw torque stays inside what its load can hold by friction (R-005 5.2 step 7)
                    float yawGrip = (float)(_b.Tuning.friction_dynamic / 1.5 * leg.Load * _t.yaw_grip_radius_frac * _l);
                    hipExtra.Y = Clamp(hipExtra.Y, -yawGrip, yawGrip);
                    StanceDof(leg, leg.Hip, hipExtra, _t.stance_hip_damping_ratio);
                    StanceDof(leg, new[] { leg.Knee }, Vector3.Zero, _t.stance_knee_damping_ratio);
                    StanceDof(leg, leg.Ankle, Vector3.Zero, _t.stance_ankle_damping_ratio);
                }
                else SwingLeg(leg);
            }
            // upper body: hold posture, arms counter-swing the opposite leg (R-005: PD toward targets with gravity feed-forward)
            for (int d = 0; d < _b.DofCount; d++)
            {
                if (_isLegDof[d]) continue;
                double target = 0;
                for (int arm = 0; arm < 2; arm++)
                {
                    if (d == _armShoulderFlex[arm]) { Leg opposite = _legs[0].Side < 0 == (arm == 0) ? _legs[1] : _legs[0]; target = _t.arm_swing_gain * _b.DofPosition(opposite.Hip[0]); }
                    if (d == _elbow[arm]) target = _t.elbow_flex_deg * Math.PI / 180;
                }
                Track(d, target, Vector3.Zero, default, false);
            }
        }

        float _swingYawTargetNow, _transfer;
        double _extraDrop;
        bool IsSwinging(Leg leg) => _phase == LocoPhase.SingleSupport && leg == _legs[1 - _stance];

        /// <summary>Torque that lets the joint carry the leg's ground force and the weight of everything distal to it: tau = -axis . (sum r x F).</summary>
        double SupportTorque(int dof, Vector3 force, Vector3 cop, bool loaded)
        {
            var info = _b.GetDof(dof);
            _b.GetLink(info.ChildLink, out var child);
            Vector3 r0 = child.Position, moment = Vector3.Zero;
            foreach (int link in _descendants[dof]) { _b.GetLink(link, out var s); moment += Vector3.Cross(s.Com - r0, new Vector3(0, -(float)(_b.LinkMassKg(link) * _g), 0)); }
            if (loaded) moment += Vector3.Cross(cop - r0, force);
            return -Vector3.Dot(_b.DofAxisWorld(dof), moment);
        }

        void StanceDof(Leg leg, int[] dofs, Vector3 extraTorque, double dampingRatio)
        {
            foreach (int d in dofs)
            {
                if (d < 0) continue;
                Vector3 force = leg.Force;
                // the ankle sets the centre of pressure: its torque must match the load the foot really carries, not the load the plan expects
                if (dofs == leg.Ankle && force.Y > 1f && leg.Load > 0) force *= Clamp((float)(leg.Load / force.Y), 0.5f, 3f);
                // a foot that has just touched is not yet flat: its ankle stays passive (damped) and takes up centre-of-pressure control gradually, so the sole settles instead of slapping down
                if (dofs == leg.Ankle && _phase == LocoPhase.DoubleSupport && leg == _legs[_stance]) force *= (float)Lipm.SmoothStep(_time / (_t.landing_time_tstar * _lim.TStar));
                double tau = SupportTorque(d, force, leg.Cop, true) + Vector3.Dot(_b.DofAxisWorld(d), extraTorque);
                _b.GetDofGains(d, true, out double stiffness, out double damping);
                double target = _b.DofPosition(d), k = 0, velocity = 0;
                // velocity-controlled touchdown: the leg that has just landed keeps its swing damping for a moment, so the force loop loads it instead of throwing the foot at the ground
                double landing = 0;
                if (_phase == LocoPhase.DoubleSupport && leg == _legs[_stance] && _t.landing_damping_scale > 0)
                {
                    _b.GetDofGains(d, false, out _, out double swingDamping);
                    landing = swingDamping * _t.landing_damping_scale * (1 - Lipm.SmoothStep(_time / (_t.landing_time_tstar * _lim.TStar)));
                }
                if (d == leg.Knee && _t.stance_posture_hz > 0)
                {
                    // soft posture spring: the knee angle whose leg length puts the CoM at its target height (keeps the leg off its straight, singular pose)
                    _b.GetLink(leg.Thigh, out var thigh);
                    Vector3 hipToAnkle = leg.FootLinkState.Position - thigh.Position;
                    double len = hipToAnkle.Length(), incline = Math.Max(0.5, -hipToAnkle.Y / Math.Max(1e-3, len));
                    double want = len + (_comHeightTarget - (_pelvisState.Position.Y - _groundY)) * incline;
                    double a = leg.ThighLength, c = leg.ShankLength, kneeMin = _t.knee_min_flex_deg * Math.PI / 180;
                    double cosInterior = Math.Min(1, Math.Max(Math.Cos(Math.PI - kneeMin), (a * a + c * c - want * want) / (2 * a * c)));
                    target = Math.PI - Math.Acos(cosInterior);
                    double ratio = _t.stance_posture_hz / _b.Tuning.Kind("knee").stance_hz;
                    k = stiffness * ratio * ratio;
                    velocity = _hasPrev[d] ? Math.Max(-1, Math.Min(1, (target - _prevTarget[d]) / _dt)) : 0;
                    _prevTarget[d] = target; _hasPrev[d] = true;
                }
                else _hasPrev[d] = false;
                _b.SetDofDrive(d, target, velocity, k, damping * dampingRatio + landing);
                _b.SetDofFeedForward(d, tau);
            }
        }

        void SwingLeg(Leg leg)
        {
            // foot path: minimum-jerk horizontally from lift-off to the capture-point target, clearance bump, slow final descent until contact
            double tau = _swingTime / _tss, s = Lipm.MinJerk(Math.Min(1, tau));
            float ground = _legs[_stance].State.SoleCentre.Y;
            double tStar = _lim.TStar, height;
            if (_stomp)
            {
                double up = 0.5 * _tss, lift = _t.stomp_lift_frac * _l, v = _t.stomp_speed_tstar * _l / tStar;
                height = _swingTime < up ? lift * Lipm.MinJerk(_swingTime / up) : Math.Max(-0.04 * _l, lift - v * (_swingTime - up));
            }
            else
            {
                // lift and clear early, be over the landing spot at hover height by swing_hover of the swing, then come down slowly until contact (R-005: touchdown <= 1 m/s)
                double vtd = _t.touchdown_speed_tstar * _l / tStar, th = _t.swing_hover, hover = vtd * (1 - th) * _tss;
                double u = Math.Min(1, tau / th);
                height = tau <= th ? hover * Lipm.MinJerk(u) + _t.swing_clearance_frac * _l * Math.Pow(Math.Sin(Math.PI * Math.Pow(u, _t.swing_rise_exponent)), 2) : Math.Max(-0.04 * _l, hover - vtd * (_swingTime - th * _tss));
                s = Lipm.MinJerk(Math.Max(0, Math.Min(1, (tau - 0.04) / (0.5 * (1.16 + th) - 0.04))));
            }
            Vector2 xz = XZ(_swingStart) + (_swingTarget - XZ(_swingStart)) * (float)s;
            float yaw = _swingStartYaw + Wrap(_swingYawTarget - _swingStartYaw) * (float)s;
            _swingYawTargetNow = yaw;
            float startLift = (_swingStart.Y - ground) * (float)(1 - s);
            Quaternion footRot = JointMath2.YawRotation(yaw);
            Vector3 sole = new Vector3(xz.X, ground + startLift + (float)height, xz.Y);
            Vector3 ankle = sole + new Vector3(0, _ankleHeight, 0) - new Vector3(Fwd(yaw).X, 0, Fwd(yaw).Y) * _ankleToSoleCentre;
            _b.GetLink(leg.Thigh, out var thigh);
            var ik = LegIk.Solve(thigh.Position, _pelvisState.Rotation, ankle, footRot, leg.ThighLength, leg.ShankLength, _t.knee_min_flex_deg * Math.PI / 180, 2.2);
            // a landing spot the leg cannot reach lowers the hips until it can (never stretches the foot target)
            double dropRate = 0.03 * _l / tStar;
            _extraDrop = ik.Clamped && tau > 0.5 ? Math.Min(0.05 * _l, _extraDrop + dropRate * _dt) : Math.Max(0, _extraDrop - 0.2 * dropRate * _dt);
            Vector3 hip = JointMath2.RotVec(ik.HipRelative), ank = JointMath2.RotVec(ik.AnkleRelative);
            // compliant touchdown (R-005 5.2 step 5, Q-012): the leg goes soft as the foot nears the ground, so contact loads it through a spring instead of a stiff position servo
            double soft = _stomp ? 1 : 1 - (1 - _t.touchdown_gain_scale) * Lipm.SmoothStep((tau - (_t.swing_hover - 0.15)) / 0.15);
            for (int a = 0; a < 3; a++)
            {
                if (leg.Hip[a] >= 0) Track(leg.Hip[a], Component(hip, a) * _b.GetDof(leg.Hip[a]).Sign, Vector3.Zero, default, true, soft);
                if (leg.Ankle[a] >= 0) Track(leg.Ankle[a], Component(ank, a) * _b.GetDof(leg.Ankle[a]).Sign, Vector3.Zero, default, true, soft);
            }
            Track(leg.Knee, ik.Knee * _b.GetDof(leg.Knee).Sign, Vector3.Zero, default, true, soft);
        }

        static float Component(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

        /// <summary>Position drive toward a target with the R-005 swing gains, velocity feed-forward from the target's own motion and gravity feed-forward.</summary>
        void Track(int dof, double target, Vector3 force, Vector3 cop, bool velocityFeedForward, double gainScale = 1)
        {
            var info = _b.GetDof(dof);
            target = Math.Max(info.LowerRad, Math.Min(info.UpperRad, target));
            double vel = velocityFeedForward && _hasPrev[dof] ? (target - _prevTarget[dof]) / _dt : 0;
            vel = Math.Max(-2.5, Math.Min(2.5, vel));
            _prevTarget[dof] = target; _hasPrev[dof] = true;
            _b.GetDofGains(dof, false, out double k, out double damping);
            // critical damping is kept when the stiffness is scaled: d scales with sqrt(k)
            _b.SetDofDrive(dof, target, vel, k * gainScale, damping * Math.Sqrt(gainScale));
            _b.SetDofFeedForward(dof, SupportTorque(dof, force, cop, false));
        }
    }

    /// <summary>Rotation helpers local to the unit module (it may not reference another module's implementation assembly).</summary>
    static class JointMath2
    {
        public static Vector3 RotVec(Quaternion q)
        {
            if (q.W < 0) q = new Quaternion(-q.X, -q.Y, -q.Z, -q.W);
            var v = new Vector3(q.X, q.Y, q.Z);
            float s = v.Length();
            if (s < 1e-9f) return v * 2f;
            return v * (2f * (float)Math.Atan2(s, q.W) / s);
        }
        public static float Yaw(Quaternion q) { var f = Vector3.Transform(Vector3.UnitZ, q); return (float)Math.Atan2(f.X, f.Z); }
        public static Quaternion YawRotation(float yaw) => Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
    }
}
