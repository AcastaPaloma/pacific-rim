"""C1 coverage, C10 thresholds only in the registry (flip tests, literal scan), C11 versions coexist, draft rule."""
import io
import os
import shutil
import tokenize

import pytest
import yaml

from conftest import METRICS
from fixtures import gen_gait as G
from fixtures import gen_style as GS
from gaitkit import m001_ratings, run_full
from kaiju_metrics.registry import Registry, default_registry, list_metrics, load_registry, registry_files
from kaiju_metrics.runner import run_metric

SPEC_IDS = {
    "M-001": [f"M-001.{i}" for i in range(1, 14)],
    "M-002": [f"M-002.{i}" for i in range(1, 6)],
    "M-003": ["M-003.a.output", "M-003.a.internal", "M-003.a.A3", "M-003.a.A5", "M-003.b.DIG", "M-003.b.DIG_max",
              "M-003.b.O", "M-003.c1", "M-003.c2", "M-003.d-i", "M-003.d-ii", "M-003.d-iii", "M-003.d-iv",
              "M-003.e.sky", "M-003.e.horizon", "M-003.e.contrast", "M-003.f"],
    "M-004": ["M-004.S0", "M-004.C_px.S0", "M-004.F.S0"] + [f"M-004.{m}.{g}" for g in ("Y", "T12", "T3")
                                                            for m in ("S_px", "C_px", "F")],
}
ADVISORY = {"M-002.2", "M-003.b.DIG_max", "M-003.b.O", "M-003.d-iii", "M-003.e.sky", "M-003.e.horizon", "M-003.e.contrast"}
INFO = {"M-003.a.A3", "M-003.a.A5"}


@pytest.mark.parametrize("metric", sorted(SPEC_IDS))
def test_registry_covers_spec(metric):
    reg = load_registry(metric)
    assert sorted(reg.ids()) == sorted(SPEC_IDS[metric])
    for s in reg.ids():
        exp = "advisory" if s in ADVISORY else "info" if s in INFO else "gating"
        assert reg.kind(s) == exp, s


@pytest.mark.parametrize("metric,cmd", [("M-001", "m001"), ("M-002", "m002"), ("M-003", "m003"), ("M-004", "m004")])
def test_report_ids_equal_spec_even_on_invalid_input(tmp_path, metric, cmd):
    bad = tmp_path / "x.gait.jsonl"
    bad.write_text("{}\n")
    rep, _ = run_metric(cmd, [str(bad) if cmd in ("m001", "m002") else str(tmp_path)], images=False)
    assert [s["id"] for s in rep["submeasures"]] == load_registry(metric).ids()
    assert rep["verdict"] == "INVALID_INPUT" and rep["exit_code"] == 2


def test_every_threshold_has_value_unit_status_source():
    for _, _, _, p in registry_files():
        d = yaml.safe_load(open(p))
        for sid, e in d["submeasures"].items():
            for k, t in (e.get("thresholds") or {}).items():
                assert set(t) >= {"value", "unit", "status", "source"}, (p, sid, k)
                assert t["status"] in ("derived", "assumed_pending_calibration", "calibrated")
                assert isinstance(t["value"], (int, float)), (p, sid, k)


# ------------------------------------------------------------------ flip tests (C10)
def _keys(metric):
    reg = load_registry(metric)
    return [(s, k) for s in reg.ids() if reg.kind(s) != "info" for k in (reg.sub(s).get("thresholds") or {})]


def _flipped_registry(tmp_path, metric, sid, key, factor):
    reg = load_registry(metric)
    d = yaml.safe_load(open(reg.path))
    v = d["submeasures"][sid]["thresholds"][key]["value"]
    d["submeasures"][sid]["thresholds"][key]["value"] = (v * factor) if v != 0 else (-1.0 if factor < 1 else 1.0)
    p = tmp_path / f"{metric}@v1.yaml"
    yaml.safe_dump(d, open(p, "w"), sort_keys=False)
    return str(p)


@pytest.fixture(scope="module")
def gait_sets(tmp_path_factory):
    root = tmp_path_factory.mktemp("gaitflip")
    d = str(root / "set")
    G.write_set(d)
    r = m001_ratings(str(root / "r.json"), "set")
    return d, r


@pytest.fixture(scope="module")
def style_sets(tmp_path_factory):
    root = tmp_path_factory.mktemp("styleflip")
    sd3, _ = GS.m003_pass_set(str(root))
    sd3a, _ = GS.m003_pass_set(str(root), set_id="m003_amber")
    GS.m003_gameplay_frames(sd3a, profile="warm_amber_haze", seq_id="gameplay_amber")   # brings an accent
    _, roles, _ = GS.load_profile("warm_amber_haze")
    da = os.path.join(sd3a, "gameplay_amber")
    for k in range(12):                                   # 2 % accent pixels so d-iii applies
        idx = GS.read_index(da, k)
        idx.reshape(-1)[:288] = roles.index("accent")
        GS.repaint(da, k, idx)
    sd4, _ = GS.m004_set(str(root))
    sd4s, _ = GS.m004_set(str(root), set_id="m004_screen", anchor_t="screen", anchor_y="screen")
    return sd3, sd3a, sd4, sd4s


_BASE = {}


def _status_map(rep):
    return {s["id"]: s["status"] for s in rep["submeasures"]}


@pytest.mark.parametrize("sid,key", _keys("M-001") + _keys("M-002"))
def test_registry_flip_gait(tmp_path, gait_sets, sid, key):
    d, r = gait_sets
    metric, cmd = sid[:5], "m00" + sid[4]
    base = _BASE.get(cmd)
    if base is None:
        base = _BASE[cmd] = run_metric(cmd, [d], ratings=r, label="set", images=False)[0]
    assert _status_map(base)[sid] == "PASS"
    changed = []
    for f in (10.0, 0.1, -1.0, -10.0, 100.0):
        p = _flipped_registry(tmp_path, metric, sid, key, f)
        rep, _ = run_metric(cmd, [d], registry=p, ratings=r, label="set", images=False)
        if _status_map(rep)[sid] != "PASS":
            changed.append(f)
            if load_registry(metric).kind(sid) == "gating":
                assert rep["verdict"] in ("FAIL", "INCOMPLETE") and rep["verdict"] != base["verdict"]
            break
    assert changed, f"moving {sid}.{key} never changed the verdict"


@pytest.mark.parametrize("sid,key", _keys("M-003") + _keys("M-004"))
def test_registry_flip_style(tmp_path, style_sets, sid, key):
    sd3, sd3a, sd4, sd4s = style_sets
    metric = sid[:5]
    if sid == "M-004.C_px.Y":     # the passing yaw set has no dithered pixels (C_px N/A): flip FAIL -> PASS instead
        sd4 = sd4s
    if sid == "M-003.d-iii":
        sd3, prof = sd3a, "slate_teal,warm_amber_haze"
    else:
        prof = "slate_teal"
    cmd, sd, opts = ("m003", sd3, {"profiles": prof}) if metric == "M-003" else \
        ("m004", sd4, {"profiles": "slate_teal", "viewpoints": "eye", "fixture": True})
    ratings = None
    if sid == "M-003.f":
        from stylekit import m003_ratings
        ratings = m003_ratings(str(tmp_path / "r.json"), os.path.basename(sd3))
    kb = (cmd, sd, ratings is not None)
    base = _BASE.get(kb)
    if base is None:
        base = _BASE[kb] = run_metric(cmd, [sd], ratings=ratings, images=False, options=opts)[0]
    st0 = _status_map(base)[sid]
    assert st0 in ("PASS", "FAIL"), f"{sid} is {st0} on its base set"
    changed = []
    for f in (10.0, 0.1, -1.0, -10.0, 100.0):
        p = _flipped_registry(tmp_path, metric, sid, key, f)
        rep, _ = run_metric(cmd, [sd], registry=p, ratings=ratings, images=False, options=opts)
        if _status_map(rep)[sid] != st0:
            changed.append(f)
            break
    assert changed, f"moving {sid}.{key} never changed its status"


# ------------------------------------------------------------------ literal scan
ALLOW = {
    ("style/m003.py", "1e-6"): "M-003 distance matching tolerance in metres; coincides with the M-052 relative threshold",
    # (file, literal): why a number equal to a registry value is legitimate there
    ("baseline.py", "0.08"): "R-002 §2 grid-detection R threshold (research procedure, not an M-003 threshold)",
    ("baseline.py", "0.05"): "R-002 §2 weak-grid R threshold (research procedure)",
    ("baseline.py", "0.03"): "comparison tolerance against R-002 tables (not a metric threshold)",
    ("baseline.py", "0.02"): "comparison tolerance against R-002 tables (not a metric threshold)",
    ("baseline.py", "0.85"): "R-002 §2 grid detection: largest period within 85 % of max R",
    ("baseline.py", "0.99"): "R-002 §2 de-noised mask: area-resampled coverage > 0.99",
    ("baseline.py", "12.0"): "R-002 §2 grid period search range 2..12 px",
    ("baseline.py", "8.0"): "R-002 §2 weak grid period <= 8 px",
    ("baseline.py", "0.1"): "R-002 §2 grid x/y period agreement < 10 %",
    ("calibrate.py", "8.0"): "default pass rating (CLI --pass-score), not a metric threshold",
    ("contracts/gait_log.py", "0.05"): "unit-sanity tolerance on com_v vs d(com_p)/dt (validator, not a metric)",
    ("contracts/gait_log.py", "0.01"): "unit-sanity floor (m/s) and yaw-rate consistency floor (validator)",
    ("contracts/gait_log.py", "16.0"): "float32 detector: ignore |x| < 16 m",
    ("contracts/gait_log.py", "1.0"): "fixed_dt plausibility (seconds, not ms)",
    ("contracts/gait_log.py", "9.81"): "contract check: header g must be 9.81 (physics truth)",
    ("contracts/palette_set.py", "1.0"): "Lab-vs-hex tolerance (8-bit rounding <= 0.88 dE00)",
    ("contracts/palette_set.py", "0.01"): "palette shares must sum to 1 +- 0.01",
    ("contracts/style_capture.py", "0.01"): "vfov consistency 1 % (validator)",
    ("cli.py", "8.0"): "calibrate --pass-score default rating, not a metric threshold",
    ("baseline.py", "0.01"): "R-002 §2 grid period search step (px)",
    ("contracts/gait_log.py", "1e-3"): "unit-quaternion / unit-normal tolerance (validator)",
    ("contracts/style_capture.py", "1e-3"): "unit-quaternion tolerance (validator)",
}
ALLOW.update({
    # fix cycle 2 (DEF-T002-arch-5): every registry value is scanned, including 0.0 / 0.5 / 2.0 / 4.0
    ("baseline.py", "2.0"): "R-002 §2 grid period search lower bound (px)",
    ("baseline.py", "0.5"): "mean of the x / y grid periods (arithmetic), and pixel centre",
    ("calibrate.py", "0.0"): "zero rating SD for a single rater",
    ("contracts/gait_log.py", "0.5"): "unused-literal guard: half-ulp statistic of the float32 detector (validator)",
    ("contracts/palette_set.py", "0.0"): "share accumulator start",
    ("style/m003.py", "0.0"): "accumulators and the signed c2 comparison 'dL(8 km) - 0.5 dL(200 m) <= 0'",
    ("style/measures.py", "4.0"): "neighbour count normalisation (4 neighbours), not d-iii / c1 thresholds",
    ("style/measures.py", "0.5"): "pixel-presence test (count > 0.5 of one pixel) and pixel centres",
    ("style/measures.py", "0.0"): "max(0, drop) and empty-array defaults",
    ("style/m004.py", "0.5"): "pixel centre (i + 0.5) in back-projection",
    ("style/m004.py", "0.0"): "valid-share default for a sequence without pairs",
    ("style/m004.py", "2.0"): "fallback of index_colour_tol_de00 when re-scoring a v1.0 report (constant added in v1.1)",
    ("style/m004.py", "0.01"): "fallback of crawl_min_interleaved_share when re-scoring a v1.0 report (added in v1.1)",
    ("gait/m002.py", "0.0"): "slip accumulator start",
    ("gait/inputs.py", "0.0"): "tolerance default when the registry has no reference unit",
    ("gait/m001.py", "0.0"): "power-limit comparison 'a_fore - limit <= 0'",
    ("style/m004.py", "12"): "frame-cache window (frames kept in memory), not d-i levels_max",
    ("baseline.py", "1.0"): "comparison tolerance (1 L*) against R-002's contrast column",
    ("style/m003.py", "1.0"): "heat-map scale for a boolean block map",
    ("style/m004.py", "1.0"): "unit depth for sky rays, k = ceil(1 / speed), 'moves >= 1 px', heat-map scale",
    ("gait/m002.py", "1.0"): "world up unit vector [0, 1, 0]",
    ("gait/m001.py", "1.0"): "division guard where v = 0",
})
ALLOW_FILES = {
    "colour.py": "sRGB / CIELAB / CIEDE2000 formula constants (2.4 gamma, 6/29, 25^7, 0.20 ... of the standards)",
    "diagnostics.py": "plot colours and styling only",
    "png.py": "PNG byte layout (signature 8 bytes, chunk header 12 bytes, bit depths 8 / 16)",
    "contracts/style_capture.py": "file format table (bit depths 8 / 16) and validator tolerances",
    "contracts/palette_set.py": "hex / sRGB 8-bit layout",
    "contracts/gait_log.py": "validator tolerances (quaternion, units) and float32 detector",
    "calibrate.py": "reconciled C16 rules (>= 8 captures, misbehaving negative anchor >= 6), not metric thresholds",
    "report.py": "summary layout (<= 40 lines, 30 sub-measure lines) and number formatting",
}


def _registry_values():
    """Float values of every threshold and float procedure constant, plus integer thresholds >= 5."""
    vals = set()
    for _, _, _, p in registry_files():
        d = yaml.safe_load(open(p))
        for e in d["submeasures"].values():
            for t in (e.get("thresholds") or {}).values():
                v = t["value"]
                if isinstance(v, float):
                    vals.add(v)
                elif isinstance(v, int) and not isinstance(v, bool):
                    vals.add(("int", v))
        for t in (d.get("procedure") or {}).values():
            v = t["value"]
            if isinstance(v, float):
                vals.add(v)
    return vals


def test_no_threshold_literals_in_code():
    vals = _registry_values()
    pkg = os.path.join(METRICS, "kaiju_metrics")
    hits = []
    for root, _, files in os.walk(pkg):
        for f in files:
            if not f.endswith(".py"):
                continue
            path = os.path.join(root, f)
            rel = os.path.relpath(path, pkg).replace(os.sep, "/")
            toks = tokenize.generate_tokens(io.StringIO(open(path).read()).readline)
            for t in toks:
                if t.type != tokenize.NUMBER:
                    continue
                is_float = "." in t.string or "e" in t.string.lower()
                try:
                    v = float(t.string)
                except ValueError:
                    continue
                if not is_float:
                    if v < 5:                    # small integers (axes, counts, 0/1/2/3/4 indices) are not scanned
                        continue
                    v = ("int", int(v))
                if v in vals and (rel, t.string) not in ALLOW and rel not in ALLOW_FILES:
                    hits.append(f"{rel}:{t.start[0]}: {t.string}")
    assert hits == [], "threshold literals outside the registry:\n" + "\n".join(hits)


# ------------------------------------------------------------------ versions and draft
def test_second_version_coexists_and_draft_cannot_fail(tmp_path):
    rd = tmp_path / "registry"
    rd.mkdir()
    v1 = load_registry("M-002").path
    shutil.copy(v1, rd / "M-002@v1.yaml")
    d = yaml.safe_load(open(v1))
    d.update(spec="M-002@v2", revision="v2.0", metric_status="draft")
    d["submeasures"]["M-002.1"]["thresholds"]["max_frac"]["value"] = 1e-6       # everything slips under v2
    yaml.safe_dump(d, open(rd / "M-002@v2.yaml", "w"), sort_keys=False)
    assert [r[1] for r in registry_files(str(rd))] == [1, 2]
    assert default_registry("M-002", str(rd)).revision == "v2.0"
    gd = tmp_path / "g"
    G.write_set(str(gd), over={"walk_fr020": G.spec_walk(0.2, strides=5, slips={(0, 2): (0.01, 0.0)})},
                names=["walk_fr020"])
    r1, _ = run_metric("m002", [str(gd)], registry=str(rd / "M-002@v1.yaml"), images=False)
    r2, _ = run_metric("m002", [str(gd)], registry=str(rd / "M-002@v2.yaml"), images=False)
    assert r1["metric_version"] == "M-002@v1" and r2["metric_version"] == "M-002@v2"
    assert [s["status"] for s in r2["submeasures"] if s["id"] == "M-002.1"] == ["FAIL"]
    assert r2["verdict"] == "INCOMPLETE" and any(f["code"] == "DRAFT_METRIC_CANNOT_FAIL" for f in r2["flags"])


def test_provisional_label_counts_assumed(tmp_path):
    rep = run_full(tmp_path, metric="m002")
    assert rep["verdict_label"] == "PASS"          # M-002 gating thresholds are all derived
    r = m001_ratings(str(tmp_path / "r.json"), "b")
    rep = run_full(tmp_path, ratings=r, label="b")
    n = len(rep["assumed_thresholds_decisive"])
    assert n > 0 and rep["verdict_label"] == f"PASS (provisional: {n} assumed)"


def test_list_metrics():
    info = list_metrics()
    assert sorted({m["metric"] for m in info}) == ["M-001", "M-002", "M-003", "M-004", "M-050", "M-052"]
