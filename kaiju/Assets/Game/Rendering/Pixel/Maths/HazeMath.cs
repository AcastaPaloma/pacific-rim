using System;

namespace Game.Rendering.Pixel
{
    /// <summary>
    /// Aerial perspective in posterised bands by real radial distance. The bands are spaced evenly in log distance
    /// between a start distance (below it: no haze) and a full distance (beyond it: pure airlight):
    /// level = (bands - 1) * ln(d / start) / ln(full / start). R-003 §5 derived bands at equal steps of Koschmieder
    /// transmittance; with a visual range of tens of kilometres that leaves everything inside several hundred metres
    /// in band 0, so an 80 m form 150 m away cannot separate from what is behind it (reconciled spec C7). The spec
    /// tests outcomes (monotone value by depth, contrast falling with distance), not the band formula, so the spacing
    /// is a per-profile pair of distances. The shader mirrors these formulas (PixelHaze.shader).
    /// </summary>
    public static class HazeMath
    {
        /// <summary>Continuous haze level in [0, bands - 1].</summary>
        public static double Level(double distanceMetres, double startMetres, double fullMetres, int bands)
        {
            if (!(startMetres > 0) || !(fullMetres > startMetres)) throw new ArgumentOutOfRangeException(nameof(startMetres));
            if (bands <= 1 || distanceMetres <= startMetres) return 0;
            double t = Math.Log(distanceMetres / startMetres) / Math.Log(fullMetres / startMetres);
            return (bands - 1) * (t > 1 ? 1 : t);
        }

        /// <summary>Band index in [0, bands - 1]: floor(level + seamWidth * (threshold - 0.5)), clamped.</summary>
        public static int Band(double distanceMetres, double startMetres, double fullMetres, int bands, double seamWidth = 0, double threshold = 0.5)
        {
            int b = (int)Math.Floor(Level(distanceMetres, startMetres, fullMetres, bands) + seamWidth * (threshold - 0.5));
            return b < 0 ? 0 : (b > bands - 1 ? bands - 1 : b);
        }

        /// <summary>Haze amount in [0, 1] of a band: band / (bands - 1). Band 0 is unhazed, the last band is pure airlight.</summary>
        public static double Amount(int band, int bands) => bands <= 1 ? 0 : band / (double)(bands - 1);

        /// <summary>Distance (m) at which band <paramref name="band"/> starts.</summary>
        public static double BandStart(int band, double startMetres, double fullMetres, int bands)
        {
            if (band <= 0) return 0;
            if (band > bands - 1) return double.PositiveInfinity;
            return startMetres * Math.Pow(fullMetres / startMetres, band / (double)(bands - 1));
        }
    }
}
