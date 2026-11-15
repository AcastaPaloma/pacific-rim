#!/usr/bin/env python
"""T-005 r4 9.5f: build a variants file (name + JSON per line) from a base dict and per-variant deltas, and validate it:
unique keys inside each JSON (JsonUtility keeps the last silently), every key a WalkTuning field (typos are silently ignored),
and every variant's effective dict different from every other. Prints the effective qp_*/imp* values per variant.
Usage: variants95f.py <set> <out.txt>   (set = name of a dict in SETS)"""
import json, re, sys, os, itertools
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '../../../..'))
src = open(os.path.join(ROOT, 'Assets/Game/Units/Mech/Contracts/MechContracts.cs')).read()
FIELDS = set()
for m in re.finditer(r'^\s*public (?:double|int|bool|string|float)\s+([^;(=]+(?:=[^;]*)?(?:,[^;]*)*);', src, re.M):
    for part in re.split(r',(?![^()]*\))', m.group(0)):
        n = re.search(r'([a-z_][a-z0-9_]*)\s*(?:=|;|$)', part.replace('public double', '').replace('public int', '').strip())
        if n: FIELDS.add(n.group(1))
FIELDS |= set(re.findall(r'\b((?:qp|imp|imp2)_[a-z0-9_]+)\b', src))
BASE = {"imp": 3, "imp2_phase_sync": 1, "qp_engage_speed_frac": 0.3, "qp_ladder": 1, "qp_match": 1, "qp_match_ss": 1, "qp_match_climb_td": 1,
        "qp_lateral": 1, "qp_om_y": 0.3, "qp_w_tau": 1, "qp_match_thr": 0.8, "qp_match_up_thr": 1.2, "qp_match_hold": 2,
        "qp_yaw_trunk": 1, "qp_om_yaw": 2.0, "qp_mu": 0.5, "qp_fr_margin": 0.95}
SETS = {
 'f1': {
  'F0base': {},
  'F1arm': {"qp_arm_ff": 1},
  'F2arm06': {"qp_arm_ff": 1, "qp_arm_ff_gain": 0.6},
  'F3arm_hz6': {"qp_arm_ff": 1, "qp_arm_ff_hz": 6.0},
  'F4budget': {"qp_yaw_budget": 1.0},
  'F5arm_budget': {"qp_arm_ff": 1, "qp_yaw_budget": 1.0},
  'F6arm_ref': {"qp_arm_ff": 1, "qp_arm_ff_ref": 1},
  'F7arm_ff0': {"qp_arm_ff": 1, "qp_yaw_ff": 0},
 },
 'f2': {
  'G0base': {},
  'G1refw1': {"qp_arm_ff": 1, "qp_arm_ff_ref_w": 1.0},
  'G2refw1g07': {"qp_arm_ff": 1, "qp_arm_ff_ref_w": 1.0, "qp_arm_ff_gain": 0.7},
  'G3refw05': {"qp_arm_ff": 1, "qp_arm_ff_ref_w": 0.5},
  'G4refw1bud': {"qp_arm_ff": 1, "qp_arm_ff_ref_w": 1.0, "qp_yaw_budget": 1.0},
  'G5bud05': {"qp_yaw_budget": 0.5, "qp_yaw_budget_floor": 0.3},
  'G6refw1ff0': {"qp_arm_ff": 1, "qp_arm_ff_ref_w": 1.0, "qp_yaw_ff": 0},
  'G7refw1hz6': {"qp_arm_ff": 1, "qp_arm_ff_ref_w": 1.0, "qp_arm_ff_hz": 6.0},
  'G8refw1g13': {"qp_arm_ff": 1, "qp_arm_ff_ref_w": 1.0, "qp_arm_ff_gain": 1.3},
 },
}
def _seeded(configs, n):
    # 'seed' = a 1e-4-scale change of qp_pd_scale (0.25 + k 1e-4): the run is deterministic, a perturbation samples the run-to-run spread of one configuration
    return {f'{cn}s{k}': dict(d, qp_pd_scale=round(0.25 + 1e-4 * k, 6)) for cn, d in configs.items() for k in range(n)}
SETS['f3'] = _seeded({
  'Hbase': {},
  'Hmeas': {"qp_arm_ff": 1, "qp_arm_ff_gain": 0.7},
  'Href': {"qp_arm_ff": 1, "qp_arm_ff_ref_w": 1.0, "qp_arm_ff_gain": 0.7},
  'Hbud': {"qp_yaw_budget": 1.0},
  'Hrefbud': {"qp_arm_ff": 1, "qp_arm_ff_ref_w": 1.0, "qp_arm_ff_gain": 0.7, "qp_yaw_budget": 1.0},
}, 4)
# 9.5h: v4 ladder (qp_ref_v4) + reference arm swing (qp_arm_ref)
V4 = {"qp_ref_v4": 1}
SETS['h1'] = {
  'I0v3base': {},
  'I1v4only': dict(V4),
  'I2arm': dict(V4, qp_arm_ref=1),
  'I3armyff': dict(V4, qp_arm_ref=1, qp_arm_ref_yawff=1.0),
  'I4armg07': dict(V4, qp_arm_ref=1, qp_arm_ref_gain=0.7),
  'I5armhz6': dict(V4, qp_arm_ref=1, qp_arm_ref_hz=6.0),
  'I6armneg': dict(V4, qp_arm_ref=1, qp_arm_ref_gain=-1.0),
  'I7armff': dict(V4, qp_arm_ref=1, qp_arm_ref_yawff=1.0, qp_arm_ff=1, qp_arm_ff_gain=0.7),
  'I8armyff0': dict(V4, qp_arm_ref=1, qp_yaw_ff=0),
}
A4 = dict(V4, qp_arm_ref=1)
SETS['h2'] = {
  'J0arm': dict(A4),
  'J1cap035': dict(A4, qp_yaw_cap=0.035),
  'J2cap05': dict(A4, qp_yaw_cap=0.05),
  'J3om1': dict(A4, qp_om_yaw=1.0),
  'J4yffcap05': dict(A4, qp_arm_ref_yawff=1.0, qp_yaw_cap=0.05),
  'J5v4cap035': dict(V4, qp_yaw_cap=0.035),
  'J6armg05': dict(A4, qp_arm_ref_gain=0.5),
  'J7cap035om1': dict(A4, qp_yaw_cap=0.035, qp_om_yaw=1.0),
  'J8cap035mu04': dict(A4, qp_yaw_cap=0.035, qp_mu=0.4),
}
SETS['h3'] = _seeded({
  'Karm': dict(A4, qp_yaw_cap=0.035),
  'Kv4': dict(V4, qp_yaw_cap=0.035),
  'Kv3': {"qp_yaw_cap": 0.035},
}, 4)
# 9.5i: touchdown re-match (qp_td_rematch) and late CoM-x reference (qp_late_cx) on the 9.5h best config (J1cap035)
J1 = dict(A4, qp_yaw_cap=0.035)
SETS['i1'] = _seeded({
  'Mbase': dict(J1),
  'Mrm1': dict(J1, qp_td_rematch=1.0),
  'Mrm15': dict(J1, qp_td_rematch=1.5),
  'Mlate': dict(J1, qp_late_cx=1.0),
  'Mrm1late': dict(J1, qp_td_rematch=1.0, qp_late_cx=1.0),
}, 4)
SETS['i2'] = _seeded({
  'Nbase': dict(J1),
  'Nem2': dict(J1, qp_cx_emax=2.0),
  'Nem4': dict(J1, qp_cx_emax=4.0),
  'Nrm1': dict(J1, qp_td_rematch=1.0),
  'Nrm1em3': dict(J1, qp_td_rematch=1.0, qp_cx_emax=3.0),
}, 4)
# 9.5j: QP joint-range rows (qp_rom), foot-pitch weight ramp at the ankle stop (qp_fp_rom), stance contact hold (qp_z_hold_ss), then rematch + late_cx on top
R = dict(J1, qp_rom=1)
RF = dict(R, qp_fp_rom=1)
RFZ = dict(RF, qp_z_hold_ss=1.0)
SETS['j1'] = _seeded({
  'Pbase': dict(J1),
  'Prom': dict(R),
  'Promfp': dict(RF),
  'Promfpz': dict(RFZ),
  'Pz1': dict(J1, qp_z_hold_ss=1.0),
  'Promfpzrl': dict(RFZ, qp_td_rematch=1.0, qp_late_cx=1.0),
}, 4)
SETS['j2'] = _seeded({
  'Qm6': dict(RFZ, qp_rom_margin_deg=6.0),
  'Qm10': dict(RFZ, qp_rom_margin_deg=10.0),
  'Qa60': dict(RFZ, qp_rom_amax=60.0),
  'Qa15': dict(RFZ, qp_rom_amax=15.0),
  'Qh05': dict(RFZ, qp_rom_horizon_s=0.05),
  'Qb8': dict(RFZ, qp_fp_rom_band_deg=8.0, qp_rom_margin_deg=6.0),
  'Qz3': dict(RFZ, qp_z_hold_ss=3.0),
  'Qz3rl': dict(RFZ, qp_z_hold_ss=3.0, qp_rom_margin_deg=6.0, qp_td_rematch=1.0, qp_late_cx=1.0),
}, 4)
SETS['j3'] = _seeded({
  'Sm1': dict(RFZ, qp_rom_margin_deg=1.0),
  'Sa100': dict(RFZ, qp_rom_amax=100.0, qp_rom_margin_deg=6.0),
  'Srl': dict(RF, qp_td_rematch=1.0, qp_late_cx=1.0),
  'Sfpz': dict(J1, qp_rom=1, qp_fp_rom=1, qp_z_hold_ss=1.0, qp_rom_amax=60.0, qp_rom_horizon_s=0.2),
}, 4)
# 9.5k: the PD under the QP feed-forward on the swing leg (qp_pd_swing, qp_pd_ref), on the best 9.5j stack (B), qp_diag 1 (log block qp.k) on all
B = dict(RFZ, qp_z_hold_ss=3.0, qp_rom_margin_deg=6.0, qp_td_rematch=1.0, qp_late_cx=1.0, qp_diag=1)
SETS['k1'] = _seeded({
  'Kbase': dict(B),
  'Ksw0': dict(B, qp_pd_swing=0.0),
  'Ksw05': dict(B, qp_pd_swing=0.05),
  'Kref': dict(B, qp_pd_ref=1),
  'Kref1': dict(B, qp_pd_ref=1, qp_pd_swing=1.0),
  'Kref05': dict(B, qp_pd_ref=1, qp_pd_swing=0.05),
  'Kref2': dict(B, qp_pd_ref=2),
}, 4)
# 9.5k cycle 2: the swing heel task is saturating the hip (kp = om_sw^2 = 16 /s2, errors of 3-6 m): gains (qp_om_sw, qp_w_swing), swing target tied to the CoM (qp_sw_follow), heel-only touchdown (qp_td_heel)
SETS['k2'] = _seeded({
  'Lom2': dict(B, qp_om_sw=2.0),
  'Lom1': dict(B, qp_om_sw=1.0),
  'Lw100': dict(B, qp_w_swing=100.0),
  'Lheel': dict(B, qp_td_heel=1),
  'Lf1': dict(B, qp_sw_follow=1.0),
  'Lf05': dict(B, qp_sw_follow=0.5),
  'Lom2f1': dict(B, qp_om_sw=2.0, qp_sw_follow=1.0),
  'Lom1f1h': dict(B, qp_om_sw=1.0, qp_sw_follow=1.0, qp_td_heel=1),
}, 4)
# 9.5k cycle 3: swing heel acceleration limit (qp_sw_amax), touchdown validity (qp_td_air_frac, qp_td_xoff_max, qp_td_heel)
SETS['k3'] = _seeded({
  'Ma20': dict(B, qp_sw_amax=20.0),
  'Ma10': dict(B, qp_sw_amax=10.0),
  'Mair': dict(B, qp_td_air_frac=0.3),
  'Mairh': dict(B, qp_td_air_frac=0.3, qp_td_heel=1),
  'Mxo8': dict(B, qp_td_xoff_max=8.0),
  'Ma20air': dict(B, qp_sw_amax=20.0, qp_td_air_frac=0.3, qp_td_heel=1),
  'Ma20airom2': dict(B, qp_sw_amax=20.0, qp_td_air_frac=0.3, qp_td_heel=1, qp_om_sw=2.0),
  'Ma20airxo': dict(B, qp_sw_amax=20.0, qp_td_air_frac=0.3, qp_td_heel=1, qp_td_xoff_max=8.0),
}, 4)
# 9.5l: joint-space swing task (qp_swj) + heel task blended in at the end of the swing; C = 9.5k best (amax 20 + air gate 0.3 + heel-only touchdown + om_sw 2)
C = dict(B, qp_sw_amax=20.0, qp_td_air_frac=0.3, qp_td_heel=1, qp_om_sw=2.0)
SETS['l1'] = _seeded({
  'Ubase': dict(C),
  'Uj1': dict(C, qp_swj=1e3),
  'Uj1om3': dict(C, qp_swj=1e3, qp_swj_om=3.0),
  'Uj1f3': dict(C, qp_swj=1e3, qp_swj_td_frac=0.3),
  'Uj1a3': dict(C, qp_swj=1e3, qp_swj_amax=3.0),
  'Uj1ank': dict(C, qp_swj=1e3, qp_swj_ankle=1),
  'Uj100': dict(C, qp_swj=100.0),
  'Uj1noam': dict(C, qp_swj=1e3, qp_sw_amax=0.0),
}, 4)
# 9.5l cycle 2: om 3 and the ankle joint were the best of cycle 1
D = dict(C, qp_swj=1e3, qp_swj_om=3.0)
SETS['l2'] = _seeded({
  'Vom3ank': dict(D, qp_swj_ankle=1),
  'Vom3ankf3': dict(D, qp_swj_ankle=1, qp_swj_td_frac=0.3),
  'Vom4': dict(D, qp_swj_om=4.0),
  'Vom4ank': dict(D, qp_swj_om=4.0, qp_swj_ankle=1),
  'Vom3noam': dict(D, qp_sw_amax=0.0),
  'Vom3ankam40': dict(D, qp_swj_ankle=1, qp_sw_amax=40.0),
  'Vom3ankja10': dict(D, qp_swj_ankle=1, qp_swj_amax=10.0),
  'Vom3ankw3': dict(D, qp_swj_ankle=1, qp_swj=3e3),
}, 4)
# 9.5l cycle 3: late swings (heel 12-22 m off at tau 5-9 s) on the best (D + ankle, weight 1e3 / 3e3)
DA = dict(D, qp_swj_ankle=1)
SETS['l3'] = _seeded({
  'Wfol05': dict(DA, qp_sw_follow=0.5),
  'Wxo8': dict(DA, qp_td_xoff_max=8.0),
  'Wf1': dict(DA, qp_swj_td_frac=0.1),
  'Wom25': dict(DA, qp_swj_om=2.5),
  'Ww3a30': dict(DA, qp_swj=3e3, qp_sw_amax=30.0),
  'Ww3ja4': dict(DA, qp_swj=3e3, qp_swj_amax=4.0),
  'Ww3fol05': dict(DA, qp_swj=3e3, qp_sw_follow=0.5),
  'Ww3f1': dict(DA, qp_swj=3e3, qp_swj_td_frac=0.1),
}, 4)
# 9.5m: stance-side speed regulation on the best 9.5l config (D = swj 1e3 om 3): CoM-x acceleration clamp (qp_cdx_max), capture-point cap of the swing heel target (qp_sw_cp), and existing weights / gains
SETS['m1'] = _seeded({
  'Xbase': dict(D),
  'Xcdx3': dict(D, qp_cdx_max=3.0),
  'Xcdx5': dict(D, qp_cdx_max=5.0),
  'Xcp08': dict(D, qp_sw_cp=0.8),
  'Xcp10': dict(D, qp_sw_cp=1.0),
  'Xcp12': dict(D, qp_sw_cp=1.2),
  'Xwcz': dict(D, qp_w_cz=1e5),
  'Xomx05': dict(D, qp_om_x=0.5),
}, 4)
# 9.5m cycle 2: the ladder climbs through contact chatter (rows 4 -> 6 within 0.2 s at 5.5 m/s) and the speed then falls; climb rule (hold, touchdown source, threshold), reference clock tied to the speed (qp_scale_min), row cap as a diagnostic only
SETS['m2'] = _seeded({
  'Ybase': dict(D),
  'Yhold4': dict(D, qp_match_hold=4),
  'Yhold8': dict(D, qp_match_hold=8),
  'Yclimb0': dict(D, qp_match_climb_td=0),
  'Yscale06': dict(D, qp_scale_min=0.6),
  'Yscale08': dict(D, qp_scale_min=0.8),
  'Ymax5': dict(D, qp_match_max_row=5),
  'Yupthr08': dict(D, qp_match_up_thr=0.8),
}, 4)
# 9.5m cycle 3: CoM-z weight x10 gave the narrowest spread (24.8-30.5) in cycle 1; combine (z weight / gain, CoM-x weight, clamp, capture cap, ankle joint task)
Z = dict(D, qp_w_cz=1e5)
SETS['m3'] = _seeded({
  'Zbase': dict(D),
  'Zwcz1': dict(Z),
  'Zwcz3': dict(D, qp_w_cz=3e5),
  'Zwcz1omz3': dict(Z, qp_om_z=3.0),
  'Zwcz1cx3': dict(Z, qp_w_cx=3e3),
  'Zwcz1cdx5': dict(Z, qp_cdx_max=5.0),
  'Zwcz1cp10': dict(Z, qp_sw_cp=1.0),
  'Zwcz1ank': dict(Z, qp_swj_ankle=1),
}, 4)
# T-005 r5 6a: step-level planner (qp_plan). P = 9.5m base D with the rematch off (qp_plan ignores it anyway); plan defaults: kxi 1, lmin .5, lmax 1.3, td_frac .8, td_tol .2, zkp 4, zkd 4, wz 1e5
P = dict(D, qp_td_rematch=0.0, qp_plan=1)
SETS['p1'] = _seeded({
  'Qplan': dict(P),
  'Qk05': dict(P, qp_plan_kxi=0.5),
  'Qk2': dict(P, qp_plan_kxi=2.0),
  'Qtol40': dict(P, qp_plan_td_tol=0.4),
  'Qfp0': dict(P, qp_plan_fp=0.0),
  'Qwz1e4': dict(P, qp_plan_wz=1e4),
  'Qwx3': dict(P, qp_plan_wx=3e3),
  'Qz9': dict(P, qp_plan_zkp=9.0, qp_plan_zkd=6.0),
}, 4)
# cycle 2: plan-mode ladder (climb at a QP touchdown, node 0, speed gate qp_plan_up; hand-off row by speed)
SETS['p2'] = _seeded({
  'Rbase': dict(P),
  'Rup065': dict(P, qp_plan_up=0.65),
  'Rup09': dict(P, qp_plan_up=0.9),
  'Rk2': dict(P, qp_plan_kxi=2.0),
  'Rtd06': dict(P, qp_plan_td_frac=0.6),
  'Rz9': dict(P, qp_plan_zkp=9.0, qp_plan_zkd=6.0),
  'Rfp0': dict(P, qp_plan_fp=0.0),
  'Rtol40': dict(P, qp_plan_td_tol=0.4),
}, 4)
# cycle 3: the swing foot lags the planned foothold (heel task only in the last 20 % of the swing, quintic over < 1 s) and lands 1-1.5 s late (late drop 2 m/s): heel task over the whole swing, faster late drop, frozen CoM reference when late
S1 = dict(P, qp_swj_td_frac=1.0)
SETS['p3'] = _seeded({
  'Sf1': dict(S1),
  'Sf05': dict(P, qp_swj_td_frac=0.5),
  'Slv6': dict(P, qp_late_vz=6.0),
  'Sf1lv6': dict(S1, qp_late_vz=6.0),
  'Sf1lv6lc0': dict(S1, qp_late_vz=6.0, qp_late_cx=0.0),
  'Sf1lv6a40': dict(S1, qp_late_vz=6.0, qp_sw_amax=40.0),
  'Sf1lv6t40': dict(S1, qp_late_vz=6.0, qp_plan_td_tol=0.4),
  'Sf1lv6k2': dict(S1, qp_late_vz=6.0, qp_plan_kxi=2.0),
}, 4)
# T-005 r5 6b: planner-owned swing (min-jerk heel x, row z profile, 2-link IK joint targets), descent term, forced support switch, speed ladder
U = dict(P, qp_swj_ankle=1)
SETS['u1'] = _seeded({
  'Ubase': dict(U),
  'Unoank': dict(P),
  'Uapex07': dict(U, qp_plan_apex=0.7),
  'Uvd2': dict(U, qp_plan_vd=2.0),
  'Utau1': dict(U, qp_plan_fp_tau=1.0),
  'Uheelw03': dict(U, qp_plan_heelw=0.3),
  'Uladder0': dict(U, qp_plan_ladder=0),
  'Uswj3': dict(U, qp_swj=3e3),
}, 4)
# 6b cycle 2: swing curve starts at the measured lift-off, z profile on the same progress
SETS['u2'] = _seeded({
  'Vbase': dict(U),
  'Vapex07': dict(U, qp_plan_apex=0.7),
  'Vvd2': dict(U, qp_plan_vd=2.0),
  'Vheelw03': dict(U, qp_plan_heelw=0.3),
  'Vladder0': dict(U, qp_plan_ladder=0),
  'Vswj3': dict(U, qp_swj=3e3),
  'Vlv6': dict(U, qp_late_vz=6.0),
  'Vam40': dict(U, qp_sw_amax=40.0, qp_swj_amax=12.0),
}, 4)
# 6b cycle 3: the swing foot touches toe-first (mask heel A + toe B for 1.2 s, no heel touchdown): toe contact counts, earlier forced switch
X = dict(U, qp_td_heel=0)
SETS['u3'] = _seeded({
  'Wbase': dict(X),
  'Wvd2': dict(X, qp_plan_vd=2.0),
  'Wlv6': dict(X, qp_late_vz=6.0),
  'Wfz1': dict(X, qp_plan_force_z=1.0),
  'Wapex07': dict(X, qp_plan_apex=0.7),
  'Wair0': dict(X, qp_td_air_frac=0.0),
  'Wswj3a07': dict(X, qp_swj=3e3, qp_plan_apex=0.7),
  'Wff105': dict(X, qp_plan_force_frac=1.05, qp_plan_force_z=1.0),
}, 4)
# T-005 r5 6c: explicit double support (qp_plan_ds), reachable foothold (qp_plan_reach), swing PD scale (qp_pd_swing) for the apex overshoot
SETS['y1'] = _seeded({
  'Ybase': dict(U),
  'Yds0': dict(U, qp_plan_ds=0),
  'Yreach0': dict(U, qp_plan_reach=0.0),
  'Ypd0': dict(U, qp_pd_swing=0.0),
  'Ypd005': dict(U, qp_pd_swing=0.05),
  'Yvd2': dict(U, qp_plan_vd=2.0),
  'Ypd0vd2': dict(U, qp_pd_swing=0.0, qp_plan_vd=2.0),
  'Ypd0a07': dict(U, qp_pd_swing=0.0, qp_plan_apex=0.7),
}, 4)
# 6c cycle 2: the IK now respects the ankle stop (foot pitch follows the ankle range); the swing foot landed toe-first at -10 .. -23 deg
SETS['y2'] = _seeded({
  'Zbase': dict(U),
  'Zvd2': dict(U, qp_plan_vd=2.0),
  'Zpd005': dict(U, qp_pd_swing=0.05),
  'Zpd005vd2': dict(U, qp_pd_swing=0.05, qp_plan_vd=2.0),
  'Za07': dict(U, qp_plan_apex=0.7),
  'Zds0': dict(U, qp_plan_ds=0),
  'Zrm3': dict(U, qp_rom_margin_deg=3.0),
  'Zfp0': dict(U, qp_fp_rom=0),
}, 4)
# 6c cycle 3: unreachable heel targets projected onto the reach circle (IK no longer replays the reference joints), best 6c combos
SETS['y3'] = _seeded({
  'Abase': dict(U),
  'Apd005': dict(U, qp_pd_swing=0.05),
  'Apd005a07': dict(U, qp_pd_swing=0.05, qp_plan_apex=0.7),
  'Apd01': dict(U, qp_pd_swing=0.1),
  'Aa07': dict(U, qp_plan_apex=0.7),
  'Apd005vd2a07': dict(U, qp_pd_swing=0.05, qp_plan_vd=2.0, qp_plan_apex=0.7),
  'Apd005lv6': dict(U, qp_pd_swing=0.05, qp_late_vz=6.0),
  'Areach0': dict(U, qp_plan_reach=0.0, qp_pd_swing=0.05),
}, 4)
# T-005 r6 7a: sole-orientation task (qp_foot). Control = the 6c best (pd 0.05, reach clamp off, 4-seed median 24.2 s)
R = dict(U, qp_pd_swing=0.05, qp_plan_reach=0.0)
SETS['p1'] = _seeded({
  'Rbase': dict(R),
  'Rfoot': dict(R, qp_foot=1),
  'Rfootnodrv': dict(R, qp_foot=1, qp_foot_drive=0),
  'Rfootw10': dict(R, qp_foot=1, qp_foot_w=10.0),
  'Rfootheel0': dict(R, qp_foot=1, qp_foot_heel_deg=0.0),
  'Rfootst0': dict(R, qp_foot=1, qp_foot_stance=0),
  'Rfootrate15': dict(R, qp_foot=1, qp_foot_rate=1.5),
  'Rfootpre03': dict(R, qp_foot=1, qp_foot_pre=0.3),
}, 4)
# T-005 r6 7b: contact constraints from the measured contact points (qp_foot_meas); drive off (7a: it fights the stance ankle PD)
N7 = dict(R, qp_foot=1, qp_foot_drive=0)
SETS['p2'] = _seeded({
  'Sbase': dict(R),
  'Snodrv': dict(N7),
  'Smeas': dict(N7, qp_foot_meas=1),
  'Smeasm15': dict(N7, qp_foot_meas=1, qp_foot_cop_m=0.15),
  'Smeasm03': dict(N7, qp_foot_meas=1, qp_foot_cop_m=0.03),
  'Smeasfl05': dict(N7, qp_foot_meas=1, qp_foot_fric_floor=0.05),
  'Smeasheel0': dict(N7, qp_foot_meas=1, qp_foot_heel_deg=0.0),
  'Smeasw3': dict(N7, qp_foot_meas=1, qp_foot_w=3.0),
}, 4)
# T-005 r6 7c: forced load transfer (qp_foot_unload) + planner reach on top of the measured-contact constraints (7b best: qp_foot_meas 1, cop_m 0.08)
M7 = dict(N7, qp_foot_meas=1)
SETS['p3'] = _seeded({
  'Tmeas': dict(M7),
  'Tun1': dict(M7, qp_foot_unload=1.0),
  'Tun2at1': dict(M7, qp_foot_unload=2.0, qp_foot_unload_at=1.0),
  'Tun05': dict(M7, qp_foot_unload=0.5),
  'Tlmax09': dict(M7, qp_plan_lmax=0.9),
  'Tun1lmax09': dict(M7, qp_foot_unload=1.0, qp_plan_lmax=0.9),
  'Tun1kxi2': dict(M7, qp_foot_unload=1.0, qp_plan_kxi=2.0),
  'Tun1ds0': dict(M7, qp_foot_unload=1.0, qp_plan_ds=0),
}, 4)
# T-005 r6 7c cycle 2: reach-driven CoM height (qp_foot_zdip) on the 7b best (qp_foot_meas 1)
SETS['p4'] = _seeded({
  'Umeas': dict(M7),
  'Uz1': dict(M7, qp_foot_zdip=1.0),
  'Uz1m1': dict(M7, qp_foot_zdip=1.0, qp_foot_zdip_margin=1.0),
  'Uz1max2': dict(M7, qp_foot_zdip=1.0, qp_foot_zdip_max=2.0),
  'Uz15': dict(M7, qp_foot_zdip=1.5),
  'Uz1up3': dict(M7, qp_foot_zdip=1.0, qp_foot_zdip_up=3.0),
  'Uz1un1': dict(M7, qp_foot_zdip=1.0, qp_foot_unload=1.0),
  'Uz1pd01': dict(M7, qp_foot_zdip=1.0, qp_pd_swing=0.1),
}, 4)
# T-005 r6 7c cycle 2: stiffer CoM-height tracking for the reach-driven dip, shorter steps
SETS['p5'] = _seeded({
  'Vmeas': dict(M7),
  'Vz1k10': dict(M7, qp_foot_zdip=1.0, qp_plan_zkp=10.0, qp_plan_zkd=8.0),
  'Vz1k20': dict(M7, qp_foot_zdip=1.0, qp_plan_zkp=20.0, qp_plan_zkd=10.0),
  'Vz1wz6': dict(M7, qp_foot_zdip=1.0, qp_plan_wz=1e6, qp_plan_zkp=10.0, qp_plan_zkd=8.0),
  'Vz1l08': dict(M7, qp_foot_zdip=1.0, qp_plan_lmax=0.8, qp_plan_zkp=10.0, qp_plan_zkd=8.0),
  'Vl08': dict(M7, qp_plan_lmax=0.8),
  'Vz1ap05': dict(M7, qp_foot_zdip=1.0, qp_plan_apex=0.5, qp_plan_zkp=10.0, qp_plan_zkd=8.0),
  'Vzkp10': dict(M7, qp_plan_zkp=10.0, qp_plan_zkd=8.0),
}, 4)
# T-005 r7 8a/8b: round-7 rows (qp_ref_v5) and controller flags on the 7b best (M7 = qp_foot 1, qp_foot_meas 1, qp_foot_drive 0)
V5 = dict(M7, qp_ref_v5=1)
R7C = dict(V5, qp_r7_noforce=1, qp_r7_clock=1, qp_foot_cop_heel_m=1.5, qp_foot_cop_toe_m=2.5, qp_fcap=1.5)
SETS['r1'] = _seeded({
  'Wctl': dict(M7),                       # control: the 7b best on the v4 rows (24.8 s)
  'Wv5': dict(V5),                        # r7 rows only
  'Wv5nf': dict(V5, qp_r7_noforce=1),
  'Wv5clk': dict(V5, qp_r7_clock=1),
  'Wv5cop': dict(V5, qp_foot_cop_heel_m=1.5, qp_foot_cop_toe_m=2.5),
  'Wv5all': dict(R7C),
}, 4)
SETS['r0'] = _seeded({
  'Xctl': dict(M7),
  'Xnf': dict(M7, qp_r7_noforce=1),
  'Xclk': dict(M7, qp_r7_clock=1),
  'Xcop': dict(M7, qp_foot_cop_heel_m=1.5, qp_foot_cop_toe_m=2.5),
  'Xfc': dict(M7, qp_fcap=1.5),
  'Xall': dict(M7, qp_r7_noforce=1, qp_r7_clock=1, qp_foot_cop_heel_m=1.5, qp_foot_cop_toe_m=2.5, qp_fcap=1.5),
}, 4)
SETS['b0'] = _seeded({
  'Yctl': dict(M7),
  'YA': dict(M7, exp_toe_m=7.0),
  'YB': dict(M7, exp_ankle_limit_nm=1.6e9),
  'YC': dict(M7, exp_toe_m=7.0, exp_ankle_limit_nm=1.6e9),
}, 4)
SETS['r2'] = _seeded({
  'Wv5': dict(V5),
  'Wv5nf': dict(V5, qp_r7_noforce=1),
  'Wv5clk': dict(V5, qp_r7_clock=1),
  'Wv5all': dict(R7C),
}, 4)
SETS['r3'] = _seeded({
  'Zfp1': dict(V5, qp_r7_fp=1.0),
  'Zfp05': dict(V5, qp_r7_fp=0.5),
  'Zfp1clk': dict(V5, qp_r7_fp=1.0, qp_r7_clock=1),
  'Zfp1cop': dict(V5, qp_r7_fp=1.0, qp_foot_cop_heel_m=1.5, qp_foot_cop_toe_m=2.5),
  'Zfp1fc': dict(V5, qp_r7_fp=1.0, qp_fcap=1.5),
  'Zfp1v4': dict(M7, qp_r7_fp=1.0),
}, 4)
C7 = dict(V5, qp_r7_clock=1)
SETS['r4'] = _seeded({
  'A1': dict(C7),
  'A2': dict(C7, qp_r7_fp=0.5),
  'A3': dict(C7, qp_plan_lmax=1.0),
  'A4': dict(C7, qp_plan_lmax=0.8),
  'A5': dict(C7, qp_plan_kxi=2.0),
  'A6': dict(C7, qp_pd_swing=0.1),
  'A7': dict(C7, qp_r7_fp=0.5, qp_plan_lmax=1.0),
  'A8': dict(C7, qp_plan_apex=0.5),
}, 4)
L7 = dict(C7, qp_plan_lmax=1.0)
SETS['r5'] = _seeded({
  'B2': dict(C7, qp_plan_lmax=0.9),
  'B3': dict(C7, qp_plan_lmax=1.1),
  'B4': dict(L7, qp_plan_lmin=0.8),
  'B5': dict(L7, qp_r7_noforce=1),
  'B6': dict(L7, qp_foot_cop_heel_m=1.5, qp_foot_cop_toe_m=2.5),
  'B7': dict(L7, qp_fcap=1.5),
  'B8': dict(L7, qp_r7_fp=0.25),
  'B9': dict(L7, qp_plan_lmin=0.7),
}, 4)
SETS['r6'] = _seeded({
  'D1': dict(L7),
  'D2': dict(L7, qp_r7_fp=0.5),
  'D3': dict(C7),
  'D4': dict(L7, qp_plan_lmin=0.8),
  'D5': dict(L7, qp_plan_lmin=0.7),
}, 4)
SETS['r7'] = _seeded({
  'E1': dict(L7, qp_ladder_up=0.6),
  'E2': dict(L7, qp_ladder_up=0.7),
  'E3': dict(L7, qp_ladder_up=0.5),
  'E4': dict(L7, qp_ladder_up=0.4),
  'E5': dict(L7, qp_ladder_up=0.6, qp_r7_fp=0.5),
}, 4)
SETS['b1'] = _seeded({
  'Qctl': dict(L7),
  'QA': dict(L7, qp_ref_v5=2, exp_toe_m=7.0),
  'QB': dict(L7, exp_ankle_limit_nm=1.6e9),
  'QC': dict(L7, qp_ref_v5=4, exp_toe_m=7.0, exp_ankle_limit_nm=1.6e9),
  'QAv5': dict(L7, exp_toe_m=7.0),
  'QCv5': dict(L7, exp_toe_m=7.0, exp_ankle_limit_nm=1.6e9),
}, 4)
SETS['t1'] = _seeded({
  'T1': dict(L7, qp_ref_v5=5),
  'T2': dict(C7, qp_ref_v5=5),
  'T3': dict(V5, qp_ref_v5=5),
  'T4': dict(L7, qp_ref_v5=5, qp_pd_swing=0.1),
}, 4)

# ---- T-005 round 8 ----------------------------------------------------------------------------------------------------------------------
# control = L7 (r7 best, v5 rows); V6 = the same flags on the round-8 ladder (very slow rows Fr 0.005 / 0.010 / 0.015 added under the same hard limits)
V6 = dict(L7, qp_ref_v5=6)
R8 = dict(qp_r8_retime=1)
# cycle 2: round-8 rows (v6 = slow rows under the hard limits, v6d = slow rows derated to 0.7 of the torque limits) with the cycle-1 lessons (whole-clock retime hurts; keep the CoM clock real, optionally stretch the planner's remaining time)
V6d = dict(L7, qp_ref_v5=7)
RT = dict(qp_r8_retime=1, qp_r8_refclock=1)
RTT = dict(qp_r8_retime=1, qp_r8_refclock=1, qp_r8_trem=1)
SETS['q1'] = _seeded({
  'L7': dict(L7),
  'V6': dict(V6),
  'V6d': dict(V6d),
  'V6c': dict(V6, qp_plan_lmax=1.3),
  'V6rt': dict(V6, **RT),
  'V6rtt': dict(V6, **RTT),
  'V6sl85': dict(V6, qp_r8_swlim=0.85),
  'V6rttsl85': dict(V6, qp_r8_swlim=0.85, **RTT),
  'V6drt': dict(V6d, **RT),
  'V6drtt': dict(V6d, **RTT),
  'L7rt': dict(L7, **RT),
  'L7rtt': dict(L7, **RTT),
}, 4)

# cycle 1: controller flags on the round-7 rows (v5) while the round-8 rows are still being optimised
SETS['q0'] = _seeded({
  'L7': dict(L7),
  'L7rt': dict(L7, **R8),
  'L7rt_g5': dict(L7, qp_r8_retime=1, qp_r8_gmin=0.5),
  'L7rt_k6': dict(L7, qp_r8_retime=1, qp_r8_gk=6.0),
  'L7sl90': dict(L7, qp_r8_swlim=0.90),
  'L7sl80': dict(L7, qp_r8_swlim=0.80),
  'L7rtsl90': dict(L7, qp_r8_retime=1, qp_r8_swlim=0.90),
  'L7rtsat': dict(L7, qp_r8_retime=1, qp_r8_satk=0.7),
  'L7rtsatsl': dict(L7, qp_r8_retime=1, qp_r8_satk=0.7, qp_r8_swlim=0.90),
}, 4)

# cycle 3: the v6 ladder without its first row(s): row 0 at Fr 0.010 (v6e) or 0.015 (v6g), and the flags of the best cycle-2 configuration
V6e = dict(L7, qp_ref_v5=8); V6g = dict(L7, qp_ref_v5=9)
SETS['q2'] = _seeded({
  'L7': dict(L7),
  'V6e': dict(V6e),
  'V6g': dict(V6g),
  'V6grtt': dict(V6g, **RTT),
  'V6ersl': dict(V6e, qp_r8_swlim=0.85),
  'V6gc7': dict(V6g, qp_plan_lmax=1.3),
  'V6gclk0': dict(V6g, qp_r7_clock=0),
  'V6gup': dict(V6g, qp_ladder_up=0.7),
}, 4)

# ---- T-005 round 9 ----------------------------------------------------------------------------------------------------------------------
# stance-side start-up: qp_r9_gate (lift-off waits for the DCM offset over the stance heel) and qp_r9_launch (reference CoM velocity bias until lift-off)
G = lambda f, tm=1.0: dict(qp_r9_gate=f, qp_r9_gate_tmax=tm)
U = lambda v, ramp=0.3, vf=1.0: dict(qp_r9_launch=v, qp_r9_ramp=ramp, qp_r9_vfrac=vf)
SETS['s1'] = _seeded({
  'L7': dict(L7),
  'G50': dict(L7, **G(0.5)),
  'G70': dict(L7, **G(0.7)),
  'G70t2': dict(L7, **G(0.7, 2.0)),
  'U1': dict(L7, **U(1.0)),
  'U2': dict(L7, **U(2.0)),
  'GU': dict(L7, **G(0.6, 1.5), **U(1.5)),
  'V6gGU': dict(V6g, **G(0.6, 1.5), **U(1.5)),
  'V6GU': dict(V6, **G(0.6, 1.5), **U(1.5)),
}, 4)
SETS['s2'] = _seeded({
  'L7': dict(L7),
  'G30': dict(L7, **G(0.3)),
  'G90': dict(L7, **G(0.9)),
  'G90t05': dict(L7, **G(0.9, 0.5)),
  'G100t3': dict(L7, **G(1.0, 3.0)),
  'U05': dict(L7, **U(0.5)),
  'U1v07': dict(L7, **U(1.0, 0.3, 0.7)),
  'U1r1': dict(L7, **U(1.0, 1.0)),
  'U1tau5': dict(L7, qp_r9_tau=5.0, **U(1.0)),
  'G50U05': dict(L7, **G(0.5), **U(0.5)),
  'G90U05': dict(L7, **G(0.9, 0.5), **U(0.5)),
}, 4)
def load_set(name):
    return SETS[name]
def check_line(line):
    seen = set()
    def hook(pairs):
        for k, _ in pairs:
            assert k not in seen, 'DUPLICATE key ' + k
            seen.add(k)
        return dict(pairs)
    d = json.loads(line, object_pairs_hook=hook)
    for k in d: assert k in FIELDS, 'UNKNOWN key ' + k
    return d
def main():
    name, out = sys.argv[1], sys.argv[2]
    deltas = load_set(name); lines = []; eff = {}
    for vn, delta in deltas.items():
        d = dict(BASE); d.update(delta)
        # write base keys then delta keys; a delta key already in BASE replaces it, so each key appears once
        s = json.dumps(d, separators=(',', ':'))
        lines.append(vn + ' ' + s); eff[vn] = check_line(s)
    for (a, da), (b, db) in itertools.combinations(eff.items(), 2): assert da != db, f'{a} == {b}'
    open(out, 'w').write('\n'.join(lines) + '\n')
    for vn, d in eff.items(): print(vn, {k: v for k, v in d.items() if k not in BASE or BASE[k] != v})
    print('ok', len(eff), 'variants, keys unique, all known, pairwise different;', out)
main()
