using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Game.Physics.Body.Qp;
using Game.Units.Mech.Contracts;
using Game.Units.Mech.Qp;
using NUnit.Framework;

namespace Game.Units.Mech.Tests
{
    /// <summary>Step 9.4b: the C# port of the sagittal whole-body QP (SagittalQpController + QpReference) against the Python prototype (controller_fid.py, b5 preset, reference v3), plus constraint, schedule and allocation tests.</summary>
    public class QpControllerTests
    {
        const int Row = 212;                       // layout of Fixtures/qp_golden.bin (Tools/GaitOpt/qp_proto/export_golden.py)
        static readonly double[] Lim = { 1.2e9, 8e8, 1e9, 1.2e9, 8e8, 1e9 };

        static GaitReferenceData Ref() { var d = Repo.Load<GaitReferenceData>("Mech/gait_reference_qp.json"); d.Validate(); return d; }
        static double[] Golden(out int ticks)
        {
            byte[] raw = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "qp_golden.bin")); ticks = raw.Length / (8 * Row);
            var a = new double[raw.Length / 8]; Buffer.BlockCopy(raw, 0, a, 0, raw.Length); return a;
        }
        static SagittalQpController Make(GaitReferenceData d, SagittalQpOptions o = null) =>
            new SagittalQpController(new QpReference(d.rows[0], 0.01, true), o ?? new SagittalQpOptions(), Lim, new GoldfarbIdnaniQp(SagittalQpController.MaxNx, 3, SagittalQpController.MaxIn));

        static void Feed(SagittalQpController c, double[] a, int row, int stride, int hasPrev)
        {
            int o = row * stride + 1;
            Array.Copy(a, o, c.Q, 0, 9); Array.Copy(a, o + 9, c.Qd, 0, 9); Array.Copy(a, o + 18, c.M, 0, 81); Array.Copy(a, o + 99, c.H, 0, 9); Array.Copy(a, o + 108, c.Pts, 0, 8); Array.Copy(a, o + 116, c.Jp, 0, 72);
            Array.Copy(a, o + 188, c.Bp, 0, 8); Array.Copy(a, o + 196, c.Com, 0, 2); Array.Copy(a, o + 198, c.ComD, 0, 2); c.AngMom = a[o + 200]; Array.Copy(a, o + 201, c.FnMeas, 0, 4); c.HasFnMeas = hasPrev > 0;
        }
        static void Feed(SagittalQpController c, double[] a, int k) => Feed(c, a, k, Row, k);
        static double WorstError(SagittalQpController c, double[] a, int row, int stride, int uOffset) { double e = 0; for (int j = 0; j < 6; j++) e = Math.Max(e, Math.Abs(c.Tau[j] - a[row * stride + uOffset + j]) / Lim[j]); return e; }

        sealed class FlakySolver : IQpSolver
        {
            readonly GoldfarbIdnaniQp _s = new GoldfarbIdnaniQp(SagittalQpController.MaxNx, 3, SagittalQpController.MaxIn); public bool Fail; public int Calls;
            public double[] X => _s.X; public double[] Lambda => _s.Lambda; public QpStatus Status { get; private set; } public int Iterations => _s.Iterations; public long FailureCount => _s.FailureCount;
            public double Regularisation { get => _s.Regularisation; set => _s.Regularisation = value; }
            public QpStatus Solve(int n, int meq, int min, double[] H, double[] g, double[] Aeq, double[] beq, double[] Ain, double[] bin, bool hUnchanged = false, int[] warm = null, int warmCount = 0)
            { Calls++; Status = Fail ? QpStatus.Infeasible : _s.Solve(n, meq, min, H, g, Aeq, beq, Ain, bin, hUnchanged, warm, warmCount); return Status; }
        }

        [Test]
        public void ReferenceSplines_MatchThePrototype()
        {
            var samples = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "qp_ref_samples.json")));
            var qr = new QpReference(Ref().rows[0], 0.01, true); var o = new double[QpReference.Channels]; double wq = 0, wpt = 0, wptv = 0, wpta = 0, wo = 0;
            foreach (var s in samples.EnumerateArray())
            {
                qr.At(s.GetProperty("t").GetDouble(), o);
                double Max(string name, int ch, double scale = 1.0) { double m = 0, i = 0; foreach (var v in s.GetProperty(name).EnumerateArray()) { m = Math.Max(m, Math.Abs(o[ch + (int)i] - v.GetDouble()) / scale); i++; } return m; }
                wq = Math.Max(wq, Math.Max(Max("q", QpReference.Cq, 1), Math.Max(Max("qd", QpReference.Cqd, 1e-2), Max("qdd", QpReference.Cqdd, 1e-3))));
                wo = Math.Max(wo, Math.Max(Max("com", QpReference.Ccom), Math.Max(Max("comd", QpReference.Ccomd, 1e-1), Math.Max(Max("comdd", QpReference.Ccomdd, 1e-2), Math.Max(Max("F", QpReference.Cf, 1e7), Math.Max(Math.Abs(o[QpReference.Cl] - s.GetProperty("L").GetDouble()) / 1e10, Math.Abs(o[QpReference.Cldot] - s.GetProperty("Ldot").GetDouble()) / 1e10))))));
                wpt = Math.Max(wpt, Max("pts", QpReference.Cpts)); wptv = Math.Max(wptv, Max("ptv", QpReference.Cptv, 1e-2)); wpta = Math.Max(wpta, Max("pta", QpReference.Cpta, 1e-2));
            }
            TestContext.WriteLine($"reference vs prototype: q-channels {wq:G3} (scaled), other {wo:G3}, sole points {wpt:G3} m, velocity {wptv:G3}, acceleration {wpta:G3} (relative to 1e-2 m/s, m/s2)");
            Assert.That(wq, Is.LessThan(1e-6), "q, qd, qdd are the same splines as the prototype's"); Assert.That(wo, Is.LessThan(1e-6), "com, comd, comdd, F, L, Ldot are the same splines");
            Assert.That(wpt, Is.LessThan(1e-6), "sole points = kinematics of the spline q (m)"); Assert.That(wptv, Is.LessThan(1e-6)); Assert.That(wpta, Is.LessThan(1e-6));
        }

        [Test]
        public void GoldenTicks_MatchThePythonPrototype()
        {
            var a = Golden(out int n); var c = Make(Ref()); double worst = 0, sum = 0; int worstK = -1, failed = 0, big = 0;
            for (int k = 0; k < n; k++)
            {
                Feed(c, a, k); bool ok = c.Step(a[k * Row]); if (!ok) failed++;
                double e = WorstError(c, a, k, Row, 206);
                if (e > worst) { worst = e; worstK = k; }
                if (e > 0.01) big++; sum += e;
            }
            TestContext.WriteLine($"golden: {n} ticks, max |dtau|/LIM = {worst:G4} at tick {worstK}, mean {sum / n:G4}, ticks > 0.01 LIM: {big}, infeasible: {failed}, touchdowns {c.Touchdowns}, ms {c.MassScale:G6}");
            Assert.That(failed, Is.EqualTo(0));
            Assert.That(worst, Is.LessThan(1e-3), "max torque difference as a fraction of the joint limit"); Assert.That(c.Touchdowns, Is.EqualTo(1));
        }

        /// <summary>Step 9.5b (stage 2, qp_lateral): golden replay with 3D corners (feet at +-6.5 m, sole 10 m wide). Neutral lateral state: the sagittal torques stay close to the planar QP; the 3D forces stay inside the diamond pyramid;
        /// the yaw moment the sagittal shear makes at the lateral foot offset is cancelled (heel / toe lateral forces); a heading error and a lateral CoM offset produce a restoring yaw moment and lateral force.</summary>
        [Test]
        public void Lateral_GoldenReplay_SharesTheConeAndHoldsYaw()
        {
            var a = Golden(out int n); var d = Ref(); var c0 = Make(d); var o = new SagittalQpOptions { Lateral = true }; var c = Make(d, o);
            double w = new QpReference(d.rows[0], 0.01, true).BodyMassKg * 9.81, worst = 0, worstCone = 0, worstYaw = 0, maxYawRef = 0; int failed = 0;
            void Corners(SagittalQpController q, double yOff)
            {
                for (int i = 0; i < 4; i++) for (int k = 0; k < 2; k++) { int cc = 2 * i + k; q.Corner[3 * cc] = q.Pts[2 * i]; q.Corner[3 * cc + 1] = (i < 2 ? -6.5 : 6.5) + (k == 0 ? -5 : 5) + yOff; q.Corner[3 * cc + 2] = q.Pts[2 * i + 1]; }
                q.ComY = 0; q.ComDY = 0; q.LRoll = 0; q.LYaw = 0; q.Izz = 25 * w / 9.81; q.YawErr = 0; q.YRef = yOff;
            }
            for (int k = 0; k < n; k++)
            {
                Feed(c0, a, k); c0.Step(a[k * Row]); Feed(c, a, k); Corners(c, 0); if (!c.Step(a[k * Row])) failed++;
                for (int j = 0; j < 6; j++) worst = Math.Max(worst, Math.Abs(c.Tau[j] - c0.Tau[j]) / Lim[j]);
                for (int q = 0; q < 8; q++) { double fz = c.Force3[3 * q + 2]; if (fz > 1e-6 * w) worstCone = Math.Max(worstCone, (Math.Abs(c.Force3[3 * q]) + Math.Abs(c.Force3[3 * q + 1])) / (o.Mu * o.FrMargin * fz)); }
                double my0 = 0; for (int q = 0; q < 8; q++) my0 -= (c.Corner[3 * q + 1] - c.ComY) * c.Force3[3 * q]; worstYaw += Math.Abs(c.LatCmd[2]); maxYawRef += Math.Abs(my0) / (w * 42.4);
            }
            TestContext.WriteLine($"lateral golden: max |dtau|/LIM vs planar {worst:G3}, cone use max {worstCone:G4}, mean |yaw moment| {worstYaw / n:G3} (W l) vs the sagittal shear's own {maxYawRef / n:G3}, infeasible {failed}");
            Assert.That(failed, Is.EqualTo(0)); Assert.That(worstCone, Is.LessThanOrEqualTo(1 + 1e-6)); Assert.That(worstYaw, Is.LessThan(0.2 * maxYawRef), "the yaw moment of the sagittal shear is (mostly) cancelled");
            Assert.That(worst, Is.LessThan(0.15), "the sagittal torques stay close to the planar QP");
            // restoring actions from one mid-stance tick
            int t0 = n / 2; Feed(c, a, t0); Corners(c, 0); c.YawErr = 0.2; c.Step(a[t0 * Row]); double yawM = c.LatCmd[2];
            Feed(c, a, t0); Corners(c, 0); c.ComY = 3.0; c.Step(a[t0 * Row]); double fy = c.LatCmd[0];
            TestContext.WriteLine($"heading error +0.2 rad -> yaw moment {yawM:G3} (W l); CoM 3 m right of the target -> lateral force {fy:G3} W");
            Assert.That(yawM, Is.LessThan(0)); Assert.That(fy, Is.LessThan(0));
        }

        /// <summary>Step 9.5b: the torque regulariser (qp_w_tau) on the golden replay: feasible every tick, torques stay near the planar QP (the reference walk needs no internal double-support force), the peak |tau| / LIM does not grow.</summary>
        [TestCase(1.0)]
        [TestCase(10.0)]
        public void TorqueRegulariser_GoldenReplay_StaysCloseAndFeasible(double wTau)
        {
            var a = Golden(out int n); var d = Ref(); var c0 = Make(d); var c = Make(d, new SagittalQpOptions { WTau = wTau }); double worst = 0, peak0 = 0, peak = 0; int failed = 0;
            for (int k = 0; k < n; k++)
            {
                Feed(c0, a, k); c0.Step(a[k * Row]); Feed(c, a, k); if (!c.Step(a[k * Row])) failed++;
                for (int j = 0; j < 6; j++) { worst = Math.Max(worst, Math.Abs(c.Tau[j] - c0.Tau[j]) / Lim[j]); peak0 = Math.Max(peak0, Math.Abs(c0.Tau[j]) / Lim[j]); peak = Math.Max(peak, Math.Abs(c.Tau[j]) / Lim[j]); }
            }
            TestContext.WriteLine($"w_tau {wTau}: max |dtau|/LIM vs planar {worst:G3}, peak |tau|/LIM {peak:G3} (planar {peak0:G3}), infeasible {failed}");
            Assert.That(failed, Is.EqualTo(0)); Assert.That(worst, Is.LessThan(0.2)); Assert.That(peak, Is.LessThanOrEqualTo(peak0 + 1e-6));
        }

        [Test]
        public void ExactReferenceStates_ReproduceThePrototypeAndTheReferenceTorques()
        {
            const int R2 = 218; byte[] raw = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "qp_refstate.bin")); var a = new double[raw.Length / 8]; Buffer.BlockCopy(raw, 0, a, 0, raw.Length);
            var d = Ref(); var qr = new QpReference(d.rows[0], 0.01, true); double w = qr.BodyMassKg * 9.81, worstPy = 0, worstRef = 0; var f = new double[8];
            for (int k = 0; k < qr.N; k++)
            {
                var c = new SagittalQpController(qr, new SagittalQpOptions(), Lim, new GoldfarbIdnaniQp(SagittalQpController.MaxNx, 3, SagittalQpController.MaxIn)); c.Init();
                for (int i = 0; i < 4; i++) { c.Schedule.Inc[i] = qr.Flags[k * 4 + i]; c.Schedule.FIn[i] = 5 * w; c.Schedule.TIn[i] = -10; for (int j = 0; j < 2; j++) f[2 * i + j] = qr.Node(QpReference.Cf + 2 * i + j, k); }
                c.Schedule.TAnchor = -k * qr.H; c.Schedule.TRel[0] = c.Schedule.TRel[1] = -10; c.SetPreviousForces(f);
                Feed(c, a, k, R2, 1); Assert.That(c.Step(0.0), Is.True, "node " + k);
                worstPy = Math.Max(worstPy, WorstError(c, a, k, R2, 206));
                double e = 0; for (int j = 0; j < 6; j++) e = Math.Max(e, Math.Abs(c.Tau[j] - a[k * R2 + 212 + j]) / Lim[j]);
                if (k >= 1 && k <= 34) worstRef = Math.Max(worstRef, e);
            }
            TestContext.WriteLine($"reference states: max |dtau|/LIM vs prototype {worstPy:G3}; vs reference torques (nodes 1-34) {worstRef:G3}");
            Assert.That(worstPy, Is.LessThan(1e-3)); Assert.That(worstRef, Is.LessThan(0.1), "check_fid: the QP at the exact reference state returns the reference torques (nodes 0 and 35-39 are the landing window and strike, where it departs on purpose)");
        }

        [Test]
        public void Replay_RespectsFrictionPyramidForceCapShearRowsAndTorqueLimits()
        {
            var a = Golden(out int n); var c = Make(Ref()); double w0 = c.Schedule == null ? 0 : 1e7 * 9.81; int dropped = 0; double maxFr = 0;
            for (int k = 0; k < n; k++)
            {
                Feed(c, a, k); long before = c.TorqueRowsDropped; Assert.That(c.Step(a[k * Row]), Is.True, "tick " + k); bool drop = c.TorqueRowsDropped > before; if (drop) dropped++;
                double w = w0 * c.MassScale;
                for (int i = 0; i < 4; i++)
                {
                    double ft = c.Force[2 * i], fn = c.Force[2 * i + 1];
                    Assert.That(Math.Abs(ft), Is.LessThanOrEqualTo(0.4 * 0.9 * fn + 1e-6 * w), "friction pyramid, tick " + k); Assert.That(fn, Is.GreaterThanOrEqualTo(-1e-6 * w));
                    Assert.That(fn, Is.LessThanOrEqualTo(c.FmaxBw[i] * w * (1 + 1e-6) + 1e-6 * w), "force cap, tick " + k); if (fn > 1e-6 * w) maxFr = Math.Max(maxFr, Math.Abs(ft) / (0.4 * fn));
                }
                for (int f = 0; f < 2; f++)
                    Assert.That(Math.Abs(c.Force[4 * f] + c.Force[4 * f + 2]), Is.LessThanOrEqualTo(Math.Max(0.4 * 0.9 * (c.FnMeas[2 * f] + c.FnMeas[2 * f + 1]), 1e-3 * w) * (1 + 1e-6) + 1e-6 * w), "per-foot shear row, tick " + k);
                for (int j = 0; j < 6; j++) { Assert.That(Math.Abs(c.Tau[j]), Is.LessThanOrEqualTo(Lim[j])); if (!drop) Assert.That(Math.Abs(c.Tau[j]), Is.LessThanOrEqualTo(0.98 * Lim[j] * (1 + 1e-6)), "torque row, tick " + k); }
            }
            TestContext.WriteLine($"replay: commanded shear / (mu fn) max {maxFr:G3}; ticks with the torque rows dropped {dropped}");
        }

        [Test]
        public void InfeasibleTick_KeepsThePreviousTorquesAndCountsIt()
        {
            var a = Golden(out _); var s = new FlakySolver(); var c = new SagittalQpController(new QpReference(Ref().rows[0], 0.01, true), new SagittalQpOptions(), Lim, s);
            for (int k = 0; k < 40; k++) { Feed(c, a, k); Assert.That(c.Step(a[k * Row]), Is.True); }
            var keep = (double[])c.Tau.Clone(); long fails = c.SolveFailures; s.Fail = true; Feed(c, a, 40);
            Assert.That(c.Step(a[40 * Row]), Is.False); Assert.That(c.Tau, Is.EqualTo(keep)); Assert.That(c.SolveFailures, Is.EqualTo(fails + 1)); Assert.That(c.LastStatus, Is.EqualTo(QpStatus.Infeasible)); Assert.That(c.TorqueRowsDropped, Is.EqualTo(1), "the torque rows were dropped once before giving up");
            s.Fail = false; Feed(c, a, 41); Assert.That(c.Step(a[41 * Row]), Is.True); Assert.That(c.Tau, Is.Not.EqualTo(keep));
        }

        [Test]
        public void StepAndReplanAndTouchdown_AllocateNothingAfterWarmUp()
        {
            var a = Golden(out int n); var c = Make(Ref());
            for (int k = 0; k < 30; k++) { Feed(c, a, k); c.Step(a[k * Row]); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int k = 30; k < n; k++) { Feed(c, a, k); c.Step(a[k * Row]); }
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.EqualTo(0), "bytes allocated over " + (n - 30) + " ticks incl. the landing replan (tick 231) and the touchdown (tick 255)");
            Assert.That(c.Touchdowns, Is.EqualTo(1));
        }

        [Test]
        public void ScheduleCases_EarlyContactIsNoTouchdown_LateHoldsThePhase_MissedIsFlagged()
        {
            var a = Golden(out int n); var d = Ref(); var qr = new QpReference(d.rows[0], 0.01, true); double w = qr.BodyMassKg * 9.81;
            // early: the swing heel (point 0) reports load at ticks 150-154 (2.5 s, before 0.6 of the step time = 2.64 s): contact is registered, the legs are not swapped
            var c = Make(d); int td0 = -1;
            for (int k = 0; k < 158; k++) { Feed(c, a, k); if (k >= 150 && k < 155) c.FnMeas[0] = 0.2 * w; c.Step(a[k * Row]); if (k == 150) { Assert.That(c.Schedule.Inc[0], Is.True); Assert.That(c.Parity, Is.EqualTo(0)); } td0 = c.Touchdowns; }
            Assert.That(td0, Is.EqualTo(0)); Assert.That(c.Parity, Is.EqualTo(0), "early contact does not re-anchor");
            // on time: the replay's touchdown at 4.25 s swaps the legs, re-anchors the clock and shifts the reference to the measured foot
            c = Make(d); for (int k = 0; k < n; k++) { Feed(c, a, k); c.Step(a[k * Row]); }
            Assert.That(c.Touchdowns, Is.EqualTo(1)); Assert.That(c.Parity, Is.EqualTo(1)); Assert.That(c.Schedule.TAnchor, Is.EqualTo(4.25).Within(1 / 60.0)); Assert.That(double.IsFinite(c.Schedule.Xoff));
            // late and missed: no load on the swing foot after tick 240: the phase holds, the clamp and the lowering of the target apply, the missed flag rises, torques stay finite
            c = Make(d); int bad = 0;
            for (int k = 0; k < n; k++) { Feed(c, a, k); if (k >= 240) { c.FnMeas[0] = c.FnMeas[1] = 0; } c.Step(a[k * Row]); foreach (double u in c.Tau) if (!double.IsFinite(u)) bad++; }
            Assert.That(c.Touchdowns, Is.EqualTo(0)); Assert.That(c.Parity, Is.EqualTo(0)); Assert.That(c.Schedule.Late(a[(n - 1) * Row]), Is.GreaterThan(1.0)); Assert.That(c.Schedule.Missed(a[(n - 1) * Row]), Is.True); Assert.That(bad, Is.EqualTo(0));
            Assert.That(c.Schedule.RefTime(a[(n - 1) * Row]), Is.EqualTo(qr.Ts - 0.3 * qr.H).Within(1e-9), "the reference time stops 0.3 h before the end of the step");
        }

        [Test]
        public void ReferenceFileV2_ValidatesAndIsConsistentWithTheForces_AndV1StillValidates()
        {
            var d = Ref(); Assert.That(d.schema, Is.EqualTo("gait_reference_opt@v2")); Assert.That(d.rows.Length, Is.EqualTo(1)); var r = d.rows[0]; Assert.That(r.nodes.Length, Is.EqualTo(40)); Assert.That(r.mu, Is.EqualTo(0.4).Within(1e-9));
            double w = r.body_mass_kg * 9.81, worst = 0;
            foreach (var n in r.nodes)
            {
                double fx = 0, fz = 0; for (int p = 0; p < 4; p++) { fx += n.f4x2[2 * p]; fz += n.f4x2[2 * p + 1]; }
                worst = Math.Max(worst, Math.Max(Math.Abs(fx - r.body_mass_kg * n.comdd[0]), Math.Abs(fz - r.body_mass_kg * (n.comdd[1] + 9.81))) / w);
            }
            Assert.That(worst, Is.LessThan(1e-4), "sum(ft, fn) = m (cdd + g) at every node (max residual / m g)");
            var v1 = Repo.Load<GaitReferenceData>("Mech/gait_reference_opt.json"); Assert.DoesNotThrow(() => v1.Validate()); Assert.That(v1.schema, Is.EqualTo("gait_reference_opt@v1")); Assert.That(v1.rows[0].nodes[0].q9, Is.Null);
            var bad = Ref(); bad.rows[0].nodes[3].q9 = null; Assert.Throws<InvalidOperationException>(() => bad.Validate());
            var worse = Ref(); worse.schema = "gait_reference_opt@v3"; Assert.Throws<InvalidOperationException>(() => worse.Validate());
        }

        [Test]
        public void ImpBelow2p5_BuildsNoQp_ReferenceFileSelection_AndTheQpConfig()
        {
            foreach (double imp in new[] { 0.0, 1.0, 2.0, 2.4 }) Assert.That(Game.Units.Mech.LocomotionController.QpWanted(new WalkTuning { imp = imp }), Is.False, "imp " + imp);
            Assert.That(Game.Units.Mech.LocomotionController.QpWanted(new WalkTuning { imp = 3.0 }), Is.True);
            Assert.That(GaitReferenceData.FileFor(0), Is.Null); Assert.That(GaitReferenceData.FileFor(1), Is.Null); Assert.That(GaitReferenceData.FileFor(2), Is.EqualTo("gait_reference_opt.json")); Assert.That(GaitReferenceData.FileFor(3), Is.EqualTo("gait_reference_qp.json"));
            var shipped = Repo.Walk(); Assert.That(shipped.imp, Is.EqualTo(0.0)); string json = File.ReadAllText(Path.Combine(Repo.Root(), "Assets", "Game", "Config", "Mech", "walk_tuning.json")); StringAssert.DoesNotContain("qp_", json);
            var q = Repo.Load<WalkTuning>("Mech/walk_tuning_qp.json"); Assert.That(q.imp, Is.EqualTo(3.0)); Assert.That(q.imp2_phase_sync, Is.EqualTo(1.0)); Assert.That(q.schema, Is.EqualTo(WalkTuning.Schema));
            var o = SagittalQpOptions.From(q); var def = new SagittalQpOptions();
            foreach (var f in typeof(SagittalQpOptions).GetFields()) Assert.That(f.GetValue(o), Is.EqualTo(f.GetValue(def)), "walk_tuning_qp.json = the b5 defaults: " + f.Name);
            foreach (var f in typeof(SagittalQpOptions).GetFields()) if (f.Name != "G") Assert.That(f.GetValue(SagittalQpOptions.From(new WalkTuning())), Is.EqualTo(f.GetValue(def)), "C# WalkTuning defaults = b5: " + f.Name);
        }
    }
}
