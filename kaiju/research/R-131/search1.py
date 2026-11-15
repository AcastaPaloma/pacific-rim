import sys, json, time
from fast import *
from curves import *
rng = np.random.default_rng(int(sys.argv[2]) if len(sys.argv) > 2 else 1)
fam = sys.argv[1]
def score(r, n_near=4):
    s = r["far_pass"] + 0.05 * min(r["far_min"] or 0, 4)
    s -= 5 * (r["near_n"] - r["near_pass"]) + 5 * (r["c6_n"] - r["c6_ok"]) + 3 * (r["c6_n"] - r["near_dark"])
    if r["c6_minstep"] is not None and r["c6_minstep"] < 1.0: s -= (1.0 - r["c6_minstep"]) * 2
    return s
out = {}
for pid in PROFILES:
    S = Set(pid, "eye70"); R = Roi(S); P = S.P; best = (-1e9, None, None); t = time.time(); Nb = P.bands
    for it in range(6000):
        if fam == "log":
            p = dict(start=rng.uniform(40, 400), full=rng.uniform(5000, 45000), B=int(rng.choice([5, 7, 9, 12, 16, 20, 24, 32, 48, 64])), gamma=float(rng.uniform(0.3, 1.6)))
            fn = log_curve(**p)
        elif fam == "knee":
            p = dict(start=rng.uniform(40, 400), knee=rng.uniform(600, 4000), full=rng.uniform(8000, 45000), w=float(rng.uniform(0.15, 0.95)), g1=float(rng.uniform(0.4, 2.0)), g2=float(rng.uniform(0.4, 2.0)), B=int(rng.choice([7, 9, 12, 16, 20, 24, 32, 48, 64])))
            if p["knee"] >= p["full"] or p["start"] >= p["knee"]: continue
            fn = knee_curve(**p)
        elif fam == "beer":
            p = dict(start=rng.uniform(0, 300), L1=float(10 ** rng.uniform(2.7, 4.2)), L2=float(10 ** rng.uniform(3.8, 5.0)), m=float(rng.uniform(0.0, 1.0)), B=int(rng.choice([7, 9, 12, 16, 20, 24, 32, 48, 64])), scale=float(rng.uniform(0.8, 1.0)))
            fn = beer_two(**p)
        r = R.run(fn); s = score(r)
        if s > best[0]: best = (s, p, r)
    s, p, r = best
    print(fam, pid, "score %.2f" % s, {k: (round(float(v), 2) if v is not None else None) for k, v in r.items()}, {k: round(v, 1) for k, v in p.items()}, round(time.time() - t), "s", flush=True)
    out[pid] = dict(score=s, params=p, res={k: (float(v) if v is not None else None) for k, v in r.items()})
json.dump(out, open("out_search_%s.json" % fam, "w"), indent=1)
