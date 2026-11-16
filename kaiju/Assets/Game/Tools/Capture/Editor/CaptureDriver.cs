// T-147 frame loop: samples a capture_path@v1 at n / fps, places the camera through the rig's scripted pose (snapped to the texel grid
// exactly like the shipping camera) and writes style_capture@v1 frames through StyleCaptureSession.SequenceWriter at each requested
// integer scale. Lossless: PNG straight from the engine targets; no resampling anywhere. Time is the frame index, never a wall clock.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Game.Cameras;
using Game.Tools.StyleCapture;
using UnityEngine;

namespace Game.Tools.Capture
{
    public sealed class SequenceResult
    {
        public string PathId, Directory;
        public int Scale, FrameCount, InternalW, InternalH, OutputW, OutputH, DeterminismFrames;
        public bool Deterministic;
        public List<int> Frames = new List<int>();
    }

    public static class CaptureDriver
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public const int Warmup = 3, DeterminismFrames = 10;

        /// <summary>T-192: focus depth (the near layer reference) for a layerSnap run. The path's reference_depth_m (600 m, a scene scale) is too close to the main layer (664.874 m) for separate slabs, so the binder fell back to the single-snap path
        /// (warning "reference depths ... too close"). With layerSnap on the near layer takes -captureFocusM, default 124.2 m (the shipping rig focus, ParallaxShiftMath.NearDepthM); layerSnap off keeps the path value.</summary>
        public const float DefaultFocusM = 124.2f;   // = ParallaxShiftMath.NearDepthM (that assembly is not referenced here)
        public static float FocusDepth(StyleCaptureSession s, CapturePath p)
        {
            if (!s.Flags.IsOn(Game.Rendering.Pixel.PixelFlags.LayerSnap)) return (float)p.ReferenceDepthM;
            string a = StyleCaptureCli.Arg("-captureFocusM");
            return a != null && float.TryParse(a, NumberStyles.Float, Inv, out float v) && v > 0f ? v : DefaultFocusM;
        }

        /// <summary>Frames to render for a path: still_frames or every frame of the clip, thinned by stride, at most maxFrames (0 = no limit).</summary>
        public static List<int> FramePlan(CapturePath p, int maxFrames, int stride)
        {
            var all = new List<int>();
            if (p.IsStill) all.AddRange(p.StillFrames);
            else for (int i = 0; i < p.FrameCount; i++) all.Add(i);
            var plan = new List<int>();
            int step = Math.Max(1, stride);
            for (int i = 0; i < all.Count; i += (p.IsStill ? 1 : step)) { plan.Add(all[i]); if (maxFrames > 0 && plan.Count >= maxFrames) break; }
            return plan;
        }

        static CameraPose PoseAt(CapturePath p, CaptureTile tile, int frame, double dpt, out CaptureSample smp)
        {
            smp = CapturePathSampler.Sample(p, frame);
            double lx = smp.E - tile.OriginX, lz = smp.N - tile.OriginZ;
            double y = p.AltIsNavd88 ? smp.Alt : tile.GroundRender(lx, lz) + smp.Alt;
            return new CameraPose { Position = new CameraVec3((float)lx, (float)y, (float)lz), YawDeg = (float)(smp.YawDeg + smp.ShakeXPx * dpt), PitchDeg = (float)(smp.PitchDeg + smp.ShakeYPx * dpt) };
        }

        static string Hash(byte[] b) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(b)).Replace("-", "").ToLowerInvariant(); }

        public static List<SequenceResult> Render(StyleCaptureSession s, CaptureTile tile, CapturePath p, string rawJson, string outRoot, int maxFrames, int stride, ICollection<int> scales)
        {
            var results = new List<SequenceResult>();
            var baseSettings = s.Settings.Clone();
            string pathDir = Path.Combine(outRoot, p.PathId);
            Directory.CreateDirectory(pathDir);
            File.WriteAllText(Path.Combine(pathDir, "capture_path.json"), rawJson);
            s.SetProfile(p.ProfileId);
            if (p.IsMotionSet) return RenderMotionSet(s, tile, p, outRoot);
            var plan = FramePlan(p, maxFrames, stride);
            try
            {
                foreach (var output in p.Outputs)
                {
                    if (scales != null && scales.Count > 0 && !scales.Contains(output.Scale)) continue;
                    // the integer scale is the pipeline's own rule k = clamp(round(H / targetLines), minScale, maxScale); pin it to the requested scale
                    s.Settings = baseSettings.Clone();
                    s.Settings.targetLines = output.InternalH; s.Settings.minScale = output.Scale; s.Settings.maxScale = output.Scale;
                    s.Capture.SetOutputSize(output.OutputW, output.OutputH); s.Apply();
                    results.Add(RenderOne(s, tile, p, output, plan, outRoot, stride));
                }
            }
            finally { s.Settings = baseSettings; s.Apply(); }
            return results;
        }

        /// <summary>
        /// M-004 motion sets (T-191): from the first keyframe pose, each requested class (S0 static, T1-T3 translation along the camera's right axis,
        /// Y1-Y2 yaw) is written as its own sequence "&lt;viewpoint&gt;_&lt;class&gt;" with purpose M-004 and the class's nominal speed, exactly as the
        /// StyleCapture M-004 job does (same formulas, grid at the path's reference depth, snap depth = reference depth). Frame limits do not apply: the metric needs
        /// 60 (S0) and 180 frames. The full file set is written: the M-004 input contract requires color_output as well.
        /// </summary>
        static List<SequenceResult> RenderMotionSet(StyleCaptureSession s, CaptureTile tile, CapturePath p, string outRoot)
        {
            var results = new List<SequenceResult>();
            var output = p.Outputs[0];
            var baseSettings = s.Settings.Clone();
            try
            {
                s.Settings = baseSettings.Clone();
                s.Settings.targetLines = output.InternalH; s.Settings.minScale = output.Scale; s.Settings.maxScale = output.Scale;
                s.Capture.SetOutputSize(output.OutputW, output.OutputH); s.Apply();
                s.Grid((float)p.ReferenceDepthM, out float metresPerTexel, out float degreesPerTexel); double dpt = degreesPerTexel;
                float refDepth = (float)p.ReferenceDepthM;
                CameraPose basePose = PoseAt(p, tile, 0, dpt, out _);
                float yawRad = basePose.YawDeg * Mathf.Deg2Rad;
                foreach (string id in p.MotionSet)
                {
                    CapturePath.TryMotionClass(id, out var m);
                    var spec = new MotionSpec { Type = m.Type, Axis = m.Axis, SpeedPxPerFrame = (float)m.SpeedPxPerFrame, Notes = "camera driven by capture_path@v1 " + p.PathId + " motion class " + id + "; no simulation, animation or VFX time" };
                    if (m.Type == "translate") { spec.SpeedSiKey = "m_per_s"; spec.SpeedSi = (float)m.SpeedPxPerFrame * metresPerTexel / StyleCaptureSession.Dt; }
                    if (m.Type == "yaw") { spec.SpeedSiKey = "deg_per_s"; spec.SpeedSi = (float)m.SpeedPxPerFrame * degreesPerTexel / StyleCaptureSession.Dt; }
                    // warm-up (C10), then start every class from a cut at the same pose
                    for (int i = 0; i < Warmup; i++) { s.Rig.Cut(); s.Rig.SetScriptedPose(basePose, refDepth); s.Rig.Tick(StyleCaptureSession.Dt); s.Capture.Render(true); s.Capture.Read(true); }
                    var w = s.Begin(p.PathId, p.ViewpointId + "_" + id, "M-004", spec, m.Frames, p.ViewpointId, true);   // the metric's input contract requires color_output too
                    s.Rig.Cut();
                    for (int i = 0; i < m.Frames; i++)
                    {
                        var pose = basePose;
                        if (m.Type == "translate") { float d = i * (float)m.SpeedPxPerFrame * metresPerTexel; pose.Position.X += Mathf.Cos(yawRad) * d; pose.Position.Z -= Mathf.Sin(yawRad) * d; }
                        else if (m.Type == "yaw") pose.YawDeg += i * (float)m.SpeedPxPerFrame * degreesPerTexel;
                        var f = w.FrameScripted(i, pose, refDepth);
                        if (i == 0) CheckInternal(p, output, f);
                    }
                    w.End();
                    results.Add(new SequenceResult { PathId = p.PathId + "/" + p.ViewpointId + "_" + id, Directory = w.Directory, Scale = output.Scale, FrameCount = m.Frames, InternalW = output.InternalW, InternalH = output.InternalH,
                        OutputW = output.OutputW, OutputH = output.OutputH, Deterministic = true, DeterminismFrames = 0 });
                    Debug.Log("[Capture] " + p.PathId + " motion " + id + ": " + m.Frames + " frames at " + m.SpeedPxPerFrame + " px/frame (" + m.Type + ")");
                }
            }
            finally { s.Settings = baseSettings; s.Apply(); }
            return results;
        }

        static void CheckInternal(CapturePath p, CaptureOutput o, CapturedFrame f)
        {
            if (f.Width != o.InternalW || f.Height != o.InternalH) throw new InvalidOperationException(p.PathId + ": renderer produced " + f.Width + "x" + f.Height + ", path asks " + o.InternalW + "x" + o.InternalH);
        }

        static SequenceResult RenderOne(StyleCaptureSession s, CaptureTile tile, CapturePath p, CaptureOutput output, List<int> plan, string outRoot, int stride)
        {
            string seq = "x" + output.Scale.ToString(Inv);
            float refDepth = FocusDepth(s, p);
            s.Grid(refDepth, out _, out float dptF); double dpt = dptF;
            string diagTag = (Environment.GetEnvironmentVariable("KAIJU_DIAG_TAG") ?? "run") + "_" + p.PathId; CaptureDiag.Geometry(diagTag);
            // warm-up (C10): the first renders of a run differ until lighting and haze state settle; throwaway frames at the first pose, nothing written
            for (int i = 0; i < Warmup; i++) { s.Rig.Cut(); s.Rig.SetScriptedPose(PoseAt(p, tile, plan[0], dpt, out _), refDepth); s.Rig.Tick(StyleCaptureSession.Dt); s.Capture.Render(true); s.Capture.Read(true); }

            var motion = new MotionSpec { Type = "scripted", Axis = "capture_path", SpeedPxPerFrame = 0f, Notes = "camera driven by capture_path@v1 " + p.PathId + " sampled at n / fps; no simulation, animation or VFX time" };
            var w = s.Begin(p.PathId, seq, "M-003", motion, plan.Count, p.ViewpointId, true);
            var hashes = new List<string>();
            int prev = int.MinValue;
            var res = new SequenceResult { PathId = p.PathId, Scale = output.Scale, Directory = w.Directory };
            foreach (int frame in plan)
            {
                if (frame != prev + 1) s.Rig.Cut();   // a still or a thinned frame is its own cut: no smoothing state carried over
                var pose = PoseAt(p, tile, frame, dpt, out var smp);
                w.ExtraJson = SidecarBlock(p, tile, output, frame, smp);
                var f = w.FrameScripted(res.Frames.Count, pose, refDepth);   // T-190: sequence index runs 0..n-1 even when the plan is thinned (style_capture@v1 forbids gaps); the path frame is in the capture_path block
                if (res.Frames.Count == 0) { res.InternalW = f.Width; res.InternalH = f.Height; res.OutputW = f.OutputWidth; res.OutputH = f.OutputHeight; CheckSize(p, output, f); }
                CaptureDiag.Frame(diagTag, frame, s.Capture, f, true);
                if (res.Frames.Count < DeterminismFrames) hashes.Add(Hash(f.OutputRgb));
                res.Frames.Add(frame);
                prev = frame;
            }
            w.End();
            if (CaptureDiag.Wants(diagTag) && int.TryParse(Environment.GetEnvironmentVariable("KAIJU_DIAG_PERMUTE"), out int permN) && permN > 0)
            {
                int pprev = int.MinValue;
                CaptureDiag.Permute(diagTag, permN, fr =>
                {
                    if (fr != pprev + 1) s.Rig.Cut();
                    s.Rig.SetScriptedPose(PoseAt(p, tile, fr, dpt, out _), refDepth); s.Rig.Tick(StyleCaptureSession.Dt);
                    s.Capture.Render(true); CaptureDiag.lastHash = CaptureDiag.CamHash(s.Capture); pprev = fr;
                }, plan);
            }
            // self-check: replay the first frames from a cut and compare the presented pixels bit for bit
            int n = Math.Min(DeterminismFrames, plan.Count); bool same = true; prev = int.MinValue;
            for (int i = 0; i < n; i++)
            {
                int frame = plan[i];
                if (frame != prev + 1) s.Rig.Cut();
                s.Rig.SetScriptedPose(PoseAt(p, tile, frame, dpt, out _), refDepth); s.Rig.Tick(StyleCaptureSession.Dt);
                s.Capture.Render(true);
                if (Hash(s.Capture.Read(true).OutputRgb) != hashes[i]) { same = false; Debug.LogError("[Capture] " + p.PathId + " " + seq + ": replay of frame " + frame + " differs from the first pass (non-deterministic render)"); }
                prev = frame;
            }
            res.Deterministic = same; res.DeterminismFrames = n; res.FrameCount = plan.Count;
            WriteHashes(res.Directory);
            Debug.Log("[Capture] " + p.PathId + " " + seq + ": " + plan.Count + " frames, " + res.InternalW + "x" + res.InternalH + " -> " + res.OutputW + "x" + res.OutputH + ", replay " + (same ? "identical" : "DIFFERENT"));
            return res;
        }

        static void CheckSize(CapturePath p, CaptureOutput o, CapturedFrame f)
        {
            if (f.Width != o.InternalW || f.Height != o.InternalH || f.OutputWidth != o.OutputW || f.OutputHeight != o.OutputH)
                throw new InvalidOperationException(p.PathId + ": renderer produced " + f.Width + "x" + f.Height + " -> " + f.OutputWidth + "x" + f.OutputHeight + ", path asks " + o.InternalW + "x" + o.InternalH + " -> " + o.OutputW + "x" + o.OutputH);
        }

        static void WriteHashes(string dir)
        {
            var files = new List<string>(System.IO.Directory.GetFiles(dir, "*.png")); files.Sort(StringComparer.Ordinal);
            var sb = new StringBuilder();
            using (var sha = SHA256.Create())
                foreach (var f in files) sb.Append(BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(f))).Replace("-", "").ToLowerInvariant()).Append("  ").Append(Path.GetFileName(f)).Append('\n');
            File.WriteAllText(Path.Combine(dir, "hashes.sha256"), sb.ToString());
        }

        static string SidecarBlock(CapturePath p, CaptureTile tile, CaptureOutput o, int frame, CaptureSample smp)
        {
            string N(double v) => v.ToString("0.######", Inv);
            string S(string v) => StyleCaptureSession.Str(v);
            return "\"capture_path\": {\"schema\": " + S(CapturePath.Schema) + ", \"path_id\": " + S(p.PathId) + ", \"pillar\": " + S(p.Pillar) + ", \"frame_index\": " + frame + ", \"t_s\": " + N(smp.T) + ", \"fps\": " + p.Fps
                + ", \"seed\": " + p.Seed.ToString(Inv) + ", \"tile\": " + S(tile.Name) + ", \"viewpoint\": " + S(p.ViewpointId) + ", \"district_id\": " + S(p.DistrictId) + ", \"time_of_day\": " + S(p.TimeOfDay) + ", \"integer_scale\": " + o.Scale
                + ", \"non_world\": " + (p.NonWorld ? "true" : "false") + ", \"requested_pose\": {\"baytm\": [" + N(smp.E) + ", " + N(smp.N) + "], \"alt\": " + N(smp.Alt) + ", \"alt_kind\": " + S(p.AltIsNavd88 ? "navd88" : "above_ground")
                + ", \"yaw_deg\": " + N(smp.YawDeg) + ", \"pitch_deg_up\": " + N(smp.PitchDeg) + "}, \"shake_internal_px\": [" + N(smp.ShakeXPx) + ", " + N(smp.ShakeYPx) + "], \"hfov_note\": \"hfov_deg is informational: the rig sets the field of view from the pixel grid\"}";
        }
    }
}
