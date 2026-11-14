"""C15: standalone validators for gait_log@v1, style_capture@v1, palette_set@v1; good examples pass and
single-fault mutants each give their specific error (file / line or key path / expected / found)."""
import copy
import json
import os
import shutil
import subprocess
import sys
import time

import pytest

from conftest import EXAMPLES, METRICS, PALETTES
from kaiju_metrics.contracts import gait_log, palette_set, style_capture

GAIT_EX = os.path.join(EXAMPLES, "gait_log_example.gait.jsonl")
STYLE_EX = os.path.join(EXAMPLES, "example_set")


def _lines():
    with open(GAIT_EX) as f:
        return [json.loads(l) for l in f]


def _write(tmp_path, recs, name="m.gait.jsonl", raw=None):
    p = tmp_path / name
    if raw is not None:
        p.write_text(raw)
    else:
        p.write_text("".join(json.dumps(r) + "\n" for r in recs))
    return str(p)


def _first_step(recs):
    return next(i for i, r in enumerate(recs) if r["rec"] == "step")


def test_gait_example_valid():
    assert gait_log.validate_file(GAIT_EX) == []


def _m_schema(r): r[0]["schema"] = "gait_log@v0"
def _m_header_missing(r): del r[0]["mass_kg"]
def _m_gap(r):
    i = [k for k, x in enumerate(r) if x["rec"] == "step"][5]
    del r[i]
def _m_time(r): r[[k for k, x in enumerate(r) if x["rec"] == "step"][3]]["t"] += 0.001
def _m_feet_len(r): r[_first_step(r)]["feet"] = r[_first_step(r)]["feet"][:1]
def _m_quat(r): r[_first_step(r)]["root_q"] = [0, 0.5, 0, 0.5]
def _m_cn(r):
    s = r[_first_step(r)]
    k = next(i for i, f in enumerate(s["feet"]) if f["cn"] is not None)
    s["feet"][k]["cn"] = [0, 2.0, 0]
def _m_source_n(r):
    e = next(x for x in r if x.get("type") == "cam_impulse"); e["source_n"] = 99999
def _m_event_order(r):
    i = next(i for i, x in enumerate(r) if x.get("type") == "contact_begin" and i > 50)
    r.insert(_first_step(r) + 1, r.pop(i))
def _m_units_g(r): r[0]["g_mps2"] = 32.2
def _m_dt_ms(r): r[0]["fixed_dt_s"] = 20.0
def _m_yaw_deg(r):
    s = r[_first_step(r)]; s["yaw"] = 33.0
def _m_neg_fn(r):
    s = r[_first_step(r)]; s["feet"][0]["fn"] = -5.0
def _m_contact_flag(r):
    s = r[_first_step(r)]; s["feet"][0]["contact"] = 2
def _m_two_headers(r): r.insert(3, copy.deepcopy(r[0]))
def _m_vel_units(r):
    for x in r:
        if x["rec"] == "step":
            x["com_v"] = [v * 3.6 for v in x["com_v"]]
def _m_thresholds(r): r[0]["thresholds"] = {"F_on_N": 1.0}
def _m_sole_points(r): r[0]["feet"][0]["sole_points_local"] = r[0]["feet"][0]["sole_points_local"][:4]


GAIT_MUTANTS = [
    (_m_schema, "header.schema", "wrong schema string"),
    (_m_header_missing, "header.mass_kg", "missing or not a finite number"),
    (_m_gap, "step.n", "not contiguous"),
    (_m_time, "step.t", "n * fixed_dt_s"),
    (_m_feet_len, "step.feet", "feet array length"),
    (_m_quat, "step.root_q", "not unit length"),
    (_m_cn, ".cn", "contact normal not unit length"),
    (_m_source_n, "cam_impulse.source_n", "does not reference a contact_begin"),
    (_m_event_order, "event.n", "must follow the step record"),
    (_m_units_g, "header.g_mps2", "9.81"),
    (_m_dt_ms, "header.physics_hz", "physics_hz * fixed_dt_s"),
    (_m_yaw_deg, "step.yaw", "radians"),
    (_m_neg_fn, ".fn", ">= 0"),
    (_m_contact_flag, ".contact", "0 or 1"),
    (_m_two_headers, "line 4", "more than one header"),
    (_m_vel_units, "com_v", "time derivative"),
    (_m_thresholds, "header.thresholds", "F_on_N and F_off_N"),
    (_m_sole_points, "sole_points_local", "five"),
]


@pytest.mark.parametrize("mut,where,msg", GAIT_MUTANTS, ids=[m[0].__name__ for m in GAIT_MUTANTS])
def test_gait_mutant_specific_error(tmp_path, mut, where, msg):
    recs = _lines()
    mut(recs)
    errs = gait_log.validate_file(_write(tmp_path, recs))
    assert errs, "mutant must be rejected"
    assert any(where in e.where and msg in (e.message + " " + e.expected) for e in errs), [str(e) for e in errs[:5]]


def test_gait_truncated_and_nan(tmp_path):
    raw = open(GAIT_EX).read()
    errs = gait_log.validate_file(_write(tmp_path, None, raw=raw[: len(raw) // 2]))
    assert any("not a valid JSON record" in e.message for e in errs)
    recs = _lines()
    i = _first_step(recs)
    txt = "".join(json.dumps(r) + "\n" for r in recs).replace(json.dumps(recs[i]["yaw"]), "NaN", 1)
    errs = gait_log.validate_file(_write(tmp_path, None, name="nan.gait.jsonl", raw=txt))
    assert errs


def test_gait_float32_positions_rejected(tmp_path):
    sys.path.insert(0, METRICS)
    from fixtures.gen_gait import build, spec_walk, write
    sp = spec_walk(0.2, strides=1)
    sp.duration_s = 4.0
    sp.float32_positions = True
    recs, _ = build(sp)
    errs = gait_log.validate_file(write(recs, str(tmp_path / "f32.gait.jsonl")))
    assert any("float32" in e.message for e in errs)


def test_gait_all_errors_listed(tmp_path):
    recs = _lines()
    _m_schema(recs); _m_quat(recs); _m_source_n(recs)
    errs = gait_log.validate_file(_write(tmp_path, recs))
    assert len(errs) >= 3


def test_gait_validate_20_strides_under_5s(tmp_path):
    from fixtures.gen_gait import build, spec_walk, write
    recs, _ = build(spec_walk(0.2, strides=20))
    p = write(recs, str(tmp_path / "w20.gait.jsonl"))
    t0 = time.time()
    assert gait_log.validate_file(p) == []
    assert time.time() - t0 < 5.0


# ------------------------------------------------------------------ palette_set
def test_palette_set_research_file_valid():
    assert palette_set.validate_file(PALETTES) == []


def _pal_mutant(tmp_path, fn):
    d = json.load(open(PALETTES))
    fn(d)
    p = tmp_path / "pal.json"
    p.write_text(json.dumps(d))
    return palette_set.validate_file(str(p))


@pytest.mark.parametrize("name,fn,msg", [
    ("schema", lambda d: d.update(schema="palette_set@v2"), "wrong schema"),
    ("hex", lambda d: d["profiles"]["slate_teal"]["colours"][0].update(hex="#ZZ0000"), "lowercase #rrggbb"),
    ("lab", lambda d: d["profiles"]["slate_teal"]["colours"][1].update(lab=[90.0, 0.0, 0.0]), "Lab inconsistent"),
    ("ramp", lambda d: d["profiles"]["slate_teal"]["colours"][2].update(role="body"), "ramp_0..ramp_7"),
    ("size", lambda d: d["profiles"]["slate_teal"].update(size=99), "size must equal"),
    ("share", lambda d: d["panels"]["ref_01A"]["palette"][0].update(share=0.5), "sum to 1"),
])
def test_palette_mutants(tmp_path, name, fn, msg):
    errs = _pal_mutant(tmp_path, fn)
    assert any(msg in e.message for e in errs), [str(e) for e in errs[:3]]


def test_canonical_palette_sha():
    import hashlib
    d = json.load(open(PALETTES))
    hexes = [c["hex"] for c in d["profiles"]["slate_teal"]["colours"]]
    s = "[" + ",".join('"' + h + '"' for h in hexes) + "]"     # hand-built canonical form
    assert palette_set.canonical_sha256(hexes) == hashlib.sha256(s.encode()).hexdigest()


# ------------------------------------------------------------------ style_capture
def test_style_example_valid(palettes):
    assert style_capture.validate_path(STYLE_EX, palettes) == []


def _style_copy(tmp_path):
    d = tmp_path / "example_set"
    shutil.copytree(STYLE_EX, d)
    return str(d)


def _edit_sidecar(d, seq, idx, fn):
    p = os.path.join(d, seq, f"f{idx:06d}.json")
    sc = json.load(open(p)); fn(sc); json.dump(sc, open(p, "w"))


def _style_mut_sha(d): _edit_sidecar(d, "gameplay", 0, lambda s: s["style"].update(palette_sha256="0" * 64))
def _style_mut_res(d): _edit_sidecar(d, "gameplay", 1, lambda s: s["resolution"].update(internal_wh=[100, 54]))
def _style_mut_missing_file(d): os.remove(os.path.join(d, "gameplay", "f000001_depth.png"))
def _style_mut_bitdepth(d):
    from PIL import Image
    import numpy as np
    Image.fromarray(np.zeros((54, 96), np.uint8)).save(os.path.join(d, "gameplay", "f000000_depth.png"))
def _style_mut_taa(d):
    _edit_sidecar(d, "gameplay", 0, lambda s: s["build"]["feature_flags"].update({"Rendering.TAA": True}))
def _style_mut_index_range(d):
    from PIL import Image
    import numpy as np
    a = np.array(Image.open(os.path.join(d, "gameplay", "f000000_index.png"))); a[0, 0] = 200
    Image.fromarray(a).save(os.path.join(d, "gameplay", "f000000_index.png"))
def _style_mut_frame_gap(d):
    p = os.path.join(d, "yaw_0p25", "sequence.json")
    m = json.load(open(p)); m["frames"] = ["f000000.json", "f000002.json"]; m["frame_count"] = 2
    json.dump(m, open(p, "w"))
    os.remove(os.path.join(d, "yaw_0p25", "f000001.json"))
def _style_mut_motion(d):
    _edit_sidecar(d, "yaw_0p25", 2, lambda s: s["motion"].update(speed_internal_px_per_frame=0.5))
def _style_mut_profile(d): _edit_sidecar(d, "gameplay", 0, lambda s: s["style"].update(palette_profile_id="neon"))
def _style_mut_alpha(d):
    from PIL import Image
    import numpy as np
    Image.fromarray(np.zeros((54, 96, 4), np.uint8)).save(os.path.join(d, "gameplay", "f000000_color_internal.png"))
def _style_mut_haze_targets(d): _edit_sidecar(d, "haze_cal", 0, lambda s: s.pop("haze_targets"))
def _style_mut_schema(d): _edit_sidecar(d, "gameplay", 1, lambda s: s.update(schema="style_capture@v0"))
def _style_mut_name(d):
    _edit_sidecar(d, "gameplay", 1, lambda s: s["files"].update(color_output="f000000_color_output.png"))
def _style_mut_quat(d): _edit_sidecar(d, "yaw_0p25", 0, lambda s: s["camera"].update(rotation_xyzw=[0, 0, 0, 2]))


STYLE_MUTANTS = [
    (_style_mut_sha, "style.palette_sha256", "canonical hash"),
    (_style_mut_res, "resolution", "output must be internal x integer_scale"),
    (_style_mut_missing_file, "files.depth", "file not found"),
    (_style_mut_bitdepth, "files.depth", "wrong PNG format"),
    (_style_mut_taa, "feature_flags", "forbidden"),
    (_style_mut_index_range, "files.palette_index", "outside the profile"),
    (_style_mut_frame_gap, "frames", "no gaps"),
    (_style_mut_motion, "motion", "differs from sequence.json"),
    (_style_mut_profile, "palette_profile_id", "unknown profile"),
    (_style_mut_alpha, "files.color_internal", "wrong PNG format"),
    (_style_mut_haze_targets, "haze_targets", "dark/light target pairs"),
    (_style_mut_schema, ".schema", "wrong schema string"),
    (_style_mut_name, "files.color_output", "frame index"),
    (_style_mut_quat, "rotation_xyzw", "not unit length"),
]


@pytest.mark.parametrize("mut,where,msg", STYLE_MUTANTS, ids=[m[0].__name__ for m in STYLE_MUTANTS])
def test_style_mutant_specific_error(tmp_path, palettes, mut, where, msg):
    d = _style_copy(tmp_path)
    mut(d)
    errs = style_capture.validate_path(d, palettes)
    assert errs, "mutant must be rejected"
    assert any(where in e.where and msg in e.message for e in errs), [str(e) for e in errs[:5]]


def test_validate_cli_exit_codes(tmp_path):
    py = sys.executable
    cli = os.path.join(METRICS, "metrics.py")
    ok = subprocess.run([py, cli, "validate", "gait_log", GAIT_EX], capture_output=True, text=True)
    assert ok.returncode == 0 and "VALID" in ok.stdout
    recs = _lines(); _m_gap(recs)
    bad = subprocess.run([py, cli, "validate", "gait_log", _write(tmp_path, recs), "--json"], capture_output=True,
                         text=True)
    assert bad.returncode == 2
    out = json.loads(bad.stdout)
    assert out["valid"] is False and out["errors"][0]["where"].startswith("line")
    ok = subprocess.run([py, cli, "validate", "style_capture", STYLE_EX], capture_output=True, text=True)
    assert ok.returncode == 0, ok.stdout + ok.stderr
    ok = subprocess.run([py, cli, "validate", "palette_set", PALETTES], capture_output=True, text=True)
    assert ok.returncode == 0
    assert "Traceback" not in bad.stderr
