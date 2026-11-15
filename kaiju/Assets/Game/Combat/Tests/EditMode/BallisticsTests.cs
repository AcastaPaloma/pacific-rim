// Analytic and determinism checks for the fixed-step ballistics (R-027 section 1). M-061 draft: ballistic accuracy vs analytic.
using System;
using Game.Core;
using NUnit.Framework;

namespace Game.Combat.Tests
{
    public class BallisticsTests
    {
        static BallisticEnvironment Env(double g = 9.80665) { var e = BallisticEnvironment.Standard(); e.Gravity = g; return e; }

        [Test]
        public void NoDrag_MatchesParabolaExactly()
        {
            var env = Env();
            CVec3 pos = new CVec3(0, 10, 0), vel = new CVec3(300, 400, 0);
            double dt = 0.01, T = 20; int n = (int)Math.Round(T / dt);
            for (int i = 0; i < n; i++) Ballistics.Step(ref pos, ref vel, 100, 0.1, DragCurve.Constant(0), env, dt);
            double x = 300 * T, y = 10 + 400 * T - 0.5 * env.Gravity * T * T;
            Assert.AreEqual(x, pos.X, 1e-9 * x);
            Assert.AreEqual(y, pos.Y, 1e-9 * Math.Abs(y) + 1e-9);
            Assert.AreEqual(400 - env.Gravity * T, vel.Y, 1e-9);
        }

        [Test]
        public void VerticalFallWithDrag_MatchesTanhSolution()
        {
            var env = Env(); double m = 50, A = 0.05, cd = 0.3;
            double k = 0.5 * env.AirDensity * cd * A / m, vt = Math.Sqrt(env.Gravity / k);
            CVec3 pos = new CVec3(0, 0, 0), vel = CVec3.Zero;
            double dt = 0.005, T = 25; int n = (int)Math.Round(T / dt);
            for (int i = 0; i < n; i++) Ballistics.Step(ref pos, ref vel, m, A, DragCurve.Constant(cd), env, dt);
            double yExact = -(vt * vt / env.Gravity) * Math.Log(Math.Cosh(env.Gravity * T / vt));
            double vExact = -vt * Math.Tanh(env.Gravity * T / vt);
            Assert.AreEqual(yExact, pos.Y, 1e-8 * Math.Abs(yExact));
            Assert.AreEqual(vExact, vel.Y, 1e-8 * Math.Abs(vExact));
        }

        [Test]
        public void HorizontalFlightWithDrag_MatchesHyperbolicSolution()
        {
            var env = Env(0); double m = 1225, A = Math.PI * 0.203 * 0.203, cd = 0.25;
            double k = 0.5 * env.AirDensity * cd * A / m, v0 = 762;
            CVec3 pos = CVec3.Zero, vel = new CVec3(v0, 0, 0);
            double dt = 0.02, T = 40; int n = (int)Math.Round(T / dt);
            for (int i = 0; i < n; i++) Ballistics.Step(ref pos, ref vel, m, A, DragCurve.Constant(cd), env, dt);
            double xExact = Math.Log(1 + k * v0 * T) / k, vExact = v0 / (1 + k * v0 * T);
            Assert.AreEqual(xExact, pos.X, 1e-9 * xExact);
            Assert.AreEqual(vExact, vel.X, 1e-9 * vExact);
        }

        [Test]
        public void ErrorShrinksFourthOrder_WhenStepHalves()
        {
            var env = Env(0); double m = 100, A = 0.2, cd = 0.4, v0 = 500;
            double k = 0.5 * env.AirDensity * cd * A / m, T = 10, xExact = Math.Log(1 + k * v0 * T) / k;
            double Err(double dt)
            {
                CVec3 pos = CVec3.Zero, vel = new CVec3(v0, 0, 0);
                int n = (int)Math.Round(T / dt);
                for (int i = 0; i < n; i++) Ballistics.Step(ref pos, ref vel, m, A, DragCurve.Constant(cd), env, dt);
                return Math.Abs(pos.X - xExact);
            }
            double e1 = Err(0.5), e2 = Err(0.25);
            Assert.Greater(e1 / e2, 12.0, "RK4 error ratio " + (e1 / e2));
        }

        [Test]
        public void Determinism_TwoRunsAreBitEqual()
        {
            CVec3 Run()
            {
                var env = Env(); env.Wind = new CVec3(5, 0, -3);
                var c = CannonSpec.Heavy(); CVec3 pos = new CVec3(0, 20, 0), vel = new CVec3(500, 450, 100);
                for (int i = 0; i < 3000; i++) Ballistics.Step(ref pos, ref vel, c.ShellMassKg, c.ReferenceAreaM2, c.Drag, env, 0.02);
                return pos;
            }
            var a = Run(); var b = Run();
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(a.X), BitConverter.DoubleToInt64Bits(b.X));
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(a.Y), BitConverter.DoubleToInt64Bits(b.Y));
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(a.Z), BitConverter.DoubleToInt64Bits(b.Z));
        }

        [Test]
        public void DragNeverAddsEnergy_AlongAFlight()
        {
            var env = Env(); var c = CannonSpec.Heavy();
            CVec3 pos = new CVec3(0, 0, 0), vel = new CVec3(762 * 0.7071, 762 * 0.7071, 0);
            double E0 = 0.5 * vel.LengthSq + env.Gravity * pos.Y, prev = E0;
            for (int i = 0; i < 6000; i++)
            {
                Ballistics.Step(ref pos, ref vel, c.ShellMassKg, c.ReferenceAreaM2, c.Drag, env, 0.02);
                double E = 0.5 * vel.LengthSq + env.Gravity * pos.Y;
                Assert.LessOrEqual(E, prev + 1e-6, "specific energy rose at step " + i);
                prev = E;
                if (pos.Y < 0) break;
            }
            Assert.Less(prev, E0);
        }

        [Test]
        public void HeavyCannon_RangeIsPlausibleFractionOfVacuumRange()
        {
            var c = CannonSpec.Heavy(); var env = Env();
            double th = Math.PI / 4;
            CVec3 landing, lv; double t;
            bool ok = Ballistics.FlyToGround(new CVec3(0, 0, 0.0001), new CVec3(c.MuzzleSpeed * Math.Cos(th), c.MuzzleSpeed * Math.Sin(th), 0), c.ShellMassKg, c.ReferenceAreaM2, c.Drag, env, 0.02, 400, out landing, out t, out lv);
            Assert.IsTrue(ok);
            double vacuum = c.MuzzleSpeed * c.MuzzleSpeed / env.Gravity, ratio = landing.X / vacuum;
            TestContext.WriteLine("Heavy 45 deg: range " + landing.X + " m, vacuum " + vacuum + " m, ratio " + ratio + ", flight " + t + " s");
            Assert.That(ratio, Is.InRange(0.45, 0.85));
            Assert.Less(lv.Length, c.MuzzleSpeed);
        }

        [Test]
        public void MuzzleEnergy_And_Recoil_Formulas()
        {
            var c = CannonSpec.Heavy();
            Assert.AreEqual(0.5 * 1225 * 762 * 762, c.MuzzleEnergyJ, 1);
            Assert.AreEqual(356e6, c.MuzzleEnergyJ, 0.01 * 356e6);
            Assert.AreEqual((1225 + 1.25 * 300) * 762, c.RecoilImpulseNs, 1e-6);
            Assert.Greater(c.RecoilImpulseNs, c.ShellMassKg * c.MuzzleSpeed);
        }

        [Test]
        public void DragCurve_Interpolates_Clamps_AndValidates()
        {
            var d = new DragCurve(new[] { 0.0, 1.0, 2.0 }, new[] { 0.1, 0.3, 0.2 });
            Assert.AreEqual(0.1, d.Cd(-1), 1e-12);
            Assert.AreEqual(0.2, d.Cd(0.5), 1e-12);
            Assert.AreEqual(0.25, d.Cd(1.5), 1e-12);
            Assert.AreEqual(0.2, d.Cd(9), 1e-12);
            Assert.AreEqual(0.4, DragCurve.Constant(0.4).Cd(3), 1e-12);
            Assert.Throws<ArgumentException>(() => new DragCurve(new[] { 1.0, 1.0 }, new[] { 0.1, 0.1 }));
            Assert.Throws<ArgumentException>(() => new DragCurve(new[] { 0.0 }, new[] { -0.1 }));
        }

        [Test]
        public void Wind_PushesTheShell()
        {
            var env = Env(0); env.Wind = new CVec3(0, 0, 20);
            CVec3 pos = CVec3.Zero, vel = new CVec3(300, 0, 0);
            for (int i = 0; i < 500; i++) Ballistics.Step(ref pos, ref vel, 46, 0.019, DragCurve.Constant(0.3), env, 0.02);
            Assert.Greater(pos.Z, 0);
        }
    }
}
