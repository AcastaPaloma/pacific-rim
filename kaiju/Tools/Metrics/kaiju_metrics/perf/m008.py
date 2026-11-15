"""M-008@v1 Performance at Bay scale (research/R-103.md section 10; registry Tools/Metrics/registry/M-008@v1.yaml).

Input: `m008_run@v1` JSON, one per run, written by the C# M008BayRun (Assets/Game/Perf/Bay/M008BayRun.cs):
  {"format": "m008_run", "schema": 1, "scenario": "m008_bay@v1" | "m008_ref@v1" | "m008_sweep@v1", "label": str, "seed": int,
   "platform": str, "rendering": bool, "tiles": "real" | "synthetic", "streaming_on": bool,
   "frame_hz": 60, "warmup_s": float, "measure_s": float,
   "bodies": {"mechs": int, "kaiju": int, "species": int, "mech_body": str, "kaiju_body": str},
   "destruction": {"fragments_peak": int, "events": int, "events_per_min": float, "rubble_persisting": bool},
   "phases": [str, ...],                      # names; frames.phase indexes them
   "frames": {"phase": [int], "total_ms": [f], "physics_ms": [f], "destruction_ms": [f], "brain_ms": [f], "streaming_ms": [f],
              "alloc_bytes": [int], "render_ms": [f] | null},   # one entry per measured frame (warm-up excluded)
   "streaming": {"late_tile_frames": int, "physics_holds": int, "tile_loads": int, "max_work_item_ms": float},
   "gc_collections": int, "peak_rss_mb": float, "sweep": {"kaiju": int, "mechs": int, "fragments_target": int} | null}
`total_ms` is the main-thread host simulation time of the frame (brain + pack, physics, destruction, streaming, bodies); it is not a
rendered frame time. `render_ms` (full rendered frame, graphics device) exists only for runs that rendered. Every figure is relative
on the Mac (Q-019, Q-207); the absolute row is PENDING and the verdict therefore never reaches PASS from Mac data alone."""
from __future__ import annotations

import math
import os

import numpy as np

from ..common import ErrorList, InvalidInput, load_json_file, sha256_file, S_NOT_RUN, S_PENDING, S_REPORTED
from ..report import part

SCEN = ("m008_bay@v1", "m008_ref@v1", "m008_sweep@v1")
CH = ("total_ms", "physics_ms", "destruction_ms", "brain_ms", "streaming_ms", "alloc_bytes")


def _num(v):
    return isinstance(v, (int, float)) and not isinstance(v, bool) and math.isfinite(v)


def _series(d, key, n, el, allow_null=False, where="frames"):
    v = d.get(key)
    if v is None and allow_null:
        return None
    if not isinstance(v, list) or not v or not all(_num(x) and x >= 0 for x in v):
        el.add(f"{where}.{key}", "needs a non-empty list of finite non-negative numbers" + (" or null" if allow_null else ""), "list", type(v).__name__)
        return None
    if n is not None and len(v) != n:
        el.add(f"{where}.{key}", "length differs from frames.phase", str(n), str(len(v)))
    return v


def load_run(path):
    el = ErrorList(path)
    if not os.path.isfile(path) or os.path.getsize(path) == 0:
        el.add("file", "input file missing or empty", "m008_run.json", path)
        raise InvalidInput(el.errors)
    try:
        d = load_json_file(path)
    except Exception as e:  # noqa: BLE001
        el.add("file", f"not valid JSON ({e})", "m008_run.json")
        raise InvalidInput(el.errors)
    if not isinstance(d, dict) or d.get("format") != "m008_run" or d.get("schema") != 1:
        el.add("envelope", "not m008_run@v1", 'format "m008_run", schema 1')
        raise InvalidInput(el.errors)
    if d.get("scenario") not in SCEN:
        el.add("scenario", "unknown scenario", " | ".join(SCEN), str(d.get("scenario")))
    for k in ("rendering", "streaming_on"):
        if not isinstance(d.get(k), bool):
            el.add(k, "missing or not a bool", "bool", str(d.get(k)))
    if d.get("tiles") not in ("real", "synthetic"):
        el.add("tiles", "must be real or synthetic", "real | synthetic", str(d.get("tiles")))
    for k in ("frame_hz", "warmup_s", "measure_s", "seed"):
        if not _num(d.get(k)) or d[k] < 0:
            el.add(k, "missing or negative", ">= 0", str(d.get(k)))
    b = d.get("bodies") if isinstance(d.get("bodies"), dict) else {}
    for k in ("mechs", "kaiju", "species"):
        if not isinstance(b.get(k), int) or isinstance(b.get(k), bool) or b[k] < 0:
            el.add(f"bodies.{k}", "missing or not an integer >= 0", ">= 0", str(b.get(k)))
    ds = d.get("destruction") if isinstance(d.get("destruction"), dict) else {}
    for k in ("fragments_peak", "events", "events_per_min"):
        if not _num(ds.get(k)) or ds[k] < 0:
            el.add(f"destruction.{k}", "missing or negative", ">= 0", str(ds.get(k)))
    if not isinstance(ds.get("rubble_persisting"), bool):
        el.add("destruction.rubble_persisting", "missing or not a bool", "bool", str(ds.get("rubble_persisting")))
    ph = d.get("phases")
    if not isinstance(ph, list) or not ph or not all(isinstance(x, str) for x in ph):
        el.add("phases", "needs a non-empty list of names", "list", str(ph))
    fr = d.get("frames") if isinstance(d.get("frames"), dict) else None
    if fr is None:
        el.add("frames", "missing", "object")
    else:
        pv = fr.get("phase")
        if not isinstance(pv, list) or not pv or not all(isinstance(x, int) and not isinstance(x, bool) and 0 <= x < len(ph or [0]) for x in pv):
            el.add("frames.phase", "needs a non-empty list of phase indices", "list of int", type(pv).__name__)
            n = None
        else:
            n = len(pv)
        for k in CH:
            _series(fr, k, n, el)
        rm = _series(fr, "render_ms", n, el, allow_null=True)
        if d.get("rendering") is True and rm is None:
            el.add("frames.render_ms", "rendering is true but there are no rendered frame times", "list", "null")
        if d.get("rendering") is False and rm is not None:
            el.add("frames.render_ms", "rendering is false but rendered frame times are given", "null", "list")
    st = d.get("streaming") if isinstance(d.get("streaming"), dict) else {}
    for k in ("late_tile_frames", "physics_holds", "tile_loads", "max_work_item_ms"):
        if not _num(st.get(k)) or st[k] < 0:
            el.add(f"streaming.{k}", "missing or negative", ">= 0", str(st.get(k)))
    for k in ("gc_collections", "peak_rss_mb"):
        if not _num(d.get(k)) or d[k] < 0:
            el.add(k, "missing or negative", ">= 0", str(d.get(k)))
    if el:
        raise InvalidInput(el.errors)
    return d


def pct(x, q):
    return float(np.percentile(np.asarray(x, dtype=float), q))


def stats(x):
    if len(x) == 0:
        return None
    a = np.asarray(x, dtype=float)
    return {"n": int(a.size), "mean": float(a.mean()), "p50": pct(a, 50), "p95": pct(a, 95), "p99": pct(a, 99), "p99.9": pct(a, 99.9), "max": float(a.max())}


def phase_table(d):
    """Per phase and overall: percentiles of total_ms and mean of each section, from one run."""
    fr = d["frames"]
    ph = np.asarray(fr["phase"], dtype=int)
    out = {}
    names = list(d["phases"]) + ["ALL"]
    for i, name in enumerate(names):
        m = np.ones(ph.size, dtype=bool) if name == "ALL" else ph == i
        if not m.any():
            out[name] = None
            continue
        row = {"frames": int(m.sum()), "total_ms": stats(np.asarray(fr["total_ms"])[m])}
        for k in ("physics_ms", "destruction_ms", "brain_ms", "streaming_ms"):
            row[k.replace("_ms", "_mean_ms")] = float(np.asarray(fr[k], dtype=float)[m].mean())
        if fr.get("render_ms") is not None:
            row["render_ms"] = stats(np.asarray(fr["render_ms"])[m])
        out[name] = row
    return out


def run(rep, inputs, reg, ratings=None, out_dir=None, images=False, options=None):
    if not inputs:
        el = ErrorList("")
        el.add("arguments", "no input given", ">= 1 m008_run.json")
        raise InvalidInput(el.errors)
    runs = []
    for p in inputs:
        runs.append(load_run(p))
        rep.add_input(p, sha256_file(p), "m008_run@v1")
    bay = [r for r in runs if r["scenario"] == "m008_bay@v1"]
    ref = [r for r in runs if r["scenario"] == "m008_ref@v1"]
    swp = [r for r in runs if r["scenario"] == "m008_sweep@v1"]
    seeds = sorted({int(r["seed"]) for r in bay})
    need = reg.p("min_runs")
    rep.coverage["required"] = [f"{need} m008_bay@v1 runs (distinct seeds)"]
    rep.coverage["provided"] = [f"{len(bay)} m008_bay@v1 runs (seeds {seeds}), {len(ref)} m008_ref@v1, {len(swp)} m008_sweep@v1"]
    rep.coverage["missing"] = [] if len(seeds) >= need else [f"{need - len(seeds)} more m008_bay@v1 seed(s)"]
    rep.coverage["optional_not_provided"] = [] if ref else ["m008_ref@v1 (reference run for normalisation)"]

    # ---- scope: is this the M-008 scenario?
    defects = []
    for r in bay:
        b, ds, tag = r["bodies"], r["destruction"], f"seed {int(r['seed'])}"
        if b["mechs"] < reg.p("min_mechs"):
            defects.append(f"{tag}: {b['mechs']} mechs < {reg.p('min_mechs')}")
        if b["kaiju"] < reg.p("min_kaiju"):
            defects.append(f"{tag}: {b['kaiju']} kaiju < {reg.p('min_kaiju')}")
        if b["species"] < reg.p("min_species"):
            defects.append(f"{tag}: {b['species']} species < {reg.p('min_species')}")
        if ds["fragments_peak"] < reg.p("min_fragments_peak"):
            defects.append(f"{tag}: fragments peak {ds['fragments_peak']:.0f} < {reg.p('min_fragments_peak')}")
        if ds["events_per_min"] < reg.p("min_events_per_min"):
            defects.append(f"{tag}: {ds['events_per_min']:.2f} destruction events/min < {reg.p('min_events_per_min')}")
        if not ds["rubble_persisting"]:
            defects.append(f"{tag}: rubble does not persist")
        if r["tiles"] != "real" or not r["streaming_on"]:
            defects.append(f"{tag}: tiles={r['tiles']} streaming_on={r['streaming_on']} (needs real Bay tiles with streaming on)")
        if r["warmup_s"] < reg.p("min_warmup_s") or r["measure_s"] < reg.p("min_measure_s"):
            defects.append(f"{tag}: warm-up {r['warmup_s']:g} s / measured {r['measure_s']:g} s (needs {reg.p('min_warmup_s'):g} / {reg.p('min_measure_s'):g})")
        if r["frame_hz"] != reg.p("frame_hz"):
            defects.append(f"{tag}: frame_hz {r['frame_hz']:g}")
    if not bay:
        defects.append("no m008_bay@v1 run among the inputs")
    rep.add("M-008.scope", diagnostics={"defects": defects, "mech_body": sorted({r["bodies"].get("mech_body", "?") for r in bay}),
                                        "kaiju_body": sorted({r["bodies"].get("kaiju_body", "?") for r in bay})},
            parts=[part("scenario_defects", len(defects), "<=", reg.t("M-008.scope", "scenario_defects_max"), unit="count")])

    # ---- a: host ms p99 / B_host, worst run
    tables = {int(r["seed"]): phase_table(r) for r in bay}
    bh = reg.t("M-008.a", "b_host_ms")
    pa = []
    for r in sorted(bay, key=lambda r: r["seed"]):
        allrow = tables[int(r["seed"])]["ALL"]
        pa.append(part(f"seed {int(r['seed'])} host p99 / B_host", allrow["total_ms"]["p99"] / bh, "<=", reg.t("M-008.a", "ratio_max"), unit="ratio",
                       host_p99_ms=allrow["total_ms"]["p99"], host_p50_ms=allrow["total_ms"]["p50"]))
    rep.add("M-008.a", parts=pa or None,
            diagnostics={"b_host_ms": bh, "reference_host_p99_ms": [tbl["ALL"]["total_ms"]["p99"] for tbl in (phase_table(r) for r in ref)]})

    # ---- b: rendered frame p99 / 16.7
    fb = reg.t("M-008.b", "frame_budget_ms")
    pb = []
    for r in sorted(bay, key=lambda r: r["seed"]):
        nm = f"seed {int(r['seed'])} frame p99 / {fb:g} ms"
        if r["frames"].get("render_ms") is None:
            pb.append({"name": nm, "value": None, "status": S_NOT_RUN, "op": "<=", "threshold": reg.t("M-008.b", "ratio_max"), "margin": None,
                       "margin_rel": None, "unit": "ratio", "note": "no rendered frame times (batch run without a graphics device or without rendering)"})
        else:
            pb.append(part(nm, pct(r["frames"]["render_ms"], 99) / fb, "<=", reg.t("M-008.b", "ratio_max"), unit="ratio"))
    rep.add("M-008.b", parts=pb or None)

    # ---- c: hitches
    lim = reg.t("M-008.c", "frame_limit_ms")
    pc = [part(f"seed {int(r['seed'])} frames > {lim:g} ms", int(sum(1 for x in (r["frames"]["render_ms"] or r["frames"]["total_ms"]) if x > lim)), "<=",
               reg.t("M-008.c", "frames_over_33ms_max"), unit="count") for r in sorted(bay, key=lambda r: r["seed"])]
    rep.add("M-008.c", parts=pc or None, diagnostics={"series": "render_ms" if bay and bay[0]["frames"].get("render_ms") else "total_ms (host)"})

    # ---- d: brain + pack share of B_host (amortised per frame)
    pd = [part(f"seed {int(r['seed'])} brain+pack mean / B_host", float(np.mean(r["frames"]["brain_ms"])) / bh, "<=",
               reg.t("M-008.d", "share_of_b_host_max"), unit="ratio") for r in sorted(bay, key=lambda r: r["seed"])]
    rep.add("M-008.d", parts=pd or None)

    # ---- e: managed allocation
    pe = [part(f"seed {int(r['seed'])} median alloc / frame", pct(r["frames"]["alloc_bytes"], 50), "<=", reg.t("M-008.e", "alloc_bytes_median_max"), unit="bytes",
               gc_collections=r["gc_collections"]) for r in sorted(bay, key=lambda r: r["seed"])]
    rep.add("M-008.e", parts=pe or None)

    # ---- f: streaming
    pf = []
    if bay:
        pf = [part("late_tile_frames", int(sum(r["streaming"]["late_tile_frames"] for r in bay)), "<=", reg.t("M-008.f", "late_tile_frames_max"), unit="count"),
              part("physics_holds", int(sum(r["streaming"]["physics_holds"] for r in bay)), "<=", reg.t("M-008.f", "physics_holds_max"), unit="count")]
    rep.add("M-008.f", parts=pf or None, diagnostics={"tile_loads": [r["streaming"]["tile_loads"] for r in bay],
                                                       "max_work_item_ms": max((r["streaming"]["max_work_item_ms"] for r in bay), default=None)})

    # ---- net, sweep (informational), absolute row (pending)
    rep.add("M-008.net", status=S_NOT_RUN, note="co-op off/on with a second process is not part of the single-process batch run (tracked, owner Track 5 follow-up)")
    sw = []
    for r in sorted(swp, key=lambda r: (r["sweep"] or {}).get("kaiju", 0)):
        t = phase_table(r)["ALL"]
        sw.append({"label": r["label"], "sweep": r.get("sweep"), "host_p50_ms": t["total_ms"]["p50"], "host_p99_ms": t["total_ms"]["p99"],
                   "ratio_p99": t["total_ms"]["p99"] / bh})
    rep.add("M-008.sweep", status=S_REPORTED if sw else S_NOT_RUN, info=sw or None, note=None if sw else "no m008_sweep@v1 run among the inputs")
    rep.add("M-008.abs", status=S_PENDING, note="pending target PC (Q-019, Q-207): absolute 60 fps needs target-class hardware; Mac figures are relative only",
            diagnostics={"thresholds": {k: reg.t("M-008.abs", k) for k in ("fps_avg_min", "frame_p99_ms_max", "frame_p999_ms_max")}})

    rep.extra["m008"] = {
        "relative_budget_note": "relative on the Mac; B_host = %.1f ms, ratio limit %.2f (Assumed)" % (bh, reg.t("M-008.a", "ratio_max")),
        "rendering": sorted({r["rendering"] for r in runs}),
        "runs": [{"label": r["label"], "scenario": r["scenario"], "seed": int(r["seed"]), "platform": r.get("platform"),
                  "phases": phase_table(r), "fragments_peak": r["destruction"]["fragments_peak"],
                  "events_per_min": r["destruction"]["events_per_min"], "peak_rss_mb": r["peak_rss_mb"], "gc_collections": r["gc_collections"]}
                 for r in sorted(runs, key=lambda r: (r["scenario"], r["seed"]))],
        "pooled_bay": {n: _pool(bay, n) for n in (list(bay[0]["phases"]) + ["ALL"] if bay else [])},
    }


def _pool(bay, name):
    xs = []
    for r in bay:
        ph = np.asarray(r["frames"]["phase"], dtype=int)
        tot = np.asarray(r["frames"]["total_ms"], dtype=float)
        if name == "ALL":
            xs.append(tot)
        elif name in r["phases"]:
            xs.append(tot[ph == r["phases"].index(name)])
    if not xs:
        return None
    return stats(np.concatenate(xs)) if sum(len(x) for x in xs) else None
