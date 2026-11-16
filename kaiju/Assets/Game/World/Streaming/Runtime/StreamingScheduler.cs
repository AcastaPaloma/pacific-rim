// Deterministic streaming scheduler (T-107, R-101 section 8.1 rules 1 to 8). Engine-free, no IO, no clock: time only advances by Tick(dt).
// Per tick: (1) assign activity-cluster origins, (2) walk the LOD quadtree per focus (screen-space error in output pixels against
// tau = 1/k, hysteresis 1.0 up / 1.15 down, prefetch lead), add the physics chunks of every body (512 m, evict at 768 m), (3) evict what is
// outside retention, (4) enforce the budget (evict to 90 %, never request what does not fit), (5) emit the load list by priority.
// The host (Unity side, T-109) executes plan.Load / plan.Evict and reports back through Completed / Advance / Released / Failed.
using System;
using System.Collections.Generic;
using Game.Core;
using Game.World.Geo;

namespace Game.World.Streaming
{
    public sealed class StreamingScheduler : IStreamingScheduler
    {
        sealed class Entry
        {
            public ResidencyKey Key;
            public TileState State;
            public long Gpu, Cpu;
            public long LastNeededTick;
            public double Priority;
        }

        struct Want { public double Priority; public bool Fallback; public bool Leaf; }

        sealed class Fail { public int Count; public double RetryAtS; public bool Parked; }

        readonly StreamingConfig _cfg;
        readonly ITileCostModel _cost;
        readonly IEventBus _bus;
        readonly ClusterAssigner _clusters;
        readonly Dictionary<ResidencyKey, Entry> _entries = new Dictionary<ResidencyKey, Entry>();
        readonly Dictionary<ResidencyKey, int> _generation = new Dictionary<ResidencyKey, int>();
        readonly Dictionary<ResidencyKey, Fail> _fail = new Dictionary<ResidencyKey, Fail>();
        readonly Dictionary<ResidencyKey, Want> _want = new Dictionary<ResidencyKey, Want>();
        HashSet<ResidencyKey> _refined = new HashSet<ResidencyKey>(), _refinedNext = new HashSet<ResidencyKey>();
        readonly Dictionary<int, List<ResidencyKey>> _ring = new Dictionary<int, List<ResidencyKey>>();
        readonly List<Entry> _scratch = new List<Entry>();
        readonly List<TileRequest> _cand = new List<TileRequest>();
        double _time;
        long _tick;
        long _gpuUsed, _cpuUsed;
        int _physicsCount;

        public StreamingScheduler(StreamingConfig cfg, ITileCostModel cost, IEventBus bus = null)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            _cost = cost ?? throw new ArgumentNullException(nameof(cost));
            _bus = bus;
            _clusters = new ClusterAssigner(cfg);
        }

        /// <summary>False defers soft cluster rebases (set during a loud combat frame); forced rebases still happen.</summary>
        public bool QuietStep = true;
        public long Ticks => _tick;
        public long GpuBytesInUse => _gpuUsed;
        public long CpuBytesInUse => _cpuUsed;
        public int ResidentCount { get { int c = 0; foreach (var e in _entries.Values) if (e.State == TileState.Resident) c++; return c; } }
        public int PhysicsChunkCount => _physicsCount;
        /// <summary>Ticks in which a wanted key could not be requested because it did not fit the budget.</summary>
        public int BudgetBlockedTicks { get; private set; }
        public bool IsParked(in ResidencyKey key) => _fail.TryGetValue(key, out var f) && f.Parked;

        /// <summary>Distance (after the prefetch lead is taken off) within which a key stays resident for a focus; diagnostic and tests.</summary>
        public double RetentionRadiusM(in ResidencyKey key, in FocusSample f)
        {
            if (key.Kind == ResidencyKind.Physics) return _cfg.PhysicsRetainRadiusM;
            return RingCap(key.Tile.Level, f, out _, out _) * (1.0 + _cfg.RenderHysteresisFrac);
        }

        public TileState GetState(in ResidencyKey key) => _entries.TryGetValue(key, out var e) ? e.State : TileState.Absent;

        public bool PhysicsRingResident(int focusId)
        {
            if (!_ring.TryGetValue(focusId, out var list)) return true;
            for (int i = 0; i < list.Count; i++)
                if (!_entries.TryGetValue(list[i], out var e) || e.State != TileState.Resident) return false;
            return true;
        }

        // ---------------- host callbacks ----------------

        public void Advance(in ResidencyKey key, TileState state)
        {
            if (_entries.TryGetValue(key, out var e) && (state == TileState.Reading || state == TileState.Building) && e.State < state && e.State >= TileState.Requested)
                e.State = state;
        }

        public void Completed(in ResidencyKey key, long gpuBytes, long cpuBytes)
        {
            if (!_entries.TryGetValue(key, out var e) || e.State == TileState.Resident || e.State == TileState.Evicting) return;
            _gpuUsed += gpuBytes - e.Gpu; _cpuUsed += cpuBytes - e.Cpu;
            _wantDirty = true;
            e.Gpu = gpuBytes; e.Cpu = cpuBytes; e.State = TileState.Resident;
            e.LastNeededTick = _tick;
            _fail.Remove(key);
            _generation.TryGetValue(key, out int g); g++; _generation[key] = g;
            _bus?.Publish(new TileResident { Tile = key.Tile, Kind = key.Kind, Chunk = key.Chunk, Generation = g });
        }

        public void Released(in ResidencyKey key)
        {
            if (!_entries.TryGetValue(key, out var e) || e.State != TileState.Evicting) return;
            _wantDirty = true;
            Remove(e);
        }

        public void Failed(in ResidencyKey key, string reason)
        {
            if (!_entries.TryGetValue(key, out var e) || e.State == TileState.Resident || e.State == TileState.Evicting) return;
            _wantDirty = true;
            Remove(e);
            if (!_fail.TryGetValue(key, out var f)) { f = new Fail(); _fail[key] = f; }
            f.Count++;
            if (f.Count >= _cfg.MaxFailures) f.Parked = true;
            else f.RetryAtS = _time + Math.Min(_cfg.BackoffMaxS, _cfg.BackoffBaseS * Math.Pow(2.0, f.Count - 1));
        }

        void Remove(Entry e)
        {
            _entries.Remove(e.Key);
            _gpuUsed -= e.Gpu; _cpuUsed -= e.Cpu;
            if (e.Key.Kind == ResidencyKind.Physics) _physicsCount--;
        }

        // ---------------- tick ----------------

        public void Tick(double dt, ReadOnlySpan<FocusSample> foci, StreamingPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            plan.Clear();
            _time += dt; _tick++;
            long pt = System.Diagnostics.Stopwatch.GetTimestamp();

            int firstInfo = plan.Clusters.Count;
            _clusters.Assign(foci, QuietStep, plan.Assignments, plan.Clusters);
            if (_bus != null)
                for (int i = firstInfo; i < plan.Clusters.Count; i++)
                {
                    var c = plan.Clusters[i];
                    if (c.Rebased) _bus.Publish(new ClusterOriginChanged { ClusterId = c.ClusterId, OldX = c.PrevOriginX, OldZ = c.PrevOriginZ, NewX = c.OriginX, NewZ = c.OriginZ });
                }

            PhaseEnd(0, ref pt);
            bool replan = ShouldReplan(foci);
            if (replan)
            {
                BuildWanted(foci);
                PhaseEnd(1, ref pt);
                ApplyFallbacks();
                PhaseEnd(2, ref pt);
                SnapshotFoci(foci);
            }
            else { ReplansSkipped++; PhaseMsLast[1] = PhaseMsLast[2] = 0.0; pt = System.Diagnostics.Stopwatch.GetTimestamp(); }

            // mark needed, find retention-based evictions
            foreach (var e in _entries.Values)
            {
                if (_want.TryGetValue(e.Key, out var w)) { e.LastNeededTick = _tick; e.Priority = w.Priority; }
                else e.Priority = 0.0;
            }
            _scratch.Clear();
            foreach (var e in _entries.Values)
                if (e.State == TileState.Resident && !_want.ContainsKey(e.Key) && !Retained(e.Key, foci)) _scratch.Add(e);
            _scratch.Sort(KeyComparer.Instance);
            for (int i = 0; i < _scratch.Count; i++) EvictEntry(_scratch[i], plan);

            PhaseEnd(3, ref pt);
            // budget: evict by lowest priority, then LRU, until under the target
            long gpuCap = _cfg.Budget.GpuBytes, cpuCap = _cfg.Budget.CpuBytes;
            long pendingGpu = 0, pendingCpu = 0;
            foreach (var e in _entries.Values) if (e.State == TileState.Evicting) { pendingGpu += e.Gpu; pendingCpu += e.Cpu; }
            if (_gpuUsed > gpuCap || _cpuUsed > cpuCap)
            {
                double tg = gpuCap * _cfg.EvictTargetFrac, tc = cpuCap * _cfg.EvictTargetFrac;
                CollectEvictable(); 
                for (int i = 0; i < _scratch.Count && (_gpuUsed - pendingGpu > tg || _cpuUsed - pendingCpu > tc); i++)
                {
                    pendingGpu += _scratch[i].Gpu; pendingCpu += _scratch[i].Cpu;
                    EvictEntry(_scratch[i], plan);
                }
            }

            PhaseEnd(4, ref pt);
            // loads
            _cand.Clear();
            foreach (var kv in _want)
            {
                var key = kv.Key;
                if (_entries.ContainsKey(key)) continue;
                if (_fail.TryGetValue(key, out var f) && (f.Parked || f.RetryAtS > _time)) continue;
                _cost.Estimate(key, out long g, out long c);
                _cand.Add(new TileRequest { Key = key, Priority = kv.Value.Priority, EstGpuBytes = g, EstCpuBytes = c, Urgent = key.Kind == ResidencyKind.Physics });
            }
            _cand.Sort(RequestComparer.Instance);
            int inFlight = 0;
            foreach (var e in _entries.Values) if (e.Key.Kind == ResidencyKind.Render && e.State >= TileState.Requested && e.State <= TileState.Building) inFlight++;
            bool blockedRender = false, blockedPhysics = false, blockedAny = false;
            bool evictorReady = false;
            for (int i = 0; i < _cand.Count; i++)
            {
                var r = _cand[i];
                bool phys = r.Key.Kind == ResidencyKind.Physics;
                if (phys ? blockedPhysics : blockedRender) continue;
                if (phys) { if (_physicsCount >= _cfg.Budget.MaxPhysicsChunks) { blockedPhysics = true; continue; } }
                else if (inFlight >= _cfg.Budget.MaxInFlightReads) { blockedRender = true; continue; }
                if (_gpuUsed + r.EstGpuBytes > gpuCap || _cpuUsed + r.EstCpuBytes > cpuCap)
                {
                    // make room: evict unwanted residents (their bytes leave the budget when the host calls Released), then retry on a later tick
                    if (!evictorReady) { CollectEvictable(); _evictCursor = 0; evictorReady = true; }
                    long needG = _gpuUsed - pendingGpu + r.EstGpuBytes - gpuCap, needC = _cpuUsed - pendingCpu + r.EstCpuBytes - cpuCap;
                    while ((needG > 0 || needC > 0) && _evictCursor < _scratch.Count)
                    {
                        var v = _scratch[_evictCursor++];
                        if (v.State != TileState.Resident) continue;
                        needG -= v.Gpu; needC -= v.Cpu; pendingGpu += v.Gpu; pendingCpu += v.Cpu;
                        EvictEntry(v, plan);
                    }
                    if (phys) blockedPhysics = true; else blockedRender = true;
                    blockedAny = true;
                    continue;
                }
                var e = new Entry { Key = r.Key, State = TileState.Requested, Gpu = r.EstGpuBytes, Cpu = r.EstCpuBytes, LastNeededTick = _tick, Priority = r.Priority };
                _entries[r.Key] = e;
                _gpuUsed += e.Gpu; _cpuUsed += e.Cpu;
                if (phys) _physicsCount++; else inFlight++;
                plan.Load.Add(r);
            }
            if (blockedAny) BudgetBlockedTicks++;
            PhaseEnd(5, ref pt);

            // the refinement memory of this tick is the hysteresis state of the next (a skipped replan did not touch it)
            if (replan) { var t = _refined; _refined = _refinedNext; _refinedNext = t; _refinedNext.Clear(); }
            if (plan.Load.Count > 0 || plan.Evict.Count > 0) _wantDirty = true;
        }

        int _evictCursor;

        // ---- T-124d: gated replan (default off: StreamingConfig.ReplanMoveM = 0 recomputes the wanted set every tick, exactly as before)
        FocusSample[] _lastFoci = new FocusSample[0];
        int _lastFociCount = -1;
        double _lastReplanTime;
        bool _wantDirty = true;
        /// <summary>Ticks whose wanted-set recompute (quadtree walks, physics rings, fallbacks) was skipped by the gate.</summary>
        public long ReplansSkipped { get; private set; }

        void SnapshotFoci(ReadOnlySpan<FocusSample> foci)
        {
            if (_lastFoci.Length < foci.Length) _lastFoci = new FocusSample[Math.Max(foci.Length, 2 * _lastFoci.Length)];
            for (int i = 0; i < foci.Length; i++) _lastFoci[i] = foci[i];
            _lastFociCount = foci.Length; _lastReplanTime = _time; _wantDirty = false;
        }

        /// <summary>True when the wanted set must be recomputed. With ReplanMoveM = 0 always. Otherwise only when something it depends on changed: residency state (a host callback
        /// or a request/evict of the last tick), the focus set or any focus parameter, a focus moved (position + lead x velocity change) by ReplanMoveM or more since the last
        /// recompute, or ReplanMaxIntervalS elapsed. Skipped ticks reuse the cached wanted set and priorities.</summary>
        bool ShouldReplan(ReadOnlySpan<FocusSample> foci)
        {
            double m = _cfg.ReplanMoveM;
            if (m <= 0.0 || _wantDirty || _lastFociCount != foci.Length || _time - _lastReplanTime >= _cfg.ReplanMaxIntervalS) return true;
            double lead = _cfg.PrefetchLeadS;
            for (int i = 0; i < foci.Length; i++)
            {
                ref readonly var a = ref foci[i]; ref var b = ref _lastFoci[i];
                if (a.Id != b.Id || a.Kind != b.Kind || a.NeedsPhysics != b.NeedsPhysics || a.NeedsRender != b.NeedsRender
                    || a.FovDeg != b.FovDeg || a.OutputHeightPx != b.OutputHeightPx || a.IntegerScale != b.IntegerScale || a.VisualRangeM != b.VisualRangeM) return true;
                double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
                double dvx = (double)a.Vx - b.Vx, dvy = (double)a.Vy - b.Vy, dvz = (double)a.Vz - b.Vz;
                double err = Math.Sqrt(dx * dx + dz * dz) + Math.Abs(dy) + lead * Math.Sqrt(dvx * dvx + dvy * dvy + dvz * dvz);
                if (err >= m) return true;
            }
            return false;
        }

        /// <summary>T-124d profile (no behaviour effect): wall ms per phase of the last Tick / the largest seen: 0 clusters, 1 wanted set, 2 fallbacks, 3 retention evict, 4 budget evict, 5 loads.</summary>
        public readonly double[] PhaseMsLast = new double[6], PhaseMsMax = new double[6];
        void PhaseEnd(int i, ref long t0)
        {
            long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            double ms = (t1 - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            PhaseMsLast[i] = ms; if (ms > PhaseMsMax[i]) PhaseMsMax[i] = ms;
            t0 = t1;
        }

        void EvictEntry(Entry e, StreamingPlan plan)
        {
            e.State = TileState.Evicting;
            plan.Evict.Add(e.Key);
            _generation.TryGetValue(e.Key, out int g);
            _bus?.Publish(new TileEvicted { Tile = e.Key.Tile, Kind = e.Key.Kind, Chunk = e.Key.Chunk, Generation = g });
        }

        /// <summary>Resident keys that no focus needs now (not wanted, not a protected fallback), lowest priority then least recently needed first.</summary>
        void CollectEvictable()
        {
            _scratch.Clear();
            foreach (var e in _entries.Values)
                if (e.State == TileState.Resident && !_want.ContainsKey(e.Key)) _scratch.Add(e);
            _scratch.Sort(EvictOrderComparer.Instance);
        }

        // ---------------- wanted set ----------------

        void BuildWanted(ReadOnlySpan<FocusSample> foci)
        {
            _want.Clear();
            foreach (var l in _ring.Values) l.Clear();
            for (int i = 0; i < foci.Length; i++)
            {
                var f = foci[i];
                if (f.Kind == FocusKind.Body && f.NeedsPhysics) AddPhysics(f);
            }
            for (int i = 0; i < foci.Length; i++) if (DrivesRender(foci[i])) AddRender(foci[i]);
        }

        static bool DrivesRender(in FocusSample f) => f.Kind == FocusKind.Camera || f.NeedsRender;

        void Raise(in ResidencyKey key, double priority, bool leaf, bool fallback)
        {
            if (_want.TryGetValue(key, out var w))
            {
                if (priority > w.Priority) w.Priority = priority;
                w.Leaf |= leaf; w.Fallback &= fallback;
                _want[key] = w;
            }
            else _want[key] = new Want { Priority = priority, Leaf = leaf, Fallback = fallback };
        }

        // physics ring: chunks of L0 tiles whose footprint is within R of the body (R x 1.5 for chunks already loaded)
        void AddPhysics(in FocusSample f)
        {
            if (!_ring.TryGetValue(f.Id, out var ring)) { ring = new List<ResidencyKey>(); _ring[f.Id] = ring; }
            double R = _cfg.PhysicsRadiusM, Rq = _cfg.PhysicsRequestRadiusM, Rk = _cfg.PhysicsRetainRadiusM, cs = ResidencyKey.ChunkSizeM;
            int x0 = (int)Math.Floor((f.X - Rk) / cs), x1 = (int)Math.Floor((f.X + Rk) / cs);
            int z0 = (int)Math.Floor((f.Z - Rk) / cs), z1 = (int)Math.Floor((f.Z + Rk) / cs);
            for (int cz = Math.Max(0, z0); cz <= z1; cz++)
                for (int cx = Math.Max(0, x0); cx <= x1; cx++)
                {
                    var tile = GeoTileId.From(0, (int)(cx / ResidencyKey.ChunksPerEdge), (int)(cz / ResidencyKey.ChunksPerEdge));
                    var key = ResidencyKey.PhysicsChunk(tile, cx % ResidencyKey.ChunksPerEdge, cz % ResidencyKey.ChunksPerEdge);
                    double bx0 = cx * cs, bz0 = cz * cs;
                    double dx = Math.Max(Math.Max(bx0 - f.X, f.X - (bx0 + cs)), 0.0), dz = Math.Max(Math.Max(bz0 - f.Z, f.Z - (bz0 + cs)), 0.0);
                    double d = Math.Sqrt(dx * dx + dz * dz);
                    bool loaded = _entries.ContainsKey(key);
                    if (d > (loaded ? Rk : Rq)) continue;
                    if (!_cost.Exists(tile)) continue;
                    if (d <= R) ring.Add(key);
                    // rule 3 with t_reach = 0 (inside the physics ring), sse term = closeness: P x 1000 outranks every render tile
                    double closeness = 1.0 - Math.Min(1.0, d / Rq);
                    Raise(key, _cfg.PhysicsPriorityBoost * (1.0 + 8.0 * closeness) / 0.25, true, false);
                }
        }

        void AddRender(in FocusSample f)
        {
            double H = f.OutputHeightPx > 0 ? f.OutputHeightPx : _cfg.DefaultOutputHeightPx;
            double fov = f.FovDeg > 0 ? f.FovDeg : _cfg.DefaultFovDeg;
            int k = f.IntegerScale > 0 ? f.IntegerScale : _cfg.DefaultIntegerScale;
            double pxPerM = H / (2.0 * Math.Tan(fov * 0.5 * Math.PI / 180.0));
            double tau = _cfg.TauFactor / k;
            double speed = Math.Sqrt((double)f.Vx * f.Vx + (double)f.Vy * f.Vy + (double)f.Vz * f.Vz);
            double lead = speed * _cfg.PrefetchLeadS;
            int top = _cfg.MaxLevel;
            double dcut = DCut(f);
            var ctx = new FocusCtx { F = f, PxPerM = pxPerM, Tau = tau, Lead = lead, DCut = dcut };
            double size = GeoTileId.BaseSizeM * (1 << top);
            double reach = dcut * (1.0 + _cfg.RenderHysteresisFrac) + lead;
            int i0 = Math.Max(0, (int)Math.Floor((f.X - reach) / size)), i1 = (int)Math.Floor((f.X + reach) / size);
            int j0 = Math.Max(0, (int)Math.Floor((f.Z - reach) / size)), j1 = (int)Math.Floor((f.Z + reach) / size);
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                    Visit(GeoTileId.From(top, i, j), ctx);
        }

        struct FocusCtx { public FocusSample F; public double PxPerM, Tau, Lead, DCut; }

        double DCut(in FocusSample f)
        {
            double v = f.VisualRangeM > 0 ? f.VisualRangeM : _cfg.DefaultVisualRangeM;
            return Math.Min(_cfg.MaxRenderRingM, Math.Max(_cfg.MinRenderRingM, v));
        }

        /// <summary>Outer radius of level n for this focus: its ring, raised to the next level's switch distance (no hole), never beyond D_cut.</summary>
        double RingCap(int level, in FocusSample f, out double pxPerM, out double tau)
        {
            double H = f.OutputHeightPx > 0 ? f.OutputHeightPx : _cfg.DefaultOutputHeightPx;
            double fov = f.FovDeg > 0 ? f.FovDeg : _cfg.DefaultFovDeg;
            int k = f.IntegerScale > 0 ? f.IntegerScale : _cfg.DefaultIntegerScale;
            pxPerM = H / (2.0 * Math.Tan(fov * 0.5 * Math.PI / 180.0));
            tau = _cfg.TauFactor / k;
            double dcut = DCut(f);
            if (level >= _cfg.MaxLevel) return dcut;
            double cap = _cfg.RenderRings[level].RadiusM;
            double next = _cost.ErrRmsM(level + 1) * pxPerM / tau * _cfg.SwitchDownFactor;
            return Math.Min(dcut, Math.Max(cap, next));
        }

        void Visit(GeoTileId tile, in FocusCtx c)
        {
            if (!_cost.Exists(tile)) return;
            int n = tile.Level;
            var key = ResidencyKey.Render(tile);
            // distance from the focus to the closest point of the tile, with the prefetch lead taken off
            double bx0 = tile.OriginX, bz0 = tile.OriginZ, bx1 = bx0 + tile.SizeM, bz1 = bz0 + tile.SizeM;
            double dx = Math.Max(Math.Max(bx0 - c.F.X, c.F.X - bx1), 0.0), dz = Math.Max(Math.Max(bz0 - c.F.Z, c.F.Z - bz1), 0.0);
            double dy = Math.Abs(c.F.Y);
            double dmin = Math.Sqrt(dx * dx + dz * dz + dy * dy);
            double dEff = Math.Max(0.0, dmin - c.Lead);
            double cap = RingCap(n, c.F, out _, out _);
            double limit = _entries.ContainsKey(key) ? cap * (1.0 + _cfg.RenderHysteresisFrac) : cap;
            if (dEff > limit) return;

            if (n > 0)
            {
                double dn = _cost.ErrRmsM(n) * c.PxPerM / c.Tau;     // distance at which level n has e_out == tau
                bool refine = dEff < dn * _cfg.SwitchUpFactor || (_refined.Contains(key) && dEff < dn * _cfg.SwitchDownFactor);
                if (refine)
                {
                    _refinedNext.Add(key);
                    for (int dj = 0; dj < 2; dj++)
                        for (int di = 0; di < 2; di++)
                            Visit(GeoTileId.From(n - 1, tile.I * 2 + di, tile.J * 2 + dj), c);
                    return;
                }
            }
            // leaf: priority P = (1 + min(8, sse / tau)) / (t_reach + 0.25), sse = error of the parent level (what is seen if this tile is missing)
            double parentErr = _cost.ErrRmsM(Math.Min(n + 1, _cfg.MaxLevel));
            double sse = parentErr * c.PxPerM / Math.Max(dmin, 1.0);
            double vClose = Closing(c.F, bx0, bz0, bx1, bz1, dmin);
            double tReach = Math.Max(0.0, dmin - cap) / Math.Max(vClose, 1.0);
            double p = (1.0 + Math.Min(8.0, sse / c.Tau)) / (tReach + 0.25);
            Raise(key, p, true, false);
        }

        static double Closing(in FocusSample f, double bx0, double bz0, double bx1, double bz1, double d)
        {
            double nx = Math.Min(Math.Max(f.X, bx0), bx1), nz = Math.Min(Math.Max(f.Z, bz0), bz1);
            double rx = nx - f.X, rz = nz - f.Z, len = Math.Sqrt(rx * rx + rz * rz);
            if (len < 1e-9) return Math.Sqrt((double)f.Vx * f.Vx + (double)f.Vz * f.Vz);
            return Math.Max(0.0, (f.Vx * rx + f.Vz * rz) / len);
        }

        /// <summary>Parent fallback (rule 2): a wanted leaf that is not resident keeps its nearest resident ancestor alive; with none resident the top-level ancestor is requested.</summary>
        void ApplyFallbacks()
        {
            _scratch.Clear();
            _fbKeys.Clear();
            foreach (var kv in _want)
                if (kv.Key.Kind == ResidencyKind.Render && kv.Value.Leaf && !IsResident(kv.Key)) _fbKeys.Add(kv.Key);
            _fbKeys.Sort();
            for (int i = 0; i < _fbKeys.Count; i++)
            {
                var key = _fbKeys[i];
                double p = _want[key].Priority;
                var t = key.Tile; bool found = false;
                while (t.Level < _cfg.MaxLevel)
                {
                    t = GeoTileId.From(t.Level + 1, t.I >> 1, t.J >> 1);
                    var ak = ResidencyKey.Render(t);
                    if (IsResident(ak)) { Raise(ak, p, false, true); found = true; break; }
                }
                if (!found && _cost.Exists(t)) Raise(ResidencyKey.Render(t), p * 1.5, false, true);
            }
        }

        readonly List<ResidencyKey> _fbKeys = new List<ResidencyKey>();

        bool IsResident(in ResidencyKey k) => _entries.TryGetValue(k, out var e) && e.State == TileState.Resident;

        bool Retained(in ResidencyKey key, ReadOnlySpan<FocusSample> foci)
        {
            key.Bounds(out double x0, out double z0, out double x1, out double z1);
            for (int i = 0; i < foci.Length; i++)
            {
                var f = foci[i];
                double dx = Math.Max(Math.Max(x0 - f.X, f.X - x1), 0.0), dz = Math.Max(Math.Max(z0 - f.Z, f.Z - z1), 0.0);
                if (key.Kind == ResidencyKind.Physics)
                {
                    if (f.Kind != FocusKind.Body || !f.NeedsPhysics) continue;
                    if (Math.Sqrt(dx * dx + dz * dz) <= _cfg.PhysicsRetainRadiusM) return true;
                }
                else
                {
                    if (!DrivesRender(f)) continue;
                    double speed = Math.Sqrt((double)f.Vx * f.Vx + (double)f.Vy * f.Vy + (double)f.Vz * f.Vz);
                    double dEff = Math.Max(0.0, Math.Sqrt(dx * dx + dz * dz + f.Y * f.Y) - speed * _cfg.PrefetchLeadS);
                    if (dEff <= RingCap(key.Tile.Level, f, out _, out _) * (1.0 + _cfg.RenderHysteresisFrac)) return true;
                }
            }
            return false;
        }

        // ---------------- comparers (all end on the key: total order, deterministic) ----------------

        sealed class KeyComparer : IComparer<Entry>
        {
            public static readonly KeyComparer Instance = new KeyComparer();
            public int Compare(Entry a, Entry b) => a.Key.CompareTo(b.Key);
        }

        sealed class RequestComparer : IComparer<TileRequest>
        {
            public static readonly RequestComparer Instance = new RequestComparer();
            public int Compare(TileRequest a, TileRequest b)
            {
                int c = b.Priority.CompareTo(a.Priority);   // higher first
                return c != 0 ? c : a.Key.CompareTo(b.Key);
            }
        }

        sealed class EvictOrderComparer : IComparer<Entry>
        {
            public static readonly EvictOrderComparer Instance = new EvictOrderComparer();
            public int Compare(Entry a, Entry b)
            {
                int c = a.Priority.CompareTo(b.Priority);                 // lowest priority first
                if (c != 0) return c;
                c = a.LastNeededTick.CompareTo(b.LastNeededTick);         // then least recently needed
                return c != 0 ? c : a.Key.CompareTo(b.Key);
            }
        }
    }
}
