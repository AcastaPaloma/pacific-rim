"""R-008 gait feasibility model (kaiju env, reads the body JSON only).
Run from the project root:  /opt/miniconda3/envs/kaiju/bin/python research/R-008/model.py
Sections: (1) whole-stance sagittal kinematics with heel/ankle/forefoot rockers, optimised for joint margin;
(2) vertical (yaw) angular-momentum balance of legs, pelvis, arms, trunk over a full stride, against the
stance-foot friction budget; (3) LIPM sagittal and lateral checks; (4) assist sizing; (5) perception table.
Conventions (sagittal): x forward, y up. Thigh/shank angles a_t, a_s from vertical, + = distal end ahead.
hip flexion = a_t + pelvic tilt; knee flexion = a_t - a_s; ankle plantarflexion = a_s + f, f = foot pitch, + = heel up."""
import json, sys, numpy as np
from scipy.optimize import minimize
J = json.load(open("Assets/Game/Config/Mech/mech_frame_balanced.json"))
S = {s["id"]: s for s in J["segments"]}; JT = {j["id"]: j for j in J["joints"]}
g, M = 9.81, J["total_mass_kg"]; W = M * g; l = 42.4          # l: R-001 leg length used by M-001
HIP_Y = S["thigh_L"]["proximal_joint"][1]; LT = HIP_Y - S["thigh_L"]["distal_end"][1]
LS = S["shank_L"]["proximal_joint"][1] - S["shank_L"]["distal_end"][1]; HA = J["foot_sole"]["ankle_height_m"]
HEEL, TOE = -J["foot_sole"]["heel_z_m"], J["foot_sole"]["toe_z_m"]; HW = 0.5 * J["stance"]["neutral_stance_width_m"]
rom = lambda j, d: JT[j]["dofs"][[x["name"] for x in JT[j]["dofs"]].index(d)]["rom_deg"]
HIP, KNEE, ANK = rom("hip_L", "flexion"), rom("knee_L", "flexion"), rom("ankle_L", "plantarflexion")
WAIST_YAW = rom("waist", "axial_rotation"); D = np.pi / 180
mT, mS, mF = (S[k]["mass_kg"] for k in ("thigh_L", "shank_L", "foot_L"))
cT = HIP_Y - S["thigh_L"]["com"][1]; cS = S["shank_L"]["proximal_joint"][1] - S["shank_L"]["com"][1]
cF = np.array([S["foot_L"]["com"][2], S["foot_L"]["com"][1] - HA])      # foot CoM rel. ankle (fwd, up)
def gait(Fr, kstep=1.0):
    v = np.sqrt(Fr * g * l); s = kstep * 1.15 * Fr ** 0.3 * l; return v, s, 2 * s / v   # v, step, stride T
def rot(f, p):  # rotate vector p by foot pitch f (heel up = clockwise)
    c, s_ = np.cos(f), np.sin(f); return np.array([c * p[0] + s_ * p[1], -s_ * p[0] + c * p[1]])
def ankle_pos(f):  # ankle (x,y) for pitch f; ankle-flat origin, heel at -HEEL, toe at +TOE
    fh, ft = np.minimum(f, 0), np.maximum(f, 0)
    ah = np.array([-HEEL, 0.0])[:, None] + rot(fh, np.array([HEEL, HA]))
    at = np.array([TOE, 0.0])[:, None] + rot(ft, np.array([-TOE, HA]))
    return np.where(f < 0, ah, np.where(f > 0, at, np.array([[0.0], [HA]])))
def smooth(u): return 0.5 * (1 - np.cos(np.pi * np.clip(u, 0, 1)))
def schedule(z, Fr, beta, kstep, n=61, tt=None):
    Xp0, y0, a1, b1, a2, b2, phs, tff, thr, fto, tilt, psi = z[:12]
    v, s, T = gait(Fr, kstep); t = np.linspace(0, beta * T, n) if tt is None else tt
    tau = t / (beta * T)
    f = np.where(tau < tff, -phs * (1 - smooth(tau / max(tff, 1e-6))), 0.0)
    f = np.where(tau >= thr, fto * smooth((tau - thr) / max(1 - thr, 1e-6)), f) * D
    w2 = 4 * np.pi / T; y = y0 + a1 * np.cos(w2 * t) + b1 * np.sin(w2 * t) + a2 * np.cos(2 * w2 * t) + b2 * np.sin(2 * w2 * t)
    ps = psi * D * np.cos(2 * np.pi * t / T); xh = Xp0 + v * t + HW * np.sin(ps)
    return t, f, xh, y, ps, tilt * D
def ik(xh, y, f):
    A = ankle_pos(f); dx, dy = A[0] - xh, y - A[1]; d = np.hypot(dx, dy)
    gam = np.arctan2(dx, dy); half = np.arccos(np.clip(d / (LT + LS), -1, 1))
    return gam + half, gam - half, d, A          # a_t, a_s (equal segment lengths)
def angles(z, Fr, beta, kstep, n=61, tt=None):
    t, f, xh, y, ps, tilt = schedule(z, Fr, beta, kstep, n, tt); at, as_, d, A = ik(xh, y, f)
    return dict(t=t, f=f, xh=xh, y=y, ps=ps, at=at, as_=as_, d=d, A=A,
                hip=(at + tilt) / D, knee=(at - as_) / D, ank=(as_ + f) / D)
def solve(Fr, beta=0.64, kstep=1.0, phs_max=15, fto_max=20, tilt_max=0, psi_max=8, exc=0.05 * l,
          kmin=0.0, fixed=None, starts=16, seed=0, flat_ss=0.0, y_min=38.0, a_max=0.10 * 9.81):
    rng = np.random.default_rng(seed); v, s, T = gait(Fr, kstep)
    b = [(-45, 10), (y_min, 42), (-3, 3), (-3, 3), (-3, 3), (-3, 3), (0, phs_max), (0.02, 0.6), (0.02, 0.98),
         (0, fto_max), (0, tilt_max), (0, psi_max), (-60, 60)]
    if fixed:
        for i, val in fixed.items(): b[i] = (val, val)
    yy = np.linspace(0, T / 2, 40); ta, tb = (beta - 0.5) / beta, 0.5 / beta
    def cons(z):
        a = angles(z, Fr, beta, kstep); m = z[12]; w2 = 4 * np.pi / T
        tau = a["t"] / (beta * T); ss = (tau > ta) & (tau < tb); xc = z[0] + v * a["t"]    # pelvis as CoM proxy
        lo = np.where(tau < z[7], -HEEL, np.where(tau > z[8], TOE, -HEEL)); hi = np.where(tau < z[7], -HEEL, TOE)
        acc = g / (a["y"] + 1.5) * np.where(xc < lo, lo - xc, np.where(xc > hi, xc - hi, 0.0))   # LIPM |a| in single support
        yc = z[1] + z[2] * np.cos(w2 * yy) + z[3] * np.sin(w2 * yy) + z[4] * np.cos(2 * w2 * yy) + z[5] * np.sin(2 * w2 * yy)
        return np.concatenate([a["hip"] - HIP[0] - m, HIP[1] - a["hip"] - m, KNEE[1] - a["knee"] - m,
                               a["ank"] - ANK[0] - m, ANK[1] - a["ank"] - m,
                               (LT + LS) * np.cos(kmin / 2 * D) - a["d"], [exc - (yc.max() - yc.min()), z[8] - z[7],
                               (min(z[8], tb) - max(z[7], ta)) - flat_ss * (tb - ta)], (a_max - acc)[ss]])
    best = None
    for _ in range(starts):
        x0 = np.array([rng.uniform(lo, hi) for lo, hi in b]); x0[12] = -30
        r = minimize(lambda z: -z[12], x0, bounds=b, constraints=[{"type": "ineq", "fun": cons}],
                     method="SLSQP", options={"maxiter": 400, "ftol": 1e-6})
        if r.success and cons(r.x).min() > -1e-3 and (best is None or r.x[12] > best.x[12]): best = r
    return best
def report(z, Fr, beta, kstep):
    a = angles(z, Fr, beta, kstep, n=301); v, s, T = gait(Fr, kstep)
    mg = {"hip_ext": (a["hip"] - HIP[0]).min(), "ank_dorsi": (a["ank"] - ANK[0]).min(), "ank_plantar": (ANK[1] - a["ank"]).min(),
          "knee_max": (KNEE[1] - a["knee"]).min()}
    ss = (a["t"] > (beta - 0.5) * T) & (a["t"] < 0.5 * T); tau = a["t"] / (beta * T)
    edge = ss & ((tau < z[7]) | (tau > z[8]))
    return mg, dict(knee_min=a["knee"].min(), heel_rise_at=z[8], toe_off_pitch=z[9], hs_pitch=-z[6], tilt=z[10],
                    psi=z[11], edge_share_ss=edge.sum() / max(ss.sum(), 1), hip_y=z[1])
if __name__ == "__main__" and "kin" in sys.argv[1:] + ["kin"]:
    np.set_printoptions(precision=2, suppress=True)
    print(f"body: hip {HIP_Y} m, thigh {LT}, shank {LS}, ankle h {HA}, heel {HEEL} toe {TOE}; ROM hip {HIP} knee {KNEE} ankle {ANK}")
    for Fr in (0.023, 0.10, 0.20):
        v, s, T = gait(Fr); print(f"Fr {Fr}: v {v:.2f} step {s:.1f} m ({s/l:.2f} l) stride T {T:.2f} s")
