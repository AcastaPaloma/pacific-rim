"""M-051 / M-053 / M-054: pass and fail fixtures, no silent pass."""
import copy
import os

import pytest

from conftest import PROJECT
from fixtures import gen_destruction as G
from kaiju_metrics.runner import run_metric
from test_tools import cli

FAULTS = os.path.join(PROJECT, "Assets", "StreamingAssets", "Geo", "global", "v1", "faults_bay.geojson")


def sm(rep, sid):
    return next(s for s in rep["submeasures"] if s["id"] == sid)


def run(cmd, tmp_path, d, name="d.json"):
    p = G.write_json(tmp_path / name, d)
    return run_metric(cmd, [str(p)], out_dir=str(tmp_path / "o"), images=False)[0]


def fails(rep, sid, part):
    assert rep["verdict"] == "FAIL" and rep["exit_code"] == 1, rep["verdict"]
    assert any(p["name"] == part and p["status"] == "FAIL" for p in sm(rep, sid)["parts"]), sm(rep, sid)["parts"]


# ------------------------------------------------------------------ M-051
def test_m051_pass(tmp_path):
    rep = run("m051", tmp_path, G.make_fragility_dump())
    assert rep["verdict"].startswith("PASS"), [s for s in rep["submeasures"] if s["status"] != "PASS"]
    assert rep["extra"]["m051"]["classes"] == ["A", "B", "C"]
    assert sm(rep, "M-051.pga")["parts"][1]["value"] is not None       # a median point exists on the grid


@pytest.mark.parametrize("mut,sid,pt", [
    (lambda d: d["classes"][0]["pga_points"][3]["counts"].__setitem__(0, 100), "M-051.pga", "max_abs_dev"),
    (lambda d: d["classes"][1]["energy_points"][5]["counts"].__setitem__(1, 0), "M-051.energy", "max_abs_dev"),
    (lambda d: d["classes"][0].update(state_decreases=1), "M-051.monotone", "violations_max"),
    (lambda d: d["classes"][2].update(repeat_identical=False), "M-051.monotone", "violations_max"),
    (lambda d: d["classes"][1]["theta_k"].__setitem__(2, 0.0155), "M-051.table", "table_abs_tol"),
    (lambda d: d["classes"][0].update(hazus_type="C2.H"), "M-051.table", "table_abs_tol"),
])
def test_m051_each_rule_fails(tmp_path, mut, sid, pt):
    d = G.make_fragility_dump()
    mut(d)
    fails(run("m051", tmp_path, d), sid, pt)


def test_m051_raw_theta_comparison_would_fail(tmp_path):
    """C-3: a dump built from Phi(ln(PGA/theta_k)/beta) (raw theta_k, no chain) must not pass."""
    d = G.make_fragility_dump()
    from kaiju_metrics.destruction.m051 import phi
    import math
    c = d["classes"][0]
    for p in c["pga_points"]:
        p["counts"] = [round(d["n"] * phi(math.log(p["pga_g"] / c["theta_k"][k]) / 0.64)) for k in range(4)]
    assert run("m051", tmp_path, d)["verdict"] == "FAIL"


@pytest.mark.parametrize("bad", ["missing", "empty", "no_classes", "small_n", "bad_counts"])
def test_m051_invalid_input(tmp_path, bad):
    d = G.make_fragility_dump()
    p = tmp_path / "d.json"
    if bad == "missing":
        p = tmp_path / "nope.json"
    elif bad == "empty":
        p.write_text("")
    else:
        if bad == "no_classes":
            d["classes"] = []
        elif bad == "small_n":
            d["n"] = 10
        else:
            d["classes"][0]["pga_points"][0]["counts"] = [1, 2]
        G.write_json(p, d)
    rep = run_metric("m051", [str(p)], out_dir=str(tmp_path / "o"), images=False)[0]
    assert rep["verdict"] == "INVALID_INPUT" and rep["exit_code"] == 2


def test_m051_cli(tmp_path):
    p = G.write_json(tmp_path / "d.json", G.make_fragility_dump())
    assert cli("m051", str(p), "--out", str(tmp_path / "c"), "--quiet").returncode == 0


# ------------------------------------------------------------------ M-053
def test_m053_pass(tmp_path):
    rep = run("m053", tmp_path, G.make_rupture_dump(FAULTS))
    assert rep["verdict"].startswith("PASS"), [s for s in rep["submeasures"] if s["status"] != "PASS"]
    assert rep["extra"]["m053"]["mapped"] == 1 and rep["extra"]["m053"]["samples"] > 10


def _shift(d):
    d["ruptures"][0]["polyline"] = [[x + 5.0, z] for x, z in d["ruptures"][0]["polyline"]]


@pytest.mark.parametrize("mut,sid,pt", [
    (_shift, "M-053.offset", "max_m"),
    (lambda d: d["ruptures"][0].update(fault_id="99999"), "M-053.mapped", "mapped_share"),
    (lambda d: d["ruptures"][0].update(vertex_end=1), "M-053.mapped", "mapped_share"),
    (lambda d: d["ruptures"][0]["slip"][1].update(applied_m=2.0), "M-053.slip", "slip_rel"),
    (lambda d: d.update(faults_sha256="0" * 64), "M-053.hash", "hash_mismatches"),
])
def test_m053_each_rule_fails(tmp_path, mut, sid, pt):
    d = G.make_rupture_dump(FAULTS)
    mut(d)
    fails(run("m053", tmp_path, d), sid, pt)


def test_m053_empty_invalid(tmp_path):
    d = G.make_rupture_dump(FAULTS)
    d["ruptures"] = []
    assert run("m053", tmp_path, d)["verdict"] == "INVALID_INPUT"


# ------------------------------------------------------------------ M-054
def test_m054_default_scenario_passes_clean(tmp_path):
    rep = run("m054", tmp_path, G.make_wc_dump())
    assert rep["verdict"].startswith("PASS") and not rep["warnings"]


def test_m054_haywired_warns_not_fails(tmp_path):
    d = G.make_wc_dump()
    d["ruptures"] = [{"name": "HayWired", "mw": 7.0, "srl_km": 84.0, "md_m": 1.9, "ad_m": 1.3, "trace_length_km": 120.0}]
    rep = run("m054", tmp_path, d)
    assert rep["verdict"].startswith("PASS") and any("dM_L" in w for w in rep["warnings"])


@pytest.mark.parametrize("mut,sid,pt", [
    (lambda r: r.update(mw=8.0), "M-054.consistency", "dM_L_max"),
    (lambda r: r.update(ad_m=r["ad_m"] * 5), "M-054.consistency", "dAD_max"),
    (lambda r: r.update(md_m=r["md_m"] * 8), "M-054.consistency", "dMD_max"),
    (lambda r: r.update(trace_length_km=10.0), "M-054.range", "srl_over_trace"),
    (lambda r: r.update(srl_km=1.0, trace_length_km=5.0), "M-054.range", "out_of_range"),
])
def test_m054_each_rule_fails(tmp_path, mut, sid, pt):
    d = G.make_wc_dump()
    mut(d["ruptures"][0])
    rep = run("m054", tmp_path, d)
    assert rep["verdict"] == "FAIL"
    assert any(p["name"] == pt and p["status"] == "FAIL" for p in sm(rep, sid)["parts"])


def test_m054_1906_anchor_discriminates(tmp_path):
    """1906: Mw 7.9, SRL 477 km (out of range 432), AD 3 m: the range part must fail."""
    d = G.make_wc_dump()
    d["ruptures"] = [{"name": "1906", "mw": 7.9, "srl_km": 477.0, "md_m": 6.0, "ad_m": 3.0, "trace_length_km": 500.0}]
    assert run("m054", tmp_path, d)["verdict"] == "FAIL"
