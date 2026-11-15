"""Crimson (5-rung ladder): the ROI-only search finds C7 9/9 but every top candidate fails M-003 c1 (drop ~3.05 > 2). Search with c1 in the loop
(c1 evaluated only for ROI-robust candidates) over the log and gamma-log families."""
import sys, json
from fast import *
from curves import *
import evalfull as EF
pid = sys.argv[1]; N = int(sys.argv[2]); rng = np.random.default_rng(5)
S = Set(pid, "eye70"); Rs = [Roi(S, "mid", sf, sg) for sf in EF.FORMS for sg in EF.GROUNDS]; sets = EF.c1_sets(pid); cal = EF.Cal(pid)
found = []; tried = 0
for it in range(N):
    p = dict(start=rng.uniform(30, 300), full=rng.uniform(6000, 45000), B=int(rng.choice([5, 6, 7, 8, 9, 10, 12, 16, 24, 32, 48, 64])), gamma=float(rng.choice([1.0, rng.uniform(0.6, 1.5)])))
    fn = log_curve(**p); rs = [R.run(fn) for R in Rs]
    if min(r["far_pass"] for r in rs) < rs[0]["far_n"] or min(r["near_pass"] for r in rs) < 4 or min(r["c6_ok"] for r in rs) < 12 or min(r["c6_minstep"] for r in rs) < 1.0: continue
    tried += 1; c1 = c1_all(sets, fn)
    if c1["pass"]:
        r = EF.full(pid, fn, sets, cal); found.append(dict(params=p, c1=c1, c2=r["c2"]["pass"], scale=r["scale"]["pass"], c7far=r["c7"]["far_pass_min_over_albedo"], c6step=float(r["c6"]["min_step"])))
        print(pid, p, c1, r["c2"]["pass"], r["scale"]["pass"], flush=True)
        if len(found) >= 6: break
print("tried", tried, "found", len(found))
json.dump(found, open("out_search3_%s.json" % pid, "w"), indent=1, default=float)
