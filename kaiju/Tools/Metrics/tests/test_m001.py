"""M-001@v1 (R-001 §9): known answers (C3), 98 % / 102 % boundary pairs and negative controls (C4), verdict rules
(C2), worst-case reporting (C6), panel handling (C8). Truth is closed form from the generator's parameters."""
import math

import numpy as np
import pytest

from fixtures import gen_gait as G
from gaitkit import alexander, failing, g, m001_ratings, run_full, run_specs, sm, L

VC = G.VC
STEP = G.STEP
REL = 0.01


def close(a, b, rel=REL):
    return abs(a - b) <= rel * abs(b)


def part(rep, sid, prefix):
    return next(p for p in sm(rep, sid)["parts"] if p["name"].startswith(prefix))


# ------------------------------------------------------------------ known answers
def test_M001_1_known_froude_non_axis_heading(tmp_path):
    rep = run_specs(tmp_path, [G.spec_walk(0.17, strides=4, heading_deg=37.0, capture="walk_fr010")])
    assert close(part(rep, "M-001.1", "walk_fr010:Fr band")["value"], 0.17)


def test_M001_2_known_stride_ratio(tmp_path):
    sp = G.spec_walk(0.20, strides=4, heading_deg=-121.0, capture="walk_fr020")
    T_true = round(sp.stride_s / 0.02) * 0.02
    rep = run_specs(tmp_path, [sp])
    assert close(sm(rep, "M-001.2")["value"], T_true / alexander(0.20))


def test_M001_3_known_duty(tmp_path):
    sp = G.spec_walk(0.20, strides=4, duty=0.68, capture="walk_fr020")
    NT = round(sp.stride_s / 0.02)
    rep = run_specs(tmp_path, [sp])
    for p in sm(rep, "M-001.3")["parts"]:
        assert close(p["value"], round(0.68 * NT) / NT)
        assert close(p["double_support"], 2 * round(0.68 * NT) / NT - 1)


def test_M001_4_known_peak_decel(tmp_path):
    rep = run_specs(tmp_path, [G.spec_estop(decel=2.2)])
    assert close(sm(rep, "M-001.4")["value"], 2.2)


def test_M001_5_known_lateral_curve(tmp_path):
    rep = run_specs(tmp_path, [G.spec_curve(rate_dps=3.0)])
    assert close(sm(rep, "M-001.5")["value"], VC * math.radians(3.0))


def test_M001_5_hand_computed_left_handed_frame(tmp_path):
    """Hand-computed: heading 30 deg, forward 5 m/s, lateral velocity ramp 0.5 m/s^2 toward Unity right.
    fwd = (sin 30, 0, cos 30) = (0.5, 0, 0.8660); right = (cos 30, 0, -sin 30) = (0.8660, 0, -0.5).
    World acceleration = 0.5 * right = (0.4330, 0, -0.25): fore-aft 0, lateral +0.5 (rightward)."""
    from kaiju_metrics.contracts.gait_log import load_gait_log
    from kaiju_metrics.gait.m001 import Cap
    from kaiju_metrics.registry import load_registry
    sp = G.spec_walk(0.20, strides=2, heading_deg=30.0, capture="walk_fr020")
    sp.speed = G.Profile(0.0, 5.0, [(100.0, 0.0)])
    sp.lat_speed = G.Profile(0.0, 0.0, [(1.0, 0.0), (8.0, 0.5), (100.0, 0.0)])
    recs, _ = G.build(sp)
    path = G.write(recs, str(tmp_path / "hand.gait.jsonl"))
    lg = load_gait_log(path)
    k = int(5.0 / 0.02)
    assert np.allclose(lg.com_v[k], [5 * 0.5 + 2.0 * 0.8660254, 0, 5 * 0.8660254 - 2.0 * 0.5], atol=1e-6)
    c = Cap(lg, load_registry("M-001"), g)
    assert abs(c.a_lat[k] - 0.5) < 1e-6 and abs(c.a_fore[k]) < 1e-6


def test_M001_6_known_net_start_accel(tmp_path):
    rep = run_specs(tmp_path, [G.spec_start(steps_to_90=3.0)])
    a = 0.9 * VC / (3.0 * STEP)
    assert close(part(rep, "M-001.6", "start:net accel")["value"], a)


def test_M001_7_8_known_yaw_trapezoid(tmp_path):
    rep = run_specs(tmp_path, [G.spec_turn_inplace(peak_rate_dps=18.0, yaw_acc_dps2=2.5, total_deg=270.0)])
    assert close(sm(rep, "M-001.7")["value"], 18.0)
    assert close(sm(rep, "M-001.8")["value"], 2.5)


def test_M001_9_known_yaw_times_speed(tmp_path):
    rep = run_specs(tmp_path, [G.spec_curve(rate_dps=4.5)])
    assert close(sm(rep, "M-001.9")["value"], VC * math.radians(4.5))


def test_M001_10_known_start_steps(tmp_path):
    rep = run_specs(tmp_path, [G.spec_start(steps_to_90=2.6)])
    a = 0.9 * VC / (2.6 * STEP)
    k90 = math.ceil(0.9 * VC / a / 0.02 - 1e-9)
    assert close(sm(rep, "M-001.10")["value"], k90 * 0.02 / STEP)


def test_M001_11_known_stop(tmp_path):
    rep = run_specs(tmp_path, [G.spec_stop(steps_to_stop=2.2)])
    d = (VC - 0.3) / (2.2 * STEP)
    k = math.floor((VC - 0.3) / d / 0.02) + 1        # first sample strictly below 0.3 m/s
    t = k * 0.02
    assert close(part(rep, "M-001.11", "stop:steps")["value"], t / STEP)
    assert close(part(rep, "M-001.11", "stop:t*")["value"], t / math.sqrt(L / g))


def test_M001_12_known_estop(tmp_path):
    rep = run_specs(tmp_path, [G.spec_estop(decel=2.0)])
    k = math.floor((VC - 0.3) / 2.0 / 0.02) + 1
    p = sm(rep, "M-001.12")
    assert close(p["value"], k * 0.02)
    assert close(p["threshold"], VC / (0.30 * g))      # 3.1 s at reference


# ------------------------------------------------------------------ boundary pairs (98 % / 102 % of the threshold)
def _status(rep, sid, prefix=None):
    return part(rep, sid, prefix)["status"] if prefix else sm(rep, sid)["status"]


@pytest.mark.parametrize("fr,exp", [(0.098, "FAIL"), (0.102, "PASS"), (0.294, "PASS"), (0.306, "FAIL")])
def test_M001_1_boundary_band(tmp_path, fr, exp):
    rep = run_specs(tmp_path, [G.spec_walk(fr, strides=4, capture="walk_fr010")])
    assert _status(rep, "M-001.1", "walk_fr010:Fr band") == exp


@pytest.mark.parametrize("fr,exp", [(0.343, "PASS"), (0.357, "FAIL")])
def test_M001_1_boundary_never_above(tmp_path, fr, exp):
    rep = run_specs(tmp_path, [G.spec_walk(fr, strides=4, capture="walk_fast")])
    assert _status(rep, "M-001.1", "max stride Fr") == exp


@pytest.mark.parametrize("ratio,exp", [(0.85 * 0.98, "FAIL"), (0.85 * 1.02, "PASS"), (1.15 * 0.98, "PASS"),
                                       (1.15 * 1.02, "FAIL")])
def test_M001_2_boundary(tmp_path, ratio, exp):
    sp = G.spec_walk(0.20, strides=4, capture="walk_fr020")
    sp.stride_s = ratio * alexander(0.20)
    sp.duration_s = 4 * sp.stride_s + 1
    assert _status(run_specs(tmp_path, [sp]), "M-001.2") == exp


@pytest.mark.parametrize("duty,exp", [(0.588, "FAIL"), (0.612, "PASS"), (0.7056, "PASS"), (0.7344, "FAIL")])
def test_M001_3_boundary(tmp_path, duty, exp):
    assert _status(run_specs(tmp_path, [G.spec_walk(0.2, strides=4, duty=duty, capture="walk_fr020")]),
                   "M-001.3") == exp


@pytest.mark.parametrize("f,exp", [(0.98, "PASS"), (1.02, "FAIL")])
def test_M001_4_boundary(tmp_path, f, exp):
    assert _status(run_specs(tmp_path, [G.spec_estop(decel=f * 0.30 * g)]), "M-001.4") == exp


@pytest.mark.parametrize("f,exp", [(0.98, "PASS"), (1.02, "FAIL")])
def test_M001_5_boundary(tmp_path, f, exp):
    lim = 0.085 * g          # reference header: (W_f/2)/h equals the reference ratio
    rate = math.degrees(f * lim / VC)
    assert _status(run_specs(tmp_path, [G.spec_curve(rate_dps=rate, yaw_acc_dps2=1.0)]), "M-001.5") == exp


@pytest.mark.parametrize("f,exp", [(0.98, "PASS"), (1.02, "FAIL")])
def test_M001_6_boundary(tmp_path, f, exp):
    a = f * 0.14 * g
    rep = run_specs(tmp_path, [G.spec_start(steps_to_90=0.9 * VC / a / STEP)])
    assert _status(rep, "M-001.6", "start:net accel") == exp


@pytest.mark.parametrize("f,exp", [(0.98, "PASS"), (1.02, "FAIL")])
def test_M001_7_boundary(tmp_path, f, exp):
    rep = run_specs(tmp_path, [G.spec_turn_inplace(peak_rate_dps=f * 26.7, yaw_acc_dps2=3.5, total_deg=400.0)])
    assert _status(rep, "M-001.7") == exp


@pytest.mark.parametrize("f,exp", [(0.98, "PASS"), (1.02, "FAIL")])
def test_M001_8_boundary(tmp_path, f, exp):
    rep = run_specs(tmp_path, [G.spec_turn_inplace(peak_rate_dps=12.0, yaw_acc_dps2=f * 4.0, total_deg=180.0)])
    assert _status(rep, "M-001.8") == exp


@pytest.mark.parametrize("f,exp", [(0.98, "PASS"), (1.02, "FAIL")])
def test_M001_9_boundary(tmp_path, f, exp):
    rate = math.degrees(f * 0.085 * g / VC)
    assert _status(run_specs(tmp_path, [G.spec_curve(rate_dps=rate, crab=True)]), "M-001.9") == exp


@pytest.mark.parametrize("steps,exp", [(2 * 0.98, "FAIL"), (2 * 1.02, "PASS"), (4 * 0.98, "PASS"), (4 * 1.02, "FAIL")])
def test_M001_10_boundary(tmp_path, steps, exp):
    assert _status(run_specs(tmp_path, [G.spec_start(steps_to_90=steps)]), "M-001.10") == exp


@pytest.mark.parametrize("steps,exp", [(1.5 * 0.98, "FAIL"), (1.5 * 1.02, "PASS"), (3 * 0.98, "PASS"), (3 * 1.02, "FAIL")])
def test_M001_11_boundary_steps(tmp_path, steps, exp):
    assert _status(run_specs(tmp_path, [G.spec_stop(steps_to_stop=steps)]), "M-001.11", "stop:steps") == exp


@pytest.mark.parametrize("ts,exp", [(2.4 * 0.98, "FAIL"), (2.4 * 1.02, "PASS"), (4.8 * 0.98, "PASS"), (4.8 * 1.02, "FAIL")])
def test_M001_11_boundary_tstar(tmp_path, ts, exp):
    steps = ts * math.sqrt(L / g) / STEP
    assert _status(run_specs(tmp_path, [G.spec_stop(steps_to_stop=steps)]), "M-001.11", "stop:t*") == exp


@pytest.mark.parametrize("f,exp", [(0.98, "FAIL"), (1.02, "PASS")])
def test_M001_12_boundary(tmp_path, f, exp):
    t = f * VC / (0.30 * g)
    rep = run_specs(tmp_path, [G.spec_estop(decel=(VC - 0.3) / t)])
    assert _status(rep, "M-001.12") == exp


@pytest.mark.parametrize("med,exp", [(8 * 0.98, "FAIL"), (8 * 1.02, "PASS")])
def test_M001_13_boundary(tmp_path, med, exp):
    r = m001_ratings(str(tmp_path / "r.json"), "b1", overall=(med, med, med))
    rep = run_specs(tmp_path, [G.spec_walk(0.2, capture="walk_fr020")], ratings=r, label="b1")
    assert _status(rep, "M-001.13", "median overall") == exp


# ------------------------------------------------------------------ verdict: one gating failure at a time (C2)
NEG = {
    "M-001.1": {"walk_fr030": G.spec_walk(0.33, strides=4, capture="walk_fr030")},
    "M-001.2": {"walk_fr020": None},
    "M-001.3": {"walk_fr020": G.spec_walk(0.20, strides=5, duty=0.76)},
    "M-001.4": {"estop": G.spec_stop(capture="estop", steps_to_stop=(VC - 0.3) / 2.5 / STEP, spike=(1.0, 1.0, 0.6))},
    "M-001.5": {"walk_fr020": None},
    "M-001.6": {"start": G.spec_start(header={"reactor_power_W": 3.0e7})},
    "M-001.7": {"turn90_inplace": G.spec_turn_inplace(peak_rate_dps=30.0, yaw_acc_dps2=3.8, total_deg=400.0)},
    "M-001.8": {"turn90_inplace": G.spec_turn_inplace(peak_rate_dps=12.0, yaw_acc_dps2=5.0, total_deg=180.0)},
    "M-001.9": {"curve": G.spec_curve(rate_dps=7.0, crab=True)},
    "M-001.10": {"start": G.spec_start(steps_to_90=4.6)},
    "M-001.11": {"stop": G.spec_stop(steps_to_stop=3.6)},
    "M-001.12": {"estop": G.spec_estop(decel=2.9)},
}


def _neg(sid):
    o = dict(NEG[sid])
    if sid == "M-001.2":
        sp = G.spec_walk(0.20, strides=5)
        sp.stride_s = 0.8 * alexander(0.20); sp.duration_s = 6 * sp.stride_s + 1
        o["walk_fr020"] = sp
    if sid == "M-001.5":
        sp = G.spec_walk(0.20, strides=5)
        sp.sway_amp_mps = 1.3 / (2 * math.pi / (round(sp.stride_s / 0.02) * 0.02))   # peak a_lat ~ 1.3 m/s^2
        sp.sway_period_s = round(sp.stride_s / 0.02) * 0.02
        o["walk_fr020"] = sp
    return o


def test_M001_reference_set_passes_headless(tmp_path):
    rep = run_full(tmp_path)
    assert failing(rep) == []
    assert rep["verdict"] == "INCOMPLETE" and sm(rep, "M-001.13")["status"] == "PENDING"
    assert rep["headless_verdict"] == "PASS" and rep["headless_verdict_label"].startswith("PASS (provisional:")
    assert rep["exit_code"] == 3


def test_M001_reference_set_with_panel_passes(tmp_path):
    r = m001_ratings(str(tmp_path / "r.json"), "build7")
    rep = run_full(tmp_path, ratings=r, label="build7")
    assert rep["verdict"] == "PASS" and rep["exit_code"] == 0
    assert rep["verdict_label"].startswith("PASS (provisional:")


@pytest.mark.parametrize("sid", sorted(NEG, key=lambda s: int(s.split(".")[1])))
def test_M001_negative_fails_only_that_id(tmp_path, sid):
    r = m001_ratings(str(tmp_path / "r.json"), "b")
    rep = run_full(tmp_path, over=_neg(sid), ratings=r, label="b")
    assert failing(rep) == [sid], failing(rep)
    assert rep["verdict"] == "FAIL" and rep["exit_code"] == 1


def test_M001_13_negative_panel(tmp_path):
    r = m001_ratings(str(tmp_path / "r.json"), "b", overall=(9, 9, 9), scen={"curve": 6})
    rep = run_full(tmp_path, ratings=r, label="b")
    assert failing(rep) == ["M-001.13"]


def test_M001_13_too_few_raters_is_pending(tmp_path):
    r = m001_ratings(str(tmp_path / "r.json"), "b", overall=(9, 9))
    rep = run_specs(tmp_path, [G.spec_walk(0.2, capture="walk_fr020")], ratings=r, label="b")
    assert sm(rep, "M-001.13")["status"] == "PENDING"


def test_M001_unsmoothed_peak_printed(tmp_path):
    """A single-step spike is attenuated by smoothing; the unsmoothed peak is reported beside it (C6)."""
    sp = G.spec_walk(0.2, strides=4, capture="walk_fr020")
    sp.speed = G.Profile(0.0, VC, [(10.0, 0.0), (0.02, 5.0), (0.02, -5.0), (100.0, 0.0)])
    rep = run_specs(tmp_path, [sp])
    p = sm(rep, "M-001.4")["parts"][0]
    assert p["unsmoothed_peak"] > 3 * p["value"]


def test_M001_missing_capture_incomplete(tmp_path):
    rep = run_specs(tmp_path, [G.spec_walk(0.2, strides=4, capture="walk_fr020")])
    assert rep["verdict"] == "INCOMPLETE" and "start" in rep["coverage"]["missing"]


def test_M001_too_few_steady_strides_not_run(tmp_path):
    rep = run_specs(tmp_path, [G.spec_walk(0.2, strides=2, capture="walk_fr020")])
    assert part(rep, "M-001.1", "walk_fr020:Fr band")["status"] == "NOT_RUN"
    assert rep["headless_verdict"] == "INCOMPLETE"


def test_M001_mass_variant_clause_pending(tmp_path):
    rep = run_specs(tmp_path, [G.spec_estop()])
    assert sm(rep, "M-001.12")["info"]["mass_variants"]["status"] == "PENDING"


def test_M001_non_reference_normalisers_flagged(tmp_path):
    sp = G.spec_walk(0.2, strides=4, capture="walk_fr020", header={"leg_length_m": 50.0})
    rep = run_specs(tmp_path, [sp])
    assert any(f["code"] == "NON_REFERENCE_NORMALISERS" for f in rep["flags"])
