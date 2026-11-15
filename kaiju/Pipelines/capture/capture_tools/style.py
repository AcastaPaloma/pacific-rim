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
from .scale import check_resolution, matches_nn_of

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


def run(set_dir, out_dir, motion=False, run_metrics=True, gameplay=True):
    res = {"check": "M-123", "scale": scale_check_set(set_dir, gameplay), "metrics": []}
    if run_metrics:
        res["metrics"].append(run_metric("m003", set_dir, os.path.join(out_dir, "m003")))
        if motion:
            res["metrics"].append(run_metric("m004", set_dir, os.path.join(out_dir, "m004")))
    res["pass"] = res["scale"]["pass"] and all(m["exit"] == 0 for m in res["metrics"])
    return res
