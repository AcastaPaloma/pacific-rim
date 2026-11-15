// World.Geo tile-set contracts (T-021 step 1): neighbours, shared edges, seam report rows. Engine-free, LayerApi 1 unchanged.
using System;
using System.Collections.Generic;

namespace Game.World.Geo
{
    /// <summary>Compass direction on the BayTM grid: North = +z (j + 1), East = +x (i + 1).</summary>
    public enum GeoDirection { North = 0, East = 1, South = 2, West = 3 }

    /// <summary>An internal edge shared by two tiles of a set. A is the west (EastWest) or south (NorthSouth) tile.</summary>
    public readonly struct GeoSharedEdge : IEquatable<GeoSharedEdge>
    {
        public readonly GeoTileId A, B;
        /// <summary>Direction from A to B: East (edge is the line x = const) or North (line z = const).</summary>
        public readonly GeoDirection FromA;
        /// <summary>Edge end points in float64 BayTM (x0,z0) -> (x1,z1), increasing along the edge.</summary>
        public readonly double X0, Z0, X1, Z1;
        public GeoSharedEdge(GeoTileId a, GeoTileId b, GeoDirection fromA, double x0, double z0, double x1, double z1)
        { A = a; B = b; FromA = fromA; X0 = x0; Z0 = z0; X1 = x1; Z1 = z1; }
        public double LengthM => Math.Abs(X1 - X0) + Math.Abs(Z1 - Z0);
        public override string ToString() => A + (FromA == GeoDirection.East ? " | " : " / ") + B;
        public bool Equals(GeoSharedEdge o) => A.Equals(o.A) && B.Equals(o.B);
        public override bool Equals(object obj) => obj is GeoSharedEdge o && Equals(o);
        public override int GetHashCode() => A.GetHashCode() * 31 + B.GetHashCode();
    }

    /// <summary>Terrain seam statistics along one shared edge (reporting only; the gate is Pipelines/geo, T-035).</summary>
    public sealed class GeoSeamReport
    {
        public const double LimitP99M = 0.20, LimitMaxM = 0.50;   // R-020 §4 M-005.10(a)
        public GeoSharedEdge Edge;
        public int Samples;
        public double MeanAbsM, P99AbsM, MaxAbsM;
        /// <summary>Largest |dE| over the full edge on the full-resolution data surface (float64 of float32 decode).</summary>
        public bool P99WithinLimit => P99AbsM <= LimitP99M;
        public bool MaxWithinLimit => MaxAbsM <= LimitMaxM;
        public override string ToString() =>
            Edge + ": n=" + Samples + " mean=" + MeanAbsM.ToString("0.0000") + " p99=" + P99AbsM.ToString("0.0000") + " max=" + MaxAbsM.ToString("0.0000") + " m";
    }

    /// <summary>A tile set was refused (version mix, bad manifest, absent folder, duplicate building owner). Lists every problem.</summary>
    public sealed class GeoTileSetRejectedException : Exception
    {
        public readonly IReadOnlyList<string> Problems;
        public GeoTileSetRejectedException(IReadOnlyList<string> problems)
            : base("[World.Geo] tile set refused: " + string.Join("; ", problems)) { Problems = problems; }
    }
}
