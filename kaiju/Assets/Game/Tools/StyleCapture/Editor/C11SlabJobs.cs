using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using Game.Cameras;
using Game.Rendering.Pixel;
using UnityEngine;

namespace Game.Tools.StyleCapture
{
    /// <summary>
    /// T-182: capture jobs for the C11 slab / depthShift evaluation (research/R-131 section 4, MODULE.md "Layer slabs"). The T1 translation sequence
    /// (eye70_tower, 180 frames, camera_right, 0.1 texel/frame at the 600 m reference) is rendered through the shipped present path in every configuration:
    /// depth the camera is snapped at (663.0 m, the reference-class edge; and 664.874 m, the rig's skyline design depth) x {single snap, depthShift, layerSnap}, each from
    /// several start offsets so the edge phase against the lattice varies. The present stage writes the log-coded depth of the texel it shows
    /// (<see cref="PixelCameraBinder.controlDepthOutput"/>), so edge positions are measured in output pixels from the real present shader, for the three class edges
    /// (near 124 m, reference 663 m, far 10.4 km). Which main depth C11 is judged on belongs to Q-260 (founders): both are captured and nothing is lowered.
    /// Analysis: Tools/Rendering/Pixel/c11_slab.py.
    /// </summary>
    public static class C11SlabJobs
    {
        public const string Schema = "c11_slab@v1";
        public const float NearDepth = 124.2f, FarDepth = 10353f;
        public static readonly float[] MainDepths = { 663.0f, 664.874f };
        public static readonly string[] Modes = { "base", "depthshift", "layersnap" };
        /// <summary>Half width (output pixels) of the depth row window read around each class point; covers the largest model error (17 px) plus the search window.</summary>
        public const int HalfWindow = 128;
        /// <summary>Class points (world, m): near box corner, the tower's far-left corner (reference), a far corner. Same points as Tools/Rendering/Pixel/pixel_checks.py.</summary>
        public static readonly Vector3 NearPoint = new Vector3(-15f, 60f, -10f), FarPoint = new Vector3(4000f, 700f, 9400f);
        public static readonly string[] ClassNames = { "near", "reference", "far" };
        public static Vector3[] ClassPoints => new[] { NearPoint, StyleCaptureJobs.TowerEdge, FarPoint };

        /// <summary>Start offset (m, along the camera's right axis) of phase j of n: golden-ratio spacing over one far texel, so the edge phase against every slab lattice varies.</summary>
        public static float StartOffsetM(int j, float startM, float spanM) => startM + (float)(((j + 0.5) * 0.6180339887498949) % 1.0) * spanM;

        public static string ConfigName(float mainDepth, string mode) => "ref" + mainDepth.ToString("0.###", CultureInfo.InvariantCulture) + "_" + mode;

        /// <summary>One group of start offsets (m along the camera's right axis): the classes whose edge is a clean silhouette over the whole span (see <see cref="Groups"/>).</summary>
        public sealed class Group { public string Tag; public float StartM, SpanM; public int Phases; public string[] Classes; }

        /// <summary>
        /// The near corner of the 80 m block is a silhouette against the background only while the camera is right of the block's left face (right coordinate above about -16 m); the far
        /// corner is occluded left of about -41 m. The edge phase against the far lattice (25 m grid) needs a span of one far texel. So two start groups: A (near and reference classes:
        /// a 1.2 m span covers four near texels, the corner stays on screen for about 130 frames) and B (reference and far classes: a 25 m span covers one far texel).
        /// </summary>
        public static Group[] Groups(int phasesB) => new[]
        {
            new Group { Tag = "A", StartM = -15.5f, SpanM = 1.2f, Phases = Mathf.Max(1, phasesB / 2), Classes = new[] { "near", "reference" } },
            new Group { Tag = "B", StartM = -41f, SpanM = 25f, Phases = phasesB, Classes = new[] { "reference", "far" } },
        };

        /// <summary>Arguments: -captureC11Phases (group B phases, default 48; group A gets half), -captureC11Frames (180), -captureC11Configs (comma separated name filters; default all).</summary>
        public static void Run(StyleCaptureSession s)
        {
            int phases = int.Parse(StyleCaptureCli.Arg("-captureC11Phases", "48"), CultureInfo.InvariantCulture), frames = int.Parse(StyleCaptureCli.Arg("-captureC11Frames", "180"), CultureInfo.InvariantCulture);
            string filter = StyleCaptureCli.Arg("-captureC11Configs", "");
            s.SetProfile(StyleCaptureJobs.MainProfile);
            var baseFlags = s.Flags;
            var geometric = GeometricDepths(StyleCaptureJobs.Eye70Tower.Pose(), s.Capture.World.fieldOfView);
            UnityEngine.Debug.Log("[C11Slab] geometric class depths (m): near " + F(geometric[0]) + ", reference " + F(geometric[1]) + ", far " + F(geometric[2]));
            var configs = new List<(string name, string mode, float near, float main, float far, bool layer, bool depthShift)>();
            foreach (float main in MainDepths)
                foreach (string mode in Modes) configs.Add((ConfigName(main, mode), mode, NearDepth, main, FarDepth, mode == "layersnap", mode == "depthshift"));
            // The class edges sit at their geometric depths (the near corner at about 123.5 m, not the 124.2 m of the R-131 class table; the far corner at about 10371 m): the lock is exact or
            // nothing (T-181), so the layers are also captured at the geometric depths, to separate "slabs cannot lock the edge" from "the configured depth is 0.2-0.6 % off".
            configs.Add(("exact_layersnap", "layersnap", geometric[0], geometric[1], geometric[2], true, false));
            foreach (var c in configs)
            {
                if (filter.Length > 0 && Array.FindIndex(filter.Split(','), f => c.name.Contains(f)) < 0) continue;
                s.Flags = baseFlags.With(PixelFlags.SubPixelPresent, true).With(PixelFlags.DepthShift, c.depthShift).With(PixelFlags.LayerSnap, c.layer);
                s.Apply();
                foreach (var g in Groups(phases))
                    for (int j = 0; j < g.Phases; j++) Sequence(s, "c11slab_" + c.name, g.Tag + j.ToString("D2", CultureInfo.InvariantCulture), c.name, c.mode, c.near, c.main, c.far, geometric, g, j, StartOffsetM(j, g.StartM, g.SpanM), frames);
            }
            // depthShift is ignored when layerSnap is on (MODULE.md): both on must render exactly like layerSnap alone.
            if (filter.Length == 0 || filter.Contains("both"))
            {
                s.Flags = baseFlags.With(PixelFlags.SubPixelPresent, true).With(PixelFlags.DepthShift, true).With(PixelFlags.LayerSnap, true); s.Apply();
                var g = Groups(phases)[1];
                Sequence(s, "c11slab_ref663_layersnap_and_depthshift", "B00", "ref663_layersnap_and_depthshift", "layersnap", NearDepth, 663.0f, FarDepth, geometric, g, 0, StartOffsetM(0, g.StartM, g.SpanM), frames);
            }
            s.Flags = baseFlags; s.Apply();
        }

        /// <summary>Eye depth (forward distance, m) of the three class points from a pose: constant along the T1 motion (the camera moves along its right axis).</summary>
        public static float[] GeometricDepths(CameraPose pose, float fovDeg)
        {
            var q = Quaternion.Euler(-pose.PitchDeg, pose.YawDeg, 0f); var pts = ClassPoints; var z = new float[3];
            for (int c = 0; c < 3; c++) z[c] = (Quaternion.Inverse(q) * (pts[c] - new Vector3(pose.Position.X, pose.Position.Y, pose.Position.Z))).z;
            return z;
        }

        /// <summary>
        /// T-181d acceptance, run by the capture tool: on cut frames from three viewpoints the layered render (every layer starts at the wanted position, so offsets and
        /// remainders are zero) is compared with the single render: share of internal texels with the same colour and palette index, depth agreement, and whether every
        /// output pixel is an exact palette colour. Writes <c>layercheck.json</c> and logs the numbers; throws when the layered path was not taken or the output is not palette-exact.
        /// </summary>
        public static void LayerCheck(StyleCaptureSession s)
        {
            s.SetProfile(StyleCaptureJobs.MainProfile);
            var baseFlags = s.Flags; var cap = s.Capture; var rig = s.Rig;
            var palette = new HashSet<int>();
            foreach (var c in s.Profile.colours) palette.Add(c.r << 16 | c.g << 8 | c.b);
            foreach (var c in s.Profile.hazeRamp) palette.Add(c.r << 16 | c.g << 8 | c.b);
            var rows = new List<string>(); bool ok = true;
            foreach (var v in new[] { StyleCaptureJobs.Street, StyleCaptureJobs.Eye70, StyleCaptureJobs.Eye70Tower })
            {
                byte[] single, singleIndex, layeredRgb, layeredIndex, layeredOut; float[] singleDepth, layeredDepth; var singleOut = new HashSet<int>(); int w = 0, h = 0;
                s.Flags = baseFlags.With(PixelFlags.SubPixelPresent, true).With(PixelFlags.LayerSnap, false).With(PixelFlags.DepthShift, false); s.Apply();
                rig.Cut(); rig.SetScriptedLayerReferences(0f, 0f); rig.SetScriptedPose(v.Pose(), 664.874f); rig.Tick(StyleCaptureSession.Dt);
                cap.Render(true); var a = cap.Read(true);
                w = a.Width; h = a.Height; single = (byte[])a.Rgb.Clone(); singleIndex = (byte[])a.Index.Clone(); singleDepth = (float[])a.Depth.Clone();
                for (int i = 0; i < a.OutputRgb.Length; i += 3) singleOut.Add(a.OutputRgb[i] << 16 | a.OutputRgb[i + 1] << 8 | a.OutputRgb[i + 2]);
                s.Flags = baseFlags.With(PixelFlags.SubPixelPresent, true).With(PixelFlags.LayerSnap, true).With(PixelFlags.DepthShift, false); s.Apply();
                rig.Cut(); rig.SetScriptedLayerReferences(0f, 0f); rig.SetScriptedPose(v.Pose(), NearDepth); rig.Tick(StyleCaptureSession.Dt);
                cap.Render(true); bool layered = cap.Binder.Layered; var b = cap.Read(true);
                layeredRgb = b.Rgb; layeredIndex = b.Index; layeredDepth = b.Depth; layeredOut = b.OutputRgb;
                int same = 0, sameIndex = 0, sameDepth = 0, n = w * h;
                for (int i = 0; i < n; i++)
                {
                    if (single[i * 3] == layeredRgb[i * 3] && single[i * 3 + 1] == layeredRgb[i * 3 + 1] && single[i * 3 + 2] == layeredRgb[i * 3 + 2]) same++;
                    if (singleIndex[i] == layeredIndex[i]) sameIndex++;
                    float da = singleDepth[i], db = layeredDepth[i];
                    if ((da >= 1e29f && db >= 1e29f) || (da < 1e29f && db < 1e29f && Mathf.Abs(da - db) <= 0.002f * da)) sameDepth++;
                }
                int notPalette = 0;
                for (int i = 0; i < layeredOut.Length; i += 3)
                {
                    int c = layeredOut[i] << 16 | layeredOut[i + 1] << 8 | layeredOut[i + 2];
                    if (!palette.Contains(c) && !singleOut.Contains(c)) notPalette++;
                }
                var lay = cap.Binder.Layout;
                string row = "{\"viewpoint\": \"" + v.Name + "\", \"layered\": " + (layered ? "true" : "false") + ", \"texels\": " + n + ", \"same_colour_share\": " + ((double)same / n).ToString("0.######", CultureInfo.InvariantCulture)
                    + ", \"same_index_share\": " + ((double)sameIndex / n).ToString("0.######", CultureInfo.InvariantCulture) + ", \"same_depth_share\": " + ((double)sameDepth / n).ToString("0.######", CultureInfo.InvariantCulture)
                    + ", \"output_pixels_not_palette\": " + notPalette + ", \"output_pixels\": " + layeredOut.Length / 3 + ", \"layers\": " + (cap.Binder.LayersJson() ?? "null") + "}";
                rows.Add(row);
                UnityEngine.Debug.Log("[C11Slab] layercheck " + v.Name + ": layered " + layered + ", same colour " + ((double)same / n).ToString("P3", CultureInfo.InvariantCulture) + ", same index " + ((double)sameIndex / n).ToString("P3", CultureInfo.InvariantCulture) + ", same depth " + ((double)sameDepth / n).ToString("P3", CultureInfo.InvariantCulture) + ", output pixels not in the palette " + notPalette);
                ok &= layered && notPalette == 0;
            }
            // One frame through the standard style_capture@v1 writer with layerSnap on and off: the layered one carries the versioned `layers` sidecar member, the single one does not.
            foreach (bool layerOn in new[] { true, false })
            {
                s.Flags = baseFlags.With(PixelFlags.SubPixelPresent, true).With(PixelFlags.LayerSnap, layerOn).With(PixelFlags.DepthShift, false); s.Apply();
                rig.Cut(); rig.SetScriptedLayerReferences(0f, 0f);
                var w = s.Begin("layercheck_sidecar", layerOn ? "layersnap_on" : "layersnap_off", "T-182", new MotionSpec(), 1, StyleCaptureJobs.Eye70.Name);
                w.FrameScripted(0, StyleCaptureJobs.Eye70.Pose(), layerOn ? NearDepth : 664.874f);
                w.End();
            }
            s.Flags = baseFlags; s.Apply();
            Directory.CreateDirectory(s.OutRoot);
            File.WriteAllText(Path.Combine(s.OutRoot, "layercheck.json"), "{\"schema\": \"c11_layercheck@v1\", \"frames\": [\n " + string.Join(",\n ", rows) + "\n]}\n");
            if (!ok) throw new InvalidOperationException("layercheck failed: the layered path was not taken or the output holds a colour outside the palette (see layercheck.json)");
        }

        static void Sequence(StyleCaptureSession s, string set, string seq, string config, string mode, float nearDepth, float mainDepth, float farDepth, float[] geometric, Group group, int phase, float startM, int frames)
        {
            var cap = s.Capture; var rig = s.Rig; var v = StyleCaptureJobs.Eye70Tower;
            s.Grid(StyleCaptureJobs.ReferenceDepth, out float metresPerTexel, out float degreesPerTexel);
            float speed = 0.1f, yawRad = v.YawDeg * Mathf.Deg2Rad;
            string dir = Path.Combine(s.OutRoot, set, seq); if (Directory.Exists(dir)) Directory.Delete(dir, true); Directory.CreateDirectory(dir);
            bool layer = mode == "layersnap";
            rig.Cut();
            rig.SetScriptedLayerReferences(layer ? mainDepth : 0f, layer ? farDepth : 0f);
            var points = ClassPoints; var json = new StringBuilder(1 << 20); int layeredFrames = 0; string rejection = null;
            json.Append('[');
            cap.Binder.controlDepthOutput = true;
            try
            {
                for (int i = 0; i < frames; i++)
                {
                    CameraPose pose = v.Pose(); float d = startM + i * speed * metresPerTexel;   // along the camera's right axis
                    pose.Position.X += Mathf.Cos(yawRad) * d; pose.Position.Z -= Mathf.Sin(yawRad) * d;
                    rig.SetScriptedPose(pose, layer ? nearDepth : mainDepth);
                    rig.Tick(StyleCaptureSession.Dt);
                    cap.Render(true);
                    var b = cap.Binder; var layout = b.Layout; var wanted = rig.WantedPose;
                    if (b.Layered) layeredFrames++; else if (layer && rejection == null) rejection = b.LayerRejection ?? "not layered (no reason given)";
                    if (i > 0) json.Append(',');
                    json.Append("\n{\"i\": ").Append(i).Append(", \"wanted_position_m\": ").Append(Vec(wanted.Position.X, wanted.Position.Y, wanted.Position.Z));
                    json.Append(", \"wanted_yaw_pitch_deg\": [").Append(F(wanted.YawDeg)).Append(", ").Append(F(wanted.PitchDeg)).Append("], \"layered\": ").Append(b.Layered ? "true" : "false");
                    Vector2Int shift = b.PresentShift();
                    json.Append(", \"present_shift_output_px\": [").Append(shift.x).Append(", ").Append(shift.y).Append("], \"classes\": [");
                    for (int c = 0; c < 3; c++)
                    {
                        if (c > 0) json.Append(", ");
                        bool vis = Project(cap.World, wanted, layout, points[c], out float px, out float py);
                        int x0 = Mathf.Max(0, Mathf.FloorToInt(px) - HalfWindow), x1 = Mathf.Min(layout.OutputWidth, Mathf.FloorToInt(px) + HalfWindow + 1);
                        json.Append("{\"visible\": ").Append(vis && x1 > x0 ? "true" : "false").Append(", \"px\": ").Append(F(px)).Append(", \"py\": ").Append(F(py));
                        if (vis && x1 > x0)
                        {
                            int rowTop = Mathf.Clamp(Mathf.FloorToInt(py), 0, layout.OutputHeight - 1);
                            byte[] rgb = cap.ReadOutputRegion(x0, layout.OutputHeight - 1 - rowTop, x1 - x0, 1);
                            json.Append(", \"row_top\": ").Append(rowTop).Append(", \"x0\": ").Append(x0).Append(", \"n\": ").Append(x1 - x0).Append(", \"depth_code_b64\": \"").Append(Convert.ToBase64String(rgb)).Append('"');
                        }
                        json.Append('}');
                    }
                    json.Append(']');
                    string layersJson = b.LayersJson();
                    if (layersJson != null) json.Append(", \"layers\": ").Append(layersJson);
                    json.Append('}');
                }
            }
            finally { cap.Binder.controlDepthOutput = false; }
            json.Append("\n]");
            using (var fs = File.Create(Path.Combine(dir, "c11_frames.json.gz"))) using (var gz = new GZipStream(fs, System.IO.Compression.CompressionLevel.Optimal)) { byte[] bytes = new UTF8Encoding(false).GetBytes(json.ToString()); gz.Write(bytes, 0, bytes.Length); }

            // A few plain colour frames of phase 0 for the eye (the seam and the composite are judged on these, not on the depth code).
            if (phase == 0 && group.Tag == "B")
                foreach (int i in new[] { 0, frames / 2, frames - 1 })
                {
                    CameraPose pose = v.Pose(); float d = startM + i * speed * metresPerTexel;
                    pose.Position.X += Mathf.Cos(yawRad) * d; pose.Position.Z -= Mathf.Sin(yawRad) * d;
                    rig.SetScriptedPose(pose, layer ? nearDepth : mainDepth); rig.Tick(StyleCaptureSession.Dt);
                    cap.Render(true); var f = cap.Read(true);
                    PixelCaptureRig.WriteRgbPng(Path.Combine(dir, "visual_f" + i.ToString("D3", CultureInfo.InvariantCulture) + "_color_output.png"), f.OutputRgb, f.OutputWidth, f.OutputHeight);
                }

            var m = new StringBuilder();
            m.Append("{\n \"schema\": \"").Append(Schema).Append("\",\n \"config\": ").Append(StyleCaptureSession.Str(config)).Append(",\n \"mode\": ").Append(StyleCaptureSession.Str(mode));
            m.Append(",\n \"main_depth_m\": ").Append(F(mainDepth)).Append(",\n \"snap_reference_depth_m\": ").Append(F(layer ? nearDepth : mainDepth));
            m.Append(",\n \"layer_depths_m\": ").Append(layer ? "[" + F(nearDepth) + ", " + F(mainDepth) + ", " + F(farDepth) + "]" : "null");
            m.Append(",\n \"class_depths_geometric_m\": [").Append(F(geometric[0])).Append(", ").Append(F(geometric[1])).Append(", ").Append(F(geometric[2])).Append("]");
            m.Append(",\n \"flags\": {\"subPixelPresent\": ").Append(s.Flags.IsOn(PixelFlags.SubPixelPresent) ? "true" : "false").Append(", \"depthShift\": ").Append(s.Flags.IsOn(PixelFlags.DepthShift) ? "true" : "false").Append(", \"layerSnap\": ").Append(s.Flags.IsOn(PixelFlags.LayerSnap) ? "true" : "false").Append("}");
            m.Append(",\n \"group\": ").Append(StyleCaptureSession.Str(group.Tag)).Append(",\n \"group_classes\": [").Append(string.Join(", ", Array.ConvertAll(group.Classes, StyleCaptureSession.Str))).Append("]").Append(",\n \"phase_index\": ").Append(phase).Append(",\n \"start_offset_m\": ").Append(F(startM)).Append(",\n \"frames\": ").Append(frames).Append(",\n \"layered_frames\": ").Append(layeredFrames);
            m.Append(",\n \"layer_rejection\": ").Append(rejection == null ? "null" : StyleCaptureSession.Str(rejection));
            m.Append(",\n \"motion\": {\"type\": \"translate\", \"axis\": \"camera_right\", \"speed_internal_px_per_frame_at_600m\": ").Append(F(speed)).Append(", \"metres_per_frame\": ").Append(F(speed * metresPerTexel)).Append(", \"dt_s\": ").Append(F(StyleCaptureSession.Dt)).Append("}");
            m.Append(",\n \"viewpoint\": \"eye70_tower\",\n \"profile\": ").Append(StyleCaptureSession.Str(s.Profile.profileId));
            var lay = cap.Binder.Layout;
            m.Append(",\n \"resolution\": {\"output_wh\": [").Append(lay.OutputWidth).Append(", ").Append(lay.OutputHeight).Append("], \"internal_wh\": [").Append(lay.Width).Append(", ").Append(lay.Height).Append("], \"scale\": ").Append(lay.Scale).Append(", \"guard\": ").Append(lay.Guard).Append("}");
            m.Append(",\n \"classes\": [");
            for (int c = 0; c < 3; c++) m.Append(c > 0 ? ", " : "").Append("{\"name\": \"").Append(ClassNames[c]).Append("\", \"world_m\": ").Append(Vec(points[c].x, points[c].y, points[c].z)).Append("}");
            m.Append("],\n \"window\": {\"half_width_output_px\": ").Append(HalfWindow).Append("},\n \"depth_code\": \"24-bit big-endian in the output RGB bytes; v = round(16777214 * ln(z / 0.1) / ln(1e7)); 16777215 = sky or empty; z in metres (eye depth)\"");
            m.Append(",\n \"files\": {\"frames\": \"c11_frames.json.gz\"},\n \"git_commit\": ").Append(StyleCaptureSession.Str(s.GitCommit)).Append(",\n \"settings_hash\": ").Append(StyleCaptureSession.Str(s.SettingsHash())).Append("\n}\n");
            File.WriteAllText(Path.Combine(dir, "sequence.json"), m.ToString());
            UnityEngine.Debug.Log("[C11Slab] " + set + "/" + seq + " start " + startM.ToString("F2", CultureInfo.InvariantCulture) + " m, layered frames " + layeredFrames + "/" + frames + (rejection != null ? ", rejected: " + rejection : ""));
        }

        /// <summary>Projects a world point from the wanted pose to output pixels (top-left origin); false when it is behind the camera or off screen.</summary>
        public static bool Project(Camera cam, CameraPose wanted, PixelLayout layout, Vector3 world, out float px, out float py)
        {
            var q = Quaternion.Euler(-wanted.PitchDeg, wanted.YawDeg, 0f);
            Vector3 v = Quaternion.Inverse(q) * (world - new Vector3(wanted.Position.X, wanted.Position.Y, wanted.Position.Z));
            float tanV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), tanH = tanV * layout.Width / layout.Height;
            px = py = float.NaN;
            if (v.z <= 0f) return false;
            px = (v.x / v.z / tanH * 0.5f + 0.5f) * layout.OutputWidth; py = (0.5f - v.y / v.z / tanV * 0.5f) * layout.OutputHeight;
            return px >= 0f && px < layout.OutputWidth && py >= 0f && py < layout.OutputHeight;
        }

        static string F(float v) => ((double)v).ToString("0.######", CultureInfo.InvariantCulture);
        static string Vec(float x, float y, float z) => "[" + x.ToString("R", CultureInfo.InvariantCulture) + ", " + y.ToString("R", CultureInfo.InvariantCulture) + ", " + z.ToString("R", CultureInfo.InvariantCulture) + "]";
    }
}
