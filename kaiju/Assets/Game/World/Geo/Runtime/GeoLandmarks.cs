// Landmarks layer (R-020 §3, static only): tower legs, deck and main cables of the Golden Gate Bridge as colliders + simple render meshes. Engine-free.
using System;
using System.Collections.Generic;

namespace Game.World.Geo
{
    public sealed class LandmarksLayer : IGeoLayer
    {
        /// <summary>Linear landmarks are cut into equal segments of at most this horizontal length.</summary>
        public const double SegmentM = 40.0;
        /// <summary>Deck box thickness (vertical). Assumed, unsourced: a plausible stiffening-truss depth; the box top follows the baked road surface.</summary>
        public const double DeckDepthM = 3.0;

        public string Name => "landmarks";

        public GeoLayerOutput Build(IGeoTileContext ctx)
        {
            var o = new GeoLayerOutput { Layer = Name };
            var layer = ctx.ManifestLayer("landmarks");
            if (layer == null) return o;
            var fc = GeoJson.Obj(GeoJson.Parse(ctx.ReadText(GeoJson.Str(layer, "file"))));
            foreach (var fo in GeoJson.Arr(GeoJson.Get(fc, "features")) ?? new List<object>())
            {
                var f = GeoJson.Obj(fo);
                var p = GeoJson.Obj(GeoJson.Get(f, "properties"));
                var m = new GeoLandmarkData
                {
                    Lid = GeoJson.Str(p, "lid"), Bid = GeoJson.Str(p, "bid"), Kind = GeoJson.Str(p, "kind"),
                    Confidence = GeoJson.Str(p, "confidence"), HeightSource = GeoJson.Str(p, "height_source"),
                    PartIdx = (int)GeoJson.Num(p, "part_idx", 0), Piece = (int)GeoJson.Num(p, "piece", 0),
                    BaseNavd88M = GeoJson.Num(p, "base_navd88_m", 0), TopNavd88M = GeoJson.Num(p, "top_navd88_m", 0),
                    HeightM = GeoJson.Num(p, "height_m", 0), WidthM = GeoJson.Num(p, "width_m", 0), DiameterM = GeoJson.Num(p, "diameter_m", 0),
                };
                var geom = GeoJson.Obj(GeoJson.Get(f, "geometry"));
                if (m.Kind == "bridge_tower") Tower(ctx, m, geom);
                else if (m.Kind == "bridge_deck" || m.Kind == "bridge_cable") Linear(ctx, m, geom, p);
                else continue;   // unknown kinds are ignored, not an error
                o.Landmarks.Add(m);
            }
            o.Landmarks.Sort((a, b) => string.CompareOrdinal(a.Lid, b.Lid));
            int cols = 0, tris = 0;
            foreach (var m in o.Landmarks) { cols += m.Colliders.Count; tris += m.Mesh?.TriangleCount ?? 0; }
            o.Stats["landmarks.count"] = o.Landmarks.Count;
            o.Stats["landmarks.colliders"] = cols;
            o.Stats["landmarks.triangles"] = tris;
            TerrainLayer.Attr(ctx, o, "landmarks");
            return o;
        }

        // ---- towers: one Polygon = one leg; oriented box (longest edge direction) from base to top ----
        static void Tower(IGeoTileContext ctx, GeoLandmarkData m, IReadOnlyDictionary<string, object> geom)
        {
            var polys = GeoGeom.Polygons(geom, ctx.OriginX, ctx.OriginZ);
            if (polys.Count == 0) return;
            m.Footprint = polys[0];
            GeoGeom.Centroid(polys, out m.AnchorLocalX, out m.AnchorLocalZ, out _);
            var ring = Triangulator.Clean(m.Footprint[0]);
            if (ring.Count < 3) return;
            double best = -1, yaw = 0;
            for (int i = 0; i < ring.Count; i++)
            {
                var a = ring[i]; var b = ring[(i + 1) % ring.Count];
                double dx = b[0] - a[0], dz = b[1] - a[1], l = dx * dx + dz * dz;
                if (l > best) { best = l; yaw = Math.Atan2(dz, dx); }
            }
            double ux = Math.Cos(yaw), uz = Math.Sin(yaw), vx = -uz, vz = ux;
            double u0 = double.MaxValue, u1 = double.MinValue, v0 = double.MaxValue, v1 = double.MinValue;
            foreach (var q in ring)
            {
                double pu = q[0] * ux + q[1] * uz, pv = q[0] * vx + q[1] * vz;
                u0 = Math.Min(u0, pu); u1 = Math.Max(u1, pu); v0 = Math.Min(v0, pv); v1 = Math.Max(v1, pv);
            }
            double cu = (u0 + u1) / 2, cv = (v0 + v1) / 2;
            double cx = cu * ux + cv * vx, cz = cu * uz + cv * vz;
            m.Colliders.Add(new GeoColliderData
            {
                Shape = GeoColliderShape.Box, P0X = cx, P0Y = m.BaseNavd88M, P0Z = cz, P1X = cx, P1Y = m.TopNavd88M, P1Z = cz,
                Width = u1 - u0, Depth = v1 - v0, YawRad = yaw,
            });
            var src = new GeoBuildingData { Polygons = new List<List<double[]>> { m.Footprint }, AnchorLocalX = m.AnchorLocalX, AnchorLocalZ = m.AnchorLocalZ };
            if (m.TopNavd88M - m.BaseNavd88M > 0.05)
            {
                m.Mesh = BuildingExtruder.Extrude(src, m.BaseNavd88M, m.TopNavd88M, 0, m.Lid);
                Tag(m.Mesh, m.Lid);
            }
        }

        // ---- deck and cables: 40 m resampling, end points are exactly the first / last original vertex ----
        static void Linear(IGeoTileContext ctx, GeoLandmarkData m, IReadOnlyDictionary<string, object> geom, IReadOnlyDictionary<string, object> p)
        {
            var lines = GeoGeom.Lines(geom, ctx.OriginX, ctx.OriginZ);
            if (lines.Count == 0 || lines[0].Length < 4) return;
            m.Line = lines[0];
            var ys = GeoJson.Arr(GeoJson.Get(p, "y_navd88_m"));
            int n = m.Line.Length / 2;
            m.Y = new double[n];
            for (int i = 0; i < n; i++) m.Y[i] = ys != null && i < ys.Count && ys[i] is double d ? d : m.BaseNavd88M;
            var pts = Resample(m.Line, m.Y, SegmentM);   // x,y,z per point
            int segs = pts.Length / 3 - 1;
            bool deck = m.Kind == "bridge_deck";
            var mesh = new MeshBuilder();
            for (int s = 0; s < segs; s++)
            {
                double x0 = pts[3 * s], y0 = pts[3 * s + 1], z0 = pts[3 * s + 2], x1 = pts[3 * s + 3], y1 = pts[3 * s + 4], z1 = pts[3 * s + 5];
                double yaw = Math.Atan2(x1 - x0, -(z1 - z0));   // side vector (cos, sin) = (-dz, dx)/len, perpendicular to the axis
                var c = new GeoColliderData { YawRad = yaw };
                if (deck)
                {
                    // the box top follows the baked road surface y: axis centre = y - depth/2
                    c.Shape = GeoColliderShape.Box; c.Width = m.WidthM; c.Depth = DeckDepthM;
                    c.P0X = x0; c.P0Y = y0 - DeckDepthM / 2; c.P0Z = z0; c.P1X = x1; c.P1Y = y1 - DeckDepthM / 2; c.P1Z = z1;
                    mesh.Prism(c.P0X, c.P0Y, c.P0Z, c.P1X, c.P1Y, c.P1Z, c.Width, c.Depth);
                }
                else
                {
                    c.Shape = GeoColliderShape.Capsule; c.Radius = m.DiameterM / 2;
                    c.P0X = x0; c.P0Y = y0; c.P0Z = z0; c.P1X = x1; c.P1Y = y1; c.P1Z = z1;
                    mesh.Prism(x0, y0, z0, x1, y1, z1, m.DiameterM, m.DiameterM);   // 4-sided tube
                }
                m.Colliders.Add(c);
            }
            // anchor: polyline midpoint (by horizontal arc length)
            var mid = Resample(m.Line, m.Y, 0, 0.5);
            m.AnchorLocalX = mid[0]; m.AnchorLocalZ = mid[2];
            m.Mesh = mesh.ToMesh(m.Lid);
            Tag(m.Mesh, m.Lid);
        }

        static void Tag(GeoMeshData mesh, string lid)
        {
            mesh.Layer = "landmarks";
            mesh.Material = "building";   // reuses the building material key (the Unity adapter resolves terrain/building/water/fault)
            mesh.Name = lid;
        }

        /// <summary>Equal-length horizontal resampling of a polyline (x,z pairs + y): N = ceil(L / maxSegmentM) segments, points x,y,z.
        /// First and last point are the original vertices bit for bit. With maxSegmentM = 0 returns the single point at arc fraction frac.</summary>
        public static double[] Resample(double[] line, double[] y, double maxSegmentM, double frac = 0)
        {
            int n = line.Length / 2;
            var cum = new double[n];
            for (int i = 1; i < n; i++)
            {
                double dx = line[2 * i] - line[2 * i - 2], dz = line[2 * i + 1] - line[2 * i - 1];
                cum[i] = cum[i - 1] + Math.Sqrt(dx * dx + dz * dz);
            }
            double L = cum[n - 1];
            int N = maxSegmentM > 0 ? Math.Max(1, (int)Math.Ceiling(L / maxSegmentM)) : 1;
            var res = new double[maxSegmentM > 0 ? (N + 1) * 3 : 3];
            int seg = 0;
            for (int k = 0; k < res.Length / 3; k++)
            {
                double t = maxSegmentM > 0 ? L * k / N : L * frac;
                double px, py, pz;
                if (maxSegmentM > 0 && k == 0) { px = line[0]; pz = line[1]; py = y[0]; }
                else if (maxSegmentM > 0 && k == N) { px = line[2 * (n - 1)]; pz = line[2 * (n - 1) + 1]; py = y[n - 1]; }
                else
                {
                    seg = 0;
                    while (seg < n - 2 && cum[seg + 1] < t) seg++;
                    double sl = cum[seg + 1] - cum[seg], a = sl > 0 ? (t - cum[seg]) / sl : 0;
                    px = line[2 * seg] + (line[2 * seg + 2] - line[2 * seg]) * a;
                    pz = line[2 * seg + 1] + (line[2 * seg + 3] - line[2 * seg + 1]) * a;
                    py = y[seg] + (y[seg + 1] - y[seg]) * a;
                }
                res[3 * k] = px; res[3 * k + 1] = py; res[3 * k + 2] = pz;
            }
            return res;
        }

        /// <summary>Collects oriented prisms (axis P0-P1, horizontal width, perpendicular depth) as flat-shaded quads, clockwise outward faces.</summary>
        sealed class MeshBuilder
        {
            readonly List<float> _pos = new List<float>(), _nrm = new List<float>();
            readonly List<int> _idx = new List<int>();

            public void Prism(double x0, double y0, double z0, double x1, double y1, double z1, double width, double depth)
            {
                double ax = x1 - x0, ay = y1 - y0, az = z1 - z0, al = Math.Sqrt(ax * ax + ay * ay + az * az);
                if (al < 1e-9) return;
                ax /= al; ay /= al; az /= al;
                double hl = Math.Sqrt(ax * ax + az * az);
                double sx = hl > 1e-9 ? -az / hl : 1, sz = hl > 1e-9 ? ax / hl : 0;       // horizontal side vector
                // up = a x s oriented to +y
                double ux = ay * sz, uy = az * sx - ax * sz, uz = -ay * sx;
                if (uy < 0) { ux = -ux; uy = -uy; uz = -uz; }
                double ul = Math.Sqrt(ux * ux + uy * uy + uz * uz); ux /= ul; uy /= ul; uz /= ul;
                double hw = width / 2, hd = depth / 2;
                double[] C(int e, int sw, int up) => new[]
                {
                    (e == 0 ? x0 : x1) + sx * hw * sw + ux * hd * up,
                    (e == 0 ? y0 : y1) + uy * hd * up,
                    (e == 0 ? z0 : z1) + sz * hw * sw + uz * hd * up,
                };
                Face(new[] { C(0, -1, -1), C(1, -1, -1), C(1, 1, -1), C(0, 1, -1) }, -ux, -uy, -uz);   // bottom
                Face(new[] { C(0, -1, 1), C(0, 1, 1), C(1, 1, 1), C(1, -1, 1) }, ux, uy, uz);          // top
                Face(new[] { C(0, -1, -1), C(0, -1, 1), C(1, -1, 1), C(1, -1, -1) }, -sx, 0, -sz);     // -side
                Face(new[] { C(0, 1, -1), C(1, 1, -1), C(1, 1, 1), C(0, 1, 1) }, sx, 0, sz);           // +side
                Face(new[] { C(0, -1, -1), C(0, 1, -1), C(0, 1, 1), C(0, -1, 1) }, -ax, -ay, -az);     // start cap
                Face(new[] { C(1, -1, -1), C(1, -1, 1), C(1, 1, 1), C(1, 1, -1) }, ax, ay, az);        // end cap
            }

            void Face(double[][] q, double nx, double ny, double nz)
            {
                // seen from outside the triangle (a,b,c) has (b-a)x(c-a) along the outward normal (BuildingExtruder convention)
                var a = q[0]; var b = q[1]; var c = q[2];
                double ex = b[0] - a[0], ey = b[1] - a[1], ez = b[2] - a[2], fx = c[0] - a[0], fy = c[1] - a[1], fz = c[2] - a[2];
                double cx = ey * fz - ez * fy, cy = ez * fx - ex * fz, cz = ex * fy - ey * fx;
                if (cx * nx + cy * ny + cz * nz < 0) { var t = q[1]; q[1] = q[3]; q[3] = t; }
                int s = _pos.Count / 3;
                foreach (var v in q) { _pos.Add((float)v[0]); _pos.Add((float)v[1]); _pos.Add((float)v[2]); _nrm.Add((float)nx); _nrm.Add((float)ny); _nrm.Add((float)nz); }
                _idx.Add(s); _idx.Add(s + 1); _idx.Add(s + 2);
                _idx.Add(s); _idx.Add(s + 2); _idx.Add(s + 3);
            }

            public GeoMeshData ToMesh(string name) => new GeoMeshData { Layer = "landmarks", Material = "building", Name = name, Positions = _pos.ToArray(), Normals = _nrm.ToArray(), Indices = _idx.ToArray() };
        }
    }
}
