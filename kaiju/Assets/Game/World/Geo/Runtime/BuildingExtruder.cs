// Footprint triangulation (ear clipping with hole bridging) and watertight flat-faced extrusion.
using System;
using System.Collections.Generic;

namespace Game.World.Geo
{
    public static class Triangulator
    {
        /// <summary>Triangulates one polygon (ring 0 outer, others holes; x,z pairs). Returns CCW (x,z) triangles as point triples.</summary>
        public static List<double[]> Triangulate(List<double[]> rings)
        {
            var outer = Clean(rings[0]);
            if (outer.Count < 3) return new List<double[]>();
            if (Area(outer) < 0) outer.Reverse();
            var holes = new List<List<double[]>>();
            for (int r = 1; r < rings.Count; r++)
            {
                var hl = Clean(rings[r]);
                if (hl.Count < 3) continue;
                if (Area(hl) > 0) hl.Reverse();
                holes.Add(hl);
            }
            holes.Sort((a, b) => MaxX(b).CompareTo(MaxX(a)));
            var poly = outer;
            foreach (var hl in holes) poly = Bridge(poly, hl);
            return EarClip(poly);
        }

        static double MaxX(List<double[]> r) { double m = double.MinValue; foreach (var p in r) m = Math.Max(m, p[0]); return m; }

        public static List<double[]> Clean(double[] flat)
        {
            var l = new List<double[]>();
            for (int i = 0; i + 1 < flat.Length; i += 2)
            {
                var p = new[] { flat[i], flat[i + 1] };
                if (l.Count > 0 && Same(l[l.Count - 1], p)) continue;
                l.Add(p);
            }
            if (l.Count > 1 && Same(l[0], l[l.Count - 1])) l.RemoveAt(l.Count - 1);
            bool changed = true;
            while (changed && l.Count >= 3)
            {
                changed = false;
                for (int i = 0; i < l.Count && l.Count >= 3; i++)
                {
                    var a = l[(i + l.Count - 1) % l.Count]; var b = l[i]; var c = l[(i + 1) % l.Count];
                    if (Math.Abs(Cross(a, b, c)) < 1e-9) { l.RemoveAt(i); changed = true; i--; }
                }
            }
            return l;
        }

        static bool Same(double[] a, double[] b) => Math.Abs(a[0] - b[0]) < 1e-7 && Math.Abs(a[1] - b[1]) < 1e-7;
        public static double Cross(double[] a, double[] b, double[] c) => (b[0] - a[0]) * (c[1] - a[1]) - (c[0] - a[0]) * (b[1] - a[1]);

        public static double Area(List<double[]> r)
        {
            double s = 0;
            for (int i = 0; i < r.Count; i++) { var a = r[i]; var b = r[(i + 1) % r.Count]; s += a[0] * b[1] - b[0] * a[1]; }
            return s / 2;
        }

        static bool SegIntersect(double[] p, double[] q, double[] a, double[] b)
        {
            double d1 = Cross(a, b, p), d2 = Cross(a, b, q), d3 = Cross(p, q, a), d4 = Cross(p, q, b);
            return ((d1 > 1e-12 && d2 < -1e-12) || (d1 < -1e-12 && d2 > 1e-12)) && ((d3 > 1e-12 && d4 < -1e-12) || (d3 < -1e-12 && d4 > 1e-12));
        }

        static List<double[]> Bridge(List<double[]> outer, List<double[]> hole)
        {
            int hi = 0;
            for (int i = 1; i < hole.Count; i++) if (hole[i][0] > hole[hi][0]) hi = i;
            var hp = hole[hi];
            int best = -1; double bd = double.MaxValue;
            for (int i = 0; i < outer.Count; i++)
            {
                double dx = outer[i][0] - hp[0], dz = outer[i][1] - hp[1], d = dx * dx + dz * dz;
                if (d >= bd) continue;
                bool ok = true;
                for (int j = 0; j < outer.Count && ok; j++)
                    if (j != i && (j + 1) % outer.Count != i && SegIntersect(hp, outer[i], outer[j], outer[(j + 1) % outer.Count])) ok = false;
                for (int j = 0; j < hole.Count && ok; j++)
                    if (j != hi && (j + 1) % hole.Count != hi && SegIntersect(hp, outer[i], hole[j], hole[(j + 1) % hole.Count])) ok = false;
                if (ok) { best = i; bd = d; }
            }
            if (best < 0) best = 0;
            var r = new List<double[]>();
            for (int i = 0; i <= best; i++) r.Add(outer[i]);
            for (int k = 0; k <= hole.Count; k++) r.Add(hole[(hi + k) % hole.Count]);
            r.Add(outer[best]);
            for (int i = best + 1; i < outer.Count; i++) r.Add(outer[i]);
            return r;
        }

        static bool InTri(double[] p, double[] a, double[] b, double[] c) =>
            Cross(a, b, p) >= -1e-12 && Cross(b, c, p) >= -1e-12 && Cross(c, a, p) >= -1e-12;

        static List<double[]> EarClip(List<double[]> poly)
        {
            var res = new List<double[]>();
            var v = new List<double[]>(poly);
            int guard = 0;
            while (v.Count > 3 && guard++ < 100000)
            {
                bool clipped = false;
                for (int i = 0; i < v.Count; i++)
                {
                    var a = v[(i + v.Count - 1) % v.Count]; var b = v[i]; var c = v[(i + 1) % v.Count];
                    if (Cross(a, b, c) <= 1e-12) continue;
                    bool ear = true;
                    for (int j = 0; j < v.Count && ear; j++)
                    {
                        var p = v[j];
                        if (ReferenceEquals(p, a) || ReferenceEquals(p, b) || ReferenceEquals(p, c)) continue;
                        if (Same(p, a) || Same(p, b) || Same(p, c)) continue;
                        if (InTri(p, a, b, c)) ear = false;
                    }
                    if (!ear) continue;
                    res.Add(new[] { a[0], a[1], b[0], b[1], c[0], c[1] });
                    v.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped)
                {   // degenerate remainder (self-touching input): fan it rather than loop forever
                    for (int i = 1; i + 1 < v.Count; i++)
                        if (Math.Abs(Cross(v[0], v[i], v[i + 1])) > 1e-12)
                            res.Add(new[] { v[0][0], v[0][1], v[i][0], v[i][1], v[i + 1][0], v[i + 1][1] });
                    v.Clear();
                }
            }
            if (v.Count == 3 && Math.Abs(Cross(v[0], v[1], v[2])) > 1e-12)
                res.Add(new[] { v[0][0], v[0][1], v[1][0], v[1][1], v[2][0], v[2][1] });
            for (int k = 0; k < res.Count; k++)
            {
                var t = res[k];
                if ((t[2] - t[0]) * (t[5] - t[1]) - (t[4] - t[0]) * (t[3] - t[1]) < 0)
                    res[k] = new[] { t[0], t[1], t[4], t[5], t[2], t[3] };
            }
            return res;
        }
    }

    /// <summary>Extrudes a footprint into a closed, flat-faced, consistently wound prism (walls, roof, floor).</summary>
    public static class BuildingExtruder
    {
        public static GeoMeshData Extrude(GeoBuildingData b, double bottomY, double topY, double roofHeightM, string name)
        {
            var pos = new List<float>(); var nrm = new List<float>(); var idx = new List<int>();
            bool apex = roofHeightM > 0 && roofHeightM < topY - bottomY && b.Polygons.Count == 1 && b.Polygons[0].Count == 1 &&
                        (b.RoofShape == "pyramidal" || b.RoofShape == "spire" || b.RoofShape == "cone");
            double eaveY = apex ? topY - roofHeightM : topY;
            foreach (var poly in b.Polygons)
            {
                for (int r = 0; r < poly.Count; r++)
                {
                    var ring = Triangulator.Clean(poly[r]);
                    if (ring.Count < 3) continue;
                    double ar = Triangulator.Area(ring);
                    // outer rings CCW, holes CW (x,z): wall outward normal is then to the right of travel
                    if ((r == 0 && ar < 0) || (r > 0 && ar > 0)) ring.Reverse();
                    for (int i = 0; i < ring.Count; i++)
                    {
                        var p = ring[i]; var q = ring[(i + 1) % ring.Count];
                        double dx = q[0] - p[0], dz = q[1] - p[1], l = Math.Sqrt(dx * dx + dz * dz);
                        if (l < 1e-6) continue;
                        float nx = (float)(dz / l), nz = (float)(-dx / l);
                        Quad(pos, nrm, idx, p[0], p[1], q[0], q[1], bottomY, eaveY, nx, nz);
                    }
                }
                var tris = Triangulator.Triangulate(poly);
                foreach (var t in tris)
                {
                    if (!apex) Tri(pos, nrm, idx, t, eaveY, 0, 1, 0, true);
                    Tri(pos, nrm, idx, t, bottomY, 0, -1, 0, false);
                }
                if (apex)
                {
                    var ring = Triangulator.Clean(poly[0]);
                    if (Triangulator.Area(ring) < 0) ring.Reverse();
                    double cx = b.AnchorLocalX, cz = b.AnchorLocalZ;
                    for (int i = 0; i < ring.Count; i++)
                    {
                        var p = ring[i]; var q = ring[(i + 1) % ring.Count];
                        AddFace(pos, nrm, idx, new[] { p[0], eaveY, p[1] }, new[] { cx, topY, cz }, new[] { q[0], eaveY, q[1] });
                    }
                }
            }
            return new GeoMeshData { Layer = "buildings", Material = "building", Name = name, Positions = pos.ToArray(), Normals = nrm.ToArray(), Indices = idx.ToArray(), ChunkX = b.ChunkX, ChunkZ = b.ChunkZ };
        }

        static void Quad(List<float> pos, List<float> nrm, List<int> idx, double x0, double z0, double x1, double z1, double y0, double y1, float nx, float nz)
        {
            int s = pos.Count / 3;
            void V(double x, double y, double z) { pos.Add((float)x); pos.Add((float)y); pos.Add((float)z); nrm.Add(nx); nrm.Add(0); nrm.Add(nz); }
            V(x0, y0, z0); V(x1, y0, z1); V(x1, y1, z1); V(x0, y1, z0);
            // clockwise seen from outside (outward normal = right of travel p->q)
            idx.Add(s); idx.Add(s + 3); idx.Add(s + 2);
            idx.Add(s); idx.Add(s + 2); idx.Add(s + 1);
        }

        static void Tri(List<float> pos, List<float> nrm, List<int> idx, double[] t, double y, float nx, float ny, float nz, bool up)
        {
            // t is CCW in (x,z); seen from +y CCW-in-(x,z) is counter-clockwise on screen, so reverse for up faces
            int s = pos.Count / 3;
            for (int k = 0; k < 3; k++) { pos.Add((float)t[2 * k]); pos.Add((float)y); pos.Add((float)t[2 * k + 1]); nrm.Add(nx); nrm.Add(ny); nrm.Add(nz); }
            if (up) { idx.Add(s); idx.Add(s + 2); idx.Add(s + 1); }
            else { idx.Add(s); idx.Add(s + 1); idx.Add(s + 2); }
        }

        static void AddFace(List<float> pos, List<float> nrm, List<int> idx, double[] a, double[] b, double[] c)
        {
            // a, c on the eave ring (CCW), b apex: choose order so the normal points outward/up
            double ux = b[0] - a[0], uy = b[1] - a[1], uz = b[2] - a[2], vx = c[0] - a[0], vy = c[1] - a[1], vz = c[2] - a[2];
            double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            if (ny < 0) { var t = b; b = c; c = t; nx = -nx; ny = -ny; nz = -nz; }
            double l = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            int s = pos.Count / 3;
            foreach (var p in new[] { a, b, c }) { pos.Add((float)p[0]); pos.Add((float)p[1]); pos.Add((float)p[2]); nrm.Add((float)(nx / l)); nrm.Add((float)(ny / l)); nrm.Add((float)(nz / l)); }
            idx.Add(s); idx.Add(s + 1); idx.Add(s + 2);
        }
    }
}
