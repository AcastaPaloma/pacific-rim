using System;
using System.IO;
using Game.Rendering.Pixel;
using Game.Rendering.Pixel.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Tools.StyleCapture
{
    /// <summary>Command-line entry points of the capture tool (Unity CLI -executeMethod, batch mode, no -nographics).</summary>
    public static class StyleCaptureCli
    {
        public const string M0Configuration = "Assets/Game/Config/Rendering/Configurations/M0StyleFrame.json";

        public static string Arg(string name, string fallback = null)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return fallback;
        }

        /// <summary>Installs the pipeline assets and builds the test scenes (all by Editor script).</summary>
        public static void Setup()
        {
            PixelPipelineInstaller.InstallAll();
            StyleTestScenes.BuildAll();
        }

        /// <summary>Quick look: one frame per profile from the main viewpoint, written as plain PNGs to -captureOut.</summary>
        public static void Preview()
        {
            string outDir = Arg("-captureOut", "Temp/pixel_preview");
            Directory.CreateDirectory(outDir);
            var catalog = AssetDatabase.LoadAssetAtPath<PixelPipelineCatalog>(PixelPipelineInstaller.CatalogPath);
            using (new PixelPipelineScope(catalog.pipeline))
            {
                EditorSceneManager.OpenScene(StyleTestScenes.StylePath, OpenSceneMode.Single);
                using (var rig = new PixelCaptureRig())
                {
                    rig.Binder.Configure(PixelCaptureRig.LoadSettings(), PixelCaptureRig.LoadFlags(M0Configuration));
                    rig.SetOutputSize(1920, 1080);
                    foreach (var profile in catalog.profiles)
                    {
                        rig.Binder.profile = profile;
                        rig.Render();
                        var f = rig.Read(true);
                        PixelCaptureRig.WriteRgbPng(Path.Combine(outDir, profile.profileId + "_internal.png"), f.Rgb, f.Width, f.Height);
                        PixelCaptureRig.WriteRgbPng(Path.Combine(outDir, profile.profileId + "_output.png"), f.OutputRgb, f.OutputWidth, f.OutputHeight);
                        PixelCaptureRig.WriteGreyPng(Path.Combine(outDir, profile.profileId + "_index.png"), f.Index, f.Width, f.Height);
                        var d16 = new ushort[f.Depth.Length];
                        PixelCaptureRig.EncodeDepthLog16(f.Depth, rig.World.nearClipPlane, rig.World.farClipPlane, d16);
                        PixelCaptureRig.WriteGrey16Png(Path.Combine(outDir, profile.profileId + "_depth.png"), d16, f.Width, f.Height);
                    }
                }
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            Debug.Log("[StyleCapture] preview written to " + outDir);
        }
    }
}
