using System;

namespace Game.Rendering.Pixel
{
    /// <summary>
    /// Engine-free mirror of the depth-aware present shift in <c>PixelPresent.shader</c> (T-180, flag rendering.pixel.depthShift). Per axis: a surface at eye
    /// depth d shifts by round(k * (zRef / d) * remainder) output pixels (<see cref="ParallaxShiftMath.DepthShiftPx"/>), clamped to the guard band. Output pixel o shows
    /// the nearest texel whose own shift lands on o; a gap (no texel lands) shows the farther sample of the window. The shader and this class are changed together.
    /// </summary>
    public static class PixelDepthShift
    {
        /// <summary>A depth of 0, negative or non-finite (sky or empty) counts as this far.</summary>
        public const float SkyDepth = 3.0e38f;

        public static float Sanitise(float d) => d > 0f && d < SkyDepth ? d : SkyDepth;

        /// <summary>Whole-output-pixel shift of a surface at depth d on one axis, clamped to ±limitPx.</summary>
        public static int ShiftPx(double referenceDepthM, double depthM, double remainderTexels, int scale, int limitPx)
        {
            int s = ParallaxShiftMath.DepthShiftPx(referenceDepthM / depthM, remainderTexels, scale);
            return Math.Max(-limitPx, Math.Min(limitPx, s));
        }

        /// <summary>
        /// One axis of the resolve over a row of texel depths (the 2D shader is separable per axis only for this test: the 2D window is the product of the 1D ones).
        /// Returns the texel index (guard included) shown at output pixel o and whether it was a gap fill.
        /// </summary>
        public static int ResolveAxis(float[] depthRow, int o, double referenceDepthM, double remainderTexels, int scale, int guardTexels, out bool gapFill)
        {
            int lim = guardTexels * scale, maxT = depthRow.Length - 1;
            int lo = Clamp(FloorDiv(o - lim, scale) + guardTexels, 0, maxT), hi = Clamp(FloorDiv(o + lim, scale) + guardTexels, 0, maxT);
            int uniform = Clamp(FloorDiv(o + ParallaxShiftMath.DepthShiftPx(1.0, remainderTexels, scale), scale) + guardTexels, 0, maxT);
            int nearT = uniform, farT = uniform; float bestNear = float.MaxValue, bestFar = -1f; bool landed = false;
            for (int t = lo; t <= hi; t++)
            {
                float d = Sanitise(depthRow[t]);
                int land = FloorDiv(o + ShiftPx(referenceDepthM, d, remainderTexels, scale, lim), scale) + guardTexels;
                if (land == t && d < bestNear) { bestNear = d; nearT = t; landed = true; }
                if (d > bestFar) { bestFar = d; farT = t; }
            }
            gapFill = !landed;
            return landed ? nearT : farT;
        }

        static int FloorDiv(int a, int b) => (int)Math.Floor(a / (double)b);
        static int Clamp(int v, int lo, int hi) => Math.Max(lo, Math.Min(hi, v));
    }
}
