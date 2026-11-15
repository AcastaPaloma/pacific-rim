// Unity backing of the flythrough harness (T-111): bodies are kinematic Rigidbody proxies in the PhysicsScene of their cluster, the surface is the
// real MeshColliders the streaming host baked, and heights come from PhysicsScene.Raycast. The harness (engine-free) places bodies by the plan's
// float32 cluster-local coordinates and reads back what Unity holds, so a loss anywhere in Transform / scene / rebase shows up in M-015@v2.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Game.World.Geo.Unity;
using Game.World.Streaming.Host;

namespace Game.World.Streaming.Unity
{
    public sealed class UnityFlythroughWorld : IFlythroughWorld
    {
        const int IgnoreRaycastLayer = 2;
        readonly ClusterPhysicsScenes _scenes;
        readonly Dictionary<int, GameObject> _bodies = new Dictionary<int, GameObject>();
        public UnityFlythroughWorld(ClusterPhysicsScenes scenes) { _scenes = scenes; }
        public int BodyCount => _bodies.Count;

        public void Place(int focusId, int clusterId, float x, float y, float z)
        {
            var cs = _scenes.GetOrCreate(clusterId);
            if (!_bodies.TryGetValue(focusId, out var go) || go == null)
            {
                go = new GameObject("Body_" + focusId) { layer = IgnoreRaycastLayer };
                var rb = go.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
                var sc = go.AddComponent<SphereCollider>(); sc.radius = 1f;
                SceneManager.MoveGameObjectToScene(go, cs.Scene);
                _bodies[focusId] = go;
            }
            else if (go.scene != cs.Scene) SceneManager.MoveGameObjectToScene(go, cs.Scene);
            go.transform.position = new Vector3(x, y, z);
        }

        public void Step(double dt)
        {
            _scenes.Simulate(0.02f);
        }

        public bool TryRead(int focusId, out int clusterId, out float x, out float y, out float z)
        {
            clusterId = -1; x = y = z = 0;
            if (!_bodies.TryGetValue(focusId, out var go) || go == null) return false;
            foreach (var c in _scenes.All) if (c.Scene == go.scene) { clusterId = c.Id; break; }
            if (clusterId == -1 && !_scenes.TryGet(-1, out _)) return false;
            var p = go.transform.position;
            x = p.x; y = p.y; z = p.z;
            return true;
        }

        public double GroundHeightLocal(int clusterId, float x, float z)
        {
            if (!_scenes.TryGet(clusterId, out var cs)) return double.NaN;
            if (cs.Physics.Raycast(new Vector3(x, 400f, z), Vector3.down, out var hit, 800f)) return hit.point.y;
            return double.NaN;
        }

        public bool TryGetClusterOrigin(int clusterId, out double ox, out double oz)
        {
            if (_scenes.TryGet(clusterId, out var cs)) { ox = cs.OriginX; oz = cs.OriginZ; return true; }
            ox = oz = 0; return false;
        }

        public void Clear() { foreach (var go in _bodies.Values) if (go != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(go); else UnityEngine.Object.DestroyImmediate(go); } _bodies.Clear(); }
    }

    public static class UnityFlythrough
    {
        /// <summary>Runs the harness on real Unity physics scenes with the synthetic Bay reader. Main thread, no rendering needed.</summary>
        public static FlythroughReport Run(FlythroughScenario sc)
        {
            var parent = new GameObject("FlythroughRoot");
            var scenes = new ClusterPhysicsScenes("FlyCluster");
            try
            {
                var runner = new InlineWorkRunner();
                var builder = new UnityTileBuilder(parent.transform, runner, GeoTileBuilder.LoadSettings(Application.dataPath)) { ClusterScenes = scenes };
                var sched = new StreamingScheduler(new StreamingConfig(), new DefaultTileCostModel());
                var host = new StreamingHostCore(sched, new SyntheticTileReader(), builder, runner, new StreamingHostOptions { MainThreadSliceMs = 1e9 });
                var world = new UnityFlythroughWorld(scenes);
                sc.AfterTick = builder.Tick;
                var rep = FlythroughHarness.Run(sc, host, world, "unity");
                world.Clear();
                builder.ReleaseAll();
                return rep;
            }
            finally
            {
                scenes.Dispose();
                if (Application.isPlaying) UnityEngine.Object.Destroy(parent); else UnityEngine.Object.DestroyImmediate(parent);
            }
        }
    }
}
