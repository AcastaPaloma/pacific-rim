using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Game.Physics.Body.Contracts;
using Game.Physics.Body.Unity;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Physics.Body.Editor
{
    /// <summary>unity run &lt;project&gt; --no-tail --log-file &lt;log&gt; -- -executeMethod Game.Physics.Body.Editor.TruthScenesCli.Run -out &lt;dir&gt; [-height m -mass kg]</summary>
    public static class TruthScenesCli
    {
        static string Arg(string name, string fallback = null)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return fallback;
        }

        public static void Run()
        {
            string outDir = Arg("-out", "Temp/truth");
            string cfg = Path.Combine(Application.dataPath, "Game/Config");
            var frame = JsonUtility.FromJson<MechFrameData>(File.ReadAllText(Path.Combine(cfg, "Mech/mech_frame_balanced.json")));
            var tuning = JsonUtility.FromJson<BodyTuning>(File.ReadAllText(Path.Combine(cfg, "Mech/body_tuning.json")));
            if (Arg("-height") != null || Arg("-mass") != null)
            {
                double h = double.Parse(Arg("-height", "80"), System.Globalization.CultureInfo.InvariantCulture), m = double.Parse(Arg("-mass", "1e7"), System.Globalization.CultureInfo.InvariantCulture);
                frame = MechFrameOps.Scale(frame, h, m); tuning = tuning.ScaledFor(m, h);
            }
            double dt = Game.Core.CoreSettings.Parse(File.ReadAllText(Path.Combine(cfg, "CoreSettings.json")), "CoreSettings.json").FixedStepSeconds;
            string commit = "unknown";
            try { var p = Process.Start(new ProcessStartInfo("git", "rev-parse --short HEAD") { WorkingDirectory = Application.dataPath, RedirectStandardOutput = true, UseShellExecute = false }); commit = p.StandardOutput.ReadToEnd().Trim(); p.WaitForExit(); } catch (Exception) { }
            string engine = "\"solver_type\":\"" + (PhysicsProjectSettings.SolverType() == 1 ? "TemporalGaussSeidel" : "ProjectedGaussSeidel") + "\",\"enhanced_determinism\":" + (PhysicsProjectSettings.EnhancedDeterminism() ? "true" : "false") + ",\"improved_patch_friction\":" + (PhysicsProjectSettings.ImprovedPatchFriction() ? "true" : "false");
            Directory.CreateDirectory(Path.Combine(outDir, "physics"));
            var results = new List<TruthScenes.Result>();
            var scenes = new List<Func<TruthScenes.Result>>
            {
                () => TruthScenes.UnitSanity(frame, tuning, dt, outDir, commit, engine),
                () => TruthScenes.Drop(frame, tuning, dt, outDir, commit, engine),
                () => TruthScenes.Drop(frame, tuning, dt, outDir, commit, engine, 20f, 2000f, "drop_x2km"),
                () => TruthScenes.KneePendulum(frame, tuning, dt, outDir, commit, engine),
                () => TruthScenes.Impact(frame, tuning, dt, outDir, commit, engine, 0, 0, "impact_e0_headon"),
                () => TruthScenes.Impact(frame, tuning, dt, outDir, commit, engine, 0, 5, "impact_e0_offset5"),
                () => TruthScenes.Impact(frame, tuning, dt, outDir, commit, engine, 0.5, 5, "impact_e05_offset5"),
                () => TruthScenes.Topple(frame, tuning, dt, outDir, commit, engine, false, 0, "topple_locked"),
                () => TruthScenes.Topple(frame, tuning, dt, outDir, commit, engine, true, 0.3, "topple_limp"),
            };
            try
            {
                foreach (var scene in scenes)
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    try { results.Add(scene()); } catch (Exception e) { var r = new TruthScenes.Result { Scene = "exception_" + results.Count }; UnityEngine.Debug.LogError("[TruthScenes] " + e); results.Add(r); }
                    File.WriteAllText(Path.Combine(outDir, "truth_scenes.json"), "{\"commit\":\"" + commit + "\",\"frame\":\"" + frame.id + "\",\"height_m\":" + frame.total_height_m + ",\"mass_kg\":" + frame.total_mass_kg + ",\"fixed_dt\":" + dt.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ",\"engine\":{" + engine + "},\"scenes\":" + TruthScenes.ToJson(results) + "}");
                }
            }
            finally { PhysicsProjectSettings.RestoreSimulationMode(); }
        }
    }
}
