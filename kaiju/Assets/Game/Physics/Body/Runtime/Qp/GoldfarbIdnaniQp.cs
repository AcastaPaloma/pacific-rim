using System;

namespace Game.Physics.Body.Qp
{
    /// <summary>
    /// Dense dual active-set QP solver (Goldfarb-Idnani, Powell's J/R formulation):
    /// minimise 0.5 x'Hx + g'x  s.t.  Aeq x = beq,  Ain x >= bin.
    /// Engine-free, no allocation after construction. Matrices are row-major flat double[].
    /// H gets a Tikhonov term regularisation * mean(diag H) (default 1e-6, relative).
    /// Constraint numbering for active sets and multipliers: equalities 0..meq-1, inequalities meq..meq+min-1.
    /// </summary>
    public sealed class GoldfarbIdnaniQp : IQpSolver
    {
        public readonly int MaxN, MaxEq, MaxIn, IterationCap;
        public double Regularisation;          // relative Tikhonov term; settable (ill-conditioned weighted QPs set it to ~0 through IQpSolver)
        public double FeasTol = 1e-10;

        // results
        public readonly double[] X;
        public readonly double[] Lambda;      // per constraint (sign as the original row), inequalities >= 0
        public readonly int[] ActiveSet;      // combined constraint indices
        public int ActiveCount;
        public int Iterations;
        public double Objective;
        public QpStatus Status;
        public long FailureCount;             // Infeasible / IterationLimit / NotPositiveDefinite solves

        readonly double[] _l, _jInit, _j, _r, _x0, _d, _z, _rv, _w, _nv, _u, _bact, _nact;
        readonly int[] _act, _warmTmp;
        readonly double[] _sgn;
        readonly bool[] _isActive;
        int _n, _meq, _min, _q;
        double[] _aeq, _beq, _ain, _bin;
        int _cholN = -1;

        double[] IQpSolver.X => X;
        double[] IQpSolver.Lambda => Lambda;
        QpStatus IQpSolver.Status => Status;
        int IQpSolver.Iterations => Iterations;
        long IQpSolver.FailureCount => FailureCount;
        double IQpSolver.Regularisation { get => Regularisation; set { Regularisation = value; _cholN = -1; } }

        public GoldfarbIdnaniQp(int maxN, int maxEq, int maxIn, int iterationCap = 60, double regularisation = 1e-6)
        {
            MaxN = maxN; MaxEq = maxEq; MaxIn = maxIn; IterationCap = iterationCap; Regularisation = regularisation;
            int nn = maxN * maxN;
            _l = new double[nn]; _jInit = new double[nn]; _j = new double[nn]; _r = new double[nn];
            _x0 = new double[maxN]; _d = new double[maxN]; _z = new double[maxN]; _rv = new double[maxN + 1];
            _w = new double[maxN]; _nv = new double[maxN]; _u = new double[maxN + 1];
            _bact = new double[maxN + 1]; _nact = new double[(maxN + 1) * maxN];
            _act = new int[maxN + 1]; _sgn = new double[maxN + 1]; _warmTmp = new int[maxN + 1];
            _isActive = new bool[maxEq + maxIn];
            X = new double[maxN]; Lambda = new double[maxEq + maxIn]; ActiveSet = new int[maxN + 1];
        }

        /// <param name="hUnchanged">true: reuse the Cholesky factor of the previous call (same n, same H).</param>
        /// <param name="warm">previous ActiveSet (combined indices) or null for a cold start; re-validated.</param>
        public QpStatus Solve(int n, int meq, int min, double[] H, double[] g, double[] Aeq, double[] beq, double[] Ain, double[] bin,
                              bool hUnchanged = false, int[] warm = null, int warmCount = 0)
        {
            if (n > MaxN || meq > MaxEq || min > MaxIn) throw new ArgumentException("QP exceeds preallocated size");
            _n = n; _meq = meq; _min = min; _aeq = Aeq; _beq = beq; _ain = Ain; _bin = bin;
            Iterations = 0; _q = 0; ActiveCount = 0;
            int m = meq + min;
            for (int k = 0; k < m; k++) { _isActive[k] = false; Lambda[k] = 0; }

            if (!(hUnchanged && _cholN == n))
            {
                if (!Factor(n, H)) { _cholN = -1; return Finish(QpStatus.NotPositiveDefinite); }
                _cholN = n;
            }
            Array.Copy(_jInit, _j, n * n);
            // x0 = -J J' g
            for (int j = 0; j < n; j++) { double s = 0; for (int i = 0; i < n; i++) s += _j[i * n + j] * g[i]; _w[j] = s; }
            for (int i = 0; i < n; i++) { double s = 0; for (int j = 0; j < n; j++) s += _j[i * n + j] * _w[j]; _x0[i] = -s; }

            // active set seed: all equalities, then warm inequalities
            int nw = 0;
            if (warm != null) { nw = warmCount < _warmTmp.Length ? warmCount : _warmTmp.Length; for (int i = 0; i < nw; i++) _warmTmp[i] = warm[i]; }
            for (int k = 0; k < meq; k++) TryAdd(k, 1.0);
            for (int i = 0; i < nw; i++) { int k = _warmTmp[i]; if (k >= meq && k < m && !_isActive[k]) TryAdd(k, 1.0); }
            SolveActive();
            if (nw > 0)
            {
                // re-validate: drop inequalities whose multiplier went negative
                while (true)
                {
                    int worst = -1; double wv = -1e-12;
                    for (int i = 0; i < _q; i++) if (_act[i] >= meq && _u[i] < wv) { wv = _u[i]; worst = i; }
                    if (worst < 0) break;
                    if (Iterations >= IterationCap) return Finish(QpStatus.IterationLimit);
                    Iterations++;
                    Drop(worst);
                    SolveActive();
                }
            }
            else
            {
                // cold: equalities-only solution has free multipliers, nothing to validate
            }
            // current x lives in X
            QpStatus st = MainLoop(m);
            if (st == QpStatus.Optimal) { SolveActive(); ComputeObjective(n, H, g); }
            return Finish(st);
        }

        QpStatus Finish(QpStatus st)
        {
            Status = st;
            ActiveCount = _q;
            for (int i = 0; i < _q; i++) { ActiveSet[i] = _act[i]; Lambda[_act[i]] = _sgn[i] * _u[i]; }
            if (st != QpStatus.Optimal) { FailureCount++; return st; }
            return st;
        }

        /// <summary>Objective 0.5 x'Hx + g'x of the last solution (original H).</summary>
        public double ComputeObjective(int n, double[] H, double[] g)
        {
            double f = 0;
            for (int i = 0; i < n; i++) { double s = 0; for (int j = 0; j < n; j++) s += H[i * n + j] * X[j]; f += X[i] * (0.5 * s + g[i]); }
            Objective = f; return f;
        }

        bool Factor(int n, double[] H)
        {
            double tr = 0; for (int i = 0; i < n; i++) tr += H[i * n + i];
            double reg = Regularisation * (tr > 0 ? tr / n : 1.0);
            // Cholesky H + reg I = L L'
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j <= i; j++)
                {
                    double s = H[i * n + j]; if (i == j) s += reg;
                    for (int k = 0; k < j; k++) s -= _l[i * n + k] * _l[j * n + k];
                    if (i == j) { if (!(s > 1e-300)) return false; _l[i * n + i] = Math.Sqrt(s); }
                    else _l[i * n + j] = s / _l[j * n + j];
                }
                for (int j = i + 1; j < n; j++) _l[i * n + j] = 0;
            }
            // J = L^-T : compute Linv by forward substitution, J[i][j] = Linv[j][i]
            for (int c = 0; c < n; c++)
            {
                for (int i = 0; i < n; i++)
                {
                    double s = i == c ? 1.0 : 0.0;
                    for (int k = c; k < i; k++) s -= _l[i * n + k] * _w[k];
                    if (i < c) { _w[i] = 0; continue; }
                    _w[i] = s / _l[i * n + i];
                }
                for (int i = 0; i < n; i++) _jInit[c * n + i] = _w[i]; // J[c][i] = Linv[i][c]
            }
            return true;
        }

        // n+ = sign * row k; returns b+
        double LoadNormal(int k, double sign)
        {
            int n = _n; double b;
            if (k < _meq) { int o = k * n; for (int i = 0; i < n; i++) _nv[i] = sign * _aeq[o + i]; b = sign * _beq[k]; }
            else { int o = (k - _meq) * n; for (int i = 0; i < n; i++) _nv[i] = sign * _ain[o + i]; b = sign * _bin[k - _meq]; }
            return b;
        }

        void ComputeD()
        {
            int n = _n;
            for (int j = 0; j < n; j++) { double s = 0; for (int i = 0; i < n; i++) s += _j[i * n + j] * _nv[i]; _d[j] = s; }
        }

        // rotate d (and J columns) so that d[q+1..] = 0
        void RotateD()
        {
            int n = _n, q = _q;
            for (int j = n - 1; j > q; j--)
            {
                double a = _d[j - 1], b = _d[j];
                if (b == 0) continue;
                double h = Math.Sqrt(a * a + b * b), c = a / h, s = b / h;
                _d[j - 1] = h; _d[j] = 0;
                for (int i = 0; i < n; i++)
                {
                    double x = _j[i * n + j - 1], y = _j[i * n + j];
                    _j[i * n + j - 1] = c * x + s * y;
                    _j[i * n + j] = -s * x + c * y;
                }
            }
        }

        bool TryAdd(int k, double sign)
        {
            int n = _n, q = _q;
            if (q >= n) return false;
            double b = LoadNormal(k, sign);
            ComputeD();
            double dd = 0; for (int i = 0; i < n; i++) dd += _d[i] * _d[i];
            RotateD();
            if (Math.Abs(_d[q]) <= 1e-9 * Math.Sqrt(dd)) return false; // linearly dependent on the active rows
            for (int i = 0; i <= q; i++) _r[i * n + q] = _d[i];
            Array.Copy(_nv, 0, _nact, q * n, n);
            _bact[q] = b; _act[q] = k; _sgn[q] = sign; _isActive[k] = true; _q = q + 1;
            return true;
        }

        void Drop(int l)
        {
            int n = _n, q = _q;
            _isActive[_act[l]] = false;
            for (int i = 0; i < q; i++) for (int j = l; j < q - 1; j++) _r[i * n + j] = _r[i * n + j + 1];
            for (int i = l; i < q - 1; i++)
            {
                _act[i] = _act[i + 1]; _sgn[i] = _sgn[i + 1]; _bact[i] = _bact[i + 1]; _u[i] = _u[i + 1];
                Array.Copy(_nact, (i + 1) * n, _nact, i * n, n);
            }
            _q = q - 1;
            for (int i = l; i < q - 1; i++)
            {
                double a = _r[i * n + i], b = _r[(i + 1) * n + i];
                double h = Math.Sqrt(a * a + b * b);
                if (h == 0) continue;
                double c = a / h, s = b / h;
                for (int j = i; j < q - 1; j++)
                {
                    double x = _r[i * n + j], y = _r[(i + 1) * n + j];
                    _r[i * n + j] = c * x + s * y;
                    _r[(i + 1) * n + j] = -s * x + c * y;
                }
                _r[(i + 1) * n + i] = 0;
                for (int row = 0; row < n; row++)
                {
                    double x = _j[row * n + i], y = _j[row * n + i + 1];
                    _j[row * n + i] = c * x + s * y;
                    _j[row * n + i + 1] = -s * x + c * y;
                }
            }
        }

        /// <summary>X and multipliers u for the minimiser with the current active rows as equalities.</summary>
        void SolveActive()
        {
            int n = _n, q = _q;
            for (int k = 0; k < q; k++)
            {
                double s = _bact[k]; int o = k * n;
                for (int i = 0; i < n; i++) s -= _nact[o + i] * _x0[i];
                for (int i = 0; i < k; i++) s -= _r[i * n + k] * _w[i];
                _w[k] = s / _r[k * n + k];
            }
            for (int i = 0; i < n; i++)
            {
                double s = _x0[i];
                for (int k = 0; k < q; k++) s += _j[i * n + k] * _w[k];
                X[i] = s;
            }
            for (int k = q - 1; k >= 0; k--)
            {
                double s = _w[k];
                for (int j = k + 1; j < q; j++) s -= _r[k * n + j] * _u[j];
                _u[k] = s / _r[k * n + k];
            }
        }

        QpStatus MainLoop(int m)
        {
            int n = _n, meq = _meq;
            while (true)
            {
                // most violated inactive constraint
                int p = -1; double best = FeasTol; double psign = 1.0;
                for (int k = 0; k < m; k++)
                {
                    if (_isActive[k]) continue;
                    double s, sc;
                    if (k < meq) { int o = k * n; s = -_beq[k]; sc = 1 + Math.Abs(_beq[k]); for (int i = 0; i < n; i++) { double tt = _aeq[o + i] * X[i]; s += tt; sc += Math.Abs(tt); } }
                    else { int o = (k - meq) * n; s = -_bin[k - meq]; sc = 1 + Math.Abs(_bin[k - meq]); for (int i = 0; i < n; i++) { double tt = _ain[o + i] * X[i]; s += tt; sc += Math.Abs(tt); } }
                    double v; double sg = 1.0;
                    if (k < meq) { v = Math.Abs(s) / sc; if (s > 0) sg = -1.0; } else v = -s / sc;
                    if (v > best) { best = v; p = k; psign = sg; }
                }
                if (p < 0) return QpStatus.Optimal;
                if (Iterations >= IterationCap) return QpStatus.IterationLimit;

                LoadNormal(p, psign);
                double up = 0;
                while (true)
                {
                    if (Iterations >= IterationCap) return QpStatus.IterationLimit;
                    Iterations++;
                    int q = _q;
                    ComputeD();
                    double dd = 0, d2 = 0;
                    for (int i = 0; i < n; i++) { double v = _d[i] * _d[i]; dd += v; if (i >= q) d2 += v; }
                    // z = J2 d2
                    for (int i = 0; i < n; i++) { double s = 0; for (int j = q; j < n; j++) s += _j[i * n + j] * _d[j]; _z[i] = s; }
                    // r = R^-1 d1
                    for (int k = q - 1; k >= 0; k--)
                    {
                        double s = _d[k];
                        for (int j = k + 1; j < q; j++) s -= _r[k * n + j] * _rv[j];
                        _rv[k] = s / _r[k * n + k];
                    }
                    // current slack of p
                    double sp = -LoadBPlus(p, psign);
                    for (int i = 0; i < n; i++) sp += _nv[i] * X[i];
                    double t1 = double.PositiveInfinity; int l = -1;
                    for (int k = 0; k < q; k++)
                        if (_act[k] >= meq && _rv[k] > 0) { double tq = _u[k] / _rv[k]; if (tq < t1) { t1 = tq; l = k; } }
                    double t2 = double.PositiveInfinity;
                    bool zeroZ = d2 <= 1e-20 * dd;
                    if (!zeroZ) t2 = -sp / d2;
                    if (t2 < 0) t2 = 0;
                    if (double.IsPositiveInfinity(t1) && double.IsPositiveInfinity(t2)) return QpStatus.Infeasible;
                    if (zeroZ)
                    {
                        for (int k = 0; k < q; k++) _u[k] -= t1 * _rv[k];
                        up += t1;
                        _u[l] = 0; Drop(l);
                        continue;
                    }
                    double t = t2 < t1 ? t2 : t1;
                    for (int i = 0; i < n; i++) X[i] += t * _z[i];
                    for (int k = 0; k < q; k++) _u[k] -= t * _rv[k];
                    up += t;
                    if (t2 <= t1)
                    {
                        if (!TryAdd(p, psign)) return QpStatus.Infeasible;
                        _u[_q - 1] = up;
                        break;
                    }
                    _u[l] = 0; Drop(l);
                }
            }
        }

        double LoadBPlus(int k, double sign) => k < _meq ? sign * _beq[k] : sign * _bin[k - _meq];
    }
}
