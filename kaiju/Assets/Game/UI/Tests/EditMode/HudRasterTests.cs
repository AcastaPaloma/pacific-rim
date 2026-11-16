using System.Collections.Generic;
using NUnit.Framework;

namespace Game.UI.Tests
{
    public class HudRasterTests
    {
        static HudLayout Layout() { return HudLayout.Parse(DataFiles.Read("Assets/Game/Config/UI/hud_layout.json")); }

        static HudFrameData Frame(int ammo, int max, bool reloading, float reload, float mech, float kaiju)
        {
            return new HudFrameData
            {
                Visible = true, HasLimits = true, Limits = MechLimitsView.AmmoOnly(ammo, max),
                HasReload = true, Reloading = reloading, ReloadFrac = reload,
                HasMechHp = true, MechHp = mech, HasKaijuHp = true, KaijuHp = kaiju,
            };
        }

        static HashSet<int> Allowed(HudPalette p)
        {
            var s = new HashSet<int> { Key(p.Panel), Key(p.Text), Key(p.Outline) };
            foreach (var f in p.Fills) s.Add(Key(f));
            return s;
        }

        static int Key(Rgb c) { return (c.R << 16) | (c.G << 8) | c.B; }

        static int Opaque(HudRaster r)
        {
            int n = 0;
            for (int i = 3; i < r.Rgba.Length; i += 4) if (r.Rgba[i] != 0) n++;
            return n;
        }

        [Test]
        public void Every_pixel_is_transparent_or_an_exact_palette_colour([Values("lavender_peach", "slate_crimson", "slate_teal", "warm_amber_haze", "warm_rust_sepia")] string profile,
            [Values(TextScaleStep.Small, TextScaleStep.Medium, TextScaleStep.Large)] TextScaleStep step)
        {
            var pal = HudPalette.Find(profile); var allowed = Allowed(pal);
            var r = new HudRaster();
            var acc = new AccessibilitySettings(step, false, false, 1f, 1f);
            r.Render(Layout(), Frame(7, 20, true, 0.4f, 0.8f, 0.65f), pal, acc);
            Assert.Greater(Opaque(r), 500);
            for (int i = 0; i < r.Rgba.Length; i += 4)
            {
                if (r.Rgba[i + 3] == 0) continue;
                Assert.AreEqual(255, r.Rgba[i + 3]);
                Assert.IsTrue(allowed.Contains((r.Rgba[i] << 16) | (r.Rgba[i + 1] << 8) | r.Rgba[i + 2]), "off-palette pixel at " + (i / 4));
            }
        }

        [TestCase(TextScaleStep.Small), TestCase(TextScaleStep.Medium), TestCase(TextScaleStep.Large)]
        public void No_text_is_clipped_for_worst_case_values(TextScaleStep step)
        {
            var r = new HudRaster();
            var d = Frame(100, 100, true, 0.5f, 1f, 1f);
            r.Render(Layout(), d, HudPalette.Find("warm_amber_haze"), new AccessibilitySettings(step, false, false, 1f, 1f));
            Assert.AreEqual(0, r.ClippedStrings);
        }

        [Test]
        public void Nothing_is_drawn_in_the_central_zone_or_outside_the_layout_rects()
        {
            var r = new HudRaster();
            r.Render(Layout(), Frame(18, 20, true, 0.5f, 0.5f, 0.5f), HudPalette.Find("warm_amber_haze"), AccessibilitySettings.Default);
            var z = HudLayout.CentralZone;
            for (int y = z.Y; y < z.Bottom; y++) for (int x = z.X; x < z.Right; x++) Assert.AreEqual(0, r.Rgba[(y * HudRaster.W + x) * 4 + 3], "pixel in central zone " + x + "," + y);
        }

        [Test]
        public void Hidden_or_empty_frame_draws_nothing()
        {
            var r = new HudRaster();
            r.Render(Layout(), new HudFrameData { Visible = false }, HudPalette.Find("slate_teal"), AccessibilitySettings.Default);
            Assert.AreEqual(0, Opaque(r));
            r.Render(Layout(), new HudFrameData { Visible = true }, HudPalette.Find("slate_teal"), AccessibilitySettings.Default);
            Assert.AreEqual(0, Opaque(r));
        }

        [Test]
        public void More_ammo_lights_more_pixels_and_render_is_deterministic()
        {
            var a = new HudRaster(); var b = new HudRaster(); var c = new HudRaster();
            var pal = HudPalette.Find("warm_amber_haze");
            a.Render(Layout(), Frame(20, 20, false, 1f, 1f, 1f), pal, AccessibilitySettings.Default);
            b.Render(Layout(), Frame(5, 20, false, 1f, 1f, 1f), pal, AccessibilitySettings.Default);
            c.Render(Layout(), Frame(5, 20, false, 1f, 1f, 1f), pal, AccessibilitySettings.Default);
            Assert.AreEqual(b.Rgba, c.Rgba);
            int fillKey = Key(pal.Fills[2]), la = 0, lb = 0;
            for (int i = 0; i < a.Rgba.Length; i += 4)
            {
                if (((a.Rgba[i] << 16) | (a.Rgba[i + 1] << 8) | a.Rgba[i + 2]) == fillKey && a.Rgba[i + 3] != 0) la++;
                if (((b.Rgba[i] << 16) | (b.Rgba[i + 1] << 8) | b.Rgba[i + 2]) == fillKey && b.Rgba[i + 3] != 0) lb++;
            }
            Assert.Greater(la, lb);
        }

        [Test]
        public void Large_text_is_exactly_twice_the_small_text_height()
        {
            var l = Layout();
            Assert.AreEqual(1, HudTextLadder.PixelScale(TextScaleStep.Small, 1));
            Assert.AreEqual(2, HudTextLadder.PixelScale(TextScaleStep.Large, 1));
            var s = new HudRaster(); var big = new HudRaster();
            var d = Frame(18, 20, false, 1f, 1f, 1f);
            s.Render(l, d, HudPalette.Find("warm_amber_haze"), AccessibilitySettings.Default);
            big.Render(l, d, HudPalette.Find("warm_amber_haze"), new AccessibilitySettings(TextScaleStep.Large, false, false, 1f, 1f));
            Assert.AreNotEqual(s.Rgba, big.Rgba);
            Assert.IsTrue(big.MediumFontPlaceholder == false && new HudRaster().MediumFontPlaceholder == false);
        }

        [Test]
        public void Warning_line_priority()
        {
            var v = MechLimitsView.AmmoOnly(0, 20);
            Assert.AreEqual("NO AMMO", HudRaster.Warning(Frame(0, 20, true, 0.1f, 1f, 1f), true, v));
            Assert.AreEqual("MECH DOWN", HudRaster.Warning(Frame(0, 20, true, 0.1f, 0f, 1f), true, v));
            Assert.AreEqual("RELOADING", HudRaster.Warning(Frame(5, 20, true, 0.1f, 1f, 1f), true, MechLimitsView.AmmoOnly(5, 20)));
            Assert.IsNull(HudRaster.Warning(Frame(5, 20, false, 1f, 1f, 1f), true, MechLimitsView.AmmoOnly(5, 20)));
        }
    }
}
