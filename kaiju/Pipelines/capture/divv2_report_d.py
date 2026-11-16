#!/usr/bin/env python
"""T-196d: tables + results.json from divv2_clips.py outputs (<S>/d_<arm>_g0.json, presented-label mapping, no guard).
  divv2_report_d.py <scratch> <capture_root> <out_dir>"""
import hashlib, json, sys
S, CAP, OUT = sys.argv[1:4]
arms = ("perslab", "shared")
R = {a: json.load(open(f"{S}/d_{a}_g0.json")) for a in arms}
clips = list(R["perslab"])
f = lambda x: f"{x:.4f}"
L = ["| clip | arm | frames | d-iv@v2 clip min | near / main / far min | d-iv@v1 min | seam share mean / max | A2 seam adherence min | blind-offset failures | failing frames |", "|---|---|---|---|---|---|---|---|---|---|"]
for c in clips:
    for a in arms:
        r = R[a][c]; sm = r["v2_slab_min"]
        L.append(f"| {c} | {a} | {r['frames']} | {f(r['v2_clip_value'])} | {' / '.join(f(sm[k]) for k in sorted(sm))} | {f(r['v1_min'])} | {f(r['seam_share_mean'])} / {f(r['seam_share_max'])} | {f(r['seam_adherence_min'])} | {r['blind_fail_frames']} | {r['n_failing_frames']} |")
tab = "\n".join(L); open(f"{OUT}/tables.md", "w").write(tab); print(tab)
hashes = {f"{a}/{c}": hashlib.sha256(open(f"{CAP}/{a}/{c}/x4/hashes.sha256", "rb").read()).hexdigest() for a in arms for c in clips}
json.dump({"schema": "t196d_results@v1", "stride": 3, "guard_px": 0, "mapping": "presented",
           "summary": {f"{a}/{c}": {k: v for k, v in r.items() if k != "per_frame"} for a in arms for c, r in R[a].items()},
           "hashes_sha256_of_capture_hashes_file": hashes,
           "per_frame": {f"{a}/{c}": r["per_frame"] for a in arms for c, r in R[a].items()}}, open(f"{OUT}/results.json", "w"))
