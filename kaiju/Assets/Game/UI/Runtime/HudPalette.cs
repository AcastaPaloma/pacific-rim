using System;

namespace Game.UI
{
    public readonly struct Rgb
    {
        public readonly byte R, G, B;
        public Rgb(byte r, byte g, byte b) { R = r; G = g; B = b; }
        public static Rgb FromHex(int hex) { return new Rgb((byte)((hex >> 16) & 255), (byte)((hex >> 8) & 255), (byte)(hex & 255)); }
        public override string ToString() { return R.ToString("x2") + G.ToString("x2") + B.ToString("x2"); }
    }

    /// <summary>
    /// The colours the HUD draws from for one pixel palette profile: panel = darkest entry, text and outline = lightest,
    /// four gauge fills = the CVD-best set (R-122 4.2, script research/R-122/cvd_palette.py). Colours are exact profile entries.
    /// </summary>
    public sealed class HudPalette
    {
        public readonly string Profile;
        public readonly Rgb Panel;
        public readonly Rgb Text;
        public readonly Rgb Outline;
        public readonly Rgb[] Fills;

        public HudPalette(string profile, Rgb panel, Rgb text, Rgb[] fills)
        {
            Profile = profile; Panel = panel; Text = text; Outline = text; Fills = fills;
        }

        /// <summary>The five shipped profiles (R-122 data, 2026-11-15).</summary>
        public static readonly HudPalette[] All =
        {
            Make("lavender_peach", 0x010b02, 0xfcfbda, 0x585d79, 0xab7676, 0xebb98b, 0xfcfbda),
            Make("slate_crimson", 0x050808, 0xad947e, 0x425e73, 0x883d3f, 0xad947e, 0xbf4243),
            Make("slate_teal", 0x16282b, 0xadbad8, 0x4b5f74, 0x63738e, 0x8493af, 0xadbad8),
            Make("warm_amber_haze", 0x031117, 0xfbc585, 0x705451, 0xb0735c, 0xeb784e, 0xfbc585),
            Make("warm_rust_sepia", 0x050604, 0xf2de8b, 0x8a4138, 0xa37048, 0xeb8c58, 0xf2de8b),
        };

        static HudPalette Make(string name, int panel, int text, int f0, int f1, int f2, int f3)
        {
            return new HudPalette(name, Rgb.FromHex(panel), Rgb.FromHex(text),
                new[] { Rgb.FromHex(f0), Rgb.FromHex(f1), Rgb.FromHex(f2), Rgb.FromHex(f3) });
        }

        public static HudPalette Find(string profile)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].Profile == profile) return All[i];
            return null;
        }
    }

    /// <summary>WCAG 2.x relative-luminance contrast (sRGB), used for M-NEW-b style checks.</summary>
    public static class Contrast
    {
        public const double TextMin = 4.5;      // WCAG 1.4.3 AA body text
        public const double NonTextMin = 3.0;   // WCAG 1.4.11 (recalled, unverified per R-122 M-NEW-b)

        static double Lin(byte c)
        {
            double s = c / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        public static double Luminance(Rgb c) { return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B); }

        public static double Ratio(Rgb a, Rgb b)
        {
            double ya = Luminance(a), yb = Luminance(b);
            double hi = ya > yb ? ya : yb, lo = ya > yb ? yb : ya;
            return (hi + 0.05) / (lo + 0.05);
        }

        /// <summary>Per-profile audit: text and outline against panel, and the worst gauge fill against panel.</summary>
        public static ContrastAudit Audit(HudPalette p)
        {
            double worst = double.MaxValue;
            for (int i = 0; i < p.Fills.Length; i++) { double r = Ratio(p.Fills[i], p.Panel); if (r < worst) worst = r; }
            return new ContrastAudit(p.Profile, Ratio(p.Text, p.Panel), Ratio(p.Outline, p.Panel), worst);
        }
    }

    public readonly struct ContrastAudit
    {
        public readonly string Profile;
        public readonly double TextOnPanel;
        public readonly double OutlineOnPanel;
        public readonly double WorstFillOnPanel;

        public ContrastAudit(string profile, double text, double outline, double worstFill)
        {
            Profile = profile; TextOnPanel = text; OutlineOnPanel = outline; WorstFillOnPanel = worstFill;
        }

        public bool TextPasses { get { return TextOnPanel >= Contrast.TextMin; } }
        public bool OutlinePasses { get { return OutlineOnPanel >= Contrast.NonTextMin; } }
        /// <summary>False in 4 of 5 profiles (R-122 4.2): fill hue alone never carries a gauge, the outline does.</summary>
        public bool FillsPassAlone { get { return WorstFillOnPanel >= Contrast.NonTextMin; } }
    }
}
