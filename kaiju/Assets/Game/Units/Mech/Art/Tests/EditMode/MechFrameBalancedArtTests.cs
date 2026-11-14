using System.Collections.Generic;
using System.Linq;
using Game.Units.Mech.Art.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Units.Mech.Art.Tests
{
    /// <summary>
    /// T-007 C17, C18, C20, C22, C24, C29, C31: the imported Balanced greybox prefab against the mech_frame@v1
    /// config (Assets/Game/Config/Mech/mech_frame_balanced.json). Every reference value is read from the config.
    /// </summary>
    public sealed class MechFrameBalancedArtTests
    {
        MechFrameConfig cfg;
        MechPartMap map;
        GameObject root;

        [SetUp]
        public void SetUp()
        {
            var text = AssetDatabase.LoadAssetAtPath<TextAsset>(MechFrameImport.ConfigPath);
            Assert.IsNotNull(text, "config missing: " + MechFrameImport.ConfigPath);
            cfg = MechFrameConfig.Parse(text.text);
            map = AssetDatabase.LoadAssetAtPath<MechPartMap>(MechFrameImport.MapPath);
            Assert.IsNotNull(map, "part map missing: " + MechFrameImport.MapPath);
            root = PrefabUtility.LoadPrefabContents(MechFrameImport.PrefabPath);
            Assert.IsNotNull(root, "prefab missing: " + MechFrameImport.PrefabPath);
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null) PrefabUtility.UnloadPrefabContents(root);
        }

        Transform Part(string segId) => map.ResolvePart(root.transform, segId);

        static float Tol(float length) => Mathf.Min(0.02f * length, 0.25f);

        [Test]
        public void Mapping_Has15UniqueEntries_ResolvingToNamedPrefabTransforms()
        {
            Assert.AreEqual(MechFrameConfig.SchemaV1, map.schema);
            Assert.AreEqual(cfg.id, map.frameId);
            Assert.AreEqual(cfg.segments.Length, map.parts.Count);
            Assert.AreEqual(15, map.parts.Count);
            CollectionAssert.AllItemsAreUnique(map.parts.Select(p => p.segmentId));
            CollectionAssert.AreEquivalent(cfg.segments.Select(s => s.id), map.parts.Select(p => p.segmentId));
            var meshParts = root.GetComponentsInChildren<MeshFilter>(true).Select(m => m.transform).ToList();
            Assert.AreEqual(15, meshParts.Count, "exactly 15 mesh parts in the prefab");
            foreach (var p in map.parts)
            {
                var t = Part(p.segmentId);
                Assert.IsNotNull(t, "unresolved " + p.segmentId);
                Assert.AreEqual(MechFrameImport.PartPrefix + p.segmentId, t.name);
                Assert.AreEqual(p.partName, t.name);
                Assert.IsTrue(meshParts.Contains(t), p.segmentId + " has no mesh");
            }
        }

        [Test]
        public void Hierarchy_FollowsKinematicTree()
        {
            foreach (var s in cfg.segments)
            {
                var t = Part(s.id);
                if (s.IsRoot) Assert.AreEqual(root.transform, t.parent, "pelvis must hang off the prefab root");
                else Assert.AreEqual(Part(s.parent), t.parent, s.id + " parent");
            }
        }

        [Test]
        public void Height_EqualsConfig_WithSoleOnGround()
        {
            var (min, max) = MeshBounds(root.transform);
            Assert.AreEqual(cfg.total_height_m, max.y - min.y, 0.01f, "H");
            Assert.AreEqual(0f, min.y, 0.01f, "sole on y = 0");
        }

        [Test]
        public void Joints_And_SegmentLengths_MatchConfigWithin2Percent()
        {
            foreach (var s in cfg.segments)
            {
                var t = Part(s.id);
                Assert.That(Vector3.Distance(t.position, s.ProximalJoint), Is.LessThanOrEqualTo(Tol(s.length_m)), s.id + " origin");
            }
            foreach (var j in cfg.joints)
                Assert.That(Vector3.Distance(Part(j.child).position, j.Position), Is.LessThanOrEqualTo(0.25f), j.id);
            foreach (var s in cfg.segments.Where(x => x.kind == "upper_arm" || x.kind == "forearm" || x.kind == "thigh" || x.kind == "shank"))
            {
                var child = cfg.segments.First(c => c.parent == s.id);
                float len = Vector3.Distance(Part(s.id).position, Part(child.id).position);
                Assert.AreEqual(s.length_m, len, 0.02f * s.length_m, s.id + " length");
            }
        }

        [Test]
        public void Axes_YUp_FacingPlusZ_IdentityRotation_UnitScale()
        {
            Assert.AreEqual(Quaternion.identity, root.transform.rotation);
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                Assert.That(Vector3.Distance(t.lossyScale, Vector3.one), Is.LessThan(1e-4f), t.name + " scale");
                if (!t.name.StartsWith(MechFrameImport.SocketPrefix))
                    Assert.That(Quaternion.Angle(t.rotation, Quaternion.identity), Is.LessThan(1e-3f), t.name + " rotation");
            }
            // head on top, left side at -x, toes toward +z (feet longer in front of the ankle than behind)
            Assert.Greater(Part("head").position.y, Part("pelvis").position.y);
            Assert.Less(Part("forearm_L").position.x, 0f);
            Assert.Greater(Part("forearm_R").position.x, 0f);
            var foot = cfg.FindSegment("foot_L");
            var (fmin, fmax) = MeshBounds(Part("foot_L"), recurse: false);
            Assert.AreEqual(foot.distal_end[2], fmax.z, 0.02f * foot.length_m, "toe at +z");
            Assert.Greater(fmax.z - Part("foot_L").position.z, Part("foot_L").position.z - fmin.z);
        }

        [Test]
        public void Sockets_LeftRightMirrorSymmetric()
        {
            var kinds = map.sockets.Select(s => s.kind).Distinct().ToList();
            CollectionAssert.IsSupersetOf(kinds, new[] { "shoulder", "hip", "hardpoint" });
            foreach (var k in kinds)
            {
                var l = map.ResolveSocket(root.transform, k, "L");
                var r = map.ResolveSocket(root.transform, k, "R");
                Assert.IsNotNull(l, k + " L"); Assert.IsNotNull(r, k + " R");
                var m = new Vector3(-1, 1, 1);
                Assert.That(Vector3.Distance(Vector3.Scale(l.position, m), r.position), Is.LessThan(0.01f), k + " position");
                Assert.That(Vector3.Distance(Vector3.Scale(l.forward, m), r.forward), Is.LessThan(1e-3f), k + " forward");
                Assert.That(Vector3.Distance(Vector3.Scale(l.up, m), r.up), Is.LessThan(1e-3f), k + " up");
                Assert.AreEqual(l.parent.name.Replace("_L", "_R"), r.parent.name, k + " parent");
                // local geometry of the mount is identical: same distance and direction (mirrored) from the parent's pivot
                Assert.That(Vector3.Distance(Vector3.Scale(l.localPosition, m), r.localPosition), Is.LessThan(0.01f), k + " local");
            }
            // limb-root sockets sit on the joint centres from the config
            Assert.That(Vector3.Distance(map.ResolveSocket(root.transform, "shoulder", "L").position, cfg.FindSegment("upper_arm_L").ProximalJoint), Is.LessThan(0.01f));
            Assert.That(Vector3.Distance(map.ResolveSocket(root.transform, "hip", "R").position, cfg.FindSegment("thigh_R").ProximalJoint), Is.LessThan(0.01f));
        }

        [Test]
        public void ForearmSwap_ThroughSocketAndMappingOnly()
        {
            // a runtime-style clone (not prefab-linked), as the body module would spawn it
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(MechFrameImport.PrefabPath));
            var m = Object.Instantiate(map);
            var r = instance.transform;
            var old = m.ResolvePart(r, "forearm_L");
            var hand = m.ResolvePart(r, "hand_L");
            var socket = m.ResolveSocket(r, "elbow", "L");
            Assert.IsNotNull(socket, "elbow socket");
            var before = m.parts.Where(p => p.segmentId != "forearm_L").ToDictionary(p => p.segmentId, p => m.ResolvePart(r, p.segmentId).position);

            var hardpoint = m.ResolveSocket(r, "hardpoint", "L");
            Assert.IsNotNull(hardpoint, "hardpoint socket");
            var hardpointLocal = hardpoint.localPosition;
            var hardpointRot = hardpoint.localRotation;

            // stand-in limb: an independent mesh (Unity's built-in cube primitive, not a mech part), mounted on
            // the elbow socket of the upper arm; it takes over the hand and the hardpoint mount
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var standIn = new GameObject("StandIn_forearm_L");
            standIn.AddComponent<MeshFilter>().sharedMesh = cube.GetComponent<MeshFilter>().sharedMesh;
            standIn.AddComponent<MeshRenderer>();
            Object.DestroyImmediate(cube);
            standIn.transform.SetParent(socket.parent, false);
            standIn.transform.position = socket.position;
            hand.SetParent(standIn.transform, true);
            hardpoint.SetParent(standIn.transform, false);       // same local mount geometry on the new limb
            hardpoint.localPosition = hardpointLocal;
            hardpoint.localRotation = hardpointRot;
            Object.DestroyImmediate(old.gameObject);
            m.Remap("forearm_L", MechPartMap.PathOf(r, standIn.transform), standIn.name);

            var newHardpoint = m.ResolveSocket(r, "hardpoint", "L");
            Assert.AreEqual(hardpoint, newHardpoint, "hardpoint socket re-rooted under the stand-in by Remap");
            Assert.AreEqual(standIn.transform, newHardpoint.parent);
            // moving the new limb carries its hardpoint (and hand) with it
            var hpBefore = newHardpoint.position;
            standIn.transform.position += new Vector3(0f, 0f, 3f);
            Assert.That(Vector3.Distance(newHardpoint.position, hpBefore + new Vector3(0f, 0f, 3f)), Is.LessThan(1e-4f));
            standIn.transform.position -= new Vector3(0f, 0f, 3f);

            Assert.AreEqual(standIn.transform, m.ResolvePart(r, "forearm_L"));
            Assert.That(Vector3.Distance(standIn.transform.position, cfg.FindSegment("forearm_L").ProximalJoint), Is.LessThan(0.01f));
            var newHand = m.ResolvePart(r, "hand_L");
            Assert.IsNotNull(newHand, "hand_L unresolved after swap");
            Assert.AreEqual(hand, newHand);
            Assert.AreEqual(standIn.transform, newHand.parent);
            foreach (var kv in before)
            {
                var t = m.ResolvePart(r, kv.Key);
                Assert.IsNotNull(t, kv.Key + " lost after swap");
                Assert.That(Vector3.Distance(t.position, kv.Value), Is.LessThan(1e-4f), kv.Key + " moved");
            }
            Assert.AreEqual(15, m.parts.Count(p => m.ResolvePart(r, p.segmentId) != null));
            Object.DestroyImmediate(m);
            Object.DestroyImmediate(instance);
        }

        [Test]
        public void Prefab_HoldsOnly15PartsAnd8MapSockets()
        {
            var parts = new HashSet<Transform>(map.parts.Select(p => Part(p.segmentId)));
            var sockets = new HashSet<Transform>(map.sockets.Select(x => root.transform.Find(x.path)));
            Assert.AreEqual(15, parts.Count);
            Assert.AreEqual(8, map.sockets.Count);
            Assert.AreEqual(8, sockets.Count);
            Assert.IsFalse(sockets.Contains(null), "a map socket does not resolve");
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root.transform) continue;
                Assert.IsTrue(parts.Contains(t) || sockets.Contains(t), "unexpected transform in prefab: " + t.name);
                if (sockets.Contains(t))
                {
                    Assert.IsTrue(t.name.StartsWith(MechFrameImport.SocketPrefix), t.name);
                    Assert.AreEqual(0, t.childCount, t.name + " must be an empty helper");
                    Assert.AreEqual(1, t.GetComponents<Component>().Length, t.name + " must hold only its Transform");
                }
            }
        }

        [Test]
        public void Remap_RejectsUnknownSegment_AndReRootsOnlyExactPrefix()
        {
            var m = ScriptableObject.CreateInstance<MechPartMap>();
            m.parts.Add(new MechPartMap.Part { segmentId = "forearm_L", partName = "A", path = "a/forearm_L" });
            m.parts.Add(new MechPartMap.Part { segmentId = "hand_L", partName = "B", path = "a/forearm_L/hand_L" });
            m.parts.Add(new MechPartMap.Part { segmentId = "x", partName = "C", path = "a/forearm_L2/hand" });
            m.sockets.Add(new MechPartMap.Socket { socketName = "s", kind = "hardpoint", side = "L", path = "a/forearm_L/s" });
            Assert.Throws<KeyNotFoundException>(() => m.Remap("no_such_segment", "z", "z"));
            m.Remap("forearm_L", "a/new", "N");
            Assert.AreEqual("a/new", m.parts[0].path);
            Assert.AreEqual("a/new/hand_L", m.parts[1].path);
            Assert.AreEqual("a/forearm_L2/hand", m.parts[2].path, "a sibling sharing the name prefix must not be re-rooted");
            Assert.AreEqual("a/new/s", m.sockets[0].path);
            Object.DestroyImmediate(m);
        }

        [Test]
        public void Budget_Triangles_And_PaletteRoleMaterials()
        {
            int tris = root.GetComponentsInChildren<MeshFilter>(true).Sum(f => MechFrameImport.TriangleCount(f.sharedMesh));
            Assert.LessOrEqual(tris, 20000);
            var mats = root.GetComponentsInChildren<MeshRenderer>(true).SelectMany(x => x.sharedMaterials).Distinct().ToList();
            Assert.LessOrEqual(mats.Count, 4);
            foreach (var mat in mats)
            {
                Assert.IsNotNull(mat);
                CollectionAssert.Contains(MechFrameImport.Roles.Select(x => "mech_" + x), mat.name);
                Assert.IsNull(mat.GetTexture("_BaseMap"), mat.name + " must be flat (no texture)");
            }
            Assert.IsNull(root.GetComponentInChildren<Collider>(true), "colliders belong to T-005");
            Assert.IsNull(root.GetComponentInChildren<ArticulationBody>(true), "ArticulationBody belongs to T-005");
        }

        static (Vector3 min, Vector3 max) MeshBounds(Transform t, bool recurse = true)
        {
            var min = Vector3.positiveInfinity;
            var max = Vector3.negativeInfinity;
            var filters = recurse ? t.GetComponentsInChildren<MeshFilter>(true) : new[] { t.GetComponent<MeshFilter>() };
            foreach (var f in filters)
            {
                var b = f.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var w = f.transform.localToWorldMatrix.MultiplyPoint3x4(c);
                    min = Vector3.Min(min, w);
                    max = Vector3.Max(max, w);
                }
            }
            return (min, max);
        }
    }
}
