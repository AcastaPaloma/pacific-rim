using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Game.Physics.Body.Editor;
using Game.Units.Mech.Scenarios;
using Game.Units.Mech.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Units.Mech.Editor
{
    /// <summary>
    /// Headless scenario entry point:
    /// unity run &lt;project&gt; --no-tail --log-file &lt;log&gt; -- -executeMethod Game.Units.Mech.Editor.ScenarioCli.Run -scenarios a,b|all -out &lt;dir&gt; [-strips] [-repeat n] [-height m -mass kg]
    /// </summary>
    public static class ScenarioCli
    {
        public static string Arg(string name, string fallback = null)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length; i++) if (a[i] == name) return i + 1 < a.Length && !a[i + 1].StartsWith("-", StringComparison.Ordinal) ? a[i + 1] : "true";
            return fallback;
        }
        public static string Commit()
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo("git", "rev-parse --short HEAD") { WorkingDirectory = Application.dataPath, RedirectStandardOutput = true, UseShellExecute = false });
                string s = p.StandardOutput.ReadToEnd().Trim(); p.WaitForExit(); return s;
            }
            catch (Exception) { return "unknown"; }
        }
        public static string EngineInfo() => "\"solver_type\":\"" + (PhysicsProjectSettings.SolverType() == 1 ? "TemporalGaussSeidel" : "ProjectedGaussSeidel") + "\",\"enhanced_determinism\":" + (PhysicsProjectSettings.EnhancedDeterminism() ? "true" : "false") + ",\"improved_patch_friction\":" + (PhysicsProjectSettings.ImprovedPatchFriction() ? "true" : "false");

        public static void Run()
        {
            string outDir = Arg("-out", "Temp/scenarios"), list = Arg("-scenarios", "stand");
            bool strips = Arg("-strips") != null; int repeat = int.Parse(Arg("-repeat", "1"));
            double dt = Game.Core.CoreSettings.Parse(File.ReadAllText(Path.Combine(Application.dataPath, "Game/Config/CoreSettings.json")), "CoreSettings.json").FixedStepSeconds;
            var config = MechConfig.LoadDefault();
            if (Arg("-height") != null || Arg("-mass") != null) config = config.ScaledTo(double.Parse(Arg("-height", "80"), System.Globalization.CultureInfo.InvariantCulture), double.Parse(Arg("-mass", "1e7"), System.Globalization.CultureInfo.InvariantCulture));
            string commit = Commit(), engine = EngineInfo();
            var names = new List<string>();
            if (list == "all") foreach (var s in ScenarioLibrary.All()) names.Add(s.Name); else names.AddRange(list.Split(','));
            Directory.CreateDirectory(outDir);
            var summary = new StringBuilder();
            summary.AppendLine("commit " + commit + " dt " + dt.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " " + engine + " frame " + config.Frame.id + " H " + config.Frame.total_height_m + " M " + config.Frame.total_mass_kg);
            var mode = UnityEngine.Physics.simulationMode;
            try
            {
                // tuning study: -variants <file> with lines "<name> <json object of walk_tuning fields>"; each scenario runs once per variant (never used for evidence captures)
                var variants = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("", null) };
                if (Arg("-variants") != null) { variants.Clear(); foreach (string line in File.ReadAllLines(Arg("-variants"))) { int sp = line.IndexOf(' '); if (sp > 0) variants.Add(new KeyValuePair<string, string>(line.Substring(0, sp), line.Substring(sp + 1))); } }
                foreach (string name in names)
                    foreach (var variant in variants)
                    for (int k = 0; k < repeat; k++)
                    {
                        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                        string capture = (repeat > 1 ? name + "_run" + (k + 1) : name) + (variant.Key.Length > 0 ? "@" + variant.Key : "");
                        var cfg = variant.Value == null ? config : config.WithWalk(w => JsonUtility.FromJsonOverwrite(variant.Value, w));
                        try { var r = ScenarioRunner.Run(ScenarioLibrary.Find(name), cfg, dt, outDir, commit, engine, strips && k == 0, capture, Arg("-nophys") == null); summary.AppendLine(r.Line()); }
                        catch (Exception e) { summary.AppendLine(capture + " EXCEPTION " + e); }
                        File.WriteAllText(Path.Combine(outDir, "summary.txt"), summary.ToString());
                    }
            }
            finally { UnityEngine.Physics.simulationMode = mode; PhysicsProjectSettings.RestoreSimulationMode(); }
        }
    }
}
