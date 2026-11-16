// T-152b: read-only ammo query on Cannon / CombatSystem.
using Game.Combat;
using Game.Core;
using NUnit.Framework;

namespace Game.Combat.Tests
{
    public class AmmoQueryTests
    {
        sealed class Mount : IWeaponMount
        {
            public EntityId Id { get; }
            public Mount(ulong id) { Id = EntityId.FromValue(id); }
            public void GetPose(out CVec3 position, out double heading, out CVec3 velocity) { position = CVec3.Zero; heading = 0; velocity = CVec3.Zero; }
        }

        [Test]
        public void Status_TracksAmmoAndReload_WithoutChangingBehaviour()
        {
            var spec = CannonSpec.Heavy(); spec.MaxAmmo = 2; spec.ReloadSteps = 5;
            var c = new Cannon(spec); var w = new SimpleTargetWorld();
            var s = c.GetAmmoStatus();
            Assert.AreEqual(2, s.Ammo); Assert.AreEqual(2, s.Capacity); Assert.AreEqual(1.0, s.ReloadFrac); Assert.IsFalse(s.Reloading);
            Assert.IsTrue(c.TryFire(default, CVec3.Zero, new CVec3(0, 0, 1), CVec3.Zero, 0, null, out _));
            s = c.GetAmmoStatus();
            Assert.AreEqual(1, s.Ammo); Assert.AreEqual(0.0, s.ReloadFrac); Assert.IsTrue(s.Reloading); Assert.AreEqual(5, c.CooldownSteps);
            c.Step(0.02, 0, w, null); c.Step(0.02, 1, w, null);
            Assert.AreEqual(0.4, c.ReloadFrac, 1e-12);
            Assert.IsFalse(c.CanFire);
            for (int i = 2; i < 5; i++) c.Step(0.02, i, w, null);
            Assert.AreEqual(1.0, c.ReloadFrac); Assert.IsTrue(c.CanFire);
            // querying never mutates
            c.GetAmmoStatus(); c.GetAmmoStatus();
            Assert.AreEqual(1, c.Ammo);
        }

        [Test]
        public void Struct_ClampsAndRejectsNaN()
        {
            Assert.AreEqual(0.0, new AmmoStatus(1, 2, double.NaN).ReloadFrac);
            Assert.AreEqual(0.0, new AmmoStatus(1, 2, -1).ReloadFrac);
            Assert.AreEqual(1.0, new AmmoStatus(1, 2, 7).ReloadFrac);
        }

        [Test]
        public void CombatSystem_QueriesByMountId()
        {
            var sys = new CombatSystem(null, new SimpleTargetWorld(), new ImpactRouter(null, null, null));
            sys.AddCannon(new Mount(3), CannonSpec.Light());
            sys.AddCannon(new Mount(4), CannonSpec.Heavy());
            IAmmoQuery q = sys;
            Assert.IsTrue(q.TryGetAmmo(EntityId.FromValue(4), out var s)); Assert.AreEqual(20, s.Capacity);
            Assert.IsTrue(q.TryGetAmmo(EntityId.FromValue(3), out s)); Assert.AreEqual(60, s.Capacity);
            Assert.IsFalse(q.TryGetAmmo(EntityId.FromValue(5), out _));
        }
    }
}
