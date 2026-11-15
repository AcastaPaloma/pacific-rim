"""Dense Goldfarb-Idnani dual active-set QP (numpy port, explicit-inverse form; n <= ~20).
    min 0.5 x'G x + g'x   s.t.  C[:meq] x = d[:meq],  C[meq:] x >= d[meq:]      (G symmetric positive definite)
solve(G, g, C, d, meq=0) -> (x, status, iters, lam)   status in {"ok", "infeasible", "maxit"}
Self-test against scipy: python gi_qp.py   (20 random QPs, equality + inequality)."""
import numpy as np


def solve(G, g, C=None, d=None, meq=0, tol=1e-10, maxit=400):
    n = len(g)
    if C is None or len(C) == 0:
        x = -np.linalg.solve(G, g); return x, "ok", 0, np.zeros(0)
    C = np.asarray(C, float); d = np.asarray(d, float); m = len(d)
    Gi = np.linalg.inv(G); x = -Gi @ g
    A = []; sg = []; u = []                      # active constraint indices, sign flips, duals
    pend = list(range(meq)); it = 0
    while True:
        it += 1
        if it > maxit: return x, "maxit", it, _lam(A, u, m, sg)
        s = C @ x - d
        if pend: p = pend.pop(0)
        else:
            cand = [i for i in range(meq, m) if i not in A]
            if not cand: break
            p = min(cand, key=lambda i: s[i])
            if s[p] >= -tol * (1 + abs(d[p])): break
        sp_sign = -1.0 if (p < meq and s[p] > 0) else 1.0
        nvec = sp_sign * C[p]; sp = sp_sign * s[p]; up = 0.0
        while True:
            it += 1
            if it > maxit: return x, "maxit", it, _lam(A, u, m, sg)
            q = len(A)
            if q:
                N = np.stack([sg[j] * C[A[j]] for j in range(q)], 1)
                GiN = Gi @ N
                try: Ns = np.linalg.solve(N.T @ GiN, GiN.T)
                except np.linalg.LinAlgError: return x, "infeasible", it, _lam(A, u, m, sg)
                H = Gi - GiN @ Ns; r = Ns @ nvec
            else: H = Gi; r = np.zeros(0)
            z = H @ nvec
            t1 = np.inf; l = -1
            for j in range(q):
                if A[j] >= meq and r[j] > 1e-12:
                    rt = u[j] / r[j]
                    if rt < t1: t1, l = rt, j
            zn = float(z @ nvec); degenerate = np.linalg.norm(z) < 1e-12 * (1 + np.linalg.norm(nvec)) or zn <= 1e-14
            if degenerate:
                if l < 0: return x, "infeasible", it, _lam(A, u, m, sg)
                t = t1; t2 = np.inf
            else:
                t2 = -sp / zn; t = min(t1, t2)
                if t < 0: t = 0.0
                x = x + t * z
            for j in range(q): u[j] -= t * r[j]
            up += t
            if not degenerate and t2 <= t1:
                A.append(p); sg.append(sp_sign); u.append(up); break
            # partial step: drop constraint l and continue
            del A[l]; del sg[l]; del u[l]
            sp = sp_sign * (C[p] @ x - d[p])
    return x, "ok", it, _lam(A, u, m, sg)


def _lam(A, u, m, sg):
    lam = np.zeros(m)
    for a, v, sgn in zip(A, u, sg): lam[a] = sgn * v
    return lam


def _selftest():
    from scipy.optimize import minimize
    rng = np.random.default_rng(3); worst = 0.0; worstkkt = 0.0
    for k in range(20):
        n = int(rng.integers(4, 15)); meq = int(rng.integers(0, min(3, n - 1) + 1)); m = meq + int(rng.integers(1, 18))
        B = rng.normal(size=(n, n)); G = B @ B.T + 0.1 * np.eye(n); g = rng.normal(size=n) * 3
        x0 = rng.normal(size=n); C = rng.normal(size=(m, n)); d = C @ x0 - np.abs(rng.normal(size=m)) * 0.5; d[:meq] = C[:meq] @ x0
        x, st, it, lam = solve(G, g, C, d, meq)
        cons = [{"type": "eq", "fun": (lambda x, i=i: C[i] @ x - d[i])} for i in range(meq)] + [{"type": "ineq", "fun": (lambda x, i=i: C[i] @ x - d[i])} for i in range(meq, m)]
        r = minimize(lambda x: 0.5 * x @ G @ x + g @ x, np.zeros(n), jac=lambda x: G @ x + g, constraints=cons, method="SLSQP", options={"ftol": 1e-14, "maxiter": 500})
        fo = lambda x: 0.5 * x @ G @ x + g @ x
        worst = max(worst, abs(fo(x) - fo(r.x)) / (1 + abs(fo(r.x))), 0.0)
        kkt = np.abs(G @ x + g - C.T @ lam).max(); feas = max((np.abs(C[:meq] @ x - d[:meq])).max() if meq else 0, (-(C[meq:] @ x - d[meq:])).max()); comp = np.abs(lam[meq:] * (C[meq:] @ x - d[meq:])).max()
        worstkkt = max(worstkkt, kkt / (1 + np.abs(g).max()), feas, comp, -min(0, lam[meq:].min()) if m > meq else 0)
        print(f"qp{k:2d} n={n:2d} m={m:2d} meq={meq} status={st} iters={it:3d} obj_rel_diff_vs_scipy={abs(fo(x) - fo(r.x)) / (1 + abs(fo(r.x))):.1e} scipy_ok={r.success} kkt={kkt:.1e} feas={feas:.1e}")
    print(f"worst obj rel diff {worst:.2e}; worst KKT residual (stationarity/feas/compl/dual sign) {worstkkt:.2e}")


if __name__ == "__main__":
    _selftest()
