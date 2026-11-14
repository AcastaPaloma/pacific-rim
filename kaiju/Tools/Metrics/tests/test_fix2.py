"""Fix cycle 2 (evaluations/T-002/eval-arch.md): one test per defect DEF-T002-arch-1 .. 7 and per audited metric."""
import json
import os
import shutil
import subprocess
import sys

import numpy as np
import pytest
from PIL import Image

from conftest import EXAMPLES, METRICS, PROJECT
from fixtures import gen_gait as G
from fixtures import gen_style as GS
from kaiju_metrics.runner import run_metric

CLI = os.path.join(METRICS, "metrics.py")


def sm(rep, sid):
    return next(s for s in rep["submeasures"] if s["id"] == sid)


def _contactless(path):
    recs = [json.loads(l) for l in open(path)]
    out = []
    for r in recs:
        if r["rec"] == "step":
            for f in r["feet"]:
                f["fn"], f["contact"], f["cn"] = 0.0, 0, None
        if r["rec"] == "event" and r["type"] in ("contact_begin", "contact_end", "cam_impulse"):
            continue
        out.append(r)
    open(path, "w").write("".join(json.dumps(r) + "\n" for r in out))


# ------------------------------------------------------------------ DEF-T002-arch-1 and the audit
def test_arch1_m002_eight_of_nine_contactless_incomplete(tmp_path):
    d = str(tmp_path / "g")
    paths = G.write_set(d)
    for name, p in paths.items():
        if name != "stomp":
            _contactless(p)
    rep, _ = run_metric("m002", [d], images=False)
    assert rep["verdict"] == "INCOMPLETE" and rep["exit_code"] == 3
    assert sm(rep, "M-002.1")["status"] == "NOT_RUN" and sm(rep, "M-002.4")["status"] == "NOT_RUN"
    assert sum("no usable contact" in m for m in rep["coverage"]["missing"]) == 8


def test_arch1_m002_one_contactless_capture_incomplete(tmp_path):
    d = str(tmp_path / "g")
    paths = G.write_set(d)
    _contactless(paths["walk_fr020"])
    rep, _ = run_metric("m002", [d], images=False)
    assert rep["verdict"] == "INCOMPLETE" and any(m.startswith("walk_fr020") for m in rep["coverage"]["missing"])


def test_arch1_audit_m001_contactless_capture_incomplete(tmp_path):
    from gaitkit import m001_ratings
    d = str(tmp_path / "g")
    paths = G.write_set(d)
    _contactless(paths["estop"])
    r = m001_ratings(str(tmp_path / "r.json"), "g")
    rep, _ = run_metric("m001", [d], ratings=r, images=False)
    assert rep["verdict"] == "INCOMPLETE" and rep["headless_verdict"] == "INCOMPLETE"
    assert any(m.startswith("estop: no contact") for m in rep["coverage"]["missing"])


def test_arch1_audit_m001_curve_without_cruise_not_run(tmp_path):
    from gaitkit import m001_ratings
    slow = G.spec_curve()
    slow.speed = G.Profile(0.0, 2.0, [(100.0, 0.0)])
    d = str(tmp_path / "g")
    G.write_set(d, over={"curve": slow})
    r = m001_ratings(str(tmp_path / "r.json"), "g")
    rep, _ = run_metric("m001", [d], ratings=r, images=False)
    p = next(p for p in sm(rep, "M-001.9")["parts"] if p["name"].startswith("curve"))
    assert p["status"] == "NOT_RUN" and rep["verdict"] != "PASS"


def test_arch1_audit_m001_curve_needs_steady_strides(tmp_path):
    short = G.spec_curve(strides=2)
    d = str(tmp_path / "g")
    G.write_set(d, over={"curve": short})
    rep, _ = run_metric("m001", [d], images=False)
    assert any(m.startswith("curve:") and "steady" in m for m in rep["coverage"]["missing"])


def test_arch1_audit_m003_profile_without_evaluated_dither_frames(tmp_path):
    """One good profile and one whose frames are all flat: DIG must be NOT_RUN for the flat profile."""
    sd, _ = GS.m003_pass_set(str(tmp_path))
    GS.m003_gameplay_frames(sd, profile="warm_amber_haze", seq_id="flat_amber")
    d = os.path.join(sd, "flat_amber")
    _, roles, _ = GS.load_profile("warm_amber_haze")
    for k in range(12):
        GS.repaint(d, k, np.full((90, 160), roles.index("ramp_3"), np.int32))
    rep, _ = run_metric("m003", [sd], images=False, options={"profiles": "slate_teal,warm_amber_haze"})
    s = sm(rep, "M-003.b.DIG")
    nr = [p for p in s["parts"] if p["status"] == "NOT_RUN"]
    assert s["status"] == "NOT_RUN" and nr and "warm_amber_haze" in nr[0]["name"]
    c1 = [p for p in sm(rep, "M-003.c1")["parts"] if "flat_amber" in p["name"]]
    assert c1 and all(p["status"] != "PASS" for p in c1[1:])      # flat frames: rho undefined, span 0
    assert rep["verdict"] != "PASS"


def test_arch1_audit_m003_profile_without_depth_bands_not_run(tmp_path):
    sd, _ = GS.m003_pass_set(str(tmp_path))
    GS.m003_gameplay_frames(sd, profile="warm_amber_haze", seq_id="sky_amber")
    d = os.path.join(sd, "sky_amber")
    for k in range(12):                                    # all sky: no depth band at all
        GS.save_png(os.path.join(d, f"f{k:06d}_depth.png"), np.full((90, 160), 65535, np.uint16))
    rep, _ = run_metric("m003", [sd], images=False, options={"profiles": "slate_teal,warm_amber_haze"})
    c1 = [p for p in sm(rep, "M-003.c1")["parts"] if p["name"].startswith("warm_amber_haze")]
    assert c1 and c1[0]["status"] == "NOT_RUN" and sm(rep, "M-003.c1")["status"] == "NOT_RUN"


def test_arch1_audit_m003_fully_masked_frame_not_hidden(tmp_path):
    sd, _ = GS.m003_pass_set(str(tmp_path))
    d = os.path.join(sd, "gameplay")
    sc = json.load(open(os.path.join(d, "f000003.json")))
    sc["files"]["mask_ui"] = "f000003_mask_ui.png"
    json.dump(sc, open(os.path.join(d, "f000003.json"), "w"))
    GS.save_png(os.path.join(d, "f000003_mask_ui.png"), np.full((360, 640), 255, np.uint8))
    rep, _ = run_metric("m003", [sd], images=False, options={"profiles": "slate_teal"})
    assert sm(rep, "M-003.a.output")["status"] == "NOT_RUN" and rep["headless_verdict"] != "PASS"


def test_arch1_audit_m004_sequence_without_dithered_pixels_reported(tmp_path):
    """C_px on a sequence with no interleaved pixel is not a silent 0 (cycle 2); since cycle 3 it is NOT_RUN and
    the verdict INCOMPLETE (DEF-T002-arch-8)."""
    sd, _ = GS.m004_set(str(tmp_path), anchor_y="direction_bands")
    rep, _ = run_metric("m004", [sd], images=False, options={"profiles": "slate_teal", "viewpoints": "eye",
                                                              "fixture": True})
    y = sm(rep, "M-004.C_px.Y")
    assert y["status"] == "NOT_RUN" and all(p["status"] == "NOT_RUN" for p in y["parts"])
    assert any("no dithered" in w for w in rep["warnings"])
    assert sm(rep, "M-004.C_px.T3")["status"] == "PASS" and rep["verdict"] == "INCOMPLETE"


# ------------------------------------------------------------------ DEF-T002-arch-2
def _trunc(p):
    b = open(p, "rb").read()
    open(p, "wb").write(b[: len(b) // 2])


@pytest.mark.parametrize("key", ["color_output", "color_internal", "depth", "mask_animated"])
def test_arch2_truncated_png_invalid_in_validate_and_metrics(tmp_path, key):
    sd, _ = GS.m003_pass_set(str(tmp_path))
    d = os.path.join(sd, "gameplay")
    sc = json.load(open(os.path.join(d, "f000002.json")))
    _trunc(os.path.join(d, sc["files"][key]))
    r = subprocess.run([sys.executable, CLI, "validate", "style_capture", sd], capture_output=True, text=True)
    assert r.returncode == 2 and "PNG" in r.stdout
    r = subprocess.run([sys.executable, CLI, "m003", sd, "--profiles", "slate_teal", "--out", str(tmp_path / "o")],
                       capture_output=True, text=True)
    assert r.returncode == 2 and "Traceback" not in r.stdout + r.stderr


def test_arch2_corrupt_png_crc_invalid(tmp_path):
    sd, _ = GS.m003_pass_set(str(tmp_path))
    p = os.path.join(sd, "gameplay", "f000001_color_output.png")
    b = bytearray(open(p, "rb").read())
    b[60] ^= 0xFF                                          # inside IDAT: CRC no longer matches
    open(p, "wb").write(bytes(b))
    from kaiju_metrics.contracts.palette_set import load_palette_set
    from kaiju_metrics.contracts.style_capture import validate_path
    errs = validate_path(sd, load_palette_set())
    assert any("CRC" in e.message or "decoded" in e.message for e in errs)


def test_arch2_unexpected_error_guard_no_traceback(tmp_path, monkeypatch):
    """A tool bug still exits 4 (distinct) but prints no stack trace unless KAIJU_METRICS_DEBUG is set."""
    env = dict(os.environ, KAIJU_METRICS_FORCE_BUG="1")
    r = subprocess.run([sys.executable, CLI, "list-metrics"], capture_output=True, text=True, env=env)
    assert r.returncode == 4 and "Traceback" not in r.stderr and "TOOL_ERROR" in r.stderr
    env["KAIJU_METRICS_DEBUG"] = "1"
    r = subprocess.run([sys.executable, CLI, "list-metrics"], capture_output=True, text=True, env=env)
    assert r.returncode == 4 and "Traceback" in r.stderr


# ------------------------------------------------------------------ DEF-T002-arch-3
@pytest.mark.parametrize("cmd", ["m003", "m004"])
def test_arch3_index_colour_mismatch_invalid(tmp_path, cmd):
    if cmd == "m003":
        sd, _ = GS.m003_pass_set(str(tmp_path)); seq = "gameplay"
        opts = {"profiles": "slate_teal"}
    else:
        sd, _ = GS.m004_set(str(tmp_path)); seq = "slate_teal_eye_T3"
        opts = {"profiles": "slate_teal", "viewpoints": "eye", "fixture": True}
    d = os.path.join(sd, seq)
    sc = json.load(open(os.path.join(d, "f000004.json")))
    p = os.path.join(d, sc["files"]["color_internal"])
    a = np.array(Image.open(p)); a[:] = 0
    GS.save_png(p, a)
    rep, _ = run_metric(cmd, [sd], images=False, options=opts)
    assert rep["verdict"] == "INVALID_INPUT" and any("palette_index" in e["where"] for e in rep["contract_errors"])


# ------------------------------------------------------------------ DEF-T002-arch-4
@pytest.mark.parametrize("key,val", [("pixel_origin", "bottom-left"), ("up", "+Z"), ("forward", "+X")])
def test_arch4_conventions_enforced(tmp_path, palettes, key, val):
    d = tmp_path / "example_set"
    shutil.copytree(os.path.join(EXAMPLES, "example_set"), d)
    p = d / "gameplay" / "f000000.json"
    sc = json.load(open(p)); sc["conventions"][key] = val; json.dump(sc, open(p, "w"))
    from kaiju_metrics.contracts.style_capture import validate_path
    errs = validate_path(str(d), palettes)
    assert any(f"conventions.{key}" in e.where for e in errs)


# ------------------------------------------------------------------ DEF-T002-arch-5
def test_arch5_min_bands_from_registry(tmp_path):
    import yaml
    from kaiju_metrics.registry import load_registry
    sd, _ = GS.m003_pass_set(str(tmp_path))
    d = yaml.safe_load(open(load_registry("M-003").path))
    d["procedure"]["min_bands"]["value"] = 7                  # more bands than the synthetic frames have
    p = tmp_path / "M-003@v1.1.yaml"
    yaml.safe_dump(d, open(p, "w"), sort_keys=False)
    rep, _ = run_metric("m003", [sd], registry=str(p), images=False, options={"profiles": "slate_teal"})
    assert sm(rep, "M-003.c1")["status"] == "NOT_RUN"


# ------------------------------------------------------------------ DEF-T002-arch-6
def test_arch6_O_advisory_and_draft_summary(tmp_path):
    import yaml
    from kaiju_metrics.registry import load_registry
    reg = load_registry("M-003")
    assert reg.kind("M-003.b.O") == "advisory"
    sd, _ = GS.m003_pass_set(str(tmp_path))
    rep, r = run_metric("m003", [sd], images=False, options={"profiles": "slate_teal"})
    assert any(w.startswith("ADVISORY M-003.b.O") for w in rep["warnings"])
    d = yaml.safe_load(open(load_registry("M-002").path))
    d.update(metric_status="draft")
    d["submeasures"]["M-002.1"]["thresholds"]["max_frac"]["value"] = -1.0
    p = tmp_path / "M-002@v9.yaml"
    yaml.safe_dump(d, open(p, "w"), sort_keys=False)
    gd = str(tmp_path / "g"); G.write_set(gd)
    rep, r = run_metric("m002", [gd], registry=str(p), images=False)
    assert rep["verdict"] == "INCOMPLETE" and rep["failures_suppressed"] is True
    assert "suppressed: draft metric" in r.summary(rep).splitlines()[1]


# ------------------------------------------------------------------ DEF-T002-arch-7
@pytest.mark.parametrize("gen", ["gen_style.py", "gen_gait.py"])
def test_arch7_generators_help_and_required_out(tmp_path, gen):
    g = os.path.join(METRICS, "fixtures", gen)
    before = set(os.listdir(os.path.join(METRICS, "fixtures")))
    r = subprocess.run([sys.executable, g, "--help"], capture_output=True, text=True, cwd=str(tmp_path))
    assert r.returncode == 0 and "usage" in r.stdout.lower()
    r = subprocess.run([sys.executable, g], capture_output=True, text=True, cwd=str(tmp_path))
    assert r.returncode == 2
    assert set(os.listdir(os.path.join(METRICS, "fixtures"))) == before and os.listdir(tmp_path) == []


# ------------------------------------------------------------------ eval-style / eval-feel follow-up
def test_style1_equal_band_means_defined_result(tmp_path):
    """All depth-band means equal (flat, no haze): Spearman undefined -> rho NOT_RUN, span FAIL, no crash."""
    sd = os.path.join(str(tmp_path), "flat")
    GS.m003_gameplay_frames(sd, mode="none", haze=False)
    GS.m003_haze_cal(sd)
    d = os.path.join(sd, "gameplay")
    _, roles, _ = GS.load_profile("slate_teal")
    for k in range(12):
        GS.repaint(d, k, np.full((90, 160), roles.index("ramp_2"), np.int32))
    r = subprocess.run([sys.executable, CLI, "m003", sd, "--profiles", "slate_teal", "--out", str(tmp_path / "o")],
                       capture_output=True, text=True)
    assert r.returncode in (1, 3) and "Traceback" not in r.stderr
    rep = json.load(open(tmp_path / "o" / "report.json"))
    c1 = sm(rep, "M-003.c1")
    rho = next(p for p in c1["parts"] if "rho" in p["name"])
    assert rho["status"] == "NOT_RUN" and c1["status"] == "FAIL"


def test_style2_O_undefined_not_zero(tmp_path):
    """Frames without interleaved windows (flat colour): O is undefined (None), not 0.0."""
    sd, _ = GS.m003_pass_set(str(tmp_path))
    d = os.path.join(sd, "gameplay")
    _, roles, _ = GS.load_profile("slate_teal")
    r = np.arange(90)[:, None] // 12                      # wide flat bands: a gradient but no interleaving
    for k in range(12):
        GS.repaint(d, k, np.broadcast_to(np.array([roles.index(f"ramp_{i}") for i in range(8)])[r], (90, 160)).astype(np.int32))
    rep, _ = run_metric("m003", [sd], images=False, options={"profiles": "slate_teal"})
    from kaiju_metrics.style import measures as M
    from kaiju_metrics.registry import load_registry
    from kaiju_metrics.style.m003 import DITHER_KEYS
    reg = load_registry("M-003")
    idx = np.zeros((40, 40), int); idx[:, 20:] = 1
    dm, _ = M.dither_metrics(idx, np.where(idx == 1, 40.0, 20.0), {k: reg.p(k) for k in DITHER_KEYS})
    assert dm["O"] is None
    o = sm(rep, "M-003.b.O")
    assert o["value"] is None and o["status"] != "REPORTED"


@pytest.mark.parametrize("f,exp", [(1.0, "PASS"), (1.02, "FAIL")])
def test_feel1_estop_floor_like_with_like(tmp_path, f, exp):
    """A stop at exactly the allowed 0.30 g passes M-001.12; one 2 % harder fails."""
    from gaitkit import run_specs
    rep = run_specs(tmp_path, [G.spec_estop(decel=f * 0.30 * G.G)])
    assert sm(rep, "M-001.12")["status"] == exp
    if f > 1:
        assert sm(rep, "M-001.4")["status"] == "FAIL"      # at exactly 0.30 g M-001.4 sits on its boundary


def test_feel2_calibrate_diff_rewrites_source(tmp_path):
    from test_tools import _cal_ratings, _cal_reports
    from kaiju_metrics.calibrate import calibrate
    slips = [0.005, 0.01, 0.015, 0.02, 0.03, 0.035, 0.045, 0.05, 0.06, 0.07]
    rd = _cal_reports(tmp_path, slips)
    rt = _cal_ratings(str(tmp_path / "rt.json"), len(slips), lambda i: slips[i] < 0.04)
    res = calibrate(rt, [rd])
    plus = [l for l in res["registry_diff"].splitlines() if l.startswith("+") and "p95_frac" in l]
    assert plus and "status: calibrated" in plus[0] and "Assumed" not in plus[0] and "rt.json" in plus[0]


# ------------------------------------------------------------------ registry v1.1 adds structure only
V11_DIFF = {"M-001": ({"min_contacts_per_foot"}, set(), {"steady_captures"}),
            "M-002": ({"min_contacts_per_foot"}, set(), set()),
            "M-003": (set(), {"M-003.b.O"}, set()),
            "M-004": ({"crawl_min_interleaved_share", "index_colour_tol_de00"}, set(), set())}


@pytest.mark.parametrize("metric", sorted(V11_DIFF))
def test_v11_keeps_every_threshold_and_v10_still_runs(tmp_path, metric):
    import yaml
    reg = os.path.join(METRICS, "registry")
    a = yaml.safe_load(open(os.path.join(reg, f"{metric}@v1.yaml")))
    b = yaml.safe_load(open(os.path.join(reg, f"{metric}@v1.1.yaml")))
    assert a["revision"] == "v1.0" and b["revision"] == "v1.1"
    for sid, e in a["submeasures"].items():
        assert (e.get("thresholds") or {}) == (b["submeasures"][sid].get("thresholds") or {}), sid
    proc, kinds, top = V11_DIFF[metric]
    assert set(b["procedure"]) - set(a["procedure"]) == proc and set(a["procedure"]) <= set(b["procedure"])
    assert {s for s in a["submeasures"] if a["submeasures"][s]["kind"] != b["submeasures"][s]["kind"]} == kinds
    assert set(b) - set(a) == top
    if metric == "M-002":                     # an old revision still runs and is named in the report
        d = str(tmp_path / "g"); G.write_set(d)
        rep, _ = run_metric("m002", [d], registry=os.path.join(reg, "M-002@v1.yaml"), images=False)
        assert rep["registry"]["revision"] == "v1.0" and rep["verdict"] == "PASS"
        assert rep["registry"]["defaults_for_later_constants"] == {"min_contacts_per_foot": 1}
