// Flythrough harness (T-111, R-101 sections 6 and 9): drives the real scheduler and host core along a scripted 75+ km Bay route with a camera,
// mechs, kaiju and debris, and measures M-015@v2 clauses (a)..(e) against the authoritative float64 state. Engine-free: the "world" behind it is
// an IFlythroughWorld (LogicFlythroughWorld here; the Unity adapter backs it with one PhysicsScene per cluster and real colliders).
// Simulated time is not wall time (R-101 O8): the step is 1 s of simulated time and nothing renders. Output: JSON `m015_dump@v2`, read by
// Tools/Metrics `m015` (never by hand).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using Game.World.Geo;

namespace Game.World.Streaming.Host
{
    /// <summary>Synthetic Bay route and surface: BayTM polyline Golden Gate -> SF bayside -> East Bay -> San Jose (about 80 km), analytic terrain.</summary>
    public static class SyntheticBay
    {
        static readonly double[] Px = { 22000, 40000, 63000, 71000, 73000 };
        static readonly double[] Pz = { 101000, 89000, 61000, 41000, 28000 };
        static readonly double[] Cum = BuildCum();
        static double[] BuildCum()
        {
            var c = new double[Px.Length];
            for (int i = 1; i < c.Length; i++) c[i] = c[i - 1] + Math.Sqrt((Px[i] - Px[i - 1]) * (Px[i] - Px[i - 1]) + (Pz[i] - Pz[i - 1]) * (Pz[i] - Pz[i - 1]));
            return c;
        }
        public static double RouteLengthM => Cum[Cum.Length - 1];

        /// <summary>Position at arc length s (clamped to the ends) and the unit heading there.</summary>
        public static void At(double s, out double x, out double z, out double hx, out double hz)
        {
            int last = Cum.Length - 1;
            double sc = Math.Max(0, Math.Min(s, Cum[last]));
            int i = 1; while (i < last && Cum[i] < sc) i++;
            double t = (sc - Cum[i - 1]) / (Cum[i] - Cum[i - 1]);
            x = Px[i - 1] + t * (Px[i] - Px[i - 1]); z = Pz[i - 1] + t * (Pz[i] - Pz[i - 1]);
            double l = Math.Sqrt((Px[i] - Px[i - 1]) * (Px[i] - Px[i - 1]) + (Pz[i] - Pz[i - 1]) * (Pz[i] - Pz[i - 1]));
            hx = (Px[i] - Px[i - 1]) / l; hz = (Pz[i] - Pz[i - 1]) / l;
        }

        /// <summary>Data surface, NAVD88 m: smooth, 3 to 17 m, a pure function of BayTM x and z.</summary>
        public static double Height(double x, double z) => 10.0 + 5.0 * Math.Sin(x / 700.0) * Math.Cos(z / 900.0) + 2.0 * Math.Sin((x + z) / 211.0);
    }

    /// <summary>Builds tile payloads from SyntheticBay.Height: Render = 33 x 33 grid over the tile; Physics = 17 x 17 grid over the 256 m chunk (16 m cells, below 3 mm interpolation error). Positions are tile-local.</summary>
    public sealed class SyntheticTileReader : ITileReader
    {
        public TilePayload Read(ResidencyKey key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            key.Bounds(out double x0, out double z0, out double x1, out double z1);
            int cells = key.Kind == ResidencyKind.Render ? 32 : 16;
            double step = (x1 - x0) / cells;
            int n = cells + 1;
            var pos = new float[n * n * 3]; var nrm = new float[n * n * 3];
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    double wx = x0 + i * step, wz = z0 + j * step;
                    int v = (j * n + i) * 3;
                    pos[v] = (float)(wx - key.Tile.OriginX); pos[v + 1] = (float)SyntheticBay.Height(wx, wz); pos[v + 2] = (float)(wz - key.Tile.OriginZ);
                    nrm[v + 1] = 1f;
                }
            var idx = new int[cells * cells * 6]; int t = 0;
            for (int j = 0; j < cells; j++)
                for (int i = 0; i < cells; i++)
                {
                    int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                    idx[t++] = a; idx[t++] = c; idx[t++] = b; idx[t++] = b; idx[t++] = c; idx[t++] = d;
                }
            var p = new TilePayload { Key = key };
            p.Meshes.Add(new GeoMeshData { Layer = "terrain", Name = "synthetic_" + key, Material = "terrain", Positions = pos, Normals = nrm, Indices = idx, ChunkX = key.ChunkX, ChunkZ = key.ChunkZ });
            p.GpuBytes = key.Kind == ResidencyKind.Render ? n * n * 32L + idx.Length * 4L : 0;
            p.CpuBytes = n * n * 32L + idx.Length * 4L;
            return p;
        }
    }

    public enum FlythroughRole : byte { Camera = 0, Mech = 1, Kaiju = 2, Debris = 3 }

    /// <summary>What the harness needs from the physics side. Every call is made on the main thread.</summary>
    public interface IFlythroughWorld
    {
        /// <summary>Puts the body at a cluster-local float32 position (creating it, or moving it to another cluster's frame).</summary>
        void Place(int focusId, int clusterId, float x, float y, float z);
        /// <summary>Advances the physics of every cluster by dt (a non-default PhysicsScene is stepped by hand).</summary>
        void Step(double dt);
        /// <summary>What the engine holds for the body now (float32, cluster-local), not what the harness placed.</summary>
        bool TryRead(int focusId, out int clusterId, out float x, out float y, out float z);
        /// <summary>Height of the data surface under a cluster-local point as the cluster's colliders report it (NaN when no collider is there).</summary>
        double GroundHeightLocal(int clusterId, float x, float z);
        /// <summary>Cluster-local origin (tile corner) the world uses for the cluster; the harness checks it equals the plan's.</summary>
        bool TryGetClusterOrigin(int clusterId, out double ox, out double oz);
    }

    public sealed class FlythroughScenario
    {
        public double RouteLengthM = SyntheticBay.RouteLengthM;
        public double StepS = 1.0;
        /// <summary>Lead mech (and the camera and debris that ride with it).</summary>
        public double MechSpeedMps = 30.0;
        public double SecondMechSpeedMps = 22.0;
        public double KaijuSpeedMps = 9.12;
        public int Mechs = 2, Kaiju = 3, Debris = 100;
        /// <summary>Ticks with every body parked at its start before measuring: the physics ring is requested ahead of the first step.</summary>
        public int WarmUpTicks = 12;
        /// <summary>Extra zero-time host ticks after each simulated step. One step is 1 s of simulated time = about 60 frames of reading and building; the host moves at most 4 reads per tick, so the drain stands for those frames (the bodies do not move during them).</summary>
        public int DrainTicks = 10;
        public int MaxSteps = 40000;
        /// <summary>Per tick after a step: lets asynchronous builders finish (Unity); the logic world needs none.</summary>
        public Action AfterTick;
        public string Label = "logic";
    }

    public sealed class FlythroughReport
    {
        public string Mode, Label;
        public double PathLengthM, SimulatedS, StepS, MechSpeedMps;
        public int Steps, Mechs, Kaiju, Debris, Bodies;
        public double MaxAbsLocalM, MaxReconstructionErrM, MaxPairChangeM, MaxRebasePairChangeM, MaxTerrainDeltaM, MaxRebaseTerrainDeltaM, RoundTripErrM, ProbeMaxErrM, PlacedVsReadMaxM;
        public int Rebases, ClusterShifts, ClustersMax, HoldEvents, FramesBelowSurface, FramesWithoutGround, TileLoads, MaxLoadsPerKey, UnclaimedChunks, WorldOriginMismatches;
        public int ProbeSteps;

        public string ToJson()
        {
            var ic = CultureInfo.InvariantCulture;
            string D(double v) => double.IsNaN(v) || double.IsInfinity(v) ? "null" : v.ToString("R", ic);
            var sb = new StringBuilder();
            sb.Append("{\n \"format\": \"m015_dump\",\n \"schema\": 2,\n");
            sb.Append(" \"mode\": \"").Append(Mode).Append("\",\n \"label\": \"").Append(Label).Append("\",\n");
            sb.Append(" \"path_length_m\": ").Append(D(PathLengthM)).Append(",\n \"simulated_s\": ").Append(D(SimulatedS)).Append(",\n \"step_s\": ").Append(D(StepS)).Append(",\n");
            sb.Append(" \"mech_speed_mps\": ").Append(D(MechSpeedMps)).Append(",\n \"steps\": ").Append(Steps).Append(",\n");
            sb.Append(" \"bodies\": {\"mechs\": ").Append(Mechs).Append(", \"kaiju\": ").Append(Kaiju).Append(", \"debris\": ").Append(Debris).Append(", \"total\": ").Append(Bodies).Append("},\n");
            sb.Append(" \"max_abs_local_m\": ").Append(D(MaxAbsLocalM)).Append(",\n");
            sb.Append(" \"max_reconstruction_err_m\": ").Append(D(MaxReconstructionErrM)).Append(",\n");
            sb.Append(" \"round_trip_err_m\": ").Append(D(RoundTripErrM)).Append(",\n \"probe_max_err_m\": ").Append(D(ProbeMaxErrM)).Append(",\n \"probe_steps\": ").Append(ProbeSteps).Append(",\n");
            sb.Append(" \"pair_change_max_m\": ").Append(D(MaxPairChangeM)).Append(",\n \"rebase_pair_change_max_m\": ").Append(D(MaxRebasePairChangeM)).Append(",\n");
            sb.Append(" \"terrain_delta_max_m\": ").Append(D(MaxTerrainDeltaM)).Append(",\n \"rebase_terrain_delta_max_m\": ").Append(D(MaxRebaseTerrainDeltaM)).Append(",\n");
            sb.Append(" \"placed_vs_read_max_m\": ").Append(D(PlacedVsReadMaxM)).Append(",\n");
            sb.Append(" \"rebases\": ").Append(Rebases).Append(",\n \"cluster_shifts\": ").Append(ClusterShifts).Append(",\n \"clusters_max\": ").Append(ClustersMax).Append(",\n");
            sb.Append(" \"hold_events\": ").Append(HoldEvents).Append(",\n \"frames_below_surface\": ").Append(FramesBelowSurface).Append(",\n \"frames_without_ground\": ").Append(FramesWithoutGround).Append(",\n");
            sb.Append(" \"world_origin_mismatches\": ").Append(WorldOriginMismatches).Append(",\n \"unclaimed_chunks\": ").Append(UnclaimedChunks).Append(",\n");
            sb.Append(" \"tile_loads\": ").Append(TileLoads).Append(",\n \"max_loads_per_key\": ").Append(MaxLoadsPerKey).Append("\n}\n");
            return sb.ToString();
        }
    }

    /// <summary>Runs one traversal. The host core, scheduler and world are the caller's (tests use the logic world, Unity the PhysicsScene world).</summary>
    public static class FlythroughHarness
    {
        sealed class Body
        {
            public int Id; public FlythroughRole Role; public double Speed, S0, Ox, Oz, Dy; public bool NeedsPhysics;
        }

        public static FlythroughReport Run(FlythroughScenario sc, StreamingHostCore host, IFlythroughWorld world, string mode)
        {
            var bodies = new List<Body>();
            bodies.Add(new Body { Id = 1, Role = FlythroughRole.Camera, Speed = sc.MechSpeedMps, S0 = 0, Dy = 60 });
            for (int m = 0; m < sc.Mechs; m++) bodies.Add(new Body { Id = 10 + m, Role = FlythroughRole.Mech, Speed = m == 0 ? sc.MechSpeedMps : sc.SecondMechSpeedMps, S0 = m == 0 ? 0 : 300 * m, Dy = 8, NeedsPhysics = true });
            for (int k = 0; k < sc.Kaiju; k++) bodies.Add(new Body { Id = 20 + k, Role = FlythroughRole.Kaiju, Speed = sc.KaijuSpeedMps, S0 = 400 * k, Dy = 8, NeedsPhysics = true });
            uint rng = 12345;
            double Rnd() { rng = rng * 1664525u + 1013904223u; return ((rng >> 8) / 16777216.0) * 2.0 - 1.0; }
            for (int d = 0; d < sc.Debris; d++) bodies.Add(new Body { Id = 100 + d, Role = FlythroughRole.Debris, Speed = sc.MechSpeedMps, S0 = 0, Ox = Rnd() * 150, Oz = Rnd() * 150, Dy = 5 + 1.5 * Rnd() });

            var rep = new FlythroughReport { Mode = mode, Label = sc.Label, PathLengthM = sc.RouteLengthM, StepS = sc.StepS, MechSpeedMps = sc.MechSpeedMps, Mechs = sc.Mechs, Kaiju = sc.Kaiju, Debris = sc.Debris, Bodies = bodies.Count };
            var foci = new FocusSample[bodies.Count];
            var wx = new double[bodies.Count]; var wy = new double[bodies.Count]; var wz = new double[bodies.Count];
            var cid = new int[bodies.Count]; var lx = new float[bodies.Count]; var ly = new float[bodies.Count]; var lz = new float[bodies.Count];
            var prevCluster = new Dictionary<int, int>();
            var loads = new Dictionary<ResidencyKey, int>();
            var seenLoad = new HashSet<ResidencyKey>();
            double L = sc.RouteLengthM;
            bool measuring = false;
            double t = 0;
            int tick = 0;
            bool anyRemaining = true;

            void Fill(double time)
            {
                for (int i = 0; i < bodies.Count; i++)
                {
                    var b = bodies[i];
                    bool rides = b.Role == FlythroughRole.Camera || b.Role == FlythroughRole.Debris;
                    double s = (rides ? bodies[1].Speed : b.Speed) * time + (rides ? bodies[1].S0 : b.S0);
                    if (!rides && s < L + 1) anyRemaining = true;
                    SyntheticBay.At(s, out double x, out double z, out double hx, out double hz);
                    double speed = s >= L ? 0 : (rides ? bodies[1].Speed : b.Speed);
                    if (b.Role == FlythroughRole.Camera) { x -= hx * 250.0; z -= hz * 250.0; }
                    x += b.Ox; z += b.Oz;
                    double y = b.Role == FlythroughRole.Camera ? SyntheticBay.Height(x, z) + b.Dy : SyntheticBay.Height(x, z) + b.Dy;
                    wx[i] = x; wy[i] = y; wz[i] = z;
                    foci[i] = new FocusSample { Id = b.Id, Kind = b.Role == FlythroughRole.Camera ? FocusKind.Camera : FocusKind.Body, X = x, Y = y, Z = z, Vx = (float)(hx * speed), Vz = (float)(hz * speed), NeedsPhysics = b.NeedsPhysics, ReachM = 100f };
                }
            }

            // pre-roll: bodies parked at their start so the first physics ring exists before the first measured step
            for (; tick < sc.WarmUpTicks; tick++)
            {
                Fill(0); for (int i = 0; i < foci.Length; i++) { foci[i].Vx = 0; foci[i].Vz = 0; }
                host.Tick(sc.StepS, foci);
                PlaceAll(host, world, foci, wx, wy, wz, cid, lx, ly, lz);
                sc.AfterTick?.Invoke();
            }
            measuring = true;
            int step = 0;
            var rebasedClusters = new HashSet<int>();
            while (true)
            {
                anyRemaining = false;
                Fill(t);
                if (!anyRemaining) break;
                if (++step > sc.MaxSteps) throw new InvalidOperationException("traversal did not finish in " + sc.MaxSteps + " steps");
                host.Tick(sc.StepS, foci);
                sc.AfterTick?.Invoke();
                rebasedClusters.Clear();
                var plan = host.LastPlan;
                foreach (var c in plan.Clusters) if (c.Rebased) { rep.Rebases++; rebasedClusters.Add(c.ClusterId); }
                rep.ClustersMax = Math.Max(rep.ClustersMax, plan.Clusters.Count);
                foreach (var r in plan.Load) { loads[r.Key] = (loads.TryGetValue(r.Key, out int n) ? n : 0) + 1; }
                for (int dtk = 0; dtk < sc.DrainTicks; dtk++)
                {
                    host.Tick(0.0, foci); sc.AfterTick?.Invoke();
                    foreach (var r in host.LastPlan.Load) { loads[r.Key] = (loads.TryGetValue(r.Key, out int n2) ? n2 : 0) + 1; }
                }
                plan = host.LastPlan;

                PlaceAll(host, world, foci, wx, wy, wz, cid, lx, ly, lz);
                world.Step(sc.StepS);
                Measure(rep, plan, bodies, foci, wx, wy, wz, cid, world, host, rebasedClusters);
                t += sc.StepS;
            }
            rep.Steps = step; rep.SimulatedS = t;
            rep.TileLoads = loads.Count;
            foreach (var kv in loads) rep.MaxLoadsPerKey = Math.Max(rep.MaxLoadsPerKey, kv.Value);
            rep.ClusterShifts = host.ClusterShifts; rep.UnclaimedChunks = host.UnclaimedChunks;
            RoundTrip(sc, host, world, rep);
            return rep;
        }

        static void PlaceAll(StreamingHostCore host, IFlythroughWorld world, FocusSample[] foci, double[] wx, double[] wy, double[] wz, int[] cid, float[] lx, float[] ly, float[] lz)
        {
            var plan = host.LastPlan;
            for (int a = 0; a < plan.Assignments.Count; a++)
            {
                var asg = plan.Assignments[a];
                int i = -1; for (int k = 0; k < foci.Length; k++) if (foci[k].Id == asg.FocusId) { i = k; break; }
                if (i < 0) continue;
                cid[i] = asg.ClusterId; lx[i] = asg.LocalX; ly[i] = asg.LocalY; lz[i] = asg.LocalZ;
                // a held body is not moved by its owner: it stays where it was (kinematic freeze); the harness still reports it as a hold event
                if (!host.IsBodyHeld(asg.FocusId) || !foci[i].NeedsPhysics) world.Place(asg.FocusId, asg.ClusterId, asg.LocalX, asg.LocalY, asg.LocalZ);
            }
        }

        static void Measure(FlythroughReport rep, StreamingPlan plan, List<Body> bodies, FocusSample[] foci, double[] wx, double[] wy, double[] wz, int[] cid, IFlythroughWorld world, StreamingHostCore host, HashSet<int> rebased)
        {
            var origins = new Dictionary<int, (double x, double z)>();
            foreach (var c in plan.Clusters)
            {
                origins[c.ClusterId] = (c.OriginX, c.OriginZ);
                if (world.TryGetClusterOrigin(c.ClusterId, out double wox, out double woz) && (wox != c.OriginX || woz != c.OriginZ)) rep.WorldOriginMismatches++;
            }
            // anchor of each cluster: its lowest-id member (pairwise distances are measured against it)
            var anchor = new Dictionary<int, int>();
            var read = new (bool ok, int cluster, float x, float y, float z)[foci.Length];
            for (int i = 0; i < foci.Length; i++)
            {
                bool ok = world.TryRead(foci[i].Id, out int rc, out float x, out float y, out float z);
                read[i] = (ok, rc, x, y, z);
                if (!ok) { rep.FramesWithoutGround++; continue; }
                if (!origins.TryGetValue(rc, out var o)) continue;
                double dx = o.x + (double)x - wx[i], dz = o.z + (double)z - wz[i], dy = (double)y - wy[i];
                rep.MaxAbsLocalM = Math.Max(rep.MaxAbsLocalM, Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z))));
                rep.MaxReconstructionErrM = Math.Max(rep.MaxReconstructionErrM, Math.Max(Math.Abs(dx), Math.Max(Math.Abs(dy), Math.Abs(dz))));
                if (!anchor.ContainsKey(rc) || foci[i].Id < foci[anchor[rc]].Id) anchor[rc] = i;
            }
            for (int i = 0; i < foci.Length; i++)
            {
                if (!read[i].ok) continue;
                int rc = read[i].cluster;
                if (!anchor.TryGetValue(rc, out int ai) || ai == i) continue;
                double dl = Dist3(read[i].x - (double)read[ai].x, read[i].y - (double)read[ai].y, read[i].z - (double)read[ai].z);
                double dw = Dist3(wx[i] - wx[ai], wy[i] - wy[ai], wz[i] - wz[ai]);
                double e = Math.Abs(dl - dw);
                rep.MaxPairChangeM = Math.Max(rep.MaxPairChangeM, e);
                if (rebased.Contains(rc)) rep.MaxRebasePairChangeM = Math.Max(rep.MaxRebasePairChangeM, e);
            }
            for (int i = 0; i < foci.Length; i++)
            {
                if (!read[i].ok || !bodies[i].NeedsPhysics) continue;
                if (host.IsBodyHeld(foci[i].Id)) { rep.HoldEvents++; continue; }
                var r = read[i];
                double g = world.GroundHeightLocal(r.cluster, r.x, r.z);
                if (double.IsNaN(g)) { rep.FramesWithoutGround++; continue; }
                double dh = Math.Abs(g - SyntheticBay.Height(wx[i], wz[i]));
                rep.MaxTerrainDeltaM = Math.Max(rep.MaxTerrainDeltaM, dh);
                if (rebased.Contains(r.cluster)) rep.MaxRebaseTerrainDeltaM = Math.Max(rep.MaxRebaseTerrainDeltaM, dh);
                if ((double)r.y < g - 0.5) rep.FramesBelowSurface++;
            }
        }

        static double Dist3(double x, double y, double z) => Math.Sqrt(x * x + y * y + z * z);

        /// <summary>(c): a probe driven out along the route at the speed cap (60 m/s) and back returns to its start; max error at every step is the (b) reconstruction of the probe.</summary>
        static void RoundTrip(FlythroughScenario sc, StreamingHostCore host, IFlythroughWorld world, FlythroughReport rep)
        {
            double L = sc.RouteLengthM;
            int half = (int)Math.Ceiling(L / 60.0), n = 2 * half;
            double ds = L / half;
            double startX = 0, startZ = 0, maxErr = 0, endErr = 0;
            var f = new FocusSample[1];
            for (int i = 0; i <= n; i++)
            {
                double s = i <= half ? i * ds : (n - i) * ds;
                SyntheticBay.At(Math.Max(0, s), out double x, out double z, out double hx, out double hz);
                if (i == 0) { startX = x; startZ = z; }
                f[0] = new FocusSample { Id = 5, Kind = FocusKind.Body, X = x, Y = 12.345, Z = z, NeedsPhysics = false, ReachM = 100f };
                host.Tick(1.0, f);
                sc.AfterTick?.Invoke();
                var a = host.LastPlan.Assignments[0]; var c = host.LastPlan.Clusters[0];
                world.Place(5, a.ClusterId, a.LocalX, a.LocalY, a.LocalZ);
                if (!world.TryRead(5, out int rc, out float px, out float py, out float pz)) throw new InvalidOperationException("probe not readable");
                double ex = c.OriginX + (double)px - x, ez = c.OriginZ + (double)pz - z;
                maxErr = Math.Max(maxErr, Math.Max(Math.Abs(ex), Math.Abs(ez)));
                if (i == n) endErr = Math.Max(Math.Abs(c.OriginX + (double)px - startX), Math.Abs(c.OriginZ + (double)pz - startZ));
                rep.MaxAbsLocalM = Math.Max(rep.MaxAbsLocalM, Math.Max(Math.Abs(px), Math.Max(Math.Abs(py), Math.Abs(pz))));
            }
            rep.ProbeMaxErrM = maxErr; rep.RoundTripErrM = endErr; rep.ProbeSteps = n + 1;
        }
    }

    /// <summary>Builder + world of the engine-free harness: physics chunk nodes live in their cluster's frame (integer offsets), ground height comes from the chunk's own payload.</summary>
    public sealed class LogicFlythroughWorld : ITileBuilder, IClusterBuilder, IFlythroughWorld
    {
        sealed class Chunk { public int Cluster; public float[] Heights; public TilePayload Payload; }
        readonly Dictionary<ResidencyKey, Chunk> _chunks = new Dictionary<ResidencyKey, Chunk>();
        readonly Dictionary<ResidencyKey, int> _assigned = new Dictionary<ResidencyKey, int>();
        readonly Dictionary<int, (double x, double z)> _origin = new Dictionary<int, (double, double)>();
        readonly Dictionary<int, (int cluster, float x, float y, float z)> _bodies = new Dictionary<int, (int, float, float, float)>();
        public int Released { get; private set; }
        public int ChunkCount => _chunks.Count;
        /// <summary>Render keys the builder holds (kept for symmetry with Unity; the logic world has no render side).</summary>
        public readonly HashSet<ResidencyKey> RenderKeys = new HashSet<ResidencyKey>();

        public void AssignCluster(in ResidencyKey key, int clusterId) { _assigned[key] = clusterId; if (_chunks.TryGetValue(key, out var c)) c.Cluster = clusterId; }
        public void ShiftClusterOrigin(int clusterId, double ox, double oz) { _origin[clusterId] = (ox, oz); }
        public void RemoveCluster(int clusterId) { _origin.Remove(clusterId); }

        public void Build(in ResidencyKey key, TilePayload payload, IBuildSink sink)
        {
            if (key.Kind == ResidencyKind.Render) { RenderKeys.Add(key); sink.Built(key, payload.GpuBytes, payload.CpuBytes); return; }
            _assigned.TryGetValue(key, out int owner);
            if (!_assigned.ContainsKey(key)) owner = -1;
            _chunks[key] = new Chunk { Cluster = owner, Payload = payload };
            sink.Built(key, payload.GpuBytes, payload.CpuBytes);
        }
        public void Release(in ResidencyKey key) { if (_chunks.Remove(key) | RenderKeys.Remove(key)) Released++; _assigned.Remove(key); }
        public void ShiftOrigin(double originX, double originZ) { }

        public void Place(int focusId, int clusterId, float x, float y, float z) { _bodies[focusId] = (clusterId, x, y, z); }
        public void Step(double dt) { }
        public bool TryRead(int focusId, out int clusterId, out float x, out float y, out float z)
        {
            if (_bodies.TryGetValue(focusId, out var b)) { clusterId = b.cluster; x = b.x; y = b.y; z = b.z; return true; }
            clusterId = -1; x = y = z = 0; return false;
        }
        public bool TryGetClusterOrigin(int clusterId, out double ox, out double oz)
        {
            if (_origin.TryGetValue(clusterId, out var o)) { ox = o.x; oz = o.z; return true; }
            ox = oz = 0; return false;
        }

        public double GroundHeightLocal(int clusterId, float x, float z)
        {
            if (!_origin.TryGetValue(clusterId, out var o)) return double.NaN;
            double wx = o.x + x, wz = o.z + z;
            var tile = GeoTileId.Containing(wx, wz, 0);
            int cx = (int)Math.Floor((wx - tile.OriginX) / ResidencyKey.ChunkSizeM), cz = (int)Math.Floor((wz - tile.OriginZ) / ResidencyKey.ChunkSizeM);
            var key = ResidencyKey.PhysicsChunk(tile, Math.Min(7, cx), Math.Min(7, cz));
            if (!_chunks.TryGetValue(key, out var ch) || ch.Cluster != clusterId) return double.NaN;
            // bilinear on the chunk's own 17 x 17 payload: the surface the colliders really present
            var m = ch.Payload.Meshes[0];
            key.Bounds(out double x0, out double z0, out _, out _);
            double u = (wx - x0) / 16.0, v = (wz - z0) / 16.0;
            int i = Math.Max(0, Math.Min(15, (int)Math.Floor(u))), j = Math.Max(0, Math.Min(15, (int)Math.Floor(v)));
            double fu = u - i, fv = v - j;
            double h00 = m.Positions[(j * 17 + i) * 3 + 1], h10 = m.Positions[(j * 17 + i + 1) * 3 + 1], h01 = m.Positions[((j + 1) * 17 + i) * 3 + 1], h11 = m.Positions[((j + 1) * 17 + i + 1) * 3 + 1];
            return (h00 * (1 - fu) + h10 * fu) * (1 - fv) + (h01 * (1 - fu) + h11 * fu) * fv;
        }
    }
}
