// Every numeric constant of the capacity model, in one place. Tags are research/R-021.md source tags:
// S9 = Hazus 6.1 fragility.csv (NHERI SimCenter DLML), Assumed = no source, a tuning default. Changing any value bumps
// DestructionContractVersions.ModelRevision.
namespace Game.World.Destruction
{
    /// <summary>Rise class (R-021 §2.1): A low (n &lt;= 3), B mid (4-7), C high (&gt;= 8).</summary>
    public enum RiseClass : byte { A = 0, B = 1, C = 2 }

    /// <summary>Hazus model building type chosen per rise class (a choice: geo_export has no structure type).</summary>
    public enum HazusType : byte { W1 = 0, C2L = 1, C2M = 2, C2H = 3 }

    /// <summary>Hazus code era (R-021 §2.4): selects the PC / LC / MC / HC drift-median rows.</summary>
    public enum CodeEra : byte { PreCode = 0, LowCode = 1, ModerateCode = 2, HighCode = 3 }

    public static class DamageParameters
    {
        /// <summary>
        /// Code era from year built for high-seismicity California (UBC Seismic Zone 4 / NEHRP map area 7):
        /// pre-1941 Pre-Code, 1941-1975 Moderate-Code, post-1975 High-Code; there is no Low-Code band in Zone 4.
        /// Source: USGS ShakeCast Appendix D, https://usgs.github.io/shakecast/v3_operator_appendd.html [S4]
        /// (read 2026-11-14). year &lt;= 0 = unknown = ModerateCode (the behaviour before this step).
        /// </summary>
        public static CodeEra EraFromYear(int yearBuilt)
        {
            if (yearBuilt <= 0) return CodeEra.ModerateCode;
            if (yearBuilt < 1941) return CodeEra.PreCode;
            return yearBuilt <= 1975 ? CodeEra.ModerateCode : CodeEra.HighCode;
        }

        /// <summary>
        /// Era actually used for a type: S4 says pre-1941 W1 is Moderate-Code (the same table, "W1 = Moderate-Code"),
        /// so W1 PreCode maps to ModerateCode. The W1 PC row of [S9] stays available through the 3-argument DriftMedian.
        /// </summary>
        public static CodeEra EffectiveEra(HazusType t, CodeEra era)
        {
            return t == HazusType.W1 && era == CodeEra.PreCode ? CodeEra.ModerateCode : era;
        }

        /// <summary>Levels from height: 4.22 m per level, median of 202 SF buildings [R-004 §1.4].</summary>
        public const double MetresPerLevel = 4.22;
        /// <summary>Rise class bounds on levels: A &lt;= 3, B &lt;= 7, C otherwise [S1, unverified bounds].</summary>
        public const int LowRiseMaxLevels = 3;
        public const int MidRiseMaxLevels = 7;
        /// <summary>Floor mass density, kg/m² of floor area [Assumed; 150 mm RC slab is 366 kg/m² (reasoned)].</summary>
        public const double FloorMassKgPerM2 = 600.0;
        /// <summary>Light low-rise (residential, n &lt;= 3) floor mass density, kg/m² [Assumed].</summary>
        public const double LightFloorMassKgPerM2 = 250.0;
        /// <summary>Effective modal mass share m_e / M0 [Assumed; Hazus alpha2 form S1].</summary>
        public const double ModalMassFactor = 0.75;
        /// <summary>Period T = Ct H^x, SI form of ASCE 7 "all other systems" (Ct = 0.0488, x = 0.75) [S3, unverified].</summary>
        public const double PeriodCt = 0.0488;
        public const double PeriodExponent = 0.75;
        /// <summary>Gravity, m/s².</summary>
        public const double Gravity = 9.80665;
        /// <summary>Roof drift to spectral displacement factor Sd = 0.75 Delta [Assumed].</summary>
        public const double RoofToSpectral = 0.75;
        /// <summary>Capacity scatter beta_E, lognormal, same as the Hazus 6.1 drift beta [S9].</summary>
        public const double BetaE = 0.4;
        /// <summary>Scatter z is clamped to +-this many sigma so no building is absurdly weak or strong [Assumed].</summary>
        public const double ZClamp = 3.0;

        /// <summary>Yield strength coefficient Cy (g) per rise class A, B, C [Assumed].</summary>
        public static double YieldCoefficient(RiseClass c)
        {
            switch (c) { case RiseClass.A: return 0.25; case RiseClass.B: return 0.15; default: return 0.10; }
        }

        /// <summary>
        /// Median peak roof drift ratio (rad) to reach state k (1 Slight .. 4 Complete), Hazus 6.1 Moderate-Code,
        /// structural, beta 0.4 [S9]. W1 0.004/0.0099/0.0306/0.075; C2.L 0.004/0.0084/0.0232/0.06;
        /// C2.M 0.0027/0.0056/0.0154/0.04; C2.H 0.002/0.0042/0.0116/0.03.
        /// </summary>
        public static double DriftMedian(HazusType t, int k) { return DriftMedian(t, k, CodeEra.ModerateCode); }

        /// <summary>
        /// Era rows of Hazus 6.1 structural fragility, median peak roof drift, beta 0.4, read from [S9] fragility.csv
        /// (STR.W1|C2.L|C2.M|C2.H . PC|LC|MC|HC). All 16 rows sourced; no gaps.
        /// </summary>
        public static double DriftMedian(HazusType t, int k, CodeEra era)
        {
            if (era != CodeEra.ModerateCode) { var r = EraRow(t, era); return r[(k < 1 ? 1 : k > 4 ? 4 : k) - 1]; }
            switch (t)
            {
                case HazusType.W1:
                    return k == 1 ? 0.004 : k == 2 ? 0.0099 : k == 3 ? 0.0306 : 0.075;
                case HazusType.C2L:
                    return k == 1 ? 0.004 : k == 2 ? 0.0084 : k == 3 ? 0.0232 : 0.06;
                case HazusType.C2M:
                    return k == 1 ? 0.0027 : k == 2 ? 0.0056 : k == 3 ? 0.0154 : 0.04;
                default:
                    return k == 1 ? 0.002 : k == 2 ? 0.0042 : k == 3 ? 0.0116 : 0.03;
            }
        }

        static double[] EraRow(HazusType t, CodeEra era)
        {
            switch (t)
            {
                case HazusType.W1:
                    return era == CodeEra.PreCode ? W1PC : era == CodeEra.LowCode ? W1LC : W1HC;
                case HazusType.C2L:
                    return era == CodeEra.PreCode ? C2LPC : era == CodeEra.LowCode ? C2LLC : C2LHC;
                case HazusType.C2M:
                    return era == CodeEra.PreCode ? C2MPC : era == CodeEra.LowCode ? C2MLC : C2MHC;
                default:
                    return era == CodeEra.PreCode ? C2HPC : era == CodeEra.LowCode ? C2HLC : C2HHC;
            }
        }
        static readonly double[] W1PC = { 0.0032, 0.0079, 0.0245, 0.06 }, W1LC = { 0.004, 0.0099, 0.0306, 0.075 }, W1HC = { 0.004, 0.012, 0.04, 0.1 };
        static readonly double[] C2LPC = { 0.0032, 0.0061, 0.0158, 0.04 }, C2LLC = { 0.004, 0.0076, 0.0197, 0.05 }, C2LHC = { 0.004, 0.01, 0.03, 0.08 };
        static readonly double[] C2MPC = { 0.0021, 0.0041, 0.0105, 0.0267 }, C2MLC = { 0.0027, 0.0051, 0.0132, 0.0333 }, C2MHC = { 0.0027, 0.0067, 0.02, 0.0533 };
        static readonly double[] C2HPC = { 0.0016, 0.0031, 0.0079, 0.02 }, C2HLC = { 0.002, 0.0038, 0.0099, 0.025 }, C2HHC = { 0.002, 0.005, 0.015, 0.04 };

        /// <summary>Hazus 6.1 drift fragility beta of the capacity side, theta_k lognormal [S9] (R-021 §2.4).</summary>
        public const double ShakingBetaCapacity = 0.4;
        /// <summary>Demand variability beta_D of the shaking route [Assumed] (R-021 §2.4).</summary>
        public const double ShakingBetaDemand = 0.5;
        /// <summary>Total shaking beta = sqrt(0.4^2 + 0.5^2) = 0.64 [S9 + Assumed] (R-021 §2.4).</summary>
        public static readonly double ShakingBetaTotal = System.Math.Sqrt(ShakingBetaCapacity * ShakingBetaCapacity + ShakingBetaDemand * ShakingBetaDemand);
        /// <summary>Design-spectrum plateau factor: Sa = PGA * min(2.5, 1.5 / T) [Assumed] (R-021 §2.4).</summary>
        public const double SpectrumPlateau = 2.5;
        /// <summary>Design-spectrum 1/T branch numerator (s): Sa = PGA * min(2.5, 1.5 / T) [Assumed] (R-021 §2.4).</summary>
        public const double SpectrumCornerS = 1.5;
        /// <summary>Event index of the first shaking draw; index 0 is the capacity scatter (R-021 §2.5) [Assumed].</summary>
        public const int FirstEventIndex = 1;

        /// <summary>Salt mixed into the ID hash for the capacity scatter (event index 0 of R-021 §2.5) [Assumed].</summary>
        public const ulong ScatterSalt = 0xD1B54A32D192ED03UL;

        // ---- debris, mass and energy (R-021 §3) ----
        /// <summary>Hazus collapse fraction phi of the Complete state: W1 0.03, C2.L 0.13, C2.M 0.10, C2.H 0.05 [S9].</summary>
        public static double CollapsePhi(HazusType t)
        {
            switch (t) { case HazusType.W1: return 0.03; case HazusType.C2L: return 0.13; case HazusType.C2M: return 0.10; default: return 0.05; }
        }
        /// <summary>Debris share of building mass f_d: 0, 0.01, 0.05, 0.25 for k = 0..3 [Assumed]; Complete = phi + (1 - phi) 0.25.</summary>
        public static double DebrisFraction(HazusType t, int k)
        {
            switch (k)
            {
                case 0: return 0.0;
                case 1: return 0.01;
                case 2: return 0.05;
                case 3: return 0.25;
                default: { double phi = CollapsePhi(t); return phi + (1.0 - phi) * 0.25; }
            }
        }
        /// <summary>Bulk rubble density, kg/m3 [Assumed].</summary>
        public const double RubbleDensity = 1200.0;
        /// <summary>Heap ring width as a fraction of height H: spread = footprint plus a ring of 0.5 H [Assumed].</summary>
        public const double SpreadRingFactor = 0.5;
        /// <summary>Fracture and comminution energy, J per kg of debris [Assumed].</summary>
        public const double FractureJPerKg = 500.0;
        /// <summary>Fracture energy is at most this share of ΔPE (a low building's fall cannot pay 500 J/kg; the rest came from the impact work already in the state) [Assumed].</summary>
        public const double FractureCapShare = 0.25;
        /// <summary>Ejecta kinetic energy as a share of ΔPE [Assumed]; the account caps it at <see cref="EjectaCapShare"/>.</summary>
        public const double EjectaShare = 0.05;
        public const double EjectaCapShare = 0.10;
        /// <summary>Share of the remaining energy that the inelastic ground impact takes; the rest is the residual [Assumed].</summary>
        public const double GroundShare = 0.98;
        /// <summary>Debris bodies per building, upper bound (R-021 §3).</summary>
        public const int MaxPieces = 64;
        /// <summary>Preferred mass of one debris body, kg [Assumed]; the piece count is ceil(M_d / this), capped at MaxPieces.</summary>
        public const double PieceTargetKg = 2000.0;
    }
}
