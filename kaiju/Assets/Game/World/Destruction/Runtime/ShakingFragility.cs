// Ground-shaking fragility route of research/R-021.md §2.4. Pure functions, no state, no allocation.
// PGA input is in g (R-022 §3 interface: per building PgaG). Chain (decided C-3, M-051): PGA -> Sa -> Sd -> drift -> P(DS >= k).
using System;

namespace Game.World.Destruction
{
    public static class ShakingFragility
    {
        /// <summary>Sa in g: PGA * min(2.5, 1.5 / T) [Assumed shape, R-021 §2.4].</summary>
        public static double SpectralAcceleration(double pgaG, double periodS)
        {
            double f = DamageParameters.SpectrumCornerS / periodS;
            return pgaG * (f < DamageParameters.SpectrumPlateau ? f : DamageParameters.SpectrumPlateau);
        }

        /// <summary>Sd in m: Sa g T^2 / 4 pi^2.</summary>
        public static double SpectralDisplacement(double pgaG, double periodS)
        {
            return SpectralAcceleration(pgaG, periodS) * DamageParameters.Gravity * periodS * periodS / (4.0 * Math.PI * Math.PI);
        }

        /// <summary>Peak roof drift ratio demand: Sd / (0.75 H) [Assumed 0.75].</summary>
        public static double DriftDemand(double pgaG, double periodS, double heightM)
        {
            return SpectralDisplacement(pgaG, periodS) / (DamageParameters.RoofToSpectral * heightM);
        }

        /// <summary>P(state &gt;= k | PGA) = Phi(ln(theta / theta_k) / beta_tot), k in 1..4 [S9, Assumed beta_D].</summary>
        public static double Exceedance(HazusType type, double heightM, double periodS, double pgaG, int k, CodeEra era = CodeEra.ModerateCode)
        {
            if (k < 1) return 1.0;
            if (k > 4 || !(pgaG > 0) || double.IsInfinity(pgaG) || !(heightM > 0) || !(periodS > 0)) return 0.0;
            double theta = DriftDemand(pgaG, periodS, heightM);
            return NormalCdf(Math.Log(theta / DamageParameters.DriftMedian(type, k, DamageParameters.EffectiveEra(type, era))) / DamageParameters.ShakingBetaTotal);
        }

        /// <summary>P(state &gt;= k | PGA) for a building, k = 1..4. Pure; this is the function the M-051 tool compares.</summary>
        public static double Exceedance(in BuildingAttributes a, double pgaG, int k)
        {
            double h = a.HeightM > 0 ? a.HeightM : DamageParameters.MetresPerLevel;
            int n = CapacityModel.ResolveLevels(a);
            var type = CapacityModel.TypeOf(CapacityModel.ClassOf(n), a.Residential);
            double t = DamageParameters.PeriodCt * Math.Pow(h, DamageParameters.PeriodExponent);
            return Exceedance(type, h, t, pgaG, k, DamageParameters.EraFromYear(a.YearBuilt));
        }

        /// <summary>State drawn from uniform u: max k with u &lt; P_k (0 if none). Monotone in PGA for fixed u.</summary>
        public static byte StateFor(HazusType type, double heightM, double periodS, double pgaG, double u, CodeEra era = CodeEra.ModerateCode)
        {
            for (int k = 4; k >= 1; k--)
                if (u < Exceedance(type, heightM, periodS, pgaG, k, era)) return (byte)k;
            return 0;
        }

        /// <summary>Standard normal CDF via erfc (Numerical Recipes erfcc, |err| &lt; 1.2e-7).</summary>
        public static double NormalCdf(double x)
        {
            double z = Math.Abs(x) / Math.Sqrt(2.0);
            double t = 1.0 / (1.0 + 0.5 * z);
            double r = t * Math.Exp(-z * z - 1.26551223 + t * (1.00002368 + t * (0.37409196 + t * (0.09678418 +
                       t * (-0.18628806 + t * (0.27886807 + t * (-1.13520398 + t * (1.48851587 +
                       t * (-0.82215223 + t * 0.17087277)))))))));
            return x >= 0 ? 1.0 - 0.5 * r : 0.5 * r;
        }
    }
}
