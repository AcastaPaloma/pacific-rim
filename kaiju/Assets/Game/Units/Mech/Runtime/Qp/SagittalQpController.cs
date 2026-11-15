using System;
using Game.Physics.Body.Qp;
using Game.Units.Mech.Contracts;

namespace Game.Units.Mech.Qp
{
    /// <summary>Parameters of <see cref="SagittalQpController"/>; defaults = the b5 preset of Tools/GaitOpt/qp_proto/run_fid.py (DEFAULT of controller_fid.py + kvs 5, onset 1, land 1, land_act 1, land_k0 35, fr_meas 1, fr_margin 0.9, mass_est 0.05, sg_floor 0.3).</summary>
    public sealed class SagittalQpOptions
    {
        public double Mu = 0.40, LimFrac = 0.98, G = 9.81;
        public double OmX = 0.8, OmZ = 2.0, KL = 2.0, OmSw = 4.0, OmTh = 3.0, OmPost = 3.0, OmFp = 4.0;
        public double WCx = 1e4, WCz = 1e4, WL = 1e3, WPrior = 1e-2, WSmooth = 1e-3, WSt = 1e6, WSwing = 1e3, WFp = 1e3, WTrunk = 1e4, WPost = 10.0, WReg = 1e-4, WBreg = 1e-6;
        public double Kvs = 5.0, OnBw = 0.03, OffBw = 0.01, TOut = 0.3, TIn = 0.08, TBlend = 0.2, LoadThr = 0.01, TdMinFrac = 0.6, LateVz = 2.0, FCap = 4.0, ZHold = 0.05;
        public bool HoldSched = true, Onset = true, Land = true, LandAct = true, FrMeas = true;
        public double FrMargin = 0.9, LandK0 = 35, VTd = 0.3, LandPost = 0.0;
        public double MassEst = 0.05, MassLo = 0.7, MassHi = 1.5, SgFloor = 0.3;
        public double SolverRegularisation = 1e-12;   // the prototype's solver has none; the default 1e-6 of the shared solver distorts the weak (1e-6..1e-4) weights
        /// <summary>Not ported (all off in b5): sigma smoothing, land_lead/xlead/vclip/nounder, om_land, sw_post, fmin_td, rate_bw, sg_fmin, predict.</summary>
        public static SagittalQpOptions From(WalkTuning t) => new SagittalQpOptions
        {
            Mu = t.qp_mu, LimFrac = t.qp_lim_frac, OmX = t.qp_om_x, OmZ = t.qp_om_z, KL = t.qp_kL, OmSw = t.qp_om_sw, OmTh = t.qp_om_th, OmPost = t.qp_om_post, OmFp = t.qp_om_fp,
            WCx = t.qp_w_cx, WCz = t.qp_w_cz, WL = t.qp_w_L, WPrior = t.qp_w_prior, WSmooth = t.qp_w_smooth, WSt = t.qp_w_st, WSwing = t.qp_w_swing, WFp = t.qp_w_fp, WTrunk = t.qp_w_trunk, WPost = t.qp_w_post, WReg = t.qp_w_reg, WBreg = t.qp_w_breg,
            Kvs = t.qp_kvs, OnBw = t.qp_on_bw, OffBw = t.qp_off_bw, TOut = t.qp_T_out, TIn = t.qp_T_in, TBlend = t.qp_T_blend, LoadThr = t.qp_load_thr, TdMinFrac = t.qp_td_min_frac, LateVz = t.qp_late_vz, FCap = t.qp_fcap, ZHold = t.qp_z_hold,
            HoldSched = t.qp_hold_sched > 0.5, Onset = t.qp_onset > 0.5, Land = t.qp_land > 0.5, LandAct = t.qp_land_act > 0.5, FrMeas = t.qp_fr_meas > 0.5, FrMargin = t.qp_fr_margin, LandK0 = t.qp_land_k0, VTd = t.qp_v_td, LandPost = t.qp_land_post,
            MassEst = t.qp_mass_est, MassLo = t.qp_mass_lo, MassHi = t.qp_mass_hi, SgFloor = t.qp_sg_floor, SolverRegularisation = t.qp_solver_reg
        };
    }

    /// <summary>
    /// Port of Tools/GaitOpt/qp_proto/controller_fid.py (the 9.3b-5 contact-aware single full inverse-dynamics QP, b5 options) to the engine-free core: one QP per tick over x = [qdd (9, scaled by D), beta (2 friction-pyramid generators
    /// per contact point)] with hard rows (3 unactuated base rows of M qdd + h = CJ' u + Jp' F, beta >= 0, fn <= fmax, 12 torque rows, per-foot shear rows) and weighted tasks (centroidal wrench, stance-point acceleration, swing heel, foot pitch,
    /// trunk, posture, force prior and smoothness, regularisers). Planar coordinates q9 = [hip x, hip z, th, (a b f) leg A, (a b f) leg B]; the caller (<see cref="WholeBodyQpDriver"/>) fills the planar model quantities.
    /// No allocation after construction. The solver is the injected <see cref="IQpSolver"/> (Goldfarb-Idnani, equalities as equality rows; the prototype eliminated them by a null-space basis, same optimum).
    /// </summary>
    public sealed class SagittalQpController
    {
        public const int NQ = 9, NJ = 6, MaxNb = 8, MaxNx = NQ + MaxNb, MaxIn = 40;
        static readonly int[] CjIdx = { 2, 3, 3, 4, 4, 5 };

        readonly QpReference _ref; readonly SagittalQpOptions _o; readonly IQpSolver _solver; readonly ContactSchedule _sch;
        readonly double _w0, _mt, _l, _g0, _h, _ts;
        readonly double[] _lim = new double[NJ], _cj = new double[NJ * NQ], _pinv = new double[NJ * NQ], _dd = new double[NQ], _n2 = new double[NQ];
        // inputs (planar, SI; filled by the caller before Step)
        public readonly double[] Q = new double[NQ], Qd = new double[NQ], M = new double[NQ * NQ], H = new double[NQ], Pts = new double[8], Jp = new double[4 * 2 * NQ], Bp = new double[8], Com = new double[2], ComD = new double[2], FnMeas = new double[4];
        public double AngMom;          // angular momentum about the CoM, counter-clockwise positive in the (x forward, z up) plane
        public bool HasFnMeas;         // false only on the first tick (no read-back yet)
        // outputs
        public readonly double[] Tau = new double[NJ];   // joint torques [hipA kneeA ankleA hipB kneeB ankleB], N m, clipped to the limits
        public readonly double[] Force = new double[8];  // commanded contact forces [ft, fn] per point, N
        // diagnostics
        public long Ticks, SolveFailures, TorqueRowsDropped, Held; public QpStatus LastStatus; public int LastIterations; public double FrictionRatioCmd, MassScale = 1.0; public int Parity => _sch.Parity; public int Touchdowns => _sch.TouchdownCount;
        public ContactSchedule Schedule => _sch;
        /// <summary>Normal-force cap per point (body weights) of the last tick.</summary>
        public double[] FmaxBw => _fmax;
        /// <summary>Previous-tick contact forces [ft, fn] per point, N (force smoothness term); for tests that set up a state.</summary>
        public void SetPreviousForces(double[] f8) => Array.Copy(f8, _fprev, 8);
        // state
        readonly double[] _fprev = new double[8], _fmax = new double[4], _fn = new double[4], _pz = new double[4], _px = new double[4], _tdRef = new double[4], _r = new double[QpReference.Channels];
        readonly int[] _sIdx = new int[4]; bool _hasComd; double _comdPrevZ, _tPrev;
        double _planAnchor = double.NaN; int _planPar = -1;
        // landing quintics per parity: coefficients [c * 6 + k] for channels x, z, pitch; start tau, duration, end state (p1, v1)
        readonly double[][] _lq = { new double[18], new double[18] }; readonly double[] _lqTau0 = new double[2], _lqTl = new double[2]; readonly double[][] _lqP1 = { new double[3], new double[3] }, _lqV1 = { new double[3], new double[3] };
        readonly double[] _ltPos = new double[3], _ltVel = new double[3], _ltAcc = new double[3];
        // QP scratch
        readonly double[] _hq = new double[MaxNx * MaxNx], _gq = new double[MaxNx], _row = new double[MaxNx], _aeq = new double[3 * MaxNx], _beq = new double[3], _ain = new double[MaxIn * MaxNx], _bin = new double[MaxIn], _ain2 = new double[MaxIn * MaxNx], _bin2 = new double[MaxIn];
        readonly double[] _gm = new double[3 * MaxNb], _em = new double[8 * MaxNb], _jb = new double[NQ * MaxNb], _qx = new double[NQ * MaxNx], _ms = new double[NQ * NQ], _hs = new double[NQ], _qdd = new double[NQ], _qq = new double[NQ], _uu = new double[NJ];
        readonly double[] _wd = new double[3], _vp = new double[8];

        public SagittalQpController(QpReference reference, SagittalQpOptions options, double[] torqueLimitsNm, IQpSolver solver)
        {
            _ref = reference; _o = options; _solver = solver; solver.Regularisation = options.SolverRegularisation; _g0 = options.G; _l = reference.LegLength; _h = reference.H; _ts = reference.Ts;
            _mt = reference.BodyMassKg; _w0 = _mt * _g0;
            Array.Copy(torqueLimitsNm, _lim, NJ);
            for (int i = 0; i < 2; i++)
            {
                int a = 3 + 3 * i, b = 4 + 3 * i, f = 5 + 3 * i, r = 3 * i;
                _cj[r * NQ + 2] = 1; _cj[r * NQ + a] = 1; _cj[(r + 1) * NQ + a] = 1; _cj[(r + 1) * NQ + b] = -1; _cj[(r + 2) * NQ + b] = 1; _cj[(r + 2) * NQ + f] = 1;
            }
            // pinv(CJ') = (CJ CJ')^-1 CJ ; null space of CJ: base x, base z, and (th, -1 -1 1, -1 -1 1)/sqrt 7
            var g6 = new double[NJ * 2 * NJ];
            for (int i = 0; i < NJ; i++) { for (int j = 0; j < NJ; j++) { double s = 0; for (int c = 0; c < NQ; c++) s += _cj[i * NQ + c] * _cj[j * NQ + c]; g6[i * 2 * NJ + j] = s; } g6[i * 2 * NJ + NJ + i] = 1; }
            for (int c = 0; c < NJ; c++)
            {
                int piv = c; for (int r = c + 1; r < NJ; r++) if (Math.Abs(g6[r * 2 * NJ + c]) > Math.Abs(g6[piv * 2 * NJ + c])) piv = r;
                if (piv != c) for (int j = 0; j < 2 * NJ; j++) { double t = g6[c * 2 * NJ + j]; g6[c * 2 * NJ + j] = g6[piv * 2 * NJ + j]; g6[piv * 2 * NJ + j] = t; }
                double inv = 1.0 / g6[c * 2 * NJ + c]; for (int j = 0; j < 2 * NJ; j++) g6[c * 2 * NJ + j] *= inv;
                for (int r = 0; r < NJ; r++) { if (r == c) continue; double f = g6[r * 2 * NJ + c]; for (int j = 0; j < 2 * NJ; j++) g6[r * 2 * NJ + j] -= f * g6[c * 2 * NJ + j]; }
            }
            for (int i = 0; i < NJ; i++) for (int c = 0; c < NQ; c++) { double s = 0; for (int k = 0; k < NJ; k++) s += g6[i * 2 * NJ + NJ + k] * _cj[k * NQ + c]; _pinv[i * NQ + c] = s; }
            double rt7 = 1.0 / Math.Sqrt(7.0); _n2[2] = rt7; for (int i = 0; i < 2; i++) { _n2[3 + 3 * i] = -rt7; _n2[4 + 3 * i] = -rt7; _n2[5 + 3 * i] = rt7; }
            _dd[0] = _dd[1] = _g0; for (int j = 2; j < NQ; j++) _dd[j] = _g0 / _l;
            var sopt = new ContactScheduleOptions { OnBw = options.OnBw, OffBw = options.OffBw, TOut = options.TOut, TIn = options.TIn, TBlend = options.TBlend, TdMinFrac = options.TdMinFrac, LateVz = options.LateVz, FCap = options.FCap, ZHold = options.ZHold, HoldSched = options.HoldSched, MissedAfterS = 0.5 };
            _sch = new ContactSchedule(reference.Flags, reference.N2, _h, _ts, sopt);
            if (options.Land) { BuildLand(0); BuildLand(1); }
            Init();
        }

        /// <summary>The prototype's k &lt; 0 call: start at reference node 0 with its contact state; Tau = the reference torques there.</summary>
        public void Init()
        {
            _sch.Reset(0);
            for (int i = 0; i < 4; i++) { _sch.Inc[i] = _ref.Flags[i]; _sch.FIn[i] = _ref.Node(QpReference.Cf + 2 * i + 1, 0); _sch.TIn[i] = -1.0; }
            Array.Clear(_fprev, 0, 8); MassScale = 1.0; _hasComd = false; _planAnchor = double.NaN; _planPar = -1;
            Array.Copy(_ref.Tau0, Tau, NJ);
        }

        // ------------------------------------------------------------------ landing quintic
        static void Solve3(double t, double b0, double b1, double b2, out double c3, out double c4, out double c5)
        {
            double t2 = t * t, t3 = t2 * t, t4 = t3 * t, t5 = t4 * t;
            double a00 = t3, a01 = t4, a02 = t5, a10 = 3 * t2, a11 = 4 * t3, a12 = 5 * t4, a20 = 6 * t, a21 = 12 * t2, a22 = 20 * t3;
            double det = a00 * (a11 * a22 - a12 * a21) - a01 * (a10 * a22 - a12 * a20) + a02 * (a10 * a21 - a11 * a20);
            c3 = (b0 * (a11 * a22 - a12 * a21) - a01 * (b1 * a22 - a12 * b2) + a02 * (b1 * a21 - a11 * b2)) / det;
            c4 = (a00 * (b1 * a22 - a12 * b2) - b0 * (a10 * a22 - a12 * a20) + a02 * (a10 * b2 - b1 * a20)) / det;
            c5 = (a00 * (a11 * b2 - b1 * a21) - a01 * (a10 * b2 - b1 * a20) + b0 * (a10 * a21 - a11 * a20)) / det;
        }

        void BuildLand(int par)
        {
            double tau0 = _o.LandK0 * _h, tl = _ts - tau0; int fi = 5 + 3 * par, i = 2 * par; var r0 = new double[QpReference.Channels]; var r1 = new double[QpReference.Channels]; var r1b = new double[QpReference.Channels];
            _ref.At(par * _ts + tau0, r0); _ref.At((par + 1) * _ts, r1); _ref.At((par + 1) * _ts + _h, r1b);
            double[] p0 = { r0[QpReference.Cpts + 2 * i], r0[QpReference.Cpts + 2 * i + 1], r0[QpReference.Cq + fi] };
            double[] v0 = { r0[QpReference.Cptv + 2 * i], r0[QpReference.Cptv + 2 * i + 1], r0[QpReference.Cqd + fi] };
            double[] a0 = { r0[QpReference.Cpta + 2 * i], r0[QpReference.Cpta + 2 * i + 1], r0[QpReference.Cqdd + fi] };
            double[] p1 = { r1[QpReference.Cpts + 2 * i], 0.0, r1[QpReference.Cq + fi] }; double[] v1 = { 0.0, -_o.VTd, (r1b[QpReference.Cq + fi] - r1[QpReference.Cq + fi]) / _h };
            var co = _lq[par];
            for (int c = 0; c < 3; c++)
            {
                Solve3(tl, p1[c] - (p0[c] + v0[c] * tl + 0.5 * a0[c] * tl * tl), v1[c] - (v0[c] + a0[c] * tl), 0.0 - a0[c], out double c3, out double c4, out double c5);
                co[c * 6] = p0[c]; co[c * 6 + 1] = v0[c]; co[c * 6 + 2] = 0.5 * a0[c]; co[c * 6 + 3] = c3; co[c * 6 + 4] = c4; co[c * 6 + 5] = c5;
            }
            _lqTau0[par] = tau0; _lqTl[par] = tl; for (int c = 0; c < 3; c++) { _lqP1[par][c] = p1[c]; _lqV1[par][c] = v1[c]; }
        }

        /// <summary>land_act: quintic from the measured state (x0, v0) at tau to the landing state of the reference (+ Xoff), ending at the step time; the heel x of the stored coefficients excludes Xoff.</summary>
        void Replan(int par, double tau, double x0x, double x0z, double x0f, double v0x, double v0z, double v0f, double xoff)
        {
            double t = Math.Max(_ts - tau, 2 * _h); var co = _lq[par]; var p1 = _lqP1[par]; var v1 = _lqV1[par];
            for (int c = 0; c < 3; c++)
            {
                double x0 = c == 0 ? x0x : c == 1 ? x0z : x0f, v0 = c == 0 ? v0x : c == 1 ? v0z : v0f, pe = p1[c] + (c == 0 ? xoff : 0.0);
                Solve3(t, pe - (x0 + v0 * t), v1[c] - v0, 0.0, out double c3, out double c4, out double c5);
                co[c * 6] = x0; co[c * 6 + 1] = v0; co[c * 6 + 2] = 0.0; co[c * 6 + 3] = c3; co[c * 6 + 4] = c4; co[c * 6 + 5] = c5;
            }
            co[0] -= xoff; _lqTau0[par] = tau; _lqTl[par] = t; _planAnchor = _sch.TAnchor; _planPar = _sch.Parity;
        }

        void LandTarget(int par, double tau)
        {
            double t0 = _lqTau0[par], tl = _lqTl[par], s = Math.Min(Math.Max(tau - t0, 0.0), tl), ex = Math.Max(0.0, tau - t0 - tl); var co = _lq[par];
            for (int c = 0; c < 3; c++)
            {
                int b = c * 6; double c0 = co[b], c1 = co[b + 1], c2 = co[b + 2], c3 = co[b + 3], c4 = co[b + 4], c5 = co[b + 5];
                _ltPos[c] = c0 + s * (c1 + s * (c2 + s * (c3 + s * (c4 + s * c5))));
                _ltVel[c] = c1 + s * (2 * c2 + s * (3 * c3 + s * (4 * c4 + s * 5 * c5)));
                _ltAcc[c] = 2 * c2 + s * (6 * c3 + s * (12 * c4 + s * 20 * c5));
            }
            if (ex > 0) { _ltPos[1] -= _o.VTd * ex; _ltVel[0] = 0; _ltVel[1] = -_o.VTd; _ltVel[2] = 0; _ltAcc[0] = _ltAcc[1] = _ltAcc[2] = 0; }
        }

        // ------------------------------------------------------------------ QP assembly helpers
        void AddRow(int nx, double w, double y)
        {
            if (w <= 0) return;
            for (int i = 0; i < nx; i++)
            {
                double ti = _row[i]; if (ti == 0) continue; double wt = w * ti; _gq[i] -= wt * y; int o = i * nx;
                for (int j = 0; j < nx; j++) _hq[o + j] += wt * _row[j];
            }
        }
        void ClearRow(int nx) { for (int i = 0; i < nx; i++) _row[i] = 0; }

        /// <summary>One control tick at clock time <paramref name="t"/> (s): fills <see cref="Tau"/> and returns true; on an infeasible QP the previous torques stay and false is returned.</summary>
        public bool Step(double t)
        {
            var o = _o; Ticks++; double g0 = _g0, l = _l, h = _h, ts = _ts;
            // online mass-scale estimate from the last tick's read-back
            if (o.MassEst > 0)
            {
                if (HasFnMeas && _hasComd && t > _tPrev + 1e-9)
                {
                    double den = g0 + (ComD[1] - _comdPrevZ) / (t - _tPrev), ftot = FnMeas[0] + FnMeas[1] + FnMeas[2] + FnMeas[3];
                    if (den > 0.5 * g0 && ftot > 0.4 * _w0) MassScale = Math.Min(o.MassHi, Math.Max(o.MassLo, MassScale + o.MassEst * (ftot / (_mt * den) - MassScale)));
                }
                _comdPrevZ = ComD[1]; _tPrev = t; _hasComd = true;
            }
            double ms = MassScale, W = _w0 * ms;
            for (int i = 0; i < NQ * NQ; i++) _ms[i] = M[i] * ms; for (int i = 0; i < NQ; i++) _hs[i] = H[i] * ms;
            for (int i = 0; i < 4; i++) { _vp[2 * i] = 0; _vp[2 * i + 1] = 0; for (int j = 0; j < NQ; j++) { _vp[2 * i] += Jp[i * 18 + j] * Qd[j]; _vp[2 * i + 1] += Jp[i * 18 + 9 + j] * Qd[j]; } _fn[i] = FnMeas[i]; _pz[i] = Pts[2 * i + 1]; _px[i] = Pts[2 * i]; }
            // measured contact, touchdown / phase
            _sch.UpdateContact(t, W, _fn, _pz);
            int newPar = 1 - _sch.Parity; for (int p = 0; p < 4; p++) _tdRef[p] = _ref.NodePointX(newPar, p);
            _sch.TryTouchdown(t, _px, _tdRef);
            int swLeg = _sch.Parity, stLeg = 1 - swLeg; double tau = t - _sch.TAnchor, late = _sch.Late(t), tref = _sch.RefTime(t), xoff = _sch.Xoff;
            _ref.At(tref, _r); var r = _r;
            // contact set and force caps
            int nS = 0; for (int i = 0; i < 4; i++) if (_sch.Inc[i]) _sIdx[nS++] = i;
            int nb = 2 * nS, nx = NQ + nb;
            for (int i = 0; i < 4; i++) _fmax[i] = 0;
            for (int a = 0; a < nS; a++) { int i = _sIdx[a]; _fmax[i] = _sch.Fmax(i, t, tref, W); }
            bool sgOn = o.SgFloor > 0 && !(_sch.Inc[2 * swLeg] || _sch.Inc[2 * swLeg + 1]);
            if (sgOn) for (int a = 0; a < nS; a++) { int i = _sIdx[a]; if (i / 2 == stLeg) _fmax[i] = Math.Max(_fmax[i], Math.Min(o.SgFloor, _sch.FIn[i] / W + (t - _sch.TIn[i]) / o.TIn)); }
            // generators: Gm (3 x nb), Em (2 nS x nb), Jb (9 x nb)
            Array.Clear(_gm, 0, _gm.Length); Array.Clear(_em, 0, _em.Length); Array.Clear(_jb, 0, _jb.Length);
            double cx = Com[0], cz = Com[1];
            for (int a = 0; a < nS; a++)
            {
                int i = _sIdx[a];
                for (int b = 0; b < 2; b++)
                {
                    int col = 2 * a + b; double gx = (b == 0 ? 1.0 : -1.0) * o.Mu * o.FrMargin, gz = 1.0;
                    _gm[col] = gx; _gm[MaxNb + col] = gz; _gm[2 * MaxNb + col] = ((Pts[2 * i] - cx) * gz - (Pts[2 * i + 1] - cz) * gx) / l;
                    _em[(2 * a) * MaxNb + col] = gx; _em[(2 * a + 1) * MaxNb + col] = gz;
                    for (int j = 0; j < NQ; j++) _jb[j * MaxNb + col] = W * (Jp[i * 18 + j] * gx + Jp[i * 18 + 9 + j] * gz);
                }
            }
            // ---- quadratic cost
            Array.Clear(_hq, 0, nx * nx); Array.Clear(_gq, 0, nx);
            double cdx = r[QpReference.Ccomdd] + o.OmX * o.OmX * (r[QpReference.Ccom] + xoff - cx) + 2 * o.OmX * (r[QpReference.Ccomd] - ComD[0]);
            double cdz = r[QpReference.Ccomdd + 1] + o.OmZ * o.OmZ * (r[QpReference.Ccom + 1] - cz) + 2 * o.OmZ * (r[QpReference.Ccomd + 1] - ComD[1]);
            double lm = AngMom * ms, ldd = r[QpReference.Cldot] + o.KL * (r[QpReference.Cl] - lm);
            _wd[0] = _mt * ms * cdx / W; _wd[1] = _mt * ms * (cdz + g0) / W; _wd[2] = ldd / (W * l);
            if (nb > 0)
            {
                for (int k = 0; k < 3; k++) { ClearRow(nx); for (int c = 0; c < nb; c++) _row[NQ + c] = _gm[k * MaxNb + c]; AddRow(nx, k == 0 ? o.WCx : k == 1 ? o.WCz : o.WL, _wd[k]); }
                for (int k = 0; k < 2 * nS; k++)
                {
                    int i = _sIdx[k / 2], d = k % 2; ClearRow(nx); for (int c = 0; c < nb; c++) _row[NQ + c] = _em[k * MaxNb + c];
                    AddRow(nx, o.WPrior, r[QpReference.Cf + 2 * i + d] / W);
                    AddRow(nx, o.WSmooth, _fprev[2 * i + d] / W);
                }
                for (int c = 0; c < nb; c++) _hq[(NQ + c) * nx + NQ + c] += o.WBreg;
            }
            // stance points: acceleration -kvs v
            for (int a = 0; a < nS; a++)
            {
                int i = _sIdx[a];
                for (int d = 0; d < 2; d++)
                {
                    ClearRow(nx); for (int j = 0; j < NQ; j++) _row[j] = Jp[i * 18 + d * 9 + j] * (j < 2 ? 1.0 : 1.0 / l);
                    AddRow(nx, o.WSt, (-o.Kvs * _vp[2 * i + d] - Bp[2 * i + d]) / g0);
                }
            }
            // feet
            double kp = o.OmSw * o.OmSw, kd = 2 * o.OmSw, kf = o.OmFp * o.OmFp, kfd = 2 * o.OmFp; bool landOn = false, haveLt = false;
            for (int leg = 0; leg < 2; leg++)
            {
                int ns = (_sch.Inc[2 * leg] ? 1 : 0) + (_sch.Inc[2 * leg + 1] ? 1 : 0); if (ns == 2) continue;
                int fidx = 5 + 3 * leg; double bl = 1.0; haveLt = false;
                if (ns == 0)
                {
                    int i = 2 * leg; bl = _sch.SwingBlend(leg, t);
                    double prx = r[QpReference.Cpts + 2 * i] + xoff, prz = r[QpReference.Cpts + 2 * i + 1], vrx = r[QpReference.Cptv + 2 * i], vrz = r[QpReference.Cptv + 2 * i + 1], arx = r[QpReference.Cpta + 2 * i], arz = r[QpReference.Cpta + 2 * i + 1];
                    if (leg == swLeg && o.Land && o.LandAct && tau >= o.LandK0 * h && !(_planAnchor == _sch.TAnchor && _planPar == _sch.Parity))
                        Replan(_sch.Parity, tau, Pts[2 * i], Pts[2 * i + 1], Q[fidx], _vp[2 * i], _vp[2 * i + 1], Qd[fidx], xoff);
                    if (leg == swLeg && o.Land && tau >= _lqTau0[_sch.Parity])
                    {
                        LandTarget(_sch.Parity, tau); haveLt = true;
                        prx = _ltPos[0] + xoff; prz = _ltPos[1]; vrx = _ltVel[0]; vrz = _ltVel[1]; arx = _ltAcc[0]; arz = _ltAcc[1];
                    }
                    if (leg == swLeg && late > 0) { prz -= o.LateVz * late; vrz -= o.LateVz; }
                    double adx = arx + kp * (prx - Pts[2 * i]) + kd * (vrx - _vp[2 * i]), adz = arz + kp * (prz - Pts[2 * i + 1]) + kd * (vrz - _vp[2 * i + 1]);
                    for (int d = 0; d < 2; d++)
                    {
                        ClearRow(nx); for (int j = 0; j < NQ; j++) _row[j] = Jp[i * 18 + d * 9 + j] * (j < 2 ? 1.0 : 1.0 / l);
                        AddRow(nx, o.WSwing * bl, ((d == 0 ? adx : adz) - Bp[2 * i + d]) / g0);
                    }
                }
                double af = r[QpReference.Cqdd + fidx] + kf * (r[QpReference.Cq + fidx] - Q[fidx]) + kfd * (r[QpReference.Cqd + fidx] - Qd[fidx]);
                if (ns == 0 && leg == swLeg && haveLt) { af = _ltAcc[2] + kf * (_ltPos[2] - Q[fidx]) + kfd * (_ltVel[2] - Qd[fidx]); landOn = true; }
                ClearRow(nx); _row[fidx] = 1.0; AddRow(nx, o.WFp * bl, af / (g0 / l));
            }
            // trunk
            { double ath = r[QpReference.Cqdd + 2] + o.OmTh * o.OmTh * (r[QpReference.Cq + 2] - Q[2]) + 2 * o.OmTh * (r[QpReference.Cqd + 2] - Qd[2]); ClearRow(nx); _row[2] = 1.0; AddRow(nx, o.WTrunk, ath / (g0 / l)); }
            // posture (joint space)
            double kq = o.OmPost * o.OmPost, kqd = 2 * o.OmPost;
            for (int j = 0; j < NJ; j++)
            {
                double ddq = 0, dq = 0, dqd = 0, qr = 0, qdr = 0;
                for (int c = 0; c < NQ; c++) { double cj = _cj[j * NQ + c]; if (cj == 0) continue; ddq += cj * r[QpReference.Cqdd + c]; qr += cj * r[QpReference.Cq + c]; qdr += cj * r[QpReference.Cqd + c]; dq += cj * Q[c]; dqd += cj * Qd[c]; }
                double aj = ddq + kq * (qr - dq) + kqd * (qdr - dqd), wp = o.WPost; if (landOn && j / 3 == swLeg) wp *= o.LandPost;
                ClearRow(nx); for (int c = 0; c < NQ; c++) _row[c] = _cj[j * NQ + c] * (c < 2 ? l : 1.0); AddRow(nx, wp, aj / (g0 / l));
            }
            for (int j = 0; j < NQ; j++) _hq[j * nx + j] += o.WReg;
            // ---- hard rows. Qx = [Ms D, -Jb] (9 x nx), Q = Qx x + hs
            for (int i = 0; i < NQ; i++)
            {
                for (int j = 0; j < NQ; j++) _qx[i * nx + j] = _ms[i * NQ + j] * _dd[j];
                for (int c = 0; c < nb; c++) _qx[i * nx + NQ + c] = -_jb[i * MaxNb + c];
            }
            // equalities: Nm' Q = 0 (rows normalised)
            for (int k = 0; k < 3; k++)
            {
                double sc = 0, e = 0;
                for (int c = 0; c < nx; c++)
                {
                    double v = k == 0 ? _qx[c] : k == 1 ? _qx[nx + c] : 0; if (k == 2) for (int i = 2; i < NQ; i++) v += _n2[i] * _qx[i * nx + c];
                    _aeq[k * nx + c] = v; sc += v * v;
                }
                e = k == 0 ? -_hs[0] : k == 1 ? -_hs[1] : 0; if (k == 2) { for (int i = 2; i < NQ; i++) e -= _n2[i] * _hs[i]; }
                sc = Math.Sqrt(sc); if (sc < 1e-300) sc = 1.0;
                for (int c = 0; c < nx; c++) _aeq[k * nx + c] /= sc; _beq[k] = e / sc;
            }
            // inequalities: 12 torque rows, beta >= 0, fn <= fmax, per-foot shear
            int mi = 0;
            for (int j = 0; j < NJ; j++)
            {
                double cu = 0; for (int i = 0; i < NQ; i++) cu += _pinv[j * NQ + i] * _hs[i]; cu /= _lim[j];
                for (int c = 0; c < nx; c++) { double au = 0; for (int i = 0; i < NQ; i++) au += _pinv[j * NQ + i] * _qx[i * nx + c]; au /= _lim[j]; _ain[j * nx + c] = -au; _ain[(NJ + j) * nx + c] = au; }
                _bin[j] = cu - o.LimFrac; _bin[NJ + j] = -o.LimFrac - cu;
            }
            mi = 2 * NJ;
            for (int c = 0; c < nb; c++) { Array.Clear(_ain, mi * nx, nx); _ain[mi * nx + NQ + c] = 1.0; _bin[mi] = 0.0; mi++; }
            for (int a = 0; a < nS; a++) { Array.Clear(_ain, mi * nx, nx); for (int c = 0; c < nb; c++) _ain[mi * nx + NQ + c] = -_em[(2 * a + 1) * MaxNb + c]; _bin[mi] = -_fmax[_sIdx[a]]; mi++; }
            if (o.FrMeas)
                for (int f = 0; f < 2; f++)
                {
                    bool any = false; for (int a = 0; a < nS; a++) if (_sIdx[a] / 2 == f) any = true; if (!any) continue;
                    double cap = Math.Max(o.Mu * o.FrMargin * (_fn[2 * f] + _fn[2 * f + 1]) / W, 1e-3);
                    Array.Clear(_ain, mi * nx, 2 * nx);
                    for (int a = 0; a < nS; a++) if (_sIdx[a] / 2 == f) for (int c = 0; c < nb; c++) { double tv = _em[(2 * a) * MaxNb + c]; _ain[mi * nx + NQ + c] -= tv; _ain[(mi + 1) * nx + NQ + c] += tv; }
                    _bin[mi] = -cap; _bin[mi + 1] = -cap; mi += 2;
                }
            // ---- solve (drop the torque rows if infeasible, as the prototype), keep the previous torques if that fails too
            var st = _solver.Solve(nx, 3, mi, _hq, _gq, _aeq, _beq, _ain, _bin); LastIterations = _solver.Iterations;
            if (st != QpStatus.Optimal)
            {
                Array.Copy(_ain, 2 * NJ * nx, _ain2, 0, (mi - 2 * NJ) * nx); Array.Copy(_bin, 2 * NJ, _bin2, 0, mi - 2 * NJ);
                st = _solver.Solve(nx, 3, mi - 2 * NJ, _hq, _gq, _aeq, _beq, _ain2, _bin2); TorqueRowsDropped++;
            }
            LastStatus = st;
            if (st != QpStatus.Optimal) { SolveFailures++; Held++; return false; }
            var x = _solver.X; for (int i = 0; i < NQ; i++) _qdd[i] = _dd[i] * x[i];
            Array.Clear(Force, 0, 8);
            for (int a = 0; a < nS; a++)
            {
                int i = _sIdx[a]; double ft = 0, fz = 0;
                for (int c = 0; c < nb; c++) { ft += _em[(2 * a) * MaxNb + c] * x[NQ + c]; fz += _em[(2 * a + 1) * MaxNb + c] * x[NQ + c]; }
                Force[2 * i] = W * ft; Force[2 * i + 1] = W * fz;
            }
            for (int i = 0; i < NQ; i++)
            {
                double s = _hs[i]; for (int j = 0; j < NQ; j++) s += _ms[i * NQ + j] * _qdd[j];
                for (int p = 0; p < 4; p++) s -= Jp[p * 18 + i] * Force[2 * p] + Jp[p * 18 + 9 + i] * Force[2 * p + 1];
                _qq[i] = s;
            }
            bool ok = true;
            for (int j = 0; j < NJ; j++)
            {
                double u = 0; for (int i = 0; i < NQ; i++) u += _pinv[j * NQ + i] * _qq[i];
                if (double.IsNaN(u) || double.IsInfinity(u)) { ok = false; break; }
                _uu[j] = Math.Min(_lim[j], Math.Max(-_lim[j], u));
            }
            if (!ok) { SolveFailures++; Held++; return false; }
            Array.Copy(_uu, Tau, NJ); Array.Copy(Force, _fprev, 8);
            double fr = 0; for (int i = 0; i < 4; i++) if (Force[2 * i + 1] > 1e-6 * W) fr = Math.Max(fr, Math.Abs(Force[2 * i]) / (o.Mu * Force[2 * i + 1]));
            FrictionRatioCmd = fr;
            return true;
        }
    }
}
