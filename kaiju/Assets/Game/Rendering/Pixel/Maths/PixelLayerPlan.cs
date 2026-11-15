using System;

namespace Game.Rendering.Pixel
{
    /// <summary>One depth slab of the layer-snap pass (T-181, design note in MODULE.md "Layer slabs").</summary>
    public struct PixelSlab
    {
        /// <summary>Eye depth (m) whose texel grid the slab's camera snaps to (rig-supplied).</summary>
        public double ReferenceDepthM;
        /// <summary>Radial distance range (m) the slab owns in the composite: [OwnFromM, OwnToM). Radial, like the haze bands.</summary>
        public double OwnFromM, OwnToM;
        /// <summary>Eye-depth range (m) the slab camera renders (near / far clip): covers the owned radial range at every pixel plus the overlap.</summary>
        public double ClipNearM, ClipFarM;
        /// <summary>Haze band that starts at OwnFromM (-1: the lower edge is not a band edge, or this is the first slab).</summary>
        public int FromBand;
    }

    /// <summary>Tuning of the slab plan (Assumed values, decided autonomously for T-181; config keys in PixelPipeline.json from T-181d).</summary>
    public sealed class PixelLayerPlanSettings
    {
        /// <summary>Relative depth overlap each slab renders beyond its owned range, so a shift difference never opens a gap.</summary>
        public double OverlapFraction = 0.10;
        /// <summary>A boundary keeps its band edge until another edge is better by more than this many local band widths (log distance).</summary>
        public double HysteresisBands = 0.25;
        public double CameraNearM = 0.3, CameraFarM = 50000;
    }

    /// <summary>
    /// Engine-free plan of the near / main / far layer slabs (R-131 4.4 step B, T-176 step T-181): where the slab boundaries go
    /// (at haze band edges, in radial distance, so the slab seam coincides with a band seam), which eye-depth range each slab camera
    /// renders, and which slab a composited pixel comes from. The shader and the binder mirror these rules.
    /// </summary>
    public static class PixelLayerPlan
    {
        public const int Near = 0, Main = 1, Far = 2, Count = 3;

        /// <summary>Length of the view ray (eye depth 1) at the corner of the camera target: radial distance = eye depth * ray length, at most this factor.</summary>
        public static double CornerRayLength(double tanHalfHorizontal, double tanHalfVertical) => Math.Sqrt(1 + tanHalfHorizontal * tanHalfHorizontal + tanHalfVertical * tanHalfVertical);

        /// <summary>Size (m) of one internal texel at an eye depth: the snap grid of a slab whose reference depth this is (the same formula as the rig's stabiliser).</summary>
        public static double GridM(double depthM, double tanHalfVertical, int internalHeight) => 2.0 * depthM * tanHalfVertical / internalHeight;

        /// <summary>
        /// Boundary between a slab of reference depth zA and the next one of reference depth zB. Admissible radial range: above zA * cornerRay * (1 + overlap)
        /// (the near slab's reference surface stays owned by it at every pixel) and below zB / (1 + overlap). Inside it, the haze band edge nearest (log) to the
        /// geometric middle of the range; with no haze or no edge inside, the middle itself (band -1). Hysteresis keeps the previous edge while it is admissible
        /// and not worse than the best by more than <c>hysteresisBands</c> local band widths.
        /// </summary>
        public static bool TryChooseBoundary(double zA, double zB, HazeTuning haze, double cornerRay, PixelLayerPlanSettings s, int previousBand,
                                             out double boundaryM, out int band, out string reason)
        {
            boundaryM = 0; band = -1; reason = null;
            double lo = zA * cornerRay * (1 + s.OverlapFraction), hi = zB / (1 + s.OverlapFraction);
            if (!(lo < hi)) { reason = "reference depths " + zA + " m and " + zB + " m are too close for separate slabs (need " + zA + " * " + cornerRay.ToString("0.###") + " * (1 + overlap)^2 < " + zB + ")"; return false; }
            double ideal = Math.Sqrt(lo * hi);
            boundaryM = ideal;
            if (haze == null || haze.HazeBands <= 1) return true;
            int best = -1; double bestCost = double.PositiveInfinity;
            for (int j = 1; j < haze.HazeBands; j++)
            {
                double e = Edge(haze, j);
                if (!(e > lo && e < hi)) continue;
                double cost = Math.Abs(Math.Log(e / ideal));
                if (cost < bestCost) { bestCost = cost; best = j; }
            }
            if (best < 0) return true;
            band = best;
            if (previousBand > 0 && previousBand < haze.HazeBands && previousBand != best)
            {
                double ep = Edge(haze, previousBand);
                if (ep > lo && ep < hi)
                {
                    // Local band width in log distance at the best edge (the band below it; for the first edge, the band above).
                    double width = best > 1 ? Math.Log(Edge(haze, best) / Edge(haze, best - 1)) : Math.Log(Edge(haze, Math.Min(2, haze.HazeBands - 1)) / Edge(haze, 1));
                    if (Math.Abs(Math.Log(ep / ideal)) - bestCost <= s.HysteresisBands * Math.Abs(width)) band = previousBand;
                }
            }
            boundaryM = Edge(haze, band);
            return true;
        }

        /// <summary>Radial distance (m) where haze band <paramref name="band"/> starts, curve gamma included.</summary>
        public static double Edge(HazeTuning haze, int band) => HazeMath.BandStart(band, haze.HazeStartM, haze.HazeFullM, haze.HazeBands, haze.HazeCurveGamma);

        /// <summary>
        /// Builds the three slabs from the rig's reference depths (near &lt; main &lt; far, eye depth m). <paramref name="haze"/> null: haze off, boundaries at the
        /// geometric middles. <paramref name="previousBands"/> (length 2, or null) are the boundary bands of the previous frame, for hysteresis.
        /// </summary>
        public static bool TryBuild(double[] referenceDepthsM, HazeTuning haze, double tanHalfHorizontal, double tanHalfVertical, PixelLayerPlanSettings s,
                                    int[] previousBands, out PixelSlab[] slabs, out int[] boundaryBands, out string reason)
        {
            slabs = null; boundaryBands = null; reason = null;
            s = s ?? new PixelLayerPlanSettings();
            if (referenceDepthsM == null || referenceDepthsM.Length != Count) { reason = "need " + Count + " reference depths"; return false; }
            for (int i = 0; i < Count; i++)
                if (!(referenceDepthsM[i] > s.CameraNearM) || !(referenceDepthsM[i] < s.CameraFarM)) { reason = "reference depth " + i + " outside the camera range"; return false; }
            if (!(referenceDepthsM[0] < referenceDepthsM[1] && referenceDepthsM[1] < referenceDepthsM[2])) { reason = "reference depths must increase near < main < far"; return false; }
            double ray = CornerRayLength(tanHalfHorizontal, tanHalfVertical);
            var bounds = new double[Count - 1]; boundaryBands = new int[Count - 1];
            for (int i = 0; i < Count - 1; i++)
            {
                int prev = previousBands != null && previousBands.Length == Count - 1 ? previousBands[i] : -1;
                if (!TryChooseBoundary(referenceDepthsM[i], referenceDepthsM[i + 1], haze, ray, s, prev, out bounds[i], out boundaryBands[i], out reason)) { boundaryBands = null; return false; }
            }
            slabs = new PixelSlab[Count];
            for (int i = 0; i < Count; i++)
            {
                var sl = new PixelSlab { ReferenceDepthM = referenceDepthsM[i], FromBand = i == 0 ? -1 : boundaryBands[i - 1] };
                sl.OwnFromM = i == 0 ? 0 : bounds[i - 1];
                sl.OwnToM = i == Count - 1 ? double.PositiveInfinity : bounds[i];
                sl.ClipNearM = i == 0 ? s.CameraNearM : Math.Max(s.CameraNearM, sl.OwnFromM / ray / (1 + s.OverlapFraction));
                sl.ClipFarM = i == Count - 1 ? s.CameraFarM : Math.Min(s.CameraFarM, sl.OwnToM * (1 + s.OverlapFraction));
                slabs[i] = sl;
            }
            return true;
        }

        /// <summary>The slab that owns a radial distance.</summary>
        public static int Owner(double radialM, PixelSlab[] slabs)
        {
            for (int i = 0; i < slabs.Length; i++) if (radialM >= slabs[i].OwnFromM && radialM < slabs[i].OwnToM) return i;
            return slabs.Length - 1;
        }

        /// <summary>
        /// Composite rule for one output pixel (mirrored by the present shader): each slab is sampled at its own shift; the first slab, near to far, whose sample
        /// is covered and lies in its own range wins; if none does (a gap where the shifts differ), the covered sample with the smallest radial distance; if no
        /// sample is covered, the far slab (sky).
        /// </summary>
        public static int Composite(PixelSlab[] slabs, bool[] covered, double[] radialM)
        {
            int n = slabs.Length;
            for (int i = 0; i < n; i++) if (covered[i] && radialM[i] >= slabs[i].OwnFromM && radialM[i] < slabs[i].OwnToM) return i;
            int best = -1;
            for (int i = 0; i < n; i++) if (covered[i] && (best < 0 || radialM[i] < radialM[best])) best = i;
            return best < 0 ? n - 1 : best;
        }

        /// <summary>
        /// Relative mismatch allowed between a slab's reference depth and the rig's grid (grid / GridM(reference) - 1). The C11 lock only holds when the grid is one
        /// texel at the reference depth; a supplied grid further off than this is rejected (T-181 design note: the model fails at 0.25 % depth mismatch).
        /// </summary>
        public const double GridTolerance = 1e-4;

        public static bool GridMatches(double gridM, double referenceDepthM, double tanHalfVertical, int internalHeight)
        {
            double want = GridM(referenceDepthM, tanHalfVertical, internalHeight);
            return want > 0 && Math.Abs(gridM / want - 1) <= GridTolerance;
        }
    }
}
