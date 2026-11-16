// Defaults of the streaming scheduler (research/R-101.md O1..O12). Every number is a field; none is read from a file here.
// M-102 results may only lower TauFactor, never raise it (the setter clamps).
using System;
using Game.World.Geo;

namespace Game.World.Streaming
{
    public sealed class StreamingConfig
    {
        /// <summary>Coarsest level of the render pyramid (geo_lod@v1: L0 native, L1..L4).</summary>
        public int MaxLevel = 4;
        /// <summary>Used when a focus leaves FovDeg / OutputHeightPx / IntegerScale at 0 (R-003: 1080p, 50 degrees, k = 4).</summary>
        public double DefaultFovDeg = 50.0, DefaultOutputHeightPx = 1080.0;
        public int DefaultIntegerScale = 4;
        double _tauFactor = 1.0;
        /// <summary>Multiplier on tau = 1/k output px. Clamped to (0, 1]: a result may force a lower tau, never a higher one (O3).</summary>
        public double TauFactor { get => _tauFactor; set => _tauFactor = Math.Min(1.0, Math.Max(1e-6, value)); }

        /// <summary>Render ring of the top level = clamp(V, Min, Max) (O2).</summary>
        public double DefaultVisualRangeM = 30000.0, MinRenderRingM = 15000.0, MaxRenderRingM = 60000.0;
        /// <summary>Prefetch lead: the selection sees a tile at (distance - speed * lead) (R-101 rule 4).</summary>
        public double PrefetchLeadS = 4.0;
        /// <summary>A tile refines at distance d_n x SwitchUpFactor and coarsens again beyond d_n x SwitchDownFactor (rule 6, section 2).</summary>
        public double SwitchUpFactor = 1.0, SwitchDownFactor = 1.15;
        /// <summary>Default RingSpec.HysteresisFrac of render rings (unload at radius x 1.25).</summary>
        public double RenderHysteresisFrac = 0.25;
        /// <summary>Render ring per level (index = level). Radius of a level below the top is raised to its next switch distance x SwitchDownFactor so the ring never leaves a hole.</summary>
        public RingSpec[] RenderRings;
        /// <summary>Physics ring: radius 512 m (2 chunks of margin over the 60 m/s cap), evict at 768 m (R-101 section 5, O1).</summary>
        public double PhysicsRadiusM = 512.0, PhysicsHysteresisFrac = 0.5;
        public double PhysicsPriorityBoost = 1000.0;
        /// <summary>T-124b (feature flag, 0 = off = old behaviour): physics PREFETCH radius, larger than PhysicsRadiusM. Chunks within it are requested (and retained
        /// out to max(prefetch, evict radius)); a body is held (PhysicsRingResident) only for chunks inside the unchanged hold ring PhysicsRadiusM, so chunks that stream
        /// in at the edge of the prefetch ring are resident before they enter the hold ring. Values below PhysicsRadiusM are ignored.</summary>
        public double PhysicsPrefetchRadiusM = 0.0;
        /// <summary>Effective request radius of the physics ring.</summary>
        public double PhysicsRequestRadiusM => Math.Max(PhysicsRadiusM, PhysicsPrefetchRadiusM);
        /// <summary>Effective retention (evict) radius of a loaded physics chunk.</summary>
        public double PhysicsRetainRadiusM => Math.Max(PhysicsRadiusM * (1.0 + PhysicsHysteresisFrac), PhysicsPrefetchRadiusM);
        /// <summary>T-124d (feature flag, 0 = off = recompute the wanted set every tick, as before): skip the wanted-set recompute (quadtree walks, physics rings, fallbacks) on a tick
        /// when residency is unchanged and no focus moved more than this many metres (position plus PrefetchLeadS x velocity change) since the last recompute; priorities of the
        /// skipped ticks are the cached ones. Bounds the plan staleness to ReplanMoveM metres and ReplanMaxIntervalS seconds.</summary>
        public double ReplanMoveM = 0.0, ReplanMaxIntervalS = 0.25;
        public StreamingBudget Budget = StreamingBudget.Default;
        /// <summary>When over budget evict until under this fraction (rule 5).</summary>
        public double EvictTargetFrac = 0.9;
        /// <summary>Failed(): backoff base * 2^(n-1) seconds up to the max; parked after MaxFailures.</summary>
        public int MaxFailures = 5;
        public double BackoffBaseS = 0.5, BackoffMaxS = 30.0;
        /// <summary>Activity clusters (O6): extent cap, link distance (Chebyshev), soft and forced rebase distance, origin grid (tile corner).</summary>
        public double ClusterExtentCapM = 5120.0, ClusterLinkM = 3072.0, RebaseM = 3072.0, ForceRebaseM = 3840.0, OriginGridM = GeoTileId.BaseSizeM;
        /// <summary>M-015@v2 (a): the bound every cluster-local coordinate must stay within.</summary>
        public double LocalBoundM = 4096.0;

        public StreamingConfig()
        {
            RenderRings = new RingSpec[5];
            double[] r = { 1536.0, 3072.0, 6144.0, 12288.0, 30000.0 };
            for (int i = 0; i < 5; i++) RenderRings[i] = new RingSpec { Level = i, RadiusM = (float)r[i], HysteresisFrac = (float)RenderHysteresisFrac, Physics = false };
        }

        public RingSpec PhysicsRing => new RingSpec { Level = 0, RadiusM = (float)PhysicsRadiusM, HysteresisFrac = (float)PhysicsHysteresisFrac, Physics = true };
    }

    /// <summary>Default cost model: the R-101 section 1 table (GPU per urban tile by level) and the error ladder 0.1 x 2^n m. Replaced by geo_lod@v1 stats.</summary>
    public sealed class DefaultTileCostModel : ITileCostModel
    {
        static readonly long[] GpuMb = { 32, 20, 15, 10, 6 };
        readonly int _maxI, _maxJ;
        /// <param name="coverageL0Tiles">Number of L0 tiles per axis that exist (0 = unbounded); coarser levels cover the same area.</param>
        public DefaultTileCostModel(int coverageL0Tiles = 0) { _maxI = _maxJ = coverageL0Tiles; }
        public bool Exists(GeoTileId tile)
        {
            if (_maxI <= 0) return true;
            long s = 1L << tile.Level;
            return tile.I * s < _maxI && tile.J * s < _maxJ;
        }
        public double ErrRmsM(int level) => level <= 0 ? 0.0 : 0.1 * Math.Pow(2.0, level);
        public void Estimate(in ResidencyKey key, out long gpuBytes, out long cpuBytes)
        {
            if (key.Kind == ResidencyKind.Physics) { gpuBytes = 0; cpuBytes = 140L * 1024; return; }   // 8.4 MB height array / 64 chunks
            int l = Math.Min(key.Tile.Level, GpuMb.Length - 1);
            gpuBytes = GpuMb[l] * 1024 * 1024;
            cpuBytes = key.Tile.Level == 0 ? 400L * 1024 : 64L * 1024;   // building ID table (0.3 MB) + bookkeeping
        }
    }
}
