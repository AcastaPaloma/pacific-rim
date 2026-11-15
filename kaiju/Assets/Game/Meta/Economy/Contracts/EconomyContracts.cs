using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Meta.Economy
{
    // Meta.Economy contract v1 (R-104 section 10.1, Q-205, Q-206). Engine-free, deterministic. Money is a long count of whole US dollars
    // (never floating point); ratios are integer basis points (1 bp = 0.01%). Physical quantities (mass) are long kilograms.

    public static class EconomyContractVersions
    {
        public const int Contract = 1;
        /// <summary>Version of the "economy" save section (economy@v1).</summary>
        public const int SaveSection = 1;
        public const string SectionName = "economy";
        public const string PricesSchema = "economy_prices@v1";
        /// <summary>Last N transactions kept in the save (R-104: ring of 256).</summary>
        public const int TransactionRing = 256;
    }

    public static class EconomyFlags
    {
        public const string Economy = "meta.economy";
    }

    /// <summary>Hazus-style structural damage state (R-021 section 1); same order as World.Destruction's DamageState.</summary>
    public enum AssetDamageState : byte { None = 0, Slight = 1, Moderate = 2, Extensive = 3, Complete = 4 }

    public enum ContractState : byte { Offered = 0, Accepted = 1, Completed = 2, Failed = 3, Declined = 4 }

    /// <summary>Outcome code carried by ContractCompleted.</summary>
    public enum ContractResult : byte { Completed = 0, Failed = 1 }

    public enum TransactionReason : byte
    {
        ContractPayout = 1, ContractAdvance = 2, AdvanceSettled = 3, Purchase = 4, Repair = 5, Upkeep = 6, Sale = 7, SalvageCredit = 8,
        ArrearsCollected = 9, FloorChassis = 10, Other = 11,
    }

    /// <summary>One protected asset: a stable string id (building id, landmark id) and its replacement cost.</summary>
    public readonly struct ContractAsset
    {
        public ContractAsset(string assetId, long replacementUsd) { AssetId = assetId; ReplacementUsd = replacementUsd; }
        public string AssetId { get; }
        public long ReplacementUsd { get; }
    }

    /// <summary>Immutable view of a contract.</summary>
    public sealed class ContractInfo
    {
        public long Id { get; set; }
        public ContractState State { get; set; }
        public ContractAsset[] Assets { get; set; }
        /// <summary>Sum of asset replacement costs (V_k).</summary>
        public long ValueUsd { get; set; }
        public long AdvanceUsd { get; set; }
        public long PayoutUsd { get; set; }
        public long LossUsd { get; set; }
        /// <summary>Accepting slot index, -1 while offered.</summary>
        public int AcceptedBySlot { get; set; } = -1;
        public long Step { get; set; }
    }

    /// <summary>Final damage state of every asset of a contract, aligned with ContractInfo.Assets.</summary>
    public sealed class ContractOutcome
    {
        public ContractOutcome(AssetDamageState[] finalStates, long step) { FinalStates = finalStates ?? throw new ArgumentNullException(nameof(finalStates)); Step = step; }
        public AssetDamageState[] FinalStates { get; }
        public long Step { get; }
    }

    public readonly struct PriceQuote
    {
        public PriceQuote(string classId, long massKg, long usdPerTonne, long priceUsd, bool assumed, string source)
        { ClassId = classId; MassKg = massKg; UsdPerTonne = usdPerTonne; PriceUsd = priceUsd; Assumed = assumed; Source = source; }
        public string ClassId { get; }
        public long MassKg { get; }
        public long UsdPerTonne { get; }
        public long PriceUsd { get; }
        public bool Assumed { get; }
        public string Source { get; }
    }

    /// <summary>Result of the Q-206 floor rule.</summary>
    public readonly struct FloorGrant
    {
        public FloorGrant(bool granted, string chassisId, long valueUsd, long grantCount)
        { Granted = granted; ChassisId = chassisId; ValueUsd = valueUsd; GrantCount = grantCount; }
        public bool Granted { get; }
        public string ChassisId { get; }
        /// <summary>Price of the chassis in the price table; it costs the player nothing and cannot be resold.</summary>
        public long ValueUsd { get; }
        /// <summary>Times the floor chassis has now been granted to this slot.</summary>
        public long GrantCount { get; }
    }

    public interface IEconomyService
    {
        /// <summary>Spendable funds, always >= 0.</summary>
        long Funds { get; }
        /// <summary>Unpaid forfeited advances and upkeep shortfalls; collected from later income before it reaches Funds. Never negative.</summary>
        long Arrears { get; }

        /// <summary>Deterministic offer list from a seed. At least one offer has an expected payout >= the guaranteed minimum when the pool allows.</summary>
        IReadOnlyList<ContractInfo> OfferContracts(long seed, long step, IReadOnlyList<ContractAsset> pool, int count);
        ContractInfo GetContract(long id);
        /// <summary>Accepts an offered contract; credits the advance. Throws InvalidOperationException when not Offered.</summary>
        ContractInfo Accept(long contractId, PlayerSlot slot);
        /// <summary>Declines an offered contract (no obligation, no debt).</summary>
        void Decline(long contractId);
        /// <summary>Settles an accepted contract once (payout per R-104 section 3) and publishes ContractCompleted once.</summary>
        ContractInfo Complete(long contractId, ContractOutcome outcome);

        /// <summary>Debits when funds suffice; otherwise returns false and changes nothing (the purchase is queued by the caller).</summary>
        bool TryPurchase(long priceUsd, TransactionReason reason, long step);
        void Credit(long amountUsd, TransactionReason reason, long step);
        /// <summary>Pays upkeep down to zero funds; the unpaid remainder becomes Arrears (never debt). Returns the amount actually paid.</summary>
        long PayUpkeep(long amountUsd, long step);

        /// <summary>Q-206: grants the city-funded floor chassis when the slot has no flyable mech. Idempotent while the slot owns one.</summary>
        FloorGrant TryGrantFloorChassis(PlayerSlot slot, int flyableMechCount, long step);
        long FloorGrantCount(PlayerSlot slot);
    }

    /// <summary>Contract completed (published exactly once per contract). IDs and numbers only.</summary>
    public struct ContractCompleted : IEvent
    {
        public long ContractId;
        public PlayerSlot Slot;
        public byte Outcome;       // ContractResult
        public long PayoutUsd;
    }
}
