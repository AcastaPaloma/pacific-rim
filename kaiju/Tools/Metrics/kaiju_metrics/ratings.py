"""Panel ratings (panel_ratings@v1) for M-001.13 (R-001 §7) and M-003 f (R-002 §4 f). The tool never invents
or defaults a score: no file, or no rows for this build, leaves the sub-measure PENDING (verdict INCOMPLETE).

File format (JSON):
{"schema": "panel_ratings@v1", "metric": "M-001" | "M-003",
 "raters":  [{"rater_id": "founder_a", "rater_type": "human" | "agent", "blind": true}],
 "ratings": [{"rater_id": "founder_a", "build": "<report label>", "capture": "<capture id>",
              "scenario": "cruise|start|stop|turn90_inplace|curve|overall" (M-001),
              "profile": "<palette profile>" (M-003), "anchor": null | "positive" | "negative", "score": 1..10}]}
"""
from __future__ import annotations

import os
import statistics

from .common import ErrorList, InvalidInput, S_FAIL, S_PASS, S_PENDING, load_json_file, rel_path
from .report import part

SCHEMA = "panel_ratings@v1"


def load_ratings(path):
    E = ErrorList(rel_path(path))
    if not os.path.isfile(path):
        E.add("file", "ratings file not found", "panel_ratings@v1 JSON", path)
        raise InvalidInput(E.errors)
    try:
        d = load_json_file(path)
    except Exception as e:  # noqa: BLE001
        E.add("file", "not valid JSON", "JSON object", str(e)[:80])
        raise InvalidInput(E.errors)
    if not isinstance(d, dict) or d.get("schema") != SCHEMA:
        E.add("$.schema", "wrong schema string", SCHEMA, repr(d.get("schema") if isinstance(d, dict) else d)[:40])
        raise InvalidInput(E.errors)
    if d.get("metric") not in ("M-001", "M-003"):
        E.add("$.metric", "ratings must name their metric", "M-001 | M-003", repr(d.get("metric")))
    raters = {}
    for i, r in enumerate(d.get("raters") or []):
        if not isinstance(r, dict) or not isinstance(r.get("rater_id"), str):
            E.add(f"$.raters[{i}]", "needs rater_id", "string", repr(r)[:40]); continue
        if r.get("rater_type") not in ("human", "agent"):
            E.add(f"$.raters[{i}].rater_type", "unknown rater type", "human | agent", repr(r.get("rater_type")))
        if not isinstance(r.get("blind"), bool):
            E.add(f"$.raters[{i}].blind", "blind flag required", "true | false", repr(r.get("blind")))
        raters[r["rater_id"]] = r
    if not raters:
        E.add("$.raters", "no raters", ">= 1 rater", 0)
    rows = d.get("ratings")
    if not isinstance(rows, list):
        E.add("$.ratings", "missing ratings list", "list", repr(rows)[:40]); rows = []
    for i, r in enumerate(rows):
        w = f"$.ratings[{i}]"
        if not isinstance(r, dict):
            E.add(w, "rating must be an object", "object", type(r).__name__); continue
        if r.get("rater_id") not in raters:
            E.add(f"{w}.rater_id", "unknown rater", sorted(raters), repr(r.get("rater_id")))
        for k in ("build", "capture"):
            if not isinstance(r.get(k), str):
                E.add(f"{w}.{k}", "missing", "string", repr(r.get(k)))
        s = r.get("score")
        if not isinstance(s, (int, float)) or isinstance(s, bool) or not (1 <= s <= 10):
            E.add(f"{w}.score", "score must be a number in 1..10", "1..10", repr(s))
        if r.get("anchor") not in (None, "positive", "negative"):
            E.add(f"{w}.anchor", "unknown anchor", "null | positive | negative", repr(r.get("anchor")))
    if E:
        raise InvalidInput(E.errors)
    d["_path"] = path
    return d


def score_m001(report, reg, ratings):
    sid = "M-001.13"
    if ratings is None:
        report.add(sid, S_PENDING, note="no panel_ratings@v1 file given (the tool never defaults a score)")
        return
    rows = [r for r in ratings["ratings"] if r["build"] == report.label and r.get("anchor") is None]
    raters = {k: v for k, v in _raters(ratings).items() if v["blind"]}
    rows = [r for r in rows if r["rater_id"] in raters]
    overall = {}
    for r in rows:
        if r.get("scenario") == "overall":
            overall[r["rater_id"]] = r["score"]
    n_min = int(reg.t(sid, "min_raters"))
    info = {"raters_with_overall": sorted(overall), "rows_used": len(rows)}
    if len(overall) < n_min:
        report.add(sid, S_PENDING, note=f"{len(overall)} blind raters with an overall score for build "
                                        f"'{report.label}' (< {n_min})", info=info)
        return
    med = statistics.median(overall.values())
    parts = [part("median overall", med, ">=", reg.t(sid, "median_overall_min"), unit="score", n=len(overall))]
    for sc in reg.sub(sid).get("scenarios", []):
        v = [r["score"] for r in rows if r.get("scenario") == sc]
        if not v:
            parts.append({"name": f"scenario {sc} median", "value": None, "status": "NOT_RUN", "op": ">",
                          "threshold": reg.t(sid, "scenario_median_max_fail"), "margin": None, "margin_rel": None,
                          "unit": "score", "note": "no scores"})
            continue
        parts.append(part(f"scenario {sc} median", statistics.median(v), ">", reg.t(sid, "scenario_median_max_fail"),
                          unit="score", n=len(v)))
    d = report.add(sid, parts=parts, info=info)
    if d["status"] == "NOT_RUN":
        d["status"] = S_PENDING


def _raters(ratings):
    return {r["rater_id"]: r for r in ratings["raters"]}


def score_m003(report, reg, ratings, profiles_present):
    sid = "M-003.f"
    if ratings is None:
        report.add(sid, S_PENDING, note="no panel_ratings@v1 file given (the tool never defaults a score)")
        return
    rows = [r for r in ratings["ratings"] if r["build"] == report.label]
    raters = _raters(ratings)
    excl, info = {}, {}
    neg_lim = reg.t(sid, "negative_anchor_exclude")
    for rid, r in raters.items():
        mine = [x for x in rows if x["rater_id"] == rid]
        if not r["blind"]:
            excl[rid] = "not blind"
        elif any(x["score"] >= neg_lim for x in mine if x.get("anchor") == "negative"):
            excl[rid] = f"scored a negative anchor >= {neg_lim}"
        elif sum(1 for x in mine if x.get("anchor") == "positive") < reg.t(sid, "positive_anchors") or \
                sum(1 for x in mine if x.get("anchor") == "negative") < reg.t(sid, "negative_anchors"):
            excl[rid] = "anchors missing (2 positive and 2 negative per set)"
    used = [x for x in rows if x["rater_id"] not in excl and x.get("anchor") is None]
    inc = sorted({x["rater_id"] for x in used})
    caps = sorted({x["capture"] for x in used})
    info.update(excluded_raters=excl, included_raters=inc, captures=len(caps))
    if not rows:
        report.add(sid, S_PENDING, note=f"no ratings for build '{report.label}'", info=info)
        return
    if len(inc) < reg.t(sid, "min_raters") or len(caps) < reg.t(sid, "min_captures"):
        report.add(sid, S_PENDING, note=f"{len(inc)} included raters (>= {reg.t(sid, 'min_raters')} needed), "
                                        f"{len(caps)} captures (>= {reg.t(sid, 'min_captures')} needed)", info=info)
        return
    mean = statistics.fmean(x["score"] for x in used)
    parts = [part("panel mean", mean, ">=", reg.t(sid, "panel_mean_min"), unit="score", n=len(used))]
    for pr in sorted({x.get("profile") for x in used if x.get("profile")}):
        v = [x["score"] for x in used if x.get("profile") == pr]
        parts.append(part(f"profile {pr} mean", statistics.fmean(v), ">=", reg.t(sid, "profile_mean_min"),
                          unit="score", n=len(v)))
    report.add(sid, parts=parts, info=info)
