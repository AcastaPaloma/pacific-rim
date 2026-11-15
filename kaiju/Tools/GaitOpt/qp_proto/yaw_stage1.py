"""T-005 r4 9.3b-4: 3D stage-1 yaw check (R-009 s.7): stage 1 alone on the 8-corner feet at the reference demand.
Run from the project root: /opt/miniconda3/envs/kaiju/bin/python Tools/GaitOpt/qp_proto/yaw_stage1.py [--ref Tools/GaitOpt/out/stride_fr010_v2_n40p2.json]
Demand per reference node (80 = two mirrored steps):
  forces  : planar reference contact forces (Fx, Fz per heel/toe) + lateral LIPM shear Fy = Fz a_lat/g (yaw_check.lateral, stance foot pivot);
  moments : about the CoM of those forces placed on the foot centre lines (y = -+HW), with the yaw row replaced by the whole-body
            dL_z/dt of yaw_check.run (legs + arms, waist share k: k = 1 -> the waist counter-rotation takes it all, k = 0 -> none).
yaw_check.py is executed unchanged except for its input path (its __main__ block, which writes out/yaw_fr010.txt, is not run).
Stage 1: 4 corners per loaded foot point pair (heel pair / toe pair of a 15 x 10 m sole), 4 pyramid generators per corner with
  mu_eff = 0.9 * 0.8 / sqrt(2) (R-009 s.2), beta >= 0, min sum w ||G beta - w_des||^2 + 1e-8 ||beta/(m g)||^2 (NNLS, exact for this form).
Report: wrench residual, per-corner friction use |Ft|/(mu Fn) against the plant mu 0.8 and the 0.4 budget, waist torque / 5e8 N m."""
import sys, os, json, argparse, pathlib
import numpy as np
from scipy.optimize import nnls
ROOT = pathlib.Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "Tools/GaitOpt"))
import planar_model as P

WAIST_LIM = 5e8                     # R-009 s.2 waist force_limit_nm


def load_yaw_check(ref_path):
    src = open(ROOT / "Tools/GaitOpt/yaw_check.py").read()
    old = 'OUT = os.path.join(ROOT, "Tools/GaitOpt/out/stride_fr010_tc.json")'
    assert src.count(old) == 1
    src = src.replace(old, f'OUT = {str(ref_path)!r}')
    g = {"__name__": "yaw_check_v2", "__file__": str(ROOT / "Tools/GaitOpt/yaw_check.py")}
    cwd = os.getcwd(); os.chdir(ROOT)
    try: exec(compile(src, "yaw_check.py", "exec"), g)
    finally: os.chdir(cwd)
    return g


def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--ref", default="Tools/GaitOpt/out/stride_fr010_v2_n40p2.json"); ap.add_argument("--psi", type=float, default=0.0)
    ap.add_argument("--out", default="Tools/GaitOpt/out/proto/yaw_stage1_v2.json"); a = ap.parse_args()
    refp = ROOT / a.ref; Y = load_yaw_check(refp); m = P.PlanarModel(); ref = P.load_reference(refp, m); W = m.W; l = m.l_leg
    NF, N2, T, Ts = Y["NF"], Y["N2"], Y["T"], Y["Ts"]; HW = Y["HW"]; g0 = Y["g"]
    base = Y["run"](0, 0, 0, "actual"); lat = Y["lateral"](Ts, base["zc"], HW)
    J = json.load(open(ROOT / "Assets/Game/Config/Mech/mech_frame_balanced.json")); wid = J["foot_sole"]["width_m"]
    mu_p, mu_b = 0.8, 0.4; mue = 0.9 * mu_p / np.sqrt(2)
    gens = [np.array([mue, 0, 1]), np.array([-mue, 0, 1]), np.array([0, mue, 1]), np.array([0, -mue, 1])]
    out = {"ref": a.ref, "mu_eff_pyramid": mue, "foot_width_m": wid, "HW_m": HW, "cases": {}}
    lines = []
    for k_tr in (1.0, 0.0):
        yr = Y["run"](a.psi, 20, k_tr, "actual+lat", lat); Mz = yr["Mz"]
        use_p, use_b, resid, rows = [], [], [], []
        for kn in range(N2):
            i_f = kn * NF // N2; tl = (kn % ref.N) * ref.h; kk = int(min(tl / Ts * 399, 399)); ay = lat["a"][kk]
            sgn = -1.0 if kn < ref.N else 1.0          # step 1: stance R (= planar leg B, y = -HW): lateral accel sign mirrors per step
            q = ref.q[kn]; pts = m.points(q); c = ref.com[kn]; F = ref.F[kn]
            yc = sgn * lat["y"][kk]                     # CoM y: yaw_check's orbit is about a stance foot at +hw; step 1 (stance R at -HW) is its mirror
            cols, wdes = [], np.zeros(6); corners = []
            for p in range(4):
                leg = p // 2; ycl = (+HW if leg == 0 else -HW)          # planar leg A = L (+HW), leg B = R (-HW) (yaw_check mapping)
                fz = F[p, 1]
                if fz <= 1e-3 * W: continue
                fy = fz * (sgn * ay) / g0                                    # LIPM shear on the loaded points, Fy = Fn a_y / g (as yaw_check)
                r = np.array([pts[p, 0] - c[0], ycl - yc, pts[p, 1] - c[1]]); f = np.array([F[p, 0], fy, fz])
                wdes[:3] += f; wdes[3:] += np.cross(r, f)
                for s in (-0.5, 0.5):
                    rc = r + np.array([0, s * wid, 0]); corners.append((kn, p, s))
                    for gv in gens: cols.append(np.concatenate([gv, np.cross(rc, gv)]))
            if not cols: continue
            wdes[5] = Mz[i_f]                                            # yaw row: whole-body dL_z/dt with the waist share k_tr
            G = np.array(cols).T; sc = np.array([1 / W] * 3 + [1 / (W * l)] * 3)
            A = np.vstack([W * sc[:, None] * G, 1e-4 * np.eye(G.shape[1])]); b = np.concatenate([sc * wdes, np.zeros(G.shape[1])])   # beta in units of m g
            beta, _ = nnls(A, b, maxiter=5000); beta = W * beta
            res = sc * (G @ beta - wdes); resid.append(np.abs(res).max())
            for ci in range(len(corners)):
                bb = beta[4 * ci:4 * ci + 4]; fx = mue * (bb[0] - bb[1]); fyc = mue * (bb[2] - bb[3]); fzc = bb.sum()
                if fzc > 1e-3 * W / 8:
                    use_p.append((np.hypot(fx, fyc) / (mu_p * fzc), kn)); use_b.append((np.hypot(fx, fyc) / (mu_b * fzc), kn))
            rows.append(dict(node=kn, Mz=float(Mz[i_f]), resid=float(res.max()), fz_tot_W=float(wdes[2] / W)))
        up = max(use_p); ub = max(use_b); rr = max(resid)
        case = dict(trunk_k=k_tr, waist_tau_Nm=float(yr["waist_tau"]), waist_frac_of_limit=float(yr["waist_tau"] / WAIST_LIM), waist_amp_deg=float(yr["waist_amp"]),
                    Mz_feet_max_Nm=float(np.abs(Mz).max()), max_corner_use_vs_mu08=float(up[0]), at_node=int(up[1]), friction_margin_vs_mu08=float(1 - up[0]),
                    max_corner_use_vs_mu04=float(ub[0]), at_node_mu04=int(ub[1]), wrench_resid_max=float(rr))
        out["cases"][f"k{k_tr:g}"] = case
        lines.append(f"waist share k={k_tr:g}: waist tau {yr['waist_tau']:.2e} N m = {yr['waist_tau'] / WAIST_LIM:.2f} of 5e8 ({yr['waist_amp']:.1f} deg); feet yaw |Mz| max {np.abs(Mz).max():.2e} N m; "
                     f"stage-1 max corner friction use {up[0]:.3f} of mu 0.8 (margin {1 - up[0]:.3f}) at node {up[1]}, {ub[0]:.3f} of mu 0.4 at node {ub[1]}; wrench residual max {rr:.1e} (W, W l units)")
    json.dump(out, open(ROOT / a.out, "w"), indent=1)
    print(f"yaw_stage1: ref {a.ref}, psi {a.psi} deg, 8-corner feet ({wid} m wide, centre lines +-{HW} m), pyramid mu_eff {mue:.3f}; lateral LIPM peak a {lat['a_peak']:.3f} m/s2")
    print("\n".join(lines))


if __name__ == "__main__":
    main()
