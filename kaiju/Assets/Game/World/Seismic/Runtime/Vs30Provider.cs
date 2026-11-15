// Vs30 per site for the BSSA14 site term. M1 rule (Q-NEW-c open, no Vs30 grid): everything is rock (760 m/s) unless the site lies
// inside a BayTM polygon marked fill (270 m/s). The polygon list is empty by default, so fill is opt-in.
using System.Collections.Generic;

namespace Game.World.Seismic
{
    public sealed class Vs30Provider
    {
        sealed class Poly { public double[] X, Z; public double MinX, MaxX, MinZ, MaxZ; }
        readonly List<Poly> _fill = new List<Poly>();

        public double RockVs30 = SeismicAssumptions.Vs30RockMs;
        public double FillVs30 = SeismicAssumptions.Vs30FillMs;
        public int FillPolygonCount { get { return _fill.Count; } }

        /// <summary>Mark a BayTM polygon (at least 3 vertices, implicitly closed) as fill.</summary>
        public void AddFillPolygon(double[] x, double[] z)
        {
            if (x == null || z == null || x.Length != z.Length || x.Length < 3) throw new System.ArgumentException("polygon needs >= 3 matching vertices");
            var p = new Poly { X = (double[])x.Clone(), Z = (double[])z.Clone(), MinX = double.MaxValue, MinZ = double.MaxValue, MaxX = double.MinValue, MaxZ = double.MinValue };
            for (int i = 0; i < x.Length; i++)
            {
                if (x[i] < p.MinX) p.MinX = x[i]; if (x[i] > p.MaxX) p.MaxX = x[i];
                if (z[i] < p.MinZ) p.MinZ = z[i]; if (z[i] > p.MaxZ) p.MaxZ = z[i];
            }
            _fill.Add(p);
        }

        public void ClearFill() { _fill.Clear(); }

        public double Vs30At(double x, double z)
        {
            for (int k = 0; k < _fill.Count; k++)
                if (Contains(_fill[k], x, z)) return FillVs30;
            return RockVs30;
        }

        static bool Contains(Poly p, double x, double z)
        {
            if (x < p.MinX || x > p.MaxX || z < p.MinZ || z > p.MaxZ) return false;
            bool inside = false;
            int n = p.X.Length;
            for (int i = 0, j = n - 1; i < n; j = i++)
                if ((p.Z[i] > z) != (p.Z[j] > z) && x < (p.X[j] - p.X[i]) * (z - p.Z[i]) / (p.Z[j] - p.Z[i]) + p.X[i]) inside = !inside;
            return inside;
        }
    }
}
