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
        public double imp_waist_hz = 0.8, imp_waist_zeta = 1.0, imp_arm_hz = 0.6, imp_arm_zeta = 1.0;
        public double imp_ff_gravity = 1.0, imp_ff_coriolis = 1.0, imp_ff_accel = 1.0, imp_ff_stance_gravity_share = 0.0;   // round 2 step 4: gains on the body's inverse dynamics; share of the gravity term applied on stance-leg DOFs   // round 2 step 3: waist yaw and shoulder drive of the tracked upper body
        public double yaw_budget_flat_wl, yaw_budget_edge_wl, yaw_filter;
        public double id_gravity, id_coriolis_gain, id_inertia_gain, id_accel_span;
    }
}
