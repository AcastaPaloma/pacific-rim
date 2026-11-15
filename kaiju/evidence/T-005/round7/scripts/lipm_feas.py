"""T-005 r7 8a: reduced-order (constant-height LIPM) feasibility of a periodic walk step under the round-7 hard limits, as an LP over the CoP trajectory.
Per step: double support Td = dsf T (CoP anywhere in the hull of the rear window [-L+a, -L+b] and the front window [a, b], ankle line of the new stance foot at 0), single support (CoP in [a, b]);
periodic: x(T) - L = x(0), v(T) = v(0); CoM within +-rx of the stance ankle (hip reach) and of the rear ankle in DS; the rear CoP weight must be zero at the end of DS (CoP in the front window).
a = heel + cop_lo - 5.25 (ankle at 5.25 from the heel), b = toe - cop_hi - 5.25. Maximises the CoP margin (distance of the CoP from the window ends, a robustness slack s >= 0).
Friction: |x - p| <= mu z0 with mu 0.37 (0.4 derated by the lateral share, 8.x) at F_z = W.
Usage: python lipm_feas.py [z0=42.2]"""
import numpy as np, sys
from scipy.optimize import linprog
z0 = float(sys.argv[1]) if len(sys.argv) > 1 else 42.2
w = np.sqrt(9.81 / z0)
def feas(Fr, T, dsf=0.2, a=-3.75, b=7.25, rx=14.0, dt=0.05, l=42.4, mu=0.37):
    v = np.sqrt(Fr * 9.81 * l); L = v * T; n = int(round(T / dt)); h = T / n; nd = int(round(dsf * n))
    # state transition per tick (exact for piecewise-constant CoP): [x, v]' = A [x, v] + B p
    c, s = np.cosh(w * h), np.sinh(w * h); A = np.array([[c, s / w], [w * s, c]]); B = np.array([1 - c, -w * s])
    # unknowns: x0, v0, p_0..p_{n-1}, slack s ; build state maps X_k = M_k [x0, v0] + sum N_kj p_j
    nu = 2 + n + 1
    M = np.eye(2); Nk = np.zeros((2, n)); rows = []
    Xs = []                              # state before tick k as (2 x nu) affine maps
    Smat = np.zeros((2, nu)); Smat[0, 0] = 1; Smat[1, 1] = 1
    cur = Smat.copy(); maps = [cur]
    for k in range(n):
        nxt = A @ cur; nxt[:, 2 + k] += B; maps.append(nxt); cur = nxt
    Aub, bub = [], []
    for k in range(n):
        e = np.zeros(nu); e[2 + k] = 1
        if k < nd:   # DS: CoP in [-L + a, b], and the last DS tick inside the front window
            lo, hi = (-L + a, b) if k < nd - 1 else (a, b)
        else: lo, hi = a, b
        r = e.copy(); r[-1] = 1; Aub.append(-r * 0 + (-e + np.eye(nu)[-1])); bub.append(-lo)   # p - s >= lo  ->  -p + s <= -lo
        Aub.append(e + np.eye(nu)[-1]); bub.append(hi)                                           # p + s <= hi
        x = maps[k][0]                                                                           # CoM x (relative to the front ankle at 0; the rear ankle is at -L)
        if k < nd: Aub.append(-x); bub.append(-(-L - rx)); Aub.append(x); bub.append(rx)        # |x - 0| <= rx and x >= -L - rx during DS
        else: Aub.append(-x); bub.append(rx); Aub.append(x); bub.append(rx)
        # friction cone of the aggregated ground force (F_z = W): |x - p| <= mu z0
        xr = maps[k][0].copy(); xr[2 + k] -= 1; Aub.append(xr); bub.append(mu * z0); Aub.append(-xr); bub.append(mu * z0)
    Aeq = [maps[n][0] - Smat[0], maps[n][1] - Smat[1]]; beq = [L, 0.0]
    cost = np.zeros(nu); cost[-1] = -1
    res = linprog(cost, A_ub=np.array(Aub), b_ub=np.array(bub), A_eq=np.array(Aeq), b_eq=np.array(beq), bounds=[(None, None)] * (nu - 1) + [(0, None)], method='highs')
    return L, (res.x[-1] if res.status == 0 else None), (res.x if res.status == 0 else None)
if __name__ == '__main__':
    print(f"omega {w:.3f}; CoP window [-3.75, 7.25] m about the ankle; rx 14 m; Td 0.2 T")
    for Fr in (0.02, 0.05, 0.08, 0.09, 0.10, 0.12):
        out = []
        for T in (2.0, 2.5, 3.0, 3.5, 4.0, 4.4, 5.5, 6.3):
            L, s, _ = feas(Fr, T)
            out.append(f"T{T}:{'--' if s is None else f'{s:.2f}'}")
        print(f"Fr {Fr:.2f} v {np.sqrt(Fr*9.81*42.4):.2f}  CoP margin (m, -- = infeasible) " + "  ".join(out))
    print("rx sweep at Fr 0.10 T 3.0:", [(r, None if feas(0.10, 3.0, rx=r)[1] is None else round(feas(0.10, 3.0, rx=r)[1], 2)) for r in (6, 8, 10, 12, 14)])
