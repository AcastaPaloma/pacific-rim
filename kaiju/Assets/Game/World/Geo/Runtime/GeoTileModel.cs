// One imported tile, engine-free: runs the IGeoLayer importers, keeps the building index, combines
// per-chunk render meshes, supports per-building removal by BuildingId without re-baking (F-043).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Game.Core;

namespace Game.World.Geo
{
    public sealed class GeoTileModel
    {
        public readonly GeoTileContext Context;
        public readonly List<GeoLayerOutput> Layers = new List<GeoLayerOutput>();
        public readonly List<GeoBuildingData> Buildings = new List<GeoBuildingData>();
        public readonly Dictionary<BuildingId, GeoBuildingData> ById = new Dictionary<BuildingId, GeoBuildingData>();
        public readonly Dictionary<BuildingId, List<GeoBuildingData>> PartsOf = new Dictionary<BuildingId, List<GeoBuildingData>>();
        public readonly HashSet<BuildingId> Removed = new HashSet<BuildingId>();
        public readonly SortedDictionary<string, double> Stats = new SortedDictionary<string, double>(StringComparer.Ordinal);
        public readonly SortedSet<string> Attributions = new SortedSet<string>(StringComparer.Ordinal);
        public GeoTileId Tile => Context.Tile;
        public GeoHeightField Heights => Context.Heights;
        public int BuildingChunks { get; private set; }

        /// <summary>Default importer order. Add a layer: implement IGeoLayer and pass a list here (no other edits).</summary>
        public static IGeoLayer[] DefaultLayers() => new IGeoLayer[] { new TerrainLayer(), new WaterLayer(), new BuildingsLayer(), new RoadsLayer(), new FaultsLayer() };

        public GeoTileModel(GeoTileContext ctx, IEnumerable<IGeoLayer> layers = null)
        {
            Context = ctx;
            var sw = Stopwatch.StartNew();
            foreach (var layer in layers ?? DefaultLayers())
            {
                var t0 = sw.Elapsed.TotalSeconds;
                var o = layer.Build(ctx);
                Layers.Add(o);
                foreach (var kv in o.Stats) Stats[kv.Key] = kv.Value;
                foreach (var a in o.Attributions) Attributions.Add(a);
                Stats["time." + layer.Name + "_s"] = sw.Elapsed.TotalSeconds - t0;
                foreach (var b in o.Buildings)
                {
                    Buildings.Add(b);
                    ById[b.Id] = b;
                }
            }
            foreach (var b in Buildings)
                if (b.PartOf.IsValid)
                {
                    if (!PartsOf.TryGetValue(b.PartOf, out var l)) PartsOf[b.PartOf] = l = new List<GeoBuildingData>();
                    l.Add(b);
                }
            BuildingChunks = (int)Math.Round(ctx.Tile.SizeM / GeoSettings.Require(ctx, GeoSettingKeys.BuildingChunkM));
            Stats["time.import_s"] = sw.Elapsed.TotalSeconds;
        }

        public GeoLayerOutput Layer(string name) => Layers.Find(l => l.Layer == name);

        /// <summary>Buildings removed by this call (the building itself and, for a structure, its parts). Idempotent.</summary>
        public List<GeoBuildingData> Remove(BuildingId id)
        {
            var res = new List<GeoBuildingData>();
            if (!ById.TryGetValue(id, out var b)) return res;
            if (Removed.Add(id)) res.Add(b);
            if (PartsOf.TryGetValue(id, out var parts))
                foreach (var p in parts) if (Removed.Add(p.Id)) res.Add(p);
            return res;
        }

        /// <summary>Combined render mesh of one building chunk, excluding removed buildings. Deterministic (sorted by id).</summary>
        public GeoMeshData CombineChunk(int cx, int cz, ICollection<BuildingId> alsoHide = null)
        {
            var pos = new List<float>(); var nrm = new List<float>(); var idx = new List<int>();
            foreach (var b in Buildings)
            {
                if (b.Mesh == null || b.ChunkX != cx || b.ChunkZ != cz || Removed.Contains(b.Id)) continue;
                if (alsoHide != null && (alsoHide.Contains(b.Id) || (b.PartOf.IsValid && alsoHide.Contains(b.PartOf)))) continue;
                int s = pos.Count / 3;
                pos.AddRange(b.Mesh.Positions); nrm.AddRange(b.Mesh.Normals);
                foreach (var i in b.Mesh.Indices) idx.Add(i + s);
            }
            return new GeoMeshData { Layer = "buildings", Material = "building", Name = "buildings_" + cx + "_" + cz, Positions = pos.ToArray(), Normals = nrm.ToArray(), Indices = idx.ToArray(), ChunkX = cx, ChunkZ = cz };
        }

        /// <summary>Unstyled terrain albedo (cells, row 0 = south, RGBA): land by height, seabed by depth, roads painted by class.</summary>
        public byte[] TerrainAlbedo(out int size)
        {
            int n = Heights.Samples - 1;
            size = n;
            var px = new byte[n * n * 4];
            var h = Heights.Data;
            double msl = Context.WaterLevelNavd88;
            double depthRamp = GeoSettings.Require(Context, GeoSettingKeys.AlbedoDepthRampM), heightRamp = GeoSettings.Require(Context, GeoSettingKeys.AlbedoHeightRampM);
            var sh = GeoSettings.Rgb(Context, "seabedShallow"); var sd = GeoSettings.Rgb(Context, "seabedDeep");
            var ll = GeoSettings.Rgb(Context, "landLow"); var lh = GeoSettings.Rgb(Context, "landHigh");
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                {
                    double hc = 0.25 * (h[r * (n + 1) + c] + h[r * (n + 1) + c + 1] + h[(r + 1) * (n + 1) + c] + h[(r + 1) * (n + 1) + c + 1]);
                    byte[] a, b; double t;
                    if (hc < msl) { t = Math.Min(1, (msl - hc) / depthRamp); a = sh; b = sd; }
                    else { t = Math.Min(1, (hc - msl) / heightRamp); a = ll; b = lh; }
                    int k = (r * n + c) * 4;
                    for (int q = 0; q < 3; q++) px[k + q] = (byte)Math.Round(a[q] + (b[q] - a[q]) * t);
                    px[k + 3] = 255;
                }
            var roads = Layer("roads");
            double minHalf = GeoSettings.Require(Context, GeoSettingKeys.RoadMinHalfWidthM);
            var rd1 = GeoSettings.Rgb(Context, "roadDrivable"); var rd2 = GeoSettings.Rgb(Context, "roadOther");
            if (roads != null)
                foreach (var rd in roads.Roads)
                {
                    if (rd.Bridge || rd.Tunnel) continue;
                    var col = rd.Drivable ? rd1 : rd2;
                    double half = Math.Max(rd.WidthM * 0.5, minHalf);
                    foreach (var l in rd.Lines)
                        for (int i = 0; i + 3 < l.Length; i += 2) Capsule(px, n, l[i], l[i + 1], l[i + 2], l[i + 3], half, col);
                }
            return px;
        }

        static void Capsule(byte[] px, int n, double x0, double z0, double x1, double z1, double half, byte[] v)
        {
            int cx0 = Math.Max(0, (int)Math.Floor(Math.Min(x0, x1) - half)), cx1 = Math.Min(n - 1, (int)Math.Ceiling(Math.Max(x0, x1) + half));
            int cz0 = Math.Max(0, (int)Math.Floor(Math.Min(z0, z1) - half)), cz1 = Math.Min(n - 1, (int)Math.Ceiling(Math.Max(z0, z1) + half));
            double dx = x1 - x0, dz = z1 - z0, L2 = dx * dx + dz * dz;
            for (int r = cz0; r <= cz1; r++)
                for (int c = cx0; c <= cx1; c++)
                {
                    double px0 = c + 0.5 - x0, pz0 = r + 0.5 - z0;
                    double t = L2 > 0 ? Math.Max(0, Math.Min(1, (px0 * dx + pz0 * dz) / L2)) : 0;
                    double ex = px0 - t * dx, ez = pz0 - t * dz;
                    if (ex * ex + ez * ez > half * half) continue;
                    int k = (r * n + c) * 4;
                    px[k] = v[0]; px[k + 1] = v[1]; px[k + 2] = v[2];
                }
        }
    }
}
