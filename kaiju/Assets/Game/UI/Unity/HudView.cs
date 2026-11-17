using System;
using UnityEngine;

namespace Game.UI.Unity
{
    /// <summary>
    /// T-154 HUD view (Unity side, behind flags ui.hud_data + ui.hud_view, both default off). Rasterises the HUD with <see cref="HudRaster"/> into a 480x270
    /// point-filtered texture and draws it over the presented frame at the exact integer scale k, offset by the present stage's own whole-pixel shift so its
    /// pixel blocks line up with the world's. Palette-locked (six exact profile colours), no filtering, no scaling other than k. Read-only: it never touches
    /// simulation state; it pulls a <see cref="HudFrameData"/> from the host each repaint.
    /// </summary>
    public sealed class HudView : MonoBehaviour
    {
        HudLayout _layout; HudPalette _palette;
        Func<HudFrameData> _source; Func<Vector2Int> _shift; Func<int> _scale;
        readonly HudRaster _raster = new HudRaster();
        Texture2D _tex; ushort[] _half;
        public AccessibilitySettings Accessibility = AccessibilitySettings.Default;

        /// <summary>Last frame's numbers for logs and the evidence record.</summary>
        public int LastScale, LastClippedStrings, LastDrawnElements;
        public Vector2Int LastShift;
        public int RepaintCount;

        public void Configure(HudLayout layout, HudPalette palette, Func<HudFrameData> source, Func<Vector2Int> presentShift, Func<int> scale)
        {
            _layout = layout; _palette = palette; _source = source; _shift = presentShift; _scale = scale;
        }

        void EnsureTexture()
        {
            if (_tex != null) return;
            _tex = new Texture2D(HudRaster.W, HudRaster.H, TextureFormat.RGBAHalf, false, true) { name = "HudView", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        }

        void OnDestroy() { if (_tex != null) Destroy(_tex); }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || _layout == null || _palette == null || _source == null) return;
            var data = _source();
            if (!data.Visible) return;
            _raster.Render(_layout, data, _palette, Accessibility);
            EnsureTexture();
            // T-154b: half-float texture holding sRGB-decoded values, so the linear backbuffer's encode-on-write returns the exact palette bytes.
            if (_half == null) _half = new ushort[_raster.Rgba.Length];
            HudLinearUpload.ToHalfRgba(_raster.Rgba, _half);
            _tex.SetPixelData(_half, 0);
            _tex.Apply(false);
            int k = _scale != null ? _scale() : HudTextScale.PixelK(Screen.height);
            if (k < 1) k = 1;
            var sh = _shift != null ? _shift() : Vector2Int.zero;
            // Present stage: output pixel o shows texel floor((o + shift) / k), so texel 0 starts at -shift.x (left) and, top-down, at +shift.y.
            // IMGUI rects in a player are in screen pixels (Screen.width x Screen.height), the same space the pixel pipeline presents in.
            var rect = new Rect(-sh.x, sh.y, HudRaster.W * k, HudRaster.H * k);
            GUI.DrawTextureWithTexCoords(rect, _tex, new Rect(0f, 1f, 1f, -1f), true);   // rows are top-down in the raster, bottom-up in the texture
            LastScale = k; LastShift = sh; LastClippedStrings = _raster.ClippedStrings; LastDrawnElements = _raster.DrawnElements; RepaintCount++;
        }
    }
}
