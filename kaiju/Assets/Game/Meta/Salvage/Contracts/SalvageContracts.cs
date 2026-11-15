using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Meta.Salvage
{
    // Meta.Salvage contract v1 (R-104 sections 5, 7, 10.2, 10.5). Engine-free, deterministic. Physical quantities (mass) are float64 kilograms
    // (saved as raw bits); money is a long count of whole US dollars. The adapter from IBodyDamageRecord / BuildingDamaged / the kaiju damage
    // record to SalvageInput lives in the composition layer (T-122); this module sees only the narrow input below.

    public static class SalvageContractVersions
    {
        public const int Contract = 1;
        /// <summary>Version of the "salvage" save section (salvage@v1).</summary>
        public const int SaveSection = 1;
        public const string SectionName = "salvage";
        public const string YieldsSchema = "salvage_yields@v1";
    }

    public static class SalvageFlags
    {
        public const string Salvage = "meta.salvage";
    }

    /// <summary>Where the wreck lies (R-104 5.1). Saved and published as a byte; append only.</summary>
    public enum SalvageLocation : byte { Land = 0, ShallowBay = 1, DeepChannel = 2 }

    /// <summary>What produced the wreck. Saved and published as a byte; append only.</summary>
    public enum SalvageSourceKind : byte { Mech = 0, Kaiju = 1, Building = 2 }

    /// <summary>
    /// One part of a wreck. ClassId is a material class of salvage_yields@v1 (mech: structure, armour, titanium, composite, drive, reactor, weapons;
    /// kaiju: bone, hide, tendon; building: debris_steel, debris_other). AbsorbedEnergyJ is the energy the part took in the fight (focused hits);
    /// the fall energy is carried once on the input. Integrity, MaxIntegrity and PriceUsd matter for mech parts only (reuse value).
    /// </summary>
    public readonly struct SalvagePart
    {
        public SalvagePart(string partId, string classId, double massKg, double absorbedEnergyJ, double integrity, double maxIntegrity, long priceUsd)
        { PartId = partId; ClassId = classId; MassKg = massKg; AbsorbedEnergyJ = absorbedEnergyJ; Integrity = integrity; MaxIntegrity = maxIntegrity; PriceUsd = priceUsd; }
        public string PartId { get; }
        public string ClassId { get; }
        public double MassKg { get; }
        public double AbsorbedEnergyJ { get; }
        public double Integrity { get; }
        public double MaxIntegrity { get; }
        /// <summary>Installed price of the part (Meta.Economy quote); 0 when unknown (no reuse value, scrap only).</summary>
        public long PriceUsd { get; }
    }

    /// <summary>
    /// Wreck to salvage handoff (T-120 Roster, composition adapters). WreckId is stable and positive (deterministic from mech id and step);
    /// the handoff is idempotent by it. BuildPriceUsd is the full build price of a Mech wreck and drives the credit cap (R-104 5.2: 12%).
    /// </summary>
    public sealed class SalvageInput
    {
        public long WreckId { get; set; }
        public SalvageSourceKind SourceKind { get; set; }
        public SalvageLocation Location { get; set; }
        /// <summary>Specific energy of the fall, J/kg (g x CoM height: 392 for the 40 m reference toppling). Zero for debris.</summary>
        public double FallEnergyJPerKg { get; set; }
        /// <summary>Mech wrecks only: price of the whole mech; must be positive for a Mech.</summary>
        public long BuildPriceUsd { get; set; }
        public SalvagePart[] Parts { get; set; }
    }

    /// <summary>What happened to one part.</summary>
    public readonly struct PartRecovery
    {
        public PartRecovery(string partId, string classId, double availableKg, double fragmentedFraction, double recoveredKg, long creditUsd, bool reused)
        { PartId = partId; ClassId = classId; AvailableKg = availableKg; FragmentedFraction = fragmentedFraction; RecoveredKg = recoveredKg; CreditUsd = creditUsd; Reused = reused; }
        public string PartId { get; }
        public string ClassId { get; }
        public double AvailableKg { get; }
        /// <summary>f_frag of R-104 5.1 for this part.</summary>
        public double FragmentedFraction { get; }
        public double RecoveredKg { get; }
        public long CreditUsd { get; }
        /// <summary>True when the credit is a reuse value (intact enough to refit), false when scrap.</summary>
        public bool Reused { get; }
    }

    /// <summary>Pure result of assessing a wreck (nothing is credited or stored).</summary>
    public sealed class SalvageResult
    {
        public long WreckId { get; set; }
        public SalvageSourceKind SourceKind { get; set; }
        public SalvageLocation Location { get; set; }
        public double AvailableKg { get; set; }
        public double RecoveredKg { get; set; }
        /// <summary>Credit before the wreck-credit cap.</summary>
        public long RawCreditUsd { get; set; }
        /// <summary>Credit paid into Meta.Economy (after the cap).</summary>
        public long CreditUsd { get; set; }
        public bool CapApplied { get; set; }
        public PartRecovery[] Parts { get; set; }
    }

    /// <summary>Outcome of Claim. Replay is true when the wreck had already been claimed; then Result is null and nothing was credited again.</summary>
    public sealed class SalvageClaim
    {
        public long WreckId { get; set; }
        public bool Replay { get; set; }
        public SalvageSourceKind SourceKind { get; set; }
        public SalvageLocation Location { get; set; }
        public double RecoveredKg { get; set; }
        public long CreditUsd { get; set; }
        public SalvageResult Result { get; set; }
    }

    public interface ISalvageService
    {
        /// <summary>Pure: recoverable mass and credit of a wreck. Throws ArgumentException for an invalid input.</summary>
        SalvageResult Assess(SalvageInput input);
        /// <summary>
        /// Idempotent by WreckId. First call stores the wreck, adds recovered mass to the class inventory, credits Meta.Economy
        /// (TransactionReason.SalvageCredit, capped) and publishes SalvageRecovered once; later calls return the stored summary and change nothing.
        /// </summary>
        SalvageClaim Claim(SalvageInput input, long step);
        bool IsClaimed(long wreckId);
        /// <summary>Total recovered mass of a material class, kg (never decreases).</summary>
        double InventoryKg(string classId);
        /// <summary>Rare part sets earned and not yet consumed (osteoderm_plate, keratin_liner, actuator_fibre). Unlocks, not currency.</summary>
        long RareAvailable(string rarePartId);
        /// <summary>Consumes rare sets on fitting (called by Meta.Refit). False and no change when fewer are available.</summary>
        bool TryConsumeRare(string rarePartId, long count);
        long TotalCreditUsd { get; }
    }

    /// <summary>A wreck was salvaged (published exactly once per WreckId). IDs and numbers only.</summary>
    public struct SalvageRecovered : IEvent
    {
        public long WreckId;
        public byte SourceKind;   // SalvageSourceKind
        public byte Location;     // SalvageLocation
        public double RecoveredKg;
        public long CreditUsd;
    }
}
