// Two-body contact law shared by cannon and melee (research/R-027.md section 2). Pure functions, no allocation.
using System;
using Game.Core;

namespace Game.Combat
{
    public static class ImpactMath
    {
        public struct Result
        {
            /// <summary>Closing speed along the normal, m/s (positive = approaching).</summary>
            public double ClosingSpeed;
            public double ReducedMassKg;
            /// <summary>J = mu (1 + e) v_n, N s.</summary>
            public double ImpulseNs;
            /// <summary>Dissipated energy 0.5 mu (1 - e^2) v_n^2, J.</summary>
            public double DissipatedJ;
            /// <summary>0.5 m_eff v_n^2, J: bound of <see cref="DissipatedJ"/>.</summary>
            public double ImpactorKineticJ;
            /// <summary>Kinetic energy gained by the target, J (J^2 / 2M; 0 for an immovable target).</summary>
            public double TargetGainJ;
        }

        /// <summary>
        /// Impactor of effective mass <paramref name="impactorMassKg"/> closing at <paramref name="closingSpeed"/> on a target of
        /// <paramref name="targetMassKg"/> (0 or less = immovable) with restitution e (clamped to 0..1).
        /// Energy balance: impactor KE before = impactor KE after + target gain + dissipated (tested to 1e-9 relative).
        /// </summary>
        public static Result Resolve(double impactorMassKg, double targetMassKg, double closingSpeed, double restitution)
        {
            var r = new Result { ClosingSpeed = closingSpeed };
            if (!(impactorMassKg > 0) || double.IsInfinity(impactorMassKg) || !(closingSpeed > 0) || double.IsInfinity(closingSpeed)) { r.ClosingSpeed = 0; return r; }
            double e = double.IsNaN(restitution) ? 0 : Math.Max(0, Math.Min(1, restitution));
            double mu = targetMassKg > 0 && !double.IsInfinity(targetMassKg) ? impactorMassKg * targetMassKg / (impactorMassKg + targetMassKg) : impactorMassKg;
            r.ReducedMassKg = mu;
            r.ImpulseNs = mu * (1 + e) * closingSpeed;
            r.DissipatedJ = 0.5 * mu * (1 - e * e) * closingSpeed * closingSpeed;
            r.ImpactorKineticJ = 0.5 * impactorMassKg * closingSpeed * closingSpeed;
            r.TargetGainJ = targetMassKg > 0 && !double.IsInfinity(targetMassKg) ? r.ImpulseNs * r.ImpulseNs / (2 * targetMassKg) : 0;
            return r;
        }

        public static ImpactRecord MakeRecord(EntityId source, WeaponKind weapon, in SweepHit hit, in Result r, long step)
        {
            return new ImpactRecord
            {
                Source = source, Weapon = weapon, Kind = hit.Kind, Building = hit.Building, Target = hit.Entity,
                Point = hit.Point, Normal = hit.Normal, ImpulseNs = r.ImpulseNs, EnergyJ = r.DissipatedJ,
                ImpactorKineticJ = r.ImpactorKineticJ, ClosingSpeed = r.ClosingSpeed, Step = step,
            };
        }
    }

    /// <summary>Receives resolved impacts from weapons.</summary>
    public interface IImpactListener
    {
        void OnImpact(in ImpactRecord impact);
    }
}
