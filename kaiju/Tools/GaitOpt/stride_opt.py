#!/usr/bin/env python
"""T-005 r3 step 8.1: sagittal stride trajectory optimisation (one step, periodic mirrored).
Run from the project root:  /opt/miniconda3/envs/kaiju/bin/python Tools/GaitOpt/stride_opt.py --fr 0.10
Model: planar, 9 DOF q = [hip x, hip y, upper-body forward lean th, leg0(thigh,shank,foot), leg1(...)]
(thigh/shank angles a_t,a_s from vertical, + = distal end ahead; foot pitch f, + = heel up; R-008 conventions).
hip flexion = a_t + th, knee flexion = a_t - a_s, ankle plantarflexion = a_s + f.
Collocation: node-wise implicit dynamics (central differences, periodic mirrored extension), joint torques
eliminated analytically from the 9 Lagrange rows (3 rows left as equality constraints). Units: m, s, 1e6 kg, MN."""
import json, sys, os, time, argparse, pathlib
import numpy as np, torch, scipy.sparse as sp
from torch.func import jacfwd, vmap
from scipy.optimize import minimize
torch.set_default_dtype(torch.float64)
ROOT = pathlib.Path(__file__).resolve().parents[2]; os.chdir(ROOT)
sys.path.insert(0, str(ROOT / "research/R-008")); _argv = sys.argv; sys.argv = ["x", "none"]
import model as R8                                                    # R-008: gait(), solve(), angles(), conventions
sys.argv = _argv
ap = argparse.ArgumentParser(); ap.add_argument("--fr", type=float, default=0.10); ap.add_argument("--nodes", type=int, default=40)
ap.add_argument("--maxiter", type=int, default=120); ap.add_argument("--rounds", type=int, default=5); ap.add_argument("--budget_s", type=float, default=480); ap.add_argument("--plot", action="store_true")
ap.add_argument("--vlim", type=float, default=0.5, help="max ground-relative point speed at touchdown, m/s")
args = ap.parse_args()
# ---------------- body data
J = json.load(open("Assets/Game/Config/Mech/mech_frame_balanced.json")); S = {s["id"]: s for s in J["segments"]}
BT = json.load(open("Assets/Game/Config/Mech/body_tuning.json")); WT = json.load(open("Assets/Game/Config/Mech/walk_tuning.json"))
LIM = {k["kind"]: k["force_limit_nm"] / 1e9 for k in BT["joint_kinds"]}            # 1e9 N m: hip, knee, ankle
LIMV = np.array([LIM["hip"], LIM["knee"], LIM["ankle"]] * 2)
MU = BT["friction_dynamic"] / WT["friction_safety"]
g, l = 9.81, 42.4; Mtot = J["total_mass_kg"] / 1e6; W = Mtot * g                   # MN
HIPY = S["thigh_L"]["proximal_joint"][1]; LT = HIPY - S["thigh_L"]["distal_end"][1]; LS = S["shank_L"]["proximal_joint"][1] - S["shank_L"]["distal_end"][1]
HA = J["foot_sole"]["ankle_height_m"]; HEELX, TOEX = J["foot_sole"]["heel_z_m"], J["foot_sole"]["toe_z_m"]
FL, FH = J["foot_sole"]["length_m"], HA
up = [s for s in J["segments"] if s["id"] in ("pelvis", "torso", "head") or s["id"].split("_")[0] in ("upper", "forearm", "hand")]
mU = sum(s["mass_kg"] for s in up) / 1e6; cU = sum(s["mass_kg"] / 1e6 * (s["com"][1] - HIPY) for s in up) / mU       # COM height above hip
IU = sum(s["mass_kg"] / 1e6 * (abs(s["proximal_joint"][1] - s["distal_end"][1]) ** 2 / 12 + (s["com"][1] - HIPY - cU) ** 2) for s in up)
mT, mS, mF = (S[k]["mass_kg"] / 1e6 for k in ("thigh_L", "shank_L", "foot_L"))
cT = HIPY - S["thigh_L"]["com"][1]; cS = S["shank_L"]["proximal_joint"][1] - S["shank_L"]["com"][1]; cF = (S["foot_L"]["com"][2], S["foot_L"]["com"][1] - HA)
MASS = torch.tensor([mU, mT, mS, mF, mT, mS, mF]); INER = torch.tensor([IU, mT * LT**2 / 12, mS * LS**2 / 12, mF * (FL**2 + FH**2) / 12] * 1)
INER = torch.cat([INER, INER[1:4]])
ROM = {"hip": (-20, 120), "knee": (0, 135), "ank": (-20, 40)}; MARG = 5.0
lo = np.array([ROM["hip"][0], ROM["knee"][0], ROM["ank"][0]]) + MARG; hi = np.array([ROM["hip"][1], ROM["knee"][1], ROM["ank"][1]]) - MARG
v_fwd = np.sqrt(args.fr * g * l); v_ref, s_ref, T_ref = R8.gait(args.fr); Tstep_ref = T_ref / 2     # R-008 stride T, step = T/2
BETA = 0.63; CLEAR = 0.5
# ---------------- kinematics (torch)
def kin(q):
    th = q[2]; hip = q[:2]; coms = [hip + cU * torch.stack([torch.sin(th), torch.cos(th)])]; angs = [th]; pts = []
    for i in (0, 1):
        a, b, f = q[3 + 3 * i], q[4 + 3 * i], q[5 + 3 * i]
        dt = torch.stack([torch.sin(a), -torch.cos(a)]); ds = torch.stack([torch.sin(b), -torch.cos(b)])
        e = torch.stack([torch.cos(f), -torch.sin(f)]); n = torch.stack([torch.sin(f), torch.cos(f)])
        knee = hip + LT * dt; ank = knee + LS * ds
        coms += [hip + cT * dt, knee + cS * ds, ank + cF[0] * e + cF[1] * n]; angs += [a, b, -f]
        pts += [ank + HEELX * e - HA * n, ank + TOEX * e - HA * n]                   # heel, toe sole points
    return torch.cat([torch.stack(coms).reshape(-1), torch.stack(angs)]), torch.stack(pts)
comf = lambda q: kin(q)[0]; ptf = lambda q: kin(q)[1]
MR = MASS.repeat_interleave(2)
def Mfun(q):
    Jk = jacfwd(comf)(q); Jc, Jw = Jk[:14], Jk[14:]
    return Jc.T @ (MR[:, None] * Jc) + Jw.T @ (INER[:, None] * Jw)
def inv_dyn(q, qd, qdd):
    M = Mfun(q); dM = jacfwd(Mfun)(q); Jk = jacfwd(comf)(q); Jy = Jk[:14].reshape(7, 2, 9)[:, 1, :]
    G = g * (MASS[:, None] * Jy).sum(0)
    return M @ qdd + torch.einsum("ijk,j,k->i", dM, qd, qd) - 0.5 * torch.einsum("ijk,i,j->k", dM, qd, qd) + G
N = args.nodes
def f_node(u):
    qm, q, qp = u[0:9], u[9:18], u[18:27]; ts = u[27]; F = u[28:36].reshape(4, 2); h = ts / N
    qd = (qp - qm) / (2 * h); qdd = (qp - 2 * q + qm) / h**2
    Jp = jacfwd(ptf)(q); Q = inv_dyn(q, qd, qdd) - torch.einsum("pdi,pd->i", Jp, F * W)
    tp = torch.stack([Q[5], Q[8]]); tk = tp - torch.stack([Q[4], Q[7]]); th_ = torch.stack([Q[3], Q[6]]) - tk
    dyn = torch.stack([Q[0] / W, Q[1] / W, (Q[2] - th_.sum()) / 1000])
    tau = torch.stack([th_[0], tk[0], tp[0], th_[1], tk[1], tp[1]]) / 1000
    ang = torch.stack([q[3] + q[2], q[3] - q[4], q[4] + q[5], q[6] + q[2], q[6] - q[7], q[7] + q[8]]) / np.pi * 180
    P, Pm, Pp = ptf(q), ptf(qm), ptf(qp)
    return torch.cat([dyn, tau, ang, P.reshape(-1), ((P - Pm) / h).reshape(-1), Pp.reshape(-1)])
NO = 39
# ---------------- schedule (node flags; points: 0 A heel, 1 A toe, 2 B heel, 3 B toe; leg A = swing/old stance at node 0)
z8 = R8.solve(args.fr, beta=BETA, tilt_max=10, phs_max=25, fto_max=25, kmin=5.6, flat_ss=0.76, starts=24, fixed={10: 10})
k_ds = int(round((BETA - 0.5) * 2 * N)); k_ff = int(round(z8.x[7] * 2 * BETA * N)); k_hr = int(round(z8.x[8] * 2 * BETA * N))
fl = np.zeros((N + 1, 4), bool)
for k in range(N):
    fl[k, 1] = k <= k_ds; fl[k, 2] = k < k_hr; fl[k, 3] = k >= k_ff
swap4 = [2, 3, 0, 1]; fl[N] = fl[0][swap4]; fle = np.vstack([fl[N - 1][swap4][None], fl])      # fle[k+1] = node k, fle[0] = node -1
act = [(k, p) for k in range(N) for p in range(4) if fl[k, p]]; aidx = {kp: i for i, kp in enumerate(act)}; nF = len(act)
# ---------------- NLP assembly
nz = N * 9 + 1 + 2 * nF
def gather(z):
    Q = z[:N * 9].reshape(N, 9); ts = z[N * 9]; d = v_fwd * ts; sw = [0, 1, 2, 6, 7, 8, 3, 4, 5]
    qN = Q[0][sw] + torch.tensor([1.0, 0, 0, 0, 0, 0, 0, 0, 0]) * d; qM = Q[N - 1][sw] - torch.tensor([1.0, 0, 0, 0, 0, 0, 0, 0, 0]) * d
    Qe = torch.cat([qM[None], Q, qN[None]]); Fv = z[N * 9 + 1:]; Ff = torch.zeros(N, 8); ii = torch.tensor([[k, 2 * p] for (k, p) in act])
    Ff = Ff.index_put((ii[:, 0], ii[:, 1]), Fv[0::2]).index_put((ii[:, 0], ii[:, 1] + 1), Fv[1::2])
    return torch.cat([Qe[:-2], Q, Qe[2:], ts.expand(N, 1), Ff], 1)
Gm = sp.csr_matrix(torch.autograd.functional.jacobian(gather, torch.zeros(nz).add(0.1)).reshape(N * 36, nz).numpy())
fnode_v = vmap(f_node); jnode_v = vmap(jacfwd(f_node))
def idx(k, c): return k * NO + c
eq_r, eq_c, eq_v = [], [], []; n_eq = 0; in_r, in_c, in_v = [], [], []; in_k = []; n_in = 0; labels_in = []; labels_eq = []
def add_eq(terms, lab):
    global n_eq
    for c, v_ in terms: eq_r.append(n_eq); eq_c.append(c); eq_v.append(v_)
    labels_eq.append(lab); n_eq += 1
def add_in(terms, const, lab):
    global n_in
    for c, v_ in terms: in_r.append(n_in); in_c.append(c); in_v.append(v_)
    in_k.append(const); labels_in.append(lab); n_in += 1
for k in range(N):
    for r in range(3): add_eq([(idx(k, r), 1.0)], ("dyn", k, r))
    for p in range(4):
        if fl[k, p]:
            add_eq([(idx(k, 15 + 2 * p + 1), 1.0)], ("contact_y", k, p))
            if fl[k + 1, p]: add_eq([(idx(k, 31 + 2 * p), 1.0), (idx(k, 15 + 2 * p), -1.0)], ("stay_x", k, p))
        else: add_in([(idx(k, 16 + 2 * p), 1.0)], 0.0, ("ground", k, p))
        if fl[k, p] and not fle[k, p]:                                              # touchdown at node k
            for c in (0, 1): pass
            add_in([(idx(k, 23 + 2 * p), -1.0)], args.vlim, ("strike_vx+", k, p)); add_in([(idx(k, 23 + 2 * p), 1.0)], args.vlim, ("strike_vx-", k, p))
            add_in([(idx(k, 24 + 2 * p), 1.0)], args.vlim, ("strike_vy", k, p))
    for j in range(6):
        add_in([(idx(k, 3 + j), -1.0)], LIMV[j], ("tau+", k, j)); add_in([(idx(k, 3 + j), 1.0)], LIMV[j], ("tau-", k, j))
        add_in([(idx(k, 9 + j), 1.0)], -lo[j % 3], ("rom_lo", k, j)); add_in([(idx(k, 9 + j), -1.0)], hi[j % 3], ("rom_hi", k, j))
    if k_ds + 0.25 * (N - k_ds) <= k <= k_ds + 0.75 * (N - k_ds):
        for p in (0, 1): add_in([(idx(k, 16 + 2 * p), 1.0)], -CLEAR, ("clear", k, p))
Seq = sp.csr_matrix((eq_v, (eq_r, eq_c)), shape=(n_eq, N * NO)); Sin = sp.csr_matrix((in_v, (in_r, in_c)), shape=(n_in, N * NO)); kin_c = np.array(in_k)
# friction cone rows (linear in z): mu*Fn +- Ft >= 0
fr_r, fr_c, fr_v = [], [], []
for i in range(nF):
    for s_, row in ((1, 2 * i), (-1, 2 * i + 1)):
        fr_r += [row, row]; fr_c += [N * 9 + 1 + 2 * i, N * 9 + 1 + 2 * i + 1]; fr_v += [s_, MU]
Fr_m = sp.csr_matrix((fr_v, (fr_r, fr_c)), shape=(2 * nF, nz))
LAB_IN = labels_in + [("friction", act[i // 2], i % 2) for i in range(2 * nF)]
cache = {}
def evalz(z):
    key = z.tobytes()
    if cache.get("k") == key: return cache
    zt = torch.tensor(z); U = gather(zt)
    with torch.no_grad(): O = fnode_v(U).numpy()
    cache.update(k=key, O=O, U=U.numpy()); cache.pop("Jz", None); return cache
def Jz(z):
    c = evalz(z)
    if "Jz" not in c:
        Jn = jnode_v(torch.tensor(c["U"])).numpy(); Jb = sp.block_diag([Jn[k] for k in range(N)], format="csr"); c["Jz"] = Jb @ Gm
    return c["Jz"]
def eq_fun(z): return Seq @ evalz(z)["O"].reshape(-1)
def eq_jac(z): return (Seq @ Jz(z)).toarray()
def in_fun(z): return np.concatenate([Sin @ evalz(z)["O"].reshape(-1) + kin_c, Fr_m @ z])
def in_jac(z): return np.vstack([(Sin @ Jz(z)).toarray(), Fr_m.toarray()])
def obj(z): O = evalz(z)["O"]; return float(((O[:, 3:9] / LIMV) ** 2).sum() / N)
def obj_grad(z):
    O = evalz(z)["O"]; gO = np.zeros_like(O); gO[:, 3:9] = 2 * O[:, 3:9] / LIMV**2 / N; return np.asarray(Jz(z).T @ gO.reshape(-1)).ravel()
# ---------------- warm start from R-008 kinematics
def warm():
    x8 = z8.x; Ts = Tstep_ref; t = np.arange(N) * Ts / N; Q = np.zeros((N, 9))
    def pose(tt):
        a = R8.angles(x8, args.fr, BETA, 1.0, tt=np.atleast_1d(tt)); return a["at"][0], a["as_"][0], a["f"][0], a["y"][0]
    for k in range(N):
        at, as_, f, y = pose(t[k]); Q[k, 0] = x8[0] + v_fwd * t[k]; Q[k, 1] = y; Q[k, 2] = 10 * np.pi / 180; Q[k, 6:9] = [at, as_, f]
    pB0 = np.array(pose(0.0)[:3]); pA = np.array([pose(t[k_ds] + Ts)[:3]])[0]
    for k in range(N):
        if k <= k_ds: Q[k, 3:6] = pose(t[k] + Ts)[:3]
        else:
            s = (k - k_ds) / (N - k_ds); sm = 0.5 * (1 - np.cos(np.pi * s)); Q[k, 3:6] = pA + sm * (pB0 - pA); Q[k, 4] -= 0.35 * np.sin(np.pi * s)
    Fv = []
    for (k, p) in act: Fv += [0.0, 1.0 / max(fl[k].sum() - (1 if (fl[k, 0] and fl[k, 1]) else 0) * 0, 1)]
    return np.concatenate([Q.reshape(-1), [Ts], Fv])
z0 = warm()
bnds = [(-300, 300) if i % 9 == 0 else (15, 45) if i % 9 == 1 else (-1.2, 1.2) for i in range(N * 9)] + [(0.8 * Tstep_ref, 1.2 * Tstep_ref)]
for i in range(nF): bnds += [(-3, 3), (0, 6)]
class Stop(Exception): pass
t0 = time.time(); best = {"z": z0.copy(), "it": 0}
def cb(zk):
    best["z"] = zk.copy(); best["it"] += 1
    if best["it"] % 10 == 0: print(f"it {best['it']} f={obj(zk):.4f} eq={np.abs(eq_fun(zk)).max():.2e} in={min(in_fun(zk).min(), 0):.2e} t={time.time()-t0:.0f}s", flush=True)
    if time.time() - t0 > args.budget_s: raise Stop
print(f"N={N} nodes, nz={nz}, eq={n_eq}, ineq={n_in + 2 * nF}, schedule: A toe nodes 0..{k_ds}, B heel 0..{k_hr-1}, B flat {k_ff}..{k_hr-1}, B toe {k_ff}..{N}, mu={MU}", flush=True)
print(f"warm start: f={obj(z0):.4f} eq={np.abs(eq_fun(z0)).max():.2e} in={in_fun(z0).min():.2e} ({time.time()-t0:.1f}s)", flush=True)
cons = [{"type": "eq", "fun": eq_fun, "jac": eq_jac}, {"type": "ineq", "fun": in_fun, "jac": in_jac}]
z = z0.copy(); status = -1; msg = ""; rounds = 0
try:
    for rounds in range(1, args.rounds + 1):                    # SLSQP restarts (fresh BFGS) from the last iterate: it wanders after ~100 its
        res = minimize(obj, z, jac=obj_grad, bounds=bnds, constraints=cons, method="SLSQP", callback=cb, options={"maxiter": args.maxiter, "ftol": 1e-8})
        z = res.x; msg = res.message; status = int(res.status)
        print(f"round {rounds}: status {status} ({msg}) f={obj(z):.4f} eq={np.abs(eq_fun(z)).max():.2e} in={min(in_fun(z).min(), 0):.2e}", flush=True)
        if status == 0: break
except Stop:
    z = best["z"]; msg = "time budget reached"; status = 99
rt = time.time() - t0
# ---------------- report
O = evalz(z)["O"]; ce = eq_fun(z); ci = in_fun(z); Qn = z[:N * 9].reshape(N, 9); ts = z[N * 9]; Fv = z[N * 9 + 1:].reshape(nF, 2)
viol_eq = np.abs(ce).max(); viol_in = max(0.0, -ci.min()); labs_e = labels_eq
tau = O[:, 3:9] * 1e9; names = ["hip", "knee", "ankle"]
peak = {f"{names[j%3]}_{'AB'[j//3]}": float(np.abs(tau[:, j]).max()) for j in range(6)}
peak_ratio = {n_: peak[n_] / (LIMV[i] * 1e9) for i, n_ in enumerate(peak)}
bind = []
for i, lab in enumerate(LAB_IN):
    pass
tol_rel = 0.02
for k in range(N):
    for j in range(6):
        if abs(tau[k, j]) >= (1 - tol_rel) * LIMV[j] * 1e9: bind.append(("torque", names[j % 3] + "_" + "AB"[j // 3], k, float(abs(tau[k, j]) / (LIMV[j] * 1e9))))
        ang = O[k, 9 + j]; rng = hi[j % 3] - lo[j % 3]
        if ang <= lo[j % 3] + tol_rel * (ROM[["hip", "knee", "ank"][j % 3]][1] - ROM[["hip", "knee", "ank"][j % 3]][0]) or ang >= hi[j % 3] - tol_rel * (ROM[["hip", "knee", "ank"][j % 3]][1] - ROM[["hip", "knee", "ank"][j % 3]][0]):
            bind.append(("rom", names[j % 3] + "_" + "AB"[j // 3], k, float(ang)))
Fn = np.zeros((N, 4)); Ft = np.zeros((N, 4))
for i, (k, p) in enumerate(act): Ft[k, p], Fn[k, p] = Fv[i]
for i, (k, p) in enumerate(act):
    if Fn[k, p] > 1e-3 and abs(Ft[k, p]) >= 0.98 * MU * Fn[k, p]: bind.append(("friction", "AB"[p // 2] + ["heel", "toe"][p % 2], k, float(abs(Ft[k, p]) / (MU * Fn[k, p]))))
fnt = Fn.sum(1); util = np.where(Fn > 1e-3, np.abs(Ft) / (MU * np.maximum(Fn, 1e-9)), 0).max()
Ec = abs(eq_fun(z)); tol = 1e-3
converged = status == 0 and viol_eq < tol and viol_in < tol
verdict = "FEASIBLE" if converged else ("NOT CONVERGED" if viol_eq < 1e-2 and viol_in < 1e-2 and False else "NOT CONVERGED")
# distinguish INFEASIBLE: solver reports status 4 (inconsistent) / 8 (positive directional derivative) at a point with violations
if status in (4,) or (status != 0 and status != 99 and "Inequality constraints incompatible" in str(msg)): verdict = "INFEASIBLE"
h = ts / N; qd = (np.roll(np.vstack([Qn, np.zeros(9)])[:N], -1, 0) - np.roll(Qn, 1, 0)) / (2 * h)       # placeholder; replaced below
Ue = evalz(z)["U"]; qdn = (Ue[:, 18:27] - Ue[:, 0:9]) / (2 * h)
knee = np.stack([O[:, 10], O[:, 13]], 1)
summary = dict(verdict=verdict, solver_status=status, solver_message=str(msg), iterations=best["it"], runtime_s=rt, fr=args.fr, v=v_fwd, step_time_s=float(ts), step_time_ref_s=Tstep_ref,
               step_length_m=float(v_fwd * ts), nodes=N, max_eq_violation=float(viol_eq), max_ineq_violation=float(viol_in), objective=obj(z), mu=MU,
               torque_limits_Nm=dict(hip=LIM["hip"] * 1e9, knee=LIM["knee"] * 1e9, ankle=LIM["ankle"] * 1e9), peak_torque_Nm=peak, peak_torque_over_limit=peak_ratio,
               peak_normal_force_total_BW=float(fnt.max()), peak_normal_force_total_at_node=int(fnt.argmax()), peak_point_normal_BW=float(Fn.max()), peak_friction_utilisation=float(util),
               knee_deg_min=float(knee.min()), knee_deg_max=float(knee.max()), binding=bind, schedule=dict(k_ds=k_ds, k_ff=k_ff, k_hr=k_hr, A_toe="0..k_ds", B_heel="0..k_hr-1", B_toe="k_ff..N"),
               points=["A_heel", "A_toe", "B_heel", "B_toe"], note="leg A = old stance leg at node 0 (toe rocker, then swing), leg B = new stance leg (heel strike at node 0); node N = node 0 with A and B swapped, hip x + step length")
out = dict(summary=summary, nodes=[dict(k=k, t=float(k * h), q=Qn[k].tolist(), qd=qdn[k].tolist(), tau_Nm=dict(zip(["hipA", "kneeA", "ankleA", "hipB", "kneeB", "ankleB"], tau[k].tolist())),
           joint_deg=O[k, 9:15].tolist(), contact_N=dict(zip(["A_heel", "A_toe", "B_heel", "B_toe"], [[float(Ft[k, p] * W * 1e6), float(Fn[k, p] * W * 1e6)] for p in range(4)]))) for k in range(N)])
pathlib.Path("Tools/GaitOpt/out").mkdir(exist_ok=True)
tag = f"stride_fr{int(round(args.fr*100)):03d}"
json.dump(out, open(f"Tools/GaitOpt/out/{tag}.json", "w"), indent=1)
lines = [f"{tag}: verdict {verdict}; SLSQP status {status} ({msg}); iterations {best['it']}; runtime {rt:.0f} s",
         f"step time {ts:.3f} s (ref {Tstep_ref:.3f}), step length {v_fwd*ts:.2f} m, v {v_fwd:.3f} m/s, objective {obj(z):.4f}",
         f"max eq violation {viol_eq:.2e}, max ineq violation {viol_in:.2e}",
         "peak |tau| / limit: " + ", ".join(f"{n_} {peak[n_]:.3e}/{LIMV[i]*1e9:.1e} = {peak_ratio[n_]:.2f}" for i, n_ in enumerate(peak)),
         f"peak total Fn {fnt.max():.2f} BW (node {fnt.argmax()}), peak point Fn {Fn.max():.2f} BW, peak friction use {util:.2f} (mu {MU})",
         f"knee deg min {knee.min():.1f} max {knee.max():.1f}", f"binding ({len(bind)}): " + "; ".join(f"{b[0]} {b[1]} @node {b[2]} ({b[3]:.3g})" for b in bind[:60])]
if viol_eq > tol or viol_in > tol:
    worst = np.argsort(-np.maximum(np.abs(ce), 0))[:5]; lines.append("worst eq: " + "; ".join(f"{labs_e[i]} {ce[i]:.2e}" for i in worst))
    wi = np.argsort(ci)[:8]; lines.append("worst ineq: " + "; ".join(f"{LAB_IN[i]} {ci[i]:.2e}" for i in wi))
open(f"Tools/GaitOpt/out/{tag}.txt", "w").write("\n".join(lines) + "\n"); print("\n".join(lines))
if args.plot:
    import matplotlib; matplotlib.use("Agg"); import matplotlib.pyplot as plt
    fig, ax = plt.subplots(3, 1, figsize=(7, 7), sharex=True); tt = np.arange(N) * h
    for j in range(6): ax[0].plot(tt, tau[:, j] / (LIMV[j] * 1e9), label=["hipA", "kneeA", "ankleA", "hipB", "kneeB", "ankleB"][j])
    ax[0].set_ylabel("tau / limit"); ax[0].legend(ncol=3, fontsize=7); ax[1].plot(tt, fnt); ax[1].set_ylabel("sum Fn (BW)")
    ax[2].plot(tt, O[:, 9:15]); ax[2].set_ylabel("joint deg"); ax[2].set_xlabel("t (s)"); plt.tight_layout(); plt.savefig(f"Tools/GaitOpt/out/{tag}.png", dpi=80)
