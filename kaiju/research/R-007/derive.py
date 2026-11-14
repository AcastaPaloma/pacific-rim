"""R-007 derivations. Run: /opt/miniconda3/envs/kaiju/bin/python research/R-007/derive.py"""
import json, numpy as np
from scipy.optimize import minimize
J = json.load(open("Assets/Game/Config/Mech/mech_frame_balanced.json"))
S = {s["id"]: s for s in J["segments"]}
g, M = 9.81, J["total_mass_kg"]; W = M*g
l = 42.4                      # R-001 leg length (hip joint in JSON at 42.0 m)
LT, LS, HA = 19.0, 19.0, 4.0  # thigh, shank, ankle height
HEEL, TOE = 5.25, 9.75
deg = np.pi/180
def gait(Fr):
    v = np.sqrt(Fr*g*l); lam = 2.3*Fr**0.3*l; return v, lam, lam/2, lam/v
print("== gait targets (R-001 relations)")
for Fr in (0.02, 0.17, 0.20, 0.23):
    v, lam, s, T = gait(Fr)
    print(f"Fr {Fr:.2f}: v {v:.2f} m/s step {s:.1f} m ({s/l:.2f} l) T_stride {T:.2f} s T_step {T/2:.2f} s cadence {120/T:.1f}/min;"
          f" step range for T ratio 0.85-1.15: {0.85*s:.1f}-{1.15*s:.1f} m")
print("measured: Fr", 2.86**2/(g*l), "step 0.3 l =", 0.3*l, "T_step", 0.3*l/2.86)

# ---------- 1. kinematic reach (sagittal, pelvis upright, double-support instant) ----------
ROM = dict(hip=(-20, 120), knee=(0, 135), ank=(-20, 40), hiprot=35)
def reach(flat, psi_max, knee_lock, kmin=0.0, margin=0.0, pf=(-25, 0), pr=(0, 40)):
    # x = [Y, at_r, as_r, p_r, at_f, as_f, p_f, psi] angles in deg; as = shank angle (ankle fwd of knee >0)
    def pts(x):
        Y, atr, asr, prr, atf, asf, pff, psi = x
        out = []
        for side, at, as_, p in (("r", atr, asr, prr), ("f", atf, asf, pff)):
            hz = (6.5*np.sin(psi*deg))*(1 if side == "f" else -1)
            az = hz + LT*np.sin(at*deg) + LS*np.sin(as_*deg)
            ay = Y - LT*np.cos(at*deg) - LS*np.cos(as_*deg)
            zc = TOE if side == "r" else -HEEL
            if flat: p = 0
            cz = az + zc*np.cos(p*deg) - HA*np.sin(p*deg)
            cy = ay - zc*np.sin(p*deg) - HA*np.cos(p*deg)
            out.append((cz, cy, at, at-as_, as_+p, p))
        return out
    def step(x):
        (rz, _, *_), (fz, *_), = pts(x)
        return fz - (rz - (HEEL+TOE))
    m = margin
    cons = [{"type": "eq", "fun": lambda x, i=i: pts(x)[i][1]} for i in (0, 1)]
    for i in (0, 1):
        cons += [{"type": "ineq", "fun": lambda x, i=i: pts(x)[i][2]-(ROM["hip"][0]+m)},
                 {"type": "ineq", "fun": lambda x, i=i: (ROM["hip"][1]-m)-pts(x)[i][2]},
                 {"type": "ineq", "fun": lambda x, i=i: pts(x)[i][3]-max(kmin, ROM["knee"][0])},
                 {"type": "ineq", "fun": lambda x, i=i: (ROM["knee"][1]-m)-pts(x)[i][3]},
                 {"type": "ineq", "fun": lambda x, i=i: pts(x)[i][4]-(ROM["ank"][0]+m)},
                 {"type": "ineq", "fun": lambda x, i=i: (ROM["ank"][1]-m)-pts(x)[i][4]}]
        if knee_lock: cons.append({"type": "eq", "fun": lambda x, i=i: pts(x)[i][3]-kmin})
    b = [(10, 42), (-60, 60), (-60, 60), pr, (-60, 60), (-60, 60), pf, (0, psi_max)]
    best = None
    for s0 in range(25):
        x0 = [np.random.uniform(lo, hi) for lo, hi in b]
        r = minimize(lambda x: -step(x), x0, bounds=b, constraints=cons, method="SLSQP", options={"maxiter": 500})
        if r.success and (best is None or -r.fun > -best.fun): best = r
    P = pts(best.x)
    return -best.fun, best.x, [(round(p[2], 1), round(p[3], 1), round(p[4], 1), round(p[5], 1)) for p in P]
np.random.seed(1)
print("== 1. max step (hip, knee, ankle, foot pitch) per leg [rear, front]")
for name, kw in [("A flat, knees free", dict(flat=True, psi_max=0, knee_lock=False)),
                 ("A' flat, stance knees >=20", dict(flat=True, psi_max=0, knee_lock=False, kmin=20)),
                 ("A'' flat, stance knees >=30", dict(flat=True, psi_max=0, knee_lock=False, kmin=30)),
                 ("B rollover, knees straight", dict(flat=False, psi_max=0, knee_lock=True)),
                 ("C B + pelvic rotation <=10", dict(flat=False, psi_max=10, knee_lock=True)),
                 ("D C + knees free", dict(flat=False, psi_max=10, knee_lock=False)),
                 ("D with 5 deg ROM margin", dict(flat=False, psi_max=10, knee_lock=False, margin=5)),
                 ("A with 5 deg ROM margin", dict(flat=True, psi_max=0, knee_lock=False, margin=5)),
                 ("D, toe-off pitch<=20, heel-strike>=-15, margin 5", dict(flat=False, psi_max=10, knee_lock=False, margin=5, pf=(-15, 0), pr=(0, 20)))]:
    s, x, P = reach(**kw)
    print(f"{name:52s} step {s:5.1f} m = {s/l:.2f} l  pelvis {x[0]:.1f} m psi {x[7]:.1f}  {P}")

# ---------- 2. swing leg ----------
legs = [S["thigh_L"], S["shank_L"], S["foot_L"]]
hip = np.array([-6.5, 42.0, 0.0]); knee = np.array([-6.5, 23.0, 0.0])
def leg_I(kflex):
    c = np.cos(kflex*deg); s_ = np.sin(kflex*deg); I = 0; md = 0; m = 0
    for seg in legs:
        r = np.array(seg["com"]) - hip; Ic = seg["inertia_principal_kgm2"][0]
        if seg["kind"] != "thigh":   # rotate about knee (shank swings back)
            rk = np.array(seg["com"]) - knee
            rk = np.array([rk[0], rk[1]*c + rk[2]*s_*0 + 0, rk[2]*c - rk[1]*s_*-1*-1])
            y, z = (np.array(seg["com"]) - knee)[1:]; rk = np.array([0, y*c + z*s_, z*c - y*s_*-1*0 - (-y)*s_*0 - y*0])
            y2 = y*c - z*s_*0; z2 = z*c
            # explicit: shank vector (y<0) rotated back by kflex: z' = z cos - (-y) sin, y' = y cos ... (exact below)
            yr = y*np.cos(kflex*deg) + z*np.sin(kflex*deg)*0
            ang = kflex*deg; yy = y*np.cos(ang) - z*np.sin(ang); zz = z*np.cos(ang) + y*np.sin(ang)
            r = np.array([0, knee[1]-hip[1]+yy, zz])
        m += seg["mass_kg"]; md += seg["mass_kg"]*r[1:]; I += Ic + seg["mass_kg"]*(r[1]**2 + r[2]**2)
    d = np.linalg.norm(md/m); return m, I, d
print("== 2. swing leg")
for kf in (0, 30, 60):
    m, I, d = leg_I(kf); Tp = 2*np.pi*np.sqrt(I/(m*g*d))
    print(f"knee {kf:2d} deg: m {m:.2e} kg ({m/M:.0%}) I_hip {I:.3e} d {d:.1f} m  pendulum period {Tp:.2f} s half {Tp/2:.2f} s")
v, lam, s, T = gait(0.20)
for beta in (0.60, 0.65, 0.72): print(f"beta {beta}: stance {beta*T:.2f} s swing {(1-beta)*T:.2f} s DS/transition {(2*beta-1)/2*T:.2f} s")
mL, I0, d0 = leg_I(0); mL, I60, d60 = leg_I(60); Ieff, deff = 0.5*(I0+I60), 0.5*(d0+d60)
def stride_traj(T, beta, th_a, n=4000):
    """hip angle over a stride: stance linear from +th_a to -th_a, swing quintic back with matched end velocities."""
    t = np.linspace(0, T, n, endpoint=False); Tst = beta*T; Tsw = T-Tst; w = -2*th_a/Tst
    th = np.where(t < Tst, th_a + w*t, 0.0)
    ts = t[t >= Tst]-Tst
    A = np.array([[1,0,0,0,0,0],[0,1,0,0,0,0],[0,0,2,0,0,0],[1,Tsw,Tsw**2,Tsw**3,Tsw**4,Tsw**5],
                  [0,1,2*Tsw,3*Tsw**2,4*Tsw**3,5*Tsw**4],[0,0,2,6*Tsw,12*Tsw**2,20*Tsw**3]])
    c = np.linalg.solve(A, [-th_a, w, 0, th_a, w, 0])
    th[t >= Tst] = sum(c[k]*ts**k for k in range(6)); dt = T/n
    thd = np.gradient(th, dt); thdd = np.gradient(thd, dt); return t, th, thd, thdd, Tst
th_a = 20*deg
print(f"swing model: hip excursion +-20 deg, I_eff {Ieff:.2e} (knee 0..60 mean), d {deff:.1f} m")
res = {}
for beta in (0.60, 0.65, 0.72):
    t, th, thd, thdd, Tst = stride_traj(T, beta, th_a); sw = t >= Tst
    tau = Ieff*thdd + mL*g*deff*np.sin(th)
    P = tau*thd
    # yaw moment from both legs: L_y = 6.5 m (zdot_L - zdot_R), leg CoM fore-aft vel = d thd cos th
    zd = deff*thd*np.cos(th); zd_other = np.roll(zd, len(t)//2)
    Ly = 6.5*mL*(zd - zd_other); My = np.gradient(Ly, t[1]-t[0])
    res[beta] = (t, Ly, My)
    print(f"beta {beta}: swing {T-Tst:.2f} s; peak hip tau {abs(tau[sw]).max():.2e} N m (inertial part {abs(Ieff*thdd[sw]).max():.2e});"
          f" peak +P {P[sw].max()/1e6:.0f} MW; mean +P over stride {np.clip(P,0,None).mean()/1e6:.0f} MW; peak thd {thd.max():.2f} rad/s;"
          f" leg yaw L peak {abs(Ly).max():.2e}; yaw moment peak {abs(My).max():.2e} N m")
# friction-limited free moment of the 15 x 10 sole under 1 BW, uniform pressure
X, Z = np.meshgrid(np.linspace(-5, 5, 401), np.linspace(-7.5, 7.5, 601)); rbar = np.hypot(X, Z).mean()
Mmax = 0.6*W*rbar
print(f"== yaw capacity: mean r {rbar:.2f} m, M_max(mu .6, 1 BW) {Mmax:.2e} N m; with Ft=0.2W: {Mmax*np.sqrt(1-(0.2/0.6)**2):.2e};"
      f" with M-002 1.5 margin (mu_use .4, Ft .2W): {0.4*W*rbar*np.sqrt(1-(0.2/0.4)**2):.2e}")
# heel-only contact (rear 3 m of sole) and toe-only (front 3 m)
Xh, Zh = np.meshgrid(np.linspace(-5, 5, 201), np.linspace(0, 3, 61)); print(f"   heel/toe strip 3 m x 10 m: M_max {0.6*W*np.hypot(Xh, Zh-1.5).mean():.2e}")
# arms: counter-swing
arms = [S["upper_arm_L"], S["forearm_L"], S["hand_L"]]; sh = np.array(S["upper_arm_L"]["proximal_joint"])
mA = sum(a["mass_kg"] for a in arms); dA = sum(a["mass_kg"]*(sh[1]-a["com"][1]) for a in arms)/mA
IA = sum(a["inertia_principal_kgm2"][0] + a["mass_kg"]*(sh[1]-a["com"][1])**2 for a in arms)
TA = 2*np.pi*np.sqrt(IA/(mA*g*dA))
print(f"== arm: m {mA:.2e} d {dA:.1f} m I_sh {IA:.2e} pendulum period {TA:.2f} s (stride {T:.2f} s)")
t, Ly, My = res[0.65]; wS = 2*np.pi/T
a1 = 2/len(t)*np.sum(Ly*np.exp(-1j*wS*t)); fund = np.real(a1*np.exp(1j*wS*t))
for frac in (0.5, 1.0):
    Lres = Ly - frac*fund; Mres = np.gradient(Lres, t[1]-t[0])
    phi = np.arcsin(min(1, frac*abs(a1)/(2*13.5*mA*dA*wS)))  # both arms, L = 2*13.5*mA*dA*phidot
    print(f"arm cancels {frac:.0%} of fundamental: shoulder amplitude {phi/deg:.1f} deg; residual yaw moment peak {abs(Mres).max():.2e} (from {abs(My).max():.2e})")
kA = IA*wS**2 - mA*g*dA
print(f"   arm drive stiffness above pendulum {kA:.2e} N m/rad; per-arm peak torque at 25 deg {kA*25*deg:.2e}; power ~{0.5*kA*(25*deg)**2*wS/1e6:.1f} MW")
Iup = 3.78e8; th_tr = np.cumsum((Ly-fund)/Iup)*(t[1]-t[0]); th_tr -= th_tr.mean()
th_tr0 = np.cumsum(Ly/Iup)*(t[1]-t[0]); th_tr0 -= th_tr0.mean()
print(f"trunk counter-rotation to absorb all leg L: amplitude {abs(th_tr0).max()/deg:.0f} deg; after full arm fundamental cancel: {abs(th_tr).max()/deg:.1f} deg (waist ROM +-30)")
# pelvic rotation effect: psi=8 deg gives 13 sin(8)=1.8 m step -> hip excursion reduction
print(f"pelvic rotation 8 deg: +{13*np.sin(8*deg):.1f} m step; hip excursion for 30 m from asin(15/38)={np.arcsin(15/38)/deg:.1f} to asin(14.1/38)={np.arcsin((15-0.9)/38)/deg:.1f} deg")
# pitch reaction vs CoP lever
print(f"pitch capacity single support 1 BW: heel {W*HEEL:.2e} toe {W*TOE:.2e} N m")

# ---------- touchdown ----------
print("== touchdown (IP, CoM h 40 m, CoP at rear toe / front heel = rollover; compass = CoP under ankles)")
h = 40.0
for lab, vv, ss in (("Fr .20", v, s), ("measured", 2.86, 0.3*l)):
    for mode, sep in (("compass", ss), ("rollover", max(ss-HEEL-TOE, 0))):
        a = np.arctan(sep/2/h); vy = vv*np.sin(a)
        out = f"{lab} {mode}: half-angle {a/deg:.1f} deg vy {vy:.2f} m/s"
        for beta in (0.60, 0.65, 0.72):
            Tds = (2*beta-1)/2*(2*ss/vv)
            Fpk = 1 + np.pi*M*2*vy/(2*Tds)/W    # half-sine extra force over DS, redirect -vy -> +vy
            out += f" | b{beta}: DS {Tds:.2f}s peak {Fpk:.2f} BW"
        print(out)
vy = v*np.sin(np.arctan((s-15)/2/h))
for Tc in (0.2, 0.33, 0.5):
    print(f"   collision-only (no push-off) redirect over {Tc} s: peak {1+np.pi*M*2*vy/(2*Tc)/W:.2f} BW; with push-off doing half: {1+np.pi*M*vy/(2*Tc)/W:.2f} BW")
k_leg = (W/vy)**2/M; print(f"leg stiffness for +1 BW on full vy {vy:.2f}: {k_leg:.2e} N/m, travel {vy*np.sqrt(M/k_leg):.2f} m")
for kf in (15, 20, 30): print(f"   knee flex {kf}: hip drop {38*(1-np.cos(kf/2*deg)):.2f} m, knee moment at 1.5 BW {1.5*W*19*np.sin(kf/2*deg):.2e} N m")
# ---------- power ----------
print("== power")
for c in (0.05, 0.10): print(f"CoT_mech {c}: mean {c*W*v/1e6:.0f} MW")
for wkg in (3.5, 4.5): print(f"human peak sum {wkg} W/kg x sqrt(45.2)={np.sqrt(45.2):.2f}: {wkg*np.sqrt(45.2)*M/1e6:.0f} MW")

# ---------- addendum: swing started in pre-swing (thigh reversal during double support, Winter H3 / push-off) ----------
print("== swing with pre-swing: quintic spans DS + swing; reports torque and yaw only in single support")
def stride_pre(T, beta, th_a, n=4000):
    Tds = (2*beta-1)/2*T; Tsw = (1-beta)*T; Tq = Tds + Tsw; Tlin = T - Tq
    t = np.linspace(0, T, n, endpoint=False); w = -2*th_a/(T*beta)   # stance angular rate
    a0 = th_a; a1 = th_a + w*Tlin
    th = np.where(t < Tlin, a0 + w*t, 0.0); ts = t[t >= Tlin]-Tlin
    A = np.array([[1,0,0,0,0,0],[0,1,0,0,0,0],[0,0,2,0,0,0],[1,Tq,Tq**2,Tq**3,Tq**4,Tq**5],
                  [0,1,2*Tq,3*Tq**2,4*Tq**3,5*Tq**4],[0,0,2,6*Tq,12*Tq**2,20*Tq**3]])
    c = np.linalg.solve(A, [a1, w, 0, a0, w, 0]); th[t >= Tlin] = sum(c[k]*ts**k for k in range(6))
    dt = T/n; thd = np.gradient(th, dt); thdd = np.gradient(thd, dt)
    ss = (t >= Tlin + Tds)  # single support of the other leg = swing of this leg
    return t, th, thd, thdd, ss
for beta in (0.60, 0.65, 0.72):
    t, th, thd, thdd, ss = stride_pre(T, beta, th_a)
    tau = Ieff*thdd + mL*g*deff*np.sin(th); P = tau*thd
    zd = deff*thd*np.cos(th); Ly = 6.5*mL*(zd - np.roll(zd, len(t)//2)); My = np.gradient(Ly, t[1]-t[0])
    ssb = ss | np.roll(ss, len(t)//2)
    a1 = 2/len(t)*np.sum(Ly*np.exp(-1j*wS*t)); Mres = np.gradient(Ly - np.real(a1*np.exp(1j*wS*t)), t[1]-t[0])
    print(f"beta {beta}: peak hip tau {abs(tau).max():.2e} (in swing {abs(tau[ss]).max():.2e}); peak +P {P.max()/1e6:.0f} MW (in swing {P[ss].max()/1e6:.0f});"
          f" mean +P {np.clip(P,0,None).mean()/1e6:.0f} MW; yaw moment peak in single support {abs(My[ssb]).max():.2e},"
          f" after arm cancel of fundamental {abs(Mres[ssb]).max():.2e}")
np.random.seed(2)
s_, x_, P_ = reach(flat=False, psi_max=0, knee_lock=True, margin=5, pf=(-15, 0), pr=(0, 20))
print(f"B rollover straight knees, margin 5, pitch -15/+20: step {s_:.1f} m {P_}")
s_, x_, P_ = reach(flat=False, psi_max=8, knee_lock=False, margin=5, pf=(-15, 0), pr=(0, 20))
print(f"D realistic with psi<=8: step {s_:.1f} m {P_}")
