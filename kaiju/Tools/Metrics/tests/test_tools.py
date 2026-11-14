"""C11 rescore, C13 determinism, C16 calibration helper, C17 baseline subset, C18 CLI behaviour."""
import hashlib
import json
import os
import shutil
import subprocess
import sys

import pytest
import yaml

from conftest import METRICS, PROJECT
from fixtures import gen_gait as G
from fixtures import gen_style as GS
from kaiju_metrics.common import dump_json
from kaiju_metrics.report import strip_run
from kaiju_metrics.runner import run_metric

CLI = os.path.join(METRICS, "metrics.py")
PY = sys.executable


def cli(*args, env=None, cwd=None):
    return subprocess.run([PY, CLI, *args], capture_output=True, text=True, env=env, cwd=cwd or PROJECT)


# ------------------------------------------------------------------ rescore (C11)
@pytest.fixture()
def m002_report(tmp_path):
    d = str(tmp_path / "gait")
    G.write_set(d, over={"walk_fr020": G.spec_walk(0.2, strides=5, slips={(0, 2): (0.02, 0.0)})})
    out = str(tmp_path / "rep")
    run_metric("m002", [d], out_dir=out, images=False)
    return os.path.join(out, "report.json"), d


def test_rescore_reproduces(m002_report, tmp_path):
    from kaiju_metrics.rescore import rescore
    rp, _ = m002_report
    old, new, diffs, identical, same = rescore(rp, out_dir=str(tmp_path / "re"))
    assert diffs == [] and identical and same
    r = cli("rescore", rp)
    assert r.returncode == 0 and "REPRODUCED" in r.stdout


def test_rescore_refuses_other_registry_unless_asked(m002_report, tmp_path):
    rp, _ = m002_report
    from kaiju_metrics.registry import load_registry
    d = yaml.safe_load(open(load_registry("M-002").path))
    d["revision"] = "v1.1"
    d["submeasures"]["M-002.1"]["thresholds"]["max_frac"]["value"] = 0.0005
    p = tmp_path / "M-002@v1.1.yaml"
    yaml.safe_dump(d, open(p, "w"), sort_keys=False)
    r = cli("rescore", rp, "--registry", str(p))
    assert r.returncode == 2 and "allow-different-registry" in r.stderr
    r = cli("rescore", rp, "--registry", str(p), "--allow-different-registry", "--out", str(tmp_path / "o"))
    assert r.returncode == 1 and "DIFFERS" in r.stdout and "verdict PASS -> FAIL" in r.stdout
    new = json.load(open(tmp_path / "o" / "report.json"))
    assert new["registry"]["revision"] == "v1.1" and new["rescored_from"]["registry"]["revision"] == "v1.0"


def test_rescore_detects_changed_input(m002_report):
    rp, d = m002_report
    with open(os.path.join(d, "stomp.gait.jsonl"), "a") as f:
        f.write("\n")
    r = cli("rescore", rp)
    assert r.returncode == 2 and "input changed" in r.stderr


# ------------------------------------------------------------------ determinism (C13)
def _stripped(path):
    return dump_json(strip_run(json.load(open(path))))


def test_determinism_gait_reports_and_env(tmp_path):
    d = str(tmp_path / "g")
    G.write_set(d)
    env1 = dict(os.environ, OMP_NUM_THREADS="1", LC_ALL="C", LANG="C")
    env2 = dict(os.environ, OMP_NUM_THREADS="8", LC_ALL="de_DE.UTF-8", LANG="de_DE.UTF-8")
    env1.pop("DISPLAY", None); env2.pop("DISPLAY", None)
    for cmd in ("m001", "m002"):
        a = cli(cmd, d, "--out", str(tmp_path / f"{cmd}a"), env=env1)
        b = cli(cmd, d, "--out", str(tmp_path / f"{cmd}b"), env=env2)
        assert a.returncode == b.returncode and a.returncode in (0, 3)
        assert _stripped(tmp_path / f"{cmd}a" / "report.json") == _stripped(tmp_path / f"{cmd}b" / "report.json")


def test_determinism_style_reports(tmp_path):
    sd, _ = GS.m003_pass_set(str(tmp_path))
    a, _ = run_metric("m003", [sd], images=False, options={"profiles": "slate_teal"})
    b, _ = run_metric("m003", [sd], images=False, options={"profiles": "slate_teal"})
    assert dump_json(strip_run(a)) == dump_json(strip_run(b))


def _tree_hash(d):
    h = hashlib.sha256()
    for root, _, files in sorted(os.walk(d)):
        for f in sorted(files):
            p = os.path.join(root, f)
            h.update(os.path.relpath(p, d).encode()); h.update(open(p, "rb").read())
    return h.hexdigest()


def test_fixtures_regenerate_bit_identically(tmp_path):
    G.write_set(str(tmp_path / "a")); G.write_set(str(tmp_path / "b"))
    assert _tree_hash(tmp_path / "a") == _tree_hash(tmp_path / "b")
    GS.m003_pass_set(str(tmp_path / "s1")); GS.m003_pass_set(str(tmp_path / "s2"))
    GS.m004_set(str(tmp_path / "s1"), static_noise=True); GS.m004_set(str(tmp_path / "s2"), static_noise=True)
    assert _tree_hash(tmp_path / "s1") == _tree_hash(tmp_path / "s2")


# ------------------------------------------------------------------ calibration (C16)
def _cal_reports(tmp_path, slips):
    reps = []
    for i, s in enumerate(slips):
        d = str(tmp_path / f"cap{i}")
        sl = {(f, k): (s, 0.0) for f in range(2) for k in range(12)}
        G.write_set(d, over={"walk_fr020": G.spec_walk(0.2, strides=5, slips=sl)}, names=["walk_fr020"])
        out = str(tmp_path / "reports" / f"cap{i}")
        run_metric("m002", [d], out_dir=out, label=f"cap{i}", images=False)
        reps.append(out)
    return str(tmp_path / "reports")


def _cal_ratings(path, n, good, neg=2):
    raters = [{"rater_id": f"f{i}", "rater_type": "human", "blind": True} for i in range(2)]
    rows = []
    for r in ("f0", "f1"):
        for i in range(n):
            rows.append({"rater_id": r, "build": "cal", "capture": f"cap{i}", "score": 9 if good(i) else 5})
        rows.append({"rater_id": r, "build": "cal", "capture": "posA", "anchor": "positive", "score": 9})
        rows.append({"rater_id": r, "build": "cal", "capture": "negA", "anchor": "negative", "score": neg})
    json.dump({"schema": "panel_ratings@v1", "metric": "M-002", "raters": raters, "ratings": rows}, open(path, "w"))
    return path


def test_calibrate_recovers_known_cut(tmp_path):
    """Synthetic ratings: captures with uniform slip s_i are rated good iff s_i < 0.04 m. The proposed M-002.2
    cut (p95 slip / L_f) must fall between the last good and the first bad capture."""
    from kaiju_metrics.calibrate import calibrate
    slips = [0.005, 0.01, 0.015, 0.02, 0.03, 0.035, 0.045, 0.05, 0.06, 0.07]
    rd = _cal_reports(tmp_path, slips)
    rt = _cal_ratings(str(tmp_path / "rt.json"), len(slips), lambda i: slips[i] < 0.04)
    out = str(tmp_path / "cal")
    res = calibrate(rt, [rd], out_dir=out)
    p = next(p for p in res["proposals"] if p["submeasure"] == "M-002.2")
    L = G.REF["foot_length_m"]
    assert 0.035 / L < p["proposed"] < 0.045 / L and p["false_pass"] == 0 and p["false_fail"] == 0
    assert "revision: v1.1" in res["registry_diff"] and "calibrated" in res["registry_diff"]
    assert os.path.isfile(os.path.join(out, "calibration_value_vs_rating.png"))
    from kaiju_metrics.registry import load_registry
    assert load_registry("M-002").revision == "v1.0"           # never writes the registry
    r = cli("calibrate", "--ratings", rt, "--reports", rd)
    assert r.returncode == 0 and "proposed" in r.stdout


def test_calibrate_refuses_few_captures_and_bad_anchors(tmp_path):
    slips = [0.005, 0.02, 0.05]
    rd = _cal_reports(tmp_path, slips)
    rt = _cal_ratings(str(tmp_path / "rt.json"), 3, lambda i: i == 0)
    r = cli("calibrate", "--ratings", rt, "--reports", rd)
    assert r.returncode == 2 and ">= 8" in r.stderr
    rt2 = _cal_ratings(str(tmp_path / "rt2.json"), 3, lambda i: i == 0, neg=7)
    r = cli("calibrate", "--ratings", rt2, "--reports", rd)
    assert r.returncode == 2 and "misbehaving anchors" in r.stderr
    r = cli("calibrate", "--ratings", rt, "--reports", rd, "--out", os.path.join(METRICS, "registry", "x"))
    assert r.returncode == 2 and "never writes" in r.stderr


# ------------------------------------------------------------------ baseline subset (C17)
def test_baseline_panels_reproduce_R002():
    from kaiju_metrics import baseline as B
    from kaiju_metrics.common import load_json_file
    from kaiju_metrics.registry import load_registry
    pj = load_json_file(B.PANELS_JSON)["panels"]
    pal = load_json_file(B.PALETTES_JSON)
    val = json.load(open(B.VALIDATION))["references"]
    reg = load_registry("M-003")
    for pid in ("ref_03D", "ref_01B"):
        m = B.panel_measures(pid, pj[pid], pal, reg)
        assert abs(m["dither"]["DIG"] - val[pid]["dither"]["DIG"]) <= 0.03
        assert abs(m["dither"]["O"] - val[pid]["dither"]["O"]) <= 0.03
        assert m["value_levels"] == val[pid]["value_levels"] and m["hue_families"] == val[pid]["hue_families"]
        assert abs(m["raw_vs_own_palette"]["2"] - pj[pid]["adherence_raw"]["2"]) <= 0.03


# ------------------------------------------------------------------ CLI (C18)
@pytest.mark.parametrize("cmd", ["m001", "m002", "m003", "m004", "validate", "calibrate", "rescore", "list-metrics",
                                 "baseline"])
def test_help_shows_inputs_and_exit_codes(cmd):
    r = cli(cmd, "--help")
    assert r.returncode == 0 and "usage" in r.stdout.lower()
    if cmd not in ("list-metrics", "baseline"):
        assert "INVALID_INPUT" in r.stdout and "INCOMPLETE" in r.stdout


def test_top_help_and_list_metrics():
    r = cli("--help")
    assert r.returncode == 0 and "exit codes" in r.stdout
    r = cli("list-metrics")
    assert r.returncode == 0 and "M-004@v1" in r.stdout and "M-001.13" in r.stdout


def test_bad_input_clear_message_no_traceback(tmp_path):
    r = cli("m002", str(tmp_path / "missing.gait.jsonl"), "--out", str(tmp_path / "o"))
    assert r.returncode == 2 and "Traceback" not in r.stdout + r.stderr and "file not found" in r.stdout
    r = cli("m003", str(tmp_path), "--out", str(tmp_path / "o3"))
    assert r.returncode == 2 and "Traceback" not in r.stdout + r.stderr
    r = cli("nosuchcommand")
    assert r.returncode == 2


def test_exit_code_table(tmp_path):
    """Live exit codes: PASS 0, FAIL 1, INVALID_INPUT 2, INCOMPLETE 3."""
    good = str(tmp_path / "good"); G.write_set(good)
    bad = str(tmp_path / "bad")
    G.write_set(bad, over={"walk_fr020": G.spec_walk(0.2, strides=5, slips={(0, 2): (0.2, 0.0)})})
    part = str(tmp_path / "part"); G.write_set(part, names=["walk_fr020"])
    broken = tmp_path / "broken"; broken.mkdir(); (broken / "x.gait.jsonl").write_text("{\"rec\":\"step\"}\n")
    codes = {n: cli("m002", d, "--out", str(tmp_path / f"o_{n}"), "--quiet").returncode
             for n, d in (("pass", good), ("fail", bad), ("invalid", str(broken)), ("incomplete", part))}
    assert codes == {"pass": 0, "fail": 1, "invalid": 2, "incomplete": 3}


def test_package_import_has_no_side_effects(tmp_path):
    r = subprocess.run([PY, "-c", "import sys; sys.path.insert(0, %r); import kaiju_metrics, kaiju_metrics.cli; "
                        "print('matplotlib' in sys.modules)" % METRICS], capture_output=True, text=True, cwd=str(tmp_path))
    assert r.returncode == 0 and r.stdout.strip() == "False" and os.listdir(tmp_path) == []
