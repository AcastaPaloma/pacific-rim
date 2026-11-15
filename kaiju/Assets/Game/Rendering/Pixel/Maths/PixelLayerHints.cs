using System;

namespace Game.Rendering.Pixel
{
    /// <summary>Engine-free validation of the v2 layer hints (T-181b). The binder renders layered only when this accepts; otherwise the v1 single-snap path, counted.</summary>
    public static class PixelLayerHints
    {
        /// <summary>Largest remainder (own texels) a layer may carry: the snap is a ceil, so the remainder is within one texel.</summary>
        public const float MaxSubTexel = 1.0f;
        /// <summary>The main layer must reproduce the v1 fields to float precision (relative).</summary>
        public const double MainTolerance = 1e-6;

        public static PixelLayerHint Get(in PixelStabilisationHints h, int slab) => slab == PixelLayerPlan.Near ? h.LayerNear : slab == PixelLayerPlan.Main ? h.LayerMain : h.LayerFar;

        /// <summary>Reference depths of the three layers (near, main, far) for <see cref="PixelLayerPlan.TryBuild"/>.</summary>
        public static double[] ReferenceDepths(in PixelStabilisationHints h) => new[] { (double)h.LayerNear.ReferenceDepth, h.LayerMain.ReferenceDepth, h.LayerFar.ReferenceDepth };

        /// <summary>
        /// True when the hints carry three usable layers. <c>LayerCount == 0</c> is a valid v1 source and returns false with a null reason (not an error);
        /// anything else that is not usable returns false with the reason (the caller counts it in PixelLog and renders v1).
        /// </summary>
        public static bool TryValidate(in PixelStabilisationHints h, double tanHalfVertical, int internalHeight, out string reason)
        {
            reason = null;
            if (h.LayerCount == 0) return false;
            if (h.LayerCount != PixelLayerPlan.Count) { reason = "LayerCount " + h.LayerCount + " is not 0 or " + PixelLayerPlan.Count; return false; }
            if (!(tanHalfVertical > 0) || internalHeight <= 0) { reason = "no grid to validate the layers against"; return false; }
            double prev = 0;
            for (int i = 0; i < PixelLayerPlan.Count; i++)
            {
                PixelLayerHint l = Get(h, i);
                string n = i == 0 ? "near" : i == 1 ? "main" : "far";
                if (!Finite(l.ReferenceDepth) || !Finite(l.Grid) || !Finite(l.OffsetRight) || !Finite(l.OffsetUp) || !Finite(l.SubTexelX) || !Finite(l.SubTexelY))
                { reason = n + " layer has a non-finite value"; return false; }
                if (!(l.ReferenceDepth > prev)) { reason = n + " layer reference depth " + l.ReferenceDepth + " m does not increase (near < main < far, positive)"; return false; }
                prev = l.ReferenceDepth;
                if (!PixelLayerPlan.GridMatches(l.Grid, l.ReferenceDepth, tanHalfVertical, internalHeight))
                { reason = n + " layer grid " + l.Grid + " m is not one texel at " + l.ReferenceDepth + " m (tolerance " + PixelLayerPlan.GridTolerance + ")"; return false; }
                if (Math.Abs(l.SubTexelX) > MaxSubTexel || Math.Abs(l.SubTexelY) > MaxSubTexel)
                { reason = n + " layer remainder is more than one texel"; return false; }
            }
            PixelLayerHint m = h.LayerMain;
            if (m.OffsetRight != 0 || m.OffsetUp != 0) { reason = "main layer must sit at the rendered pose (offset 0)"; return false; }
            if (m.SubTexelX != h.SubTexelX || m.SubTexelY != h.SubTexelY) { reason = "main layer remainder differs from the v1 remainder"; return false; }
            if (Math.Abs(m.ReferenceDepth / h.ReferenceDepth - 1) > MainTolerance || Math.Abs(m.Grid / h.PositionGrid - 1) > MainTolerance)
            { reason = "main layer reference depth or grid differs from the v1 fields"; return false; }
            // A layer's own snapped position cannot be further than one of its grids from the rendered one per axis plus the rendered pose's own grid.
            for (int i = 0; i < PixelLayerPlan.Count; i += 2)
            {
                PixelLayerHint l = Get(h, i);
                double lim = 2.0 * Math.Max(l.Grid, h.PositionGrid);
                if (Math.Abs(l.OffsetRight) > lim || Math.Abs(l.OffsetUp) > lim) { reason = (i == 0 ? "near" : "far") + " layer offset exceeds two grids"; return false; }
            }
            return true;
        }

        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
