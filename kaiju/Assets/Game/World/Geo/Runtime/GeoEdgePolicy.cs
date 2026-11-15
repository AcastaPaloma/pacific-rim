// Edge-vertex policy for multi-tile terrain (T-021 step 3): on every border shared with a tile of the set, both tiles
// use the mean of the two tiles' data heights, and the RTIN keeps every lattice node of that border as a vertex, so the
// two render meshes (and the two physics surfaces) have identical edge vertices. Borders without a neighbour are untouched.
using System;
using System.Collections.Generic;

namespace Game.World.Geo
{
    public sealed class GeoEdgePolicy
    {
        /// <summary>Indexed by (int)GeoDirection (N, E, S, W): the border is shared with a tile of the set.</summary>
        public readonly bool[] Forced = new bool[4];
        /// <summary>Per forced side: shared heights along the border (index = x for N/S, z for E/W), corners included.</summary>
        public readonly float[][] Rows = new float[4][];
        /// <summary>Corner overrides in order SW, SE, NW, NE; NaN = unchanged.</summary>
        public readonly float[] Corners = { float.NaN, float.NaN, float.NaN, float.NaN };

        public bool Any => Forced[0] || Forced[1] || Forced[2] || Forced[3] || !float.IsNaN(Corners[0]) || !float.IsNaN(Corners[1]) || !float.IsNaN(Corners[2]) || !float.IsNaN(Corners[3]);

        /// <summary>Writes the shared heights into a tile's data lattice (row = z, col = x, n samples per side).</summary>
        public void Apply(float[] data, int n)
        {
            for (int k = 0; k < n; k++)
            {
                if (Rows[(int)GeoDirection.South] != null) data[k] = Rows[(int)GeoDirection.South][k];
                if (Rows[(int)GeoDirection.North] != null) data[(n - 1) * n + k] = Rows[(int)GeoDirection.North][k];
                if (Rows[(int)GeoDirection.West] != null) data[k * n] = Rows[(int)GeoDirection.West][k];
                if (Rows[(int)GeoDirection.East] != null) data[k * n + n - 1] = Rows[(int)GeoDirection.East][k];
            }
            if (!float.IsNaN(Corners[0])) data[0] = Corners[0];
            if (!float.IsNaN(Corners[1])) data[n - 1] = Corners[1];
            if (!float.IsNaN(Corners[2])) data[(n - 1) * n] = Corners[2];
            if (!float.IsNaN(Corners[3])) data[(n - 1) * n + n - 1] = Corners[3];
        }

        /// <summary>
        /// Policy for one tile of a set. profiles(tile) returns the data edge rows of any tile in the set
        /// (the caller may cache). Returns null when no tile of the set touches this one (edges and corners).
        /// Every shared point takes the mean of all set tiles that contain it, summed in (j, i) order, so each
        /// tile computes the identical float.
        /// </summary>
        public static GeoEdgePolicy For(GeoTileSet set, GeoTileId id, Func<GeoTileId, GeoEdgeProfiles> profiles)
        {
            var p = new GeoEdgePolicy();
            var own = profiles(id);
            int n = own.North.Length;
            foreach (GeoDirection d in new[] { GeoDirection.North, GeoDirection.East, GeoDirection.South, GeoDirection.West })
            {
                if (!set.TryGetNeighbour(id, d, out var nb)) continue;
                var theirs = profiles(nb).Of(Opposite(d));
                var mine = own.Of(d);
                bool mineFirst = d == GeoDirection.East || d == GeoDirection.North ? true : false;   // A (west/south tile) first
                var row = new float[n];
                for (int k = 0; k < n; k++) row[k] = (float)(mineFirst ? mine[k] + theirs[k] : theirs[k] + mine[k]) * 0.5f;
                p.Forced[(int)d] = true;
                p.Rows[(int)d] = row;
            }
            // corners: mean over every set tile containing the corner point
            for (int cz = 0; cz < 2; cz++)
                for (int cx = 0; cx < 2; cx++)
                {
                    var vals = new List<(int j, int i, double v)>();
                    int[] oxs = cx == 1 ? new[] { 0, 1 } : new[] { -1, 0 };
                    int[] ozs = cz == 1 ? new[] { 0, 1 } : new[] { -1, 0 };
                    foreach (int ox in oxs)
                        foreach (int oz in ozs)
                        {
                            int ti = id.I + ox, tj = id.J + oz;
                            if (ti < 0 || tj < 0) continue;
                            var t = GeoTileId.From(id.Level, ti, tj);
                            if (ox == 0 && oz == 0) { vals.Add((tj, ti, CornerOf(own, cx, cz, n))); continue; }
                            if (!set.Contains(t)) continue;
                            // the same point seen from tile t: opposite corner along every axis that differs
                            int tx = ox == 0 ? cx : 1 - cx, tz = oz == 0 ? cz : 1 - cz;
                            vals.Add((tj, ti, CornerOf(profiles(t), tx, tz, n)));
                        }
                    if (vals.Count < 2) continue;
                    vals.Sort((a, b) => a.j != b.j ? a.j.CompareTo(b.j) : a.i.CompareTo(b.i));
                    double s = 0; foreach (var v in vals) s += v.v;
                    p.Corners[cz * 2 + cx] = (float)(s / vals.Count);
                }
            return p.Any ? p : null;
        }

        static double CornerOf(GeoEdgeProfiles e, int cx, int cz, int n) => (cz == 0 ? e.South : e.North)[cx == 0 ? 0 : n - 1];

        static GeoDirection Opposite(GeoDirection d) =>
            d == GeoDirection.North ? GeoDirection.South : d == GeoDirection.South ? GeoDirection.North : d == GeoDirection.East ? GeoDirection.West : GeoDirection.East;
    }
}
