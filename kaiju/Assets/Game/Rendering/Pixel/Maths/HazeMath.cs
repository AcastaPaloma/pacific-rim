using System;

namespace Game.Rendering.Pixel
{
    /// <summary>
    /// Aerial perspective in posterised bands (R-003 §5): transmittance T = exp(-beta d) by real radial distance,
    /// Koschmieder visual range V = 3.912 / beta, bands at equal steps of T. The shader mirrors these formulas
    /// (PixelCommon.hlsl); EditMode tests compare the two through captures.
    /// </summary>
    public static class HazeMath
    {
        public const double KoschmiederConstant = 3.912;

        public static double Beta(double visibilityMetres)
        {
            if (visibilityMetres <= 0) throw new ArgumentOutOfRangeException(nameof(visibilityMetres));
            return KoschmiederConstant / visibilityMetres;
        }

        public static double Transmittance(double distanceMetres, double beta) => Math.Exp(-beta * Math.Max(0, distanceMetres));

        /// <summary>Band index in [0, bands-1]: floor(bands * (1 - T) + seamWidth * (threshold - 0.5)), clamped.</summary>
        public static int Band(double distanceMetres, double beta, int bands, double seamWidth = 0, double threshold = 0.5)
        {
            double v = bands * (1.0 - Transmittance(distanceMetres, beta)) + seamWidth * (threshold - 0.5);
            int b = (int)Math.Floor(v);
            return b < 0 ? 0 : (b > bands - 1 ? bands - 1 : b);
        }

        /// <summary>Haze amount in [0, 1] of a band: band / (bands - 1). Band 0 is unhazed, the last band is pure airlight.</summary>
        public static double Amount(int band, int bands) => bands <= 1 ? 0 : band / (double)(bands - 1);

        /// <summary>Distance (m) at which band <paramref name="band"/> starts: -ln(1 - band / bands) / beta.</summary>
        public static double BandStart(int band, int bands, double beta)
        {
            if (band <= 0) return 0;
            if (band >= bands) return double.PositiveInfinity;
            return -Math.Log(1.0 - band / (double)bands) / beta;
        }
    }
}
