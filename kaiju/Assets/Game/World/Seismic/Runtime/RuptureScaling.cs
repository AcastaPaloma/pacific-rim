// Rupture scaling: Wells & Coppersmith 1994 (BSSA 84(4) 974-1002) Table 2A, strike-slip (SS), as transcribed in research/R-022.md
// section 2 [S2] (OCR of the scanned table; R-022 did not mark any value uncertain). Every coefficient lives in Wc94StrikeSlip.
// Relations: M = a + b log10(X) and log10(Y) = a + b M. SRL km, MD / AD m (maximum / average surface displacement), W km, A km2.
// Medians only; the sigma of each relation is exposed for M-054 (R-022 section 5). M > 7.5 is extrapolation [S6].
using System;

namespace Game.World.Seismic
{
    /// <summary>One regression line: a + b * x, with its standard deviation, sample size and validity range of the INDEPENDENT variable.</summary>
    public readonly struct Wc94Relation
    {
        public readonly double A, B, Sigma;
        public readonly int N;
        public readonly double Min, Max;
        public Wc94Relation(double a, double b, double sigma, int n, double min, double max)
        { A = a; B = b; Sigma = sigma; N = n; Min = min; Max = max; }
        public double Eval(double x) { return A + B * x; }
        public bool InRange(double x) { return x >= Min && x <= Max; }
    }

    /// <summary>WC94 Table 2A strike-slip coefficients (R-022 section 2 table). The single place the numbers are written.</summary>
    public static class Wc94StrikeSlip
    {
        // M = a + b log SRL ; validity: SRL 1.3-432 km (M 5.6-8.1)
        public static readonly Wc94Relation MFromSrl = new Wc94Relation(5.16, 1.12, 0.28, 43, 1.3, 432.0);
        // log SRL = a + b M ; validity: M 5.6-8.1
        public static readonly Wc94Relation LogSrlFromM = new Wc94Relation(-3.55, 0.74, 0.23, 43, 5.6, 8.1);
        // M = a + b log MD ; validity: MD 0.01-14.6 m
        public static readonly Wc94Relation MFromMd = new Wc94Relation(6.81, 0.78, 0.29, 43, 0.01, 14.6);
        // log MD = a + b M ; validity: same M range as SRL (R-022: "same")
        public static readonly Wc94Relation LogMdFromM = new Wc94Relation(-7.03, 1.03, 0.34, 43, 5.6, 8.1);
        // M = a + b log AD ; validity: AD 0.05-8.0 m
        public static readonly Wc94Relation MFromAd = new Wc94Relation(7.04, 0.89, 0.28, 29, 0.05, 8.0);
        // log AD = a + b M ; validity: same M range (R-022: "same")
        public static readonly Wc94Relation LogAdFromM = new Wc94Relation(-6.32, 0.90, 0.28, 29, 5.6, 8.1);
        // log W = a + b M ; validity: M 4.8-8.1
        public static readonly Wc94Relation LogWidthFromM = new Wc94Relation(-0.76, 0.27, 0.14, 87, 4.8, 8.1);
        // M = a + b log A ; validity: A 3-5184 km2
        public static readonly Wc94Relation MFromArea = new Wc94Relation(3.98, 1.02, 0.23, 83, 3.0, 5184.0);

        /// <summary>Paper statement used by R-022: average displacement per event is about half the maximum [S2]. Not used by the default scenario (it uses the AD regression).</summary>
        public const double AverageToMaximumRatio = 0.5;

        /// <summary>Magnitude validity range of the M -> L / D relations (R-022 section 2).</summary>
        public const double MwMin = 5.6, MwMax = 8.1;
        /// <summary>Above this the regressions extrapolate and underestimate large strike-slip events [S6].</summary>
        public const double MwExtrapolationAbove = 7.5;
    }

    /// <summary>Magnitude, rupture length, displacement, width and area, both directions (pure, deterministic).</summary>
    public static class RuptureScaling
    {
        /// <summary>Surface rupture length, km, from Mw.</summary>
        public static double SrlKmFromMw(double mw) { return Math.Pow(10.0, Wc94StrikeSlip.LogSrlFromM.Eval(mw)); }
        /// <summary>Mw from surface rupture length, km. The M-on-log-L fit differs from inverting the L-on-M fit (independent regressions).</summary>
        public static double MwFromSrlKm(double srlKm) { return Wc94StrikeSlip.MFromSrl.Eval(Math.Log10(srlKm)); }

        public static double MaxDisplacementMFromMw(double mw) { return Math.Pow(10.0, Wc94StrikeSlip.LogMdFromM.Eval(mw)); }
        public static double MwFromMaxDisplacementM(double mdM) { return Wc94StrikeSlip.MFromMd.Eval(Math.Log10(mdM)); }

        public static double AvgDisplacementMFromMw(double mw) { return Math.Pow(10.0, Wc94StrikeSlip.LogAdFromM.Eval(mw)); }
        public static double MwFromAvgDisplacementM(double adM) { return Wc94StrikeSlip.MFromAd.Eval(Math.Log10(adM)); }

        public static double WidthKmFromMw(double mw) { return Math.Pow(10.0, Wc94StrikeSlip.LogWidthFromM.Eval(mw)); }
        /// <summary>Rupture area, km2, by inverting M = a + b log A (R-022 table: 1436 km2 at 7.2).</summary>
        public static double AreaKm2FromMw(double mw) { return Math.Pow(10.0, (mw - Wc94StrikeSlip.MFromArea.A) / Wc94StrikeSlip.MFromArea.B); }
        public static double MwFromAreaKm2(double areaKm2) { return Wc94StrikeSlip.MFromArea.Eval(Math.Log10(areaKm2)); }

        /// <summary>Average displacement from maximum by the paper's "about half" statement.</summary>
        public static double AvgFromMaxDisplacementM(double mdM) { return mdM * Wc94StrikeSlip.AverageToMaximumRatio; }

        /// <summary>True when Mw is inside the WC94 strike-slip validity range 5.6-8.1.</summary>
        public static bool MwInRange(double mw) { return mw >= Wc94StrikeSlip.MwMin && mw <= Wc94StrikeSlip.MwMax; }
        /// <summary>True above 7.5, where the regression extrapolates (warn, do not refuse).</summary>
        public static bool IsExtrapolation(double mw) { return mw > Wc94StrikeSlip.MwExtrapolationAbove; }

        /// <summary>Medians for one magnitude, as the scenario and M-054 need them.</summary>
        public static RuptureSize SizeFromMw(double mw)
        {
            return new RuptureSize
            {
                Mw = mw,
                SrlKm = SrlKmFromMw(mw),
                MdM = MaxDisplacementMFromMw(mw),
                AdM = AvgDisplacementMFromMw(mw),
                WidthKm = WidthKmFromMw(mw),
                AreaKm2 = AreaKm2FromMw(mw),
            };
        }
    }

    /// <summary>Median rupture dimensions for one Mw.</summary>
    public struct RuptureSize
    {
        public double Mw, SrlKm, MdM, AdM, WidthKm, AreaKm2;
    }
}
