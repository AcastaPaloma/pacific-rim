// World.Geo settings: Assets/Game/Config/WorldGeo.json is the single source of truth (DEF-T003-arch-4).
using System;
using System.Collections.Generic;
using System.IO;

namespace Game.World.Geo
{
    public static class GeoSettings
    {
        /// <summary>Every key the importer reads; Load refuses a config that lacks one.</summary>
        public static readonly string[] RequiredKeys =
        {
            GeoSettingKeys.TerrainMaxErrorM, GeoSettingKeys.TerrainChunkCells, GeoSettingKeys.BuildingSkirtM, GeoSettingKeys.BuildingChunkM,
            GeoSettingKeys.WaterChunkCells, GeoSettingKeys.AlbedoDepthRampM, GeoSettingKeys.AlbedoHeightRampM, GeoSettingKeys.RoadMinHalfWidthM,
            GeoSettingKeys.MaterialSmoothness,
        };
        public static readonly string[] PaletteNames = { "seabedShallow", "seabedDeep", "landLow", "landHigh", "roadDrivable", "roadOther", "building", "water" };

        /// <summary>Flattens settings (numbers) and palette (palette.&lt;name&gt;.0..2, 0-255) into one map; throws if anything is missing.</summary>
        public static Dictionary<string, double> Load(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("[World.Geo] config missing: " + path);
            var root = GeoJson.Obj(GeoJson.Parse(File.ReadAllText(path)));
            var d = new Dictionary<string, double>(StringComparer.Ordinal);
            var s = GeoJson.Obj(GeoJson.Get(root, "settings"));
            if (s != null) foreach (var kv in s) if (kv.Value is double v) d[kv.Key] = v;
            var p = GeoJson.Obj(GeoJson.Get(root, "palette"));
            if (p != null)
                foreach (var kv in p)
                {
                    var a = GeoJson.Arr(kv.Value);
                    for (int i = 0; a != null && i < a.Count && i < 3; i++) d["palette." + kv.Key + "." + i] = (double)a[i];
                }
            var missing = new List<string>();
            foreach (var k in RequiredKeys) if (!d.ContainsKey(k)) missing.Add(k);
            foreach (var n in PaletteNames) for (int i = 0; i < 3; i++) if (!d.ContainsKey("palette." + n + "." + i)) missing.Add("palette." + n);
            if (missing.Count > 0) throw new InvalidDataException("[World.Geo] " + path + " lacks: " + string.Join(", ", missing));
            return d;
        }

        /// <summary>A required value; throws (never a silent code default) when absent.</summary>
        public static double Require(IGeoTileContext ctx, string key)
        {
            double v = ctx.Setting(key, double.NaN);
            if (double.IsNaN(v)) throw new InvalidDataException("[World.Geo] setting '" + key + "' missing (Assets/Game/Config/WorldGeo.json)");
            return v;
        }

        public static byte[] Rgb(IGeoTileContext ctx, string name) =>
            new[] { (byte)Require(ctx, "palette." + name + ".0"), (byte)Require(ctx, "palette." + name + ".1"), (byte)Require(ctx, "palette." + name + ".2") };
    }
}
