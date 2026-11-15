// BSSA14 NGA-West2 median PGA/PGV, strike-slip, and Worden 2012 MMI from PGV. Source: research/R-022.md section 3.
// ln Y = FE + FP + FS. Basin-depth term (delta z1) = 0 (Assumed). No sigma (median only).
using System;

namespace Game.World.Seismic
{
    public static class Bssa14
    {
        /// <summary>Coefficients as transcribed in the OpenQuake zalachoris_rathje_2019 table, 3 significant figures (R-022 section 3, [S4]); the BSSA14 paper's own table was not read.</summary>
        public struct Coef
        {
            public double E1, E4, E5, E6, Mh, C1, C2, C3, H, Clin, Vc, F4, F5;
        }

        public static readonly Coef Pga = new Coef { E1 = 0.486, E4 = 1.43, E5 = 0.0505, E6 = -0.166, Mh = 5.5, C1 = -1.13, C2 = 0.192, C3 = -0.00809, H = 4.5, Clin = -0.60, Vc = 1500, F4 = -0.15, F5 = -0.00701 };
        public static readonly Coef Pgv = new Coef { E1 = 5.08, E4 = 1.07, E5 = -0.154, E6 = 0.225, Mh = 6.2, C1 = -1.24, C2 = 0.149, C3 = -0.00344, H = 5.3, Clin = -0.84, Vc = 1300, F4 = -0.10, F5 = -0.00844 };

        public const double Mref = 4.5, Rref = 1.0, Vref = 760.0;
        /// <summary>Worden 2012 branch point, log10 PGV (cm/s).</summary>
        public const double MmiBreakLogPgv = 0.53;

        static double Fe(Coef c, double m)
        {
            double d = m - c.Mh;
            return m <= c.Mh ? c.E1 + c.E4 * d + c.E5 * d * d : c.E1 + c.E6 * d;
        }

        static double Fp(Coef c, double m, double rjbKm)
        {
            double r = Math.Sqrt(rjbKm * rjbKm + c.H * c.H);
            return (c.C1 + c.C2 * (m - Mref)) * Math.Log(r / Rref) + c.C3 * (r - Rref);
        }

        /// <summary>Median at Vs30 = 760 (PGAr when used with <see cref="Pga"/>), g or cm/s.</summary>
        static double Rock(Coef c, double m, double rjbKm) { return Math.Exp(Fe(c, m) + Fp(c, m, rjbKm)); }

        /// <summary>Median PGA in g.</summary>
        public static double PgaG(double mw, double rjbKm, double vs30)
        {
            return Site(Pga, mw, rjbKm, vs30);
        }

        /// <summary>Median PGV in cm/s.</summary>
        public static double PgvCmS(double mw, double rjbKm, double vs30)
        {
            return Site(Pgv, mw, rjbKm, vs30);
        }

        static double Site(Coef c, double m, double rjbKm, double vs30)
        {
            double pgar = Rock(Pga, m, rjbKm);
            double lin = c.Clin * Math.Log(Math.Min(vs30, c.Vc) / Vref);
            double f2 = c.F4 * (Math.Exp(c.F5 * (Math.Min(vs30, Vref) - 360.0)) - Math.Exp(c.F5 * 400.0));
            double nl = f2 * Math.Log((pgar + 0.1) / 0.1);
            return Math.Exp(Fe(c, m) + Fp(c, m, rjbKm) + lin + nl);
        }

        /// <summary>MMI from PGV (cm/s), Worden et al. 2012 as listed by CGS MS-48 (R-022 section 3).</summary>
        public static double Mmi(double pgvCmS)
        {
            double l = Math.Log10(Math.Max(pgvCmS, 1e-9));
            return l <= MmiBreakLogPgv ? 3.78 + 1.47 * l : 2.89 + 3.16 * l;
        }
    }
}
