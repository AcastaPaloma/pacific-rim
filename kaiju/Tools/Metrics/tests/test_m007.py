"""M-007@v1 (brain_log@v1): synthetic logs with known flank geometry, focus fire, switching and leaks."""
import json
import math
import os

import pytest

from conftest import METRICS
from kaiju_metrics.brain.m007 import ff_chance, spread_deg
from kaiju_metrics.runner import run_metric

STEP_S = 0.02
PERIOD = 10


def ent(n):
    return f"ent:{n}"


def header(brain_id, run_id):
    return {"kind": "header", "schema": "brain_log@v1", "run_id": run_id, "seed": 1, "step_seconds": STEP_S,
            "brain_period_steps": PERIOD, "brain_id": brain_id, "config_hash": "0", "scenario": "m007",
            "size_class": {"H_m": 80, "mass_t": 10000}, "d_eng_m": 400}


def sample(step, tid, x=0.0, y=0.0, alive=1):
    return {"kind": "target_sample", "step": step, "t": step * STEP_S, "ent": ent(tid), "tkind": "mech", "alive": alive,
            "x": x, "y": y, "heading_rad": 0.0, "hp": 1.0}


def dec(step, k, x, y, state, prev, target, reason=0, vis=1, age=0.0):
    return {"kind": "decision", "step": step, "t": step * STEP_S, "ent": ent(k), "pack": 1, "role": "wing", "alive": 1,
            "x": x, "y": y, "heading_rad": 0.0, "speed": 1.0, "hp": 1.0, "state": state, "prev_state": prev,
            "reason": reason, "target": ent(target), "tgt_visible": vis, "tgt_mem_age_s": age, "i_heading_rad": 0.0,
            "i_speed_frac": 0.6, "i_target": ent(target), "i_action": 0, "score_cur": 0, "score_best": 0}


def pos(bearing_deg, r=250.0):
    b = math.radians(bearing_deg)
    return r * math.cos(b), r * math.sin(b)


def engagement_rows(bearings, targets=(101, 102), extra=None):
    """Three kaiju at the given bearings around target 101; tick 0 approach, tick 1 kaiju 1 enters Charge, tick 2 all hold."""
    rows = []
    for i, step in enumerate((0, PERIOD, 2 * PERIOD)):
        for k, b in enumerate(bearings, start=1):
            for t in targets:   # the harness writes the target samples at every step that has a decision
                rows.append(sample(step + k, t, x=0.0 if t == 101 else 3000.0))
            x, y = pos(b)
            st = 3 if (i >= 1 and k == 1) or i == 2 else 1
            prev = 3 if (i == 2 and k == 1) else 1
            rows.append(dec(step + k, k, x, y, st, prev, 101))
    if extra:
        rows += extra
    return rows


def write_log(path, brain_id, rows, run_id="r"):
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        for r in [header(brain_id, run_id)] + rows:
            f.write(json.dumps(r) + "\n")


def make_set(tmp_path, name, brain_id, rows_fn, n=100):
    d = tmp_path / name
    d.mkdir()
    for i in range(n):
        write_log(str(d / f"b{i:03d}.jsonl"), brain_id, rows_fn(i), run_id=f"{name}{i}")
    return str(d)


def run(tmp_path, inputs, **kw):
    return run_metric("m007", inputs, out_dir=str(tmp_path / "out"), images=False, **kw)[0]


def sm(rep, sid):
    return next(s for s in rep["submeasures"] if s["id"] == sid)


FLANKED = [0, 140, 220]
CLUSTER = [0, 20, 40]


def sets(tmp_path, scripted_b, control_b, n=100):
    s = make_set(tmp_path, "s", "scripted@1", lambda i: engagement_rows(scripted_b), n)
    c = make_set(tmp_path, "c", "directcharge@1", lambda i: engagement_rows(control_b), n)
    return [s, c]


def test_math_helpers():
    assert abs(ff_chance(3, 2) - 0.75) < 1e-12
    assert abs(ff_chance(4, 2) - 0.6875) < 1e-12
    assert abs(spread_deg([350, 10]) - 20) < 1e-9
    assert abs(spread_deg([0, 120, 240]) - 240) < 1e-9


def test_flanked_scripted_vs_clustered_control_passes_headless(tmp_path):
    rep = run(tmp_path, sets(tmp_path, FLANKED, CLUSTER))
    assert sm(rep, "M-007.C1")["status"] == "PASS"
    assert rep["extra"]["m007"]["scripted"]["fr_commit"] == 1.0
    assert rep["extra"]["m007"]["control"]["fr_commit"] == 0.0
    assert sm(rep, "M-007.C4")["status"] == "PASS"
    assert sm(rep, "M-007.C5")["status"] == "PENDING"
    assert rep["verdict"] == "INCOMPLETE" and rep["exit_code"] == 3
    assert rep["headless_verdict"] == "PASS"


def test_discrimination_vs_null_reported(tmp_path):
    rep = run(tmp_path, sets(tmp_path, FLANKED, CLUSTER))
    d = sm(rep, "M-007.C1")["diagnostics"]["discrimination_vs_null"]
    assert d["ci_overlap"] is False and d["scripted_better_non_overlapping"] is True
    # identical behaviour in both sets: C2 CI overlaps (a measured fact, not a status change)
    (tmp_path / "b").mkdir()
    rep2 = run(tmp_path / "b", sets(tmp_path / "b", FLANKED, FLANKED))
    d2 = sm(rep2, "M-007.C1")["diagnostics"]["discrimination_vs_null"]
    assert d2["ci_overlap"] is True and d2["scripted_better_non_overlapping"] is False
    assert "discrimination_switch_rate_vs_null" in sm(rep2, "M-007.C3")["diagnostics"]


def test_clustered_scripted_fails_c1(tmp_path):
    rep = run(tmp_path, sets(tmp_path, CLUSTER, CLUSTER))
    assert sm(rep, "M-007.C1")["status"] == "FAIL"
    assert rep["verdict"] == "FAIL" and rep["exit_code"] == 1


def test_control_as_flanked_as_scripted_fails_ratio_to_null(tmp_path):
    rep = run(tmp_path, sets(tmp_path, FLANKED, FLANKED))
    c1 = sm(rep, "M-007.C1")
    assert c1["status"] == "FAIL"
    assert next(p for p in c1["parts"] if p["name"] == "fr_commit_over_null")["status"] == "FAIL"


def test_missing_control_is_incomplete_not_pass(tmp_path):
    s = make_set(tmp_path, "s", "scripted@1", lambda i: engagement_rows(FLANKED))
    rep = run(tmp_path, [s])
    assert rep["headless_verdict"] == "INCOMPLETE"
    assert sm(rep, "M-007.C1")["status"] == "NOT_RUN"
    assert "control runs" in rep["coverage"]["missing"]


def test_fewer_than_100_runs_is_incomplete(tmp_path):
    rep = run(tmp_path, sets(tmp_path, FLANKED, CLUSTER, n=5))
    assert sm(rep, "M-007.runs")["status"] == "NOT_RUN"
    assert rep["headless_verdict"] == "INCOMPLETE"


def test_leak_fails_c4(tmp_path):
    def rows(i):
        r = engagement_rows(FLANKED)
        if i == 0:
            r.append(dec(40, 1, 100.0, 0.0, 1, 1, 101, vis=0, age=3.5))
        return r
    s = make_set(tmp_path, "s", "scripted@1", rows)
    c = make_set(tmp_path, "c", "directcharge@1", lambda i: engagement_rows(CLUSTER))
    rep = run(tmp_path, [s, c])
    assert sm(rep, "M-007.C4")["status"] == "FAIL"
    assert rep["extra"]["m007"]["scripted"]["leaks"] == 1


def test_thrash_counts_a_b_a_within_4s(tmp_path):
    def rows(i):
        r = engagement_rows(FLANKED, targets=(101, 102))
        x, y = pos(0)
        for step, tgt, reason in ((100, 102, 11), (150, 101, 11)):    # A->B at 2.0 s, B->A at 3.0 s
            r.append(dec(step, 1, x, y, 1, 1, tgt, reason=reason))
        return r
    s = make_set(tmp_path, "s", "scripted@1", rows)
    c = make_set(tmp_path, "c", "directcharge@1", lambda i: engagement_rows(CLUSTER))
    rep = run(tmp_path, [s, c])
    p = rep["extra"]["m007"]["scripted"]
    assert p["voluntary"] == 200 and p["thrash"] == 100 and abs(p["thrash_fraction"] - 0.5) < 1e-9
    assert sm(rep, "M-007.C3")["status"] == "FAIL"


def test_focus_fire_excess(tmp_path):
    # 3 kaiju, 2 live targets, all on target 101: FF 1.0 vs chance 0.75 => FE 1.0
    rep = run(tmp_path, [make_set(tmp_path, "s", "scripted@1", lambda i: engagement_rows(FLANKED, targets=(101, 102)))])
    assert abs(rep["extra"]["m007"]["scripted"]["fe"] - 1.0) < 1e-9
    assert sm(rep, "M-007.C2")["status"] == "PASS"


@pytest.mark.parametrize("mutate", ["truncate", "schema", "reason", "nan", "header_missing", "unknown_brain"])
def test_invalid_inputs_exit_2(tmp_path, mutate):
    p = tmp_path / "bad.jsonl"
    write_log(str(p), "scripted@1", engagement_rows(FLANKED))
    lines = p.read_text().split("\n")
    if mutate == "truncate":
        p.write_text("\n".join(lines)[:-30])
    elif mutate == "schema":
        h = json.loads(lines[0]); h["schema"] = "brain_log@v2"; lines[0] = json.dumps(h); p.write_text("\n".join(lines))
    elif mutate == "reason":
        lines[3] = lines[3].replace('"reason": 0', '"reason": 99'); p.write_text("\n".join(lines))
    elif mutate == "nan":
        lines[3] = lines[3].replace('"x": ', '"x": NaN, "y0": '); p.write_text("\n".join(lines))
    elif mutate == "header_missing":
        p.write_text("\n".join(lines[1:]))
    else:
        h = json.loads(lines[0]); h["brain_id"] = "other@1"; lines[0] = json.dumps(h); p.write_text("\n".join(lines))
    rep = run(tmp_path, [str(p)])
    assert rep["exit_code"] == 2 and rep["verdict"] == "INVALID_INPUT"


def test_deterministic_report(tmp_path):
    a = run(tmp_path, sets(tmp_path, FLANKED, CLUSTER))
    b = run(tmp_path, [str(tmp_path / "s"), str(tmp_path / "c")])
    assert a["extra"] == b["extra"]
    assert sm(a, "M-007.C1")["diagnostics"]["fr_commit_ci95_bootstrap"] == [1.0, 1.0]


def test_cli_validate_brain_log(tmp_path):
    from test_tools import cli
    p = tmp_path / "ok.jsonl"
    write_log(str(p), "scripted@1", engagement_rows(FLANKED))
    r = cli("validate", "brain_log", str(p))
    assert r.returncode == 0
