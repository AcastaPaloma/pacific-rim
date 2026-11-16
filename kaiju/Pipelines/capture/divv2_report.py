#!/usr/bin/env python
"""T-196c: assemble evidence/T-196c/EVIDENCE.md + results json from divv2_clips.py outputs (perslab/shared x guard 0/4)."""
import hashlib, json, os, sys
S, CAP, OUT = sys.argv[1], sys.argv[2], sys.argv[3]
arms = {"perslab": "per-slab (M3LayeredFarLift, layerSharedPhase OFF)", "shared": "shared phase (M3LayeredSharedPhase, ON)"}
R = {(a, g): json.load(open(f"{S}/{a}_g{g}.json")) for a in arms for g in (0, 4)}
clips = list(R[("perslab", 0)])
summ, hashes = {}, {}
for (a, g), d in R.items():
    for c, r in d.items():
        summ[f"{a}/g{g}/{c}"] = {k: v for k, v in r.items() if k != "per_frame"}
for a in arms:
    for c in clips:
        p = f"{CAP}/{a}/{c}/x4/hashes.sha256"
        hashes[f"{a}/{c}"] = hashlib.sha256(open(p, "rb").read()).hexdigest()
json.dump({"schema": "t196c_results@v1", "stride": 3, "guard_px": [0, 4], "summary": summ, "hashes_sha256_of_capture_hashes_file": hashes,
           "per_frame": {f"{a}/g{g}/{c}": r["per_frame"] for (a, g), d in R.items() for c, r in d.items()}}, open(f"{OUT}/results.json", "w"))
L = []
def f(x): return f"{x:.4f}"
for g in (0, 4):
    L.append(f"\n### d-iv@v2, label guard {g} px ({'literal capture labels' if g == 0 else 'one block, review tolerance'})\n")
    L.append("| clip | arm | frames (stride 3) | d-iv@v2 clip min | slab near / main / far min | d-iv@v1 min | seam share mean / max | A2 seam adherence min | failing frames | worst frame |")
    L.append("|---|---|---|---|---|---|---|---|---|---|")
    for c in clips:
        for a in arms:
            r = R[(a, g)][c]; sm = r["v2_slab_min"]
            L.append(f"| {c} | {a} | {r['frames']} | {f(r['v2_clip_value'])} | {' / '.join(f(sm[k]) for k in sorted(sm))} | {f(r['v1_min'])} | {f(r['seam_share_mean'])} / {f(r['seam_share_max'])} | {f(r['seam_adherence_min'])} | {r['n_failing_frames']} | {r['worst_frame']} |")
tab = "\n".join(L)
open(f"{OUT}/tables.md", "w").write(tab)
print(tab)
