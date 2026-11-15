#!/usr/bin/env python
"""T-005 assist: build a variants file (name + JSON per line). L7 = the r7 best pure-physics config (119bc1b, step8b.md); each variant adds walk_mode 1 + as_* gains.
Usage: mkvar.py <set> <out.txt> ; 4 'seeds' = qp_pd_scale 0.25 + k 1e-4 (deterministic run, a perturbation samples the spread)."""
import json, sys, os
L7 = json.loads('{"imp":3,"imp2_phase_sync":1,"qp_engage_speed_frac":0.3,"qp_ladder":1,"qp_match":1,"qp_match_ss":1,"qp_match_climb_td":1,"qp_lateral":1,"qp_om_y":0.3,"qp_w_tau":1,"qp_match_thr":0.8,"qp_match_up_thr":1.2,"qp_match_hold":2,"qp_yaw_trunk":1,"qp_om_yaw":2.0,"qp_mu":0.5,"qp_fr_margin":0.95,"qp_ref_v4":1,"qp_arm_ref":1,"qp_yaw_cap":0.035,"qp_rom":1,"qp_fp_rom":1,"qp_z_hold_ss":3.0,"qp_rom_margin_deg":6.0,"qp_td_rematch":0.0,"qp_late_cx":1.0,"qp_diag":1,"qp_sw_amax":20.0,"qp_td_air_frac":0.3,"qp_td_heel":1,"qp_om_sw":2.0,"qp_swj":1000.0,"qp_swj_om":3.0,"qp_plan":1,"qp_swj_ankle":1,"qp_pd_swing":0.05,"qp_plan_reach":0.0,"qp_foot":1,"qp_foot_drive":0,"qp_foot_meas":1,"qp_ref_v5":1,"qp_r7_clock":1,"qp_plan_lmax":1.0}')
A = dict(L7, walk_mode=1)
SETS = {}
def seeded(cfgs, n=4): return {f'{k}s{i}': dict(d, qp_pd_scale=round(0.25 + 1e-4 * i, 6)) for k, d in cfgs.items() for i in range(n)}
# s1: coarse ladder, each assist component alone then together (1 seed each)
SETS['s1'] = {
  'X0ctl': dict(L7),
  'X1servo': dict(A, as_v_cap=0.1, as_v_tau=2.0),
  'X2up': dict(A, as_up=0.3),
  'X3tilt': dict(A, as_tilt_k=1.0, as_tilt_d=1.0, as_tilt_cap=0.2),
  'X4all': dict(A, as_v_cap=0.1, as_v_tau=2.0, as_up=0.3, as_tilt_k=1.0, as_tilt_d=1.0, as_tilt_cap=0.2),
  'X5strong': dict(A, as_v_cap=0.3, as_v_tau=1.0, as_up=0.5, as_tilt_k=3.0, as_tilt_d=3.0, as_tilt_cap=0.5),
}
SETS['s2'] = {
  'Y1servo': dict(A, as_v_cap=0.5, as_v_tau=0.5),
  'Y2zpd': dict(A, as_z_om=1.0, as_z_cap=0.5),
  'Y3tilt': dict(A, as_tilt_k=5.0, as_tilt_d=5.0, as_tilt_cap=1.0),
  'Y4all': dict(A, as_v_cap=0.5, as_v_tau=0.5, as_z_om=1.0, as_z_cap=0.5, as_tilt_k=5.0, as_tilt_d=5.0, as_tilt_cap=1.0),
  'Y5servo_tilt': dict(A, as_v_cap=0.5, as_v_tau=0.5, as_tilt_k=5.0, as_tilt_d=5.0, as_tilt_cap=1.0),
}
K = {"walk_mode": 2}   # walk_mode 2 needs no imp / qp flags: the base walk_tuning.json + the assisted gait
SETS['k1'] = {
  'K0': dict(K),
  'K1sup07': dict(K, kin_support=0.7),
  'K2sup10': dict(K, kin_support=1.0),
}
SETS['k2'] = {
  'P0sup09g1': dict(K, kin_support=0.9),
  'P1sup05g1': dict(K, kin_support=0.5),
  'P2sup05g6': dict(K, kin_support=0.5, kin_gain=6.0),
  'P3sup02g6': dict(K, kin_support=0.2, kin_gain=6.0),
  'P4sup00g6': dict(K, kin_support=0.0, kin_gain=6.0),
  'P5sup00g20': dict(K, kin_support=0.0, kin_gain=20.0),
  'P6meas05g6': dict(K, kin_support=0.5, kin_gain=6.0, kin_ik_plan=0),
}
SETS['k3'] = {
  'Q0sup09g1': dict(K, kin_support=0.9),
  'Q1sup05g6': dict(K, kin_support=0.5, kin_gain=6.0),
  'Q2sup00g6': dict(K, kin_support=0.0, kin_gain=6.0),
  'Q3sup00g20': dict(K, kin_support=0.0, kin_gain=20.0),
  'Q4sup00g6m': dict(K, kin_support=0.0, kin_gain=6.0, kin_ik_plan=0),
}
B6 = dict(K, kin_support=0.0, kin_gain=6.0)
SETS['k4'] = {
  'R0k8d4': dict(B6),
  'R1k2d08': dict(B6, kin_rot_k=2.0, kin_rot_d=0.8),
  'R2k4d1': dict(B6, kin_rot_k=4.0, kin_rot_d=1.0),
  'R3k1d05': dict(B6, kin_rot_k=1.0, kin_rot_d=0.5),
  'R4k2d08s3': dict(B6, kin_rot_k=2.0, kin_rot_d=0.8, kin_support=0.3),
  'R5k2d08om2': dict(B6, kin_rot_k=2.0, kin_rot_d=0.8, kin_om=2.0),
  'R6k2d08om5': dict(B6, kin_rot_k=2.0, kin_rot_d=0.8, kin_om=5.0),
}
SETS['k5'] = {
  'S0plan2': dict(B6, kin_rot_k=2.0, kin_rot_d=0.8, kin_ik_plan=2),
  'S1plan1': dict(B6, kin_rot_k=2.0, kin_rot_d=0.8, kin_ik_plan=1),
  'S2plan1k8': dict(B6, kin_ik_plan=1),
  'S3plan1sup03': dict(B6, kin_rot_k=2.0, kin_rot_d=0.8, kin_ik_plan=1, kin_support=0.3),
  'S4plan1g12': dict(B6, kin_rot_k=2.0, kin_rot_d=0.8, kin_ik_plan=1, kin_gain=12.0),
  'S5plan0': dict(B6, kin_rot_k=2.0, kin_rot_d=0.8, kin_ik_plan=0),
}
SETS['k6'] = {
  'T0k8d4': dict(B6),
  'T1k2d08': dict(B6, kin_rot_k=2.0, kin_rot_d=0.8),
  'T2k4d2': dict(B6, kin_rot_k=4.0, kin_rot_d=2.0),
  'T3k8d4g12': dict(B6, kin_gain=12.0),
  'T4k8d4s03': dict(B6, kin_support=0.3),
  'T5k8d4s05': dict(B6, kin_support=0.5),
}
SETS['k7'] = {
  'U0base': dict(B6),
  'U1om15': dict(B6, kin_om=1.5),
  'U2om15c2': dict(B6, kin_om=1.5, kin_f_cap=0.2, kin_fz_cap=0.2),
  'U3om1c1': dict(B6, kin_om=1.0, kin_f_cap=0.1, kin_fz_cap=0.1),
  'U4om15c2rot': dict(B6, kin_om=1.5, kin_f_cap=0.2, kin_fz_cap=0.2, kin_rot_cap=0.2),
  'U5om15c2hz3': dict(B6, kin_om=1.5, kin_f_cap=0.2, kin_fz_cap=0.2, kin_hz=3.0),
}
C1 = dict(B6, kin_om=1.5, kin_f_cap=0.2, kin_fz_cap=0.2)
SETS['k8'] = {
  'V0c1': dict(C1),
  'V1c1noise': dict(C1, kin_noise=0.05, kin_seed=1),
  'V2c1rot2': dict(C1, kin_rot_cap=0.2),
  'V3c1lift1': dict(C1, kin_lift_m=1.0),
  'V4c1clear': dict(C1, kin_clear=0.1),
}
SETS['k9'] = {
  'W0hd02': dict(C1, kin_hdelay=0.2, kin_lift_m=1.0),
  'W1hd02ds3': dict(C1, kin_hdelay=0.2, kin_lift_m=1.0, kin_ds=0.3),
  'W2hd03ds3sw': dict(C1, kin_hdelay=0.3, kin_lift_m=1.0, kin_ds=0.3, kin_sway_from=0.1),
  'W3hd02ds3sw1.5': dict(C1, kin_hdelay=0.2, kin_lift_m=1.0, kin_ds=0.3, kin_sway_m=1.5),
  'W4hd02clear': dict(C1, kin_hdelay=0.2, kin_lift_m=1.0, kin_clear=0.1),
}
SETS['k10'] = {
  'X0cl10': dict(C1, kin_clear=0.10),
  'X1cl12': dict(C1, kin_clear=0.12),
  'X2cl15': dict(C1, kin_clear=0.15),
  'X3cl12hd': dict(C1, kin_clear=0.12, kin_hdelay=0.15, kin_lift_m=1.0),
  'X4cl12noise': dict(C1, kin_clear=0.12, kin_noise=0.05, kin_seed=2),
}
C2 = dict(C1, kin_clear=0.12, kin_hdelay=0.15, kin_lift_m=1.0)
SETS['k11'] = {
  'Y0f05r5': dict(C2, kin_f_cap=0.05, kin_fz_cap=0.05),
  'Y1f1r1': dict(C2, kin_f_cap=0.1, kin_fz_cap=0.1, kin_rot_cap=0.1),
  'Y2f05r05': dict(C2, kin_f_cap=0.05, kin_fz_cap=0.05, kin_rot_cap=0.05),
  'Y3f02r02': dict(C2, kin_f_cap=0.02, kin_fz_cap=0.02, kin_rot_cap=0.02),
  'Y4zero': dict(C2, kin_f_cap=1e-6, kin_fz_cap=1e-6, kin_rot_cap=1e-6),
  'Y5f1r5': dict(C2, kin_f_cap=0.1, kin_fz_cap=0.1),
}
C3 = dict(C2, kin_f_cap=0.05, kin_fz_cap=0.05)
SETS['k12'] = {
  'Z0reach99': dict(C3, kin_reach=0.99),
  'Z1reach95': dict(C3, kin_reach=0.95),
  'Z2rot3': dict(C3, kin_rot_cap=0.3),
  'Z3rot2': dict(C3, kin_rot_cap=0.2),
  'Z4reach99rot3': dict(C3, kin_reach=0.99, kin_rot_cap=0.3),
  'Z5reach99rot2': dict(C3, kin_reach=0.99, kin_rot_cap=0.2),
}
SETS['fin'] = {'F0': dict(K)}   # the defaults of walk_mode 2 (kin_* defaults in MechContracts.cs are the final tuning)
SETS['seeds'] = {f'N{i}': dict(K, kin_noise=0.05, kin_seed=i) for i in range(4)}
SETS['sp'] = {
  'S25': dict(K),
  'S35': dict(K, step_length_max_frac=0.35, kin_fmax_frac=0.3),
  'S45': dict(K, step_length_max_frac=0.45, kin_fmax_frac=0.4),
  'S60': dict(K, step_length_max_frac=0.6, kin_fmax_frac=0.5),
}
SETS['lean'] = {
  'L0': dict(K),
  'L1lean-05': dict(K, kin_lean=-0.05),
  'L2lean10': dict(K, kin_lean=0.1),
  'L3plan2': dict(K, kin_ik_plan=2),
  'L4rot5': dict(K, kin_rot_cap=0.5),
  'L5arm0': dict(K, kin_arm=0.0),
}
SETS['att'] = {
  'A0w0': dict(K),
  'A1w2': dict(K, kin_att_w=0.2),
  'A2w4': dict(K, kin_att_w=0.4),
  'A3w6': dict(K, kin_att_w=0.6),
  'A4w4k16': dict(K, kin_att_w=0.4, kin_rot_k=16.0, kin_rot_d=6.0),
  'A5w4cap4': dict(K, kin_att_w=0.4, kin_rot_cap=0.4),
}
SETS['ts'] = {
  'T40': dict(K, kin_tscale=0.4, kin_noise=0.05, kin_seed=0),
  'T50': dict(K, kin_tscale=0.5, kin_noise=0.05, kin_seed=0),
  'T60': dict(K, kin_tscale=0.6, kin_noise=0.05, kin_seed=0),
  'T75': dict(K, kin_tscale=0.75, kin_noise=0.05, kin_seed=0),
  'T50b': dict(K, kin_tscale=0.5, kin_noise=0.05, kin_seed=1),
  'T60b': dict(K, kin_tscale=0.6, kin_noise=0.05, kin_seed=1),
}
SETS['ts2'] = {f'T{int(ts*100)}n{i}': dict(K, kin_tscale=ts, kin_noise=0.05, kin_seed=i) for ts in (0.6, 0.7) for i in range(4)}
SETS['ctl'] = {'X0ctl': dict(L7)}   # pure physics (walk_mode 0) control: must reproduce the r7 L7 seed-0 run (fall at 25.6 s) and the gait log of the first control run byte for byte
SETS['seeds'] = {f'N{i}': dict(K, kin_noise=0.05, kin_seed=i) for i in range(4)}
SETS['rb'] = {
  'R0': dict(K),
  'R1hit0': dict(K, kin_hit_m=0),
  'R2cap5': dict(K, kin_rot_cap=0.5),
  'R3ts08': dict(K, kin_tscale=0.8),
  'R4hit0cap5': dict(K, kin_hit_m=0, kin_rot_cap=0.5),
}
SETS['rc'] = {
  'Q0': dict(K, kin_hit_m=0, kin_rot_cap=0.5),
  'Q1hit6': dict(K, kin_hit_m=6.0, kin_rot_cap=0.5),
  'Q2hit10': dict(K, kin_hit_m=10.0, kin_rot_cap=0.5),
  'Q3hit6c4': dict(K, kin_hit_m=6.0, kin_rot_cap=0.4),
}
def main():
    name, out = sys.argv[1], sys.argv[2]
    sets = SETS[name]
    lines = [vn + ' ' + json.dumps(d, separators=(',', ':')) for vn, d in sets.items()]
    open(out, 'w').write('\n'.join(lines) + '\n'); print('ok', len(lines), out)
if __name__ == '__main__': main()
