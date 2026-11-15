using System;

namespace Game.Units.Mech
{
    // Data spec gait_reference_opt@v1 (Assets/Game/Config/Mech/gait_reference_opt.json), written by Tools/GaitOpt/export_reference.py. Plain serialisable fields so the file loads with the
    // same JSON library as walk_tuning.json (JsonUtility in Unity, System.Text.Json with IncludeFields in the dotnet tests: see Repo.Load). Conventions are in the file's "conventions" block.
    [Serializable] public sealed class GaitRefSource { public string solver_commit, solver_scripts, exporter; public string[] files, verdicts; }
    [Serializable] public sealed class GaitRefConventions { public string model, angles, mapping_to_reference_gait, torques, forces, roles, frame, units; }
    [Serializable] public sealed class GaitRefSchedule { public int k_ds, k_ff, k_hr; }

    /// <summary>One leg at one node. Angles in degrees, rates in degrees/s, torques in N m, forces in N.</summary>
    [Serializable]
    public sealed class GaitRefLeg
    {
        public double hip_deg, knee_deg, ankle_deg, hip_dps, knee_dps, ankle_dps, foot_pitch_deg, ankle_x_m, ankle_height_m, hip_tau_nm, knee_tau_nm, ankle_tau_nm, heel_ft_n, heel_fn_n, toe_ft_n, toe_fn_n;
        public bool heel_contact, toe_contact;
    }

    [Serializable]
    public sealed class GaitRefNode
    {
        public double phase, t_s, pelvis_x_m, pelvis_x_rel_m, pelvis_z_m, pelvis_pitch_deg;
        public GaitRefLeg stance, swing;
    }

    /// <summary>One optimised stride at one Froude number: nodes over one step, node N = node 0 with stance and swing swapped.</summary>
    [Serializable]
    public sealed class GaitRefRow
    {
        public double fr, step_time_s, step_length_m, v, leg_length_m, duty_factor, max_eq_violation;
        public string verdict, source_file;
        public GaitRefSchedule schedule;
        public GaitRefNode[] nodes;
    }

    [Serializable]
    public sealed class GaitReferenceData
    {
        public const string Schema = "gait_reference_opt@v1";
        public string schema, notes;
        public GaitRefSource source;
        public GaitRefConventions conventions;
        public GaitRefRow[] rows;

        /// <summary>Throws when the file is not usable: schema, row order, node counts, FEASIBLE verdicts.</summary>
        public void Validate()
        {
            if (schema != Schema) throw new InvalidOperationException("gait reference schema '" + schema + "' is not " + Schema);
            if (rows == null || rows.Length == 0) throw new InvalidOperationException("gait reference has no rows");
            for (int i = 0; i < rows.Length; i++)
            {
                var r = rows[i];
                if (i > 0 && !(r.fr > rows[i - 1].fr)) throw new InvalidOperationException("gait reference rows are not sorted by Froude number");
                if (r.verdict != "FEASIBLE") throw new InvalidOperationException("gait reference row " + r.fr + " is not FEASIBLE");
                if (r.nodes == null || r.nodes.Length < 4 || r.nodes.Length != rows[0].nodes.Length) throw new InvalidOperationException("gait reference row " + r.fr + " has a wrong node count");
                if (!(r.step_time_s > 0) || !(r.step_length_m > 0) || !(r.leg_length_m > 0)) throw new InvalidOperationException("gait reference row " + r.fr + " has a non-positive step time, length or leg length");
            }
        }
    }

    /// <summary>Feed-forward and contact data of the optimised reference at a stride phase, for the controller (round 3 step 8.5). Left and right as in <see cref="GaitSample"/>.</summary>
    public struct OptimisedLeg
    {
        public double HipRate, KneeRate, AnkleRate;            // rad/s, same sign conventions as LegSample angles
        public double HipTorque, KneeTorque, AnkleTorque;      // N m, conjugate to the joint angles (positive = towards more flexion / plantarflexion), reference mech size
        public double HeelNormal, HeelTangential, ToeNormal, ToeTangential;   // N, per sole point
        public bool HeelContact, ToeContact;
        public bool IsStance;
    }

    public struct OptimisedSample
    {
        public double Phase;
        public double PelvisX;       // m, in the frame moving with the mean speed
        public OptimisedLeg L, R;
    }
}
