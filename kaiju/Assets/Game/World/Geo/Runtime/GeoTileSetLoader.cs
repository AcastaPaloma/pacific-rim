// Resolves tile keys to validated manifests, refuses mixed versions, reads edge profiles and building ids (T-021 step 1).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Game.World.Geo
{
    public static class GeoTileSetLoader
    {
        /// <summary>LayerApi this loader understands (GeoContractVersions.LayerApi, unchanged at 1).</summary>
        public const int SupportedLayerApi = 1;
        public const string ProjectionPrefix = "kaiju:BayTM@v1";

        /// <summary>tilesRoot = .../StreamingAssets/Geo/tiles/v1. Folder of a tile = tilesRoot/L0/30_36.</summary>
        public static string TileDirectory(string tilesRoot, GeoTileId id) => Path.Combine(tilesRoot, id.Folder.Replace('/', Path.DirectorySeparatorChar));

        /// <summary>Tiles in the list whose folder (manifest.json) is absent.</summary>
        public static List<GeoTileId> Absent(string tilesRoot, IEnumerable<GeoTileId> ids) =>
            ids.Where(t => !File.Exists(Path.Combine(TileDirectory(tilesRoot, t), "manifest.json"))).ToList();

        /// <summary>
        /// Loads every manifest, validates each (spec, projection, tile size, level, origin, completeness, optional file hashes)
        /// and refuses a set whose tiles disagree on spec / projection / vertical datum / tile size / water level.
        /// Throws GeoTileSetRejectedException listing every problem.
        /// </summary>
        public static GeoTileSet Load(string tilesRoot, IEnumerable<GeoTileId> ids, bool verifyHashes = false, int layerApi = GeoContractVersions.LayerApi)
        {
            var set = new GeoTileSet(ids);
            var problems = new List<string>();
            if (layerApi != SupportedLayerApi) problems.Add("LayerApi " + layerApi + " is not supported (loader understands " + SupportedLayerApi + ")");
            var mans = new Dictionary<GeoTileId, IReadOnlyDictionary<string, object>>();
            foreach (var t in set.Tiles)
            {
                var dir = TileDirectory(tilesRoot, t);
                var path = Path.Combine(dir, "manifest.json");
                if (!File.Exists(path)) { problems.Add(t + ": tile folder absent (" + dir + ")"); continue; }
                IReadOnlyDictionary<string, object> man;
                try { man = GeoJson.Obj(GeoJson.Parse(File.ReadAllText(path))); }
                catch (FormatException e) { problems.Add(t + ": manifest.json malformed: " + e.Message); continue; }
                var local = dir;
                foreach (var p in ValidateOne(t, man, verifyHashes ? (Func<string, byte[]>)(f => File.ReadAllBytes(Path.Combine(local, f))) : null)) problems.Add(t + ": " + p);
                mans[t] = man;
                set.Attach(t, dir, man);
            }
            problems.AddRange(CheckConsistent(mans));
            if (problems.Count > 0) throw new GeoTileSetRejectedException(problems);
            return set;
        }

        /// <summary>Problems of one manifest against its own tile key and the versions this build accepts.</summary>
        public static List<string> ValidateOne(GeoTileId id, IReadOnlyDictionary<string, object> man, Func<string, byte[]> readFile = null)
        {
            var p = GeoManifestValidator.Validate(man, readFile);
            if (man == null) return p;
            var crs = GeoJson.Str(man, "crs");
            if (crs != null && !crs.StartsWith(ProjectionPrefix, StringComparison.Ordinal)) p.Add("projection '" + crs + "' is not " + ProjectionPrefix);
            var size = GeoJson.Num(man, "tile_size_m");
            if (double.IsNaN(size)) p.Add("missing tile_size_m");
            else if (size != id.SizeM) p.Add("tile_size_m " + size + " does not match level " + id.Level + " (" + id.SizeM + ")");
            if (GeoJson.Str(man, "tile") != id.ToString()) p.Add("manifest tile '" + GeoJson.Str(man, "tile") + "' does not match the requested key");
            if (GeoJson.Num(man, "level") != id.Level || GeoJson.Num(man, "i") != id.I || GeoJson.Num(man, "j") != id.J) p.Add("manifest level/i/j do not match the tile key");
            return p;
        }

        static List<string> CheckConsistent(Dictionary<GeoTileId, IReadOnlyDictionary<string, object>> mans)
        {
            var p = new List<string>();
            foreach (var key in new[] { "spec", "crs", "vertical" })
                Mixed(p, key, mans, m => GeoJson.Str(m, key));
            Mixed(p, "tile_size_m", mans, m => GeoJson.Num(m, "tile_size_m").ToString("R"));
            Mixed(p, "water_level_navd88_m", mans, m => GeoJson.Num(m, "water_level_navd88_m").ToString("R"));
            return p;
        }

        static void Mixed(List<string> p, string field, Dictionary<GeoTileId, IReadOnlyDictionary<string, object>> mans, Func<IReadOnlyDictionary<string, object>, string> get)
        {
            var groups = mans.GroupBy(kv => get(kv.Value) ?? "<none>").ToList();
            if (groups.Count <= 1) return;
            p.Add("mixed " + field + ": " + string.Join(" vs ", groups.Select(g => "'" + g.Key + "' (" + string.Join(",", g.Select(kv => kv.Key.ToString()).OrderBy(s => s, StringComparer.Ordinal)) + ")")));
        }

        /// <summary>Decodes terrain.r16 of one tile through the same reader the importer uses.</summary>
        public static GeoHeightField ReadHeights(GeoTileSet set, GeoTileId id)
        {
            var man = set.Manifest(id);
            var dir = set.Directory(id);
            var terr = LayerOf(man, "terrain");
            var r16 = LayerOf(man, "terrain_r16");
            int n = (int)GeoJson.Num(terr, "samples", 2049);
            double sp = GeoJson.Num(terr, "spacing_m", 1.0);
            return GeoHeightField.FromR16(File.ReadAllBytes(Path.Combine(dir, GeoJson.Str(r16, "file"))), n, sp, GeoJson.Num(r16, "h_min_m"), GeoJson.Num(r16, "h_max_m"));
        }

        /// <summary>Edge rows of one tile's data surface, for the seam check.</summary>
        public static GeoEdgeProfiles ReadEdgeProfiles(GeoTileSet set, GeoTileId id) => GeoEdgeProfiles.From(ReadHeights(set, id));

        /// <summary>All `bid`s of a tile's buildings.geojson (structures and parts), in file order.</summary>
        public static List<string> ReadBuildingIds(GeoTileSet set, GeoTileId id)
        {
            var man = set.Manifest(id);
            var file = GeoJson.Str(LayerOf(man, "buildings"), "file");
            var fc = GeoJson.Obj(GeoJson.Parse(File.ReadAllText(Path.Combine(set.Directory(id), file))));
            var l = new List<string>();
            foreach (var fo in GeoJson.Arr(GeoJson.Get(fc, "features")) ?? new List<object>())
                l.Add(GeoJson.Str(GeoJson.Obj(GeoJson.Get(GeoJson.Obj(fo), "properties")), "bid"));
            return l;
        }

        static IReadOnlyDictionary<string, object> LayerOf(IReadOnlyDictionary<string, object> man, string name)
        {
            foreach (var lo in GeoJson.Arr(GeoJson.Get(man, "layers")))
            {
                var L = GeoJson.Obj(lo);
                if (GeoJson.Str(L, "layer") == name) return L;
            }
            return null;
        }
    }
}
