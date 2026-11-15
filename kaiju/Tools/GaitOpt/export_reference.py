#!/usr/bin/env python
"""T-005 r3 step 8.4a: export the three FEASIBLE optimised strides to Assets/Game/Config/Mech/gait_reference_opt.json (schema gait_reference_opt@v1).
Run from the project root:  /opt/miniconda3/envs/kaiju/bin/python Tools/GaitOpt/export_reference.py
Leg B of the solver (new stance leg, heel strike at node 0, toe at node N) is exported as "stance", leg A (old stance leg: toe rocker, then swing) as "swing"."""
import json, pathlib, sys
import numpy as np
ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT = ROOT / "Assets/Game/Config/Mech/gait_reference_opt.json"
FILES = ["stride_fr010_lat.json", "stride_fr015_lat2.json", "stride_fr020_lat2.json"]
SOLVER_COMMIT = "7d79db8974bd6a29fc32b8f945428769a5bde905"
LEG_LENGTH_M, DUTY = 42.4, 0.63          # as in stride_opt.py (l = 42.4, BETA = 0.63)
D = 180 / np.pi
J = json.load(open(ROOT / "Assets/Game/Config/Mech/mech_frame_balanced.json")); S = {s["id"]: s for s in J["segments"]}
HIPY = S["thigh_L"]["proximal_joint"][1]; LT = HIPY - S["thigh_L"]["distal_end"][1]; LS = S["shank_L"]["proximal_joint"][1] - S["shank_L"]["distal_end"][1]

def leg(q, qd, tau, contact, i0, hip_xy, trunk, trunk_d, heel_on, toe_on, tk):
    a, b, f = q[i0:i0 + 3]; ad, bd, fd = qd[i0:i0 + 3]
    knee = hip_xy + LT * np.array([np.sin(a), -np.cos(a)]); ank = knee + LS * np.array([np.sin(b), -np.cos(b)])
    return dict(
        hip_deg=(a + trunk) * D, knee_deg=(a - b) * D, ankle_deg=(b + f) * D,
        hip_dps=(ad + trunk_d) * D, knee_dps=(ad - bd) * D, ankle_dps=(bd + fd) * D,
        foot_pitch_deg=f * D, ankle_x_m=float(ank[0]), ankle_height_m=float(ank[1]),
        hip_tau_nm=tau[tk[0]], knee_tau_nm=tau[tk[1]], ankle_tau_nm=tau[tk[2]],
        heel_ft_n=contact[0][0], heel_fn_n=contact[0][1], toe_ft_n=contact[1][0], toe_fn_n=contact[1][1],
        heel_contact=bool(heel_on), toe_contact=bool(toe_on))

rows = []; src = []
for fn in FILES:
    d = json.load(open(ROOT / "Tools/GaitOpt/out" / fn)); s = d["summary"]; N = s["nodes"]; sch = s["schedule"]
    assert s["verdict"] == "FEASIBLE" and N == 40
    T, v = s["step_time_s"], s["v"]
    xs = np.array([n["q"][0] - v * n["t"] for n in d["nodes"]]); c = xs.mean()      # frame that moves with the mean walking speed, hip centre at its origin
    nodes = []
    for k, n in enumerate(d["nodes"]):
        q = np.array(n["q"]); qd = np.array(n["qd"]); hip = q[:2].copy(); hip[0] = q[0] - v * n["t"] - c
        # ankle x is taken in the same moving frame: subtract the same offset
        tau = n["tau_Nm"]; cn = n["contact_N"]
        # solver legs: A = swing role (indices 3..5), B = stance role (6..8)
        def mk(i0, name_tau, heel, toe, cont_h, cont_t):
            e = leg(q, qd, {"hip": tau["hip" + name_tau], "knee": tau["knee" + name_tau], "ankle": tau["ankle" + name_tau]}, [cont_h, cont_t], i0,
                    np.array([q[0], q[1]]), q[2], qd[2], heel, toe, ("hip", "knee", "ankle"))
            e["ankle_x_m"] = e["ankle_x_m"] - v * n["t"] - c; return e
        swing = mk(3, "A", False, k <= sch["k_ds"], cn["A_heel"], cn["A_toe"])
        stance = mk(6, "B", k < sch["k_hr"], k >= sch["k_ff"], cn["B_heel"], cn["B_toe"])
        nodes.append(dict(phase=k / N, t_s=n["t"], pelvis_x_m=float(q[0]), pelvis_x_rel_m=float(hip[0]), pelvis_z_m=float(q[1]), pelvis_pitch_deg=float(q[2] * D), stance=stance, swing=swing))
    rows.append(dict(fr=s["fr"], step_time_s=T, step_length_m=s["step_length_m"], v=v, leg_length_m=LEG_LENGTH_M, duty_factor=DUTY, verdict=s["verdict"], source_file=fn,
                     max_eq_violation=s["max_eq_violation"], schedule=dict(k_ds=sch["k_ds"], k_ff=sch["k_ff"], k_hr=sch["k_hr"]), nodes=nodes))
    src.append(f"{fn}: {s['verdict']}, fr {s['fr']}, residual {s['max_eq_violation']:.2e}")
rows.sort(key=lambda r: r["fr"])
conv = dict(
    model="Planar sagittal model of Tools/GaitOpt/stride_opt.py: q = [hip x, hip height, upper-body forward lean th, leg (thigh a_t, shank a_s, foot f) x2]. x forward, z up. Angles a_t, a_s from the downward vertical, positive = distal end ahead; f = foot pitch, positive = heel up (toes down). Both hips coincide at one point (no hip width, no lateral motion).",
    angles="hip_deg = a_t + th (flexion, positive = thigh forward of the trunk); knee_deg = a_t - a_s (flexion, positive = bent, 0 = straight); ankle_deg = a_s + f (plantarflexion, positive = toes down relative to the shank). Rates are the time derivatives of these. foot_pitch_deg = f (absolute, positive = toes down). pelvis_pitch_deg = th, positive = leaning forward.",
    mapping_to_reference_gait="Intended identical to ReferenceGait's LegSample: HipFlexion = line angle + alpha + PelvisTilt (thigh forward positive, plus the pelvic tilt), Knee = flexion positive, AnklePlantar = foot pitch + shank angle with FootPitch positive = toes down. Signs match by reading both definitions; NOT verified against the Unity joint axes. 8.5 verifies the mapping in Unity.",
    torques="tau is the generalised force the actuator supplies conjugate to the joint angle as defined above (positive = torque in the direction of increasing hip flexion, knee flexion, ankle plantarflexion); joint power = tau x rate. N m, as in the solver (body data units). Sign against Unity joint axes is NOT verified (8.5).",
    forces="Per foot, heel and toe sole points: fn = normal force (N, >= 0), ft = tangential (horizontal, in-plane) force (N), positive = forward on the foot. Contact flags are the solver's schedule (heel: heel point loaded; toe: toe point loaded; both = flat).",
    roles="Leg B of the solver is 'stance': heel strike at node 0, flat, then toe rocker until node N. Leg A is 'swing': toe rocker for nodes 0..k_ds, then swing until its heel strike at node N. Node N is node 0 with stance and swing swapped (mirrored periodic boundary); pelvis_x_m advances by step_length_m. In ReferenceGait the left leg is stance during the first half stride (stride phase 0..0.5), the right leg during the second half.",
    frame="pelvis_x_rel_m and ankle_x_m are in a frame moving at the mean walking speed v, origin at the mean hip position (periodic: node N equals node 0 with legs swapped). pelvis_x_m is the solver's absolute x. phase = k / N over one step (N = 40).",
    units="m, s, N, N m, degrees and degrees/s. Mech data of the balanced frame (leg length 42.4 m); other sizes scale time by sqrt(l/42.4) and lengths by l/42.4 in ReferenceGait (forces and torques are not rescaled).")
out = dict(schema="gait_reference_opt@v1",
           notes="Optimised one-step reference (T-005 round 3, 8.4a), read by ReferenceGait at imp = 2: linear in Froude between rows (clamped to 0.10..0.20), linear in phase. Generated by Tools/GaitOpt/export_reference.py; do not edit by hand.",
           source=dict(solver_commit=SOLVER_COMMIT, solver_scripts="Tools/GaitOpt/stride_opt_tc.py --lat (collocation, scipy trust-constr)", files=FILES, verdicts=src, exporter="Tools/GaitOpt/export_reference.py"),
           conventions=conv, rows=rows)
OUT.write_text(json.dumps(out, indent=1))
print("wrote", OUT, OUT.stat().st_size, "bytes"); print("\n".join(src))
