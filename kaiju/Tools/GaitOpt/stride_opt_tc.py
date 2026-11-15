#!/usr/bin/env python
"""T-005 r3 step 8.1b: re-solve the stride_opt.py problem with scipy trust-constr (scaled variables/constraints, sparse Jacobians).
Run from the project root:
  /opt/miniconda3/envs/kaiju/bin/python Tools/GaitOpt/stride_opt_tc.py [--nodes 40] [--warm Tools/GaitOpt/out/stride_fr010.json] [--maxiter 500] [--out Tools/GaitOpt/out/stride_fr010_tc]
The problem (model, constraints, limits, objective, schedule) is taken verbatim from stride_opt.py: its source up to the solver call is exec'd
unchanged (so stride_opt.py is not modified and nothing is relaxed). Only the solver, scaling and warm start differ."""
import sys, os, time, json, argparse, pathlib
HERE = pathlib.Path(__file__).resolve().parent
ap = argparse.ArgumentParser()
ap.add_argument("--fr", type=float, default=0.10); ap.add_argument("--nodes", type=int, default=40)
ap.add_argument("--maxiter", type=int, default=500); ap.add_argument("--budget_s", type=float, default=0, help="wall-clock budget, 0 = none")
ap.add_argument("--warm", default=None, help="warm-start json (default Tools/GaitOpt/out/stride_frNNN.json)")
ap.add_argument("--out", default=None, help="output prefix (writes .json and .txt), default Tools/GaitOpt/out/stride_frNNN_tc")
ap.add_argument("--vlim", type=float, default=0.5); ap.add_argument("--tol", type=float, default=1e-5); ap.add_argument("--gtol", type=float, default=1e-8)
ap.add_argument("--hess", default="bfgs", choices=["sr1", "bfgs"]); ap.add_argument("--tr0", type=float, default=0.05); ap.add_argument("--mu0", type=float, default=1e-3)
ap.add_argument("--lat", action="store_true", help="add lateral LIPM shear to the friction cone: mu_eff(k) = sqrt(mu^2-(a_lat(k)/g)^2) (tightens the cone only)")
ap.add_argument("--trans", action="store_true", help="9.3b-3: ADD transition-feasibility rows (off = old behaviour): Fn = 0 at the first and last loaded node of every contact "
                "interval (complementarity), |dFn/dt| <= --frate BW per 0.11 s between all consecutive nodes of every point, lift-off point speed <= --vlift")
ap.add_argument("--frate", type=float, default=0.5, help="--trans: max |Fn(k+1)-Fn(k)| in BW per 0.11 s of time (scaled by h = ts/N)")
ap.add_argument("--vlift", type=float, default=0.5, help="--trans: max ground-relative point speed over the node interval after the last loaded node, m/s")
A = ap.parse_args()

# ---------------- problem definition: exec stride_opt.py up to (not including) its SLSQP section
src = (HERE / "stride_opt.py").read_text(); cut = src.index('cons = [{"type": "eq"')
sys.argv = ["stride_opt.py", "--fr", str(A.fr), "--nodes", str(A.nodes), "--vlim", str(A.vlim)]
P = {"__file__": str(HERE / "stride_opt.py"), "__name__": "stride_opt_problem"}
exec(compile(src[:cut], str(HERE / "stride_opt.py"), "exec"), P)
import numpy as np, scipy.sparse as sp
from scipy.optimize import minimize, NonlinearConstraint, Bounds, BFGS, SR1
N, nz, nF, W, MU, act, fl = P["N"], P["nz"], P["nF"], P["W"], P["MU"], P["act"], P["fl"]
evalz, Jz, obj, obj_grad = P["evalz"], P["Jz"], P["obj"], P["obj_grad"]
Seq, Sin, kin_c, Fr_m, LAB_IN, labels_eq = P["Seq"], P["Sin"], P["kin_c"], P["Fr_m"], P["LAB_IN"], P["labels_eq"]
bnds, v_fwd, Tref, LIMV, LIM, lo, hi, ROM = P["bnds"], P["v_fwd"], P["Tstep_ref"], P["LIMV"], P["LIM"], P["lo"], P["hi"], P["ROM"]
tag = f"stride_fr{int(round(A.fr*100)):03d}"; warm_path = A.warm or f"Tools/GaitOpt/out/{tag}.json"; out_pre = A.out or f"Tools/GaitOpt/out/{tag}_tc"
eq_fun0 = lambda z: Seq @ evalz(z)["O"].reshape(-1)
in_fun0 = lambda z: np.concatenate([Sin @ evalz(z)["O"].reshape(-1) + kin_c, Fr_m @ z])
# ---------------- warm start from a previous json (any node count; periodic mirrored linear interpolation)
def warm_from_json(path):
    D = json.load(open(path)); nd = D["nodes"]; N0 = len(nd); ts0 = D["summary"]["step_time_s"]
    Q0 = np.array([n["q"] for n in nd]); sw = [0, 1, 2, 6, 7, 8, 3, 4, 5]; d = v_fwd * ts0
    shift = np.zeros(9); shift[0] = D["summary"].get("step_length_m", d); Qe = np.vstack([Q0, (Q0[0][sw] + shift)[None]])             # node N0 = node 0 mirrored, hip x + step
    names = ["A_heel", "A_toe", "B_heel", "B_toe"]
    F0 = np.array([[nd[k]["contact_N"][nm] for nm in names] for k in range(N0)]) / (W * 1e6)     # (N0, 4, [Ft, Fn]) in BW
    F0e = np.concatenate([F0, F0[0][[2, 3, 0, 1]][None]], 0)
    tn = np.arange(N) / N * N0; Q = np.stack([np.interp(tn, np.arange(N0 + 1), Qe[:, c]) for c in range(9)], 1)
    Fv = []
    for (k, p) in act: Fv += [float(np.interp(tn[k], np.arange(N0 + 1), F0e[:, p, 0])), float(max(0.0, np.interp(tn[k], np.arange(N0 + 1), F0e[:, p, 1])))]
    return np.concatenate([Q.reshape(-1), [ts0], Fv])
z0 = warm_from_json(warm_path) if os.path.exists(warm_path) else P["z0"].copy()
lb = np.array([b[0] for b in bnds]); ub = np.array([b[1] for b in bnds]); z0 = np.clip(z0, lb + 1e-6 * (ub - lb), ub - 1e-6 * (ub - lb))
# ---------------- --lat: lateral LIPM shear in the friction cone (formulas of yaw_check.py lateral(); HW, zc, omega)
LAT = None; GR = P["g"]
if A.lat:
    import torch; sys.path.insert(0, str(HERE.parent.parent / "research" / "R-008")); from model import HW as HWL
    def lat_a(z):                      # a_lat at the nodes (m/s2) for the step time and CoM height of z
        ts_ = z[N * 9]; Qz = z[:N * 9].reshape(N, 9); Mw = P["MASS"].numpy()
        zc = float(np.mean([(Mw * P["comf"](torch.tensor(Qz[k]))[:14].reshape(7, 2)[:, 1].numpy()).sum() / Mw.sum() for k in range(N)]))
        w = np.sqrt(GR / zc); C_, S_ = np.cosh(w * ts_), np.sinh(w * ts_)
        y0, u0 = np.linalg.solve(np.array([[C_ + 1, S_ / w], [w * S_, C_ + 1]]), np.array([HWL * (C_ - 1), w * S_ * HWL]))
        tt = np.linspace(0, ts_, 400); aa = w ** 2 * (HWL + (y0 - HWL) * np.cosh(w * tt) + u0 / w * np.sinh(w * tt) - HWL)
        return np.interp(np.arange(N) * ts_ / N, tt, aa), zc, w, ts_
    def mu_eff_of(a): return np.sqrt(np.maximum(MU ** 2 - (a / GR) ** 2, 1e-12))
    def build_Fr(mue):
        r_, c_, v_ = [], [], []
        for i, (k, p) in enumerate(act):
            for s_, row in ((1, 2 * i), (-1, 2 * i + 1)): r_ += [row, row]; c_ += [N * 9 + 1 + 2 * i, N * 9 + 1 + 2 * i + 1]; v_ += [s_, mue[k]]
        return sp.csr_matrix((v_, (r_, c_)), shape=(2 * nF, nz))
    a_lat, zc_lat, w_lat, ts_lat = lat_a(z0); mue = mu_eff_of(a_lat); Fr_m = build_Fr(mue); LAT = dict(a_peak=float(np.abs(a_lat).max()), zc=zc_lat, omega=float(w_lat), hw=float(HWL))
    print(f"--lat: zc {zc_lat:.2f} m, omega {w_lat:.3f}, step {ts_lat:.3f} s, peak a_lat {np.abs(a_lat).max()/GR:.3f} g, mu_eff min {mue.min():.4f} (mu {MU:.4f})", flush=True)
# ---------------- --trans (9.3b-3): transition-feasibility rows, all ADDED to the unchanged problem (linear in z and in the node outputs O)
NOc = P["NO"]; fle = P["fle"]; aidx = P["aidx"]; SW4 = [2, 3, 0, 1]
Sx_O = sp.csr_matrix((0, N * NOc)); Sx_z = sp.csr_matrix((0, nz)); Ex_z = sp.csr_matrix((0, nz)); lab_x, lab_e = [], []
if A.trans:
    iFn = lambda k, p: N * 9 + 1 + 2 * aidx[(k, p)] + 1
    def fn_at(k, p):                      # z index of Fn of point p at node k (0..N; node N = node 0 with legs swapped), None if unloaded
        if k == N: k, p = 0, SW4[p]
        return iFn(k, p) if fl[k, p] else None
    er = []
    for (k, p) in act:                    # (a) complementarity: a point that touches down / lifts off at this node carries no force here
        first, last = not fle[k, p], not fl[k + 1, p]
        if first or last: er.append(iFn(k, p)); lab_e.append(("compl_fn0", k, p, "first" if first else "last"))
    Ex_z = sp.csr_matrix((np.ones(len(er)), (np.arange(len(er)), er)), shape=(len(er), nz))
    Rh = A.frate / 0.11 / N               # BW per (ts/N) per second of step time: bound = Rh * ts
    zr, zc_, zv = [], [], []; orr, oc, ov = [], [], []; nr = 0
    for p in range(4):                    # (b) normal-force rate bound between consecutive nodes (incl. N-1 -> N = mirrored node 0)
        for k in range(N):
            a_, b_ = fn_at(k + 1, p), fn_at(k, p)
            if a_ is None and b_ is None: continue
            for sg in (1.0, -1.0):        # Rh*ts - sg*(Fn(k+1) - Fn(k)) >= 0
                zr.append(nr); zc_.append(N * 9); zv.append(Rh)
                if a_ is not None: zr.append(nr); zc_.append(a_); zv.append(-sg)
                if b_ is not None: zr.append(nr); zc_.append(b_); zv.append(sg)
                lab_x.append(("frate+" if sg > 0 else "frate-", k, p)); nr += 1
    for (k, p) in act:                    # (c) lift-off speed: |P(k+1) - P(k)| <= vlift * h at the last loaded node (x both signs, z up; ground row stops z down)
        if fl[k + 1, p]: continue
        for c_, sg, nm in ((0, 1.0, "lift_vx+"), (0, -1.0, "lift_vx-"), (1, 1.0, "lift_vy")):
            orr += [nr, nr]; oc += [k * NOc + 31 + 2 * p + c_, k * NOc + 15 + 2 * p + c_]; ov += [-sg, sg]
            zr.append(nr); zc_.append(N * 9); zv.append(A.vlift / N); lab_x.append((nm, k, p)); nr += 1
    Sx_z = sp.csr_matrix((zv, (zr, zc_)), shape=(nr, nz)); Sx_O = sp.csr_matrix((ov, (orr, oc)), shape=(nr, N * NOc))
    print(f"--trans: {len(lab_e)} Fn=0 rows, {sum(l[0].startswith('frate') for l in lab_x)} force-rate rows ({A.frate} BW / 0.11 s -> {Rh*Tref:.3f} BW/node at ts_ref), "
          f"{sum(l[0].startswith('lift') for l in lab_x)} lift-off rows (vlift {A.vlift} m/s)", flush=True)
LAB_IN = LAB_IN + lab_x; labels_eq_all = labels_eq + lab_e
eq_fun0 = lambda z: np.concatenate([Seq @ evalz(z)["O"].reshape(-1), Ex_z @ z])
in_fun0 = lambda z: np.concatenate([Sin @ evalz(z)["O"].reshape(-1) + kin_c, Fr_m @ z, Sx_O @ evalz(z)["O"].reshape(-1) + Sx_z @ z])
# ---------------- scaling: x = (z - z0) / sv ; constraint rows scaled to O(1)
sv = np.ones(nz); Qs = np.tile([10.0, 5.0, 0.3, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5], N); sv[:N * 9] = Qs; sv[N * 9] = Tref; sv[N * 9 + 1:] = 1.0
rs_in = np.array([1 / 30.0 if (isinstance(l, tuple) and l[0] in ("rom_lo", "rom_hi")) else 1.0 for l in LAB_IN])              # deg rows -> ~1
# Redundant rows: with heel AND toe of foot B on the ground at nodes k and k+1 (heel stay_x active), the toe stay_x row is implied by
# heel y=0, toe y=0 (flat sole -> constant pitch) and heel stay_x. Dropping them (21 rows, Jacobian rank 243/264) leaves the feasible set identical
# and removes the singular Jacobian that makes trust-constr fall back to dense SVD / wander. Residuals are still reported on ALL rows.
keep = np.array([not (l[0] == "stay_x" and l[2] == 3 and fl[l[1], 2] and fl[l[1] + 1, 2]) for l in labels_eq]); Seq_k = Seq[np.where(keep)[0]]
rs_eq = np.ones(Seq_k.shape[0])
def zof(x): return z0 + sv * x
def f(x): return obj(zof(x))
def g(x): return obj_grad(zof(x)) * sv
def ceq(x): z = zof(x); return np.concatenate([rs_eq * (Seq_k @ evalz(z)["O"].reshape(-1)), Ex_z @ z])
def Jeq(x): z = zof(x); return sp.vstack([sp.diags(rs_eq) @ (Seq_k @ Jz(z)), Ex_z]).tocsr() @ sp.diags(sv)
def cin(x): return rs_in * in_fun0(zof(x))
def Jin(x): z = zof(x); return sp.diags(rs_in) @ sp.vstack([Sin @ Jz(z), Fr_m, Sx_O @ Jz(z) + Sx_z]).tocsr() @ sp.diags(sv)
HQ = SR1 if A.hess == "sr1" else BFGS
cons = [NonlinearConstraint(ceq, 0, 0, jac=lambda x: Jeq(x).tocsr(), hess=HQ()), NonlinearConstraint(cin, 0, np.inf, jac=lambda x: Jin(x).tocsr(), hess=HQ())]
bx = Bounds((lb - z0) / sv, (ub - z0) / sv)
print(f"trust-constr: N={N} nz={nz} eq={Seq_k.shape[0] + Ex_z.shape[0]}/{Seq.shape[0] + Ex_z.shape[0]} ineq={len(LAB_IN)} warm={warm_path if os.path.exists(warm_path) else 'R-008'}", flush=True)
t0 = time.time(); st = {"it": 0, "x": np.zeros(nz)}
def viol(z): return np.abs(eq_fun0(z)).max(), max(0.0, -in_fun0(z).min())
def cb(xk, state):
    st["it"] += 1; st["x"] = xk.copy()
    ve, vi = viol(zof(xk)); print(f"it {state.nit} f={obj(zof(xk)):.5f} eq={ve:.2e} in={vi:.2e} tr={state.tr_radius:.1e} mu={state.barrier_parameter:.1e} t={time.time()-t0:.0f}s ({(time.time()-t0)/max(1,state.nit):.2f}s/it)", flush=True)
    return bool(A.budget_s and time.time() - t0 > A.budget_s)
ve, vi = viol(z0); print(f"warm start: f={obj(z0):.5f} eq={ve:.2e} in={vi:.2e} ({time.time()-t0:.1f}s)", flush=True)
def solve(x0):
    return minimize(f, x0, jac=g, hess=HQ(), constraints=cons, bounds=bx, method="trust-constr", callback=cb,
                    options={"maxiter": A.maxiter, "gtol": A.gtol, "xtol": 1e-12, "barrier_tol": 1e-9, "initial_barrier_parameter": A.mu0, "initial_tr_radius": A.tr0, "verbose": 0})
res = solve(np.zeros(nz)); iters = int(res.nit); resolved = False
if LAT is not None:                    # one outer fixed-point iteration: a_lat depends on the step time, which is a variable
    a_new, zc_new, w_new, ts_new = lat_a(zof(res.x)); mue_new = mu_eff_of(a_new); chg = float(np.max(np.abs(mue_new / mue - 1)))
    print(f"--lat outer check: step {ts_new:.3f} s, max change of mu_eff {100*chg:.2f} %", flush=True)
    if chg > 0.01:
        mue = mue_new; Fr_m = build_Fr(mue); res = solve(res.x.copy()); iters += int(res.nit); resolved = True
z = zof(res.x); rt = time.time() - t0; status = int(res.status); msg = str(res.message)
# ---------------- report (same schema as stride_opt.py plus solver info)
O = evalz(z)["O"]; ce = eq_fun0(z); ci = in_fun0(z); Qn = z[:N * 9].reshape(N, 9); ts = z[N * 9]; Fv = z[N * 9 + 1:].reshape(nF, 2)
viol_eq = float(np.abs(ce).max()); viol_in = float(max(0.0, -ci.min(), (lb - z).max(), (z - ub).max()))
dyn_idx = [i for i, l in enumerate(labels_eq) if l[0] == "dyn"]; viol_dyn = float(np.abs(ce[dyn_idx]).max())
tau = O[:, 3:9] * 1e9; names = ["hip", "knee", "ankle"]; peak = {f"{names[j%3]}_{'AB'[j//3]}": float(np.abs(tau[:, j]).max()) for j in range(6)}
peak_ratio = {n_: peak[n_] / (LIMV[i] * 1e9) for i, n_ in enumerate(peak)}
Fn = np.zeros((N, 4)); Ft = np.zeros((N, 4))
for i, (k, p) in enumerate(act): Ft[k, p], Fn[k, p] = Fv[i]
fnt = Fn.sum(1); util = float(np.where(Fn > 1e-3, np.abs(Ft) / (MU * np.maximum(Fn, 1e-9)), 0).max())
tol_rel = 0.02; bind = []; kn = ["hip", "knee", "ank"]
for k in range(N):
    for j in range(6):
        if abs(tau[k, j]) >= (1 - tol_rel) * LIMV[j] * 1e9: bind.append(("torque", names[j % 3] + "_" + "AB"[j // 3], k, float(abs(tau[k, j]) / (LIMV[j] * 1e9))))
        ang = O[k, 9 + j]; r = ROM[kn[j % 3]][1] - ROM[kn[j % 3]][0]
        if ang <= lo[j % 3] + tol_rel * r or ang >= hi[j % 3] - tol_rel * r: bind.append(("rom", names[j % 3] + "_" + "AB"[j // 3], k, float(ang)))
for i, (k, p) in enumerate(act):
    if Fn[k, p] > 1e-3 and abs(Ft[k, p]) >= 0.98 * MU * Fn[k, p]: bind.append(("friction", "AB"[p // 2] + ["heel", "toe"][p % 2], k, float(abs(Ft[k, p]) / (MU * Fn[k, p]))))
ref = {"strike_vx+": A.vlim, "strike_vx-": A.vlim, "strike_vy": A.vlim, "clear": P["CLEAR"]}
for i, l in enumerate(LAB_IN):
    if isinstance(l, tuple) and l[0] in ref and ci[i] <= tol_rel * ref[l[0]]: bind.append((l[0], str(l[2]), int(l[1]), float(ci[i])))
for i, l in enumerate(LAB_IN):
    if isinstance(l, tuple) and l[0] in ("frate+", "frate-", "lift_vx+", "lift_vx-", "lift_vy") and ci[i] <= 1e-4: bind.append((l[0], "AB"[l[2] // 2] + ["heel", "toe"][l[2] % 2], int(l[1]), float(ci[i])))
TRS = None
if A.trans:
    Fe = np.vstack([Fn, Fn[0][SW4][None]]); dFn = np.abs(np.diff(Fe, axis=0)); h_ = ts / N
    Ox = evalz(z)["O"]; lift = {f"{'AB'[p // 2]}_{['heel', 'toe'][p % 2]}@node{k}": float(np.hypot(*(Ox[k, 31 + 2 * p:33 + 2 * p] - Ox[k, 15 + 2 * p:17 + 2 * p])) / h_) for (k, p) in act if not fl[k + 1, p]}
    TRS = dict(frate_BW_per_0p11s=A.frate, vlift=A.vlift, n_fn0_rows=len(lab_e), n_rate_rows=int(sum(l[0].startswith("frate") for l in lab_x)), n_lift_rows=int(sum(l[0].startswith("lift") for l in lab_x)),
               max_dFn_per_node_BW=float(dFn.max()), max_dFn_rate_BW_per_0p11s=float(dFn.max() / h_ * 0.11), max_Fn_at_fn0_nodes_BW=float(max([abs(z[iFn(k, p)]) for (_, k, p, _) in lab_e] + [0.0])),
               liftoff_speed_mps=lift, total_Fn_per_node_BW=[float(x) for x in Fn.sum(1)])
rom_margin = {}
for j in range(6):
    a = O[:, 9 + j]; kk = kn[j % 3]; rom_margin[f"{names[j%3]}_{'AB'[j//3]}"] = dict(to_5deg_margin_bound=float(min((a - lo[j % 3]).min(), (hi[j % 3] - a).min())), to_physical_rom=float(min((a - ROM[kk][0]).min(), (ROM[kk][1] - a).min())))
conv_stat = status in (1, 2)
if viol_dyn <= A.tol and viol_eq <= A.tol and viol_in <= A.tol: verdict = "FEASIBLE"
elif conv_stat and (viol_eq > A.tol or viol_in > A.tol): verdict = "INFEASIBLE"        # converged to a stationary point of the problem with constraints still violated (local statement)
else: verdict = "NOT CONVERGED"
lat_sum = None
if LAT is not None:
    a_fin, zc_fin, w_fin, _ = lat_a(z); mue_fin = mu_eff_of(a_fin); stale = float(np.max(np.abs(mue_fin / mue - 1)))
    use3 = 0.0; Fy = np.zeros((N, 4))
    for (k, p) in act:
        if Fn[k, p] > 1e-3: Fy[k, p] = Fn[k, p] * a_fin[k] / GR; use3 = max(use3, float(np.hypot(Ft[k, p], Fy[k, p]) / (MU * Fn[k, p])))
    lat_sum = dict(mu_eff_min=float(mue.min()), mu_eff_min_final_a=float(mue_fin.min()), friction_use_3d_max=use3, a_lat_peak_g=float(np.abs(a_fin).max() / GR), zc=float(zc_fin), omega=float(w_fin),
                   resolved_once=resolved, mu_eff_stale_rel=stale, mu_eff_nodes=[float(m_) for m_ in mue])
    if verdict == "FEASIBLE" and use3 > 1.0 + 1e-3: verdict = "NOT CONVERGED"; lat_sum["note"] = "3D friction use above 1 (mu_eff stale vs final step time)"
h = ts / N; Ue = evalz(z)["U"]; qdn = (Ue[:, 18:27] - Ue[:, 0:9]) / (2 * h); knee = np.stack([O[:, 10], O[:, 13]], 1)
summary = dict(verdict=verdict, **({"lat": lat_sum} if lat_sum else {}), **({"trans": TRS} if TRS else {}), solver="scipy trust-constr", solver_status=status, solver_message=msg, iterations=iters, runtime_s=rt, fr=A.fr, v=v_fwd, step_time_s=float(ts), step_time_ref_s=Tref,
               step_length_m=float(v_fwd * ts), nodes=N, max_eq_violation=viol_eq, max_dyn_residual=viol_dyn, max_ineq_violation=viol_in, residual_tol=A.tol, objective=obj(z), mu=MU,
               solver_info=dict(optimality=float(res.optimality), constr_violation=float(res.constr_violation), barrier_parameter=float(res.barrier_parameter), tr_radius=float(res.tr_radius), nfev=int(res.nfev), njev=int(res.njev), warm_start=warm_path),
               torque_limits_Nm=dict(hip=LIM["hip"] * 1e9, knee=LIM["knee"] * 1e9, ankle=LIM["ankle"] * 1e9), peak_torque_Nm=peak, peak_torque_over_limit=peak_ratio,
               peak_normal_force_total_BW=float(fnt.max()), peak_normal_force_total_at_node=int(fnt.argmax()), peak_point_normal_BW=float(Fn.max()), peak_friction_utilisation=util,
               knee_deg_min=float(knee.min()), knee_deg_max=float(knee.max()), rom_margin_deg=rom_margin, binding=bind, schedule=dict(k_ds=P["k_ds"], k_ff=P["k_ff"], k_hr=P["k_hr"]),
               points=["A_heel", "A_toe", "B_heel", "B_toe"], note="same conventions as stride_opt.py: leg A = old stance leg at node 0, leg B = new stance leg; node N = node 0 with A and B swapped, hip x + step length")
out = dict(summary=summary, nodes=[dict(k=k, t=float(k * h), q=Qn[k].tolist(), qd=qdn[k].tolist(), tau_Nm=dict(zip(["hipA", "kneeA", "ankleA", "hipB", "kneeB", "ankleB"], tau[k].tolist())),
           joint_deg=O[k, 9:15].tolist(), contact_N=dict(zip(["A_heel", "A_toe", "B_heel", "B_toe"], [[float(Ft[k, p] * W * 1e6), float(Fn[k, p] * W * 1e6)] for p in range(4)]))) for k in range(N)])
pathlib.Path(out_pre).parent.mkdir(parents=True, exist_ok=True); json.dump(out, open(out_pre + ".json", "w"), indent=1)
lines = [f"{pathlib.Path(out_pre).name}: verdict {verdict}; trust-constr status {status} ({msg}); iterations {iters}; runtime {rt:.0f} s; N={N}",
         f"max dynamics residual {viol_dyn:.2e}, max eq residual {viol_eq:.2e}, max ineq violation {viol_in:.2e} (feasible tol {A.tol:.0e}); optimality {res.optimality:.2e}",
         f"objective {obj(z):.5f}; step time {ts:.3f} s (ref {Tref:.3f}), step length {v_fwd*ts:.2f} m, v {v_fwd:.3f} m/s",
         "peak |tau| / limit: " + ", ".join(f"{n_} {peak[n_]:.3e}/{LIMV[i]*1e9:.1e} = {peak_ratio[n_]:.3f}" for i, n_ in enumerate(peak)),
         f"peak total Fn {fnt.max():.3f} BW (node {fnt.argmax()}), peak point Fn {Fn.max():.3f} BW, peak friction use {util:.3f} (mu {MU:.3f})",
         "ROM margin to 5 deg-margin bound / to physical ROM (deg): " + ", ".join(f"{n_} {m['to_5deg_margin_bound']:.1f}/{m['to_physical_rom']:.1f}" for n_, m in rom_margin.items()),
         f"knee deg min {knee.min():.1f} max {knee.max():.1f}", f"binding within 2% ({len(bind)}): " + "; ".join(f"{b[0]} {b[1]} @node {b[2]} ({b[3]:.3g})" for b in bind[:80])]
if viol_eq > A.tol or viol_in > A.tol:
    wo = np.argsort(-np.abs(ce))[:5]; lines.append("worst eq: " + "; ".join(f"{labels_eq_all[i]} {ce[i]:.2e}" for i in wo))
    wi = np.argsort(ci)[:8]; lines.append("worst ineq: " + "; ".join(f"{LAB_IN[i]} {ci[i]:.2e}" for i in wi))
if lat_sum: lines.append(f"--lat: mu_eff min {lat_sum['mu_eff_min']:.4f} (used; mu {MU:.4f}; {'re-solved once' if resolved else 'single solve'}), mu_eff min at final step time {lat_sum['mu_eff_min_final_a']:.4f} (stale {100*lat_sum['mu_eff_stale_rel']:.2f} %), peak a_lat {lat_sum['a_lat_peak_g']:.3f} g (zc {lat_sum['zc']:.2f} m, omega {lat_sum['omega']:.3f}); 3D friction use sqrt(Ft^2+Fy^2)/(mu Fn) max {lat_sum['friction_use_3d_max']:.3f}; verdict {verdict}")
if TRS: lines.append(f"--trans: Fn=0 rows {TRS['n_fn0_rows']} (max |Fn| there {TRS['max_Fn_at_fn0_nodes_BW']:.1e} BW), rate rows {TRS['n_rate_rows']} (max |dFn| {TRS['max_dFn_rate_BW_per_0p11s']:.3f} BW/0.11 s, bound {A.frate}), lift-off speeds (m/s): " + ", ".join(f"{k_} {v_:.2f}" for k_, v_ in TRS['liftoff_speed_mps'].items()))
open(out_pre + ".txt", "w").write("\n".join(lines) + "\n"); print("\n".join(lines))
