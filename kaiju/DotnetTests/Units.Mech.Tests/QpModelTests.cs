using System;
using Game.Units.Mech.Qp;
using NUnit.Framework;

namespace Game.Units.Mech.Tests
{
    /// <summary>Step 9.4a: WholeBodyModel (M_ss, A_G, point Jacobians) on a toy planar 2-link chain (revolute about world z, two dummy leading columns) and ContactSchedule logic.</summary>
    public class QpModelTests
    {
        const double L1 = 0.5, C1 = 0.2, C2 = 0.3, M1 = 3.0, M2 = 2.0, I1 = 0.07, I2 = 0.04;
        const int Cols = 4;             // columns 0,1 are other DOFs (junk), 2,3 the chain joints
        static readonly int[] Sag = { 2, 3 };

        // forward kinematics of the chain at (q1, q2): origin joint 1 at 0, joint 2 at L1 along link 1
        static void Fk(double q1, double q2, double[] com1, double[] com2, double[] j2, double[] pLocalWorld, double a, double b)
        {
            double c1 = Math.Cos(q1), s1 = Math.Sin(q1), c12 = Math.Cos(q1 + q2), s12 = Math.Sin(q1 + q2);
            com1[0] = C1 * c1; com1[1] = C1 * s1; com1[2] = 0;
            j2[0] = L1 * c1; j2[1] = L1 * s1; j2[2] = 0;
            com2[0] = j2[0] + C2 * c12; com2[1] = j2[1] + C2 * s12; com2[2] = 0;
            pLocalWorld[0] = j2[0] + a * c12 - b * s12; pLocalWorld[1] = j2[1] + a * s12 + b * c12; pLocalWorld[2] = 0;
        }

        static WholeBodyModel Build(double q1, double q2, out double[] jac, double[] pointLocal)
        {
            var m = new WholeBodyModel(2, 2, 1) { Links = 2, S = 2, Points = 1 };
            m.Mass[0] = M1; m.Mass[1] = M2;
            m.InertiaDiag[0] = m.InertiaDiag[1] = 0.01; m.InertiaDiag[2] = I1; m.InertiaDiag[3] = m.InertiaDiag[4] = 0.01; m.InertiaDiag[5] = I2;
            // link rotations about z by q1 and q1 + q2 (world from link)
            double[] th = { q1, q1 + q2 };
            for (int i = 0; i < 2; i++) { double c = Math.Cos(th[i]), s = Math.Sin(th[i]); int o = 9 * i; m.Rot[o] = c; m.Rot[o + 1] = -s; m.Rot[o + 3] = s; m.Rot[o + 4] = c; m.Rot[o + 8] = 1; }
            var com1 = new double[3]; var com2 = new double[3]; var j2 = new double[3]; var p = new double[3];
            Fk(q1, q2, com1, com2, j2, p, pointLocal[0], pointLocal[1]);
            for (int k = 0; k < 3; k++) { m.Com[k] = com1[k]; m.Com[3 + k] = com2[k]; m.RefPos[k] = 0; m.RefPos[3 + k] = j2[k]; }   // reference points: the joints (not the COMs)
            m.LinkRow[0] = 0; m.LinkRow[1] = 6; m.SagCols[0] = 2; m.SagCols[1] = 3; m.PointLink[0] = 1;
            m.PointPos[0] = p[0]; m.PointPos[1] = p[1]; m.PointPos[2] = p[2];
            // Jacobian about each link's reference point: omega = z q1' (+ z q2'), v_ref = z x (ref - joint) per joint
            jac = new double[12 * Cols];
            var rnd = new Random(3);
            for (int r = 0; r < 12; r++) { jac[r * Cols] = rnd.NextDouble(); jac[r * Cols + 1] = rnd.NextDouble(); }   // junk in the non-sagittal columns
            // link 0, ref = joint 1 at origin: v = 0, w = z q1'
            jac[5 * Cols + 2] = 1;
            // link 1, ref = joint 2: v = z x (j2 - 0) q1' = (-j2y, j2x, 0) q1', w = z (q1' + q2')
            jac[6 * Cols + 2] = -j2[1]; jac[7 * Cols + 2] = j2[0]; jac[11 * Cols + 2] = 1; jac[11 * Cols + 3] = 1;
            return m;
        }

        [Test]
        public void Mss_IsSymmetricPositiveDefinite_AndEqualsTheHandTwoLinkMatrix()
        {
            double q2 = 0.7; var m = Build(0.3, q2, out var jac, new[] { 0.1, 0.05 });
            m.Compute(jac, Cols);
            Assert.That(m.Mss[1], Is.EqualTo(m.Mss[2]).Within(0), "exactly symmetric");
            Assert.That(m.Mss[0] > 0 && m.Mss[0] * m.Mss[3] - m.Mss[1] * m.Mss[2] > 0, "positive definite");
            // hand: M11 = I1 + I2 + m1 C1^2 + m2 (L1^2 + C2^2 + 2 L1 C2 cos q2), M12 = I2 + m2 (C2^2 + L1 C2 cos q2), M22 = I2 + m2 C2^2   (rod inertia about the COM, z axis)
            double m11 = I1 + I2 + M1 * C1 * C1 + M2 * (L1 * L1 + C2 * C2 + 2 * L1 * C2 * Math.Cos(q2));
            double m12 = I2 + M2 * (C2 * C2 + L1 * C2 * Math.Cos(q2)), m22 = I2 + M2 * C2 * C2;
            Assert.That(m.Mss[0], Is.EqualTo(m11).Within(1e-12)); Assert.That(m.Mss[1], Is.EqualTo(m12).Within(1e-12)); Assert.That(m.Mss[3], Is.EqualTo(m22).Within(1e-12));
        }

        [Test]
        public void AG_RowsAreTheSumOfLinkMomenta_AboutTheTotalCom()
        {
            double q1 = 0.3, q2 = 0.7, d1 = 1.3, d2 = -0.8; var pl = new[] { 0.1, 0.05 };
            var m = Build(q1, q2, out var jac, pl); m.Compute(jac, Cols);
            // link velocities by central difference of the forward kinematics
            double e = 1e-6; double[] v1 = new double[3], v2 = new double[3];
            var a1 = new double[3]; var a2 = new double[3]; var b1 = new double[3]; var b2 = new double[3]; var j = new double[3]; var pp = new double[3];
            Fk(q1 + e * d1, q2 + e * d2, a1, a2, j, pp, pl[0], pl[1]); Fk(q1 - e * d1, q2 - e * d2, b1, b2, j, pp, pl[0], pl[1]);
            for (int k = 0; k < 3; k++) { v1[k] = (a1[k] - b1[k]) / (2 * e); v2[k] = (a2[k] - b2[k]) / (2 * e); }
            double w1 = d1, w2 = d1 + d2;
            double mt = M1 + M2, cx = (M1 * m.Com[0] + M2 * m.Com[3]) / mt, cy = (M1 * m.Com[1] + M2 * m.Com[4]) / mt;
            Assert.That(m.ComTotal[0], Is.EqualTo(cx).Within(1e-12)); Assert.That(m.TotalMass, Is.EqualTo(mt));
            double px = M1 * v1[0] + M2 * v2[0], py = M1 * v1[1] + M2 * v2[1];
            double lz = M1 * ((m.Com[0] - cx) * v1[1] - (m.Com[1] - cy) * v1[0]) + I1 * w1 + M2 * ((m.Com[3] - cx) * v2[1] - (m.Com[4] - cy) * v2[0]) + I2 * w2;
            double[] qd = { d1, d2 }; double[] h = new double[6];
            for (int r = 0; r < 6; r++) for (int s = 0; s < 2; s++) h[r] += m.AG[r * 2 + s] * qd[s];
            Assert.That(h[0], Is.EqualTo(px).Within(1e-6)); Assert.That(h[1], Is.EqualTo(py).Within(1e-6)); Assert.That(h[2], Is.EqualTo(0).Within(1e-12));
            Assert.That(h[3], Is.EqualTo(0).Within(1e-12)); Assert.That(h[4], Is.EqualTo(0).Within(1e-12)); Assert.That(h[5], Is.EqualTo(lz).Within(1e-6));
        }

        [Test]
        public void PointJacobian_MatchesFiniteDifferenceOfTheForwardKinematics()
        {
            double q1 = -0.4, q2 = 0.9; var pl = new[] { 0.12, -0.07 };
            var m = Build(q1, q2, out var jac, pl); m.Compute(jac, Cols);
            double e = 1e-6; var x = new double[3]; var y = new double[3]; var c1 = new double[3]; var c2 = new double[3]; var j = new double[3];
            for (int s = 0; s < 2; s++)
            {
                double dq1 = s == 0 ? e : 0, dq2 = s == 1 ? e : 0;
                Fk(q1 + dq1, q2 + dq2, c1, c2, j, x, pl[0], pl[1]); Fk(q1 - dq1, q2 - dq2, c1, c2, j, y, pl[0], pl[1]);
                for (int k = 0; k < 2; k++) Assert.That(m.Jp[k * 2 + s], Is.EqualTo((x[k] - y[k]) / (2 * e)).Within(1e-6), $"J[{k},{s}]");
                Assert.That(m.Jp[2 * 2 + s], Is.EqualTo(0).Within(1e-12));
            }
        }

        [Test]
        public void Compute_AllocatesNothingAfterWarmUp()
        {
            var m = Build(0.3, 0.7, out var jac, new[] { 0.1, 0.05 }); m.Compute(jac, Cols);
            long a = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) m.Compute(jac, Cols);
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - a, Is.EqualTo(0));
        }

        // ---------------- ContactSchedule ----------------
        const double H = 0.1, TS = 1.0; const int N2 = 20;     // 20 nodes of 0.1 s, step 1 s
        static ContactSchedule Sched()
        {
            // leg 0 (points 0,1) loaded in nodes 10..19 (stance in the second step), leg 1 (points 2,3) loaded in nodes 0..9 and unloaded 10..19
            var f = new bool[N2 * 4];
            for (int k = 0; k < N2; k++) { bool l0 = k >= 10; for (int p = 0; p < 2; p++) { f[k * 4 + p] = l0; f[k * 4 + 2 + p] = !l0; } }
            return new ContactSchedule(f, N2, H, TS, ContactScheduleOptions.Default);
        }
        static readonly double[] Zero4 = new double[4];
        static void Tick(ContactSchedule s, double t, double fn0, double fn1, double fn2, double fn3, double[] fn, double[] z) { fn[0] = fn0; fn[1] = fn1; fn[2] = fn2; fn[3] = fn3; s.UpdateContact(t, 1000, fn, z); }

        [Test]
        public void TOff_IsTheFirstUnscheduledNode()
        {
            var s = Sched();
            Assert.That(s.TOff(2, 0.0), Is.EqualTo(1.0).Within(1e-9));      // leg 1 loaded 0..0.9, unscheduled from node 10
            Assert.That(s.TOff(2, 1.2), Is.EqualTo(1.2).Within(1e-9));      // already unloaded
            Assert.That(s.TOff(0, 1.0), Is.EqualTo(2.0).Within(1e-9));      // wraps through the cycle
        }

        [Test]
        public void Touchdown_OnTime_SwapsLegsAndReanchors()
        {
            var s = Sched(); var fn = new double[4]; var z = new double[4]; var x = new[] { 0.3, 0.4, 0.0, 0.1 }; var xr = new[] { 0.25, 0.35, 0, 0 };
            s.Reset(0); Tick(s, 0.5, 0, 0, 800, 800, fn, z); Assert.That(s.Inc[2] && s.Inc[3]);   // the stance leg is leg 1 (swing = parity 0 = leg 0)
            Tick(s, 0.5, 0, 0, 800, 800, fn, z);
            Assert.That(s.TryTouchdown(0.5, x, xr), Is.False, "nothing landed on the swing leg");
            Tick(s, 0.9, 100, 0, 800, 800, fn, z); Assert.That(s.Inc[0]);
            Assert.That(s.TryTouchdown(0.9, x, xr), Is.True);
            Assert.That(s.Parity, Is.EqualTo(1)); Assert.That(s.TAnchor, Is.EqualTo(0.9)); Assert.That(s.Xoff, Is.EqualTo(0.05).Within(1e-12)); Assert.That(s.TouchdownCount, Is.EqualTo(1));
            Assert.That(s.Late(1.0), Is.EqualTo(0));
        }

        [Test]
        public void Touchdown_Early_StaysInContactButDoesNotReanchor()
        {
            var s = Sched(); var fn = new double[4]; var z = new double[4]; var x = new double[4];
            s.Reset(0); Tick(s, 0.4, 100, 0, 800, 800, fn, z);        // 0.4 s < 0.6 ts
            Assert.That(s.TryTouchdown(0.4, x, x), Is.False); Assert.That(s.Parity, Is.EqualTo(0)); Assert.That(s.Inc[0], "early contact is kept");
            Assert.That(s.TryTouchdown(0.7, x, x), Is.True, "the early contact re-anchors once the step passes TdMinFrac"); Assert.That(s.TAnchor, Is.EqualTo(0.7));
        }

        [Test]
        public void Touchdown_Late_HoldsPhaseAndLowersTheTarget_ThenMissedFlagsAndStillReanchors()
        {
            var s = Sched(); var fn = new double[4]; var z = new double[4]; var x = new[] { 0.5, 0.5, 0, 0 }; var xr = new[] { 0.5, 0.5, 0, 0 };
            s.Reset(0);
            Assert.That(s.RefTime(1.0), Is.EqualTo(0.97).Within(1e-9)); Assert.That(s.RefTime(1.4), Is.EqualTo(0.97).Within(1e-9), "reference time is held at ts - 0.3 h");
            Assert.That(s.Late(1.0), Is.EqualTo(0.03).Within(1e-9)); Assert.That(s.LateDrop(1.5), Is.EqualTo(2.0 * 0.53).Within(1e-9)); Assert.That(s.Missed(1.2), Is.False); Assert.That(s.Missed(1.6), Is.True);
            Tick(s, 1.6, 0, 0, 800, 800, fn, z); Assert.That(s.TryTouchdown(1.6, x, xr), Is.False);
            Tick(s, 1.7, 100, 0, 800, 800, fn, z);
            Assert.That(s.TryTouchdown(1.7, x, xr), Is.True); Assert.That(s.Late(1.7), Is.EqualTo(0)); Assert.That(s.Parity, Is.EqualTo(1));
        }

        [Test]
        public void Hysteresis_HoldsAScheduledLoadedPointAtTheGround_AndFmaxRampsOutAndIn()
        {
            var s = Sched(); var fn = new double[4]; var z = new double[4]; s.Reset(0);
            Tick(s, 0.0, 0, 0, 800, 0, fn, z); Assert.That(s.Inc[2]); Assert.That(s.Inc[3], Is.False);
            z[2] = 0.0; Tick(s, 0.1, 0, 0, 5, 0, fn, z); Assert.That(s.Inc[2], "force dropout while scheduled loaded and at the ground: held");
            z[2] = 0.2; Tick(s, 0.2, 0, 0, 5, 0, fn, z); Assert.That(s.Inc[2], Is.False, "a real lift releases"); Assert.That(s.TRel[1], Is.EqualTo(0.2).Within(1e-12));
            Assert.That(s.SwingBlend(1, 0.2), Is.EqualTo(1), "stance leg while parity 0 swings leg 0");
            s.Parity = 1; Assert.That(s.SwingBlend(1, 0.2), Is.EqualTo(0).Within(1e-12)); Assert.That(s.SwingBlend(1, 0.3), Is.EqualTo(0.5).Within(1e-12)); Assert.That(s.SwingBlend(1, 5), Is.EqualTo(1));
            s.Reset(0); Tick(s, 0.0, 0, 0, 100, 0, fn, z);                          // first contact with 100 N of 1000 N at t = 0
            Assert.That(s.Fmax(2, 0.0, 0.0, 1000), Is.EqualTo(0.1).Within(1e-12));   // fIn / W
            Assert.That(s.Fmax(2, 0.04, 0.04, 1000), Is.EqualTo(0.1 + 0.04 / 0.08).Within(1e-12));   // + t / T_in
            Assert.That(s.Fmax(2, 0.9, 0.95, 1000), Is.EqualTo(4.0 * 0.05 / 0.3).Within(1e-9), "ramp to the scheduled unload at t = 1.0");
        }

        [Test]
        public void Schedule_AllocatesNothingAfterWarmUp()
        {
            var s = Sched(); var fn = new double[4]; var z = new double[4]; var x = new double[4];
            for (int i = 0; i < 10; i++) { Tick(s, i * 0.01, 0, 0, 800, 800, fn, z); s.TryTouchdown(i * 0.01, x, x); s.Fmax(2, 0.1, 0.1, 1000); s.TOff(0, 0.3); }
            long a = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) { Tick(s, i * 0.001, 0, 0, 800, 800, fn, z); s.TryTouchdown(i * 0.001, x, x); s.Fmax(2, 0.1, 0.1, 1000); s.TOff(0, 0.3); s.RefTime(0.2); }
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - a, Is.EqualTo(0));
        }
    }
}
