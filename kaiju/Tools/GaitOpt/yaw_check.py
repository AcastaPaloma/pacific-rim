"""T-005 r3 8.2: yaw and lateral check of the 8.1b sagittal stride (Fr 0.10, out/stride_fr010_tc.json).
Run from the project root: /opt/miniconda3/envs/kaiju/bin/python Tools/GaitOpt/yaw_check.py

Reuse: constants and helpers are imported from research/R-008 (model.py, yaw.py), which are not edited.
yaw.py's yaw_cycle() builds its own legs from the R-008 kinematic schedule and keeps total_L() as a closure, so it
cannot take an external trajectory without rewriting it. Here the same formulas (segment list, L about the CoM,
fixed-point trunk share, tau_w = d/dt(I_up * dpsi), caps mu_u W r sqrt(1-(Ft/mu_u Fn)^2)) are re-applied to the
8.1b trajectory: legs from q (thigh/shank/foot angles), pelvis/torso/head/arms from the body JSON."""
import sys, os, json, numpy as np
ROOT = os.getcwd()
sys.path.insert(0, os.path.join(ROOT, "research", "R-008"))
import model as Mo
from model import S, W, M, g, D, HW, LT, LS, HIP_Y, mT, mS, mF, cT, cS, cF, rot, WAIST_YAW
from yaw import dper, lp

KH = 9                       # harmonics kept; yaw.py keeps 8 of a 7.6 s stride (~1 Hz); 9 of 9.09 s keeps the same ~1 Hz
NF = 640                     # fine grid per stride
OUT = os.path.join(ROOT, "Tools/GaitOpt/out/stride_fr010_tc.json")
d = json.load(open(OUT)); sm = d["summary"]; nd = d["nodes"]; n0 = len(nd)
Ts, s_len, v = sm["step_time_s"], sm["step_length_m"], sm["v"]; T = 2 * Ts; N2 = 2 * n0; dtn = T / N2
mu = sm["mu"]; lim = sm["torque_limits_Nm"]
q = np.array([x["q"] for x in nd]); C = {k: np.array([x["contact_N"][k] for x in nd]) for k in nd[0]["contact_N"]}
tn = np.arange(N2) * dtn
# ---- stride from one step: R leg = B (step 1) then A (step 2); L leg = A then B; node 0: B = new stance leg = R (yaw.py t=0)
hx = np.concatenate([q[:, 0], q[:, 0] + s_len]) - v * tn          # periodic part of hip x (drift cancels in L)
hy = np.concatenate([q[:, 1], q[:, 1]])
angA, angB = q[:, 3:6], q[:, 6:9]
angR = np.vstack([angB, angA]); angL = np.vstack([angA, angB])
def leg_pts(a, hx, hy):
    at, as_, f = a[:, 0], a[:, 1], a[:, 2]
    hip = np.vstack([hx, hy]); e1 = np.vstack([np.sin(at), -np.cos(at)]); e2 = np.vstack([np.sin(as_), -np.cos(as_)])
    knee = hip + LT * e1
    return {"thigh": hip + cT * e1, "shank": knee + cS * e2, "foot": knee + LS * e2 + rot(f, cF)}, knee + LS * e2
hxw = np.concatenate([q[:, 0], q[:, 0] + s_len])
PR, ankR = leg_pts(angR, hxw, hy); PL, ankL = leg_pts(angL, hxw, hy)
sole_min = min((ankR[1] - Mo.HA).min(), (ankL[1] - Mo.HA).min())
def up(x, K=None):                                # band-limit (K harmonics) and FFT-upsample a periodic signal to NF samples
    F = np.fft.rfft(x); F[(K or KH) + 1:] = 0; G = np.zeros(NF // 2 + 1, complex); G[:len(F)] = F; return np.fft.irfft(G, NF) * NF / len(x)
tf = np.arange(NF) * T / NF; dt = T / NF
def prep(P, drift_cols=True):
    out = {}
    for nm in ("thigh", "shank", "foot"):
        out[nm] = (up(P[nm][0] - v * tn), up(P[nm][1]))
    return out
GR, GL = prep(PR), prep(PL); hxf, hyf = up(hx), up(hy)
# ---- actual contact forces per leg over the stride (columns: heel/toe [Fx, Fn]); linear periodic interpolation to the fine grid
def cont(R_first):
    R = {"heel": np.vstack([C["B_heel"], C["A_heel"]]), "toe": np.vstack([C["B_toe"], C["A_toe"]])}
    L = {"heel": np.vstack([C["A_heel"], C["B_heel"]]), "toe": np.vstack([C["A_toe"], C["B_toe"]])}
    f = lambda a: np.array([np.interp(tf, tn, a[:, k], period=T) for k in (0, 1)])
    return {nm: {p: f(a[p]) for p in a} for nm, a in (("R", R), ("L", L))}
CF = cont(True)
# ---- body constants as in yaw.py
arms = [k for k in S if S[k]["kind"] in ("upper_arm", "forearm", "hand")]; shy = S["upper_arm_L"]["proximal_joint"][1]
Iup = S["torso"]["inertia_principal_kgm2"][1] + S["head"]["inertia_principal_kgm2"][1] + sum(
    S[k]["mass_kg"] * S[k]["com"][0] ** 2 + S[k]["inertia_principal_kgm2"][1] for k in arms)
def segs_legs(psi_deg):
    ps = psi_deg * D * np.cos(2 * np.pi * tf / T); sg = []
    for G, side in ((GR, -1), (GL, +1)):             # R leg at w = -HW, L leg at w = +HW (yaw.py)
        for nm, m in (("thigh", mT), ("shank", mS), ("foot", mF)):
            sg.append((m, G[nm][0] - side * HW * np.sin(ps), side * HW + 0 * tf, G[nm][1]))
    return sg, ps
def total_L(sg_legs, ps, th, arm_amp):
    sg = list(sg_legs); yh = hyf; z0 = 0 * tf
    sg.append((S["pelvis"]["mass_kg"], hxf, z0, yh + S["pelvis"]["com"][1] - HIP_Y))
    Lrot = S["pelvis"]["inertia_principal_kgm2"][1] * dper(ps, dt) + (
        S["torso"]["inertia_principal_kgm2"][1] + S["head"]["inertia_principal_kgm2"][1]) * dper(th, dt)
    for nm in ("torso", "head"): sg.append((S[nm]["mass_kg"], hxf, z0, yh + S[nm]["com"][1] - HIP_Y))
    for k in arms:
        side = 1 if S[k]["side"] == "R" else -1
        phi = -side * arm_amp * D * np.cos(2 * np.pi * tf / T); r = shy - S[k]["com"][1]; w0 = -side * abs(S[k]["com"][0])
        xa = r * np.sin(phi) * np.cos(th) - w0 * np.sin(th); wa = r * np.sin(phi) * np.sin(th) + w0 * np.cos(th)
        sg.append((S[k]["mass_kg"], hxf + xa, wa, yh + shy - HIP_Y - r * np.cos(phi)))
        Lrot = Lrot + S[k]["inertia_principal_kgm2"][1] * dper(th, dt)
    m = np.array([a[0] for a in sg]); X = np.array([a[1] for a in sg]); Wl = np.array([a[2] for a in sg]); Y = np.array([a[3] for a in sg])
    xc = (m[:, None] * X).sum(0) / M; wc = (m[:, None] * Wl).sum(0) / M; yc = (m[:, None] * Y).sum(0) / M
    Xd = np.array([dper(x, dt) for x in X]); Wd = np.array([dper(x, dt) for x in Wl])
    L = (m[:, None] * ((X - xc) * (Wd - dper(wc, dt)) - (Wl - wc) * (Xd - dper(xc, dt)))).sum(0) + Lrot
    return L, yc, m.sum()
# ---- friction caps (yaw.py formulas), flat / edge radii exactly as yaw.py
Xg, Zg = np.meshgrid(np.linspace(-5, 5, 201), np.linspace(-7.5, 7.5, 301)); rf = np.hypot(Xg, Zg).mean(); re = 2.5
mu_u = mu                                            # = 0.6 / 1.5, the same derated mu as yaw.py (mu / sf)
cap_flat_y = mu_u * W * rf * np.sqrt(1 - (0.2 / mu_u) ** 2); cap_edge_y = mu_u * W * re * np.sqrt(1 - (0.2 / mu_u) ** 2)
# lateral LIPM (section 2), needed for the combined friction case
def lateral(Tstep, zc, hw):
    w = np.sqrt(g / zc); C_, S_ = np.cosh(w * Tstep), np.sinh(w * Tstep)
    # CoM y(t) = hw + (y0-hw) cosh + (u0/w) sinh about the stance foot at +hw; mirror at the exchange: y(Ts) = -y0, u(Ts) = -u0
    A = np.array([[C_ + 1, S_ / w], [(C_ + 1) * 0 + w * S_, C_ + 1]]); b = np.array([hw * (C_ - 1), w * S_ * hw])
    y0, u0 = np.linalg.solve(A, b); tt = np.linspace(0, Tstep, 400); yy = hw + (y0 - hw) * np.cosh(w * tt) + u0 / w * np.sinh(w * tt)
    uu = (y0 - hw) * w * np.sinh(w * tt) + u0 * np.cosh(w * tt); aa = w ** 2 * (yy - hw)
    return dict(w=w, y0=y0, u0=u0, tt=tt, y=yy, u=uu, a=aa, a_peak=np.abs(aa).max(), a_rms=np.sqrt((aa ** 2).mean()),
                dv_step=np.ptp(uu), dv_peak=np.abs(uu).max(), y_max=np.abs(yy).max())
def run(psi_deg, arm_amp, trunk_k, cap_mode, lat=None):
    sgl, ps = segs_legs(psi_deg); Lz = lambda th: total_L(sgl, ps, th, arm_amp)
    L0 = Lz(ps)[0]; thw = 0 * tf
    for _ in range(6):
        L = Lz(ps + thw)[0]; e = L - (1 - trunk_k) * L0; thw = thw - np.cumsum(e - e.mean()) / Iup * dt; thw -= thw.mean()
    L, yc, Mtot = Lz(ps + thw); Mz = dper(L, dt); tau_w = dper(Iup * dper(ps + thw, dt), dt)
    # stance foot: single support = other foot unloaded
    nR = CF["R"]["heel"][1] + CF["R"]["toe"][1]; nL = CF["L"]["heel"][1] + CF["L"]["toe"][1]
    thr = 0.01 * W; isn = (np.arange(NF) % (NF // N2)) == 0     # forces are nodal (impulse-like at toe-off): evaluate util at the 80 nodes only
    ssR = (nR > thr) & (nL <= thr) & isn; ssL = (nL > thr) & (nR <= thr) & isn; ss = ssR | ssL
    cap = np.full(NF, np.nan); ft = np.zeros(NF); fn = np.zeros(NF); flat = np.zeros(NF, bool)
    for key, msk in (("R", ssR), ("L", ssL)):
        h, t_ = CF[key]["heel"], CF[key]["toe"]; fnn = h[1] + t_[1]; ftt = np.abs(h[0] + t_[0])
        fl = (h[1] > thr) & (t_[1] > thr); fn[msk] = fnn[msk]; ft[msk] = ftt[msk]; flat[msk] = fl[msk]
    if lat is not None and cap_mode == "actual+lat":    # lateral LIPM shear M a(t) on the stance foot, step 1 and 2 identical
        k = np.minimum((tf % Ts) / Ts * 399, 399).astype(int); fy = fn * np.abs(lat["a"][k]) / g   # lateral LIPM with variable Fn: Fy = Fn (y - p)/zc = Fn a/g
        ft = np.hypot(ft, fy)
    if cap_mode == "yaw.py":
        fn = np.where(ss, W, 0.0); ft = np.where(ss, 0.2 * W, 0.0)
    r = np.where(flat, rf, re)      # flat/edge from the JSON contacts (heel and toe both loaded = flat)
    ratio = np.where(ss, ft / np.maximum(mu_u * fn, 1), 0); cap = np.where(ss, mu_u * fn * r * np.sqrt(np.clip(1 - ratio ** 2, 0, 1)), np.nan)
    util = np.abs(Mz[ss]) / np.maximum(cap[ss], 1.0)
    return dict(Mz_ss=np.abs(Mz[ss]).max(), Mz_all=np.abs(Mz).max(), util=util.max(), waist_amp=np.abs(thw).max() / D,
                waist_tau=np.abs(tau_w).max(), zc=yc.mean(), Mtot=Mtot, ss_frac=ss.sum() / N2, cap_min=np.nanmin(cap), cap_med=np.nanmedian(cap),
                flat_frac=(flat[ss]).mean(), t_bind=tf[ss][np.argmax(util)], fn_bind=fn[ss][np.argmax(util)] / W, cap_bind=cap[ss][np.argmax(util)], Mz=Mz, ss=ss, fric_use_max=np.nanmax(np.where(ss, ft / np.maximum(mu_u * fn, 1), 0)))
def min_k(psi, mode, target, lat):
    if run(psi, 20, 0, mode, lat)["util"] <= target: return 0.0
    if run(psi, 20, 1.0, mode, lat)["util"] > target: return float("nan")
    lo, hi = 0.0, 1.0
    for _ in range(14):
        mid = 0.5 * (lo + hi)
        if run(psi, 20, mid, mode, lat)["util"] <= target: hi = mid
        else: lo = mid
    return hi
if __name__ == "__main__":
    import time; t0 = time.time(); R = []; P = R.append
    base = run(0, 0, 0, "actual"); zc = base["zc"]
    lat = lateral(Ts, zc, HW)
    P(f"yaw_check: 8.1b stride Fr 0.10, step time {Ts:.3f} s (stride {T:.3f} s), step {s_len:.2f} m, v {v:.3f} m/s; total mass check {base['Mtot']:.4e} vs {M:.4e} kg")
    P(f"geometry: hip half-width HW {HW} m, I_upper {Iup:.3e} kg m2, KH {KH}, fine grid {NF}, mu_u {mu_u:.2f}, W {W:.3e} N; sole min height over both stance feet (sanity, ~0) {sole_min:.2f} m")
    P(f"caps yaw.py-style (Fn=W, Ft=0.2W): flat {cap_flat_y:.3e} edge {cap_edge_y:.3e} N m (rf {rf:.3f} m, re {re} m)")
    P(f"single-support share of the stride {base['ss_frac']:.2f}; flat-foot share of single support {base['flat_frac']:.2f}; waist ROM +-{WAIST_YAW[1]} deg; waist force_limit: not defined in mech_frame_balanced.json or model.py/yaw.py (only hip/knee/ankle limits {lim} in the GaitOpt data)")
    tab = {}
    for psi in (0.0, 8.0):
        for mode in ("yaw.py", "actual", "actual+lat"):
            y0 = run(psi, 0, 0, mode, lat); ya = run(psi, 20, 0, mode, lat)
            k1 = min_k(psi, mode, 1.0, lat); k2 = min_k(psi, mode, 0.67, lat)
            yk1 = run(psi, 20, k1, mode, lat) if k1 == k1 else None; yk2 = run(psi, 20, k2, mode, lat) if k2 == k2 else None
            yf = run(psi, 20, 1.0, mode, lat)
            tab[(psi, mode)] = dict(y0=y0, ya=ya, k1=k1, k2=k2, yk1=yk1, yk2=yk2, yf=yf)
            f = lambda y: "n/a" if y is None else f"waist {y['waist_amp']:.1f} deg, tau {y['waist_tau']:.2e} N m (max Mz_ss {y['Mz_ss']:.2e}, util {y['util']:.2f})"
            P(f"psi {psi:.0f} deg | caps {mode:10s} | Mz_ss none {y0['Mz_ss']:.3e} arms {ya['Mz_ss']:.3e} | util none {y0['util']:.2f} arms {ya['util']:.2f}"
              f" | trunk share <=1: {k1:.2f} [{f(yk1)}] <=0.67: {k2:.2f} [{f(yk2)}] | full trunk (k=1): {f(yf)} | min cap in SS {ya['cap_min']:.2e} med {ya['cap_med']:.2e}, max friction use {ya['fric_use_max']:.2f}; util peak at t {ya['t_bind']:.2f} s (stance Fn {ya['fn_bind']:.2f} W, cap {ya['cap_bind']:.2e})")
    P(f"lateral LIPM (step time {Ts:.3f} s, feet at +-{HW} m, zc {zc:.2f} m, omega {lat['w']:.3f} 1/s, SS pivot over the whole step): orbit start y0 {lat['y0']:+.2f} m, u0 {lat['u0']:+.3f} m/s;"
      f" peak |a| {lat['a_peak']:.3f} m/s2 ({lat['a_peak']/g:.3f} g), rms {lat['a_rms']:.3f}; lateral dv over a step (ptp u) {lat['dv_step']:.3f} m/s, peak |u| {lat['dv_peak']:.3f}; max |y| {lat['y_max']:.2f} m"
      f"; shear/Fn at 1 W: {lat['a_peak']/g:.3f} vs mu_u {mu_u:.2f}")
    # friction cone per foot at every loaded node: sagittal alone vs sagittal + lateral LIPM shear (Fy = Fn a/g, phase: orbit starts at step boundary)
    cs, cl = [], []
    for key in "RL":
        h, t_ = CF[key]["heel"], CF[key]["toe"]
        for i in range(0, NF, NF // N2):
            for pt in (h, t_):
                if pt[1][i] > 0.01 * W:
                    kk = int(min((tf[i] % Ts) / Ts * 399, 399)); sg_ = abs(pt[0][i]) / (mu_u * pt[1][i]); lt = abs(lat["a"][kk]) / g / mu_u
                    cs.append((sg_, tf[i])); cl.append((np.hypot(sg_, lt), tf[i]))
    ms, ml = max(cs), max(cl)
    P(f"friction cone use per contact point (Ft/(mu_u Fn)): sagittal only max {ms[0]:.3f} at t {ms[1]:.2f} s; with lateral LIPM shear max {ml[0]:.3f} at t {ml[1]:.2f} s; lateral part alone {lat['a_peak']/g/mu_u:.3f} at its peak, {np.abs(lat['a']).min()/g/mu_u:.3f} at its minimum")
    P("yaw.py own Fr 0.100 S1 k1.00 row: Mz none 4.10e+08 arms 3.44e+08 util 4.05; trunk share <=1: 0.75, <=0.67: 0.83 (waist 18.1 deg, 2.2e+08 N m); full: waist 21.7 deg 2.7e+08; LIPM (sagittal) a 0.48 m/s2 dv 0.12; step 24.4 m, stride 7.57 s, psi 8.0")
    P(f"runtime {time.time()-t0:.1f} s")
    open(os.path.join(ROOT, "Tools/GaitOpt/out/yaw_fr010.txt"), "w").write("\n".join(R) + "\n"); print("\n".join(R))
