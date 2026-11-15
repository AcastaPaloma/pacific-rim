// Seam data check and building ownership across shared edges (T-021 step 1). Reporting only: the gate is Pipelines/geo (T-035).
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.World.Geo
{
    /// <summary>The four edge rows of a data surface. Index along the edge: W/E edges run south to north, S/N edges west to east.</summary>
    public sealed class GeoEdgeProfiles
    {
        public double[] North, East, South, West;
        public double[] Of(GeoDirection d) => d == GeoDirection.North ? North : d == GeoDirection.East ? East : d == GeoDirection.South ? South : West;

        /// <summary>Data layout: Data[row = z (south first)][col = x].</summary>
        public static GeoEdgeProfiles From(GeoHeightField hf)
        {
            int n = hf.Samples; var d = hf.Data;
            var p = new GeoEdgeProfiles { North = new double[n], East = new double[n], South = new double[n], West = new double[n] };
            for (int k = 0; k < n; k++)
            {
                p.South[k] = d[k]; p.North[k] = d[(n - 1) * n + k];
                p.West[k] = d[k * n]; p.East[k] = d[k * n + n - 1];
            }
            return p;
        }
    }

    public static class GeoSeams
    {
        /// <summary>|dE| per edge sample between A's far edge and B's near edge (same BayTM points).</summary>
        public static GeoSeamReport Measure(GeoSharedEdge edge, GeoEdgeProfiles a, GeoEdgeProfiles b)
        {
            var pa = a.Of(edge.FromA);
            var pb = b.Of(edge.FromA == GeoDirection.East ? GeoDirection.West : GeoDirection.South);
            if (pa.Length != pb.Length) throw new InvalidOperationException("edge profile length mismatch on " + edge);
            var d = new double[pa.Length];
            double sum = 0, max = 0;
            for (int k = 0; k < d.Length; k++) { d[k] = Math.Abs(pa[k] - pb[k]); sum += d[k]; if (d[k] > max) max = d[k]; }
            Array.Sort(d);
            int idx = Math.Max(0, (int)Math.Ceiling(0.99 * d.Length) - 1);   // nearest-rank p99
            return new GeoSeamReport { Edge = edge, Samples = d.Length, MeanAbsM = sum / d.Length, P99AbsM = d[idx], MaxAbsM = max };
        }

        /// <summary>One report per shared edge. profiles(tile) is called once per tile (the caller may stream from disk).</summary>
        public static List<GeoSeamReport> Report(GeoTileSet set, Func<GeoTileId, GeoEdgeProfiles> profiles)
        {
            var cache = new Dictionary<GeoTileId, GeoEdgeProfiles>();
            GeoEdgeProfiles Get(GeoTileId t) => cache.TryGetValue(t, out var p) ? p : cache[t] = profiles(t);
            return set.SharedEdges.Select(e => Measure(e, Get(e.A), Get(e.B))).ToList();
        }

        /// <summary>Real tiles: edge profiles from terrain.r16 via the importer's reader.</summary>
        public static List<GeoSeamReport> Report(GeoTileSet set) => Report(set, t => GeoTileSetLoader.ReadEdgeProfiles(set, t));

        /// <summary>bid -> tiles that list it, only for bids listed by more than one tile (T-003 owner rule: at most one).</summary>
        public static SortedDictionary<string, List<GeoTileId>> DuplicateOwners(IEnumerable<KeyValuePair<GeoTileId, IEnumerable<string>>> bidsByTile)
        {
            var owners = new Dictionary<string, List<GeoTileId>>(StringComparer.Ordinal);
            foreach (var kv in bidsByTile)
                foreach (var bid in new HashSet<string>(kv.Value))
                {
                    if (!owners.TryGetValue(bid, out var l)) owners[bid] = l = new List<GeoTileId>();
                    l.Add(kv.Key);
                }
            var dup = new SortedDictionary<string, List<GeoTileId>>(StringComparer.Ordinal);
            foreach (var kv in owners) if (kv.Value.Count > 1) dup[kv.Key] = kv.Value;
            return dup;
        }

        /// <summary>Asserts a building id appears in at most one tile of the set; returns the count of distinct ids.</summary>
        public static int AssertSingleOwner(IEnumerable<KeyValuePair<GeoTileId, IEnumerable<string>>> bidsByTile)
        {
            var all = bidsByTile.ToList();
            var dup = DuplicateOwners(all);
            if (dup.Count > 0)
                throw new GeoTileSetRejectedException(dup.Take(20).Select(kv => "building " + kv.Key + " owned by " + string.Join(", ", kv.Value)).ToList());
            return all.SelectMany(kv => kv.Value).Distinct().Count();
        }

        public static int AssertSingleOwner(GeoTileSet set) =>
            AssertSingleOwner(set.Tiles.Select(t => new KeyValuePair<GeoTileId, IEnumerable<string>>(t, GeoTileSetLoader.ReadBuildingIds(set, t))));
    }
}
