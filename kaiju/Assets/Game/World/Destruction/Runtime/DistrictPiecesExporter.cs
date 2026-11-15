// Writer of `district_pieces@v1` (T-036c): the clipped pieces of a multi-tile district build, input of
// `Pipelines/geo/m005_check.py district --pieces`. Schema (m005_check.py v12_pieces):
//   {"format":"district_pieces","schema":1,"pieces":[{"bid","tile","area_m2","height_m","base_navd88_m"}, ...]}
// One piece per (building, district tile square) where the building's full baked footprint (all polygons, holes subtracted) overlaps the square;
// height and base are the building's own (pieces carry the full footprint's height/base). Pure, engine-free, deterministic.
// Not DamageModel code: it lives in Destruction because Destruction is the consumer that needs per-tile pieces of cross-tile buildings.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Game.World.Geo;

namespace Game.World.Destruction
{
    /// <summary>The buildings one tile owns (tile-local footprints, as <c>BuildingsLayer</c> produces them).</summary>
    public sealed class DistrictTileBuildings
    {
        public GeoTileId Tile;
        public IReadOnlyList<GeoBuildingData> Buildings;
    }

    public struct DistrictPiece
    {
        public string Bid;
        public GeoTileId Tile;
        public double AreaM2, HeightM, BaseNavd88M;
    }

    public static class DistrictPiecesExporter
    {
        /// <summary>Pieces of every building of every listed tile against the squares of all listed tiles, ordered by (owner tile order, building order, square j then i).</summary>
        public static List<DistrictPiece> Build(IReadOnlyList<DistrictTileBuildings> tiles)
        {
            if (tiles == null || tiles.Count == 0) throw new ArgumentException("An empty district proves nothing", nameof(tiles));
            var squares = new List<GeoTileId>();
            foreach (var t in tiles) squares.Add(t.Tile);
            squares.Sort((a, b) => a.J != b.J ? a.J.CompareTo(b.J) : a.I.CompareTo(b.I));
            var pieces = new List<DistrictPiece>();
            foreach (var owner in tiles)
            {
                foreach (GeoBuildingData b in owner.Buildings)
                {
                    if (b.Polygons == null || b.Polygons.Count == 0) continue;
                    BBox(b, owner.Tile, out double minX, out double minZ, out double maxX, out double maxZ);
                    foreach (GeoTileId sq in squares)
                    {
                        double x0 = sq.OriginX, z0 = sq.OriginZ, x1 = x0 + sq.SizeM, z1 = z0 + sq.SizeM;
                        if (maxX <= x0 || minX >= x1 || maxZ <= z0 || minZ >= z1) continue;
                        double area = ClippedAreaM2(b.Polygons, owner.Tile.OriginX, owner.Tile.OriginZ, x0, z0, x1, z1);
                        if (area <= 0) continue;
                        pieces.Add(new DistrictPiece { Bid = b.Bid, Tile = sq, AreaM2 = area, HeightM = b.HeightM, BaseNavd88M = b.BaseNavd88M });
                    }
                }
            }
            return pieces;
        }

        static void BBox(GeoBuildingData b, GeoTileId owner, out double minX, out double minZ, out double maxX, out double maxZ)
        {
            minX = minZ = double.MaxValue; maxX = maxZ = double.MinValue;
            foreach (var poly in b.Polygons)
                foreach (double[] ring in poly)
                    for (int i = 0; i + 1 < ring.Length; i += 2)
                    {
                        double x = ring[i] + owner.OriginX, z = ring[i + 1] + owner.OriginZ;
                        if (x < minX) minX = x; if (x > maxX) maxX = x; if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
                    }
        }

        /// <summary>
        /// Area of the footprint (Polygons[p][ring]: ring 0 outer, the rest holes; tile-local metres, origin added in float64) inside the square
        /// [x0,x1] x [z0,z1] (BayTM). Each ring is clipped by Sutherland-Hodgman (exact for area, also for concave rings), holes are subtracted.
        /// </summary>
        public static double ClippedAreaM2(List<List<double[]>> polygons, double originX, double originZ, double x0, double z0, double x1, double z1)
        {
            double total = 0;
            foreach (var poly in polygons)
                for (int r = 0; r < poly.Count; r++)
                {
                    double a = Math.Abs(ClippedRingArea(poly[r], originX, originZ, x0, z0, x1, z1));
                    total += r == 0 ? a : -a;
                }
            return total < 0 ? 0 : total;
        }

        static double ClippedRingArea(double[] ring, double ox, double oz, double x0, double z0, double x1, double z1)
        {
            var pts = new List<double>(ring.Length);
            for (int i = 0; i + 1 < ring.Length; i += 2) { pts.Add(ring[i] + ox); pts.Add(ring[i + 1] + oz); }
            // drop a closing vertex equal to the first
            int n = pts.Count / 2;
            if (n > 1 && pts[0] == pts[2 * (n - 1)] && pts[1] == pts[2 * (n - 1) + 1]) { pts.RemoveRange(2 * (n - 1), 2); }
            for (int side = 0; side < 4 && pts.Count >= 6; side++) pts = ClipSide(pts, side, x0, z0, x1, z1);
            if (pts.Count < 6) return 0;
            double s = 0; int m = pts.Count / 2;
            for (int i = 0; i < m; i++)
            {
                int j = (i + 1) % m;
                s += pts[2 * i] * pts[2 * j + 1] - pts[2 * j] * pts[2 * i + 1];
            }
            return 0.5 * s;
        }

        static double Signed(int side, double x, double z, double x0, double z0, double x1, double z1)
        {
            switch (side)
            {
                case 0: return x - x0;
                case 1: return x1 - x;
                case 2: return z - z0;
                default: return z1 - z;
            }
        }

        static List<double> ClipSide(List<double> pts, int side, double x0, double z0, double x1, double z1)
        {
            var o = new List<double>(pts.Count + 8);
            int n = pts.Count / 2;
            for (int i = 0; i < n; i++)
            {
                double ax = pts[2 * i], az = pts[2 * i + 1];
                int k = (i + 1) % n;
                double bx = pts[2 * k], bz = pts[2 * k + 1];
                double da = Signed(side, ax, az, x0, z0, x1, z1), db = Signed(side, bx, bz, x0, z0, x1, z1);
                bool ina = da >= 0, inb = db >= 0;
                if (ina) { o.Add(ax); o.Add(az); }
                if (ina != inb)
                {
                    double t = da / (da - db);
                    o.Add(ax + t * (bx - ax)); o.Add(az + t * (bz - az));
                }
            }
            return o;
        }

        static string Num(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) throw new ArgumentException("Not finite: " + v);
            return v.ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary>The `district_pieces@v1` JSON of <paramref name="pieces"/> (one piece per line for diff-ability).</summary>
        public static string ToJson(IReadOnlyList<DistrictPiece> pieces)
        {
            if (pieces == null || pieces.Count == 0) throw new ArgumentException("An empty pieces file is rejected by the checker", nameof(pieces));
            var sb = new StringBuilder(pieces.Count * 96 + 64);
            sb.Append("{\"format\":\"district_pieces\",\"schema\":1,\"pieces\":[");
            for (int i = 0; i < pieces.Count; i++)
            {
                DistrictPiece p = pieces[i];
                if (string.IsNullOrEmpty(p.Bid)) throw new ArgumentException("Piece without a bid at " + i);
                sb.Append(i > 0 ? ",\n" : "\n").Append("{\"bid\":\"").Append(p.Bid.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\",\"tile\":\"").Append(p.Tile.ToString())
                  .Append("\",\"area_m2\":").Append(Num(p.AreaM2)).Append(",\"height_m\":").Append(Num(p.HeightM))
                  .Append(",\"base_navd88_m\":").Append(Num(p.BaseNavd88M)).Append('}');
            }
            return sb.Append("\n]}\n").ToString();
        }
    }
}
