using System;

namespace Game.UI
{
    /// <summary>Everything the HUD view draws for one frame. Built by the host from contracts only (limits provider, reload query, health numbers).</summary>
    public struct HudFrameData
    {
        public bool Visible;
        public bool HasLimits; public MechLimitsView Limits;
        public bool HasReload; public bool Reloading; public float ReloadFrac;   // 0..1, 1 = ready
        public bool HasMechHp; public float MechHp;                              // 0..1
        public bool HasKaijuHp; public float KaijuHp;                            // 0..1
    }

    /// <summary>
    /// T-154 HUD view, pure part: draws the HUD into a 480x270 RGBA frame on the integer grid (R-122 4.1). Layout from hud_layout.json (<see cref="HudLayout"/>),
    /// glyphs from <see cref="HudFont"/>, colours only from the <see cref="HudPalette"/> (exact profile entries: panel, text/outline, four fills), so every
    /// pixel is either transparent (alpha 0) or one of those six colours. No filtering, no anti-aliasing, no floats in the geometry. Rows are top-down.
    /// The text ladder: S = 5x7 font at 1 internal px, L = 5x7 at 2 internal px (k and 2k on screen); M has no 7x10 font yet and falls back to S (placeholder).
    /// </summary>
    public sealed class HudRaster
    {
        public const int W = HudLayout.RefW, H = HudLayout.RefH;
        public readonly byte[] Rgba = new byte[W * H * 4];
        /// <summary>Strings shortened because they did not fit their layout rect (M-NEW-c wants 0 at S, M, L).</summary>
        public int ClippedStrings;
        public int DrawnElements;
        public bool MediumFontPlaceholder;

        Rgb _panel, _text;
        TextScaleStep _step;

        public void Clear() { Array.Clear(Rgba, 0, Rgba.Length); ClippedStrings = 0; DrawnElements = 0; }

        public void Render(HudLayout layout, in HudFrameData dIn, HudPalette pal, AccessibilitySettings acc)
        {
            var d = dIn;
            Clear();
            if (layout == null || pal == null || !d.Visible) return;
            _panel = pal.Panel; _text = pal.Text;
            int rs = HudTextLadder.PixelScale(acc.TextScale, 1);
            _step = acc.TextScale;
            MediumFontPlaceholder = HudTextLadder.IsPlaceholder(acc.TextScale);
            var v = d.Limits;
            // T-154c: real health from the limits view fills the mech bar when the host did not supply its own number.
            if (d.HasLimits && v.Has(LimitsAvailability.Health) && !d.HasMechHp) { d.HasMechHp = true; d.MechHp = v.HealthFrac; }
            bool ammo = d.HasLimits && v.Has(LimitsAvailability.Ammo) && v.AmmoMax > 0;

            if (d.HasMechHp) { Label(layout, "mech_hp_label", "MECH " + Pct(d.MechHp) + "%", rs); Bar(layout, "mech_hp_bar", d.MechHp, pal.Fills[1], pal); }
            if (d.HasKaijuHp) { Label(layout, "kaiju_label", "KAIJU " + Pct(d.KaijuHp) + "%", rs); Bar(layout, "kaiju_bar", d.KaijuHp, pal.Fills[3], pal); }
            if (d.HasLimits && v.Has(LimitsAvailability.Power)) { Label(layout, "power_label", "PWR", rs); Bar(layout, "power_bar", v.PowerFrac, pal.Fills[0], pal); }
            if (d.HasLimits && v.Has(LimitsAvailability.Heat)) { Label(layout, "heat_label", "HEA", rs); Bar(layout, "heat_bar", v.HeatFrac, pal.Fills[1], pal); }
            if (d.HasLimits && v.Has(LimitsAvailability.Strain)) VBar(layout, "strain_gauge", v.StrainFrac, pal.Fills[3], pal);
            if (ammo)
            {
                Label(layout, "ammo_text", v.AmmoCount + "/" + v.AmmoMax, rs);
                Pips(layout, "ammo_pips", v.AmmoCount, v.AmmoMax, pal.Fills[2], pal);
                if (d.HasReload) SmallBar(layout, "reload_bar", d.ReloadFrac, pal.Fills[3], pal);
            }
            string warn = Warning(d, ammo, v);
            if (warn != null) Label(layout, "warning_line", warn, rs);
        }

        static int Pct(float f) { return !(f > 0f) ? 0 : f >= 1f ? 100 : (int)(f * 100f + 0.5f); }

        /// <summary>One transient line, most urgent first (R-122: max one warning line). Null when nothing to say.</summary>
        public static string Warning(in HudFrameData d, bool ammo, MechLimitsView v)
        {
            if (d.HasMechHp && d.MechHp <= 0f) return "MECH DOWN";
            if (d.HasKaijuHp && d.KaijuHp <= 0f) return "KAIJU DOWN";
            if (ammo && v.AmmoCount == 0) return "NO AMMO";
            if (d.HasMechHp && d.MechHp < 0.25f) return "ARMOUR LOW";
            if (d.HasReload && d.Reloading) return "RELOADING";
            return null;
        }

        // ---- primitives (top-down, clipped to the frame) ----
        void Put(int x, int y, Rgb c)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            int i = (y * W + x) * 4;
            Rgba[i] = c.R; Rgba[i + 1] = c.G; Rgba[i + 2] = c.B; Rgba[i + 3] = 255;
        }

        void Fill(int x, int y, int w, int h, Rgb c)
        {
            for (int yy = y; yy < y + h; yy++) for (int xx = x; xx < x + w; xx++) Put(xx, yy, c);
        }

        void Frame(int x, int y, int w, int h, Rgb c)
        {
            Fill(x, y, w, 1, c); Fill(x, y + h - 1, w, 1, c); Fill(x, y, 1, h, c); Fill(x + w - 1, y, 1, h, c);
        }

        void Backdrop(HudRect r) { Fill(r.X - 2, r.Y - 2, r.W + 4, r.H + 4, _panel); }

        void Text(int x, int y, string s, int scale, Rgb c)
        {
            int cx = x;
            for (int i = 0; i < s.Length; i++)
            {
                for (int gy = 0; gy < HudFont.GlyphHeight(_step); gy++)
                    for (int gx = 0; gx < HudFont.GlyphWidth(_step); gx++)
                        if (HudFont.Pixel(s[i], gx, gy, _step)) Fill(cx + gx * scale, y + gy * scale, scale, scale, c);
                cx += HudFont.AdvanceOf(_step) * scale;
            }
        }

        // ---- elements ----
        void Label(HudLayout l, string id, string s, int scale)
        {
            var e = l.Find(id); if (e == null || string.IsNullOrEmpty(s)) return;
            var r = e.Rect;
            if (HudFont.HeightPx(scale, _step) > r.H) { ClippedStrings++; return; }
            while (s.Length > 0 && HudFont.MeasureWidth(s, scale, _step) > r.W) { s = s.Substring(0, s.Length - 1); ClippedStrings++; }
            if (s.Length == 0) return;
            Backdrop(new HudRect(r.X, r.Y + (r.H - HudFont.HeightPx(scale, _step)) / 2, HudFont.MeasureWidth(s, scale, _step), HudFont.HeightPx(scale, _step)));
            Text(r.X, r.Y + (r.H - HudFont.HeightPx(scale, _step)) / 2, s, scale, _text);
            DrawnElements++;
        }

        static int LitCount(float frac, int n)
        {
            if (!(frac > 0f)) return 0;
            if (frac >= 1f) return n;
            int lit = (int)(frac * n + 0.5f);
            return lit == 0 ? 1 : lit;   // a non-empty gauge shows at least one segment: empty and low stay distinguishable
        }

        void Bar(HudLayout l, string id, float frac, Rgb fill, HudPalette pal)
        {
            var e = l.Find(id); if (e == null) return;
            var r = e.Rect; int n = e.Segments < 1 ? 1 : e.Segments;
            Backdrop(r); Frame(r.X, r.Y, r.W, r.H, pal.Outline);
            int ix = r.X + 1, iy = r.Y + 1, iw = r.W - 2, ih = r.H - 2, lit = LitCount(frac, n);
            for (int i = 0; i < lit; i++)
            {
                int x0 = ix + i * iw / n, x1 = ix + (i + 1) * iw / n, w = x1 - x0;
                if (w >= 2 && i < n - 1) w--;   // 1 px gap between segments
                Fill(x0, iy, w, ih, fill);
            }
            DrawnElements++;
        }

        void VBar(HudLayout l, string id, float frac, Rgb fill, HudPalette pal)
        {
            var e = l.Find(id); if (e == null) return;
            var r = e.Rect; int n = e.Segments < 1 ? 1 : e.Segments;
            Backdrop(r); Frame(r.X, r.Y, r.W, r.H, pal.Outline);
            int ix = r.X + 1, iy = r.Y + 1, iw = r.W - 2, ih = r.H - 2, lit = LitCount(frac, n);
            for (int i = 0; i < lit; i++)
            {
                int y1 = iy + ih - i * ih / n, y0 = iy + ih - (i + 1) * ih / n, h = y1 - y0;
                if (h >= 2 && i < n - 1) { y1--; h--; }
                Fill(ix, y0, iw, h, fill);
            }
            DrawnElements++;
        }

        /// <summary>Continuous (whole-pixel) fill bar for the reload progress: 1 px outline, fill width rounded to a pixel.</summary>
        void SmallBar(HudLayout l, string id, float frac, Rgb fill, HudPalette pal)
        {
            var e = l.Find(id); if (e == null) return;
            var r = e.Rect;
            Backdrop(r); Frame(r.X, r.Y, r.W, r.H, pal.Outline);
            int iw = r.W - 2, w = !(frac > 0f) ? 0 : frac >= 1f ? iw : (int)(frac * iw);
            if (w > 0) Fill(r.X + 1, r.Y + 1, w, r.H - 2, fill);
            DrawnElements++;
        }

        void Pips(HudLayout l, string id, int count, int max, Rgb fill, HudPalette pal)
        {
            var e = l.Find(id); if (e == null) return;
            var r = e.Rect; int n = Math.Min(max, e.Segments < 1 ? 1 : e.Segments);
            int lit = n == max ? Math.Min(count, n) : (count <= 0 ? 0 : Math.Max(1, (int)((long)count * n / max)));
            Backdrop(r);
            for (int i = 0; i < n; i++)
            {
                int x0 = r.X + i * r.W / n, x1 = r.X + (i + 1) * r.W / n, w = x1 - x0;
                if (w >= 3) w--;
                if (i < lit) Fill(x0, r.Y, w, r.H, fill); else Frame(x0, r.Y, w, r.H, pal.Outline);
            }
            DrawnElements++;
        }
    }
}
