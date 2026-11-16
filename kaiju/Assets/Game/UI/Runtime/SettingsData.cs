using System;
using System.Collections.Generic;
using System.IO;

namespace Game.UI
{
    public enum SettingsLoadStatus
    {
        Ok = 0,
        /// <summary>No file: defaults.</summary>
        Missing = 1,
        /// <summary>File unreadable or not JSON: defaults (caller may keep a .bad copy).</summary>
        Corrupt = 2,
        /// <summary>Older schema upgraded in memory; save to persist.</summary>
        Migrated = 3,
        /// <summary>Written by a newer build: known fields read, <see cref="SettingsLoadResult.ShouldNotOverwrite"/> set.</summary>
        NewerVersion = 4,
    }

    public sealed class SettingsLoadResult
    {
        public SettingsData Data;
        public SettingsLoadStatus Status;
        public int FromVersion;
        public bool ShouldNotOverwrite { get { return Status == SettingsLoadStatus.NewerVersion; } }
    }

    /// <summary>
    /// The persisted player settings, `settings@v1` (R-122 4.5; T-153 data layer). Pure data, never read by the simulation or the replay
    /// (M-NEW-g). Integers only (percent), so JSON round-trips byte-identically. Behind flag ui.settings (default off); with the flag off nothing loads it.
    /// </summary>
    public sealed class SettingsData
    {
        public const int CurrentVersion = 1;
        public const string SchemaName = "settings@v1";

        // Graphics
        public string PixelProfile = "lavender_peach";
        public int InternalScale;          // 0 = auto, else 4 or 6
        public bool Fullscreen = true;
        public bool Vsync = true;
        public int ResolutionW = 1920, ResolutionH = 1080;
        // Input
        public int DeadzonePct = 15;
        public bool InvertLookY;
        public int SensitivityPct = 100;
        public bool HoldToToggle;
        public bool PadGlyphs;             // false = keyboard glyphs
        public List<RebindOverride> Rebinds = new List<RebindOverride>();
        // Audio (0..100)
        public int MasterPct = 100, MusicPct = 80, EffectsPct = 100, StructurePct = 100, FootfallPct = 100;
        // Accessibility
        public TextScaleStep TextScale = TextScaleStep.Small;
        public bool HighContrast, ReduceBlink, Subtitles;
        public int ShakePct = 100, FlashPct = 100;
        // Online
        public string PlayerName = "";

        public static SettingsData Defaults() { return new SettingsData(); }

        public AccessibilitySettings ToAccessibility()
        {
            return new AccessibilitySettings(TextScale, HighContrast, ReduceBlink, ShakePct / 100f, FlashPct / 100f);
        }

        static int Pct(int v) { return v < 0 ? 0 : (v > 100 ? 100 : v); }

        /// <summary>Clamps every field to its legal range (also run after load).</summary>
        public void Sanitize()
        {
            if (HudPalette.Find(PixelProfile) == null) PixelProfile = "lavender_peach";
            if (InternalScale != 0 && InternalScale != 4 && InternalScale != 6) InternalScale = 0;
            if (ResolutionW < 640 || ResolutionW > 16384 || ResolutionH < 360 || ResolutionH > 16384) { ResolutionW = 1920; ResolutionH = 1080; }
            DeadzonePct = Math.Max(0, Math.Min(50, DeadzonePct));
            SensitivityPct = Math.Max(10, Math.Min(300, SensitivityPct));
            MasterPct = Pct(MasterPct); MusicPct = Pct(MusicPct); EffectsPct = Pct(EffectsPct); StructurePct = Pct(StructurePct); FootfallPct = Pct(FootfallPct);
            ShakePct = Pct(ShakePct); FlashPct = Pct(FlashPct);
            if (!Enum.IsDefined(typeof(TextScaleStep), TextScale)) TextScale = TextScaleStep.Small;
            if (PlayerName == null) PlayerName = "";
            if (PlayerName.Length > 24) PlayerName = PlayerName.Substring(0, 24);
            if (Rebinds == null) Rebinds = new List<RebindOverride>();
        }

        // ---- JSON ----

        public string ToJson()
        {
            Sanitize();
            var root = new SortedDictionary<string, object>(StringComparer.Ordinal);
            root["schema"] = SchemaName;
            root["version"] = (double)CurrentVersion;
            root["graphics"] = Obj("profile", PixelProfile, "internal_scale", InternalScale, "fullscreen", Fullscreen, "vsync", Vsync, "res_w", ResolutionW, "res_h", ResolutionH);
            var rb = new List<object>();
            foreach (var o in Rebinds) rb.Add(Obj("action", o.ActionId, "index", o.BindingIndex, "path", o.Path));
            var input = Obj("deadzone_pct", DeadzonePct, "invert_y", InvertLookY, "sensitivity_pct", SensitivityPct, "hold_to_toggle", HoldToToggle, "pad_glyphs", PadGlyphs);
            input["rebinds"] = rb;
            root["input"] = input;
            root["audio"] = Obj("master", MasterPct, "music", MusicPct, "effects", EffectsPct, "structure", StructurePct, "footfall", FootfallPct);
            root["accessibility"] = Obj("text_scale", (int)TextScale, "high_contrast", HighContrast, "reduce_blink", ReduceBlink, "subtitles", Subtitles, "shake_pct", ShakePct, "flash_pct", FlashPct);
            root["online"] = Obj("player_name", PlayerName);
            return MiniJson.Write(root);
        }

        static SortedDictionary<string, object> Obj(params object[] kv)
        {
            var d = new SortedDictionary<string, object>(StringComparer.Ordinal);
            for (int i = 0; i + 1 < kv.Length; i += 2)
            {
                object v = kv[i + 1];
                if (v is int) v = (double)(int)v;
                d[(string)kv[i]] = v;
            }
            return d;
        }

        public static SettingsLoadResult FromJson(string json)
        {
            var res = new SettingsLoadResult { Data = Defaults(), Status = SettingsLoadStatus.Ok, FromVersion = CurrentVersion };
            object root;
            if (string.IsNullOrWhiteSpace(json)) { res.Status = SettingsLoadStatus.Missing; return res; }
            if (!MiniJson.TryParse(json, out root) || MiniJson.Obj(root) == null) { res.Status = SettingsLoadStatus.Corrupt; return res; }
            var o = MiniJson.Obj(root);
            int ver = MiniJson.Int(o, "version", 0);
            res.FromVersion = ver;
            if (ver > CurrentVersion) res.Status = SettingsLoadStatus.NewerVersion;
            else if (ver < CurrentVersion) { o = Migrate(o, ver); res.Status = SettingsLoadStatus.Migrated; }
            ReadV1(o, res.Data);
            res.Data.Sanitize();
            return res;
        }

        /// <summary>
        /// Migration stub. v0 means "no version field" (a hand-edited or pre-release file): it is treated as v1-shaped, so the step is the identity.
        /// Real migrations go here as `if (from &lt; 2) { ... }` blocks that rewrite the tree to the next schema; each is tested with a fixture.
        /// </summary>
        public static IDictionary<string, object> Migrate(IDictionary<string, object> tree, int from)
        {
            // from == 0 -> 1: identity (nothing was ever shipped under another shape).
            return tree;
        }

        static void ReadV1(IDictionary<string, object> o, SettingsData d)
        {
            var g = MiniJson.Obj(MiniJson.Get(o, "graphics"));
            d.PixelProfile = MiniJson.Str(g, "profile", d.PixelProfile);
            d.InternalScale = MiniJson.Int(g, "internal_scale", d.InternalScale);
            d.Fullscreen = MiniJson.Bool(g, "fullscreen", d.Fullscreen);
            d.Vsync = MiniJson.Bool(g, "vsync", d.Vsync);
            d.ResolutionW = MiniJson.Int(g, "res_w", d.ResolutionW);
            d.ResolutionH = MiniJson.Int(g, "res_h", d.ResolutionH);
            var i = MiniJson.Obj(MiniJson.Get(o, "input"));
            d.DeadzonePct = MiniJson.Int(i, "deadzone_pct", d.DeadzonePct);
            d.InvertLookY = MiniJson.Bool(i, "invert_y", d.InvertLookY);
            d.SensitivityPct = MiniJson.Int(i, "sensitivity_pct", d.SensitivityPct);
            d.HoldToToggle = MiniJson.Bool(i, "hold_to_toggle", d.HoldToToggle);
            d.PadGlyphs = MiniJson.Bool(i, "pad_glyphs", d.PadGlyphs);
            var rb = MiniJson.Arr(MiniJson.Get(i, "rebinds"));
            if (rb != null)
                for (int k = 0; k < rb.Count; k++)
                {
                    var e = MiniJson.Obj(rb[k]);
                    string a = MiniJson.Str(e, "action", null), p = MiniJson.Str(e, "path", null);
                    if (a != null && p != null) d.Rebinds.Add(new RebindOverride { ActionId = a, BindingIndex = MiniJson.Int(e, "index", 0), Path = p });
                }
            var au = MiniJson.Obj(MiniJson.Get(o, "audio"));
            d.MasterPct = MiniJson.Int(au, "master", d.MasterPct);
            d.MusicPct = MiniJson.Int(au, "music", d.MusicPct);
            d.EffectsPct = MiniJson.Int(au, "effects", d.EffectsPct);
            d.StructurePct = MiniJson.Int(au, "structure", d.StructurePct);
            d.FootfallPct = MiniJson.Int(au, "footfall", d.FootfallPct);
            var ac = MiniJson.Obj(MiniJson.Get(o, "accessibility"));
            d.TextScale = (TextScaleStep)MiniJson.Int(ac, "text_scale", (int)d.TextScale);
            d.HighContrast = MiniJson.Bool(ac, "high_contrast", d.HighContrast);
            d.ReduceBlink = MiniJson.Bool(ac, "reduce_blink", d.ReduceBlink);
            d.Subtitles = MiniJson.Bool(ac, "subtitles", d.Subtitles);
            d.ShakePct = MiniJson.Int(ac, "shake_pct", d.ShakePct);
            d.FlashPct = MiniJson.Int(ac, "flash_pct", d.FlashPct);
            d.PlayerName = MiniJson.Str(MiniJson.Obj(MiniJson.Get(o, "online")), "player_name", d.PlayerName);
        }
    }

    /// <summary>File persistence for <see cref="SettingsData"/>: atomic write (temp + replace), corrupt files are kept as `.bad` and defaults returned.</summary>
    public static class SettingsStore
    {
        public static SettingsLoadResult Load(string path)
        {
            if (!File.Exists(path)) return new SettingsLoadResult { Data = SettingsData.Defaults(), Status = SettingsLoadStatus.Missing, FromVersion = 0 };
            string text;
            try { text = File.ReadAllText(path); }
            catch (IOException) { return new SettingsLoadResult { Data = SettingsData.Defaults(), Status = SettingsLoadStatus.Corrupt }; }
            var r = SettingsData.FromJson(text);
            if (r.Status == SettingsLoadStatus.Corrupt)
            {
                try { File.Copy(path, path + ".bad", true); } catch (IOException) { }
            }
            return r;
        }

        /// <summary>Writes atomically. Refuses (returns false) when <paramref name="loaded"/> says the file came from a newer build.</summary>
        public static bool Save(string path, SettingsData data, SettingsLoadResult loaded)
        {
            if (loaded != null && loaded.ShouldNotOverwrite) return false;
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, data.ToJson());
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
            return true;
        }
    }
}
