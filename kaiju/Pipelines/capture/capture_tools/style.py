"""Per-frame style hook (M-123). Wraps Tools/Metrics M-003/M-004 via its CLI (exit 0 PASS, 1 FAIL, 2 INVALID, 3 INCOMPLETE)
and adds the integer-scale / no-stretch check that needs only the sidecar and the two colour PNGs."""
from __future__ import annotations

import glob
import json
import os
import subprocess
import sys

import numpy as np

from ._metrics import METRICS_PY, read_png
from .scale import check_resolution, matches_nn_of, matches_shifted_nn_of

VERDICT = {0: "PASS", 1: "FAIL", 2: "INVALID_INPUT", 3: "INCOMPLETE", 4: "TOOL_ERROR"}


def scale_check_set(set_dir, gameplay=True, min_match=1.0):
    """Every sidecar f*.json under set_dir: resolution rules and output == nearest-neighbour(internal)."""
    rows = []
    for sc_path in sorted(glob.glob(os.path.join(set_dir, "**", "f*.json"), recursive=True)):
        sc = json.load(open(sc_path))
        if sc.get("schema") != "style_capture@v1":
            continue
        d, res = os.path.dirname(sc_path), sc["resolution"]
        errs = check_resolution(res["internal_wh"], res["output_wh"], gameplay)
        m = None
        if not errs:
            ci = read_png(os.path.join(d, sc["files"]["color_internal"]))
            co = read_png(os.path.join(d, sc["files"]["color_output"]))
            shift = (sc.get("camera", {}).get("snap", {}) or {}).get("present_shift_output_px")
            if shift is not None:   # exact relation (T-147): the present stage shows texel floor((y - sy) / k), floor((x + sx) / k)
                m = matches_shifted_nn_of(co, ci, res["integer_scale"], tuple(shift))
            else:
                m = matches_nn_of(co, ci, res["integer_scale"], tuple(res.get("output_block_offset_px", (0, 0))))
            if m < min_match:
                errs.append(f"output differs from nearest-neighbour upscale of internal ({m:.4%} match): stretched or filtered")
        rows.append({"frame": os.path.relpath(sc_path, set_dir), "match": m, "errors": errs, "ok": not errs})
    return {"check": "M-123/integer-scale", "frames": len(rows), "rows": rows, "pass": bool(rows) and all(r["ok"] for r in rows)}


def run_metric(metric, set_dir, out_dir, extra=()):
    """metric in {'m003','m004'}; returns exit status, verdict and the parsed report.json."""
    os.makedirs(out_dir, exist_ok=True)
    cmd = [sys.executable, METRICS_PY, metric, set_dir, "--out", out_dir, "--no-images", "--quiet", *extra]
    p = subprocess.run(cmd, capture_output=True, text=True)
    rep = os.path.join(out_dir, "report.json")
    return {"metric": metric, "exit": p.returncode, "verdict": VERDICT.get(p.returncode, "?"),
            "report": json.load(open(rep)) if os.path.exists(rep) else None, "tail": p.stdout[-300:] + p.stderr[-300:]}


def divv2_set(set_dir, out_dir):
    """T-196e: d-iv@v2 for the layered frames of a capture set (sequence dirs with sidecars f*.json + depth), no manual div_v2.json.
    Writes <out_dir>/div_v2.json (read by seam_summary) and returns it; None when the set has no layered frame (v1 stays the rule).
    d-iv@v1 of the same frames is reported beside it (v1_min), never dropped."""
    from . import divv2, divv2_adapter as A
    rows = []
    for sj in sorted(glob.glob(os.path.join(set_dir, "*", "sequence.json"))):
        seq = os.path.dirname(sj)
        for n in sorted(x for x in os.listdir(seq) if x.startswith("f") and x.endswith(".json")):
            sc = json.load(open(os.path.join(seq, n)))
            if divv2_adapter_layered(sc) is False or "depth" not in sc.get("files", {}):
                continue
            sc, d, c = A.load_frame(seq, n)
            r = A.evaluate_frame(sc, d, c)
            if r["version"] != "v2":
                continue
            off = tuple(sc["resolution"]["output_block_offset_px"])
            rows.append({"r": r, "v1": divv2.d_iv_v2(c, sc["resolution"]["integer_scale"], offset=off)["frame"],
                         "seq": os.path.basename(seq), "frame": n})
    if not rows:
        return None
    cv = divv2.clip_value([x["r"] for x in rows])
    seam = [1 - x["r"]["eval_area"] for x in rows]
    res = {"metric": divv2.METRIC["id"], "threshold": divv2.THRESHOLD, "layered_frames": len(rows), "gate_value": cv["gate_value"],
           "ok": cv["ok"], "failures": cv["failures"][:10], "v1_min": min(x["v1"] for x in rows),
           "seam_share_max": max(seam), "seam_share_cap": 0.05,
           "seam_adherence_min": min(x["r"].get("seam_adherence", 1.0) for x in rows)}
    os.makedirs(out_dir, exist_ok=True)
    json.dump(res, open(os.path.join(out_dir, "div_v2.json"), "w"), indent=1)
    return res


def divv2_adapter_layered(sc):
    from . import divv2_adapter as A
    return A.declared_offsets(sc) is not None


def metric_ok(m):
    """Exit 0, or, for a layered set, d-iv@v2 passes and d-iv@v1 is the ONLY failing gating sub-measure (v2 is the rule for layered
    frames, v1 stays reported). Unlayered sets and every other failure keep the m003 verdict."""
    if m["exit"] == 0:
        return True
    v2, rep = m.get("d_iv_v2"), m.get("report")
    if not v2 or not rep or not v2["ok"]:
        return False
    fails = {s["id"] for s in rep["submeasures"] if s.get("kind") == "gating" and s["status"] == "FAIL"}
    return m["exit"] == 1 and fails == {"M-003.d-iv"}


def _capture_sets(root):
    """A capture set is a directory whose child directories hold sequence.json (the layout of Tools/Metrics). The T-147 driver writes
    <out>/<path_id>/x4/sequence.json, so <out> is a tree of sets: returns [root] for a set, else every child that is one."""
    if glob.glob(os.path.join(root, "*", "sequence.json")):
        return [root]
    return sorted(d for d in glob.glob(os.path.join(root, "*")) if os.path.isdir(d) and glob.glob(os.path.join(d, "*", "sequence.json")))


def run(set_dir, out_dir, motion=False, run_metrics=True, gameplay=True):
    sets = _capture_sets(set_dir)
    if len(sets) == 1 and sets[0] == set_dir or not sets:
        return _run_one(set_dir, out_dir, motion, run_metrics, gameplay)
    rows = {os.path.basename(d): _run_one(d, os.path.join(out_dir, os.path.basename(d)), motion, run_metrics, gameplay) for d in sets}
    return {"check": "M-123", "sets": rows, "pass": all(r["pass"] for r in rows.values()),
            "frames": sum(r["scale"]["frames"] for r in rows.values())}


def _run_one(set_dir, out_dir, motion=False, run_metrics=True, gameplay=True):
    res = {"check": "M-123", "scale": scale_check_set(set_dir, gameplay), "metrics": []}
    if run_metrics:
        m3 = run_metric("m003", set_dir, os.path.join(out_dir, "m003"))
        v2 = divv2_set(set_dir, os.path.join(out_dir, "m003"))
        if v2:
            m3["d_iv_v2"] = v2
        res["metrics"].append(m3)
        if motion:
            res["metrics"].append(run_metric("m004", set_dir, os.path.join(out_dir, "m004")))
    res["pass"] = res["scale"]["pass"] and all(metric_ok(m) for m in res["metrics"])
    return res
