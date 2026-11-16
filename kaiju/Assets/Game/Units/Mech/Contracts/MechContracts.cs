using System;
using Game.Control.Contracts;
using Game.Core;
using Game.Physics.Body.Contracts;

namespace Game.Units.Mech.Contracts
{
    public static class MechContractVersions
    {
        public const int Unit = 1; public const string GaitLog = "gait_log@v1";
        /// <summary>The M0 locomotion controller is a biped controller: it accepts frames with exactly this many feet (the body itself takes any number). Other body plans need their own controller.</summary>
        public const int BipedFeet = 2;
        public const string FlagEnabled = "units.mech.enabled", FlagGaitLog = "units.mech.gaitLog";
    }

    public enum LocoPhase { Stand = 0, Shift = 1, DoubleSupport = 2, SingleSupport = 3, Falling = 4, Fallen = 5 }

    /// <summary>Why the command differs from the intent as received (C14: never obeyed unphysically, never refused silently).</summary>
    [Flags]
    public enum LimitCause
    {
        None = 0, EffortRange = 1, Accel = 2, Decel = 4, YawRate = 8, YawAccel = 16, LateralAccel = 32, StopDistance = 64, TurnBeforeWalk = 128, Arrived = 256, Fallen = 512, StepReach = 1024
    }

    /// <summary>The locomotion command after limiting: what the controller actually pursues this step.</summary>
    public struct LocoCommand
    {
        public MoveId Move;
        public float Speed;        // m/s along Heading
        public float Heading;      // rad, atan2(x, z)
        public float YawRate;      // rad/s
        public float RequestedSpeed, RequestedHeading;
        public LimitCause Causes;
    }

    public struct MechFallStarted : IEvent { public EntityId Unit; public long Step; }
    public struct MechFallEnded : IEvent { public EntityId Unit; public long Step; }
    /// <summary>Published when the set of active limit causes changes.</summary>
    public struct IntentLimited : IEvent { public EntityId Unit; public long Step; public int Causes; public float RequestedSpeed, Speed; }

    /// <summary>One mech: a body, a seat with any number of pilots, and the locomotion state. Registered per unit; nothing is a singleton.</summary>
    public interface IMechUnit
    {
        EntityId Id { get; }
        PilotSeat Seat { get; }
        IArticulatedBody Body { get; }
        LocoPhase Phase { get; }
        LocoCommand Command { get; }
        IntentFrame Intent { get; }
    }

    /// <summary>
    /// Data spec <c>walk_tuning@v1</c> (Assets/Game/Config/Mech/walk_tuning.json). Dimensionless wherever possible (g, leg lengths, body weights) so one file serves 76-88 m and 6,000-15,000 t.
    /// Sources per field are in the file's notes: research/R-001 (limits), research/R-005 section 5 (control loop).
    /// </summary>
    [Serializable]
    public sealed class WalkTuning
    {
        public const string Schema = "walk_tuning@v1";
        public string schema;
        // speed and timing (R-001 sections 1, 4)
        public double fr_max, fr_cruise, stride_k, stride_exp, step_time_max_tstar, duty_factor;
        // limits on the command (R-001 sections 2, 3)
        public double accel_start_g, decel_normal_g, decel_emergency_g, lateral_accel_g, yaw_rate_max_tstar_deg, yaw_accel_max_tstar_deg, yaw_rate_walking_tstar_deg;
        public double turn_first_deg, arrive_fraction_of_step, stop_speed_fr, max_step_yaw_deg;
        // foot placement (fractions of leg length unless noted)
        public double step_width_walk_frac, cop_inboard_frac, cop_heel_frac, cop_toe_frac, cop_stand_frac, sole_margin, reach_forward_frac, reach_back_frac, reach_side_frac, foot_gap_frac;
        // body carriage
        public double vault, knee_load_deg, knee_mid_deg, knee_extend_tstar, height_rate_tstar, height_rate_vault_tstar, com_drop_stand_frac, com_drop_walk_frac, step_reach_margin_frac, step_reach_front_frac, step_length_max_frac, yaw_grip_radius_frac, touchdown_share, capture_trigger_frac, capture_step_time_tstar, height_hz, attitude_hz, yaw_hz, attitude_torque_frac, yaw_torque_frac, dcm_gain, force_min_bw, force_max_bw;
        // swing
        public double swing_clearance_frac, touchdown_speed_tstar, touchdown_gain_scale, landing_damping_scale, landing_time_tstar, transfer_lead, swing_rise_exponent, swing_toe_up_deg, heel_strike_deg, swing_hover, swing_retarget_tstar, stand_min_share, knee_min_flex_deg, swing_freeze, shift_time_tstar, liftoff_load_bw, arm_swing_gain, trunk_counter_gain, elbow_flex_deg;
        public double stance_knee_damping_ratio, stance_hip_damping_ratio, stance_ankle_damping_ratio, stance_posture_hz;
        // falling
        public double fall_tilt_deg, fall_height_frac, fall_damping_ratio, rest_speed_fr;
        // stomp
        public double stomp_lift_frac, stomp_speed_tstar;
        // controller constants (filters, thresholds, guards): data, not code (DEF-T005-arch-1)
        public double reference_leg_length_m, load_filter, rest_time_s, turn_start_rad, turn_done_rad, shift_dcm_tolerance_frac, shift_timeout_factor, step_end_freeze, cop_travel_full_step_ratio, transfer_floor_delay, transfer_filter, transfer_done_share, ds_overrun_factor, transfer_overrun_share, ds_timeout_factor, leg_length_floor_frac, vault_length_floor_frac, touchdown_min_swing, stand_dcm_frac, stand_align_frac, stand_yaw_rad, friction_safety, load_scale_min, load_scale_max, posture_incline_floor, stance_velocity_max_rps, stomp_rise_fraction, touchdown_overdrive_frac, swing_horizontal_delay, swing_horizontal_end, knee_max_flex_deg, extra_drop_rate_tstar, extra_drop_max_frac, extra_drop_recover, touchdown_soften_span, target_velocity_max_rps, reach_speed_floor_frac, yaw_approach_factor, turn_first_speed_frac, turn_stop_factor, walking_speed_frac, footfall_probe_gain_m, footfall_probe_exponent, load_known_bw, yaw_report_threshold_rad;
        // reference gait (research/R-007 section 4): the coordinated whole-cycle pattern the controller tracks
        public double ref_track, ref_track_swing, ref_track_pitch, ref_track_upper, ref_height_blend, swing_ff_gain, swing_ff_filter, swing_ff_accel_max, ref_rollover_start_frac, ref_rollover_full_frac, ref_front_bias_frac, ref_heel_strike_deg, ref_toe_off_deg, ref_foot_flat_at, ref_heel_rise_start, ref_knee_mid_flat_deg, ref_preswing_knee_deg, ref_knee_extend_by, ref_pelvis_yaw_deg, ref_waist_counter_deg, ref_arm_amp_deg, ref_sway_scale, ref_swing_peak_at, ref_final_approach_tstar, ref_toe_off_floor, ref_ankle_margin_deg, ref_ankle_soft_deg, ref_ankle_heel_rise, ref_ankle_free_span, ref_reach_margin_frac, ref_softmin_frac, ref_waist_ff, ref_waist_max_deg, ref_waist_cancels_pelvis;
        // round 2 (research/R-008): stance-leg joint impedance around the state-indexed stance posture (heel strike, rollover, heel rise and push-off, pelvic tilt), yaw-moment budget, engine inverse dynamics
        public double imp, imp_hip_height_frac, imp_knee_min_deg, imp_knee_land_deg, imp_ankle_margin_deg, imp_heel_strike_max_deg, imp_toe_off_max_deg, imp_leg_hz, imp_leg_zeta, imp_ankle_hz, imp_rocker_blend_deg, imp_preswing_pitch_deg, imp_share_floor, imp_pelvis_tilt_deg, imp_tilt_rate_dps, imp_swing_pitch_end, imp_edge_grip_scale, imp_flat_pitch_deg, imp_land_knee_scale, imp_height_scale;
        // round 2 step 2: joint-space stance impedance (hip, knee, ankle) per stance sub-phase (heel, flat, toe), natural frequency (Hz) and damping ratio; virtual-force correction scale
        public double imp_vf_scale;
        public double imp_hip_hz_heel, imp_hip_hz_flat, imp_hip_hz_toe, imp_knee_hz_heel, imp_knee_hz_flat, imp_knee_hz_toe, imp_ankle_hz_heel, imp_ankle_hz_flat, imp_ankle_hz_toe;
        public double imp_hip_zeta_heel, imp_hip_zeta_flat, imp_hip_zeta_toe, imp_knee_zeta_heel, imp_knee_zeta_flat, imp_knee_zeta_toe, imp_ankle_zeta_heel, imp_ankle_zeta_flat, imp_ankle_zeta_toe;
        public double imp_step_length_max_frac = 0.66;   // round 2 step 6a: step length cap (x leg length) when imp > 0; the reference's step at Fr 0.15 is 0.651 l
        public double imp_kload_hip = 0.5, imp_kload_knee = 1.0, imp_kload_ankle = 0.5, imp_kload_zeta = 1.0;   // round 2 step 6b: stance stiffness = kload x foot load x leg length
        public double imp_waist_ff = 0.9, imp_waist_max_deg = 25;   // imp=1 waist table; legacy imp=0 uses ref_waist_ff, ref_waist_max_deg
        public double imp_waist_hz = 0.8, imp_waist_zeta = 1.0, imp_arm_hz = 0.6, imp_arm_zeta = 1.0;
        public double imp_ff_gravity = 1.0, imp_ff_coriolis = 1.0, imp_ff_accel = 1.0, imp_ff_stance_gravity_share = 0.0;   // round 2 step 4: gains on the body's inverse dynamics; share of the gravity term applied on stance-leg DOFs   // round 2 step 3: waist yaw and shoulder drive of the tracked upper body
        public double imp2_ff_scale = 1.0, imp2_vf_scale = 0.0, imp2_ff_sign_hip = 1.0, imp2_ff_sign_knee = 1.0, imp2_ff_sign_ankle = 1.0, imp2_phase_sync = 0.0, imp2_swing_noclamp = 0.0, imp2_swing_gain_scale = 1.0;   // step 8.5 c2: 1 = reference phase re-anchored at measured touchdown at the data step time; 1 = no reach clamp on the imp 2 swing target; multiplier on the imp 2 swing PD gains;   // round 3 step 8.5 (imp = 2): optimised-torque feed-forward scale, virtual-force scale, per-joint torque sign
        // round 4 step 9.4b (imp = 3, sagittal whole-body QP; Tools/GaitOpt/qp_proto/controller_fid.py b5 preset). Defaults = b5, so walk_tuning.json needs none of them; walk_tuning_qp.json lists them.
        public double qp_mu = 0.40, qp_lim_frac = 0.98, qp_om_x = 0.8, qp_om_z = 2.0, qp_kL = 2.0, qp_om_sw = 4.0, qp_om_th = 3.0, qp_om_post = 3.0, qp_om_fp = 4.0;
        public double qp_w_cx = 1e4, qp_w_cz = 1e4, qp_w_L = 1e3, qp_w_prior = 1e-2, qp_w_smooth = 1e-3, qp_w_st = 1e6, qp_w_swing = 1e3, qp_w_fp = 1e3, qp_w_trunk = 1e4, qp_w_post = 10.0, qp_w_reg = 1e-4, qp_w_breg = 1e-6;
        public double qp_kvs = 5.0, qp_on_bw = 0.03, qp_off_bw = 0.01, qp_T_out = 0.3, qp_T_in = 0.08, qp_T_blend = 0.2, qp_load_thr = 0.01, qp_td_min_frac = 0.6, qp_late_vz = 2.0, qp_fcap = 4.0, qp_z_hold = 0.05;
        public double qp_hold_sched = 1, qp_onset = 1, qp_land = 1, qp_land_act = 1, qp_fr_meas = 1, qp_fr_margin = 0.9, qp_land_k0 = 35, qp_v_td = 0.3, qp_land_post = 0.0, qp_mass_est = 0.05, qp_mass_lo = 0.7, qp_mass_hi = 1.5, qp_sg_floor = 0.3;
        public double qp_yaw_trunk = 0, qp_yaw_ff = 1, qp_yaw_ff_alpha = 0.3, qp_yaw_cap = 0.1, qp_yaw_ff_max = 0, qp_arm_yaw = 0, qp_arm_trim_max = 0.6, qp_arm_leak_s = 2.0;   // qp_arm_yaw (9.5e, needs qp_yaw_trunk): shoulder flexion trim that cancels the yaw momentum of the legs and arms: trim rate per arm = -qp_arm_yaw x L_rest / (2 S), S = d(L_arm)/d(joint rate); trim limited to qp_arm_trim_max rad and leaking with qp_arm_leak_s;   // step 9.5e (imp = 3, qp_lateral): qp_yaw_trunk = 1 regulates the TRUNK (pelvis, torso, head) yaw, not the whole body: ground yaw moment target = -(I_trunk om^2 e + 2 om L_trunk) + qp_yaw_ff x d(L of the legs and arms)/dt (filtered, alpha qp_yaw_ff_alpha; the raw rate clamped to qp_yaw_ff_max W l when > 0, against touchdown spikes; |target| <= qp_yaw_cap W l); 0 = the whole-body yaw task of 9.5b
        // step 9.5f (imp = 3, qp_lateral + qp_yaw_trunk; all default 0 = off). qp_arm_ff = 1: the shoulders are driven by a yaw-momentum cancelling feed-forward instead of the imp 2 arm reference: arm rate = -qp_arm_ff_gain x L_legs / (2 S) (L_legs = vertical angular momentum about the CoM of thighs, shanks, feet, low-passed with qp_yaw_ff_alpha; S = d(L_arm)/d(shoulder rate)), integrated (leak qp_arm_ff_leak_s, limit qp_arm_ff_max rad), tracked with its own derivative feed-forward at qp_arm_ff_hz (0 = imp_arm_hz); qp_arm_ff_ref = 1 keeps the reference arm and adds the feed-forward on top; qp_arm_ff_hp_s: time constant of the high-pass that removes the mean of L_legs (0 = off); qp_arm_ff_ref_w w in [0, 1] replaces the measured L_legs by (1 - w) measured + w predicted from the QP row's own reference joint velocities (phase-locked, no lag). qp_yaw_budget = k > 0 (yaw-aware sagittal budget): the CoM-x task weight is scaled by max(qp_yaw_budget_floor, 1 - k s), s = yaw ground-moment target / the yaw moment the cone can carry (mu' sum fz r), so the pitch / angular-momentum task wins the cone when the yaw row takes a share of it
        public double qp_arm_ff = 0, qp_arm_ff_gain = 1.0, qp_arm_ff_hz = 3.0, qp_arm_ff_leak_s = 2.0, qp_arm_ff_max = 0.8, qp_arm_ff_ref = 0, qp_arm_ff_ref_w = 0, qp_arm_ff_hp_s = 1.0, qp_yaw_budget = 0, qp_yaw_budget_floor = 0.1;
        // step 9.5h (imp = 3; all default 0 = off): qp_ref_v4 = 1 reads gait_reference_qp_ladder_v4.json and gait_reference_qp_v4.json (the 3D-aware strides of 9.5g, with the shoulder_* reference arm swing; the Fr 0.10 v4 row replaces the main file's row at the end of the ladder); qp_arm_ref = 1: the shoulders track the reference arm swing of the active QP row (flexion + hand forward, anti-phase pair, qp_arm_ref_gain x, own acceleration feed-forward, tracked at qp_arm_ref_hz, 0 = imp_arm_hz, blended in over qp_arm_ref_blend_s after the hand-off); qp_arm_ref_yawff = w in [0, 1]: the trunk yaw task's feed-forward takes the arms' yaw-momentum rate (1 - w) measured + w predicted from the reference shoulder acceleration
        public double qp_ref_v5 = 0, qp_ref_v4 = 0, qp_arm_ref = 0, qp_arm_ref_gain = 1.0, qp_arm_ref_hz = 3.0, qp_arm_ref_blend_s = 0.5, qp_arm_ref_yawff = 0;
        // step 9.5i (imp = 3; default 0 = off): qp_td_rematch = d0 > 0: at each QP touchdown (re-anchor) whose measured state is farther than d0 (match distance, qp_match features and scales) from node 0 of the active row, the row restarts at its best-matching node anchored on the landed heel (needs qp_match); qp_late_cx = k: while the touchdown is late the CoM-x reference keeps moving at k x the reference CoM speed instead of freezing at the step end
        // qp_cx_emax = e > 0 (m): the CoM-x position error of the CoM row is clamped to +-e (an off-schedule re-anchor can then not command more than om_x^2 e of braking); re-match candidates are the row nodes before qp_td_min_frac of the step (a later node re-triggers the touchdown at once)
        public double qp_td_rematch = 0, qp_late_cx = 0, qp_cx_emax = 0;
        // step 9.5j (imp = 3; each default 0 = off): qp_rom = 1: inequality rows on qdd of the six QP joints, velocity-damper form: qd + qdd h within +-sqrt(2 a d) (d = distance of q to the DOF's anatomical limit minus qp_rom_margin_deg, h = qp_rom_horizon_s, a = qp_rom_amax rad/s2), and +-d / h back when q is past the margin point; the bound on qdd is limited to +-2 a; a tick whose rows are infeasible drops them;
        // qp_fp_rom = 1 (needs qp_rom): the foot-pitch task weight of a leg ramps to 0 as its ankle comes within qp_fp_rom_band_deg of the margin point (the QP folds the knee to reach a pitch the ankle stop forbids); qp_z_hold_ss = z (m): a scheduled-loaded stance point stays in the contact set through a force dropout while it is lower than z (qp_z_hold is the minimum)
        public double qp_rom = 0, qp_rom_margin_deg = 3.0, qp_rom_horizon_s = 0.1, qp_rom_amax = 30.0, qp_fp_rom = 0, qp_fp_rom_band_deg = 3.0, qp_z_hold_ss = 0;
        // step 9.5k (imp = 3; each default 0 / -1 = off): the PD under the QP feed-forward is a stiff servo to the LEGACY (imp 2) target; on the swing leg its torque is several times the QP's feed-forward and it drives the leg along the legacy foot path, not the QP's.
        // qp_pd_swing >= 0: PD stiffness scale of the swing leg of the QP (damping x sqrt) instead of qp_pd_scale (the stance leg keeps qp_pd_scale); qp_pd_ref = 1: the PD target / rate of the swing leg of the QP are the QP reference's joint angles / rates (hip = a + th, knee = a - b, ankle = f + b), 2: of both legs; qp_sw_follow = b in [0, 1]: the swing foot's reference x is shifted by b x (measured CoM x - reference CoM x - anchor offset), i.e. tied to the body, not to the stance-foot anchor (the swing heel task has kp = om_sw^2 = 16 /s2: a 3 m CoM-x error asks for 5 g of foot acceleration, the hip sits at its limit); qp_sw_amax = a (m/s2): the commanded swing-heel acceleration (reference + kp e + kd de) is limited to a (it reaches 100-800 m/s2 = the hip at its limit); qp_td_air_frac = f: a touchdown (re-anchor) needs the swing leg continuously unloaded for f x step time; qp_td_xoff_max = x (m): and a re-anchor offset within x of the current one; qp_td_heel = 1: only a heel contact of the swing leg is a touchdown (re-anchor), not a toe scuff; qp_diag = 1: log block qp.k (estimated PD torque of the six QP DOFs, swing-task state)
        public double qp_diag = 0, qp_pd_swing = -1, qp_pd_ref = 0, qp_td_heel = 0, qp_sw_follow = 0, qp_sw_amax = 0, qp_td_air_frac = 0, qp_td_xoff_max = 0;
        // step 9.5l (imp = 3; default 0 = off): qp_swj (weight, e.g. 1e3) = joint-space swing task: the swing leg's hip and knee (+ ankle if qp_swj_ankle = 1) track the reference q, qd, qdd of the QP clock (PD qp_swj_om /s, commanded joint acceleration limited to qp_swj_amax rad/s2); the world-frame heel task is then blended in over the last qp_swj_td_frac of the swing (smoothstep, 1 when late), its acceleration limit qp_sw_amax kept; the joint task fades to 50 % while the heel task is on
        public double qp_swj = 0, qp_swj_om = 2.0, qp_swj_amax = 6.0, qp_swj_td_frac = 0.2, qp_swj_ankle = 0;
        // step 9.5m (imp = 3; default 0 = off): qp_cdx_max = a (m/s2): the commanded CoM-x acceleration of the CoM row is limited to +-a (the cone / CoP carry 3-5 m/s2; an unreachable demand of 10-20 m/s2 trades the vertical task for friction capacity);
        // qp_sw_cp = k: the swing heel target is limited to the capture point of the measured CoM, x_com + k v_x sqrt(z_com / g) (k 0.8 = the reference's heel-CoM offset at the row speed; a lower speed puts the foot nearer, never beyond the reference target)
        public double qp_cdx_max = 0, qp_sw_cp = 0;
        // round 5 step 6a (imp = 3; qp_plan default 0 = off, imp 0/1/2 and qp_plan 0 unchanged): qp_plan = 1: step-level LIPM / capture-point planner instead of the CoM-x PD of the reference and the touchdown rematch. omega = sqrt(g / z0), z0 = the row reference's mean CoM height; xi = x_com + v_x / omega;
        // target heel x_fp = xi_pred(T_rem) - b (xi_pred = x_cop + (xi - x_cop) e^(omega T_rem), b = L / (e^(omega T) - 1), T / L = row step time / length), the step length from the stance heel limited to [qp_plan_lmin, qp_plan_lmax] x L; touchdown only from phase qp_plan_td_frac x T with the heel within qp_plan_td_tol x L of the target (forced at T).
        // The CoM row demands only a_x = omega^2 (x_com - x_cop_des), x_cop_des = xi - (xi_ref_dot - qp_plan_kxi (xi - xi_ref)) / omega clamped to the support points (qp_plan_wx weight, 0 = qp_w_cx); the stance vertical task tracks the row CoM z with qp_plan_zkp / qp_plan_zkd (/s2, /s) and weight qp_plan_wz;
        // the swing heel target (blended in at the end of the swing as qp_swj) is x_fp (landing quintic re-planned when x_fp moves by qp_plan_replan_m); the row clock is the phase since the touchdown (no rematch while qp_plan = 1)
        public double qp_plan = 0, qp_plan_kxi = 1.0, qp_plan_lmin = 0.5, qp_plan_lmax = 1.3, qp_plan_td_frac = 0.8, qp_plan_td_tol = 0.2, qp_plan_zkp = 4.0, qp_plan_zkd = 4.0, qp_plan_wz = 1e5, qp_plan_wx = 0, qp_plan_replan_m = 1.0, qp_plan_fp = 1.0, qp_plan_up = 0.8;
        // round 5 step 6b (qp_plan = 1): qp_plan_sw = 1 the planner owns the swing: heel x on a min-jerk curve from the lift-off heel to x_fp over T (x_fp low-passed with qp_plan_fp_tau s), heel z = the row's swing profile x qp_plan_apex, converted every tick to hip / knee (ankle: row foot pitch) targets by 2-link IK for the joint-space swing task (reference joints only when the IK fails), heel task weight >= qp_plan_heelw;
        // past phase qp_plan_down_frac a downward heel velocity qp_plan_vd (m/s) is added until contact; a support switch without contact at phase qp_plan_force_frac with the heel under qp_plan_force_z m, and at qp_plan_max_frac whatever the height. qp_plan_ladder = 1: the row follows the mean speed of the last step (climb at v >= row speed + qp_plan_vhyst, drop at v < the row below - qp_plan_vhyst, one row per step, at a QP touchdown, node 0)
        // round 6 step 7a (qp_plan = 1; qp_foot default 0 = off, everything else byte-identical): sole-orientation task. qp_foot = 1: every foot has a commanded world pitch f_cmd (+ toe down) used by the foot-pitch row, the swing IK and the ankle joint drive target
        // (ankle target = measured shank pitch + f_cmd, so the stiff stance ankle PD holds the sole level instead of the reference ankle angle). Swing leg: the reference pitch until the swing progress qp_foot_pre, then a smoothstep to level with a heel-first margin of qp_foot_heel_deg (toe up) at touchdown; its pitch-row weight x (1 + (qp_foot_w - 1) u).
        // Stance leg (qp_foot_stance = 1): f_cmd slews from the landing pitch to 0 (level) at qp_foot_rate rad/s; the pitch row of a leg with partial contact (heel or toe only) has weight x qp_foot_w. qp_foot_drive = 1 writes the ankle drive target. Diagnostics in qp.k[38..51] (needs qp_diag).
        public double qp_foot = 0, qp_foot_pre = 0.5, qp_foot_heel_deg = 2.0, qp_foot_stance = 1, qp_foot_w = 1.0, qp_foot_rate = 0.5, qp_foot_drive = 1, qp_foot_sat = 0.97;   // qp_foot_sat: applied ankle torque / force limit above which the ankle counts as saturated (diagnostic)
        // round 6 step 7b (qp_foot = 1): qp_foot_meas = 1: contact constraints from the measured contact points. Per-point shear cap mu x margin x measured point normal force (not the whole foot's); a foot with heel and toe both in contact keeps its centre of pressure
        // inside [heel + m, toe - m] (m = qp_foot_cop_m x the sole span) as two linear rows on the point forces; the DCM centre-of-pressure target is clamped to that inner interval for a flat foot and to the single contact point for a partial one.
        // round 6 step 7c (qp_foot = 1): qp_foot_unload = d (s, 0 = off): once the double-support clock passes qp_foot_unload_at x T_ds with the rear foot still loaded and the front leg carrying at least 30 % of the weight, the rear leg's normal-force cap ramps to zero over d (the load transfer is forced; round 5: the rear foot unloaded 4.5 s after the touchdown, leaving no time to swing)
        public double qp_foot_unload = 0, qp_foot_unload_at = 1.5;
        // round 6 step 7c (qp_foot = 1): qp_foot_zdip = g (0 = off): reach-driven CoM height. Round-6 diagnosis: the CoM stays at standing height (42.2 m) all step, the swing leg is straight (knee 4-9 deg) and its heel hovers 0.5-0.9 m above the ground
        // (IK out of reach 100-170 ticks per step). When the swing IK target is out of reach by e (m), the CoM height reference of the stance row is lowered by g x (e x D / |dz| + qp_foot_zdip_margin), at most qp_foot_zdip_max, rising at qp_foot_zdip_up and falling at qp_foot_zdip_dn (m/s)
        public double qp_foot_zdip = 0, qp_foot_zdip_max = 4.0, qp_foot_zdip_margin = 0.5, qp_foot_zdip_up = 1.0, qp_foot_zdip_dn = 0.5;
        public double qp_foot_meas = 0, qp_foot_cop_m = 0.08, qp_foot_fric_floor = 0.02;
        // round 7 (all default 0 = off; qp_plan = 1): qp_r7_fp = f in (0, 1]: the foothold keeps the DCM offset of the reference row at the touchdown node (heel_x = xi_pred - ((1-f) b + f (xi_ref - heel_ref))) instead of the point-foot LIPM b. qp_r7_noforce = 1: no support switch without a measured contact of the swing foot (the front foot must carry load before the switch; the swing target keeps descending while the touchdown is late). qp_r7_clock = 1: the swing clock starts at the reference end of the double support (row k_ds) even if the rear foot still carries load (the rear force cap ramps to 0 by the schedule). qp_foot_cop_heel_m / qp_foot_cop_toe_m (m, > 0 override the symmetric qp_foot_cop_m x sole span): measured-contact CoP window [heel + h, toe - t].
        public double exp_ankle_limit_nm = 0, exp_toe_m = 0;
        // round 14 body variant (0 = off, bit-identical): body_r14_sole_m = L replaces the 15 m sole by L m with the ankle kept at the same fraction (0.35 L behind it, 0.65 L ahead); body_r14_width_m = W (0 = keep) the sole width. Foot mass kept, inertia of the solid box, area = L x W. Applied by MechConfig.WithExperiments.
        // round 15 (default 0 = off, bit-identical): body_r15_stance_m = half stance width (m): hips, knees, ankles and feet of both legs move to x = +/- this value (the neutral 13 m stance is 6.5), so a 10 x 15 m sole (150 m^2, 327 kPa, M-006.4) fits without foot overlap (needs body_r14_sole_m / _width_m). Applied by MechConfig.WithExperiments. qp_r15_ref = 1: the imp 2 legacy reference (the walk before the QP hand-off) is the qp_ref_v5 main row instead of gait_reference_opt.json, so its foot geometry matches the plant (qp_ref_v5 = 10 selects the v7s10 rows made for the 10 m sole).
        public double body_r15_stance_m = 0, qp_r15_ref = 0;
        // round 16 (default 0 = off, bit-identical): qp_r16 = 1 turns on the learned residual on the lateral QP task (a linear policy, Tools/GaitLearn): lateral reference shift dy = qp_r16_dymax tanh(wy . f) (m) and lateral gain scale exp(clamp(wg . f, +-qp_r16_gmax)), f = [1, (stance-foot centre - CoM y)/l, (stance-foot centre - y_ref)/l, vy/sqrt(g l), single-support flag]. Weights 0 = no change.
        public double qp_r16 = 0, qp_r16_dymax = 4.0, qp_r16_gmax = 0.5, qp_r16_wy0, qp_r16_wy1, qp_r16_wy2, qp_r16_wy3, qp_r16_wy4, qp_r16_wg0, qp_r16_wg1, qp_r16_wg2, qp_r16_wg3, qp_r16_wg4;
        public double body_r14_sole_m = 0, body_r14_width_m = 0;   // round 7 body experiments (0 = off), applied by MechConfig.WithExperiments (scenario CLI variants only)
        public double qp_r7_fp = 0, qp_r7_noforce = 0, qp_r7_clock = 0, qp_foot_cop_heel_m = 0, qp_foot_cop_toe_m = 0;
        // round 8 (all default 0 = off; qp_plan = 1, qp_plan_sw = 1): qp_r8_retime = 1: the swing clock of the planner advances with the REALISED swing-foot progress instead of real time: the plan phase rate g = clamp(1 - qp_r8_gk x lag, qp_r8_gmin, qp_r8_gmax),
        // lag = MinJerk(phase) - (x_heel - x_lift) / (x_fp - x_lift) (planned minus measured fraction of the swing distance), multiplied by (1 - qp_r8_satk x s) with s = the saturation of the swing hip torque of the last tick above qp_r8_satfrac of its limit; the reference clock of the CoM / DCM rows and the earliest / forced touchdown times follow the retimed phase.
        // qp_r8_swlim = f (0 = off): the hard torque rows of the swing leg's hip and knee use f x the torque limit instead of qp_lim_frac while the swing leg is in the air (a saturation row that keeps torque for the stance leg and makes the swing lag, which the retime then waits for). qp_ref_v5 = 6 reads gait_reference_qp_ladder_v6.json / gait_reference_qp_v6.json (round-8 rows at Fr 0.005 .. 0.015).
        public double qp_r8_retime = 0, qp_r8_gmin = 0.3, qp_r8_gk = 3.0, qp_r8_gmax = 1.0, qp_r8_satk = 0, qp_r8_satfrac = 0.9, qp_r8_swlim = 0, qp_r8_refclock = 0, qp_r8_trem = 0;   // cycle 1 (retime of the whole reference clock: 13-17 s, worse than L7 30.8 s) -> qp_r8_refclock = 1: the CoM / DCM reference clock stays on real time (only the swing foot curve is retimed); qp_r8_trem = 1: the planner's remaining step time includes the stretch of the retimed swing, trem += (T_sw - s)(1 / g - 1)
        // round 9 (all default 0 = off; qp_plan = 1): stance-side start-up momentum. qp_r9_gate = f: the swing does not start (the CoM / DCM reference clock stays at the lift-off node) until the measured DCM offset of the CoM over the stance heel (CoM x + v / omega - heel x) is at least f x the reference's at the lift-off node, or qp_r9_gate_tmax s past the reference lift-off time.
        // qp_r9_launch = v (m/s): until the swing lifts off, the reference CoM x-velocity gets a bias v x deficit x ramp (deficit = clamp((qp_r9_vfrac x reference CoM speed at the lift-off node - measured) / that, 0, 1); ramp over qp_r9_ramp s from the step start), the reference CoM x gets its integral, which decays with time constant qp_r9_tau s after the lift-off and is reset at the touchdown.
        // round 10 (default 0 = off; qp_plan = 1 and qp_diag = 1): qp_r10_diag = 1 appends to the gait-log qp.k array PlanDiag[16..17] (launch offset, gate ratio) and the per-step record SagittalQpController.R10Log (DCM offset at touchdown vs the reference, step length and period vs the reference, rear-foot unload time, CoP target before the sole clamp and the clamp interval); read-only, changes no control output.
        // qp_r10_*: further round-10 flags below.
        // qp_r10_fp = f in (0, 1] (needs qp_plan = 1, ignored when qp_r7_fp > 0): the landing foothold keeps f x the reference's DCM offset at the touchdown node, scaled by (measured CoM speed / reference speed there, in [qp_r10_fp_min, 1]); the first quantity to diverge in the control is the DCM offset at the first QP touchdown (0.73 of the reference).
        public double qp_r10_diag = 0, qp_r10_fp = 0, qp_r10_fp_min = 0.3;
        // round 11 (default 0 = off; imp = 3): qp_r11_pdq = b in (0, 1]: the PD drive of the six sagittal leg DOFs the QP feeds forward is retargeted from the legacy imp-2 target to the state the QP intends one tick ahead
        // (q + qd dt + qdd* dt^2 / 2, rate qd + qdd* dt, qdd* = the QP solution mapped to the joint) by the blend b (1 = fully the QP's), so the PD only corrects deviations from the QP's motion instead of pulling toward a second plan.
        // Round-11 trace: the legacy-target PD adds 240 (single) / 390-435 (double support) MN m per joint on top of the QP's torque, and the measured CoM acceleration misses the QP's command by a median 0.8-1.5 m/s2.
        // qp_r11_pdq_k = s (default 1): extra stiffness / damping scale on those PD drives while qp_r11_pdq is on (damping x sqrt(s)).
        // qp_r11_pdq_mode (with qp_r11_pdq > 0): which legs are retargeted: 0 both legs always; 1 the stance leg only; 2 both legs only while the swing-parity leg still has a measured contact (double support); 3 all but the airborne swing leg.
        // Cycle 1: mode 0 removed the mean double-support braking (-0.7..-1.0 m/s2 with legacy targets) but the swing foot no longer lifts (the legacy swing PD lifts it), 19.5 s vs 30.8 s.
        public double qp_r11_pdq = 0, qp_r11_pdq_k = 1.0, qp_r11_pdq_mode = 0;
        // qp_r11_ds (needs qp_plan = 1, qp_plan_ds = 1): double-support CoP target. 0 = as before (step 6c: smoothstep from the rear foot's most forward contact point, its TOE, to the front foot over T_ds);
        // 1 = no sweep: the DCM-feedback CoP target clamped to the union of the contact intervals; 2 = the sweep starts at the rear-foot point nearest the DCM target. Round-11 trace: with 15 m soles and 8.65 m steps the rear toe lies ~5 m ahead of the front heel,
        // so the sweep starts ahead of the CoM (commanded cdx -0.1..-1.0 m/s2 in the first double support) and the measured CoM decelerates by a mean 0.7-1.0 m/s2 in every double support (all four control seeds).
        public double qp_r11_ds = 0;
        // round 12 (default 0 = off; needs qp_plan = 1): qp_r12_reartoe = 1: in double support (swing not started, both legs in the contact schedule) the rear (swing-parity) leg's toe normal-force cap is zero while the toe x > CoM x - qp_r12_reartoe_m,
        // and the rear sole stays under the sole-flat pitch task (qp_foot, weight qp_foot_w) while it still carries load. Round-11 trace: the rear toe carries 32-92 % of the load 3-8 m ahead of the CoM and brakes it by 0.7-1.0 m/s2 in every double support.
        public double qp_r12_reartoe = 0, qp_r12_reartoe_m = 0;
        // round 13 (default 0 = off; needs imp = 3 and qp_plan = 1): qp_r13_copservo = 1: while the swing-parity (rear) leg is still in the contact schedule and the other leg also is (double support) the rear ankle torque is a measured-CoP servo
        // (the legacy PD stiffness on that ankle is removed, damping kept at qp_r13_kd x the legacy value; the QP's ankle feed-forward is replaced): tau = sign x fn x (x_target - x_ankle) + integral of the measured CoP error (qp_r13_gi per s),
        // x_target = CoM x - qp_r13_m, held at or behind heel + qp_r13_heel. qp_r13_sign = +1 / -1 is the sign of (CoP - ankle) = sign x tau / fn in the ankle DOF convention (probe cycle).
        public double qp_r13_copservo = 0, qp_r13_m = 0.5, qp_r13_sign = 1, qp_r13_gi = 1.0, qp_r13_kd = 0.5, qp_r13_heel = 0.3, qp_r13_fmin = 0.02;
        public double qp_r9_gate = 0, qp_r9_gate_tmax = 1.0, qp_r9_launch = 0, qp_r9_ramp = 0.3, qp_r9_vfrac = 1.0, qp_r9_tau = 2.0;
        public double qp_plan_sw = 1, qp_plan_apex = 1.0, qp_plan_vd = 1.0, qp_plan_fp_tau = 0.5, qp_plan_heelw = 1.0, qp_plan_down_frac = 0.9, qp_plan_force_frac = 1.15, qp_plan_force_z = 0.3, qp_plan_max_frac = 1.3, qp_plan_ladder = 1, qp_plan_vhyst = 0.2, qp_plan_ds = 1, qp_plan_reach = 0.7;   // step 6c: qp_plan_ds = 1 explicit double support (CoP target rear -> front foot over the reference T_ds, swing clock from the measured lift-off at rear load < 10 %, T_swing = T - T_ds, the reference clock holds at the lift-off node until then); qp_plan_reach f: x_fp limited to what the swing hip can cover in the remaining time at f x its torque limit (bang-bang from the leg inertia; 0 = off)   // qp_plan_up u: with qp_plan the ladder climbs one row (at node 0, anchored on the landed heel) only at a QP touchdown once the forward speed is u x the next row's speed (no best-node restart); the hand-off row is the fastest row the speed reaches the same way
        public double qp_lateral = 0, qp_om_y = 0.5, qp_w_cy = 1e3, qp_w_roll = 1e3, qp_k_roll = 2.0, qp_w_yaw = 1e6, qp_om_yaw = 1.0, qp_ladder = 0, qp_ladder_up = 0.9, qp_w_tau = 0;   // qp_w_tau (9.5b): QP cost weight on sum (tau / LIM)^2, 0 = off;   // qp_ladder = 1 (9.5b start): the QP walks a speed ladder of @v2 rows (gait_reference_qp_ladder.json, then the Fr 0.10 row), one row up at a touchdown once the forward speed is qp_ladder_up x the next row's speed;   // step 9.5b (imp = 3, stage 2): qp_lateral = 1 makes the contact forces 3D (4 sole corners per foot, a friction pyramid with lateral generators shared with the sagittal shear) with lateral CoM (om_y, w_cy), roll momentum (k_roll, w_roll) and yaw momentum / heading (om_yaw, w_yaw) tasks; the stance hip yaw / roll and ankle roll take their feed-forward from the QP force; 0 = the sagittal QP of 9.5
        // step 9.5c (qp_match = 1, needs qp_engage_speed_frac > 0): state-matched hand-off. At a touchdown the measured state (CoM x / z relative to the landed heel, CoM velocity, trunk pitch, swing heel and toe x / z relative to the landed heel; 9 features, scaled by qp_match_len_scale x leg length, qp_match_vel_scale x sqrt(g L), qp_match_pitch_scale rad = 1, so pitch is nearly unweighted) is compared with every node of every ladder row; the QP starts at the best row / node when the RMS of the scaled differences <= qp_match_thr and no scaled difference exceeds qp_match_zmax. qp_match_ss = 1 evaluates the match on every single-support tick with the loaded leg as planar leg B (only reference nodes with the same contact set), not at touchdowns. On the ladder, after qp_match_hold touchdowns on a row (qp_match_climb_td = 1: touchdown events; else the QP's own re-anchors, climbing from a matching single-support state; qp_match_max_row caps the row), the next row is entered at its best node when the RMS <= qp_match_up_thr.
        public double qp_match_len_scale = 0.05, qp_match_vel_scale = 0.05, qp_match_pitch_scale = 1.0, qp_match_load_hi = 0.85, qp_match_load_lo = 0.05, qp_match_up_zmax = 2.25, qp_match_climb_td = 0, qp_match_max_row = -1, qp_match_ss = 0, qp_match = 0, qp_match_thr = 0.8, qp_match_zmax = 1.5, qp_match_up_thr = 1.2, qp_match_hold = 2;
        public double qp_solver_reg = 1e-12, qp_pd_scale = 0.25, qp_leg_a_left = 1, qp_engage_speed_frac = 0.0, qp_scale_min = 1.0, qp_scale_tau = 1.0;   // qp_scale_min < 1 (step 9.5): after the hand-off the reference clock runs at s = clamp(forward speed / reference speed, qp_scale_min, 1) of real time (filter qp_scale_tau s); 1 = real time. qp_engage_speed_frac: 0 = the QP starts when walking starts; > 0 = the imp 2 walk runs first and the QP takes over at the first touchdown of planar leg B once the CoM speed is this fraction of the reference speed;   // gain scale of the sagittal leg PD under the QP feed-forward (tune in 9.5); planar leg A is the left leg (1) or the right leg (0)
        public double yaw_budget_flat_wl, yaw_budget_edge_wl, yaw_filter;
        public double id_gravity, id_coriolis_gain, id_inertia_gain, id_accel_span;
        /// <summary>R-008 section 1 whole-stance schedule (imp = 1), one row per Froude number; required when imp &gt; 0 (ReferenceGait throws if absent).</summary>
        public StanceScheduleRow[] imp_stance_schedule;
    }

    /// <summary>One row of the imp = 1 stance schedule (research/R-008/model.py): foot placement fraction, hip height (m at l = 42) with four Fourier terms (m), heel-strike pitch, foot-flat and heel-rise fractions of stance, toe-off pitch, pelvic tilt, pelvis yaw amplitude (deg).</summary>
    [Serializable]
    public sealed class StanceScheduleRow
    {
        public double froude, reach_frac, hip_height, hip_c1, hip_s1, hip_c2, hip_s2, heel_strike_deg, foot_flat_at, heel_rise_at, toe_off_deg, tilt_deg, yaw_deg;
    }
}
