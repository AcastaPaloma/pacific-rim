// World.Destruction contracts (layer L2): engine-free data types and interfaces for persistent building damage (F-043).
// Other modules reference only this assembly. Numbers behind the states: research/R-021.md.
using Game.Core;

namespace Game.World.Destruction
{
    public static class DestructionContractVersions
    {
        /// <summary>IDamageModel / BuildingDamaged shape.</summary>
        public const int DamageApi = 1;
        /// <summary>Version of the capacity and fragility tables; saved as `model_rev` (R-021 §5).</summary>
        public const int ModelRevision = 1;
    }

    /// <summary>Damage-state scale (R-021 §1, Hazus-style [S1]). Append only; the byte is saved and published.</summary>
    public enum DamageState : byte
    {
        None = 0,
        Slight = 1,
        Moderate = 2,
        Extensive = 3,
        Complete = 4,
    }

    /// <summary>Why damage happened (R-021 §5). Append only.</summary>
    public enum DamageCause : byte
    {
        Impact = 0,
        Shaking = 1,
        FaultRupture = 2,
        Debris = 3,
    }

    /// <summary>
    /// Published on the Core bus once per state CHANGE (not per sub-threshold energy increment), same step.
    /// First three fields are fixed by plans/M1.md Track 2; the rest are R-021 §5 extensions.
    /// </summary>
    public struct BuildingDamaged : IEvent
    {
        public BuildingId BuildingId;
        /// <summary>New <see cref="DamageState"/>.</summary>
        public byte State;
        /// <summary>Energy delivered by the contact that caused this change (J), ΔKE of the physics contact record.</summary>
        public float EnergyJ;
        public byte PrevState;
        /// <summary><see cref="DamageCause"/>.</summary>
        public byte Cause;
        /// <summary>Impactor entity; default = none.</summary>
        public EntityId Source;
        /// <summary>Debris mass emitted by this change, kg (increment).</summary>
        public float DebrisKg;
    }

    /// <summary>Per-building damage record, keyed by <see cref="BuildingId"/>. Unmanaged; the save keeps float64.</summary>
    public struct BuildingDamageState
    {
        public BuildingId Id;
        /// <summary><see cref="DamageState"/>; monotone non-decreasing.</summary>
        public byte State;
        /// <summary>Cumulative delivered energy, J.</summary>
        public double CumulativeEnergyJ;
        /// <summary>Debris mass emitted so far, kg.</summary>
        public double DebrisKg;
        /// <summary>Clock step of the last state change.</summary>
        public long LastStep;
        /// <summary>Cause of the last change (<see cref="DamageCause"/>).</summary>
        public byte Cause;
    }

    /// <summary>The attributes the capacity model reads (all available from geo_export@v1, R-021 §0).</summary>
    public struct BuildingAttributes
    {
        /// <summary>Height H, metres.</summary>
        public double HeightM;
        /// <summary>building:levels, or 0 when unknown (then round(H / 4.22 m), min 1; R-004 §1.4).</summary>
        public int Levels;
        /// <summary>Footprint area A, m².</summary>
        public double FootprintM2;
        /// <summary>use = residential (selects the light wood-frame class).</summary>
        public bool Residential;
    }

    /// <summary>Result of <see cref="IDamageModel.Apply"/>.</summary>
    public struct DamageChange
    {
        /// <summary>False when the building is not registered (nothing applied).</summary>
        public bool Known;
        /// <summary>True when the state byte increased.</summary>
        public bool Changed;
        public byte PrevState;
        public BuildingDamageState Current;
    }

    /// <summary>
    /// Damage model service (registered by World.Destruction). Deterministic: the same attributes and the same
    /// sequence of energies give the same states, in any process, with no clock or Random.
    /// </summary>
    public interface IDamageModel
    {
        /// <summary>Registers a building (idempotent: a known id keeps its damage and gets the new capacity).</summary>
        void Register(BuildingId id, in BuildingAttributes attributes);
        bool TryGetState(BuildingId id, out BuildingDamageState state);
        /// <summary>
        /// Adds delivered energy (J, ΔKE of a contact). Non-positive or non-finite energy is ignored. State = highest
        /// k with cumulative energy at or above that building's E_k. Never allocates in steady state.
        /// When the state changes, the model publishes <see cref="BuildingDamaged"/> if it has a bus.
        /// </summary>
        DamageChange Apply(BuildingId id, double energyJ, DamageCause cause, EntityId source, long step);
        /// <summary>Energy to reach damage state 1..4 for the registered building, J, incl. its deterministic scatter.</summary>
        bool TryGetThresholds(BuildingId id, out double slightJ, out double moderateJ, out double extensiveJ, out double completeJ);
    }
}
