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
    c1 = [p for p in sm(rep, "M-003.c1")["parts"] if "warm_amber_haze" in p["name"]]
    assert c1 and c1[0]["status"] == "NOT_RUN"
    assert rep["verdict"] != "PASS"


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
    """C_px on a sequence with no interleaved pixel is not a silent 0: NOT_APPLICABLE part plus a warning."""
    sd, _ = GS.m004_set(str(tmp_path))
    rep, _ = run_metric("m004", [sd], images=False, options={"profiles": "slate_teal", "viewpoints": "eye",
                                                              "fixture": True})
    y = sm(rep, "M-004.C_px.Y")
    assert all(p["status"] == "NOT_APPLICABLE" for p in y["parts"])
    assert any("no dithered" in w for w in rep["warnings"])
    assert sm(rep, "M-004.C_px.T3")["status"] == "PASS"


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
