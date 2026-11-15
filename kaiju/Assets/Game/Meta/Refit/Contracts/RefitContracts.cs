using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Meta.Refit
{
    // Meta.Refit contract v1 (R-104 sections 9, 10.4, 10.5; D-017, D-019, D-025, F-062). Engine-free, deterministic. One modular frame with a slot list and
    // chassis classes that are constraint sets over it; a part catalogue (parts@v1); validation of mass, power, heat, actuator torque, centre of mass and
    // tilt margin from physics; output MechBuildSpec@v1 that Physics.Body reads once T-005 lands. Physical quantities are float64 SI (m, kg, s, W, J, N m);
    // money is a long count of whole US dollars; masses in the catalogue are long kilograms.

    public static class RefitContractVersions
    {
        public const int Contract = 1;
        /// <summary>Version of the "refit" save section (refit@v1).</summary>
        public const int SaveSection = 1;
        public const string SectionName = "refit";
        public const string PartsSchema = "parts@v1";
        public const string ChassisSchema = "chassis@v1";
        public const string BuildSpecSchema = "MechBuildSpec@v1";
    }

    public static class RefitFlags
    {
        /// <summary>Install the refit service. Default off.</summary>
        public const string Refit = "meta.refit";
    }

    /// <summary>The first constraint a build breaks, in the order the validator checks them (R-104 section 9). Append only.</summary>
    public enum RefitViolation : byte
    {
        None = 0, UnknownChassis = 1, UnknownSlot = 2, SlotMissing = 3, UnknownPart = 4, WrongSlotType = 5, WrongChassis = 6,
        Mass = 7, Power = 8, HeatRejection = 9, HeatStorage = 10, Torque = 11, ComHeight = 12, ComLateral = 13, ComForeAft = 14, TiltMargin = 15,
    }

    /// <summary>One catalogue part. Mass is the sum of the composition (class id to kg); price classes are those of Meta.Economy (structure, armour, ...).</summary>
    public sealed class PartDef
    {
        /// <summary>RarePart: the salvage rare part set consumed when this part is fitted (osteoderm_plate, keratin_liner, actuator_fibre), or empty.</summary>
        public string Id, SlotType, Tier, RarePart, Source, Note;
        public bool Assumed;
        /// <summary>Chassis ids the part fits.</summary>
        public string[] Chassis;
        public SortedDictionary<string, long> CompositionKg;
        public long MassKg;
        /// <summary>Box size (m) used for the own inertia of the part.</summary>
        public double SizeX, SizeY, SizeZ;
        /// <summary>CoM offset from the slot anchor for the right-hand side; a mirrored (left) slot negates x.</summary>
        public double ComX, ComY, ComZ;
        /// <summary>Peak electrical demand (W). A pulse part (weapon) overlaps the other demands only through the largest pulse (R-104 section 9: one cannon).</summary>
        public double PowerPeakW;
        public bool PowerPulse;
        /// <summary>Sustained waste heat (W) and the burst-profile average (W) over the 600 s profile.</summary>
        public double HeatW, HeatPeakW;
        /// <summary>Sub-masses of MassKg: radiator mass and coolant mass.</summary>
        public long RadiatorKg, CoolantKg;
        /// <summary>Actuator torque bonus in basis points (tendon fibre, capped at 1500 = +15%).</summary>
        public int TorqueBonusBps;
        public bool IsRare => !string.IsNullOrEmpty(RarePart);
    }

    /// <summary>One slot of the modular frame.</summary>
    public sealed class SlotDef
    {
        public string Name, Type, Parent;
        public int ParentIndex = -1;
        /// <summary>Nominal part CoM in the chassis frame (x lateral right, y up, z forward; origin at the ankle midpoint on the ground).</summary>
        public double AnchorX, AnchorY, AnchorZ;
        /// <summary>Joint axis position of an actuated slot.</summary>
        public double PivotX, PivotY, PivotZ;
        public bool Mirror, Actuated, MovesSelf;
    }

    /// <summary>One chassis class: the slot list with geometry and the limits of R-104 section 9 as data.</summary>
    public sealed class ChassisDef
    {
        public string Id, Label;
        public long MassCapKg;
        public double FootAreaM2, GroundPressurePa, StanceHalfWidthM, AlphaRadS2, FrameHeightM;
        public double ComHeightLoFrac, ComHeightHiFrac, ComLateralM, ComForeAftLoM, ComForeAftHiM, TiltMinDeg;
        public double ReactorWPerKg, BufferJ, BufferWindowS, Cot, WalkSpeedMs;
        public double PassiveHeatW, RadiatorWPerKg, CoolantJPerKgK, CoolantDeltaK, BurstS;
        public double TorqueNmPerKg, TorqueDerate;
        public SlotDef[] Slots;
        /// <summary>Slot indices moved by each slot's actuator (descendants, plus the slot itself when it moves itself); empty for non-actuated slots.</summary>
        public int[][] Moved;
        /// <summary>Limbs whose loss is checked for the tilt margin: name to slot indices.</summary>
        public SortedDictionary<string, int[]> Limbs;
        /// <summary>Default build: part id per slot, aligned with Slots.</summary>
        public string[] DefaultBuild;

        public int SlotIndex(string name)
        {
            for (int i = 0; i < Slots.Length; i++) if (string.Equals(Slots[i].Name, name, StringComparison.Ordinal)) return i;
            return -1;
        }
    }

    public interface IPartCatalog
    {
        PartDef Get(string partId);
        /// <summary>All parts in id order.</summary>
        IReadOnlyList<PartDef> All { get; }
        IReadOnlyList<string> PriceClasses { get; }
    }

    public interface IChassisCatalog
    {
        ChassisDef Get(string chassisId);
        IReadOnlyList<ChassisDef> All { get; }
    }

    /// <summary>Prices a part in whole US dollars (mass x class index, Meta.Economy's price table); the composition layer adapts the economy table.</summary>
    public interface IPartPricing
    {
        long PartPriceUsd(PartDef part);
        /// <summary>What the player gets for a part taken out (R-104 section 7: 40% of the price).</summary>
        long SellBackUsd(long priceUsd);
    }

    /// <summary>Whether the slot may refit the mech now (hangar phase, mech flyable and owned by the slot). The composition layer adapts Hangar and Roster.</summary>
    public interface IRefitGate
    {
        bool CanRefit(PlayerSlot slot, EntityId mech);
    }

    /// <summary>A constraint value against its limit; Margin is positive when the build is inside the limit.</summary>
    public sealed class ConstraintMargin
    {
        public RefitViolation Kind { get; set; }
        /// <summary>The slot name for a per-slot constraint (torque), else empty.</summary>
        public string Subject { get; set; } = "";
        public double Value { get; set; }
        public double Limit { get; set; }
        public double Margin { get; set; }
        public bool Ok { get; set; }
    }

    /// <summary>MechBuildSpec@v1: what Physics.Body reads. All float64, deterministic; the hash covers chassis, slot parts, mass, CoM and inertia.</summary>
    public sealed class MechBuildSpec
    {
        public string Schema { get; set; } = RefitContractVersions.BuildSpecSchema;
        public string Chassis { get; set; }
        public string[] SlotNames { get; set; }
        public string[] PartIds { get; set; }
        public long[] PartMassKg { get; set; }
        public long TotalMassKg { get; set; }
        /// <summary>Centre of mass in the chassis frame (m).</summary>
        public double ComX { get; set; }
        public double ComY { get; set; }
        public double ComZ { get; set; }
        /// <summary>Inertia tensor about the CoM (kg m2): Ixx, Iyy, Izz, Ixy, Ixz, Iyz (off-diagonals are -sum m x y).</summary>
        public double[] Inertia { get; set; }
        public double PowerRatedW { get; set; }
        public double PowerPeakW { get; set; }
        public double HeatRejectionW { get; set; }
        public double HeatStorageJ { get; set; }
        /// <summary>Rated actuator torque (N m) per slot, aligned with SlotNames; 0 for a slot without an actuator.</summary>
        public double[] TorqueLimitNm { get; set; }
        public long Hash { get; set; }
    }

    public sealed class RefitReport
    {
        public bool Ok { get; set; }
        public RefitViolation FirstViolation { get; set; }
        /// <summary>Slot, part or chassis the first violation concerns.</summary>
        public string Subject { get; set; } = "";
        public string Detail { get; set; } = "";
        /// <summary>Margins in check order (empty when the build failed before the physics, i.e. a structural violation).</summary>
        public List<ConstraintMargin> Margins { get; } = new List<ConstraintMargin>();
        /// <summary>The computed spec when the build is structurally sound (also when a physical limit is broken); null otherwise.</summary>
        public MechBuildSpec Spec { get; set; }
    }

    public enum RefitStatus : byte { Applied = 0, NoChange = 1, Invalid = 2, InsufficientFunds = 3, NotAllowed = 4, UnknownMech = 5, RareUnavailable = 6 }

    public sealed class RefitResult
    {
        public RefitStatus Status { get; set; }
        public RefitReport Report { get; set; }
        public MechBuildSpec Spec { get; set; }
        /// <summary>Net cost in whole US dollars: new parts at price, minus the sell-back of the parts taken out (negative when the refit pays out).</summary>
        public long CostUsd { get; set; }
        public int SlotsChanged { get; set; }
        public int RareConsumed { get; set; }
    }

    /// <summary>Snapshot of a registered mech's build.</summary>
    public sealed class MechBuild
    {
        public EntityId MechId { get; set; }
        public PlayerSlot Slot { get; set; }
        public string Chassis { get; set; }
        public long SpecHash { get; set; }
        public long Refits { get; set; }
        public SortedDictionary<string, string> Parts { get; set; }
        public SortedDictionary<string, double> Wear { get; set; }
        /// <summary>True for slots whose part the city issued (floor chassis): not resellable.</summary>
        public SortedDictionary<string, bool> Issued { get; set; }
    }

    public interface IRefitService
    {
        IPartCatalog Parts { get; }
        IChassisCatalog Chassis { get; }
        /// <summary>The chassis's default build (slot name to part id). Throws ArgumentException for an unknown chassis.</summary>
        IReadOnlyDictionary<string, string> DefaultBuild(string chassisId);
        /// <summary>Pure. Slot structure, then mass, power, heat, torque, CoM box, tilt margin; the first violated one with every margin.</summary>
        RefitReport Validate(string chassisId, IReadOnlyDictionary<string, string> assignment);
        /// <summary>Pure. The spec of a valid build; false with the report otherwise.</summary>
        bool TryBuild(string chassisId, IReadOnlyDictionary<string, string> assignment, out MechBuildSpec spec, out RefitReport report);
        /// <summary>Pure. Price of a whole build, whole US dollars.</summary>
        long PriceOf(string chassisId, IReadOnlyDictionary<string, string> assignment);

        /// <summary>
        /// Registers a mech's initial build (a purchase or the city-issued floor chassis; nothing is charged here). Throws ArgumentException for an invalid
        /// build, one with a rare part (rare parts are fitted through Apply) or an id already registered. cityIssued parts cannot be sold back.
        /// </summary>
        MechBuildSpec Register(EntityId mech, PlayerSlot slot, string chassisId, IReadOnlyDictionary<string, string> assignment, bool cityIssued, long step);
        bool IsRegistered(EntityId mech);
        /// <summary>Snapshot, or null for an unknown id.</summary>
        MechBuild Get(EntityId mech);
        /// <summary>Net cost of changing the mech to <paramref name="assignment"/>; no side effects. Status is Applied when it could be applied now.</summary>
        RefitResult Quote(EntityId mech, IReadOnlyDictionary<string, string> assignment);
        /// <summary>
        /// Hangar refit: validates, checks the gate, rare sets and funds, then pays through Meta.Economy (new parts at price, taken-out parts at the
        /// sell-back, city-issued parts at nothing), consumes rare sets and publishes RefitApplied once. Atomic: any refusal changes nothing.
        /// </summary>
        RefitResult Apply(EntityId mech, IReadOnlyDictionary<string, string> assignment, long step);
        /// <summary>Adds wear (0..1, saturating) to one slot's part. Throws for an unknown mech or slot.</summary>
        void AddWear(EntityId mech, string slotName, double amount);
    }

    /// <summary>A refit was applied (published exactly once per Apply that changed a slot). IDs and numbers only.</summary>
    public struct RefitApplied : IEvent
    {
        public EntityId Mech;
        public PlayerSlot Slot;
        /// <summary>Net cost in whole US dollars (negative when the refit paid out).</summary>
        public long CostUsd;
        public long SpecHash;
        public long Step;
    }
}
