using UnityEditor;
using UnityEngine;

namespace Game.Physics.Body.Editor
{
    /// <summary>
    /// Writes the project-wide PhysX settings that have no runtime API (R-005 section 4: R3 solver type, R15 improved patch friction, R16 enhanced determinism)
    /// into ProjectSettings/DynamicsManager.asset through SerializedObject. Run: -executeMethod Game.Physics.Body.Editor.PhysicsProjectSettings.Apply
    /// </summary>
    public static class PhysicsProjectSettings
    {
        public const int SolverTemporalGaussSeidel = 1;

        public static void Apply()
        {
            var so = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset")[0]);
            so.FindProperty("m_SolverType").intValue = SolverTemporalGaussSeidel;
            so.FindProperty("m_EnableEnhancedDeterminism").boolValue = true;
            so.FindProperty("m_ImprovedPatchFriction").boolValue = true;
            so.FindProperty("m_SimulationMode").intValue = 0; // FixedUpdate: scenario code switches to Script mode only in memory
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log("[Physics.Body] DynamicsManager: solver=TGS, enhanced determinism on, improved patch friction on");
        }

        public static string Describe()
        {
            var so = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset")[0]);
            return "solverType=" + so.FindProperty("m_SolverType").intValue + " enhancedDeterminism=" + so.FindProperty("m_EnableEnhancedDeterminism").boolValue + " improvedPatchFriction=" + so.FindProperty("m_ImprovedPatchFriction").boolValue;
        }
    }
}
