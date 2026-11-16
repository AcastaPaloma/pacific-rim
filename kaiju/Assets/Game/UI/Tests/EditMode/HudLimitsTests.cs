using System.Collections.Generic;
using NUnit.Framework;

namespace Game.UI.Tests
{
    public class LimitsContractTests
    {
        [Test]
        public void Constructor_clamps_and_classifies()
        {
            var v = new MechLimitsView(LimitsAvailability.Heat | LimitsAvailability.Ammo, 2f, -1f, 0.9f, 99, 20, float.NaN);
            Assert.AreEqual(1f, v.PowerFrac);
            Assert.AreEqual(0f, v.PowerPeakFrac);
            Assert.AreEqual(0f, v.StrainFrac);
            Assert.AreEqual(HeatState.Overheat, v.Heat);
            Assert.AreEqual(20, v.AmmoCount);
            Assert.IsTrue(v.IsValid);
            Assert.AreEqual(UiContractVersions.LimitsView, v.SchemaVersion);
        }

        [TestCase(0f, 0), TestCase(0.59f, 0), TestCase(0.6f, 1), TestCase(0.85f, 2), TestCase(0.999f, 2), TestCase(1f, 3), TestCase(float.NaN, 0)]
        public void Threshold_rungs(float f, int rung) { Assert.AreEqual(rung, LimitsThresholds.Rung(f)); }

        [Test]
        public void AmmoOnly_has_only_ammo()
        {
            var v = MechLimitsView.AmmoOnly(5, 20);
            Assert.IsTrue(v.Has(LimitsAvailability.Ammo));
            Assert.IsFalse(v.Has(LimitsAvailability.Heat));
        }

        [Test]
        public void Accessibility_defaults_match_R122()
        {
            var a = AccessibilitySettings.Default;
            Assert.AreEqual(TextScaleStep.Small, a.TextScale);
            Assert.AreEqual(1f, a.ShakeGain);
            Assert.AreEqual(1f, a.FlashGain);
            Assert.IsFalse(a.HighContrast);
            Assert.AreEqual(0.5f, new AccessibilitySettings(TextScaleStep.Large, true, true, 0.5f, 7f).ShakeGain);
            Assert.AreEqual(1f, new AccessibilitySettings(TextScaleStep.Large, true, true, 0.5f, 7f).FlashGain);
        }
    }

    public class ScriptedProviderTests
    {
        static List<MechLimitsView> Run(int frames)
        {
            var p = new ScriptedLimitsProvider();
            p.SetRamp(1, 10, 20);
            var outp = new List<MechLimitsView>();
            for (int i = 0; i < frames; i++)
            {
                MechLimitsView v;
                Assert.IsTrue(p.TryGetLimits(1, out v));
                outp.Add(v);
                p.Advance();
            }
            return outp;
        }

        [Test]
        public void Ramp_is_deterministic_and_holds_last_frame()
        {
            var a = Run(14); var b = Run(14);
            for (int i = 0; i < a.Count; i++) Assert.AreEqual(a[i], b[i]);
            Assert.AreEqual(1f, a[9].HeatFrac);
            Assert.AreEqual(0, a[9].AmmoCount);
            Assert.AreEqual(a[9], a[13]);
            Assert.AreEqual(HeatState.Lockout, a[13].Heat);
            Assert.AreEqual(20, a[0].AmmoCount);
        }

        [Test]
        public void Unknown_mech_has_no_view_and_reset_rewinds()
        {
            var p = new ScriptedLimitsProvider();
            MechLimitsView v;
            Assert.IsFalse(p.TryGetLimits(9, out v));
            p.SetRamp(2, 5, 10); p.Advance(); p.Advance(); p.Reset();
            Assert.AreEqual(0, p.Frame);
            Assert.IsTrue(p.TryGetLimits(2, out v));
            Assert.AreEqual(0f, v.HeatFrac);
        }
    }

    public class ContrastTests
    {
        [Test]
        public void Black_white_is_21_and_symmetric()
        {
            var k = new Rgb(0, 0, 0); var w = new Rgb(255, 255, 255);
            Assert.AreEqual(21.0, Contrast.Ratio(k, w), 1e-9);
            Assert.AreEqual(Contrast.Ratio(k, w), Contrast.Ratio(w, k), 1e-12);
            Assert.AreEqual(1.0, Contrast.Ratio(w, w), 1e-12);
        }

        [Test]
        public void Five_profiles_present()
        {
            Assert.AreEqual(5, HudPalette.All.Length);
            Assert.IsNotNull(HudPalette.Find("slate_teal"));
            Assert.IsNull(HudPalette.Find("nope"));
        }

        // R-122 4.2 baseline text-on-panel contrast per profile.
        [TestCase("lavender_peach", 19.0), TestCase("slate_crimson", 7.0), TestCase("slate_teal", 7.86),
         TestCase("warm_amber_haze", 12.25), TestCase("warm_rust_sepia", 15.08)]
        public void Text_contrast_matches_research(string profile, double expected)
        {
            var a = Contrast.Audit(HudPalette.Find(profile));
            Assert.AreEqual(expected, a.TextOnPanel, 0.05);
            Assert.IsTrue(a.TextPasses);
            Assert.IsTrue(a.OutlinePasses);
        }

        [TestCase("lavender_peach", 3.10), TestCase("slate_crimson", 2.67), TestCase("slate_teal", 2.32),
         TestCase("warm_amber_haze", 2.81), TestCase("warm_rust_sepia", 2.80)]
        public void Worst_fill_matches_research_and_outline_is_required(string profile, double expected)
        {
            var a = Contrast.Audit(HudPalette.Find(profile));
            Assert.AreEqual(expected, a.WorstFillOnPanel, 0.1);
            // Four of five profiles miss 3:1 for fills alone: that is why every gauge carries an outline.
            if (profile != "lavender_peach") Assert.IsFalse(a.FillsPassAlone);
        }
    }

    public class TextScaleTests
    {
        [Test]
        public void Ladder_at_1080p()
        {
            Assert.AreEqual(28, HudTextScale.BodyPx(TextScaleStep.Small, 1080));
            Assert.AreEqual(40, HudTextScale.BodyPx(TextScaleStep.Medium, 1080));
            Assert.AreEqual(56, HudTextScale.BodyPx(TextScaleStep.Large, 1080));
            Assert.AreEqual(200, HudTextScale.PercentOfSmall(TextScaleStep.Large, 1080));
        }

        [Test]
        public void Four_k_uses_k6_and_meets_minimum()
        {
            Assert.AreEqual(42, HudTextScale.BodyPx(TextScaleStep.Small, 2160));
            Assert.IsTrue(HudTextScale.MeetsPcMinimum(TextScaleStep.Small, 1080));
            Assert.IsTrue(HudTextScale.MeetsPcMinimum(TextScaleStep.Small, 2160));
        }

        [Test]
        public void Low_resolution_fails_minimum_and_scale_is_integer()
        {
            Assert.IsFalse(HudTextScale.MeetsPcMinimum(TextScaleStep.Small, 540)); // k=2 -> 14 px
            Assert.AreEqual(1, HudTextScale.PixelK(100));
            foreach (int h in new[] { 540, 720, 1080, 1440, 2160 })
                foreach (TextScaleStep s in new[] { TextScaleStep.Small, TextScaleStep.Medium, TextScaleStep.Large })
                    Assert.AreEqual(0, HudTextScale.BodyPx(s, h) % HudTextScale.GlyphRows(s));
        }
    }

    public class HudViewModelTests
    {
        static HudModel Build(MechLimitsView v, AccessibilitySettings a, HudPalette p, bool flag = true, bool has = true)
        {
            return HudViewModel.Build(flag, has, v, a, p, 1080);
        }

        static MechLimitsView Full(float heat, int ammo)
        {
            return new MechLimitsView(LimitsAvailability.Power | LimitsAvailability.Heat | LimitsAvailability.Ammo | LimitsAvailability.Strain,
                0.8f, 1f, heat, ammo, 20, heat);
        }

        [Test]
        public void Flag_off_or_no_view_hides_hud()
        {
            Assert.IsFalse(Build(Full(0.9f, 5), AccessibilitySettings.Default, HudPalette.All[0], flag: false).Visible);
            Assert.IsFalse(Build(Full(0.9f, 5), AccessibilitySettings.Default, HudPalette.All[0], has: false).Visible);
            Assert.AreEqual(0, Build(Full(0.9f, 5), AccessibilitySettings.Default, HudPalette.All[0], flag: false).Gauges.Count);
        }

        [Test]
        public void Ammo_only_view_shows_one_gauge_with_count_text()
        {
            var m = Build(MechLimitsView.AmmoOnly(18, 20), AccessibilitySettings.Default, HudPalette.All[1]);
            Assert.AreEqual(1, m.Gauges.Count);
            Assert.AreEqual("18/20", m.Gauges[0].ValueText);
            Assert.AreEqual("AMM", m.Gauges[0].Label);
            Assert.AreEqual(14, m.Gauges[0].LitSegments); // 0.9 * 16 = 14.4
        }

        [Test]
        public void Every_gauge_in_every_state_has_non_colour_cues_on_every_profile()
        {
            foreach (var pal in HudPalette.All)
                foreach (float heat in new[] { 0f, 0.3f, 0.6f, 0.85f, 1f })
                    foreach (int ammo in new[] { 20, 10, 0 })
                    {
                        var m = Build(Full(heat, ammo), AccessibilitySettings.Default, pal);
                        Assert.AreEqual(4, m.Gauges.Count);
                        foreach (var g in m.Gauges)
                        {
                            Assert.IsTrue(g.HasShapeCue, pal.Profile + " " + g.Kind + " " + heat);
                            Assert.IsTrue(g.HasTextCue, pal.Profile + " " + g.Kind + " " + heat);
                            Assert.GreaterOrEqual(g.OutlineThickness, 1);
                            Assert.AreEqual(pal.Outline.ToString(), g.Outline.ToString());
                        }
                    }
        }

        [Test]
        public void Warning_changes_icon_tag_and_outline_not_just_colour()
        {
            var cool = Build(Full(0.1f, 20), AccessibilitySettings.Default, HudPalette.All[2]).Gauges[1];
            var hot = Build(Full(0.9f, 20), AccessibilitySettings.Default, HudPalette.All[2]).Gauges[1];
            Assert.AreNotEqual(cool.Icon, hot.Icon);
            Assert.AreEqual("", cool.StateTag);
            Assert.AreEqual("HOT", hot.StateTag);
            Assert.Greater(hot.OutlineThickness, cool.OutlineThickness);
            Assert.Greater(hot.LitSegments, cool.LitSegments);
            Assert.AreEqual("LOCK", Build(Full(1f, 20), AccessibilitySettings.Default, HudPalette.All[2]).Gauges[1].StateTag);
        }

        [Test]
        public void Blink_is_capped_and_can_be_reduced()
        {
            var hot = Full(0.9f, 20);
            var def = Build(hot, AccessibilitySettings.Default, HudPalette.All[0]).Gauges[1];
            Assert.IsTrue(def.Blink);
            Assert.LessOrEqual(def.BlinkHz, HudViewModel.MaxBlinkHz);
            var calm = new AccessibilitySettings(TextScaleStep.Small, false, true, 1f, 1f);
            var r = Build(hot, calm, HudPalette.All[0]);
            foreach (var g in r.Gauges) { Assert.IsFalse(g.Blink); Assert.AreEqual(0f, g.BlinkHz); }
        }

        [Test]
        public void High_contrast_thickens_outline_and_text_scale_flows_through()
        {
            var acc = new AccessibilitySettings(TextScaleStep.Large, true, false, 1f, 1f);
            var m = Build(Full(0.1f, 20), acc, HudPalette.All[3]);
            Assert.AreEqual(56, m.BodyPx);
            Assert.AreEqual(8, m.TextScaleFactor);
            foreach (var g in m.Gauges) Assert.AreEqual(2, g.OutlineThickness);
        }

        [Test]
        public void Empty_ammo_vs_low_ammo_stay_distinguishable()
        {
            var one = Build(MechLimitsView.AmmoOnly(1, 100), AccessibilitySettings.Default, HudPalette.All[0]).Gauges[0];
            var zero = Build(MechLimitsView.AmmoOnly(0, 100), AccessibilitySettings.Default, HudPalette.All[0]).Gauges[0];
            Assert.AreEqual(1, one.LitSegments);
            Assert.AreEqual(0, zero.LitSegments);
            Assert.AreEqual("EMPTY", zero.StateTag);
            Assert.AreNotEqual(one.Icon, zero.Icon);
        }

        [Test]
        public void Build_is_deterministic()
        {
            var a = Build(Full(0.7f, 7), AccessibilitySettings.Default, HudPalette.All[4]);
            var b = Build(Full(0.7f, 7), AccessibilitySettings.Default, HudPalette.All[4]);
            Assert.AreEqual(a.Gauges.Count, b.Gauges.Count);
            for (int i = 0; i < a.Gauges.Count; i++)
            {
                Assert.AreEqual(a.Gauges[i].LitSegments, b.Gauges[i].LitSegments);
                Assert.AreEqual(a.Gauges[i].ValueText, b.Gauges[i].ValueText);
                Assert.AreEqual(a.Gauges[i].Icon, b.Gauges[i].Icon);
            }
        }
    }
}
