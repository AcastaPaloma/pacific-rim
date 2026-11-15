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
        int _readsRender, _readsPhysics;

        /// <summary>Origin of the render frame (anchor cluster's origin); NaN until the first tick with a focus.</summary>
        public double RenderOriginX { get; private set; } = double.NaN;
        public double RenderOriginZ { get; private set; } = double.NaN;
        public int AnchorClusterId { get; private set; } = -1;
        /// <summary>Origin changes after the first placement (each is one exact integer shift of all tile roots).</summary>
        public int RebaseCount { get; private set; }
        public int LoadsDispatched { get; private set; }
        public int Failures { get; private set; }
        public int Dropped { get; private set; }
        public double LastBuildMs { get; private set; }
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
                _resident.Remove(k);
                _sched.Released(k);
            }
            ApplyOrigin(foci);
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
                if (!r.Urgent && done > 0 && _opt.ClockMs() - t0 >= _opt.MainThreadSliceMs) break;
                done++;
                var st = _sched.GetState(r.Key);
                if (st != TileState.Requested && st != TileState.Reading && st != TileState.Building) { Dropped++; continue; }
                if (r.Error != null) { Failures++; _sched.Failed(r.Key, r.Error); continue; }
                _sched.Advance(r.Key, TileState.Building);
                try { _builder.Build(r.Key, r.Payload, this); }
                catch (Exception e) { Failures++; _sched.Failed(r.Key, "build: " + e.Message); }
            }
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
