using System;

namespace Game.Units.Mech.Qp
{
    /// <summary>
    /// Rigid-body model of the whole mech from the engine's dense link Jacobian (step 9.4 of T-005; the engine exposes no mass matrix).
    /// Engine-free, no allocation after construction, SI units, matrices row-major flat double[].
    /// Inputs per tick: Jacobian rows (6 per link: 3 linear then 3 angular, about the link's reference point <see cref="RefPos"/>, world frame), link mass,
    /// principal inertia (link frame), link rotation (world from link), link COM, sole point positions. Outputs on the sagittal column subset S:
    /// M_ss = sum_i Jc_i' diag(m_i I3, Iw_i) Jc_i, the point Jacobians (3 x S) and the centroidal momentum matrix A_G (6 x S, about the total COM).
    /// </summary>
    public sealed class WholeBodyModel
    {
        public readonly int MaxLinks, MaxS, MaxPoints;
        public int Links, S, Points;
        // inputs
        public readonly double[] Mass, InertiaDiag, Rot, Com, RefPos;
        public readonly int[] LinkRow, SagCols, PointLink;
        public readonly double[] PointPos;
        // outputs
        public readonly double[] Mss, Jp, AG, ComTotal;
        public double TotalMass;
        readonly double[] _jcl, _ja, _iwja;     // scratch, 3 x S each

        public WholeBodyModel(int maxLinks, int maxS, int maxPoints)
        {
            MaxLinks = maxLinks; MaxS = maxS; MaxPoints = maxPoints;
            Mass = new double[maxLinks]; InertiaDiag = new double[3 * maxLinks]; Rot = new double[9 * maxLinks]; Com = new double[3 * maxLinks]; RefPos = new double[3 * maxLinks];
            LinkRow = new int[maxLinks]; SagCols = new int[maxS]; PointLink = new int[maxPoints]; PointPos = new double[3 * maxPoints];
            Mss = new double[maxS * maxS]; Jp = new double[maxPoints * 3 * maxS]; AG = new double[6 * maxS]; ComTotal = new double[3];
            _jcl = new double[3 * maxS]; _ja = new double[3 * maxS]; _iwja = new double[3 * maxS];
        }

        /// <summary>Evaluates the outputs from the row-major Jacobian <paramref name="jac"/> with <paramref name="cols"/> columns.</summary>
        public void Compute(double[] jac, int cols)
        {
            int n = S, nl = Links;
            double mt = 0, cx = 0, cy = 0, cz = 0;
            for (int i = 0; i < nl; i++) { double m = Mass[i]; mt += m; cx += m * Com[3 * i]; cy += m * Com[3 * i + 1]; cz += m * Com[3 * i + 2]; }
            TotalMass = mt; double inv = mt > 0 ? 1.0 / mt : 0; cx *= inv; cy *= inv; cz *= inv;
            ComTotal[0] = cx; ComTotal[1] = cy; ComTotal[2] = cz;
            Array.Clear(Mss, 0, n * n); Array.Clear(AG, 0, 6 * n);

            for (int i = 0; i < nl; i++)
            {
                int r = LinkRow[i]; double m = Mass[i];
                double dx = Com[3 * i] - RefPos[3 * i], dy = Com[3 * i + 1] - RefPos[3 * i + 1], dz = Com[3 * i + 2] - RefPos[3 * i + 2];
                double ex = Com[3 * i] - cx, ey = Com[3 * i + 1] - cy, ez = Com[3 * i + 2] - cz;
                // world inertia Iw = R diag(I) R'
                int o = 9 * i; double i0 = InertiaDiag[3 * i], i1 = InertiaDiag[3 * i + 1], i2 = InertiaDiag[3 * i + 2];
                double w00 = Rot[o] * Rot[o] * i0 + Rot[o + 1] * Rot[o + 1] * i1 + Rot[o + 2] * Rot[o + 2] * i2;
                double w01 = Rot[o] * Rot[o + 3] * i0 + Rot[o + 1] * Rot[o + 4] * i1 + Rot[o + 2] * Rot[o + 5] * i2;
                double w02 = Rot[o] * Rot[o + 6] * i0 + Rot[o + 1] * Rot[o + 7] * i1 + Rot[o + 2] * Rot[o + 8] * i2;
                double w11 = Rot[o + 3] * Rot[o + 3] * i0 + Rot[o + 4] * Rot[o + 4] * i1 + Rot[o + 5] * Rot[o + 5] * i2;
                double w12 = Rot[o + 3] * Rot[o + 6] * i0 + Rot[o + 4] * Rot[o + 7] * i1 + Rot[o + 5] * Rot[o + 8] * i2;
                double w22 = Rot[o + 6] * Rot[o + 6] * i0 + Rot[o + 7] * Rot[o + 7] * i1 + Rot[o + 8] * Rot[o + 8] * i2;
                for (int s = 0; s < n; s++)
                {
                    int c = SagCols[s];
                    double l0 = jac[r * cols + c], l1 = jac[(r + 1) * cols + c], l2 = jac[(r + 2) * cols + c];
                    double a0 = jac[(r + 3) * cols + c], a1 = jac[(r + 4) * cols + c], a2 = jac[(r + 5) * cols + c];
                    // COM velocity = v_ref + w x (com - ref) = (J_lin - [d]x J_ang) qd
                    _jcl[s] = l0 - (dy * a2 - dz * a1); _jcl[n + s] = l1 - (dz * a0 - dx * a2); _jcl[2 * n + s] = l2 - (dx * a1 - dy * a0);
                    _ja[s] = a0; _ja[n + s] = a1; _ja[2 * n + s] = a2;
                    _iwja[s] = w00 * a0 + w01 * a1 + w02 * a2; _iwja[n + s] = w01 * a0 + w11 * a1 + w12 * a2; _iwja[2 * n + s] = w02 * a0 + w12 * a1 + w22 * a2;
                }
                for (int s = 0; s < n; s++)
                {
                    double jx = _jcl[s], jy = _jcl[n + s], jz = _jcl[2 * n + s];
                    AG[s] += m * jx; AG[n + s] += m * jy; AG[2 * n + s] += m * jz;
                    AG[3 * n + s] += m * (ey * jz - ez * jy) + _iwja[s];
                    AG[4 * n + s] += m * (ez * jx - ex * jz) + _iwja[n + s];
                    AG[5 * n + s] += m * (ex * jy - ey * jx) + _iwja[2 * n + s];
                }
                for (int s = 0; s < n; s++)
                    for (int t = s; t < n; t++)
                        Mss[s * n + t] += m * (_jcl[s] * _jcl[t] + _jcl[n + s] * _jcl[n + t] + _jcl[2 * n + s] * _jcl[2 * n + t])
                                        + _ja[s] * _iwja[t] + _ja[n + s] * _iwja[n + t] + _ja[2 * n + s] * _iwja[2 * n + t];
            }
            for (int s = 0; s < n; s++) for (int t = 0; t < s; t++) Mss[s * n + t] = Mss[t * n + s];

            // point Jacobians J_p = J_lin(ref) - [p - ref]x J_ang
            for (int p = 0; p < Points; p++)
            {
                int link = PointLink[p], r = LinkRow[link];
                double dx = PointPos[3 * p] - RefPos[3 * link], dy = PointPos[3 * p + 1] - RefPos[3 * link + 1], dz = PointPos[3 * p + 2] - RefPos[3 * link + 2];
                int b = p * 3 * n;
                for (int s = 0; s < n; s++)
                {
                    int c = SagCols[s];
                    double a0 = jac[(r + 3) * cols + c], a1 = jac[(r + 4) * cols + c], a2 = jac[(r + 5) * cols + c];
                    Jp[b + s] = jac[r * cols + c] - (dy * a2 - dz * a1);
                    Jp[b + n + s] = jac[(r + 1) * cols + c] - (dz * a0 - dx * a2);
                    Jp[b + 2 * n + s] = jac[(r + 2) * cols + c] - (dx * a1 - dy * a0);
                }
            }
        }

        /// <summary>Picks the sagittal entries (model columns <see cref="SagCols"/>) of a full-length vector, e.g. the bias force h.</summary>
        public void Gather(double[] full, double[] sag) { for (int s = 0; s < S; s++) sag[s] = full[SagCols[s]]; }
    }
}
