using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Game.Cameras;
using Game.Core;
using Game.Rendering.Pixel;
using UnityEngine;

namespace Game.Tools.StyleCapture
{
    /// <summary>
    /// The capture jobs that produce the T-004 evidence (reconciled spec, evidence list). Each job writes
    /// style_capture@v1 capture sets under the output root. Jobs are selected with -captureJobs (comma separated):
    /// stills, hazecal, m004, m004ab, controls, stages, cut, orbit, dolly.
    /// </summary>
    public static class StyleCaptureJobs
    {
        static readonly string[] s_AllProfiles = { "warm_rust_sepia", "warm_amber_haze", "slate_teal", "lavender_peach", "slate_crimson" };
        /// <summary>The five profiles, or the subset named by -captureProfiles (comma separated).</summary>
        public static string[] AllProfiles { get { string a = StyleCaptureCli.Arg("-captureProfiles"); return string.IsNullOrEmpty(a) ? s_AllProfiles : a.Split(','); } }
        public const float ReferenceDepth = 600f;   // R-002 §5: speeds are defined at the 600 m reference depth
        public const string MainProfile = "slate_teal";

        public sealed class Viewpoint
        {
            public string Name; public Vector3 Position; public float YawDeg, PitchDeg;
            public CameraPose Pose(float dx = 0, float dz = 0, float dyaw = 0, float dpitch = 0) =>
                new CameraPose { Position = new CameraVec3(Position.x + dx, Position.y, Position.z + dz), YawDeg = YawDeg + dyaw, PitchDeg = PitchDeg + dpitch };
        }

        public static readonly Viewpoint Street = new Viewpoint { Name = "street", Position = new Vector3(-10, 1.7f, -150), YawDeg = 0, PitchDeg = 6 };
        public static readonly Viewpoint Eye70 = new Viewpoint { Name = "eye70", Position = new Vector3(0, 70, -150), YawDeg = 0, PitchDeg = 3 };
        public static readonly Viewpoint Elevated = new Viewpoint { Name = "elevated300", Position = new Vector3(0, 300, -600), YawDeg = 0, PitchDeg = 1 };
        /// <summary>The eye-height viewpoint turned toward the 160 m tower (about 640 m away): its edge is tracked for the smoothness check.</summary>
        public static readonly Viewpoint Eye70Tower = new Viewpoint { Name = "eye70_tower", Position = new Vector3(0, 70, -150), YawDeg = 22, PitchDeg = 3 };
        public static readonly Viewpoint[] StillViewpoints = { Street, Eye70, Elevated };
        public static readonly Viewpoint[] MotionViewpoints = { Eye70, Street };

        static CameraPose Station(Viewpoint v, int i) => v.Pose((i - 5.5f) * 22f, (i % 3) * 15f, (i - 5.5f) * 1.6f);

        /// <summary>
        /// The pose a still is taken from: the wanted pose with its rotation on the rotation grid. Rendered right after
        /// a cut, the rig renders exactly this pose with zero remainder, so every still of a sequence declares the same
        /// (zero) output offset.
        /// </summary>
        static CameraPose OnGrid(StyleCaptureSession s, CameraPose pose)
        {
            s.Rig.Cut(); s.Rig.SetScriptedPose(pose, ReferenceDepth); s.Rig.Tick(StyleCaptureSession.Dt);
            pose = s.Rig.RenderedPose;
            s.Rig.Cut();
            return pose;
        }

        static readonly MotionSpec Static = new MotionSpec();
        static MotionSpec Scripted(string notes) => new MotionSpec { Type = "scripted", Axis = "stations", Notes = notes };

        // ---- M-003 stills ------------------------------------------------------------------------------------------

        public static void Stills(StyleCaptureSession s, string set = "m003_stills")
        {
            foreach (string profile in AllProfiles)
            {
                s.SetProfile(profile); s.Capture.SetOutputSize(1920, 1080); s.Apply();
                foreach (var v in StillViewpoints)
                {
                    var w = s.Begin(set, profile + "_" + v.Name, "M-003", Scripted("twelve fixed stations; each pose is on the snap grid, so the remainder is zero"), 12, v.Name);
                    for (int i = 0; i < 12; i++) w.FrameScripted(i, OnGrid(s, Station(v, i)), ReferenceDepth);
                    w.End();
                }
                Detail(s, set.Replace("stills", "details"), profile + "_detail_sky", "detail_sky", Eye70.Pose(0, 0, 25, 14));
                Detail(s, set.Replace("stills", "details"), profile + "_detail_gradient", "detail_gradient", new CameraPose { Position = new CameraVec3(-90, 50, 130), YawDeg = -34, PitchDeg = 2 });
                Detail(s, set.Replace("stills", "details"), profile + "_detail_scale", "detail_scale", ScalePose());
                foreach (var size in new[] { new Vector2Int(2560, 1440), new Vector2Int(3840, 2160) })
                {
                    if (size.y == 2160 && profile != MainProfile) continue;   // 4K is reported for one profile only
                    s.Capture.SetOutputSize(size.x, size.y); s.Apply();
                    Detail(s, set + "_" + size.y + "p", profile + "_eye70", "eye70", Eye70.Pose());
                    s.Capture.SetOutputSize(1920, 1080); s.Apply();
                }
            }
        }

        /// <summary>The scale view: the far 1.8 m marker is 600 m away and the far 80 m block about 1 km behind it.</summary>
        public static CameraPose ScalePose()
        {
            Vector3 marker = StyleTestScenes.MarkerFar;
            var position = new Vector3(marker.x - 40f, 12f, marker.z - 598f);
            Vector3 d = marker - position;
            return new CameraPose { Position = new CameraVec3(position.x, position.y, position.z), YawDeg = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg + 6f, PitchDeg = 3f };
        }

        static void Detail(StyleCaptureSession s, string set, string sequence, string viewpoint, CameraPose pose)
        {
            var w = s.Begin(set, sequence, "M-003", Static, 1, viewpoint);
            w.FrameScripted(0, OnGrid(s, pose), ReferenceDepth);
            w.End();
        }

        // ---- haze calibration --------------------------------------------------------------------------------------

        public static void HazeCal(StyleCaptureSession s, string set = "haze_cal")
        {
            var pose = new CameraPose { Position = new CameraVec3(0, 0, 0), YawDeg = 0, PitchDeg = 0 };
            foreach (string profile in AllProfiles)
            {
                s.SetProfile(profile); s.Apply();
                pose = OnGrid(s, pose);
                var w = s.Begin(set, profile, "haze_cal", Static, 1, "haze_cal");
                w.FrameScripted(0, pose, ReferenceDepth, HazeTargets(s));
                w.End();
            }
        }

        static HazeTarget[] HazeTargets(StyleCaptureSession s)
        {
            var layout = s.Capture.Binder.ComputeLayout();
            var cam = s.Capture.World;
            float tanV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), tanH = tanV * layout.Width / layout.Height;
            var targets = new List<HazeTarget>();
            float[] distances = StyleTestScenes.HazeCalDistances;
            for (int i = 0; i < distances.Length; i++)
            {
                float d = distances[i], size = StyleTestScenes.HazeCalPanelSize * d;
                float azimuth = (i - (distances.Length - 1) * 0.5f) * StyleTestScenes.HazeCalAzimuthStep;
                var rotation = Quaternion.Euler(0, azimuth * Mathf.Rad2Deg, 0);
                Vector3 centre = rotation * new Vector3(0, 0, d), right = rotation * Vector3.right;
                int[] dark = Box(cam, centre - right * size * 0.5f, right, size, tanH, tanV, layout), light = Box(cam, centre + right * size * 0.5f, right, size, tanH, tanV, layout);
                // Both boxes of a pair get the same size (trimmed on their outer sides), so the pair mean weighs them equally.
                int w = Mathf.Min(dark[2] - dark[0], light[2] - light[0]), h = Mathf.Min(dark[3] - dark[1], light[3] - light[1]);
                dark[0] = dark[2] - w; light[2] = light[0] + w;
                dark[1] = Mathf.Max(dark[1], light[1]); light[1] = dark[1]; dark[3] = dark[1] + h; light[3] = light[1] + h;
                targets.Add(new HazeTarget { Distance = d, DarkPx = dark, LightPx = light });
            }
            return targets.ToArray();
        }

        /// <summary>Pixel box [x0, y0, x1, y1] (top-left origin, end-exclusive) inside a square panel, with a two-texel margin.</summary>
        static int[] Box(Camera cam, Vector3 centre, Vector3 right, float size, float tanH, float tanV, PixelLayout layout)
        {
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int cx = -1; cx <= 1; cx += 2)
                for (int cy = -1; cy <= 1; cy += 2)
                {
                    Vector3 v = cam.transform.InverseTransformPoint(centre + right * (cx * size * 0.5f) + Vector3.up * (cy * size * 0.5f));
                    float x = (v.x / v.z / tanH * 0.5f + 0.5f) * layout.Width, y = (0.5f - v.y / v.z / tanV * 0.5f) * layout.Height;
                    minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                }
            // The quad's far edge is foreshortened: take the inner box of the projected corners.
            return new[] { Mathf.CeilToInt(minX) + 2, Mathf.CeilToInt(minY) + 2, Mathf.FloorToInt(maxX) - 2, Mathf.FloorToInt(maxY) - 2 };
        }

        // ---- M-004 motion sequences -------------------------------------------------------------------------------

        public sealed class MotionSequence { public string Id, Type, Axis; public float Speed; public int Frames; }
        public static readonly MotionSequence[] M004Sequences =
        {
            new MotionSequence { Id = "S0", Type = "static", Axis = "none", Speed = 0f, Frames = 60 },
            new MotionSequence { Id = "T1", Type = "translate", Axis = "camera_right", Speed = 0.1f, Frames = 180 },
            new MotionSequence { Id = "T2", Type = "translate", Axis = "camera_right", Speed = 0.25f, Frames = 180 },
            new MotionSequence { Id = "T3", Type = "translate", Axis = "camera_right", Speed = 1.0f, Frames = 180 },
            new MotionSequence { Id = "Y1", Type = "yaw", Axis = "world_up", Speed = 0.25f, Frames = 180 },
            new MotionSequence { Id = "Y2", Type = "yaw", Axis = "world_up", Speed = 1.0f, Frames = 180 },
        };

        /// <summary>Writes the six M-004 sequences from one viewpoint with the session's current settings.</summary>
        public static void MotionSet(StyleCaptureSession s, string set, Viewpoint v, bool writeOutput, string only = null, float snapDepth = ReferenceDepth)
        {
            s.Grid(ReferenceDepth, out float metresPerTexel, out float degreesPerTexel);
            foreach (var m in M004Sequences)
            {
                if (only != null && Array.IndexOf(only.Split('+'), m.Id) < 0) continue;
                var spec = new MotionSpec { Type = m.Type, Axis = m.Axis, SpeedPxPerFrame = m.Speed };
                if (m.Type == "translate") { spec.SpeedSiKey = "m_per_s"; spec.SpeedSi = m.Speed * metresPerTexel / StyleCaptureSession.Dt; }
                if (m.Type == "yaw") { spec.SpeedSiKey = "deg_per_s"; spec.SpeedSi = m.Speed * degreesPerTexel / StyleCaptureSession.Dt; }
                var w = s.Begin(set, v.Name + "_" + m.Id, "M-004", spec, m.Frames, v.Name, writeOutput);
                float yawRad = v.YawDeg * Mathf.Deg2Rad;
                s.Rig.Cut();   // every sequence starts from the same state
                for (int i = 0; i < m.Frames; i++)
                {
                    CameraPose pose = v.Pose();
                    if (m.Type == "translate")
                    {
                        float d = i * m.Speed * metresPerTexel;   // along the camera's right axis
                        pose.Position.X += Mathf.Cos(yawRad) * d; pose.Position.Z -= Mathf.Sin(yawRad) * d;
                    }
                    else if (m.Type == "yaw") pose.YawDeg += i * m.Speed * degreesPerTexel;
                    w.FrameScripted(i, pose, snapDepth);
                }
                w.End();
            }
        }

        /// <summary>
        /// The M-004 sets: six sequences from two viewpoints per profile. Sub-pixel compensation is off here, so the
        /// applied output offset is zero and constant: the T-002 validator requires one resolution block per sequence.
        /// The internal image, index and depth do not depend on that flag (the present stage is the only reader);
        /// the compensated output is captured by <see cref="SubPixel"/>.
        /// </summary>
        public static void M004(StyleCaptureSession s, string anchor = null, string prefix = "m004_")
        {
            var baseFlags = s.Flags; var baseSettings = s.Settings.Clone();
            s.Flags = baseFlags.With(PixelFlags.SubPixelPresent, false);
            if (anchor != null) s.Settings.ditherAnchor = anchor;   // the same sets with another anchoring mode (configured default when null)
            foreach (string profile in AllProfiles)
            {
                s.SetProfile(profile); s.Apply();
                foreach (var v in MotionViewpoints)
                    MotionSet(s, prefix + profile, v, true);
            }
            s.Settings = baseSettings; s.Flags = baseFlags; s.Apply();
        }

        /// <summary>World point on the left silhouette edge of the 160 m tower as seen from <see cref="Eye70Tower"/> (its far-left corner).</summary>
        public static readonly Vector3 TowerEdge = new Vector3(310, 120, 440);

        /// <summary>
        /// C11: T1 and Y1 with sub-pixel compensation on (the M0 configuration) and off, with output. The snap's
        /// reference depth is the depth of the tower edge that the check tracks: position snapping locks one plane, and
        /// only on that plane does the image move in whole texels (R-003 §7), so that is where smoothness is measured.
        /// </summary>
        public static void SubPixel(StyleCaptureSession s)
        {
            s.SetProfile(MainProfile);
            var baseFlags = s.Flags;
            Vector3 forward = Quaternion.Euler(-Eye70Tower.PitchDeg, Eye70Tower.YawDeg, 0) * Vector3.forward;
            float depth = Vector3.Dot(TowerEdge - Eye70Tower.Position, forward);
            s.Flags = baseFlags.With(PixelFlags.SubPixelPresent, true); s.Apply();
            MotionSet(s, "c11_subpixel_on", Eye70Tower, true, "T1+Y1", depth);
            s.Flags = baseFlags.With(PixelFlags.SubPixelPresent, false); s.Apply();
            MotionSet(s, "c11_subpixel_off", Eye70Tower, true, "T1+Y1", depth);
            s.Flags = baseFlags; s.Apply();
        }

        /// <summary>A/B evidence (C12): each anchoring mode and each stabiliser switched off, same sequences, one profile and viewpoint.</summary>
        public static void M004Ab(StyleCaptureSession s)
        {
            s.SetProfile(MainProfile);
            var baseSettings = s.Settings.Clone(); var baseFlags = s.Flags.With(PixelFlags.SubPixelPresent, false);
            void Variant(string name, string anchor, bool snap, bool rotq)
            {
                s.Settings = baseSettings.Clone(); s.Settings.ditherAnchor = anchor;
                s.Flags = baseFlags.With(CameraFlags.SnapPosition, snap).With(CameraFlags.QuantiseRotation, rotq);
                s.Apply();
                MotionSet(s, "m004ab_" + name, Eye70, true);
            }
            Variant("hybrid", "hybrid", true, true);
            Variant("direction", "direction", true, true);
            Variant("screen", "screen", true, true);
            Variant("hybrid_nosnap", "hybrid", false, true);
            Variant("hybrid_norotq", "hybrid", true, false);
            Variant("direction_nosnap", "direction", false, true);
            Variant("direction_norotq", "direction", true, false);
            Variant("screen_nostab", "screen", false, false);
            // Temporal-noise control (C8): the static sequence with a dither that changes every frame.
            s.Settings = baseSettings.Clone(); s.Flags = baseFlags; s.Apply();
            var w = s.Begin("m004ab_control_frame_noise", Eye70.Name + "_S0", "M-004", new MotionSpec(), 60, Eye70.Name, true);
            s.Rig.Cut();
            for (int i = 0; i < 60; i++) { s.Capture.Binder.controlTemporalNoiseSeed = i + 1; w.FrameScripted(i, Eye70.Pose(), ReferenceDepth); }
            s.Capture.Binder.controlTemporalNoiseSeed = 0; w.End();
            s.Settings = baseSettings; s.Flags = baseFlags.With(PixelFlags.SubPixelPresent, true); s.Apply();
        }

        // ---- negative controls and stage toggles ---------------------------------------------------------------------

        static void TwelveStills(StyleCaptureSession s, string set, string sequence)
        {
            var w = s.Begin(set, sequence, "M-003", Scripted("twelve fixed stations from the eye70 viewpoint"), 12, Eye70.Name);
            for (int i = 0; i < 12; i++) w.FrameScripted(i, OnGrid(s, Station(Eye70, i)), ReferenceDepth);
            w.End();
        }

        public static void Controls(StyleCaptureSession s)
        {
            s.SetProfile(MainProfile);
            var baseFlags = s.Flags;
            s.Flags = baseFlags.With(PixelFlags.Quantise, false); s.Apply(); TwelveStills(s, "control_unquantised", MainProfile + "_eye70");
            s.Flags = baseFlags.With(PixelFlags.Dither, false); s.Apply(); TwelveStills(s, "control_dither_off", MainProfile + "_eye70");
            s.Flags = baseFlags.With(PixelFlags.Haze, false); s.Apply(); TwelveStills(s, "control_haze_off", MainProfile + "_eye70");
            s.Flags = baseFlags; s.Apply();
            s.Capture.PresentComponent.controlBilinear = true; TwelveStills(s, "control_bilinear", MainProfile + "_eye70"); s.Capture.PresentComponent.controlBilinear = false;
            s.Capture.Binder.controlReversedHaze = true; TwelveStills(s, "control_reversed_haze", MainProfile + "_eye70"); s.Capture.Binder.controlReversedHaze = false;
            // A sub-pixel-shifted frame: the declared block offset is non-zero (grid integrity must pass with it and fail without).
            s.Grid(ReferenceDepth, out float metresPerTexel, out _);
            var w = s.Begin("control_subpixel_offset", MainProfile + "_eye70", "M-003", new MotionSpec(), 1, Eye70.Name);
            // Start on the pose (cut), then move 0.3 texel right and up: the rig keeps the rendered pose and reports the remainder.
            var pose = Eye70.Pose(); s.Rig.Cut(); s.Rig.SetScriptedPose(pose, ReferenceDepth); s.Rig.Tick(StyleCaptureSession.Dt);
            pose.Position.X += 0.3f * metresPerTexel; pose.Position.Y += 0.3f * metresPerTexel;
            w.FrameScripted(0, pose, ReferenceDepth); w.End();
        }

        /// <summary>Stage A/B (C14): the M0 configuration, each stage off one at a time, and every combination of the optional stages.</summary>
        public static void Stages(StyleCaptureSession s)
        {
            s.SetProfile(MainProfile);
            var baseFlags = s.Flags; var pose = Eye70.Pose();
            void One(string name, PixelFlagState flags)
            {
                s.Flags = flags; s.Apply();
                var w = s.Begin("stages_ab", name, "M-003", new MotionSpec(), 1, Eye70.Name);
                w.FrameScripted(0, OnGrid(s, pose), ReferenceDepth); w.End();
            }
            One("m0_all_on", baseFlags);
            One("off_lowres", baseFlags.With(PixelFlags.LowRes, false));
            One("off_dither", baseFlags.With(PixelFlags.Dither, false));
            One("off_haze", baseFlags.With(PixelFlags.Haze, false));
            One("off_quantise", baseFlags.With(PixelFlags.Quantise, false));
            One("on_supersample", baseFlags.With(PixelFlags.Supersample, true));
            One("on_outline", baseFlags.With(PixelFlags.Outline, true));
            string[] optional = { PixelFlags.Haze, PixelFlags.Supersample, PixelFlags.Outline, PixelFlags.Dither };
            for (int mask = 0; mask < 16; mask++)
            {
                var flags = baseFlags; string name = "combo";
                for (int b = 0; b < 4; b++) { bool on = (mask & (1 << b)) != 0; flags = flags.With(optional[b], on); name += "_" + optional[b].Substring("rendering.pixel.".Length) + (on ? "1" : "0"); }
                One(name, flags);
            }
            s.Flags = baseFlags; s.Apply();
        }

        // ---- camera cut, orbit with a footfall impulse, dolly --------------------------------------------------------

        public static void Cut(StyleCaptureSession s)
        {
            s.SetProfile(MainProfile); s.Apply();
            s.Grid(ReferenceDepth, out float metresPerTexel, out float degreesPerTexel);
            CameraPose b = Street.Pose(137.3f, 40.2f, 31.7f, 1.3f);
            var w = s.Begin("cut_check", "moving_then_cut", "M-004", Scripted("eleven frames of a moving, turning camera at one place, then a cut to another pose"), 12, "cut");
            s.Rig.Cut();
            for (int i = 0; i < 11; i++) w.FrameScripted(i, Eye70.Pose(i * 0.37f * metresPerTexel, 0, i * 0.41f * degreesPerTexel), ReferenceDepth);
            s.Rig.Cut(); w.FrameScripted(11, b, ReferenceDepth); w.End();
            var w2 = s.Begin("cut_check", "static_at_new_pose", "M-004", new MotionSpec(), 1, "cut");
            s.Rig.Cut(); w2.FrameScripted(0, b, ReferenceDepth); w2.End();
        }

        sealed class OrbitTarget : ICameraTarget
        {
            public Vector3 Ground; public float Height = 80f;
            public bool TryGetTarget(out CameraVec3 groundPosition, out float heightMetres) { groundPosition = new CameraVec3(Ground.x, Ground.y, Ground.z); heightMetres = Height; return true; }
        }

        /// <summary>
        /// Twenty seconds in follow mode: the rig orbits the near 80 m block at about 5 degrees per second and one
        /// footfall-style impulse arrives over a real Core event bus at t = 8 s (event contract only).
        /// </summary>
        public static void Orbit(StyleCaptureSession s, string profile = MainProfile)
        {
            s.SetProfile(profile); s.Apply();
            var bus = new EventBus(new CollectingErrorSink(), 10000);
            try
            {
                s.Rig.Subscribe(bus);
                var target = new OrbitTarget { Ground = new Vector3(StyleTestScenes.Block80Near.x, 0, StyleTestScenes.Block80Near.z) };
                s.Rig.SetTarget(target); s.Rig.ClearScriptedPose(); s.Rig.SetOrbitYaw(-50f); s.Rig.Cut();
                const int frames = 1200;
                var motion = new MotionSpec { Type = "scripted", Axis = "orbit", SpeedSiKey = "deg_per_s", SpeedSi = 5f, Notes = "follow mode: orbit about the near 80 m block at 5 deg/s; one CameraShakeImpulse of 3e7 N s at frame 480" };
                var w = s.Begin("orbit_" + profile, "orbit_20s", "M-004", motion, frames, "orbit", false);
                for (int i = 0; i < frames; i++)
                {
                    s.Rig.SetOrbitYaw(-50f + i * 5f * StyleCaptureSession.Dt);
                    if (i == 480)
                    {
                        bus.Publish(new CameraShakeImpulse { OriginX = target.Ground.x + 20f, OriginY = 0f, OriginZ = target.Ground.z, ImpulseNs = 3e7f });
                        w.ExtraJson = "\"event\": {\"type\": \"CameraShakeImpulse\", \"impulse_ns\": 3e7}";
                    }
                    w.FrameFollow(i);
                }
                w.End();
            }
            finally { s.Rig.Unsubscribe(); s.Rig.SetTarget(null); }
        }

        /// <summary>Reported check (B15): a dolly toward the 10 km layer so haze band edges sweep across surfaces.</summary>
        public static void Dolly(StyleCaptureSession s, string profile = MainProfile)
        {
            s.SetProfile(profile); s.Apply();
            const float metresPerFrame = 6f;
            var motion = new MotionSpec { Type = "dolly", Axis = "camera_forward", SpeedPxPerFrame = 0.25f, SpeedSiKey = "m_per_s", SpeedSi = metresPerFrame / StyleCaptureSession.Dt, Notes = "dolly toward the 10 km layer; 0.25 px/frame is the lateral image speed 10 degrees off axis at 1.4 km" };
            var baseFlags = s.Flags; s.Flags = baseFlags.With(PixelFlags.SubPixelPresent, false); s.Apply();   // one resolution block per sequence (see M004)
            var w = s.Begin("dolly_" + profile, "eye70_D1", "M-004", motion, 180, Eye70.Name, true);
            s.Rig.Cut();
            for (int i = 0; i < 180; i++) w.FrameScripted(i, Eye70.Pose(0, i * metresPerFrame), ReferenceDepth);
            w.End();
            s.Flags = baseFlags; s.Apply();
        }
    }
}
