// Multi-tile Unity adapter (T-021 step 2): staged build of a 2x1 set, placement, cross-tile terrain sampling, building lookup.
using System.IO;
using System.Linq;
using Game.Core;
using Game.World.Geo.Tests;
using NUnit.Framework;
using UnityEngine;

namespace Game.World.Geo.Unity.Tests
{
    [Category("GeoMultiTile")]
    public class GeoMultiTileTests
    {
        static GeoTileSetBuilder _b;
        static readonly GeoTileId A = GeoTileId.From(0, 30, 36), B = GeoTileId.From(0, 30, 35);

        static string TilesRoot => Path.Combine(GeoTestData.ProjectRoot(), "Assets", "StreamingAssets", "Geo", "tiles", "v1");

        [OneTimeSetUp]
        public void Build()
        {
            var ids = new[] { A, B };
            var absent = GeoTileSetLoader.Absent(TilesRoot, ids);
            if (absent.Count > 0) Assert.Ignore("tile folders absent: " + string.Join(", ", absent));
            var set = GeoTileSetLoader.Load(TilesRoot, ids, verifyHashes: false);
            _b = new GeoTileSetBuilder(set, A, GeoTestData.Settings()) { BuildingColliders = false, TerrainCollider = false };
            Assert.AreEqual(2, _b.Pending);
            Assert.IsNotNull(_b.BuildNext(), "first call builds one tile");
            Assert.AreEqual(1, _b.Instances.Count);
            Assert.AreEqual(1, _b.World.Tiles.Count);
            Assert.IsNotNull(_b.BuildNext());
            Assert.IsNull(_b.BuildNext());
            Assert.IsTrue(_b.Done);
        }

        [OneTimeTearDown]
        public void TearDown() { if (_b != null) Object.DestroyImmediate(_b.Root); _b = null; }

        [Test]
        public void TwoRoots_AtTileOffset_2048m()
        {
            Assert.AreEqual(2, _b.Instances.Count);
            Assert.AreEqual(2, _b.Root.GetComponentsInChildren<GeoTileInstance>().Length);
            var a = _b.Instances[0].transform.position; var b = _b.Instances[1].transform.position;
            Assert.AreEqual(0f, a.x); Assert.AreEqual(0f, a.z);
            Assert.AreEqual(0f, b.x); Assert.AreEqual(-2048f, b.z, "L0_30_35 is one tile south of L0_30_36");
        }

        [Test]
        public void FloatingOriginShift_MovesAllRoots()
        {
            _b.ShiftOrigin(A.OriginX + 2048, A.OriginZ);
            try
            {
                Assert.AreEqual(-2048f, _b.Instances[0].transform.position.x);
                Assert.AreEqual(-2048f, _b.Instances[1].transform.position.x);
                Assert.AreEqual(-2048f, _b.Instances[1].transform.position.z);
            }
            finally { _b.ShiftOrigin(A.OriginX, A.OriginZ); }
            Assert.AreEqual(-2048f, _b.Instances[1].transform.position.z);
            Assert.AreEqual(0f, _b.Instances[1].transform.position.x);
        }

        [Test]
        public void SampleTerrain_AcrossSharedEdge_Agrees()
        {
            var w = _b.World;
            double z0 = A.OriginZ;   // shared edge: north edge of B, south edge of A
            for (int k = 0; k < 8; k++)
            {
                double x = A.OriginX + 100 + k * 250;
                double north = w.SampleTerrain(x, z0);          // tile A
                double south = w.SampleTerrain(x, z0 - 1e-4);   // tile B
                Assert.IsFalse(double.IsNaN(north) || double.IsNaN(south));
                Assert.AreEqual(north, south, 0.01, "x=" + x);
            }
            Assert.IsTrue(double.IsNaN(w.SampleTerrain(A.OriginX + 10, z0 - 3000)), "outside the set");
        }

        [Test]
        public void BuildingLookup_WorksForEachTile()
        {
            var w = _b.World;
            foreach (var inst in _b.Instances)
            {
                var bd = inst.Model.Buildings.First(x => !x.PartOf.IsValid);
                Assert.IsTrue(w.TryGetBuilding(bd.Id, out var info), inst.TileName);
                Assert.AreEqual(bd.Id, info.Id);
                Assert.IsTrue(inst.Model.Tile.Contains(info.AnchorX, info.AnchorZ), "anchor lies in its own tile");
            }
        }

        [Test]
        public void RemoveBuilding_RoutesToOwningTile()
        {
            var w = _b.World;
            var inst = _b.Instances[1];
            var bd = inst.Model.Buildings.First(x => !x.PartOf.IsValid);
            Assert.GreaterOrEqual(w.RemoveBuilding(bd.Id), 1);
            Assert.IsFalse(w.TryGetBuilding(bd.Id, out _));
            Assert.IsTrue(w.IsRemoved(bd.Id));
            Assert.IsTrue(inst.Model.Removed.Contains(bd.Id));
            Assert.IsFalse(_b.Instances[0].Model.Removed.Contains(bd.Id));
        }
    }
}
