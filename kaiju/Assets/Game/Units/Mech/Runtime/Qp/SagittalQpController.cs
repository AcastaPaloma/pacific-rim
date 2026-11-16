using System;
using Game.Physics.Body.Qp;
using Game.Units.Mech.Contracts;

namespace Game.Units.Mech.Qp
{
    /// <summary>Parameters of <see cref="SagittalQpController"/>; defaults = the b5 preset of Tools/GaitOpt/qp_proto/run_fid.py (DEFAULT of controller_fid.py + kvs 5, onset 1, land 1, land_act 1, land_k0 35, fr_meas 1, fr_margin 0.9, mass_est 0.05, sg_floor 0.3).</summary>
    public sealed class SagittalQpOptions
    {
        public double Mu = 0.40, LimFrac = 0.98, G = 9.81;
        public double OmX = 0.8, OmZ = 2.0, KL = 2.0, OmSw = 4.0, OmTh = 3.0, OmPost = 3.0, OmFp = 4.0;
        public double WCx = 1e4, WCz = 1e4, WL = 1e3, WPrior = 1e-2, WSmooth = 1e-3, WSt = 1e6, WSwing = 1e3, WFp = 1e3, WTrunk = 1e4, WPost = 10.0, WReg = 1e-4, WBreg = 1e-6;
        public double Kvs = 5.0, OnBw = 0.03, OffBw = 0.01, TOut = 0.3, TIn = 0.08, TBlend = 0.2, LoadThr = 0.01, TdMinFrac = 0.6, LateVz = 2.0, FCap = 4.0, ZHold = 0.05;
        public bool HoldSched = true, Onset = true, Land = true, LandAct = true, FrMeas = true;
        public double FrMargin = 0.9, LandK0 = 35, VTd = 0.3, LandPost = 0.0;
        public double MassEst = 0.05, MassLo = 0.7, MassHi = 1.5, SgFloor = 0.3;
        public double SolverRegularisation = 1e-12;
        /// <summary>Step 9.5b (stage 2, qp_lateral): 3D contact forces at 2 sole corners per heel / toe point (diamond pyramid |fx| + |fy| &lt;= mu' fz shared by both shears), lateral CoM, roll and yaw momentum rows. Off = the planar QP unchanged.</summary>
        public double WTau;   // step 9.5b: weight of sum (tau_j / LIM_j)^2 (0 = off, as the prototype); fixes the internal double-support force the other rows leave free
        public double SwJ, SwJOm = 2.0, SwJAmax = 6.0, SwJTd = 0.2; public bool SwJAnkle; public double SwFollow, SwAmax, TdAir, TdXoffMax; public bool Diag, TdHeel; public bool Rom, FpRom; public double RomMargin = 3 * Math.PI / 180, RomHorizon = 0.1, RomAmax = 30.0, FpRomBand = 3 * Math.PI / 180;   // step 9.5j (qp_rom, qp_fp_rom)
        public int R11Ds;   // round 11 (qp_r11_ds): 0 = the 6c double-support CoP sweep from the rear toe; 1 = no sweep (DCM CoP target on the union of the contacts); 2 = sweep from the rear-foot point nearest the DCM target
        public double CdxMax, SwCp;   // step 9.5m (qp_cdx_max, qp_sw_cp; 0 = off)
        public double FootUnload, FootUnloadAt = 1.5, FootZdip, FootZdipMax = 4.0, FootZdipMargin = 0.5, FootZdipUp = 1.0, FootZdipDn = 0.5; public bool Foot, FootStance = true, FootDrive = true, FootMeas; public double FootPre = 0.5, FootHeelDeg = 2.0, FootW = 1.0, FootRate = 0.5, FootCopM = 0.08, FootFricFloor = 0.02; public bool R7NoForce, R7Clock; public double R7Fp; public bool R8Retime, R8RefClock, R8Trem; public double R8Gmin = 0.3, R8Gk = 3.0, R8Gmax = 1.0, R8SatK, R8SatFrac = 0.9, R8SwLim; public bool R10Diag; public double R10Fp, R10FpMin = 0.3, R9Gate, R9GateTmax = 1.0, R9Launch, R9Ramp = 0.3, R9Vfrac = 1.0, R9Tau = 2.0; public double FootCopHeelM, FootCopToeM;   // round 7   // round 6 (qp_foot*; Foot false = off)
        public bool Plan; public double PlanKxi = 1.0, PlanLmin = 0.5, PlanLmax = 1.3, PlanTdFrac = 0.8, PlanTdTol = 0.2, PlanZkp = 4.0, PlanZkd = 4.0, PlanWz = 1e5, PlanWx, PlanReplanM = 1.0, PlanFp = 1.0, PlanSw = 1, PlanApex = 1.0, PlanVd = 1.0, PlanFpTau = 0.5, PlanHeelW = 1.0, PlanDownFrac = 0.9, PlanForceFrac = 1.15, PlanForceZ = 0.3, PlanMaxFrac = 1.3, PlanDs = 1, PlanReach = 0.7;   // step 6a (qp_plan*; Plan false = off)
        public double LateCx, CxEmax;   // CxEmax (qp_cx_emax, 9.5i): clamp of the CoM-x position error in the CoM row (0 = off)   // step 9.5i (qp_late_cx k): while the touchdown is late the CoM-x reference advances at k x the reference CoM speed (0 = frozen at the step end, as the prototype)
        public double ArmRefYawFf;   // step 9.5h (qp_arm_ref_yawff w): the filtered measured yaw-momentum rate of the arms in the feed-forward is replaced by (1 - w) measured + w predicted from the reference shoulder acceleration (DLArmRefPred)
        public bool YawTrunk; public double YawFf = 1.0, YawCap = 0.1, YawBudget = 0, YawBudgetFloor = 0.1;   // step 9.5e: trunk yaw task with the legs' and arms' yaw-momentum rate as feed-forward
        public bool Lateral; public double OmY = 0.5, WCy = 1e3, WRoll = 1e3, KRoll = 2.0, WYaw = 1e6, OmYaw = 1.0;   // the prototype's solver has none; the default 1e-6 of the shared solver distorts the weak (1e-6..1e-4) weights
        /// <summary>Not ported (all off in b5): sigma smoothing, land_lead/xlead/vclip/nounder, om_land, sw_post, fmin_td, rate_bw, sg_fmin, predict.</summary>
        public static SagittalQpOptions From(WalkTuning t) => new SagittalQpOptions
        {
            Mu = t.qp_mu, LimFrac = t.qp_lim_frac, OmX = t.qp_om_x, OmZ = t.qp_om_z, KL = t.qp_kL, OmSw = t.qp_om_sw, OmTh = t.qp_om_th, OmPost = t.qp_om_post, OmFp = t.qp_om_fp,
            WCx = t.qp_w_cx, WCz = t.qp_w_cz, WL = t.qp_w_L, WPrior = t.qp_w_prior, WSmooth = t.qp_w_smooth, WSt = t.qp_w_st, WSwing = t.qp_w_swing, WFp = t.qp_w_fp, WTrunk = t.qp_w_trunk, WPost = t.qp_w_post, WReg = t.qp_w_reg, WBreg = t.qp_w_breg,
            Kvs = t.qp_kvs, OnBw = t.qp_on_bw, OffBw = t.qp_off_bw, TOut = t.qp_T_out, TIn = t.qp_T_in, TBlend = t.qp_T_blend, LoadThr = t.qp_load_thr, TdMinFrac = t.qp_td_min_frac, LateVz = t.qp_late_vz, FCap = t.qp_fcap, ZHold = Math.Max(t.qp_z_hold, t.qp_z_hold_ss),
            HoldSched = t.qp_hold_sched > 0.5, Onset = t.qp_onset > 0.5, Land = t.qp_land > 0.5, LandAct = t.qp_land_act > 0.5, FrMeas = t.qp_fr_meas > 0.5, FrMargin = t.qp_fr_margin, LandK0 = t.qp_land_k0, VTd = t.qp_v_td, LandPost = t.qp_land_post,
            MassEst = t.qp_mass_est, MassLo = t.qp_mass_lo, MassHi = t.qp_mass_hi, SgFloor = t.qp_sg_floor, SolverRegularisation = t.qp_solver_reg,
            FootZdip = t.qp_foot > 0.5 && t.qp_plan > 0.5 ? t.qp_foot_zdip : 0.0, FootZdipMax = t.qp_foot_zdip_max, FootZdipMargin = t.qp_foot_zdip_margin, FootZdipUp = t.qp_foot_zdip_up, FootZdipDn = t.qp_foot_zdip_dn,
            FootUnload = t.qp_foot > 0.5 && t.qp_plan > 0.5 ? t.qp_foot_unload : 0.0, FootUnloadAt = t.qp_foot_unload_at,
            Foot = t.qp_foot > 0.5 && t.qp_plan > 0.5, FootPre = t.qp_foot_pre, FootHeelDeg = t.qp_foot_heel_deg, FootStance = t.qp_foot_stance > 0.5, FootW = t.qp_foot_w, FootRate = t.qp_foot_rate, FootDrive = t.qp_foot_drive > 0.5,
            FootMeas = t.qp_foot > 0.5 && t.qp_plan > 0.5 && t.qp_foot_meas > 0.5, FootCopM = t.qp_foot_cop_m, FootFricFloor = t.qp_foot_fric_floor, R7NoForce = t.qp_plan > 0.5 && t.qp_r7_noforce > 0.5, R7Fp = t.qp_plan > 0.5 ? t.qp_r7_fp : 0.0, R8Retime = t.qp_plan > 0.5 && t.qp_r8_retime > 0.5, R8RefClock = t.qp_r8_refclock > 0.5, R8Trem = t.qp_r8_trem > 0.5, R8Gmin = t.qp_r8_gmin, R8Gk = t.qp_r8_gk, R8Gmax = t.qp_r8_gmax, R8SatK = t.qp_r8_satk, R8SatFrac = t.qp_r8_satfrac, R8SwLim = t.qp_plan > 0.5 ? t.qp_r8_swlim : 0.0, R9Gate = t.qp_plan > 0.5 ? t.qp_r9_gate : 0.0, R10Diag = t.qp_plan > 0.5 && t.qp_r10_diag > 0.5, R11Ds = t.qp_plan > 0.5 ? (int)Math.Round(t.qp_r11_ds) : 0, R10Fp = t.qp_plan > 0.5 && t.qp_r7_fp <= 0.0 ? t.qp_r10_fp : 0.0, R10FpMin = t.qp_r10_fp_min, R9GateTmax = t.qp_r9_gate_tmax, R9Launch = t.qp_plan > 0.5 ? t.qp_r9_launch : 0.0, R9Ramp = t.qp_r9_ramp, R9Vfrac = t.qp_r9_vfrac, R9Tau = t.qp_r9_tau, R7Clock = t.qp_plan > 0.5 && t.qp_r7_clock > 0.5, FootCopHeelM = t.qp_foot_cop_heel_m, FootCopToeM = t.qp_foot_cop_toe_m,
            WTau = t.qp_w_tau, YawTrunk = t.qp_yaw_trunk > 0.5, YawFf = t.qp_yaw_ff, YawCap = t.qp_yaw_cap, YawBudget = t.qp_yaw_budget, YawBudgetFloor = t.qp_yaw_budget_floor, ArmRefYawFf = t.qp_arm_ref_yawff, LateCx = t.qp_late_cx, CxEmax = t.qp_cx_emax, Rom = t.qp_rom > 0.5, FpRom = t.qp_rom > 0.5 && t.qp_fp_rom > 0.5, RomMargin = t.qp_rom_margin_deg * Math.PI / 180, RomHorizon = t.qp_rom_horizon_s, RomAmax = t.qp_rom_amax, FpRomBand = t.qp_fp_rom_band_deg * Math.PI / 180, SwJ = t.qp_swj, SwJOm = t.qp_swj_om, SwJAmax = t.qp_swj_amax, SwJTd = t.qp_swj_td_frac, SwJAnkle = t.qp_swj_ankle > 0.5, CdxMax = t.qp_cdx_max, SwCp = t.qp_sw_cp, Plan = t.qp_plan > 0.5, PlanKxi = t.qp_plan_kxi, PlanLmin = t.qp_plan_lmin, PlanLmax = t.qp_plan_lmax, PlanTdFrac = t.qp_plan_td_frac, PlanTdTol = t.qp_plan_td_tol, PlanZkp = t.qp_plan_zkp, PlanZkd = t.qp_plan_zkd, PlanWz = t.qp_plan_wz, PlanWx = t.qp_plan_wx, PlanReplanM = t.qp_plan_replan_m, PlanFp = t.qp_plan_fp, PlanSw = t.qp_plan_sw, PlanApex = t.qp_plan_apex, PlanVd = t.qp_plan_vd, PlanFpTau = t.qp_plan_fp_tau, PlanHeelW = t.qp_plan_heelw, PlanDownFrac = t.qp_plan_down_frac, PlanForceFrac = t.qp_plan_force_frac, PlanForceZ = t.qp_plan_force_z, PlanMaxFrac = t.qp_plan_max_frac, PlanDs = t.qp_plan_ds, PlanReach = t.qp_plan_reach, SwFollow = t.qp_sw_follow, SwAmax = t.qp_sw_amax, TdAir = t.qp_td_air_frac, TdXoffMax = t.qp_td_xoff_max, Diag = t.qp_diag > 0.5, TdHeel = t.qp_td_heel > 0.5, Lateral = t.qp_lateral > 0.5, OmY = t.qp_om_y, WCy = t.qp_w_cy, WRoll = t.qp_w_roll, KRoll = t.qp_k_roll, WYaw = t.qp_w_yaw, OmYaw = t.qp_om_yaw
        };
    }

    /// <summary>
    /// Port of Tools/GaitOpt/qp_proto/controller_fid.py (the 9.3b-5 contact-aware single full inverse-dynamics QP, b5 options) to the engine-free core: one QP per tick over x = [qdd (9, scaled by D), beta (2 friction-pyramid generators
    /// per contact point)] with hard rows (3 unactuated base rows of M qdd + h = CJ' u + Jp' F, beta >= 0, fn <= fmax, 12 torque rows, per-foot shear rows) and weighted tasks (centroidal wrench, stance-point acceleration, swing heel, foot pitch,
    /// trunk, posture, force prior and smoothness, regularisers). Planar coordinates q9 = [hip x, hip z, th, (a b f) leg A, (a b f) leg B]; the caller (<see cref="WholeBodyQpDriver"/>) fills the planar model quantities.
    /// No allocation after construction. The solver is the injected <see cref="IQpSolver"/> (Goldfarb-Idnani, equalities as equality rows; the prototype eliminated them by a null-space basis, same optimum).
    /// </summary>
    public sealed class SagittalQpController
    {
        public const int NQ = 9, NJ = 6, MaxNb = 32, MaxNx = NQ + MaxNb, MaxIn = 128;   // MaxNb: 8 generators x 4 points with Lateral (2 without)
        static readonly int[] CjIdx = { 2, 3, 3, 4, 4, 5 };

        readonly QpReference _ref; readonly SagittalQpOptions _o; readonly IQpSolver _solver; readonly ContactSchedule _sch;
        readonly double _w0, _mt, _l, _g0, _h, _ts;
        readonly double[] _lim = new double[NJ], _cj = new double[NJ * NQ], _pinv = new double[NJ * NQ], _dd = new double[NQ], _n2 = new double[NQ];
        // inputs (planar, SI; filled by the caller before Step)
        public readonly double[] Q = new double[NQ], Qd = new double[NQ], M = new double[NQ * NQ], H = new double[NQ], Pts = new double[8], Jp = new double[4 * 2 * NQ], Bp = new double[8], Com = new double[2], ComD = new double[2], FnMeas = new double[4];
        public double AngMom;          // angular momentum about the CoM, counter-clockwise positive in the (x forward, z up) plane
        public double TimeScale = 1.0;   // step 9.5 hand-off: the caller's clock t runs at this fraction of real time (reference velocities x s, accelerations x s^2; measured velocities enter the planners divided by s). 1 = unchanged
        public int StartNode;          // step 9.5c state-matched hand-off: Init starts at reference node StartNode (0 = the heel strike of leg B) of the first step: contact flags, first-contact forces, torques and the Xoff anchor are those of that node
        public QpReference Reference => _ref;
        /// <summary>Reference joint-space velocity q9 (index i) of the current tick (rad/s, real time), valid after Step.</summary>
        public double RefQd(int i) => _r[QpReference.Cqd + i];
        /// <summary>Step 9.5h: reference shoulder flexion (rad, + hand forward) of the arm on the lateral side of leg A (arm 0) / leg B (arm 1) of the current tick, its rate (rad/s) and acceleration (rad/s2, real time); valid after Step. 0 when the row has no shoulder reference.</summary>
        /// <summary>Step 9.5k: reference planar joint angle q9 (index i, rad) of the current tick, valid after Step.</summary>
        public double RefQ(int i) => _r[QpReference.Cq + i];
        /// <summary>Round 11: generalized acceleration of the last successful solve (planar coordinates [x z theta, a b f leg A, a b f leg B]).</summary>
        public double SolQdd(int i) => _qdd[i];
        /// <summary>Step 9.5k diagnostics (qp_diag): swing leg, reference time, time since the anchor, late, swing blend, contact points in the set (bit mask), swing heel x / z error (target - measured), commanded heel acceleration x / z (m/s2), landing quintic active, swing heel target z, measured z, foot-pitch scale, touchdowns.</summary>
        public readonly double[] Diag = new double[16];
        public double RefArm(int arm) => _r[QpReference.Carm + arm];
        public double RefArmQd(int arm) => _r[QpReference.CarmD + arm] * TimeScale;
        public double RefArmQdd(int arm) => _r[QpReference.CarmDD + arm] * TimeScale * TimeScale;
        public bool AnchorXoff;        // hand-off start (step 9.5): the first Step after Init sets Xoff so that the reference heel of leg B (just landed) is at its measured x
        bool _anchorPending;
        public bool HasFnMeas;         // false only on the first tick (no read-back yet)
        // Lateral (stage 2) inputs, heading frame (x forward, y right, z up: algebraically right-handed in Unity world coordinates): corner c = 2 * point + k (k 0 = -x side, 1 = +x side of the sole), [x, y, z above ground]
        public readonly double[] Corner = new double[8 * 3];
        public double ComY, ComDY, LRoll, LYaw, Izz = 1.0, YawErr, YRef;
        public bool TrunkYaw => _o.YawTrunk;
        public double YawShareLog;   // 9.5f: share of the cone moment budget the yaw target uses (log)
        public double LTrunk, ITrunk = 1.0, DLRest, LLegs, LArms, DLArms, DLArmRefPred;   // 9.5h: DLArms = filtered rate of the arms' yaw momentum, DLArmRefPred = the same predicted from the reference shoulder acceleration (set by the caller)
        /// <summary>Yaw-momentum rate of legs + arms fed forward to the trunk yaw task (9.5h: optionally with the arms' share from the reference).</summary>
        double DLRestFf => DLRest + o_ArmRefYawFfW * (DLArmRefPred - DLArms);
        double o_ArmRefYawFfW => _o.ArmRefYawFf;   // step 9.5e: yaw momentum about the CoM of the trunk (pelvis, torso, head), its yaw inertia, filtered rate of the yaw momentum of the legs and arms
        public readonly double[] YawLog = new double[8];   // log only: [L_trunk, I_trunk, dL_rest/dt, target, L_rest]   // CoM lateral position / velocity, angular momentum about the CoM along forward and up, yaw inertia about the CoM, heading error (rad), lateral CoM target
        /// <summary>Lateral: commanded 3D corner forces [fx forward, fy right, fz up] per corner, N (heading frame).</summary>
        public readonly double[] Force3 = new double[8 * 3];
        /// <summary>Lateral: last commanded [sum fy / W, roll moment / (W l), yaw moment / (W l)] and their targets (log only).</summary>
        public readonly double[] LatCmd = new double[6];
        // outputs
        /// <summary>Step 9.5j: anatomical range (rad) of the six joints [hipA kneeA ankleA hipB kneeB ankleB] for the qp_rom rows; set by the driver (+-inf = unbounded).</summary>
        public readonly double[] RomLo = { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity }, RomHi = { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity };
        /// <summary>Step 9.5j diagnostics: ROM rows added to the last QP (count), ticks where they were infeasible and dropped, bit mask of binding joints (bit j: lower, bit 6 + j: upper bound row active in the last tick), foot-pitch weight scale of leg A / B, contact points in the set.</summary>
        public int RomRows, NS; public long RomRowsDropped; public int RomActive; public readonly double[] FpScale = { 1.0, 1.0 };
        public readonly double[] Tau = new double[NJ];   // joint torques [hipA kneeA ankleA hipB kneeB ankleB], N m, clipped to the limits
        public readonly double[] Force = new double[8];  // commanded contact forces [ft, fn] per point, N
        // diagnostics
        public long Ticks, SolveFailures, TorqueRowsDropped, Held; public QpStatus LastStatus; public int LastIterations; public double FrictionRatioCmd, MassScale = 1.0; public int Parity => _sch.Parity; public int Touchdowns => _sch.TouchdownCount;
        public ContactSchedule Schedule => _sch;
        /// <summary>Normal-force cap per point (body weights) of the last tick.</summary>
        public double[] FmaxBw => _fmax;
        /// <summary>Previous-tick contact forces [ft, fn] per point, N (force smoothness term); for tests that set up a state.</summary>
        public void SetPreviousForces(double[] f8) => Array.Copy(f8, _fprev, 8);
        // state
        readonly double[] _fprev = new double[8], _fmax = new double[4], _fn = new double[4], _pz = new double[4], _px = new double[4], _tdRef = new double[4], _r = new double[QpReference.Channels];
        readonly int[] _sIdx = new int[4]; bool _hasComd; double _comdPrevZ, _tPrev;
        double _planAnchor = double.NaN; int _planPar = -1;
        // step 6a (qp_plan): LIPM constants of the row (omega, step length, capture-point offset b), the stance heel x of the current step, the current foothold / DCM, the end x of the landing quintic
        double _pOm, _pL, _pB, _stHeelX = double.NaN, _pXfp, _planEnd = double.NaN;
        /// <summary>Step 6a diagnostics (qp_plan): target heel x, DCM, reference DCM, desired CoP, CoM-x acceleration of the CoM row, stance heel x, time since touchdown, CoM x.</summary>
        // round 6 (qp_foot): commanded world pitch of each foot (rad, + toe down), its slew state and the diagnostics block (see WalkTuning qp_foot)
        public readonly double[] FootCmd = new double[2]; readonly bool[] _fcSwing = { true, true }; readonly double[] _fcU = new double[2]; double _fcT = double.NaN, _tdPitchDeg;
        public readonly double[] FootLog = new double[15];   // [0,1] measured foot pitch (deg), [2,3] commanded, [4,5] contact phase (0 none, 1 heel, 2 flat, 3 toe), [6] pitch of the landed foot at the last touchdown (deg), [7] measured CoP x, [8] commanded CoP x (m), [9] measured / [10] commanded forward shear (W), [11,12] ankle applied torque / limit (set by the controller owner), [13] swing level-task weight u, [14] CoM height lowering (m)
        double _zDip, _zT = double.NaN, _ikShort;   // step 7c: CoM height lowering (m), its clock, how far (m) and over how much height (m) the swing IK target was out of reach this tick
        public double ZDip => _zDip;
        public double FtMeas;   // summed forward shear force of both feet (N), set by the driver
        public double SwingInertia;   // step 6c: inertia of one leg about its hip (kg m2), set by the driver; 0 = no reach clamp
        double _lastXfer, _apexCmd, _lastApexCmd, _lastReach;
        double _swS = double.NaN, _tSwPrev = double.NaN, _r8g = 1.0;
        double _r9dx, _r9v, _r9tPrev = double.NaN, _r9Held; public double R9GateHeld => _r9Held;      // round 9: CoM launch offset / velocity added to the reference; seconds the lift-off was held by the DCM gate (this step)
        /// <summary>Round 10 (qp_r10_diag = 1): per-step record held until the next touchdown: [0] touchdown time, [1] measured DCM offset of the CoM over the landed heel, [2] the reference's at its touchdown node, [3] measured step length (landed heel - rear heel), [4] reference step length, [5] step period since the previous touchdown (-1 first), [6] reference step time, [7] rear-foot unload time of the step that just ended (s from its touchdown to rear load < 10 % W; -1 never), [8] its reference double-support time, [9] landed foot, [10] CoM speed x at touchdown; per tick: [11] DCM-feedback CoP target before the sole clamp, [12] / [13] the clamp interval (heel + m, toe - m).</summary>
        public readonly double[] R10Log = new double[16]; double _r10Unl = double.NaN, _r10TdPrev = double.NaN, _r10Tds;
        public readonly double[] PlanDiag = new double[18];   // [8..11]: swing apex, landing error vs x_fp, duration of the last step (held until the next touchdown), IK failures
        double _swT0 = double.NaN; readonly double[] _rs = new double[QpReference.Channels], _tl0 = new double[2]; double _tFp, _swX0 = double.NaN, _apex, _lastApex, _lastLand, _lastDur, _ikTPrev = double.NaN; bool _fpReset = true; long _ikFail, _ikClamp; readonly double[] _ikQ = new double[3], _ikQd = new double[3], _ikPrev = new double[3];
        static double MinJerk(double s) { s = Math.Max(0.0, Math.Min(1.0, s)); return s * s * s * (10 + s * (-15 + 6 * s)); }
        static double MinJerkD(double s) { s = Math.Max(0.0, Math.Min(1.0, s)); return 30 * s * s * (1 - s) * (1 - s); }
        static double MinJerkDD(double s) { s = Math.Max(0.0, Math.Min(1.0, s)); return 60 * s * (1 - 3 * s + 2 * s * s); }
        /// <summary>Step 6b: 2-link sagittal IK of the swing leg: hip = measured hip (from the measured heel and joint angles), ankle at the heel target for foot pitch f; gives the hip / knee / ankle joint targets (hip = a + th, knee = a - b, ankle = b + f). False when the target is out of reach.</summary>
        bool PlanIk(int leg, double px, double pz, double f, double t)
        {
            double lt = _ref.ThighM, ls = _ref.ShankM, hx = _ref.HeelX, ha = _ref.AnkleH; int a0 = 3 + 3 * leg; double a = Q[a0], b = Q[a0 + 1], f0 = Q[a0 + 2], th = Q[2];
            double rx = lt * Math.Sin(a) + ls * Math.Sin(b) + hx * Math.Cos(f0) - ha * Math.Sin(f0), rz = -lt * Math.Cos(a) - ls * Math.Cos(b) - hx * Math.Sin(f0) - ha * Math.Cos(f0);
            double hipx = Pts[4 * leg] - rx, hipz = Pts[4 * leg + 1] - rz;
            double ax = px - (hx * Math.Cos(f) - ha * Math.Sin(f)), az = pz + hx * Math.Sin(f) + ha * Math.Cos(f), dx = ax - hipx, dz = az - hipz, D = Math.Sqrt(dx * dx + dz * dz);
            _ikShort = D >= 0.99 * (lt + ls) ? (D - 0.99 * (lt + ls)) * D / Math.Max(Math.Abs(dz), 1.0) : 0.0;   // step 7c: vertical hip drop that brings the heel target back to 0.99 of the leg length
            if (_o.PlanReach > 0 && D >= 0.999 * (lt + ls) && D < 3 * (lt + ls)) { double sc = 0.995 * (lt + ls) / D; dx *= sc; dz *= sc; D = 0.995 * (lt + ls); _ikClamp++; }   // step 6c: a target beyond the leg's reach is projected onto the reachable circle (the leg points at it) instead of replaying the reference joints
            if (!(D < 0.999 * (lt + ls)) || D < Math.Abs(lt - ls) + 0.05 || double.IsNaN(D)) return false;
            double ck = Math.Max(-1.0, Math.Min(1.0, (D * D - lt * lt - ls * ls) / (2 * lt * ls))), k = Math.Acos(ck);
            double phi = Math.Atan2(dx, -dz), psi = Math.Atan2(-ls * Math.Sin(k), lt + ls * Math.Cos(k)), aa = phi - psi, bb = aa - k;
            double ank = bb + f, alo = RomLo[3 * leg + 2] + _o.RomMargin, ahi = RomHi[3 * leg + 2] - _o.RomMargin;
            if (_o.Rom && (ank < alo || ank > ahi))
            {
                // step 6d: the ankle stop forbids a level foot at this leg pose: the foot takes the pitch the ankle can reach and the leg is solved again for it (the swing foot was landing toe first, pitch -10 to -23 deg)
                double f2 = Math.Max(alo, Math.Min(ahi, ank)) - bb; ax = px - (hx * Math.Cos(f2) - ha * Math.Sin(f2)); az = pz + hx * Math.Sin(f2) + ha * Math.Cos(f2); dx = ax - hipx; dz = az - hipz; D = Math.Sqrt(dx * dx + dz * dz);
                if (D < 0.999 * (lt + ls) && D > Math.Abs(lt - ls) + 0.05)
                {
                    ck = Math.Max(-1.0, Math.Min(1.0, (D * D - lt * lt - ls * ls) / (2 * lt * ls))); k = Math.Acos(ck); phi = Math.Atan2(dx, -dz); psi = Math.Atan2(-ls * Math.Sin(k), lt + ls * Math.Cos(k)); aa = phi - psi; bb = aa - k; f = f2;
                }
            }
            _ikQ[0] = aa + th; _ikQ[1] = k; _ikQ[2] = bb + f;
            double dt = t - _ikTPrev;
            for (int j = 0; j < 3; j++) { double v = dt > 1e-6 && dt < 0.2 ? (_ikQ[j] - _ikPrev[j]) / dt : 0.0; _ikQd[j] += 0.3 * (v - _ikQd[j]); _ikPrev[j] = _ikQ[j]; }
            _ikTPrev = t; return true;
        }
        // landing quintics per parity: coefficients [c * 6 + k] for channels x, z, pitch; start tau, duration, end state (p1, v1)
        readonly double[][] _lq = { new double[18], new double[18] }; readonly double[] _lqTau0 = new double[2], _lqTl = new double[2]; readonly double[][] _lqP1 = { new double[3], new double[3] }, _lqV1 = { new double[3], new double[3] };
        readonly double[] _ltPos = new double[3], _ltVel = new double[3], _ltAcc = new double[3];
        // QP scratch
        readonly double[] _hq = new double[MaxNx * MaxNx], _gq = new double[MaxNx], _row = new double[MaxNx], _aeq = new double[3 * MaxNx], _beq = new double[3], _ain = new double[MaxIn * MaxNx], _bin = new double[MaxIn], _ain2 = new double[MaxIn * MaxNx], _bin2 = new double[MaxIn];
        readonly double[] _gm = new double[3 * MaxNb], _em = new double[8 * MaxNb], _jb = new double[NQ * MaxNb], _qx = new double[NQ * MaxNx], _ms = new double[NQ * NQ], _hs = new double[NQ], _qdd = new double[NQ], _qq = new double[NQ], _uu = new double[NJ];
        readonly double[] _wd = new double[3], _vp = new double[8];
        readonly double[] _gxc = new double[MaxNb], _gyc = new double[MaxNb], _fprevY = new double[4]; readonly int[] _cc = new int[MaxNb];

        public SagittalQpController(QpReference reference, SagittalQpOptions options, double[] torqueLimitsNm, IQpSolver solver)
        {
            _ref = reference; _o = options; _solver = solver; solver.Regularisation = options.SolverRegularisation; _g0 = options.G; _l = reference.LegLength; _h = reference.H; _ts = reference.Ts;
            _mt = reference.BodyMassKg; _w0 = _mt * _g0;
            Array.Copy(torqueLimitsNm, _lim, NJ);
            for (int i = 0; i < 2; i++)
            {
                int a = 3 + 3 * i, b = 4 + 3 * i, f = 5 + 3 * i, r = 3 * i;
                _cj[r * NQ + 2] = 1; _cj[r * NQ + a] = 1; _cj[(r + 1) * NQ + a] = 1; _cj[(r + 1) * NQ + b] = -1; _cj[(r + 2) * NQ + b] = 1; _cj[(r + 2) * NQ + f] = 1;
            }
            // pinv(CJ') = (CJ CJ')^-1 CJ ; null space of CJ: base x, base z, and (th, -1 -1 1, -1 -1 1)/sqrt 7
            var g6 = new double[NJ * 2 * NJ];
            for (int i = 0; i < NJ; i++) { for (int j = 0; j < NJ; j++) { double s = 0; for (int c = 0; c < NQ; c++) s += _cj[i * NQ + c] * _cj[j * NQ + c]; g6[i * 2 * NJ + j] = s; } g6[i * 2 * NJ + NJ + i] = 1; }
            for (int c = 0; c < NJ; c++)
            {
                int piv = c; for (int r = c + 1; r < NJ; r++) if (Math.Abs(g6[r * 2 * NJ + c]) > Math.Abs(g6[piv * 2 * NJ + c])) piv = r;
                if (piv != c) for (int j = 0; j < 2 * NJ; j++) { double t = g6[c * 2 * NJ + j]; g6[c * 2 * NJ + j] = g6[piv * 2 * NJ + j]; g6[piv * 2 * NJ + j] = t; }
                double inv = 1.0 / g6[c * 2 * NJ + c]; for (int j = 0; j < 2 * NJ; j++) g6[c * 2 * NJ + j] *= inv;
                for (int r = 0; r < NJ; r++) { if (r == c) continue; double f = g6[r * 2 * NJ + c]; for (int j = 0; j < 2 * NJ; j++) g6[r * 2 * NJ + j] -= f * g6[c * 2 * NJ + j]; }
            }
            for (int i = 0; i < NJ; i++) for (int c = 0; c < NQ; c++) { double s = 0; for (int k = 0; k < NJ; k++) s += g6[i * 2 * NJ + NJ + k] * _cj[k * NQ + c]; _pinv[i * NQ + c] = s; }
            double rt7 = 1.0 / Math.Sqrt(7.0); _n2[2] = rt7; for (int i = 0; i < 2; i++) { _n2[3 + 3 * i] = -rt7; _n2[4 + 3 * i] = -rt7; _n2[5 + 3 * i] = rt7; }
            _dd[0] = _dd[1] = _g0; for (int j = 2; j < NQ; j++) _dd[j] = _g0 / _l;
            var sopt = new ContactScheduleOptions { OnBw = options.OnBw, OffBw = options.OffBw, TOut = options.TOut, TIn = options.TIn, TBlend = options.TBlend, TdMinFrac = options.Plan ? options.PlanTdFrac : options.TdMinFrac, LateVz = options.LateVz, FCap = options.FCap, ZHold = options.ZHold, HoldSched = options.HoldSched, HeelOnly = options.TdHeel, MinAirFrac = options.TdAir, XoffMax = options.TdXoffMax, MissedAfterS = 0.5 };
            _sch = new ContactSchedule(reference.Flags, reference.N2, _h, _ts, sopt);
            if (options.Plan)
            {
                double zs = 0; for (int k = 0; k < reference.N; k++) zs += reference.Node(QpReference.Ccom + 1, k);
                for (int p = 0; p < 2; p++) { _tl0[p] = 0.75 * _ts; for (int k = 0; k < reference.N; k++) { double tk = p * _ts + k * _h; if (!_sch.Scheduled(2 * p, tk) && !_sch.Scheduled(2 * p + 1, tk)) { _tl0[p] = k * _h; break; } } }   // reference lift-off of the swing leg of parity p (s into the step)
                _pOm = Math.Sqrt(_g0 / (zs / reference.N)); _pL = reference.StepLength; _pB = _pL / (Math.Exp(_pOm * _ts) - 1.0); _sch.PlanOn = true;
            }
            if (options.Land) { BuildLand(0); BuildLand(1); }
            Init();
        }

        /// <summary>The prototype's k &lt; 0 call: start at reference node 0 with its contact state; Tau = the reference torques there.</summary>
        public void Init()
        {
            _sch.Reset(0);
            for (int i = 0; i < 4; i++) { _sch.Inc[i] = _ref.Flags[StartNode * 4 + i]; _sch.FIn[i] = _ref.Node(QpReference.Cf + 2 * i + 1, StartNode); _sch.TIn[i] = -1.0; }
            _anchorPending = AnchorXoff; Array.Clear(_fprev, 0, 8); Array.Clear(_fprevY, 0, 4); MassScale = 1.0; _hasComd = false; _planAnchor = double.NaN; _planPar = -1; _stHeelX = double.NaN; _planEnd = double.NaN; _fpReset = true; _swX0 = double.NaN; _swT0 = double.NaN; _swS = double.NaN; _r8g = 1.0; _apex = 0; _ikTPrev = double.NaN; Array.Clear(_ikQd, 0, 3);
            if (StartNode == 0) Array.Copy(_ref.Tau0, Tau, NJ); else _ref.TauAt(StartNode, Tau);
        }

        // ------------------------------------------------------------------ landing quintic
        static void Solve3(double t, double b0, double b1, double b2, out double c3, out double c4, out double c5)
        {
            double t2 = t * t, t3 = t2 * t, t4 = t3 * t, t5 = t4 * t;
            double a00 = t3, a01 = t4, a02 = t5, a10 = 3 * t2, a11 = 4 * t3, a12 = 5 * t4, a20 = 6 * t, a21 = 12 * t2, a22 = 20 * t3;
            double det = a00 * (a11 * a22 - a12 * a21) - a01 * (a10 * a22 - a12 * a20) + a02 * (a10 * a21 - a11 * a20);
            c3 = (b0 * (a11 * a22 - a12 * a21) - a01 * (b1 * a22 - a12 * b2) + a02 * (b1 * a21 - a11 * b2)) / det;
            c4 = (a00 * (b1 * a22 - a12 * b2) - b0 * (a10 * a22 - a12 * a20) + a02 * (a10 * b2 - b1 * a20)) / det;
            c5 = (a00 * (a11 * b2 - b1 * a21) - a01 * (a10 * b2 - b1 * a20) + b0 * (a10 * a21 - a11 * a20)) / det;
        }

        void BuildLand(int par)
        {
            double tau0 = _o.LandK0 * _h, tl = _ts - tau0; int fi = 5 + 3 * par, i = 2 * par; var r0 = new double[QpReference.Channels]; var r1 = new double[QpReference.Channels]; var r1b = new double[QpReference.Channels];
            _ref.At(par * _ts + tau0, r0); _ref.At((par + 1) * _ts, r1); _ref.At((par + 1) * _ts + _h, r1b);
            double[] p0 = { r0[QpReference.Cpts + 2 * i], r0[QpReference.Cpts + 2 * i + 1], r0[QpReference.Cq + fi] };
            double[] v0 = { r0[QpReference.Cptv + 2 * i], r0[QpReference.Cptv + 2 * i + 1], r0[QpReference.Cqd + fi] };
            double[] a0 = { r0[QpReference.Cpta + 2 * i], r0[QpReference.Cpta + 2 * i + 1], r0[QpReference.Cqdd + fi] };
            double[] p1 = { r1[QpReference.Cpts + 2 * i], 0.0, r1[QpReference.Cq + fi] }; double[] v1 = { 0.0, -_o.VTd, (r1b[QpReference.Cq + fi] - r1[QpReference.Cq + fi]) / _h };
            var co = _lq[par];
            for (int c = 0; c < 3; c++)
            {
                Solve3(tl, p1[c] - (p0[c] + v0[c] * tl + 0.5 * a0[c] * tl * tl), v1[c] - (v0[c] + a0[c] * tl), 0.0 - a0[c], out double c3, out double c4, out double c5);
                co[c * 6] = p0[c]; co[c * 6 + 1] = v0[c]; co[c * 6 + 2] = 0.5 * a0[c]; co[c * 6 + 3] = c3; co[c * 6 + 4] = c4; co[c * 6 + 5] = c5;
            }
            _lqTau0[par] = tau0; _lqTl[par] = tl; for (int c = 0; c < 3; c++) { _lqP1[par][c] = p1[c]; _lqV1[par][c] = v1[c]; }
        }

        /// <summary>land_act: quintic from the measured state (x0, v0) at tau to the landing state of the reference (+ Xoff), ending at the step time; the heel x of the stored coefficients excludes Xoff.</summary>
        void Replan(int par, double tau, double x0x, double x0z, double x0f, double v0x, double v0z, double v0f, double xoff, double xEnd = double.NaN)
        {
            double t = Math.Max(_ts - tau, 2 * _h); var co = _lq[par]; var p1 = _lqP1[par]; var v1 = _lqV1[par];
            for (int c = 0; c < 3; c++)
            {
                double x0 = c == 0 ? x0x : c == 1 ? x0z : x0f, v0 = c == 0 ? v0x : c == 1 ? v0z : v0f, pe = c == 0 && !double.IsNaN(xEnd) ? xEnd : p1[c] + (c == 0 ? xoff : 0.0);
                Solve3(t, pe - (x0 + v0 * t), v1[c] - v0, 0.0, out double c3, out double c4, out double c5);
                co[c * 6] = x0; co[c * 6 + 1] = v0; co[c * 6 + 2] = 0.0; co[c * 6 + 3] = c3; co[c * 6 + 4] = c4; co[c * 6 + 5] = c5;
            }
            co[0] -= xoff; _lqTau0[par] = tau; _lqTl[par] = t; _planAnchor = _sch.TAnchor; _planPar = _sch.Parity;
        }

        void LandTarget(int par, double tau)
        {
            double t0 = _lqTau0[par], tl = _lqTl[par], s = Math.Min(Math.Max(tau - t0, 0.0), tl), ex = Math.Max(0.0, tau - t0 - tl); var co = _lq[par];
            for (int c = 0; c < 3; c++)
            {
                int b = c * 6; double c0 = co[b], c1 = co[b + 1], c2 = co[b + 2], c3 = co[b + 3], c4 = co[b + 4], c5 = co[b + 5];
                _ltPos[c] = c0 + s * (c1 + s * (c2 + s * (c3 + s * (c4 + s * c5))));
                _ltVel[c] = c1 + s * (2 * c2 + s * (3 * c3 + s * (4 * c4 + s * 5 * c5)));
                _ltAcc[c] = 2 * c2 + s * (6 * c3 + s * (12 * c4 + s * 20 * c5));
            }
            if (ex > 0) { _ltPos[1] -= _o.VTd * ex; _ltVel[0] = 0; _ltVel[1] = -_o.VTd; _ltVel[2] = 0; _ltAcc[0] = _ltAcc[1] = _ltAcc[2] = 0; }
        }

        // ------------------------------------------------------------------ QP assembly helpers
        void AddRow(int nx, double w, double y)
        {
            if (w <= 0) return;
            for (int i = 0; i < nx; i++)
            {
                double ti = _row[i]; if (ti == 0) continue; double wt = w * ti; _gq[i] -= wt * y; int o = i * nx;
                for (int j = 0; j < nx; j++) _hq[o + j] += wt * _row[j];
            }
        }
        void ClearRow(int nx) { for (int i = 0; i < nx; i++) _row[i] = 0; }

        double _ydT;   // yaw ground-moment target of this tick (N m), set by YawMoment before the rows are built
        /// <summary>Yaw ground-moment target: whole-body heading task, or with YawTrunk the trunk task with the legs' and arms' yaw-momentum rate feed-forward (9.5e). Logs YawLog.</summary>
        double YawMoment(double W, double ms)
        {
            var o = _o; double l = _l;
            double myd = -Izz * ms * (o.OmYaw * o.OmYaw * YawErr + 2 * o.OmYaw * (LYaw / Math.Max(Izz, 1e-9)));
            if (o.YawTrunk) { double cap = o.YawCap * W * l; myd = Math.Max(-cap, Math.Min(cap, -(ITrunk * o.OmYaw * o.OmYaw * YawErr + 2 * o.OmYaw * LTrunk) + o.YawFf * DLRestFf)); YawLog[0] = LTrunk; YawLog[1] = ITrunk; YawLog[2] = DLRest; YawLog[3] = myd; YawLog[4] = LYaw - LTrunk; YawLog[5] = LLegs; YawLog[6] = LArms; }
            return myd;
        }
        /// <summary>Step 9.5f yaw-aware sagittal budget: share of the yaw moment the friction cone can carry (mu' W r_eff, r_eff the mean plan distance of the contact corners from the CoM) that the yaw target uses.</summary>
        double YawShare(double myd, double W, int nS, int gpp, double cx)
        {
            double rs = 0; int n = 0;
            for (int a = 0; a < nS; a++) { int i = _sIdx[a]; for (int k = 0; k < 2; k++) { int q = 2 * i + k; double px = Corner[3 * q] - cx, py = Corner[3 * q + 1] - ComY; rs += Math.Sqrt(px * px + py * py); n++; } }
            if (n == 0) return 0.0;
            double b = _o.Mu * _o.FrMargin * W * rs / n;
            return b > 1e-9 ? Math.Min(1.5, Math.Abs(myd) / b) : 0.0;
        }

        /// <summary>Stage 2 rows on the generators: lateral force = m (om_y^2 (y_ref - y) - 2 om_y vy), roll moment = -k_roll L_roll, yaw moment = -Izz (om_yaw^2 e + 2 om_yaw w), plus prior (fy = 0) and smoothness on the per-point lateral force. Normalised by W, W l.</summary>
        void LateralRows(int nx, int nS, int gpp, double ms, double W, double cx, double cz)
        {
            var o = _o; double l = _l, mt = _mt * ms;
            double ay = o.OmY * o.OmY * (YRef - ComY) - 2 * o.OmY * ComDY;
            double mrd = -o.KRoll * LRoll * ms, myd = _ydT;
            LatCmd[3] = mt * ay / W; LatCmd[4] = mrd / (W * l); LatCmd[5] = myd / (W * l);
            int nb = gpp * nS;
            for (int k = 0; k < 3; k++)
            {
                ClearRow(nx);
                for (int c = 0; c < nb; c++)
                {
                    int q = _cc[c]; double gx = _gxc[c], gy = _gyc[c], px = Corner[3 * q] - cx, py = Corner[3 * q + 1] - ComY, pz = Corner[3 * q + 2] - cz;
                    _row[NQ + c] = k == 0 ? gy : k == 1 ? (py - pz * gy) / l : (px * gy - py * gx) / l;   // gz = 1
                }
                AddRow(nx, k == 0 ? o.WCy : k == 1 ? o.WRoll : o.WYaw, LatCmd[3 + k]);
            }
            for (int a = 0; a < nS; a++)
            {
                int i = _sIdx[a]; ClearRow(nx); for (int b = 0; b < gpp; b++) _row[NQ + gpp * a + b] = _gyc[gpp * a + b];
                AddRow(nx, o.WPrior, 0.0); AddRow(nx, o.WSmooth, _fprevY[i] / W);
            }
        }

        /// <summary>Round 6 (qp_foot): commanded pitch of a leg's foot. A leg in contact (or not the swing leg) slews from its measured pitch to level at FootRate; the swing leg follows the reference pitch until the swing progress FootPre, then a smoothstep to a heel-first level (-FootHeelDeg).</summary>
        void FootUpdate(int leg, int ns, int swLeg, double tref, double late, double fRef, double fMeas, double dtc)
        {
            var o = _o;
            if (ns == 0 && leg == swLeg)
            {
                double tl0 = _tl0[leg], sp = Math.Max(0.0, Math.Min(1.0, (tref - leg * _ts - tl0) / Math.Max(_ts - tl0, 1e-3))), x = late > 0 ? 1.0 : Math.Max(0.0, Math.Min(1.0, (sp - o.FootPre) / Math.Max(1.0 - o.FootPre, 1e-3)));
                double u = x * x * (3 - 2 * x); _fcU[leg] = u; _fcSwing[leg] = true;
                FootCmd[leg] = (1 - u) * fRef + u * (-o.FootHeelDeg * Math.PI / 180.0);
            }
            else
            {
                _fcU[leg] = 0; if (_fcSwing[leg]) { FootCmd[leg] = fMeas; _fcSwing[leg] = false; }
                double tgt = o.FootStance ? 0.0 : fRef, st = o.FootRate * dtc; FootCmd[leg] += Math.Max(-st, Math.Min(st, tgt - FootCmd[leg]));
            }
        }

        /// <summary>Round 9 lift-off gate (qp_r9_gate = f > 0): the swing does not start (the reference clock stays at the lift-off node) until the measured DCM offset of the CoM over the stance heel reaches f x the reference's at that node, or qp_r9_gate_tmax s after the reference lift-off time.</summary>
        bool R9Hold(double tau0, double tds, int sw)
        {
            if (_o.R9Gate <= 0 || !double.IsNaN(_swT0) || tau0 < 0.5 * tds) { if (_o.R9Gate > 0 && !double.IsNaN(_swT0)) { } else _r9Held = 0.0; return false; }
            int st = 1 - sw, kd = Math.Min(_ref.N - 1, Math.Max(0, (int)Math.Round(tds / _h))); double om = _pOm;
            double bref = _ref.NodeDcmOffsetAt(sw * _ref.N + kd, 2 * st, om), bm = Com[0] + ComD[0] / om - _px[2 * st];
            bool hold = tau0 < tds + _o.R9GateTmax && bm < _o.R9Gate * bref; _r9Held = hold ? Math.Max(0.0, tau0 - tds) : _r9Held; PlanDiag[17] = bm / Math.Max(bref, 1e-6);
            return hold;
        }

        /// <summary>Step 6c: swing clock from the measured lift-off (rear-leg load under 10 % of the weight); the reference clock holds at the lift-off node until then and runs 1:1 after it.</summary>
        void PlanClock(double t, double W)
        {
            int sw = _sch.Parity; double tau0 = t - _sch.TAnchor, tds = _tl0[sw], tsw = _ts - tds, rear = _fn[2 * sw] + _fn[2 * sw + 1];
            bool r9hold = R9Hold(tau0, tds, sw);
            if (_o.R10Diag) { _r10Tds = tds; if (double.IsNaN(_r10Unl) && rear < 0.1 * W) _r10Unl = tau0; }
            if (double.IsNaN(_swT0) && !r9hold && tau0 >= 0.5 * tds && rear < 0.1 * W)
            {
                _lastXfer = tau0;
                if (tau0 > tds) { double m0 = MinJerk((tau0 - tds) / tsw); _swT0 = tds; _swX0 = m0 < 0.9 && !double.IsNaN(_pXfp) ? (_px[2 * sw] - _pXfp * m0) / (1.0 - m0) : _px[2 * sw]; }
                else { _swT0 = tau0; _swX0 = _px[2 * sw]; }
            }
            if (double.IsNaN(_swT0) && !r9hold && _o.R7Clock && tau0 >= tds) { _lastXfer = tau0; _swT0 = tds; _swX0 = _px[2 * sw]; }   // round 7: the swing starts at the reference end of the double support
            if (_o.R8Retime && !double.IsNaN(_swT0))
            {
                // round 8: the plan phase of the swing (s since the lift-off, advances at g <= 1 while the foot lags the min-jerk curve or the swing hip is saturated)
                if (double.IsNaN(_swS)) { _swS = Math.Max(0.0, tau0 - _swT0); _tSwPrev = t; _r8g = 1.0; }
                else
                {
                    double dt = Math.Max(0.0, Math.Min(0.1, t - _tSwPrev)); _tSwPrev = t;
                    double dsw0 = Math.Max(tsw, 1e-3), dx = _pXfp - _swX0, m = MinJerk(_swS / dsw0), p = Math.Abs(dx) > 0.5 ? Math.Max(-0.5, Math.Min(1.2, (_px[2 * sw] - _swX0) / dx)) : m, lag = m - p;
                    double g = 1.0 - _o.R8Gk * lag;
                    if (_o.R8SatK > 0 && _lim[3 * sw] > 0) { double sat = Math.Max(0.0, Math.Min(1.0, (Math.Abs(Tau[3 * sw]) / _lim[3 * sw] - _o.R8SatFrac) / Math.Max(1.0 - _o.R8SatFrac, 1e-3))); g *= 1.0 - _o.R8SatK * sat; }
                    _r8g = Math.Max(_o.R8Gmin, Math.Min(_o.R8Gmax, g)); _swS += _r8g * dt; PlanDiag[15] = _r8g;
                }
            }
            _sch.PlanRefTau = double.IsNaN(_swT0) ? Math.Min(tau0, tds) : tds + (_o.R8Retime && !_o.R8RefClock && !double.IsNaN(_swS) ? _swS : tau0 - _swT0);
            _sch.PlanMinTau = double.IsNaN(_swT0) ? _ts : _o.R8Retime && !double.IsNaN(_swS) ? tau0 + Math.Max(0.0, 0.8 * tsw - _swS) : _swT0 + 0.8 * tsw;
            _sch.PlanForceTau = double.IsNaN(_swT0) ? _ts : _o.R8Retime && !double.IsNaN(_swS) ? tau0 + Math.Max(0.0, tsw - _swS) : _swT0 + tsw;
        }

        /// <summary>Step 6a: foothold of the current step from the capture point (see qp_plan in WalkTuning); sets the touchdown target of the schedule.</summary>
        void PlanEval(double t, double W)
        {
            int st = 1 - _sch.Parity; double om = _pOm;
            if (double.IsNaN(_stHeelX)) _stHeelX = _px[2 * st];
            double xi = Com[0] + ComD[0] / om, fh = _fn[2 * st], ft = _fn[2 * st + 1];
            double r8Stretch = 0.0; if (_o.R8Retime && _o.R8Trem && !double.IsNaN(_swS)) { double tswp = _ts - _tl0[_sch.Parity]; r8Stretch = Math.Max(0.0, tswp - _swS) * (1.0 / Math.Max(_r8g, 1e-3) - 1.0); }   // round 8: remaining real time of the retimed swing beyond the plan
            double xcop = fh + ft > 0.05 * W ? (fh * _px[2 * st] + ft * _px[2 * st + 1]) / (fh + ft) : 0.5 * (_px[2 * st] + _px[2 * st + 1]);
            double trem = Math.Max(_ts - (t - _sch.TAnchor), 0.05) + r8Stretch, xip = xcop + (xi - xcop) * Math.Exp(om * trem);
            double ex = Math.Exp(om * _ts) - 1.0, x1 = Math.Min(Math.Max(xip - _pB, _stHeelX + _o.PlanLmin * _pL), _stHeelX + _o.PlanLmax * _pL);
            double b1 = (x1 - _stHeelX) / ex, raw = Math.Min(Math.Max(xip - b1, _stHeelX + _o.PlanLmin * _pL), _stHeelX + _o.PlanLmax * _pL);   // step 6c: b re-evaluated for the planned step length
            if (_o.R7Fp > 0)
            {
                // round 7: the landing heel keeps the DCM offset of the reference at the touchdown node (xi_ref - heel_ref there: 6-10 m in the round-7 rows, the LIPM point-foot b is 2.7-7.5 m)
                int ldg = _sch.Parity, np = 1 - ldg; double dref = _ref.NodeDcmOffset(np, 2 * ldg, om);
                raw = Math.Min(Math.Max(xip - ((1.0 - _o.R7Fp) * b1 + _o.R7Fp * dref), _stHeelX + _o.PlanLmin * _pL), _stHeelX + _o.PlanLmax * _pL);
            }
            if (_o.R10Fp > 0)
            {
                // round 10 (qp_r10_fp = f): as qp_r7_fp, but the reference DCM offset at the touchdown node is scaled by the share of the reference speed the CoM has now (offset over the heel ~ v / omega at touchdown), floored at qp_r10_fp_min; the LIPM b1 is kept for the rest
                int ldg = _sch.Parity, np = 1 - ldg; double dref = _ref.NodeDcmOffset(np, 2 * ldg, om), sc = Math.Max(_o.R10FpMin, Math.Min(1.0, ComD[0] / om / Math.Max(dref, 1e-6)));
                raw = Math.Min(Math.Max(xip - ((1.0 - _o.R10Fp) * b1 + _o.R10Fp * dref * sc), _stHeelX + _o.PlanLmin * _pL), _stHeelX + _o.PlanLmax * _pL);
            }
            if (_o.PlanReach > 0 && SwingInertia > 0)
            {
                int sw = _sch.Parity; double tr2 = Math.Max(_ts - (t - _sch.TAnchor), 0.2), alpha = _o.PlanReach * _lim[3 * sw] / SwingInertia, dth = alpha * 0.25 * tr2 * tr2;
                double reach = _px[2 * sw] + Math.Max(ComD[0], 0.0) * tr2 + (_ref.ThighM + _ref.ShankM) * Math.Sin(Math.Min(dth, 1.2)); _lastReach = reach; if (raw > reach) raw = Math.Max(reach, _stHeelX + 0.3 * _pL);
            }
            if (_fpReset || double.IsNaN(_pXfp)) { _pXfp = raw; _fpReset = false; } else { double dtf = t - _tFp; if (dtf > 0) _pXfp += Math.Min(1.0, dtf / Math.Max(_o.PlanFpTau, 1e-3)) * (raw - _pXfp); }
            _tFp = t;
            _sch.PlanTarget = _pXfp; _sch.PlanTol = _o.PlanTdTol * _pL; PlanDiag[0] = _pXfp; PlanDiag[1] = xi; PlanDiag[5] = _stHeelX;
        }

        /// <summary>One control tick at clock time <paramref name="t"/> (s): fills <see cref="Tau"/> and returns true; on an infeasible QP the previous torques stay and false is returned.</summary>
        public bool Step(double t)
        {
            var o = _o; Ticks++; double g0 = _g0, l = _l, h = _h, ts = _ts;
            // online mass-scale estimate from the last tick's read-back
            if (o.MassEst > 0)
            {
                if (HasFnMeas && _hasComd && t > _tPrev + 1e-9)
                {
                    double den = g0 + (ComD[1] - _comdPrevZ) / ((t - _tPrev) / TimeScale), ftot = FnMeas[0] + FnMeas[1] + FnMeas[2] + FnMeas[3];
                    if (den > 0.5 * g0 && ftot > 0.4 * _w0) MassScale = Math.Min(o.MassHi, Math.Max(o.MassLo, MassScale + o.MassEst * (ftot / (_mt * den) - MassScale)));
                }
                _comdPrevZ = ComD[1]; _tPrev = t; _hasComd = true;
            }
            double ms = MassScale, W = _w0 * ms;
            for (int i = 0; i < NQ * NQ; i++) _ms[i] = M[i] * ms; for (int i = 0; i < NQ; i++) _hs[i] = H[i] * ms;
            for (int i = 0; i < 4; i++) { _vp[2 * i] = 0; _vp[2 * i + 1] = 0; for (int j = 0; j < NQ; j++) { _vp[2 * i] += Jp[i * 18 + j] * Qd[j]; _vp[2 * i + 1] += Jp[i * 18 + 9 + j] * Qd[j]; } _fn[i] = FnMeas[i]; _pz[i] = Pts[2 * i + 1]; _px[i] = Pts[2 * i]; }
            if (_anchorPending) { _sch.Xoff = Pts[4] - _ref.Node(QpReference.Cpts + 4, StartNode); _anchorPending = false; }
            // measured contact, touchdown / phase
            if (o.Plan && o.PlanDs > 0.5) PlanClock(t, W);
            _sch.UpdateContact(t, W, _fn, _pz);
            if (o.Plan) PlanEval(t, W);
            int newPar = 1 - _sch.Parity; for (int p = 0; p < 4; p++) _tdRef[p] = _ref.NodePointX(newPar, p);
            double tAnchOld = _sch.TAnchor;
            if (o.Plan)
            {
                _apex = Math.Max(_apex, _pz[2 * _sch.Parity]);
                if (!_sch.TryTouchdown(t, _px, _tdRef))
                {
                    double tau2 = t - tAnchOld;
                    if (!o.R7NoForce && (tau2 >= o.PlanMaxFrac * _ts || (tau2 >= o.PlanForceFrac * _ts && _pz[2 * _sch.Parity] < o.PlanForceZ))) _sch.ForceTouchdown(t, _px, _tdRef);
                }
                if (_sch.LastCallReanchored)
                {
                    int ld = 1 - _sch.Parity; _tdPitchDeg = Q[5 + 3 * ld] * 180.0 / Math.PI; _lastLand = _px[2 * ld] - _pXfp; _lastApex = _apex; _lastApexCmd = _apexCmd; _apexCmd = 0; _lastDur = t - tAnchOld; _apex = 0; _swX0 = double.NaN; _swT0 = double.NaN; _swS = double.NaN; _r8g = 1.0; _fpReset = true; _ikTPrev = double.NaN;
                    if (_o.R10Diag)
                    {
                        int rr = 1 - ld; double om10 = _pOm;
                        R10Log[0] = t; R10Log[1] = Com[0] + ComD[0] / om10 - _px[2 * ld]; R10Log[2] = _ref.NodeDcmOffset(rr, 2 * ld, om10); R10Log[3] = _px[2 * ld] - _px[2 * rr]; R10Log[4] = _pL;
                        R10Log[5] = double.IsNaN(_r10TdPrev) ? -1.0 : t - _r10TdPrev; R10Log[6] = _ts; R10Log[7] = double.IsNaN(_r10Unl) ? -1.0 : _r10Unl; R10Log[8] = _r10Tds; R10Log[9] = ld; R10Log[10] = ComD[0];
                        _r10TdPrev = t; _r10Unl = double.NaN;
                    }
                    _stHeelX = _px[2 * ld]; PlanEval(t, W);
                }
                PlanDiag[8] = _lastApex; PlanDiag[9] = _lastLand; PlanDiag[10] = _lastDur; PlanDiag[11] = _ikFail; PlanDiag[12] = _lastXfer; PlanDiag[13] = _lastApexCmd; PlanDiag[14] = _lastReach;
            }
            else _sch.TryTouchdown(t, _px, _tdRef);
            int swLeg = _sch.Parity, stLeg = 1 - swLeg; double tau = t - _sch.TAnchor, late = _sch.Late(t), tref = _sch.RefTime(t), xoff = _sch.Xoff;
            _ref.At(tref, _r); var r = _r;
            double tsc = TimeScale;
            if (tsc != 1.0)
            {
                double s2 = tsc * tsc;
                for (int i = 0; i < NQ; i++) { r[QpReference.Cqd + i] *= tsc; r[QpReference.Cqdd + i] *= s2; }
                for (int i = 0; i < 2; i++) { r[QpReference.Ccomd + i] *= tsc; r[QpReference.Ccomdd + i] *= s2; }
                r[QpReference.Cl] *= tsc; r[QpReference.Cldot] *= s2;
                for (int i = 0; i < 8; i++) { r[QpReference.Cptv + i] *= tsc; r[QpReference.Cpta + i] *= s2; }
            }
            if (o.R9Launch > 0 && o.Plan)
            {
                // round 9 launch: until the swing lifts off, the reference CoM gets a forward velocity bias (and its integral) in proportion to the speed deficit against the reference at the lift-off node
                double dt9 = double.IsNaN(_r9tPrev) ? 0.0 : Math.Max(0.0, Math.Min(0.1, t - _r9tPrev)); _r9tPrev = t;
                if (_sch.LastCallReanchored) { _r9dx = 0.0; _r9v = 0.0; }
                if (double.IsNaN(_swT0))
                {
                    int kd9 = Math.Min(_ref.N - 1, Math.Max(0, (int)Math.Round(_tl0[swLeg] / _h))); double vt = Math.Max(_ref.Node(QpReference.Ccomd, swLeg * _ref.N + kd9) * tsc, 1e-6) * o.R9Vfrac;
                    double def = Math.Max(0.0, Math.Min(1.0, (vt - ComD[0]) / vt)), ramp = o.R9Ramp > 1e-6 ? Math.Min(1.0, tau / o.R9Ramp) : 1.0;
                    _r9v = o.R9Launch * def * ramp; _r9dx += _r9v * dt9;
                }
                else { _r9v = 0.0; _r9dx *= Math.Exp(-dt9 / Math.Max(o.R9Tau, 1e-3)); }
                r[QpReference.Ccom] += _r9dx; r[QpReference.Ccomd] += _r9v; PlanDiag[16] = _r9dx;
            }
            // contact set and force caps
            int nS = 0; for (int i = 0; i < 4; i++) if (_sch.Inc[i]) _sIdx[nS++] = i;
            bool lat = o.Lateral; int gpp = lat ? 8 : 2;   // generators per point
            int nb = gpp * nS, nx = NQ + nb;
            for (int i = 0; i < 4; i++) _fmax[i] = 0;
            for (int a = 0; a < nS; a++) { int i = _sIdx[a]; _fmax[i] = _sch.Fmax(i, t, tref, W); }
            bool sgOn = o.SgFloor > 0 && !(_sch.Inc[2 * swLeg] || _sch.Inc[2 * swLeg + 1]);
            if (sgOn) for (int a = 0; a < nS; a++) { int i = _sIdx[a]; if (i / 2 == stLeg) _fmax[i] = Math.Max(_fmax[i], Math.Min(o.SgFloor, _sch.FIn[i] / W + (t - _sch.TIn[i]) / o.TIn)); }
            if (o.Foot && o.FootUnload > 0 && o.Plan && double.IsNaN(_swT0) && nS > 0)
            {
                // step 7c: forced load transfer: the rear (swing-parity) leg's normal-force cap ramps to zero once the front leg carries the weight
                int rl = _sch.Parity, fl = 1 - rl; double tds = Math.Max(_tl0[rl], 1e-3), ur = Math.Max(0.0, Math.Min(1.0, (tau - o.FootUnloadAt * tds) / o.FootUnload));
                if (ur > 0 && (_fn[2 * fl] + _fn[2 * fl + 1]) > 0.3 * W) { double cap = o.FCap * (1.0 - ur); for (int a = 0; a < nS; a++) { int i = _sIdx[a]; if (i / 2 == rl) _fmax[i] = Math.Min(_fmax[i], cap); } }
            }
            // generators: Gm (3 x nb), Em (2 nS x nb), Jb (9 x nb)
            Array.Clear(_gm, 0, _gm.Length); Array.Clear(_em, 0, _em.Length); Array.Clear(_jb, 0, _jb.Length);
            double cx = Com[0], cz = Com[1];
            for (int a = 0; a < nS; a++)
            {
                int i = _sIdx[a];
                for (int b = 0; b < gpp; b++)
                {
                    int col = gpp * a + b, m = b & 3; double mu1 = o.Mu * o.FrMargin, gx = lat ? (m == 0 ? mu1 : m == 1 ? -mu1 : 0.0) : (b == 0 ? 1.0 : -1.0) * mu1, gz = 1.0;
                    _gxc[col] = gx; _gyc[col] = lat ? (m == 2 ? mu1 : m == 3 ? -mu1 : 0.0) : 0.0; _cc[col] = 2 * i + (b >> 2);
                    _gm[col] = gx; _gm[MaxNb + col] = gz; _gm[2 * MaxNb + col] = ((Pts[2 * i] - cx) * gz - (Pts[2 * i + 1] - cz) * gx) / l;
                    _em[(2 * a) * MaxNb + col] = gx; _em[(2 * a + 1) * MaxNb + col] = gz;
                    for (int j = 0; j < NQ; j++) _jb[j * MaxNb + col] = W * (Jp[i * 18 + j] * gx + Jp[i * 18 + 9 + j] * gz);
                }
            }
            double wcxEff = o.WCx; YawShareLog = 0;
            if (lat) { _ydT = YawMoment(W, ms); if (o.YawTrunk && o.YawBudget > 0 && nb > 0) { YawShareLog = YawShare(_ydT, W, nS, gpp, cx); wcxEff = o.WCx * Math.Max(o.YawBudgetFloor, 1.0 - o.YawBudget * YawShareLog); } }
            // ---- quadratic cost
            Array.Clear(_hq, 0, nx * nx); Array.Clear(_gq, 0, nx);
            double cxr = r[QpReference.Ccom] + (late > 0 ? o.LateCx * late * r[QpReference.Ccomd] / tsc : 0.0);
            double ecx = cxr + xoff - cx; if (o.CxEmax > 0) ecx = Math.Max(-o.CxEmax, Math.Min(o.CxEmax, ecx));
            double cdx = r[QpReference.Ccomdd] + o.OmX * o.OmX * ecx + 2 * o.OmX * (r[QpReference.Ccomd] - ComD[0]);
            if (o.CdxMax > 0) cdx = Math.Max(-o.CdxMax, Math.Min(o.CdxMax, cdx));
            double cdz = r[QpReference.Ccomdd + 1] + o.OmZ * o.OmZ * (r[QpReference.Ccom + 1] - cz) + 2 * o.OmZ * (r[QpReference.Ccomd + 1] - ComD[1]);
            if (o.Plan)
            {
                // step 6a: DCM feedback. x_cop_des = xi - (xi_ref_dot - k (xi - xi_ref)) / omega on the support points; the CoM row asks only a_x = omega^2 (x_com - x_cop_des); the vertical row tracks the reference CoM z
                double om = _pOm, xir = cxr + xoff + r[QpReference.Ccomd] / om, xird = r[QpReference.Ccomd] + r[QpReference.Ccomdd] / om, xi = cx + ComD[0] / om;
                double xcd = xi - (xird - o.PlanKxi * (xi - xir)) / om, lo = double.MaxValue, hi = -double.MaxValue;
                for (int i = 0; i < 4; i++) if (_sch.Inc[i]) { lo = Math.Min(lo, _px[i]); hi = Math.Max(hi, _px[i]); }
                if (o.FootMeas)
                {
                    // step 7b: a flat foot (heel and toe in contact) offers the inner interval [heel + m, toe - m] of its sole, a partial one its single contact point
                    lo = double.MaxValue; hi = -double.MaxValue;
                    for (int f = 0; f < 2; f++)
                    {
                        bool hh = _sch.Inc[2 * f], tt = _sch.Inc[2 * f + 1]; if (!hh && !tt) continue;
                        double a0 = hh ? _px[2 * f] : _px[2 * f + 1], a1 = tt ? _px[2 * f + 1] : _px[2 * f];
                        if (hh && tt) { double mm = o.FootCopM * (a1 - a0); double mh = o.FootCopHeelM > 0 ? o.FootCopHeelM : mm, mt = o.FootCopToeM > 0 ? o.FootCopToeM : mm; a0 += mh; a1 -= mt; }
                        lo = Math.Min(lo, a0); hi = Math.Max(hi, a1);
                    }
                }
                if (o.R10Diag) { R10Log[11] = xcd; R10Log[12] = lo; R10Log[13] = hi; }
                xcd = hi >= lo ? Math.Max(lo, Math.Min(hi, xcd)) : cx;
                if (o.PlanDs > 0.5)
                {
                    // step 6c: double support, the CoP target moves from the rear (swing-leg) foot to the front foot over T_ds; held at the front foot when the rear load is still high at 1.5 T_ds
                    int rl = _sch.Parity, fl = 1 - rl; double tds = Math.Max(_tl0[rl], 1e-3), rlo = double.MaxValue, rhi = -double.MaxValue, flo = double.MaxValue, fhi = -double.MaxValue;
                    for (int i = 0; i < 4; i++) if (_sch.Inc[i]) { if (i / 2 == rl) { rlo = Math.Min(rlo, _px[i]); rhi = Math.Max(rhi, _px[i]); } else { flo = Math.Min(flo, _px[i]); fhi = Math.Max(fhi, _px[i]); } }
                    if (rhi >= rlo && fhi >= flo && double.IsNaN(_swT0) && o.R11Ds != 1)
                    {
                        double u = Math.Max(0.0, Math.Min(1.0, tau / tds)), w = tau >= 1.5 * tds ? 1.0 : u * u * (3 - 2 * u);
                        // round 11 (qp_r11_ds = 2): the sweep starts at the rear foot's point nearest the DCM target, not at its toe (a 15 m sole puts the rear toe ~5 m ahead of the front heel and of the CoM: the sweep then commands braking)
                        double r0 = o.R11Ds == 2 ? Math.Max(rlo, Math.Min(rhi, xcd)) : rhi;
                        xcd = (1 - w) * r0 + w * Math.Max(flo, Math.Min(fhi, xcd));
                    }
                }
                cdx = om * om * (cx - xcd);
                if (o.FootZdip > 0)
                {
                    // step 7c: reach-driven CoM height (see qp_foot_zdip); the IK of the swing leg ran this tick only while it is in flight
                    double dtz = double.IsNaN(_zT) ? 0.0 : Math.Max(0.0, Math.Min(0.1, (t - _zT) / tsc)), want = _ikShort > 0 ? Math.Min(o.FootZdipMax, o.FootZdip * (_ikShort + o.FootZdipMargin)) : 0.0;
                    _zDip += Math.Max(-o.FootZdipDn * dtz, Math.Min(o.FootZdipUp * dtz, want - _zDip)); _zT = t; _ikShort = 0.0;
                }
                cdz = r[QpReference.Ccomdd + 1] + o.PlanZkp * (r[QpReference.Ccom + 1] - _zDip - cz) + o.PlanZkd * (r[QpReference.Ccomd + 1] - ComD[1]);
                PlanDiag[2] = xir; PlanDiag[3] = xcd; PlanDiag[4] = cdx; PlanDiag[6] = tau; PlanDiag[7] = cx;
            }
            double lm = AngMom * ms, ldd = r[QpReference.Cldot] + o.KL * (r[QpReference.Cl] - lm);
            _wd[0] = _mt * ms * cdx / W; _wd[1] = _mt * ms * (cdz + g0) / W; _wd[2] = ldd / (W * l);
            if (nb > 0)
            {
                for (int k = 0; k < 3; k++) { ClearRow(nx); for (int c = 0; c < nb; c++) _row[NQ + c] = _gm[k * MaxNb + c]; AddRow(nx, k == 0 ? (o.Plan && o.PlanWx > 0 ? o.PlanWx : wcxEff) : k == 1 ? (o.Plan ? o.PlanWz : o.WCz) : o.WL, _wd[k]); }
                for (int k = 0; k < 2 * nS; k++)
                {
                    int i = _sIdx[k / 2], d = k % 2; ClearRow(nx); for (int c = 0; c < nb; c++) _row[NQ + c] = _em[k * MaxNb + c];
                    AddRow(nx, o.WPrior, r[QpReference.Cf + 2 * i + d] / W);
                    AddRow(nx, o.WSmooth, _fprev[2 * i + d] / W);
                }
                for (int c = 0; c < nb; c++) _hq[(NQ + c) * nx + NQ + c] += o.WBreg;
                if (lat) LateralRows(nx, nS, gpp, ms, W, cx, cz);
            }
            // stance points: acceleration -kvs v
            for (int a = 0; a < nS; a++)
            {
                int i = _sIdx[a];
                for (int d = 0; d < 2; d++)
                {
                    ClearRow(nx); for (int j = 0; j < NQ; j++) _row[j] = Jp[i * 18 + d * 9 + j] * (j < 2 ? 1.0 : 1.0 / l);
                    AddRow(nx, o.WSt, (-o.Kvs * _vp[2 * i + d] - Bp[2 * i + d]) / g0);
                }
            }
            // feet
            if (o.Diag) Array.Clear(Diag, 6, 7);
            double xoffSw = xoff + o.SwFollow * (cx - r[QpReference.Ccom] - xoff);   // step 9.5k (qp_sw_follow beta): the swing foot's reference is tied to the measured CoM by beta (beta = 1: foot minus CoM as in the reference, whatever the CoM-x error), not to the stance-foot anchor
            double kp = o.OmSw * o.OmSw, kd = 2 * o.OmSw, kf = o.OmFp * o.OmFp, kfd = 2 * o.OmFp; bool landOn = false, haveLt = false;
            double dtfc = double.IsNaN(_fcT) ? 0.0 : Math.Max(0.0, Math.Min(0.1, (t - _fcT) / tsc));
            for (int leg = 0; leg < 2; leg++)
            {
                int ns = (_sch.Inc[2 * leg] ? 1 : 0) + (_sch.Inc[2 * leg + 1] ? 1 : 0);
                if (o.Foot) FootUpdate(leg, ns, swLeg, tref, late, r[QpReference.Cq + 5 + 3 * leg], Q[5 + 3 * leg], dtfc);
                if (ns == 2) continue;
                int fidx = 5 + 3 * leg; double bl = 1.0; haveLt = false;
                if (ns == 0)
                {
                    int i = 2 * leg; bl = _sch.SwingBlend(leg, t);
                    double xo = leg == swLeg ? xoffSw : xoff, prx = r[QpReference.Cpts + 2 * i] + xo, prz = r[QpReference.Cpts + 2 * i + 1], vrx = r[QpReference.Cptv + 2 * i], vrz = r[QpReference.Cptv + 2 * i + 1], arx = r[QpReference.Cpta + 2 * i], arz = r[QpReference.Cpta + 2 * i + 1];
                    bool planFp = o.Plan && o.PlanFp > 0.5 && leg == swLeg, planSw = o.Plan && o.PlanSw > 0.5 && leg == swLeg;
                    if (!planSw && leg == swLeg && o.Land && o.LandAct && tau >= o.LandK0 * h && (!(_planAnchor == _sch.TAnchor && _planPar == _sch.Parity) || (planFp && Math.Abs(_pXfp - _planEnd) > o.PlanReplanM)))
                    {
                        Replan(_sch.Parity, tau, Pts[2 * i], Pts[2 * i + 1], Q[fidx], _vp[2 * i] / tsc, _vp[2 * i + 1] / tsc, Qd[fidx] / tsc, xo, planFp ? _pXfp : double.NaN);
                        if (planFp) _planEnd = _pXfp;
                    }
                    if (!planSw && leg == swLeg && o.Land && tau >= _lqTau0[_sch.Parity])
                    {
                        LandTarget(_sch.Parity, tau); haveLt = true;
                        if (tsc != 1.0) for (int c = 0; c < 3; c++) { _ltVel[c] *= tsc; _ltAcc[c] *= tsc * tsc; }
                        prx = _ltPos[0] + xo; prz = _ltPos[1]; vrx = _ltVel[0]; vrz = _ltVel[1]; arx = _ltAcc[0]; arz = _ltAcc[1];
                    }
                    if (planFp && !planSw && !haveLt) { double sp0 = Math.Min(1.0, Math.Max(0.0, tau / ts)); prx += sp0 * sp0 * (3 - 2 * sp0) * (_pXfp - (_lqP1[_sch.Parity][0] + xo)); }   // step 6a: the reference heel path warped to the planned foothold
                    bool ikOk = false; double ikF = 0;
                    if (planSw)
                    {
                        // step 6b: min-jerk heel x from the lift-off heel to x_fp over T, heel z = the row's swing profile x apex scale, a downward velocity past PlanDownFrac
                        double phi = tau / ts;
                        if (double.IsNaN(_swT0)) { _swT0 = Math.Min(tau, 0.75 * ts); _swX0 = Pts[2 * i]; }   // the curve starts at the measured lift-off of the swing foot
                        bool r8 = o.R8Retime && !double.IsNaN(_swS); double gg = r8 ? _r8g : 1.0, tsw8 = r8 ? _swS : tau - _swT0;   // round 8: plan time of the swing and its rate (1 = real time)
                        double dsw = o.PlanDs > 0.5 ? ts - _tl0[_sch.Parity] : ts - _swT0, ss = tsw8 / dsw, tl0 = _tl0[_sch.Parity], rate = (ts - tl0) / dsw * gg;
                        double dxs = _pXfp - _swX0; prx = _swX0 + dxs * MinJerk(ss); vrx = dxs * MinJerkD(ss) / dsw * gg; arx = dxs * MinJerkDD(ss) / (dsw * dsw) * gg * gg;
                        _ref.At(_sch.Parity * ts + tl0 + Math.Max(0.0, Math.Min(1.0, ss)) * (ts - tl0), _rs);   // the row's swing profile (heel z) on the same progress
                        double ca = Math.Min(1.0, o.PlanApex); prz = Math.Max(0.0, _rs[QpReference.Cpts + 2 * i + 1] * ca); vrz = _rs[QpReference.Cptv + 2 * i + 1] * ca * rate; arz = _rs[QpReference.Cpta + 2 * i + 1] * ca * rate * rate;
                        _apexCmd = Math.Max(_apexCmd, prz);
                        if (ss > o.PlanDownFrac) { prz -= o.PlanVd * (tsw8 - o.PlanDownFrac * dsw); vrz -= o.PlanVd; }
                        ikF = o.Foot ? FootCmd[leg] : r[QpReference.Cq + fidx];
                    }
                    if (leg == swLeg && late > 0) { prz -= o.LateVz * late; vrz -= o.LateVz; }
                    if (planSw) { ikOk = PlanIk(leg, prx, prz, ikF, t); if (!ikOk) { _ikFail++; _ikTPrev = double.NaN; } }
                    if (o.SwCp > 0 && leg == swLeg)
                    {
                        // step 9.5m: the heel target never lies beyond k x the capture point of the measured CoM (a CoM slower than the row gets a nearer foot, not the reference's 10 m)
                        double cap = cx + o.SwCp * Math.Max(ComD[0], 0.0) * Math.Sqrt(Math.Max(cz, 1.0) / g0);
                        if (prx > cap) { prx = cap; vrx = Math.Min(vrx, ComD[0]); arx = 0.0; }
                    }
                    double whl = 1.0;   // step 9.5l (qp_swj): heel-task blend, 0 until the last SwJTd of the swing, smoothstep to 1 (1 when late)
                    if (o.SwJ > 0 && leg == swLeg)
                    {
                        double sp = (tref - swLeg * ts) / ts, f0 = Math.Max(o.SwJTd, 1e-3), u = late > 0 ? 1.0 : Math.Max(0.0, Math.Min(1.0, (sp - (1.0 - f0)) / f0)); whl = u * u * (3 - 2 * u);
                        if (planSw) whl = Math.Max(whl, o.PlanHeelW);
                        double kj = o.SwJOm * o.SwJOm, kjd = 2 * o.SwJOm; int nj = o.SwJAnkle ? 3 : 2;
                        for (int jj = 0; jj < nj; jj++)
                        {
                            int j = 3 * leg + jj; double ddq = 0, dq = 0, dqd = 0, qr = 0, qdr = 0;
                            for (int c = 0; c < NQ; c++) { double cj = _cj[j * NQ + c]; if (cj == 0) continue; ddq += cj * r[QpReference.Cqdd + c]; qr += cj * r[QpReference.Cq + c]; qdr += cj * r[QpReference.Cqd + c]; dq += cj * Q[c]; dqd += cj * Qd[c]; }
                            if (ikOk) { ddq = 0; qr = _ikQ[jj]; qdr = _ikQd[jj]; }
                            double aj = ddq + kj * (qr - dq) + kjd * (qdr - dqd); if (o.SwJAmax > 0) aj = Math.Max(-o.SwJAmax, Math.Min(o.SwJAmax, aj));
                            ClearRow(nx); for (int c = 0; c < NQ; c++) _row[c] = _cj[j * NQ + c] * (c < 2 ? l : 1.0); AddRow(nx, o.SwJ * bl * (1.0 - 0.5 * whl), aj / (g0 / l));
                            if (o.Diag && jj == 0) { Diag[15] = whl; }
                        }
                    }
                    double adx = arx + kp * (prx - Pts[2 * i]) + kd * (vrx - _vp[2 * i]), adz = arz + kp * (prz - Pts[2 * i + 1]) + kd * (vrz - _vp[2 * i + 1]);
                    if (o.SwAmax > 0 && leg == swLeg) { double am = Math.Sqrt(adx * adx + adz * adz); if (am > o.SwAmax) { double k = o.SwAmax / am; adx *= k; adz *= k; } }   // step 9.5k (qp_sw_amax): the commanded heel acceleration is limited to what the hip can deliver
                    if (o.Diag && leg == swLeg) { Diag[0] = swLeg; Diag[4] = bl; Diag[6] = prx - Pts[2 * i]; Diag[7] = prz - Pts[2 * i + 1]; Diag[8] = adx; Diag[9] = adz; Diag[10] = haveLt ? 1 : 0; Diag[11] = prz; Diag[12] = Pts[2 * i + 1]; }
                    for (int d = 0; d < 2; d++)
                    {
                        ClearRow(nx); for (int j = 0; j < NQ; j++) _row[j] = Jp[i * 18 + d * 9 + j] * (j < 2 ? 1.0 : 1.0 / l);
                        if (whl > 1e-6) AddRow(nx, o.WSwing * bl * whl, ((d == 0 ? adx : adz) - Bp[2 * i + d]) / g0);
                    }
                }
                double af = r[QpReference.Cqdd + fidx] + kf * (r[QpReference.Cq + fidx] - Q[fidx]) + kfd * (r[QpReference.Cqd + fidx] - Qd[fidx]);
                if (o.Foot) af = kf * (FootCmd[leg] - Q[fidx]) + kfd * (0.0 - Qd[fidx]);
                else if (ns == 0 && leg == swLeg && haveLt) { af = _ltAcc[2] + kf * (_ltPos[2] - Q[fidx]) + kfd * (_ltVel[2] - Qd[fidx]); landOn = true; }
                double wfs = 1.0; if (o.Foot) { if (ns == 0 && leg == swLeg) wfs = 1.0 + (o.FootW - 1.0) * _fcU[leg]; else if (ns == 1) wfs = o.FootW; }
                double fps = 1.0;
                if (o.FpRom) { int aj = 3 * leg + 2; double qa = 0; for (int c = 0; c < NQ; c++) qa += _cj[aj * NQ + c] * Q[c]; double dist = Math.Min(qa - RomLo[aj], RomHi[aj] - qa) - o.RomMargin; fps = Math.Max(0.0, Math.Min(1.0, dist / Math.Max(o.FpRomBand, 1e-6))); }
                FpScale[leg] = fps; if (o.Diag && leg == swLeg) Diag[13] = fps;
                ClearRow(nx); _row[fidx] = 1.0; AddRow(nx, o.WFp * bl * fps * wfs, af / (g0 / l));
            }
            if (o.Foot)
            {
                _fcT = t; double sfn = 0, sfx = 0;
                for (int i = 0; i < 4; i++) { sfn += _fn[i]; sfx += _fn[i] * _px[i]; }
                for (int lg = 0; lg < 2; lg++) { FootLog[lg] = Q[5 + 3 * lg] * 180.0 / Math.PI; FootLog[2 + lg] = FootCmd[lg] * 180.0 / Math.PI; FootLog[4 + lg] = (_sch.Inc[2 * lg] ? 1 : 0) + (_sch.Inc[2 * lg + 1] ? 2 : 0) == 3 ? 2 : _sch.Inc[2 * lg] ? 1 : _sch.Inc[2 * lg + 1] ? 3 : 0; }
                FootLog[6] = _tdPitchDeg; FootLog[7] = sfn > 0.05 * W ? sfx / sfn : 0.0; FootLog[9] = FtMeas / W; FootLog[13] = _fcU[swLeg]; FootLog[14] = _zDip;
            }
            if (o.Diag) { Diag[1] = tref; Diag[2] = tau; Diag[3] = late; Diag[5] = (_sch.Inc[0] ? 1 : 0) + (_sch.Inc[1] ? 2 : 0) + (_sch.Inc[2] ? 4 : 0) + (_sch.Inc[3] ? 8 : 0); Diag[14] = _sch.TouchdownCount; }
            // trunk
            { double ath = r[QpReference.Cqdd + 2] + o.OmTh * o.OmTh * (r[QpReference.Cq + 2] - Q[2]) + 2 * o.OmTh * (r[QpReference.Cqd + 2] - Qd[2]); ClearRow(nx); _row[2] = 1.0; AddRow(nx, o.WTrunk, ath / (g0 / l)); }
            // posture (joint space)
            double kq = o.OmPost * o.OmPost, kqd = 2 * o.OmPost;
            for (int j = 0; j < NJ; j++)
            {
                double ddq = 0, dq = 0, dqd = 0, qr = 0, qdr = 0;
                for (int c = 0; c < NQ; c++) { double cj = _cj[j * NQ + c]; if (cj == 0) continue; ddq += cj * r[QpReference.Cqdd + c]; qr += cj * r[QpReference.Cq + c]; qdr += cj * r[QpReference.Cqd + c]; dq += cj * Q[c]; dqd += cj * Qd[c]; }
                double aj = ddq + kq * (qr - dq) + kqd * (qdr - dqd), wp = o.WPost; if (landOn && j / 3 == swLeg) wp *= o.LandPost;
                ClearRow(nx); for (int c = 0; c < NQ; c++) _row[c] = _cj[j * NQ + c] * (c < 2 ? l : 1.0); AddRow(nx, wp, aj / (g0 / l));
            }
            for (int j = 0; j < NQ; j++) _hq[j * nx + j] += o.WReg;
            // ---- hard rows. Qx = [Ms D, -Jb] (9 x nx), Q = Qx x + hs
            for (int i = 0; i < NQ; i++)
            {
                for (int j = 0; j < NQ; j++) _qx[i * nx + j] = _ms[i * NQ + j] * _dd[j];
                for (int c = 0; c < nb; c++) _qx[i * nx + NQ + c] = -_jb[i * MaxNb + c];
            }
            // equalities: Nm' Q = 0 (rows normalised)
            for (int k = 0; k < 3; k++)
            {
                double sc = 0, e = 0;
                for (int c = 0; c < nx; c++)
                {
                    double v = k == 0 ? _qx[c] : k == 1 ? _qx[nx + c] : 0; if (k == 2) for (int i = 2; i < NQ; i++) v += _n2[i] * _qx[i * nx + c];
                    _aeq[k * nx + c] = v; sc += v * v;
                }
                e = k == 0 ? -_hs[0] : k == 1 ? -_hs[1] : 0; if (k == 2) { for (int i = 2; i < NQ; i++) e -= _n2[i] * _hs[i]; }
                sc = Math.Sqrt(sc); if (sc < 1e-300) sc = 1.0;
                for (int c = 0; c < nx; c++) _aeq[k * nx + c] /= sc; _beq[k] = e / sc;
            }
            // inequalities: 12 torque rows, beta >= 0, fn <= fmax, per-foot shear
            int mi = 0;
            for (int j = 0; j < NJ; j++)
            {
                double cu = 0; for (int i = 0; i < NQ; i++) cu += _pinv[j * NQ + i] * _hs[i]; cu /= _lim[j];
                for (int c = 0; c < nx; c++) { double au = 0; for (int i = 0; i < NQ; i++) au += _pinv[j * NQ + i] * _qx[i * nx + c]; au /= _lim[j]; _ain[j * nx + c] = -au; _ain[(NJ + j) * nx + c] = au; }
                double limf = o.LimFrac;
                if (o.R8SwLim > 0 && o.Plan && j >= 3 * swLeg && j < 3 * swLeg + 2 && !(_sch.Inc[2 * swLeg] || _sch.Inc[2 * swLeg + 1]) && !double.IsNaN(_swT0)) limf = Math.Min(limf, o.R8SwLim);   // round 8: swing hip / knee saturation row
                _bin[j] = cu - limf; _bin[NJ + j] = -limf - cu;
            }
            mi = 2 * NJ;
            if (o.WTau > 0) for (int j = 0; j < NJ; j++) { for (int c = 0; c < nx; c++) _row[c] = _ain[(NJ + j) * nx + c]; AddRow(nx, o.WTau, o.LimFrac + _bin[NJ + j]); }   // tau_j / LIM = row . x + cu with cu = -LimFrac - bin
            for (int c = 0; c < nb; c++) { Array.Clear(_ain, mi * nx, nx); _ain[mi * nx + NQ + c] = 1.0; _bin[mi] = 0.0; mi++; }
            for (int a = 0; a < nS; a++) { Array.Clear(_ain, mi * nx, nx); for (int c = 0; c < nb; c++) _ain[mi * nx + NQ + c] = -_em[(2 * a + 1) * MaxNb + c]; _bin[mi] = -_fmax[_sIdx[a]]; mi++; }
            if (o.FrMeas && lat)
                for (int f = 0; f < 2; f++)
                {
                    bool any = false; for (int a = 0; a < nS; a++) if (_sIdx[a] / 2 == f) any = true; if (!any) continue;
                    double cap = Math.Max(o.Mu * o.FrMargin * (_fn[2 * f] + _fn[2 * f + 1]) / W, 1e-3);
                    for (int q = 0; q < 4; q++)
                    {
                        double sx = (q & 1) == 0 ? 1.0 : -1.0, sy = (q & 2) == 0 ? 1.0 : -1.0; Array.Clear(_ain, mi * nx, nx);
                        for (int a = 0; a < nS; a++) if (_sIdx[a] / 2 == f) for (int b = 0; b < gpp; b++) { int c = gpp * a + b; _ain[mi * nx + NQ + c] = -(sx * _gxc[c] + sy * _gyc[c]); }
                        _bin[mi] = -cap; mi++;
                    }
                }
            else if (o.FrMeas)
                for (int f = 0; f < 2; f++)
                {
                    bool any = false; for (int a = 0; a < nS; a++) if (_sIdx[a] / 2 == f) any = true; if (!any) continue;
                    double cap = Math.Max(o.Mu * o.FrMargin * (_fn[2 * f] + _fn[2 * f + 1]) / W, 1e-3);
                    Array.Clear(_ain, mi * nx, 2 * nx);
                    for (int a = 0; a < nS; a++) if (_sIdx[a] / 2 == f) for (int c = 0; c < nb; c++) { double tv = _em[(2 * a) * MaxNb + c]; _ain[mi * nx + NQ + c] -= tv; _ain[(mi + 1) * nx + NQ + c] += tv; }
                    _bin[mi] = -cap; _bin[mi + 1] = -cap; mi += 2;
                }
            if (o.FootMeas && nS > 0)
            {
                // step 7b (qp_foot_meas): contact constraints from the measured contact points. (1) per-point shear cap mu x margin x the point's measured normal force (floor FootFricFloor W: a point that has just made contact);
                // (2) a foot with heel and toe both in contact keeps its centre of pressure inside [heel + m, toe - m]: (s - m) f_h >= m f_t and (s - m) f_t >= m f_h on the normal forces (s = heel-toe distance, m = FootCopM s)
                for (int a = 0; a < nS; a++)
                {
                    int i = _sIdx[a]; double cap = Math.Max(o.Mu * o.FrMargin * _fn[i] / W, o.FootFricFloor);
                    if (lat)
                        for (int q = 0; q < 4; q++)
                        {
                            double sx = (q & 1) == 0 ? 1.0 : -1.0, sy = (q & 2) == 0 ? 1.0 : -1.0; Array.Clear(_ain, mi * nx, nx);
                            for (int b = 0; b < gpp; b++) { int c = gpp * a + b; _ain[mi * nx + NQ + c] = -(sx * _gxc[c] + sy * _gyc[c]); }
                            _bin[mi] = -cap; mi++;
                        }
                    else
                    {
                        Array.Clear(_ain, mi * nx, 2 * nx);
                        for (int c = 0; c < nb; c++) { double tv = _em[(2 * a) * MaxNb + c]; _ain[mi * nx + NQ + c] -= tv; _ain[(mi + 1) * nx + NQ + c] += tv; }
                        _bin[mi] = -cap; _bin[mi + 1] = -cap; mi += 2;
                    }
                }
                for (int f = 0; f < 2; f++)
                {
                    int ah = -1, at = -1; for (int a = 0; a < nS; a++) { if (_sIdx[a] == 2 * f) ah = a; else if (_sIdx[a] == 2 * f + 1) at = a; }
                    if (ah < 0 || at < 0) continue;
                    double sp = _px[2 * f + 1] - _px[2 * f]; if (sp < 0.5) continue; double m = o.FootCopM * sp, mhl = o.FootCopHeelM > 0 ? o.FootCopHeelM : m, mtl = o.FootCopToeM > 0 ? o.FootCopToeM : m;
                    for (int k = 0; k < 2; k++)
                    {
                        // k = 0: (s - mt) f_h >= mt f_t (CoP <= toe - mt); k = 1: (s - mh) f_t >= mh f_h (CoP >= heel + mh)
                        int aa = k == 0 ? ah : at, bb = k == 0 ? at : ah; Array.Clear(_ain, mi * nx, nx); double mk = k == 0 ? mtl : mhl;
                        for (int c = 0; c < nb; c++) _ain[mi * nx + NQ + c] = (sp - mk) * _em[(2 * aa + 1) * MaxNb + c] - mk * _em[(2 * bb + 1) * MaxNb + c];
                        _bin[mi] = 0.0; mi++;
                    }
                }
            }
            // step 9.5j (qp_rom): joint-range rows on qdd_j = CJ_j D x[0..8], velocity-damper form: the joint's next velocity over the horizon T (qd + qdd T) stays on the braking envelope
            // of the margin point, +-sqrt(2 a_b dist) (a_b = RomAmax) while inside, and +-dist / T back toward it once past it; a bound on qdd limited to +-2 a_b keeps the row reachable
            int miBase = mi; RomRows = 0; RomActive = 0; NS = nS;
            if (o.Rom)
            {
                double hr = Math.Max(o.RomHorizon, 1e-3), ab = Math.Max(o.RomAmax, 1e-3), cap = 2 * ab;
                for (int j = 0; j < NJ; j++)
                {
                    double qj = 0, qdj = 0; for (int c = 0; c < NQ; c++) { double cj = _cj[j * NQ + c]; if (cj == 0) continue; qj += cj * Q[c]; qdj += cj * Qd[c]; }
                    if (!double.IsInfinity(RomHi[j]))
                    {
                        double d = RomHi[j] - o.RomMargin - qj, vlim = d >= 0 ? Math.Sqrt(2 * ab * d) : d / hr, ub = Math.Min(cap, Math.Max(-cap, (vlim - qdj) / hr));
                        Array.Clear(_ain, mi * nx, nx); for (int c = 0; c < NQ; c++) _ain[mi * nx + c] = -_cj[j * NQ + c] * _dd[c]; _bin[mi] = -ub; mi++; RomRows++;
                        if (qdj > vlim) RomActive |= 1 << (6 + j);
                    }
                    if (!double.IsInfinity(RomLo[j]))
                    {
                        double d = qj - (RomLo[j] + o.RomMargin), vlim = d >= 0 ? -Math.Sqrt(2 * ab * d) : -d / hr, lb = Math.Min(cap, Math.Max(-cap, (vlim - qdj) / hr));
                        Array.Clear(_ain, mi * nx, nx); for (int c = 0; c < NQ; c++) _ain[mi * nx + c] = _cj[j * NQ + c] * _dd[c]; _bin[mi] = lb; mi++; RomRows++;
                        if (qdj < vlim) RomActive |= 1 << j;
                    }
                }
            }
            // ---- solve (drop the ROM rows, then the torque rows if infeasible, as the prototype), keep the previous torques if that fails too
            var st = _solver.Solve(nx, 3, mi, _hq, _gq, _aeq, _beq, _ain, _bin); LastIterations = _solver.Iterations;
            if (st != QpStatus.Optimal && RomRows > 0) { RomRowsDropped++; mi = miBase; RomActive = 0; st = _solver.Solve(nx, 3, mi, _hq, _gq, _aeq, _beq, _ain, _bin); LastIterations = _solver.Iterations; }
            if (st != QpStatus.Optimal)
            {
                Array.Copy(_ain, 2 * NJ * nx, _ain2, 0, (mi - 2 * NJ) * nx); Array.Copy(_bin, 2 * NJ, _bin2, 0, mi - 2 * NJ);
                st = _solver.Solve(nx, 3, mi - 2 * NJ, _hq, _gq, _aeq, _beq, _ain2, _bin2); TorqueRowsDropped++;
            }
            LastStatus = st;
            if (st != QpStatus.Optimal) { SolveFailures++; Held++; return false; }
            var x = _solver.X; for (int i = 0; i < NQ; i++) _qdd[i] = _dd[i] * x[i];
            Array.Clear(Force, 0, 8);
            for (int a = 0; a < nS; a++)
            {
                int i = _sIdx[a]; double ft = 0, fz = 0;
                for (int c = 0; c < nb; c++) { ft += _em[(2 * a) * MaxNb + c] * x[NQ + c]; fz += _em[(2 * a + 1) * MaxNb + c] * x[NQ + c]; }
                Force[2 * i] = W * ft; Force[2 * i + 1] = W * fz;
            }
            for (int i = 0; i < NQ; i++)
            {
                double s = _hs[i]; for (int j = 0; j < NQ; j++) s += _ms[i * NQ + j] * _qdd[j];
                for (int p = 0; p < 4; p++) s -= Jp[p * 18 + i] * Force[2 * p] + Jp[p * 18 + 9 + i] * Force[2 * p + 1];
                _qq[i] = s;
            }
            bool ok = true;
            for (int j = 0; j < NJ; j++)
            {
                double u = 0; for (int i = 0; i < NQ; i++) u += _pinv[j * NQ + i] * _qq[i];
                if (double.IsNaN(u) || double.IsInfinity(u)) { ok = false; break; }
                _uu[j] = Math.Min(_lim[j], Math.Max(-_lim[j], u));
            }
            if (!ok) { SolveFailures++; Held++; return false; }
            Array.Copy(_uu, Tau, NJ); Array.Copy(Force, _fprev, 8);
            if (o.Foot) { double cn = 0, cxs = 0, cs = 0; for (int i = 0; i < 4; i++) { cn += Force[2 * i + 1]; cxs += Force[2 * i + 1] * _px[i]; cs += Force[2 * i]; } FootLog[8] = cn > 0.05 * W ? cxs / cn : 0.0; FootLog[10] = cs / W; }
            double fr = 0; for (int i = 0; i < 4; i++) if (Force[2 * i + 1] > 1e-6 * W) fr = Math.Max(fr, Math.Abs(Force[2 * i]) / (o.Mu * Force[2 * i + 1]));
            if (lat)
            {
                Array.Clear(Force3, 0, 24); Array.Clear(_fprevY, 0, 4);
                for (int c = 0; c < nb; c++) { double bx = x[NQ + c] * W; int k = _cc[c]; Force3[3 * k] += _gxc[c] * bx; Force3[3 * k + 1] += _gyc[c] * bx; Force3[3 * k + 2] += bx; _fprevY[k / 2] += _gyc[c] * bx; }
                fr = 0; for (int i = 0; i < 4; i++) { double fx = Force3[6 * i] + Force3[6 * i + 3], fy = Force3[6 * i + 1] + Force3[6 * i + 4], fz = Force3[6 * i + 2] + Force3[6 * i + 5]; if (fz > 1e-6 * W) fr = Math.Max(fr, Math.Sqrt(fx * fx + fy * fy) / (o.Mu * fz)); }
                double sy = 0, mr = 0, my = 0; for (int k = 0; k < 8; k++) { double fx = Force3[3 * k], fy = Force3[3 * k + 1], fz = Force3[3 * k + 2], px = Corner[3 * k] - cx, py = Corner[3 * k + 1] - ComY, pz = Corner[3 * k + 2] - cz; sy += fy; mr += py * fz - pz * fy; my += px * fy - py * fx; }
                LatCmd[0] = sy / W; LatCmd[1] = mr / (W * l); LatCmd[2] = my / (W * l);
            }
            FrictionRatioCmd = fr;
            return true;
        }
    }
}
