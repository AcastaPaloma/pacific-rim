// T-006 step 2: capture entry point. Rebuilds the style-frame scene (StyleFrameScene.Build) and writes style_capture@v1 sets
// through StyleCapture's session and rig (ThirdPersonRig scripted poses, snapped to the texel grid). Deterministic: fixed poses,
// fixed frame counts, no random or time-based values. Exit code 1 on any error.
// Stills:  unity run . --no-tail --log-file <log> --timeout 1800 -- -executeMethod Game.Tools.StyleFrame.StyleFrameCapture.CaptureAllCli
// Motion:  ... -executeMethod Game.Tools.StyleFrame.StyleFrameCapture.CaptureAllCli -styleFrameSet motion
// Options: -styleFrameSet stills|motion|all (default stills); -styleFrameOut <dir> (default evidence/T-006/captures_local); -styleFrameProfile <id>; -captureCommit <hash>.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Game.Cameras;
using Game.Rendering.Pixel;
using Game.Tools.StyleCapture;
using UnityEditor;
using UnityEngine;

namespace Game.Tools.StyleFrame
{
    public static class StyleFrameCapture
    {
        public const string DefaultOut = "evidence/T-006/captures_local";
        public static readonly string[] Profiles = { "slate_teal", "slate_crimson", "lavender_peach", "warm_amber_haze", "warm_rust_sepia" };
        static readonly string[] Anchors = { "screen", "direction", "hybrid" };
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const int OrbitFrames = 120, PushInFrames = 120, StaticFrames = 30;
        const double OrbitTotalDeg = 10.0, PushInMetresPerFrame = 0.2;

        public static void CaptureAllCli()
        {
            int code = 0;
            try { Run(); }
            catch (Exception e) { Debug.LogError("[StyleFrameCapture] FAILED: " + e); code = 1; }
            EditorApplication.Exit(code);
        }

        [MenuItem("Game/Tools/Capture style frame (stills)")]
        public static void CaptureMenu() => Run();

        static void Run()
        {
            string set = StyleCaptureCli.Arg("-styleFrameSet", "stills");
            string outRoot = StyleCaptureCli.Arg("-styleFrameOut", DefaultOut);
            string profile = StyleCaptureCli.Arg("-styleFrameProfile", StyleFrameScene.DefaultProfile);
            bool stills = set == "stills" || set == "all", motion = set == "motion" || set == "all";
            if (set == "timing")
                throw new NotSupportedException("StyleTiming.Run and PassTimingCli open StyleTestScenes.StylePath (hard-coded) and use the Eye70 viewpoint: not reusable on the style-frame scene. Request recorded in tasks/T-006.impl.md (a scene path and a pose parameter).");
            if (!stills && !motion) throw new ArgumentException("unknown -styleFrameSet '" + set + "' (stills, motion, all)");

            int errors = 0, warnings = 0; var messages = new List<string>();
            Application.LogCallback onLog = (message, stack, type) =>
            {
                if (type == LogType.Log) return;
                if (type == LogType.Warning) warnings++; else errors++;
                messages.Add(type + ": " + message.Replace('\n', ' '));
            };
            Application.logMessageReceived += onLog;
            ShotPlan plan;
            try
            {
                StyleFrameScene.Build(profile);
                plan = StyleFrameShots.LastPlan ?? throw new InvalidOperationException("no shot plan after the build");
                Directory.CreateDirectory(outRoot);
                using (var s = new StyleCaptureSession(StyleFrameScene.ScenePath, outRoot))
                {
                    if (stills) Stills(s, plan);
                    if (motion) Motion(s, plan);
                }
            }
            finally { Application.logMessageReceived -= onLog; }

            string tag = set;
            Debug.Log("[StyleFrameCapture] set " + set + " written to " + outRoot + "; log: " + errors + " errors, " + warnings + " warnings");
            File.WriteAllText(Path.Combine(outRoot, "capture_log_" + tag + ".txt"), "set " + set + "\nerrors " + errors + "\nwarnings " + warnings + "\n" + string.Join("\n", messages) + (messages.Count > 0 ? "\n" : ""));
            if (errors > 0) throw new InvalidOperationException("capture logged " + errors + " errors; first: " + messages.Find(m => m.StartsWith("Error", StringComparison.Ordinal) || m.StartsWith("Exception", StringComparison.Ordinal) || m.StartsWith("Assert", StringComparison.Ordinal)));
        }

        // ------------------------------------------------------------------ helpers
        /// <summary>The pose a still is taken from: on the rotation and position grid, so the rig renders it with zero remainder (as StyleCaptureJobs does).</summary>
        static CameraPose OnGrid(StyleCaptureSession s, ShotDef shot)
        {
            s.Rig.Cut(); s.Rig.SetScriptedPose(shot.Pose, (float)shot.ReferenceDepth); s.Rig.Tick(StyleCaptureSession.Dt);
            var pose = s.Rig.RenderedPose;
            s.Rig.Cut();
            return pose;
        }

        static string FrameJson(ShotDef shot, string profile, string purpose) =>
            "\"style_frame\": {\"framing\": " + StyleCaptureSession.Str(shot.Name) + ", \"profile\": " + StyleCaptureSession.Str(profile) + ", \"purpose\": " + StyleCaptureSession.Str(purpose)
            + ", \"shot_params\": " + StyleCaptureSession.Str(StyleFrameShots.ShotsPath) + ", \"props\": " + StyleCaptureSession.Str(StyleFrameShots.PropsPath) + "}";

        static void Still(StyleCaptureSession s, string set, string sequence, ShotDef shot, string profile, string purpose)
        {
            var w = s.Begin(set, sequence, "M-003", new MotionSpec(), 1, shot.Name);
            w.ExtraJson = FrameJson(shot, profile, purpose);
            w.FrameScripted(0, OnGrid(s, shot), (float)shot.ReferenceDepth);
            w.End();
        }

        // ------------------------------------------------------------------ stills
        const int WarmupStills = 3;
        static void Stills(StyleCaptureSession s, ShotPlan plan)
        {
            var sizes = new[] { new Vector2Int(1920, 1080), new Vector2Int(2560, 1440) };
            // Warm-up (C10): the first capture of a run differed between runs (lighting/haze state not settled), so render
            // WarmupStills throwaway stills of the hero first and delete them; every kept capture then follows a settled state.
            s.Capture.SetOutputSize(sizes[0].x, sizes[0].y); s.Apply(); s.SetProfile(StyleFrameScene.DefaultProfile);
            for (int i = 0; i < WarmupStills; i++) Still(s, "_warmup", "w" + i, plan.Hero, StyleFrameScene.DefaultProfile, "warm-up, deleted");
            string warmDir = System.IO.Path.Combine(s.OutRoot, "_warmup");
            if (System.IO.Directory.Exists(warmDir)) System.IO.Directory.Delete(warmDir, true);
            foreach (var size in sizes)
            {
                string res = size.y + "p";
                s.Capture.SetOutputSize(size.x, size.y); s.Apply();
                foreach (string profile in Profiles)
                {
                    s.SetProfile(profile);
                    Still(s, "hero_" + res, profile, plan.Hero, profile, "hero frame, one of five profiles");
                }
                s.SetProfile(StyleFrameScene.DefaultProfile);
                foreach (var alt in plan.Alternatives)
                    Still(s, "alt_" + res, StyleFrameScene.DefaultProfile + "_" + alt.Name, alt, StyleFrameScene.DefaultProfile, "alternative framing");
            }

            // controls on the hero (default profile, 1080p): pipeline stage toggles through flags and the present component, nothing else changes
            s.Capture.SetOutputSize(1920, 1080); s.SetProfile(StyleFrameScene.DefaultProfile);
            var baseFlags = s.Flags; var hero = plan.Hero; string p0 = StyleFrameScene.DefaultProfile;
            s.Flags = baseFlags.With(PixelFlags.Dither, false); s.Apply(); Still(s, "controls_hero_1080p", "dither_off", hero, p0, "control: dither stage off");
            s.Flags = baseFlags.With(PixelFlags.Quantise, false); s.Apply(); Still(s, "controls_hero_1080p", "unquantised", hero, p0, "control: palette quantise stage off");
            s.Flags = baseFlags.With(PixelFlags.Haze, false); s.Apply(); Still(s, "controls_hero_1080p", "haze_off", hero, p0, "control: haze stage off");
            s.Flags = baseFlags; s.Apply();
            s.Capture.PresentComponent.controlBilinear = true;
            try { Still(s, "controls_hero_1080p", "bilinear", hero, p0, "control: bilinear present instead of integer-scale nearest"); }
            finally { s.Capture.PresentComponent.controlBilinear = false; }
            s.Flags = baseFlags; s.Apply();
        }

        // ------------------------------------------------------------------ motion (M-004): static, slow orbit and slow push-in around the hero framing, per anchoring mode
        static void Motion(StyleCaptureSession s, ShotPlan plan)
        {
            s.Capture.SetOutputSize(1920, 1080); s.Apply(); s.SetProfile(StyleFrameScene.DefaultProfile);
            for (int i = 0; i < WarmupStills; i++) Still(s, "_warmup", "w" + i, plan.Hero, StyleFrameScene.DefaultProfile, "warm-up, deleted");   // C10: settle before the first kept frame
            string warmDir = System.IO.Path.Combine(s.OutRoot, "_warmup");
            if (System.IO.Directory.Exists(warmDir)) System.IO.Directory.Delete(warmDir, true);
            var baseSettings = s.Settings.Clone(); var baseFlags = s.Flags;
            var hero = plan.Hero; var m = plan.Mech;
            double d = hero.DistanceToMech, th0 = hero.CompassAzimuthFromMechDeg;
            double ux = (m.X - hero.X) / d, uz = (m.Z - hero.Z) / d;   // horizontal unit vector, camera toward mech
            try
            {
                foreach (string anchor in Anchors)
                {
                    s.Settings = baseSettings.Clone(); s.Settings.ditherAnchor = anchor; s.Flags = baseFlags; s.Apply();
                    string set = "motion_" + anchor;

                    var sw = s.Begin(set, "hero_static", "M-004", new MotionSpec(), StaticFrames, hero.Name);
                    s.Rig.Cut();
                    for (int i = 0; i < StaticFrames; i++) sw.FrameScripted(i, hero.Pose, (float)hero.ReferenceDepth);
                    sw.End();

                    double degPerFrame = OrbitTotalDeg / (OrbitFrames - 1);
                    var orbit = new MotionSpec { Type = "scripted", Axis = "orbit", SpeedSiKey = "deg_per_s", SpeedSi = (float)(degPerFrame / StyleCaptureSession.Dt), Notes = "camera orbits the mech at constant distance and height, 10 degrees in 120 frames, aimed at the mech; driven by the capture script, no simulation or animation time" };
                    var ow = s.Begin(set, "hero_orbit", "M-004", orbit, OrbitFrames, hero.Name);
                    s.Rig.Cut();
                    for (int i = 0; i < OrbitFrames; i++)
                    {
                        double th = (th0 - 0.5 * OrbitTotalDeg + i * degPerFrame) * Math.PI / 180.0;
                        double cx = m.X + d * Math.Sin(th), cz = m.Z + d * Math.Cos(th);
                        var pose = hero.Pose;
                        pose.Position.X = (float)cx; pose.Position.Z = (float)cz;
                        pose.YawDeg = (float)(Math.Atan2(m.X - cx, m.Z - cz) * 180.0 / Math.PI);
                        ow.FrameScripted(i, pose, (float)hero.ReferenceDepth);
                    }
                    ow.End();

                    var push = new MotionSpec { Type = "dolly", Axis = "camera_forward", SpeedSiKey = "m_per_s", SpeedSi = (float)(PushInMetresPerFrame / StyleCaptureSession.Dt), Notes = "camera moves 0.2 m per frame straight toward the mech at constant height and pitch (24 m in 120 frames); driven by the capture script" };
                    var pw = s.Begin(set, "hero_pushin", "M-004", push, PushInFrames, hero.Name);
                    s.Rig.Cut();
                    for (int i = 0; i < PushInFrames; i++)
                    {
                        var pose = hero.Pose;
                        pose.Position.X = (float)(hero.X + ux * PushInMetresPerFrame * i); pose.Position.Z = (float)(hero.Z + uz * PushInMetresPerFrame * i);
                        pw.FrameScripted(i, pose, (float)hero.ReferenceDepth);
                    }
                    pw.End();
                }
            }
            finally { s.Settings = baseSettings; s.Flags = baseFlags; s.Apply(); }
        }
    }
}
