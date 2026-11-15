#!/opt/miniconda3/envs/kaiju/bin/python
"""Reference QPs for the C# Goldfarb-Idnani solver (T-005 r4 9.2).
min 0.5 x'Hx + g'x  s.t.  Aeq x = beq, Ain x >= bin.  scipy SLSQP, then the active set it finds is
re-solved exactly (KKT system, lstsq) and the KKT certificate is checked; JSON -> out/qp_reference/cases.json."""
import json, os
import numpy as np
from scipy.optimize import minimize

rng = np.random.default_rng(20240915)
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "out", "qp_reference")


def spd(n, cond=50.0):
    q, _ = np.linalg.qr(rng.standard_normal((n, n)))
    ev = np.exp(np.linspace(0, np.log(cond), n))
    return (q * ev) @ q.T


def kkt_solve(H, g, Aeq, beq, Ain, bin_, x_guess, tol=1e-6):
    n = len(g)
    act = [i for i in range(len(bin_)) if Ain[i] @ x_guess - bin_[i] < tol] if len(bin_) else []
    for _ in range(20):
        A = np.vstack([Aeq.reshape(-1, n), Ain[act].reshape(-1, n)]) if (len(act) or len(beq)) else np.zeros((0, n))
        b = np.concatenate([beq, bin_[act]]) if len(act) or len(beq) else np.zeros(0)
        k = len(b)
        K = np.block([[H, -A.T], [A, np.zeros((k, k))]])
        sol = np.linalg.lstsq(K, np.concatenate([-g, b]), rcond=None)[0]
        x, lam = sol[:n], sol[n:]
        ne = len(beq)
        # drop negative inequality multipliers, add violated rows
        changed = False
        for j in range(len(act) - 1, -1, -1):
            if lam[ne + j] < -1e-9:
                act.pop(j); changed = True
        viol = [i for i in range(len(bin_)) if i not in act and Ain[i] @ x - bin_[i] < -1e-10]
        if viol:
            act.append(min(viol, key=lambda i: Ain[i] @ x - bin_[i])); changed = True
        if not changed:
            return x, act
    return x, act


def kkt_resid(H, g, Aeq, beq, Ain, bin_, x):
    n = len(g)
    ne = len(beq)
    act = [i for i in range(len(bin_)) if abs(Ain[i] @ x - bin_[i]) < 1e-8]
    A = np.vstack([Aeq.reshape(-1, n), Ain[act].reshape(-1, n)])
    r = H @ x + g
    if A.shape[0]:
        lam = np.linalg.lstsq(A.T, r, rcond=None)[0]
        stat = np.abs(r - A.T @ lam).max()
        neg = max(0.0, -lam[ne:].min()) if len(act) else 0.0
    else:
        stat, neg = np.abs(r).max(), 0.0
    feas = max(np.abs(Aeq @ x - beq).max() if ne else 0.0, max(0.0, -(Ain @ x - bin_).min()) if len(bin_) else 0.0)
    return float(stat), float(neg), float(feas)


cases = []


def solve_case(name, H, g, Aeq, beq, Ain, bin_, xtol=1e-6, expect="optimal"):
    n = len(g)
    Aeq = np.asarray(Aeq, float).reshape(-1, n); beq = np.asarray(beq, float)
    Ain = np.asarray(Ain, float).reshape(-1, n); bin_ = np.asarray(bin_, float)
    c = dict(name=name, n=n, meq=len(beq), min=len(bin_), H=H.ravel().tolist(), g=g.tolist(), Aeq=Aeq.ravel().tolist(),
             beq=beq.tolist(), Ain=Ain.ravel().tolist(), bin=bin_.tolist(), xtol=xtol, status=expect)
    if expect == "infeasible":
        cases.append(c); return
    cons = []
    if len(beq): cons.append(dict(type="eq", fun=lambda x: Aeq @ x - beq, jac=lambda x: Aeq))
    if len(bin_): cons.append(dict(type="ineq", fun=lambda x: Ain @ x - bin_, jac=lambda x: Ain))
    x0 = np.zeros(n)
    res = minimize(lambda x: 0.5 * x @ H @ x + g @ x, x0, jac=lambda x: H @ x + g, constraints=cons, method="SLSQP",
                   options=dict(ftol=1e-15, maxiter=1000))
    x, act = kkt_solve(H, g, Aeq, beq, Ain, bin_, res.x)
    stat, neg, feas = kkt_resid(H, g, Aeq, beq, Ain, bin_, x)
    scale = 1 + np.abs(g).max() + np.abs(H @ x).max()
    assert stat < 1e-8 * scale and neg < 1e-8 * scale and feas < 1e-8 * (1 + np.abs(x).max()), (name, stat, neg, feas)
    c.update(x=x.tolist(), obj=float(0.5 * x @ H @ x + g @ x), scipy_x=res.x.tolist(), scipy_dx=float(np.abs(res.x - x).max()),
             kkt=dict(stat=stat, neg=neg, feas=feas))
    cases.append(c)


# random strictly convex QPs, n 5-40, m 0-80 (feasible by construction, some with equalities)
for i in range(14):
    n = int(rng.integers(5, 41)); m = int(rng.integers(0, 81)); me = int(rng.integers(0, max(1, n // 3))) if i % 2 else 0
    H = spd(n); g = rng.standard_normal(n) * 3
    xf = rng.standard_normal(n)
    Aeq = rng.standard_normal((me, n)); beq = Aeq @ xf
    Ain = rng.standard_normal((m, n)); bin_ = Ain @ xf - rng.uniform(0, 1.0, m) * (rng.random(m) < 0.8)
    solve_case(f"random_{i}_n{n}_m{m}_e{me}", H, g, Aeq, beq, Ain, bin_)

# stage 1: nonnegativity bounds + 6-row wrench equality, 16 and 32 variables
for n, tag in [(16, "s1_16"), (32, "s1_32")]:
    for r in range(3):
        G = rng.standard_normal((6, n)); G[2] = np.abs(G[2]) + 0.5  # normal-force row positive
        beta0 = rng.uniform(0.05, 1.0, n) * (rng.random(n) < 0.7)
        wrench = G @ beta0 + rng.standard_normal(6) * 0.05 * (r == 2)  # r==2: slightly off, bounds will be active
        Hh = np.diag(rng.uniform(0.5, 2.0, n)) + 0.1 * spd(n, 5.0)
        gg = rng.standard_normal(n) * 0.5
        solve_case(f"{tag}_{r}", Hh, gg, G, wrench, np.eye(n), np.zeros(n))

# stage 2: 20 vars, 12 equalities, 56 inequalities (28 torque rows two-sided style, 28 bounds)
for r in range(3):
    n, me, m = 20, 12, 56
    xf = rng.standard_normal(n)
    Aeq = rng.standard_normal((me, n)); beq = Aeq @ xf
    T = rng.standard_normal((28, n)); lim = np.abs(T @ xf) + rng.uniform(0.1, 1.0, 28)
    Ain = np.vstack([T, -T]); bin_ = np.concatenate([-lim, -lim]) * 1.0
    bin_ = np.concatenate([-lim + 0.0, -lim + 0.0])
    # rows: T x >= -lim and -T x >= -lim  (|T x| <= lim)
    solve_case(f"s2_20_{r}", spd(n, 30.0), rng.standard_normal(n) * 5, Aeq, beq, Ain, bin_)

# degenerate
n = 8; H = spd(n, 10.0); xf = rng.standard_normal(n)
A = rng.standard_normal((6, n)); b = A @ xf - 0.2
Adup = np.vstack([A, A, 2.0 * A[:3]]); bdup = np.concatenate([b, b, 2.0 * b[:3]])
solve_case("duplicate_rows", H, rng.standard_normal(n) * 4, np.zeros((0, n)), [], Adup, bdup)
Ared = np.vstack([A, A[0] + A[1], A[2] - A[3]]); bred = np.concatenate([b, [b[0] + b[1] - 1e-3, b[2] - b[3]]])
solve_case("redundant_rows", H, rng.standard_normal(n) * 4, np.zeros((0, n)), [], Ared, bred)
Ae = rng.standard_normal((3, n)); Ae = np.vstack([Ae, Ae[0] + Ae[1]]); be = Ae[:3] @ xf; be = np.concatenate([be, [be[0] + be[1]]])
solve_case("redundant_equality", H, rng.standard_normal(n), Ae, be, A, A @ xf - 0.05)
solve_case("zero_gradient", H, np.zeros(n), np.zeros((0, n)), [], A, b + 0.5)
solve_case("zero_gradient_unconstrained_opt", H, np.zeros(n), np.zeros((0, n)), [], A, -np.abs(b) - 0.1)
nt = 10; q, _ = np.linalg.qr(rng.standard_normal((nt, nt)))
Ht = (q * np.logspace(-4, 0, nt)) @ q.T; xt = rng.standard_normal(nt); At = rng.standard_normal((12, nt))
solve_case("tight_H", Ht, rng.standard_normal(nt), np.zeros((0, nt)), [], At, At @ xt - 0.1, xtol=1e-4)
solve_case("bounds_only_corner", np.eye(4), np.array([-5., -5, 5, 5]), np.zeros((0, 4)), [], np.vstack([np.eye(4), -np.eye(4)]), -np.ones(8))

# infeasible
solve_case("infeasible_boxes", spd(5, 5.0), rng.standard_normal(5), np.zeros((0, 5)), [],
           np.array([[1., 0, 0, 0, 0], [-1., 0, 0, 0, 0]]), [1.0, 0.0], expect="infeasible")
Ai = rng.standard_normal((2, 6))
solve_case("infeasible_equalities", spd(6, 5.0), rng.standard_normal(6), np.vstack([Ai, Ai[0]]), [0.3, 0.1, 0.9],
           np.zeros((0, 6)), [], expect="infeasible")

os.makedirs(OUT, exist_ok=True)
with open(os.path.join(OUT, "cases.json"), "w") as f:
    json.dump(dict(seed=20240915, cases=cases), f)
print(len(cases), "cases ->", os.path.join(OUT, "cases.json"))
for c in cases:
    print(c["name"], c["n"], c["meq"], c["min"], c["status"], c.get("kkt"), c.get("scipy_dx"))
