using System;
using Game.Core;

namespace Game.AI.Pack
{
    /// <summary>Flag name of the pack layer (T-113). Default off in Config/FeatureFlags/AI.Pack.json.</summary>
    public static class PackFlags
    {
        public const string Pack = "ai.pack";
        public const string BrainId = "pack@1";
    }

    /// <summary>Pack roles (R-103 4.2). Free = no assignment (new, lost contact, regrouping).</summary>
    public enum PackRole : byte { Free = 0, Anchor = 1, Flanker = 2, Bait = 3, Raider = 4 }

    /// <summary>Pack goals (R-103 3.3). Regroup = withdraw to the healthiest nearby ally (covering rule, 4.3).</summary>
    public enum PackGoal : byte { None = 0, Hunt = 1, Raid = 2, Regroup = 3 }

    /// <summary>
    /// Minimal species input of the pack layer (T-113). A later adapter fills it from Units.Kaiju KaijuSpeciesSpec@v1 (T-112);
    /// Pack never references that module. Numbers of the three defaults: R-102 (speeds, mass), R-103 4.2 (role fit, *Assumed*).
    /// </summary>
    public readonly struct SpeciesRole
    {
        public readonly int SpeciesId;
        public readonly float HeightM;
        public readonly float CruiseMps;
        public readonly float ChargeMps;
        public readonly float MeleeReachM;
        public readonly float StandoffM;
        public readonly float HpRel;          // strength weight relative to the 10,000 t mech reference (R-103 0.2, Assumed)
        public readonly float FitAnchor, FitFlanker, FitBait, FitRaider; // 0..1
        public readonly bool CrossesDeepWater;

        public SpeciesRole(int speciesId, float heightM, float cruiseMps, float chargeMps, float meleeReachM, float standoffM,
            float hpRel, float fitAnchor, float fitFlanker, float fitBait, float fitRaider, bool crossesDeepWater)
        {
            SpeciesId = speciesId; HeightM = heightM; CruiseMps = cruiseMps; ChargeMps = chargeMps; MeleeReachM = meleeReachM;
            StandoffM = standoffM; HpRel = hpRel; FitAnchor = fitAnchor; FitFlanker = fitFlanker; FitBait = fitBait;
            FitRaider = fitRaider; CrossesDeepWater = crossesDeepWater;
        }

        public float Fit(PackRole r)
        {
            switch (r)
            {
                case PackRole.Anchor: return FitAnchor;
                case PackRole.Flanker: return FitFlanker;
                case PackRole.Bait: return FitBait;
                case PackRole.Raider: return FitRaider;
                default: return 0f;
            }
        }

        // R-102 section 5 speeds: cruise sqrt(Fr g l) at Fr 0.30/0.20/0.10, l 30/45/92 m; charge 15.3/12.4/11.6 m/s. Reach 0.5 H, standoff 7 x reach (R-103 0.2).
        public static readonly SpeciesRole Strider = new SpeciesRole(1, 60f, 9.4f, 15.3f, 30f, 210f, 0.5f, 0.1f, 0.9f, 1.0f, 0.3f, true);
        public static readonly SpeciesRole Breacher = new SpeciesRole(2, 100f, 9.4f, 12.4f, 50f, 350f, 1.0f, 0.5f, 0.8f, 0.4f, 0.7f, true);
        public static readonly SpeciesRole Bastion = new SpeciesRole(3, 160f, 9.5f, 11.6f, 80f, 560f, 2.5f, 1.0f, 0.2f, 0.0f, 1.0f, false);
    }

    /// <summary>One pack member as the host reports it each coordinator tick (no engine types).</summary>
    public struct PackMemberState
    {
        public EntityId Id;
        public SpeciesRole Species;
        public Float2 Pos;
        public float HpFrac;
        public bool Alive;
        public BrainState State;   // last brain state; Retreat/Withdrawn members leave the allocation
        public EntityId TargetId;  // the member's current target claim (default = none)
    }

    /// <summary>What the pack layer asks of one member (R-103 7). Informational for any brain; enforced by <see cref="PackMemberBrain"/>.</summary>
    public struct PackDirective
    {
        public PackRole Role;
        public PackGoal Goal;
        public EntityId FocusTargetId;   // hunt focus; for a Raider the infrastructure id
        public Float2 SlotPos;           // where the role wants the member (standoff slot, flank slot, bait point)
        public Float2 RoutePos;          // transit waypoint (straight line in T-113; IRoutePlanner later)
        public bool AllowMelee;          // false for Bait, Regroup, and a Raider in transit
        public bool CommitReady;         // hunt: the formation is in place (slots held, Bait drawing) or the gate timed out; Anchor/Flanker may commit
        public bool Valid;
    }

    /// <summary>Optional brain interface that accepts the directive as an input (T-041 policy adapter may implement it).</summary>
    public interface IDirectedBrain : IKaijuBrain
    {
        void Decide(in BrainObservation obs, in PackDirective directive, ref BrainIntent intent);
    }

    /// <summary>Infrastructure target as the host/adapter reports it (R-103 7 IInfrastructureSource; F-044).</summary>
    public struct InfraTarget
    {
        public EntityId Id;
        public Float2 Pos;
        public byte Class;       // 0 crane, 1 power node, 2 bridge span, 3 airport (weights in PackConfig)
        public float Hp;         // remaining structural hp, in the same units as PackConfig.RaidDamageRate
        public bool Alive;
    }

    public interface IInfrastructureSource
    {
        int Count { get; }
        InfraTarget Get(int index);
    }

    /// <summary>Path time and next waypoint. Straight line here; the Bay cost-field adapter comes with Track 1.</summary>
    public interface IRoutePlanner
    {
        float PathLength(Float2 from, Float2 to, in SpeciesRole species);
        Float2 NextWaypoint(Float2 from, Float2 to, in SpeciesRole species);
    }

    public sealed class StraightLineRoutePlanner : IRoutePlanner
    {
        public float PathLength(Float2 from, Float2 to, in SpeciesRole species)
        {
            float dx = to.X - from.X, dy = to.Y - from.Y;
            return MathF.Sqrt(dx * dx + dy * dy);
        }
        public Float2 NextWaypoint(Float2 from, Float2 to, in SpeciesRole species) => to;
    }

    /// <summary>Pack layer constants. R-103 values, all *Assumed* until T-040 / Track 1 data lands. Use <see cref="Default"/>.</summary>
    public struct PackConfig
    {
        public float WingOffsetRad;          // flank slot offset from the pack-to-target bearing (R-024 1.4)
        public float FlankSpreadMinRad;      // commit when engaged spread reaches this
        public float MechWeaponRangeM;       // bait holds at BaitBandFrac x this
        public float BaitBandFrac;
        public float BaitRetreatHp;          // bait leaves the band below this hp
        public float AnchorSpeedFrac;
        public float RoleDwellSeconds;       // voluntary role change needs this dwell
        public float BidMargin;              // incumbent bonus (challenger needs this ratio)
        public float SwitchCost;             // bid penalty for leaving a role before the dwell
        public float PosScaleM;              // path-distance scale of the position fit
        public float GoalDwellSeconds;       // minimum time between voluntary goal switches
        public float GoalSwitchRatio;        // U_new > ratio x U_current
        public float FocusClaimExpirySeconds;
        public float BoardStalenessSeconds;  // target entries older than this drop out of the strategic board
        public float MechStrength;           // enemy strength in HpRel units, per mech
        public float MechValue;              // hunt value per mech at full hp
        public float HuntHandlingBaseSeconds;
        public float RaidDamageRate;         // infra hp per second per raider (handling_s = hp / (rate x raiders))
        public float DefenseRadiusM;         // a known mech this close to an infra target makes it defended
        public float RiskScale;
        public float AnchorMinFit;           // covering rule threshold (R-103 4.3)
        public float RegroupMinSeconds;
        public float WeightCrane, WeightPower, WeightBridge, WeightAirport;
        public float TransitDistanceM;       // farther than this from the slot a member walks at full speed (Bay-scale transit)
        public bool BayTactics;              // Bay-scale hunt tactics (T-115b): approach latch, ring-up stage, commit gate, Bait staging, D_eng-bounded slots; off = the T-113 unit-scale behaviour
        public float SlotApproachFrac;       // speed fraction on the last stretch to a slot (floor of the tactical speed)
        public float StageFrac;              // before the advance the hunt slots sit on a ring this many times wider than the engagement slots
        public float StageTimeoutSeconds;    // advance this long after every member is within twice its stage radius
        public float SlotTolFrac;            // a slot counts as held within max(SlotTolMinM, this x standoff)
        public float SlotTolMinM;
        public float BaitDrawSeconds;        // Bait inside its band this long counts as drawing fire (mech lock is 3 s)
        public float GateTimeoutSeconds;     // the commit gate opens this long after the first member is near the target, ready or not
        public float CommitRangeFrac;        // a member commits only within this x standoff of the target
        public float BaitOffsetRad;          // Bait slot offset against the first flanker's side (strike geometry, P4)
        public float AnchorOffsetRad;        // Anchor slot offset against the first flanker's side (keeps the engaged spread above 90 degrees)
        public float LatchReleaseM;          // the approach bearing is re-latched while the pack centroid is farther than this from the target

        public static PackConfig Default()
        {
            const float deg = 0.017453292519943295f;
            return new PackConfig
            {
                WingOffsetRad = 70f * deg, FlankSpreadMinRad = 90f * deg,
                MechWeaponRangeM = 300f, BaitBandFrac = 0.9f, BaitRetreatHp = 0.6f, AnchorSpeedFrac = 0.5f,
                RoleDwellSeconds = 20f, BidMargin = 1.15f, SwitchCost = 0.25f, PosScaleM = 5000f,
                GoalDwellSeconds = 30f, GoalSwitchRatio = 1.3f, FocusClaimExpirySeconds = 6f, BoardStalenessSeconds = 60f,
                MechStrength = 2.5f, MechValue = 4f, HuntHandlingBaseSeconds = 120f, RaidDamageRate = 1f,
                DefenseRadiusM = 3000f, RiskScale = 1f, AnchorMinFit = 0.3f, RegroupMinSeconds = 30f,
                WeightCrane = 1f, WeightPower = 2f, WeightBridge = 3f, WeightAirport = 3f, TransitDistanceM = 1500f,
                SlotApproachFrac = 0f, StageFrac = 1.6f, StageTimeoutSeconds = 45f, SlotTolFrac = 0.4f, SlotTolMinM = 80f, BaitDrawSeconds = 4f, GateTimeoutSeconds = 60f, CommitRangeFrac = 1.6f,
                BaitOffsetRad = 0.9f, AnchorOffsetRad = 0.6f, LatchReleaseM = 700f,
            };
        }

        /// <summary>
        /// Tuned for the 60 km Bay (T-115b, M-007@v2 harness): the pack rings up outside the engagement radius, engages together, the Bait draws the
        /// lock from a band inside mech range while every other member holds slightly behind it, and Anchor/Flanker commit together on the gate.
        /// All numbers *Assumed* (R-103 0.2 numbers where they exist); thresholds of the metric are untouched.
        /// </summary>
        public static PackConfig Bay()
        {
            PackConfig c = Default();
            c.BayTactics = true; c.BaitBandFrac = 0.8f; c.TransitDistanceM = 400f; c.SlotApproachFrac = 0.8f;
            return c;
        }
    }

    /// <summary>Ablations of the pack layer for the M-007@v2 controls (R-103 5.3). `None` is the candidate.</summary>
    public enum PackControl : byte { None = 0, NoRoles = 1, NoBait = 2, NearestInfra = 3, RandomRole = 4 }

    /// <summary>Counters for tests, brain_log@v2 and the P2/P3 clauses (R-103 5).</summary>
    public struct PackStats
    {
        public int Ticks, RoleChanges, ForcedRoleChanges, GoalSwitches, Reallocations;
    }
}
