using System;
using Game.Control.Contracts;
using Game.Core;
using Game.Physics.Body.Contracts;

namespace Game.Units.Mech.Contracts
{
    public static class MechContractVersions { public const int Unit = 1; public const string GaitLog = "gait_log@v1"; }

    public enum LocoPhase { Stand = 0, Shift = 1, DoubleSupport = 2, SingleSupport = 3, Falling = 4, Fallen = 5 }

    /// <summary>Why the command differs from the intent as received (C14: never obeyed unphysically, never refused silently).</summary>
    [Flags]
    public enum LimitCause
    {
        None = 0, EffortRange = 1, Accel = 2, Decel = 4, YawRate = 8, YawAccel = 16, LateralAccel = 32, StopDistance = 64, TurnBeforeWalk = 128, Arrived = 256, Fallen = 512
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
        public double accel_start_g, decel_normal_g, decel_emergency_g, lateral_accel_g, yaw_rate_max_tstar_deg, yaw_accel_max_tstar_deg;
        public double turn_first_deg, arrive_fraction_of_step, stop_speed_fr, max_step_yaw_deg;
        // foot placement (fractions of leg length unless noted)
        public double step_width_walk_frac, cop_inboard_frac, cop_heel_frac, cop_toe_frac, cop_stand_frac, sole_margin, reach_forward_frac, reach_back_frac, reach_side_frac, foot_gap_frac;
        // body carriage
        public double com_drop_stand_frac, com_drop_walk_frac, step_reach_margin_frac, step_length_max_frac, yaw_grip_radius_frac, touchdown_share, capture_trigger_frac, capture_step_time_tstar, height_hz, attitude_hz, yaw_hz, attitude_torque_frac, yaw_torque_frac, dcm_gain, force_min_bw, force_max_bw;
        // swing
        public double swing_clearance_frac, touchdown_speed_tstar, swing_hover, swing_retarget_tstar, stand_min_share, knee_min_flex_deg, swing_freeze, shift_time_tstar, liftoff_load_bw, arm_swing_gain, elbow_flex_deg;
        public double stance_knee_damping_ratio, stance_hip_damping_ratio, stance_ankle_damping_ratio, stance_posture_hz;
        // falling
        public double fall_tilt_deg, fall_height_frac, fall_damping_ratio, rest_speed_fr;
        // stomp
        public double stomp_lift_frac, stomp_speed_tstar;
    }
}
