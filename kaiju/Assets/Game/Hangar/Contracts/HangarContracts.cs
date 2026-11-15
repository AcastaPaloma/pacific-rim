// Hangar contracts (layer L3, engine-free). T-034, D-017 / F-060 minimum: a real Bay site, a spawn point and a
// save section that carries body damage and repair state across fight -> hangar -> fight. No refit, upgrades, economy or UI.
using System;
using System.Collections.Generic;
using Game.Core;
using Game.World.Geo;

namespace Game.Hangar
{
    public static class HangarContractVersions
    {
        public const int Contract = 1;
        /// <summary>Version of the "hangar" save section (hangar@v1).</summary>
        public const int SaveSection = 1;
        public const string SectionName = "hangar";
    }

    public static class HangarFlags
    {
        /// <summary>Install the hangar stub service. Default off.</summary>
        public const string Stub = "hangar.stub";
    }

    public enum HangarPhase : byte { Fight = 0, Hangar = 1 }

    /// <summary>One body part's damage as the fight left it. Floats are float64 and carried bit-exact.</summary>
    public struct PartDamage : IEquatable<PartDamage>
    {
        /// <summary>Stable part name: non-empty, no '/' and no control characters, compared ordinally.</summary>
        public string PartId;
        public double Integrity;
        public double MaxIntegrity;
        public long LastHitStep;
        public long Hits;

        public bool Equals(PartDamage o) =>
            string.Equals(PartId, o.PartId, StringComparison.Ordinal)
            && BitConverter.DoubleToInt64Bits(Integrity) == BitConverter.DoubleToInt64Bits(o.Integrity)
            && BitConverter.DoubleToInt64Bits(MaxIntegrity) == BitConverter.DoubleToInt64Bits(o.MaxIntegrity)
            && LastHitStep == o.LastHitStep && Hits == o.Hits;
        public override bool Equals(object obj) => obj is PartDamage o && Equals(o);
        public override int GetHashCode() => (PartId ?? "").Length ^ Hits.GetHashCode();
    }

    /// <summary>
    /// Narrow damage input (what Hangar needs from the body, nothing more). The body module adapts its own record to this
    /// (for the placeholder body: PlaceholderDamageRecord, adapter lives in the composition layer, not in Hangar or Units).
    /// CopyParts lists every part, damaged or not. ApplyParts replaces the record's part state with the given parts.
    /// </summary>
    public interface IBodyDamageRecord
    {
        PlayerSlot Slot { get; }
        void CopyParts(List<PartDamage> into);
        void ApplyParts(IReadOnlyList<PartDamage> parts);
    }

    /// <summary>One part as the hangar holds it: the damage plus the repair state.</summary>
    public struct PartRepairState
    {
        public PartDamage Damage;
        /// <summary>True when repaired in the hangar and not hit since.</summary>
        public bool Repaired;
        /// <summary>Position in the repair queue, -1 = not queued.</summary>
        public long QueueIndex;
    }

    /// <summary>A real site (R-025). Position is float64 BayTM@v1 metres; the spawn height comes from the terrain.</summary>
    public sealed class HangarSite
    {
        public HangarSite(string id, string name, GeoTileId tile, double bayX, double bayZ, double headingDeg, string source,
                          BuildingId anchorBuilding = default, double anchorOffsetX = 0, double anchorOffsetZ = 0)
        {
            Id = string.IsNullOrEmpty(id) ? throw new ArgumentException("Empty site id") : id;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Tile = tile; BayX = bayX; BayZ = bayZ; HeadingDeg = headingDeg; Source = source ?? "";
            AnchorBuilding = anchorBuilding; AnchorOffsetX = anchorOffsetX; AnchorOffsetZ = anchorOffsetZ;
            if (!tile.Contains(bayX, bayZ)) throw new ArgumentException("Site " + id + " position is outside tile " + tile);
        }
        public string Id { get; }
        public string Name { get; }
        public GeoTileId Tile { get; }
        public double BayX { get; }
        public double BayZ { get; }
        /// <summary>Compass heading of the body at spawn, degrees clockwise from north (+z).</summary>
        public double HeadingDeg { get; }
        public string Source { get; }
        /// <summary>When valid and present in the loaded world, the spawn is its Geo anchor plus the offset instead of BayX/BayZ.</summary>
        public BuildingId AnchorBuilding { get; }
        public double AnchorOffsetX { get; }
        public double AnchorOffsetZ { get; }
    }

    public readonly struct HangarSpawn
    {
        public HangarSpawn(HangarSite site, double x, double y, double z, double headingDeg, bool terrainKnown, bool fromAnchor)
        { Site = site; X = x; Y = y; Z = z; HeadingDeg = headingDeg; TerrainKnown = terrainKnown; FromAnchor = fromAnchor; }
        public HangarSite Site { get; }
        public double X { get; }
        /// <summary>NAVD88 metres of the data surface; NaN when TerrainKnown is false (tile not loaded).</summary>
        public double Y { get; }
        public double Z { get; }
        public double HeadingDeg { get; }
        public bool TerrainKnown { get; }
        public bool FromAnchor { get; }
    }

    /// <summary>The stub's whole surface. Repair is instant and free (debug panel); no refit, upgrades or economy.</summary>
    public interface IHangarService
    {
        HangarSite Site { get; }
        HangarPhase GetPhase(PlayerSlot slot);
        /// <summary>Completed hangar visits (incremented by LeaveHangar).</summary>
        long GetCycle(PlayerSlot slot);
        /// <summary>Copies the body's parts (ordinal part order). False when the slot has no body in the hangar record.</summary>
        bool TryGetParts(PlayerSlot slot, List<PartRepairState> into);
        /// <summary>Fight over: copies every part from the damage record into the hangar and enters the hangar phase.</summary>
        void EnterHangar(IBodyDamageRecord record);
        void QueueRepair(PlayerSlot slot, string partId);
        /// <summary>Queues every part below its maximum. Returns how many were queued.</summary>
        int QueueAllDamaged(PlayerSlot slot);
        /// <summary>Repairs the queued parts in queue order (integrity = max, Repaired = true). Returns how many.</summary>
        int ProcessRepairs(PlayerSlot slot);
        /// <summary>Writes the hangar's parts into the record, returns to the fight phase and returns the spawn.</summary>
        HangarSpawn LeaveHangar(IBodyDamageRecord record);
        HangarSpawn ResolveSpawn();
    }
}
