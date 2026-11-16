// T-147 batch entry. Behind the default-off flag tools.capture.enabled (Assets/Game/Config/FeatureFlags/Tools.Capture.json); a run turns it on
// for itself with -captureEnable (nothing is installed in the committed config).
//   unity run . --no-tail --log-file <log> --timeout 1800 -- -executeMethod Game.Tools.Capture.CaptureCli.Run -capturePaths <dir|a.json,b.json> -captureOut <dir>
//       [-captureScale 4,8] [-captureMaxFrames N] [-captureStride K] [-captureEnable] [-captureFailOnBlocked] [-captureConfig <pixel configuration json>] [-captureInstall]
// Exit 0 ok, 1 error (lint failure, render error, log errors, non-deterministic replay), 3 at least one path was blocked and -captureFailOnBlocked was given.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Game.Core;
using Game.Tools.StyleCapture;
using UnityEditor;
using UnityEngine;

namespace Game.Tools.Capture
{
    public static class CaptureCli
    {
        public const string FlagName = "tools.capture.enabled";
        public const string StableOrderFlag = "tools.capture.stable_draw_order";
        public const string DefaultPaths = "docs/store/paths";

        public static bool FlagOn()
        {
            IFeatureFlags flags = FeatureFlagLoader.LoadFolder("Assets/Game/Config/FeatureFlags", new CollectingErrorSink());
            return flags.IsOn(FlagName);
        }

        public static void Run()
        {
            int code = 0;
            try { code = Execute(); }
            catch (Exception e) { Debug.LogError("[Capture] FAILED: " + e); code = 1; }
            EditorApplication.Exit(code);
        }

        static int Int(string name, int fallback) => int.TryParse(StyleCaptureCli.Arg(name), out int v) ? v : fallback;
        static bool Has(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;

        public static List<string> PathFiles(string arg)
        {
            var files = new List<string>();
            foreach (var part in (arg ?? DefaultPaths).Split(','))
            {
                if (Directory.Exists(part)) files.AddRange(Directory.GetFiles(part, "*.json").OrderBy(x => x, StringComparer.Ordinal));
                else if (File.Exists(part)) files.Add(part);
                else throw new FileNotFoundException("capture path not found: " + part);
            }
            return files;
        }

        static int Execute()
        {
            if (!FlagOn() && !Has("-captureEnable")) { Debug.Log("[Capture] flag " + FlagName + " is off and -captureEnable was not given: nothing to do"); return 0; }
            { IFeatureFlags fl = FeatureFlagLoader.LoadFolder("Assets/Game/Config/FeatureFlags", new CollectingErrorSink()); CaptureScene.StableDrawOrder = fl.IsOn(StableOrderFlag) || Has("-captureStableOrder"); } { if (float.TryParse(StyleCaptureCli.Arg("-captureLiftM"), NumberStyles.Float, CultureInfo.InvariantCulture, out float lift)) CaptureScene.BuildingLiftMeters = lift; }
            if (float.TryParse(StyleCaptureCli.Arg("-captureTerrainSink"), NumberStyles.Float, CultureInfo.InvariantCulture, out float sink)) CaptureScene.TerrainSinkM = sink;   // T-194c, unverified, default 0.02 (T-194d), pass 0 to disable   // opt-in only: UNVERIFIED (priority alone did not fix, queue tier not yet run)   // T-194b
            string variants = StyleCaptureCli.Arg("-captureTuningVariants");
            if (!string.IsNullOrEmpty(variants)) return ExecuteVariants(variants);
            if (Has("-captureInstall")) Game.Rendering.Pixel.Editor.PixelPipelineInstaller.InstallAll();   // T-190: regenerate the baked profiles from PaletteProfileTuning.json first
            return ExecuteOne(StyleCaptureCli.Arg("-captureOut", "evidence/T-147/captures_local"), StyleCaptureCli.Arg("-captureConfig", StyleCaptureCli.M0Configuration), StyleCaptureCli.Arg("-capturePaths"));
        }

        /// <summary>
        /// T-193 tuning sweep in ONE editor process. -captureTuningVariants <dir|a.json,b.json>: each file is a complete PaletteProfileTuning.json
        /// (optional top-level "capturePaths": "a,b" overrides -capturePaths for that variant). For every variant and every configuration of
        /// -captureConfig (comma list) the tuning file is swapped in, the baked profiles are regenerated and the capture is written to
        /// <out>/<variant>/<configName>/. The committed tuning file and profile assets are restored at the end (also on error).
        /// </summary>
        static int ExecuteVariants(string variants)
        {
            string tuningPath = Game.Rendering.Pixel.Editor.PaletteProfileGenerator.TuningPath;
            string original = File.ReadAllText(tuningPath);
            string outRoot = StyleCaptureCli.Arg("-captureOut", "evidence/T-193/sweep_local");
            var configs = (StyleCaptureCli.Arg("-captureConfig", StyleCaptureCli.M0Configuration)).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var files = new List<string>();
            foreach (var part in variants.Split(','))
            {
                if (Directory.Exists(part)) files.AddRange(Directory.GetFiles(part, "*.json").OrderBy(x => x, StringComparer.Ordinal));
                else if (File.Exists(part)) files.Add(part);
                else throw new FileNotFoundException("tuning variant not found: " + part);
            }
            int worst = 0;
            try
            {
                foreach (var f in files)
                {
                    string raw = File.ReadAllText(f);
                    string name = Path.GetFileNameWithoutExtension(f);
                    string pathsOverride = null;
                    var pm = System.Text.RegularExpressions.Regex.Match(raw, "\"capturePaths\"\\s*:\\s*\"([^\"]*)\""); if (pm.Success) pathsOverride = pm.Groups[1].Value;
                    File.WriteAllText(tuningPath, raw);
                    Game.Rendering.Pixel.Editor.PixelPipelineInstaller.InstallAll();
                    foreach (var cfg in configs)
                    {
                        string cname = Path.GetFileNameWithoutExtension(cfg);
                        Debug.Log("[Capture] variant " + name + " config " + cname);
                        try { worst = Math.Max(worst, ExecuteOne(Path.Combine(outRoot, name, cname), cfg, pathsOverride ?? StyleCaptureCli.Arg("-capturePaths"))); }
                        catch (Exception e) { Debug.LogError("[Capture] variant " + name + "/" + cname + " FAILED: " + e.Message); worst = 1; }
                    }
                }
            }
            finally
            {
                File.WriteAllText(tuningPath, original);
                Game.Rendering.Pixel.Editor.PixelPipelineInstaller.InstallAll();
            }
            return worst;
        }

        static int ExecuteOne(string outRoot, string configPath, string pathsArg)
        {
            int maxFrames = Int("-captureMaxFrames", 0), stride = Int("-captureStride", 1);
            var scales = new HashSet<int>((StyleCaptureCli.Arg("-captureScale") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => int.Parse(x, CultureInfo.InvariantCulture)));
            var files = PathFiles(pathsArg);

            // load and lint everything first: one bad path file stops the run before any frame is written
            var paths = new List<KeyValuePair<CapturePath, string>>(); var problems = new List<string>(); var ids = new HashSet<string>();
            foreach (var f in files)
            {
                string raw = File.ReadAllText(f); var p = CapturePath.Parse(raw);
                foreach (var e in p.Lint()) problems.Add(Path.GetFileName(f) + ": " + e);
                if (p.PathId != null && !ids.Add(p.PathId)) problems.Add(Path.GetFileName(f) + ": L02 duplicate path_id " + p.PathId);
                paths.Add(new KeyValuePair<CapturePath, string>(p, raw));
            }
            if (problems.Count > 0) throw new InvalidOperationException("capture_path lint failed:\n" + string.Join("\n", problems));

            int errors = 0, warnings = 0; var messages = new List<string>();
            Application.LogCallback onLog = (m, st, type) => { if (type == LogType.Log) return; if (type == LogType.Warning) warnings++; else errors++; messages.Add(type + ": " + m.Replace('\n', ' ')); };
            Application.logMessageReceived += onLog;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var results = new List<SequenceResult>(); var blocked = new List<string>();
            try
            {
                Directory.CreateDirectory(outRoot);
                // capability gate: every `requires` tag is unmet today, and a tile that is not in StreamingAssets is blocked on streaming (M2)
                var runnable = new List<KeyValuePair<CapturePath, string>>();
                foreach (var kv in paths)
                {
                    string tag = kv.Key.FirstBlocker(null) ?? (CaptureScene.TileAvailable(kv.Key.Tile) ? null : "m2_streaming");
                    if (tag != null) { blocked.Add(kv.Key.PathId + ": blocked: " + tag); Debug.Log("[Capture] " + kv.Key.PathId + " blocked: " + tag); }
                    else runnable.Add(kv);
                }
                foreach (var group in runnable.GroupBy(kv => kv.Key.Tile + "|" + string.Join(",", kv.Key.HideBids.OrderBy(x => x, StringComparer.Ordinal))))
                {
                    var first = group.First().Key;
                    var tile = CaptureScene.Build(first.Tile, first.ProfileId, first.HideBids);
                    using (var s = new StyleCaptureSession(tile.ScenePath, outRoot, configPath))
                        foreach (var kv in group)
                        {
                            var p = kv.Key;
                            results.AddRange(CaptureDriver.Render(s, tile, p, kv.Value, outRoot, maxFrames, stride, scales));
                        }
                }
            }
            finally { Application.logMessageReceived -= onLog; }

            bool deterministic = results.All(r => r.Deterministic);
            var j = new StringBuilder();
            j.Append("{\n \"schema\": \"capture_run@v1\",\n \"out\": \"").Append(outRoot.Replace("\\", "/")).Append("\",\n \"seconds\": ").Append(watch.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture));
            j.Append(",\n \"errors\": ").Append(errors).Append(", \"warnings\": ").Append(warnings).Append(", \"deterministic\": ").Append(deterministic ? "true" : "false").Append(",\n \"blocked\": [")
             .Append(string.Join(", ", blocked.Select(StyleCaptureSession.Str))).Append("],\n \"sequences\": [");
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                j.Append(i > 0 ? ",\n  " : "\n  ").Append("{\"path_id\": ").Append(StyleCaptureSession.Str(r.PathId)).Append(", \"scale\": ").Append(r.Scale).Append(", \"frames\": ").Append(r.FrameCount).Append(", \"internal_wh\": [").Append(r.InternalW).Append(", ").Append(r.InternalH)
                 .Append("], \"output_wh\": [").Append(r.OutputW).Append(", ").Append(r.OutputH).Append("], \"replay_frames\": ").Append(r.DeterminismFrames).Append(", \"replay_identical\": ").Append(r.Deterministic ? "true" : "false").Append("}");
            }
            j.Append("\n ]\n}\n");
            File.WriteAllText(Path.Combine(outRoot, "capture_run.json"), j.ToString());
            string summary = "[Capture] " + results.Count + " sequences, " + blocked.Count + " blocked, replay " + (deterministic ? "identical" : "DIFFERENT") + ", " + watch.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture) + " s; log: " + errors + " errors, " + warnings + " warnings";
            Debug.Log(summary);
            File.WriteAllText(Path.Combine(outRoot, "capture_log_summary.txt"), summary + "\n" + string.Join("\n", messages.Take(8)) + (messages.Count > 0 ? "\n" : ""));
            File.WriteAllText(Path.Combine(outRoot, "capture_log_warnings.txt"), string.Join("\n", messages) + (messages.Count > 0 ? "\n" : ""));
            if (errors > 0) throw new InvalidOperationException("capture logged " + errors + " errors; first: " + messages.FirstOrDefault(m => !m.StartsWith("Warning", StringComparison.Ordinal)));
            if (!deterministic) throw new InvalidOperationException("replay of the first frames differed from the first pass");
            if (blocked.Count > 0 && Has("-captureFailOnBlocked")) return 3;
            return 0;
        }
    }
}
