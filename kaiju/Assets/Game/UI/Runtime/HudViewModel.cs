using System.Collections.Generic;

namespace Game.UI
{
    public enum GaugeKind { Power = 0, Heat = 1, Ammo = 2, Strain = 3 }

    /// <summary>Icon shape per gauge and state: identity and state are carried by shape, not hue (R-122 4.2).</summary>
    public enum GaugeIcon
    {
        Bolt = 0, BoltLow = 1,
        Flame = 2, FlameHot = 3, FlameLock = 4,
        Shell = 5, ShellEmpty = 6,
        Heart = 7, HeartWarn = 8, HeartCrit = 9,
    }

    /// <summary>One gauge, fully resolved: everything a renderer needs, nothing it must decide.</summary>
    public sealed class HudGaugeModel
    {
        public GaugeKind Kind;
        public string Label;        // 3-letter label (PWR, HEA, AMM, STR)
        public string StateTag;     // empty when nominal; otherwise text cue (WARM, HOT, LOCK, ...)
        public string ValueText;    // "18/20" for ammo, "73%" otherwise
        public GaugeIcon Icon;
        public int Segments;        // total segments
        public int LitSegments;
        public int Rung;            // 0 nominal .. 3 lockout/overload
        public int OutlineThickness; // px in the internal pixel grid
        public Rgb Fill;
        public Rgb Outline;
        public bool Blink;
        public float BlinkHz;       // <= 2, 0 when not blinking
        public bool HasShapeCue;    // icon differs between nominal and warning states
        public bool HasTextCue;     // label always, state tag when rung > 0
    }

    public sealed class HudModel
    {
        public bool Visible;
        public int TextScaleFactor;
        public int BodyPx;
        public Rgb Panel;
        public Rgb Text;
        public readonly List<HudGaugeModel> Gauges = new List<HudGaugeModel>();
    }

    /// <summary>
    /// Pure view-model: limits view + accessibility settings + palette to gauge descriptions. Redundant coding
    /// (icon shape, label, state tag, segment count, outline) is always on; colour is only an extra (R-122 4.2).
    /// </summary>
    public static class HudViewModel
    {
        public const int SegmentCount = 16;
        public const float MaxBlinkHz = 2f;

        static readonly string[] HeatTags = { "", "WARM", "HOT", "LOCK" };
        static readonly string[] StrainTags = { "", "TENSE", "CRIT", "OVER" };

        public static HudModel Build(bool hudFlag, bool hasView, MechLimitsView view, AccessibilitySettings acc, HudPalette pal, int outputHeight)
        {
            var m = new HudModel
            {
                Visible = hudFlag && hasView,
                TextScaleFactor = HudTextScale.Factor(acc.TextScale, outputHeight),
                BodyPx = HudTextScale.BodyPx(acc.TextScale, outputHeight),
                Panel = pal.Panel,
                Text = pal.Text,
            };
            if (!m.Visible) return m;
            if (view.Has(LimitsAvailability.Power))
                m.Gauges.Add(Gauge(GaugeKind.Power, "PWR", view.PowerFrac, view.PowerFrac < 0.25f ? 2 : 0, pal, acc, 0, view));
            if (view.Has(LimitsAvailability.Heat))
                m.Gauges.Add(Gauge(GaugeKind.Heat, "HEA", view.HeatFrac, (int)view.Heat, pal, acc, 1, view));
            if (view.Has(LimitsAvailability.Ammo))
                m.Gauges.Add(Gauge(GaugeKind.Ammo, "AMM", view.AmmoMax == 0 ? 0f : (float)view.AmmoCount / view.AmmoMax,
                    view.AmmoMax > 0 && view.AmmoCount == 0 ? 2 : 0, pal, acc, 2, view));
            if (view.Has(LimitsAvailability.Strain))
                m.Gauges.Add(Gauge(GaugeKind.Strain, "STR", view.StrainFrac, (int)view.Strain, pal, acc, 3, view));
            return m;
        }

        static HudGaugeModel Gauge(GaugeKind kind, string label, float frac, int rung, HudPalette pal, AccessibilitySettings acc,
            int fillSlot, MechLimitsView view)
        {
            int lit = (int)(frac * SegmentCount + 0.5f);
            // A non-empty gauge always shows at least one lit segment so "empty" and "low" stay distinguishable.
            if (frac > 0f && lit == 0) lit = 1;
            bool warn = rung >= 2;
            var g = new HudGaugeModel
            {
                Kind = kind,
                Label = label,
                Rung = rung,
                Segments = SegmentCount,
                LitSegments = lit,
                Fill = pal.Fills[fillSlot],
                Outline = pal.Outline,
                // Outline is always present (1 px, lightest colour); warning or high contrast thickens it to 2 px.
                OutlineThickness = (warn || acc.HighContrast) ? 2 : 1,
                Icon = IconFor(kind, rung),
                StateTag = TagFor(kind, rung),
                ValueText = kind == GaugeKind.Ammo ? view.AmmoCount + "/" + view.AmmoMax : (int)(frac * 100f + 0.5f) + "%",
            };
            g.Blink = warn && !acc.ReduceBlink;
            g.BlinkHz = g.Blink ? MaxBlinkHz : 0f;
            g.HasShapeCue = !warn || g.Icon != IconFor(kind, 0);
            g.HasTextCue = g.Label.Length == 3 && (rung == 0 || g.StateTag.Length > 0);
            return g;
        }

        static GaugeIcon IconFor(GaugeKind kind, int rung)
        {
            switch (kind)
            {
                case GaugeKind.Power: return rung >= 2 ? GaugeIcon.BoltLow : GaugeIcon.Bolt;
                case GaugeKind.Heat: return rung >= 3 ? GaugeIcon.FlameLock : rung == 2 ? GaugeIcon.FlameHot : GaugeIcon.Flame;
                case GaugeKind.Ammo: return rung >= 2 ? GaugeIcon.ShellEmpty : GaugeIcon.Shell;
                default: return rung >= 3 ? GaugeIcon.HeartCrit : rung >= 2 ? GaugeIcon.HeartWarn : GaugeIcon.Heart;
            }
        }

        static string TagFor(GaugeKind kind, int rung)
        {
            if (rung == 0) return "";
            switch (kind)
            {
                case GaugeKind.Heat: return HeatTags[rung];
                case GaugeKind.Strain: return StrainTags[rung];
                case GaugeKind.Power: return "LOW";
                default: return "EMPTY";
            }
        }
    }
}
