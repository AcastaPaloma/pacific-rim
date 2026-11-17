// T-154c: read-only health query on LinearEntityHealth.
using Game.Combat;
using Game.Core;
using NUnit.Framework;

namespace Game.Combat.Tests
{
    public class HealthQueryTests
    {
        static ImpactRecord Hit(EntityId target, double joules) { var r = default(ImpactRecord); r.Target = target; r.EnergyJ = joules; return r; }

        [Test]
        public void Status_ClampsAndReportsFraction()
        {
            Assert.AreEqual(0.5, new HealthStatus(50, 100).Fraction, 1e-12);
            Assert.AreEqual(1.0, new HealthStatus(500, 100).Fraction);
            Assert.AreEqual(0.0, new HealthStatus(-5, 100).Fraction);
            Assert.AreEqual(0.0, new HealthStatus(double.NaN, 100).Fraction);
            Assert.AreEqual(0.0, new HealthStatus(10, 0).Fraction);
            Assert.AreEqual(0.0, new HealthStatus(10, double.NaN).Fraction);
        }

        [Test]
        public void LinearHealth_ReportsCurrentOverRegistered_AndQueryDoesNotMutate()
        {
            var h = new LinearEntityHealth(); var id = EntityId.FromValue(11);
            Assert.IsFalse(h.TryGetHealth(id, out _));
            h.Register(id, 100.0);
            Assert.IsTrue(h.TryGetHealth(id, out var s));
            Assert.AreEqual(100.0, s.HitPoints); Assert.AreEqual(100.0, s.MaxHitPoints); Assert.AreEqual(1.0, s.Fraction);
            // 100 MJ * 0.15 hp/MJ = 15 hp
            Assert.IsTrue(h.TryApply(id, Hit(id, 100e6), out double removed));
            Assert.AreEqual(15.0, removed, 1e-9);
            h.TryGetHealth(id, out s); h.TryGetHealth(id, out s);
            Assert.AreEqual(85.0, s.HitPoints, 1e-9); Assert.AreEqual(100.0, s.MaxHitPoints); Assert.AreEqual(0.85, s.Fraction, 1e-9);
            Assert.IsTrue(h.TryGetHitPoints(id, out double hp)); Assert.AreEqual(85.0, hp, 1e-9);
        }

        [Test]
        public void LinearHealth_FloorsAtZero()
        {
            var h = new LinearEntityHealth(); var id = EntityId.FromValue(3); h.Register(id, 10);
            h.TryApply(id, Hit(id, 1e12), out _);
            Assert.IsTrue(h.TryGetHealth(id, out var s)); Assert.AreEqual(0.0, s.Fraction);
        }
    }
}
