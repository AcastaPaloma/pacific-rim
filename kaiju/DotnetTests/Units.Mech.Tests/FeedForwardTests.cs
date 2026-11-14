using System;
using Game.Units.Mech;
using Game.Units.Mech.Contracts;
using NUnit.Framework;

namespace Game.Units.Mech.Tests
{
    /// <summary>Round 2 step 4: inverse-dynamics feed-forward, engine-free pieces.</summary>
    public class FeedForwardTests
    {
        [Test]
        public void Sum_AppliesGains_AndStanceGravityShare()
        {
            var t = Repo.Walk();
            t.imp_ff_gravity = 2; t.imp_ff_coriolis = 3; t.imp_ff_accel = 5;
            Assert.That(InverseDynamicsFeedForward.Torque(t, 10, 100, 1000, 1, 1e9), Is.EqualTo(2 * 10 + 3 * 100 + 5 * 1000));
            Assert.That(InverseDynamicsFeedForward.Torque(t, 10, 100, 1000, 0.5, 1e9), Is.EqualTo(2 * 0.5 * 10 + 3 * 100 + 5 * 1000));
        }

        [Test]
        public void ZeroGains_GiveZero_AndShippedDefaultsAreOne()
        {
            var t = Repo.Walk();
            Assert.That(t.imp, Is.EqualTo(0));
            Assert.That(t.imp_ff_gravity, Is.EqualTo(1)); Assert.That(t.imp_ff_coriolis, Is.EqualTo(1)); Assert.That(t.imp_ff_accel, Is.EqualTo(1)); Assert.That(t.imp_ff_stance_gravity_share, Is.EqualTo(1));
            t.imp_ff_gravity = 0; t.imp_ff_coriolis = 0; t.imp_ff_accel = 0;
            Assert.That(InverseDynamicsFeedForward.Torque(t, 10, 100, 1000, 1, 1e9), Is.EqualTo(0));
        }

        [Test]
        public void Total_IsClampedToTheDriveForceLimit_BothSigns()
        {
            var t = Repo.Walk();
            Assert.That(InverseDynamicsFeedForward.Torque(t, 8e8, 1e8, 1e8, 1, 5e8), Is.EqualTo(5e8));
            Assert.That(InverseDynamicsFeedForward.Torque(t, -8e8, -1e8, 0, 1, 5e8), Is.EqualTo(-5e8));
            Assert.That(InverseDynamicsFeedForward.Torque(t, 1e8, 0, 0, 1, 5e8), Is.EqualTo(1e8));
        }

        [Test]
        public void ReferenceAcceleration_OfASinusoid_IsMinusWSquaredQ()
        {
            double stride = 2.4, amp = 0.3, w = 2 * Math.PI / stride, phase = 0.37, h = InverseDynamicsFeedForward.PhaseStep;
            Func<double, double> q = p => amp * Math.Sin(2 * Math.PI * p);
            double qdd = InverseDynamicsFeedForward.SecondDerivative(q(phase - h), q(phase), q(phase + h), h, stride);
            Assert.That(qdd, Is.EqualTo(-w * w * q(phase)).Within(0.01 * w * w * amp));   // central difference error O(h^2)
        }
    }
}
