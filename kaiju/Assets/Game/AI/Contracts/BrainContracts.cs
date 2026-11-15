using System;
using Game.Core;

namespace Game.AI
{
    /// <summary>Brain contract constants (R-024 section 2).</summary>
    public static class BrainContracts
    {
        public const int ContractVersion = 1;
        public const int MaxTargets = 8;  // NT
        public const int MaxAllies = 7;   // NA
    }

    /// <summary>Minimal 2D vector (world metres, x east / y north in the harness).</summary>
    public readonly struct Float2
    {
        public readonly float X;
        public readonly float Y;
        public Float2(float x, float y) { X = x; Y = y; }
    }

    /// <summary>Brain states (R-024 1.4). Withdrawn = Idle with the withdrawn flag (6th state).</summary>
    public enum BrainState : byte { Idle = 0, Approach = 1, Flank = 2, Charge = 3, Retreat = 4, Withdrawn = 5 }

    public enum BrainAction : byte { None = 0, Melee = 1 }

    /// <summary>Pack role by sorted EntityId rank (informational in the observation).</summary>
    public enum BrainRole : byte { None = 0, Wing = 1, Centre = 2 }

    /// <summary>Why the last decision changed state or target; diagnostic only (brain_log).</summary>
    public enum ReasonCode : byte
    {
        None = 0,
        TargetAcquired = 1,
        InStandoffSolo = 2,
        InStandoffWing = 3,
        FlankSlotReached = 4,
        FlankTimeout = 5,
        AllyCharging = 6,
        CentreHoldTimeout = 7,
        ChargeNoContact = 8,
        TargetLost = 9,
        RetargetForced = 10,
        RetargetVoluntary = 11,
        LowHp = 12,
        Withdrawn = 13,
        NoAlliesLeft = 14,
    }

    /// <summary>Reference constants (R-024 1.2 / 1.3 / 1.5) with the R-024 defaults. Use <see cref="Default"/>.</summary>
    public struct BrainConfig
    {
        public int BrainPeriodSteps;       // round(0.2 s / StepSeconds); 10 at 0.02 s
        public float SightRangeM;          // R_s
        public float MemorySeconds;        // lost-target memory
        public float AllyAwarenessM;
        public float CruiseSpeedMps;
        public float MeleeReachM;          // r_m
        public float StandoffM;            // d_s
        public float EngageRadiusM;        // metric only
        public float WingOffsetRad;        // theta_f
        public float RetreatHpFrac;
        public float RetargetRatio;
        public float RetargetMinSeconds;
        public float SwitchBackLockoutSeconds; // R-024 rev 3: no voluntary switch back to the target just left within this time
        public float FacingHalfAngleRad;   // facing_me window
        public float WeightDistance, WeightHp, WeightFacing, WeightFocus;
        public float SpeedApproach, SpeedApproachWing, SpeedFlank, SpeedCharge, SpeedRetreat;
        public float CentreHoldTimeoutSeconds;
        public float FlankTimeoutSeconds;
        public float FlankBearingTolRad;
        public float FlankTimeoutMargin;       // R-024 rev2: timeout = margin * arc/speed + slack, floored at FlankTimeoutSeconds
        public float FlankTimeoutSlackSeconds;
        public float AssemblyRadiusM;          // pack-assembly gate: all live allies within this of the target
        public float AssemblyTimeoutSeconds;   // wings stop waiting for assembly after this
        public float FlankSpreadMinRad;        // charge from a flank slot only when the engaged spread reaches this
        public bool DirectCharge;              // M-007 null control: Approach then Charge, no Flank / hold
        public float CentreHoldMaxSeconds;     // absolute cap of the centre hold
        public float ChargeNoContactSeconds;
        public float RetreatSafeDistanceM;
        public float RetreatSafeSeconds;

        public static BrainConfig Default()
        {
            const float deg = 0.017453292519943295f;
            return new BrainConfig
            {
                BrainPeriodSteps = 10,
                SightRangeM = 1200f, MemorySeconds = 3f, AllyAwarenessM = 1500f,
                CruiseSpeedMps = 9.12f, MeleeReachM = 40f, StandoffM = 250f, EngageRadiusM = 400f,
                WingOffsetRad = 70f * deg, RetreatHpFrac = 0.35f,
                RetargetRatio = 1.25f, RetargetMinSeconds = 4f, SwitchBackLockoutSeconds = 12f, FacingHalfAngleRad = 45f * deg,
                WeightDistance = 1.0f, WeightHp = 0.8f, WeightFacing = 0.6f, WeightFocus = 0.5f,
                SpeedApproach = 0.6f, SpeedApproachWing = 0.8f, SpeedFlank = 0.8f, SpeedCharge = 1.0f, SpeedRetreat = 0.7f,
                CentreHoldTimeoutSeconds = 6f, FlankTimeoutSeconds = 10f, FlankBearingTolRad = 15f * deg,
                FlankTimeoutMargin = 1.5f, FlankTimeoutSlackSeconds = 6f, AssemblyRadiusM = 375f, AssemblyTimeoutSeconds = 60f,
                FlankSpreadMinRad = 90f * deg, CentreHoldMaxSeconds = 90f,
                ChargeNoContactSeconds = 8f, RetreatSafeDistanceM = 600f, RetreatSafeSeconds = 10f,
            };
        }
    }

    public struct SelfObs
    {
        public EntityId Id;
        public uint PackId;
        public Float2 Pos;
        public float HeadingRad, Speed, HpFrac;
        public byte State;
        public int StateAgeSteps;
        public float Cooldowns;
    }

    public struct TargetObs
    {
        public EntityId Id;
        public Float2 Pos;
        public float HeadingRad, Speed, HpFrac;
        public bool Alive, Visible;
        public float MemoryAge; // seconds since last seen (0 when visible)
    }

    public struct AllyObs
    {
        public EntityId Id;
        public Float2 Pos;
        public float HeadingRad, HpFrac;
        public bool Alive;
        public byte State;
        public EntityId TargetId;
        public byte Role;
    }

    /// <summary>
    /// Observation passed to the brain. Allocate once and refill in place each decision (no per-tick allocation).
    /// Targets[0..TargetCount) and Allies[0..AllyCount) must be sorted by EntityId ordinal.
    /// </summary>
    public sealed class BrainObservation
    {
        public long Step;
        public double TimeSeconds;
        public SelfObs Self;
        public int TargetCount;
        public readonly TargetObs[] Targets = new TargetObs[BrainContracts.MaxTargets];
        public int AllyCount;
        public readonly AllyObs[] Allies = new AllyObs[BrainContracts.MaxAllies];
    }

    public struct BrainIntent
    {
        public float DesiredHeadingRad;
        public float SpeedFrac;
        public EntityId TargetId; // default = none
        public BrainAction Action;
        public BrainState State;
        public ReasonCode Reason;
    }

    /// <summary>Brain interface shared by the scripted baseline and the RL policy. R-024 section 2.1.</summary>
    public interface IKaijuBrain
    {
        int ContractVersion { get; }
        void Reset(ulong seed, in BrainConfig cfg);
        void Decide(in BrainObservation obs, ref BrainIntent intent);
    }
}
