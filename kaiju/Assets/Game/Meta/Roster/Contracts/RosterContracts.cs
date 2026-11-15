using System;
using System.Collections.Generic;
using Game.Core;
using Game.Hangar;

namespace Game.Meta.Roster
{
    // Meta.Roster contract v1 (R-104 sections 6, 10.3). Engine-free, deterministic. Permadeath (D-020, F-010, Q-206): a mech is Lost when its core
    // part is at integrity 0 at fight end or the player scraps it; Lost is absorbing; the city-funded floor chassis means a slot is never without a
    // flyable mech. Salvage of the wreck is handed over by the composition layer (T-122) using MechInstance.WreckId (idempotent there by WreckId).

    public static class RosterContractVersions
    {
        public const int Contract = 1;
        /// <summary>Version of the "roster" save section (roster@v1).</summary>
        public const int SaveSection = 1;
        public const string SectionName = "roster";
    }

    public static class RosterFlags
    {
        /// <summary>Install the roster (permadeath rules). Default off.</summary>
        public const string Permadeath = "meta.permadeath";
    }

    /// <summary>State of a mech. Saved as a byte; append only. Lost is absorbing.</summary>
    public enum MechState : byte { Flyable = 0, Lost = 1 }

    /// <summary>Why a mech was lost. Saved as a byte; append only. None only while Flyable.</summary>
    public enum LossCause : byte { None = 0, CoreDestroyed = 1, Scrapped = 2 }

    /// <summary>Snapshot of one mech instance (tombstones stay in the roster: the record, the wreck id and the pilot record are kept).</summary>
    public sealed class MechInstance
    {
        public EntityId MechId { get; set; }
        public PlayerSlot Slot { get; set; }
        public string Chassis { get; set; }
        public MechState State { get; set; }
        /// <summary>Hash of the MechBuildSpec the mech was built to (0 = none yet; Meta.Refit fills it).</summary>
        public long SpecHash { get; set; }
        /// <summary>Stable positive id of the wreck, set once at the loss, 0 while flyable. Deterministic from mech id and step.</summary>
        public long WreckId { get; set; }
        /// <summary>Fights survived (hangar cycles).</summary>
        public long Cycles { get; set; }
        /// <summary>True for the city-funded floor chassis (Q-206): free, non-resellable, cannot be scrapped.</summary>
        public bool IsFloor { get; set; }
        public LossCause Cause { get; set; }
        public long CreatedStep { get; set; }
        public long LossStep { get; set; }
    }

    /// <summary>The pilot record of one slot (F-008 strain, D-019). No pilot death in v1 (Q-NEW-d deferred).</summary>
    public sealed class PilotRecord
    {
        public PlayerSlot Slot { get; set; }
        /// <summary>Last recorded pilot strain, 0..1.</summary>
        public double Strain { get; set; }
        public long Sorties { get; set; }
        /// <summary>Sorties of strain recovery still owed after a loss (1 after a loss, 0 otherwise).</summary>
        public long RecoverySorties { get; set; }
        public long MechsLost { get; set; }
    }

    /// <summary>What a fight end did to the slot's mech.</summary>
    public sealed class FightEndResult
    {
        public EntityId MechId { get; set; }
        public bool Lost { get; set; }
        /// <summary>True when the mech had already been lost: nothing changed and nothing was published.</summary>
        public bool Replay { get; set; }
        public long WreckId { get; set; }
        public LossCause Cause { get; set; }
        /// <summary>True when this call granted the floor chassis because the slot had no flyable mech left.</summary>
        public bool FloorGranted { get; set; }
        public EntityId FloorMechId { get; set; }
    }

    public interface IRosterService
    {
        /// <summary>Adds a flyable mech to the slot (and makes it active when the slot has no active mech). Throws ArgumentException for an empty chassis.</summary>
        MechInstance Create(PlayerSlot slot, string chassis, long specHash, long step);
        /// <summary>Stores the MechBuildSpec hash after a refit (T-122). Throws for an unknown mech; a Lost mech keeps its last hash and the call is ignored.</summary>
        void SetSpecHash(EntityId mechId, long specHash);
        /// <summary>Snapshot, or null for an unknown id.</summary>
        MechInstance Get(EntityId mechId);
        /// <summary>Every mech of the slot in id order, tombstones included. Clears <paramref name="into"/> first.</summary>
        void Mechs(PlayerSlot slot, List<MechInstance> into);
        int FlyableCount(PlayerSlot slot);
        /// <summary>The mech flown next: the one set with SetActive, else the lowest-id flyable. Invalid id when the slot has none.</summary>
        EntityId ActiveMech(PlayerSlot slot);
        /// <summary>Throws InvalidOperationException unless the mech is Flyable and owned by the slot.</summary>
        void SetActive(PlayerSlot slot, EntityId mechId);
        PilotRecord Pilot(PlayerSlot slot);
        /// <summary>
        /// Player scraps a mech: Lost (cause Scrapped), wreck id assigned, MechLost published once. A Lost mech returns its stored snapshot unchanged.
        /// The floor chassis cannot be scrapped (non-resellable; stops a grant-and-scrap loop): throws InvalidOperationException.
        /// </summary>
        MechInstance Retire(EntityId mechId, long step);
        /// <summary>
        /// Fight over for the slot's active mech: evaluates the loss rule on the core parts, counts the sortie for the pilot and records the strain.
        /// On a loss: absorbing Lost, wreck id, MechLost once, strain recovery, and the floor chassis when no flyable mech is left.
        /// </summary>
        FightEndResult OnFightEnd(IBodyDamageRecord record, long step, double pilotStrain = 0.0);
        /// <summary>As above for an explicit mech. A mech already Lost gives a Replay result and changes nothing.</summary>
        FightEndResult OnFightEnd(EntityId mechId, IBodyDamageRecord record, long step, double pilotStrain = 0.0);
        /// <summary>Q-206: grants the floor chassis when the slot has no flyable mech (never stacked). Returns the new mech, or null.</summary>
        MechInstance GrantFloor(PlayerSlot slot, long step);
        /// <summary>Lowest EntityId value that the entity allocator must not issue after a load (greater than every roster mech id).</summary>
        ulong NextEntityValue { get; }
        /// <summary>The slot's floor grants so far.</summary>
        long FloorGrants(PlayerSlot slot);
    }

    /// <summary>A mech was lost (published exactly once per mech). IDs and numbers only.</summary>
    public struct MechLost : IEvent
    {
        public EntityId MechId;
        public PlayerSlot Slot;
        public long WreckId;
        public byte Cause;     // LossCause
        public long Step;
    }
}
