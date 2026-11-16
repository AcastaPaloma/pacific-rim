"""T-005 r17: shared rollout helpers for GaitLearn (project path derived from this file's location; works in any worktree).
Rollout = one scenario run of ScenarioCli (walk_fr010, 63 s) for one (flag-set, seed) pair. Seed k = qp_pd_scale 0.25 + 1e-4 k (a tiny perturbation).
Usage from scripts: from common import *"""
import json, os, re, subprocess, sys, time, shutil
import numpy as np
P = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
PY = "/opt/miniconda3/envs/kaiju/bin/python"
WORK = os.environ.get("GAITLEARN_WORK", "/private/tmp/claude-501/t005r17")
L7 = {"imp":3,"imp2_phase_sync":1,"qp_engage_speed_frac":0.3,"qp_ladder":1,"qp_match":1,"qp_match_ss":1,"qp_match_climb_td":1,"qp_lateral":1,"qp_om_y":0.3,"qp_w_tau":1,"qp_match_thr":0.8,"qp_match_up_thr":1.2,"qp_match_hold":2,"qp_yaw_trunk":1,"qp_om_yaw":2.0,"qp_mu":0.5,"qp_fr_margin":0.95,"qp_ref_v4":1,"qp_arm_ref":1,"qp_yaw_cap":0.035,"qp_rom":1,"qp_fp_rom":1,"qp_z_hold_ss":3.0,"qp_rom_margin_deg":6.0,"qp_td_rematch":0.0,"qp_late_cx":1.0,"qp_diag":1,"qp_sw_amax":20.0,"qp_td_air_frac":0.3,"qp_td_heel":1,"qp_om_sw":2.0,"qp_swj":1000.0,"qp_swj_om":3.0,"qp_plan":1,"qp_swj_ankle":1,"qp_pd_swing":0.05,"qp_plan_reach":0.0,"qp_foot":1,"qp_foot_drive":0,"qp_foot_meas":1,"qp_ref_v5":1,"qp_r7_clock":1,"qp_plan_lmax":1.0,"qp_pd_scale":0.25}
CONTROL = {**L7, "qp_r10_diag": 1}                       # the known control (25.6 31.2 38.7 30.4 on seeds 0-3)
N1 = {"body_r14_sole_m":10.0,"body_r14_width_m":15.0,"body_r15_stance_m":8.0,"qp_ref_v5":10,"qp_r10_diag":1}
Y8 = {**L7, **N1, "qp_om_y": 0.8}                        # r15/r16 plant (N1 body + faster lateral QP)

def nbatch():
    o = subprocess.run("ps ax -o command | grep -i '[U]nity.app/Contents/MacOS/Unity .*-batchmode' | grep -vc AssetImportWorker", shell=True, capture_output=True, text=True).stdout
    return int(o.strip() or 0)

def vline(name, cfg, k):
    d = dict(cfg); d["qp_pd_scale"] = round(0.25 + 1e-4 * k, 4)
    return f"{name} " + json.dumps(d, separators=(",", ":"))

def batch(lines, tag, keep_gait=False, timeout=3600, isolated=True):
    """lines: 'name json' variant lines (run in this order in ONE Unity process). Returns ({name: fall_time_or_63}, out_dir)."""
    o = f"{WORK}/{tag}"; shutil.rmtree(o, ignore_errors=True); os.makedirs(o)
    open(f"{o}/variants.txt", "w").write("\n".join(lines) + "\n")
    while nbatch() >= 2: time.sleep(60)
    subprocess.run(f"cd {P} && unity run . --no-tail --log-file {o}/scen.log --timeout {timeout} -- -executeMethod Game.Units.Mech.Editor.ScenarioCli.Run -scenarios walk_fr010 -out {o}/out -variants {o}/variants.txt -physlist none {'-isolated' if isolated else ''} > {o}/scen.out 2>&1", shell=True)
    res = {}
    try:
        for l in open(f"{o}/out/summary.txt"):
            m = re.match(r"walk_fr010@(\S+)\s+t=\s*([\d.]+)s fell=(\w+)(?: at=([\d.]+))?", l)
            if m: res[m.group(1)] = float(m.group(4)) if m.group(3) == "True" and m.group(4) else float(m.group(2))
    except FileNotFoundError: pass
    return res, o

def ds_metrics(gait):
    r = subprocess.run([PY, os.path.join(P, "Tools/GaitLearn/metrics.py"), gait], capture_output=True, text=True).stdout.split()
    return (float(r[0]), float(r[1]), int(r[2])) if len(r) == 3 else (float("nan"), float("nan"), 0)

def eval_configs(named, seeds, tag, out_json=None):
    """named: {label: cfg}. One isolated batch; returns {label: [(k, fall_s, ds_a, ds_len, n_eps)]} (DS metrics from the gait logs, see metrics.py)."""
    lines = [vline(f"{lab}s{k}", cfg, k) for lab, cfg in named.items() for k in seeds]
    res, o = batch(lines, tag, isolated=True)
    out = {}
    for lab in named:
        rows = []
        for k in seeds:
            a, l, n = ds_metrics(f"{o}/out/gait/walk_fr010@{lab}s{k}.gait.jsonl")
            rows.append((k, res.get(f"{lab}s{k}"), a, l, n))
        out[lab] = rows
    if out_json: json.dump(out, open(out_json, "w"))
    shutil.copy(f"{o}/out/summary.txt", f"{out_json}.summary.txt") if out_json and os.path.exists(f"{o}/out/summary.txt") else None
    return out

def fmt(lab, rows):
    f = [r[1] for r in rows if r[1] is not None]; a = [r[2] for r in rows if r[2] == r[2]]; l = [r[3] for r in rows if r[3] == r[3]]
    return (f"{lab:18s} surv " + " ".join(f"{(r[1] or 0):5.1f}" for r in rows) + f" | med {np.median(f):5.1f} mean {np.mean(f):5.1f} | DS a " + " ".join(f"{r[2]:+.2f}" for r in rows) +
            f" mean {np.mean(a):+.2f} | DS len mean {np.mean(l):.2f}")
