// One PhysicsScene per activity cluster (T-111, R-101 section 6, D1 of T-109): a cluster's bodies and the physics chunks they need live in
// their own Unity scene (LocalPhysicsMode.Physics3D), in float32 coordinates around the cluster's tile-corner origin, so two bodies 50 km
// apart never share one float32 frame. A non-default PhysicsScene is not simulated automatically: Simulate(dt) steps every cluster by hand.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.World.Streaming.Unity
{
    public sealed class ClusterPhysicsScenes : IDisposable
    {
        public sealed class ClusterScene
        {
            public int Id;
            public Scene Scene;
            public PhysicsScene Physics;
            public double OriginX, OriginZ;
            /// <summary>Editor preview scene (edit mode, where SceneManager.CreateScene is play-mode only); it has its own PhysicsScene too.</summary>
            public bool Preview;
        }

        readonly Dictionary<int, ClusterScene> _scenes = new Dictionary<int, ClusterScene>();
        readonly string _prefix;
        public ClusterPhysicsScenes(string namePrefix = "StreamCluster") { _prefix = namePrefix; }

        public int Count => _scenes.Count;
        public IEnumerable<ClusterScene> All => _scenes.Values;

        public ClusterScene GetOrCreate(int clusterId)
        {
            if (_scenes.TryGetValue(clusterId, out var c)) return c;
            Scene scene; bool preview = false;
#if UNITY_EDITOR
            if (!Application.isPlaying) { scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene(); preview = true; }
            else
#endif
            scene = SceneManager.CreateScene(_prefix + "_" + clusterId, new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            if (!scene.IsValid()) throw new InvalidOperationException("could not create a physics scene for cluster " + clusterId);
            c = new ClusterScene { Id = clusterId, Scene = scene, Physics = scene.GetPhysicsScene(), Preview = preview };
            if (!c.Physics.IsValid()) throw new InvalidOperationException("scene of cluster " + clusterId + " has no PhysicsScene");
            _scenes[clusterId] = c;
            return c;
        }

        public bool TryGet(int clusterId, out ClusterScene scene) => _scenes.TryGetValue(clusterId, out scene);

        public void SetOrigin(int clusterId, double ox, double oz)
        {
            var c = GetOrCreate(clusterId);
            c.OriginX = ox; c.OriginZ = oz;
        }

        /// <summary>Steps every cluster scene. Call once per fixed step (Physics.autoSimulation does not do it for these scenes).</summary>
        public void Simulate(float dt)
        {
            Physics.SyncTransforms();
            foreach (var c in _scenes.Values) c.Physics.Simulate(dt);
        }

        /// <summary>Destroys the scene's content and drops it.</summary>
        public void Remove(int clusterId)
        {
            if (!_scenes.TryGetValue(clusterId, out var c)) return;
            _scenes.Remove(clusterId);
            Close(c);
        }

        static void Close(ClusterScene c)
        {
            var s = c.Scene;
            if (!s.IsValid()) return;
            foreach (var go in s.GetRootGameObjects()) { if (Application.isPlaying) UnityEngine.Object.Destroy(go); else UnityEngine.Object.DestroyImmediate(go); }
#if UNITY_EDITOR
            if (c.Preview) { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(s); return; }
#endif
            SceneManager.UnloadSceneAsync(s);
        }

        public void Dispose()
        {
            foreach (var c in new List<ClusterScene>(_scenes.Values)) Remove(c.Id);
        }
    }
}
