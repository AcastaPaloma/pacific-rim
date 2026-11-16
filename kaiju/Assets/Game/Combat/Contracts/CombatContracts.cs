// Combat contracts (layer L3, engine-free): weapon-facing data types, the target query, damage sinks and bus events.
// Numbers behind them: research/R-027.md. Destruction is fed through its existing IDamageModel (energy in joules).
using System;
using Game.Core;

namespace Game.Combat
{
    public static class CombatContractVersions
    {
        public const int Api = 1;
    }

    public static class CombatFlags
    {
        /// <summary>Default off. Gates the melee mount in the composition installer.</summary>
        public const string Melee = "combat.melee";
        /// <summary>Default off. Gates the cannon mount in the composition installer.</summary>
        public const string Cannon = "combat.cannon";
    }

    /// <summary>Weapon ids on events. Append only.</summary>
    public enum WeaponKind : byte { None = 0, Cannon = 1, Melee = 2 }

    /// <summary>Engine-free double-precision vector, metres / m/s / N s depending on use. Right-handed, +Y up.</summary>
    public readonly struct CVec3 : IEquatable<CVec3>
    {
        public readonly double X, Y, Z;
        public CVec3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static CVec3 Zero => default;
        public static CVec3 Up => new CVec3(0, 1, 0);
        public static CVec3 operator +(CVec3 a, CVec3 b) => new CVec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static CVec3 operator -(CVec3 a, CVec3 b) => new CVec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static CVec3 operator -(CVec3 a) => new CVec3(-a.X, -a.Y, -a.Z);
        public static CVec3 operator *(CVec3 a, double s) => new CVec3(a.X * s, a.Y * s, a.Z * s);
        public static CVec3 operator *(double s, CVec3 a) => new CVec3(a.X * s, a.Y * s, a.Z * s);
        public static double Dot(CVec3 a, CVec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static CVec3 Cross(CVec3 a, CVec3 b) => new CVec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
        public double LengthSq => X * X + Y * Y + Z * Z;
        /// <summary>Unit vector, or <paramref name="fallback"/> when the length is zero or not finite.</summary>
        public CVec3 Normalized(CVec3 fallback)
        {
            double l = Length;
            return l > 0 && !double.IsInfinity(l) && !double.IsNaN(l) ? this * (1.0 / l) : fallback;
        }
        public bool IsFinite => !(double.IsNaN(X) || double.IsNaN(Y) || double.IsNaN(Z) || double.IsInfinity(X) || double.IsInfinity(Y) || double.IsInfinity(Z));
        public bool Equals(CVec3 o) => X == o.X && Y == o.Y && Z == o.Z;
        public override bool Equals(object obj) => obj is CVec3 o && Equals(o);
        public override int GetHashCode() => X.GetHashCode() * 31 * 31 + Y.GetHashCode() * 31 + Z.GetHashCode();
        public override string ToString() => "(" + X.ToString("R") + ", " + Y.ToString("R") + ", " + Z.ToString("R") + ")";
    }

    public enum TargetKind : byte { None = 0, Building = 1, Kaiju = 2, Mech = 3, Ground = 4 }

    /// <summary>First contact of a swept sphere with a target volume.</summary>
    public struct SweepHit
    {
        public bool Hit;
        /// <summary>Fraction 0..1 along the swept segment where the sphere first touches.</summary>
        public double T;
        /// <summary>Contact point on the target surface.</summary>
        public CVec3 Point;
        /// <summary>Unit surface normal pointing out of the target, toward the impactor.</summary>
        public CVec3 Normal;
        public TargetKind Kind;
        public BuildingId Building;
        public EntityId Entity;
        /// <summary>Effective mass of the target for the impact, kg; 0 or less = immovable (buildings pass their mass M0, or 0).</summary>
        public double TargetMassKg;
        /// <summary>Target velocity at the contact, m/s.</summary>
        public CVec3 TargetVelocity;
    }

    /// <summary>
    /// Narrow query contract onto the world's volumes (buildings, kaiju, mechs). The Unity adapter wraps physics sweeps; tests
    /// and the engine-free harness use SimpleTargetWorld. Must be deterministic and allocation-free in steady state.
    /// </summary>
    public interface ICombatTargetQuery
    {
        /// <summary>Sweeps a sphere of <paramref name="radius"/> from <paramref name="from"/> to <paramref name="to"/>; returns the earliest contact, ignoring <paramref name="ignore"/> (the shooter).</summary>
        bool SweepSphere(in CVec3 from, in CVec3 to, double radius, EntityId ignore, out SweepHit hit);
    }

    /// <summary>One resolved impact, handed to the damage sinks and published as <see cref="ImpactImpulse"/>.</summary>
    public struct ImpactRecord
    {
        public EntityId Source;
        public WeaponKind Weapon;
        public TargetKind Kind;
        public BuildingId Building;
        public EntityId Target;
        public CVec3 Point;
        /// <summary>Surface normal out of the target.</summary>
        public CVec3 Normal;
        /// <summary>Impulse delivered to the target, N s, along -Normal (the target is pushed away from the impactor).</summary>
        public double ImpulseNs;
        /// <summary>Energy dissipated by the contact, J: the delta-KE of the contact record that Destruction expects.</summary>
        public double EnergyJ;
        /// <summary>Kinetic energy of the impactor along the normal before contact, J (upper bound of <see cref="EnergyJ"/>).</summary>
        public double ImpactorKineticJ;
        /// <summary>Closing speed along the normal, m/s.</summary>
        public double ClosingSpeed;
        public long Step;
    }

    /// <summary>Read-only ammo and reload state of one cannon (T-152b). <see cref="ReloadFrac"/> is 0..1, 1 = ready to fire; non-finite input reads as 0.</summary>
    public readonly struct AmmoStatus
    {
        public readonly int Ammo, Capacity;
        public readonly double ReloadFrac;
        public AmmoStatus(int ammo, int capacity, double reloadFrac)
        {
            Ammo = ammo; Capacity = capacity;
            ReloadFrac = !(reloadFrac > 0) ? 0 : (reloadFrac > 1 ? 1 : reloadFrac);
        }
        public bool Reloading => ReloadFrac < 1;
    }

    /// <summary>Read-only ammo query (HUD and tools). Never mutates weapon state. False when the owner has no cannon.</summary>
    public interface IAmmoQuery
    {
        bool TryGetAmmo(EntityId owner, out AmmoStatus status);
    }

    /// <summary>Hit points of entities that can be shot or struck (kaiju today; a placeholder mech through an adapter).</summary>
    public interface IEntityHealth
    {
        /// <summary>Applies an impact; <paramref name="hpRemoved"/> is what was taken. False when the entity is unknown.</summary>
        bool TryApply(EntityId entity, in ImpactRecord impact, out double hpRemoved);
        bool TryGetHitPoints(EntityId entity, out double hitPoints);
    }

    /// <summary>The mount a weapon sits on: a pose provider. Placeholder body today, the walking body after T-005.</summary>
    public interface IWeaponMount
    {
        EntityId Id { get; }
        /// <summary>World position of the mount origin (m), heading (rad, atan2(x, z)) and velocity (m/s).</summary>
        void GetPose(out CVec3 position, out double heading, out CVec3 velocity);
    }

    /// <summary>Bus: a weapon discharged.</summary>
    public struct WeaponFired : IEvent
    {
        public EntityId Source;
        public byte Weapon;
        public long Step;
        public double MuzzleX, MuzzleY, MuzzleZ;
        public double DirX, DirY, DirZ;
        public double ProjectileMassKg;
    }

    /// <summary>Bus: recoil as an OUTPUT. The real body applies it at the muzzle point (T-005 era); the placeholder ignores it.</summary>
    public struct RecoilImpulse : IEvent
    {
        public EntityId Source;
        public long Step;
        /// <summary>Impulse on the shooter, N s (opposite to the shot).</summary>
        public double ImpulseX, ImpulseY, ImpulseZ;
        public double PointX, PointY, PointZ;
    }

    /// <summary>Bus: an impact resolved (cannon or melee). Carries the impulse that Destruction's energy-only contract cannot.</summary>
    public struct ImpactImpulse : IEvent
    {
        public EntityId Source;
        public byte Weapon;
        public byte TargetKind;
        public BuildingId Building;
        public EntityId Target;
        public long Step;
        public double PointX, PointY, PointZ;
        public double NormalX, NormalY, NormalZ;
        public double ImpulseNs;
        public double EnergyJ;
    }
}
