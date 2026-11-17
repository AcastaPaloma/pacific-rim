// T-154c: the 7x10 medium font (glyph set, overlap, deterministic raster) and the real-health limits contract.
using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;

namespace Game.UI.Tests
{
    public class HudFontMediumTests
    {
        static HudLayout Layout() { return HudLayout.Parse(DataFiles.Read("Assets/Game/Config/UI/hud_layout.json")); }

        [Test]
        public void Covers_every_character_the_small_font_has_and_every_hud_string()
        {
            Assert.AreEqual(HudFont.Charset(), HudFontMedium.Charset());
            foreach (char c in "PWRHETAMO0123456789/:%.-! +?*KAIJU DOWN LOW RELOADING") Assert.IsTrue(HudFontMedium.HasGlyph(c), c.ToString());
            foreach (var e in Layout().Elements) if (e.Text != null) foreach (char c in e.Text) Assert.IsTrue(HudFontMedium.HasGlyph(c), e.Id + " " + c);
            foreach (char c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ") Assert.IsTrue(HudFontMedium.HasGlyph(c));
        }

        [Test]
        public void Glyphs_are_distinct_nonblank_inside_the_7x10_cell_and_use_full_height()
        {
            var seen = new Dictionary<string, char>();
            foreach (char c in HudFontMedium.Charset())
            {
                var rows = HudFontMedium.Rows(c);
                Assert.AreEqual(HudFontMedium.GlyphH, rows.Length);
                foreach (byte r in rows) Assert.Less(r, 1 << HudFontMedium.GlyphW, c + " row exceeds 7 columns");
                if (c == ' ') continue;
                string key = Convert.ToBase64String(rows);
                Assert.IsFalse(seen.ContainsKey(key), c + " duplicates " + (seen.ContainsKey(key) ? seen[key].ToString() : ""));
                seen[key] = c;
                int lit = 0; for (int y = 0; y < 10; y++) for (int x = 0; x < 7; x++) if (HudFontMedium.Pixel(c, x, y)) lit++;
                Assert.Greater(lit, 3, c.ToString());
            }
            foreach (char c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789")
            {
                Assert.AreNotEqual(0, HudFontMedium.Rows(c)[0], c + " top row blank");
                Assert.AreNotEqual(0, HudFontMedium.Rows(c)[9], c + " bottom row blank");
            }
            Assert.IsFalse(HudFontMedium.Pixel('A', -1, 0)); Assert.IsFalse(HudFontMedium.Pixel('A', 7, 0)); Assert.IsFalse(HudFontMedium.Pixel('A', 0, 10));
        }

        [Test]
        public void Rendered_strings_never_overlap_and_keep_a_one_pixel_gap()
        {
            const string text = "MECH 100% KAIJU 7/20 ARMOUR LOW!";
            var seen = new HashSet<long>();
            for (int i = 0; i < text.Length; i++)
                for (int y = 0; y < 10; y++)
                    for (int x = 0; x < 7; x++)
                        if (HudFontMedium.Pixel(text[i], x, y))
                            Assert.IsTrue(seen.Add((long)(i * HudFontMedium.Advance + x) * 100 + y), "pixel drawn twice at glyph " + i);
            for (int i = 0; i < text.Length; i++)
                for (int y = 0; y < 10; y++)
                    Assert.IsFalse(HudFontMedium.Pixel(text[i], 7, y), "column 7 is the gap");
            Assert.AreEqual(8, HudFont.AdvanceOf(TextScaleStep.Medium)); Assert.AreEqual(6, HudFont.AdvanceOf(TextScaleStep.Small));
            Assert.AreEqual(39, HudFont.MeasureWidth("ABCDE", 1, TextScaleStep.Medium)); // 5 * 8 - 1
            Assert.AreEqual(29, HudFont.MeasureWidth("ABCDE", 1, TextScaleStep.Small));
            Assert.AreEqual(10, HudFont.HeightPx(1, TextScaleStep.Medium)); Assert.AreEqual(7, HudFont.HeightPx(1, TextScaleStep.Small));
        }

        static string Ascii(string s)
        {
            var sb = new StringBuilder();
            for (int y = 0; y < 10; y++)
            {
                for (int i = 0; i < s.Length; i++)
                {
                    for (int x = 0; x < 7; x++) sb.Append(HudFontMedium.Pixel(s[i], x, y) ? '#' : '.');
                    if (i < s.Length - 1) sb.Append('.');
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        [Test]
        public void Glyph_snapshot_is_stable()
        {
            const string expected =
                ".#####..######..#.....#\n" +
                "#.....#.#.....#.##...##\n" +
                "#.....#.#.....#.#.#.#.#\n" +
                "#.....#.#.....#.#.#.#.#\n" +
                "#.....#.######..#..#..#\n" +
                "#.....#.#.....#.#.....#\n" +
                "#.....#.#.....#.#.....#\n" +
                "#.....#.#.....#.#.....#\n" +
                "#.....#.#.....#.#.....#\n" +
                ".#####..######..#.....#\n";
            Assert.AreEqual(expected, Ascii("OBM"));
        }

        static ulong Fnv(byte[] b) { ulong h = 14695981039346656037UL; foreach (byte x in b) { h ^= x; h *= 1099511628211UL; } return h; }

        static HudFrameData Frame()
        {
            return new HudFrameData
            {
                Visible = true, HasLimits = true, Limits = MechLimitsView.AmmoOnly(17, 20),
                HasReload = true, Reloading = true, ReloadFrac = 0.4f, HasMechHp = true, MechHp = 0.72f, HasKaijuHp = true, KaijuHp = 0.35f,
            };
        }

        static HudRaster Render(TextScaleStep step)
        {
            var r = new HudRaster();
            r.Render(Layout(), Frame(), HudPalette.Find("warm_amber_haze"), new AccessibilitySettings(step, false, false, 1f, 1f));
            return r;
        }

        [Test]
        public void Medium_raster_is_deterministic_differs_from_small_and_is_not_a_placeholder()
        {
            var a = Render(TextScaleStep.Medium); var b = Render(TextScaleStep.Medium); var s = Render(TextScaleStep.Small);
            Assert.AreEqual(a.Rgba, b.Rgba);
            Assert.AreNotEqual(Fnv(a.Rgba), Fnv(s.Rgba));
            Assert.IsFalse(a.MediumFontPlaceholder);
            Assert.AreEqual(0, a.ClippedStrings);
            Assert.AreEqual(ExpectedMediumHash, Fnv(a.Rgba), "raster snapshot changed: Fnv=" + Fnv(a.Rgba));
        }

        const ulong ExpectedMediumHash = 10186844960933536465UL; // FNV-1a of the 480x270 RGBA frame, warm_amber_haze, frame above

        [Test]
        public void Medium_raster_stays_on_the_six_profile_colours()
        {
            var pal = HudPalette.Find("warm_amber_haze");
            var ok = new HashSet<int> { 0 };
            Func<Rgb, int> key = c => (c.R << 16) | (c.G << 8) | c.B;
            ok.Add(key(pal.Panel)); ok.Add(key(pal.Text)); ok.Add(key(pal.Outline)); foreach (var f in pal.Fills) ok.Add(key(f));
            var r = Render(TextScaleStep.Medium);
            for (int i = 0; i < r.Rgba.Length; i += 4)
                if (r.Rgba[i + 3] != 0) Assert.IsTrue(ok.Contains((r.Rgba[i] << 16) | (r.Rgba[i + 1] << 8) | r.Rgba[i + 2]), "off-palette pixel at " + i / 4);
        }

        // ---- real health in the limits contract ----
        [Test]
        public void Limits_view_carries_health_and_old_constructors_do_not()
        {
            var ammo = MechLimitsView.AmmoOnly(3, 10);
            Assert.IsFalse(ammo.Has(LimitsAvailability.Health)); Assert.AreEqual(0f, ammo.HealthFrac);
            var h = ammo.WithHealth(0.5f);
            Assert.IsTrue(h.Has(LimitsAvailability.Health)); Assert.IsTrue(h.Has(LimitsAvailability.Ammo));
            Assert.AreEqual(0.5f, h.HealthFrac); Assert.AreEqual(3, h.AmmoCount); Assert.IsTrue(h.IsValid);
            Assert.AreEqual(1f, ammo.WithHealth(7f).HealthFrac); Assert.AreEqual(0f, ammo.WithHealth(float.NaN).HealthFrac);
        }

        [Test]
        public void Health_provider_adds_to_inner_view_or_stands_alone()
        {
            var src = new DelegateHealthSource((int id, out float f) => { f = id == 7 ? 0.25f : 0f; return id == 7; });
            var inner = new CannonAmmoProvider(new DelegateAmmoSource((int id, out CannonAmmoState st) => { st = new CannonAmmoState(4, 8, false, 1f); return id == 7; }));
            var p = new HealthLimitsProvider(inner, src);
            Assert.IsTrue(p.TryGetLimits(7, out var v));
            Assert.AreEqual(4, v.AmmoCount); Assert.AreEqual(0.25f, v.HealthFrac); Assert.IsTrue(v.Has(LimitsAvailability.Health | LimitsAvailability.Ammo));
            Assert.IsFalse(p.TryGetLimits(8, out _));
            Assert.IsTrue(new HealthLimitsProvider(null, src).TryGetLimits(7, out v)); Assert.IsFalse(v.Has(LimitsAvailability.Ammo));
            Assert.IsTrue(new HealthLimitsProvider(inner, null).TryGetLimits(7, out v)); Assert.IsFalse(v.Has(LimitsAvailability.Health));
        }

        [Test]
        public void Raster_uses_limits_health_for_the_mech_bar_unless_the_host_supplies_one()
        {
            var layout = Layout(); var pal = HudPalette.Find("warm_amber_haze"); var acc = AccessibilitySettings.Default;
            var onlyLimits = new HudFrameData { Visible = true, HasLimits = true, Limits = MechLimitsView.AmmoOnly(5, 10).WithHealth(0.5f) };
            var none = new HudFrameData { Visible = true, HasLimits = true, Limits = MechLimitsView.AmmoOnly(5, 10) };
            var a = new HudRaster(); a.Render(layout, onlyLimits, pal, acc);
            var b = new HudRaster(); b.Render(layout, none, pal, acc);
            Assert.AreNotEqual(a.Rgba, b.Rgba);
            var host = onlyLimits; host.HasMechHp = true; host.MechHp = 1f;
            var c = new HudRaster(); c.Render(layout, host, pal, acc);
            var d = onlyLimits; d.HasMechHp = true; d.MechHp = 1f; d.Limits = MechLimitsView.AmmoOnly(5, 10);
            var e = new HudRaster(); e.Render(layout, d, pal, acc);
            Assert.AreEqual(e.Rgba, c.Rgba, "host number wins over limits health");
        }
    }
}
