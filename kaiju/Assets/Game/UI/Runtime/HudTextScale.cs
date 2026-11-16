namespace Game.UI
{
    /// <summary>
    /// Pixel-font ladder (R-122 4.4): S = 5x7 font at k, M = 7x10 font at k, L = 5x7 font at 2k. Integer scales only.
    /// k is the pixel pipeline's upscale factor at the output height (480x270 internal; 4 at 1080p, 6 at 4K per R-122).
    /// </summary>
    public static class HudTextScale
    {
        public const int MinBodyPxPc = 18; // XAG 101 [5], at 1080p

        public static int PixelK(int outputHeight)
        {
            if (outputHeight >= 2160) return 6;
            int k = outputHeight / 270;
            return k < 1 ? 1 : k;
        }

        public static int GlyphRows(TextScaleStep step) { return step == TextScaleStep.Medium ? 10 : 7; }
        public static int GlyphCols(TextScaleStep step) { return step == TextScaleStep.Medium ? 7 : 5; }

        /// <summary>Integer scale applied to the font for this step.</summary>
        public static int Factor(TextScaleStep step, int outputHeight)
        {
            int k = PixelK(outputHeight);
            return step == TextScaleStep.Large ? k * 2 : k;
        }

        /// <summary>Body (glyph) height in output pixels.</summary>
        public static int BodyPx(TextScaleStep step, int outputHeight)
        {
            return GlyphRows(step) * Factor(step, outputHeight);
        }

        /// <summary>Text size relative to Small, in percent (L is exactly 200 %).</summary>
        public static int PercentOfSmall(TextScaleStep step, int outputHeight)
        {
            return BodyPx(step, outputHeight) * 100 / BodyPx(TextScaleStep.Small, outputHeight);
        }

        public static bool MeetsPcMinimum(TextScaleStep step, int outputHeight)
        {
            return BodyPx(step, outputHeight) >= MinBodyPxPc * (outputHeight >= 2160 ? 2 : 1);
        }
    }
}
