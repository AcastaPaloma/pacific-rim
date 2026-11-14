// geo_tile@v1 manifest validation (R-004 §3.3, M-005.8). Same rules as Pipelines/geo/kaiju_geo/manifest.py.
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Game.World.Geo
{
    public static class GeoManifestValidator
    {
        public static readonly string[] TopFields = { "spec", "tile", "level", "i", "j", "crs", "vertical", "bounds_baytm", "bounds_wgs84", "origin_baytm_m", "water_level_navd88_m", "build", "layers" };
        public static readonly string[] BuildFields = { "script", "git", "built_utc", "inputs_sha256" };
        public static readonly string[] LayerFields = { "layer", "file", "sha256", "sources" };
        public static readonly string[] SourceFields = { "product", "item", "url", "licence", "attribution", "fetched_utc", "source_sha256" };
        public static readonly string[] RequiredLayers = { "terrain", "terrain_r16", "water", "buildings", "roads", "faults" };
        public static readonly string[] Licences = { "US-Gov-PD", "ODbL-1.0", "PDDL-1.0", "CC0-1.0", "CC-BY-4.0" };

        static bool Empty(object v) =>
            v == null || (v is string s && s.Trim().Length == 0) || (v is List<object> l && l.Count == 0) ||
            (v is Dictionary<string, object> d && d.Count == 0);

        /// <summary>Returns problems; empty means complete. With readFile, also verifies output sha256.</summary>
        public static List<string> Validate(IReadOnlyDictionary<string, object> man, Func<string, byte[]> readFile = null)
        {
            var errs = new List<string>();
            if (man == null) { errs.Add("manifest is not a JSON object"); return errs; }
            foreach (var k in TopFields)
                if (!man.ContainsKey(k) || Empty(man[k])) errs.Add("missing top-level field '" + k + "'");
            if (GeoJson.Str(man, "spec") != GeoContractVersions.TileSpec) errs.Add("spec is not " + GeoContractVersions.TileSpec);
            var b = GeoJson.Obj(GeoJson.Get(man, "build"));
            foreach (var k in BuildFields)
                if (Empty(GeoJson.Get(b, k))) errs.Add("build." + k + " missing");
            var names = new HashSet<string>(StringComparer.Ordinal);
            var layers = GeoJson.Arr(GeoJson.Get(man, "layers")) ?? new List<object>();
            for (int i = 0; i < layers.Count; i++)
            {
                var L = GeoJson.Obj(layers[i]);
                var nm = GeoJson.Str(L, "layer") ?? ("#" + i);
                names.Add(nm);
                foreach (var k in LayerFields)
                    if (L == null || !L.ContainsKey(k) || Empty(L[k])) errs.Add("layer " + nm + ": missing '" + k + "'");
                var srcs = GeoJson.Arr(GeoJson.Get(L, "sources")) ?? new List<object>();
                for (int j = 0; j < srcs.Count; j++)
                {
                    var s = GeoJson.Obj(srcs[j]);
                    foreach (var k in SourceFields)
                        if (Empty(GeoJson.Get(s, k))) errs.Add("layer " + nm + " source " + j + ": missing '" + k + "'");
                    var lic = GeoJson.Str(s, "licence");
                    if (lic != null && Array.IndexOf(Licences, lic) < 0) errs.Add("layer " + nm + " source " + j + ": licence '" + lic + "' not accepted");
                }
                var file = GeoJson.Str(L, "file");
                var sha = GeoJson.Str(L, "sha256");
                if (readFile != null && file != null && sha != null)
                {
                    byte[] data = null;
                    try { data = readFile(file); } catch (IOException) { }
                    if (data == null) errs.Add("layer " + nm + ": file " + file + " missing");
                    else if (Sha256Hex(data) != sha) errs.Add("layer " + nm + ": sha256 mismatch for " + file);
                }
            }
            foreach (var r in RequiredLayers)
                if (!names.Contains(r)) errs.Add("required layer '" + r + "' absent");
            return errs;
        }

        public static string Sha256Hex(byte[] data)
        {
            using (var h = SHA256.Create())
            {
                var d = h.ComputeHash(data);
                var sb = new StringBuilder(64);
                foreach (var x in d) sb.Append(x.ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>Every distinct attribution string (and notice) in the manifest, ordinal order.</summary>
        public static SortedSet<string> Attributions(IReadOnlyDictionary<string, object> man)
        {
            var o = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var lo in GeoJson.Arr(GeoJson.Get(man, "layers")) ?? new List<object>())
                foreach (var so in GeoJson.Arr(GeoJson.Get(GeoJson.Obj(lo), "sources")) ?? new List<object>())
                {
                    var s = GeoJson.Obj(so);
                    var a = GeoJson.Str(s, "attribution"); if (!string.IsNullOrEmpty(a)) o.Add(a);
                    var n = GeoJson.Str(s, "attribution_notice"); if (!string.IsNullOrEmpty(n)) o.Add(n);
                }
            return o;
        }
    }
}
