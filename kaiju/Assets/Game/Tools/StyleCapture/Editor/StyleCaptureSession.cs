using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Game.Cameras;
using Game.Rendering.Pixel;
using Game.Rendering.Pixel.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Tools.StyleCapture
{
    /// <summary>Motion block of style_capture@v1 (identical in sequence.json and in every frame sidecar).</summary>
    public sealed class MotionSpec
    {
        public string Type = "static";       // static | translate | yaw | pitch | dolly | scripted
        public string Axis = "none";
        public float SpeedPxPerFrame;        // internal px per frame (at the reference depth for translation, at the screen centre for yaw)
        public string SpeedSiKey;            // "m_per_s" or "deg_per_s"; null for none
        public float SpeedSi;
        public string Notes = "camera driven by the capture script; no simulation, animation or VFX time";
    }

    public struct HazeTarget { public float Distance; public int[] DarkPx, LightPx; }

    /// <summary>
    /// A capture session on one scene: loads flags (Config/FeatureFlags plus a configuration file), settings and the
    /// catalog, activates the pixel pipeline, and writes style_capture@v1 sequences (research/R-002 §6).
    /// </summary>
    public sealed class StyleCaptureSession : IDisposable
    {
        public const float Dt = 1f / 60f;
        public readonly PixelCaptureRig Capture;
        public readonly ThirdPersonRig Rig;
        public readonly PixelPipelineCatalog Catalog;
        public readonly string OutRoot, GitCommit;
        public PixelPipelineSettings Settings;
        public PixelFlagState Flags;
        public ThirdPersonRigSettings RigSettings;
        readonly PixelPipelineScope m_Scope;
        ushort[] m_Depth16; byte[] m_MaskPng; int m_MaskW, m_MaskH;

        public StyleCaptureSession(string scenePath, string outRoot, string configurationPath = StyleCaptureCli.M0Configuration)
        {
            OutRoot = outRoot;
            Catalog = AssetDatabase.LoadAssetAtPath<PixelPipelineCatalog>(PixelPipelineInstaller.CatalogPath) ?? throw new InvalidOperationException("pixel pipeline assets missing: run StyleCaptureCli.Setup");
            m_Scope = new PixelPipelineScope(Catalog.pipeline);
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Capture = new PixelCaptureRig();
            Rig = Capture.World.GetComponent<ThirdPersonRig>() ?? throw new InvalidOperationException("world camera has no ThirdPersonRig");
            Settings = PixelCaptureRig.LoadSettings();
            Flags = PixelCaptureRig.LoadFlags(configurationPath);
            RigSettings = ThirdPersonRigSettings.FromJson(File.ReadAllText("Assets/Game/Config/Camera/ThirdPersonRig.json"));
            GitCommit = StyleCaptureCli.Arg("-captureCommit") ?? Git("rev-parse HEAD");
            Capture.SetOutputSize(1920, 1080);
            Apply();
        }

        static string Git(string args)
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo("git", args) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
                string s = p.StandardOutput.ReadToEnd().Trim(); p.WaitForExit();
                return string.IsNullOrEmpty(s) ? "unknown" : s;
            }
            catch (Exception) { return "unknown"; }
        }

        /// <summary>Pushes the current settings and flag state into the binder and the rig.</summary>
        public void Apply()
        {
            Capture.Binder.Configure(Settings, Flags);
            Rig.Configure(RigSettings, Flags.IsOn(CameraFlags.SnapPosition), Flags.IsOn(CameraFlags.QuantiseRotation), Flags.IsOn(CameraFlags.FootfallImpulse));
        }

        public void SetProfile(string id) { Capture.Binder.profile = Catalog.FindProfile(id) ?? throw new ArgumentException("unknown palette profile " + id); }
        public PaletteProfile Profile => Capture.Binder.profile;

        /// <summary>Nominal texel size (m) at a depth, and texel angle (degrees), for the current output size.</summary>
        public void Grid(float depth, out float metresPerTexel, out float degreesPerTexel)
        {
            Rig.Tick(0f);   // sets the field of view from the grid
            Capture.Binder.TryGetGrid(out PixelGrid grid);
            metresPerTexel = 2f * depth * grid.TanHalfVerticalFov / grid.InternalHeight;
            degreesPerTexel = grid.TexelAngle * Mathf.Rad2Deg;
        }

        public string SettingsHash()
        {
            var sb = new StringBuilder(JsonUtility.ToJson(Settings));
            var flags = new List<string>(Flags.OnFlags); flags.Sort(StringComparer.Ordinal);
            sb.Append('|').Append(string.Join(",", flags)).Append('|').Append(Profile != null ? Profile.lutSha256 + Profile.ditherSpread.ToString("R", CultureInfo.InvariantCulture) : "none");
            sb.Append('|').Append(JsonUtility.ToJson(RigSettings)).Append('|').Append(Capture.Binder.controlReversedHaze).Append(Capture.Binder.controlTemporalNoiseSeed).Append(Capture.PresentComponent.controlBilinear);
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "").ToLowerInvariant();
        }

        public SequenceWriter Begin(string captureSetId, string sequenceId, string purpose, MotionSpec motion, int frameCount, string viewpoint, bool writeOutput = true)
            => new SequenceWriter(this, captureSetId, sequenceId, purpose, motion, frameCount, viewpoint, writeOutput);

        public void Dispose()
        {
            Capture.Dispose();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            m_Scope.Dispose();
        }

        /// <summary>Writes one sequence directory: per frame the images and the sidecar, then sequence.json.</summary>
        public sealed class SequenceWriter
        {
            readonly StyleCaptureSession m_S; readonly string m_Set, m_Seq, m_Purpose, m_Viewpoint, m_Dir; readonly MotionSpec m_Motion; readonly int m_Count; readonly bool m_Output;
            readonly List<string> m_Frames = new List<string>();
            public string Directory => m_Dir;
            /// <summary>Extra top-level sidecar JSON members for the next frame (already formatted, without the leading comma).</summary>
            public string ExtraJson;

            internal SequenceWriter(StyleCaptureSession s, string set, string seq, string purpose, MotionSpec motion, int count, string viewpoint, bool output)
            {
                m_S = s; m_Set = set; m_Seq = seq; m_Purpose = purpose; m_Motion = motion; m_Count = count; m_Viewpoint = viewpoint; m_Output = output;
                m_Dir = Path.Combine(s.OutRoot, set, seq);
                if (System.IO.Directory.Exists(m_Dir)) System.IO.Directory.Delete(m_Dir, true);
                System.IO.Directory.CreateDirectory(m_Dir);
            }

            /// <summary>Renders the scripted pose (through the rig's stabiliser) and writes the frame.</summary>
            public CapturedFrame FrameScripted(int index, CameraPose pose, float referenceDepth, HazeTarget[] hazeTargets = null)
            {
                m_S.Rig.SetScriptedPose(pose, referenceDepth);
                m_S.Rig.Tick(Dt);
                return Write(index, hazeTargets);
            }

            /// <summary>Advances the rig in follow mode by one step and writes the frame.</summary>
            public CapturedFrame FrameFollow(int index)
            {
                m_S.Rig.ClearScriptedPose();
                m_S.Rig.Tick(Dt);
                return Write(index, null);
            }

            CapturedFrame Write(int index, HazeTarget[] hazeTargets)
            {
                var s = m_S; var cap = s.Capture;
                cap.Render(m_Output);
                CapturedFrame f = cap.Read(m_Output);
                string stem = "f" + index.ToString("D6", CultureInfo.InvariantCulture);
                PixelCaptureRig.WriteRgbPng(Path.Combine(m_Dir, stem + "_color_internal.png"), f.Rgb, f.Width, f.Height);
                if (m_Output) PixelCaptureRig.WriteRgbPng(Path.Combine(m_Dir, stem + "_color_output.png"), f.OutputRgb, f.OutputWidth, f.OutputHeight);
                PixelCaptureRig.WriteGreyPng(Path.Combine(m_Dir, stem + "_index.png"), f.Index, f.Width, f.Height);
                if (s.m_Depth16 == null || s.m_Depth16.Length != f.Depth.Length) s.m_Depth16 = new ushort[f.Depth.Length];
                PixelCaptureRig.EncodeDepthLog16(f.Depth, cap.World.nearClipPlane, cap.World.farClipPlane, s.m_Depth16);
                PixelCaptureRig.WriteGrey16Png(Path.Combine(m_Dir, stem + "_depth.png"), s.m_Depth16, f.Width, f.Height);
                if (s.m_MaskPng == null || s.m_MaskW != f.Width || s.m_MaskH != f.Height)
                {
                    s.m_MaskPng = ImageConversion.EncodeArrayToPNG(new byte[f.Width * f.Height], UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm, (uint)f.Width, (uint)f.Height, (uint)f.Width);
                    s.m_MaskW = f.Width; s.m_MaskH = f.Height;
                }
                File.WriteAllBytes(Path.Combine(m_Dir, stem + "_mask_animated.png"), s.m_MaskPng);
                File.WriteAllText(Path.Combine(m_Dir, stem + ".json"), Sidecar(index, stem, hazeTargets));
                m_Frames.Add(stem + ".json");
                return f;
            }

            public void End(string extraJson = null)
            {
                var j = new StringBuilder();
                j.Append("{\n \"schema\": \"").Append(PixelContractVersions.StyleCapture).Append("\",\n \"capture_set_id\": ").Append(Str(m_Set)).Append(",\n \"sequence_id\": ").Append(Str(m_Seq));
                j.Append(",\n \"purpose\": ").Append(Str(m_Purpose)).Append(",\n \"frame_count\": ").Append(m_Frames.Count).Append(",\n \"motion\": ").Append(MotionJson());
                j.Append(",\n \"frames\": [");
                for (int i = 0; i < m_Frames.Count; i++) j.Append(i > 0 ? ", " : "").Append(Str(m_Frames[i]));
                j.Append("],\n \"renderer_settings_hash\": ").Append(Str(m_S.SettingsHash())).Append(",\n \"viewpoint\": ").Append(Str(m_Viewpoint));
                if (!string.IsNullOrEmpty(extraJson)) j.Append(",\n ").Append(extraJson);
                j.Append("\n}\n");
                File.WriteAllText(Path.Combine(m_Dir, "sequence.json"), j.ToString());
                if (m_Frames.Count != m_Count) UnityEngine.Debug.LogWarning("[StyleCapture] " + m_Set + "/" + m_Seq + ": wrote " + m_Frames.Count + " frames, planned " + m_Count);
            }

            string MotionJson()
            {
                var m = m_Motion;
                string si = m.SpeedSiKey == null ? "{}" : "{" + Str(m.SpeedSiKey) + ": " + Num(m.SpeedSi) + "}";
                return "{\"type\": " + Str(m.Type) + ", \"axis\": " + Str(m.Axis) + ", \"speed_internal_px_per_frame\": " + Num(m.SpeedPxPerFrame) + ", \"speed_si\": " + si + ", \"scripted\": true, \"notes\": " + Str(m.Notes) + "}";
            }

            string Sidecar(int index, string stem, HazeTarget[] hazeTargets)
            {
                var s = m_S; var binder = s.Capture.Binder; var layout = binder.Layout; var cam = s.Capture.World; var rig = s.Rig;
                var profile = binder.profile; var hints = binder.Hints; bool hasHints = binder.HasHints;
                Vector2Int shift = binder.PresentShift();
                // The offset the present stage applied: zero when sub-pixel compensation is off, whatever the rig's remainder is.
                bool applied = hasHints && s.Flags.IsOn(PixelFlags.SubPixelPresent);
                int k = layout.Scale;
                // Output pixel o shows texel floor((o + shift) / k) (bottom-left origin). In PNG coordinates (top-left
                // origin) the k x k blocks therefore start at x = -shift.x (mod k) and y = +shift.y (mod k).
                int ox = ((-shift.x % k) + k) % k, oy = ((shift.y % k) + k) % k;
                float hfov = 2f * Mathf.Atan(Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * layout.Width / layout.Height) * Mathf.Rad2Deg;
                Vector3 p = cam.transform.position; Quaternion q = cam.transform.rotation;
                CameraPose rendered = rig.RenderedPose, wanted = rig.WantedPose;
                bool snapOn = hasHints && (rig.SnapPosition || rig.QuantiseRotation);
                string anchor = !s.Flags.IsOn(PixelFlags.Dither) ? "screen" : s.Settings.Anchor == PixelDitherAnchor.Hybrid ? "surface" : s.Settings.Anchor == PixelDitherAnchor.Direction ? "direction" : "screen";
                float stepDeg = hasHints ? hints.RotationStep * Mathf.Rad2Deg : 0f;

                var j = new StringBuilder(4096);
                j.Append("{\n \"schema\": \"").Append(PixelContractVersions.StyleCapture).Append("\",\n \"capture_set_id\": ").Append(Str(m_Set)).Append(",\n \"sequence_id\": ").Append(Str(m_Seq)).Append(",\n");
                j.Append(" \"frame\": {\"index\": ").Append(index).Append(", \"sequence_length\": ").Append(m_Count).Append(", \"sim_time_s\": ").Append(Num(index * Dt)).Append(", \"dt_s\": ").Append(Num(Dt)).Append(", \"sim_paused\": true},\n");
                j.Append(" \"build\": {\"git_commit\": ").Append(Str(s.GitCommit)).Append(", \"unity_version\": ").Append(Str(Application.unityVersion)).Append(", \"render_pipeline\": \"URP 17.6.0 (Render Graph), asset ").Append(s.Catalog.pipeline.name).Append("\", \"feature_flags\": {");
                bool first = true;
                foreach (string flag in PixelFlags.All) { j.Append(first ? "" : ", ").Append(Str(flag)).Append(": ").Append(s.Flags.IsOn(flag) ? "true" : "false"); first = false; }
                foreach (string flag in CameraFlags.All) j.Append(", ").Append(Str(flag)).Append(": ").Append(s.Flags.IsOn(flag) ? "true" : "false");
                j.Append(", \"rendering.pixel.ditherMask\": ").Append(Str(s.Settings.ditherMask)).Append(", \"rendering.pixel.ditherAnchor\": ").Append(Str(s.Settings.ditherAnchor));
                j.Append(", \"control.reversedHaze\": ").Append(binder.controlReversedHaze ? "true" : "false").Append(", \"control.frameNoise\": ").Append(binder.controlTemporalNoiseSeed != 0 ? "true" : "false").Append(", \"control.bilinearPresent\": ").Append(s.Capture.PresentComponent.controlBilinear ? "true" : "false");
                j.Append(", \"antialiasing\": \"none\", \"post_processing\": false}},\n");
                j.Append(" \"style\": {\"palette_set\": \"").Append(PixelContractVersions.PaletteSet).Append("\", \"palette_profile_id\": ").Append(Str(profile.profileId)).Append(", \"palette_sha256\": ").Append(Str(profile.paletteSha256)).Append(", \"district_id\": \"style_test_scene\", \"time_of_day\": \"fixed_v1\"},\n");
                j.Append(" \"resolution\": {\"internal_wh\": [").Append(layout.Width).Append(", ").Append(layout.Height).Append("], \"output_wh\": [").Append(layout.OutputWidth).Append(", ").Append(layout.OutputHeight).Append("], \"integer_scale\": ").Append(k);
                j.Append(", \"output_block_offset_px\": [").Append(ox).Append(", ").Append(oy).Append("], \"subpixel_offset_internal_px\": [").Append(Sub(applied ? hints.SubTexelX : 0f)).Append(", ").Append(Sub(applied ? hints.SubTexelY : 0f)).Append("]},\n");
                j.Append(" \"camera\": {\"rig\": ").Append(Str(rig.RigId)).Append(", \"projection\": \"perspective\", \"hfov_deg\": ").Append(Num(hfov)).Append(", \"vfov_deg\": ").Append(Num(cam.fieldOfView)).Append(", \"near_m\": ").Append(Num(cam.nearClipPlane)).Append(", \"far_m\": ").Append(Num(cam.farClipPlane));
                j.Append(", \"origin_offset_m\": [0, 0, 0], \"position_local_m\": ").Append(Vec(p.x, p.y, p.z)).Append(", \"rotation_xyzw\": [").Append(Num(q.x)).Append(", ").Append(Num(q.y)).Append(", ").Append(Num(q.z)).Append(", ").Append(Num(q.w)).Append("]");
                j.Append(", \"yaw_pitch_roll_deg\": ").Append(Vec(rendered.YawDeg, -rendered.PitchDeg, 0f));
                j.Append(", \"wanted_position_local_m\": ").Append(Vec(wanted.Position.X, wanted.Position.Y, wanted.Position.Z)).Append(", \"wanted_yaw_pitch_roll_deg\": ").Append(Vec(wanted.YawDeg, -wanted.PitchDeg, 0f));
                j.Append(",\n  \"snap\": {\"enabled\": ").Append(snapOn ? "true" : "false").Append(", \"position_snap\": ").Append(hasHints && rig.SnapPosition ? "true" : "false").Append(", \"rotation_quantise\": ").Append(hasHints && rig.QuantiseRotation ? "true" : "false");
                j.Append(", \"reference_depth_m\": ").Append(Num(rig.ReferenceDepth)).Append(", \"position_grid_m\": ").Append(Num(hasHints ? hints.PositionGrid : 0f)).Append(", \"yaw_step_deg\": ").Append(Num(stepDeg)).Append(", \"pitch_step_deg\": ").Append(Num(stepDeg));
                j.Append(", \"snapped_position_local_m\": ").Append(Vec(p.x, p.y, p.z)).Append(", \"snapped_yaw_pitch_roll_deg\": ").Append(Vec(rendered.YawDeg, -rendered.PitchDeg, 0f));
                j.Append(", \"remainder_internal_px\": [").Append(Sub(hasHints ? hints.SubTexelX : 0f)).Append(", ").Append(Sub(hasHints ? hints.SubTexelY : 0f)).Append("]");
                j.Append(", \"dither_origin_texels\": [").Append(hasHints ? hints.DitherOriginX : 0).Append(", ").Append(hasHints ? hints.DitherOriginY : 0).Append("], \"present_shift_output_px\": [").Append(shift.x).Append(", ").Append(shift.y).Append("], \"cut\": ").Append(hasHints && hints.Cut ? "true" : "false");
                j.Append(", \"dither_anchor\": ").Append(Str(anchor)).Append("}},\n");
                j.Append(" \"conventions\": {\"handedness\": \"left (Unity)\", \"up\": \"+Y\", \"forward\": \"+Z\", \"pixel_origin\": \"top-left; pixel centre at (x+0.5, y+0.5)\", \"rotation\": \"camera-to-world quaternion of the camera transform used for rendering this frame (after snapping)\", \"yaw_pitch_roll\": \"Unity Euler angles in degrees: yaw about +Y (0 = +Z, positive right), pitch about +X (positive looks down, as in the R-002 horizon formula), roll about +Z\", \"subpixel_offset\": \"wanted view minus rendered view in internal texels, +x right, +y up\"},\n");
                j.Append(" \"motion\": ").Append(MotionJson()).Append(",\n");
                j.Append(" \"files\": {\"color_internal\": ").Append(Str(stem + "_color_internal.png")).Append(", \"color_output\": ").Append(m_Output ? Str(stem + "_color_output.png") : "null").Append(", \"palette_index\": ").Append(Str(stem + "_index.png"));
                j.Append(", \"depth\": ").Append(Str(stem + "_depth.png")).Append(", \"mask_ui\": null, \"mask_animated\": ").Append(Str(stem + "_mask_animated.png")).Append(", \"motion_vectors\": null},\n");
                j.Append(" \"encodings\": {\"color_internal\": \"8-bit sRGB PNG, RGB, no alpha; final palette-quantised image before upscale and before UI (guard band cropped)\", \"color_output\": \"8-bit sRGB PNG, RGB, no alpha; the presented frame after upscale and sub-pixel compensation, no UI\", \"palette_index\": \"8-bit greyscale PNG at internal resolution; value = index into the profile colour list; 255 = not palette-quantised\", ");
                j.Append("\"depth\": {\"format\": \"png16_log\", \"resolution\": \"internal\", \"quantity\": \"eye-space linear depth z (m) along the camera forward axis\", \"decode\": \"z = near_m * (far_m / near_m) ** (v / 65535); v = 65535 means sky / no geometry\"}, \"mask_animated\": \"8-bit PNG at internal resolution; 255 = animated element (none in this scene)\"}");
                if (hazeTargets != null)
                {
                    j.Append(",\n \"haze_targets\": [");
                    for (int i = 0; i < hazeTargets.Length; i++)
                    {
                        var t = hazeTargets[i];
                        j.Append(i > 0 ? ", " : "").Append("{\"distance_m\": ").Append(Num(t.Distance)).Append(", \"dark_px\": [").Append(string.Join(", ", t.DarkPx)).Append("], \"light_px\": [").Append(string.Join(", ", t.LightPx)).Append("]}");
                    }
                    j.Append("]");
                }
                if (!string.IsNullOrEmpty(ExtraJson)) { j.Append(",\n ").Append(ExtraJson); ExtraJson = null; }
                j.Append("\n}\n");
                return j.ToString();
            }
        }

        public static string Str(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        public static string Num(float v) => ((double)v).ToString("0.######", CultureInfo.InvariantCulture);
        /// <summary>Sub-texel remainders are reported to 1e-4 texel (float noise below that would make equal frames look different).</summary>
        public static string Sub(float v) { double r = Math.Round(v, 4); return (r == 0 ? 0.0 : r).ToString("0.####", CultureInfo.InvariantCulture); }
        public static string Vec(float x, float y, float z) => "[" + Num4(x) + ", " + Num4(y) + ", " + Num4(z) + "]";
        static string Num4(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    }
}
