// M-015@v2, logic part (R-101 section 6): a synthetic 75+ km Golden Gate -> San Jose traversal with a camera, 2 mechs, 3 kaiju and 100 debris bodies.
// For every focus at every fixed step: the cluster-local position stays within 4096 m and reconstructs the float64 world position within 1 mm;
// the camera/mech distance across rebases changes by < 1 mm; a probe driven out and back returns within 1 mm; no tile toggles more than once per crossing.
// Simulated time may exceed wall time (O8): the step is 1 s of simulated time and nothing renders.
using System;
using System.Collections.Generic;
using NUnit.Framework;
using Game.World.Geo;

namespace Game.World.Streaming.Tests
{
    public class BayTraversalTests
    {
        // BayTM polyline: Golden Gate -> SF bayside -> Oakland / Fremont side -> San Jose (synthetic, float64 metres)
        static readonly double[,] Path =
        {
            { 22000, 101000 }, { 40000, 89000 }, { 63000, 61000 }, { 71000, 41000 }, { 73000, 28000 },
        };

        static double[] _cum;
        static double Length { get { Init(); return _cum[_cum.Length - 1]; } }
        static void Init()
        {
            if (_cum != null) return;
            _cum = new double[Path.GetLength(0)];
            for (int i = 1; i < _cum.Length; i++)
                _cum[i] = _cum[i - 1] + Math.Sqrt(Math.Pow(Path[i, 0] - Path[i - 1, 0], 2) + Math.Pow(Path[i, 1] - Path[i - 1, 1], 2));
        }
        static void At(double s, out double x, out double z)
        {
            Init();
            if (s <= 0) { x = Path[0, 0]; z = Path[0, 1]; return; }
            int last = _cum.Length - 1;
            if (s >= _cum[last]) { x = Path[last, 0]; z = Path[last, 1]; return; }
            int i = 1; while (_cum[i] < s) i++;
            double t = (s - _cum[i - 1]) / (_cum[i] - _cum[i - 1]);
            x = Path[i - 1, 0] + t * (Path[i, 0] - Path[i - 1, 0]); z = Path[i - 1, 1] + t * (Path[i, 1] - Path[i - 1, 1]);
        }

        sealed class Mover { public int Id; public FocusKind Kind; public double Speed, S0; public double Ox, Oz; public bool Debris; }

        static double Heading(double s, out double hx, out double hz)
        {
            At(s, out double x0, out double z0); At(s + 5, out double x1, out double z1);
            double l = Math.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0)); if (l < 1e-9) { hx = hz = 0; return 0; }
            hx = (x1 - x0) / l; hz = (z1 - z0) / l; return l;
        }

        [Test]
        public void PathIsAtLeast75Km() { Assert.GreaterOrEqual(Length, 75000.0); }

        [Test]
        public void Traversal_75km_2Mechs_3Kaiju_100Debris_EveryBodyWithin4096m_AndNoTileToggles()
        {
            var movers = new List<Mover>();
            movers.Add(new Mover { Id = 1, Kind = FocusKind.Camera, Speed = 30.0, S0 = 0, Ox = 0, Oz = 0 });           // follows mech 10 at an offset
            movers.Add(new Mover { Id = 10, Kind = FocusKind.Body, Speed = 30.0, S0 = 0 });                                // mech, design speed O1
            movers.Add(new Mover { Id = 11, Kind = FocusKind.Body, Speed = 22.0, S0 = 300 });                              // mech
            movers.Add(new Mover { Id = 20, Kind = FocusKind.Body, Speed = 9.12, S0 = 0 });                                // kaiju (9.12 m/s, R-101 section 6)
            movers.Add(new Mover { Id = 21, Kind = FocusKind.Body, Speed = 9.12, S0 = 400 });
            movers.Add(new Mover { Id = 22, Kind = FocusKind.Body, Speed = 9.12, S0 = 800 });
            uint rng = 12345;
            Func<double> rnd = () => { rng = rng * 1664525u + 1013904223u; return ((rng >> 8) / 16777216.0) * 2.0 - 1.0; };
            for (int i = 0; i < 100; i++) movers.Add(new Mover { Id = 100 + i, Kind = FocusKind.Body, Speed = 30.0, S0 = 0, Ox = rnd() * 150, Oz = rnd() * 150, Debris = true });   // travel with mech 10

            double L = Length;
            var cfg = new StreamingConfig();
            var h = new Harness(cfg);
            var foci = new FocusSample[movers.Count];
            var world = new double[movers.Count, 3];
            var near = new Dictionary<ResidencyKey, bool>();
            var visits = new Dictionary<ResidencyKey, int>();
            double maxLocal = 0, maxRecon = 0, maxPair = 0;
            int steps = 0, maxClusters = 0, rebases = 0;
            double prevPair = double.NaN;
            bool anyRemaining = true;
            double t = 0;
            while (anyRemaining)
            {
                anyRemaining = false;
                for (int m = 0; m < movers.Count; m++)
                {
                    var mv = movers[m];
                    double s = mv.S0 + mv.Speed * t;
                    if (mv.Debris || mv.Id == 1) s = movers[1].S0 + movers[1].Speed * t;   // debris and the camera ride with mech 10
                    if (!mv.Debris && mv.Id != 1 && s < L + 1) anyRemaining = true;
                    double sc = Math.Min(s, L);
                    At(sc, out double x, out double z);
                    Heading(sc, out double hx, out double hz);
                    double speed = (mv.Debris || mv.Id == 1) ? movers[1].Speed : mv.Speed;
                    if (s >= L) speed = 0;
                    double off = mv.Id == 1 ? 250.0 : 0.0;
                    x += hx * (mv.Id == 1 ? -off : 0) + mv.Ox; z += hz * (mv.Id == 1 ? -off : 0) + mv.Oz;
                    foci[m] = new FocusSample
                    {
                        Id = mv.Id, Kind = mv.Kind, X = x, Z = z, Y = mv.Id == 1 ? 60.0 : 8.0 + (mv.Debris ? 3 * rnd() : 0),
                        Vx = (float)(hx * speed), Vz = (float)(hz * speed), NeedsPhysics = mv.Kind == FocusKind.Body && !mv.Debris, NeedsRender = false, ReachM = 100f,
                    };
                    world[m, 0] = foci[m].X; world[m, 1] = foci[m].Y; world[m, 2] = foci[m].Z;
                }
                var plan = h.Step(1.0, foci);
                steps++; t += 1.0;
                maxClusters = Math.Max(maxClusters, plan.Clusters.Count);
                foreach (var c in plan.Clusters) if (c.Rebased) rebases++;

                for (int a = 0; a < plan.Assignments.Count; a++)
                {
                    var asg = plan.Assignments[a];
                    ClusterInfo ci = default; bool found = false;
                    foreach (var c in plan.Clusters) if (c.ClusterId == asg.ClusterId) { ci = c; found = true; break; }
                    Assert.IsTrue(found);
                    int m = movers.FindIndex(q => q.Id == asg.FocusId);
                    double lm = Math.Max(Math.Abs(asg.LocalX), Math.Max(Math.Abs(asg.LocalY), Math.Abs(asg.LocalZ)));
                    maxLocal = Math.Max(maxLocal, lm);
                    double ex = Math.Abs(ci.OriginX + (double)asg.LocalX - world[m, 0]);
                    double ez = Math.Abs(ci.OriginZ + (double)asg.LocalZ - world[m, 2]);
                    double ey = Math.Abs((double)asg.LocalY - world[m, 1]);
                    maxRecon = Math.Max(maxRecon, Math.Max(ex, Math.Max(ey, ez)));
                    Assert.LessOrEqual(lm, 4096.0, "focus " + asg.FocusId + " at step " + steps);
                }
                // camera 1 vs mech 10 distance, from cluster-local coordinates, is the same in every cluster frame (a rebase changes nothing)
                if (TryLocal(plan, 1, out var c1) && TryLocal(plan, 10, out var m10) && c1.ClusterId == m10.ClusterId)
                {
                    double d = Math.Sqrt(Math.Pow(c1.LocalX - m10.LocalX, 2) + Math.Pow(c1.LocalY - m10.LocalY, 2) + Math.Pow(c1.LocalZ - m10.LocalZ, 2));
                    double dw = Math.Sqrt(Math.Pow(world[0, 0] - world[1, 0], 2) + Math.Pow(world[0, 1] - world[1, 1], 2) + Math.Pow(world[0, 2] - world[1, 2], 2));
                    maxPair = Math.Max(maxPair, Math.Abs(d - dw));
                    prevPair = d;
                }

                // visit accounting (per key: rising edge of "some focus is within the key's retention radius")
                foreach (var key in h.Loads.Keys)
                {
                    bool nb = false;
                    for (int m = 0; m < foci.Length && !nb; m++)
                    {
                        var f = foci[m];
                        if (key.Kind == ResidencyKind.Physics && (f.Kind != FocusKind.Body || !f.NeedsPhysics)) continue;
                        if (key.Kind == ResidencyKind.Render && f.Kind != FocusKind.Camera && !f.NeedsRender) continue;
                        double d = Harness.DistXZ(key, f.X, f.Z);
                        double speed = Math.Sqrt((double)f.Vx * f.Vx + (double)f.Vz * f.Vz);
                        if (key.Kind == ResidencyKind.Render) d = Math.Max(0, Math.Sqrt(d * d + f.Y * f.Y) - speed * cfg.PrefetchLeadS);
                        if (d <= h.Sched.RetentionRadiusM(key, f)) nb = true;
                    }
                    near.TryGetValue(key, out bool was);
                    if (nb && !was) visits[key] = (visits.TryGetValue(key, out int v) ? v : 0) + 1;
                    near[key] = nb;
                }
                Assert.Less(steps, 20000, "traversal did not finish");
            }

            Assert.GreaterOrEqual(steps, 8000, "the slowest kaiju needs more than 8000 s of simulated time for 75 km");
            Assert.LessOrEqual(maxLocal, 4096.0, "M-015@v2 (a)");
            Assert.LessOrEqual(maxRecon, 1e-3, "M-015@v2 (b): 1 mm");
            Assert.LessOrEqual(maxPair, 1e-3, "M-015@v2 (d): pairwise distance across rebases within 1 mm");
            Assert.Greater(rebases, 20, "the lead mech alone crosses > 30 tile corners");
            Assert.GreaterOrEqual(maxClusters, 2, "mechs and kaiju separate into several clusters");
            foreach (var kv in h.Loads)
            {
                visits.TryGetValue(kv.Key, out int v);
                Assert.LessOrEqual(kv.Value, Math.Max(1, v), kv.Key + " was loaded " + kv.Value + " times for " + v + " crossings");
            }
            Assert.AreEqual(0, h.Cost.Exists(GeoTileId.From(0, 0, 0)) ? 0 : 1);
            Assert.LessOrEqual(h.MaxGpu, cfg.Budget.GpuBytes);
            Assert.Greater(h.Log.OriginChanges, 20);
            TestContext.WriteLine("steps=" + steps + " maxLocal=" + maxLocal.ToString("F1") + " maxRecon_mm=" + (maxRecon * 1000).ToString("F4") + " maxPair_mm=" + (maxPair * 1000).ToString("F4")
                + " rebases=" + rebases + " maxClusters=" + maxClusters + " tiles=" + h.Loads.Count + " maxGpuMB=" + (h.MaxGpu >> 20));
        }

        static bool TryLocal(StreamingPlan plan, int id, out ClusterAssignment a)
        {
            foreach (var x in plan.Assignments) if (x.FocusId == id) { a = x; return true; }
            a = default; return false;
        }

        [Test]
        public void Probe_DrivenOutAndBack_ReturnsWithin1mm()
        {
            var h = new Harness();
            double L = Length;
            double maxErr = 0, endErr = 0;
            double startX = 0, startZ = 0;
            int half = (int)Math.Ceiling(L / 60.0), n = 2 * half;   // 60 m/s (the hard cap) out, same step size back: lands exactly on the start
            double ds = L / half;
            for (int i = 0; i <= n; i++)
            {
                double s = i <= half ? i * ds : (n - i) * ds;
                At(Math.Max(0, s), out double x, out double z);
                if (i == 0) { startX = x; startZ = z; }
                var f = new FocusSample { Id = 5, Kind = FocusKind.Body, X = x, Y = 12.345, Z = z, NeedsPhysics = true };
                var plan = h.Step(1.0, f);
                var a = plan.Assignments[0]; var c = plan.Clusters[0];
                double ex = c.OriginX + (double)a.LocalX - x, ez = c.OriginZ + (double)a.LocalZ - z;
                maxErr = Math.Max(maxErr, Math.Max(Math.Abs(ex), Math.Abs(ez)));
                if (i == n) endErr = Math.Max(Math.Abs(c.OriginX + (double)a.LocalX - startX), Math.Abs(c.OriginZ + (double)a.LocalZ - startZ));
                Assert.LessOrEqual(Math.Abs(a.LocalX), 4096.0); Assert.LessOrEqual(Math.Abs(a.LocalZ), 4096.0);
            }
            Assert.LessOrEqual(maxErr, 1e-3, "M-015@v2 (b)");
            Assert.LessOrEqual(endErr, 1e-3, "M-015@v2 (c): round trip");
        }

        [Test]
        public void LoneCameraTraversal_EveryTileLoadedOnceEvictedAtMostOnce_PerCrossing()
        {
            var h = new Harness();
            double L = Length;
            for (double s = 0; s <= L; s += 30.0)
            {
                At(s, out double x, out double z); Heading(s, out double hx, out double hz);
                h.Step(1.0, new FocusSample { Id = 1, Kind = FocusKind.Camera, X = x, Y = 80, Z = z, Vx = (float)(hx * 30), Vz = (float)(hz * 30) });
            }
            Assert.Greater(h.Loads.Count, 200);
            int worst = 0;
            foreach (var kv in h.Loads) worst = Math.Max(worst, kv.Value);
            Assert.AreEqual(1, worst, "a single pass loads every tile exactly once");
            foreach (var kv in h.Evictions) Assert.AreEqual(1, kv.Value);
        }
    }
}
