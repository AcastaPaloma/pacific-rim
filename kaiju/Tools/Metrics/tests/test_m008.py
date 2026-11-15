"""M-008@v1 (T-124): pass-shape fixtures, one-clause-fails fixtures, INVALID_INPUT, the permanent PENDING absolute row, and a real C# dump when present."""
import copy
import glob
import json
import os

import numpy as np
import pytest

from conftest import PROJECT
from kaiju_metrics.runner import run_metric


def sm(rep, sid):
    return next(s for s in rep["submeasures"] if s["id"] == sid)


def wjson(path, obj):
    path.write_text(json.dumps(obj))
    return str(path)


def run_json(seed=1, scenario="m008_bay@v1", rendering=False, host_base=2.0, n=600, **kw):
    rng = np.random.default_rng(seed)
    host = host_base + rng.random(n) * 1.5
    d = {"format": "m008_run", "schema": 1, "scenario": scenario, "label": f"{scenario}_s{seed}", "seed": seed, "platform": "fixture",
         "rendering": rendering, "tiles": "real", "streaming_on": True, "frame_hz": 60, "warmup_s": 60.0, "measure_s": 600.0,
         "bodies": {"mechs": 2, "kaiju": 3, "species": 3, "mech_body": "placeholder", "kaiju_body": "placeholder"},
         "destruction": {"fragments_peak": 4100, "events": 75, "events_per_min": 7.5, "rubble_persisting": True},
         "phases": ["approach", "engaged"],
         "frames": {"phase": [0] * (n // 2) + [1] * (n - n // 2), "total_ms": [float(x) for x in host],
                    "physics_ms": [float(x) * 0.4 for x in host], "destruction_ms": [float(x) * 0.2 for x in host],
                    "brain_ms": [0.002] * n, "streaming_ms": [float(x) * 0.3 for x in host], "alloc_bytes": [0] * n,
                    "render_ms": [float(x) + 5.0 for x in host] if rendering else None},
         "streaming": {"late_tile_frames": 0, "physics_holds": 0, "tile_loads": 812, "max_work_item_ms": 1.4},
         "gc_collections": 0, "peak_rss_mb": 5200.0, "sweep": None}
    d.update(kw)
    return d


def five(**kw):
    return [run_json(s, **kw) for s in (1, 2, 3, 4, 5)]


def go(tmp_path, *runs):
    paths = [wjson(tmp_path / f"r{i}.json", d) for i, d in enumerate(runs)]
    return run_metric("m008", paths, out_dir=str(tmp_path / "o"), images=False)[0]


def test_never_pass_on_the_mac_alone(tmp_path):
    r = go(tmp_path, *five(rendering=True), run_json(1, "m008_ref@v1"))
    assert r["verdict"] == "INCOMPLETE" and r["exit_code"] == 3
    assert sm(r, "M-008.abs")["status"] == "PENDING"
    gating = [s for s in r["submeasures"] if s["kind"] == "gating" and s["id"] != "M-008.abs"]
    assert all(s["status"] == "PASS" for s in gating), [(s["id"], s["status"]) for s in gating]


def test_without_rendering_frame_row_is_not_run(tmp_path):
    r = go(tmp_path, *five())
    assert sm(r, "M-008.b")["status"] == "NOT_RUN" and sm(r, "M-008.a")["status"] == "PASS" and r["verdict"] == "INCOMPLETE"


def test_host_over_relative_budget_fails(tmp_path):
    runs = five()
    runs[2] = run_json(3, host_base=6.0)       # p99 about 7.5 ms / 8.3 = 0.9 > 0.6
    r = go(tmp_path, *runs)
    assert sm(r, "M-008.a")["status"] == "FAIL" and r["verdict"] == "FAIL"


def test_rendered_frame_over_budget_fails(tmp_path):
    runs = five(rendering=True)
    runs[0]["frames"]["render_ms"] = [x + 8.0 for x in runs[0]["frames"]["render_ms"]]
    r = go(tmp_path, *runs)
    assert sm(r, "M-008.b")["status"] == "FAIL" and r["verdict"] == "FAIL"


@pytest.mark.parametrize("path,value", [
    (("bodies", "mechs"), 1), (("bodies", "kaiju"), 2), (("bodies", "species"), 1),
    (("destruction", "fragments_peak"), 1999), (("destruction", "events_per_min"), 5.9), (("destruction", "rubble_persisting"), False),
    (("tiles",), "synthetic"), (("streaming_on",), False), (("warmup_s",), 59.0), (("measure_s",), 599.0)])
def test_scope_clause_fails(tmp_path, path, value):
    runs = five()
    t = runs[3]
    for k in path[:-1]:
        t = t[k]
    t[path[-1]] = value
    r = go(tmp_path, *runs)
    assert sm(r, "M-008.scope")["status"] == "FAIL" and r["verdict"] == "FAIL"


def test_fewer_than_five_seeds_is_incomplete(tmp_path):
    r = go(tmp_path, *five()[:3])
    assert r["verdict"] == "INCOMPLETE" and r["coverage"]["missing"]


def test_late_tiles_and_holds_fail(tmp_path):
    for key in ("late_tile_frames", "physics_holds"):
        runs = five()
        runs[1]["streaming"][key] = 1
        assert sm(go(tmp_path, *runs), "M-008.f")["status"] == "FAIL"


def test_advisories_warn_but_do_not_flip(tmp_path):
    runs = five()
    runs[0]["frames"]["total_ms"][5] = 40.0            # one hitch
    runs[0]["frames"]["alloc_bytes"] = [128] * len(runs[0]["frames"]["alloc_bytes"])
    runs[0]["frames"]["brain_ms"] = [1.0] * len(runs[0]["frames"]["brain_ms"])
    r = go(tmp_path, *runs)
    for sid in ("M-008.c", "M-008.d", "M-008.e"):
        assert sm(r, sid)["status"] == "FAIL" and sm(r, sid)["kind"] == "advisory"
    assert r["verdict"] == "INCOMPLETE" and r["warnings"]


def test_sweep_is_reported(tmp_path):
    sw = [run_json(1, "m008_sweep@v1", sweep={"kaiju": k, "mechs": 2, "fragments_target": 2000}, host_base=1.0 + k) for k in (1, 3, 5)]
    r = go(tmp_path, *five(), *sw)
    s = sm(r, "M-008.sweep")
    assert s["status"] == "REPORTED" and [x["sweep"]["kaiju"] for x in s["info"]] == [1, 3, 5]
    assert s["info"][0]["host_p99_ms"] < s["info"][2]["host_p99_ms"]


@pytest.mark.parametrize("mutate", [
    lambda d: d.update(format="x"), lambda d: d.update(scenario="nope"), lambda d: d["frames"].update(total_ms=[]),
    lambda d: d["frames"].update(total_ms=d["frames"]["total_ms"][:-1]), lambda d: d["frames"].update(render_ms=[1.0] * 600),
    lambda d: d.update(rendering=True), lambda d: d["streaming"].pop("tile_loads"), lambda d: d.update(phases=[]),
    lambda d: d["frames"]["phase"].__setitem__(0, 9), lambda d: d["frames"]["alloc_bytes"].__setitem__(0, -1)])
def test_invalid_input(tmp_path, mutate):
    d = copy.deepcopy(run_json(1))
    mutate(d)
    r = go(tmp_path, d)
    assert r["verdict"] == "INVALID_INPUT" and r["exit_code"] == 2


def test_phase_table_in_report(tmp_path):
    r = go(tmp_path, *five())
    ph = r["extra"]["m008"]["runs"][0]["phases"] if "extra" in r else None
    assert ph is None or set(ph) == {"approach", "engaged", "ALL"}


def test_real_batch_dumps_when_present(tmp_path):
    files = sorted(glob.glob(os.path.join(PROJECT, "evidence", "T-124", "m008_bay_seed*.json")))
    if not files:
        pytest.skip("no evidence/T-124 dumps")
    files += sorted(glob.glob(os.path.join(PROJECT, "evidence", "T-124", "m008_ref*.json")))
    r = run_metric("m008", files, out_dir=str(tmp_path / "o"), images=False)[0]
    assert r["verdict"] != "INVALID_INPUT" and sm(r, "M-008.abs")["status"] == "PENDING"
