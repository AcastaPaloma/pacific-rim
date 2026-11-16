// Main-thread side of the streaming host (T-109): turns TilePayloads into GameObjects. Render keys become meshes at (tile origin - render
// origin); physics keys become MeshColliders whose cooking (Physics.BakeMesh) runs on a worker so the main thread only attaches the result.
// ShiftOrigin moves tile roots by exact integers (origin is a tile corner) and calls Physics.SyncTransforms once.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.SceneManagement;
using Game.World.Geo;
using Game.World.Geo.Unity;
using Game.World.Streaming.Host;

namespace Game.World.Streaming.Unity
{
    public sealed class UnityTileBuilder : ITileBuilder, IClusterBuilder
    {
        sealed class Node
        {
            public GameObject Root;
            public readonly List<Mesh> Meshes = new List<Mesh>();
            public GeoTileId Tile; public double X0, Z0;
            public int Cluster = int.MinValue;   // physics nodes in cluster mode
            public bool IsPhysics;
        }
        sealed class Baking { public ResidencyKey Key; public Node Node; public IBuildSink Sink; public volatile bool Done; public volatile string Error; public long Gpu, Cpu; }

        // T-124b: sliced render builds. The managed array conversion of a payload mesh runs on the work runner; the main thread then does one cheap stage
        // (vertices, normals, uv, triangles) per mesh per step inside a time budget, so a 46 ms L3 tile no longer lands in one frame.
        sealed class MeshJob
        {
            public Node Node; public GeoMeshData D; public GameObject Go; public Mesh M; public int Stage;
            public Vector3[] V, N; public Vector2[] UV; public volatile bool Ready;
        }
        sealed class PendingRender { public ResidencyKey Key; public Node Node; public IBuildSink Sink; public int Left; public long Gpu, Cpu; }
        readonly List<MeshJob> _jobs = new List<MeshJob>();
        readonly Dictionary<Node, PendingRender> _pendingRender = new Dictionary<Node, PendingRender>();
        /// <summary>Feature flag (default false = old single-frame build). True: render payload meshes are built over several Ticks, at most RenderSliceMs of main-thread work per Tick (at least one stage).</summary>
        public bool SliceRenderBuilds;
        public double RenderSliceMs = 2.0;
        /// <summary>Longest single main-thread stage of the sliced path (ms), since construction; profile.</summary>
        public double MaxStageMs { get; private set; }
        public int RenderJobsPending => _jobs.Count;

        readonly Transform _parent;
        readonly IWorkRunner _runner;
        readonly Dictionary<ResidencyKey, Node> _nodes = new Dictionary<ResidencyKey, Node>();
        readonly List<Baking> _baking = new List<Baking>();
        readonly Dictionary<ResidencyKey, int> _assigned = new Dictionary<ResidencyKey, int>();
        Material _terrain;
        ClusterPhysicsScenes _scenes;

        public double OriginX { get; private set; }
        public double OriginZ { get; private set; }
        /// <summary>Milliseconds of the last ShiftOrigin (M-101 counts a rebase frame as a streaming frame).</summary>
        public double LastShiftMs { get; private set; }
        public int ResidentNodes => _nodes.Count;
        public int ColliderBakesPending => _baking.Count;
        public Transform Parent => _parent;
        /// <summary>Cluster mode (T-111): physics chunks go into the PhysicsScene of the cluster that needs them, at (tile corner - cluster origin). Null = M1 behaviour, one frame.</summary>
        public ClusterPhysicsScenes ClusterScenes { get => _scenes; set => _scenes = value; }
        /// <summary>Milliseconds of the last ShiftClusterOrigin.</summary>
        public double LastClusterShiftMs { get; private set; }

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
            bool physics = key.Kind == ResidencyKind.Physics;
            var node = new Node { Tile = key.Tile, Root = new GameObject("Stream_" + key), IsPhysics = physics };
            node.X0 = key.Tile.OriginX; node.Z0 = key.Tile.OriginZ;   // payload meshes are tile-local, also for physics chunks
            if (physics && _scenes != null)
            {
                _assigned.TryGetValue(key, out int cid);
                if (!_assigned.ContainsKey(key)) cid = -1;
                Attach(node, cid);
            }
            else
            {
                node.Root.transform.SetParent(_parent, false);
                node.Root.transform.localPosition = RootOffset(key);
            }
            _nodes[key] = node;
            long gpu = 0, cpu = 0;
            if (!physics && SliceRenderBuilds)
            {
                var pr = new PendingRender { Key = key, Node = node, Sink = sink };
                foreach (var d in p.Meshes)
                {
                    if (d.VertexCount == 0 || d.Indices.Length == 0) continue;
                    var go = new GameObject(d.Name); go.transform.SetParent(node.Root.transform, false);
                    var job = new MeshJob { Node = node, D = d, Go = go };
                    _jobs.Add(job); pr.Left++;
                    pr.Gpu += (long)d.VertexCount * 32 + d.Indices.Length * 4L; pr.Cpu += (long)d.VertexCount * 32 + d.Indices.Length * 4L;
                    _runner.Run(() => Prepare(job));
                }
                if (pr.Left == 0) { sink.Built(key, 0, 0); return; }
                _pendingRender[node] = pr;
                return;
            }
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

        static void Prepare(MeshJob j)
        {
            var d = j.D; int n = d.VertexCount;
            var v = new Vector3[n];
            for (int i = 0; i < n; i++) v[i] = new Vector3(d.Positions[3 * i], d.Positions[3 * i + 1], d.Positions[3 * i + 2]);
            j.V = v;
            if (d.Normals != null && d.Normals.Length == d.Positions.Length)
            {
                var nn = new Vector3[n];
                for (int i = 0; i < n; i++) nn[i] = new Vector3(d.Normals[3 * i], d.Normals[3 * i + 1], d.Normals[3 * i + 2]);
                j.N = nn;
            }
            if (d.Uv != null)
            {
                var uv = new Vector2[n];
                for (int i = 0; i < n; i++) uv[i] = new Vector2(d.Uv[2 * i], d.Uv[2 * i + 1]);
                j.UV = uv;
            }
            j.Ready = true;
        }

        void TickRenderJobs()
        {
            if (_jobs.Count == 0) return;
            long t0 = Stopwatch.GetTimestamp(); double toMs = 1000.0 / Stopwatch.Frequency; int staged = 0;
            while (_jobs.Count > 0)
            {
                if (staged > 0 && (Stopwatch.GetTimestamp() - t0) * toMs >= RenderSliceMs) break;
                MeshJob j = null; int at = -1;
                for (int i = 0; i < _jobs.Count; i++) if (_jobs[i].Ready) { j = _jobs[i]; at = i; break; }
                if (j == null) break;
                if (j.Go == null) { _jobs.RemoveAt(at); continue; }
                long ts = Stopwatch.GetTimestamp(); var d = j.D;
                switch (j.Stage)
                {
                    case 0: j.M = new Mesh { name = d.Name, indexFormat = d.VertexCount > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 }; j.M.vertices = j.V; j.V = null; break;
                    case 1: if (j.N != null) j.M.normals = j.N; j.N = null; if (j.UV != null) j.M.uv = j.UV; j.UV = null; break;
                    default: j.M.SetTriangles(d.Indices, 0, true); if (d.Normals == null) j.M.RecalculateNormals(); break;
                }
                j.Stage++; staged++;
                MaxStageMs = Math.Max(MaxStageMs, (Stopwatch.GetTimestamp() - ts) * toMs);
                if (j.Stage < 3) continue;
                _jobs.RemoveAt(at);
                j.Node.Meshes.Add(j.M);
                j.Go.AddComponent<MeshFilter>().sharedMesh = j.M;
                var mr = j.Go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = d.Material == "terrain" ? TerrainMaterial : MaterialFor(d.Material);
                if (_pendingRender.TryGetValue(j.Node, out var pr) && --pr.Left == 0) { _pendingRender.Remove(j.Node); pr.Sink.Built(pr.Key, pr.Gpu, pr.Cpu); }
            }
        }

        /// <summary>Main thread, once per frame: attaches finished collider bakes (cheap: the cooked data is already on the mesh).</summary>
        public void Tick()
        {
            TickRenderJobs();
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

        // puts a physics node into the scene of its cluster at (tile corner - cluster origin); exact in float32 because both are multiples of 2048
        void Attach(Node node, int cluster)
        {
            var cs = _scenes.GetOrCreate(cluster);
            if (node.Root.scene != cs.Scene) { node.Root.transform.SetParent(null, false); SceneManager.MoveGameObjectToScene(node.Root, cs.Scene); }
            node.Cluster = cluster;
            node.Root.transform.position = new Vector3((float)(node.X0 - cs.OriginX), 0f, (float)(node.Z0 - cs.OriginZ));
        }

        public void AssignCluster(in ResidencyKey key, int clusterId)
        {
            _assigned[key] = clusterId;
            if (_scenes != null && _nodes.TryGetValue(key, out var n) && n.IsPhysics && n.Cluster != clusterId) { Attach(n, clusterId); Physics.SyncTransforms(); }
        }

        public void ShiftClusterOrigin(int clusterId, double originX, double originZ)
        {
            if (_scenes == null) return;
            if (originX % GeoTileId.BaseSizeM != 0 || originZ % GeoTileId.BaseSizeM != 0) throw new ArgumentException("origin must be a tile corner");
            var sw = Stopwatch.StartNew();
            _scenes.SetOrigin(clusterId, originX, originZ);
            bool any = false;
            foreach (var kv in _nodes)
            {
                var n = kv.Value;
                if (!n.IsPhysics || n.Cluster != clusterId) continue;
                n.Root.transform.position = new Vector3((float)(n.X0 - originX), 0f, (float)(n.Z0 - originZ));
                any = true;
            }
            if (any) Physics.SyncTransforms();
            LastClusterShiftMs = sw.Elapsed.TotalMilliseconds;
        }

        public void RemoveCluster(int clusterId)
        {
            if (_scenes == null) return;
            foreach (var n in _nodes.Values) if (n.IsPhysics && n.Cluster == clusterId) n.Cluster = int.MinValue;   // its nodes are re-attached by AssignCluster or released
            _scenes.Remove(clusterId);
        }

        public void Release(in ResidencyKey key)
        {
            _assigned.Remove(key);
            if (!_nodes.TryGetValue(key, out var n)) return;
            _nodes.Remove(key);
            for (int i = _baking.Count - 1; i >= 0; i--) if (_baking[i].Node == n) _baking.RemoveAt(i);
            for (int i = _jobs.Count - 1; i >= 0; i--) if (_jobs[i].Node == n) { if (_jobs[i].M != null) Kill(_jobs[i].M); _jobs.RemoveAt(i); }
            _pendingRender.Remove(n);
            foreach (var m in n.Meshes) if (m != null) Kill(m);
            if (n.Root != null) Kill(n.Root);
        }

        public void ShiftOrigin(double originX, double originZ)
        {
            if (originX % GeoTileId.BaseSizeM != 0 || originZ % GeoTileId.BaseSizeM != 0) throw new ArgumentException("origin must be a tile corner");
            var sw = Stopwatch.StartNew();
            OriginX = originX; OriginZ = originZ;
            foreach (var kv in _nodes)
            {
                if (_scenes != null && kv.Value.IsPhysics) continue;   // cluster mode: physics chunks follow their cluster's origin, not the render origin
                kv.Value.Root.transform.localPosition = new Vector3((float)(kv.Value.X0 - originX), 0f, (float)(kv.Value.Z0 - originZ));
            }
            if (_nodes.Count > 0) Physics.SyncTransforms();
            LastShiftMs = sw.Elapsed.TotalMilliseconds;
        }

        static void Kill(UnityEngine.Object o) { if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o); }

        public void ReleaseAll() { foreach (var k in new List<ResidencyKey>(_nodes.Keys)) Release(k); }
    }
}
