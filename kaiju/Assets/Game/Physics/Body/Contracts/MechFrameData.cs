using System;

namespace Game.Physics.Body.Contracts
{
    /// <summary>
    /// Data spec <c>mech_frame@v1</c> (research/R-005 section 1.5). Field names mirror the JSON keys exactly so the same classes load through
    /// UnityEngine.JsonUtility (engine) and System.Text.Json with IncludeFields (dotnet tests). Units: m, kg, s, degrees. Frame: x right, y up, z forward.
    /// </summary>
    [Serializable]
    public sealed class MechFrameData
    {
        public const string Schema = "mech_frame@v1";
        public string schema;
        public string id;
        public double total_height_m;
        public double total_mass_kg;
        public double[] com_rest;
        public double hull_volume_m3;
        public double mean_hull_density_kg_m3;
        public StanceData stance;
        public FootSoleData foot_sole;
        public SegmentData[] segments;
        public JointData[] joints;
        public int m0_active_dof;
    }

    [Serializable] public sealed class StanceData { public double neutral_stance_width_m; }

    [Serializable]
    public sealed class FootSoleData
    {
        public double length_m, width_m, area_m2, heel_z_m, toe_z_m, ankle_z_m, ankle_height_m;
    }

    [Serializable]
    public sealed class SegmentData
    {
        public string id, kind, side, parent;
        public double[] proximal_joint, distal_end, com, inertia_principal_kgm2;
        public double length_m, width_x_m, height_y_m, depth_z_m, mass_kg, hull_volume_m3, hull_density_kg_m3;
    }

    [Serializable]
    public sealed class JointData
    {
        public string id, parent, child, unity_joint_type;
        public double[] position;
        public DofData[] dofs;
    }

    [Serializable]
    public sealed class DofData
    {
        public string name;
        public double[] axis;
        public string unity_drive, positive;
        public int unity_sign;
        public double[] rom_deg;
        public string m0;
    }
}
