"""T-005 r16: separable CMA-ES over the 10 weights of the qp_r16 lateral residual policy (linear, 5 features x 2 outputs), evaluated by batch Unity rollouts
(ScenarioCli, walk_fr010, 63 s, N1 body + qp_om_y 0.8). Resumable: state in <out>/state.json. Fitness = mean over the training seeds of min(fall time, 63) s.
Usage: train.py <out_dir> [--gens N] [--lam 12] [--sigma 0.5] [--base-extra JSON]   (python: /opt/miniconda3/envs/kaiju/bin/python)"""
import json, os, re, subprocess, sys, time, shutil
import numpy as np
P = "/Users/kuanw/Me/code/pacific-rim/.claude/worktrees/agent-abf9a33a7aa192d55/kaiju"
sys.path.insert(0, P + "/evidence/T-005/round15/scripts")
L7 = {"imp":3,"imp2_phase_sync":1,"qp_engage_speed_frac":0.3,"qp_ladder":1,"qp_match":1,"qp_match_ss":1,"qp_match_climb_td":1,"qp_lateral":1,"qp_om_y":0.3,"qp_w_tau":1,"qp_match_thr":0.8,"qp_match_up_thr":1.2,"qp_match_hold":2,"qp_yaw_trunk":1,"qp_om_yaw":2.0,"qp_mu":0.5,"qp_fr_margin":0.95,"qp_ref_v4":1,"qp_arm_ref":1,"qp_yaw_cap":0.035,"qp_rom":1,"qp_fp_rom":1,"qp_z_hold_ss":3.0,"qp_rom_margin_deg":6.0,"qp_td_rematch":0.0,"qp_late_cx":1.0,"qp_diag":1,"qp_sw_amax":20.0,"qp_td_air_frac":0.3,"qp_td_heel":1,"qp_om_sw":2.0,"qp_swj":1000.0,"qp_swj_om":3.0,"qp_plan":1,"qp_swj_ankle":1,"qp_pd_swing":0.05,"qp_plan_reach":0.0,"qp_foot":1,"qp_foot_drive":0,"qp_foot_meas":1,"qp_ref_v5":1,"qp_r7_clock":1,"qp_plan_lmax":1.0,"qp_pd_scale":0.25}
N1 = {"body_r14_sole_m":10.0,"body_r14_width_m":15.0,"body_r15_stance_m":8.0,"qp_ref_v5":10,"qp_r10_diag":1}
BASE = {**L7, **N1, "qp_om_y": 0.8, "qp_r16": 1}
NAMES = [f"qp_r16_wy{i}" for i in range(5)] + [f"qp_r16_wg{i}" for i in range(5)]
TRAIN_K = [0, 1, 2, 3]

def nbatch():
    o = subprocess.run("ps ax -o command | grep '[U]nity.app/Contents/MacOS/Unity ' | grep -- -batchmode | grep -vc AssetImportWorker", shell=True, capture_output=True, text=True).stdout
    return int(o.strip() or 0)

def variant(name, w, k, extra=None):
    d = dict(BASE); d.update(extra or {}); d.update({n: round(float(x), 5) for n, x in zip(NAMES, w)}); d["qp_pd_scale"] = round(0.25 + 1e-4 * k, 4)
    return f"{name}s{k} " + json.dumps(d, separators=(",", ":"))

def rollout(lines, tag, workdir):
    """lines: variant lines; returns {variant_name: fall_time (63 if survived)}"""
    o = f"{workdir}/{tag}"; shutil.rmtree(o, ignore_errors=True); os.makedirs(o)
    open(f"{o}/variants.txt", "w").write("\n".join(lines) + "\n")
    while nbatch() >= 2: time.sleep(30)
    subprocess.run(f"cd {P} && unity run . --no-tail --log-file {o}/scen.log --timeout 3600 -- -executeMethod Game.Units.Mech.Editor.ScenarioCli.Run -scenarios walk_fr010 -out {o}/out -variants {o}/variants.txt -physlist none > {o}/scen.out 2>&1", shell=True)
    res = {}
    try:
        for l in open(f"{o}/out/summary.txt"):
            m = re.match(r"walk_fr010@(\S+)\s+t=\s*([\d.]+)s fell=(\w+)(?: at=([\d.]+))?", l)
            if m: res[m.group(1)] = float(m.group(4)) if m.group(3) == "True" and m.group(4) else float(m.group(2))
    except FileNotFoundError: pass
    return res, o

def evaluate(cands, ks, tag, workdir, extra=None):
    lines = [variant(f"c{i}", w, k, extra) for i, w in enumerate(cands) for k in ks]
    res, o = rollout(lines, tag, workdir)
    fit = []
    for i in range(len(cands)):
        t = [res.get(f"c{i}s{k}") for k in ks]
        fit.append(float(np.mean([min(x, 63.0) for x in t])) if all(x is not None for x in t) else float("nan"))
    return fit, res, o

if __name__ == "__main__":
    out = sys.argv[1]; os.makedirs(out, exist_ok=True)
    a = sys.argv[2:]; gens = int(a[a.index("--gens") + 1]) if "--gens" in a else 20; lam = int(a[a.index("--lam") + 1]) if "--lam" in a else 12
    sig0 = float(a[a.index("--sigma") + 1]) if "--sigma" in a else 0.5
    work = "/private/tmp/claude-501/t005r16"; os.makedirs(work, exist_ok=True)
    sf = f"{out}/state.json"; n = len(NAMES); rng = np.random.default_rng(16)
    if os.path.exists(sf):
        s = json.load(open(sf)); m = np.array(s["m"]); C = np.array(s["C"]); sig = s["sigma"]; ps = np.array(s["ps"]); gen = s["gen"]; hist = s["hist"]; rng = np.random.default_rng(16 + gen)
    else:
        m = np.zeros(n); C = np.ones(n); sig = sig0; ps = np.zeros(n); gen = 0; hist = []
    mu = lam // 2; wts = np.log(mu + 0.5) - np.log(np.arange(1, mu + 1)); wts /= wts.sum(); mueff = 1 / (wts ** 2).sum()
    cs = (mueff + 2) / (n + mueff + 5); ds = 1 + cs + 2 * max(0, np.sqrt((mueff - 1) / (n + 1)) - 1); chiN = np.sqrt(n) * (1 - 1 / (4 * n) + 1 / (21 * n * n))
    cmu = min(1.0, 2 * (mueff - 2 + 1 / mueff) / ((n + 2) ** 2 + mueff))
    while gen < gens:
        z = rng.standard_normal((lam, n)); X = np.clip(m + sig * np.sqrt(C) * z, -3, 3)
        cands = [m.copy()] + [x for x in X[:lam - 1]]; zz = np.vstack([np.zeros(n), z[:lam - 1]])   # candidate 0 = the mean (tracks progress), rest samples
        fit, res, o = evaluate(cands, TRAIN_K, f"gen{gen:03d}", work)
        fit = np.array([f if f == f else 0.0 for f in fit])
        order = np.argsort(-fit[1:]) + 1; sel = order[:mu]
        yw = (wts[:, None] * ((np.array(cands)[sel] - m) / sig)).sum(0)
        zw = (wts[:, None] * ((np.array(cands)[sel] - m) / (sig * np.sqrt(C)))).sum(0)
        m = np.array(cands)[sel].T @ wts
        ps = (1 - cs) * ps + np.sqrt(cs * (2 - cs) * mueff) * zw
        sig *= np.exp(cs / ds * (np.linalg.norm(ps) / chiN - 1))
        Y = (np.array(cands)[sel] - (m - sig * 0)) ; Yd = (np.array(cands)[sel] - m) / sig
        C = (1 - cmu) * C + cmu * (wts[:, None] * Yd ** 2).sum(0); C = np.clip(C, 1e-3, 1e2)
        rec = {"gen": gen, "mean_cand_fit": float(fit[0]), "best_fit": float(fit.max()), "median_fit": float(np.median(fit)), "sigma": float(sig), "best_w": [float(x) for x in cands[int(np.argmax(fit))]], "fits": [float(f) for f in fit]}
        hist.append(rec); gen += 1
        json.dump({"m": m.tolist(), "C": C.tolist(), "sigma": float(sig), "ps": ps.tolist(), "gen": gen, "hist": hist}, open(sf, "w"))
        shutil.copy(f"{o}/out/summary.txt", f"{out}/gen{gen-1:03d}_summary.txt") if os.path.exists(f"{o}/out/summary.txt") else None
        shutil.rmtree(f"{o}/out/gait", ignore_errors=True)
        print(f"gen {gen-1} mean-cand {fit[0]:.1f} best {fit.max():.1f} median {np.median(fit):.1f} sigma {sig:.3f}", flush=True)
