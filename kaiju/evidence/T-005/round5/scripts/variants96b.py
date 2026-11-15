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
