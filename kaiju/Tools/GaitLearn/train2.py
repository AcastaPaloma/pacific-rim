"""T-005 r17: separable CMA-ES over the 22 weights of the lateral (qp_r16, 10 weights) + sagittal (qp_r17, 12 weights) residual policy, isolated rollouts (deterministic per config+seed),
fitness over TRAIN seeds = mean of per-seed score: survival fraction + DS-a term + DS-length term (see score()). Resumable: <out>/state.json.
Usage: train2.py <out_dir> [--gens N] [--lam 14] [--sigma 0.4] [--seeds 8]   (python: /opt/miniconda3/envs/kaiju/bin/python)"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import *
from concurrent.futures import ProcessPoolExecutor
NAMES = [f"qp_r16_wy{i}" for i in range(5)] + [f"qp_r16_wg{i}" for i in range(5)] + [f"qp_r17_wa{i}" for i in range(6)] + [f"qp_r17_wu{i}" for i in range(6)]
PLANT = {**Y8, "qp_r16": 1, "qp_r17": 1}
TRAIN = list(range(8)); HELD = list(range(8, 16))

def cfg_of(w): return {**PLANT, **{n: round(float(x), 5) for n, x in zip(NAMES, w)}}

def score(T, a, l):
    """per-seed score in [0, 2]: survival (T/63) + DS a (-1.0 -> 0 ... >= -0.2 -> 1) * 0.5 + DS length (2.0 s -> 0 ... <= 0.9 s -> 1) * 0.5. No DS episode -> DS terms 0."""
    s = min(T, 63.0) / 63.0
    if a == a: s += 0.5 * min(1.0, max(0.0, (a + 1.0) / 0.8))
    if l == l: s += 0.5 * min(1.0, max(0.0, (2.0 - l) / 1.1))
    return s

def _m(f): return ds_metrics(f)

def evaluate(cands, seeds, tag, keep=False):
    """cands: list of weight vectors; returns per-cand list of (k, T, a, l) and the out dir"""
    lines = [vline(f"c{i}s{k}", cfg_of(w), k) for i, w in enumerate(cands) for k in seeds]
    res, o = batch(lines, tag, isolated=True)
    files = [f"{o}/out/gait/walk_fr010@c{i}s{k}.gait.jsonl" for i in range(len(cands)) for k in seeds]
    with ProcessPoolExecutor(6) as ex: ms = list(ex.map(_m, files))
    rows = []; j = 0
    for i in range(len(cands)):
        r = []
        for k in seeds:
            T = res.get(f"c{i}s{k}"); a, l, n = ms[j]; j += 1; r.append((k, T if T is not None else 0.0, a, l))
        rows.append(r)
    if not keep: shutil.rmtree(f"{o}/out/gait", ignore_errors=True)
    return rows, o

def fitness(rows): return [float(np.mean([score(T, a, l) for (_, T, a, l) in r])) for r in rows]

if __name__ == "__main__":
    out = sys.argv[1]; os.makedirs(out, exist_ok=True)
    A = sys.argv[2:]; gens = int(A[A.index("--gens") + 1]) if "--gens" in A else 25; lam = int(A[A.index("--lam") + 1]) if "--lam" in A else 14
    sig0 = float(A[A.index("--sigma") + 1]) if "--sigma" in A else 0.4
    sf = f"{out}/state.json"; n = len(NAMES); rng = np.random.default_rng(17)
    if os.path.exists(sf):
        s = json.load(open(sf)); m = np.array(s["m"]); C = np.array(s["C"]); sig = s["sigma"]; ps = np.array(s["ps"]); gen = s["gen"]; hist = s["hist"]; rng = np.random.default_rng(17 + gen)
    else:
        m = np.zeros(n); C = np.ones(n); sig = sig0; ps = np.zeros(n); gen = 0; hist = []
    mu = lam // 2; wts = np.log(mu + 0.5) - np.log(np.arange(1, mu + 1)); wts /= wts.sum(); mueff = 1 / (wts ** 2).sum()
    cs = (mueff + 2) / (n + mueff + 5); ds = 1 + cs + 2 * max(0, np.sqrt((mueff - 1) / (n + 1)) - 1); chiN = np.sqrt(n) * (1 - 1 / (4 * n) + 1 / (21 * n * n))
    cmu = min(1.0, 2 * (mueff - 2 + 1 / mueff) / ((n + 2) ** 2 + mueff))
    while gen < gens:
        z = rng.standard_normal((lam, n)); X = np.clip(m + sig * np.sqrt(C) * z, -3, 3)
        cands = [m.copy()] + [x for x in X[:lam - 1]]
        rows, o = evaluate(cands, TRAIN, f"gen{gen:03d}")
        fit = np.array(fitness(rows))
        order = np.argsort(-fit[1:]) + 1; sel = order[:mu]
        zw = (wts[:, None] * ((np.array(cands)[sel] - m) / (sig * np.sqrt(C)))).sum(0)
        m = np.array(cands)[sel].T @ wts
        ps = (1 - cs) * ps + np.sqrt(cs * (2 - cs) * mueff) * zw
        sig *= np.exp(cs / ds * (np.linalg.norm(ps) / chiN - 1))
        Yd = (np.array(cands)[sel] - m) / sig
        C = (1 - cmu) * C + cmu * (wts[:, None] * Yd ** 2).sum(0); C = np.clip(C, 1e-3, 1e2)
        mc = rows[0]; bi = int(np.argmax(fit))
        rec = {"gen": gen, "mean_cand_fit": float(fit[0]), "best_fit": float(fit.max()), "median_fit": float(np.median(fit)), "sigma": float(sig), "best_w": [float(x) for x in cands[bi]], "fits": [float(f) for f in fit],
               "mean_cand": [[float(v) for v in r] for r in mc], "best_cand": [[float(v) for v in r] for r in rows[bi]]}
        hist.append(rec); gen += 1
        json.dump({"m": m.tolist(), "C": C.tolist(), "sigma": float(sig), "ps": ps.tolist(), "gen": gen, "hist": hist}, open(sf, "w"))
        Ts = [r[1] for r in mc]; As = [r[2] for r in mc]; Ls = [r[3] for r in mc]
        print(f"gen {gen-1} mean-cand fit {fit[0]:.3f} (surv med {np.median(Ts):.1f} DSa {np.nanmean(As):+.2f} DSlen {np.nanmean(Ls):.2f}) best {fit.max():.3f} median {np.median(fit):.3f} sigma {sig:.3f}", flush=True)
