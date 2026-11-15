// A set of same-level geo_tile@v1 tiles: placement offsets, neighbours, shared edges (T-021 step 1).
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.World.Geo
{
    public sealed class GeoTileSet
    {
        readonly Dictionary<GeoTileId, string> _dirs = new Dictionary<GeoTileId, string>();
        readonly Dictionary<GeoTileId, IReadOnlyDictionary<string, object>> _manifests = new Dictionary<GeoTileId, IReadOnlyDictionary<string, object>>();
        readonly List<GeoSharedEdge> _edges;

        /// <summary>Tiles sorted by (j, i).</summary>
        public IReadOnlyList<GeoTileId> Tiles { get; }
        public int Level { get; }
        public double TileSizeM { get; }

        /// <summary>Index-only set (synthetic cases, no manifests). All tiles must share one level.</summary>
        public GeoTileSet(IEnumerable<GeoTileId> tiles)
        {
            var list = tiles.Distinct().OrderBy(t => t.J).ThenBy(t => t.I).ToList();
            if (list.Count == 0) throw new GeoTileSetRejectedException(new[] { "tile set is empty" });
            Level = list[0].Level;
            if (list.Any(t => t.Level != Level)) throw new GeoTileSetRejectedException(new[] { "tile set mixes levels: " + string.Join(", ", list.Select(t => t.ToString())) });
            TileSizeM = list[0].SizeM;
            Tiles = list;
            _edges = BuildEdges();
        }

        internal void Attach(GeoTileId id, string dir, IReadOnlyDictionary<string, object> manifest) { _dirs[id] = dir; _manifests[id] = manifest; }

        public bool Contains(GeoTileId id) => Tiles.Contains(id);
        public string Directory(GeoTileId id) => _dirs.TryGetValue(id, out var d) ? d : null;
        public IReadOnlyDictionary<string, object> Manifest(GeoTileId id) => _manifests.TryGetValue(id, out var m) ? m : null;

        /// <summary>Tile origin in float64 BayTM metres (i, j) * size; exact (multiple of 2048 m).</summary>
        public static void OriginOf(GeoTileId id, out double x, out double z) { x = id.OriginX; z = id.OriginZ; }

        /// <summary>Offset of a tile root relative to a reference tile (e.g. the tile holding the floating origin), BayTM metres.</summary>
        public static void OffsetFrom(GeoTileId reference, GeoTileId id, out double dx, out double dz) { dx = id.OriginX - reference.OriginX; dz = id.OriginZ - reference.OriginZ; }

        public static GeoTileId Step(GeoTileId id, GeoDirection d)
        {
            int di = d == GeoDirection.East ? 1 : d == GeoDirection.West ? -1 : 0;
            int dj = d == GeoDirection.North ? 1 : d == GeoDirection.South ? -1 : 0;
            if (id.I + di < 0 || id.J + dj < 0) throw new ArgumentOutOfRangeException(nameof(d), "no tile beyond the grid origin");
            return GeoTileId.From(id.Level, id.I + di, id.J + dj);
        }

        /// <summary>The neighbouring tile in the set, or false when the neighbour is absent (the edge is external).</summary>
        public bool TryGetNeighbour(GeoTileId id, GeoDirection d, out GeoTileId neighbour)
        {
            neighbour = default;
            if ((d == GeoDirection.West && id.I == 0) || (d == GeoDirection.South && id.J == 0)) return false;
            var n = Step(id, d);
            if (!Contains(n)) return false;
            neighbour = n;
            return true;
        }

        /// <summary>Internal edges: each listed once, A = west or south tile.</summary>
        public IReadOnlyList<GeoSharedEdge> SharedEdges => _edges;

        List<GeoSharedEdge> BuildEdges()
        {
            var set = new HashSet<GeoTileId>(Tiles);
            var e = new List<GeoSharedEdge>();
            foreach (var t in Tiles)
            {
                double s = t.SizeM;
                var east = GeoTileId.From(t.Level, t.I + 1, t.J);
                if (set.Contains(east)) e.Add(new GeoSharedEdge(t, east, GeoDirection.East, t.OriginX + s, t.OriginZ, t.OriginX + s, t.OriginZ + s));
                var north = GeoTileId.From(t.Level, t.I, t.J + 1);
                if (set.Contains(north)) e.Add(new GeoSharedEdge(t, north, GeoDirection.North, t.OriginX, t.OriginZ + s, t.OriginX + s, t.OriginZ + s));
            }
            return e;
        }

        /// <summary>Tile keys of a rectangular block, inclusive: Range(0, 25, 27, 36, 38) is the 3 x 3 Golden Gate block.</summary>
        public static List<GeoTileId> Range(int level, int i0, int i1, int j0, int j1)
        {
            var l = new List<GeoTileId>();
            for (int j = j0; j <= j1; j++) for (int i = i0; i <= i1; i++) l.Add(GeoTileId.From(level, i, j));
            return l;
        }

        public static List<GeoTileId> ParseKeys(IEnumerable<string> keys)
        {
            var l = new List<GeoTileId>();
            foreach (var k in keys)
            {
                if (!GeoTileId.TryParse(k, out var id)) throw new GeoTileSetRejectedException(new[] { "bad tile key '" + k + "'" });
                l.Add(id);
            }
            return l;
        }
    }
}
