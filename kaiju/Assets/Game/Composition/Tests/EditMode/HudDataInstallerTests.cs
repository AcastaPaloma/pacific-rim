// T-152b: Combat ammo query -> HUD IAmmoSource adapter and its flag gate. Engine-free: dotnet (Composition.Adapters.Tests) and Unity EditMode.
using System;
using System.Collections.Generic;
using Game.Combat;
using Game.Composition;
using Game.Core;
using Game.UI;
using NUnit.Framework;

namespace Game.Composition.Tests
{
    public class HudDataInstallerTests
    {
        sealed class Registry : IServiceRegistry
        {
            readonly Dictionary<Type, object> _map = new Dictionary<Type, object>();
            public void Register<T>(T implementation) where T : class { _map.Add(typeof(T), implementation); }
            public T Resolve<T>() where T : class { return (T)_map[typeof(T)]; }
            public bool TryResolve<T>(out T implementation) where T : class { bool ok = _map.TryGetValue(typeof(T), out var o); implementation = ok ? (T)o : null; return ok; }
            public bool IsRegistered<T>() where T : class { return _map.ContainsKey(typeof(T)); }
            public int Count => _map.Count;
        }

        sealed class Mount : IWeaponMount
        {
            public EntityId Id { get; }
            public Mount(ulong id) { Id = EntityId.FromValue(id); }
            public void GetPose(out CVec3 position, out double heading, out CVec3 velocity) { position = CVec3.Zero; heading = 0; velocity = CVec3.Zero; }
        }

        static IFeatureFlags Flags(bool on) => new FeatureFlagSnapshot(new List<KeyValuePair<string, bool>> { new KeyValuePair<string, bool>(HudDataInstaller.Flag, on) });

        static CombatSystem System(out CannonMount m, int ammo = 3, int reload = 10)
        {
            var sys = new CombatSystem(null, new SimpleTargetWorld(), new ImpactRouter(null, null, null));
            var spec = CannonSpec.Heavy(); spec.MaxAmmo = ammo; spec.ReloadSteps = reload;
            m = sys.AddCannon(new Mount(7), spec);
            return sys;
        }

        [Test]
        public void FlagOff_InstallsNothing()
        {
            var reg = new Registry(); var sys = System(out _); reg.Register<IAmmoQuery>(sys);
            Assert.IsNull(HudDataInstaller.Install(reg, Flags(false)));
            Assert.IsFalse(reg.IsRegistered<IAmmoSource>());
            Assert.AreEqual(1, reg.Count);
        }

        [Test]
        public void FlagOn_NoQuery_InstallsNothing()
        {
            var reg = new Registry();
            Assert.IsNull(HudDataInstaller.Install(reg, Flags(true)));
            Assert.AreEqual(0, reg.Count);
        }

        [Test]
        public void FlagOn_RegistersSource_ReportsAmmoAndReload()
        {
            var reg = new Registry(); var sys = System(out var m); reg.Register<IAmmoQuery>(sys);
            var src = HudDataInstaller.Install(reg, Flags(true));
            Assert.IsNotNull(src); Assert.AreSame(src, reg.Resolve<IAmmoSource>());
            Assert.IsTrue(src.TryGetAmmo(7, out var st));
            Assert.AreEqual(3, st.Count); Assert.AreEqual(3, st.Max); Assert.IsFalse(st.Reloading); Assert.AreEqual(1f, st.ReloadFrac);
            Assert.IsTrue(sys.Fire(m, 0, 0));
            Assert.IsTrue(src.TryGetAmmo(7, out st));
            Assert.AreEqual(2, st.Count); Assert.IsTrue(st.Reloading); Assert.AreEqual(0f, st.ReloadFrac);
            for (int i = 0; i < 4; i++) m.Cannon.Step(0.02, i, null, null);
            Assert.IsTrue(src.TryGetAmmo(7, out st));
            Assert.AreEqual(0.4f, st.ReloadFrac, 1e-5f);
        }

        [Test]
        public void UnknownOrInvalidMech_ReportsNothing()
        {
            var src = new CombatAmmoSource(System(out _));
            Assert.IsFalse(src.TryGetAmmo(8, out _));
            Assert.IsFalse(src.TryGetAmmo(0, out _));
            Assert.IsFalse(src.TryGetAmmo(-3, out _));
        }

        [Test]
        public void ProviderOverAdapter_GivesAmmoOnlyLimits()
        {
            var p = new CannonAmmoProvider(new CombatAmmoSource(System(out _)));
            Assert.IsTrue(p.TryGetLimits(7, out var v));
            Assert.AreEqual(3, v.AmmoCount); Assert.AreEqual(3, v.AmmoMax);
            Assert.IsFalse(p.TryGetLimits(9, out _));
        }

        // ---- T-154c: health query -> IHealthSource ----
        [Test]
        public void Health_FlagOff_InstallsNothing()
        {
            var reg = new Registry(); var h = new LinearEntityHealth(); reg.Register<IHealthQuery>(h);
            Assert.IsNull(HudDataInstaller.InstallHealth(reg, Flags(false)));
            Assert.IsFalse(reg.IsRegistered<IHealthSource>());
            Assert.IsNull(HudDataInstaller.InstallHealth(new Registry(), Flags(true)));
        }

        [Test]
        public void Health_FlagOn_ReportsFractionAndFeedsLimitsProvider()
        {
            var reg = new Registry(); var h = new LinearEntityHealth(); h.Register(EntityId.FromValue(7), 200); reg.Register<IHealthQuery>(h);
            var src = HudDataInstaller.InstallHealth(reg, Flags(true));
            Assert.IsNotNull(src); Assert.AreSame(src, reg.Resolve<IHealthSource>());
            Assert.IsTrue(src.TryGetHealthFraction(7, out float f)); Assert.AreEqual(1f, f);
            var rec = default(ImpactRecord); rec.Target = EntityId.FromValue(7); rec.EnergyJ = 200e6; // 30 hp
            h.TryApply(rec.Target, rec, out _);
            Assert.IsTrue(src.TryGetHealthFraction(7, out f)); Assert.AreEqual(0.85f, f, 1e-5f);
            var p = new HealthLimitsProvider(new CannonAmmoProvider(new CombatAmmoSource(System(out _))), src);
            Assert.IsTrue(p.TryGetLimits(7, out var v));
            Assert.AreEqual(3, v.AmmoCount); Assert.IsTrue(v.Has(LimitsAvailability.Health)); Assert.AreEqual(0.85f, v.HealthFrac, 1e-5f);
        }

        [Test]
        public void Health_UnknownOrInvalidMech_ReportsNothing()
        {
            var src = new CombatHealthSource(new LinearEntityHealth());
            Assert.IsFalse(src.TryGetHealthFraction(8, out _)); Assert.IsFalse(src.TryGetHealthFraction(0, out _)); Assert.IsFalse(src.TryGetHealthFraction(-1, out _));
        }
    }
}
