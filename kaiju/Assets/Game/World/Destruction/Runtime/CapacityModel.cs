// Capacity model of research/R-021.md §2.1-2.2 and §2.5. Pure functions, no state, no allocation.
using System;
using Game.Core;

namespace Game.World.Destruction
{
    public struct Capacity
    {
        public RiseClass Rise;
        public HazusType Type;
        public int Levels;
        public double MassKg;      // M0
        public double PeriodS;     // T
        /// <summary>Median energy (no scatter) to reach state 1..4, J.</summary>
        public double E1, E2, E3, E4;
        /// <summary>Capacity energies with the building's scatter applied, J.</summary>
        public double S1, S2, S3, S4;
    }

    public static class CapacityModel
    {
        public static int ResolveLevels(in BuildingAttributes a)
        {
            if (a.Levels > 0) return a.Levels;
            int n = (int)Math.Round(a.HeightM / DamageParameters.MetresPerLevel, MidpointRounding.AwayFromZero);
            return n < 1 ? 1 : n;
        }

        public static RiseClass ClassOf(int levels)
        {
            return levels <= DamageParameters.LowRiseMaxLevels ? RiseClass.A
                 : levels <= DamageParameters.MidRiseMaxLevels ? RiseClass.B : RiseClass.C;
        }

        public static HazusType TypeOf(RiseClass c, bool residential)
        {
            switch (c)
            {
                case RiseClass.A: return residential ? HazusType.W1 : HazusType.C2L;
                case RiseClass.B: return HazusType.C2M;
                default: return HazusType.C2H;
            }
        }

        /// <summary>Median energy to reach state k (1..4), J: work under an elastic-perfectly-plastic pushover (§2.2).</summary>
        public static double MedianEnergy(HazusType type, RiseClass rise, double heightM, double effMassKg, double periodS, int k)
        {
            double ay = DamageParameters.YieldCoefficient(rise) * DamageParameters.Gravity;
            double dy = ay * periodS * periodS / (4.0 * Math.PI * Math.PI);
            double sd = DamageParameters.RoofToSpectral * DamageParameters.DriftMedian(type, k) * heightM;
            return sd >= dy
                ? effMassKg * ay * (0.5 * dy + (sd - dy))
                : 0.5 * effMassKg * ay * sd * sd / dy;
        }

        public static Capacity Compute(BuildingId id, in BuildingAttributes a)
        {
            var c = new Capacity();
            double h = a.HeightM > 0 ? a.HeightM : DamageParameters.MetresPerLevel;
            int n = ResolveLevels(a);
            c.Levels = n;
            c.Rise = ClassOf(n);
            c.Type = TypeOf(c.Rise, a.Residential);
            double rho = a.Residential && n <= DamageParameters.LowRiseMaxLevels
                ? DamageParameters.LightFloorMassKgPerM2 : DamageParameters.FloorMassKgPerM2;
            double area = a.FootprintM2 > 0 ? a.FootprintM2 : 1.0;
            c.MassKg = rho * area * n;
            c.PeriodS = DamageParameters.PeriodCt * Math.Pow(h, DamageParameters.PeriodExponent);
            double me = DamageParameters.ModalMassFactor * c.MassKg;
            c.E1 = MedianEnergy(c.Type, c.Rise, h, me, c.PeriodS, 1);
            c.E2 = MedianEnergy(c.Type, c.Rise, h, me, c.PeriodS, 2);
            c.E3 = MedianEnergy(c.Type, c.Rise, h, me, c.PeriodS, 3);
            c.E4 = MedianEnergy(c.Type, c.Rise, h, me, c.PeriodS, 4);
            double f = Math.Exp(DamageParameters.BetaE * ScatterZ(id, 0));
            c.S1 = c.E1 * f; c.S2 = c.E2 * f; c.S3 = c.E3 * f; c.S4 = c.E4 * f;
            return c;
        }

        // ---- deterministic hashing (R-021 §2.5) ----
        // Core's IdHash is internal, so this is the same SplitMix64 finaliser applied to the (source, id) pair and an
        // event index. No string or object hashing, no System.Random, no clock.
        public static ulong Hash(BuildingId id, int eventIndex)
        {
            unchecked
            {
                ulong x = id.SourceId ^ ((ulong)id.Source * 0x9E3779B97F4A7C15UL)
                        ^ DamageParameters.ScatterSalt ^ ((ulong)(uint)eventIndex * 0xBF58476D1CE4E5B9UL);
                x += 0x9E3779B97F4A7C15UL;
                x ^= x >> 30; x *= 0xBF58476D1CE4E5B9UL;
                x ^= x >> 27; x *= 0x94D049BB133111EBUL;
                x ^= x >> 31;
                return x;
            }
        }

        /// <summary>Uniform in (0,1) from the ID hash and an event index.</summary>
        public static double Uniform(BuildingId id, int eventIndex)
        {
            return ((Hash(id, eventIndex) >> 11) + 0.5) / 9007199254740992.0;
        }

        /// <summary>Standard normal (Box-Muller), clamped to +-ZClamp. Reproducible, not saved.</summary>
        public static double ScatterZ(BuildingId id, int eventIndex)
        {
            ulong h = Hash(id, eventIndex);
            double u1 = ((h >> 11) + 0.5) / 9007199254740992.0;
            double u2 = ((Hash(id, eventIndex + 0x10000) >> 11) + 0.5) / 9007199254740992.0;
            double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            double c = DamageParameters.ZClamp;
            return z < -c ? -c : z > c ? c : z;
        }
    }
}
