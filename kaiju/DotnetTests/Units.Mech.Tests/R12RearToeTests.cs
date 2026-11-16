using System;
using System.IO;
using Game.Physics.Body.Qp;
using Game.Units.Mech.Contracts;
using Game.Units.Mech.Qp;
using NUnit.Framework;

namespace Game.Units.Mech.Tests
{
    /// <summary>T-005 round 12: the rear-toe force cap (qp_r12_reartoe) is off by default; when on, the rear (swing-parity) leg's toe cap is zero in double support while its toe lies ahead of CoM x - m.</summary>
    public class R12RearToeTests
    {
        const int Row = 212; static readonly double[] Lim = { 1.2e9, 8e8, 1e9, 1.2e9, 8e8, 1e9 };

        [Test]
        public void R12FlagsDefaultOff_AndNeedQpPlan()
        {
            var t = new WalkTuning(); Assert.AreEqual(0.0, t.qp_r12_reartoe); Assert.AreEqual(0.0, t.qp_r12_reartoe_m);
            Assert.IsFalse(SagittalQpOptions.From(t).R12RearToe);
            t.qp_plan = 0; t.qp_r12_reartoe = 1; Assert.IsFalse(SagittalQpOptions.From(t).R12RearToe, "needs qp_plan");
            t.qp_plan = 1; t.qp_r12_reartoe_m = 1.0; var o = SagittalQpOptions.From(t); Assert.IsTrue(o.R12RearToe); Assert.AreEqual(1.0, o.R12RearToeM);
        }

        [Test]
        public void Replay_RearToeCapIsZeroOnlyAheadOfTheCom_AndFlagOffChangesNothing()
        {
            var d = Repo.Load<GaitReferenceData>("Mech/gait_reference_qp.json"); d.Validate();
            byte[] raw = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "qp_golden.bin")); int n = raw.Length / (8 * Row);
            var a = new double[raw.Length / 8]; Buffer.BlockCopy(raw, 0, a, 0, raw.Length);
            SagittalQpController Mk(bool on, double m) => new SagittalQpController(new QpReference(d.rows[0], 0.01, true), new SagittalQpOptions { Plan = true, R12RearToe = on, R12RearToeM = m }, Lim, new GoldfarbIdnaniQp(SagittalQpController.MaxNx, 3, SagittalQpController.MaxIn));
            var off = Mk(false, 0.0); var on0 = Mk(true, 0.0); var on1 = Mk(true, 1e3);   // m = 1000 m: every rear toe counts as ahead of the CoM
            int zeroed = 0, ticksDs = 0;
            for (int k = 0; k < n; k++)
            {
                foreach (var c in new[] { off, on0, on1 })
                {
                    int o = k * Row + 1;
                    Array.Copy(a, o, c.Q, 0, 9); Array.Copy(a, o + 9, c.Qd, 0, 9); Array.Copy(a, o + 18, c.M, 0, 81); Array.Copy(a, o + 99, c.H, 0, 9); Array.Copy(a, o + 108, c.Pts, 0, 8); Array.Copy(a, o + 116, c.Jp, 0, 72);
                    Array.Copy(a, o + 188, c.Bp, 0, 8); Array.Copy(a, o + 196, c.Com, 0, 2); Array.Copy(a, o + 198, c.ComD, 0, 2); c.AngMom = a[o + 200]; Array.Copy(a, o + 201, c.FnMeas, 0, 4); c.HasFnMeas = k > 0;
                    c.Step(a[k * Row]);
                }
                var s = off.Schedule; int rl = s.Parity, fl = 1 - rl; bool ds = (s.Inc[2 * rl] || s.Inc[2 * rl + 1]) && (s.Inc[2 * fl] || s.Inc[2 * fl + 1]);
                if (ds && s.Inc[2 * rl + 1]) { ticksDs++; if (on1.FmaxBw[2 * rl + 1] == 0.0 && off.FmaxBw[2 * rl + 1] > 0.0) zeroed++; if (on0.FmaxBw[2 * rl + 1] == 0.0 && off.FmaxBw[2 * rl + 1] > 0.0) Assert.That(off.Pts[2 * (2 * rl + 1)], Is.GreaterThan(off.Com[0]), "m = 0: zeroed only while the toe is ahead of the CoM, tick " + k); }
                if (!ds) for (int i = 0; i < 4; i++) Assert.AreEqual(off.FmaxBw[i], on1.FmaxBw[i], 0.0, "outside double support the caps are untouched, tick " + k);
                for (int i = 0; i < 4; i++) Assert.GreaterOrEqual(off.FmaxBw[i], on0.FmaxBw[i] - 1e-12, "the flag only lowers caps");
            }
            Assert.That(zeroed, Is.GreaterThan(0), "the cap is exercised by the golden replay");
            TestContext.WriteLine($"golden ticks {n}, double-support ticks with a rear toe {ticksDs}, toe cap zeroed at m = 1000: {zeroed}");
        }
    }
}
