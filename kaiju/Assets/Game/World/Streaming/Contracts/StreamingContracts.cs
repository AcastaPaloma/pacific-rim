// World.Streaming contracts (layer L2, engine-free): the focus set, residency keys and events, budgets, rings, the streaming plan and
// the activity-cluster origin assignment (T-107, research/R-101.md section 8.1, Track 2 of plans/M2.md). No IO, no Unity types.
// All events are unmanaged and carry IDs only. Distances in metres, positions in float64 BayTM@v1 (x east, y NAVD88 up, z north).
using System;
using System.Collections.Generic;
using Game.Core;
using Game.World.Geo;

namespace Game.World.Streaming
{
    public static class StreamingContractVersions
    {
        /// <summary>Shapes of FocusSample, ResidencyKey, TileResident, TileEvicted, StreamingPlan, ClusterAssignment.</summary>
        public const int Api = 1;
        /// <summary>Numbers of StreamingConfig defaults (R-101 O1..O12). Change of a default bumps this.</summary>
        public const int DefaultsRevision = 1;
    }

    /// <summary>Feature flags of World.Streaming (Assets/Game/Config/FeatureFlags/Streaming.json, default off).</summary>
    public static class StreamingFlags
    {
        /// <summary>Install the streaming scheduler. Off: M1 GeoTileSetBuilder behaviour is unchanged.</summary>
        public const string Enabled = "world.streaming";
    }

    public enum FocusKind : byte
    {
        /// <summary>A viewpoint (local or remote client). Drives the render rings.</summary>
        Camera = 0,
        /// <summary>A simulated body (mech, kaiju, debris). Drives the physics ring (and the render rings when NeedsRender).</summary>
        Body = 1,
    }

    /// <summary>One streaming focus, sampled once per tick. Double world position (BayTM, NAVD88 y).</summary>
    public struct FocusSample
    {
        /// <summary>Stable per focus (cluster and ring state are keyed by it).</summary>
        public int Id;
        public FocusKind Kind;
        public double X, Y, Z;
        public float Vx, Vy, Vz;
        /// <summary>Body reach beyond its centre, m (informational; the physics ring already includes the 100 m reach margin, R-101 section 5).</summary>
        public float ReachM;
        /// <summary>Cameras: vertical FOV, output height and integer upscale k (tau = 1/k output px). Zero fields fall back to the config defaults.</summary>
        public float FovDeg;
        public int OutputHeightPx;
        public int IntegerScale;
        /// <summary>Active haze profile visual range V; the render ring is clamp(V, 15 km, 60 km). 0 = config default.</summary>
        public float VisualRangeM;
        /// <summary>Body: request physics chunks (false for debris and bodies parked kinematic; they keep their cluster).</summary>
        public bool NeedsPhysics;
        /// <summary>Body: also drive the render pyramid (the player's mech when no separate camera exists). Cameras always do; AI bodies far from every camera do not (T-107 decision D2).</summary>
        public bool NeedsRender;
    }

    /// <summary>Filled once per tick by the host: every camera, mech and kaiju (plus remote proxies as cameras with their own V).</summary>
    public interface IStreamingFocus
    {
        /// <summary>Writes up to buffer.Length samples and returns how many it wrote.</summary>
        int Fill(Span<FocusSample> buffer);
    }

    public enum ResidencyKind : byte
    {
        /// <summary>A whole tile at its LOD level (render geometry).</summary>
        Render = 0,
        /// <summary>One 256 m chunk of an L0 tile: colliders and per-building objects (the physics ring).</summary>
        Physics = 1,
    }

    /// <summary>Lifecycle of one residency key. Absent -> Requested -> Reading -> Building -> Resident -> Evicting -> Absent.</summary>
    public enum TileState : byte { Absent = 0, Requested = 1, Reading = 2, Building = 3, Resident = 4, Evicting = 5 }

    /// <summary>What is resident: a tile (Render) or a chunk of an L0 tile (Physics). A value type usable as a dictionary key.</summary>
    public readonly struct ResidencyKey : IEquatable<ResidencyKey>, IComparable<ResidencyKey>
    {
        /// <summary>Chunk value of a Render key.</summary>
        public const ushort WholeTile = 0xFFFF;
        /// <summary>Chunk edge, m (two chunks fit the 512 m physics ring radius).</summary>
        public const double ChunkSizeM = 256.0;
        /// <summary>Chunks per L0 tile edge (2048 / 256).</summary>
        public const int ChunksPerEdge = 8;

        public readonly GeoTileId Tile;
        public readonly ResidencyKind Kind;
        /// <summary>cx + cz * 8 (cx east, cz north) for Physics; WholeTile for Render.</summary>
        public readonly ushort Chunk;

        public ResidencyKey(GeoTileId tile, ResidencyKind kind, ushort chunk) { Tile = tile; Kind = kind; Chunk = chunk; }
        public static ResidencyKey Render(GeoTileId tile) => new ResidencyKey(tile, ResidencyKind.Render, WholeTile);
        public static ResidencyKey PhysicsChunk(GeoTileId l0Tile, int cx, int cz)
        {
            if (l0Tile.Level != 0) throw new ArgumentException("physics chunks belong to L0 tiles", nameof(l0Tile));
            if (cx < 0 || cx >= ChunksPerEdge || cz < 0 || cz >= ChunksPerEdge) throw new ArgumentOutOfRangeException(nameof(cx));
            return new ResidencyKey(l0Tile, ResidencyKind.Physics, (ushort)(cx + cz * ChunksPerEdge));
        }
        public int ChunkX => Chunk == WholeTile ? 0 : Chunk % ChunksPerEdge;
        public int ChunkZ => Chunk == WholeTile ? 0 : Chunk / ChunksPerEdge;

        /// <summary>Axis-aligned footprint in BayTM metres.</summary>
        public void Bounds(out double x0, out double z0, out double x1, out double z1)
        {
            if (Kind == ResidencyKind.Render) { x0 = Tile.OriginX; z0 = Tile.OriginZ; x1 = x0 + Tile.SizeM; z1 = z0 + Tile.SizeM; return; }
            x0 = Tile.OriginX + ChunkX * ChunkSizeM; z0 = Tile.OriginZ + ChunkZ * ChunkSizeM; x1 = x0 + ChunkSizeM; z1 = z0 + ChunkSizeM;
        }

        /// <summary>Total order used for every deterministic tie break: kind, level, i, j, chunk.</summary>
        public int CompareTo(ResidencyKey o)
        {
            int c = Kind.CompareTo(o.Kind); if (c != 0) return c;
            c = Tile.Level.CompareTo(o.Tile.Level); if (c != 0) return c;
            c = Tile.I.CompareTo(o.Tile.I); if (c != 0) return c;
            c = Tile.J.CompareTo(o.Tile.J); if (c != 0) return c;
            return Chunk.CompareTo(o.Chunk);
        }
        public bool Equals(ResidencyKey o) => Kind == o.Kind && Chunk == o.Chunk && Tile.Equals(o.Tile);
        public override bool Equals(object obj) => obj is ResidencyKey o && Equals(o);
        public override int GetHashCode() => (Tile.GetHashCode() * 31 + (int)Kind) * 31 + Chunk;
        public override string ToString() => Kind == ResidencyKind.Render ? Tile.ToString() : Tile + "#c" + Chunk;
    }

    /// <summary>A key became resident (reported by the host through IStreamingScheduler.Completed). IDs only.</summary>
    public struct TileResident : IEvent
    {
        public GeoTileId Tile;
        public ResidencyKind Kind;
        /// <summary>ResidencyKey.WholeTile for Render.</summary>
        public ushort Chunk;
        /// <summary>1 on the first residency of the key, +1 on each reload.</summary>
        public int Generation;
    }

    /// <summary>The scheduler decided to evict a key. The host flushes the tile's destruction journal (F-043) and releases it, then calls Released.</summary>
    public struct TileEvicted : IEvent
    {
        public GeoTileId Tile;
        public ResidencyKind Kind;
        public ushort Chunk;
        public int Generation;
    }

    /// <summary>One load request in a StreamingPlan, highest priority first.</summary>
    public struct TileRequest
    {
        public ResidencyKey Key;
        public double Priority;
        /// <summary>Estimated bytes reserved against the budget.</summary>
        public long EstGpuBytes, EstCpuBytes;
        /// <summary>True: allowed to preempt the main-thread slice (physics ring, R-101 rule 3).</summary>
        public bool Urgent;
    }

    /// <summary>All limits are enforced by the scheduler and never exceeded (estimates are reserved at request time).</summary>
    public struct StreamingBudget
    {
        public long GpuBytes, CpuBytes;
        /// <summary>Concurrent Render requests (Requested + Reading + Building).</summary>
        public int MaxInFlightReads;
        /// <summary>Handed to the Unity loader; the scheduler itself does no main-thread work.</summary>
        public float MainThreadSliceMs;
        /// <summary>Resident + in-flight physics chunks over all bodies.</summary>
        public int MaxPhysicsChunks;

        public static StreamingBudget Default => new StreamingBudget
        {
            GpuBytes = 1500L * 1024 * 1024, CpuBytes = 350L * 1024 * 1024, MaxInFlightReads = 4, MainThreadSliceMs = 2.0f, MaxPhysicsChunks = 600,
        };
    }

    /// <summary>One residency ring. Render rings are per LOD level; the physics ring is chunk level.</summary>
    public struct RingSpec
    {
        public int Level;
        /// <summary>Outer limit: a tile of this level is wanted only while its (prefetch-adjusted) distance is within it.</summary>
        public float RadiusM;
        /// <summary>Retention beyond the radius: a resident key is released only past RadiusM * (1 + HysteresisFrac).</summary>
        public float HysteresisFrac;
        public bool Physics;
    }

    /// <summary>The scheduler's output for one Tick. Lists are reused between ticks (no allocation after warm-up).</summary>
    public sealed class StreamingPlan
    {
        public readonly List<TileRequest> Load = new List<TileRequest>();
        public readonly List<ResidencyKey> Evict = new List<ResidencyKey>();
        public readonly List<ClusterAssignment> Assignments = new List<ClusterAssignment>();
        public readonly List<ClusterInfo> Clusters = new List<ClusterInfo>();
        public void Clear() { Load.Clear(); Evict.Clear(); Assignments.Clear(); Clusters.Clear(); }
    }

    /// <summary>Per-tile cost and LOD error from the manifest (geo_lod@v1 stats). The scheduler never reads files.</summary>
    public interface ITileCostModel
    {
        /// <summary>False for tiles outside the baked coverage (never requested).</summary>
        bool Exists(GeoTileId tile);
        /// <summary>RMS geometric error of a level against L0, metres (level 0 = 0). Drives the selection.</summary>
        double ErrRmsM(int level);
        void Estimate(in ResidencyKey key, out long gpuBytes, out long cpuBytes);
    }

    public interface IStreamingScheduler
    {
        /// <summary>Deterministic: same foci and same Completed/Failed/Released calls give the same plan.</summary>
        void Tick(double dt, ReadOnlySpan<FocusSample> foci, StreamingPlan plan);
        TileState GetState(in ResidencyKey key);
        /// <summary>The host reports the key built; fires TileResident.</summary>
        void Completed(in ResidencyKey key, long gpuBytes, long cpuBytes);
        /// <summary>Optional progress report (Reading, Building) for diagnostics.</summary>
        void Advance(in ResidencyKey key, TileState state);
        /// <summary>The host released an evicted key; its bytes leave the budget.</summary>
        void Released(in ResidencyKey key);
        /// <summary>Retry with exponential backoff, parked after StreamingConfig.MaxFailures.</summary>
        void Failed(in ResidencyKey key, string reason);
        /// <summary>True when every physics chunk of the body's ring is Resident (else the host holds the body kinematic).</summary>
        bool PhysicsRingResident(int focusId);
    }

    // ---- activity-cluster origins (R-101 section 6) ----

    /// <summary>Where one focus sits in its cluster (M-015@v2 (a): every |Local*| must stay at or below 4096 m).</summary>
    public struct ClusterAssignment
    {
        public int FocusId;
        public int ClusterId;
        /// <summary>BayTM position minus the cluster origin, float32 (origin y = 0).</summary>
        public float LocalX, LocalY, LocalZ;
    }

    public struct ClusterInfo
    {
        public int ClusterId;
        /// <summary>Tile corner (multiple of 2048 m) the cluster's local frame is centred on.</summary>
        public double OriginX, OriginZ;
        public int FocusCount;
        /// <summary>Chebyshev extent of the members' AABB, at most StreamingConfig.ClusterExtentCapM (5120 m).</summary>
        public double ExtentM;
        /// <summary>True when the origin moved this tick (the host shifts the cluster root by exact integers).</summary>
        public bool Rebased;
        public double PrevOriginX, PrevOriginZ;
        /// <summary>A member beyond the soft rebase distance (3072 m) that was not rebased because the step was not quiet.</summary>
        public bool RebaseDue;
    }

    /// <summary>A cluster's origin moved (existing floating origin, F-042). IDs and numbers only.</summary>
    public struct ClusterOriginChanged : IEvent
    {
        public int ClusterId;
        public double OldX, OldZ, NewX, NewZ;
    }
}
