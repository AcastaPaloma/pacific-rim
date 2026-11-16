// Editor/batch tools of the M1 slice (T-039b): builds M1Slice.unity and its materials, and the macOS development player.
//   unity run <project> -- -executeMethod Game.Slice.Editor.SliceEditorTools.BuildScene
//   unity run <project> -- -executeMethod Game.Slice.Editor.SliceEditorTools.BuildMac
using System.IO;
using Game.Rendering.Pixel;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Slice.Editor
{
    public static class SliceEditorTools
    {
        public const string ScenePath = "Assets/Game/Slice/Scenes/M1Slice.unity";
        const string MatDir = "Assets/Game/Slice/Materials";
        const string CatalogPath = "Assets/Game/Config/Rendering/PixelPipelineCatalog.asset";

        static Material Mat(string name, Color c)
        {
            string path = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit");
                m = new Material(sh); AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", 0.1f);
            EditorUtility.SetDirty(m);
            return m;
        }

        public static void BuildScene()
        {
            Game.Core.Unity.CoreConfigAsset.CreateOrUpdate();   // picks up Config/FeatureFlags/Slice.json
            Directory.CreateDirectory(MatDir); Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            AssetDatabase.Refresh();
            var kaiju = Mat("SliceKaiju", new Color(0.55f, 0.12f, 0.10f));
            var mech = Mat("SliceMech", new Color(0.25f, 0.45f, 0.75f));
            var imp = Mat("SliceImpactor", new Color(0.9f, 0.8f, 0.2f));
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("SliceBootstrap");
            var b = go.AddComponent<SliceBootstrap>();
            b.sliceFlags = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Slice/Config/slice_m1.flags.json");
            b.feelcheckFlags = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Slice/Config/slice_feelcheck.flags.json");   // T-189
            b.demoFlags = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Slice/Config/slice_demo.flags.json");   // T-200
            b.mechFrameJson = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/Mech/mech_frame_balanced.json");
            b.mechBodyJson = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/Mech/body_tuning.json");
            b.mechWalkJson = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/Mech/walk_tuning.json");
            b.mechWalkFastJson = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/Mech/walk_tuning_fast.json");   // T-201
            b.cameraSettings = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/Camera/ThirdPersonRig.json");
            b.geoSettings = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/WorldGeo.json");
            b.controlTuning = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/Control/control_tuning.json");
            b.hudLayoutJson = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/UI/hud_layout.json");   // T-154
            b.hudColoursJson = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/UI/hud_colours.json");
            b.pixelCatalog = AssetDatabase.LoadAssetAtPath<PixelPipelineCatalog>(CatalogPath);
            b.kaijuMaterial = kaiju; b.mechMaterial = mech; b.impactorMaterial = imp;
            if (b.sliceFlags == null || b.cameraSettings == null || b.pixelCatalog == null || b.geoSettings == null || b.controlTuning == null) throw new System.InvalidOperationException("[Slice] missing flag file, camera settings or pixel catalog");
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[Slice] wrote " + ScenePath);
        }

        public static void BuildMac()
        {
            BuildScene();
            string outDir = "Builds/M1Slice";
            Directory.CreateDirectory(outDir);
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath }, locationPathName = outDir + "/M1Slice.app", target = BuildTarget.StandaloneOSX,
                options = BuildOptions.Development,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log("[Slice] build " + report.summary.result + " size " + report.summary.totalSize + " bytes, " + report.summary.totalTime);
            if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
