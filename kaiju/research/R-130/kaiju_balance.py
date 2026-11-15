"""R-130 offline check: graviportal kaiju gait timing, foot loads, ground response,
static/dynamic stability margins and tail sizing for three body plans at 10,000 t.

Run (kaiju env only):
    /opt/miniconda3/envs/kaiju/bin/python research/R-130/kaiju_balance.py
Writes research/R-130/kaiju_balance_out.json and prints the tables quoted in R-130.md.

Engine-free, deterministic (no randomness). Every body dimension below is a design
choice (Assumed) unless the comment says Sourced; see R-130 section 2 for the reasons.
"""
import json
import math
import os

import numpy as np

G = 9.81
M = 1.0e7  # 10,000 t (task brief)
HERE = os.path.dirname(os.path.abspath(__file__))

# ---------------------------------------------------------------- body plans (Assumed)
BODIES = {
    # Q: graviportal quadruped, sauropod/elephant hybrid, head raised to ~80 m on the neck.
    "Q": dict(name="graviportal quadruped", l=36.0, h_com=34.0, girdle=45.0, width=24.0,
              front_share=0.60,           # elephant-like fore/hind split (memory-based, see R-130 s1)
              pad_area=100.0, pad_len=11.3,
              tail=dict(frac=0.06, L=40.0, base=24.0, base_h=36.0),
              body_yaw_rg=22.0),          # radius of gyration (yaw) of body without tail, m
    # B: upright biped with heavy tail (classic kaiju); tail can touch down (tripod).
    "B": dict(name="upright tailed biped", l=34.0, h_com=36.0, width=16.0,
              foot_len=22.0, foot_wid=12.0, heel=7.0, toe=15.0, pad_area=211.0,
              tail=dict(frac=0.18, L=70.0, base=6.0, base_h=26.0, ground_contact=50.0),
              front=dict(frac=0.62, d=3.0),   # torso+head+arms centroid ahead of hip, m
              body_yaw_rg=7.0),
    # T: horizontal, tail-balanced biped (theropod-like); head raised to 70-80 m.
    "T": dict(name="horizontal tail-balanced biped", l=45.0, h_com=47.0, width=18.0,
              foot_len=24.0, foot_wid=12.0, heel=6.0, toe=18.0, pad_area=230.0,
              tail=dict(frac=0.25, L=80.0, base=4.0, base_h=45.0, ground_contact=None),
              front=dict(frac=0.55, d=22.0),
              body_yaw_rg=16.0),
}

# Froude band and duty factor law (Assumed; anchored on Sourced animal data in R-130 s1)
FR_POINTS = [0.02, 0.05, 0.08, 0.10, 0.15, 0.20, 0.25, 0.35, 0.50]
def duty_factor(plan, fr):
    """Assumed monotone law: quadruped 0.80 at Fr 0.02 -> 0.60 at Fr 0.25 -> 0.52 at 0.5;
    bipeds 0.68 -> 0.58 -> 0.52. Never below 0.5 for any limb in a walk/amble (no flight)."""
    if plan == "Q":
        pts = [(0.02, 0.80), (0.25, 0.60), (0.50, 0.52)]
    else:
        pts = [(0.02, 0.68), (0.25, 0.58), (0.50, 0.52)]
    xs, ys = zip(*pts)
    return float(np.interp(fr, xs, ys))


def stride_len(l, fr):
    """Alexander & Jayes 1983 relative stride lambda/h = 2.3 Fr^0.3 (Sourced)."""
    return 2.3 * fr ** 0.3 * l


# ---------------------------------------------------------------- gait table
def gait_table(key, b):
    rows = []
    l = b["l"]
    for fr in FR_POINTS:
        v = math.sqrt(fr * G * l)
        lam = stride_len(l, fr)
        T = lam / v
        beta = duty_factor(key, fr)
        n_feet = 4 if key == "Q" else 2
        # per-foot peak from a half-sine stance profile: mean over stride = share*mg
        if key == "Q":
            shares = {"fore": b["front_share"] / 2, "hind": (1 - b["front_share"]) / 2}
        else:
            shares = {"foot": 0.5}
        peaks = {k: s * math.pi / (2 * beta) for k, s in shares.items()}  # in BW
        swing = (1 - beta) * T
        # natural half-period of the leg as a uniform rod pendulum about the hip
        t_nat_half = math.pi * math.sqrt(2 * l / (3 * G))
        rows.append(dict(fr=fr, v=v, stride=lam, period=T, f_stride=1 / T, beta=beta,
                         stance=beta * T, swing=swing, swing_over_pend_half=swing / t_nat_half,
                         peak_bw_per_foot=peaks,
                         sum_bw_ripple=sum_force_ripple(key, b, beta)))
    return rows


def sum_force_ripple(key, b, beta, n=2000):
    """Sum of half-sine vertical forces over all feet across one stride (BW units).
    Returns (min, max): the whole-body vertical force ripple, a proxy for CoM bounce."""
    t = np.linspace(0, 1, n, endpoint=False)
    if key == "Q":
        # lateral-sequence walk, 25% offsets: LH 0, LF 0.25, RH 0.5, RF 0.75 (Sourced pattern)
        feet = [(0.00, (1 - b["front_share"]) / 2), (0.25, b["front_share"] / 2),
                (0.50, (1 - b["front_share"]) / 2), (0.75, b["front_share"] / 2)]
    else:
        feet = [(0.0, 0.5), (0.5, 0.5)]
    tot = np.zeros(n)
    for ph, share in feet:
        u = (t - ph) % 1.0
        on = u < beta
        peak = share * math.pi / (2 * beta)
        tot += np.where(on, peak * np.sin(np.pi * u / beta), 0.0)
    return [float(tot.min()), float(tot.max())]


# ---------------------------------------------------------------- quadruped static stability
def polygon_margin(pts, c):
    """Signed min distance of point c to the edges of the convex hull of pts (2-D).
    Positive inside. Returns -inf for fewer than 3 points."""
    if len(pts) < 3:
        return -math.inf
    P = np.array(pts)
    # convex hull (monotone chain)
    P = P[np.lexsort((P[:, 1], P[:, 0]))]
    def cross(o, a, bb):
        return (a[0] - o[0]) * (bb[1] - o[1]) - (a[1] - o[1]) * (bb[0] - o[0])
    lower, upper = [], []
    for p in P:
        while len(lower) >= 2 and cross(lower[-2], lower[-1], p) <= 0:
            lower.pop()
        lower.append(tuple(p))
    for p in P[::-1]:
        while len(upper) >= 2 and cross(upper[-2], upper[-1], p) <= 0:
            upper.pop()
        upper.append(tuple(p))
    hull = lower[:-1] + upper[:-1]
    if len(hull) < 3:
        return -math.inf
    d = math.inf
    for i in range(len(hull)):
        a = np.array(hull[i]); bb = np.array(hull[(i + 1) % len(hull)])
        e = bb - a
        nrm = np.array([e[1], -e[0]]) / np.linalg.norm(e)  # outward for ccw hull
        dist = -np.dot(np.array(c) - a, nrm)
        d = min(d, dist)
    return d


def quad_ssm(b, fr, beta, n=1000, pad_r=0.0, stride_scale=1.0):
    """Static stability margin of a lateral-sequence walk (McGhee & Frank 1968 style).
    pad_r > 0 replaces each point foot by 8 points on a circle of that radius."""
    l = b["l"]; Lb = b["girdle"]; W = b["width"]
    lam = stride_len(l, fr) * stride_scale
    s = beta * lam  # distance a foot travels relative to its girdle during stance
    # hips at x=0, shoulders at x=Lb; CoM so that static fore share matches
    xc = b["front_share"] * Lb
    feet = [("LH", 0.00, 0.0, -W / 2), ("LF", 0.25, Lb, -W / 2),
            ("RH", 0.50, 0.0, +W / 2), ("RF", 0.75, Lb, +W / 2)]
    margins, counts = [], []
    for t in np.linspace(0, 1, n, endpoint=False):
        pts = []
        for _, ph, gx, gy in feet:
            u = (t - ph) % 1.0
            if u < beta:
                fx = gx + s / 2 - s * u / beta
                if pad_r > 0:
                    for a in np.linspace(0, 2 * math.pi, 8, endpoint=False):
                        pts.append((fx + pad_r * math.cos(a), gy + pad_r * math.sin(a)))
                else:
                    pts.append((fx, gy))
        if pad_r > 0:
            # with pads a single foot already gives a polygon; count feet, not points
            nfeet = len(pts) // 8
            counts.append(nfeet)
            margins.append(polygon_margin(pts, (xc, 0.0)) if nfeet >= 3 else -math.inf)
            continue
        counts.append(len(pts))
        margins.append(polygon_margin(pts, (xc, 0.0)))
    m = np.array(margins); c = np.array(counts)
    finite = m[np.isfinite(m)]
    return dict(fr=fr, beta=beta, pad_r=pad_r, stride_scale=stride_scale, frac_ge3=float(np.mean(c >= 3)), frac_2=float(np.mean(c == 2)),
                min_ssm_m=float(m.min()) if np.all(np.isfinite(m)) else None,
                min_ssm_when_3plus_m=float(finite.min()) if finite.size else None,
                statically_stable=bool(np.all(np.isfinite(m)) and m.min() > 0))


def quad_beta_sweep(b, fr=0.08, pad_r=0.0, stride_scale=1.0):
    out = []
    for beta in [0.55, 0.60, 0.65, 0.70, 0.75, 0.80, 0.85, 0.90]:
        out.append(quad_ssm(b, fr, beta, pad_r=pad_r, stride_scale=stride_scale))
    return out


# ---------------------------------------------------------------- tail mass properties
def tail_props(b):
    """Tail as a cone (apex at tip): linear density ~ (1 - s/L)^2. Returns CoM distance
    behind the hip, inertia about the hip vertical axis (yaw) and about the tail base."""
    t = b["tail"]
    mt = t["frac"] * M; L = t["L"]; r0 = t["base"]
    s = np.linspace(0, L, 4001)
    lam = (1 - s / L) ** 2
    lam = lam / np.trapezoid(lam, s) * mt
    r = r0 + s
    com = float(np.trapezoid(lam * r, s) / mt)
    I_hip = float(np.trapezoid(lam * r ** 2, s))
    I_base = float(np.trapezoid(lam * s ** 2, s))
    return dict(mass_kg=mt, com_behind_hip_m=com, I_yaw_about_hip=I_hip, I_about_base=I_base,
                I_base_check_mL2_over_10=mt * L ** 2 / 10)


def tail_balance(key, b):
    tp = tail_props(b)
    out = dict(tail=tp)
    mt = tp["mass_kg"]
    I_body = (1 - b["tail"]["frac"]) * M * b["body_yaw_rg"] ** 2
    R = tp["I_yaw_about_hip"] / I_body
    out["I_body_yaw_ex_tail"] = I_body
    out["tail_to_body_yaw_inertia"] = R
    # zero-angular-momentum reorientation: body yaw obtainable from a +-40 deg tail sweep
    out["body_yaw_from_80deg_tail_sweep_deg"] = 80.0 * R / (1 + R)
    if "front" in b:
        mf = b["front"]["frac"] * M; df = b["front"]["d"]
        x_c = (mf * df - mt * tp["com_behind_hip_m"]) / M  # legs assumed on the hip line
        out["com_ahead_of_hip_m"] = x_c
        out["com_ahead_of_hip_over_l"] = x_c / b["l"]
        out["static_hip_pitch_torque_Nm_total"] = G * (mf * df - mt * tp["com_behind_hip_m"])
        out["static_hip_pitch_torque_no_tail_Nm"] = G * mf * df
        # tail mass needed to put the CoM at 0.10 l ahead of the hip for this front moment
        x_target = 0.10 * b["l"]
        need = (mf * df - x_target * M) / tp["com_behind_hip_m"]
        out["tail_frac_for_com_0p10l"] = need / M
        # forward lean of the front mass needed to bring the CoM over the hip (x_c = 0),
        # front centroid at r_f above the hip when upright (Assumed 20 m for B, 22 m for T)
        r_f = 20.0 if key == "B" else 22.0
        d_needed = mt * tp["com_behind_hip_m"] / mf
        out["front_offset_for_com_over_hip_m"] = d_needed
        out["upright_lean_for_com_over_hip_deg"] = (math.degrees(math.asin(min(1.0, d_needed / r_f)))
                                                    if key == "B" else None)
    # tail pitch torque authority vs the ankle (CoP) authority
    tau_tail = 1.0e9  # Assumed tail-base actuator limit, same class as mech hip (R-005 s3.3)
    out["tail_base_torque_limit_Nm"] = tau_tail
    if key != "Q":
        out["ankle_toe_authority_Nm"] = M * G * 0.9 * b["toe"]
        out["ankle_heel_authority_Nm"] = M * G * 0.9 * b["heel"]
        out["ankle_strategy_ceiling_g_fwd"] = 0.9 * b["toe"] / b["h_com"]
        out["ankle_strategy_ceiling_g_back"] = 0.9 * b["heel"] / b["h_com"]
        # free moment budget of one stance foot (M-019 style): mu F_n r, r ~ foot_len/4
        out["free_moment_budget_Nm"] = 0.6 * M * G * b["foot_len"] / 4 / 1.5
    out["tail_torque_body_yaw_accel_dps2"] = math.degrees(tau_tail / I_body)
    # sustained tail torque over a half swing: tail angular accel and swing amplitude
    a_t = tau_tail / tp["I_yaw_about_hip"]
    out["tail_accel_at_limit_dps2"] = math.degrees(a_t)
    return out


# ---------------------------------------------------------------- biped LIPM / DCM / tripod
def biped_balance(key, b):
    h = b["h_com"]; w0 = math.sqrt(G / h)
    out = dict(omega0=w0, time_constant_s=1 / w0)
    # lateral static margin in double support: half stance width + half foot width
    lat = b["width"] / 2 + b["foot_wid"] / 2
    out["double_support_lateral_margin_m"] = lat
    out["double_support_lateral_ceiling_g"] = 0.9 * lat / h
    out["single_support_lateral_ceiling_g"] = 0.9 * (b["foot_wid"] / 2) / h
    # capture: max CoM speed recoverable by an ankle-only strategy (DCM within toe lever)
    out["v_capture_ankle_only_mps"] = 0.9 * b["toe"] * w0
    # tripod (tail on ground)
    tc = b["tail"].get("ground_contact")
    if tc:
        foot_pts = []
        for sy in (-1, 1):
            y0 = sy * b["width"] / 2
            for dx in (-b["heel"], b["toe"]):
                for dy in (-b["foot_wid"] / 2, b["foot_wid"] / 2):
                    foot_pts.append((dx, y0 + dy))
        c = (0.0, 0.0)  # CoM over the ankles when standing
        m_feet = polygon_margin(foot_pts, c)
        tri = foot_pts + [(-tc, 0.0)]
        m_tri = polygon_margin(tri, c)
        # backward reach of the CoM before tipping, two feet vs tripod
        out["tripod"] = dict(tail_contact_behind_ankle_m=tc, margin_two_feet_m=m_feet,
                             margin_tripod_m=m_tri,
                             back_reach_two_feet_m=b["heel"], back_reach_tripod_m=tc,
                             back_push_ceiling_two_feet_g=0.9 * b["heel"] / h,
                             back_push_ceiling_tripod_g=0.9 * tc / h,
                             # load on the tail if the CoM is shifted back 5 m (statics)
                             tail_load_bw_com_back_5m=5.0 / tc)
    return out


# ---------------------------------------------------------------- ground: pressure, sinkage
# Terzaghi strip/square footing, general shear: q_u = 1.3 c Nc + gamma D Nq + 0.4 gamma B Ngamma
# (square footing factors; Sourced textbook form). Factors for phi (Terzaghi table, Sourced):
TERZ = {0: (5.7, 1.0, 0.0), 30: (37.2, 22.5, 19.7), 34: (52.6, 36.5, 36.0)}
SOILS = {  # Assumed representative values (R-130 s5)
    "bay_mud":   dict(c=10e3, phi=0, gamma=15e3),
    "soft_clay": dict(c=25e3, phi=0, gamma=17e3),
    "stiff_clay": dict(c=100e3, phi=0, gamma=19e3),
    "loose_sand": dict(c=0.0, phi=30, gamma=17e3),
    "dense_sand": dict(c=0.0, phi=34, gamma=19e3),
}


def bearing(soil, B, D):
    s = SOILS[soil]; Nc, Nq, Ng = TERZ[s["phi"]]
    return 1.3 * s["c"] * Nc + s["gamma"] * D * Nq + 0.4 * s["gamma"] * B * Ng


def sink_depth(soil, B, p):
    """Quasi-static punching depth: smallest D with q_u(B, D) >= p (capped at 30 m)."""
    for D in np.linspace(0, 30, 3001):
        if bearing(soil, B, D) >= p:
            return float(D)
    return 30.0


def ground_table():
    rows = []
    cases = [("Q fore, static", BODIES["Q"]["front_share"] / 2, 100.0),
             ("Q fore, walk peak Fr0.08", None, 100.0),
             ("B/T single stance static", 1.0, 211.0),
             ("B/T walk peak Fr0.10", None, 211.0),
             ("Q fore, geometric-scaled pad 23 m2", BODIES["Q"]["front_share"] / 2, 23.0)]
    gt_q = {r["fr"]: r for r in gait_table("Q", BODIES["Q"])}
    gt_b = {r["fr"]: r for r in gait_table("B", BODIES["B"])}
    for name, share, A in cases:
        if share is None:
            if name.startswith("Q"):
                F = gt_q[0.08]["peak_bw_per_foot"]["fore"] * M * G
            else:
                F = gt_b[0.10]["peak_bw_per_foot"]["foot"] * M * G
        else:
            F = share * M * G
        p = F / A
        B = math.sqrt(A)
        rows.append(dict(case=name, force_MN=F / 1e6, area_m2=A, pressure_kPa=p / 1e3,
                         sink_m={s: sink_depth(s, B, p) for s in SOILS},
                         q_u_surface_kPa={s: bearing(s, B, 0.0) / 1e3 for s in SOILS}))
    return rows


# ---------------------------------------------------------------- footfall seismic hook
def seismic_table():
    """Energy per footfall and a Gutenberg-Richter equivalent magnitude.
    log10 E_s[J] = 1.5 M + 4.8 (Sourced relation). Seismic efficiency eta is Assumed."""
    rows = []
    for label, F, sink, v_td, m_eff in [
            ("Q fore on stiff ground", 0.39 * M * G, 0.05, 0.6, 0.15 * M),
            ("Q fore on soft clay", 0.39 * M * G, 1.0, 0.6, 0.15 * M),
            ("B/T on stiff ground", 0.80 * M * G, 0.05, 1.0, 0.25 * M),
            ("B/T on bay mud", 0.80 * M * G, 3.0, 1.0, 0.25 * M),
            ("B tail slam", 0.20 * M * G, 0.5, 6.0, 0.18 * M)]:
        e_impact = 0.5 * m_eff * v_td ** 2
        e_sink = F * sink
        e = e_impact + e_sink
        mags = {f"eta_{eta:g}": (math.log10(eta * e) - 4.8) / 1.5 for eta in (1e-3, 1e-2, 1e-1)}
        rows.append(dict(case=label, impulse_MNs=m_eff * v_td / 1e6, e_impact_MJ=e_impact / 1e6,
                         e_sink_MJ=e_sink / 1e6, e_total_MJ=e / 1e6, magnitude=mags))
    return rows


# ---------------------------------------------------------------- joint torque, column limbs
def torque_table():
    rows = []
    for key, b in BODIES.items():
        gt = {r["fr"]: r for r in gait_table(key, b)}
        fr = 0.08 if key == "Q" else 0.10
        peak = max(gt[fr]["peak_bw_per_foot"].values()) * M * G
        for e in (0.03, 0.08, 0.15):  # GRF line offset from the knee/elbow, fraction of l (Assumed)
            rows.append(dict(body=key, fr=fr, peak_grf_MN=peak / 1e6, offset_frac_l=e,
                             joint_torque_Nm=peak * e * b["l"]))
    return rows


# ---------------------------------------------------------------- QP size / time extrapolation
def qp_cost():
    """Extrapolate dense QP time from T-005 r4 step 9.2 warm-solve p50 (Release):
    n=16: 27.2 us, n=32: 124.7 us -> exponent fitted, then evaluated at kaiju sizes."""
    k = math.log(124.7 / 27.2) / math.log(2.0)
    base = 124.7e-6
    out = dict(exponent=k)
    sizes = {
        "mech planar full-ID (9 dof, 4 pts)": 9 + 8,
        "mech 3D full-ID (29 nv, 8 pts x4)": 29 + 32,
        "biped kaiju 3D full-ID (35 nv, 8 pts x4)": 35 + 32,
        "quad kaiju 3D full-ID (60 nv, 16 pts x4)": 60 + 64,
        "quad centroidal wrench QP (4 feet x 6)": 24,
        "quad centroidal force QP (16 pts x 3)": 48,
    }
    for name, n in sizes.items():
        out[name] = dict(n=n, est_ms=base * (n / 32) ** k * 1e3)
    return out


def main():
    res = dict(mass_kg=M, g=G, bodies=BODIES)
    res["gait"] = {k: gait_table(k, b) for k, b in BODIES.items()}
    res["quad_beta_sweep_fr008"] = quad_beta_sweep(BODIES["Q"], 0.08)
    res["quad_beta_sweep_fr020"] = quad_beta_sweep(BODIES["Q"], 0.20)
    pr = math.sqrt(BODIES["Q"]["pad_area"] / math.pi)
    res["quad_beta_sweep_fr008_pads"] = quad_beta_sweep(BODIES["Q"], 0.08, pad_r=pr)
    res["quad_beta_sweep_fr002_pads"] = quad_beta_sweep(BODIES["Q"], 0.02, pad_r=pr)
    res["quad_beta_sweep_fr008_pads_halfstride"] = quad_beta_sweep(BODIES["Q"], 0.08, pad_r=pr, stride_scale=0.5)
    res["tail"] = {k: tail_balance(k, b) for k, b in BODIES.items()}
    res["biped"] = {k: biped_balance(k, BODIES[k]) for k in ("B", "T")}
    res["quad_dynamic"] = dict(omega0=math.sqrt(G / BODIES["Q"]["h_com"]),
                               lateral_ceiling_4feet_g=0.9 * (BODIES["Q"]["width"] / 2) / BODIES["Q"]["h_com"])
    res["ground"] = ground_table()
    res["seismic"] = seismic_table()
    res["torque"] = torque_table()
    res["qp"] = qp_cost()
    with open(os.path.join(HERE, "kaiju_balance_out.json"), "w") as f:
        json.dump(res, f, indent=1, default=float)

    # ---------------- printed summary
    print("== gait (v m/s, stride m, period s, f Hz, beta, swing s, swing/pend_half, peak BW/foot, sum BW ripple)")
    for k, rows in res["gait"].items():
        print(f"-- {k} {BODIES[k]['name']} l={BODIES[k]['l']} m")
        for r in rows:
            pk = ", ".join(f"{n} {v:.2f}" for n, v in r["peak_bw_per_foot"].items())
            print(f"Fr {r['fr']:.2f}: v {r['v']:.2f}  stride {r['stride']:.1f}  T {r['period']:.1f}  "
                  f"f {r['f_stride']:.3f}  beta {r['beta']:.2f}  swing {r['swing']:.1f}  "
                  f"sw/pend {r['swing_over_pend_half']:.2f}  peak[{pk}]  sum[{r['sum_bw_ripple'][0]:.2f},{r['sum_bw_ripple'][1]:.2f}]")
    for tag in ("quad_beta_sweep_fr008", "quad_beta_sweep_fr020", "quad_beta_sweep_fr008_pads",
                "quad_beta_sweep_fr002_pads", "quad_beta_sweep_fr008_pads_halfstride"):
        print(f"== {tag}: beta, frac>=3 feet, frac 2 feet, min SSM (m), min SSM while >=3 feet, static")
        for r in res[tag]:
            print(f"beta {r['beta']:.2f}: ge3 {r['frac_ge3']:.2f}  two {r['frac_2']:.2f}  "
                  f"ssm {r['min_ssm_m'] if r['min_ssm_m'] is None else round(r['min_ssm_m'], 2)}  "
                  f"ssm3 {None if r['min_ssm_when_3plus_m'] is None else round(r['min_ssm_when_3plus_m'], 2)}  "
                  f"static {r['statically_stable']}")
    print("== quad dynamic", json.dumps(res["quad_dynamic"]))
    print("== tail")
    for k, t in res["tail"].items():
        print(k, json.dumps({kk: (round(v, 4) if isinstance(v, float) else v) for kk, v in t.items() if kk != "tail"}),
              "tail:", json.dumps({kk: float(f"{v:.4g}") for kk, v in t["tail"].items()}))
    print("== biped")
    for k, t in res["biped"].items():
        print(k, json.dumps(t, default=lambda x: round(x, 3)))
    print("== ground (pressure kPa, sink m by soil)")
    for r in res["ground"]:
        print(f"{r['case']}: F {r['force_MN']:.1f} MN, A {r['area_m2']:.0f}, p {r['pressure_kPa']:.0f} kPa, "
              f"sink {json.dumps({s: round(v, 2) for s, v in r['sink_m'].items()})}, "
              f"q_u0 {json.dumps({s: round(v) for s, v in r['q_u_surface_kPa'].items()})}")
    print("== seismic")
    for r in res["seismic"]:
        print(f"{r['case']}: impulse {r['impulse_MNs']:.2f} MN s, E {r['e_total_MJ']:.1f} MJ "
              f"(impact {r['e_impact_MJ']:.1f}, sink {r['e_sink_MJ']:.1f}), M {json.dumps({k: round(v, 2) for k, v in r['magnitude'].items()})}")
    print("== torque (column-limb offset)")
    for r in res["torque"]:
        print(f"{r['body']} Fr {r['fr']}: peak GRF {r['peak_grf_MN']:.1f} MN, offset {r['offset_frac_l']} l -> {r['joint_torque_Nm']:.2e} N m")
    print("== qp", json.dumps(res["qp"], default=lambda x: round(x, 3)))


if __name__ == "__main__":
    main()
