// Loads the global baked fault layer (Assets/StreamingAssets/Geo/global/v1/faults_bay.geojson, BayTM metres) as GeoFaultData records (T-036c).
// Same record type and the same parser (GeoJson, GeoGeom.Lines) the per-tile FaultsLayer uses, with origin 0: the production source of fault traces
// for World.Seismic (FaultTraceSet.PiecesFromGeo(records, 0, 0)), because no baked district tile carries a fault (all are empty by design).
// Engine-free. Verifies the layer's manifest hash when the manifest sits next to the file.
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace Game.World.Geo
{
    public static class GeoFaultsLoader
    {
        public const string ManifestSuffix = ".manifest.json";

        /// <summary>All features of the global fault layer, file order. Throws FormatException when the file is not a FeatureCollection, or its manifest hash differs.</summary>
        public static List<GeoFaultData> LoadGlobal(string faultsGeojsonPath, bool verifyHash = true)
        {
            byte[] bytes = File.ReadAllBytes(faultsGeojsonPath);
            if (verifyHash)
            {
                string mp = Path.ChangeExtension(faultsGeojsonPath, null) + ManifestSuffix;
                if (File.Exists(mp))
                {
                    var man = GeoJson.Obj(GeoJson.Parse(File.ReadAllText(mp)));
                    string want = GeoJson.Str(man, "sha256");
                    using (var sha = SHA256.Create())
                    {
                        string got = ToHex(sha.ComputeHash(bytes));
                        if (want != null && !string.Equals(want, got, StringComparison.OrdinalIgnoreCase))
                            throw new FormatException("faults layer sha256 mismatch: manifest " + want + ", file " + got);
                    }
                }
            }
            var fc = GeoJson.Obj(GeoJson.Parse(System.Text.Encoding.UTF8.GetString(bytes)));
            var feats = GeoJson.Arr(GeoJson.Get(fc, "features")) ?? throw new FormatException("not a FeatureCollection: " + faultsGeojsonPath);
            var list = new List<GeoFaultData>(feats.Count);
            foreach (var fo in feats)
            {
                var f = GeoJson.Obj(fo);
                var p = GeoJson.Obj(GeoJson.Get(f, "properties"));
                list.Add(new GeoFaultData
                {
                    Id = GeoJson.Get(f, "id") as string, FaultId = GeoJson.Str(p, "fault_id"), FaultName = GeoJson.Str(p, "fault_name"),
                    Section = GeoJson.Str(p, "section"), Linetype = GeoJson.Str(p, "linetype"), SlipRate = GeoJson.Str(p, "slip_rate"),
                    SlipSense = GeoJson.Str(p, "slip_sense"), Age = GeoJson.Str(p, "age"), Lines = GeoGeom.Lines(GeoJson.Get(f, "geometry"), 0, 0),
                });
            }
            return list;
        }

        static string ToHex(byte[] b)
        {
            var c = new char[b.Length * 2];
            for (int i = 0; i < b.Length; i++) { c[2 * i] = "0123456789abcdef"[b[i] >> 4]; c[2 * i + 1] = "0123456789abcdef"[b[i] & 15]; }
            return new string(c);
        }
    }
}
