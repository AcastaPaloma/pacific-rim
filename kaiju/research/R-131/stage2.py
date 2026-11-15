"""Stage 2: full gates (evalfull) for the top candidates of every family and profile, plus parameter-perturbation robustness and a model seam check
(ground albedo: rung jump between consecutive haze amounts must be <= 1 so seam windows hold lightness-neighbouring rungs only)."""
import glob, json, sys
from evalfull import *
from curves import *
mk = {"log": log_curve, "knee": knee_curve, "beer": beer_two}
def seam_ok(P, fn):
    d = np.exp(np.linspace(np.log(30), np.log(30000), 4000)); a = fn(d)
    s = S0_GROUND + (P.t["airlightValue"] - S0_GROUND) * a; r = P.quantise_grey(s)[2]
    ua, idx = np.unique(a, return_index=True); rr = r[idx]
    return int(np.abs(np.diff(rr)).max()), int(len(ua))
def gates(r):
    c = r["c7"]; return bool(c["far_pass_min_over_albedo"] == c["far_n"] and c["near_pass_min"] == 4 and r["c6"]["lighter"] == 12 and r["c6"]["min_step"] >= 1.0 and r["c1"]["pass"] and r["c2"]["pass"] and r["scale"]["pass"])
out = {}
for fam in ("log", "knee", "beer"):
    for fn_ in glob.glob("out_search2_%s_*.json" % fam):
        for pid, cands in json.load(open(fn_)).items():
            sets = c1_sets(pid); cal = Cal(pid); P = Profile(pid)
            for c in cands[:8]:
                f = mk[fam](**c["params"]); r = full(pid, f, sets, cal)
                jump, nlev = seam_ok(P, f)
                # robustness: +-5 % on every distance parameter, count how many of 8 perturbations keep far 9/9 and c6 12/12 (ROI model, albedo mid)
                rob = 0; tot = 0
                for dm in (0.95, 1.05):
                    for key in [k for k in c["params"] if k in ("start", "knee", "full", "L1", "L2")][:3]:
                        p = dict(c["params"]); p[key] *= dm; rr = c7_blocks(sets[0], mk[fam](**p)); l6 = c6_layers(sets[0], mk[fam](**p)); tot += 1
                        rob += int(rr["far_1km"]["pass"] == rr["far_1km"]["in_view"] and l6["lighter"] == 12)
                rec = dict(profile=pid, family=fam, params=c["params"], gates=gates(r), seam_max_rung_jump=jump, levels=nlev, perturb_pass=f"{rob}/{tot}", full=r)
                out.setdefault(pid, []).append(rec)
                print(pid, fam, "ALL_GATES" if rec["gates"] else "-", "seamjump", jump, "robust", rec["perturb_pass"], "c7far", r["c7"]["far_pass_min_over_albedo"], "c1", {k: round(float(v), 2) for k, v in r["c1"].items() if k != "pass"}, "c2", r["c2"]["pass"], "scale", r["scale"]["pass"], "c6step", round(float(r["c6"]["min_step"]), 2), flush=True)
json.dump(out, open("out_stage2.json", "w"), indent=1, default=float)
