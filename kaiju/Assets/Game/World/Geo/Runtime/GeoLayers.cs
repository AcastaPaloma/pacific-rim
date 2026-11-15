// IGeoLayer importers (MODULARITY §4.6): terrain+bathymetry, water, buildings, roads, faults. Engine-free.
using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.World.Geo
{
    /// <summary>Settings keys (values in Assets/Game/Config/WorldGeo.json).</summary>
    public static class GeoSettingKeys
    {
        public const string TerrainMaxErrorM = "terrainMaxErrorM";
        public const string TerrainChunkCells = "terrainChunkCells";
        public const string BuildingSkirtM = "buildingSkirtM";
        public const string BuildingChunkM = "buildingChunkM";
        public const string WaterChunkCells = "waterChunkCells";
        public const string AlbedoDepthRampM = "albedoDepthRampM";
        public const string AlbedoHeightRampM = "albedoHeightRampM";
        public const string RoadMinHalfWidthM = "roadMinHalfWidthM";
        public const string MaterialSmoothness = "materialSmoothness";
    }

    public static class GeoGeom
    {
        public static List<List<double[]>> Polygons(object geometry, double ox, double oz)
        {
            var g = GeoJson.Obj(geometry);
            var type = GeoJson.Str(g, "type");
            var coords = GeoJson.Arr(GeoJson.Get(g, "coordinates"));
            var res = new List<List<double[]>>();
            if (type == "Polygon") res.Add(Rings(coords, ox, oz));
            else if (type == "MultiPolygon") foreach (var p in coords) res.Add(Rings(GeoJson.Arr(p), ox, oz));
            return res;
        }

        static List<double[]> Rings(List<object> poly, double ox, double oz)
        {
            var rings = new List<double[]>();
            foreach (var r in poly) rings.Add(Line(GeoJson.Arr(r), ox, oz));
            return rings;
        }

        public static double[] Line(List<object> pts, double ox, double oz)
        {
            var a = new double[pts.Count * 2];
            for (int i = 0; i < pts.Count; i++) { var p = GeoJson.Arr(pts[i]); a[2 * i] = (double)p[0] - ox; a[2 * i + 1] = (double)p[1] - oz; }
            return a;
        }

        public static List<double[]> Lines(object geometry, double ox, double oz)
        {
            var g = GeoJson.Obj(geometry);
            var type = GeoJson.Str(g, "type");
            var coords = GeoJson.Arr(GeoJson.Get(g, "coordinates"));
            var res = new List<double[]>();
            if (type == "LineString") res.Add(Line(coords, ox, oz));
            else if (type == "MultiLineString") foreach (var l in coords) res.Add(Line(GeoJson.Arr(l), ox, oz));
            return res;
        }

        /// <summary>Area centroid and area of polygons (holes subtract).</summary>
        public static void Centroid(List<List<double[]>> polys, out double cx, out double cz, out double area)
        {
            double a = 0, sx = 0, sz = 0;
            foreach (var poly in polys)
                foreach (var ring in poly)
                {
                    int n = ring.Length / 2;
                    if (n > 1 && ring[0] == ring[2 * (n - 1)] && ring[1] == ring[2 * (n - 1) + 1]) n--;
                    // per ring, in coordinates shifted to its first vertex (precision); signed: CCW outers +, CW holes -
                    double x0 = ring[0], z0 = ring[1], ra = 0, rx = 0, rz = 0;
                    for (int i = 0; i < n; i++)
                    {
                        double xa = ring[2 * i] - x0, za = ring[2 * i + 1] - z0;
                        double xb = ring[2 * ((i + 1) % n)] - x0, zb = ring[2 * ((i + 1) % n) + 1] - z0;
                        double c = xa * zb - xb * za;
                        ra += c; rx += (xa + xb) * c; rz += (za + zb) * c;
                    }
                    ra /= 2;
                    if (ra == 0) continue;
                    a += ra;
                    sx += ra * (rx / (6 * ra) + x0);
                    sz += ra * (rz / (6 * ra) + z0);
                }
            area = a;
            cx = a != 0 ? sx / a : 0;
            cz = a != 0 ? sz / a : 0;
        }
    }

    public sealed class TerrainLayer : IGeoLayer
    {
        public string Name => "terrain";
        public GeoLayerOutput Build(IGeoTileContext ctx)
        {
            var c = (GeoTileContext)ctx;
            var L = ctx.ManifestLayer("terrain_r16");
            int n = (int)GeoJson.Num(ctx.ManifestLayer("terrain"), "samples", 2049);
            double sp = GeoJson.Num(ctx.ManifestLayer("terrain"), "spacing_m", 1.0);
            var hf = GeoHeightField.FromR16(ctx.ReadBytes(GeoJson.Str(L, "file")), n, sp, GeoJson.Num(L, "h_min_m"), GeoJson.Num(L, "h_max_m"));
            // neighbours in a tile set: shared border heights (mean of both tiles) and every border node kept as a vertex
            bool[] force = null;
            if (c.Edge != null)
            {
                c.Edge.Apply(hf.Data, n);
                force = c.Edge.Forced;
            }
            var rt = RtinMesher.Build(hf.Data, n, sp, (float)GeoSettings.Require(ctx, GeoSettingKeys.TerrainMaxErrorM), (int)GeoSettings.Require(ctx, GeoSettingKeys.TerrainChunkCells), "terrain", force);
            hf.Render = rt.RenderHeights;
            c.Heights = hf;
            var o = new GeoLayerOutput { Layer = Name };
            o.Meshes.AddRange(rt.Chunks);
            o.Stats["terrain.triangles"] = rt.Triangles;
            o.Stats["terrain.vertices"] = rt.Vertices;
            o.Stats["terrain.chunks"] = rt.Chunks.Count;
            o.Stats["terrain.max_error_m"] = rt.MaxErrorM;
            o.Stats["terrain.rms_error_m"] = rt.RmsErrorM;
            o.Stats["terrain.max_error_setting_m"] = GeoSettings.Require(ctx, GeoSettingKeys.TerrainMaxErrorM);
            o.Stats["terrain.h_min_m"] = hf.HMin;
            o.Stats["terrain.h_max_m"] = hf.HMax;
            Attr(ctx, o, "terrain");
            return o;
        }

        internal static void Attr(IGeoTileContext ctx, GeoLayerOutput o, string layer)
        {
            foreach (var so in GeoJson.Arr(GeoJson.Get(ctx.ManifestLayer(layer), "sources")) ?? new List<object>())
            {
                var s = GeoJson.Obj(so);
                var a = GeoJson.Str(s, "attribution"); if (!string.IsNullOrEmpty(a)) o.Attributions.Add(a);
                var nn = GeoJson.Str(s, "attribution_notice"); if (!string.IsNullOrEmpty(nn)) o.Attributions.Add(nn);
            }
        }
    }

    /// <summary>Water surface at static MSL (+0.969 m NAVD88, Q-028) over water cells only (piers and diked land stay dry).</summary>
    public sealed class WaterLayer : IGeoLayer
    {
        public string Name => "water";
        public GeoLayerOutput Build(IGeoTileContext ctx)
        {
            var c = (GeoTileContext)ctx;
            var png = PngGray8.Decode(ctx.ReadBytes(GeoJson.Str(ctx.ManifestLayer("water"), "file")), out int w, out int h);
            var cells = new bool[w * h];
            for (int r = 0; r < h; r++)          // png row 0 = north; WaterCells row 0 = south
                for (int x = 0; x < w; x++) cells[(h - 1 - r) * w + x] = png[r * w + x] >= 128;
            c.WaterCells = cells;
            float y = (float)ctx.WaterLevelNavd88;
            int chunk = (int)GeoSettings.Require(ctx, GeoSettingKeys.WaterChunkCells);
            var o = new GeoLayerOutput { Layer = Name };
            int tris = 0, wet = 0;
            foreach (var b in cells) if (b) wet++;
            for (int cz = 0; cz < h / chunk; cz++)
                for (int cx = 0; cx < w / chunk; cx++)
                {
                    var pos = new List<float>(); var idx = new List<int>();
                    var used = new bool[chunk * chunk];
                    for (int r = 0; r < chunk; r++)
                        for (int q = 0; q < chunk; q++)
                        {
                            int gz = cz * chunk + r, gx = cx * chunk + q;
                            if (used[r * chunk + q] || !cells[gz * w + gx]) continue;
                            int ww = 1;
                            while (q + ww < chunk && !used[r * chunk + q + ww] && cells[gz * w + gx + ww]) ww++;
                            int hh = 1;
                            while (r + hh < chunk)
                            {
                                bool ok = true;
                                for (int k = 0; k < ww && ok; k++) ok = !used[(r + hh) * chunk + q + k] && cells[(gz + hh) * w + gx + k];
                                if (!ok) break;
                                hh++;
                            }
                            for (int a = 0; a < hh; a++) for (int k = 0; k < ww; k++) used[(r + a) * chunk + q + k] = true;
                            int s = pos.Count / 3;
                            float x0 = gx, x1 = gx + ww, z0 = gz, z1 = gz + hh;
                            pos.AddRange(new[] { x0, y, z0, x1, y, z0, x1, y, z1, x0, y, z1 });
                            idx.AddRange(new[] { s, s + 3, s + 2, s, s + 2, s + 1 });  // up-facing, clockwise from above
                        }
                    if (idx.Count == 0) continue;
                    var p = pos.ToArray();
                    var nrm = new float[p.Length];
                    for (int v = 1; v < nrm.Length; v += 3) nrm[v] = 1;
                    o.Meshes.Add(new GeoMeshData { Layer = Name, Material = "water", Name = "water_" + cx + "_" + cz, Positions = p, Normals = nrm, Indices = idx.ToArray(), ChunkX = cx, ChunkZ = cz });
                    tris += idx.Count / 3;
                }
            o.Stats["water.triangles"] = tris;
            o.Stats["water.cells"] = wet;
            o.Stats["water.fraction"] = (double)wet / cells.Length;
            o.Stats["water.level_navd88_m"] = ctx.WaterLevelNavd88;
            TerrainLayer.Attr(ctx, o, "water");
            return o;
        }
    }

    public sealed class BuildingsLayer : IGeoLayer
    {
        public string Name => "buildings";
        public GeoLayerOutput Build(IGeoTileContext ctx)
        {
            var o = new GeoLayerOutput { Layer = Name };
            var fc = GeoJson.Obj(GeoJson.Parse(ctx.ReadText(GeoJson.Str(ctx.ManifestLayer("buildings"), "file"))));
            double skirt = GeoSettings.Require(ctx, GeoSettingKeys.BuildingSkirtM), chunkM = GeoSettings.Require(ctx, GeoSettingKeys.BuildingChunkM);
            int chunks = (int)Math.Round(ctx.Tile.SizeM / chunkM);
            int tris = 0, degenerate = 0, excluded = 0;
            foreach (var fo in GeoJson.Arr(GeoJson.Get(fc, "features")))
            {
                var f = GeoJson.Obj(fo);
                var p = GeoJson.Obj(GeoJson.Get(f, "properties"));
                var b = new GeoBuildingData
                {
                    Bid = GeoJson.Str(p, "bid"),
                    HeightM = GeoJson.Num(p, "height_m"), MinHeightM = GeoJson.Num(p, "min_height_m", 0),
                    BaseNavd88M = GeoJson.Num(p, "base_navd88_m"), HeightSource = GeoJson.Str(p, "height_source"),
                    RoofShape = GeoJson.Str(p, "roof_shape"), Name = GeoJson.Str(p, "name"), HasParts = GeoJson.Bool(p, "has_parts"),
                    Excluded = GeoJson.Str(p, "excluded"), HeightCappedToParent = GeoJson.Bool(p, "height_capped_to_parent"),
                    Polygons = GeoGeom.Polygons(GeoJson.Get(f, "geometry"), ctx.OriginX, ctx.OriginZ),
                };
                b.Id = ParseBid(b.Bid);
                b.GroundMinNavd88M = GeoJson.Num(p, "ground_min_navd88_m", b.BaseNavd88M);
                var po = GeoJson.Str(p, "part_of");
                if (po != null) b.PartOf = ParseBid(po);
                GeoGeom.Centroid(b.Polygons, out b.AnchorLocalX, out b.AnchorLocalZ, out b.AreaM2);
                b.ChunkX = Clamp((int)Math.Floor(b.AnchorLocalX / chunkM), chunks);
                b.ChunkZ = Clamp((int)Math.Floor(b.AnchorLocalZ / chunkM), chunks);
                var remGeom = GeoJson.Get(p, "remainder_geometry");
                if (b.HasParts && remGeom != null)
                    b.RenderPolygons = GeoGeom.Polygons(remGeom, ctx.OriginX, ctx.OriginZ);
                if (b.Excluded != null) excluded++;
                else if (!b.HasParts || b.RenderPolygons != null)
                {
                    // walls reach below the lowest ground under the footprint, so no building floats on a slope or pit
                    double bottom = b.MinHeightM > 0 ? b.BaseNavd88M + b.MinHeightM : Math.Min(b.BaseNavd88M, b.GroundMinNavd88M) - skirt;
                    double top = b.BaseNavd88M + b.HeightM;
                    double rh = GeoJson.Num(p, "roof_height_m", 0);
                    if (top - bottom > 0.05)
                    {
                        var shapeSrc = b;
                        if (b.RenderPolygons != null)
                        {
                            shapeSrc = new GeoBuildingData { Polygons = b.RenderPolygons, ChunkX = b.ChunkX, ChunkZ = b.ChunkZ };
                            GeoGeom.Centroid(shapeSrc.Polygons, out shapeSrc.AnchorLocalX, out shapeSrc.AnchorLocalZ, out shapeSrc.AreaM2);
                            rh = 0;  // remainders are flat
                        }
                        b.Mesh = BuildingExtruder.Extrude(shapeSrc, bottom, top, double.IsNaN(rh) ? 0 : rh, b.Bid);
                        tris += b.Mesh.TriangleCount;
                    }
                    else degenerate++;
                }
                o.Buildings.Add(b);
            }
            o.Buildings.Sort((x, y) => x.Id.CompareTo(y.Id));
            o.Stats["buildings.count"] = o.Buildings.Count;
            o.Stats["buildings.triangles"] = tris;
            o.Stats["buildings.degenerate_skipped"] = degenerate;
            o.Stats["buildings.excluded_not_extruded"] = excluded;
            TerrainLayer.Attr(ctx, o, "buildings");
            return o;
        }

        static int Clamp(int v, int n) => v < 0 ? 0 : (v >= n ? n - 1 : v);

        /// <summary>geo_tile bid (osm:w123 / osm:r123) -> Core BuildingId (bld:osm-way:123 / bld:osm-relation:123).</summary>
        public static BuildingId ParseBid(string bid)
        {
            if (bid == null || bid.Length < 6 || !bid.StartsWith("osm:", StringComparison.Ordinal)) throw new FormatException("bad bid " + bid);
            var src = bid[4] == 'w' ? BuildingSource.OsmWay : bid[4] == 'r' ? BuildingSource.OsmRelation : BuildingSource.None;
            if (src == BuildingSource.None || !ulong.TryParse(bid.Substring(5), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id))
                throw new FormatException("bad bid " + bid);
            return BuildingId.From(src, id);
        }
    }

    /// <summary>Roads as records plus a ground decal raster (no coplanar geometry: no z-fighting); bridges and tunnels are flagged and not draped.</summary>
    public sealed class RoadsLayer : IGeoLayer
    {
        public string Name => "roads";
        public GeoLayerOutput Build(IGeoTileContext ctx)
        {
            var o = new GeoLayerOutput { Layer = Name };
            var fc = GeoJson.Obj(GeoJson.Parse(ctx.ReadText(GeoJson.Str(ctx.ManifestLayer("roads"), "file"))));
            int bridges = 0, tunnels = 0; double km = 0, kmDrive = 0;
            foreach (var fo in GeoJson.Arr(GeoJson.Get(fc, "features")))
            {
                var f = GeoJson.Obj(fo);
                var p = GeoJson.Obj(GeoJson.Get(f, "properties"));
                var r = new GeoRoadData
                {
                    Wid = GeoJson.Str(p, "wid"), Class = GeoJson.Str(p, "class"), Name = GeoJson.Str(p, "name"),
                    WidthM = GeoJson.Num(p, "width_m"), Bridge = GeoJson.Bool(p, "bridge"), Tunnel = GeoJson.Bool(p, "tunnel"),
                    Drivable = GeoJson.Bool(p, "drivable"), LayerIndex = (int)GeoJson.Num(p, "layer", 0),
                    Lines = GeoGeom.Lines(GeoJson.Get(f, "geometry"), ctx.OriginX, ctx.OriginZ),
                };
                if (r.Bridge) bridges++;
                if (r.Tunnel) tunnels++;
                double len = 0;
                foreach (var l in r.Lines) for (int i = 0; i + 3 < l.Length; i += 2) len += Math.Sqrt((l[i + 2] - l[i]) * (l[i + 2] - l[i]) + (l[i + 3] - l[i + 1]) * (l[i + 3] - l[i + 1]));
                km += len / 1000; if (r.Drivable) kmDrive += len / 1000;
                o.Roads.Add(r);
            }
            o.Stats["roads.count"] = o.Roads.Count;
            o.Stats["roads.bridges"] = bridges;
            o.Stats["roads.tunnels"] = tunnels;
            o.Stats["roads.km"] = km;
            o.Stats["roads.km_drivable"] = kmDrive;
            TerrainLayer.Attr(ctx, o, "roads");
            return o;
        }
    }

    /// <summary>Fault traces clipped to the tile (empty by design for L0_30_36: nearest trace 11.6 km, R-004 §7).</summary>
    public sealed class FaultsLayer : IGeoLayer
    {
        public string Name => "faults";
        public GeoLayerOutput Build(IGeoTileContext ctx)
        {
            var o = new GeoLayerOutput { Layer = Name };
            var fc = GeoJson.Obj(GeoJson.Parse(ctx.ReadText(GeoJson.Str(ctx.ManifestLayer("faults"), "file"))));
            foreach (var fo in GeoJson.Arr(GeoJson.Get(fc, "features")))
            {
                var f = GeoJson.Obj(fo);
                var p = GeoJson.Obj(GeoJson.Get(f, "properties"));
                o.Faults.Add(new GeoFaultData
                {
                    Id = GeoJson.Get(f, "id") as string, FaultName = GeoJson.Str(p, "fault_name"), Section = GeoJson.Str(p, "section"),
                    Linetype = GeoJson.Str(p, "linetype"), SlipRate = GeoJson.Str(p, "slip_rate"), SlipSense = GeoJson.Str(p, "slip_sense"),
                    Age = GeoJson.Str(p, "age"), Lines = GeoGeom.Lines(GeoJson.Get(f, "geometry"), ctx.OriginX, ctx.OriginZ),
                });
            }
            o.Stats["faults.count"] = o.Faults.Count;
            o.Stats["faults.empty_by_design"] = GeoJson.Bool(fc, "empty_by_design") ? 1 : 0;
            var near = GeoJson.Arr(GeoJson.Get(fc, "nearest_faults"));
            if (near != null && near.Count > 0) o.Stats["faults.nearest_m"] = GeoJson.Num(GeoJson.Obj(near[0]), "distance_m");
            TerrainLayer.Attr(ctx, o, "faults");
            return o;
        }
    }
}
