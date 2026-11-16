using System;
using System.Globalization;
using System.Text;

namespace Game.Rendering.Pixel
{
    /// <summary>
    /// The <c>layers</c> member of a style_capture@v1 frame sidecar (T-182, versioned key <see cref="Schema"/>): what the layered path used for one frame.
    /// Engine-free so the writer is tested without Unity. Absent from sidecars of frames rendered on the single-snap path (layerSnap off or hints rejected).
    /// </summary>
    public static class PixelLayerSidecar
    {
        public const string Schema = "pixel_layers@v1";

        /// <summary>
        /// Formats the member value (a JSON object, no trailing newline). <paramref name="shiftsOutputPx"/> holds near / main / far as x, y pairs (length 6),
        /// as applied by the present stage (zero when sub-pixel compensation is off). <paramref name="sharedPhase"/>: the shifts share one lattice phase (T-196, member <c>shared_phase</c>). An unbounded upper edge (the far slab) is written as null.
        /// </summary>
        public static string Build(in PixelStabilisationHints hints, PixelSlab[] slabs, int[] boundaryBands, int[] shiftsOutputPx, int scale, int guardTexels, bool subPixelApplied, bool depthShiftIgnored, bool sharedPhase = false)
        {
            if (slabs == null || slabs.Length != PixelLayerPlan.Count) throw new ArgumentException("need three slabs", nameof(slabs));
            if (shiftsOutputPx == null || shiftsOutputPx.Length != 2 * PixelLayerPlan.Count) throw new ArgumentException("need three x, y shift pairs", nameof(shiftsOutputPx));
            PixelStabilisationHints h = hints;
            var j = new StringBuilder(768);
            j.Append("{\"schema\": \"").Append(Schema).Append("\", \"count\": ").Append(PixelLayerPlan.Count);
            j.Append(", \"slab_names\": [\"near\", \"main\", \"far\"]");
            j.Append(", \"references_m\": ").Append(Arr(i => F(PixelLayerHints.Get(h, i).ReferenceDepth)));
            j.Append(", \"grids_m\": ").Append(Arr(i => F(PixelLayerHints.Get(h, i).Grid)));
            j.Append(", \"offsets_m\": ").Append(Pairs(i => F(PixelLayerHints.Get(h, i).OffsetRight), i => F(PixelLayerHints.Get(h, i).OffsetUp)));
            j.Append(", \"remainder_own_texels\": ").Append(Pairs(i => Round4(F(PixelLayerHints.Get(h, i).SubTexelX)), i => Round4(F(PixelLayerHints.Get(h, i).SubTexelY))));
            j.Append(", \"shift_output_px\": ").Append(Pairs(i => shiftsOutputPx[2 * i], i => shiftsOutputPx[2 * i + 1]));
            j.Append(", \"layer_cut\": [").Append(Join(i => PixelLayerHints.Get(h, i).Cut ? "true" : "false")).Append("]");
            j.Append(", \"boundaries_radial_m\": [").Append(Num(slabs[1].OwnFromM)).Append(", ").Append(Num(slabs[2].OwnFromM)).Append("]");
            j.Append(", \"boundary_haze_bands\": [").Append(boundaryBands != null && boundaryBands.Length == 2 ? boundaryBands[0] + ", " + boundaryBands[1] : "-1, -1").Append("]");
            j.Append(", \"slab_clip_eye_m\": [");
            for (int i = 0; i < slabs.Length; i++) j.Append(i > 0 ? ", " : "").Append("[").Append(Num(slabs[i].ClipNearM)).Append(", ").Append(Num(slabs[i].ClipFarM)).Append("]");
            j.Append("], \"scale\": ").Append(scale).Append(", \"guard_texels\": ").Append(guardTexels);
            j.Append(", \"subpixel_applied\": ").Append(subPixelApplied ? "true" : "false").Append(", \"depth_shift_ignored\": ").Append(depthShiftIgnored ? "true" : "false");
            // T-196: written only when the shifts share one lattice phase, so sidecars of the per-slab path are unchanged.
            if (sharedPhase) j.Append(", \"shared_phase\": true");
            j.Append(", \"composite\": \"first slab near to far that is covered and owns the sample (radial distance); a gap takes the covered sample with the smallest radial distance; nothing covered is the far slab (sky)\"}");
            return j.ToString();
        }

        static string Join(Func<int, string> f)
        {
            var s = new StringBuilder();
            for (int i = 0; i < PixelLayerPlan.Count; i++) s.Append(i > 0 ? ", " : "").Append(f(i));
            return s.ToString();
        }
        static string Arr(Func<int, double> f) => "[" + Join(i => Num(f(i))) + "]";
        static string Pairs(Func<int, double> x, Func<int, double> y) => "[" + Join(i => "[" + Num(x(i)) + ", " + Num(y(i)) + "]") + "]";
        /// <summary>A float as the double of its shortest round-trip text (124.2f is written 124.2, not 124.199997).</summary>
        static double F(float v) => double.Parse(v.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        static double Round4(double v) { double r = Math.Round(v, 4); return r == 0 ? 0 : r; }
        static string Num(double v) => double.IsInfinity(v) || double.IsNaN(v) ? "null" : v.ToString("0.######", CultureInfo.InvariantCulture);
    }
}
