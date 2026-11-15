"""Test helpers for gait fixtures: write synthetic captures, run a metric, pick sub-measures."""
import json
import math
import os

from fixtures import gen_gait as G
from kaiju_metrics.runner import run_metric

g = G.G
L = G.REF["leg_length_m"]


def write_specs(d, specs):
    os.makedirs(d, exist_ok=True)
    paths = []
    for sp in specs:
        recs, _ = G.build(sp)
        paths.append(G.write(recs, os.path.join(str(d), f"{sp.capture}.gait.jsonl")))
    return paths


def run_specs(tmp_path, specs, metric="m001", registry=None, ratings=None, label=None, name="set"):
    d = os.path.join(str(tmp_path), name)
    write_specs(d, specs)
    out = os.path.join(str(tmp_path), f"out_{metric}_{name}")
    rep, _ = run_metric(metric, [d], registry=registry, out_dir=out, label=label, ratings=ratings, images=False)
    return rep


def run_full(tmp_path, over=None, metric="m001", **kw):
    return run_specs(tmp_path, list(G.reference_specs(over).values()), metric=metric, **kw)


def sm(rep, sid):
    return next(s for s in rep["submeasures"] if s["id"] == sid)


def failing(rep, kinds=("gating",)):
    return sorted(s["id"] for s in rep["submeasures"] if s["status"] == "FAIL" and s["kind"] in kinds)


def write_ratings(path, metric, build, rows, raters):
    with open(path, "w") as f:
        json.dump({"schema": "panel_ratings@v1", "metric": metric, "raters": raters, "ratings": rows}, f)
    return path


def m001_ratings(path, build, overall=(8, 8, 9), scen=8):
    raters = [{"rater_id": f"r{i}", "rater_type": "agent", "blind": True} for i in range(len(overall))]
    rows = []
    for i, o in enumerate(overall):
        rows.append({"rater_id": f"r{i}", "build": build, "capture": "panel", "scenario": "overall", "score": o})
        for sc in ("cruise", "start", "stop", "turn90_inplace", "curve"):
            rows.append({"rater_id": f"r{i}", "build": build, "capture": "panel", "scenario": sc,
                         "score": scen if not isinstance(scen, dict) else scen.get(sc, 8)})
    return write_ratings(path, "M-001", build, rows, raters)


def alexander(fr):
    return 2.3 * fr ** -0.2 * math.sqrt(L / g)
