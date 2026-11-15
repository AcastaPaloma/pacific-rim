// Planar geometry helpers for InfraRecord (BayTM metres). Deterministic, no allocation.
using System;

namespace Game.World.Infrastructure
{
    public static class InfraGeometry
    {
        /// <summary>Owner point as the bake defines it: node, arc-length midpoint of a polyline, centroid-ish interior point of an area (bbox centre moved inside when needed).</summary>
        public static void OwnerPoint(InfraRecord r, out double x, out double z)
        {
            var p = r.Points;
            int n = p.Length / 2;
            if (n == 1 || n == 0) { x = n == 0 ? 0 : p[0]; z = n == 0 ? 0 : p[1]; return; }
            if (!r.IsArea)
            {
                double total = 0;
                for (int i = 2; i < p.Length; i += 2) total += Hyp(p[i] - p[i - 2], p[i + 1] - p[i - 1]);
                double half = total * 0.5, acc = 0;
                for (int i = 2; i < p.Length; i += 2)
                {
                    double d = Hyp(p[i] - p[i - 2], p[i + 1] - p[i - 1]);
                    if (acc + d >= half && d > 0) { double t = (half - acc) / d; x = p[i - 2] + t * (p[i] - p[i - 2]); z = p[i - 1] + t * (p[i + 1] - p[i - 1]); return; }
                    acc += d;
                }
                x = p[p.Length - 2]; z = p[p.Length - 1]; return;
            }
            // Area: first vertex-average inside the ring, else the vertex nearest the bbox centre (never outside the object).
            double cx = (r.MinX + r.MaxX) * 0.5, cz = (r.MinZ + r.MaxZ) * 0.5;
            if (Contains(p, cx, cz)) { x = cx; z = cz; return; }
            double best = double.MaxValue; x = p[0]; z = p[1];
            for (int i = 0; i < p.Length; i += 2) { double d = Hyp(p[i] - cx, p[i + 1] - cz); if (d < best) { best = d; x = p[i]; z = p[i + 1]; } }
        }

        public static double Hyp(double a, double b) => Math.Sqrt(a * a + b * b);

        /// <summary>Even-odd test of (x,z) against a ring given as x,z pairs.</summary>
        public static bool Contains(double[] ring, double x, double z)
        {
            bool inside = false;
            int n = ring.Length / 2;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = ring[2 * i], zi = ring[2 * i + 1], xj = ring[2 * j], zj = ring[2 * j + 1];
                if ((zi > z) != (zj > z) && x < (xj - xi) * (z - zi) / (zj - zi) + xi) inside = !inside;
            }
            return inside;
        }

        public static double DistanceToSegment(double px, double pz, double ax, double az, double bx, double bz)
        {
            double dx = bx - ax, dz = bz - az, l2 = dx * dx + dz * dz;
            double t = l2 <= 0 ? 0 : Math.Max(0, Math.Min(1, ((px - ax) * dx + (pz - az) * dz) / l2));
            return Hyp(px - (ax + t * dx), pz - (az + t * dz));
        }

        /// <summary>Distance from (x,z) to the record's geometry; 0 inside an area.</summary>
        public static double Distance(InfraRecord r, double x, double z)
        {
            var p = r.Points;
            if (p.Length == 2) return Hyp(x - p[0], z - p[1]);
            if (r.IsArea && Contains(p, x, z)) return 0;
            double best = double.MaxValue;
            for (int i = 2; i < p.Length; i += 2)
            {
                double d = DistanceToSegment(x, z, p[i - 2], p[i - 1], p[i], p[i + 1]);
                if (d < best) best = d;
            }
            return best;
        }
    }
}
