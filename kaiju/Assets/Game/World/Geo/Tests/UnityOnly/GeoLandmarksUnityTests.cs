// Landmarks in Unity (T-023 step 2B): static colliders of the Golden Gate Bridge across the L0_26_37 / L0_26_38 cut.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Core;
using Game.World.Geo.Tests;
using NUnit.Framework;
using UnityEngine;

namespace Game.World.Geo.Unity.Tests
{
    [Category("GeoLandmarks")]
    public class GeoLandmarksUnityTests
    {
        static GeoTileSetBuilder _b;
        static readonly GeoTileId A = GeoTileId.From(0, 26, 37), B = GeoTileId.From(0, 26, 38);
        const double CutZ = 77824.0;

        static string TilesRoot => Path.Combine(GeoTestData.ProjectRoot(), "Assets", "StreamingAssets", "Geo", "tiles", "v1");

        [OneTimeSetUp]
        public void Build()
        {
            var ids = new[] { A, B };
            var absent = GeoTileSetLoader.Absent(TilesRoot, ids);
            if (absent.Count > 0) Assert.Ignore("tile folders absent: " + string.Join(", ", absent));
            var set = GeoTileSetLoader.Load(TilesRoot, ids, verifyHashes: false);
            _b = new GeoTileSetBuilder(set, A, GeoTestData.Settings()) { BuildingColliders = true, TerrainCollider = false };
            while (_b.BuildNext() != null) { }
            Physics.SyncTransforms();
        }

        [OneTimeTearDown]
        public void TearDown() { if (_b != null) Object.DestroyImmediate(_b.Root); _b = null; }

        static GeoTileInstance Inst(GeoTileId id) => _b.Instances.First(i => i.TileName == id.ToString());

        static IEnumerable<Collider> Cols(GeoTileInstance i) => i.LandmarksRoot == null ? new Collider[0] : i.LandmarksRoot.GetComponentsInChildren<Collider>(true);

        static IEnumerable<Collider> All() => _b.Instances.SelectMany(Cols);

        static Transform Landmark(GeoTileInstance i, string kind)
        {
            foreach (var lm in i.Model.Layer("landmarks").Landmarks)
                if (lm.Kind == kind) return i.LandmarksRoot.Find(lm.Lid);
            return null;
        }

        [Test]
        public void ColliderCounts()
        {
            var a = Inst(A); var b = Inst(B);
            int towers = a.Model.Layer("landmarks").Landmarks.Count(l => l.Kind == "bridge_tower") + b.Model.Layer("landmarks").Landmarks.Count(l => l.Kind == "bridge_tower");
            Assert.AreEqual(4, towers);
            Assert.AreEqual(4, All().Count(c => c is BoxCollider && LandmarkKind(c) == "bridge_tower"));
            Assert.AreEqual(36, Cols(a).Count(c => c is BoxCollider && LandmarkKind(c) == "bridge_deck"));
            Assert.AreEqual(14, Cols(b).Count(c => c is BoxCollider && LandmarkKind(c) == "bridge_deck"));
            Assert.AreEqual(72, Cols(a).Count(c => c is CapsuleCollider));
            Assert.AreEqual(28, Cols(b).Count(c => c is CapsuleCollider));
            foreach (var c in Cols(a).Concat(Cols(b)).OfType<CapsuleCollider>()) { Assert.AreEqual(0.46f, c.radius, 0.005f); Assert.AreEqual(2, c.direction); }
        }

        static string LandmarkKind(Collider c)
        {
            var inst = c.GetComponentInParent<GeoTileInstance>();
            string lid = c.transform.parent.name;
            var lm = inst.Model.Layer("landmarks").Landmarks.First(l => l.Lid == lid);
            return lm.Kind;
        }

        [Test]
        public void AllStatic_OnLandmarkLayer_NoRigidbody()
        {
            int layer = LayerMask.NameToLayer("LandmarkStatic");
            Assert.GreaterOrEqual(layer, 0, "LandmarkStatic layer defined in TagManager");
            Assert.Greater(All().Count(), 0);
            foreach (var c in All())
            {
                Assert.IsTrue(c.gameObject.isStatic, c.name);
                Assert.AreEqual(layer, c.gameObject.layer, c.name);
                Assert.IsTrue(c.transform.parent.gameObject.isStatic);
                Assert.AreEqual(layer, c.transform.parent.gameObject.layer);
                Assert.IsNull(c.GetComponentInParent<Rigidbody>());
            }
        }

        // world-space (double) ends of a collider along its local Z axis
        static void Ends(GeoTileInstance inst, Collider c, out Vector3d s, out Vector3d e)
        {
            float len = c is BoxCollider bc ? bc.size.z : ((CapsuleCollider)c).height - 2f * ((CapsuleCollider)c).radius;
            var f = c.transform.forward; var p = c.transform.position;
            Vector3 ps = p - f * (len * 0.5f), pe = p + f * (len * 0.5f);
            inst.ToWorld(ps, out double sx, out double sy, out double sz);
            inst.ToWorld(pe, out double ex, out double ey, out double ez);
            s = new Vector3d { x = sx, y = sy, z = sz }; e = new Vector3d { x = ex, y = ey, z = ez };
        }

        struct Vector3d { public double x, y, z; }

        [Test]
        public void Continuity_AtCut_DeckAndCables()
        {
            var a = Inst(A); var b = Inst(B);
            int checkedPairs = 0;
            foreach (var kind in new[] { "bridge_deck", "bridge_cable" })
            {
                foreach (var lmA in a.Model.Layer("landmarks").Landmarks.Where(l => l.Kind == kind))
                {
                    var ta = a.LandmarksRoot.Find(lmA.Lid);
                    var colsA = ta.GetComponentsInChildren<Collider>(true);
                    Ends(a, colsA[colsA.Length - 1], out _, out var endA);
                    Assert.AreEqual(CutZ, endA.z, 0.05, lmA.Lid + " last collider ends at the cut");
                    // the matching piece in B: the piece whose first collider starts at the same point
                    bool found = false;
                    foreach (var lmB in b.Model.Layer("landmarks").Landmarks.Where(l => l.Kind == kind))
                    {
                        var colsB = b.LandmarksRoot.Find(lmB.Lid).GetComponentsInChildren<Collider>(true);
                        Ends(b, colsB[0], out var startB, out _);
                        if (System.Math.Abs(startB.x - endA.x) < 1e-3 && System.Math.Abs(startB.y - endA.y) < 1e-3 && System.Math.Abs(startB.z - endA.z) < 1e-3) { found = true; break; }
                    }
                    Assert.IsTrue(found, lmA.Lid + " continues in " + B);
                    checkedPairs++;
                }
            }
            Assert.AreEqual(3, checkedPairs, "deck + 2 cables");
        }

        [Test]
        public void DeckBoxTop_AtFirstEndpoint_MatchesBakedY()
        {
            var a = Inst(A);
            var lm = a.Model.Layer("landmarks").Landmarks.First(l => l.Kind == "bridge_deck");
            var cols = a.LandmarksRoot.Find(lm.Lid).GetComponentsInChildren<BoxCollider>(true);
            Ends(a, cols[0], out var s, out _);
            double top = s.y + cols[0].size.y * 0.5;
            Assert.AreEqual(lm.Y[0], top, 0.01);
        }

        [Test]
        public void Export_HasLandmarks()
        {
            foreach (var inst in _b.Instances)
            {
                var ex = inst.Export(null);
                Assert.IsTrue(ex.ContainsKey("landmarks"));
                Assert.IsTrue(ex.ContainsKey("buildings"));
                var arr = (List<object>)ex["landmarks"];
                Assert.AreEqual(5, arr.Count, inst.TileName);
                foreach (Dictionary<string, object> e in arr)
                {
                    Assert.IsFalse(string.IsNullOrEmpty((string)e["lid"]));
                    Assert.IsFalse(string.IsNullOrEmpty((string)e["bid"]));
                    Assert.IsTrue(e.ContainsKey("height_m") && e.ContainsKey("anchor") && e.ContainsKey("colliders"));
                }
            }
        }

        [Test]
        public void RemovingEveryBuilding_LeavesLandmarks()
        {
            int before = All().Count();
            Assert.Greater(before, 0);
            foreach (var inst in _b.Instances)
                foreach (var id in inst.Model.Buildings.Where(x => !x.PartOf.IsValid).Select(x => x.Id).ToList())
                    inst.RemoveBuilding(id);
            var after = All().ToList();
            Assert.AreEqual(before, after.Count);
            foreach (var c in after) Assert.IsTrue(c != null && c.enabled && c.gameObject.activeInHierarchy);
        }
    }
}
