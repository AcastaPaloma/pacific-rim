#!/usr/bin/env python
"""T-196c: per clip d-iv@v2 / d-iv@v1 / A2 seam adherence / seam share over a capture root (CaptureCli output).

  divv2_clips.py <capture_root> --out <json> [--stride N] [--clips a,b]
Per clip (<root>/<path_id>/x4): per frame divv2_adapter.evaluate_frame (v2 per slab + whole frame, seam adherence = A2-style
palette check of the excluded seam pixels), plus v1 whole-frame d-iv at the sidecar output_block_offset_px.
Clip value = min over frames (the gated numbers); nothing here changes a gate."""
import argparse, glob, hashlib, json, os, sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from capture_tools import divv2, divv2_adapter as A


def v1_value(out, k, off):
    r = divv2.d_iv_v2(out, k, offset=tuple(off))
    return r["frame"]


def _frame_row(args):
    seq_dir, n, g = args
    sc, d, c = A.load_frame(seq_dir, n)
    return _row(sc, d, c, g)


def clip(seq_dir, stride=1, jobs=8, guard=0):
    names = sorted(n for n in os.listdir(seq_dir) if n.startswith("f") and n.endswith(".json"))[::stride]
    from multiprocessing import Pool
    with Pool(jobs) as p:
        rows = p.map(_frame_row, [(seq_dir, n, guard) for n in names], chunksize=2)
    return _summ(rows)


def _row(sc, d, c, guard=0):
    if True:
        r = A.evaluate_frame(sc, d, c, guard)
        k = sc["resolution"]["integer_scale"]
        v1 = v1_value(c, k, sc["resolution"]["output_block_offset_px"])
        L = sc.get("layers") or {}
        row = {"frame": sc["frame"]["index"], "layered": r["version"] == "v2", "v2_frame": r["frame"], "v2_ok": r["ok"], "v1": v1,
               "eval_area": r["eval_area"], "seam_share": 1 - r["eval_area"], "seam_adherence": r.get("seam_adherence", 1.0),
               "slabs": {int(s): {"score": v["score"], "declared": list(v["declared"]), "blocks": v["blocks"],
                                  "blind_ok": v["blind_estimate_ok"], "ambiguous": v["ambiguous"],
                                  "margin": v["margin_vs_second"]} for s, v in r["slabs"].items()},
               "shared_phase": bool(L.get("shared_phase")), "shifts": L.get("shift_output_px"), "failures": r["failures"]}
        return row


def _summ(rows):
    fails = [(r["frame"], r["failures"][0]) for r in rows if r["failures"]]
    lay = [r for r in rows if r["layered"]]
    vals = [r["v2_frame"] for r in rows] + [s["score"] for r in rows for s in r["slabs"].values()]
    slab_min = {}
    for r in rows:
        for s, v in r["slabs"].items(): slab_min[s] = min(slab_min.get(s, 1.0), v["score"])
    worst = min(rows, key=lambda r: min([r["v2_frame"]] + [s["score"] for s in r["slabs"].values()]))
    return {"frames": len(rows), "layered_frames": len(lay), "v2_clip_value": min(vals), "v2_ok_frames": sum(r["v2_ok"] for r in rows),
            "v2_slab_min": slab_min, "v1_min": min(r["v1"] for r in rows), "v1_mean": float(np.mean([r["v1"] for r in rows])),
            "seam_share_mean": float(np.mean([r["seam_share"] for r in lay])) if lay else 0.0,
            "seam_share_max": max([r["seam_share"] for r in lay], default=0.0),
            "seam_adherence_min": min(r["seam_adherence"] for r in rows),
            "blind_fail_frames": sum(1 for r in rows for s in r["slabs"].values() if not s["blind_ok"]),
            "ambiguous_slab_frames": sum(1 for r in rows for s in r["slabs"].values() if s["ambiguous"]),
            "worst_frame": worst["frame"], "worst_failures": worst["failures"][:3],
            "first_failures": fails[:5], "n_failing_frames": len(fails), "per_frame": rows}


def main():
    ap = argparse.ArgumentParser(); ap.add_argument("root"); ap.add_argument("--out", required=True)
    ap.add_argument("--stride", type=int, default=1); ap.add_argument("--clips", default=""); ap.add_argument("--guard", type=int, default=0)
    a = ap.parse_args()
    clips = [c for c in sorted(os.listdir(a.root)) if os.path.isdir(os.path.join(a.root, c, "x4"))]
    if a.clips: clips = [c for c in clips if c in a.clips.split(",")]
    res = {}
    for c in clips:
        res[c] = clip(os.path.join(a.root, c, "x4"), a.stride, guard=a.guard)
        r = res[c]
        print(f"{c}: frames {r['frames']} layered {r['layered_frames']} v2 {r['v2_clip_value']:.4f} slabs {r['v2_slab_min']} v1 min {r['v1_min']:.4f} "
              f"seam mean/max {r['seam_share_mean']:.4f}/{r['seam_share_max']:.4f} A2seam {r['seam_adherence_min']:.4f} fail frames {r['n_failing_frames']}", flush=True)
    json.dump(res, open(a.out, "w"))


if __name__ == "__main__":
    main()
