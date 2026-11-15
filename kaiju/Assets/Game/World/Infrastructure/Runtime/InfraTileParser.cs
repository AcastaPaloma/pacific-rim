// Reads the baked `infra@v1` sidecar of one L0 tile (infrastructure.geojson + infrastructure.manifest.json) into InfraRecords.
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Game.World.Geo;

namespace Game.World.Infrastructure
{
    public static class InfraTileParser
    {
        /// <summary>
        /// Parses a tile file. When <paramref name="manifestJson"/> is given the sha256 of the UTF-8 bytes of <paramref name="geojson"/> and the
        /// record count must match it (FormatException otherwise). Records come out in file order (sorted by id by the bake).
        /// </summary>
        public static List<InfraRecord> Parse(string geojson, string manifestJson = null)
        {
            if (geojson == null) throw new ArgumentNullException(nameof(geojson));
            var root = GeoJson.Obj(GeoJson.Parse(geojson));
            if (root == null || GeoJson.Str(root, "spec") != InfrastructureContractVersions.LayerSpec)
                throw new FormatException("not an " + InfrastructureContractVersions.LayerSpec + " file");
            var feats = GeoJson.Arr(GeoJson.Get(root, "features")) ?? throw new FormatException("features missing");
            if (manifestJson != null)
            {
                var m = GeoJson.Obj(GeoJson.Parse(manifestJson));
                string want = GeoJson.Str(m, "sha256");
                if (!string.Equals(want, Sha256Hex(geojson), StringComparison.Ordinal)) throw new FormatException("infrastructure.geojson sha256 differs from its manifest");
                if ((int)GeoJson.Num(m, "count", -1) != feats.Count) throw new FormatException("record count differs from the manifest");
            }
            string tile = GeoJson.Obj(manifestJson == null ? null : GeoJson.Parse(manifestJson)) is var mm ? GeoJson.Str(mm, "tile") ?? "" : "";
            var list = new List<InfraRecord>(feats.Count);
            foreach (var f in feats) list.Add(ParseFeature(GeoJson.Obj(f), tile));
            return list;
        }

        public static string Sha256Hex(string text)
        {
            using (var sha = SHA256.Create())
            {
                var h = sha.ComputeHash(new UTF8Encoding(false).GetBytes(text));
                var sb = new StringBuilder(64);
                foreach (byte b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        static InfraRecord ParseFeature(IReadOnlyDictionary<string, object> f, string tile)
        {
            var p = GeoJson.Obj(GeoJson.Get(f, "properties")) ?? throw new FormatException("feature without properties");
            var kind = (InfraKind)(int)GeoJson.Num(p, "kind_id", 0);
            string osm = GeoJson.Str(p, "osm") ?? throw new FormatException("feature without osm id");
            if (osm.Length < 2 || (osm[0] != 'w' && osm[0] != 'n') || !ulong.TryParse(osm.Substring(1), out ulong sid))
                throw new FormatException("bad osm id " + osm);
            var r = new InfraRecord { Tile = tile };
            r.Id = InfraId.From(kind, osm[0] == 'w' ? InfraSource.OsmWay : InfraSource.OsmNode, sid);
            var g = GeoJson.Obj(GeoJson.Get(f, "geometry")) ?? throw new FormatException("feature without geometry");
            string gt = GeoJson.Str(g, "type");
            var c = GeoJson.Get(g, "coordinates");
            var pts = new List<double>();
            if (gt == "Point") AddPt(pts, c);
            else if (gt == "LineString") foreach (var q in GeoJson.Arr(c)) AddPt(pts, q);
            else if (gt == "Polygon") { foreach (var q in GeoJson.Arr(GeoJson.Arr(c)[0])) AddPt(pts, q); r.IsArea = true; }
            else throw new FormatException("unsupported geometry " + gt);
            r.Points = pts.ToArray();
            r.MinX = r.MinZ = double.MaxValue; r.MaxX = r.MaxZ = double.MinValue;
            for (int i = 0; i < r.Points.Length; i += 2)
            {
                r.MinX = Math.Min(r.MinX, r.Points[i]); r.MaxX = Math.Max(r.MaxX, r.Points[i]);
                r.MinZ = Math.Min(r.MinZ, r.Points[i + 1]); r.MaxZ = Math.Max(r.MaxZ, r.Points[i + 1]);
            }
            InfraGeometry.OwnerPoint(r, out r.X, out r.Z);
            r.LengthM = GeoJson.Num(p, "length_m"); r.AreaM2 = GeoJson.Num(p, "area_m2"); r.WidthM = GeoJson.Num(p, "width_m");
            r.HeightM = GeoJson.Num(p, "height_m"); r.VoltageV = GeoJson.Num(p, "voltage_v"); r.Lanes = GeoJson.Num(p, "lanes");
            r.Layer = (int)GeoJson.Num(p, "layer", 0);
            r.Movable = GeoJson.Bool(p, "movable"); r.Footway = GeoJson.Bool(p, "footway");
            r.Landmark = GeoJson.Str(p, "landmark");
            r.Class = GeoJson.Str(p, "class") ?? GeoJson.Str(p, "crane_type") ?? GeoJson.Str(p, "substation");
            return r;
        }

        static void AddPt(List<double> pts, object o)
        {
            var a = GeoJson.Arr(o);
            if (a == null || a.Count < 2 || !(a[0] is double x) || !(a[1] is double z)) throw new FormatException("bad coordinate");
            pts.Add(x); pts.Add(z);
        }
    }
}
