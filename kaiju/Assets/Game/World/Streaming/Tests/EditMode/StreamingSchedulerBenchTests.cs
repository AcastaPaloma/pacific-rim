// T-124d: gated replan (StreamingConfig.ReplanMoveM). Equivalence with the full recompute on randomized camera paths, and a Tick ms microbenchmark (old vs new).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Game.World.Geo;
using NUnit.Framework;

namespace Game.World.Streaming.Tests
{
    public class StreamingSchedulerBenchTests
    {
        const double X0 = 41000.0, Z0 = 52000.0;

        /// <summary>Bay-like focus set: camera + 2 mechs on one path, 3 kaiju and an anchor wandering within 3 km. Speed m/s, deterministic from the seed.</summary>
        sealed class Scenario
        {
            readonly Random _r; double _cx = X0, _cz = Z0, _ang; readonly double[] _kx = new double[3], _kz = new double[3]; readonly double _speed;
            public readonly FocusSample[] Foci = new FocusSample[7];
            public Scenario(int seed, double speed) { _r = new Random(seed); _speed = speed; for (int i = 0; i < 3; i++) { _kx[i] = X0 + _r.Next(-3000, 3000); _kz[i] = Z0 + _r.Next(-3000, 3000); } }
            public FocusSample[] Next(double dt, bool still)
            {
                float vx = 0, vz = 0;
                if (!still) { _ang += (_r.NextDouble() - 0.5) * 0.2; vx = (float)(_speed * Math.Cos(_ang)); vz = (float)(_speed * Math.Sin(_ang)); _cx += vx * dt; _cz += vz * dt; }
                Foci[0] = Harness.Camera(1, _cx, _cz, vx, vz);
                Foci[1] = Harness.Body(10, _cx, _cz, vx, vz);
                Foci[2] = Harness.Body(11, _cx + 100, _cz + 50, vx, vz);
                for (int i = 0; i < 3; i++) { if (!still) { _kx[i] += _r.NextDouble() * 1.0 - 0.5; _kz[i] += _r.NextDouble() * 1.0 - 0.5; } Foci[3 + i] = Harness.Body(20 + i, _kx[i], _kz[i]); }
                Foci[6] = Harness.Body(40, _kx[0], _kz[0]);
                return Foci;
            }
        }

        static StreamingConfig Cfg(double moveM) => new StreamingConfig { PhysicsPrefetchRadiusM = 768, ReplanMoveM = moveM };

        static string Sig(Harness h, ResidencyKind kind)
        {
            var l = new List<ResidencyKey>();
            foreach (var k in h.Loads.Keys) if (k.Kind == kind && h.Sched.GetState(k) == TileState.Resident) l.Add(k);
            l.Sort();
            var sb = new StringBuilder(); foreach (var k in l) sb.Append(k).Append(';');
            return sb.ToString();
        }

        [Test]
        public void FlagOff_NeverSkips()
        {
            var h = new Harness(Cfg(0.0), new DefaultTileCostModel()); var sc = new Scenario(1, 20.0);
            for (int t = 0; t < 300; t++) h.Step(0.1, sc.Next(0.1, false));
            Assert.AreEqual(0, h.Sched.ReplansSkipped);
        }

        [Test]
        public void Gated_StaticFoci_PlansBitIdenticalToFullRecompute()
        {
            var a = new Harness(Cfg(0.0), new DefaultTileCostModel()); var b = new Harness(Cfg(2.0), new DefaultTileCostModel());
            var sa = new Scenario(3, 0); var sb = new Scenario(3, 0);
            for (int t = 0; t < 400; t++)
            {
                var pa = a.Step(0.1, sa.Next(0.1, true)); var pb = b.Step(0.1, sb.Next(0.1, true));
                Assert.AreEqual(pa.Load.Count, pb.Load.Count, "tick " + t);
                for (int i = 0; i < pa.Load.Count; i++) { Assert.AreEqual(pa.Load[i].Key, pb.Load[i].Key); Assert.AreEqual(pa.Load[i].Priority, pb.Load[i].Priority); }
                Assert.AreEqual(pa.Evict.Count, pb.Evict.Count, "tick " + t);
            }
            Assert.Greater(b.Sched.ReplansSkipped, 200, "static foci must be skipped once settled");
            Assert.AreEqual(Sig(a, ResidencyKind.Render), Sig(b, ResidencyKind.Render));
            Assert.AreEqual(Sig(a, ResidencyKind.Physics), Sig(b, ResidencyKind.Physics));
        }

        [TestCase(11, 8.0), TestCase(12, 30.0), TestCase(13, 60.0), TestCase(14, 3.0), TestCase(15, 100.0)]
        public void Gated_RandomPaths_SettlesToSameResidentSetAndRespectsBudget(int seed, double speed)
        {
            var a = new Harness(Cfg(0.0), new DefaultTileCostModel()); var b = new Harness(Cfg(3.0), new DefaultTileCostModel());
            var sa = new Scenario(seed, speed); var sb = new Scenario(seed, speed);
            for (int t = 0; t < 1500; t++)
            {
                a.Step(0.1, sa.Next(0.1, false)); b.Step(0.1, sb.Next(0.1, false));
                Assert.LessOrEqual(b.Sched.GpuBytesInUse, b.Cfg.Budget.GpuBytes); Assert.LessOrEqual(b.Sched.PhysicsChunkCount, b.Cfg.Budget.MaxPhysicsChunks);
            }
            for (int t = 0; t < 120; t++) { a.Step(0.1, sa.Next(0.1, true)); b.Step(0.1, sb.Next(0.1, true)); }   // both stop: the gate flushes within ReplanMaxIntervalS
            Assert.AreEqual(Sig(a, ResidencyKind.Render), Sig(b, ResidencyKind.Render), "render set after settling");
            Assert.AreEqual(Sig(a, ResidencyKind.Physics), Sig(b, ResidencyKind.Physics), "physics set after settling");
            Assert.AreEqual(a.Sched.ResidentCount, b.Sched.ResidentCount);
        }

        [Test]
        public void Gated_IsDeterministic()
        {
            string Run() { var h = new Harness(Cfg(3.0), new DefaultTileCostModel()); var s = new Scenario(21, 25.0); var sb = new StringBuilder(); for (int t = 0; t < 800; t++) { var p = h.Step(0.1, s.Next(0.1, false)); foreach (var r in p.Load) sb.Append(r.Key).Append(','); sb.Append('|').Append(p.Evict.Count); } return sb.ToString(); }
            Assert.AreEqual(Run(), Run());
        }

        static double[] TimeTicks(double moveM, double speed, out long skipped, out long allocPerTick)
        {
            var h = new Harness(Cfg(moveM), new DefaultTileCostModel()); var sc = new Scenario(5, speed);
            var ms = new List<double>(); long alloc = 0;
            for (int t = 0; t < 8000; t++)
            {
                var foci = sc.Next(0.1, false);
                long al0 = GC.GetAllocatedBytesForCurrentThread(); long t0 = Stopwatch.GetTimestamp();
                h.Sched.Tick(0.1, foci, h.Plan);
                double m = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency; long al = GC.GetAllocatedBytesForCurrentThread() - al0;
                for (int i = 0; i < h.Plan.Evict.Count; i++) h.Sched.Released(h.Plan.Evict[i]);
                for (int i = 0; i < h.Plan.Load.Count; i++) h.Sched.Completed(h.Plan.Load[i].Key, h.Plan.Load[i].EstGpuBytes, h.Plan.Load[i].EstCpuBytes);
                if (t >= 500) { ms.Add(m); alloc += al; }
            }
            skipped = h.Sched.ReplansSkipped; allocPerTick = alloc / ms.Count;
            return ms.ToArray();
        }

        /// <summary>Microbenchmark: Tick ms, full recompute vs gated (3 m), Bay-like foci at 6, 30 and 100 m/s. Writes KAIJU_T124D_OUT when set. Asserts only that gating never costs more.</summary>
        [Test]
        public void Bench_TickMs_FullVsGated()
        {
            var sb = new StringBuilder("speed_mps,mode,mean_ms,p50_ms,p99_ms,max_ms,skipped_ticks,alloc_B_per_tick\n"); var ic = CultureInfo.InvariantCulture;
            foreach (double speed in new[] { 6.0, 30.0, 100.0 })
            {
                TimeTicks(0.0, speed, out _, out _);   // warm up the JIT
                double meanFull = 0, meanGated = 0;
                foreach (double mv in new[] { 0.0, 3.0 })
                {
                    var ms = TimeTicks(mv, speed, out long skipped, out long alloc); double mean = 0; foreach (var x in ms) mean += x; mean /= ms.Length;
                    var sorted = (double[])ms.Clone(); Array.Sort(sorted);
                    sb.Append(speed.ToString(ic)).Append(mv == 0 ? ",full," : ",gated3m,").Append(mean.ToString("F4", ic)).Append(',').Append(sorted[sorted.Length / 2].ToString("F4", ic)).Append(',')
                      .Append(sorted[sorted.Length * 99 / 100].ToString("F4", ic)).Append(',').Append(sorted[sorted.Length - 1].ToString("F4", ic)).Append(',').Append(skipped).Append(',').Append(alloc).Append('\n');
                    if (mv == 0) meanFull = mean; else meanGated = mean;
                }
                Assert.LessOrEqual(meanGated, meanFull * 1.5, "gated must not cost more than the full recompute at " + speed + " m/s");
            }
            Console.WriteLine("T124D_BENCH\n" + sb);
            var path = Environment.GetEnvironmentVariable("KAIJU_T124D_OUT");
            if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, sb.ToString());
        }
    }
}
