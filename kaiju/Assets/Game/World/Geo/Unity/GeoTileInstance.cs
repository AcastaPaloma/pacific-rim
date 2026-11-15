// One built tile in the scene: chunk renderers, per-building addressable objects with colliders,
// a full-resolution TerrainCollider, removal by BuildingId, and the as-built measurement export (geo_export@v1).
using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Core;
using UnityEngine;

namespace Game.World.Geo.Unity
{
    public sealed class GeoTileInstance : MonoBehaviour
    {
        public GeoTileModel Model { get; private set; }
        public double FloatingOriginX { get; private set; }
        public double FloatingOriginZ { get; private set; }
        public string TileName;
        public Texture2D Albedo { get; private set; }
        public TerrainCollider PhysicsSurface { get; private set; }

        readonly Dictionary<(int, int), MeshFilter> _buildingChunks = new Dictionary<(int, int), MeshFilter>();
        readonly Dictionary<BuildingId, GameObject> _objects = new Dictionary<BuildingId, GameObject>();
        readonly List<Renderer> _renderers = new List<Renderer>();
        Transform _bRoot;

        public IReadOnlyList<Renderer> Renderers => _renderers;

        internal void Init(GeoTileModel model, GeoBuildOptions opt)
        {
            Model = model;
            TileName = model.Tile.ToString();
            FloatingOriginX = opt.FloatingOriginX;
            FloatingOriginZ = opt.FloatingOriginZ;
            // tile-local frame: root sits at (tile origin - floating origin); exact in float32 (multiples of 2048)
            transform.localPosition = new Vector3((float)(model.Context.OriginX - opt.FloatingOriginX), 0f, (float)(model.Context.OriginZ - opt.FloatingOriginZ));

            var albedo = model.TerrainAlbedo(out int asz);
            Albedo = new Texture2D(asz, asz, TextureFormat.RGBA32, false) { name = TileName + "_albedo", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
            Albedo.SetPixelData(albedo, 0);
            Albedo.Apply(false, false);
            var terrainMat = GeoMaterials.Make(Color.white, Albedo, "geo_terrain");

            var tRoot = Child("Terrain");
            foreach (var m in model.Layer("terrain").Meshes) AddRenderer(tRoot, m, terrainMat);
            var wRoot = Child("Water");
            foreach (var m in model.Layer("water").Meshes) AddRenderer(wRoot, m, GeoMaterials.Water);

            _bRoot = Child("Buildings");
            var chunkRoot = Child("BuildingChunks");
            for (int cz = 0; cz < model.BuildingChunks; cz++)
                for (int cx = 0; cx < model.BuildingChunks; cx++)
                {
                    var d = model.CombineChunk(cx, cz);
                    if (d.VertexCount == 0) continue;
                    _buildingChunks[(cx, cz)] = AddRenderer(chunkRoot, d, GeoMaterials.Building).GetComponent<MeshFilter>();
                }
            foreach (var b in model.Buildings)
            {
                var go = new GameObject(b.Bid);
                go.transform.SetParent(_bRoot, false);
                var gb = go.AddComponent<GeoBuilding>();
                gb.Bid = b.Bid; gb.CoreId = b.Id.ToString(); gb.PartOf = b.PartOf.IsValid ? b.PartOf.ToString() : null;
                gb.HeightSource = b.HeightSource; gb.BaseNavd88M = b.BaseNavd88M; gb.HeightM = b.HeightM; gb.HasParts = b.HasParts;
                if (b.Mesh != null && opt.BuildingColliders)
                {
                    var mc = go.AddComponent<MeshCollider>();
                    mc.sharedMesh = GeoTileBuilder.ToMesh(b.Mesh);
                }
                _objects[b.Id] = go;
            }

            BuildLandmarks(model);

            if (opt.TerrainCollider) PhysicsSurface = BuildTerrainCollider(model);
        }

        /// <summary>Landmark physics layer (R-020 §3): static, never destroyed, not indexed by BuildingId (RemoveBuilding cannot touch them).</summary>
        public const string LandmarkLayerName = "LandmarkStatic";
        static bool _layerErrorLogged;
        public Transform LandmarksRoot { get; private set; }

        void BuildLandmarks(GeoTileModel model)
        {
            var list = model.Layer("landmarks")?.Landmarks;
            if (list == null || list.Count == 0) return;
            int layer = LayerMask.NameToLayer(LandmarkLayerName);
            if (layer < 0)
            {
                if (!_layerErrorLogged) { _layerErrorLogged = true; Debug.LogError("[World.Geo] physics layer '" + LandmarkLayerName + "' is not defined (ProjectSettings/TagManager); landmarks stay on layer 0"); }
                layer = 0;
            }
            LandmarksRoot = Child("Landmarks");
            foreach (var lm in list)
            {
                var go = new GameObject(lm.Lid);
                go.transform.SetParent(LandmarksRoot, false);
                go.layer = layer; go.isStatic = true;
                if (lm.Mesh != null && lm.Mesh.VertexCount > 0)
                {
                    go.AddComponent<MeshFilter>().sharedMesh = GeoTileBuilder.ToMesh(lm.Mesh);
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = GeoMaterials.Building;
                    _renderers.Add(mr);
                }
                for (int i = 0; i < lm.Colliders.Count; i++)
                {
                    var c = lm.Colliders[i];
                    var cg = new GameObject(lm.Lid + "_c" + i);
                    cg.transform.SetParent(go.transform, false);
                    cg.layer = layer; cg.isStatic = true;
                    var p0 = new Vector3((float)c.P0X, (float)c.P0Y, (float)c.P0Z);
                    var p1 = new Vector3((float)c.P1X, (float)c.P1Y, (float)c.P1Z);
                    var d = p1 - p0;
                    float len = d.magnitude;
                    cg.transform.localPosition = (p0 + p1) * 0.5f;
                    if (len < 1e-6f) { cg.transform.localRotation = Quaternion.identity; }
                    else if (c.Shape == GeoColliderShape.Box)
                    {
                        // local Z = P0->P1, local X = horizontal side vector (cos yaw, 0, sin yaw), local Y = Z x X (= Depth axis; vertical-ish for deck, horizontal for a vertical leg)
                        var f = d / len;
                        var w = new Vector3((float)Math.Cos(c.YawRad), 0f, (float)Math.Sin(c.YawRad));
                        var up = Vector3.Cross(f, w);
                        cg.transform.localRotation = up.sqrMagnitude > 1e-12f ? Quaternion.LookRotation(f, up) : Quaternion.LookRotation(f);
                        var bc = cg.AddComponent<BoxCollider>();
                        bc.size = new Vector3((float)c.Width, (float)c.Depth, len);
                    }
                    else
                    {
                        var f = d / len;
                        cg.transform.localRotation = Quaternion.LookRotation(f, Mathf.Abs(f.y) < 0.99f ? Vector3.up : Vector3.right);
                        var cc = cg.AddComponent<CapsuleCollider>();
                        cc.direction = 2; cc.radius = (float)c.Radius; cc.height = len + 2f * (float)c.Radius;
                    }
                }
            }
        }

        Transform Child(string n)
        {
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            return go.transform;
        }

        GameObject AddRenderer(Transform parent, GeoMeshData d, Material mat)
        {
            var go = new GameObject(d.Name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = GeoTileBuilder.ToMesh(d);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            _renderers.Add(mr);
            return go;
        }

        /// <summary>Collider height lattice step (GeoHeightLattice). Unity stores 15-bit levels (0..32766 over size.y), so levels are integer multiples of the step and edge heights snapped by GeoEdgePolicy are represented exactly.</summary>
        public const double ColliderStepM = GeoHeightLattice.StepM;

        TerrainCollider BuildTerrainCollider(GeoTileModel model)
        {
            var hf = model.Heights;
            int n = hf.Samples;
            GeoHeightLattice.Plan(hf.Data, out double step, out long kMin);
            double baseY = kMin * step, range = GeoHeightLattice.MaxLevel * step;
            var td = new TerrainData { heightmapResolution = n };
            td.size = new Vector3((float)((n - 1) * hf.Spacing), (float)range, (float)((n - 1) * hf.Spacing));
            var h = new float[n, n];
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                    h[r, c] = (float)(GeoHeightLattice.Level(hf.Data[r * n + c], step, kMin) / (double)GeoHeightLattice.MaxLevel);
            td.SetHeights(0, 0, h);
            var go = new GameObject("PhysicsSurface");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0, (float)baseY, 0);
            var tc = go.AddComponent<TerrainCollider>();
            tc.terrainData = td;
            return tc;
        }

        /// <summary>Removes a building (a structure takes its parts) without re-baking; rebuilds only the touched chunks.</summary>
        public List<GeoBuildingData> RemoveBuilding(BuildingId id)
        {
            var removed = Model.Remove(id);
            var dirty = new HashSet<(int, int)>();
            foreach (var b in removed)
            {
                if (_objects.TryGetValue(b.Id, out var go))
                {
                    if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
                    _objects.Remove(b.Id);
                }
                if (b.Mesh != null) dirty.Add((b.ChunkX, b.ChunkZ));
            }
            foreach (var k in dirty)
                if (_buildingChunks.TryGetValue(k, out var mf)) mf.sharedMesh = GeoTileBuilder.ToMesh(Model.CombineChunk(k.Item1, k.Item2));
            return removed;
        }

        /// <summary>Capture-only: render chunks without the given structures (e.g. the observer's own tower), or restore with null. Model untouched.</summary>
        public void SetRenderExclusion(ICollection<BuildingId> hide)
        {
            foreach (var kv in _buildingChunks) kv.Value.sharedMesh = GeoTileBuilder.ToMesh(Model.CombineChunk(kv.Key.Item1, kv.Key.Item2, hide));
        }

        public bool HasObject(BuildingId id) => _objects.ContainsKey(id);

        /// <summary>Floating-origin rebase (F-042): new origin must be a tile corner; the root moves by an exact integer.</summary>
        public void ShiftOrigin(double originX, double originZ)
        {
            if (originX % GeoTileId.BaseSizeM != 0 || originZ % GeoTileId.BaseSizeM != 0) throw new ArgumentException("origin must be a tile corner");
            FloatingOriginX = originX; FloatingOriginZ = originZ;
            transform.localPosition = new Vector3((float)(Model.Context.OriginX - originX), 0f, (float)(Model.Context.OriginZ - originZ));
            Physics.SyncTransforms();
        }

        /// <summary>float64 BayTM of a scene position (Unity position + floating origin).</summary>
        public void ToWorld(Vector3 p, out double x, out double y, out double z)
        {
            x = p.x + FloatingOriginX; y = p.y; z = p.z + FloatingOriginZ;
        }

        /// <summary>Physics surface height (TerrainCollider raycast) at float64 BayTM; NaN if missed.</summary>
        public double RaycastTerrain(double x, double z)
        {
            var o = new Vector3((float)(x - FloatingOriginX), 2000f, (float)(z - FloatingOriginZ));
            if (PhysicsSurface != null && PhysicsSurface.Raycast(new Ray(o, Vector3.down), out var hit, 5000f)) return hit.point.y;
            return double.NaN;
        }

        public long TriangleCount(string layer = null)
        {
            long t = 0;
            foreach (var r in _renderers)
            {
                if (r == null) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                if (layer != null && !r.transform.parent.name.StartsWith(layer, StringComparison.Ordinal)) continue;
                for (int s = 0; s < mf.sharedMesh.subMeshCount; s++) t += mf.sharedMesh.GetIndexCount(s) / 3;
            }
            return t;
        }

        // ---------------------------------------------------------------- geo_export@v1
        /// <summary>As-built facts in float64 BayTM (positions = scene position + floating origin), for the M-005@v1 checker.</summary>
        public Dictionary<string, object> Export(IList<double[]> controlPoints)
        {
            var root = transform.position;
            var buildings = new List<object>();
            foreach (var b in Model.Buildings)
            {
                if (Model.Removed.Contains(b.Id)) continue;
                double top = double.NaN, minY = double.NaN;
                if (b.Mesh != null)
                {
                    top = double.MinValue; minY = double.MaxValue;
                    for (int i = 1; i < b.Mesh.Positions.Length; i += 3)
                    {
                        double y = root.y + b.Mesh.Positions[i];
                        if (y > top) top = y; if (y < minY) minY = y;
                    }
                }
                // footprint as built: float32 tile-local vertices placed by the root, back to float64 world
                var polys = new List<object>();
                foreach (var poly in b.RenderPolygons ?? b.Polygons)
                {
                    var rings = new List<object>();
                    foreach (var ring in poly)
                    {
                        var pts = new List<object>(ring.Length / 2);
                        for (int i = 0; i + 1 < ring.Length; i += 2)
                            pts.Add(new List<object> { Math.Round(root.x + (double)(float)ring[i] + FloatingOriginX, 4), Math.Round(root.z + (double)(float)ring[i + 1] + FloatingOriginZ, 4) });
                        rings.Add(pts);
                    }
                    polys.Add(rings);
                }
                double ax = root.x + (double)(float)b.AnchorLocalX + FloatingOriginX, az = root.z + (double)(float)b.AnchorLocalZ + FloatingOriginZ;
                buildings.Add(new Dictionary<string, object>
                {
                    ["bid"] = b.Bid, ["core_id"] = b.Id.ToString(), ["part_of"] = b.PartOf.IsValid ? Model.ById[b.PartOf].Bid : null,
                    ["has_parts"] = b.HasParts, ["height_source"] = b.HeightSource, ["base_navd88_m"] = b.BaseNavd88M,
                    ["source_height_m"] = b.HeightM, ["top_navd88_m"] = top, ["mesh_min_y_m"] = minY,
                    ["height_as_built_m"] = b.Mesh != null ? top - b.BaseNavd88M : double.NaN,
                    ["anchor_baytm"] = new List<object> { Math.Round(ax, 4), Math.Round(az, 4) }, ["area_m2"] = b.AreaM2,
                    ["footprint_is_remainder"] = b.RenderPolygons != null, ["excluded"] = b.Excluded, ["min_height_m"] = b.MinHeightM,
                    ["ground_min_navd88_m"] = b.GroundMinNavd88M, ["height_capped_to_parent"] = b.HeightCappedToParent, ["outline_area_m2"] = b.AreaM2,
                    ["has_collider"] = _objects.TryGetValue(b.Id, out var go) && go.GetComponent<MeshCollider>() != null,
                    ["footprint"] = polys,
                });
            }
            var landmarks = new List<object>();
            if (LandmarksRoot != null)
                foreach (var lm in Model.Layer("landmarks").Landmarks)
                {
                    var t = LandmarksRoot.Find(lm.Lid);
                    double top = double.NaN, bot = double.NaN;
                    if (t != null)
                    {
                        bool any = false; Bounds bb = default;
                        var mr = t.GetComponent<MeshRenderer>();
                        if (mr != null) { bb = mr.bounds; any = true; }
                        else foreach (var col in t.GetComponentsInChildren<Collider>()) { if (!any) { bb = col.bounds; any = true; } else bb.Encapsulate(col.bounds); }
                        if (any)
                        {
                            ToWorld(bb.max, out _, out double ty, out _); ToWorld(bb.min, out _, out double by, out _);
                            top = ty; bot = by;
                        }
                    }
                    List<object> fp = null;
                    if (lm.Footprint != null)
                    {
                        fp = new List<object>();
                        foreach (var ring in lm.Footprint)
                        {
                            var pts = new List<object>(ring.Length / 2);
                            for (int i = 0; i + 1 < ring.Length; i += 2)
                                pts.Add(new List<object> { Math.Round(root.x + (double)(float)ring[i] + FloatingOriginX, 4), Math.Round(root.z + (double)(float)ring[i + 1] + FloatingOriginZ, 4) });
                            fp.Add(pts);
                        }
                    }
                    landmarks.Add(new Dictionary<string, object>
                    {
                        ["lid"] = lm.Lid, ["bid"] = lm.Bid, ["kind"] = lm.Kind, ["piece"] = lm.Piece,
                        ["base_navd88_m"] = Round(bot), ["top_navd88_m"] = Round(top), ["height_m"] = Round(top - bot),
                        ["anchor"] = new List<object> { Math.Round(root.x + (double)(float)lm.AnchorLocalX + FloatingOriginX, 4), Math.Round(root.z + (double)(float)lm.AnchorLocalZ + FloatingOriginZ, 4) },
                        ["footprint"] = fp, ["colliders"] = lm.Colliders.Count,
                    });
                }
            var samples = new List<object>();
            if (controlPoints != null)
                foreach (var p in controlPoints)
                {
                    double lx = p[0] - Model.Context.OriginX, lz = p[1] - Model.Context.OriginZ;
                    samples.Add(new List<object> { p[0], p[1], Round(RaycastTerrain(p[0], p[1])), Round(Model.Heights.SampleRender(lx, lz)), Round(Model.Heights.SampleData(lx, lz)) });
                }
            var stats = new Dictionary<string, object>();
            foreach (var kv in Model.Stats) stats[kv.Key] = kv.Value;
            stats["renderers"] = _renderers.Count;
            stats["triangles.total"] = TriangleCount();
            stats["building_objects"] = _objects.Count;
            stats["removed"] = Model.Removed.Count;
            double maxAbs = 0;
            foreach (var r in _renderers)
            {
                if (r == null) continue;
                var bnd = r.bounds;
                maxAbs = Math.Max(maxAbs, Math.Max(Math.Max(Math.Abs(bnd.min.x), Math.Abs(bnd.max.x)), Math.Max(Math.Abs(bnd.min.z), Math.Abs(bnd.max.z))));
            }
            stats["max_abs_scene_position_m"] = maxAbs;
            return new Dictionary<string, object>
            {
                ["spec"] = GeoContractVersions.ExportSpec, ["tile"] = TileName,
                ["floating_origin_baytm"] = new List<object> { FloatingOriginX, FloatingOriginZ },
                ["tile_root_scene_position"] = new List<object> { (double)root.x, (double)root.y, (double)root.z },
                ["water_level_navd88_m"] = Model.Context.WaterLevelNavd88,
                ["buildings"] = buildings,
                ["landmarks"] = landmarks,
                ["terrain_samples_columns"] = new List<object> { "x", "z", "physics_navd88_m", "render_navd88_m", "data_navd88_m" },
                ["terrain_samples"] = samples,
                ["stats"] = stats,
                ["attributions"] = new List<object>(Model.Attributions),
            };
        }

        static double Round(double v) => double.IsNaN(v) ? double.NaN : Math.Round(v, 4);
    }
}
