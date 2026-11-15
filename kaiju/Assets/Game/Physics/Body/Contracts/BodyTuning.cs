using System;

namespace Game.Physics.Body.Contracts
{
    /// <summary>
    /// Data spec <c>body_tuning@v1</c>: every number the articulated body needs that is not geometry or mass (those come from mech_frame@v1).
    /// File: Assets/Game/Config/Mech/body_tuning.json. Sources are cited per field in that file's "notes".
    /// </summary>
    [Serializable]
    public sealed class BodyTuning
    {
        public const string Schema = "body_tuning@v1";
        public string schema;
        /// <summary>The mass and height the force limits and the reactor figure were written for (R-005 reference mech). Other sizes scale them (R-005 section 1.4).</summary>
        public double reference_mass_kg, reference_height_m;
        /// <summary>PhysX mass unit in kg. Measured (evidence/T-005/probes): articulation links heavier than about 1e5 units get no contact impulse, so the body is simulated in a scaled mass unit; all contract values stay SI.</summary>
        public double sim_mass_unit_kg;
        public double gravity_mps2;
        public int solver_iterations, solver_velocity_iterations;
        public double contact_offset_m, max_depenetration_mps, sleep_threshold;
        public double linear_damping, angular_damping, joint_friction;
        public double max_linear_velocity_mps, max_angular_velocity_rps;
        public double leg_max_joint_velocity_rps, arm_max_joint_velocity_rps, body_max_joint_velocity_rps;
        public double friction_static, friction_dynamic, restitution;
        public string[] continuous_collision_kinds;
        /// <summary>R-001 contact hysteresis as fractions of body weight (F_on 0.03, F_off 0.01).</summary>
        public double contact_on_bw, contact_off_bw;
        /// <summary>Loading window after a contact begins, in t* = sqrt(l/g), over which FootLoaded takes the peak force and the impulse (R-001 section 6: 0.10 of the nominal stride).</summary>
        public double contact_load_window_tstar;
        public double damping_ratio;
        public JointKindTuning[] joint_kinds;
        public double reactor_power_w;

        /// <summary>R-005 section 1.4: torques scale with M H, power with M H^0.5. Returns a copy for a frame of the given mass and height.</summary>
        public BodyTuning ScaledFor(double massKg, double heightM)
        {
            var t = (BodyTuning)MemberwiseClone();
            double km = massKg / reference_mass_kg, kl = heightM / reference_height_m;
            t.reference_mass_kg = massKg; t.reference_height_m = heightM;
            t.reactor_power_w = reactor_power_w * km * Math.Sqrt(kl);
            t.joint_kinds = new JointKindTuning[joint_kinds.Length];
            for (int i = 0; i < joint_kinds.Length; i++) t.joint_kinds[i] = new JointKindTuning { kind = joint_kinds[i].kind, force_limit_nm = joint_kinds[i].force_limit_nm * km * kl, swing_hz = joint_kinds[i].swing_hz / Math.Sqrt(kl), stance_hz = joint_kinds[i].stance_hz / Math.Sqrt(kl) };
            return t;
        }

        public JointKindTuning Kind(string kind)
        {
            if (joint_kinds != null) foreach (var k in joint_kinds) if (k.kind == kind) return k;
            return null;
        }
    }

    /// <summary>Per joint kind (hip, knee, ankle, waist, shoulder, elbow): torque limit (R-005 section 3.3) and the natural frequencies the gain rule k = I w^2 uses (section 3.2).</summary>
    [Serializable]
    public sealed class JointKindTuning
    {
        public string kind;
        public double force_limit_nm;
        public double swing_hz, stance_hz;
    }
}
