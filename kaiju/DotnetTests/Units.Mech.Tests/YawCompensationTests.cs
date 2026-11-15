using System;
using System.Numerics;
using Game.Units.Mech;
using NUnit.Framework;

namespace Game.Units.Mech.Tests
{
    /// <summary>Round 2 step 3: yaw-momentum compensation, engine-free pieces (trunk share estimator, friction utilisation, link momentum, config defaults).</summary>
    public class YawCompensationTests
    {
        static double Share(double k, int n = 200, int window = 100)
        {
            var w = new YawShareWindow(400);
            for (int i = 0; i < n; i++) { double l = 3e7 * Math.Sin(2 * Math.PI * i / 100.0) + 1e7 * Math.Sin(2 * Math.PI * i / 50.0); w.Push(-k * l, l); }
            return w.Share(window);
        }

        [Test] public void Share_PureCancel_IsOne() => Assert.That(Share(1.0), Is.EqualTo(1.0).Within(1e-9));
        [Test] public void Share_Half_IsHalf() => Assert.That(Share(0.5), Is.EqualTo(0.5).Within(1e-9));
        [Test] public void Share_NullUntilWindowFull_AndWhenLegsAreStill()
        {
            var w = new YawShareWindow(50);
            for (int i = 0; i < 9; i++) w.Push(-1, 1 + i);
            Assert.That(double.IsNaN(w.Share(10)), "fewer samples than the stride");
            w.Push(-1, 1); Assert.That(double.IsNaN(w.Share(10)), Is.False);
            var still = new YawShareWindow(50);
            for (int i = 0; i < 20; i++) still.Push(0, 0);
            Assert.That(double.IsNaN(still.Share(10)), "no leg momentum, no share");
            w.Clear(); Assert.That(double.IsNaN(w.Share(2)));
        }

        [Test] public void Share_UsesOnlyTheLastWindow()
        {
            var w = new YawShareWindow(30);
            for (int i = 0; i < 100; i++) w.Push(i < 90 ? 5 : -2.0 * Math.Sin(i), i < 90 ? 1 : 2.0 * Math.Sin(i));   // the old samples wrap out of the ring
            Assert.That(w.Share(10), Is.EqualTo(1.0).Within(1e-9));
        }

        [Test] public void Util_IsMomentOverBudget_AndNullWithoutBudget()
        {
            Assert.That(YawCompensation.Util(-5e7, 1.65e8), Is.EqualTo(5e7 / 1.65e8).Within(1e-12));
            Assert.That(double.IsNaN(YawCompensation.Util(5e7, 0)));
            // budget: mu / safety x load x radius fraction x leg length, two feet add
            double b = YawCompensation.GripBudget(0.9, 1.5, 1e6, 0.12, 42);
            Assert.That(b, Is.EqualTo(0.9 / 1.5 * 1e6 * 0.12 * 42).Within(1e-6));
            Assert.That(YawCompensation.GripBudget(0.9, 1.5, -5, 0.12, 42), Is.EqualTo(0));
        }

        [Test] public void LinkMomentum_SpinAndOrbit()
        {
            // a link spinning about the vertical with principal inertia 2 about y: L = 2 w; yawed 90 deg the spin axis follows the body frame
            Assert.That(YawCompensation.LinkYawMomentum(0, Vector3.Zero, Vector3.Zero, new[] { 1.0, 2.0, 3.0 }, Quaternion.Identity, new Vector3(0, 4, 0)), Is.EqualTo(8).Within(1e-5));
            var tipped = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)(Math.PI / 2));   // body z now points down: inertia about the vertical is the z one
            Assert.That(YawCompensation.LinkYawMomentum(0, Vector3.Zero, Vector3.Zero, new[] { 1.0, 2.0, 3.0 }, tipped, new Vector3(0, 4, 0)), Is.EqualTo(12).Within(1e-4));
            // orbital: mass 10 at x = 2 moving +z about the CoM: (r x v)_y = r_z v_x - r_x v_z = -2 (m 10 -> -20 times speed 3 = -60)
            Assert.That(YawCompensation.LinkYawMomentum(10, new Vector3(2, 0, 0), new Vector3(0, 0, 3), new[] { 1.0, 1.0, 1.0 }, Quaternion.Identity, Vector3.Zero), Is.EqualTo(-60).Within(1e-5));
        }

        [Test] public void ShippedConfig_HasWaistAndArmGains_AndImpStaysOff()
        {
            var t = Repo.Walk();
            Assert.That(t.imp, Is.EqualTo(0));
            Assert.That(t.imp_waist_hz, Is.EqualTo(0.8)); Assert.That(t.imp_waist_zeta, Is.EqualTo(1.0));
            Assert.That(t.imp_arm_hz, Is.EqualTo(0.6)); Assert.That(t.imp_arm_zeta, Is.EqualTo(1.0));
            StanceImpedance.PdGains(1e9, t.imp_waist_hz, t.imp_waist_zeta, out double k, out double c);
            Assert.That(k, Is.GreaterThan(0)); Assert.That(c, Is.GreaterThan(0));
        }
    }
}
