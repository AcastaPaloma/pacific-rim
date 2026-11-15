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
        // 9.5g optional (absent in older rows, then 0): shoulder flexion (+ hand forward), rate and torque of the straight arm on the same lateral side as this leg; anti-phase pair from the 3D-aware stride (stride_opt_tc.py --yaw)
        public double shoulder_deg, shoulder_dps, shoulder_tau_nm;
    }

    [Serializable]
    public sealed class GaitRefNode
    {
        public double yaw_mz_nm, yaw_m0_nm, yaw_mt_nm, yaw_budget_nm, yaw_cone_use;   // 9.5g optional (0 when absent): yaw moment of legs + arms about the CoM, of the planned shear, free ground torsion, torsion capacity left, shared-cone use
        public double phase, t_s, pelvis_x_m, pelvis_x_rel_m, pelvis_z_m, pelvis_pitch_deg;
        public GaitRefLeg stance, swing;
        // @v2 additions (gait_reference_qp.json; absent in @v1 files, then null / 0): planar q9, qd9, forces [ft, fn] of the 4 sole points, CoM and its derivatives, angular momentum about the CoM, sole points (position, velocity, acceleration), SI
        public double[] q9, qd9, f4x2, com, comd, comdd, pts, ptv, pta;
        public double ang_mom, ang_mom_dot;
    }

    /// <summary>One optimised stride at one Froude number: nodes over one step, node N = node 0 with stance and swing swapped.</summary>
    [Serializable]
    public sealed class GaitRefRow
    {
        public double fr, step_time_s, step_length_m, v, leg_length_m, duty_factor, max_eq_violation;
        public double body_mass_kg, mu, thigh_m, shank_m, heel_x_m, toe_x_m, ankle_height_m;   // @v2 only: planar model data (leg segment lengths, sole point x relative to the ankle, ankle height)
        public string verdict, source_file;
        public GaitRefSchedule schedule;
        public GaitRefNode[] nodes;
    }

    [Serializable]
    public sealed class GaitReferenceData
    {
        public const string Schema = "gait_reference_opt@v1", SchemaV2 = "gait_reference_opt@v2";
        /// <summary>Reference file (under Config/Mech) for a walk tuning: the imp = 3 QP reads the @v2 file, imp 1.5 .. 2.5 the @v1 file, below 1.5 none.</summary>
        public const double QpImp = 2.5;   // imp at and above which the whole-body QP (imp = 3) is built
        public const string LadderFile = "gait_reference_qp_ladder_v3.json";   // step 9.5b: low-Froude @v2 rows of the imp 3 start ladder (qp_ladder); step 9.5f: the v3 file (Fr 0.02 .. 0.09; the old gait_reference_qp_ladder.json has 0.02 .. 0.06)
        public static string FileFor(double imp) => imp >= QpImp ? "gait_reference_qp.json" : imp >= 1.5 ? "gait_reference_opt.json" : null;
        public string schema, notes;
        public GaitRefSource source;
        public GaitRefConventions conventions;
        public GaitRefRow[] rows;

        /// <summary>Throws when the file is not usable: schema, row order, node counts, FEASIBLE verdicts.</summary>
        public void Validate()
        {
            if (schema != Schema && schema != SchemaV2) throw new InvalidOperationException("gait reference schema '" + schema + "' is not " + Schema + " or " + SchemaV2);
            if (rows == null || rows.Length == 0) throw new InvalidOperationException("gait reference has no rows");
            for (int i = 0; i < rows.Length; i++)
            {
                var r = rows[i];
                if (i > 0 && !(r.fr > rows[i - 1].fr)) throw new InvalidOperationException("gait reference rows are not sorted by Froude number");
                if (r.verdict != "FEASIBLE") throw new InvalidOperationException("gait reference row " + r.fr + " is not FEASIBLE");
                if (r.nodes == null || r.nodes.Length < 4 || r.nodes.Length != rows[0].nodes.Length) throw new InvalidOperationException("gait reference row " + r.fr + " has a wrong node count");
                if (!(r.step_time_s > 0) || !(r.step_length_m > 0) || !(r.leg_length_m > 0)) throw new InvalidOperationException("gait reference row " + r.fr + " has a non-positive step time, length or leg length");
                if (schema == SchemaV2)
                {
                    if (!(r.body_mass_kg > 0) || !(r.mu > 0)) throw new InvalidOperationException("gait reference @v2 row " + r.fr + " has no body mass or friction coefficient");
                    foreach (var n in r.nodes)
                        if (n.q9 == null || n.q9.Length != 9 || n.qd9 == null || n.qd9.Length != 9 || n.f4x2 == null || n.f4x2.Length != 8 || n.com == null || n.com.Length != 2 || n.comd == null || n.comd.Length != 2 || n.comdd == null || n.comdd.Length != 2
                            || n.pts == null || n.pts.Length != 8)
                            throw new InvalidOperationException("gait reference @v2 row " + r.fr + " has a node without the planar data (q9, qd9, f4x2, com, comd, comdd, pts)");
                }
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
