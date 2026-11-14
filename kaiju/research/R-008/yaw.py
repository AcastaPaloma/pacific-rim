"""R-008 (2)-(4): yaw angular-momentum balance, LIPM checks and assist sizing on top of model.py.
Run from the project root: /opt/miniconda3/envs/kaiju/bin/python research/R-008/yaw.py"""
import sys, os, json, numpy as np
sys.path.insert(0, os.path.dirname(__file__)); from model import *
N = 1200
def quintic(p0, v0, p1, v1, Tw, u):
    A = np.array([[1, 0, 0, 0, 0, 0], [0, 1, 0, 0, 0, 0], [0, 0, 2, 0, 0, 0], [1, Tw, Tw**2, Tw**3, Tw**4, Tw**5],
                  [0, 1, 2*Tw, 3*Tw**2, 4*Tw**3, 5*Tw**4], [0, 0, 2, 6*Tw, 12*Tw**2, 20*Tw**3]])
    c = np.linalg.solve(A, [p0, v0, 0, p1, v1, 0]); return sum(c[k] * u**k for k in range(6))
KH = 8                                                 # harmonics kept: drive bandwidth ~1 Hz (R-005 3.2)
def lp(x, K=None):
    F = np.fft.rfft(x); F[(K or KH) + 1:] = 0; return np.fft.irfft(F, len(x))
def dper(x, dt): return (np.roll(x, -1) - np.roll(x, 1)) / (2 * dt)
def leg_cycle(z, Fr, beta, kstep, mode, clr=3.0):
    """leg R over one stride: stance from the kinematic solution until t_end, then a quintic (hip, knee) swing.
    mode 'TO': swing starts at toe-off (beta T); 'PS': thigh and knee leave the stance pattern at contralateral
    heel strike (T/2), R-007 change 3 (the toe-on-ground constraint in pre-swing is not enforced: approximation)."""
    v, s, T = gait(Fr, kstep); t = np.arange(N) * T / N; dt = T / N
    te = beta * T if mode == "TO" else 0.5 * T
    a = angles(z, Fr, beta, kstep, tt=t); at, as_, f = a["at"].copy(), a["as_"].copy(), a["f"].copy()
    kn = at - as_; i0 = int(np.ceil(te / dt)); sw = t >= te
    d1 = lambda x, i: (x[i] - x[i - 1]) / dt if i > 0 else (x[1] - x[0]) / dt   # one-sided, inside stance only
    u = t[sw] - te; Tw = T - te; j = i0 - 1
    at_sw = quintic(at[j], d1(at, j), at[0], d1(at, 0), Tw - (te - t[j]) * 0, u + (te - t[j]) * 0)
    kb = quintic(kn[j], d1(kn, j), kn[0], d1(kn, 0), Tw, u); fs = f[i0-1] + (f[0] - f[i0-1]) * smooth(u / Tw)
    for B in np.arange(0, 61, 1.0) * D:               # smallest knee bump that gives the sole clearance
        kk = kb + B * np.sin(np.pi * u / Tw) ** 2; ass = at_sw - kk
        hip = np.vstack([a["xh"][sw], a["y"][sw]])
        ank = hip + LT * np.vstack([np.sin(at_sw), -np.cos(at_sw)]) + LS * np.vstack([np.sin(ass), -np.cos(ass)])
        heel = ank + rot(fs, np.array([-HEEL, -HA])); toe = ank + rot(fs, np.array([TOE, -HA]))
        mid = (u > 0.35 * Tw) & (u < 0.65 * Tw)
        if min(heel[1][mid].min(), toe[1][mid].min()) >= clr: break
    at[sw], as_[sw], f[sw] = at_sw, ass, fs
    hip = np.vstack([a["xh"], a["y"]]); e1 = np.vstack([np.sin(at), -np.cos(at)]); e2 = np.vstack([np.sin(as_), -np.cos(as_)])
    knee = hip + LT * e1; P = {"thigh": hip + cT * e1, "shank": knee + cS * e2, "foot": knee + LS * e2 + rot(f, cF)}
    return t, dt, T, v, a, P, sw, dict(knee_peak=((at - as_)[sw]).max() / D, bump=B / D, clear=min(heel[1][mid].min(), toe[1][mid].min()))
def yaw_cycle(z, Fr, beta=0.64, kstep=1.0, mode="TO", arm_amp=0.0, trunk_k=0.0, mu=0.6, sf=1.5):
    t, dt, T, v, a, P, sw, info = leg_cycle(z, Fr, beta, kstep, mode)
    xp = z[0] + v * t; psi = a["ps"]; h = N // 2
    segs = []                                          # (mass, x_rel_pelvis, w, y) per segment per time
    for nm, m in (("thigh", mT), ("shank", mS), ("foot", mF)):
        xr = lp(P[nm][0] - xp); y = lp(P[nm][1])
        segs.append((m, xr, -HW + 0 * xr, y)); segs.append((m, np.roll(xr, h), HW + 0 * xr, np.roll(y, h)))
    yh = a["y"]; segs.append((S["pelvis"]["mass_kg"], 0 * t, 0 * t, yh + S["pelvis"]["com"][1] - HIP_Y))
    up = [("torso", 0), ("head", 0)]; arms = [k for k in S if S[k]["kind"] in ("upper_arm", "forearm", "hand")]
    shy = S["upper_arm_L"]["proximal_joint"][1]
    Iup = S["torso"]["inertia_principal_kgm2"][1] + S["head"]["inertia_principal_kgm2"][1] + sum(
        S[k]["mass_kg"] * S[k]["com"][0] ** 2 + S[k]["inertia_principal_kgm2"][1] for k in arms)
    def total_L(th):
        sg = list(segs); Lrot = S["pelvis"]["inertia_principal_kgm2"][1] * dper(psi, dt) + (
            S["torso"]["inertia_principal_kgm2"][1] + S["head"]["inertia_principal_kgm2"][1]) * dper(th, dt)
        for nm in ("torso", "head"): sg.append((S[nm]["mass_kg"], 0 * t, 0 * t, yh + S[nm]["com"][1] - HIP_Y))
        for k in arms:
            side = 1 if S[k]["side"] == "R" else -1      # R: w = -13.5; arm forward when the R leg is back
            phi = -side * arm_amp * D * np.cos(2 * np.pi * t / T); r = shy - S[k]["com"][1]; w0 = -side * abs(S[k]["com"][0])
            xa, wa = r * np.sin(phi) * np.cos(th) - w0 * np.sin(th), r * np.sin(phi) * np.sin(th) + w0 * np.cos(th)
            sg.append((S[k]["mass_kg"], xa, wa, yh + shy - HIP_Y - r * np.cos(phi)))
            Lrot = Lrot + S[k]["inertia_principal_kgm2"][1] * dper(th, dt)
        m = np.array([q[0] for q in sg]); X = np.array([q[1] for q in sg]); Wl = np.array([q[2] for q in sg]); Y = np.array([q[3] for q in sg])
        xc = (m[:, None] * X).sum(0) / M; wc = (m[:, None] * Wl).sum(0) / M; yc = (m[:, None] * Y).sum(0) / M
        Xd = np.array([dper(x, dt) for x in X]); Wd = np.array([dper(x, dt) for x in Wl])
        L = (m[:, None] * ((X - xc) * (Wd - dper(wc, dt)) - (Wl - wc) * (Xd - dper(xc, dt)))).sum(0) + Lrot
        return L, xc, yc
    L0, xc, yc = total_L(psi); thw = 0 * t
    for _ in range(6):                                 # fixed point: final L = (1 - k) L0 (arms ride on the trunk)
        L, xc, yc = total_L(psi + thw); thw = thw - np.cumsum((L - (1 - trunk_k) * L0) - (L - (1 - trunk_k) * L0).mean()) / Iup * dt; thw -= thw.mean()
    L, xc, yc = total_L(psi + thw); Mz = dper(L, dt)
    Lup = Iup * dper(psi + thw, dt); tau_w = dper(Lup, dt)
    tau = t / (beta * T); ssR = (t > (beta - 0.5) * T) & (t < 0.5 * T)
    flat = (tau >= z[7]) & (tau <= z[8]); ss = ssR | np.roll(ssR, h); flatb = flat | np.roll(flat, h)
    X, Z = np.meshgrid(np.linspace(-5, 5, 201), np.linspace(-7.5, 7.5, 301)); rf = np.hypot(X, Z).mean(); re = 2.5
    mu_u = mu / sf; der = np.sqrt(max(0, 1 - (0.2 / mu_u) ** 2))
    cap = np.where(flatb, mu_u * W * rf * der, mu_u * W * re * der)
    util = np.abs(Mz[ss]) / cap[ss]
    # LIPM sagittal check in R single support: CoP limited to heel / sole / toe of the R foot
    xcw = xc + xp; w2 = g / yc.mean(); lo = np.where(tau < z[7], -HEEL, np.where(tau > z[8], TOE, -HEEL)); hi = np.where(tau < z[7], -HEEL, TOE)
    out = np.where(xcw < lo, xcw - lo, np.where(xcw > hi, xcw - hi, 0.0)); acc = w2 * out
    dv = np.cumsum(np.where(ssR, acc, 0)) * dt
    return dict(Mz_ss=np.abs(Mz[ss]).max(), Mz_all=np.abs(Mz).max(), util=util.max(), cap_flat=mu_u * W * rf * der,
                cap_edge=mu_u * W * re * der, waist_amp=np.abs(thw).max() / D, waist_tau=np.abs(tau_w).max(), L_amp=np.abs(L).max(),
                acc_ss=np.abs(acc[ssR]).max(), dv_ss=np.ptp(dv[ssR]), zc=yc.mean(), Iup=Iup, excess=np.clip(np.abs(Mz[ss]) - cap[ss], 0, None).max(),
                psi_dd=np.abs(dper(dper(psi, dt), dt)).max() / D, **info)
def min_k(z, Fr, ks, mode="TO", arm=20, target=1.0):
    lo, hi = 0.0, 1.0
    if yaw_cycle(z, Fr, kstep=ks, mode=mode, arm_amp=arm, trunk_k=0)["util"] <= target: return 0.0
    for _ in range(12):
        mid = 0.5 * (lo + hi); (hi := mid) if yaw_cycle(z, Fr, kstep=ks, mode=mode, arm_amp=arm, trunk_k=mid)["util"] <= target else (lo := mid)
    return hi
if __name__ == "__main__" and "assist" not in sys.argv:
    SC = {"S1": dict(), "S2": dict(phs_max=25, fto_max=40, tilt_max=10)}
    for Fr in (0.023, 0.05, 0.10, 0.15, 0.20, 0.23, 0.30):
        for sc, kw in SC.items():
            for ks in (1.0, 0.85):
                r = solve(Fr, kstep=ks, flat_ss=0.75, kmin=5, starts=8, **kw)
                if r is None: print(Fr, sc, ks, "no solution", flush=True); continue
                mg, inf = report(r.x, Fr, 0.64, ks); z = r.x
                y0 = yaw_cycle(z, Fr, kstep=ks, mode="TO"); ya = yaw_cycle(z, Fr, kstep=ks, mode="TO", arm_amp=20)
                yp = yaw_cycle(z, Fr, kstep=ks, mode="PS", arm_amp=20)
                k1 = min_k(z, Fr, ks); k2 = min_k(z, Fr, ks, target=0.67); yk = yaw_cycle(z, Fr, kstep=ks, arm_amp=20, trunk_k=k2)
                yf = yaw_cycle(z, Fr, kstep=ks, arm_amp=20, trunk_k=1.0)
                print(f"Fr {Fr:.3f} {sc} k{ks:.2f} | margin {z[12]:+5.1f} {min(mg, key=mg.get)} hs {inf['hs_pitch']:.0f} to {inf['toe_off_pitch']:.0f} tilt {inf['tilt']:.0f} psi {inf['psi']:.1f} hr {inf['heel_rise_at']:.2f}"
                      f" | Mz none {y0['Mz_ss']:.2e} arms {ya['Mz_ss']:.2e} PS+arms {yp['Mz_ss']:.2e} util {ya['util']:.2f}"
                      f" | trunk share for util<=1: {k1:.2f}, <=0.67: {k2:.2f} (waist {yk['waist_amp']:.1f} deg, {yk['waist_tau']:.1e} N m); full: waist {yf['waist_amp']:.1f} deg {yf['waist_tau']:.1e}"
                      f" | excess(arms only) {ya['excess']:.2e} | LIPM a {ya['acc_ss']:.2f} m/s2 dv {ya['dv_ss']:.2f} | psi_dd {ya['psi_dd']:.1f} deg/s2 | swing knee {ya['knee_peak']:.0f} clear {ya['clear']:.1f} m", flush=True)
    print(f"caps (mu 0.6/1.5, Ft 0.2 W): flat {ya['cap_flat']:.2e} edge {ya['cap_edge']:.2e} N m; I_upper {ya['Iup']:.2e} kg m2; KH {KH}")
def assist_table():
    """yaw-moment shortfall the stance foot cannot supply (mu 0.6 / 1.5), S2 pattern, nominal step, arms +-20 deg"""
    for Fr in (0.10, 0.20):
        r = solve(Fr, flat_ss=0.75, kmin=5, starts=8, phs_max=25, fto_max=40, tilt_max=10); z = r.x
        for k in (0.0, 0.5, 0.75):
            y = yaw_cycle(z, Fr, arm_amp=20, trunk_k=k); ym = yaw_cycle(z, Fr, arm_amp=20, trunk_k=k, sf=1.0)
            print(f"assist Fr {Fr} trunk share {k}: peak demand {y['Mz_ss']:.2e} N m, shortfall {y['excess']:.2e} N m"
                  f" = {y['excess']/1e9:.2f} of the 1e9 ankle limit = {y['excess']/(W*l):.4f} W l; with mu 0.6 and no safety factor {ym['excess']:.2e}")
if __name__ == "__main__" and "assist" in sys.argv: assist_table()
