"""M-101 Streaming hitch (research/R-101.md section 9).

Input: `streaming_frames@v1` JSON, one run per (speed, mode):
  {"format": "streaming_frames", "schema": 1, "label": str, "speed_mps": 30 | 60, "mode": "streaming" | "control",
   "rendering": bool,               # true when frame_ms are rendered frame times from a graphics-device run
   "frame_ms": [float, ...] | null, # per rendered frame (after the warm-up frames), main-thread frame time
   "streaming_main_ms": [float, ...],   # per frame: main-thread time in Streaming.* markers (host tick + build slice + collider attach)
   "max_work_item_ms": float,           # longest single main-thread work item
   "late_tile_frames": int, "physics_holds": int,
   "draw_calls": [int, ...] | null,
   "cold_start": bool}
`control` runs have every tile pre-resident on the same path. Clause (a), (b), (e) need `frame_ms` / `draw_calls` (else NOT_RUN,
verdict INCOMPLETE); (c) and (d) are judged from the streaming-side numbers alone. Speeds 30 and 60 m/s, both modes, are required."""
from __future__ import annotations

import math
import os

import numpy as np

from ..common import ErrorList, InvalidInput, load_json_file, sha256_file
from ..report import part
from ..common import S_NOT_RUN


def _isnum(v):
    return isinstance(v, (int, float)) and not isinstance(v, bool) and math.isfinite(v)


def _series(d, key, el, allow_null):
    v = d.get(key)
    if v is None and allow_null:
        return None
    if not isinstance(v, list) or not v or not all(_isnum(x) and x >= 0 for x in v):
        el.add(key, "needs a non-empty list of finite non-negative numbers" + (" or null" if allow_null else ""), "list", type(v).__name__)
        return None
    return v


def load_run(path):
    el = ErrorList(path)
    if not os.path.isfile(path) or os.path.getsize(path) == 0:
        el.add("file", "input file missing or empty", "streaming_frames.json", path)
        raise InvalidInput(el.errors)
    try:
        d = load_json_file(path)
    except Exception as e:  # noqa: BLE001
        el.add("file", f"not valid JSON ({e})", "streaming_frames.json")
        raise InvalidInput(el.errors)
    if not isinstance(d, dict) or d.get("format") != "streaming_frames" or d.get("schema") != 1:
        el.add("envelope", "not streaming_frames@v1", 'format "streaming_frames", schema 1')
        raise InvalidInput(el.errors)
    if d.get("mode") not in ("streaming", "control"):
        el.add("mode", "must be streaming or control", "streaming | control", str(d.get("mode")))
    if not _isnum(d.get("speed_mps")) or d["speed_mps"] <= 0:
        el.add("speed_mps", "missing or not positive", "> 0", str(d.get("speed_mps")))
    if not isinstance(d.get("rendering"), bool):
        el.add("rendering", "missing or not a bool", "bool", str(d.get("rendering")))
    frames = _series(d, "frame_ms", el, True)
    if d.get("rendering") is True and frames is None:
        el.add("frame_ms", "rendering is true but there are no frame times", "list", "null")
    if d.get("rendering") is False and frames is not None:
        el.add("frame_ms", "rendering is false but frame times are given", "null", "list")
    _series(d, "streaming_main_ms", el, False)
    dc = _series(d, "draw_calls", el, True)
    if frames is not None and dc is not None and len(dc) != len(frames):
        el.add("draw_calls", "length differs from frame_ms", str(len(frames)), str(len(dc)))
    for k in ("max_work_item_ms",):
        if not _isnum(d.get(k)) or d[k] < 0:
            el.add(k, "missing or negative", ">= 0", str(d.get(k)))
    for k in ("late_tile_frames", "physics_holds"):
        if not isinstance(d.get(k), int) or isinstance(d.get(k), bool) or d[k] < 0:
            el.add(k, "missing or not an integer >= 0", ">= 0", str(d.get(k)))
    if not isinstance(d.get("cold_start"), bool):
        el.add("cold_start", "missing or not a bool", "bool", str(d.get("cold_start")))
    if el:
        raise InvalidInput(el.errors)
    return d


def p99(x):
    return float(np.percentile(np.asarray(x, dtype=float), 99))


def run(rep, inputs, reg, ratings=None, out_dir=None, images=False, options=None):
    if not inputs:
        el = ErrorList("")
        el.add("arguments", "no input given", ">= 1 streaming_frames.json")
        raise InvalidInput(el.errors)
    runs = []
    for p in inputs:
        runs.append(load_run(p))
        rep.add_input(p, sha256_file(p), "streaming_frames@v1")
    speeds = [float(s) for s in reg.p("speeds_mps")]
    have = {(r["speed_mps"], r["mode"], r["cold_start"]) for r in runs}
    missing = []
    for s in speeds:
        for m in ("streaming", "control"):
            if not any(r["speed_mps"] == s and r["mode"] == m and not r["cold_start"] for r in runs):
                missing.append(f"{s:g} m/s {m}")
    rep.coverage["required"] = [f"{s:g} m/s x streaming, control" for s in speeds]
    rep.coverage["provided"] = sorted(f"{r['speed_mps']:g} m/s {r['mode']}{' (cold)' if r['cold_start'] else ''}" for r in runs)
    rep.coverage["missing"] = missing
    stream = [r for r in runs if r["mode"] == "streaming" and not r["cold_start"]]

    # (a) no frame over the limit
    pa = []
    for r in stream:
        nm = f"{r['label']} {r['speed_mps']:g} m/s"
        if r["frame_ms"] is None:
            pa.append({"name": nm, "value": None, "status": S_NOT_RUN, "op": "<=", "threshold": reg.t("M-101.a", "frames_over_max"),
                       "margin": None, "margin_rel": None, "unit": "count", "note": "no rendered frame times (rendering false)"})
        else:
            over = sum(1 for x in r["frame_ms"] if x > reg.t("M-101.a", "frame_limit_ms"))
            pa.append(part(nm, over, "<=", reg.t("M-101.a", "frames_over_max"), unit="count",
                           max_frame_ms=max(r["frame_ms"])))
    rep.add("M-101.a", parts=pa or None, diagnostics={"runs": len(stream)})
    # (b) p99 against the control of the same speed
    pb = []
    for r in stream:
        nm = f"{r['label']} {r['speed_mps']:g} m/s"
        ctl = next((c for c in runs if c["mode"] == "control" and c["speed_mps"] == r["speed_mps"] and not c["cold_start"]), None)
        if r["frame_ms"] is None or ctl is None or ctl["frame_ms"] is None:
            pb.append({"name": nm, "value": None, "status": S_NOT_RUN, "op": "<=", "threshold": reg.t("M-101.b", "p99_ratio_max"),
                       "margin": None, "margin_rel": None, "unit": "ratio", "note": "needs rendered frame times of the run and its control"})
        else:
            pb.append(part(nm, p99(r["frame_ms"]) / p99(ctl["frame_ms"]), "<=", reg.t("M-101.b", "p99_ratio_max"), unit="ratio"))
    rep.add("M-101.b", parts=pb or None)
    # (c) diagnostic, main-thread streaming time
    pc = []
    for r in stream:
        nm = f"{r['label']} {r['speed_mps']:g} m/s"
        pc.append(part(nm + " p99 per frame", p99(r["streaming_main_ms"]), "<=", reg.t("M-101.c", "main_ms_p99_max"), unit="ms"))
        pc.append(part(nm + " max work item", r["max_work_item_ms"], "<=", reg.t("M-101.c", "work_item_ms_max"), unit="ms"))
    rep.add("M-101.c", parts=pc or None)
    # (d) late tiles and holds
    pd = [part("late_tile_frames", sum(r["late_tile_frames"] for r in stream), "<=", reg.t("M-101.d", "late_tile_frames_max"), unit="count"),
          part("physics_holds", sum(r["physics_holds"] for r in stream), "<=", reg.t("M-101.d", "physics_holds_max"), unit="count")] if stream else None
    rep.add("M-101.d", parts=pd)
    # (e) draw calls
    pe = []
    for r in stream:
        nm = f"{r['label']} {r['speed_mps']:g} m/s"
        if r["draw_calls"] is None:
            pe.append({"name": nm, "value": None, "status": S_NOT_RUN, "op": "<=", "threshold": reg.t("M-101.e", "draw_calls_max"),
                       "margin": None, "margin_rel": None, "unit": "calls", "note": "no draw-call counter"})
        else:
            pe.append(part(nm, max(r["draw_calls"]), "<=", reg.t("M-101.e", "draw_calls_max"), unit="calls"))
    rep.add("M-101.e", parts=pe or None)
    rep.extra["m101"] = {"runs": [{"label": r["label"], "speed_mps": r["speed_mps"], "mode": r["mode"], "rendering": r["rendering"],
                                   "frames": len(r["frame_ms"]) if r["frame_ms"] else 0, "cold_start": r["cold_start"]} for r in runs]}
