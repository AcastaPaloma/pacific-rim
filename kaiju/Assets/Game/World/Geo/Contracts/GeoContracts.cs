// World.Geo contracts (layer L2): engine-free data types and interfaces for GIS tiles (F-040, F-042, F-043).
// Other modules reference only this assembly. Versioned: changing a data shape bumps GeoContractVersions.
using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.World.Geo
{
    public static class GeoContractVersions
    {
        /// <summary>Baked tile data spec read by the importer (research/R-004.md §3).</summary>
        public const string TileSpec = "geo_tile@v1";
        /// <summary>As-built measurement export read by the M-005@v1 checker (Pipelines/geo/m005_check.py).</summary>
        public const string ExportSpec = "geo_export@v1";
        /// <summary>IGeoLayer / GeoLayerOutput shape.</summary>
        public const int LayerApi = 1;
    }

    /// <summary>Feature flags of World.Geo (Assets/Game/Config/FeatureFlags/WorldGeo.json, default off).</summary>
    public static class GeoFlags
    {
        /// <summary>Install the IGeoWorld service and allow runtime tile builds.</summary>
        public const string Enabled = "world.geo.enabled";
    }

    /// <summary>BayTM@v1 tile index (R-004 §3.1): level L covers 2048 * 2^L metres; origin = (i, j) * size.</summary>
    public readonly struct GeoTileId : IEquatable<GeoTileId>
    {
        public const double BaseSizeM = 2048.0;
        public readonly int Level, I, J;
        private GeoTileId(int level, int i, int j) { Level = level; I = i; J = j; }
        public static GeoTileId From(int level, int i, int j)
        {
            if (level < 0 || level > 8 || i < 0 || j < 0) throw new ArgumentOutOfRangeException(nameof(level), "tile index out of range");
            return new GeoTileId(level, i, j);
        }
        public double SizeM => BaseSizeM * (1 << Level);
        public double OriginX => I * SizeM;
        public double OriginZ => J * SizeM;
        public static GeoTileId Containing(double x, double z, int level = 0)
        {
            double s = BaseSizeM * (1 << level);
            return From(level, (int)Math.Floor(x / s), (int)Math.Floor(z / s));
        }
        /// <summary>Half-open ownership [x0, x1) x [z0, z1).</summary>
        public bool Contains(double x, double z) => x >= OriginX && x < OriginX + SizeM && z >= OriginZ && z < OriginZ + SizeM;
        public override string ToString() => "L" + Level + "_" + I.ToString("00") + "_" + J.ToString("00");
        public string Folder => "L" + Level + "/" + I.ToString("00") + "_" + J.ToString("00");
        public static bool TryParse(string s, out GeoTileId id)
        {
            id = default;
            if (string.IsNullOrEmpty(s) || s[0] != 'L') return false;
            var p = s.Substring(1).Split('_');
            if (p.Length != 3) return false;
            if (!int.TryParse(p[0], out var l) || !int.TryParse(p[1], out var i) || !int.TryParse(p[2], out var j)) return false;
            if (l < 0 || i < 0 || j < 0) return false;
            id = new GeoTileId(l, i, j);
            return true;
        }
        public bool Equals(GeoTileId o) => Level == o.Level && I == o.I && J == o.J;
        public override bool Equals(object obj) => obj is GeoTileId o && Equals(o);
        public override int GetHashCode() => (Level * 397 + I) * 397 + J;
    }

    /// <summary>Engine-free mesh payload in tile-local metres (x east, y NAVD88 up, z north). Left-handed, clockwise front faces.</summary>
    public sealed class GeoMeshData
    {
        public string Layer;
        public string Name;
        /// <summary>Material key resolved by the Unity adapter: terrain, building, water, fault.</summary>
        public string Material;
        public float[] Positions = Array.Empty<float>();   // 3 per vertex
        public float[] Normals;                              // 3 per vertex or null
        public float[] Uv;                                   // 2 per vertex or null
        public int[] Indices = Array.Empty<int>();           // 3 per triangle
        public int ChunkX, ChunkZ;
        public int VertexCount => Positions.Length / 3;
        public int TriangleCount => Indices.Length / 3;
    }

    /// <summary>One OSM building or building:part as imported (geo_tile@v1 buildings.geojson + own mesh).</summary>
    public sealed class GeoBuildingData
    {
        public BuildingId Id;
        public string Bid;                 // e.g. osm:w431972186
        public BuildingId PartOf;          // default when not a part
        public bool HasParts;              // structure rendered through its parts only
        public double HeightM, MinHeightM, BaseNavd88M;
        /// <summary>Lowest ground under the footprint (walls reach below it).</summary>
        public double GroundMinNavd88M;
        /// <summary>Non-surface feature kept as a record and never extruded ("underground", "ship"), else null.</summary>
        public string Excluded;
        /// <summary>A part height from a fallback capped at its structure's known height.</summary>
        public bool HeightCappedToParent;
        public string HeightSource, RoofShape, Name;
        /// <summary>Footprint polygons, tile-local metres: Polygons[p][ring] = x0,z0,x1,z1,... (ring 0 outer CCW, holes CW).</summary>
        public List<List<double[]>> Polygons = new List<List<double[]>>();
        /// <summary>Rendered footprint when it differs from Polygons: a structure with parts renders its outline remainder (outline minus parts). Null = Polygons.</summary>
        public List<List<double[]>> RenderPolygons;
        public double AnchorLocalX, AnchorLocalZ;   // footprint area centroid, tile-local
        public double AreaM2;
        /// <summary>Optional SF Assessor roll fields baked by the tile pipeline; 0 / null = absent.</summary>
        public int YearBuilt, Stories;
        public string StructClass;
        public GeoMeshData Mesh;                    // null when nothing of this record is rendered (structure fully covered by its parts)
        public int ChunkX, ChunkZ;
    }

    public enum GeoColliderShape { Box, Capsule }

    /// <summary>One static collider of a landmark, tile-local metres (x east, z north), y NAVD88 (R-020 §3).
    /// Box: P0 and P1 are the centres of the two end faces of the axis (a tower leg: bottom and top centre; a deck piece: the road-surface-offset
    /// axis centre, the box top is at axis y + Depth/2). Width is the extent along the horizontal side vector (cos Yaw, 0, sin Yaw);
    /// Depth is the extent along the remaining perpendicular (vertical for a sloped deck piece, horizontal across Width for a vertical leg).
    /// Capsule: P0 and P1 are the axis ends, Radius the radius; Width, Depth and Yaw are unused (0).</summary>
    public sealed class GeoColliderData
    {
        public GeoColliderShape Shape;
        public double P0X, P0Y, P0Z, P1X, P1Y, P1Z;
        public double Width, Depth, Radius, YawRad;
    }

    /// <summary>One baked landmark part (geo_tile landmarks.geojson, R-020): a tower leg, a deck piece or a cable piece, static only.
    /// Linear kinds are cut at tile edges at shared float64 nodes (last vertex of one piece == first of the next).</summary>
    public sealed class GeoLandmarkData
    {
        public string Lid, Bid, Kind, Confidence, HeightSource;
        public int PartIdx, Piece;
        public double BaseNavd88M, TopNavd88M, HeightM, WidthM, DiameterM;
        public double AnchorLocalX, AnchorLocalZ;
        /// <summary>Tower leg footprint rings, tile-local x0,z0,x1,z1,... (null for linear kinds).</summary>
        public List<double[]> Footprint;
        /// <summary>Linear kinds: original vertices, tile-local x0,z0,x1,z1,... and NAVD88 y per vertex (null for towers).</summary>
        public double[] Line, Y;
        public List<GeoColliderData> Colliders = new List<GeoColliderData>();
        public GeoMeshData Mesh;
    }

    public sealed class GeoRoadData
    {
        public string Wid, Class, Name;
        public double WidthM;
        public bool Bridge, Tunnel, Drivable;
        public int LayerIndex;
        public List<double[]> Lines = new List<double[]>();  // tile-local x0,z0,x1,z1,...
    }

    public sealed class GeoFaultData
    {
        public string Id, FaultName, Section, Linetype, SlipRate, SlipSense, Age;
        public List<double[]> Lines = new List<double[]>();
    }

    /// <summary>What one IGeoLayer contributes to a tile.</summary>
    public sealed class GeoLayerOutput
    {
        public string Layer;
        public readonly List<GeoMeshData> Meshes = new List<GeoMeshData>();
        public readonly List<GeoBuildingData> Buildings = new List<GeoBuildingData>();
        public readonly List<GeoRoadData> Roads = new List<GeoRoadData>();
        public readonly List<GeoFaultData> Faults = new List<GeoFaultData>();
        public readonly List<GeoLandmarkData> Landmarks = new List<GeoLandmarkData>();
        /// <summary>Free-form statistics (counts, errors) reported in the tile stats.</summary>
        public readonly SortedDictionary<string, double> Stats = new SortedDictionary<string, double>(StringComparer.Ordinal);
        /// <summary>Attribution strings of the layer's sources (manifest), for the credits asset.</summary>
        public readonly SortedSet<string> Attributions = new SortedSet<string>(StringComparer.Ordinal);
    }

    /// <summary>Heights as built (the render surface and the full-resolution data surface), tile-local sampling.</summary>
    public interface IGeoHeightField
    {
        int Samples { get; }           // 2049
        double Spacing { get; }        // 1 m at L0
        /// <summary>Bilinear height of the full-resolution geo_tile surface (r16 decoded), NAVD88 m.</summary>
        double SampleData(double localX, double localZ);
        /// <summary>Height of the rendered (LOD) surface, NAVD88 m.</summary>
        double SampleRender(double localX, double localZ);
    }

    /// <summary>Read access to one baked tile while it is being built. Layers run in a fixed order (terrain first).</summary>
    public interface IGeoTileContext
    {
        GeoTileId Tile { get; }
        double OriginX { get; }
        double OriginZ { get; }
        double WaterLevelNavd88 { get; }
        string ReadText(string file);
        byte[] ReadBytes(string file);
        /// <summary>Manifest layer entry by name (as parsed JSON object) or null.</summary>
        IReadOnlyDictionary<string, object> ManifestLayer(string layer);
        /// <summary>Available after the terrain layer ran.</summary>
        IGeoHeightField Terrain { get; }
        /// <summary>Water mask, cell-centred (Samples-1)^2, row 0 = south; true = water surface. Available after the water layer ran.</summary>
        bool[] WaterCells { get; }
        double Setting(string key, double fallback);
    }

    /// <summary>MODULARITY §4.6 extension point: one importer per GIS layer. Add a layer = one class + registration.</summary>
    public interface IGeoLayer
    {
        string Name { get; }
        GeoLayerOutput Build(IGeoTileContext context);
    }

    public struct GeoBuildingInfo
    {
        public BuildingId Id, PartOf;
        public double BaseNavd88M, TopNavd88M, AnchorX, AnchorZ;   // anchor in float64 BayTM
        public string HeightSource;
        public bool HasParts;
    }

    /// <summary>Runtime world service (registered by GeoModule.Install when world.geo.enabled is on).</summary>
    public interface IGeoWorld
    {
        IReadOnlyList<GeoTileId> Tiles { get; }
        double WaterLevelNavd88 { get; }
        bool TryGetBuilding(BuildingId id, out GeoBuildingInfo info);
        /// <summary>Removes one building at runtime, no re-bake. A structure with parts removes its parts too. Returns buildings removed.</summary>
        int RemoveBuilding(BuildingId id);
        bool IsRemoved(BuildingId id);
        /// <summary>NAVD88 height of the data surface at a float64 BayTM position, NaN outside loaded tiles.</summary>
        double SampleTerrain(double x, double z);
    }

    /// <summary>Published on the Core bus when a building is removed (same step).</summary>
    public struct GeoBuildingRemoved : IEvent
    {
        public BuildingId Building;
        public BuildingId Structure;
    }

    public sealed class GeoTileRejectedException : Exception
    {
        public readonly IReadOnlyList<string> Problems;
        public GeoTileRejectedException(string tile, IReadOnlyList<string> problems)
            : base("[World.Geo] tile " + tile + " refused: " + string.Join("; ", problems)) { Problems = problems; }
    }
}
