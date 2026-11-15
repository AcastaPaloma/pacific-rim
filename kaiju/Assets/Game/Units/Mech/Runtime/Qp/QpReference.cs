using System;

namespace Game.Units.Mech.Qp
{
    /// <summary>
    /// Periodic reference of the sagittal whole-body QP (port of controller_fid.py _build_ref / refc and planar_model.load_reference, sigma = 0): the 2N-node two-step cycle built from the @v2 node data of
    /// one step (second half = first half with the legs swapped and x advanced by one step length), cubic periodic splines per channel with the x drift removed, central-difference qd and qdd of q.
    /// Planar q9 = [hip x, hip z, th, a0 b0 f0 (leg A), a1 b1 f1 (leg B)]; points 0 A heel, 1 A toe, 2 B heel, 3 B toe. SI. Construction allocates, <see cref="At"/> does not.
    /// </summary>
    public sealed class QpReference
    {
        public const int Cq = 0, Cqd = 9, Cqdd = 18, Cf = 27, Ccom = 35, Ccomd = 37, Ccomdd = 39, Cl = 41, Cldot = 42, Cpts = 43, Cptv = 51, Cpta = 59, Channels = 67;
        static readonly int[] Sw = { 0, 1, 2, 6, 7, 8, 3, 4, 5 }, Sw4 = { 2, 3, 0, 1 };

        public readonly int N, N2;
        public readonly double H, Ts, T2, StepLength, BodyMassKg, LegLength, Mu, Speed;
        public readonly bool[] Flags;            // [node * 4 + point] scheduled loaded (raw fn > loadThr m g, optionally with the onset node of each loaded interval)
        public readonly double[] Tau0 = new double[6];   // reference joint torques at node 0 [hipA kneeA ankleA hipB kneeB ankleB], N m
        readonly double[] _node;                 // [ch * N2 + k] raw node values (drift included)
        readonly double[] _y, _m, _drift;        // detrended nodes, second derivatives, per-period drift per channel

        public QpReference(GaitRefRow row, double loadThr, bool onset)
        {
            _row = row; var nd = row.nodes; N = nd.Length; N2 = 2 * N; H = row.step_time_s / N; Ts = row.step_time_s; T2 = N2 * H; StepLength = row.step_length_m; BodyMassKg = row.body_mass_kg; LegLength = row.leg_length_m; Mu = row.mu; Speed = row.v;
            if (nd[0].q9 == null || nd[0].pts == null) throw new InvalidOperationException("gait reference row has no @v2 node data (q9, pts)");
            double d = StepLength, h = H; int n2 = N2;
            _node = new double[Channels * n2]; _y = new double[Channels * n2]; _m = new double[Channels * n2]; _drift = new double[Channels];
            var q = new double[9 * n2];     // [i * n2 + k]
            for (int k = 0; k < N; k++)
            {
                var nn = nd[k];
                for (int i = 0; i < 9; i++) { q[i * n2 + k] = nn.q9[i]; q[i * n2 + N + k] = nn.q9[Sw[i]] + (i == 0 ? d : 0.0); }
                for (int p = 0; p < 4; p++)
                    for (int c = 0; c < 2; c++)
                    {
                        _node[(Cf + 2 * p + c) * n2 + k] = nn.f4x2[2 * p + c]; _node[(Cf + 2 * p + c) * n2 + N + k] = nn.f4x2[2 * Sw4[p] + c];
                    }
                for (int c = 0; c < 2; c++)
                {
                    _node[(Ccom + c) * n2 + k] = nn.com[c]; _node[(Ccom + c) * n2 + N + k] = nn.com[c] + (c == 0 ? d : 0.0);
                    _node[(Ccomd + c) * n2 + k] = _node[(Ccomd + c) * n2 + N + k] = nn.comd[c];
                    _node[(Ccomdd + c) * n2 + k] = _node[(Ccomdd + c) * n2 + N + k] = nn.comdd[c];
                }
                _node[Cl * n2 + k] = _node[Cl * n2 + N + k] = nn.ang_mom; _node[Cldot * n2 + k] = _node[Cldot * n2 + N + k] = nn.ang_mom_dot;
            }
            // central differences of q on the periodic extension (as the optimiser and load_reference)
            for (int i = 0; i < 9; i++)
                for (int k = 0; k < n2; k++)
                {
                    double qp = k == n2 - 1 ? q[i * n2] + (i == 0 ? 2 * d : 0.0) : q[i * n2 + k + 1];
                    double qm = k == 0 ? q[i * n2 + n2 - 1] - (i == 0 ? 2 * d : 0.0) : q[i * n2 + k - 1];
                    double qk = q[i * n2 + k];
                    _node[(Cq + i) * n2 + k] = qk; _node[(Cqd + i) * n2 + k] = (qp - qm) / (2 * h); _node[(Cqdd + i) * n2 + k] = (qp - 2 * qk + qm) / (h * h);
                }
            _drift[Cq] = 2 * d; _drift[Ccom] = 2 * d;
            _lt = row.thigh_m; _ls = row.shank_m; _heel = row.heel_x_m; _toe = row.toe_x_m; _ha = row.ankle_height_m;
            if (!(_lt > 0 && _ls > 0 && _ha > 0)) throw new InvalidOperationException("gait reference @v2 row has no leg geometry (thigh_m, shank_m, heel_x_m, toe_x_m, ankle_height_m)");
            var tmp = new double[Channels];
            for (int k = 0; k < n2; k++) { for (int c = Cq; c < Cqdd + 9; c++) tmp[c] = _node[c * n2 + k]; PointKin(tmp, tmp); for (int c = Cpts; c < Cpta + 8; c++) _node[c * n2 + k] = tmp[c]; }
            BuildSplines();
            // schedule flags, as FIDWalker.flags (raw loaded, onset: the node before a loaded interval counts as loaded)
            Flags = new bool[n2 * 4]; double w = BodyMassKg * 9.81;
            for (int k = 0; k < n2; k++) for (int p = 0; p < 4; p++) Flags[k * 4 + p] = _node[(Cf + 2 * p + 1) * n2 + k] > loadThr * w;
            if (onset) { var f0 = (bool[])Flags.Clone(); for (int k = 0; k < n2; k++) for (int p = 0; p < 4; p++) Flags[k * 4 + p] = f0[k * 4 + p] | f0[((k + 1) % n2) * 4 + p]; }
            var s0 = nd[0].swing; var b0 = nd[0].stance;
            Tau0[0] = s0.hip_tau_nm; Tau0[1] = s0.knee_tau_nm; Tau0[2] = s0.ankle_tau_nm; Tau0[3] = b0.hip_tau_nm; Tau0[4] = b0.knee_tau_nm; Tau0[5] = b0.ankle_tau_nm;
        }

        readonly GaitRefRow _row;
        /// <summary>Reference joint torques [hipA kneeA ankleA hipB kneeB ankleB] at node k of the first step (step 9.5c: a state-matched start at node k).</summary>
        public void TauAt(int k, double[] o) { var n = _row.nodes[k]; o[0] = n.swing.hip_tau_nm; o[1] = n.swing.knee_tau_nm; o[2] = n.swing.ankle_tau_nm; o[3] = n.stance.hip_tau_nm; o[4] = n.stance.knee_tau_nm; o[5] = n.stance.ankle_tau_nm; }

        void BuildSplines()
        {
            int n = N2; double h = H;
            // periodic cubic spline: M[i-1] + 4 M[i] + M[i+1] = 6 / h^2 (y[i-1] - 2 y[i] + y[i+1]); invert the cyclic tridiagonal matrix once
            var a = new double[n * 2 * n];
            for (int i = 0; i < n; i++) { a[i * 2 * n + i] += 4; a[i * 2 * n + (i + 1) % n] += 1; a[i * 2 * n + (i + n - 1) % n] += 1; a[i * 2 * n + n + i] = 1; }
            for (int c = 0; c < n; c++)
            {
                int piv = c; for (int r = c + 1; r < n; r++) if (Math.Abs(a[r * 2 * n + c]) > Math.Abs(a[piv * 2 * n + c])) piv = r;
                if (piv != c) for (int j = 0; j < 2 * n; j++) { double t = a[c * 2 * n + j]; a[c * 2 * n + j] = a[piv * 2 * n + j]; a[piv * 2 * n + j] = t; }
                double inv = 1.0 / a[c * 2 * n + c]; for (int j = 0; j < 2 * n; j++) a[c * 2 * n + j] *= inv;
                for (int r = 0; r < n; r++) { if (r == c) continue; double f = a[r * 2 * n + c]; if (f == 0) continue; for (int j = 0; j < 2 * n; j++) a[r * 2 * n + j] -= f * a[c * 2 * n + j]; }
            }
            var rhs = new double[n];
            for (int ch = 0; ch < Channels; ch++)
            {
                for (int k = 0; k < n; k++) _y[ch * n + k] = _node[ch * n + k] - _drift[ch] * (k * h) / T2;
                for (int i = 0; i < n; i++) rhs[i] = 6.0 / (h * h) * (_y[ch * n + (i + n - 1) % n] - 2 * _y[ch * n + i] + _y[ch * n + (i + 1) % n]);
                for (int i = 0; i < n; i++) { double s = 0; for (int j = 0; j < n; j++) s += a[i * 2 * n + n + j] * rhs[j]; _m[ch * n + i] = s; }
            }
        }

        /// <summary>Reference channels at the cycle time <paramref name="tq"/> (any real, periodic with drift). The normal forces are clipped at 0. <paramref name="o"/> has length >= <see cref="Channels"/>.</summary>
        public void At(double tq, double[] o)
        {
            double nw = Math.Floor(tq / T2), tl = tq - nw * T2, fr = tl / T2 + nw; int n = N2; double h = H;
            int i = (int)(tl / h); if (i < 0) i = 0; if (i > n - 1) i = n - 1; int i1 = (i + 1) % n; double s = tl / h - i, r = 1 - s;
            double c0 = (r * r * r - r) * h * h / 6.0, c1 = (s * s * s - s) * h * h / 6.0;
            for (int ch = 0; ch < Channels; ch++)
            {
                int b = ch * n; o[ch] = r * _y[b + i] + s * _y[b + i1] + c0 * _m[b + i] + c1 * _m[b + i1] + _drift[ch] * fr;
            }
            for (int p = 0; p < 4; p++) if (o[Cf + 2 * p + 1] < 0) o[Cf + 2 * p + 1] = 0;
            PointKin(o, o);
        }

        readonly double _lt, _ls, _heel, _toe, _ha;
        /// <summary>Sole points of the planar model from q, qd, qdd channels of <paramref name="src"/> (position, velocity J qd, acceleration J qdd + Jdot qd) into <paramref name="dst"/>; same kinematics as planar_model.PlanarModel.kin.</summary>
        void PointKin(double[] src, double[] dst)
        {
            for (int leg = 0; leg < 2; leg++)
            {
                int ia = 3 + 3 * leg, ib = ia + 1, ifo = ia + 2;
                double a = src[Cq + ia], b = src[Cq + ib], f = src[Cq + ifo], ad = src[Cqd + ia], bd = src[Cqd + ib], fd = src[Cqd + ifo], add = src[Cqdd + ia], bdd = src[Cqdd + ib], fdd = src[Cqdd + ifo];
                double sa = Math.Sin(a), ca = Math.Cos(a), sb = Math.Sin(b), cb = Math.Cos(b), sf = Math.Sin(f), cf = Math.Cos(f);
                double dtx = sa, dtz = -ca, dsx = sb, dsz = -cb;       // thigh and shank directions (distal end), e = (cf, -sf) foot forward, n = (sf, cf) foot normal
                double ax = src[Cq] + _lt * dtx + _ls * dsx, az = src[Cq + 1] + _lt * dtz + _ls * dsz;
                for (int k = 0; k < 2; k++)
                {
                    double x = k == 0 ? _heel : _toe; int p = 2 * leg + k;
                    double px = ax + x * cf - _ha * sf, pz = az - x * sf - _ha * cf;
                    dst[Cpts + 2 * p] = px; dst[Cpts + 2 * p + 1] = pz;
                    // J columns: hip x, z; a: LT (ca, sa); b: LS (cb, sb); f: -x n - HA e
                    double jax = _lt * ca, jaz = _lt * sa, jbx = _ls * cb, jbz = _ls * sb, jfx = -x * sf - _ha * cf, jfz = -x * cf + _ha * sf;
                    dst[Cptv + 2 * p] = src[Cqd] + jax * ad + jbx * bd + jfx * fd; dst[Cptv + 2 * p + 1] = src[Cqd + 1] + jaz * ad + jbz * bd + jfz * fd;
                    double bx = -ad * ad * _lt * dtx - bd * bd * _ls * dsx - fd * fd * (x * cf - _ha * sf), bz = -ad * ad * _lt * dtz - bd * bd * _ls * dsz - fd * fd * (-x * sf - _ha * cf);
                    dst[Cpta + 2 * p] = src[Cqdd] + jax * add + jbx * bdd + jfx * fdd + bx; dst[Cpta + 2 * p + 1] = src[Cqdd + 1] + jaz * add + jbz * bdd + jfz * fdd + bz;
                }
            }
        }

        /// <summary>Raw reference x of a sole point at node <c>parity * N</c> (the start of the step in which that leg swings), drift included.</summary>
        public double NodePointX(int parity, int point) => _node[(Cpts + 2 * point) * N2 + parity * N];
        /// <summary>Raw node value of a channel.</summary>
        public double Node(int channel, int k) => _node[channel * N2 + k];
    }
}
