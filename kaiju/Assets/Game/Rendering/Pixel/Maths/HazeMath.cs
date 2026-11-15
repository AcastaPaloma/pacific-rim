using System;

namespace Game.Rendering.Pixel
{
    /// <summary>
    /// Aerial perspective in posterised bands by real radial distance. The bands are spaced evenly in log distance
    /// between a start distance (below it: no haze) and a full distance (beyond it: pure airlight):
    /// level = (bands - 1) * pow(ln(d / start) / ln(full / start), gamma); gamma 1 is the pure log spacing, below 1 lifts near levels. R-003 §5 derived bands at equal steps of Koschmieder
    /// transmittance; with a visual range of tens of kilometres that leaves everything inside several hundred metres
    /// in band 0, so an 80 m form 150 m away cannot separate from what is behind it (reconciled spec C7). The spec
    /// tests outcomes (monotone value by depth, contrast falling with distance), not the band formula, so the spacing
    /// is a per-profile pair of distances. The shader mirrors these formulas (PixelHaze.shader).
    /// </summary>
    public static class HazeMath
    {
        /// <summary>Continuous haze level in [0, bands - 1].</summary>
        public static double Level(double distanceMetres, double startMetres, double fullMetres, int bands, double gamma = 1.0)
        {
            if (!(startMetres > 0) || !(fullMetres > startMetres)) throw new ArgumentOutOfRangeException(nameof(startMetres));
            if (bands <= 1 || distanceMetres <= startMetres) return 0;
            double t = Math.Log(distanceMetres / startMetres) / Math.Log(fullMetres / startMetres);
            t = t > 1 ? 1 : t;
            return (bands - 1) * (gamma == 1.0 ? t : Math.Pow(t, gamma));
        }

        /// <summary>
        /// T-190 far lift: the level of <see cref="Level"/> pushed toward the last band beyond a far start distance:
        /// level + (bands - 1 - level) * w^power, w = clamp(ln(d / farStart) / ln(farFull / farStart), 0, 1). Equal to <paramref name="level"/> at and below farStart
        /// (continuous), the last band from farFull on. farStart &lt;= 0 or farFull &lt;= farStart means no lift.
        /// </summary>
        public static double FarLift(double level, double distanceMetres, int bands, double farStartMetres, double farFullMetres, double power = 1.0)
        {
            if (!(farStartMetres > 0) || !(farFullMetres > farStartMetres) || bands <= 1 || distanceMetres <= farStartMetres) return level;
            double w = Math.Log(distanceMetres / farStartMetres) / Math.Log(farFullMetres / farStartMetres);
            w = w > 1 ? 1 : w;
            if (power != 1.0) w = Math.Pow(w, power);
            return level + (bands - 1 - level) * w;
        }

        /// <summary>
        /// Band index in [0, bands - 1] with a dithered seam. The seam reaches <paramref name="seamTexels"/> texels to
        /// each side of a band boundary, measured along the surface: its half width in bands is
        /// min(seamTexels * levelGradient, seamWidthMax / 2), where levelGradient is the change of level to the next
        /// texel of the same surface. Inside the seam the threshold picks the upper band with a probability that rises
        /// linearly across it; outside, and on a face at one distance (gradient 0), the band is floor(level).
        /// </summary>
        public static int Band(double level, int bands, double levelGradient = 0, double seamTexels = 0, double seamWidthMax = 0, double threshold = 0.5)
        {
            double boundary = Math.Floor(level + 0.5);
            double halfWidth = Math.Min(seamTexels * levelGradient, 0.5 * seamWidthMax);
            double band = Math.Floor(level);
            if (halfWidth > 1e-5 && Math.Abs(level - boundary) < halfWidth)
                band = threshold < 0.5 + (level - boundary) / (2.0 * halfWidth) ? boundary : boundary - 1.0;
            int b = (int)band;
            return b < 0 ? 0 : (b > bands - 1 ? bands - 1 : b);
        }

        /// <summary>Band index of a distance without a seam.</summary>
        public static int Band(double distanceMetres, double startMetres, double fullMetres, int bands) => Band(Level(distanceMetres, startMetres, fullMetres, bands), bands);

        /// <summary>Haze amount in [0, 1] of a band: band / (bands - 1). Band 0 is unhazed, the last band is pure airlight.</summary>
        public static double Amount(int band, int bands) => bands <= 1 ? 0 : band / (double)(bands - 1);

        /// <summary>Distance (m) at which band <paramref name="band"/> starts.</summary>
        public static double BandStart(int band, double startMetres, double fullMetres, int bands) => BandStart(band, startMetres, fullMetres, bands, 1.0);

        /// <summary>Distance (m) at which band <paramref name="band"/> starts under a curve gamma (the inverse of <see cref="Level"/>): start * (full / start)^((band / (bands - 1))^(1 / gamma)).</summary>
        public static double BandStart(int band, double startMetres, double fullMetres, int bands, double gamma)
        {
            if (!(gamma > 0)) throw new ArgumentOutOfRangeException(nameof(gamma));
            if (band <= 0) return 0;
            if (band > bands - 1) return double.PositiveInfinity;
            double u = band / (double)(bands - 1);
            return startMetres * Math.Pow(fullMetres / startMetres, gamma == 1.0 ? u : Math.Pow(u, 1.0 / gamma));
        }
    }
}
