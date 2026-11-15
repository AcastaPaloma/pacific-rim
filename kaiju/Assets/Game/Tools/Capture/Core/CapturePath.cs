// capture_path@v1 (T-147, docs/store/capture_plan.md section 3): parse and lint. Engine-free; the Python lint in
// Pipelines/capture/capture_tools/lint.py implements the same rule ids (L01..L16) over the same JSON.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Game.World.Geo;

namespace Game.Tools.Capture
{
    public struct CaptureKeyframe
    {
        public double T, E, N, Alt, Yaw, Pitch, Hfov;
    }

    public struct CaptureShake { public double T, AmpPx, DecayS; }

    public struct CaptureOutput { public int InternalW, InternalH, OutputW, OutputH, Scale; }

    /// <summary>One M-004 motion class (Tools/Metrics registry M-004@v1.1 classes): nominal speed in internal px per frame, translation at the reference depth, yaw at the screen centre.</summary>
    public struct MotionClass { public string Id, Type, Axis; public double SpeedPxPerFrame; public int Frames; }

    /// <summary>One scripted camera path. Altitudes are metres above ground or NAVD88 (<see cref="AltIsNavd88"/>); positions are BayTM metres.</summary>
    public sealed class CapturePath
    {
        public const string Schema = "capture_path@v1";
        public static readonly string[] KnownProfiles = { "warm_rust_sepia", "warm_amber_haze", "slate_teal", "lavender_peach", "slate_crimson" };
        public static readonly string[] KnownRequires = { "walk", "kaiju", "m2_streaming", "hud", "coop", "logotype", "mech_mesh" };
        public static readonly string[] Interpolations = { "linear", "catmull_rom", "ease_in_out" };
        public static readonly string[] Purposes = { "M-003", "M-004" };
        /// <summary>The six M-004 classes, same table as Game.Tools.StyleCapture.StyleCaptureJobs.M004Sequences (S0 60 frames, the rest 180).</summary>
        public static readonly MotionClass[] MotionClasses =
        {
            new MotionClass { Id = "S0", Type = "static", Axis = "none", SpeedPxPerFrame = 0.0, Frames = 60 },
            new MotionClass { Id = "T1", Type = "translate", Axis = "camera_right", SpeedPxPerFrame = 0.1, Frames = 180 },
            new MotionClass { Id = "T2", Type = "translate", Axis = "camera_right", SpeedPxPerFrame = 0.25, Frames = 180 },
            new MotionClass { Id = "T3", Type = "translate", Axis = "camera_right", SpeedPxPerFrame = 1.0, Frames = 180 },
            new MotionClass { Id = "Y1", Type = "yaw", Axis = "world_up", SpeedPxPerFrame = 0.25, Frames = 180 },
            new MotionClass { Id = "Y2", Type = "yaw", Axis = "world_up", SpeedPxPerFrame = 1.0, Frames = 180 },
        };

        public static bool TryMotionClass(string id, out MotionClass c)
        {
            foreach (var m in MotionClasses) if (m.Id == id) { c = m; return true; }
            c = default(MotionClass); return false;
        }

        public string Purpose = "M-003";
        /// <summary>M-004 motion classes to render from the first keyframe pose (purpose M-004 only); empty for ordinary paths.</summary>
        public List<string> MotionSet = new List<string>();
        public string SchemaId, PathId, Pillar, ViewpointId, Tile, ProfileId, DistrictId, TimeOfDay, Interpolation, DitherAnchor, Mode, Actors, FallbackOf;
        public double ViewpointE, ViewpointN, ViewpointHeadingDeg, DurationS, ReferenceDepthM = 600.0;
        public int Fps;
        public long Seed;
        public bool AltIsNavd88, SnapEnabled, NonWorld;
        public List<CaptureKeyframe> Keyframes = new List<CaptureKeyframe>();
        public List<CaptureShake> Shake = new List<CaptureShake>();
        public List<CaptureOutput> Outputs = new List<CaptureOutput>();
        public List<int> StillFrames = new List<int>();
        public List<string> Requires = new List<string>();
        /// <summary>Structures hidden from rendering (bid strings such as osm:w28824850): the observer's own tower at a deck viewpoint.</summary>
        public List<string> HideBids = new List<string>();
        /// <summary>Problems found while reading the structure (missing or mistyped members); Lint adds the rule checks.</summary>
        public List<string> ParseErrors = new List<string>();

        public int FrameCount => (int)Math.Round(DurationS * Fps);
        public bool IsStill => Mode == "still";
        public bool IsMotionSet => Purpose == "M-004";

        static readonly Regex IdRx = new Regex("^[a-z0-9_]+$"), TileRx = new Regex("^L0_[0-9]+_[0-9]+$");

        public static CapturePath Parse(string json)
        {
            var p = new CapturePath();
            object root;
            try { root = GeoJson.Parse(json); } catch (Exception e) { p.ParseErrors.Add("L00 not valid JSON: " + e.Message); return p; }
            var o = GeoJson.Obj(root);
            if (o == null) { p.ParseErrors.Add("L00 root is not an object"); return p; }
            p.SchemaId = GeoJson.Str(o, "schema"); p.PathId = GeoJson.Str(o, "path_id"); p.Pillar = GeoJson.Str(o, "pillar");
            p.ProfileId = GeoJson.Str(o, "profile_id"); p.DistrictId = GeoJson.Str(o, "district_id"); p.TimeOfDay = GeoJson.Str(o, "time_of_day");
            p.Interpolation = GeoJson.Str(o, "interpolation"); p.Mode = GeoJson.Str(o, "mode"); p.Actors = GeoJson.Str(o, "actors"); p.FallbackOf = GeoJson.Str(o, "fallback_of");
            p.Fps = (int)GeoJson.Num(o, "fps", -1); p.DurationS = GeoJson.Num(o, "duration_s", double.NaN); p.Seed = (long)GeoJson.Num(o, "seed", -1);
            p.NonWorld = GeoJson.Bool(o, "non_world");
            string purpose = GeoJson.Str(o, "purpose"); if (purpose != null) p.Purpose = purpose;
            var ms = GeoJson.Obj(GeoJson.Get(o, "motion_set"));
            if (ms != null)
            {
                var cl = GeoJson.Arr(GeoJson.Get(ms, "classes"));
                if (cl == null) p.ParseErrors.Add("L17 motion_set.classes must be a list");
                else foreach (var x in cl) { if (x is string cs) p.MotionSet.Add(cs); else p.ParseErrors.Add("L17 motion_set.classes entries must be strings"); }
            }
            else if (GeoJson.Get(o, "motion_set") != null) p.ParseErrors.Add("L17 motion_set must be an object");
            double rd = GeoJson.Num(o, "reference_depth_m", double.NaN); if (!double.IsNaN(rd)) p.ReferenceDepthM = rd;
            var vp = GeoJson.Obj(GeoJson.Get(o, "viewpoint"));
            if (vp == null) p.ParseErrors.Add("L14 viewpoint missing");
            else
            {
                p.ViewpointId = GeoJson.Str(vp, "id"); p.Tile = GeoJson.Str(vp, "tile"); p.ViewpointHeadingDeg = GeoJson.Num(vp, "heading_deg", double.NaN);
                var b = GeoJson.Arr(GeoJson.Get(vp, "baytm"));
                if (b == null || b.Count != 2 || !(b[0] is double) || !(b[1] is double)) p.ParseErrors.Add("L14 viewpoint.baytm must be [east, north]");
                else { p.ViewpointE = (double)b[0]; p.ViewpointN = (double)b[1]; }
            }
            var snap = GeoJson.Obj(GeoJson.Get(o, "snap"));
            p.SnapEnabled = snap != null && GeoJson.Bool(snap, "enabled"); p.DitherAnchor = snap != null ? GeoJson.Str(snap, "dither_anchor") : null;
            var kfs = GeoJson.Arr(GeoJson.Get(o, "keyframes"));
            if (kfs == null) p.ParseErrors.Add("L05 keyframes missing");
            else
            {
                int navd = 0, agl = 0;
                for (int i = 0; i < kfs.Count; i++)
                {
                    var k = GeoJson.Obj(kfs[i]); if (k == null) { p.ParseErrors.Add("L10 keyframe " + i + " is not an object"); continue; }
                    var pos = GeoJson.Arr(GeoJson.Get(k, "pos_baytm"));
                    bool hasAgl = GeoJson.Get(k, "alt_m") is double, hasNavd = GeoJson.Get(k, "alt_navd88") is double;
                    var kf = new CaptureKeyframe { T = GeoJson.Num(k, "t_s", double.NaN), Yaw = GeoJson.Num(k, "yaw_deg", double.NaN), Pitch = GeoJson.Num(k, "pitch_deg", double.NaN), Hfov = GeoJson.Num(k, "hfov_deg", double.NaN) };
                    if (pos == null || pos.Count != 2 || !(pos[0] is double) || !(pos[1] is double)) p.ParseErrors.Add("L10 keyframe " + i + ": pos_baytm must be [east, north]"); else { kf.E = (double)pos[0]; kf.N = (double)pos[1]; }
                    if (hasAgl == hasNavd) p.ParseErrors.Add("L10 keyframe " + i + ": exactly one of alt_m (above ground) and alt_navd88 is required");
                    else { kf.Alt = hasAgl ? GeoJson.Num(k, "alt_m") : GeoJson.Num(k, "alt_navd88"); if (hasNavd) navd++; else agl++; }
                    if (double.IsNaN(kf.T) || double.IsNaN(kf.Yaw) || double.IsNaN(kf.Pitch)) p.ParseErrors.Add("L10 keyframe " + i + ": t_s, yaw_deg and pitch_deg are required numbers");
                    p.Keyframes.Add(kf);
                }
                if (navd > 0 && agl > 0) p.ParseErrors.Add("L10 keyframes mix alt_m and alt_navd88");
                p.AltIsNavd88 = navd > 0;
            }
            var sh = GeoJson.Arr(GeoJson.Get(o, "shake"));
            if (sh != null)
                foreach (var so in sh)
                {
                    var s = GeoJson.Obj(so);
                    if (s == null) { p.ParseErrors.Add("L13 shake entry is not an object"); continue; }
                    p.Shake.Add(new CaptureShake { T = GeoJson.Num(s, "t_s", double.NaN), AmpPx = GeoJson.Num(s, "amp_internal_px", double.NaN), DecayS = GeoJson.Num(s, "decay_s", double.NaN) });
                }
            var outs = GeoJson.Arr(GeoJson.Get(o, "outputs"));
            if (outs == null) p.ParseErrors.Add("L08 outputs missing");
            else
                foreach (var oo in outs)
                {
                    var q = GeoJson.Obj(oo); var iw = GeoJson.Arr(GeoJson.Get(q, "internal_wh")); var ow = GeoJson.Arr(GeoJson.Get(q, "output_wh"));
                    if (q == null || iw == null || ow == null || iw.Count != 2 || ow.Count != 2 || !(iw[0] is double) || !(iw[1] is double) || !(ow[0] is double) || !(ow[1] is double) || !(GeoJson.Get(q, "scale") is double))
                    { p.ParseErrors.Add("L08 output needs internal_wh, output_wh (two numbers each) and scale"); continue; }
                    p.Outputs.Add(new CaptureOutput { InternalW = (int)(double)iw[0], InternalH = (int)(double)iw[1], OutputW = (int)(double)ow[0], OutputH = (int)(double)ow[1], Scale = (int)GeoJson.Num(q, "scale") });
                }
            var sf = GeoJson.Arr(GeoJson.Get(o, "still_frames"));
            if (sf != null) foreach (var x in sf) { if (x is double d && d == Math.Floor(d)) p.StillFrames.Add((int)d); else p.ParseErrors.Add("L09 still_frames entries must be integers"); }
            var rq = GeoJson.Arr(GeoJson.Get(o, "requires"));
            if (rq != null) foreach (var x in rq) { if (x is string s) p.Requires.Add(s); else p.ParseErrors.Add("L11 requires entries must be strings"); }
            var hb = GeoJson.Arr(GeoJson.Get(o, "hide_bids"));
            if (hb != null) foreach (var x in hb) { if (x is string s) p.HideBids.Add(s); else p.ParseErrors.Add("L11 hide_bids entries must be strings"); }
            return p;
        }

        /// <summary>Every rule of capture_plan.md section 3; empty list = clean. Rule ids match the Python lint.</summary>
        public List<string> Lint()
        {
            var e = new List<string>(ParseErrors);
            if (SchemaId != Schema) e.Add("L01 schema must be " + Schema);
            if (string.IsNullOrEmpty(PathId) || !IdRx.IsMatch(PathId)) e.Add("L02 path_id must match [a-z0-9_]+");
            if (Fps != 30 && Fps != 60) e.Add("L03 fps must be 30 or 60");
            if (double.IsNaN(DurationS) || DurationS <= 0) e.Add("L04 duration_s must be > 0");
            else if (Fps > 0 && Math.Abs(DurationS * Fps - Math.Round(DurationS * Fps)) > 1e-6) e.Add("L04 duration_s * fps must be an integer");
            if (Array.IndexOf(Interpolations, Interpolation) < 0) e.Add("L06 interpolation must be linear, catmull_rom or ease_in_out");
            int minKeys = Interpolation == "catmull_rom" ? 4 : 2;
            if (Keyframes.Count < minKeys) e.Add("L05 needs at least " + minKeys + " keyframes for " + Interpolation);
            else
            {
                if (Math.Abs(Keyframes[0].T) > 1e-9) e.Add("L05 first keyframe t_s must be 0");
                if (!double.IsNaN(DurationS) && Math.Abs(Keyframes[Keyframes.Count - 1].T - DurationS) > 1e-9) e.Add("L05 last keyframe t_s must equal duration_s");
                for (int i = 1; i < Keyframes.Count; i++) if (!(Keyframes[i].T > Keyframes[i - 1].T)) { e.Add("L05 keyframes must be strictly sorted by t_s (at " + i + ")"); break; }
            }
            foreach (var k in Keyframes) if (!double.IsNaN(k.Pitch) && Math.Abs(k.Pitch) > 89.0) { e.Add("L10 pitch_deg must be within +-89"); break; }
            if (!SnapEnabled) e.Add("L07 snap.enabled must be true (unsnapped camera poses are not allowed)");
            if (!NonWorld && DitherAnchor != "direction") e.Add("L07 snap.dither_anchor must be direction");
            if (Mode != "clip" && Mode != "still") e.Add("L09 mode must be clip or still");
            if (Mode == "still")
            {
                if (StillFrames.Count == 0) e.Add("L09 still mode needs still_frames");
                for (int i = 0; i < StillFrames.Count; i++)
                {
                    if (StillFrames[i] < 0 || StillFrames[i] >= Math.Max(1, FrameCount)) { e.Add("L09 still_frames entry out of range: " + StillFrames[i]); break; }
                    if (i > 0 && StillFrames[i] <= StillFrames[i - 1]) { e.Add("L09 still_frames must be strictly increasing"); break; }
                }
            }
            if (Outputs.Count == 0) e.Add("L08 at least one output is required");
            foreach (var o in Outputs)
            {
                if (o.Scale < 1 || o.OutputW != o.InternalW * o.Scale || o.OutputH != o.InternalH * o.Scale) e.Add("L08 output_wh must equal internal_wh * integer scale (" + o.OutputW + "x" + o.OutputH + " vs " + o.InternalW + "x" + o.InternalH + " x" + o.Scale + ")");
                bool fhd = o.OutputW == 1920 && o.OutputH == 1080, uhd = o.OutputW == 3840 && o.OutputH == 2160;
                if (!fhd && !uhd) e.Add("L08 output must be 1920x1080 or 3840x2160");
                else if (uhd && Mode == "clip") e.Add("L08 3840x2160 is for stills only (clips are 1920x1080)");
            }
            if (ProfileId == null || Array.IndexOf(KnownProfiles, ProfileId) < 0) e.Add("L15 profile_id must be one of the five palette profiles");
            foreach (var r in Requires) if (Array.IndexOf(KnownRequires, r) < 0) e.Add("L11 unknown requires tag '" + r + "'");
            if (FallbackOf != null && !IdRx.IsMatch(FallbackOf)) e.Add("L11 fallback_of must be a path_id");
            if (Seed < 0) e.Add("L12 seed must be a non-negative integer");
            foreach (var s in Shake)
                if (double.IsNaN(s.T) || s.T < 0 || s.T > DurationS || double.IsNaN(s.AmpPx) || s.AmpPx < 0 || double.IsNaN(s.DecayS) || s.DecayS <= 0) { e.Add("L13 shake needs 0 <= t_s <= duration_s, amp_internal_px >= 0, decay_s > 0"); break; }
            if (Tile == null || !TileRx.IsMatch(Tile)) e.Add("L14 viewpoint.tile must look like L0_30_36");
            if (string.IsNullOrEmpty(ViewpointId)) e.Add("L14 viewpoint.id is required");
            if (string.IsNullOrEmpty(DistrictId)) e.Add("L14 district_id is required");
            if (Array.IndexOf(Purposes, Purpose) < 0) e.Add("L17 purpose must be M-003 or M-004");
            else if (IsMotionSet)
            {
                if (MotionSet.Count == 0) e.Add("L17 purpose M-004 needs motion_set.classes");
                foreach (var c in MotionSet) if (!TryMotionClass(c, out _)) { e.Add("L17 unknown motion class '" + c + "'"); break; }
                if (new HashSet<string>(MotionSet).Count != MotionSet.Count) e.Add("L17 motion_set.classes has duplicates");
                if (Mode != "clip") e.Add("L17 an M-004 motion set is a clip");
                int need = 0; foreach (var c in MotionSet) if (TryMotionClass(c, out var mc)) need = Math.Max(need, mc.Frames);
                if (Fps > 0 && FrameCount < need) e.Add("L17 duration_s * fps must cover the longest class (" + need + " frames)");
            }
            else if (MotionSet.Count > 0) e.Add("L17 motion_set is only for purpose M-004");
            return e;
        }

        /// <summary>First missing capability tag, or null when the path can be rendered with what exists. Tags in <paramref name="available"/> are satisfied.</summary>
        public string FirstBlocker(ICollection<string> available)
        {
            foreach (var r in Requires) if (available == null || !available.Contains(r)) return r;
            return null;
        }

        public static string TileKey(double e, double n, out int ix, out int iz) { ix = (int)Math.Floor(e / 2048.0); iz = (int)Math.Floor(n / 2048.0); return "L0_" + ix.ToString(CultureInfo.InvariantCulture) + "_" + iz.ToString(CultureInfo.InvariantCulture); }
    }
}
