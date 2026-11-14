// Unity-only World.Geo tests (need the engine): scene build, colliders, removal, floating origin, flag, module rules.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Core;
using Game.World.Geo.Tests;
using NUnit.Framework;
using UnityEngine;

namespace Game.World.Geo.Unity.Tests
{
    sealed class RegistryStub : IServiceRegistry
    {
        public readonly Dictionary<Type, object> Map = new Dictionary<Type, object>();
        public void Register<T>(T impl) where T : class => Map.Add(typeof(T), impl);
        public T Resolve<T>() where T : class => (T)Map[typeof(T)];
        public bool TryResolve<T>(out T impl) where T : class { impl = Map.TryGetValue(typeof(T), out var o) ? (T)o : null; return impl != null; }
        public bool IsRegistered<T>() where T : class => Map.ContainsKey(typeof(T));
    }

    public class GeoUnityTests
    {
        static GeoTileInstance _tile;
        static GeoTileInstance Tile()
        {
            if (_tile != null) return _tile;
            var id = GeoTileId.From(0, 30, 36);
            _tile = GeoTileBuilder.Build(GeoTestData.M0Dir, new GeoBuildOptions { FloatingOriginX = id.OriginX, FloatingOriginZ = id.OriginZ, Settings = GeoTestData.Settings(), BuildingColliders = true });
            Physics.SyncTransforms();
            return _tile;
        }

        [OneTimeTearDown]
        public void TearDown() { if (_tile != null) UnityEngine.Object.DestroyImmediate(_tile.gameObject); _tile = null; }

        [Test]
        public void FeatureFlag_FileExists_AndDefaultsOff()
        {
            var p = Path.Combine(Application.dataPath, "Game", "Config", "FeatureFlags", "WorldGeo.json");
            var flags = GeoJson.Obj(GeoJson.Get(GeoJson.Obj(GeoJson.Parse(File.ReadAllText(p))), "flags"));
            Assert.IsTrue(flags.ContainsKey(GeoFlags.Enabled));
            foreach (var kv in flags) Assert.AreEqual(false, kv.Value, kv.Key + " must ship off");
        }

        [Test]
        public void Module_InstallsOnlyWhenFlagOn()
        {
            var reg = new RegistryStub();
            Assert.IsNull(GeoModule.Install(reg, FeatureFlagSnapshot.AllOff));
            Assert.IsFalse(reg.IsRegistered<IGeoWorld>());
            Assert.IsNotNull(GeoModule.Install(reg, new FeatureFlagSnapshot(new[] { new KeyValuePair<string, bool>(GeoFlags.Enabled, true) })));
            Assert.IsTrue(reg.IsRegistered<IGeoWorld>());
        }

        [Test]
        public void Builder_RefusesDoctoredManifest()
        {
            var dir = GeoTestData.Doctored(m => ((Dictionary<string, object>)((List<object>)m["layers"])[3]).Remove("sources"));
            Assert.Throws<GeoTileRejectedException>(() => GeoTileBuilder.Build(dir, new GeoBuildOptions { FloatingOriginX = 61440, FloatingOriginZ = 73728, VerifyHashes = false }));
            Assert.AreEqual(0, UnityEngine.Object.FindObjectsByType<GeoTileInstance>(FindObjectsSortMode.None).Count(t => t != _tile), "no half-built tile left behind");
        }

        [Test]
        public void Tile_HasAddressableBuildings_WithCollidersAndChunkedRenderers()
        {
            var t = Tile();
            var gbs = t.GetComponentsInChildren<GeoBuilding>();
            Assert.AreEqual(t.Model.Buildings.Count, gbs.Length);
            int withMesh = t.Model.Buildings.Count(b => b.Mesh != null);
            Assert.AreEqual(withMesh, t.GetComponentsInChildren<MeshCollider>().Length);
            Assert.LessOrEqual(t.Renderers.Count, 200, "draw calls (one per renderer) within the 200 target");
            Assert.LessOrEqual(t.TriangleCount(), 1500000);
            Assert.IsNotNull(t.PhysicsSurface);
        }

        [Test]
        public void PhysicsSurface_MatchesData_AndWaterLevelIsMsl()
        {
            var t = Tile();
            // Coit Tower hill ~ 91 m NAVD88; open water east of the piers ~ -15 m
            Assert.AreEqual(t.Model.Heights.SampleData(61591.4 - 61440, 75450.7 - 73728), t.RaycastTerrain(61591.4, 75450.7), 0.02);
            Assert.Greater(t.RaycastTerrain(61591.4, 75450.7), 88.0);
            Assert.AreEqual(0.969, t.Model.Context.WaterLevelNavd88, 1e-9);
        }

        [Test]
        public void FloatingOrigin_ShiftByTileMultiple_KeepsWorldPositionsWithin1mm()
        {
            var t = Tile();
            var pts = new List<double[]> { new[] { 61591.4, 75450.7 }, new[] { 62375.6, 74051.1 }, new[] { 63300.0, 74900.0 } };
            var a = t.Export(pts);
            t.ShiftOrigin(61440 + 2048, 73728 - 4096);
            var b = t.Export(pts);
            t.ShiftOrigin(61440, 73728);
            var ba = (List<object>)a["buildings"]; var bb = (List<object>)b["buildings"];
            for (int i = 0; i < ba.Count; i++)
            {
                var p = (List<object>)((Dictionary<string, object>)ba[i])["anchor_baytm"];
                var q = (List<object>)((Dictionary<string, object>)bb[i])["anchor_baytm"];
                Assert.AreEqual((double)p[0], (double)q[0], 1e-3);
                Assert.AreEqual((double)p[1], (double)q[1], 1e-3);
            }
            var sa = (List<object>)a["terrain_samples"]; var sb = (List<object>)b["terrain_samples"];
            for (int i = 0; i < sa.Count; i++) Assert.AreEqual((double)((List<object>)sa[i])[2], (double)((List<object>)sb[i])[2], 1e-3);
            Assert.LessOrEqual((double)((Dictionary<string, object>)b["stats"])["max_abs_scene_position_m"], 4096.0 + 64);
        }

        [Test]
        public void RemoveBuilding_ByBid_AtRuntime_WithoutRebake()
        {
            var t = Tile();
            var svc = new GeoWorldService();
            svc.Add(t);
            var mill = BuildingsLayer.ParseBid("osm:w72963089");
            Assert.IsTrue(svc.TryGetBuilding(mill, out var info));
            Assert.AreEqual(197.0, info.TopNavd88M - info.BaseNavd88M, 1e-6);
            long before = t.TriangleCount("BuildingChunks");
            Assert.AreEqual(1, svc.RemoveBuilding(mill));
            Assert.IsTrue(svc.IsRemoved(mill));
            Assert.IsFalse(t.HasObject(mill));
            Assert.Less(t.TriangleCount("BuildingChunks"), before);
            var tam = BuildingsLayer.ParseBid("osm:w24222973");
            Assert.GreaterOrEqual(svc.RemoveBuilding(tam), 5);
            Assert.IsTrue(t.Model.PartsOf[tam].All(p => !t.HasObject(p.Id)));
            _tile = null;  // modified: rebuild for later tests
            UnityEngine.Object.DestroyImmediate(t.gameObject);
        }

        [Test]
        public void ModuleRules_AsmdefsReferenceOnlyCoreContractsAndOwnAssemblies()
        {
            var root = Path.Combine(Application.dataPath, "Game", "World", "Geo");
            var allowed = new HashSet<string> { "Game.Core.Contracts", "Game.World.Geo.Contracts", "Game.World.Geo", "Game.World.Geo.Unity", "Game.World.Geo.Tests",
                "UnityEngine.TestRunner", "UnityEditor.TestRunner" };
            foreach (var f in Directory.GetFiles(root, "*.asmdef", SearchOption.AllDirectories))
            {
                var refs = GeoJson.Arr(GeoJson.Get(GeoJson.Obj(GeoJson.Parse(File.ReadAllText(f))), "references"));
                foreach (var r in refs) Assert.IsTrue(allowed.Contains((string)r), Path.GetFileName(f) + " references " + r);
            }
        }
    }
}
