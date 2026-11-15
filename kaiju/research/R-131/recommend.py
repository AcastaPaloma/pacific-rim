"""Final table: baseline vs recommended pure-tuning log curves (existing keys hazeStartM / hazeFullM / hazeBands / hazeCurveGamma; no code change)."""
import json
from evalfull import *
from curves import *
REC = {"warm_rust_sepia": dict(start=64.3, full=21160.0, B=64, gamma=1.0), "warm_amber_haze": dict(start=79.2, full=38400.0, B=32, gamma=1.0),
       "slate_teal": None, "lavender_peach": None, "slate_crimson": dict(start=155.4, full=16800.0, B=24, gamma=0.72)}
out = {}
for pid in PROFILES:
    sets = c1_sets(pid); cal = Cal(pid); P = Profile(pid)
    row = {"baseline": full(pid, current_amount(P), sets, cal)}
    if REC[pid]:
        f = log_curve(**REC[pid]); row["recommended"] = full(pid, f, sets, cal); row["params"] = REC[pid]
        # perturbation: +-5 % on start and full, +-1 band count step is not smooth, so test start/full only (ROI model)
        ok = tot = 0
        for ds in (0.9, 0.95, 1.05, 1.1):
            for df in (0.9, 0.95, 1.05, 1.1):
                p = dict(REC[pid]); p["start"] *= ds; p["full"] *= df; g = log_curve(**p)
                r = c7_blocks(sets[0], g); l6 = c6_layers(sets[0], g); tot += 1; ok += int(r["far_1km"]["pass"] == r["far_1km"]["in_view"] and r["near_150m"]["pass"] == 4 and l6["lighter"] == 12)
        row["perturb_start_full_pm10pct_pass"] = f"{ok}/{tot}"
    out[pid] = row
    for k in ("baseline", "recommended"):
        if k in row:
            r = row[k]; print(pid, k, "C7far", r["c7"]["far_pass_min_over_albedo"], "/", r["c7"]["far_n"], "min%.2f" % r["c7"]["far_min"], "near", r["c7"]["near_pass_min"], "C6", r["c6"]["lighter"], "step%.2f" % r["c6"]["min_step"], "c1", {a: round(float(b), 2) for a, b in r["c1"].items() if a in ("drop", "rho", "span")}, r["c1"]["pass"], "c2", r["c2"]["pass"], "scale %.1f" % r["scale"]["marker_dL"])
    if "perturb_start_full_pm10pct_pass" in row: print("   perturb", row["perturb_start_full_pm10pct_pass"])
json.dump(out, open("out_recommend.json", "w"), indent=1, default=float)
