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

        /// <summary>
        /// Writes style_capture@v1 capture sets. Arguments: -captureOut &lt;dir&gt; (default evidence/T-004/captures),
        /// -captureJobs &lt;list&gt; (stills,hazecal,m004,m004ab,subpixel,controls,stages,cut,orbit,dolly; default all), -captureProfiles &lt;list&gt;,
        /// -captureCommit &lt;hash&gt; (default: git rev-parse HEAD).
        /// </summary>
        public static void Run()
        {
            string outRoot = Arg("-captureOut", "evidence/T-004/captures");
            var jobs = new System.Collections.Generic.HashSet<string>(Arg("-captureJobs", "stills,hazecal,m004,m004ab,subpixel,controls,stages,cut,orbit,dolly").Split(','));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            // Count every error and warning Unity logs during the capture: the evidence must show a clean log.
            int errors = 0, warnings = 0; var firstMessages = new System.Collections.Generic.List<string>();
            Application.LogCallback onLog = (message, stack, type) =>
            {
                if (type == LogType.Log) return;
                if (type == LogType.Warning) warnings++; else errors++;
                if (firstMessages.Count < 8) firstMessages.Add(type + ": " + message);
            };
            Application.logMessageReceived += onLog;
            try
            {
            if (jobs.Contains("hazecal"))
                using (var s = new StyleCaptureSession(StyleTestScenes.HazeCalPath, outRoot)) StyleCaptureJobs.HazeCal(s);
            using (var s = new StyleCaptureSession(StyleTestScenes.StylePath, outRoot))
            {
                if (jobs.Contains("stills")) StyleCaptureJobs.Stills(s);
                if (jobs.Contains("controls")) StyleCaptureJobs.Controls(s);
                if (jobs.Contains("stages")) StyleCaptureJobs.Stages(s);
                if (jobs.Contains("cut")) StyleCaptureJobs.Cut(s);
                if (jobs.Contains("m004")) StyleCaptureJobs.M004(s);
                if (jobs.Contains("m004ab")) StyleCaptureJobs.M004Ab(s);
                if (jobs.Contains("subpixel")) StyleCaptureJobs.SubPixel(s);
                if (jobs.Contains("orbit")) StyleCaptureJobs.Orbit(s);
                if (jobs.Contains("dolly")) StyleCaptureJobs.Dolly(s);
            }
            }
            finally { Application.logMessageReceived -= onLog; }
            string summary = "[StyleCapture] jobs " + string.Join(",", jobs) + " written to " + outRoot + " in " + watch.Elapsed.TotalSeconds.ToString("F1") + " s; log: " + errors + " errors, " + warnings + " warnings";
            Debug.Log(summary);
            File.WriteAllText(Path.Combine(outRoot, "capture_log_summary.txt"), summary + "\n" + string.Join("\n", firstMessages) + (firstMessages.Count > 0 ? "\n" : ""));
            if (errors > 0) throw new InvalidOperationException("capture logged " + errors + " errors; first: " + firstMessages[0]);
        }
    }
}
