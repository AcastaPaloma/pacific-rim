// World.Infrastructure contracts (layer L2): the F-044 targets (bridges, power, port cranes, airport assets) as an engine-free registry (T-105).
// Data comes from the baked `infra@v1` sidecar layer of Pipelines/geo (OSM, ODbL). Damage goes through World.Destruction's IDamageModel.
using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Core;
using Game.World.Destruction;

namespace Game.World.Infrastructure
{
    public static class InfrastructureContractVersions
    {
        /// <summary>IInfrastructureRegistry / InfraId shape.</summary>
        public const int RegistryApi = 1;
        /// <summary>Version of the Assumed damage proxies (<c>InfraDamageProfile</c>). Bump on any change.</summary>
        public const int ModelRevision = 1;
        public const string LayerSpec = "infra@v1";
        public const string LayerFile = "infrastructure.geojson";
    }

    public static class InfrastructureFlags
    {
        /// <summary>Register the infrastructure registry. Default off (Config/FeatureFlags/WorldInfrastructure.json).</summary>
        public const string Enabled = "infrastructure.enabled";
    }

    /// <summary>Target class. The byte equals `kind_id` in Pipelines/geo/config/infra.json and is saved; append only.</summary>
    public enum InfraKind : byte
    {
        None = 0,
        Bridge = 1,
        PowerTower = 2,
        PowerLine = 3,
        Substation = 4,
        PowerPlant = 5,
        PortCrane = 6,
        Runway = 7,
        Terminal = 8,
        Hangar = 9,
        ControlTower = 10,
    }

    /// <summary>Namespace of the source id. Append only.</summary>
    public enum InfraSource : byte
    {
        None = 0,
        OsmWay = 1,
        OsmNode = 2,
    }

    /// <summary>Kinds as a bit set for queries (bit = 1 &lt;&lt; (byte)kind).</summary>
    public static class InfraKinds
    {
        public static uint Mask(InfraKind k) => 1u << (int)k;
        public static uint Mask(params InfraKind[] kinds) { uint m = 0; foreach (var k in kinds) m |= Mask(k); return m; }
        public const uint All = 0x7FEu;
        public static readonly uint Power = Mask(InfraKind.PowerTower, InfraKind.PowerLine, InfraKind.Substation, InfraKind.PowerPlant);
        public static readonly uint Airport = Mask(InfraKind.Runway, InfraKind.Terminal, InfraKind.Hangar, InfraKind.ControlTower);

        public static string Token(InfraKind k)
        {
            switch (k)
            {
                case InfraKind.Bridge: return "bridge";
                case InfraKind.PowerTower: return "power-tower";
                case InfraKind.PowerLine: return "power-line";
                case InfraKind.Substation: return "substation";
                case InfraKind.PowerPlant: return "power-plant";
                case InfraKind.PortCrane: return "port-crane";
                case InfraKind.Runway: return "runway";
                case InfraKind.Terminal: return "terminal";
                case InfraKind.Hangar: return "hangar";
                case InfraKind.ControlTower: return "control-tower";
                default: return null;
            }
        }
    }

    /// <summary>
    /// Persistent identity of one infrastructure object = (kind, source namespace, 52-bit source id), e.g. the OSM way of a bridge.
    /// Independent of load order. Canonical string "inf:bridge:osm-way:123".
    /// </summary>
    public readonly struct InfraId : IEquatable<InfraId>, IComparable<InfraId>
    {
        public const ulong MaxSourceId = (1UL << 52) - 1;
        public readonly InfraKind Kind;
        public readonly InfraSource Source;
        public readonly ulong SourceId;
        InfraId(InfraKind kind, InfraSource source, ulong id) { Kind = kind; Source = source; SourceId = id; }

        public static InfraId From(InfraKind kind, InfraSource source, ulong sourceId)
        {
            if (InfraKinds.Token(kind) == null) throw new ArgumentOutOfRangeException(nameof(kind), "Unknown infrastructure kind " + (int)kind);
            if (source != InfraSource.OsmWay && source != InfraSource.OsmNode) throw new ArgumentOutOfRangeException(nameof(source));
            if (sourceId > MaxSourceId) throw new ArgumentOutOfRangeException(nameof(sourceId), "Source id does not fit 52 bits");
            return new InfraId(kind, source, sourceId);
        }

        public bool IsValid => Kind != InfraKind.None;
        public bool Equals(InfraId o) => Kind == o.Kind && Source == o.Source && SourceId == o.SourceId;
        public override bool Equals(object obj) => obj is InfraId o && Equals(o);
        public override int GetHashCode() => unchecked((int)(SourceId * 0x9E3779B97F4A7C15UL >> 32) ^ ((int)Kind << 24) ^ ((int)Source << 20));
        public int CompareTo(InfraId o) => Kind != o.Kind ? Kind.CompareTo(o.Kind) : Source != o.Source ? Source.CompareTo(o.Source) : SourceId.CompareTo(o.SourceId);
        public static bool operator ==(InfraId a, InfraId b) => a.Equals(b);
        public static bool operator !=(InfraId a, InfraId b) => !a.Equals(b);

        public override string ToString() =>
            IsValid ? "inf:" + InfraKinds.Token(Kind) + ":" + (Source == InfraSource.OsmWay ? "osm-way" : "osm-node") + ":" + SourceId.ToString(CultureInfo.InvariantCulture) : "inf:none";

        public static bool TryParse(string s, out InfraId id)
        {
            id = default;
            if (s == null || !s.StartsWith("inf:", StringComparison.Ordinal)) return false;
            string[] p = s.Split(':');
            if (p.Length != 4) return false;
            InfraKind kind = InfraKind.None;
            for (int k = 1; k <= 10; k++) if (InfraKinds.Token((InfraKind)k) == p[1]) kind = (InfraKind)k;
            InfraSource src = p[2] == "osm-way" ? InfraSource.OsmWay : p[2] == "osm-node" ? InfraSource.OsmNode : InfraSource.None;
            if (kind == InfraKind.None || src == InfraSource.None) return false;
            for (int i = 0; i < p[3].Length; i++) if (p[3][i] < '0' || p[3][i] > '9') return false;
            if (!ulong.TryParse(p[3], NumberStyles.None, CultureInfo.InvariantCulture, out ulong v) || v > MaxSourceId) return false;
            id = new InfraId(kind, src, v); return true;
        }

        /// <summary>The Core <see cref="BuildingId"/> under which the damage model keeps this object (source `infra`).</summary>
        public BuildingId ToBuildingId() =>
            BuildingId.From(BuildingSource.Infrastructure, ((ulong)(byte)Kind << 56) | ((ulong)(byte)Source << 52) | SourceId);

        public static bool TryFromBuildingId(BuildingId b, out InfraId id)
        {
            id = default;
            if (b.Source != BuildingSource.Infrastructure) return false;
            var kind = (InfraKind)(byte)(b.SourceId >> 56);
            var src = (InfraSource)(byte)((b.SourceId >> 52) & 0xF);
            if (InfraKinds.Token(kind) == null || (src != InfraSource.OsmWay && src != InfraSource.OsmNode)) return false;
            id = new InfraId(kind, src, b.SourceId & MaxSourceId); return true;
        }
    }

    /// <summary>One baked target. Coordinates are BayTM@v1 metres (x easting, z northing); heights are not baked (read the terrain).</summary>
    public sealed class InfraRecord
    {
        public InfraId Id;
        /// <summary>L0 tile that owns the record ("L0_30_36"), "" when added by hand.</summary>
        public string Tile = "";
        /// <summary>Owner point: the node, the arc-length midpoint of a line, an interior point of an area.</summary>
        public double X, Z;
        public double MinX, MinZ, MaxX, MaxZ;
        /// <summary>x,z pairs: 1 pair for a node, the polyline, or the outer ring of an area (closed, last = first).</summary>
        public double[] Points = Array.Empty<double>();
        public bool IsArea;
        /// <summary>Tagged or measured values; NaN when the source has none (never invented).</summary>
        public double LengthM = double.NaN, AreaM2 = double.NaN, WidthM = double.NaN, HeightM = double.NaN, VoltageV = double.NaN, Lanes = double.NaN;
        public int Layer;
        public bool Movable, Footway;
        /// <summary>Slug of the F-041 landmark this bridge is part of (T-104 registry), or null.</summary>
        public string Landmark;
        /// <summary>Raw OSM class (highway/railway value, crane type, ...), or null.</summary>
        public string Class;
    }

    /// <summary>Result of <see cref="IInfrastructureRegistry.Apply"/>.</summary>
    public struct InfraDamageChange
    {
        public bool Known;
        public bool Changed;
        public DamageState Prev, Current;
        public double CumulativeEnergyJ;
    }

    /// <summary>
    /// Registry of the loaded infrastructure targets (registered by World.Infrastructure). Queries are deterministic (ties broken by InfraId).
    /// Damage is stored by World.Destruction's <see cref="IDamageModel"/> under <see cref="InfraId.ToBuildingId"/>; this interface forwards to it.
    /// </summary>
    public interface IInfrastructureRegistry
    {
        int Count { get; }
        int CountOf(InfraKind kind);
        bool TryGet(InfraId id, out InfraRecord record);
        /// <summary>Records whose geometry lies within <paramref name="radiusM"/> of (x,z), kinds in <paramref name="kindMask"/>, sorted by distance then id.</summary>
        void QueryRadius(double x, double z, double radiusM, uint kindMask, List<InfraRecord> into);
        /// <summary>Nearest record by distance to its geometry (0 inside an area) within <paramref name="maxRadiusM"/>.</summary>
        bool TryNearest(double x, double z, uint kindMask, double maxRadiusM, out InfraRecord record, out double distanceM);
        /// <summary>Damage state of the target (None when undamaged or unknown).</summary>
        DamageState StateOf(InfraId id);
        /// <summary>False when the target is unknown or at <see cref="DamageState.Complete"/> (a collapsed bridge, a destroyed crane).</summary>
        bool IsOperational(InfraId id);
        /// <summary>Delivers energy (J) to a target through the damage model; publishes BuildingDamaged on the bus like any building.</summary>
        InfraDamageChange Apply(InfraId id, double energyJ, DamageCause cause, EntityId source, long step);
    }
}
