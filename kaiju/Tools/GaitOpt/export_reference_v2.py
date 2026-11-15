#!/usr/bin/env python
"""T-005 r4 step 9.4b: export the v3 stride (reference of the sagittal whole-body QP, step 9.3b-5) to Assets/Game/Config/Mech/gait_reference_qp.json (schema gait_reference_opt@v2).
Run from the project root:  /opt/miniconda3/envs/kaiju/bin/python Tools/GaitOpt/export_reference_v2.py [stride.json]
One row (Fr 0.10, mu 0.40). Rows and nodes keep the @v1 layout (stance = solver leg B, swing = leg A, N = 40 nodes of one step) so OptimisedReference / ReferenceGait read the file unchanged;
the per-node @v2 additions (planar_model.load_reference, SI, node k of the first step; the second half of the 2N-node cycle is node k with the legs swapped and x advanced by step_length_m):
  q9 qd9 (planar q = [hip x, hip z, th, a0 b0 f0 (leg A), a1 b1 f1 (leg B)], rad), f4x2 (A heel, A toe, B heel, B toe as [ft, fn] N), com comd comdd (x, z), ang_mom ang_mom_dot (about the CoM, ccw),
  pts ptv pta (4 sole points x (x, z): position, velocity J qd, acceleration J qdd + Jdot qd with the node central-difference qdd)."""
import json, os, pathlib, sys
import numpy as np
HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import planar_model as P
ROOT = P.ROOT
SRC = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else HERE / "out/stride_fr010_v3_n40p2.json"
OUT = pathlib.Path(sys.argv[2]) if len(sys.argv) > 2 else ROOT / "Assets/Game/Config/Mech/gait_reference_qp.json"      # optional 2nd argument: output path (9.5g)
D = 180 / np.pi
model = P.PlanarModel(); ref = P.load_reference(SRC, model)
J = json.load(open(os.environ.get("R7_FRAME") or ROOT / "Assets/Game/Config/Mech/mech_frame_balanced.json")); S = {s["id"]: s for s in J["segments"]}
HIPY = S["thigh_L"]["proximal_joint"][1]; LT = HIPY - S["thigh_L"]["distal_end"][1]; LS = S["shank_L"]["proximal_joint"][1] - S["shank_L"]["distal_end"][1]
d0 = json.load(open(SRC)); s = d0["summary"]; N = s["nodes"] if "nodes" in s and isinstance(s["nodes"], int) else len(d0["nodes"]); sch = s["schedule"]; v, T = s["v"], s["step_time_s"]
assert s["verdict"] == "FEASIBLE" and N == 40 and ref.N == N and abs(ref.d - s["step_length_m"]) < 1e-9
offp = np.zeros(9); offp[0] = 2 * ref.d
qp_ = np.vstack([ref.q[1:], ref.q[:1] + offp]); qm_ = np.vstack([ref.q[-1:] - offp, ref.q[:-1]]); qd_cd = (qp_ - qm_) / (2 * ref.h)     # the controller's qd (central difference of q), as controller_fid._build_ref
xs = np.array([n["q"][0] - v * n["t"] for n in d0["nodes"]]); c0 = xs.mean()

def arm_fields(n, side):          # 9.5g: strides from stride_opt_tc.py --yaw carry the shoulder flexion of the arm on the same lateral side as leg A (swing) / B (stance); absent otherwise (then no field is written)
    if "arm_psi_rad" not in n: return {}
    sg = "a" if side == "A" else "b"
    return dict(shoulder_deg=n[f"arm_{sg}_flex_deg"], shoulder_dps=n[f"arm_{sg}_rate_dps"], shoulder_tau_nm=n["shoulder_tau_Nm"][side])

def leg(q, qd, tau, contact, i0, trunk, trunk_d, heel_on, toe_on, tn, extra=None):
    a, b, f = q[i0:i0 + 3]; ad, bd, fd = qd[i0:i0 + 3]
    knee = q[:2] + LT * np.array([np.sin(a), -np.cos(a)]); ank = knee + LS * np.array([np.sin(b), -np.cos(b)])
    return dict(hip_deg=(a + trunk) * D, knee_deg=(a - b) * D, ankle_deg=(b + f) * D, hip_dps=(ad + trunk_d) * D, knee_dps=(ad - bd) * D, ankle_dps=(bd + fd) * D, foot_pitch_deg=f * D,
                ankle_x_m=float(ank[0] - v * tn - c0), ankle_height_m=float(ank[1]), hip_tau_nm=tau[0], knee_tau_nm=tau[1], ankle_tau_nm=tau[2],
                heel_ft_n=contact[0][0], heel_fn_n=contact[0][1], toe_ft_n=contact[1][0], toe_fn_n=contact[1][1], heel_contact=bool(heel_on), toe_contact=bool(toe_on), **(extra or {}))

nodes = []
for k, n in enumerate(d0["nodes"]):
    q = np.array(n["q"]); qd = np.array(n["qd"]); tau = n["tau_Nm"]; cn = n["contact_N"]; hipx = q[0] - v * n["t"] - c0
    sw = leg(q, qd, [tau["hipA"], tau["kneeA"], tau["ankleA"]], [cn["A_heel"], cn["A_toe"]], 3, q[2], qd[2], False, k <= sch["k_ds"], n["t"], arm_fields(n, "A"))
    st = leg(q, qd, [tau["hipB"], tau["kneeB"], tau["ankleB"]], [cn["B_heel"], cn["B_toe"]], 6, q[2], qd[2], k < sch["k_hr"], k >= sch["k_ff"], n["t"], arm_fields(n, "B"))
    coms, Jc, Jw, pts, Jp, ba, bp = model.kin(ref.q[k], qd_cd[k])
    ptv = np.einsum("pdi,i->pd", Jp, qd_cd[k]); pta = np.einsum("pdi,i->pd", Jp, ref.qdd[k]) + bp   # bp uses qd_cd
    nodes.append(dict(phase=k / N, t_s=n["t"], pelvis_x_m=float(q[0]), pelvis_x_rel_m=float(hipx), pelvis_z_m=float(q[1]), pelvis_pitch_deg=float(q[2] * D), stance=st, swing=sw,
                      q9=ref.q[k].tolist(), qd9=ref.qd[k].tolist(), f4x2=ref.F[k].reshape(8).tolist(), com=ref.com[k].tolist(), comd=ref.comd[k].tolist(), comdd=ref.comdd[k].tolist(),
                      ang_mom=float(ref.L[k]), ang_mom_dot=float(ref.Ldot[k]), pts=pts.reshape(8).tolist(), ptv=ptv.reshape(8).tolist(), pta=pta.reshape(8).tolist(),
                      **({k_: n[k_] for k_ in ("yaw_mz_Nm", "yaw_m0_Nm", "yaw_mt_Nm", "yaw_budget_Nm", "yaw_cone_use") if k_ in n})))
row = dict(fr=s["fr"], step_time_s=T, step_length_m=s["step_length_m"], v=v, leg_length_m=42.4, duty_factor=0.63, verdict=s["verdict"], source_file=SRC.name, max_eq_violation=s["max_eq_violation"],
           schedule=dict(k_ds=sch["k_ds"], k_ff=sch["k_ff"], k_hr=sch["k_hr"]), body_mass_kg=model.mtot, mu=s["mu"], thigh_m=model.LT, shank_m=model.LS, heel_x_m=model.HEELX, toe_x_m=model.TOEX, ankle_height_m=model.HA, nodes=nodes, **({"yaw": s["yaw"]} if "yaw" in s else {}))
conv = dict(model="Planar sagittal model of Tools/GaitOpt/planar_model.py (same as @v1). q9 = [hip x, hip z, th, a0, b0, f0, a1, b1, f1] with leg 0 = solver leg A (exported as 'swing'), leg 1 = leg B ('stance'). x forward, z up.",
            angles="as @v1: hip = a + th, knee = a - b, ankle = b + f (degrees in the legs, radians in q9 qd9).", mapping_to_reference_gait="as @v1", torques="as @v1", forces="f4x2 = [ft, fn] per sole point A heel, A toe, B heel, B toe (N); fn >= 0.",
            roles="as @v1; the controller builds the 2N = 80 node periodic cycle by swapping the legs (q: [0,1,2,6,7,8,3,4,5], x + step_length_m), as planar_model.load_reference.",
            frame="@v1 fields as before; the @v2 additions are in the solver's absolute frame (pelvis x advances by step_length_m per step).",
            units="SI (m, s, kg, N, N m, rad) for the @v2 additions; degrees in the @v1 fields. ptv = J qd, pta = J qdd + Jdot qd of the four sole points with the node central-difference qd and qdd (the controller's reference).")
if "arm_psi_rad" in d0["nodes"][0]:
    conv["arm_9_5g"] = ("Optional (stride_opt_tc.py --yaw): stance/swing.shoulder_deg / shoulder_dps / shoulder_tau_nm = shoulder flexion (+ hand forward, mech_frame shoulder_L/R flexion sign), its rate (central difference over the nodes) and torque "
                        "(MN m model, N m here) of the straight arm on the same lateral side as that leg (swing = leg A, stance = leg B); anti-phase pair, arm_B = -arm_A, anti-periodic over a step. Node yaw_mz_nm = yaw moment of legs + arms about the CoM (= required ground yaw moment incl. "
                        "the moment of the planned shear), yaw_m0_nm = moment of the planned sagittal + lateral-LIPM shear about the CoM, yaw_mt_nm = Mz - M0 = free torsional ground moment, yaw_budget_nm = torsion capacity left in the shared friction cone (kappa-derated); row.yaw = summary.")
out = dict(schema="gait_reference_opt@v2", notes="Reference of the sagittal whole-body QP (T-005 round 4, 9.4b), read at imp >= 2.5. Generated by Tools/GaitOpt/export_reference_v2.py from stride_fr010_v3_n40p2.json; do not edit by hand.",
           source=dict(solver_commit="712477e", solver_scripts="Tools/GaitOpt/stride_opt_tc.py --lat --trans --v3", files=[SRC.name], verdicts=[f"{SRC.name}: {s['verdict']}, fr {s['fr']}, residual {s['max_eq_violation']:.2e}"], exporter="Tools/GaitOpt/export_reference_v2.py"),
           conventions=conv, rows=[row])
OUT.write_text(json.dumps(out, indent=1)); print("wrote", OUT, OUT.stat().st_size, "bytes")
