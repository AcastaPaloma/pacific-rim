// World.Emergence contracts (layer L2): Bay-wide emergence of k kaiju clusters along a World.Seismic rupture (F-020, R-103 4.5, T-114).
// Engine-free. Events are unmanaged (ids and numbers only). Nothing here references World.Seismic runtime; the trace is passed as
// plain arrays (`EmergenceTrace`) and the rupture is read from the Seismic.Contracts events.
using Game.Core;

namespace Game.World.Emergence
{
    public static class EmergenceContractVersions
    {
        /// <summary>Event shapes (EmergenceSiteSelected, KaijuEmerged, EmergenceSequenceDone) and the input structs.</summary>
        public const int EventApi = 1;
        /// <summary>Version of the Assumed constants (mix table, cluster spread, time window, band). Bump on any change.</summary>
        public const int ModelRevision = 1;
    }

    public static class EmergenceFlags
    {
        /// <summary>Plan and publish the Bay-wide emergence sequence on every FaultRupture. Default off (Config/FeatureFlags/WorldEmergence.json).</summary>
        public const string Enabled = "emergence.enabled";
    }

    /// <summary>
    /// Species ids used in events. Same numbering as `Game.AI.Pack.SpeciesRole.SpeciesId` (Strider 1, Breacher 2, Bastion 3), so a host maps
    /// <see cref="KaijuEmerged.SpeciesId"/> straight to a Units.Kaiju species and to a pack role fit. Append only.
    /// </summary>
    public static class EmergenceSpecies
    {
        public const byte Strider = 1, Breacher = 2, Bastion = 3;
        public const int Count = 3;
    }

    /// <summary>One site of the sequence: where on the fault a cluster rises, and when. Fires once per (EventId, SiteIdx), in site order.</summary>
    public struct EmergenceSiteSelected : IEvent
    {
        public uint EventId;
        public int SiteIdx;
        public double X, Z;
        public uint FaultId;
        /// <summary>Vertex of the dissolved fault system the site sits on (inside the ruptured range, never touching a bridge).</summary>
        public int VertexIdx;
        /// <summary>Simulation s from QuakeBegan of the first kaiju of the cluster.</summary>
        public float TimeS;
        public byte KaijuCount;
        /// <summary>Distance from the site to the nearest infrastructure cluster, m (-1 when no target source).</summary>
        public float DistToTargetM;
    }

    /// <summary>One kaiju rises. <see cref="KaijuId"/> is the entity id the host must use when it spawns the body (allocated here, never reused).</summary>
    public struct KaijuEmerged : IEvent
    {
        public uint EventId;
        public int SiteIdx;
        /// <summary>Index inside the site's cluster, 0-based, in emergence order.</summary>
        public byte Ordinal;
        public byte SpeciesId;
        public EntityId KaijuId;
        public double X, Z;
        /// <summary>Simulation s from QuakeBegan.</summary>
        public float TimeS;
        /// <summary>Initial facing in rad (BayTM, atan2(dz, dx)) toward the nearest infrastructure cluster; 0 without targets.</summary>
        public float HeadingRad;
    }

    /// <summary>Published after the last <see cref="KaijuEmerged"/> of the sequence.</summary>
    public struct EmergenceSequenceDone : IEvent
    {
        public uint EventId;
        public int Sites, Kaiju;
        public int SitesRequested;
        public float FirstTimeS, LastTimeS;
    }

    /// <summary>An F-044 infrastructure cluster the pack will roam to (centre in BayTM metres, weight = summed target value).</summary>
    public struct EmergenceTarget
    {
        public double X, Z;
        public float Weight;
        public EmergenceTarget(double x, double z, float weight = 1f) { X = x; Z = z; Weight = weight; }
    }

    public interface IEmergenceTargets
    {
        int Count { get; }
        EmergenceTarget Get(int index);
    }

    /// <summary>The ruptured fault system as plain arrays (vertices in BayTM metres, arc length from vertex 0, bridged segments flagged).</summary>
    public sealed class EmergenceTrace
    {
        public readonly uint FaultId;
        public readonly double[] X, Z, ArcM;
        /// <summary>Bridge[i] is true when segment i (vertex i to i+1) is an unmapped gap. Length VertexCount - 1.</summary>
        public readonly bool[] Bridge;

        public EmergenceTrace(uint faultId, double[] x, double[] z, double[] arcM, bool[] bridge)
        {
            if (x == null || z == null || arcM == null || bridge == null) throw new System.ArgumentNullException();
            if (x.Length < 2 || z.Length != x.Length || arcM.Length != x.Length || bridge.Length != x.Length - 1)
                throw new System.ArgumentException("EmergenceTrace: arrays must have N, N, N, N-1 elements with N >= 2");
            FaultId = faultId; X = x; Z = z; ArcM = arcM; Bridge = bridge;
        }

        public int VertexCount { get { return X.Length; } }
        public bool TouchesBridge(int v) { return (v > 0 && Bridge[v - 1]) || (v < Bridge.Length && Bridge[v]); }
    }

    /// <summary>Resolves the dissolved fault system of a FaultRupture event (the Composition adapter wraps World.Seismic's FaultTraceSet).</summary>
    public interface IEmergenceTraceSource
    {
        bool TryGet(uint faultId, out EmergenceTrace trace);
    }
}
