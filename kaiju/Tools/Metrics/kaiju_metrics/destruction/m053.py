"""M-053@v1 Rupture offset from the mapped trace (research/R-022.md §5, R-026 §1).

Input: `fault_rupture_dump@v1` JSON (exporter side: FaultRupture / QuakeBegan events plus the rupture centreline) and
the baked `faults_bay.geojson` (BayTM) with its manifest. The faults file defaults to the project's
Assets/StreamingAssets/Geo/global/v1/faults_bay.geojson; a dump may name another project-relative `faults_path`.
  {"format": "fault_rupture_dump", "schema": 1, "faults_sha256": "<optional, must equal the file's>",
   "ruptures": [ {"event_id": <int>, "fault_id": "<str, baked fault_id>", "section": "<str, baked section>",
        "vertex_start": <int>, "vertex_end": <int>,
        "polyline": [[x, z], ...],                    # the rupture centreline, BayTM metres, float64, vertex_start..vertex_end
        "slip": [ {"applied_m": <m>, "expected_m": <m>}, ... ] } ] }   # right-lateral offset vs the slip profile
Checks: every rupture's (fault_id, section) is a baked trace and its vertex range is valid (mapped_share = 1);
the polyline resampled every 10 m has p95 / max distance to the dissolved trace of that fault and section within the
thresholds; |applied - expected| / expected <= 5 %; faults file hash equals its manifest hash. Mapped-scale uncertainty
(0.5 mm at map scale) is reported, never judged. Missing or empty input is INVALID_INPUT."""
from __future__ import annotations

import json
import math
import os

from ..common import PROJECT_ROOT, ErrorList, InvalidInput, load_json_file, sha256_file
from ..report import part

DEFAULT_FAULTS = os.path.join(PROJECT_ROOT, "Assets", "StreamingAssets", "Geo", "global", "v1", "faults_bay.geojson")
STEP_M = 10.0


def _scale_m(s):
    """Map scale string '1:24,000' -> 0.5 mm at map scale in metres (R-022 §5 cartographic rule), else None."""
    try:
        return 0.0005 * float(str(s).split(":")[1].replace(",", ""))
    except Exception:  # noqa: BLE001
        return None


def load_dump(path):
    el = ErrorList(path)
    if not os.path.isfile(path) or os.path.getsize(path) == 0:
        el.add("file", "input file missing or empty", "fault_rupture_dump.json", path)
        raise InvalidInput(el.errors)
    try:
        d = load_json_file(path)
    except Exception as e:  # noqa: BLE001
        el.add("file", f"not valid JSON ({e})", "fault_rupture_dump.json")
        raise InvalidInput(el.errors)
    if not isinstance(d, dict) or d.get("format") != "fault_rupture_dump" or d.get("schema") != 1:
        el.add("envelope", "not fault_rupture_dump@v1", 'format "fault_rupture_dump", schema 1')
        raise InvalidInput(el.errors)
    rs = d.get("ruptures")
    if not isinstance(rs, list) or not rs:
        el.add("ruptures", "non-empty list required", ">= 1 rupture")
        raise InvalidInput(el.errors)
    for i, r in enumerate(rs):
        w = f"ruptures[{i}]"
        if not isinstance(r, dict):
            el.add(w, "must be an object")
            continue
        for k in ("fault_id", "section"):
            if not isinstance(r.get(k), str):
                el.add(f"{w}.{k}", "missing", "string")
        for k in ("vertex_start", "vertex_end"):
            if not isinstance(r.get(k), int) or isinstance(r.get(k), bool):
                el.add(f"{w}.{k}", "missing or not an integer", "int")
        pl = r.get("polyline")
        if not (isinstance(pl, list) and len(pl) >= 2 and all(
                isinstance(p, list) and len(p) == 2 and all(isinstance(c, (int, float)) and not isinstance(c, bool)
                                                              and math.isfinite(c) for c in p) for p in pl)):
            el.add(f"{w}.polyline", "need >= 2 finite [x, z] points", "polyline")
        sl = r.get("slip")
        if not (isinstance(sl, list) and sl and all(
                isinstance(s, dict) and all(isinstance(s.get(k), (int, float)) and not isinstance(s.get(k), bool)
                                            and math.isfinite(s[k]) for k in ("applied_m", "expected_m"))
                and s["expected_m"] > 0 for s in sl)):
            el.add(f"{w}.slip", "need >= 1 {applied_m, expected_m > 0} sample", "list")
    if el:
        raise InvalidInput(el.errors)
    return d


def run(rep, inputs, reg, ratings=None, out_dir=None, images=False, options=None):
    from shapely.geometry import LineString, Point, shape
    from shapely.ops import unary_union
    if not inputs:
        el = ErrorList("")
        el.add("arguments", "no input given", ">= 1 fault_rupture_dump.json")
        raise InvalidInput(el.errors)
    dumps = []
    for p in inputs:
        dumps.append(load_dump(p))
        rep.add_input(p, sha256_file(p), "fault_rupture_dump@v1")
    fp = DEFAULT_FAULTS
    for d in dumps:
        if d.get("faults_path"):
            fp = os.path.join(PROJECT_ROOT, d["faults_path"])
    el = ErrorList(fp)
    mp = os.path.splitext(fp)[0] + ".manifest.json"
    if not os.path.isfile(fp) or not os.path.isfile(mp):
        el.add("faults", "faults_bay.geojson or its manifest missing", "both files", fp)
        raise InvalidInput(el.errors)
    rep.add_input(fp, sha256_file(fp), "faults_bay.geojson")
    rep.add_input(mp, sha256_file(mp), "faults manifest")
    rep.coverage["required"] = ["fault_rupture_dump", "faults_bay.geojson"]
    rep.coverage["provided"] = ["fault_rupture_dump", "faults_bay.geojson"]
    with open(fp, encoding="utf-8") as f:
        gj = json.load(f)
    with open(mp, encoding="utf-8") as f:
        man = json.load(f)
    actual = sha256_file(fp)
    hash_ok = actual == man.get("sha256") and all(d.get("faults_sha256", actual) == actual for d in dumps)
    traces, scales = {}, {}
    for ft in gj["features"]:
        pr = ft["properties"]
        key = (str(pr.get("fault_id")), pr.get("section"))
        traces.setdefault(key, []).append(shape(ft["geometry"]))
        sc = _scale_m(pr.get("mapped_scale"))
        if sc is not None:
            scales[key] = max(scales.get(key, 0.0), sc)
    dissolved = {k: unary_union(v) for k, v in traces.items()}
    total = mapped = 0
    dists, slip_rel, unmapped = [], 0.0, []
    for di, d in enumerate(dumps):
        for r in d["ruptures"]:
            total += 1
            key = (r["fault_id"], r["section"])
            n = len(r["polyline"])
            ok = key in dissolved and 0 <= r["vertex_start"] <= r["vertex_end"] and (r["vertex_end"] - r["vertex_start"] + 1) == n
            if not ok:
                unmapped.append(f"event {r.get('event_id')} {key}")
                continue
            mapped += 1
            line = LineString(r["polyline"])
            n_s = max(2, int(math.ceil(line.length / STEP_M)) + 1)
            for i in range(n_s):
                dists.append(dissolved[key].distance(line.interpolate(min(line.length, i * STEP_M))))
            for s in r["slip"]:
                slip_rel = max(slip_rel, abs(s["applied_m"] - s["expected_m"]) / s["expected_m"])
    dists.sort()
    p95 = dists[min(len(dists) - 1, int(math.ceil(0.95 * len(dists))) - 1)] if dists else None
    rep.add("M-053.offset", diagnostics={"samples": len(dists), "step_m": STEP_M},
            parts=[part("p95_m", p95, "<=", reg.t("M-053.offset", "p95_m"), unit="m"),
                   part("max_m", dists[-1] if dists else None, "<=", reg.t("M-053.offset", "max_m"), unit="m")],
            note=None if dists else "no mapped rupture: offset not measured")
    rep.add("M-053.mapped", diagnostics={"where": "; ".join(unmapped[:5]) or None, "ruptures": total},
            parts=[part("mapped_share", mapped / total, ">=", reg.t("M-053.mapped", "mapped_share"), unit="share")])
    rep.add("M-053.slip", parts=[part("slip_rel", slip_rel, "<=", reg.t("M-053.slip", "slip_rel"), unit="relative")])
    rep.add("M-053.hash", diagnostics={"actual": actual, "manifest": man.get("sha256")},
            parts=[part("hash_mismatches", 0 if hash_ok else 1, "<=", reg.t("M-053.hash", "hash_mismatches"),
                        unit="count")])
    rep.extra["m053"] = {"ruptures": total, "mapped": mapped, "samples": len(dists),
                         "mapped_scale_uncertainty_m": {f"{k[0]}/{k[1]}": v for k, v in sorted(scales.items())
                                                        if k in {(r['fault_id'], r['section']) for d in dumps
                                                                 for r in d['ruptures']}}}
