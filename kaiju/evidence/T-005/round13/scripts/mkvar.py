"""T-005 r13: variants = L7 control (v5 rows, qp_plan=1) + extra flags, 4 seeds each (seed = 1e-4-scale change of qp_pd_scale, as r8-r10). Usage: mkvar.py <cycle> > variants.txt"""
import json,sys
L7={"imp":3,"imp2_phase_sync":1,"qp_engage_speed_frac":0.3,"qp_ladder":1,"qp_match":1,"qp_match_ss":1,"qp_match_climb_td":1,"qp_lateral":1,"qp_om_y":0.3,"qp_w_tau":1,"qp_match_thr":0.8,"qp_match_up_thr":1.2,"qp_match_hold":2,"qp_yaw_trunk":1,"qp_om_yaw":2.0,"qp_mu":0.5,"qp_fr_margin":0.95,"qp_ref_v4":1,"qp_arm_ref":1,"qp_yaw_cap":0.035,"qp_rom":1,"qp_fp_rom":1,"qp_z_hold_ss":3.0,"qp_rom_margin_deg":6.0,"qp_td_rematch":0.0,"qp_late_cx":1.0,"qp_diag":1,"qp_sw_amax":20.0,"qp_td_air_frac":0.3,"qp_td_heel":1,"qp_om_sw":2.0,"qp_swj":1000.0,"qp_swj_om":3.0,"qp_plan":1,"qp_swj_ankle":1,"qp_pd_swing":0.05,"qp_plan_reach":0.0,"qp_foot":1,"qp_foot_drive":0,"qp_foot_meas":1,"qp_ref_v5":1,"qp_r7_clock":1,"qp_plan_lmax":1.0,"qp_pd_scale":0.25}
C={
 '1': {'L7':{}, 'CSp':{'qp_r13_copservo':1,'qp_r13_sign':1,'qp_r10_diag':1}, 'CSn':{'qp_r13_copservo':1,'qp_r13_sign':-1,'qp_r10_diag':1}},
}
for name,extra in C[sys.argv[1]].items():
    for k in range(4):
        ex=dict(extra); pd=ex.pop('_pd',0.25); d=dict(L7); d.update(ex); d['qp_pd_scale']=round(pd+1e-4*k,4)
        print(f"{name}s{k} "+json.dumps(d,separators=(',',':')))
