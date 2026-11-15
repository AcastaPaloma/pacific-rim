"""M-054@v1 Length-displacement-magnitude consistency (research/R-022.md §2, §5; Wells & Coppersmith 1994 Table 2A, SS).

Input: `rupture_dump@v1` JSON
  {"format": "rupture_dump", "schema": 1,
   "ruptures": [ {"name": <str>, "mw": <Mw>, "srl_km": <arc length of ruptured vertices>, "md_m": <max slip>,
                  "ad_m": <mean slip>, "trace_length_km": <dissolved trace length, > 0>} ] }
dM_L = Mw - (5.16 + 1.12 log10 SRL); dAD = log10 AD - (-6.32 + 0.90 Mw); dMD = log10 MD - (-7.03 + 1.03 Mw).
Pass: |dM_L| <= 0.28, |dAD| <= 0.28, |dMD| <= 0.34 (1 sigma). Warn 1-2 sigma (recorded in the report warnings, verdict
unaffected). Fail beyond 2 sigma, SRL > trace length, or outside the validity ranges (M 5.6-8.1, SRL 1.3-432 km,
MD 0.01-14.6 m, AD 0.05-8.0 m)."""
from __future__ import annotations

import math
import os

from ..common import ErrorList, InvalidInput, load_json_file, sha256_file
from ..report import part

SIGMA = {"dM_L": 0.28, "dAD": 0.28, "dMD": 0.34}
RANGES = {"mw": (5.6, 8.1), "srl_km": (1.3, 432.0), "md_m": (0.01, 14.6), "ad_m": (0.05, 8.0)}


def deviations(r):
    return {"dM_L": r["mw"] - (5.16 + 1.12 * math.log10(r["srl_km"])),
            "dAD": math.log10(r["ad_m"]) - (-6.32 + 0.90 * r["mw"]),
            "dMD": math.log10(r["md_m"]) - (-7.03 + 1.03 * r["mw"])}


def load_dump(path):
    el = ErrorList(path)
    if not os.path.isfile(path) or os.path.getsize(path) == 0:
        el.add("file", "input file missing or empty", "rupture_dump.json", path)
        raise InvalidInput(el.errors)
    try:
        d = load_json_file(path)
    except Exception as e:  # noqa: BLE001
        el.add("file", f"not valid JSON ({e})", "rupture_dump.json")
        raise InvalidInput(el.errors)
    if not isinstance(d, dict) or d.get("format") != "rupture_dump" or d.get("schema") != 1:
        el.add("envelope", "not rupture_dump@v1", 'format "rupture_dump", schema 1')
        raise InvalidInput(el.errors)
    rs = d.get("ruptures")
    if not isinstance(rs, list) or not rs:
        el.add("ruptures", "non-empty list required", ">= 1 rupture")
        raise InvalidInput(el.errors)
    for i, r in enumerate(rs):
        if not isinstance(r, dict):
            el.add(f"ruptures[{i}]", "must be an object")
            continue
        for k in ("mw", "srl_km", "md_m", "ad_m", "trace_length_km"):
            v = r.get(k)
            if isinstance(v, bool) or not isinstance(v, (int, float)) or not math.isfinite(v) or v <= 0:
                el.add(f"ruptures[{i}].{k}", "missing or not a positive finite number", "> 0", str(v))
    if el:
        raise InvalidInput(el.errors)
    return d


def run(rep, inputs, reg, ratings=None, out_dir=None, images=False, options=None):
    if not inputs:
        el = ErrorList("")
        el.add("arguments", "no input given", ">= 1 rupture_dump.json")
        raise InvalidInput(el.errors)
    rows = []
    for p in inputs:
        d = load_dump(p)
        rep.add_input(p, sha256_file(p), "rupture_dump@v1")
        rows += d["ruptures"]
    rep.coverage["required"] = ["rupture_dump"]
    rep.coverage["provided"] = ["rupture_dump"]
    worst = {k: 0.0 for k in SIGMA}
    out_range, over_trace, warns = [], [], []
    for r in rows:
        nm = r.get("name", "?")
        dv = deviations(r)
        for k in SIGMA:
            worst[k] = max(worst[k], abs(dv[k]))
            if SIGMA[k] < abs(dv[k]) <= 2 * SIGMA[k]:
                warns.append(f"{nm}: {k} = {dv[k]:+.2f} (1-2 sigma)")
        for k, (lo, hi) in RANGES.items():
            if not lo <= r[k] <= hi:
                out_range.append(f"{nm}: {k} {r[k]}")
        if r["srl_km"] > r["trace_length_km"] * (1 + 1e-9):
            over_trace.append(nm)
    for w in warns:
        rep.warn("M-054 " + w + ": record the reason (e.g. HayWired length)")
    parts = [part(f"{k}_max", worst[k], "<=", reg.t("M-054.consistency", f"{k}_max_sigma2"), unit="log10 / M")
             for k in SIGMA]
    rep.add("M-054.consistency", parts=parts,
            diagnostics={"worst": worst, "warn_1_to_2_sigma": warns, "ruptures": len(rows)})
    rep.add("M-054.range", diagnostics={"where": "; ".join(out_range[:5]) or None},
            parts=[part("out_of_range", len(out_range), "<=", reg.t("M-054.range", "out_of_range_max"), unit="count"),
                   part("srl_over_trace", len(over_trace), "<=", reg.t("M-054.range", "srl_over_trace_max"),
                        unit="count")])
    rep.extra["m054"] = {"ruptures": len(rows), "warnings": len(warns)}
