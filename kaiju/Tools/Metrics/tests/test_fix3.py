"""Fix cycle 3: DEF-T002-arch-8 (gating NOT_APPLICABLE), arch-9 (rescore semantics), feel-3 (numerical tolerance),
DEF-T004-arch-2 (per-frame output offsets)."""
import json
import os
import shutil
import subprocess
import sys

import numpy as np
import pytest
from PIL import Image

from conftest import METRICS
from fixtures import gen_gait as G
from fixtures import gen_style as GS
from kaiju_metrics.runner import run_metric

CLI = os.path.join(METRICS, "metrics.py")


def sm(rep, sid):
    return next(s for s in rep["submeasures"] if s["id"] == sid)


# ------------------------------------------------------------------ DEF-T002-feel-3: tolerance
def test_feel3_estop_exactly_at_limit_passes_M001_4(tmp_path):
    from gaitkit import run_specs
    rep = run_specs(tmp_path, [G.spec_estop(decel=0.30 * G.G)])
    p = sm(rep, "M-001.4")
    assert p["status"] == "PASS" and abs(p["value"] - 0.30 * G.G) < 1e-12      # float noise ~1e-14 above
    rep = run_specs(tmp_path, [G.spec_estop(decel=0.30 * G.G * (1 + 1e-6))], name="b")
    assert sm(rep, "M-001.4")["status"] == "FAIL"                                # tolerance never loosens


@pytest.mark.parametrize("op,thr,value,exp", [
    ("<=", 2.943, 2.943 * (1 + 1e-12), "PASS"), ("<=", 2.943, 2.943 * (1 + 1e-6), "FAIL"),
    (">=", 0.95, 0.95 * (1 - 1e-12), "PASS"), (">=", 0.95, 0.95 * (1 - 1e-6), "FAIL"),
    ("<", 0.005, 0.005 * (1 - 1e-12), "FAIL"), ("<", 0.005, 0.005 * (1 - 1e-6), "PASS"),
    (">", 6.0, 6.0, "FAIL"), ("in", 0.10, 0.10 * (1 - 1e-12), "PASS"), ("<=", 0.0, 1e-15, "FAIL"),
])
def test_feel3_uniform_tolerance_exact_threshold(op, thr, value, exp):
    from kaiju_metrics.report import judge, tool_settings
    assert tool_settings()["compare_rel_tol"] == 1e-9
    assert judge(value, op, thr, 0.30 if op == "in" else None)[0] == exp


def test_feel3_settings_recorded_in_report(tmp_path):
    from gaitkit import run_specs
    rep = run_specs(tmp_path, [G.spec_walk(0.2, capture="walk_fr020")], metric="m002")
    assert rep["tool"]["settings"]["compare_rel_tol"] == 1e-9 and rep["tool"]["semantics"] == 3


# ------------------------------------------------------------------ DEF-T002-arch-8: one rule for gating items
def test_arch8_stock_m004_pass_fixture_measures_every_gating_item(tmp_path):
    sd, _ = GS.m004_set(str(tmp_path))
    rep, _ = run_metric("m004", [sd], images=False, options={"profiles": "slate_teal", "viewpoints": "eye",
                                                              "fixture": True})
    gating = [s for s in rep["submeasures"] if s["kind"] == "gating"]
    assert all(s["status"] == "PASS" for s in gating) and rep["verdict"] == "PASS"
    assert all(p["status"] == "PASS" for s in gating for p in s["parts"])
    assert sm(rep, "M-004.C_px.Y")["value"] is not None


def test_arch8_one_undithered_T1_sequence_incomplete(tmp_path):
    sd, _ = GS.m004_set(str(tmp_path))
    shutil.rmtree(os.path.join(sd, "slate_teal_eye_T1"))
    GS.m004_sequence(sd, "slate_teal_eye_T1", "translate", 0.1, anchor="none", viewpoint="eye")
    rep, _ = run_metric("m004", [sd], images=False, options={"profiles": "slate_teal", "viewpoints": "eye",
                                                              "fixture": True})
    c = sm(rep, "M-004.C_px.T12")
    assert c["status"] == "NOT_RUN" and rep["verdict"] == "INCOMPLETE" and rep["exit_code"] == 3


@pytest.mark.parametrize("metric", ["M-001", "M-002", "M-003", "M-004"])
def test_arch8_shared_rule_gating_not_applicable_is_incomplete(metric):
    """The rule lives in the report model: any gating sub-measure NOT_APPLICABLE without a spec clause, or
    NOT_RUN / PENDING, makes the verdict INCOMPLETE; with a cited clause it does not block."""
    from kaiju_metrics.registry import load_registry
    from kaiju_metrics.report import Report, part
    reg = load_registry(metric)
    gating = [s for s in reg.ids() if reg.kind(s) == "gating"]

    def build(na_status, clause=None):
        r = Report(metric, reg, "x", {"command": "t", "inputs": [], "options": {}})
        for s in reg.ids():
            if s == gating[0]:
                r.add(s, na_status, note="test", spec_clause=clause)
            elif reg.kind(s) == "info":
                r.add(s, "REPORTED", value=1.0)
            else:
                r.add(s, parts=[part("p", 1.0, "<=", 2.0)])
        r.finalize()
        return r.verdict
    assert build("PASS") == "PASS"
    for st in ("NOT_APPLICABLE", "NOT_RUN", "PENDING"):
        assert build(st) == "INCOMPLETE", st
    assert build("NOT_APPLICABLE", clause="R-00x §n: does not apply to this input class") == "PASS"
    # a gating item whose parts are partly NOT_APPLICABLE (no clause) is NOT_RUN, never PASS
    r = Report(metric, reg, "x", {"command": "t", "inputs": [], "options": {}})
    d = r.add(gating[0], parts=[part("a", 1.0, "<=", 2.0), {"name": "b", "status": "NOT_APPLICABLE", "value": None,
                                                            "margin_rel": None, "op": "<=", "threshold": 2.0}])
    assert d["status"] == "NOT_RUN"
