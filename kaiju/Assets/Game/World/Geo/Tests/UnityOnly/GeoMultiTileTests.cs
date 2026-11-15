// Multi-tile Unity adapter (T-021 steps 2-3): staged build of a 2x1 set, placement, cross-tile terrain sampling, building lookup,
// shared edge vertices, seamless colliders.
using System.Collections.Generic;
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
            _b = new GeoTileSetBuilder(set, A, GeoTestData.Settings()) { BuildingColliders = false, TerrainCollider = true };
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

        // ---------------------------------------------------------------- step 3: seams
        // A (L0_30_36) is north of B (L0_30_35); the shared edge is A's row 0 (local z = 0) and B's last row (local z = 2048).
        // Scene z of the edge is 0 (floating origin = A's origin).
        const float EdgeTol = 0.001f;

        static SortedDictionary<float, float> EdgeVertices(GeoTileInstance inst, float localZ)
        {
            var res = new SortedDictionary<float, float>();
            foreach (var mf in inst.transform.Find("Terrain").GetComponentsInChildren<MeshFilter>())
                foreach (var v in mf.sharedMesh.vertices)
                {
                    if (v.z != localZ) continue;
                    if (res.TryGetValue(v.x, out var y)) Assert.AreEqual(y, v.y, 0f, "vertex duplicated across chunks must agree, x=" + v.x);
                    else res[v.x] = v.y;
                }
            return res;
        }

        [Test]
        public void SharedEdge_RenderVertices_IdenticalInBothMeshes()
        {
            var ea = EdgeVertices(_b.Instances[0], 0f);
            var eb = EdgeVertices(_b.Instances[1], 2048f);
            Assert.AreEqual(2049, ea.Count, "A keeps every edge node");
            Assert.AreEqual(ea.Count, eb.Count, "B keeps every edge node");
            double maxGap = 0;
            foreach (var kv in ea)
            {
                Assert.IsTrue(eb.TryGetValue(kv.Key, out var yb), "B has no vertex at x=" + kv.Key);
                maxGap = System.Math.Max(maxGap, System.Math.Abs(kv.Value - yb));
            }
            Assert.LessOrEqual(maxGap, EdgeTol, "edge vertex gap (m)");
        }

        [Test]
        public void SharedEdge_NoCrack_BetweenVertices()
        {
            var ea = EdgeVertices(_b.Instances[0], 0f);
            var eb = EdgeVertices(_b.Instances[1], 2048f);
            var xs = new List<float>(ea.Keys);
            for (int k = 0; k + 1 < xs.Count; k += 16)
            {
                float x = xs[k] + 0.5f * (xs[k + 1] - xs[k]);
                float ya = Mid(ea, xs[k], xs[k + 1]), yb = Mid(eb, xs[k], xs[k + 1]);
                Assert.AreEqual(ya, yb, EdgeTol, "render surfaces differ at the edge midpoint x=" + x);
                // the physics surfaces: a straight-down ray through the midpoint hits, from the scene as a whole
                Assert.IsTrue(Physics.Raycast(new Vector3(x, 2000f, 0f), Vector3.down, out var hit, 5000f), "ray through edge midpoint x=" + x + " missed");
                Assert.AreEqual(ya, hit.point.y, EdgeTol + 0.01f, "physics vs render at the midpoint, x=" + x);
            }
        }

        static float Mid(SortedDictionary<float, float> e, float x0, float x1) => 0.5f * (e[x0] + e[x1]);

        /// <summary>Straight-down hit on one tile's collider at scene (x, z=0). Exactly on the border first; if the collider rejects a border ray, a hair inward (tolerance grows by slope * offset).</summary>
        static float Hit(GeoTileInstance inst, float x, float inward, out float usedOffset)
        {
            foreach (float off in new[] { 0f, 1e-3f, 1e-2f })
            {
                var o = new Vector3(x, 2000f, inward * off);
                if (inst.PhysicsSurface.Raycast(new Ray(o, Vector3.down), out var hit, 5000f)) { usedOffset = off; return hit.point.y; }
            }
            Assert.Fail("collider of " + inst.TileName + " missed a straight-down ray at x=" + x);
            usedOffset = 0; return float.NaN;
        }

        [Test]
        public void SharedEdge_Colliders_MeetWithoutGap()
        {
            Physics.SyncTransforms();
            var a = _b.Instances[0]; var b = _b.Instances[1];
            Assert.IsNotNull(a.PhysicsSurface); Assert.IsNotNull(b.PhysicsSurface);
            var ea = EdgeVertices(a, 0f);
            double worst = 0; int n = 0;
            foreach (var kv in ea)
            {
                if (n++ % 8 != 0 && kv.Key != 2048f) continue;
                float x = kv.Key;
                float ya = Hit(a, x, +1f, out var offA), yb = Hit(b, x, -1f, out var offB);
                float tol = EdgeTol + 2f * System.Math.Max(offA, offB);
                Assert.AreEqual(ya, yb, tol, "colliders differ at the border, x=" + x + " (offsets " + offA + ", " + offB + ")");
                Assert.AreEqual(kv.Value, ya, tol + 0.0025f, "collider vs render edge vertex, x=" + x);
                worst = System.Math.Max(worst, System.Math.Abs(ya - yb));
            }
            Assert.LessOrEqual(worst, EdgeTol + 0.02, "worst collider step across the border (m)");
            // midpoints between vertices: a hit on at least one collider (no crack)
            for (float x = 0.5f; x < 2048f; x += 64f)
                Assert.IsTrue(Physics.Raycast(new Vector3(x, 2000f, 0f), Vector3.down, 5000f), "no collider under the edge midpoint x=" + x);
        }

        [Test]
        public void ColliderBounds_Touch_WithoutOverlapBeyond005m2()
        {
            Physics.SyncTransforms();
            var ba = _b.Instances[0].PhysicsSurface.bounds; var bb = _b.Instances[1].PhysicsSurface.bounds;
            // overlap area in plan (x, z) of the two collider bounds
            double ox = System.Math.Max(0, System.Math.Min(ba.max.x, bb.max.x) - System.Math.Max(ba.min.x, bb.min.x));
            double oz = System.Math.Max(0, System.Math.Min(ba.max.z, bb.max.z) - System.Math.Max(ba.min.z, bb.min.z));
            Assert.LessOrEqual(ox * oz, 0.05, "collider overlap area (m2)");
            Assert.AreEqual(ba.min.z, bb.max.z, 1e-3, "colliders meet at the shared edge");
        }
    }
}
