// Flag gate and Geo adapter tests (T-024 step 4). Engine-free.
using System;
using System.Collections.Generic;
using System.IO;
using Game.Core;
using Game.World.Geo;
using NUnit.Framework;

namespace Game.World.Destruction.Tests
{
    sealed class FakeRegistry : IServiceRegistry
    {
        readonly Dictionary<Type, object> _map = new Dictionary<Type, object>();
        public void Register<T>(T implementation) where T : class { _map.Add(typeof(T), implementation); }
        public T Resolve<T>() where T : class { return (T)_map[typeof(T)]; }
        public bool TryResolve<T>(out T implementation) where T : class
        {
            bool ok = _map.TryGetValue(typeof(T), out var o); implementation = ok ? (T)o : null; return ok;
        }
        public bool IsRegistered<T>() where T : class { return _map.ContainsKey(typeof(T)); }
    }

    public class DestructionWiringTests
    {
        static FeatureFlagSnapshot Flags(bool on)
        {
            return new FeatureFlagSnapshot(new[] { new KeyValuePair<string, bool>(DestructionFlags.Enabled, on) });
        }

        static double[] Square(double x, double z, double s)
        {
            return new[] { x, z, x + s, z, x + s, z + s, x, z + s };
        }

        [Test] public void FlagName_IsDestructionEnabled() { Assert.AreEqual("destruction.enabled", DestructionFlags.Enabled); }

        [Test]
        public void Install_FlagOff_RegistersNothing()
        {
            var reg = new FakeRegistry();
            Assert.IsNull(DestructionModule.Install(reg, Flags(false)));
            Assert.IsFalse(reg.IsRegistered<IDamageModel>());
            Assert.IsNull(DestructionModule.Install(reg, new FeatureFlagSnapshot(null)));   // unlisted reads off
            Assert.IsFalse(reg.IsRegistered<IDamageModel>());
        }

        [Test]
        public void Install_FlagOn_RegistersDamageModel()
        {
            var reg = new FakeRegistry();
            var m = DestructionModule.Install(reg, Flags(true));
            Assert.IsNotNull(m);
            Assert.AreSame(m, reg.Resolve<IDamageModel>());
        }

        [Test]
        public void Install_NullFlagsOrRegistry_IsOff()
        {
            var reg = new FakeRegistry();
            Assert.IsNull(DestructionModule.Install(reg, null));
            Assert.IsNull(DestructionModule.Install(null, Flags(true)));
            Assert.IsFalse(reg.IsRegistered<IDamageModel>());
        }

        [Test]
        public void FlagFile_ExistsAndDefaultsOff()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            string path = null;
            for (var d = new DirectoryInfo(dir); d != null; d = d.Parent)
            {
                string p = Path.Combine(d.FullName, "Assets", "Game", "Config", "FeatureFlags", "WorldDestruction.json");
                if (File.Exists(p)) { path = p; break; }
            }
            Assert.IsNotNull(path, "WorldDestruction.json not found");
            string text = File.ReadAllText(path);
            StringAssert.Contains("\"destruction.enabled\": false", text);
        }

        [Test]
        public void Adapter_AreaFromPolygons_OuterMinusHoles()
        {
            var b = new GeoBuildingData { HeightM = 12, AreaM2 = 999 };
            b.Polygons.Add(new List<double[]> { Square(0, 0, 10), Square(2, 2, 3) });   // 100 - 9
            b.Polygons.Add(new List<double[]> { Square(50, 50, 4) });                   // + 16
            var a = BuildingAttributesFromGeo.From(b);
            Assert.AreEqual(107.0, a.FootprintM2, 1e-9);
            Assert.AreEqual(12.0, a.HeightM, 1e-12);
        }

        [Test]
        public void Adapter_Defaults_LevelsUnknownNotResidential()
        {
            var b = new GeoBuildingData { HeightM = 20 };
            b.Polygons.Add(new List<double[]> { Square(0, 0, 5) });
            var a = BuildingAttributesFromGeo.From(b);
            Assert.AreEqual(0, a.Levels);
            Assert.IsFalse(a.Residential);
        }

        [Test]
        public void Adapter_NoPolygons_FallsBackToAreaThenZero()
        {
            Assert.AreEqual(80.0, BuildingAttributesFromGeo.From(new GeoBuildingData { HeightM = 5, AreaM2 = 80 }).FootprintM2, 1e-12);
            Assert.AreEqual(0.0, BuildingAttributesFromGeo.From(new GeoBuildingData { HeightM = 5 }).FootprintM2, 1e-12);
        }

        [Test]
        public void Adapter_ClockwiseRing_SamePositiveArea()
        {
            var b = new GeoBuildingData { HeightM = 5 };
            b.Polygons.Add(new List<double[]> { new[] { 0.0, 0, 0, 6, 4, 6, 4, 0 } });
            Assert.AreEqual(24.0, BuildingAttributesFromGeo.From(b).FootprintM2, 1e-12);
        }

        [Test]
        public void Adapter_Result_RegistersAndDamagesInModel()
        {
            var b = new GeoBuildingData { Id = BuildingId.From(BuildingSource.OsmWay, 7), HeightM = 25 };
            b.Polygons.Add(new List<double[]> { Square(0, 0, 20) });
            var model = DestructionModule.Install(new FakeRegistry(), Flags(true));
            model.Register(b.Id, BuildingAttributesFromGeo.From(b));
            Assert.IsTrue(model.TryGetState(b.Id, out var s));
            Assert.AreEqual(0, s.State);
            Assert.IsTrue(model.TryGetThresholds(b.Id, out var e1, out _, out _, out _));
            Assert.Greater(e1, 0);
        }

        [Test]
        public void Adapter_Null_Throws() { Assert.Throws<ArgumentNullException>(() => BuildingAttributesFromGeo.From(null)); }
    }
}
