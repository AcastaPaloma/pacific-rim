using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Game.UI.Tests
{
    static class DataFiles
    {
        public static string Read(string rel)
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 12 && dir != null; i++)
            {
                string p = Path.Combine(dir, rel);
                if (File.Exists(p)) return File.ReadAllText(p);
                dir = Path.GetDirectoryName(dir);
            }
            string cwd = Path.Combine(Directory.GetCurrentDirectory(), rel);
            if (File.Exists(cwd)) return File.ReadAllText(cwd);
            throw new FileNotFoundException(rel);
        }
    }

    public class HudColourTableTests
    {
        static HudColourTable Load() { return HudColourTable.Parse(DataFiles.Read("Assets/Game/Config/UI/hud_colours.json")); }

        [Test]
        public void Table_parses_and_sha_matches()
        {
            var t = Load();
            Assert.AreEqual(5, t.Entries.Count);
            Assert.AreEqual(64, t.TableSha256.Length);
        }

        [Test]
        public void Table_equals_embedded_palettes()
        {
            var t = Load();
            foreach (var e in HudPalette.All)
            {
                var p = t.Find(e.Profile);
                Assert.NotNull(p, e.Profile);
                Assert.AreEqual(e.Panel.ToString(), p.Panel.ToString(), e.Profile);
                Assert.AreEqual(e.Text.ToString(), p.Text.ToString(), e.Profile);
                for (int i = 0; i < 4; i++) Assert.AreEqual(e.Fills[i].ToString(), p.Fills[i].ToString(), e.Profile + " fill " + i);
            }
        }

        [Test]
        public void Tampering_is_rejected()
        {
            string s = DataFiles.Read("Assets/Game/Config/UI/hud_colours.json").Replace("\"585d79\"", "\"585d7a\"");
            Assert.Throws<HudColourTableException>(() => HudColourTable.Parse(s));
            Assert.Throws<HudColourTableException>(() => HudColourTable.Parse("{\"schema\": 1, \"profiles\": {}}"));
            Assert.Throws<HudColourTableException>(() => HudColourTable.Parse("not json"));
        }

        // Parity with Tools/ui/hud_colours.py (python de A B -> [normal, protan, deutan, tritan]).
        [TestCase("ab7676", "ebb98b", 33.2982, 33.3971, 31.6459, 23.647)]
        [TestCase("425e73", "883d3f", 47.115, 23.1444, 34.1621, 62.7099)]
        [TestCase("bf4243", "4b5f74", 66.8346, 29.8021, 48.6946, 85.0245)]
        public void DeltaE_matches_python(string a, string b, double n, double p, double d, double t)
        {
            var ca = Rgb.FromHex(Convert.ToInt32(a, 16)); var cb = Rgb.FromHex(Convert.ToInt32(b, 16));
            Assert.AreEqual(n, CvdMath.DeltaE76(ca, cb, CvdMath.Kind.Normal), 0.001);
            Assert.AreEqual(p, CvdMath.DeltaE76(ca, cb, CvdMath.Kind.Protan), 0.001);
            Assert.AreEqual(d, CvdMath.DeltaE76(ca, cb, CvdMath.Kind.Deutan), 0.001);
            Assert.AreEqual(t, CvdMath.DeltaE76(ca, cb, CvdMath.Kind.Tritan), 0.001);
        }

        [Test]
        public void Fills_min_de_in_table_matches_csharp()
        {
            foreach (var e in Load().Entries)
                Assert.AreEqual(e.FillsMinDe, CvdMath.MinPairwiseWorstDeltaE(e.Fills), 0.001, e.Profile);
        }

        [Test]
        public void Flag_off_uses_embedded_palette()
        {
            var t = Load();
            Assert.AreSame(HudPalette.Find("slate_teal"), HudColourTable.Resolve("slate_teal", false, t));
            Assert.AreNotSame(HudPalette.Find("slate_teal"), HudColourTable.Resolve("slate_teal", true, t));
            Assert.AreSame(HudPalette.Find("slate_teal"), HudColourTable.Resolve("slate_teal", true, null));
        }
    }

    public class HudFontLayoutTests
    {
        [Test]
        public void Font_has_all_hud_characters()
        {
            foreach (char c in "PWRHETAMO0123456789/:%.-! ") Assert.IsTrue(HudFont.HasGlyph(c), c.ToString());
            foreach (char c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ") Assert.IsTrue(HudFont.HasGlyph(c));
        }

        [Test]
        public void Glyphs_are_distinct_and_nonblank()
        {
            var seen = new Dictionary<string, char>();
            foreach (char c in HudFont.Charset())
            {
                string key = Convert.ToBase64String(HudFont.Rows(c));
                if (c == ' ') continue;
                int lit = 0; for (int y = 0; y < 7; y++) for (int x = 0; x < 5; x++) if (HudFont.Pixel(c, x, y)) lit++;
                Assert.Greater(lit, 0, c.ToString());
                Assert.IsFalse(seen.ContainsKey(key), c + " duplicates " + (seen.ContainsKey(key) ? seen[key].ToString() : ""));
                seen[key] = c;
            }
        }

        [Test]
        public void Measure_and_wrap()
        {
            Assert.AreEqual(29, HudFont.MeasureWidth("18/20", 1));
            Assert.AreEqual(58, HudFont.MeasureWidth("18/20", 2));
            foreach (var line in HudFont.Wrap("OVERHEAT WARNING REACTOR POWER LOW", 100, 1)) Assert.LessOrEqual(HudFont.MeasureWidth(line, 1), 100);
            Assert.AreEqual(1, HudFont.Wrap("OK", 100, 1).Count);
        }

        [Test]
        public void Text_ladder_meets_size_minimum()
        {
            Assert.AreEqual(28, HudTextLadder.BodyHeightPx(TextScaleStep.Small, 4));
            Assert.AreEqual(56, HudTextLadder.BodyHeightPx(TextScaleStep.Large, 4));
            Assert.GreaterOrEqual(HudTextLadder.BodyHeightPx(TextScaleStep.Small, 4), 18);
            Assert.AreEqual(42, HudTextLadder.BodyHeightPx(TextScaleStep.Small, 6));
            Assert.IsFalse(HudTextLadder.IsPlaceholder(TextScaleStep.Medium)); // T-154c: the 7x10 font exists
            Assert.AreEqual(40, HudTextLadder.BodyHeightPx(TextScaleStep.Medium, 4));
        }

        static HudLayout Layout() { return HudLayout.Parse(DataFiles.Read("Assets/Game/Config/UI/hud_layout.json")); }

        [Test]
        public void Layout_is_valid()
        {
            var errs = Layout().Validate();
            Assert.IsEmpty(errs, string.Join("; ", errs));
        }

        [TestCase(TextScaleStep.Small), TestCase(TextScaleStep.Medium), TestCase(TextScaleStep.Large)]
        public void No_text_is_clipped_at_any_step(TextScaleStep step)
        {
            foreach (int k in new[] { 4, 6 }) Assert.IsEmpty(Layout().ClippedText(step, k), step + " k=" + k);
        }

        [Test]
        public void Integer_placement()
        {
            var l = Layout();
            var r = l.Place(l.Find("ammo_text"), 4);
            Assert.AreEqual(388 * 4, r.X);
            Assert.AreEqual(84 * 4, r.W);
            Assert.LessOrEqual(r.Right, 1920);
            Assert.LessOrEqual(l.Place(l.Find("heat_label"), 6).Bottom, 1620);
        }

        [Test]
        public void Bad_layouts_are_rejected_or_flagged()
        {
            Assert.Throws<HudLayoutException>(() => HudLayout.Parse("{\"schema\":1,\"ref_w\":640,\"ref_h\":360,\"elements\":[]}"));
            var l = HudLayout.Parse("{\"schema\":1,\"ref_w\":480,\"ref_h\":270,\"elements\":[{\"id\":\"a\",\"kind\":\"bar\",\"x\":200,\"y\":120,\"w\":10,\"h\":6},{\"id\":\"b\",\"kind\":\"bar\",\"x\":205,\"y\":120,\"w\":10,\"h\":6},{\"id\":\"c\",\"kind\":\"bar\",\"x\":475,\"y\":0,\"w\":10,\"h\":6}]}");
            var errs = l.Validate();
            Assert.IsTrue(errs.Exists(e => e.Contains("central")));
            Assert.IsTrue(errs.Exists(e => e.Contains("overlaps")));
            Assert.IsTrue(errs.Exists(e => e.Contains("outside")));
        }
    }

    public class CannonAmmoProviderTests
    {
        [Test]
        public void Reports_ammo_only_view()
        {
            var p = new CannonAmmoProvider(new DelegateAmmoSource((int id, out CannonAmmoState s) => { s = new CannonAmmoState(18, 20, false, 1f); return id == 1; }));
            MechLimitsView v;
            Assert.IsTrue(p.TryGetLimits(1, out v));
            Assert.AreEqual(18, v.AmmoCount); Assert.AreEqual(20, v.AmmoMax);
            Assert.IsTrue(v.Has(LimitsAvailability.Ammo));
            Assert.IsFalse(v.Has(LimitsAvailability.Heat));
            Assert.IsFalse(p.TryGetLimits(2, out v));
        }

        [Test]
        public void No_source_or_zero_max_hides_the_block()
        {
            MechLimitsView v;
            Assert.IsFalse(new CannonAmmoProvider(null).TryGetLimits(1, out v));
            var p = new CannonAmmoProvider(new DelegateAmmoSource((int id, out CannonAmmoState s) => { s = new CannonAmmoState(0, 0, false, 1f); return true; }));
            Assert.IsFalse(p.TryGetLimits(1, out v));
        }

        [Test]
        public void Reload_fraction_from_cooldown()
        {
            var ready = CannonAmmoState.FromCooldown(5, 20, 0, 150);
            Assert.IsFalse(ready.Reloading); Assert.AreEqual(1f, ready.ReloadFrac);
            var half = CannonAmmoState.FromCooldown(5, 20, 75, 150);
            Assert.IsTrue(half.Reloading); Assert.AreEqual(0.5f, half.ReloadFrac, 1e-6);
            var p = new CannonAmmoProvider(new DelegateAmmoSource((int id, out CannonAmmoState s) => { s = half; return true; }));
            bool reloading; float f;
            Assert.IsTrue(p.TryGetReload(7, out reloading, out f));
            Assert.IsTrue(reloading); Assert.AreEqual(0.5f, f, 1e-6);
        }

        [Test]
        public void Ammo_is_clamped_by_contract()
        {
            var p = new CannonAmmoProvider(new DelegateAmmoSource((int id, out CannonAmmoState s) => { s = new CannonAmmoState(99, 20, false, 1f); return true; }));
            MechLimitsView v; p.TryGetLimits(0, out v);
            Assert.AreEqual(20, v.AmmoCount);
        }
    }
}
