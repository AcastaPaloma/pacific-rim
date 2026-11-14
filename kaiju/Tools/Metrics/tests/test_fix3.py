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
