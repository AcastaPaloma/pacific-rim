// Unity adapter for World.Geo: turns an engine-free GeoTileModel into GameObjects (runtime and Editor API; never scene text).
using System;
using System.Collections.Generic;
using System.IO;
using Game.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.World.Geo.Unity
{
    public sealed class GeoBuildOptions
    {
        /// <summary>Floating origin in float64 BayTM. Must be a tile corner (multiple of 2048 m, R-004 §2.4).</summary>
        public double FloatingOriginX, FloatingOriginZ;
        public IReadOnlyDictionary<string, double> Settings;
        public bool BuildingColliders = true;
        public bool TerrainCollider = true;
        public bool VerifyHashes = true;
        public Transform Parent;
        /// <summary>Shared-border policy for tiles with neighbours in a set (GeoTileSetBuilder fills it); null for a single tile.</summary>
        public GeoEdgePolicy Edge;
    }

    public static class GeoTileBuilder
    {
        public const string ConfigFile = "Game/Config/WorldGeo.json";

        /// <summary>Settings from Assets/Game/Config/WorldGeo.json; throws if the file or any key is missing (no code defaults).</summary>
        public static Dictionary<string, double> LoadSettings(string projectAssetsPath = null) =>
            GeoSettings.Load(Path.Combine(projectAssetsPath ?? Application.dataPath, ConfigFile));

        public static string TileDirectory(GeoTileId id) =>
            Path.Combine(GeoModule.StreamingRoot, "Geo", "tiles", "v1", id.Folder.Replace('/', Path.DirectorySeparatorChar));

        /// <summary>Validates, imports and instantiates one tile. Throws GeoTileRejectedException for an incomplete manifest.</summary>
        public static GeoTileInstance Build(string tileDirectory, GeoBuildOptions opt)
        {
            if (opt.FloatingOriginX % GeoTileId.BaseSizeM != 0 || opt.FloatingOriginZ % GeoTileId.BaseSizeM != 0)
                throw new ArgumentException("floating origin must be a tile corner (multiple of 2048 m)");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var settings = opt.Settings ?? LoadSettings();
            GeoMaterials.Configure(settings);
            var ctx = new GeoTileContext(tileDirectory, settings, opt.VerifyHashes) { Edge = opt.Edge };
            var model = new GeoTileModel(ctx);
            var root = new GameObject("GeoTile_" + ctx.Tile);
            if (opt.Parent != null) root.transform.SetParent(opt.Parent, false);
            var inst = root.AddComponent<GeoTileInstance>();
            inst.Init(model, opt);
            model.Stats["time.unity_build_s"] = sw.Elapsed.TotalSeconds;
            return inst;
        }

        public static Mesh ToMesh(GeoMeshData d)
        {
            int n = d.VertexCount;
            var m = new Mesh { name = d.Name, indexFormat = n > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            var v = new Vector3[n];
            for (int i = 0; i < n; i++) v[i] = new Vector3(d.Positions[3 * i], d.Positions[3 * i + 1], d.Positions[3 * i + 2]);
            m.vertices = v;
            if (d.Normals != null && d.Normals.Length == d.Positions.Length)
            {
                var nn = new Vector3[n];
                for (int i = 0; i < n; i++) nn[i] = new Vector3(d.Normals[3 * i], d.Normals[3 * i + 1], d.Normals[3 * i + 2]);
                m.normals = nn;
            }
            if (d.Uv != null)
            {
                var uv = new Vector2[n];
                for (int i = 0; i < n; i++) uv[i] = new Vector2(d.Uv[2 * i], d.Uv[2 * i + 1]);
                m.uv = uv;
            }
            m.SetTriangles(d.Indices, 0, true);
            if (d.Normals == null) m.RecalculateNormals();
            return m;
        }
    }

    /// <summary>Shared unstyled materials (URP Lit), palette and smoothness from WorldGeo.json. The pixel pipeline (T-004) restyles them.</summary>
    public static class GeoMaterials
    {
        static Material _building, _water;
        static IReadOnlyDictionary<string, double> _cfg;

        public static void Configure(IReadOnlyDictionary<string, double> settings)
        {
            _cfg = settings;
            _building = null; _water = null;
        }

        static double Get(string k)
        {
            if (_cfg == null || !_cfg.TryGetValue(k, out var v)) throw new InvalidOperationException("[World.Geo] material setting '" + k + "' missing (GeoMaterials.Configure)");
            return v;
        }

        public static Color Palette(string name) => new Color((float)(Get("palette." + name + ".0") / 255.0), (float)(Get("palette." + name + ".1") / 255.0), (float)(Get("palette." + name + ".2") / 255.0));

        static Shader Lit()
        {
            var s = Shader.Find("Universal Render Pipeline/Lit");
            if (s == null) s = Shader.Find("Standard");
            return s;
        }

        public static Material Make(Color c, Texture tex = null, string name = "geo")
        {
            var m = new Material(Lit()) { name = name };
            m.SetColor("_BaseColor", c);
            m.color = c;
            if (tex != null) { m.SetTexture("_BaseMap", tex); m.mainTexture = tex; }
            m.SetFloat("_Smoothness", (float)Get(GeoSettingKeys.MaterialSmoothness));
            return m;
        }

        public static Material Building => _building != null ? _building : (_building = Make(Palette("building"), null, "geo_building"));
        public static Material Water => _water != null ? _water : (_water = Make(Palette("water"), null, "geo_water"));
    }

    /// <summary>Marks one building's addressable object (own collider; render goes through its chunk).</summary>
    public sealed class GeoBuilding : MonoBehaviour
    {
        public string Bid;
        public string CoreId;
        public string PartOf;
        public string HeightSource;
        public double BaseNavd88M, HeightM;
        public bool HasParts;
    }
}
