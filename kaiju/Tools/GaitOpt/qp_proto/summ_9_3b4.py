"""Summary table of a run_9_3b4.sh set: python Tools/GaitOpt/qp_proto/summ_9_3b4.py PREFIX [suffix ...]"""
import sys, json, pathlib
D = pathlib.Path(__file__).resolve().parents[1] / "out/proto"; P = sys.argv[1]
suf = sys.argv[2:] or ["A", "Amir", "Bp", "Bm", "Cm09", "Cm11", "Cmu06", "Clim08", "Clim08aw", "Cdel2", "Dp", "Dm"]
hdr = "run      fell steps speed  comz%l pitch foot%  sat%  fr>1%  frmax solver%  it99 Aitems  steps_speed(last 6)  B(rec)"
print(hdr)
for s in suf:
    f = D / f"{P}_{s}.json"
    if not f.exists(): print(f"{s:8s} missing"); continue
    m = json.load(open(f)); ps = m["pass"]; nA = sum(ps.values())
    b = m.get("push", {}); rec = f"rec={b.get('recovered_within_steps')} PASS_B={b.get('PASS_B')}" if b else ""
    print(f"{s:8s} {str(m['fell'])[0]:4s} {m['steps_done']:5.1f} {m['mean_speed_ratio']:6.3f} {m['com_z_rms_pct_l']:6.2f} {m['pelvis_pitch_rms_deg']:5.2f} {m['foot_placement_rms_pct_step']:6.2f} {m['sat_ticks_pct']:5.2f} "
          f"{m['fr_plant_vs_mu04_ticks_gt1_pct_Fn>0.001W']:6.2f} {m['fr_plant_vs_mu04_max']:5.2f} {m['solver_ok_pct']:7.2f} {m.get('gi_iters_p99', float('nan')):5.0f} {nA}/8 {[round(x, 3) for x in m['step_speed_ratio'][-6:]]} {rec}")
