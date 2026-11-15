// Loads the baked faults_bay.geojson (BayTM, float64) into FaultPieces. Dotnet-only (System.Text.Json); not part of the Unity assembly.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Game.World.Seismic.Tests
{
    public static class RealFaults
    {
        public static string FindFile()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null)
            {
                string p = Path.Combine(dir, "Assets", "StreamingAssets", "Geo", "global", "v1", "faults_bay.geojson");
                if (File.Exists(p)) return p;
                dir = Path.GetDirectoryName(dir);
            }
            throw new FileNotFoundException("faults_bay.geojson not found above " + AppContext.BaseDirectory);
        }

        static string Str(JsonElement p, string n)
        {
            JsonElement v;
            return p.TryGetProperty(n, out v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }

        static double[] Flat(JsonElement line)
        {
            var l = new List<double>();
            foreach (JsonElement c in line.EnumerateArray()) { l.Add(c[0].GetDouble()); l.Add(c[1].GetDouble()); }
            return l.ToArray();
        }

        /// <summary>Production path (T-036c): the global layer through World.Geo's loader (GeoFaultData, baked fault_id) and FaultTraceSet.PiecesFromGeo.</summary>
        public static List<FaultPiece> Load()
        {
            return FaultTraceSet.PiecesFromGeo(Game.World.Geo.GeoFaultsLoader.LoadGlobal(FindFile()), 0, 0);
        }

        /// <summary>Independent reading of the same file with System.Text.Json, kept only to prove the production path loses nothing.</summary>
        public static List<FaultPiece> LoadDirect()
        {
            var list = new List<FaultPiece>();
            using (JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(FindFile())))
            {
                foreach (JsonElement f in doc.RootElement.GetProperty("features").EnumerateArray())
                {
                    JsonElement p = f.GetProperty("properties"), g = f.GetProperty("geometry"), c = g.GetProperty("coordinates");
                    var lines = new List<double[]>();
                    if (g.GetProperty("type").GetString() == "LineString") lines.Add(Flat(c));
                    else foreach (JsonElement l in c.EnumerateArray()) lines.Add(Flat(l));
                    foreach (double[] xz in lines)
                        list.Add(new FaultPiece { BakedFaultId = Str(p, "fault_id"), FaultName = Str(p, "fault_name"), Section = Str(p, "section"), Linetype = Str(p, "linetype"), SlipSense = Str(p, "slip_sense"), Xz = xz });
                }
            }
            return list;
        }
    }
}
