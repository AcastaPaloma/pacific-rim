using System;
using System.Collections.Generic;
using System.Numerics;
using Game.Control.Contracts;
using Game.Core;
using Game.Physics.Body.Contracts;
using Game.Physics.Body.Qp;
using Game.Units.Mech.Contracts;
using Game.Units.Mech.Qp;

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
            public double StanceTime;          // time since this leg last began to carry load
            public float Weight;               // share of the total ground force this step (0 = swing)
            public Vector3 Force, Cop;         // commanded ground reaction and its point of application
            public Vector3 HipExtra;           // step 9.5b: the trunk-attitude hip torque of this tick (zero while swinging); read only by the imp 3 qp_lateral path
            public FootState State; public LinkState FootLinkState;
            public float Yaw; public Vector2 Ankle2, Sole2;
        }

        public struct Debug
        {
            public LocoPhase Phase; public int Stance; public Vector2 Dcm, CopCommand, SwingTarget, Reference; public float ComHeightTarget, StepTime, TimeInStep; public bool SwingClamped;
            public double[] AngStRef, AngStMeas, AngSwRef, AngSwMeas; public double RefFroude;   // round 2 step 6a (imp > 0): hip flexion, knee, ankle plantar (deg), stance and swing leg; null when not available
            public double RefPhase; public double[] TauFfSt, TauFfSw;   // round 3 step 8.5 (imp = 2): optimised feed-forward torque hip, knee, ankle (N m) after scale, sign and limit; null otherwise
            public double YawMomentNm, YawBudgetNm, YawUtil, TrunkShare;   // round 2 step 3 (NaN logs as null)
            public int QpStatus; public long QpFailures; public double QpMassScale, QpFrictionCmd; public bool QpOn; public double[] QpJ, QpK; public double QpScale; public int QpIters; public double QpMs; public double[] QpTau; public double[] QpLat, QpYaw; public int QpRow; public bool QpMatchTd; public double QpMatchD, QpMatchZ; public int QpMatchRow, QpMatchNode, QpMatchTds;   // QpLat (step 9.5b, qp_lateral): [sum fy / W, roll, yaw moment / (W l)] commanded and their targets; round 4 step 9.4b (imp = 3): last QP status (0 optimal), failed solves, mass-scale estimate, commanded shear / (mu fn); log only
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
        readonly ReferenceGait _ref;
        readonly double[] _prevVelTarget; readonly double[] _ffAccel;
        // round 2 step 3: yaw-momentum bookkeeping (vertical angular momentum about the CoM from link states and the frame's principal inertias)
        readonly double[][] _linkInertia; readonly bool[] _upperLink; readonly YawShareWindow _shareWindow;
        double _prevYawL = double.NaN, _yawMomentF;
        double _heelOff, _toeOff;
        readonly float _soleFullHalfLength, _soleHalfLength, _soleHalfWidth, _ankleToSoleCentre, _ankleHeight, _footWidth, _stanceWidth, _comRest, _pelvisRest;
        readonly double _hipLimit, _kTilt, _dTilt, _kYaw, _dYaw;

        // state
        LocoPhase _phase = LocoPhase.Stand;
        int _stance;                         // leg index of the stance (or new stance) leg
        double _time, _syncT, _stepT, _tds, _tss;    // time since the step began (touchdown), step timings
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

        public LocomotionController(IArticulatedBody body, WalkTuning tuning, double dt, IEventBus bus, GaitReferenceData gaitReference = null, IQpSolver qpSolver = null, GaitReferenceData qpReference = null)
        {
            if (tuning == null || tuning.schema != WalkTuning.Schema) throw new InvalidOperationException("walk tuning schema is not " + WalkTuning.Schema);
            _b = body; _t = tuning; _dt = dt; _bus = bus; _g = body.Tuning.gravity_mps2; _mass = body.TotalMassKg;
            var f = body.Frame;
            if (body.FootCount != MechContractVersions.BipedFeet) throw new InvalidOperationException("the biped locomotion controller needs exactly 2 feet; this frame has " + body.FootCount);
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
                if (info.Kind == "waist" && info.AxisIndex == 1) _waistYaw = d;
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
            _linkInertia = new double[body.LinkCount][]; _upperLink = new bool[body.LinkCount];
            for (int i = 0; i < body.LinkCount; i++)
            {
                string lid = body.LinkId(i);
                foreach (var sg in f.segments) if (sg.id == lid) { _linkInertia[i] = sg.inertia_principal_kgm2; break; }
            }
            if (_waistYaw >= 0) foreach (int i in _descendants[_waistYaw]) _upperLink[i] = true;
            _shareWindow = new YawShareWindow(Math.Max(16, (int)Math.Ceiling(8 / dt)));
            _l = JointPos(f, body.LinkId(_legs[0].Thigh))[1];
            _lim = new MechLimits(tuning, _l, _g);
            _limiter = new IntentLimiter(_lim, dt);
            var sole = f.foot_sole;
            _soleFullHalfLength = (float)(sole.length_m * 0.5); _soleHalfLength = (float)(sole.length_m * 0.5 * (1 - tuning.sole_margin)); _soleHalfWidth = (float)(sole.width_m * 0.5 * (1 - tuning.sole_margin));
            _ankleToSoleCentre = (float)((sole.heel_z_m + sole.toe_z_m) * 0.5 - sole.ankle_z_m); _ankleHeight = (float)sole.ankle_height_m; _heelOff = sole.heel_z_m - sole.ankle_z_m; _toeOff = sole.toe_z_m - sole.ankle_z_m; _footWidth = (float)sole.width_m;
            _stanceWidth = (float)f.stance.neutral_stance_width_m; _comRest = (float)f.com_rest[1]; _pelvisRest = (float)f.segments[_pelvis].proximal_joint[1];
            _hipLimit = body.GetDof(_legs[0].Hip[0]).ForceLimitNm;
            double iTilt = body.DofInertia(_legs[0].Hip[0], true), iYaw = _legs[0].Hip[1] >= 0 ? body.DofInertia(_legs[0].Hip[1], true) : iTilt;
            double wa = 2 * Math.PI * tuning.attitude_hz / Math.Sqrt(_l / tuning.reference_leg_length_m), wy = 2 * Math.PI * tuning.yaw_hz / Math.Sqrt(_l / tuning.reference_leg_length_m);
            _kTilt = iTilt * wa * wa; _dTilt = 2 * iTilt * wa; _kYaw = iYaw * wy * wy; _dYaw = 2 * iYaw * wy;
            _comHeightTarget = _pelvisRest;
            if (tuning.imp >= 1.5 && gaitReference == null) throw new InvalidOperationException("imp = 2 needs the optimised reference (Config/Mech/gait_reference_opt.json): none was given");
            _ref = new ReferenceGait(GaitGeometry.From(f), tuning, _g, gaitReference);
            _prevVelTarget = new double[nd]; _ffAccel = new double[nd];
            if (QpWanted(tuning))
            {
                // imp = 3: the sagittal whole-body QP overwrites the feed-forward of the six sagittal leg DOFs (planar leg A = the left leg unless qp_leg_a_left = 0); imp 0 / 1 / 2 build nothing
                qpReference ??= gaitReference;   // step 9.5: with qp_engage_speed_frac > 0 the imp 2 pipeline runs on the @v1 reference (gaitReference) until the hand-off and the QP reads its own @v2 reference
                if (qpReference == null || qpReference.schema != GaitReferenceData.SchemaV2) throw new InvalidOperationException("imp = 3 needs the QP reference (Config/Mech/gait_reference_qp.json, gait_reference_opt@v2)");
                if (qpSolver == null) throw new InvalidOperationException("imp = 3 needs a QP solver (IBodyWorld.CreateQpSolver)");
                int a0 = (_legs[0].Side < 0) == (tuning.qp_leg_a_left > 0.5) ? 0 : 1;
                _qpDof = new bool[nd]; _qpSwingDof = new bool[nd]; _drvK = new double[nd]; _drvC = new double[nd]; _drvT = new double[nd]; _drvV = new double[nd]; _qpPdSwing = tuning.qp_pd_swing; _qpPdRef = (int)tuning.qp_pd_ref; _qpDiag = tuning.qp_diag > 0.5; _qpFoot = tuning.qp_foot > 0.5 && tuning.qp_plan > 0.5; _qpFootDrive = tuning.qp_foot_drive > 0.5; _qpPd = tuning.qp_pd_scale; _qpEngageFrac = tuning.qp_engage_speed_frac; _qpVRef = qpReference.rows[0].v;
                // a hand-off start can begin at either foot's touchdown: one driver per assignment of planar leg A / B (the second one only then); the walking-start mode builds just the configured one
                _qpDrv = new WholeBodyQpDriver[2]; _qpFoots = new int[2]; _qpLegs = new Leg[2][]; _qpLat = tuning.qp_lateral > 0.5;
                // step 9.5b ladder (qp_ladder): one pair of drivers per @v2 row (rows sorted by Froude, the walk climbs them); otherwise the single row 0
                int nRows = tuning.qp_ladder > 0.5 && _qpEngageFrac > 0 ? qpReference.rows.Length : 1; _qpRowDrv = new WholeBodyQpDriver[nRows][]; _qpRowV = new double[nRows];
                for (int row = nRows - 1; row >= 0; row--)
                {
                _qpRowV[row] = qpReference.rows[row].v;
                for (int k = 0; k < (_qpEngageFrac > 0 ? 2 : 1); k++)
                {
                    int a = k == 0 ? a0 : 1 - a0; var la = _legs[a]; var lb = _legs[1 - a];
                    var dofs = new[] { la.Hip[0], la.Knee, la.Ankle[0], lb.Hip[0], lb.Knee, lb.Ankle[0] };
                    foreach (int d in dofs) _qpDof[d] = true;
                    _qpDrv[k] = new WholeBodyQpDriver(body, tuning, qpReference.rows[row], qpSolver, dofs, new[] { la.Foot, lb.Foot }, dt); _qpFoots[k] = lb.Foot; _qpLegs[k] = new[] { la, lb };
                }
                _qpRowDrv[row] = new[] { _qpDrv[0], _qpDrv[1] };
                }
                _qp = _qpDrv[0]; _qpFootB = _qpFoots[0];
                _qpMatch = tuning.qp_match > 0.5 && _qpEngageFrac > 0; if (_qpMatch) BuildMatchFeatures();
            }
        }

        /// <summary>True when this walk tuning asks for the imp = 3 whole-body QP (imp &gt;= 2.5); only then is a QP driver built.</summary>
        public static bool QpWanted(WalkTuning t) => t.imp >= GaitReferenceData.QpImp;
        public bool QpActive => _qp != null;
        Leg[][] _qpLegs; Leg[] _qpLegNow; bool _qpLat; WholeBodyQpDriver[][] _qpRowDrv; double[] _qpRowV; int _qpRow; WholeBodyQpDriver _qp; WholeBodyQpDriver[] _qpDrv; int[] _qpFoots; readonly bool[] _qpDof; readonly double _qpPd = 1.0; double _qpTime; double _qpScale = 1.0; bool _qpEngaged; readonly bool[] _qpPrevContact = new bool[2]; int _qpFootB; double _qpVRef, _qpEngageFrac;
        /// <summary>Joint drive write; the sagittal leg DOFs of the imp = 3 QP run on the reduced PD gains (qp_pd_scale on stiffness, its square root on damping) around the feed-forward the QP writes after this.</summary>
        void Drive(int dof, double target, double velocity, double stiffness, double damping)
        {
            if (_qpEngaged && _qpDof[dof]) { double sc = _qpPdSwing >= 0 && _qpSwingDof[dof] ? _qpPdSwing : _qpPd; stiffness *= sc; damping *= Math.Sqrt(sc); _drvK[dof] = stiffness; _drvC[dof] = damping; _drvT[dof] = target; _drvV[dof] = velocity; }
            _b.SetDofDrive(dof, target, velocity, stiffness, damping);
        }
        // step 9.5k (qp_pd_swing, qp_pd_ref, qp_diag): last PD of each QP DOF (scaled gains, target, rate); the swing leg of the QP (its parity) as of the previous tick
        // round 6 (qp_foot): sole-orientation task, ankle drive target, ankle saturation counters
        double _qpPdSwing = -1; int _qpPdRef; bool _qpDiag, _qpFoot, _qpFootDrive; long _qpAnkSatSwing, _qpAnkSatStance; double[] _drvK, _drvC, _drvT, _drvV; bool[] _qpSwingDof;

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
            double stepTime = _lim.StepTime(Math.Max(_cmd.Speed, _t.reach_speed_floor_frac * _lim.VCruise));
            _cmd = _limiter.Update(intent, _com2, _pelvisYaw, _vel2.Length(), StepCapFrac * _l / stepTime);
            StateMachine(intent);
            WriteDrives();
            if (_qp != null) ApplyQp();
            Report(step);
        }

        readonly double[] _armTrim = new double[2];   // step 9.5e: shoulder flexion trim (rad) that servos the yaw momentum of legs and arms to zero (qp_arm_yaw); zero otherwise
        void QpArmTrim()
        {
            double kappa = _t.qp_arm_yaw; if (kappa == 0 || _qp == null) return;
            double lrest = _qp.Controller.YawLog[4], lim = _t.target_velocity_max_rps;
            for (int arm = 0; arm < 2; arm++)
            {
                int d = _armShoulderFlex[arm]; if (d < 0) continue;
                var info = _b.GetDof(d); _b.GetLink(info.ChildLink, out var child); Vector3 axis = _b.DofAxisWorld(d), r0 = child.Position; double sens = 0;
                foreach (int link in _descendants[d]) { _b.GetLink(link, out var sl); Vector3 v = Vector3.Cross(axis, sl.Com - r0), rc = sl.Com - _com; sens += _b.LinkMassKg(link) * (rc.Z * v.X - rc.X * v.Z); }
                if (Math.Abs(sens) < 1e6) continue;
                double rate = Math.Max(-lim, Math.Min(lim, -kappa * lrest / (2 * sens)));
                double tr = _armTrim[arm] + (rate - _armTrim[arm] / Math.Max(_t.qp_arm_leak_s, 1e-3)) * _dt;
                _armTrim[arm] = Math.Max(-_t.qp_arm_trim_max, Math.Min(_t.qp_arm_trim_max, tr));
            }
        }

        // step 9.5f (qp_arm_ff): the shoulders cancel the yaw momentum of the legs by feed-forward (replacing the imp 2 arm reference, which runs at the legacy phase and amplitude, not the QP row's stride)
        readonly double[] _armFf = new double[2], _armFfRate = new double[2], _armFfQdd = new double[2]; double _lLegsF, _lLegsMean, _lLegsRefLog, _armFfBlend; bool _armFfOn;
        double YawSens(int d)
        {
            var info = _b.GetDof(d); _b.GetLink(info.ChildLink, out var child); Vector3 axis = _b.DofAxisWorld(d), r0 = child.Position; double sens = 0;
            foreach (int link in _descendants[d]) { _b.GetLink(link, out var sl); Vector3 v = Vector3.Cross(axis, sl.Com - r0), rc = sl.Com - _com; sens += _b.LinkMassKg(link) * (rc.Z * v.X - rc.X * v.Z); }
            return sens;
        }
        /// <summary>Yaw momentum of the legs about the CoM predicted from the QP row's own reference joint velocities (sum over hip, knee, ankle of d(L_y)/d(rate) x reference rate), phase-locked to the row's stride with no measurement lag.</summary>
        double LegsYawFromReference()
        {
            var c = _qp.Controller; double L = 0; var legs = _qpLegNow ?? _qpLegs[0];
            for (int k = 0; k < 2; k++)
            {
                var leg = legs[k]; int a = 3 + 3 * k, b = a + 1, f = a + 2; double qth = c.RefQd(2);
                L += YawSens(leg.Hip[0]) * (c.RefQd(a) + qth) + YawSens(leg.Knee) * (c.RefQd(a) - c.RefQd(b)) + YawSens(leg.Ankle[0]) * (c.RefQd(f) + c.RefQd(b));
            }
            return L;
        }
        // step 9.5h (qp_arm_ref): the shoulders track the reference arm swing of the active QP row (3D-aware stride, 9.5g); arm 0 = left. The row's arm A / B is the arm on the lateral side of its leg A / B.
        readonly double[] _armRef = new double[2], _armRefQdd = new double[2]; double _armRefBlend; bool _armRefOn;
        void QpArmRef()
        {
            if (_t.qp_arm_ref < 0.5 || _qp == null || _qpLegNow == null) return;
            var c = _qp.Controller; double pred = 0;
            for (int arm = 0; arm < 2; arm++)
            {
                int d = _armShoulderFlex[arm]; if (d < 0) continue;
                int k = (_qpLegNow[0].Side < 0) == (arm == 0) ? 0 : 1;
                _armRef[arm] = _t.qp_arm_ref_gain * c.RefArm(k); _armRefQdd[arm] = _t.qp_arm_ref_gain * c.RefArmQdd(k);
                pred += YawSens(d) * _armRefQdd[arm];
            }
            c.DLArmRefPred = pred;
            _armRefOn = true; _armRefBlend = Math.Min(1.0, _armRefBlend + _dt / Math.Max(_t.qp_arm_ref_blend_s, 1e-3));
        }
        void QpArmFf()
        {
            if (_t.qp_arm_ff < 0.5 || _qp == null) return;
            double lim = _t.target_velocity_max_rps, lmeas = _qp.Controller.YawLog[5], wr = Math.Max(0, Math.Min(1.0, _t.qp_arm_ff_ref_w));
            double lref = LegsYawFromReference(); _lLegsRefLog = lref; double lleg = wr > 0 ? (1 - wr) * lmeas + wr * lref : lmeas;
            if (_t.qp_arm_ff_hp_s > 0) { _lLegsMean += Math.Min(1.0, _dt / _t.qp_arm_ff_hp_s) * (lleg - _lLegsMean); lleg -= _lLegsMean; }   // high-pass: the mean of the legs' yaw momentum is no cancellation target (it integrates the arms to their limit)
            _lLegsF += Math.Max(0, Math.Min(1.0, _t.qp_yaw_ff_alpha)) * (lleg - _lLegsF);
            for (int arm = 0; arm < 2; arm++)
            {
                int d = _armShoulderFlex[arm]; if (d < 0) continue;
                double sens = YawSens(d); if (Math.Abs(sens) < 1e6) continue;
                double rate = Math.Max(-lim, Math.Min(lim, -_t.qp_arm_ff_gain * _lLegsF / (2 * sens) - _armFf[arm] / Math.Max(_t.qp_arm_ff_leak_s, 1e-3)));
                double qdd = (rate - _armFfRate[arm]) / _dt; _armFfQdd[arm] += 0.5 * (Math.Max(-_t.swing_ff_accel_max, Math.Min(_t.swing_ff_accel_max, qdd)) - _armFfQdd[arm]); _armFfRate[arm] = rate;
                _armFf[arm] = Math.Max(-_t.qp_arm_ff_max, Math.Min(_t.qp_arm_ff_max, _armFf[arm] + rate * _dt));
            }
            _armFfOn = true; _armFfBlend = Math.Min(1.0, _armFfBlend + _dt / 0.5);
        }

        // ---- step 9.5c: state-matched hand-off ----
        bool _qpPlanTd; double _qpStepX, _qpStepT = -1, _qpClock; bool _qpMatch; double[][][] _mRef; bool[][] _mSs; int _qpTdSinceRow; int _qpMatchTds; readonly double[] _mMeas = new double[9];
        double[] _mScale;
        void BuildMatchFeatures()
        {
            double l = _qpRowDrv[0][0].Controller.Reference.LegLength, sg = Math.Sqrt(_g * l);
            _mScale = new double[9]; for (int i = 0; i < 9; i++) _mScale[i] = i == 2 || i == 3 ? _t.qp_match_vel_scale * sg : i == 4 ? _t.qp_match_pitch_scale : _t.qp_match_len_scale * l;
            _mRef = new double[_qpRowDrv.Length][][]; _mSs = new bool[_qpRowDrv.Length][];
            for (int r = 0; r < _qpRowDrv.Length; r++)
            {
                var rf = _qpRowDrv[r][0].Controller.Reference; int n = rf.N; _mRef[r] = new double[n][]; _mSs[r] = new bool[n]; double wRef = rf.BodyMassKg * _g;
                for (int k = 0; k < n; k++)
                {
                    double bh = rf.Node(QpReference.Cpts + 4, k);
                    _mSs[r][k] = rf.Node(QpReference.Cf + 1, k) + rf.Node(QpReference.Cf + 3, k) < _t.qp_match_load_lo * wRef && rf.Node(QpReference.Cf + 5, k) + rf.Node(QpReference.Cf + 7, k) > _t.qp_match_load_hi * wRef;
                    _mRef[r][k] = new[] { rf.Node(QpReference.Ccom, k) - bh, rf.Node(QpReference.Ccom + 1, k), rf.Node(QpReference.Ccomd, k), rf.Node(QpReference.Ccomd + 1, k), rf.Node(QpReference.Cq + 2, k),
                        rf.Node(QpReference.Cpts, k) - bh, rf.Node(QpReference.Cpts + 1, k), rf.Node(QpReference.Cpts + 2, k) - bh, rf.Node(QpReference.Cpts + 3, k) };
                }
            }
        }
        /// <summary>Single support with the loaded leg (>= 85 % of the weight, the other leg <= 5 %) as planar leg B; k = the driver index of that assignment.</summary>
        bool SsConfig(out int k)
        {
            k = _qpLegs[0][1] == _legs[_stance] ? 0 : 1; double wN = _mass * _g;
            return _phase == LocoPhase.SingleSupport && _qpLegs[k][1].State.NormalForceN >= _t.qp_match_load_hi * wN && _qpLegs[k][0].State.NormalForceN <= _t.qp_match_load_lo * wN;
        }
        /// <summary>The 9 measured match features for a landed leg B (index into the _qpLegs pair) in the heading frame (forward distances relative to the landed heel, heights above ground).</summary>
        void MeasureMatch(int tdk)
        {
            var legs = _qpLegs[tdk]; var la = legs[0]; var lb = legs[1]; var fw = Fwd(_pelvisYaw); var fw3 = new Vector3(fw.X, 0, fw.Y);
            Vector3 up = Vector3.Transform(Vector3.UnitY, _pelvisState.Rotation); double th = Math.Atan2(Vector3.Dot(up, fw3), up.Y);
            Vector3 Pt(Leg lg, double zoff) => lg.FootLinkState.Position + Vector3.Transform(new Vector3(0f, (float)-_ankleHeight, (float)zoff), lg.FootLinkState.Rotation);
            Vector3 bh = Pt(lb, _heelOff), ah = Pt(la, _heelOff), at = Pt(la, _toeOff); double bx = Vector3.Dot(bh, fw3);
            _mMeas[0] = Vector3.Dot(_com, fw3) - bx; _mMeas[1] = _com.Y - _groundY; _mMeas[2] = Vector3.Dot(_comVel, fw3); _mMeas[3] = _comVel.Y; _mMeas[4] = th;
            _mMeas[5] = Vector3.Dot(ah, fw3) - bx; _mMeas[6] = ah.Y - _groundY; _mMeas[7] = Vector3.Dot(at, fw3) - bx; _mMeas[8] = at.Y - _groundY;
        }
        /// <summary>Best row (within rows r0..r1) and node of the measured state: distance = RMS of the scaled feature differences; zmax = largest scaled difference of that best node.</summary>
        double BestMatch(int r0, int r1, bool ssOnly, out int row, out int node, out double zmax, int kMax = int.MaxValue)
        {
            double best = double.MaxValue; row = r0; node = 0; zmax = 0;
            for (int r = r0; r <= r1; r++)
                for (int k = 0; k < Math.Min(kMax, _mRef[r].Length); k++)
                {
                    if (ssOnly && !_mSs[r][k]) continue;
                    double ss = 0, zm = 0; var rf = _mRef[r][k];
                    for (int i = 0; i < 9; i++) { double z = (_mMeas[i] - rf[i]) / _mScale[i]; ss += z * z; if (Math.Abs(z) > zm) zm = Math.Abs(z); }
                    double d = Math.Sqrt(ss / 9); if (d < best) { best = d; row = r; node = k; zmax = zm; }
                }
            return best;
        }

        /// <summary>Step 9.5i (qp_td_rematch): after a QP touchdown, the measured state against the active row; when node 0 (where the re-anchor put the clock) is farther than qp_td_rematch, restart the row at its best node anchored on the landed heel (as the ladder climb).</summary>
        void QpTouchdownRematch()
        {
            var c = _qp.Controller; var landed = (_qpLegNow ?? _qpLegs[0])[1 - c.Parity]; int tdk = _qpLegs[0][1] == landed ? 0 : 1;
            MeasureMatch(tdk); var r0 = _mRef[_qpRow][0]; double ss = 0;
            for (int i = 0; i < 9; i++) { double z = (_mMeas[i] - r0[i]) / _mScale[i]; ss += z * z; }
            double d0 = Math.Sqrt(ss / 9), dm = BestMatch(_qpRow, _qpRow, false, out int mr, out int mk, out double zm, (int)(_t.qp_td_min_frac * _mRef[_qpRow].Length)); _qpMatchTds++;
            _dbg.QpMatchTd = true; _dbg.QpMatchD = d0; _dbg.QpMatchZ = dm; _dbg.QpMatchRow = mr; _dbg.QpMatchNode = mk; _dbg.QpMatchTds = _qpMatchTds;
            if (d0 <= _t.qp_td_rematch || mk == 0 || dm >= d0) return;
            _qp = _qpRowDrv[_qpRow][tdk]; _qpLegNow = _qpLegs[tdk]; _qp.Reset(true, mk); _qpTime = mk * _qp.Controller.Reference.H;
        }

        /// <summary>imp = 3: while walking (not Stand) the QP torques replace the feed-forward of the six sagittal leg DOFs (last write wins); the QP clock restarts when walking starts.</summary>
        void ApplyQp()
        {
            _dbg.QpMatchTd = false;
            if (_phase == LocoPhase.Stand) { _qpEngaged = false; _dbg.QpOn = false; return; }
            if (!_qpEngaged)
            {
                // step 9.5: with qp_engage_speed_frac > 0 the imp 2 walk runs first; the QP takes over at the first touchdown of planar leg B (the reference starts at the heel strike of B) once the CoM speed reaches the fraction of the reference speed
                bool td = false; int tdk = 0;
                if (_qpEngageFrac > 0) for (int k = 0; k < 2; k++) { _b.GetFoot(_qpFoots[k], out var fk); if (fk.Contact && !_qpPrevContact[k]) { td = true; tdk = k; } _qpPrevContact[k] = fk.Contact; }
                bool go = _qpEngageFrac <= 0 || (td && (_phase == LocoPhase.SingleSupport || _phase == LocoPhase.DoubleSupport) && Vector2.Dot(_vel2, Fwd(_pelvisYaw)) >= _qpEngageFrac * _qpVRef);
                int startNode = 0;
                if (_qpMatch)
                {
                    // step 9.5c: hand off only when the measured state is on a reference row (best row / node, RMS of the scaled differences <= qp_match_thr)
                    go = false;
                    bool ssMode = _t.qp_match_ss > 0.5, ssOk = false;
                    if (ssMode && _phase == LocoPhase.SingleSupport)
                    {
                        // single-support start: the loaded leg is planar leg B, the swing leg A; only nodes where the reference has the same contact set are candidates
                        ssOk = SsConfig(out tdk);
                    }
                    if (ssMode ? ssOk : td && (_phase == LocoPhase.SingleSupport || _phase == LocoPhase.DoubleSupport))
                    {
                        int rowCap = _qpRowDrv.Length - 1;
                        if (_t.qp_plan > 0.5) { double vfe = Vector2.Dot(_vel2, Fwd(_pelvisYaw)); rowCap = 0; while (rowCap + 1 < _qpRowDrv.Length && vfe >= _t.qp_plan_up * _qpRowV[rowCap + 1]) rowCap++; }   // qp_plan: hand-off row by speed
                        MeasureMatch(tdk); _qpMatchTds++; double dm = BestMatch(0, rowCap, ssMode, out int mr, out int mk, out double zm);
                        _dbg.QpMatchTd = true; _dbg.QpMatchD = dm; _dbg.QpMatchZ = zm; _dbg.QpMatchRow = mr; _dbg.QpMatchNode = mk; _dbg.QpMatchTds = _qpMatchTds;
                        if (dm <= _t.qp_match_thr && zm <= _t.qp_match_zmax) { go = true; _qpRow = mr; startNode = mk; _qpTdSinceRow = 0; }
                    }
                    if (go) { _qp = _qpRowDrv[_qpRow][tdk]; _qpLegNow = _qpLegs[tdk]; _qpVRef = _qpRowV[_qpRow]; }
                }
                else if (go)
                {
                    _qpRow = 0; double vf = Vector2.Dot(_vel2, Fwd(_pelvisYaw));
                    while (_qpRow + 1 < _qpRowDrv.Length && vf >= _t.qp_ladder_up * _qpRowV[_qpRow + 1]) _qpRow++;   // ladder: the fastest row the speed already reaches
                    _qp = _qpRowDrv[_qpRow][tdk]; _qpLegNow = _qpLegs[tdk]; _qpVRef = _qpRowV[_qpRow];
                }
                if (!go) return;
                _qp.Reset(_qpEngageFrac > 0, startNode); _qpTime = startNode * _qp.Controller.Reference.H; _qpEngaged = true; _qpScale = Math.Min(1.0, Math.Max(_t.qp_scale_min, Vector2.Dot(_vel2, Fwd(_pelvisYaw)) / _qpVRef));
            }
            else if (_qpRowDrv.Length > 1)
            {
                // ladder: at a touchdown, one row up once the forward speed reaches qp_ladder_up x the next row's speed; the new row's QP starts at its node 0 anchored on the landed heel
                bool td = false; int tdk = 0;
                for (int k = 0; k < 2; k++) { _b.GetFoot(_qpFoots[k], out var fk); if (fk.Contact && !_qpPrevContact[k]) { td = true; tdk = k; } _qpPrevContact[k] = fk.Contact; }
                if (td) _qpTdSinceRow++;
                int upNode = 0, upRow = _qpRow + 1; bool up = false;
                if (_t.qp_plan > 0.5)
                {
                    // step 6a: climb at a QP touchdown (the previous tick's re-anchor of the active row), speed-gated, to node 0 of the next row anchored on the landed heel
                    int maxRowP = _t.qp_match_max_row < 0 ? _qpRowDrv.Length - 1 : Math.Min(_qpRowDrv.Length - 1, (int)_t.qp_match_max_row);
                    if (_t.qp_plan_ladder > 0.5)
                    {
                        // step 6b: the row follows the mean speed of the last step (the next row up is the commanded speed); hysteresis qp_plan_vhyst, one row per step, at a QP touchdown, node 0
                        if (_qpPlanTd)
                        {
                            var fw2 = Fwd(_pelvisYaw); double xc = Vector3.Dot(_com, new Vector3(fw2.X, 0, fw2.Y));
                            if (_qpStepT >= 0)
                            {
                                double vbar = (xc - _qpStepX) / Math.Max(_qpClock - _qpStepT, 1e-3), hy = _t.qp_plan_vhyst; int nr = _qpRow;
                                if (_qpRow + 1 <= maxRowP && vbar >= _qpRowV[_qpRow] + hy) nr = _qpRow + 1; else if (_qpRow > 0 && vbar < _qpRowV[_qpRow - 1] - hy) nr = _qpRow - 1;
                                _dbg.QpMatchD = vbar;
                                if (nr != _qpRow) { var landed = (_qpLegNow ?? _qpLegs[0])[1 - _qp.Controller.Parity]; tdk = _qpLegs[0][1] == landed ? 0 : 1; up = true; upNode = 0; upRow = nr; }
                            }
                            _qpStepX = xc; _qpStepT = _qpClock;
                        }
                    }
                    else if (_qpPlanTd && _qpRow + 1 <= maxRowP && Vector2.Dot(_vel2, Fwd(_pelvisYaw)) >= _t.qp_plan_up * _qpRowV[_qpRow + 1])
                    {
                        var landed = (_qpLegNow ?? _qpLegs[0])[1 - _qp.Controller.Parity]; tdk = _qpLegs[0][1] == landed ? 0 : 1; up = true; upNode = 0;
                    }
                    _qpPlanTd = false;
                }
                else if (_qpMatch)
                {
                    // step 9.5c: climb to the next row (at most qp_match_max_row) once the QP has re-anchored on qp_match_hold touchdowns of its own on this row, from a single-support state that matches a node of that row (qp_match_ss) or at a touchdown
                    int maxRow = _t.qp_match_max_row < 0 ? _qpRowDrv.Length - 1 : Math.Min(_qpRowDrv.Length - 1, (int)_t.qp_match_max_row);
                    bool ssMode = _t.qp_match_ss > 0.5 && _t.qp_match_climb_td < 0.5, ok = false; int ck = tdk;
                    if (_t.qp_match_climb_td > 0.5) ok = _qpRow + 1 <= maxRow && td && _qpTdSinceRow >= _t.qp_match_hold;   // 9.5c cycle 2 rule: touchdown events (contact chatter counts) on the row, any node of the next row
                    else if (_qpRow + 1 <= maxRow && _qp.Controller.Touchdowns >= _t.qp_match_hold) ok = ssMode ? SsConfig(out ck) : td;
                    if (ok)
                    {
                        tdk = ck; MeasureMatch(tdk); double dm = BestMatch(_qpRow + 1, _qpRow + 1, ssMode, out int mr, out int mk, out double zm);
                        _dbg.QpMatchTd = true; _dbg.QpMatchD = dm; _dbg.QpMatchZ = zm; _dbg.QpMatchRow = mr; _dbg.QpMatchNode = mk; _dbg.QpMatchTds = _qpMatchTds;
                        if (dm <= _t.qp_match_up_thr && zm <= _t.qp_match_up_zmax) { up = true; upNode = mk; }
                    }
                }
                else up = td && _qpRow + 1 < _qpRowDrv.Length && Vector2.Dot(_vel2, Fwd(_pelvisYaw)) >= _t.qp_ladder_up * _qpRowV[_qpRow + 1];
                if (up)
                {
                    _qpRow = upRow; _qp = _qpRowDrv[_qpRow][tdk]; _qpLegNow = _qpLegs[tdk]; _qpVRef = _qpRowV[_qpRow];
                    _qp.Reset(true, upNode); _qpTime = upNode * _qp.Controller.Reference.H; _qpTdSinceRow = 0; _qpScale = Math.Min(1.0, Math.Max(_t.qp_scale_min, Vector2.Dot(_vel2, Fwd(_pelvisYaw)) / _qpVRef));
                }
            }
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp(); _qpClock += _dt;
            if (_t.qp_scale_min < 1.0)
            {
                double st = Math.Min(1.0, Math.Max(_t.qp_scale_min, Vector2.Dot(_vel2, Fwd(_pelvisYaw)) / _qpVRef));
                _qpScale += (st - _qpScale) * Math.Min(1.0, _dt / _t.qp_scale_tau);
            }
            _qp.TimeScale = _qpScale; _qp.DesiredYaw = _cmd.Heading;
            int tdBefore = _qp.Controller.Touchdowns;
            _qp.Apply(_qpTime, _pelvisYaw, _groundY); _qpTime += _dt * _qpScale; _qpPlanTd = _t.qp_plan > 0.5 && _qp.Controller.Schedule.LastCallReanchored;
            if (_qpPdSwing >= 0 || _qpPdRef > 0 || _qpDiag || _qpFoot || _t.qp_r11_pdq > 0) QpSwingPd();
            if (_qpMatch && _t.qp_td_rematch > 0 && _t.qp_plan < 0.5 && _qp.Controller.Touchdowns > tdBefore) QpTouchdownRematch();
            if (_qpLat) QpLateralDrives();
            var c = _qp.Controller; _dbg.QpOn = true; _dbg.QpScale = _qpScale; _dbg.QpMs = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1e3 / System.Diagnostics.Stopwatch.Frequency; _dbg.QpIters = c.LastIterations; _dbg.QpTau ??= new double[6]; System.Array.Copy(c.Tau, _dbg.QpTau, 6);
            _dbg.QpRow = _qpRow;
            if (_qpLat) { _dbg.QpLat ??= new double[6]; System.Array.Copy(c.LatCmd, _dbg.QpLat, 6); }
            if (_qpLat && c.TrunkYaw) { QpArmTrim(); QpArmFf(); QpArmRef(); _dbg.QpYaw ??= new double[19]; System.Array.Copy(c.YawLog, _dbg.QpYaw, 5); _dbg.QpYaw[5] = _armTrim[0]; _dbg.QpYaw[6] = _armTrim[1]; _dbg.QpYaw[7] = c.YawLog[5]; _dbg.QpYaw[8] = c.YawLog[6]; _dbg.QpYaw[9] = c.YawShareLog; _dbg.QpYaw[10] = _armFf[0]; _dbg.QpYaw[11] = _armFf[1]; _dbg.QpYaw[12] = _lLegsRefLog; _dbg.QpYaw[13] = _armRef[0]; _dbg.QpYaw[14] = _armRef[1]; _dbg.QpYaw[15] = _armShoulderFlex[0] >= 0 ? _b.DofPosition(_armShoulderFlex[0]) : 0; _dbg.QpYaw[16] = _armShoulderFlex[1] >= 0 ? _b.DofPosition(_armShoulderFlex[1]) : 0; _dbg.QpYaw[17] = c.DLArmRefPred; _dbg.QpYaw[18] = c.DLArms; }
            if (_t.qp_rom > 0.5 || _t.qp_z_hold_ss > 0) { _dbg.QpJ ??= new double[6]; _dbg.QpJ[0] = c.NS; _dbg.QpJ[1] = c.RomRows; _dbg.QpJ[2] = c.RomActive; _dbg.QpJ[3] = c.RomRowsDropped; _dbg.QpJ[4] = c.FpScale[0]; _dbg.QpJ[5] = c.FpScale[1]; }   // step 9.5j log
            _dbg.QpStatus = (int)c.LastStatus; _dbg.QpFailures = c.SolveFailures; _dbg.QpMassScale = c.MassScale; _dbg.QpFrictionCmd = c.FrictionRatioCmd;
        }

        /// <summary>Step 9.5k: marks the swing leg of the QP (parity 0 = planar leg A) for the next tick's PD scale (qp_pd_swing); qp_pd_ref re-issues the PD target / rate of the swing (1) or both (2) legs from the QP reference joint angles / rates with the scaled gains of this tick; qp_diag fills the qp.k log block.</summary>
        void QpSwingPd()
        {
            var c = _qp.Controller; var legs = _qpLegNow ?? _qpLegs[0]; int sw = c.Parity;
            for (int pl = 0; pl < 2; pl++)
            {
                var leg = legs[pl]; int[] d3 = { leg.Hip[0], leg.Knee, leg.Ankle[0] }; int a = 3 + 3 * pl, b = a + 1, f = a + 2;
                for (int j = 0; j < 3; j++) _qpSwingDof[d3[j]] = pl == sw;
                if (_qpPdRef > 0 && (pl == sw || _qpPdRef > 1))
                {
                    double th = c.RefQ(2), thd = c.RefQd(2);
                    double[] tg = { c.RefQ(a) + th, c.RefQ(a) - c.RefQ(b), c.RefQ(f) + c.RefQ(b) };
                    double[] vl = { c.RefQd(a) + thd, c.RefQd(a) - c.RefQd(b), c.RefQd(f) + c.RefQd(b) };
                    for (int j = 0; j < 3; j++)
                    {
                        int d = d3[j]; var info = _b.GetDof(d); double t = Math.Max(info.LowerRad, Math.Min(info.UpperRad, tg[j]));
                        if (_drvK[d] <= 0 && _drvC[d] <= 0) continue;
                        _drvT[d] = t; _drvV[d] = vl[j]; _b.SetDofDrive(d, t, vl[j], _drvK[d], _drvC[d]);
                    }
                }
            }
            if (_qpFoot)
            {
                // round 6: the ankle PD target = measured shank pitch + the commanded sole pitch (the stiff stance ankle then holds the sole level instead of the reference ankle angle); ankle authority: applied torque / force limit
                for (int pl = 0; pl < 2; pl++)
                {
                    var leg = legs[pl]; int d = leg.Ankle[0]; var info = _b.GetDof(d); int a = 3 + 3 * pl;
                    if (_qpFootDrive && (_drvK[d] > 0 || _drvC[d] > 0))
                    {
                        double tg = Math.Max(info.LowerRad, Math.Min(info.UpperRad, c.Q[a + 1] + c.FootCmd[pl])), vl = c.Qd[a + 1];
                        _drvT[d] = tg; _drvV[d] = vl; _b.SetDofDrive(d, tg, vl, _drvK[d], _drvC[d]);
                    }
                    double ratio = _b.DofAppliedTorque(d) / Math.Max(info.ForceLimitNm, 1.0); c.FootLog[11 + pl] = ratio;
                    if (Math.Abs(ratio) > _t.qp_foot_sat) { if (pl == c.Parity) _qpAnkSatSwing++; else _qpAnkSatStance++; }
                }
            }
            if (_t.qp_r11_pdq > 0)
            {
                // round 11 (qp_r11_pdq = b): the PD of the six sagittal leg DOFs tracks the state the QP intends one tick ahead (implicit drive: target q + qd dt + qdd* dt^2 / 2, rate qd + qdd* dt),
                // blended b with the legacy target, so it corrects deviations from the QP's motion instead of pulling to the legacy imp-2 plan; qp_r11_pdq_k scales its gains.
                double bq = Math.Min(1.0, _t.qp_r11_pdq), kq = Math.Max(0, _t.qp_r11_pdq_k), th2 = c.SolQdd(2);
                int pm = (int)Math.Round(_t.qp_r11_pdq_mode); var inc = c.Schedule.Inc; bool ds = inc[2 * sw] || inc[2 * sw + 1];
                for (int pl = 0; pl < 2; pl++)
                {
                    if (pm == 1 && pl == sw) continue;            // stance leg only
                    if (pm == 2 && !ds) continue;                 // both legs, only while the swing-parity leg still has contact (double support)
                    if (pm == 3 && pl == sw && !ds) continue;     // all but the airborne swing leg
                    var leg = legs[pl]; int[] d3 = { leg.Hip[0], leg.Knee, leg.Ankle[0] }; int a = 3 + 3 * pl, b = a + 1, f = a + 2;
                    double[] acc = { c.SolQdd(a) + th2, c.SolQdd(a) - c.SolQdd(b), c.SolQdd(f) + c.SolQdd(b) };
                    for (int j = 0; j < 3; j++)
                    {
                        int d = d3[j]; if (_drvK[d] <= 0 && _drvC[d] <= 0) continue;
                        var info = _b.GetDof(d); double q = _b.DofPosition(d), qd = _b.DofVelocity(d), aj = double.IsNaN(acc[j]) || double.IsInfinity(acc[j]) ? 0 : acc[j];
                        double tq = q + qd * _dt + 0.5 * aj * _dt * _dt, vq = qd + aj * _dt;
                        double tg = Math.Max(info.LowerRad, Math.Min(info.UpperRad, (1 - bq) * _drvT[d] + bq * tq)), vl = (1 - bq) * _drvV[d] + bq * vq;
                        _drvT[d] = tg; _drvV[d] = vl; _b.SetDofDrive(d, tg, vl, _drvK[d] * kq, _drvC[d] * Math.Sqrt(kq));   // the qp.k PD log keeps the unscaled gains
                    }
                }
            }
            if (_qpDiag)
            {
                _dbg.QpK ??= new double[_qpFoot ? (_t.qp_r10_diag > 0.5 ? 70 : 53) : _t.qp_plan > 0.5 ? 38 : 22];
                for (int pl = 0; pl < 2; pl++)
                {
                    var leg = legs[pl]; int[] d3 = { leg.Hip[0], leg.Knee, leg.Ankle[0] };
                    for (int j = 0; j < 3; j++)
                    {
                        int d = d3[j]; var info = _b.GetDof(d); double pd = _drvK[d] * (_drvT[d] - _b.DofPosition(d)) + _drvC[d] * (_drvV[d] - _b.DofVelocity(d)), room = Math.Max(0, info.ForceLimitNm - Math.Abs(c.Tau[3 * pl + j]));
                        _dbg.QpK[3 * pl + j] = Math.Max(-room, Math.Min(room, pd));   // PD torque the drive can add on top of the QP torque (the drive force limit is the room left by |ff|)
                    }
                }
                for (int i = 0; i < 16; i++) _dbg.QpK[6 + i] = c.Diag[i];
                if (_t.qp_plan > 0.5) for (int i = 0; i < 16; i++) _dbg.QpK[22 + i] = c.PlanDiag[i];
                if (_qpFoot) for (int i = 0; i < 15; i++) _dbg.QpK[38 + i] = c.FootLog[i];
                if (_qpFoot && _t.qp_r10_diag > 0.5) { _dbg.QpK[53] = c.PlanDiag[16]; _dbg.QpK[54] = c.PlanDiag[17]; for (int i = 0; i < 14; i++) _dbg.QpK[55 + i] = c.R10Log[i]; _dbg.QpK[69] = c.R9GateHeld; }   // round 10: 53,54 = PlanDiag[16..17]; 55..68 = R10Log[0..13]; 69 = seconds the lift-off was held by the gate
            }
        }

        /// <summary>Step 9.5b (stage 2): the stance hip yaw / roll and ankle roll / yaw DOFs of a leg the QP loads take the torque that carries the QP's 3D corner forces (tau = -axis . sum (p_c - r0) x f_c, plus the weight of the distal links), plus the trunk-attitude hip torque; replaces the legacy virtual-force term for those DOFs.</summary>
        void QpLateralDrives()
        {
            var drv = _qp; if (_qpLegNow == null) _qpLegNow = _qpLegs[0];
            for (int pl = 0; pl < 2; pl++)
            {
                var leg = _qpLegNow[pl]; double fz = 0; for (int k = 0; k < 4; k++) fz += drv.ForceWorld[4 * pl + k].Y;
                if (fz <= 1e-3 * _mass * _g) continue;
                for (int j = 0; j < 4; j++)
                {
                    int d = j == 0 ? leg.Hip[1] : j == 1 ? leg.Hip[2] : j == 2 ? leg.Ankle[1] : leg.Ankle[2];
                    if (d < 0 || _qpDof[d]) continue;
                    var info = _b.GetDof(d); _b.GetLink(info.ChildLink, out var child);
                    Vector3 r0 = child.Position, moment = Vector3.Zero;
                    foreach (int link in _descendants[d]) { _b.GetLink(link, out var sl); moment += Vector3.Cross(sl.Com - r0, new Vector3(0, -(float)(_b.LinkMassKg(link) * _g), 0)); }
                    for (int k = 0; k < 4; k++) moment += Vector3.Cross(drv.CornerWorld[4 * pl + k] - r0, drv.ForceWorld[4 * pl + k]);
                    double tau = -Vector3.Dot(_b.DofAxisWorld(d), moment) + (j < 2 ? Vector3.Dot(_b.DofAxisWorld(d), new Vector3(leg.HipExtra.X, 0, leg.HipExtra.Z)) : 0), lim = info.ForceLimitNm;   // the QP's yaw row owns the whole-body yaw: the vertical part of the trunk-attitude torque (and its grip clamp) is left out
                    _b.SetDofFeedForward(d, Math.Max(-lim, Math.Min(lim, tau)));
                }
            }
        }

        /// <summary>The step length cap (fraction of leg length): the legacy flat-foot cap for imp = 0, the reference gait's own range for imp > 0 (heel-to-toe rollover).</summary>
        double StepCapFrac => _t.imp > 0 && _t.ref_track > 0 ? _t.imp_step_length_max_frac : _t.step_length_max_frac;

        void ImpAngles()
        {
            if (!ImpActive) { _dbg.AngStRef = null; return; }
            _dbg.AngStRef ??= new double[3]; _dbg.AngStMeas ??= new double[3]; _dbg.AngSwRef ??= new double[3]; _dbg.AngSwMeas ??= new double[3];
            const double R2D = 180 / Math.PI;
            for (int k = 0; k < 2; k++)
            {
                var leg = _legs[k == 0 ? _stance : 1 - _stance]; var rl = RefLeg(leg);
                var rf = k == 0 ? _dbg.AngStRef : _dbg.AngSwRef; var ms = k == 0 ? _dbg.AngStMeas : _dbg.AngSwMeas;
                rf[0] = rl.HipFlexion * R2D; rf[1] = rl.Knee * R2D; rf[2] = rl.AnklePlantar * R2D;
                ms[0] = _b.DofPosition(leg.Hip[0]) * R2D; ms[1] = _b.DofPosition(leg.Knee) * R2D; ms[2] = _b.DofPosition(leg.Ankle[0]) * R2D;
            }
            _dbg.RefFroude = _ref.Speed * _ref.Speed / (_g * _l);
            _dbg.RefPhase = StancePhase();
            if (_o2) { _dbg.TauFfSt ??= new double[3]; _dbg.TauFfSw ??= new double[3]; for (int j = 0; j < 3; j++) { _dbg.TauFfSt[j] = O2Tau(_legs[_stance], j); _dbg.TauFfSw[j] = O2Tau(_legs[1 - _stance], j); } }
            else { _dbg.TauFfSt = null; _dbg.TauFfSw = null; }
        }

        // ---- imp = 2 (round 3 step 8.5): the optimised joint torque is the feed-forward of the joint PD ----
        bool _o2; OptimisedSample _o2s;
        OptimisedLeg O2Leg(Leg leg) => leg.Side < 0 ? _o2s.L : _o2s.R;
        /// <summary>Optimised torque of one joint (StanceImpedance.Hip / Knee / Ankle), times imp2_ff_scale and the joint's sign key.</summary>
        double O2Tau(Leg leg, int joint)
        {
            var o = O2Leg(leg);
            double tau = joint == StanceImpedance.Hip ? o.HipTorque * _t.imp2_ff_sign_hip : joint == StanceImpedance.Knee ? o.KneeTorque * _t.imp2_ff_sign_knee : o.AnkleTorque * _t.imp2_ff_sign_ankle;
            return _t.imp2_ff_scale * tau;
        }
        double O2Rate(Leg leg, int joint) { var o = O2Leg(leg); return joint == StanceImpedance.Hip ? o.HipRate : joint == StanceImpedance.Knee ? o.KneeRate : o.AnkleRate; }
        double O2Ff(Leg leg, int joint, int dof) { double lim = _b.GetDof(dof).ForceLimitNm; return Math.Max(-lim, Math.Min(lim, O2Tau(leg, joint))); }

        void Report(long step)
        {
            ImpAngles();
            YawDiagnostics();
            _dbg.Phase = _phase; _dbg.Stance = _stance; _dbg.Dcm = _dcm; _dbg.ComHeightTarget = (float)_comHeightTarget; _dbg.StepTime = (float)_stepT; _dbg.TimeInStep = (float)_time;
            if (_cmd.Causes != _lastCauses) { _lastCauses = _cmd.Causes; _bus?.Publish(new IntentLimited { Unit = _b.UnitId, Step = step, Causes = (int)_cmd.Causes, RequestedSpeed = _cmd.RequestedSpeed, Speed = _cmd.Speed }); }
        }

        /// <summary>Round 2 step 3: free vertical moment the stance foot(s) must supply, its friction budget and utilisation, and the trunk share of the legs' yaw momentum. From link states only (no contact torque in the body contract).</summary>
        void YawDiagnostics()
        {
            double lUpper = 0, lLower = 0;
            for (int i = 0; i < _b.LinkCount; i++)
            {
                if (_linkInertia[i] == null) continue;
                _b.GetLink(i, out var ls);
                double l = YawCompensation.LinkYawMomentum(_b.LinkMassKg(i), ls.Com - _com, ls.Velocity, _linkInertia[i], ls.Rotation, ls.AngularVelocity);
                if (_upperLink[i]) lUpper += l; else lLower += l;
            }
            double total = lUpper + lLower;
            double raw = double.IsNaN(_prevYawL) ? 0 : -(total - _prevYawL) / _dt;   // minus the rate of vertical angular momentum about the CoM
            _prevYawL = total;
            _yawMomentF += (_t.yaw_filter > 0 ? Math.Min(1, _t.yaw_filter) : 1) * (raw - _yawMomentF);
            double budget = 0;
            foreach (var leg in _legs) budget += YawCompensation.GripBudget(_b.Tuning.friction_dynamic, _t.friction_safety, leg.Load, _t.yaw_grip_radius_frac, _l);
            bool walking = _t.ref_track > 0 && _ref.Speed > 1e-3 && !_stomp && (_phase == LocoPhase.SingleSupport || _phase == LocoPhase.DoubleSupport);
            double share = double.NaN;
            if (walking) { _shareWindow.Push(lUpper, lLower); share = _shareWindow.Share((int)Math.Round(2 * _stepT / _dt)); }
            else _shareWindow.Clear();
            _dbg.YawMomentNm = _yawMomentF; _dbg.YawBudgetNm = budget; _dbg.YawUtil = YawCompensation.Util(_yawMomentF, budget); _dbg.TrunkShare = share;
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
                leg.Load += _t.load_filter * (leg.State.NormalForceN - leg.Load);
                leg.StanceTime = leg.Weight > 0 ? leg.StanceTime + _dt : 0;
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
                if (_restTime > _t.rest_time_s) { _phase = LocoPhase.Fallen; _bus?.Publish(new MechFallEnded { Unit = _b.UnitId, Step = step }); }
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

        /// <summary>imp = 1: the CoP may reach the toe line while the reference has the heel up (Toe) and the heel line at heel strike (Heel); flat and legacy keep the margined sole.</summary>
        Vector2 ClampToSoleImp(Leg leg, Vector2 p)
        {
            if (!ImpActive) return ClampToSole(leg, p);
            StanceImpedance.CopRange(RefSupport(leg), _soleHalfLength, _soleFullHalfLength, out float lo, out float hi);
            Vector2 r = p - leg.Sole2, f = Fwd(leg.Yaw), s = Right(leg.Yaw);
            return leg.Sole2 + s * Clamp(Vector2.Dot(r, s), -_soleHalfWidth, _soleHalfWidth) + f * Clamp(Vector2.Dot(r, f), lo, hi);
        }

        bool ImpActive => _t.imp > 0 && _t.ref_track > 0 && !_stomp && _ref.Speed > 1e-3 && (_phase == LocoPhase.SingleSupport || _phase == LocoPhase.DoubleSupport);
        bool Imp2Sync => _t.imp2_phase_sync > 0 && _t.imp >= 1.5 && _ref.IsOptimised;
        // imp2_phase_sync: phase 0 of the new step is the measured touchdown (_syncT reset there), rate 1/data step time, clamped at 1 if touchdown is late
        double StancePhase() => (_stance == LeftLeg ? 0 : 0.5) + 0.5 * (Imp2Sync ? Math.Min(1, _syncT / _ref.StepTime) : Math.Min(1, _time / _stepT));
        // round 2 step 4: reference samples at phase -h, 0, +h (the model trajectory, not the drive targets) for the reference accelerations; cached per WriteDrives call
        GaitSample _ffM, _ff0, _ffP;
        void SampleReferenceForFf()
        {
            double p = StancePhase(), h = InverseDynamicsFeedForward.PhaseStep;
            _ffM = _ref.Sample(p - h); _ff0 = _ref.Sample(p); _ffP = _ref.Sample(p + h);
        }
        double RefQdd(Func<GaitSample, double> q) => InverseDynamicsFeedForward.SecondDerivative(q(_ffM), q(_ff0), q(_ffP), InverseDynamicsFeedForward.PhaseStep, 2 * _ref.StepTime);
        static Func<GaitSample, LegSample> LegOf(Leg leg) => leg.Side < 0 ? (Func<GaitSample, LegSample>)(g => g.L) : (g => g.R);
        /// <summary>The body's own inverse dynamics on one DOF (gravity, Coriolis, torque for the reference acceleration), gains from config, clamped to the DOF's force limit.</summary>
        double ImpFf(int d, double qddRef, double gravityShare = 1)
        {
            double g = _t.imp_ff_gravity * gravityShare > 0 ? _b.DofGravityTorque(d) : 0, c = _t.imp_ff_coriolis > 0 ? _b.DofCoriolisTorque(d) : 0, a = _t.imp_ff_accel > 0 ? _b.DofTorqueForAcceleration(d, qddRef) : 0;
            return InverseDynamicsFeedForward.Torque(_t, g, c, a, gravityShare, _b.GetDof(d).ForceLimitNm);
        }
        LegSample RefLeg(Leg leg) { var smp = _ref.Sample(StancePhase()); return leg.Side < 0 ? smp.L : smp.R; }
        FootSupport RefSupport(Leg leg) { var s = RefLeg(leg).Support; return s == FootSupport.Swing ? FootSupport.Flat : s; }

        void StateMachine(in IntentFrame intent)
        {
            Leg a = _legs[0], b = _legs[1];
            bool wantsWalk = _cmd.Speed > 1e-3f;
            float feetYaw = a.Yaw + 0.5f * Wrap(b.Yaw - a.Yaw);
            bool turnMove = intent.move == MoveId.Turn || intent.move == MoveId.Walk;
            bool wantsTurn = turnMove && Math.Abs(Wrap(_cmd.Heading - feetYaw)) > (float)(_phase == LocoPhase.Stand ? _t.turn_start_rad : _t.turn_done_rad);
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
                        int swing = turn < -(float)_t.turn_done_rad ? 1 - rightLeg : rightLeg;
                        if (wantsStomp) swing = Vector2.DistanceSquared(a.Sole2, XZ(intent.target)) <= Vector2.DistanceSquared(b.Sole2, XZ(intent.target)) ? 0 : 1;
                        _stance = 1 - swing; _stomp = wantsStomp; _phase = LocoPhase.Shift; _time = 0; _syncT = 0; _firstStep = true;
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
                        _capture = true; PlanTimings(); _time = _tds; _syncT = 0; BeginSwing();
                    }
                    break;
                }
                case LocoPhase.Shift:
                {
                    Leg st = _legs[_stance], sw = _legs[1 - _stance];
                    if (!wantsWalk && !wantsTurn && !wantsStomp) { _phase = LocoPhase.Stand; goto case LocoPhase.Stand; }
                    _time += _dt; _syncT += _dt;
                    float wCop = Vector2.Dot(Nominal(sw, 0, 1, true) - Nominal(st, 0, 1, true), Right(st.Yaw)) * sw.Side;
                    float by = _stomp ? 0f : (float)Lipm.OffsetLateral(wCop, _omega, _stepT);
                    Vector2 reference = Nominal(st, 0, 1, true) + Right(st.Yaw) * (sw.Side * by);
                    Regulate(reference, st, sw, 0f); _dbg.Reference = reference;
                    bool there = (_dcm - reference).Length() < (float)_t.shift_dcm_tolerance_frac * _l && (sw.State.NormalForceN < _t.liftoff_load_bw * _mass * _g || _time > _t.shift_time_tstar * _lim.TStar);
                    if (there || _time > _t.shift_timeout_factor * _t.shift_time_tstar * _lim.TStar)
                    { _time = _tds; BeginSwing(); }
                    targetHeight = _pelvisRest - _t.com_drop_walk_frac * _l;
                    break;
                }
                case LocoPhase.DoubleSupport:
                case LocoPhase.SingleSupport:
                {
                    _time += _dt; _syncT += _dt;
                    Leg st = _legs[_stance], sw = _legs[1 - _stance];
                    float heading = _cmd.Heading;
                    double T = _stepT, tr = Math.Max(T - _time, _t.step_end_freeze * T);
                    float speed = _stomp ? 0 : Math.Min(_cmd.Speed, _vel2.Length() + (float)(_lim.AccelStart * T));
                    speed = Math.Min(speed, (float)(StepCapFrac * _l / T));
                    float s = (float)(speed * T);
                    float travelScale = Clamp(s / (float)((_t.cop_toe_frac - _t.cop_heel_frac) * _l * _t.cop_travel_full_step_ratio), 0, 1);
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
                    if (_time < T * (1 - _t.step_end_freeze)) _offsetHeld = Lipm.DeadbeatOffset(_dcm, nNow, pdot, dcmEndDesired, _omega, tr);
                    Vector2 copStance = ClampToSoleImp(st, nNow + _offsetHeld);
                    if (_stomp) { Vector2 hold = Nominal(st, 0, 1, true); copStance = ClampToSole(st, _dcm + (float)_t.dcm_gain * (_dcm - hold)); _dbg.Reference = hold; }
                    Vector2 dcmEnd = Lipm.DcmAtEnd(_dcm, copStance, pdot, _omega, tr);

                    if (_phase == LocoPhase.DoubleSupport)
                    {
                        Vector2 copTrail = ClampToSoleImp(sw, _copAtTouchdown), want = nNow + _offsetHeld, seg = copStance - copTrail;
                        float wNew = Clamp(Vector2.Dot(want - copTrail, seg) / Math.Max(1e-3f, seg.LengthSquared()), 0, 1);
                        wNew = Math.Max(wNew, (float)Lipm.SmoothStep((_time - _t.transfer_floor_delay * _tds) / _tds));
                        // the trailing leg keeps carrying the body until the landed foot really bears load: the planned share may lead the measured one only by a margin
                        double loads = st.Load + sw.Load;
                        if (loads > _t.load_known_bw * _mass * _g) wNew = Math.Min(wNew, (float)(st.Load / loads + _t.transfer_lead));
                        _transfer = Math.Max(_transfer * (1 - (float)_t.transfer_filter) + wNew * (float)_t.transfer_filter, (float)_t.touchdown_share);
                        wNew = _transfer;
                        Assign(st, wNew, copStance); Assign(sw, 1 - wNew, copTrail);
                        if ((_time >= _tds && wNew > _t.transfer_done_share) || (_time >= _t.ds_overrun_factor * _tds && wNew > _t.transfer_overrun_share) || _time >= _t.ds_timeout_factor * _tds) BeginSwing();
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
                            if (_t.imp2_swing_noclamp > 0 && _t.imp >= 1.5 && _ref.IsOptimised) { along2 = along; side2 = side; }   // step 8.5 c2: imp 2 only
                            _dbg.SwingClamped = along2 != along || side2 != side;
                            Vector2 wanted = st.Ankle2 + sf * along2 + sr * (side2 * sw.Side) + Fwd(yawNext) * _ankleToSoleCentre, change = wanted - _swingTarget;
                            float maxChange = (float)(_t.swing_retarget_tstar * _l / _lim.TStar * _dt);
                            _swingTarget += _swingTime <= _dt * 1.5 || change.Length() <= maxChange ? change : change * (maxChange / change.Length());
                            _swingYawTarget = yawNext;
                        }
                        bool landed = sw.State.Contact && tau > _t.touchdown_min_swing;
                        if (landed) Touchdown(wantsWalk, wantsTurn);
                    }
                    _dbg.CopCommand = copStance; _dbg.SwingTarget = _swingTarget;
                    // the hips ride low enough for both legs to reach a step of this length with flat feet (geometry, not tuning)
                    double legMax = Math.Sqrt(st.ThighLength * st.ThighLength + st.ShankLength * st.ShankLength + 2 * st.ThighLength * st.ShankLength * Math.Cos(_t.knee_min_flex_deg * Math.PI / 180));
                    double half = _t.step_reach_front_frac * s + _t.step_reach_margin_frac * _l, legTotal = st.ThighLength + st.ShankLength;
                    double stepDrop = legTotal - Math.Sqrt(Math.Max(legMax * legMax - half * half, _t.leg_length_floor_frac * _t.leg_length_floor_frac * legTotal * legTotal));
                    targetHeight = _pelvisRest - Math.Max(_t.com_drop_walk_frac * _l, stepDrop) - _extraDrop;
                    if (_t.ref_track > 0 && _t.ref_height_blend > 0 && _ref.Speed > 1e-3)
                    {
                        var smp = _ref.Sample(StancePhase());
                        double refPelvis = smp.HipHeight - (_l - _pelvisRest);
                        targetHeight += (refPelvis - _extraDrop - targetHeight) * _t.ref_height_blend;
                    }
                    if (_t.vault > 0)
                    {
                        // R-007 change 1: no crouch. The hips vault over a near-straight stance leg: knee bent for loading at touchdown, extended by mid-stance;
                        // the pelvis height follows from the leg length and the hip's horizontal distance to the stance ankle, shared between the legs by their load shares.
                        double sum = 0, wsumV = 0;
                        foreach (var leg in _legs)
                        {
                            if (leg.Weight <= 0) continue;
                            _b.GetLink(leg.Thigh, out var hipLink);
                            double dx = hipLink.Position.X - leg.FootLinkState.Position.X, dz = hipLink.Position.Z - leg.FootLinkState.Position.Z, d2 = dx * dx + dz * dz;
                            double knee = (_t.knee_load_deg + (_t.knee_mid_deg - _t.knee_load_deg) * Lipm.SmoothStep(leg.StanceTime / (_t.knee_extend_tstar * _lim.TStar))) * Math.PI / 180;
                            double len2 = leg.ThighLength * leg.ThighLength + leg.ShankLength * leg.ShankLength + 2 * leg.ThighLength * leg.ShankLength * Math.Cos(knee);
                            double hipHeight = leg.FootLinkState.Position.Y + Math.Sqrt(Math.Max(len2 - d2, _t.vault_length_floor_frac * _t.vault_length_floor_frac * len2));
                            sum += leg.Weight * (_pelvisState.Position.Y - _groundY + hipHeight - hipLink.Position.Y); wsumV += leg.Weight;
                        }
                        if (wsumV > 0) targetHeight = sum / wsumV - _extraDrop;
                    }
                    break;
                }
            }
            double rate = (_t.vault > 0 && _phase != LocoPhase.Stand ? _t.height_rate_vault_tstar : _t.height_rate_tstar) * _l / _lim.TStar * _dt; // how fast the height target may move
            _comHeightTarget += Math.Max(-rate, Math.Min(rate, targetHeight - _comHeightTarget));
            Forces();
        }

        void PlanTimings()
        {
            _stepT = _stomp ? _t.step_time_max_tstar * _lim.TStar : _capture ? _t.capture_step_time_tstar * _lim.TStar : _lim.StepTime(_cmd.Speed);
            _capture = false;
            _tds = (2 * _t.duty_factor - 1) * _stepT; _tss = _stepT - _tds;
            // the reference gait for this step: same step time, the step length the limited command allows
            double planSpeed = _stomp ? 0 : Math.Min(_cmd.Speed, StepCapFrac * _l / _stepT);
            if (_t.ref_track > 0) _ref.Configure(planSpeed, planSpeed * _stepT);
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
            bool calm = Math.Abs(Vector2.Dot(_vel2, Fwd(st.Yaw))) < _lim.StopSpeed && (_dcm - mid).Length() < (float)_t.stand_dcm_frac * (st.Ankle2 - sw.Ankle2).Length() && along < (float)_t.stand_align_frac * _l && Math.Abs(Wrap(sw.Yaw - st.Yaw)) < (float)_t.stand_yaw_rad;
            if (_stomp || (!wantsWalk && !wantsTurn && calm))
            {
                if (_stomp) _stompDone = true;
                _stomp = false; _phase = LocoPhase.Stand; return;
            }
            _copAtTouchdown = _copPrev;
            _stance = 1 - _stance; _phase = LocoPhase.DoubleSupport; _time = 0; _syncT = 0; _transfer = (float)_t.touchdown_share;
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
            double wc = 2 * Math.PI * _t.height_hz / Math.Sqrt(_l / _t.reference_leg_length_m);
            double fy = _mass * (_g + wc * wc * (_comHeightTarget - (_pelvisState.Position.Y - _groundY)) - 2 * wc * _pelvisState.Velocity.Y);
            fy = Math.Max(_t.force_min_bw * _mass * _g, Math.Min(_t.force_max_bw * _mass * _g, fy));
            Vector2 cop = Vector2.Zero; float wsum = 0;
            foreach (var leg in _legs) { cop += XZ(leg.Cop) * leg.Weight; wsum += leg.Weight; }
            if (wsum <= 0) return;
            cop /= wsum; _copPrev = cop;
            Vector2 fh = (_com2 - cop) * (float)(fy / _zc);
            // never ask the ground for more friction than two thirds of what it has (R-005 5.3: mu_avail >= 1.5 mu_req)
            float fhMax = (float)(fy * _b.Tuning.friction_dynamic / _t.friction_safety);
            if (fh.Length() > fhMax) fh *= fhMax / fh.Length();
            // vertical force by the planned share; horizontal force by the load each foot really carries, so a lightly loaded foot is never asked for friction it does not have
            double loadSum = 0; foreach (var leg in _legs) if (leg.Weight > 0) loadSum += leg.Load;
            foreach (var leg in _legs)
            {
                float share = leg.Weight / wsum, grip = loadSum > _t.load_known_bw * _mass * _g && leg.Weight > 0 ? (float)(leg.Load / loadSum) : share;
                leg.Force = new Vector3(fh.X * grip, (float)fy * share, fh.Y * grip);
            }
        }

        // ---------------------------------------------------------------- joint level
        void WriteDrives()
        {
            Leg st = _legs[_stance], sw = _legs[1 - _stance];
            bool impNow = ImpActive; if (impNow) SampleReferenceForFf();
            _o2 = impNow && _ref.IsOptimised && _ref.TryOptimised(StancePhase(), out _o2s);
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
                    float yawGrip = (float)(_b.Tuning.friction_dynamic / _t.friction_safety * leg.Load * _t.yaw_grip_radius_frac * _l);
                    hipExtra.Y = Clamp(hipExtra.Y, -yawGrip, yawGrip); leg.HipExtra = hipExtra;
                    if (ImpActive)
                    {
                        // round 2 (R-008 section 3): joint-space impedance toward the reference's stance joint angles, per joint and stance sub-phase; the force loop stays as a scaled correction
                        var rl = RefLeg(leg); var sup = rl.Support == FootSupport.Swing ? FootSupport.Flat : rl.Support;
                        var lof = LegOf(leg);
                        StanceDof(leg, leg.Hip, hipExtra, _t.stance_hip_damping_ratio, leg.Hip[0], rl.HipFlexion, StanceImpedance.Hip, sup, RefQdd(g => lof(g).HipFlexion));
                        StanceDof(leg, new[] { leg.Knee }, Vector3.Zero, _t.stance_knee_damping_ratio, leg.Knee, rl.Knee, StanceImpedance.Knee, sup, RefQdd(g => lof(g).Knee));
                        StanceDof(leg, leg.Ankle, Vector3.Zero, _t.stance_ankle_damping_ratio, leg.Ankle[0], rl.AnklePlantar, StanceImpedance.Ankle, sup, RefQdd(g => lof(g).AnklePlantar));
                    }
                    else
                    {
                    StanceDof(leg, leg.Hip, hipExtra, _t.stance_hip_damping_ratio);
                    StanceDof(leg, new[] { leg.Knee }, Vector3.Zero, _t.stance_knee_damping_ratio);
                    StanceDof(leg, leg.Ankle, Vector3.Zero, _t.stance_ankle_damping_ratio);
                    }
                }
                else { leg.HipExtra = Vector3.Zero; SwingLeg(leg); }
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
                // upper-body counter-rotation: the trunk turns against the legs (shoulder forward on the side of the trailing leg), taking up part of the swing leg's yaw reaction
                if (_t.ref_track > 0 && _ref.Speed > 1e-3 && (_phase == LocoPhase.SingleSupport || _phase == LocoPhase.DoubleSupport))
                {
                    // tracked reference: arms against their own legs and the trunk against the limbs' angular momentum (R-007 change 4)
                    var smp = _ref.Sample(StancePhase());
                    bool imp = ImpActive;   // round 2 step 3: with imp = 1 the waist and both shoulders follow the reference fully, with their own drive gains
                    double trackUpper = imp ? 1 : _t.ref_track * _t.ref_track_upper;
                    if (imp && _armRefOn && (d == _armShoulderFlex[0] || d == _armShoulderFlex[1]))
                    {
                        // step 9.5h: reference arm swing of the QP row blended in over qp_arm_ref_blend_s after the hand-off (the imp 2 reference arm underneath until then); the qp_arm_ff feed-forward, when on, is added
                        bool armL = d == _armShoulderFlex[0]; int ai = armL ? 0 : 1; double bl = _armRefBlend, fb = _armFfOn ? _armFfBlend : 0;
                        Track(d, (1.0 - bl) * (armL ? smp.ArmFlexionL : smp.ArmFlexionR) + bl * _armRef[ai] + fb * _armFf[ai], Vector3.Zero, default, true, 1, _t.qp_arm_ref_hz > 0 ? _t.qp_arm_ref_hz : _t.imp_arm_hz, _t.imp_arm_zeta, (1.0 - bl) * RefQdd(g => armL ? g.ArmFlexionL : g.ArmFlexionR) + bl * _armRefQdd[ai] + fb * _armFfQdd[ai]); continue;
                    }
                    if (imp && _armFfOn && (d == _armShoulderFlex[0] || d == _armShoulderFlex[1]))
                    {
                        // step 9.5f: momentum-cancelling shoulder feed-forward, blended in over 0.5 s after the hand-off; qp_arm_ff_ref = 1 keeps the reference arm underneath
                        bool armL = d == _armShoulderFlex[0]; int ai = armL ? 0 : 1; double refW = _t.qp_arm_ff_ref > 0.5 ? 1.0 : 1.0 - _armFfBlend, bl = _armFfBlend;
                        Track(d, refW * (armL ? smp.ArmFlexionL : smp.ArmFlexionR) + bl * _armFf[ai], Vector3.Zero, default, true, 1, _t.qp_arm_ff_hz > 0 ? _t.qp_arm_ff_hz : _t.imp_arm_hz, _t.imp_arm_zeta, refW * RefQdd(g => armL ? g.ArmFlexionL : g.ArmFlexionR) + bl * _armFfQdd[ai]); continue;
                    }
                    if (imp && (d == _armShoulderFlex[0] || d == _armShoulderFlex[1])) { bool armL = d == _armShoulderFlex[0]; Track(d, (armL ? smp.ArmFlexionL : smp.ArmFlexionR) + _armTrim[armL ? 0 : 1], Vector3.Zero, default, true, 1, _t.imp_arm_hz, _t.imp_arm_zeta, RefQdd(g => armL ? g.ArmFlexionL : g.ArmFlexionR)); continue; }
                    if (d == _armShoulderFlex[0]) target += (smp.ArmFlexionL - target) * trackUpper;
                    if (d == _armShoulderFlex[1]) target += (smp.ArmFlexionR - target) * trackUpper;
                    if (d == _waistYaw && trackUpper > 0) { Track(d, -(smp.WaistYaw + smp.PelvisYaw) * trackUpper, Vector3.Zero, default, true, 1, imp ? _t.imp_waist_hz : 0, _t.imp_waist_zeta, imp ? RefQdd(g => -(g.WaistYaw + g.PelvisYaw)) : (double?)null); continue; }   // anatomical waist rotation is positive to the left; the reference yaw is positive to the right
                }
                else if (d == _waistYaw && _t.trunk_counter_gain != 0)
                {
                    Leg right = _legs[0].Side > 0 ? _legs[0] : _legs[1], left = right == _legs[0] ? _legs[1] : _legs[0];
                    target = -_t.trunk_counter_gain * 0.5 * (_b.DofPosition(right.Hip[0]) - _b.DofPosition(left.Hip[0]));
                }
                if (d == _armShoulderFlex[0]) target += _armTrim[0]; else if (d == _armShoulderFlex[1]) target += _armTrim[1];
                Track(d, target, Vector3.Zero, default, false);
            }
        }

        float _swingYawTargetNow, _transfer;
        int LeftLeg => _legs[0].Side < 0 ? 0 : 1;
        int _waistYaw = -1;
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

        void StanceDof(Leg leg, int[] dofs, Vector3 extraTorque, double dampingRatio, int trackDof = -1, double refAngle = 0, int joint = 0, FootSupport support = FootSupport.Flat, double refQdd = 0)
        {
            foreach (int d in dofs)
            {
                if (d < 0) continue;
                Vector3 force = leg.Force;
                // the ankle sets the centre of pressure: its torque must match the load the foot really carries, not the load the plan expects
                if (dofs == leg.Ankle && force.Y > 1f && leg.Load > 0) force *= Clamp((float)(leg.Load / force.Y), (float)_t.load_scale_min, (float)_t.load_scale_max);
                // a foot that has just touched is not yet flat: its ankle stays passive (damped) and takes up centre-of-pressure control gradually, so the sole settles instead of slapping down
                if (dofs == leg.Ankle && _phase == LocoPhase.DoubleSupport && leg == _legs[_stance]) force *= (float)Lipm.SmoothStep(_time / (_t.landing_time_tstar * _lim.TStar));
                double tau = SupportTorque(d, force, leg.Cop, true) + Vector3.Dot(_b.DofAxisWorld(d), extraTorque);
                if (d == trackDof)
                {
                    // joint impedance: PD toward the reference angle (natural frequency and damping ratio from config for this joint and sub-phase); the virtual-force torque is only a correction
                    var info = _b.GetDof(d);
                    double refTarget = Math.Max(info.LowerRad, Math.Min(info.UpperRad, refAngle));   // run 3: the anatomical angle is the DOF coordinate (no Sign); the run-1 fall was the 0.25 virtual-force scale
                    StanceImpedance.Gains(_t, joint, support, out double hz, out double zeta);
                    StanceImpedance.PdGains(_b.DofInertia(d, true), hz, zeta, out double kImp, out double cImp);
                    double v = _hasPrev[d] ? Math.Max(-_t.target_velocity_max_rps, Math.Min(_t.target_velocity_max_rps, (refTarget - _prevTarget[d]) / _dt)) : 0;
                    _prevTarget[d] = refTarget; _hasPrev[d] = true;
                    // round 2 step 6b: stiffness sized from the load the joint carries (never softer than the Hz gains)
                    StanceImpedance.LoadGains(_t, joint, leg.Load, _mass, _g, _l, kImp, cImp, out kImp, out cImp);
                    if (_o2)
                    {
                        // imp = 2: the optimised rate is the velocity target and the optimised torque the feed-forward (the virtual-force term only at imp2_vf_scale, 0 by default: the optimised torque already carries the load)
                        v = Math.Max(-_t.target_velocity_max_rps, Math.Min(_t.target_velocity_max_rps, O2Rate(leg, joint)));
                        Drive(d, refTarget, v, kImp, cImp);
                        double lim2 = _b.GetDof(d).ForceLimitNm;
                        _b.SetDofFeedForward(d, Math.Max(-lim2, Math.Min(lim2, SupportTorque(d, force, leg.Cop, true) * _t.imp2_vf_scale + Vector3.Dot(_b.DofAxisWorld(d), extraTorque) + O2Ff(leg, joint, d))));
                        continue;
                    }
                    Drive(d, refTarget, v, kImp, cImp);
                    _b.SetDofFeedForward(d, SupportTorque(d, force, leg.Cop, true) * _t.imp_vf_scale + Vector3.Dot(_b.DofAxisWorld(d), extraTorque) + ImpFf(d, refQdd, _t.imp_ff_stance_gravity_share));   // round 2 step 4: stance-leg gravity is only partly carried by the joints (contact carries the rest)
                    continue;
                }
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
                    double len = hipToAnkle.Length(), incline = Math.Max(_t.posture_incline_floor, -hipToAnkle.Y / Math.Max(1e-3, len));
                    double want = len + (_comHeightTarget - (_pelvisState.Position.Y - _groundY)) * incline;
                    double a = leg.ThighLength, c = leg.ShankLength, kneeMin = _t.knee_min_flex_deg * Math.PI / 180;
                    double cosInterior = Math.Min(1, Math.Max(Math.Cos(Math.PI - kneeMin), (a * a + c * c - want * want) / (2 * a * c)));
                    target = Math.PI - Math.Acos(cosInterior);
                    double ratio = _t.stance_posture_hz / _b.Tuning.Kind("knee").stance_hz;
                    k = stiffness * ratio * ratio;
                    velocity = _hasPrev[d] ? Math.Max(-_t.stance_velocity_max_rps, Math.Min(_t.stance_velocity_max_rps, (target - _prevTarget[d]) / _dt)) : 0;
                    _prevTarget[d] = target; _hasPrev[d] = true;
                }
                else _hasPrev[d] = false;
                Drive(d, target, velocity, k, damping * dampingRatio + landing);
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
                double up = _t.stomp_rise_fraction * _tss, lift = _t.stomp_lift_frac * _l, v = _t.stomp_speed_tstar * _l / tStar;
                height = _swingTime < up ? lift * Lipm.MinJerk(_swingTime / up) : Math.Max(-_t.touchdown_overdrive_frac * _l, lift - v * (_swingTime - up));
            }
            else
            {
                // lift and clear early, be over the landing spot at hover height by swing_hover of the swing, then come down slowly until contact (R-005: touchdown <= 1 m/s)
                double vtd = _t.touchdown_speed_tstar * _l / tStar, th = _t.swing_hover, hover = vtd * (1 - th) * _tss;
                double u = Math.Min(1, tau / th);
                height = tau <= th ? hover * Lipm.MinJerk(u) + _t.swing_clearance_frac * _l * Math.Pow(Math.Sin(Math.PI * Math.Pow(u, _t.swing_rise_exponent)), 2) : Math.Max(-_t.touchdown_overdrive_frac * _l, hover - vtd * (_swingTime - th * _tss));
                s = Lipm.MinJerk(Math.Max(0, Math.Min(1, (tau - _t.swing_horizontal_delay) / (_t.swing_horizontal_end - _t.swing_horizontal_delay))));
            }
            double refPitch = 0;
            if (_t.ref_track > 0 && !_stomp && _ref.Speed > 1e-3)
            {
                // tracked reference (R-007): lift, clearance, hover and final approach, foot pitch and forward progress of the swing foot come from the reference gait at this swing phase
                double duty = _ref.Duty, u = Math.Min(1, tau);
                var a0 = _ref.Sample(duty + 1e-9).L; var a1 = _ref.Sample(1 - 1e-9).L; var au = _ref.Sample(duty + u * (1 - duty) * (1 - 1e-9)).L;
                double T2 = 2 * _ref.StepTime, w0 = a0.Ankle.Z + _ref.Speed * duty * T2, w1 = a1.Ankle.Z + _ref.Speed * T2, wu = au.Ankle.Z + _ref.Speed * (duty + u * (1 - duty)) * T2;
                double sRef = Math.Abs(w1 - w0) > 1e-6 ? Math.Max(0, Math.Min(1, (wu - w0) / (w1 - w0))) : s;
                double trackSwing = _t.ref_track * _t.ref_track_swing;
                refPitch = au.FootPitch * trackSwing * _t.ref_track_pitch;
                // sole-centre height of the reference foot above the ground
                double hRef = au.Ankle.Y - _ankleToSoleCentre * Math.Sin(au.FootPitch) - _ankleHeight * Math.Cos(au.FootPitch);
                if (tau > 1) hRef = Math.Max(-_t.touchdown_overdrive_frac * _l, -(_t.touchdown_speed_tstar * _l / tStar) * (_swingTime - _tss));
                s += (sRef - s) * trackSwing; height += (hRef - height) * trackSwing;
            }
            Vector2 xz = XZ(_swingStart) + (_swingTarget - XZ(_swingStart)) * (float)s;
            float yaw = _swingStartYaw + Wrap(_swingYawTarget - _swingStartYaw) * (float)s;
            _swingYawTargetNow = yaw;
            float startLift = (_swingStart.Y - ground) * (float)(1 - s);
            // toes up through the swing (ground clearance of a 15 m foot), easing to a small heel-first angle for landing
            double toeUp = _stomp ? 0 : (_t.swing_toe_up_deg * Math.Sin(Math.PI * Math.Min(1, tau / _t.swing_hover)) + _t.heel_strike_deg * Lipm.SmoothStep(tau / _t.swing_hover)) * Math.PI / 180;
            Quaternion footRot = JointMath2.YawRotation(yaw) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)(refPitch - toeUp));
            Vector3 sole = new Vector3(xz.X, ground + startLift + (float)height, xz.Y);
            Vector3 ankle = sole + new Vector3(0, _ankleHeight, 0) - new Vector3(Fwd(yaw).X, 0, Fwd(yaw).Y) * _ankleToSoleCentre;
            _b.GetLink(leg.Thigh, out var thigh);
            var ik = LegIk.Solve(thigh.Position, _pelvisState.Rotation, ankle, footRot, leg.ThighLength, leg.ShankLength, _t.knee_min_flex_deg * Math.PI / 180, _t.knee_max_flex_deg * Math.PI / 180);
            // a landing spot the leg cannot reach lowers the hips until it can (never stretches the foot target)
            double dropRate = _t.extra_drop_rate_tstar * _l / tStar;
            _extraDrop = ik.Clamped && tau > _t.touchdown_min_swing ? Math.Min(_t.extra_drop_max_frac * _l, _extraDrop + dropRate * _dt) : Math.Max(0, _extraDrop - _t.extra_drop_recover * dropRate * _dt);
            Vector3 hip = JointMath2.RotVec(ik.HipRelative), ank = JointMath2.RotVec(ik.AnkleRelative);
            // compliant touchdown (R-005 5.2 step 5, Q-012): the leg goes soft as the foot nears the ground, so contact loads it through a spring instead of a stiff position servo
            double soft = _stomp ? 1 : 1 - (1 - _t.touchdown_gain_scale) * Lipm.SmoothStep((tau - (_t.swing_hover - _t.touchdown_soften_span)) / _t.touchdown_soften_span);
            // round 2 step 4 (imp = 1): the swing leg's pitch DOFs get the reference leg's accelerations, the others gravity and Coriolis only
            bool ff = ImpActive; var lof = LegOf(leg);
            double sg = _o2 ? _t.imp2_swing_gain_scale : 1;   // step 8.5 c2: imp 2 swing PD gain multiplier (on top of the touchdown softening)
            for (int a = 0; a < 3; a++)
            {
                if (leg.Hip[a] >= 0) Track(leg.Hip[a], Component(hip, a) * _b.GetDof(leg.Hip[a]).Sign, Vector3.Zero, default, true, soft * sg, 0, 1, ff ? (a == 0 ? RefQdd(g => lof(g).HipFlexion) : 0) : (double?)null);
                if (leg.Ankle[a] >= 0) Track(leg.Ankle[a], Component(ank, a) * _b.GetDof(leg.Ankle[a]).Sign, Vector3.Zero, default, true, soft * sg, 0, 1, ff ? (a == 0 ? RefQdd(g => lof(g).AnklePlantar) : 0) : (double?)null);
            }
            Track(leg.Knee, ik.Knee * _b.GetDof(leg.Knee).Sign, Vector3.Zero, default, true, soft * sg, 0, 1, ff ? RefQdd(g => lof(g).Knee) : (double?)null);
            if (_o2)
            {
                // imp = 2: the optimised swing-leg torque replaces the inverse-dynamics feed-forward of the pitch joints
                if (leg.Hip[0] >= 0) _b.SetDofFeedForward(leg.Hip[0], O2Ff(leg, StanceImpedance.Hip, leg.Hip[0]));
                _b.SetDofFeedForward(leg.Knee, O2Ff(leg, StanceImpedance.Knee, leg.Knee));
                if (leg.Ankle[0] >= 0) _b.SetDofFeedForward(leg.Ankle[0], O2Ff(leg, StanceImpedance.Ankle, leg.Ankle[0]));
            }
        }

        static float Component(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

        /// <summary>Position drive toward a target with the R-005 swing gains, velocity feed-forward from the target's own motion and gravity feed-forward.</summary>
        void Track(int dof, double target, Vector3 force, Vector3 cop, bool velocityFeedForward, double gainScale = 1, double impHz = 0, double impZeta = 1, double? ffQdd = null)
        {
            var info = _b.GetDof(dof);
            target = Math.Max(info.LowerRad, Math.Min(info.UpperRad, target));
            double vel = velocityFeedForward && _hasPrev[dof] ? (target - _prevTarget[dof]) / _dt : 0;
            vel = Math.Max(-_t.target_velocity_max_rps, Math.Min(_t.target_velocity_max_rps, vel));
            // R-007 change 3: inverse-dynamics feed-forward. The torque that gives the limb the acceleration its own target asks for (distal inertia x target acceleration), so the drive only corrects errors.
            double accel = velocityFeedForward && _hasPrev[dof] && _t.swing_ff_gain > 0 ? (vel - _prevVelTarget[dof]) / _dt : 0;
            _ffAccel[dof] += _t.swing_ff_filter * (Math.Max(-_t.swing_ff_accel_max, Math.Min(_t.swing_ff_accel_max, accel)) - _ffAccel[dof]);
            if (!velocityFeedForward || !_hasPrev[dof]) _ffAccel[dof] = 0;
            _prevVelTarget[dof] = vel;
            _prevTarget[dof] = target; _hasPrev[dof] = true;
            _b.GetDofGains(dof, false, out double k, out double damping);
            if (impHz > 0) StanceImpedance.PdGains(_b.DofInertia(dof, false), impHz, impZeta, out k, out damping);   // round 2 step 3: k = I w^2, c = 2 zeta I w on the distal inertia
            // critical damping is kept when the stiffness is scaled: d scales with sqrt(k)
            Drive(dof, target, vel, k * gainScale, damping * Math.Sqrt(gainScale));
            // imp = 1: the body's own inverse dynamics replaces the distal-weight term and the differenced-target term (no contact on these DOFs, gravity is not counted twice)
            if (ffQdd.HasValue) _b.SetDofFeedForward(dof, ImpFf(dof, ffQdd.Value));
            else _b.SetDofFeedForward(dof, SupportTorque(dof, force, cop, false) + _t.swing_ff_gain * _b.DofInertia(dof, false) * _ffAccel[dof]);
        }
    }

    /// <summary>Round 2 step 4: pure logic of the inverse-dynamics feed-forward (gains, total, clamp to the drive force limit, reference acceleration), engine-free.</summary>
    public static class InverseDynamicsFeedForward
    {
        /// <summary>Phase offset of the central difference of the reference sample (1/128 of a stride).</summary>
        public const double PhaseStep = 1.0 / 128;

        /// <summary>imp_ff_gravity * share * gravity + imp_ff_coriolis * coriolis + imp_ff_accel * torque-for-acceleration, clamped to the DOF's force limit (the body leaves the PD the remainder of that limit).</summary>
        public static double Torque(WalkTuning t, double gravityNm, double coriolisNm, double accelNm, double gravityShare, double forceLimitNm)
        {
            double tau = t.imp_ff_gravity * gravityShare * gravityNm + t.imp_ff_coriolis * coriolisNm + t.imp_ff_accel * accelNm;
            return Math.Max(-forceLimitNm, Math.Min(forceLimitNm, tau));
        }

        /// <summary>Central second difference of a function of phase (q at phase-h, phase, phase+h) as rad/s2, for a stride of the given duration.</summary>
        public static double SecondDerivative(double qMinus, double q0, double qPlus, double phaseStep, double strideSeconds)
        {
            double dt = phaseStep * strideSeconds;
            return (qPlus - 2 * q0 + qMinus) / (dt * dt);
        }
    }

    /// <summary>Round 2 step 2 (R-008 section 3): pure logic of the joint-space stance impedance (gain lookup, PD gains, CoP range), engine-free.</summary>
    public static class StanceImpedance
    {
        public const int Hip = 0, Knee = 1, Ankle = 2;

        /// <summary>Natural frequency (Hz) and damping ratio of a joint for a stance sub-phase: Heel = loading, Flat, Toe = heel rise and push-off (a leg the reference has already lifted is treated as Flat).</summary>
        public static void Gains(WalkTuning t, int joint, FootSupport support, out double hz, out double zeta)
        {
            int s = support == FootSupport.Heel ? 0 : support == FootSupport.Toe ? 2 : 1;
            switch (joint)
            {
                case Hip: hz = s == 0 ? t.imp_hip_hz_heel : s == 1 ? t.imp_hip_hz_flat : t.imp_hip_hz_toe; zeta = s == 0 ? t.imp_hip_zeta_heel : s == 1 ? t.imp_hip_zeta_flat : t.imp_hip_zeta_toe; break;
                case Knee: hz = s == 0 ? t.imp_knee_hz_heel : s == 1 ? t.imp_knee_hz_flat : t.imp_knee_hz_toe; zeta = s == 0 ? t.imp_knee_zeta_heel : s == 1 ? t.imp_knee_zeta_flat : t.imp_knee_zeta_toe; break;
                default: hz = s == 0 ? t.imp_ankle_hz_heel : s == 1 ? t.imp_ankle_hz_flat : t.imp_ankle_hz_toe; zeta = s == 0 ? t.imp_ankle_zeta_heel : s == 1 ? t.imp_ankle_zeta_flat : t.imp_ankle_zeta_toe; break;
            }
        }

        /// <summary>Load-sized stance gains: K = kload_joint * W_leg * l (N m/rad), D = 2 zeta sqrt(K I_eff), I_eff = mass l^2 share, share = W_leg / (mass g) clamped to [0,1]; each blended with the Hz gains by Max. kload_joint = 0 returns the Hz gains.</summary>
        public static void LoadGains(WalkTuning t, int joint, double loadN, double massKg, double g, double legLengthM, double kHz, double cHz, out double stiffness, out double damping)
        {
            double kl = joint == Hip ? t.imp_kload_hip : joint == Knee ? t.imp_kload_knee : t.imp_kload_ankle;
            stiffness = kHz; damping = cHz;
            if (kl <= 0 || loadN <= 0) return;
            double k = kl * loadN * legLengthM, share = Math.Max(0, Math.Min(1, loadN / (massKg * g))), ieff = massKg * legLengthM * legLengthM * share;
            stiffness = Math.Max(kHz, k); damping = Math.Max(cHz, 2 * t.imp_kload_zeta * Math.Sqrt(k * ieff));
        }

        /// <summary>Stiffness k = I w^2 and damping c = 2 zeta I w for an inertia I (kg m^2) about the joint axis.</summary>
        public static void PdGains(double inertia, double hz, double zeta, out double stiffness, out double damping)
        {
            double w = 2 * Math.PI * hz;
            stiffness = inertia * w * w; damping = 2 * zeta * inertia * w;
        }

        /// <summary>Allowed CoP range along the foot from the sole centre: flat keeps the margined sole; heel allows the heel line (full half length behind); toe allows the toe line (full half length ahead).</summary>
        public static void CopRange(FootSupport support, float halfMargined, float halfFull, out float lo, out float hi)
        {
            lo = -halfMargined; hi = halfMargined;
            if (support == FootSupport.Toe) hi = halfFull;
            else if (support == FootSupport.Heel) lo = -halfFull;
        }
    }

    /// <summary>Round 2 step 3: pure logic of the yaw-momentum bookkeeping (link angular momentum, friction budget, utilisation, trunk share), engine-free.</summary>
    public static class YawCompensation
    {
        /// <summary>Vertical angular momentum of one link about the CoM: orbital m (r x v)_y plus spin (R diag(I) R^T w)_y, with the principal inertias taken along the link axes.</summary>
        public static double LinkYawMomentum(double mass, Vector3 rel, Vector3 vel, double[] inertia, Quaternion rot, Vector3 omega)
        {
            double orbital = mass * (rel.Z * vel.X - rel.X * vel.Z);
            Vector3 wl = Vector3.Transform(omega, Quaternion.Conjugate(rot));
            Vector3 hl = new Vector3((float)(inertia[0] * wl.X), (float)(inertia[1] * wl.Y), (float)(inertia[2] * wl.Z));
            return orbital + Vector3.Transform(hl, rot).Y;
        }
        /// <summary>The yaw friction budget of one foot: mu / safety x load x radius fraction x leg length (the limit the controller uses for the hip yaw torque).</summary>
        public static double GripBudget(double mu, double safety, double load, double radiusFrac, double legLength) => mu / safety * Math.Max(0, load) * radiusFrac * legLength;
        /// <summary>|moment| / budget; NaN (logged as null) when there is no budget.</summary>
        public static double Util(double moment, double budget) => budget > 1e-9 ? Math.Abs(moment) / budget : double.NaN;
    }

    /// <summary>Sliding window of the upper-body and lower-body vertical angular momentum; the share is the least-squares fraction of the lower body's momentum the upper body cancels (1 = pure cancellation).</summary>
    public sealed class YawShareWindow
    {
        readonly double[] _u, _l; int _head, _count;
        public YawShareWindow(int capacity) { _u = new double[capacity]; _l = new double[capacity]; }
        public void Clear() { _head = 0; _count = 0; }
        public void Push(double upper, double lower) { _u[_head] = upper; _l[_head] = lower; _head = (_head + 1) % _u.Length; if (_count < _u.Length) _count++; }
        /// <summary>-sum(u l) / sum(l l) over the last <paramref name="n"/> samples; NaN until the window holds n samples or when the lower body has no momentum.</summary>
        public double Share(int n)
        {
            if (n < 2 || n > _u.Length || _count < n) return double.NaN;
            double ul = 0, ll = 0;
            for (int k = 1; k <= n; k++) { int i = ((_head - k) % _u.Length + _u.Length) % _u.Length; ul += _u[i] * _l[i]; ll += _l[i] * _l[i]; }
            return ll > 1e-12 ? -ul / ll : double.NaN;
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
