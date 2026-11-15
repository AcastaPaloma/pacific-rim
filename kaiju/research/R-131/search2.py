import sys, json, time
from fast import *
from curves import *
import evalfull as EF
fam = sys.argv[1]; seed = int(sys.argv[2]); N = int(sys.argv[3]); only = sys.argv[4:] or PROFILES
rng = np.random.default_rng(seed)
def robust(Rs, fn):
    rs = [R.run(fn) for R in Rs]
    far = min(r["far_pass"] for r in rs); near = min(r["near_pass"] for r in rs); c6 = min(r["c6_ok"] for r in rs); nd = min(r["near_dark"] for r in rs)
    fmin = min((r["far_min"] or 0) for r in rs); step = min(r["c6_minstep"] for r in rs)
    return dict(far=far, far_n=rs[0]["far_n"], near=near, c6=c6, nd=nd, far_min=float(fmin), step=float(step))
def score(r):
    s = r["far"] + 0.1 * min(r["far_min"], 4) + 0.05 * min(r["step"], 3)
    s -= 5 * (r["far_n"] - r["far"] if False else 0) + 5 * (4 - r["near"]) + 5 * (12 - r["c6"]) + 3 * (12 - r["nd"])
    if r["step"] < 1.0: s -= 2 * (1.0 - r["step"])
    return s
def draw(P):
    if fam == "log":
        return dict(start=rng.uniform(40, 400), full=rng.uniform(5000, 45000), B=int(rng.choice([5, 7, 9, 12, 16, 20, 24, 32, 48, 64])), gamma=float(rng.choice([1.0, rng.uniform(0.5, 1.6)]))), log_curve
    if fam == "knee":
        p = dict(start=rng.uniform(40, 400), knee=rng.uniform(600, 4000), full=rng.uniform(8000, 45000), w=float(rng.uniform(0.15, 0.95)), g1=float(rng.uniform(0.5, 1.8)), g2=float(rng.uniform(0.5, 1.8)), B=int(rng.choice([7, 9, 12, 16, 20, 24, 32, 48, 64])))
        return (p, knee_curve) if p["start"] < p["knee"] < p["full"] else (None, None)
    if fam == "beer":
        return dict(start=rng.uniform(0, 300), L1=float(10 ** rng.uniform(2.7, 4.2)), L2=float(10 ** rng.uniform(3.8, 5.0)), m=float(rng.uniform(0, 1)), B=int(rng.choice([7, 9, 12, 16, 20, 24, 32, 48, 64])), scale=float(rng.uniform(0.8, 1.0))), beer_two
out = {}
for pid in only:
    S = Set(pid, "eye70"); Rs = [Roi(S, "mid", sf, sg) for sf in EF.FORMS for sg in EF.GROUNDS]
    cands = []; t = time.time()
    for it in range(N):
        p, mk = draw(S.P)
        if p is None: continue
        r = robust(Rs, mk(**p)); cands.append((score(r), p, r))
    cands.sort(key=lambda c: -c[0]); out[pid] = [dict(score=c[0], params=c[1], res=c[2]) for c in cands[:25]]
    b = cands[0]; print(fam, pid, "best %.2f" % b[0], b[2], {k: round(v, 1) for k, v in b[1].items()}, round(time.time() - t), "s", flush=True)
json.dump(out, open("out_search2_%s_%d.json" % (fam, seed), "w"), indent=1, default=float)
