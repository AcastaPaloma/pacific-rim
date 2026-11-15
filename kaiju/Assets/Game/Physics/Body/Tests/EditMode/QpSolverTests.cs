using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Game.Physics.Body.Qp;
using NUnit.Framework;

namespace Game.Physics.Body.Tests
{
    /// <summary>Tests of GoldfarbIdnaniQp against scipy references (Tools/GaitOpt/qp_reference.py -> out/qp_reference/cases.json) and KKT certificates.</summary>
    public class QpSolverTests
    {
        sealed class Qp
        {
            public string Name, Status; public int N, Meq, Min; public double XTol;
            public double[] H, G, Aeq, Beq, Ain, Bin, X; public double Obj;
        }

        // ---- minimal JSON reader (no engine/System.Text.Json dependency) ----
        sealed class Json
        {
            readonly string _s; int _i;
            Json(string s) { _s = s; }
            public static object Parse(string s) { var j = new Json(s); return j.Value(); }
            void Ws() { while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++; }
            object Value()
            {
                Ws(); char c = _s[_i];
                if (c == '{')
                {
                    _i++; var d = new Dictionary<string, object>(); Ws();
                    if (_s[_i] == '}') { _i++; return d; }
                    while (true) { Ws(); string k = Str(); Ws(); _i++; d[k] = Value(); Ws(); if (_s[_i++] == '}') return d; }
                }
                if (c == '[')
                {
                    _i++; var l = new List<object>(); Ws();
                    if (_s[_i] == ']') { _i++; return l; }
                    while (true) { l.Add(Value()); Ws(); if (_s[_i++] == ']') return l; }
                }
                if (c == '"') return Str();
                int st = _i; while (_i < _s.Length && ",]} \n\r\t".IndexOf(_s[_i]) < 0) _i++;
                string t = _s.Substring(st, _i - st);
                if (t == "null") return null;
                return double.Parse(t, CultureInfo.InvariantCulture);
            }
            string Str() { _i++; int st = _i; while (_s[_i] != '"') _i++; return _s.Substring(st, _i++ - st); }
        }

        static double[] Arr(object o) { var l = (List<object>)o; var a = new double[l.Count]; for (int i = 0; i < a.Length; i++) a[i] = (double)l[i]; return a; }

        static string RepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 14 && dir != null; i++, dir = Path.GetDirectoryName(dir)) if (Directory.Exists(Path.Combine(dir, "DotnetTests"))) return dir;
            dir = Directory.GetCurrentDirectory();
            for (int i = 0; i < 14 && dir != null; i++, dir = Path.GetDirectoryName(dir)) if (Directory.Exists(Path.Combine(dir, "DotnetTests"))) return dir;
            throw new DirectoryNotFoundException("repo root (folder containing DotnetTests) not found");
        }

        static List<Qp> _cases;
        static List<Qp> Cases()
        {
            if (_cases != null) return _cases;
            var root = (Dictionary<string, object>)Json.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "Tools", "GaitOpt", "out", "qp_reference", "cases.json")));
            var res = new List<Qp>();
            foreach (var o in (List<object>)root["cases"])
            {
                var d = (Dictionary<string, object>)o;
                var q = new Qp { Name = (string)d["name"], Status = (string)d["status"], N = (int)(double)d["n"], Meq = (int)(double)d["meq"], Min = (int)(double)d["min"], XTol = (double)d["xtol"],
                    H = Arr(d["H"]), G = Arr(d["g"]), Aeq = Arr(d["Aeq"]), Beq = Arr(d["beq"]), Ain = Arr(d["Ain"]), Bin = Arr(d["bin"]) };
                if (d.ContainsKey("x")) { q.X = Arr(d["x"]); q.Obj = (double)d["obj"]; }
                res.Add(q);
            }
            return _cases = res;
        }

        public static IEnumerable<string> OptimalNames() { foreach (var c in Cases()) if (c.Status == "optimal") yield return c.Name; }
        public static IEnumerable<string> InfeasibleNames() { foreach (var c in Cases()) if (c.Status == "infeasible") yield return c.Name; }
        static Qp Get(string name) { foreach (var c in Cases()) if (c.Name == name) return c; throw new KeyNotFoundException(name); }

        static QpStatus Run(GoldfarbIdnaniQp s, Qp q, bool hUnchanged = false, int[] warm = null, int warmCount = 0) =>
            s.Solve(q.N, q.Meq, q.Min, q.H, q.G, q.Aeq, q.Beq, q.Ain, q.Bin, hUnchanged, warm, warmCount);

        /// <summary>KKT certificate for H x + g = Aeq' le + Ain' li; reg adds reg*mean(diag H) to H.</summary>
        static void CheckKkt(int n, int meq, int min, double[] H, double reg, double[] g, double[] Aeq, double[] beq, double[] Ain, double[] bin, double[] x, double[] lam, string tag)
        {
            double tr = 0; for (int i = 0; i < n; i++) tr += H[i * n + i];
            double rg = reg * (tr > 0 ? tr / n : 1.0);
            double gs = 1; for (int i = 0; i < n; i++) gs = Math.Max(gs, Math.Abs(g[i]));
            double xs = 1; for (int i = 0; i < n; i++) xs = Math.Max(xs, Math.Abs(x[i]));
            double hs = 1; for (int i = 0; i < H.Length; i++) hs = Math.Max(hs, Math.Abs(H[i]));
            double ls = 1; for (int k = 0; k < meq + min; k++) ls = Math.Max(ls, Math.Abs(lam[k]));
            double scale = gs + hs * xs;
            for (int i = 0; i < n; i++)
            {
                double r = g[i] + rg * x[i];
                for (int j = 0; j < n; j++) r += H[i * n + j] * x[j];
                for (int k = 0; k < meq; k++) r -= Aeq[k * n + i] * lam[k];
                for (int k = 0; k < min; k++) r -= Ain[k * n + i] * lam[meq + k];
                Assert.LessOrEqual(Math.Abs(r), 1e-8 * (scale + ls * 10), $"{tag}: stationarity row {i}");
            }
            for (int k = 0; k < meq; k++)
            {
                double s = -beq[k], sc = 1 + Math.Abs(beq[k]);
                for (int i = 0; i < n; i++) { s += Aeq[k * n + i] * x[i]; sc += Math.Abs(Aeq[k * n + i] * x[i]); }
                Assert.LessOrEqual(Math.Abs(s), 1e-8 * sc, $"{tag}: equality {k}");
            }
            for (int k = 0; k < min; k++)
            {
                double s = -bin[k], sc = 1 + Math.Abs(bin[k]);
                for (int i = 0; i < n; i++) { s += Ain[k * n + i] * x[i]; sc += Math.Abs(Ain[k * n + i] * x[i]); }
                Assert.GreaterOrEqual(s, -1e-8 * sc, $"{tag}: inequality {k} feasibility");
                double l = lam[meq + k];
                Assert.GreaterOrEqual(l, -1e-9 * ls, $"{tag}: multiplier {k} sign");
                Assert.LessOrEqual(Math.Abs(l * s), 1e-8 * ls * sc, $"{tag}: complementarity {k}");
            }
        }

        static void CheckKkt(Qp q, GoldfarbIdnaniQp s, double reg, string tag) =>
            CheckKkt(q.N, q.Meq, q.Min, q.H, reg, q.G, q.Aeq, q.Beq, q.Ain, q.Bin, s.X, s.Lambda, tag);

        static double MaxRelDiff(double[] a, double[] b, int n)
        {
            double sc = 1, d = 0; for (int i = 0; i < n; i++) { sc = Math.Max(sc, Math.Abs(b[i])); d = Math.Max(d, Math.Abs(a[i] - b[i])); }
            return d / sc;
        }

        // ---------------------------------------------------------------- scipy comparison
        public static readonly List<string> Report = new List<string>();

        [TestCaseSource(nameof(OptimalNames))]
        public void MatchesScipyAndKkt(string name)
        {
            var q = Get(name);
            var s = new GoldfarbIdnaniQp(40, 12, 80, iterationCap: 400, regularisation: 0.0);
            Assert.AreEqual(QpStatus.Optimal, Run(s, q), name);
            Assert.LessOrEqual(MaxRelDiff(s.X, q.X, q.N), q.XTol, name + " x vs scipy");
            Assert.LessOrEqual(Math.Abs(s.Objective - q.Obj), 1e-8 * (1 + Math.Abs(q.Obj)), name + " objective");
            CheckKkt(q, s, 0.0, name);
            TestContext.Out.WriteLine($"{name}: n={q.N} meq={q.Meq} min={q.Min} cold iterations={s.Iterations} active={s.ActiveCount}");
            if (s.Iterations > 60) TestContext.Out.WriteLine($"  NOTE {name} needed {s.Iterations} > 60 iterations");
        }

        [TestCaseSource(nameof(OptimalNames))]
        public void DefaultCapAndRegularisationSolveEveryReferenceCase(string name)
        {
            var q = Get(name);
            var s = new GoldfarbIdnaniQp(40, 12, 80); // cap 60, reg 1e-6
            var st = Run(s, q);
            Assert.AreEqual(QpStatus.Optimal, st, name + $" (iterations {s.Iterations})");
            // 1e-6 relative Tikhonov moves the optimum by about reg * cond; the objective stays within 1e-5 relative
            Assert.LessOrEqual(Math.Abs(s.Objective - q.Obj), 1e-5 * (1 + Math.Abs(q.Obj)), name + " objective");
            CheckKkt(q, s, 1e-6, name);
        }

        [TestCaseSource(nameof(InfeasibleNames))]
        public void DetectsInfeasible(string name)
        {
            var q = Get(name);
            var s = new GoldfarbIdnaniQp(40, 12, 80, iterationCap: 400, regularisation: 0.0);
            Assert.AreEqual(QpStatus.Infeasible, Run(s, q), name);
            Assert.AreEqual(1, s.FailureCount);
        }

        [TestCaseSource(nameof(OptimalNames))]
        public void WarmStartSameOptimumFewerIterations(string name)
        {
            var q = Get(name);
            var s = new GoldfarbIdnaniQp(40, 12, 80, iterationCap: 400, regularisation: 0.0);
            Assert.AreEqual(QpStatus.Optimal, Run(s, q));
            int cold = s.Iterations; var x = (double[])s.X.Clone(); var act = (int[])s.ActiveSet.Clone(); int na = s.ActiveCount;
            Assert.AreEqual(QpStatus.Optimal, Run(s, q, true, act, na));
            Assert.LessOrEqual(s.Iterations, cold, name + " warm iterations");
            Assert.LessOrEqual(MaxRelDiff(s.X, x, q.N), 1e-9, name + " warm x");
            TestContext.Out.WriteLine($"{name}: cold {cold} warm {s.Iterations}");
        }

        // ---------------------------------------------------------------- random C# problems
        sealed class Rnd
        {
            readonly Random _r; public Rnd(int seed) { _r = new Random(seed); }
            public double U() => _r.NextDouble();
            public double N() { double u = 1 - _r.NextDouble(), v = _r.NextDouble(); return Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * v); }
            public int I(int lo, int hi) => _r.Next(lo, hi + 1);
        }

        static Qp RandomQp(Rnd r, int n, int meq, int min, bool bounds = false)
        {
            var q = new Qp { N = n, Meq = meq, Min = min };
            var L = new double[n * n]; for (int i = 0; i < L.Length; i++) L[i] = r.N() / Math.Sqrt(n);
            q.H = new double[n * n];
            for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) { double s = i == j ? 0.5 : 0; for (int k = 0; k < n; k++) s += L[i * n + k] * L[j * n + k]; q.H[i * n + j] = s; }
            q.G = new double[n]; for (int i = 0; i < n; i++) q.G[i] = 3 * r.N();
            var xf = new double[n]; for (int i = 0; i < n; i++) xf[i] = bounds ? r.U() + 0.05 : r.N();
            q.Aeq = new double[meq * n]; q.Beq = new double[meq];
            for (int k = 0; k < meq; k++) { double b = 0; for (int i = 0; i < n; i++) { q.Aeq[k * n + i] = r.N(); b += q.Aeq[k * n + i] * xf[i]; } q.Beq[k] = b; }
            q.Ain = new double[min * n]; q.Bin = new double[min];
            for (int k = 0; k < min; k++)
            {
                double b = 0;
                for (int i = 0; i < n; i++) { q.Ain[k * n + i] = bounds ? (i == k ? 1 : 0) : r.N(); b += q.Ain[k * n + i] * xf[i]; }
                q.Bin[k] = bounds ? 0.0 : b - (r.U() < 0.8 ? r.U() : 0.0);
            }
            return q;
        }

        [Test]
        public void RandomQpsPassKktCertificate()
        {
            var r = new Rnd(1234);
            var s = new GoldfarbIdnaniQp(40, 14, 80, iterationCap: 400, regularisation: 0.0);
            int maxIt = 0;
            for (int t = 0; t < 300; t++)
            {
                int n = r.I(5, 40), m = r.I(0, 80), me = r.I(0, Math.Min(14, n / 2));
                var q = RandomQp(r, n, me, m);
                Assert.AreEqual(QpStatus.Optimal, Run(s, q), $"random {t} n={n} m={m} me={me}");
                CheckKkt(q, s, 0.0, $"random {t}");
                maxIt = Math.Max(maxIt, s.Iterations);
                // warm restart gives the same x
                var x = (double[])s.X.Clone(); int cold = s.Iterations;
                Assert.AreEqual(QpStatus.Optimal, Run(s, q, true, (int[])s.ActiveSet.Clone(), s.ActiveCount));
                Assert.LessOrEqual(MaxRelDiff(s.X, x, n), 1e-9);
                Assert.LessOrEqual(s.Iterations, cold);
            }
            TestContext.Out.WriteLine($"300 random QPs: max cold iterations {maxIt}");
        }

        [TestCase(16)]
        [TestCase(32)]
        public void Stage1ShapeRandomWrenches(int n)
        {
            var r = new Rnd(77 + n);
            var s = new GoldfarbIdnaniQp(40, 12, 80, iterationCap: 60);
            int maxIt = 0;
            for (int t = 0; t < 200; t++)
            {
                var q = RandomQp(r, n, 6, n, bounds: true);
                Assert.AreEqual(QpStatus.Optimal, Run(s, q), $"stage1 {t}");
                CheckKkt(q, s, 1e-6, $"stage1 {t}");
                maxIt = Math.Max(maxIt, s.Iterations);
            }
            TestContext.Out.WriteLine($"stage-1 n={n}: max cold iterations {maxIt} (cap 60)");
        }

        [Test]
        public void IterationLimitIsReportedAndCounted()
        {
            var q = RandomQp(new Rnd(5), 30, 0, 60);
            var s = new GoldfarbIdnaniQp(40, 12, 80, iterationCap: 2, regularisation: 0.0);
            Assert.AreEqual(QpStatus.IterationLimit, Run(s, q));
            Assert.AreEqual(1, s.FailureCount);
        }

        // ---------------------------------------------------------------- allocation and timing
        [Test]
        public void NoAllocationAfterFirstCall()
        {
            var r = new Rnd(9);
            var qs = new[] { RandomQp(r, 32, 6, 32, true), RandomQp(r, 20, 12, 56), RandomQp(r, 16, 6, 16, true) };
            var s = new GoldfarbIdnaniQp(40, 12, 80);
            foreach (var q in qs) Run(s, q); // first calls
            var act = new int[41]; int na;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int rep = 0; rep < 50; rep++)
                foreach (var q in qs)
                {
                    Run(s, q);
                    na = s.ActiveCount; Array.Copy(s.ActiveSet, act, na);
                    Run(s, q, rep % 2 == 0, act, na);
                }
            long delta = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, delta, "bytes allocated by Solve after the first call");
        }

        [TestCase(16, 6, 16, true, "stage-1 single support")]
        [TestCase(32, 6, 32, true, "stage-1 double support")]
        [TestCase(20, 12, 56, false, "stage-2")]
        public void Timing(int n, int meq, int min, bool bounds, string label)
        {
            var r = new Rnd(31 + n);
            // a sequence of slowly varying QPs sharing the structure (H fixed, g and the wrench drift) as in a control loop
            var q = RandomQp(r, n, meq, min, bounds);
            var s = new GoldfarbIdnaniQp(40, 12, 80);
            var act = new int[41]; int na = 0; bool have = false;
            const int N = 1000; var t = new double[N]; long totalIt = 0; int fails = 0;
            double tick = 1.0 / Stopwatch.Frequency * 1e6;
            for (int w = 0; w < 20; w++) { Run(s, q, have, have ? act : null, na); }
            for (int k = 0; k < N; k++)
            {
                for (int i = 0; i < n; i++) q.G[i] += 0.01 * r.N();
                long t0 = Stopwatch.GetTimestamp();
                var st = Run(s, q, have, have ? act : null, na);
                long t1 = Stopwatch.GetTimestamp();
                t[k] = (t1 - t0) * tick;
                if (st == QpStatus.Optimal) { na = s.ActiveCount; Array.Copy(s.ActiveSet, act, na); have = true; totalIt += s.Iterations; } else { fails++; have = false; }
            }
            Array.Sort(t);
            double p50 = t[N / 2], p99 = t[(int)(N * 0.99)];
            TestContext.Out.WriteLine($"TIMING {label} n={n} meq={meq} min={min}: p50 {p50:F1} us, p99 {p99:F1} us, max {t[N - 1]:F1} us, mean warm iterations {(double)totalIt / N:F2}, failures {fails}");
            Assert.AreEqual(0, fails);
#if !DEBUG
            Assert.Less(p99, 5000.0, "p99 ceiling 5 ms (Release)");
#endif
        }
    }
}
