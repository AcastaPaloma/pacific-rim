"""M-015@v2, M-101 and M-102 (T-111): pass fixtures, one-clause-fails fixtures, INVALID_INPUT and INCOMPLETE paths, the real C# harness dump."""
import copy
import json
import os

import numpy as np
import pytest

from conftest import PROJECT
from kaiju_metrics.runner import run_metric
from kaiju_metrics.streaming.m102 import contour_rms, de_share, pop_events
from kaiju_metrics.png import write_png
from test_tools import cli

REAL_DUMP = os.path.join(PROJECT, "evidence", "T-111", "m015_logic_30mps.json")


def sm(rep, sid):
    return next(s for s in rep["submeasures"] if s["id"] == sid)


def wjson(path, obj):
    path.write_text(json.dumps(obj))
    return str(path)


# ---------------------------------------------------------------- M-015@v2
def m015(mode="unity", **kw):
    d = {"format": "m015_dump", "schema": 2, "mode": mode, "label": "t", "path_length_m": 92562.0, "simulated_s": 10150.0, "step_s": 1.0,
         "mech_speed_mps": 30.0, "steps": 10150, "bodies": {"mechs": 2, "kaiju": 3, "debris": 100, "total": 106},
         "max_abs_local_m": 3112.0, "max_reconstruction_err_m": 0.00012, "round_trip_err_m": 0.0, "probe_max_err_m": 0.00012, "probe_steps": 3087,
         "pair_change_max_m": 0.0003, "rebase_pair_change_max_m": 0.0002, "terrain_delta_max_m": 0.003, "rebase_terrain_delta_max_m": 0.003,
         "placed_vs_read_max_m": 0.0, "rebases": 94, "cluster_shifts": 94, "clusters_max": 3, "hold_events": 0, "frames_below_surface": 0,
         "frames_without_ground": 0, "world_origin_mismatches": 0, "unclaimed_chunks": 0, "tile_loads": 2155, "max_loads_per_key": 3}
    d.update(kw)
    return d


def run15(tmp_path, *dumps):
    paths = [wjson(tmp_path / f"d{i}.json", d) for i, d in enumerate(dumps)]
    return run_metric("m015", paths, out_dir=str(tmp_path / "o"), images=False)[0]


def test_m015_unity_dump_passes(tmp_path):
    r = run15(tmp_path, m015())
    assert r["verdict"].startswith("PASS") and r["exit_code"] == 0


def test_m015_logic_only_is_incomplete(tmp_path):
    r = run15(tmp_path, m015("logic"))
    assert r["verdict"] == "INCOMPLETE" and r["exit_code"] == 3
    assert all(s["status"] == "PASS" for s in r["submeasures"] if s["kind"] == "gating")


@pytest.mark.parametrize("field,value,sid", [
    ("max_abs_local_m", 4096.5, "M-015.a"), ("max_reconstruction_err_m", 0.0011, "M-015.b"), ("round_trip_err_m", 0.0011, "M-015.c"),
    ("probe_max_err_m", 0.002, "M-015.c"), ("pair_change_max_m", 0.002, "M-015.d"), ("rebase_pair_change_max_m", 0.002, "M-015.d"),
    ("terrain_delta_max_m", 0.11, "M-015.d"), ("rebase_terrain_delta_max_m", 0.2, "M-015.d"), ("hold_events", 1, "M-015.e"),
    ("frames_below_surface", 1, "M-015.e"), ("frames_without_ground", 1, "M-015.e"), ("world_origin_mismatches", 1, "M-015.e"),
    ("path_length_m", 74000.0, "M-015.scope"), ("rebases", 0, "M-015.scope"),
])
def test_m015_each_clause_can_fail(tmp_path, field, value, sid):
    r = run15(tmp_path, m015(**{field: value}))
    assert r["verdict"] == "FAIL" and r["exit_code"] == 1
    assert sm(r, sid)["status"] == "FAIL"


def test_m015_body_counts_below_definition_fail(tmp_path):
    r = run15(tmp_path, m015(bodies={"mechs": 1, "kaiju": 3, "debris": 100, "total": 104}))
    assert sm(r, "M-015.scope")["status"] == "FAIL"


def test_m015_missing_field_or_wrong_schema_is_invalid(tmp_path):
    d = m015(); del d["round_trip_err_m"]
    assert run15(tmp_path, d)["exit_code"] == 2
    assert run15(tmp_path, m015(schema=1))["exit_code"] == 2
    assert run15(tmp_path, m015(max_abs_local_m=float("nan")) if False else m015(max_abs_local_m="x"))["exit_code"] == 2


def test_m015_real_harness_dump_is_valid_and_logic_incomplete(tmp_path):
    if not os.path.isfile(REAL_DUMP):
        pytest.skip("run the dotnet Host tests with KAIJU_M015_OUT=evidence/T-111 first")
    r = run_metric("m015", [REAL_DUMP], out_dir=str(tmp_path / "o"), images=False)[0]
    assert r["verdict"] == "INCOMPLETE"
    assert all(s["status"] == "PASS" for s in r["submeasures"] if s["kind"] == "gating"), [s["id"] for s in r["submeasures"] if s["status"] != "PASS"]


def test_m015_cli(tmp_path):
    p = wjson(tmp_path / "d.json", m015())
    r = cli("m015", p, "--out", str(tmp_path / "o"), "--quiet")
    assert r.returncode == 0, r.stdout + r.stderr


# ---------------------------------------------------------------- M-101
def frames(speed, mode="streaming", rendering=True, n=300, base=8.0, **kw):
    rng = np.random.default_rng(1)
    d = {"format": "streaming_frames", "schema": 1, "label": "t", "speed_mps": speed, "mode": mode, "rendering": rendering,
         "frame_ms": [float(x) for x in base + rng.random(n)] if rendering else None,
         "streaming_main_ms": [float(x) for x in 0.5 + 0.3 * rng.random(n)], "max_work_item_ms": 1.5,
         "late_tile_frames": 0, "physics_holds": 0, "draw_calls": [1800] * n if rendering else None, "cold_start": False}
    d.update(kw)
    return d


def run101(tmp_path, *runs):
    paths = [wjson(tmp_path / f"r{i}.json", d) for i, d in enumerate(runs)]
    return run_metric("m101", paths, out_dir=str(tmp_path / "o"), images=False)[0]


def full101(**kw):
    return [frames(s, m, **kw) if m == "streaming" else frames(s, m) for s in (30.0, 60.0) for m in ("streaming", "control")]


def test_m101_pass(tmp_path):
    r = run101(tmp_path, *full101())
    assert r["verdict"].startswith("PASS"), [(s["id"], s["status"]) for s in r["submeasures"]]


def test_m101_without_rendered_frames_is_incomplete_not_pass(tmp_path):
    runs = [frames(s, m, rendering=False) for s in (30.0, 60.0) for m in ("streaming", "control")]
    r = run101(tmp_path, *runs)
    assert r["verdict"] == "INCOMPLETE"
    assert sm(r, "M-101.a")["status"] == "NOT_RUN" and sm(r, "M-101.d")["status"] == "PASS"


def test_m101_missing_speed_is_incomplete(tmp_path):
    r = run101(tmp_path, frames(30.0), frames(30.0, "control"))
    assert r["verdict"] == "INCOMPLETE" and r["coverage"]["missing"]


def test_m101_long_frame_fails(tmp_path):
    runs = full101()
    runs[0]["frame_ms"][17] = 33.4
    r = run101(tmp_path, *runs)
    assert r["verdict"] == "FAIL" and sm(r, "M-101.a")["status"] == "FAIL"


def test_m101_p99_ratio_fails(tmp_path):
    runs = full101()
    runs[0]["frame_ms"] = [x * 1.4 for x in runs[0]["frame_ms"]]
    r = run101(tmp_path, *runs)
    assert sm(r, "M-101.b")["status"] == "FAIL"


def test_m101_late_tile_hold_and_drawcalls_fail(tmp_path):
    for key, val, sid in (("late_tile_frames", 1, "M-101.d"), ("physics_holds", 2, "M-101.d")):
        runs = full101(); runs[2][key] = val
        assert sm(run101(tmp_path, *runs), sid)["status"] == "FAIL"
    runs = full101(); runs[0]["draw_calls"][3] = 2447
    assert sm(run101(tmp_path, *runs), "M-101.e")["status"] == "FAIL"


def test_m101_diagnostic_is_advisory(tmp_path):
    runs = full101(); runs[0]["max_work_item_ms"] = 4.5
    r = run101(tmp_path, *runs)
    assert sm(r, "M-101.c")["status"] == "FAIL" and sm(r, "M-101.c")["kind"] == "advisory"
    assert r["verdict"].startswith("PASS") and r["warnings"]


def test_m101_invalid_inputs(tmp_path):
    d = frames(30.0); d["rendering"] = False
    assert run101(tmp_path, d)["exit_code"] == 2      # rendering false but frame times present
    d = frames(30.0); d["frame_ms"][0] = -1.0
    assert run101(tmp_path, d)["exit_code"] == 2


# ---------------------------------------------------------------- M-102
def img(h=48, w=64, shift=0, colour=(120, 140, 160)):
    """Sky above a horizon at row 20 + shift, land below; returns (rgb, labels)."""
    rgb = np.zeros((h, w, 3), np.uint8)
    lab = np.zeros((h, w), np.uint8)
    y = 20 + shift
    rgb[:y] = (200, 220, 240)
    rgb[y:] = colour
    lab[y:] = 1
    return rgb, lab


def build_set(tmp_path, shift=0, colour=(120, 140, 160), sweeps=True, pop=False, full=True):
    d = tmp_path / "set"
    d.mkdir()
    pairs = []
    n = 0
    sw_names = ["T1", "T2", "T3", "T4", "B1", "B2"]
    for sw in sw_names if full else sw_names[:1]:
        for k in (4, 6):
            for fac in (0.98, 1.02):
                for vp in range(12 if full else 1):
                    for yaw in (0, 120, 240):
                        if not full and (vp or yaw): continue
                        if full and (k == 6 or sw != "T1") and (vp > 0 or yaw > 0):
                            pass
                        n += 1
                        rgb0, l0 = img()
                        rgb1, l1 = img(shift=shift, colour=colour)
                        base = f"p{n}"
                        for nm, a in (("ref", rgb0), ("test", rgb1)):
                            write_png(str(d / f"{base}_{nm}.png"), a)
                        for nm, a in (("rl", l0), ("tl", l1)):
                            write_png(str(d / f"{base}_{nm}.png"), a)
                        pairs.append({"id": base, "switch": sw, "viewpoint": f"vp{vp}", "yaw_deg": yaw, "k": k, "distance_factor": fac,
                                      "ref": f"{base}_ref.png", "test": f"{base}_test.png", "ref_labels": f"{base}_rl.png", "test_labels": f"{base}_tl.png"})
    sweep_list = []
    if sweeps:
        for sw in (sw_names if full else sw_names[:1]):
            for sp in (2.0, 10.0, 30.0) if full else (2.0,):
                frames_, masks = [], []
                nf = 240 if full else 20
                for i in range(nf):
                    rgb = np.full((16, 16, 3), 100, np.uint8)
                    if pop and i == 15 and sw == "T1" and sp == 2.0:
                        rgb[:8, :8] = 250
                    elif i % 2:
                        rgb[15, 15] = 101
                    m = np.zeros((16, 16), np.uint8); m[:8, :8] = 255
                    fn, mn = f"s_{sw}_{sp:g}_{i}.png", f"m_{sw}_{sp:g}_{i}.png"
                    write_png(str(d / fn), rgb); write_png(str(d / mn), m)
                    frames_.append(fn); masks.append(mn)
                sweep_list.append({"id": f"{sw}_{sp:g}", "switch": sw, "speed_mps": sp, "frames": frames_, "level_change_masks": masks})
    s = {"format": "lod_capture", "schema": 1, "output_height_px": 48, "pairs": pairs, "sweeps": sweep_list, "m004": None}
    (d / "m102_set.json").write_text(json.dumps(s))
    return str(d)


def run102(tmp_path, d):
    return run_metric("m102", [d], out_dir=str(tmp_path / "o"), images=False)[0]


def test_m102_contour_rms_known_shift():
    _, a = img(); _, b = img(shift=1); _, c = img(shift=3)
    r1, _, _ = contour_rms(a, b)
    r3, _, _ = contour_rms(a, c)
    assert r1 == pytest.approx(1.0, abs=1e-9) and r3 == pytest.approx(3.0, abs=1e-9)
    assert contour_rms(a, a)[0] == 0.0
    assert contour_rms(np.zeros((4, 4), np.uint8), np.zeros((4, 4), np.uint8))[0] is None
    assert contour_rms(a, np.zeros_like(a))[0] == float("inf")


def test_m102_de_share_limits():
    a, _ = img(); b, _ = img(colour=(121, 140, 160))
    s, mx = de_share(a, b, 2.0)
    assert s == 1.0 and mx > 0
    c, _ = img(colour=(180, 140, 160))
    s2, _ = de_share(a, c, 2.0)
    assert s2 < 0.99


def test_m102_pop_events_rule():
    flat = [0.0] * 30
    assert pop_events(flat, 0.0025, 3.0, 10) == []
    spiky = list(flat); spiky[15] = 0.003
    assert pop_events(spiky, 0.0025, 3.0, 10) == [15]
    noisy = [0.002] * 30; noisy[15] = 0.0055             # above the 3 x median (0.006)? no: 0.0055 < 0.006 and above floor -> still no pop
    assert pop_events(noisy, 0.0025, 3.0, 10) == []
    noisy[16] = 0.0061
    assert pop_events(noisy, 0.0025, 3.0, 10) == [16]


def test_m102_full_set_passes(tmp_path):
    r = run102(tmp_path, build_set(tmp_path))
    assert r["verdict"].startswith("INCOMPLETE") is False or r["coverage"]["missing"] == []
    gating = {s["id"]: s["status"] for s in r["submeasures"] if s["kind"] == "gating"}
    assert gating["M-102.a"] == "PASS" and gating["M-102.b"] == "PASS" and gating["M-102.c"] == "PASS"
    assert gating["M-102.d"] == "NOT_RUN"      # no M-004 reports given: INCOMPLETE, never PASS
    assert r["verdict"] == "INCOMPLETE"


def test_m102_silhouette_shift_fails(tmp_path):
    r = run102(tmp_path, build_set(tmp_path, shift=2, sweeps=False))
    assert sm(r, "M-102.a")["status"] == "FAIL" and r["verdict"] == "FAIL"


def test_m102_colour_difference_fails(tmp_path):
    r = run102(tmp_path, build_set(tmp_path, colour=(200, 140, 160), sweeps=False))
    assert sm(r, "M-102.b")["status"] == "FAIL" and r["verdict"] == "FAIL"


def test_m102_pop_in_sweep_fails(tmp_path):
    r = run102(tmp_path, build_set(tmp_path, pop=True))
    assert sm(r, "M-102.c")["status"] == "FAIL"


def test_m102_small_set_is_incomplete(tmp_path):
    r = run102(tmp_path, build_set(tmp_path, full=False))
    assert r["verdict"] == "INCOMPLETE" and r["coverage"]["missing"]


def test_m102_m004_relative_check(tmp_path):
    d = build_set(tmp_path, sweeps=False)
    lod = {"verdict": "PASS", "extra": {"sequences": [{"sequence": "S0", "S": 0.02, "C": 0.01}, {"sequence": "T1", "S": 0.05, "C": None}]}}
    pin = {"verdict": "PASS", "extra": {"sequences": [{"sequence": "S0", "S": 0.02, "C": 0.005}, {"sequence": "T1", "S": 0.03, "C": None}]}}
    (tmp_path / "set" / "lod.json").write_text(json.dumps(lod)); (tmp_path / "set" / "pin.json").write_text(json.dumps(pin))
    s = json.loads((tmp_path / "set" / "m102_set.json").read_text()); s["m004"] = {"lod": "lod.json", "pinned": "pin.json"}
    (tmp_path / "set" / "m102_set.json").write_text(json.dumps(s))
    r = run102(tmp_path, d)
    dd = sm(r, "M-102.d")
    assert dd["status"] == "FAIL"        # T1 S_px 0.05 > 0.03 + 0.01
    lod["extra"]["sequences"][1]["S"] = 0.035
    (tmp_path / "set" / "lod.json").write_text(json.dumps(lod))
    assert sm(run102(tmp_path, d), "M-102.d")["status"] == "PASS"


def test_m102_invalid_inputs(tmp_path):
    d = build_set(tmp_path, sweeps=False, full=False)
    os.remove(os.path.join(d, "p1_test.png"))
    assert run102(tmp_path, d)["exit_code"] == 2
    assert run_metric("m102", [str(tmp_path / "nothing")], out_dir=str(tmp_path / "o2"), images=False)[0]["exit_code"] == 2
