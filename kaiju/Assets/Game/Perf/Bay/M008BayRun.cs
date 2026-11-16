// M-008@v1 Bay scenario harness (T-124, research/R-103.md section 10, plans/M2.md Track 5). Editor-only, batch mode, main thread:
//   unity run <project> -- -executeMethod Game.Perf.Bay.M008BayRun.Run -m008Out DIR -m008Tiles <tiles/v1 root> [-m008Raster bay_cost_256m_v1.bin]
//        [-m008Scenario bay|ref|sweep] [-m008Seeds 1,2,3,4,5] [-m008Warmup 60] [-m008Measure 600] [-m008Kaiju N] [-m008Mechs N] [-m008Realtime 1]
// What runs per frame (60 Hz, paced to wall time so asynchronous tile reads and collider bakes happen in real time):
//   brain + pack   the m007_bay `BayHarness` (scripted brains + PackCoordinator + route planner on the T-102 raster), 10 Hz steps, `mixed` scenario on the
//                  `Real` Bay layout (mechs guard the Port of Oakland cranes), pack spawned 4.5 km east of the Port; its cost is the wall time between hook calls
//   streaming      the real StreamingHostCore over the baked Bay tiles (GeoL0TileReader L0 + GeoLodTileReader L1..L4, ThreadPoolWorkRunner), UnityTileBuilder with
//                  one PhysicsScene per cluster; foci = camera at the host mech, 2+ mech bodies, 3+ kaiju bodies, one debris anchor
//   physics        kinematic placeholder bodies (boxes) placed from the plan, `PhysicsScene.Simulate(1/60)` for every cluster, ground raycasts, mech cannon raycasts (2 Hz)
//   destruction    scripted structural impacts every 8 s at the kaiju: the real `DamageModel` takes the impact energy, the real `DebrisAccounting` gives the piece
//                  count and masses, and dynamic Rigidbody fragments (<= 64 per building, 16 spawned per frame) rest as rubble in the cluster scene
// Bodies are PLACEHOLDERS (T-005 walk is not done; T-117 kaiju bodies not built): kinematic boxes moved by the harness, flagged in the output. No rendering here:
// `total_ms` is main-thread host simulation per frame; M-008.b stays NOT_RUN until a rendering run exists. Output `m008_run@v1`, read by Tools/Metrics `m008`.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using Game.AI.PackHarness;
using Game.Composition;
using Game.Core;
using Game.World.Destruction;
using Game.World.Geo;
using Game.World.Geo.Unity;
using Game.World.Streaming;
using Game.World.Streaming.Host;
using Game.World.Streaming.Unity;

namespace Game.Perf.Bay
{
    public sealed class M008Options
    {
        public string OutDir = "evidence/T-124";
        public string TilesRoot;
        public string RasterPath;
        public string Scenario = "bay";            // bay | ref | sweep
        public ulong Seed = 1;
        public double WarmupS = 60.0, MeasureS = 600.0;
        public int Kaiju = 3, Mechs = 2;
        public double EventPeriodS = 8.0;           // destruction impacts, 7.5 / min
        public int SpawnPiecesPerFrame = 16;
        public bool Realtime = true;
        public bool Destruction = true;
        public string Label = "";
        public double SpawnDistM = 4500.0;          // pack spawn east of the Port (R-103: camera at the host mech, pack 2.5 km away once the pack closes)
        public int FragmentsTarget = 0;             // sweep: cap on live fragments (0 = none)
        public double PhysicsPrefetchM = 0.0;       // T-124b: StreamingConfig.PhysicsPrefetchRadiusM (0 = off)
        public bool SliceRender = false;            // T-124b: UnityTileBuilder.SliceRenderBuilds
        public double UrgentSliceMs = 0.0;          // T-124b: StreamingHostOptions.UrgentSliceMs (0 = off)
    }

    public static class M008BayRun
    {
        static string Arg(string[] a, string n, string d) { for (int i = 0; i < a.Length - 1; i++) if (a[i] == n) return a[i + 1]; return d; }
        static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);

        /// <summary>Batch entry. Writes m008_&lt;scenario&gt;_seed&lt;n&gt;.json per seed into -m008Out.</summary>
        public static void Run()
        {
            int code = 0;
            try
            {
                var a = Environment.GetCommandLineArgs();
                var o = new M008Options
                {
                    OutDir = Arg(a, "-m008Out", Path.Combine(Directory.GetCurrentDirectory(), "evidence", "T-124")),
                    TilesRoot = Arg(a, "-m008Tiles", null), RasterPath = Arg(a, "-m008Raster", null),
                    Scenario = Arg(a, "-m008Scenario", "bay"), WarmupS = D(Arg(a, "-m008Warmup", "60")), MeasureS = D(Arg(a, "-m008Measure", "600")),
                    Realtime = Arg(a, "-m008Realtime", "1") != "0",
                };
                if (o.TilesRoot == null || !Directory.Exists(o.TilesRoot)) throw new ArgumentException("-m008Tiles must name the baked tiles/v1 root (L0, L1.. folders)");
                o.RasterPath = o.RasterPath ?? Path.Combine(Directory.GetCurrentDirectory(), "evidence", "T-115", "bay_cost_256m_v1.bin");
                o.PhysicsPrefetchM = D(Arg(a, "-m008Prefetch", "0")); o.UrgentSliceMs = D(Arg(a, "-m008UrgentSlice", "0")); o.SliceRender = Arg(a, "-m008SliceRender", "0") != "0";
                var sd = Arg(a, "-m008Spawn", null); if (sd != null) o.SpawnDistM = D(sd);
                if (o.Scenario == "ref") { o.Kaiju = 1; o.Mechs = 1; o.Destruction = false; }
                if (a.Length > 0) { var k = Arg(a, "-m008Kaiju", null); if (k != null) o.Kaiju = int.Parse(k); var m = Arg(a, "-m008Mechs", null); if (m != null) o.Mechs = int.Parse(m); var f = Arg(a, "-m008Fragments", null); if (f != null) o.FragmentsTarget = int.Parse(f); }
                Directory.CreateDirectory(o.OutDir);
                foreach (var sp in Arg(a, "-m008Seeds", "1").Split(','))
                {
                    o.Seed = ulong.Parse(sp);
                    string label = o.Label.Length > 0 ? o.Label : "m008_" + o.Scenario + (o.Scenario == "sweep" ? "_k" + o.Kaiju + "_m" + o.Mechs + "_f" + o.FragmentsTarget : "");
                    var sw = Stopwatch.StartNew();
                    string json = RunOne(o, label);
                    string path = Path.Combine(o.OutDir, label + "_seed" + o.Seed + ".json");
                    File.WriteAllText(path, json);
                    UnityEngine.Debug.Log("M008: wrote " + path + " in " + sw.Elapsed.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture) + " s");
                }
            }
            catch (Exception e) { UnityEngine.Debug.LogError("M008 FAILED: " + e); code = 1; }
            UnityEditor.EditorApplication.Exit(code);
        }

        // ------------------------------------------------------------------------------------------------ one run

        sealed class Body
        {
            public int Id, Kind;             // Kind 0 mech, 1 kaiju, 2 debris anchor, 3 camera
            public double X, Z, VX, VZ; public float GroundY = 10f; public bool Alive = true, Held;
            public Vector3 Size; public GameObject Go; public int Cluster = int.MinValue; public int Species;
        }

        sealed class Fragment { public Transform T; public int Cluster; }

        const int BodyLayer = 9, FragLayer = 10;

        static string RunOne(M008Options o, string label)
        {
            var settings = GeoTileBuilder.LoadSettings(Application.dataPath);
            var field = BayRasterCostField.Load(o.RasterPath);
            // scenario
            var sb = new StringBuilder(); for (int i = 0; i < o.Kaiju; i++) sb.Append("LMH"[i % 3]); string comp = sb.ToString();
            var lay = BayLayout.Real.Translated(0, 0);
            lay.MixedX = (float)(lay.Infra[5].X + o.SpawnDistM); lay.MixedY = (float)(lay.Infra[5].Y - 200.0);
            var sc = new BayScenario { Name = "mixed", Composition = comp, Seed = o.Seed, Map = "real", Field = field, Layout = lay, DurationSeconds = o.WarmupS + o.MeasureS + 1.0, MechCountOverride = o.Mechs };
            var hr = new BayHarness(sc, TextWriter.Null);
            const double Step = 0.1; const int FramesPerStep = 6; const float Dt = 1f / 60f;

            // streaming host over the real tiles
            var parent = new GameObject("M008Root");
            var scenes = new ClusterPhysicsScenes("M008Cluster");
            var router = new LevelRoutingReader();
            router.Register(0, new GeoL0TileReader(o.TilesRoot, settings, 4));
            GeoLodTileReader.RegisterAll(router, o.TilesRoot);
            var runner = new ThreadPoolWorkRunner();
            var builder = new UnityTileBuilder(parent.transform, runner, settings) { ClusterScenes = scenes, SliceRenderBuilds = o.SliceRender };
            var sched = new StreamingScheduler(new StreamingConfig { PhysicsPrefetchRadiusM = o.PhysicsPrefetchM }, new DefaultTileCostModel());
            var host = new StreamingHostCore(sched, router, builder, runner, new StreamingHostOptions { UrgentSliceMs = o.UrgentSliceMs });

            // bodies (placeholders): camera rides the host mech
            var bodies = new List<Body>();
            Body cam = new Body { Id = 1, Kind = 3 };
            bodies.Add(cam);
            for (int i = 0; i < o.Mechs; i++) bodies.Add(new Body { Id = 10 + i, Kind = 0, Size = new Vector3(16f, 80f, 16f) });
            float[] kh = { 0f, 60f, 100f, 160f };
            for (int i = 0; i < o.Kaiju; i++) { int sp = "LMH"[i % 3] == 'L' ? 1 : "LMH"[i % 3] == 'M' ? 2 : 3; float h = kh[sp]; bodies.Add(new Body { Id = 20 + i, Kind = 1, Species = sp, Size = new Vector3(0.3f * h, h, 0.6f * h) }); }
            var anchor = new Body { Id = 40, Kind = 2, Size = Vector3.one }; bodies.Add(anchor);
            var foci = new FocusSample[bodies.Count];

            var dm = new DamageModel();
            var rnd = new System.Random((int)(o.Seed * 7919 + 13));
            var frags = new List<Fragment>(8192);
            var spawnQ = new Queue<(int cluster, Vector3 pos, float mass, float size, Vector3 vel)>();
            var origins = new Dictionary<int, (double x, double z)>();

            // measured channels
            var phase = new List<int>(40000); var total = new List<float>(40000); var phys = new List<float>(40000); var destr = new List<float>(40000);
            var brainL = new List<float>(40000); var strm = new List<float>(40000); var alloc = new List<long>(40000);
            long lateFrames = 0, holds = 0, noGround = 0, evictedFrags = 0, spawned = 0, deferredEvents = 0; int events = 0, peakFrags = 0; double maxItem = 0; double spawnMsSum = 0; long spawnPieces = 0;
            var hitchLog = new List<string>(); int gcHitch = 0; var holdLog = new List<string>(); var holdDiag = new List<string>(); var lastHeld = new Dictionary<int, long>(); var holdByKind = new long[4]; double firstHoldS = -1;
            bool engaged = false; double nextEvent = o.WarmupS + 4.0; int evId = 0; long loadsStart = 0; long rssMaxKb = 0; int gc0 = 0; long frameNo = 0;
            double simNow = 0; var wall = Stopwatch.StartNew();
            var gap = Stopwatch.StartNew(); long gapAlloc0 = GC.GetAllocatedBytesForCurrentThread();
            bool measuring = false;
            var proc = Process.GetCurrentProcess();

            RaycastHit hit;
            int groundMask = ~((1 << BodyLayer) | (1 << FragLayer)), bodyMask = 1 << BodyLayer;

            Body FindById(int id) { foreach (var b in bodies) if (b.Id == id) return b; return null; }

            void Snapshot(BayHarness h)
            {
                int mi = 0, ki = 0; double minD = double.MaxValue;
                for (int i = 0; i < h.MechCount; i++) if (h.TryGetMech(i, out _, out double mx, out double my, out float mh, out bool ma) && mi < o.Mechs) { var b = FindById(10 + mi++); b.VX = (mx - b.X) / Step; b.VZ = (my - b.Z) / Step; if (Math.Abs(b.VX) > 100 || Math.Abs(b.VZ) > 100) { b.VX = 0; b.VZ = 0; } b.X = mx; b.Z = my; b.Alive = ma; }
                for (int i = 0; i < h.KaijuCount; i++) if (h.TryGetKaiju(i, out _, out _, out double kx, out double ky, out float kh2, out bool ka) && ki < o.Kaiju) { var b = FindById(20 + ki++); b.VX = (kx - b.X) / Step; b.VZ = (ky - b.Z) / Step; if (Math.Abs(b.VX) > 100 || Math.Abs(b.VZ) > 100) { b.VX = 0; b.VZ = 0; } b.X = kx; b.Z = ky; b.Alive = ka; }
                var host0 = FindById(10); cam.X = host0.X; cam.Z = host0.Z; cam.VX = host0.VX; cam.VZ = host0.VZ;
                foreach (var kb in bodies) if (kb.Kind == 1 && kb.Alive) foreach (var mb in bodies) if (mb.Kind == 0 && mb.Alive) { double dx = kb.X - mb.X, dz = kb.Z - mb.Z; minD = Math.Min(minD, Math.Sqrt(dx * dx + dz * dz)); }
                if (!engaged && minD <= 1200.0) engaged = true;
            }

            void Frame(double frameSim, bool pre, int f, double brainMs, long brainAlloc)
            {
                {

                    bool inMeasure = !pre && frameSim >= o.WarmupS && frameSim < o.WarmupS + o.MeasureS;
                    if (inMeasure && !measuring) { measuring = true; host.ResetItemProfile(); loadsStart = host.LoadsDispatched; gc0 = GC.CollectionCount(0); if (nextEvent < frameSim) nextEvent = frameSim + 4.0; }
                    if (o.Realtime && !pre)
                    {   // pace to wall time: frame n of the whole run is due at n * Dt
                        double due = frameNo * (double)Dt; while (wall.Elapsed.TotalSeconds < due) Thread.Sleep(1);
                    }
                    if (!pre) frameNo++;
                    long a0 = GC.GetAllocatedBytesForCurrentThread();
                    long t0 = Stopwatch.GetTimestamp();

                    // ---- streaming
                    for (int i = 0; i < bodies.Count; i++)
                    {
                        var b = bodies[i];
                        if (b.Kind == 2) { var kb = FindById(20); if (kb != null) { b.X = kb.X; b.Z = kb.Z; b.VX = kb.VX; b.VZ = kb.VZ; } }
                        foci[i] = new FocusSample
                        {
                            Id = b.Id, Kind = b.Kind == 3 ? FocusKind.Camera : FocusKind.Body, X = b.X, Y = b.GroundY, Z = b.Z, Vx = (float)b.VX, Vz = (float)b.VZ, ReachM = b.Size.y,
                            NeedsPhysics = b.Kind == 0 || b.Kind == 1 || b.Kind == 2, NeedsRender = false,
                        };
                    }
                    host.Tick(Dt, foci);
                    builder.Tick();
                    long t1 = Stopwatch.GetTimestamp();
                    var plan = host.LastPlan;
                    maxItem = Math.Max(maxItem, host.LastBuildMs);

                    // ---- physics: bodies from the plan, simulate, cannon
                    foreach (var c in plan.Clusters)
                    {
                        if (c.Rebased && frags.Count > 0)
                        {
                            Vector3 dd = new Vector3((float)(c.PrevOriginX - c.OriginX), 0f, (float)(c.PrevOriginZ - c.OriginZ));
                            foreach (var fr in frags) if (fr.Cluster == c.ClusterId && fr.T != null) fr.T.position += dd;
                        }
                        origins[c.ClusterId] = (c.OriginX, c.OriginZ);
                    }
                    int heldNow = 0;
                    for (int a = 0; a < plan.Assignments.Count; a++)
                    {
                        var asg = plan.Assignments[a];
                        var b = FindById(asg.FocusId); if (b == null || b.Kind == 3) continue;
                        b.Held = host.IsBodyHeld(asg.FocusId);
                        if (b.Held) { heldNow++; if (measuring) { holdByKind[b.Kind]++; if (firstHoldS < 0) firstHoldS = frameSim; if (holdLog.Count < 40 && (!lastHeld.TryGetValue(b.Id, out var lh) || frameNo - lh > 1)) holdLog.Add(b.Id + "@" + frameSim.ToString("0.00", CultureInfo.InvariantCulture) + " cl" + asg.ClusterId + " (" + b.X.ToString("0", CultureInfo.InvariantCulture) + "," + b.Z.ToString("0", CultureInfo.InvariantCulture) + ")"); if (holdDiag.Count < 12 && b.Id == 20 && (!lastHeld.TryGetValue(b.Id, out var lh2) || frameNo - lh2 > 1)) { var sbd = new StringBuilder(); for (int ox = -2; ox <= 2; ox++) for (int oz = -2; oz <= 2; oz++) { double wx = b.X + ox * 256.0, wz = b.Z + oz * 256.0; var tid = GeoTileId.Containing(wx, wz, 0); int ccx = Math.Min(7, (int)Math.Floor((wx - tid.OriginX) / ResidencyKey.ChunkSizeM)), ccz = Math.Min(7, (int)Math.Floor((wz - tid.OriginZ) / ResidencyKey.ChunkSizeM)); var kk = ResidencyKey.PhysicsChunk(tid, ccx, ccz); var st = sched.GetState(kk); if (st != TileState.Resident) sbd.Append(kk.ToString()).Append(':').Append(st).Append(' '); } holdDiag.Add(frameSim.ToString("0.00", CultureInfo.InvariantCulture) + " " + sbd + "| inflight " + host.InFlight + " load " + plan.Load.Count); } lastHeld[b.Id] = frameNo; } continue; }
                        var cs = scenes.GetOrCreate(asg.ClusterId);
                        float gy;
                        if (cs.Physics.Raycast(new Vector3(asg.LocalX, 600f, asg.LocalZ), Vector3.down, out hit, 1200f, groundMask)) { gy = hit.point.y; b.GroundY = gy; }
                        else { if (measuring) noGround++; continue; }
                        if (b.Go == null)
                        {
                            b.Go = new GameObject("Body_" + b.Id) { layer = BodyLayer };
                            var rb = b.Go.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
                            var bc = b.Go.AddComponent<BoxCollider>(); bc.size = b.Size;
                        }
                        if (b.Cluster != asg.ClusterId) { SceneManager.MoveGameObjectToScene(b.Go, cs.Scene); b.Cluster = asg.ClusterId; }
                        b.Go.transform.position = new Vector3(asg.LocalX, gy + b.Size.y * 0.5f, asg.LocalZ);
                        if (b.Kind == 2) b.Go.transform.localScale = Vector3.one;
                    }
                    if (measuring && heldNow > 0) { lateFrames++; holds += heldNow; }
                    scenes.Simulate(Dt);
                    if (frameNo % 30 == 0)
                    {
                        foreach (var mb in bodies) if (mb.Kind == 0 && mb.Alive && mb.Go != null && scenes.TryGet(mb.Cluster, out var cs2))
                            foreach (var kb in bodies) if (kb.Kind == 1 && kb.Alive && kb.Cluster == mb.Cluster)
                            {
                                Vector3 from = mb.Go.transform.position + Vector3.up * 30f; Vector3 dir = (kb.Go.transform.position - from);
                                float dist = dir.magnitude; if (dist > 1f && dist < 900f) cs2.Physics.Raycast(from, dir / dist, out hit, 900f, bodyMask);
                            }
                    }
                    long t2 = Stopwatch.GetTimestamp();

                    // ---- destruction: scripted impact -> DamageModel -> DebrisAccounting -> queued rigid-body fragments; clusters evicted by the host lose their rubble to the journal path
                    if (o.Destruction)
                    {
                        if (measuring && frameSim >= nextEvent)
                        {
                            Body kb = null; for (int q2 = 0; q2 < bodies.Count && kb == null; q2++) { int want = 20 + ((evId + q2) % Math.Max(1, o.Kaiju)); var cand = FindById(want); if (cand != null && cand.Kind == 1 && cand.Alive && cand.Go != null && scenes.TryGet(cand.Cluster, out _)) kb = cand; }
                            if (kb == null) kb = bodies.Find(x => x.Kind == 0 && x.Alive && x.Go != null && scenes.TryGet(x.Cluster, out _));
                            if (kb != null && scenes.TryGet(kb.Cluster, out var cs3) && (o.FragmentsTarget == 0 || frags.Count + spawnQ.Count < o.FragmentsTarget))
                            {
                                nextEvent += o.EventPeriodS; events++;
                                double hM = 25.0 + (evId * 37 % 90), fp = 600.0 + (evId * 131 % 2400);
                                var id = BuildingId.From(BuildingSource.OsmWay, 9000000000UL + (ulong)evId);
                                dm.Register(id, new BuildingAttributes { HeightM = hM, FootprintM2 = fp, Levels = 0, Residential = false, YearBuilt = 1975 });
                                dm.TryGetThresholds(id, out _, out _, out _, out double completeJ);
                                var ch = dm.Apply(id, completeJ * 1.01, DamageCause.Impact, Game.Core.EntityId.FromValue((ulong)kb.Id), (long)frameNo);
                                if (ch.Changed && dm.TryGetDebrisAccount(id, out var acc))
                                {
                                    float side = (float)Math.Sqrt(fp);
                                    Vector3 c0 = kb.Go.transform.position + new Vector3(((float)rnd.NextDouble() - 0.5f) * 120f, 0f, ((float)rnd.NextDouble() - 0.5f) * 120f);
                                    if (cs3.Physics.Raycast(new Vector3(c0.x, 600f, c0.z), Vector3.down, out hit, 1200f, groundMask)) c0.y = hit.point.y; else c0.y = kb.GroundY;
                                    for (int p = 0; p < acc.PieceCount; p++)
                                    {
                                        float mass = (float)DebrisAccounting.PieceMassKg(acc.DebrisKg, acc.PieceCount, p);
                                        float sz = Mathf.Clamp((float)Math.Pow(mass / 1200.0, 1.0 / 3.0), 0.8f, 6f);
                                        Vector3 pos = c0 + new Vector3(((float)rnd.NextDouble() - 0.5f) * side, 1f + (float)rnd.NextDouble() * (float)hM * 0.8f, ((float)rnd.NextDouble() - 0.5f) * side);
                                        spawnQ.Enqueue((kb.Cluster, pos, mass, sz, new Vector3(((float)rnd.NextDouble() - 0.5f) * 6f, 0f, ((float)rnd.NextDouble() - 0.5f) * 6f)));
                                    }
                                }
                                evId++;
                            }
                            else if (kb == null || !scenes.TryGet(kb.Cluster, out _)) { deferredEvents++; }
                        }
                        long ts0 = Stopwatch.GetTimestamp(); int made = 0;
                        while (spawnQ.Count > 0 && made < o.SpawnPiecesPerFrame)
                        {
                            var q = spawnQ.Dequeue();
                            if (!scenes.TryGet(q.cluster, out var cs4)) continue;
                            var go = new GameObject("Frag") { layer = FragLayer };
                            var rb = go.AddComponent<Rigidbody>(); rb.mass = q.mass; rb.linearVelocity = q.vel;
                            go.AddComponent<BoxCollider>().size = new Vector3(q.size, q.size, q.size);
                            SceneManager.MoveGameObjectToScene(go, cs4.Scene);
                            go.transform.position = q.pos;
                            frags.Add(new Fragment { T = go.transform, Cluster = q.cluster }); spawned++; made++;
                        }
                        if (made > 0) { spawnMsSum += (Stopwatch.GetTimestamp() - ts0) * 1000.0 / Stopwatch.Frequency; spawnPieces += made; }
                        // fragments whose cluster scene is gone were removed with it (rubble then lives in the streaming journal, T-110)
                        if (frameNo % 60 == 0 && frags.Count > 0)
                        {
                            int before = frags.Count; frags.RemoveAll(fr => !scenes.TryGet(fr.Cluster, out _) || fr.T == null); evictedFrags += before - frags.Count;
                        }
                        if (measuring) peakFrags = Math.Max(peakFrags, frags.Count);
                    }
                    long t3 = Stopwatch.GetTimestamp();

                    if (measuring && inMeasure)
                    {
                        double toMs = 1000.0 / Stopwatch.Frequency;
                        double bm = f == 0 ? brainMs : 0.0;
                        phase.Add(engaged ? 1 : 0); strm.Add((float)((t1 - t0) * toMs)); phys.Add((float)((t2 - t1) * toMs)); destr.Add((float)((t3 - t2) * toMs));
                        brainL.Add((float)bm); total.Add((float)((t3 - t0) * toMs + bm));
                        alloc.Add(GC.GetAllocatedBytesForCurrentThread() - a0 + (f == 0 ? brainAlloc : 0));
                        if ((t3 - t0) * toMs + bm > 12.0 && hitchLog.Count < 40)
                            hitchLog.Add(frameSim.ToString("0.0", CultureInfo.InvariantCulture) + "s tot=" + ((t3 - t0) * toMs + bm).ToString("0.0", CultureInfo.InvariantCulture) + " strm=" + ((t1 - t0) * toMs).ToString("0.0", CultureInfo.InvariantCulture) + " phys=" + ((t2 - t1) * toMs).ToString("0.0", CultureInfo.InvariantCulture) + " destr=" + ((t3 - t2) * toMs).ToString("0.0", CultureInfo.InvariantCulture) + " brain=" + bm.ToString("0.0", CultureInfo.InvariantCulture) + " build=" + host.LastBuildMs.ToString("0.0", CultureInfo.InvariantCulture) + " shift=" + builder.LastShiftMs.ToString("0.0", CultureInfo.InvariantCulture) + " cshift=" + builder.LastClusterShiftMs.ToString("0.0", CultureInfo.InvariantCulture) + " items=" + host.LastPlan.Load.Count + " gc0=" + GC.CollectionCount(0));
                        if (frameNo % 600 == 0) { proc.Refresh(); rssMaxKb = Math.Max(rssMaxKb, proc.WorkingSet64 / 1024); }
                    }
                                }
            }

            hr.AfterStep = (h, step, now) =>
            {
                double brainMs = gap.Elapsed.TotalMilliseconds; long brainAlloc = GC.GetAllocatedBytesForCurrentThread() - gapAlloc0;
                simNow = now + Step;
                Snapshot(h);
                for (int f = 0; f < FramesPerStep; f++) Frame(now + f * (Step / FramesPerStep), false, f, brainMs, brainAlloc);
                gap.Restart(); gapAlloc0 = GC.GetAllocatedBytesForCurrentThread();
            };


            // loading screen: bodies parked at their start, host ticks until the rings are resident and nothing is in flight (cold start, not part of the measured window)
            Snapshot(hr);
            var preSw = Stopwatch.StartNew(); int calm = 0; long preTicks = 0;
            while (calm < 60 && preSw.Elapsed.TotalSeconds < 1200.0)
            {
                Frame(0.0, true, 0, 0.0, 0);
                if (host.InFlight == 0 && host.LastPlan.Load.Count == 0 && builder.ColliderBakesPending == 0) calm++; else calm = 0;
                preTicks++; if (frameNo == 0 && preTicks % 8 == 0) Thread.Sleep(1);
            }
            double prerollS = preSw.Elapsed.TotalSeconds;
            wall.Restart(); gap.Restart(); gapAlloc0 = GC.GetAllocatedBytesForCurrentThread(); maxItem = 0;

            hr.Run();

            int gcDelta = GC.CollectionCount(0) - gc0;
            proc.Refresh(); rssMaxKb = Math.Max(rssMaxKb, proc.WorkingSet64 / 1024);
            int liveFrags = frags.Count;
            long loads = host.LoadsDispatched - loadsStart; int distinctSpecies = 0; { var seen = new HashSet<int>(); foreach (var b in bodies) if (b.Kind == 1) seen.Add(b.Species); distinctSpecies = seen.Count; }
            // teardown
            foreach (var b in bodies) if (b.Go != null) UnityEngine.Object.DestroyImmediate(b.Go);
            frags.Clear(); host.Dispose(); builder.ReleaseAll(); scenes.Dispose(); UnityEngine.Object.DestroyImmediate(parent);

            double events_per_min = events * 60.0 / o.MeasureS;
            bool rubble = !o.Destruction || (spawned > 0 && liveFrags > 0);
            var ic = CultureInfo.InvariantCulture; var s = new StringBuilder(1 << 20);
            string scen = o.Scenario == "bay" ? "m008_bay@v1" : o.Scenario == "ref" ? "m008_ref@v1" : "m008_sweep@v1";
            s.Append("{\"format\": \"m008_run\", \"schema\": 1, \"scenario\": \"").Append(scen).Append("\", \"label\": \"").Append(label + "_seed" + o.Seed).Append("\", \"seed\": ").Append(o.Seed);
            s.Append(", \"platform\": \"").Append(SystemInfo.operatingSystem.Replace("\"", "'")).Append(" | ").Append(SystemInfo.processorType.Replace("\"", "'")).Append(" x").Append(SystemInfo.processorCount).Append(" | batch, no rendering, realtime=").Append(o.Realtime ? "1" : "0").Append("\"");
            s.Append(", \"rendering\": false, \"tiles\": \"real\", \"streaming_on\": true, \"frame_hz\": 60, \"warmup_s\": ").Append(o.WarmupS.ToString("R", ic)).Append(", \"measure_s\": ").Append(o.MeasureS.ToString("R", ic));
            s.Append(", \"bodies\": {\"mechs\": ").Append(o.Mechs).Append(", \"kaiju\": ").Append(o.Kaiju).Append(", \"species\": ").Append(distinctSpecies).Append(", \"mech_body\": \"placeholder kinematic box (Units.Placeholder pending T-005)\", \"kaiju_body\": \"placeholder kinematic box (T-117 pending)\"}");
            s.Append(", \"destruction\": {\"fragments_peak\": ").Append(peakFrags).Append(", \"events\": ").Append(events).Append(", \"events_per_min\": ").Append(events_per_min.ToString("0.###", ic));
            s.Append(", \"rubble_persisting\": ").Append(rubble ? "true" : "false").Append(", \"fragments_spawned\": ").Append(spawned).Append(", \"fragments_live_end\": ").Append(liveFrags).Append(", \"fragments_evicted_with_cluster\": ").Append(evictedFrags).Append(", \"deferred_events\": ").Append(deferredEvents);
            s.Append(", \"spawn_ms_per_piece\": ").Append((spawnPieces > 0 ? spawnMsSum / spawnPieces : 0.0).ToString("0.####", ic)).Append("}");
            s.Append(", \"phases\": [\"approach\", \"engaged\"], \"frames\": {");
            Series(s, "phase", phase, ic); s.Append(", "); Series(s, "total_ms", total, ic); s.Append(", "); Series(s, "physics_ms", phys, ic); s.Append(", "); Series(s, "destruction_ms", destr, ic); s.Append(", ");
            Series(s, "brain_ms", brainL, ic); s.Append(", "); Series(s, "streaming_ms", strm, ic); s.Append(", "); Series(s, "alloc_bytes", alloc, ic); s.Append(", \"render_ms\": null}");
            s.Append(", \"streaming\": {\"late_tile_frames\": ").Append(lateFrames).Append(", \"physics_holds\": ").Append(holds).Append(", \"tile_loads\": ").Append(loads).Append(", \"max_work_item_ms\": ").Append(maxItem.ToString("0.###", ic)).Append(", \"frames_without_ground\": ").Append(noGround).Append(", \"holds_by_kind_mech_kaiju_anchor\": [").Append(holdByKind[0]).Append(',').Append(holdByKind[1]).Append(',').Append(holdByKind[2]).Append("], \"hold_diag\": \"").Append(string.Join(" || ", holdDiag)).Append("\", \"hold_starts\": \"").Append(string.Join("; ", holdLog)).Append("\", \"item_profile\": {\"max_item_ms\": ").Append(host.MaxItemMs.ToString("0.###", ic)).Append(", \"max_item_key\": \"").Append(host.MaxItemKey).Append("\", \"max_item_ms_physics\": ").Append(host.MaxItemMsPhysics.ToString("0.###", ic)).Append(", \"max_item_ms_render\": ").Append(host.MaxItemMsRender.ToString("0.###", ic)).Append(", \"items\": ").Append(host.ItemCount).Append(", \"max_items_per_pump\": ").Append(host.MaxItemsPerPump).Append(", \"hitches\": \"").Append(string.Join(" || ", hitchLog)).Append("\"}, \"prefetch_m\": ").Append(o.PhysicsPrefetchM.ToString("0.#", ic)).Append(", \"slice_render\": ").Append(o.SliceRender ? 1 : 0).Append(", \"max_stage_ms\": ").Append(builder.MaxStageMs.ToString("0.###", ic)).Append(", \"urgent_slice_ms\": ").Append(o.UrgentSliceMs.ToString("0.#", ic)).Append(", \"host_failures\": ").Append(host.Failures).Append(", \"host_dropped\": ").Append(host.Dropped).Append(", \"first_hold_sim_s\": ").Append(firstHoldS.ToString("0.##", ic)).Append(", \"preroll_s\": ").Append(prerollS.ToString("0.#", ic)).Append(", \"preroll_ticks\": ").Append(preTicks).Append("}");
            s.Append(", \"gc_collections\": ").Append(gcDelta).Append(", \"peak_rss_mb\": ").Append((rssMaxKb / 1024.0).ToString("0.#", ic));
            s.Append(", \"sweep\": ").Append(o.Scenario == "sweep" ? "{\"kaiju\": " + o.Kaiju + ", \"mechs\": " + o.Mechs + ", \"fragments_target\": " + o.FragmentsTarget + "}" : "null").Append(", \"engaged\": ").Append(engaged ? "true" : "false").Append("}\n");
            return s.ToString();
        }

        static void Series(StringBuilder s, string name, List<float> v, CultureInfo ic)
        {
            s.Append('"').Append(name).Append("\": [");
            for (int i = 0; i < v.Count; i++) { if (i > 0) s.Append(','); s.Append(v[i].ToString("0.###", ic)); }
            s.Append(']');
        }
        static void Series(StringBuilder s, string name, List<int> v, CultureInfo ic)
        {
            s.Append('"').Append(name).Append("\": [");
            for (int i = 0; i < v.Count; i++) { if (i > 0) s.Append(','); s.Append(v[i]); }
            s.Append(']');
        }
        static void Series(StringBuilder s, string name, List<long> v, CultureInfo ic)
        {
            s.Append('"').Append(name).Append("\": [");
            for (int i = 0; i < v.Count; i++) { if (i > 0) s.Append(','); s.Append(v[i]); }
            s.Append(']');
        }
    }
}
