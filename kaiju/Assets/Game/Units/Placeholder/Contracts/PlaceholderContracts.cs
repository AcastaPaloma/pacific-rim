// TEMPORARY (T-030c): contracts of the placeholder body. Delete with the body when T-005's walking mech replaces it.
using System;
using Game.Core;

namespace Game.Units.Placeholder
{
    public static class PlaceholderContractVersions
    {
        public const int Body = 1;
        public const int DamageRecord = 1;
        /// <summary>Schema tag of <see cref="PlaceholderDamageData"/> when a save section carries it (hangar stub, T-034).</summary>
        public const string DamageSchema = "placeholder_damage@v1";
    }

    public static class PlaceholderFlags
    {
        /// <summary>Default off. Gates the Unity adapter and its composition installer; the engine-free classes are always usable.</summary>
        public const string Body = "units.placeholderBody";
    }

    public enum PlaceholderDamageState { Intact = 0, Damaged = 1, Critical = 2, Destroyed = 3 }

    /// <summary>Plain data of a damage record: what a save section stores and restores.</summary>
    [Serializable]
    public struct PlaceholderDamageData
    {
        public float MaxHitPoints, HitPoints;
        public int HitsTaken;
    }

    /// <summary>
    /// Per-body hit points and damage state. Engine-free and tiny on purpose: the hangar stub (T-034) reads and repairs it,
    /// the first playable scene reads it for a debug readout. State thresholds: above 66% of max Intact, above 33% Damaged,
    /// above 0 Critical, 0 Destroyed. Damage and repair are clamped; non-finite or non-positive amounts are ignored.
    /// </summary>
    public sealed class PlaceholderDamageRecord
    {
        public const float DamagedBelow = 2f / 3f, CriticalBelow = 1f / 3f;

        public EntityId Body { get; }
        public float MaxHitPoints { get; private set; }
        public float HitPoints { get; private set; }
        public int HitsTaken { get; private set; }

        public PlaceholderDamageRecord(EntityId body, float maxHitPoints)
        {
            if (!(maxHitPoints > 0f) || float.IsInfinity(maxHitPoints)) throw new ArgumentOutOfRangeException(nameof(maxHitPoints));
            Body = body; MaxHitPoints = maxHitPoints; HitPoints = maxHitPoints;
        }

        public float Fraction => HitPoints / MaxHitPoints;

        public PlaceholderDamageState State =>
            HitPoints <= 0f ? PlaceholderDamageState.Destroyed
            : Fraction <= CriticalBelow ? PlaceholderDamageState.Critical
            : Fraction <= DamagedBelow ? PlaceholderDamageState.Damaged
            : PlaceholderDamageState.Intact;

        /// <summary>Takes damage; returns the hit points actually removed.</summary>
        public float ApplyDamage(float amount)
        {
            if (!(amount > 0f) || float.IsInfinity(amount)) return 0f;
            float taken = Math.Min(amount, HitPoints);
            if (taken > 0f) { HitPoints -= taken; HitsTaken++; }
            return taken;
        }

        /// <summary>Restores hit points (a destroyed body can be repaired: the hangar decides when); returns the amount restored.</summary>
        public float Repair(float amount)
        {
            if (!(amount > 0f) || float.IsInfinity(amount)) return 0f;
            float given = Math.Min(amount, MaxHitPoints - HitPoints);
            HitPoints += given;
            return given;
        }

        public void RepairFully() => HitPoints = MaxHitPoints;

        public PlaceholderDamageData Export() => new PlaceholderDamageData { MaxHitPoints = MaxHitPoints, HitPoints = HitPoints, HitsTaken = HitsTaken };

        /// <summary>Restores a saved record; values are clamped to a valid state.</summary>
        public void Import(PlaceholderDamageData d)
        {
            if (d.MaxHitPoints > 0f && !float.IsInfinity(d.MaxHitPoints)) MaxHitPoints = d.MaxHitPoints;
            HitPoints = float.IsNaN(d.HitPoints) ? MaxHitPoints : Math.Max(0f, Math.Min(d.HitPoints, MaxHitPoints));
            HitsTaken = Math.Max(0, d.HitsTaken);
        }
    }
}
