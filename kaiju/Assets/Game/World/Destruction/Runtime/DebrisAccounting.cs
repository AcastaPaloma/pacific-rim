// Debris mass and energy account of research/R-021.md §3. Pure functions of (type, attributes, state), no state, no allocation.
// Cumulative by design: a state change adds F(k) - F(j), so mass and energy balance for every path to the same state.
using System;
using Game.Core;

namespace Game.World.Destruction
{
    public static class DebrisAccounting
    {
        public static double DebrisKgAt(HazusType type, double massKg, int state)
        {
            return massKg * DamageParameters.DebrisFraction(type, state);
        }

        public static int PieceCountFor(double debrisKg)
        {
            if (!(debrisKg > 0)) return 0;
            double n = Math.Ceiling(debrisKg / DamageParameters.PieceTargetKg);
            return n < 1 ? 1 : n > DamageParameters.MaxPieces ? DamageParameters.MaxPieces : (int)n;
        }

        /// <summary>Mass of piece i of n: equal shares, the last takes the remainder so the sum is exact.</summary>
        public static double PieceMassKg(double debrisKg, int count, int index)
        {
            double share = debrisKg / count;
            return index < count - 1 ? share : debrisKg - share * (count - 1);
        }

        public static DebrisAccount Compute(BuildingId id, HazusType type, double massKg, double heightM,
                                            double footprintM2, int state, double cumulativeEnergyJ)
        {
            var a = new DebrisAccount { Id = id, State = (byte)state, MassKg = massKg, ExternalWorkJ = cumulativeEnergyJ };
            double md = DebrisKgAt(type, massKg, state);
            a.DebrisKg = md;
            a.StandingKg = massKg - md;
            a.PieceCount = PieceCountFor(md);
            double sum = 0;
            for (int i = 0; i < a.PieceCount; i++) sum += PieceMassKg(md, a.PieceCount, i);
            a.PieceMassSumKg = sum;
            if (!(md > 0)) return a;

            double h = heightM > 0 ? heightM : DamageParameters.MetresPerLevel;
            double side = Math.Sqrt(footprintM2 > 0 ? footprintM2 : 1.0);
            double spreadSide = side + 2.0 * DamageParameters.SpreadRingFactor * h;
            double hp = md / DamageParameters.RubbleDensity / (spreadSide * spreadSide);
            a.HeapHeightM = hp;
            double hc = Math.Min(0.5 * hp, 0.5 * h);
            double dpe = md * DamageParameters.Gravity * (0.5 * h - hc);
            a.PotentialEnergyJ = dpe;
            a.FractureJ = Math.Min(DamageParameters.FractureJPerKg * md, DamageParameters.FractureCapShare * dpe);
            a.EjectaJ = Math.Min(DamageParameters.EjectaShare, DamageParameters.EjectaCapShare) * dpe;
            double rest = dpe - a.FractureJ - a.EjectaJ;
            a.GroundJ = rest > 0 ? DamageParameters.GroundShare * rest : 0.0;
            a.ResidualJ = dpe - a.FractureJ - a.GroundJ - a.EjectaJ;
            return a;
        }
    }
}
