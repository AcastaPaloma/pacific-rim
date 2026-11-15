// Median shaking field of one rupture: BSSA14 (strike-slip, Mw of the rupture) at Rjb to the ruptured polyline.
// Rjb rule (documented choice): only MAPPED ruptured segments count, i.e. segments between VertexStart and VertexEnd whose
// FaultSystem.Bridge flag is false. Bridged gap segments are interpolation across unmapped gaps (zero slip), so they are not rupture.
using System;
using System.Collections.Generic;

namespace Game.World.Seismic
{
    public sealed class ShakingField : IShakingField
    {
        readonly Dictionary<uint, Entry> _byEvent = new Dictionary<uint, Entry>();

        sealed class Entry
        {
            public double Mw;
            public double[] X0, Z0, X1, Z1;
        }

        public ShakingField() { }
        public ShakingField(RuptureResult r) { Add(r); }

        /// <summary>Register a rupture (replaces any earlier one with the same EventId).</summary>
        public void Add(RuptureResult r)
        {
            var sys = r.System;
            var a = new List<int>();
            for (int i = r.VertexStart; i < r.VertexEnd; i++) if (!sys.Bridge[i]) a.Add(i);
            var e = new Entry { Mw = r.Mw, X0 = new double[a.Count], Z0 = new double[a.Count], X1 = new double[a.Count], Z1 = new double[a.Count] };
            for (int k = 0; k < a.Count; k++)
            {
                int i = a[k];
                e.X0[k] = sys.X[i]; e.Z0[k] = sys.Z[i]; e.X1[k] = sys.X[i + 1]; e.Z1[k] = sys.Z[i + 1];
            }
            _byEvent[r.EventId] = e;
        }

        public bool TryGetShaking(long eventId, double x, double z, double vs30, out ShakingSample s)
        {
            s = default(ShakingSample);
            Entry e;
            if (eventId < 0 || eventId > uint.MaxValue || !_byEvent.TryGetValue((uint)eventId, out e) || e.X0.Length == 0) return false;
            double rjbM = Rjb(e.X0, e.Z0, e.X1, e.Z1, x, z);
            double rKm = rjbM / 1000.0;
            double pga = Bssa14.PgaG(e.Mw, rKm, vs30), pgv = Bssa14.PgvCmS(e.Mw, rKm, vs30);
            s.PgaG = (float)pga; s.PgvCmS = (float)pgv; s.Mmi = (float)Bssa14.Mmi(pgv); s.RjbM = (float)rjbM;
            return true;
        }

        /// <summary>Horizontal distance from (x, z) to the nearest of the given segments, m.</summary>
        public static double Rjb(double[] x0, double[] z0, double[] x1, double[] z1, double x, double z)
        {
            double best = double.MaxValue;
            for (int i = 0; i < x0.Length; i++)
            {
                double d = PointSegment(x0[i], z0[i], x1[i], z1[i], x, z);
                if (d < best) best = d;
            }
            return best;
        }

        public static double PointSegment(double ax, double az, double bx, double bz, double px, double pz)
        {
            double dx = bx - ax, dz = bz - az, l2 = dx * dx + dz * dz;
            double t = l2 > 0 ? ((px - ax) * dx + (pz - az) * dz) / l2 : 0;
            t = t < 0 ? 0 : (t > 1 ? 1 : t);
            double qx = ax + t * dx - px, qz = az + t * dz - pz;
            return Math.Sqrt(qx * qx + qz * qz);
        }
    }
}
