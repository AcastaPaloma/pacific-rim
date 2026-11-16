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
        public float PaceFrac;           // far transit speed fraction that makes every member arrive together (0 = full speed)
        public bool BaitJoin;            // hunt: the strike is under way and the Bait may join it (Bait-and-strike)
        public bool CommitReady;         // hunt: the formation is in place (slots held, Bait drawing) or the gate timed out; Anchor/Flanker may commit
        public double GateOpenAt;        // T-113b: time the hunt commit gate opened (valid while CommitReady in a hunt)
        public bool BaitFirst;           // T-113b: bait-first strike geometry is active for this pack
        public bool BaitGo;              // T-113c: the Bait may step into its band (timed strike: the Anchor is BaitLeadS from weapon range)
        public float CommitDelayS;       // T-113c: with AnchorLead a striker that is faster than the Anchor waits this long after the gate opened before it commits (all arrive together, Anchor first)
        public float CommitRangeM;       // T-113b: when > 0, an Anchor/Flanker commits from within this distance of the focus (its hold slot may lie outside the default commit range)
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

    /// <summary>Path length and next waypoint. Straight line by default; <see cref="GridRoutePlanner"/> runs on an <see cref="IBayCostField"/> (Track 1 bake adapter, or the harness's synthetic Bay).</summary>
    public interface IRoutePlanner
    {
        /// <summary>Effective walking length in metres (soft ground weighted); +Infinity when the species cannot reach it.</summary>
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
        public float StageFrac;              // before the advance the hunt slots sit on a ring this many times wider than the engagement slots (and outside 1.15 x the member's 5 x height engagement radius)
        public float StageTimeoutSeconds;    // advance this long after every member is within twice its stage radius
        public float SlotTolFrac;            // a slot counts as held within max(SlotTolMinM, this x standoff)
        public float SlotTolMinM;
        public float AnchorTolMul;           // the Anchor's slot tolerance is this multiple of a Flanker's
        public float PaceMinFrac;            // lower bound of the synchronised-arrival pace (0 = pacing off)
        public float BaitJoinDelaySeconds;   // the Bait joins the strike this long after the gate opened (0 = never)
        public float BaitStepInM;            // the Bait steps into its band once every holder is within this of its slot
        public float BaitDrawSeconds;        // Bait inside its band this long counts as drawing fire (mech lock is 3 s)
        public float GateTimeoutSeconds;     // the commit gate opens this long after the first member is near the target, ready or not
        public float CommitRangeFrac;        // a member commits only within this x standoff of the target
        public float CommitRangeFracAnchor;  // T-115j: > 0 = the Anchor's own commit range (x standoff / slot radius) while CommitRangeFrac applies to the Flanker and Bait; 0 = every role uses CommitRangeFrac (T-115i behaviour)
        public float BaitOffsetRad;          // Bait slot offset against the first flanker's side (strike geometry, P4)
        public float AnchorOffsetRad;        // Anchor slot offset against the first flanker's side (keeps the engaged spread above 90 degrees)
        public bool RoutePlanning;           // T-115c: members transit along IRoutePlanner waypoints and goal utility / pacing use planner path lengths (needs a planner on the coordinator; straight line otherwise)
        public float LatchReleaseM;          // the approach bearing is re-latched while the pack centroid is farther than this from the target
        // T-113b bait-first strike (R-103 4.2 Bait "nearest threat at the edge of enemy weapon range", P4 strike >= 90 deg from the Bait):
        // with a Bait in the hunt, the strikers (Anchor/Flanker) hold outside mech weapon range on one side of the approach axis, the Bait takes a wide
        // slot on the other side and steps in first to draw the lock, and the strikers commit only once it has drawn it.
        public bool BaitFirst;
        public float BaitWideOffsetRad;      // Bait slot offset from the approach axis (to the side opposite every striker)
        public float HoldOutsideMarginM;     // with a Bait, a striker's hold slot is at least MechWeaponRangeM + this from the focus
        public float StrikeArcMinRad, StrikeArcMaxRad;   // striker slots spread over [min, max] on the side opposite the Bait (anchors nearest the axis)
        // T-113b pincer: once the strike is on, the Bait holds its distance to the focus at (nearest charging striker's distance) +/- BaitSwingM,
        // the sign alternating every BaitSwingSeconds, so the nearest threat to the mech alternates between two bearings >= 90 deg apart (a slow turret
        // swinging between them re-acquires each time). 0 = off.
        public float BaitSwingM, BaitSwingSeconds;
        // T-113b: in a hunt, a member not charging steers around any seen mech other than the focus that is within this distance (0 = off)
        public float AvoidOtherMechsM;
        // T-113c timed strike (needs BaitFirst): at the gate each striker gets a launch delay so that it reaches melee AnchorLeadLagS after the slowest striker (the Anchor,
        // 11.6 m/s and from further out). The heavy Anchor therefore takes the mech's fire on the way in (cheap in hp fraction, which P5's kill exchange is measured in), and
        // the Striders and the Bait close from outside weapon range just behind it. The Bait joins after the longest delay.
        public bool AnchorLead;
        public bool BaitTimed;               // the Bait's step-in is timed against the Anchor's run (BaitLeadS) instead of following the holders; the gate then does not wait for its draw
        public float AnchorHoldM;            // > 0: under bait-first the Anchor holds at this distance (>= weapon range + margin) instead of its species standoff, so it arrives soon after the Strikers
        public float AnchorLeadLagS;
        public float BaitLeadS;              // the Bait steps into its band when the Anchor is this many seconds from the mech's weapon range (lock 3 s + turret swing + the 3 s the metric needs to count an aim)
        public float AccelMps2;              // kaiju linear acceleration used for the arrival-time estimate (the harness and the body model: 2 m/s^2)
        public bool StrictAnchorTol;         // T-113b: under bait-first the Anchor holds the Flanker's slot tolerance (no AnchorTolMul)

        /// <summary>T-115j: the commit range (x standoff) for a role; the Anchor uses CommitRangeFracAnchor when set, every other role CommitRangeFrac.</summary>
        public readonly float CommitRangeFracFor(PackRole role) => role == PackRole.Anchor && CommitRangeFracAnchor > 0f ? CommitRangeFracAnchor : CommitRangeFrac;

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
                SlotApproachFrac = 0f, StageFrac = 1.0f, StageTimeoutSeconds = 120f, SlotTolFrac = 0.4f, SlotTolMinM = 80f, AnchorTolMul = 2f, PaceMinFrac = 0f, BaitJoinDelaySeconds = 0f, BaitStepInM = 100f, BaitDrawSeconds = 4f, GateTimeoutSeconds = 60f, CommitRangeFrac = 1.6f, CommitRangeFracAnchor = 0f,
                BaitOffsetRad = 0.9f, AnchorOffsetRad = 0.6f, LatchReleaseM = 700f,
                AnchorLead = false, BaitTimed = false, AnchorHoldM = 0f, AnchorLeadLagS = 2f, BaitLeadS = 9f, AccelMps2 = 2f,
                BaitFirst = false, BaitWideOffsetRad = 100f * deg, HoldOutsideMarginM = 40f, StrikeArcMinRad = 0f, StrikeArcMaxRad = 70f * deg,
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
            c.BayTactics = true; c.BaitJoinDelaySeconds = 7f; c.PaceMinFrac = 0.4f; c.BaitBandFrac = 0.8f; c.TransitDistanceM = 400f; c.SlotApproachFrac = 0.8f;
            c.RoutePlanning = true;   // T-115c: a no-op while the coordinator holds the straight-line planner
            // T-113b: bait-first strike geometry (fixes the C1 unflanked commits) stays OFF by default: on the same hold-out seeds it raised C1 0.818 -> 0.898
            // but turned P4 (+0.001 -> -0.077) and P1 (0.705 -> 0.654) from PASS to FAIL (evidence/T-113b/compare.txt). Kept as a flag for the R-NEW-a hunt-timing work.
            // T-113c re-checked it on hold-out seeds 3001-3030 under evaluator rev 2 with the best of three timing variants (hold 340 m, Anchor hold 340 m, 1 s draw, avoid other mechs):
            // C1 0.818 -> 0.932 and P1 0.916 -> 0.957 improve, P4 +0.001 -> -0.066, P5 0.926 -> 0.854 and P6 0.797 -> 0.661 get worse, so it stays OFF (evidence/T-113c/compare.txt).
            c.BaitFirst = false; c.BaitSwingSeconds = 4f;
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
