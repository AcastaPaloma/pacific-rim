// T-140: macOS player build entry (M3 Track 1). Driven by Tools/Build/build.sh:
//   unity run <project> -- -executeMethod Game.Tools.Build.PlayerBuild.Run -buildOut <dir> [-buildRepeat 2] [-buildDev]
// Builds the scenes enabled in EditorBuildSettings (the M-120 / build_exclusions@v1 list, checked here too) to
// <dir>/<name>.app and writes <dir>.unity_build.json with only deterministic fields (no timestamps) so the manifest
// diff (M-121) can compare two builds. -buildRepeat N builds N times in this one editor session: <dir>, <dir>.repro2 ...
// Content hashes, attribution, tile set, flag snapshot and the manifest are written by build.sh (Python), not here.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Game.Tools.Build
{
    public static class PlayerBuild
    {
        public const string ProductFile = "KaijuGame";

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        static bool Has(string name) { return Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0; }

        /// <summary>Enabled build scenes, or throws when the list is empty, missing a file or holds a template scene.</summary>
        public static string[] SceneList()
        {
            var scenes = new List<string>();
            foreach (var s in EditorBuildSettings.scenes)
            {
                if (!s.enabled) continue;
                if (!File.Exists(s.path)) throw new InvalidOperationException("build scene missing: " + s.path);
                if (s.path.Contains("SampleScene")) throw new InvalidOperationException("template scene in build list (build_exclusions@v1): " + s.path);
                scenes.Add(s.path);
            }
            if (scenes.Count == 0) throw new InvalidOperationException("EditorBuildSettings lists no enabled scene");
            return scenes.ToArray();
        }

        public static void Run()
        {
            int code = 1;
            try { code = RunInner(); }
            catch (Exception ex) { Debug.LogError("[Build] " + ex); }
            if (code != 0) EditorApplication.Exit(code);
        }

        static int RunInner()
        {
            string outDir = Arg("-buildOut") ?? "Builds/Player";
            int repeat = 1; int.TryParse(Arg("-buildRepeat"), out repeat); if (repeat < 1) repeat = 1;
            bool dev = Has("-buildDev");
            var scenes = SceneList();
            AssetDatabase.Refresh();
            for (int n = 1; n <= repeat; n++)
            {
                string dir = n == 1 ? outDir : outDir + ".repro" + n;
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
                string app = dir + "/" + ProductFile + ".app";
                var opts = new BuildPlayerOptions
                {
                    scenes = scenes, locationPathName = app, target = BuildTarget.StandaloneOSX,
                    options = dev ? BuildOptions.Development : BuildOptions.None,
                };
                var report = BuildPipeline.BuildPlayer(opts);
                var sum = report.summary;
                Debug.Log("[Build] #" + n + " " + sum.result + " size " + sum.totalSize + " bytes, " + sum.totalTime + " errors " + sum.totalErrors);
                WriteUnityJson(dir + ".unity_build.json", scenes, sum.result.ToString(), dev, app);
                if (sum.result != BuildResult.Succeeded) return 2;
            }
            return 0;
        }

        static string J(string s) { return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""; }

        static void WriteUnityJson(string path, string[] scenes, string result, bool dev, string app)
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"spec\": \"unity_build@v1\",\n");
            sb.Append("  \"result\": ").Append(J(result)).Append(",\n");
            sb.Append("  \"unity_version\": ").Append(J(Application.unityVersion)).Append(",\n");
            sb.Append("  \"target\": \"StandaloneOSX\",\n  \"development\": ").Append(dev ? "true" : "false").Append(",\n");
            sb.Append("  \"app\": ").Append(J(Path.GetFileName(app))).Append(",\n");
            sb.Append("  \"scenes\": [");
            for (int i = 0; i < scenes.Length; i++)
                sb.Append(i > 0 ? ", " : "").Append("{\"path\": ").Append(J(scenes[i])).Append(", \"guid\": ").Append(J(AssetDatabase.AssetPathToGUID(scenes[i]))).Append("}");
            sb.Append("]\n}\n");
            File.WriteAllText(path, sb.ToString());
        }
    }
}
