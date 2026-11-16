// Engine-free host of the streaming scheduler (T-109, R-101 sections 1, 5, 6): runs the scheduler's plan, reads tiles on worker threads,
// hands payloads to a main-thread builder inside a time slice, keeps the render origin on the anchor cluster's origin, and tracks
// ring membership. No Unity type; the Unity adapter (Game.World.Streaming.Unity) implements ITileBuilder.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Game.World.Geo;

namespace Game.World.Streaming.Host
{
    /// <summary>Result of reading one residency key off the main thread: engine-free mesh data and its byte cost.</summary>
    public sealed class TilePayload
    {
        public ResidencyKey Key;
        public readonly List<GeoMeshData> Meshes = new List<GeoMeshData>();
        public long GpuBytes, CpuBytes;
    }

    /// <summary>Worker-thread IO and decode. Throws on failure (reported to the scheduler as Failed). The T-108 geo_lod@v1 reader plugs in here.</summary>
    public interface ITileReader { TilePayload Read(ResidencyKey key, CancellationToken ct); }

    public interface IBuildSink
    {
        /// <summary>Main thread only. The key is resident; actual bytes replace the estimate.</summary>
        void Built(in ResidencyKey key, long gpuBytes, long cpuBytes);
        void BuildFailed(in ResidencyKey key, string reason);
    }

    /// <summary>Main-thread side. Build may finish later (collider cooking): it calls the sink when done.</summary>
    public interface ITileBuilder
    {
        void Build(in ResidencyKey key, TilePayload payload, IBuildSink sink);
        void Release(in ResidencyKey key);
        /// <summary>Tile roots move to (tile origin - new origin); new builds use it. Origin is a tile corner, so the shift is exact.</summary>
        void ShiftOrigin(double originX, double originZ);
    }

    /// <summary>
    /// Optional second face of a builder (T-111, R-101 section 6): one physics scene per activity cluster. Physics chunks live in the frame of the
    /// cluster whose body needs them (tile corner minus the cluster origin); render keys keep the single render origin of ITileBuilder.ShiftOrigin.
    /// All calls are main thread. When a builder implements this, ShiftOrigin must leave physics chunk nodes alone.
    /// </summary>
    public interface IClusterBuilder
    {
        /// <summary>The physics key will be built for this cluster (called right before ITileBuilder.Build; -1 = no body claims it).</summary>
        void AssignCluster(in ResidencyKey key, int clusterId);
        /// <summary>The cluster appeared or its origin moved (exact tile corner): its physics nodes move by the exact integer difference.</summary>
        void ShiftClusterOrigin(int clusterId, double originX, double originZ);
        /// <summary>The cluster has no members any more (its scene may be dropped once empty).</summary>
        void RemoveCluster(int clusterId);
    }

    public interface IWorkRunner { void Run(Action work); }

    public sealed class ThreadPoolWorkRunner : IWorkRunner
    {
        public void Run(Action work) { ThreadPool.QueueUserWorkItem(_ => work()); }
    }

    /// <summary>Runs work at once on the caller (tests; deterministic).</summary>
    public sealed class InlineWorkRunner : IWorkRunner { public void Run(Action work) { work(); } }

    public sealed class StreamingHostOptions
    {
        /// <summary>Main-thread build time per Tick; urgent (physics) payloads ignore it.</summary>
        public double MainThreadSliceMs = 2.0;
        public int MaxReads = 4;
        public int MaxPhysicsReads = 4;
        /// <summary>T-124b (feature flag, 0 = off = old behaviour): main-thread build time per Tick for URGENT (physics) payloads. Past it, at least one urgent payload is built and the
        /// rest wait for the next Tick, so a burst of chunks arriving together (a cluster change, a spawn, a fast body) is spread over frames instead of one long frame.</summary>
        public double UrgentSliceMs = 0.0;
        /// <summary>Wall clock in ms (injectable for tests).</summary>
        public Func<double> ClockMs = () => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;
    }

    public sealed class StreamingHostCore : IBuildSink, IDisposable
    {
        struct ReadResult { public ResidencyKey Key; public TilePayload Payload; public string Error; public bool Urgent; }

        readonly IStreamingScheduler _sched;
        readonly ITileReader _reader;
        readonly ITileBuilder _builder;
        readonly IWorkRunner _runner;
        readonly StreamingHostOptions _opt;
        readonly StreamingPlan _plan = new StreamingPlan();
        readonly List<TileRequest> _pending = new List<TileRequest>();
        readonly ConcurrentQueue<ReadResult> _done = new ConcurrentQueue<ReadResult>();
        readonly List<ReadResult> _ready = new List<ReadResult>();
        readonly Dictionary<ResidencyKey, bool> _reading = new Dictionary<ResidencyKey, bool>();   // key -> urgent
        readonly HashSet<ResidencyKey> _resident = new HashSet<ResidencyKey>();
        readonly CancellationTokenSource _cts = new CancellationTokenSource();
        readonly IClusterBuilder _clusterBuilder;
        readonly Dictionary<int, (double x, double z)> _clusterOrigins = new Dictionary<int, (double, double)>();
        readonly HashSet<int> _physicsFoci = new HashSet<int>();
        readonly List<int> _scratchIds = new List<int>();
        readonly List<ResidencyKey> _scratchKeys = new List<ResidencyKey>();
        readonly Dictionary<ResidencyKey, int> _chunkOwner = new Dictionary<ResidencyKey, int>();
        ulong _assignHash;
        int _readsRender, _readsPhysics;

        /// <summary>Origin of the render frame (anchor cluster's origin); NaN until the first tick with a focus.</summary>
        public double RenderOriginX { get; private set; } = double.NaN;
        public double RenderOriginZ { get; private set; } = double.NaN;
        public int AnchorClusterId { get; private set; } = -1;
        /// <summary>Cluster origin shifts delivered to an IClusterBuilder (appearance of a cluster is not counted).</summary>
        public int ClusterShifts { get; private set; }
        /// <summary>Physics chunks built while no body of any cluster claimed them.</summary>
        public int UnclaimedChunks { get; private set; }
        /// <summary>Resident physics chunks moved to another cluster because clusters merged, split or traded bodies.</summary>
        public int ChunkReassignments { get; private set; }
        /// <summary>Origin changes after the first placement (each is one exact integer shift of all tile roots).</summary>
        public int RebaseCount { get; private set; }
        public int LoadsDispatched { get; private set; }
        public int Failures { get; private set; }
        public int Dropped { get; private set; }
        public double LastBuildMs { get; private set; }
        /// <summary>T-124b profile of the main-thread build items (ITileBuilder.Build): the longest single item since the last ResetItemProfile, its key and kind, and the count of items.</summary>
        public double MaxItemMs { get; private set; }
        public string MaxItemKey { get; private set; } = "";
        public double MaxItemMsPhysics { get; private set; }
        public double MaxItemMsRender { get; private set; }
        public long ItemCount { get; private set; }
        /// <summary>Largest number of payloads built in one Pump (burst size).</summary>
        public int MaxItemsPerPump { get; private set; }
        public void ResetItemProfile() { MaxItemMs = 0; MaxItemKey = ""; MaxItemMsPhysics = 0; MaxItemMsRender = 0; ItemCount = 0; MaxItemsPerPump = 0; }
        public StreamingPlan LastPlan => _plan;
        /// <summary>(oldX, oldZ, newX, newZ): fired right after the builder shifted its roots.</summary>
        public event Action<double, double, double, double> OriginShifted;

        public StreamingHostCore(IStreamingScheduler scheduler, ITileReader reader, ITileBuilder builder, IWorkRunner runner = null, StreamingHostOptions options = null)
        {
            _sched = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _builder = builder ?? throw new ArgumentNullException(nameof(builder));
            _runner = runner ?? new ThreadPoolWorkRunner();
            _opt = options ?? new StreamingHostOptions();
            _clusterBuilder = builder as IClusterBuilder;
        }

        /// <summary>Cluster of a body in the last plan (-1 when unknown).</summary>
        public int ClusterOf(int focusId)
        {
            for (int i = 0; i < _plan.Assignments.Count; i++) if (_plan.Assignments[i].FocusId == focusId) return _plan.Assignments[i].ClusterId;
            return -1;
        }

        public IReadOnlyCollection<ResidencyKey> Resident => _resident;
        public bool IsResident(in ResidencyKey key) => _resident.Contains(key);
        public int ResidentCount(ResidencyKind kind, int level = -1)
        {
            int n = 0;
            foreach (var k in _resident) if (k.Kind == kind && (level < 0 || k.Tile.Level == level)) n++;
            return n;
        }
        public int InFlight => _reading.Count + _pending.Count + _ready.Count;
        public bool IsBodyHeld(int focusId) => !_sched.PhysicsRingResident(focusId);

        public void Tick(double dt, ReadOnlySpan<FocusSample> foci)
        {
            _sched.Tick(dt, foci, _plan);
            for (int i = 0; i < _plan.Evict.Count; i++)
            {
                var k = _plan.Evict[i];
                _builder.Release(k);
                _chunkOwner.Remove(k);
                _resident.Remove(k);
                _sched.Released(k);
            }
            ApplyOrigin(foci);
            _physicsFoci.Clear();
            for (int i = 0; i < foci.Length; i++) if (foci[i].Kind == FocusKind.Body && foci[i].NeedsPhysics) _physicsFoci.Add(foci[i].Id);
            ApplyClusters();
            ReassignChunks();
            RemoveEndedClusters();
            for (int i = 0; i < _plan.Load.Count; i++) _pending.Add(_plan.Load[i]);
            Dispatch();
            Pump();
        }

        void ApplyOrigin(ReadOnlySpan<FocusSample> foci)
        {
            if (_plan.Clusters.Count == 0) return;
            int anchorFocus = int.MaxValue;
            for (int i = 0; i < foci.Length; i++) if (foci[i].Kind == FocusKind.Camera && foci[i].Id < anchorFocus) anchorFocus = foci[i].Id;
            if (anchorFocus == int.MaxValue) for (int i = 0; i < foci.Length; i++) if (foci[i].Id < anchorFocus) anchorFocus = foci[i].Id;
            int cid = -1;
            for (int i = 0; i < _plan.Assignments.Count; i++) if (_plan.Assignments[i].FocusId == anchorFocus) { cid = _plan.Assignments[i].ClusterId; break; }
            if (cid < 0) return;
            for (int i = 0; i < _plan.Clusters.Count; i++)
            {
                var c = _plan.Clusters[i];
                if (c.ClusterId != cid) continue;
                AnchorClusterId = cid;
                if (c.OriginX == RenderOriginX && c.OriginZ == RenderOriginZ) return;
                if (c.OriginX % GeoTileId.BaseSizeM != 0 || c.OriginZ % GeoTileId.BaseSizeM != 0) throw new InvalidOperationException("cluster origin is not a tile corner");
                double ox = RenderOriginX, oz = RenderOriginZ;
                bool first = double.IsNaN(ox);
                _builder.ShiftOrigin(c.OriginX, c.OriginZ);
                RenderOriginX = c.OriginX; RenderOriginZ = c.OriginZ;
                if (!first) { RebaseCount++; OriginShifted?.Invoke(ox, oz, c.OriginX, c.OriginZ); }
                return;
            }
        }

        void ApplyClusters()
        {
            if (_clusterBuilder == null) return;
            for (int i = 0; i < _plan.Clusters.Count; i++)
            {
                var c = _plan.Clusters[i];
                if (_clusterOrigins.TryGetValue(c.ClusterId, out var o) && o.x == c.OriginX && o.z == c.OriginZ) continue;
                if (c.OriginX % GeoTileId.BaseSizeM != 0 || c.OriginZ % GeoTileId.BaseSizeM != 0) throw new InvalidOperationException("cluster origin is not a tile corner");
                bool known = _clusterOrigins.ContainsKey(c.ClusterId);
                _clusterBuilder.ShiftClusterOrigin(c.ClusterId, c.OriginX, c.OriginZ);
                _clusterOrigins[c.ClusterId] = (c.OriginX, c.OriginZ);
                if (known) ClusterShifts++;
            }
        }

        // after ReassignChunks: a cluster that merged into another hands its chunks over before its scene goes
        void RemoveEndedClusters()
        {
            if (_clusterBuilder == null) return;
            if (_clusterOrigins.Count > _plan.Clusters.Count)
            {
                _scratchIds.Clear();
                foreach (var id in _clusterOrigins.Keys)
                {
                    bool live = false;
                    for (int i = 0; i < _plan.Clusters.Count && !live; i++) live = _plan.Clusters[i].ClusterId == id;
                    if (!live) _scratchIds.Add(id);
                }
                for (int i = 0; i < _scratchIds.Count; i++) { _clusterBuilder.RemoveCluster(_scratchIds[i]); _clusterOrigins.Remove(_scratchIds[i]); }
            }
        }

        void ReassignChunks()
        {
            if (_clusterBuilder == null) return;
            ulong h = 1469598103934665603UL;
            for (int i = 0; i < _plan.Assignments.Count; i++)
            {
                var a = _plan.Assignments[i];
                h = (h ^ (ulong)(uint)a.FocusId) * 1099511628211UL;
                h = (h ^ (ulong)(uint)a.ClusterId) * 1099511628211UL;
            }
            if (h == _assignHash) return;
            _assignHash = h;
            _scratchKeys.Clear();
            foreach (var kv in _chunkOwner) _scratchKeys.Add(kv.Key);
            for (int i = 0; i < _scratchKeys.Count; i++)
            {
                var k = _scratchKeys[i];
                if (!_resident.Contains(k)) continue;
                int o = OwnerCluster(k);
                if (o < 0 || o == _chunkOwner[k]) continue;
                _chunkOwner[k] = o; ChunkReassignments++;
                _clusterBuilder.AssignCluster(k, o);
            }
        }

        /// <summary>Cluster of the body (with physics) nearest to the chunk; ties go to the lower id. -1 when no body is known.</summary>
        int OwnerCluster(in ResidencyKey key)
        {
            key.Bounds(out double x0, out double z0, out double x1, out double z1);
            int best = -1; double bd = double.MaxValue;
            for (int pass = 0; pass < 2 && best < 0; pass++)
                for (int i = 0; i < _plan.Assignments.Count; i++)
                {
                    var a = _plan.Assignments[i];
                    if (pass == 0 && !_physicsFoci.Contains(a.FocusId)) continue;
                    double ox = 0, oz = 0; bool f = false;
                    for (int k = 0; k < _plan.Clusters.Count; k++) if (_plan.Clusters[k].ClusterId == a.ClusterId) { ox = _plan.Clusters[k].OriginX; oz = _plan.Clusters[k].OriginZ; f = true; break; }
                    if (!f) continue;
                    double x = ox + a.LocalX, z = oz + a.LocalZ;
                    double dx = Math.Max(0, Math.Max(x0 - x, x - x1)), dz = Math.Max(0, Math.Max(z0 - z, z - z1));
                    double d = dx * dx + dz * dz;
                    if (d < bd || (d == bd && a.ClusterId < best)) { bd = d; best = a.ClusterId; }
                }
            return best;
        }

        void Dispatch()
        {
            if (_pending.Count == 0) return;
            _pending.Sort((a, b) => a.Urgent != b.Urgent ? (a.Urgent ? -1 : 1) : b.Priority.CompareTo(a.Priority));
            int w = 0;
            for (int i = 0; i < _pending.Count; i++)
            {
                var r = _pending[i];
                bool phys = r.Key.Kind == ResidencyKind.Physics;
                if ((phys ? _readsPhysics >= _opt.MaxPhysicsReads : _readsRender >= _opt.MaxReads)) { _pending[w++] = r; continue; }
                if (_sched.GetState(r.Key) != TileState.Requested) { Dropped++; continue; }
                if (phys) _readsPhysics++; else _readsRender++;
                _reading[r.Key] = r.Urgent;
                LoadsDispatched++;
                _sched.Advance(r.Key, TileState.Reading);
                var key = r.Key; bool urgent = r.Urgent; var ct = _cts.Token;
                _runner.Run(() =>
                {
                    var res = new ReadResult { Key = key, Urgent = urgent };
                    try { res.Payload = _reader.Read(key, ct); if (res.Payload == null) res.Error = "reader returned null"; }
                    catch (Exception e) { res.Error = e.GetType().Name + ": " + e.Message; }
                    _done.Enqueue(res);
                });
            }
            _pending.RemoveRange(w, _pending.Count - w);
        }

        /// <summary>Takes finished reads and builds them on the calling (main) thread inside the slice.</summary>
        public void Pump()
        {
            while (_done.TryDequeue(out var r))
            {
                _reading.Remove(r.Key);
                if (r.Key.Kind == ResidencyKind.Physics) _readsPhysics--; else _readsRender--;
                _ready.Add(r);
            }
            if (_ready.Count == 0) return;
            _ready.Sort((a, b) => a.Urgent != b.Urgent ? (a.Urgent ? -1 : 1) : a.Key.CompareTo(b.Key));
            double t0 = _opt.ClockMs();
            int done = 0;
            for (int i = 0; i < _ready.Count; i++)
            {
                var r = _ready[i];
                if (done > 0)
                {
                    double slice = r.Urgent ? _opt.UrgentSliceMs : _opt.MainThreadSliceMs;
                    if ((!r.Urgent || slice > 0.0) && _opt.ClockMs() - t0 >= slice) break;
                }
                done++;
                var st = _sched.GetState(r.Key);
                if (st != TileState.Requested && st != TileState.Reading && st != TileState.Building) { Dropped++; continue; }
                if (r.Error != null) { Failures++; _sched.Failed(r.Key, r.Error); continue; }
                _sched.Advance(r.Key, TileState.Building);
                if (_clusterBuilder != null && r.Key.Kind == ResidencyKind.Physics)
                {
                    int owner = OwnerCluster(r.Key);
                    if (owner < 0) UnclaimedChunks++;
                    _clusterBuilder.AssignCluster(r.Key, owner);
                    _chunkOwner[r.Key] = owner;
                }
                double ti = _opt.ClockMs();
                try { _builder.Build(r.Key, r.Payload, this); }
                catch (Exception e) { Failures++; _sched.Failed(r.Key, "build: " + e.Message); }
                double di = _opt.ClockMs() - ti; ItemCount++;
                if (di > MaxItemMs) { MaxItemMs = di; MaxItemKey = r.Key.ToString(); }
                if (r.Key.Kind == ResidencyKind.Physics) { if (di > MaxItemMsPhysics) MaxItemMsPhysics = di; } else if (di > MaxItemMsRender) MaxItemMsRender = di;
            }
            if (done > MaxItemsPerPump) MaxItemsPerPump = done;
            _ready.RemoveRange(0, done);
            LastBuildMs = _opt.ClockMs() - t0;
        }

        public void Built(in ResidencyKey key, long gpuBytes, long cpuBytes)
        {
            var st = _sched.GetState(key);
            if (st == TileState.Absent || st == TileState.Evicting) { _builder.Release(key); return; }
            _resident.Add(key);
            _sched.Completed(key, gpuBytes, cpuBytes);
        }

        public void BuildFailed(in ResidencyKey key, string reason) { Failures++; _sched.Failed(key, reason); }

        public void Dispose() { _cts.Cancel(); _cts.Dispose(); }
    }
}
