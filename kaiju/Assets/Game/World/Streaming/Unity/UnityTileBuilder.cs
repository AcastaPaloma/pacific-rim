// Main-thread side of the streaming host (T-109): turns TilePayloads into GameObjects. Render keys become meshes at (tile origin - render
// origin); physics keys become MeshColliders whose cooking (Physics.BakeMesh) runs on a worker so the main thread only attaches the result.
// ShiftOrigin moves tile roots by exact integers (origin is a tile corner) and calls Physics.SyncTransforms once.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Game.World.Geo;
using Game.World.Geo.Unity;
using Game.World.Streaming.Host;

namespace Game.World.Streaming.Unity
{
    public sealed class UnityTileBuilder : ITileBuilder
    {
        sealed class Node
        {
            public GameObject Root;
            public readonly List<Mesh> Meshes = new List<Mesh>();
            public GeoTileId Tile; public double X0, Z0;
        }
        sealed class Baking { public ResidencyKey Key; public Node Node; public IBuildSink Sink; public volatile bool Done; public volatile string Error; public long Gpu, Cpu; }

        readonly Transform _parent;
        readonly IWorkRunner _runner;
        readonly Dictionary<ResidencyKey, Node> _nodes = new Dictionary<ResidencyKey, Node>();
        readonly List<Baking> _baking = new List<Baking>();
        Material _terrain;

        public double OriginX { get; private set; }
        public double OriginZ { get; private set; }
        /// <summary>Milliseconds of the last ShiftOrigin (M-101 counts a rebase frame as a streaming frame).</summary>
        public double LastShiftMs { get; private set; }
        public int ResidentNodes => _nodes.Count;
        public int ColliderBakesPending => _baking.Count;
        public Transform Parent => _parent;

        public UnityTileBuilder(Transform parent, IWorkRunner runner, IReadOnlyDictionary<string, double> geoSettings)
        {
            _parent = parent; _runner = runner ?? new ThreadPoolWorkRunner();
            GeoMaterials.Configure(geoSettings);
        }

        Material TerrainMaterial => _terrain != null ? _terrain : (_terrain = GeoMaterials.Make(new Color(0.45f, 0.5f, 0.35f), null, "stream_terrain"));

        static Material MaterialFor(string key) => key == "water" ? GeoMaterials.Water : key == "terrain" ? null : GeoMaterials.Building;

        public Vector3 RootOffset(in ResidencyKey key)
        {
            return new Vector3((float)(key.Tile.OriginX - OriginX), 0f, (float)(key.Tile.OriginZ - OriginZ));
        }

        public bool TryGetRoot(in ResidencyKey key, out Transform t) { if (_nodes.TryGetValue(key, out var n)) { t = n.Root.transform; return true; } t = null; return false; }

        public void Build(in ResidencyKey key, TilePayload p, IBuildSink sink)
        {
            if (_nodes.ContainsKey(key)) Release(key);
            var node = new Node { Tile = key.Tile, Root = new GameObject("Stream_" + key) };
            node.X0 = key.Tile.OriginX; node.Z0 = key.Tile.OriginZ;   // payload meshes are tile-local, also for physics chunks
            node.Root.transform.SetParent(_parent, false);
            node.Root.transform.localPosition = RootOffset(key);
            _nodes[key] = node;
            bool physics = key.Kind == ResidencyKind.Physics;
            long gpu = 0, cpu = 0;
            foreach (var d in p.Meshes)
            {
                if (d.VertexCount == 0 || d.Indices.Length == 0) continue;
                // payload positions are tile-local (origin = tile corner); chunk meshes of a physics key stay tile-local too, so the root sits at the tile corner
                var mesh = GeoTileBuilder.ToMesh(d);
                node.Meshes.Add(mesh);
                var go = new GameObject(d.Name);
                go.transform.SetParent(node.Root.transform, false);
                if (physics) { go.AddComponent<MeshCollider>(); }
                else
                {
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = d.Material == "terrain" ? TerrainMaterial : MaterialFor(d.Material);
                }
                gpu += (long)d.VertexCount * 32 + d.Indices.Length * 4L;
                cpu += (long)d.VertexCount * 32 + d.Indices.Length * 4L;
            }
            if (!physics) { sink.Built(key, gpu, cpu); return; }
            var b = new Baking { Key = key, Node = node, Sink = sink, Gpu = 0, Cpu = cpu };
            var ids = new EntityId[node.Meshes.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = node.Meshes[i].GetEntityId();
            _baking.Add(b);
            _runner.Run(() =>
            {
                try { for (int i = 0; i < ids.Length; i++) Physics.BakeMesh(ids[i], false); }
                catch (Exception e) { b.Error = e.Message; }
                b.Done = true;
            });
        }

        /// <summary>Main thread, once per frame: attaches finished collider bakes (cheap: the cooked data is already on the mesh).</summary>
        public void Tick()
        {
            for (int i = _baking.Count - 1; i >= 0; i--)
            {
                var b = _baking[i];
                if (!b.Done) continue;
                _baking.RemoveAt(i);
                if (!_nodes.TryGetValue(b.Key, out var n) || n != b.Node) continue;   // released meanwhile
                if (b.Error != null) { b.Sink.BuildFailed(b.Key, "bake: " + b.Error); continue; }
                var cols = n.Root.GetComponentsInChildren<MeshCollider>(true);
                for (int c = 0; c < cols.Length && c < n.Meshes.Count; c++) cols[c].sharedMesh = n.Meshes[c];
                b.Sink.Built(b.Key, b.Gpu, b.Cpu);
            }
        }

        public void Release(in ResidencyKey key)
        {
            if (!_nodes.TryGetValue(key, out var n)) return;
            _nodes.Remove(key);
            for (int i = _baking.Count - 1; i >= 0; i--) if (_baking[i].Node == n) _baking.RemoveAt(i);
            foreach (var m in n.Meshes) if (m != null) Kill(m);
            if (n.Root != null) Kill(n.Root);
        }

        public void ShiftOrigin(double originX, double originZ)
        {
            if (originX % GeoTileId.BaseSizeM != 0 || originZ % GeoTileId.BaseSizeM != 0) throw new ArgumentException("origin must be a tile corner");
            var sw = Stopwatch.StartNew();
            OriginX = originX; OriginZ = originZ;
            foreach (var kv in _nodes) kv.Value.Root.transform.localPosition = new Vector3((float)(kv.Value.X0 - originX), 0f, (float)(kv.Value.Z0 - originZ));
            if (_nodes.Count > 0) Physics.SyncTransforms();
            LastShiftMs = sw.Elapsed.TotalMilliseconds;
        }

        static void Kill(UnityEngine.Object o) { if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o); }

        public void ReleaseAll() { foreach (var k in new List<ResidencyKey>(_nodes.Keys)) Release(k); }
    }
}
