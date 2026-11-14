"""M-004@v1 (R-002 §5): S0, S_px, C_px, F per motion class. Truth is the generator's own exact scene geometry
(frontal wall at the 600 m reference depth; sky by direction) and nearest-pixel reprojection. Guards (C9),
coverage (C7), boundaries and negatives (C4), parallel determinism (C13)."""
import json
import os

import numpy as np
import pytest

from fixtures import gen_style as GS
from kaiju_metrics.runner import run_metric


def run4(sd, fixture=True, jobs=None, profiles="slate_teal", viewpoints="eye"):
    opts = {"profiles": profiles, "viewpoints": viewpoints}
    if fixture:
        opts["fixture"] = True
    if jobs:
        opts["jobs"] = jobs
    rep, _ = run_metric("m004", [sd], images=False, options=opts)
    return rep


def sm(rep, sid):
    return next(s for s in rep["submeasures"] if s["id"] == sid)


def seqs(rep):
    return {s["sequence"].split("_")[-1]: s for s in rep["extra"]["sequences"]}


def failing(rep):
    return sorted(s["id"] for s in rep["submeasures"] if s["status"] == "FAIL" and s["kind"] == "gating")


@pytest.fixture(scope="module")
def pass_set(tmp_path_factory):
    root = tmp_path_factory.mktemp("m4")
    return GS.m004_set(str(root))


@pytest.fixture(scope="module")
def screen_set(tmp_path_factory):
    root = tmp_path_factory.mktemp("m4s")
    return GS.m004_set(str(root), set_id="m004_screen", anchor_t="screen", anchor_y="screen", static_noise=True)


def test_M004_pass_set_known_answers(pass_set):
    sd, truth = pass_set
    rep = run4(sd)
    assert rep["verdict"] == "PASS" and rep["verdict_label"].endswith("[fixture mode: not a gating run]")
    got = seqs(rep)
    for k, t in truth.items():
        c = k.split("_")[-1]
        assert abs(got[c]["S"] - t["S_px"]) < 1e-12, c
        assert abs(got[c]["F"] - t["F"]) < 1e-12, c
        assert got[c]["lag"] == t["lag"]
    assert got["T3"]["C"] == 0.0 and got["S0"]["C"] == 0.0


def test_M004_screen_set_known_answers_and_R002_controls(screen_set):
    """R-002 controls: screen-fixed dither under yaw fails, temporal-noise static fails (S0 > 0)."""
    sd, truth = screen_set
    rep = run4(sd)
    got = seqs(rep)
    for k, t in truth.items():
        c = k.split("_")[-1]
        assert abs(got[c]["S"] - t["S_px"]) < 1e-12 and abs(got[c]["F"] - t["F"]) < 1e-12, c
    assert rep["verdict"] == "FAIL"
    for sid in ("M-004.S0", "M-004.S_px.Y", "M-004.S_px.T12", "M-004.S_px.T3", "M-004.F.T3", "M-004.F.Y"):
        assert sm(rep, sid)["status"] == "FAIL", sid
    assert got["T3"]["C"] >= got["T3"]["S"]            # checker pixels are the ones that change


def test_M004_lag_definition_slow_drift(screen_set):
    """k = ceil(1/speed): at 0.1 px/frame consecutive-frame change is tiny but S_px (lag 10) is ~0.87."""
    sd, truth = screen_set
    assert truth["slate_teal_eye_T1"]["lag"] == 10 and truth["slate_teal_eye_T1"]["S_px"] > 0.8


def _one(tmp_path, name, *a, **kw):
    sd = os.path.join(str(tmp_path), "set")
    t = GS.m004_sequence(sd, name, *a, **kw)
    return sd, t


@pytest.mark.parametrize("cls,speed", [("T2", 0.25), ("T3", 1.0)])
def test_M004_S_px_translation_boundary(tmp_path, cls, speed):
    """98 % / 102 % of 0.12: screen checker on the first `cols` columns; cols chosen by the generator truth."""
    W = 480
    want = {0.98: "PASS", 1.02: "FAIL"}
    for f, exp in want.items():
        best = None
        c0 = int(f * 0.12 * W / 0.874)
        for cols in range(c0 - 3, c0 + 4):
            sd = os.path.join(str(tmp_path), f"b{f}_{cols}")
            t = GS.m004_sequence(sd, cls, "translate", speed, anchor="screen", W=W, H=24, n_frames=60,
                                 checker_cols=cols)
            if best is None or abs(t["S_px"] - f * 0.12) < abs(best[1]["S_px"] - f * 0.12):
                best = (sd, t)
        sd, t = best
        assert abs(t["S_px"] - f * 0.12) < 0.0015
        rep, _ = run_metric("m004", [sd], images=False, options={"fixture": True, "profiles": "slate_teal"})
        sid = "M-004.S_px.T12" if cls == "T2" else "M-004.S_px.T3"
        p = sm(rep, sid)["parts"][0]
        assert abs(p["value"] - t["S_px"]) < 1e-12 and p["status"] == exp


def test_M004_S0_boundary_and_negative_only_S0(tmp_path):
    sd, t = _one(tmp_path, "S0", "static", 0.0, anchor="screen", patch_change=True, checker_cols=80)
    rep, _ = run_metric("m004", [sd], images=False, options={"fixture": True, "profiles": "slate_teal"})
    assert abs(sm(rep, "M-004.S0")["value"] - t["S_px"]) < 1e-12 and t["S_px"] > 0
    assert sm(rep, "M-004.S0")["status"] == "FAIL"
    assert sm(rep, "M-004.F.S0")["status"] == "PASS" and sm(rep, "M-004.C_px.S0")["status"] == "PASS"
    sd2, t2 = _one(tmp_path / "z", "S0", "static", 0.0, anchor="screen")
    rep2, _ = run_metric("m004", [sd2], images=False, options={"fixture": True, "profiles": "slate_teal"})
    assert sm(rep2, "M-004.S0")["value"] == 0.0 and sm(rep2, "M-004.S0")["status"] == "PASS"


@pytest.mark.parametrize("grp,mk", [
    ("T12", lambda d: [GS.m004_sequence(d, n, "translate", s, anchor="screen") for n, s in (("T1", 0.1), ("T2", 0.25))]),
    ("T3", lambda d: GS.m004_sequence(d, "T3", "translate", 1.0, anchor="screen")),
    ("Y", lambda d: GS.m004_sequence(d, "Y2", "yaw", 1.0, anchor="screen", scene="sky")),
])
def test_M004_negative_fails_only_its_class(tmp_path, pass_set, grp, mk):
    """A full passing set with one class replaced by screen-fixed dither fails only that class's IDs."""
    import shutil
    sd0, _ = pass_set
    sd = os.path.join(str(tmp_path), "m004_pass")
    shutil.copytree(sd0, sd)
    for n in {"T12": ["T1", "T2"], "T3": ["T3"], "Y": ["Y2"]}[grp]:
        shutil.rmtree(os.path.join(sd, f"slate_teal_eye_{n}"))
    tmp = os.path.join(str(tmp_path), "new")
    mk(tmp)
    for d in os.listdir(tmp):
        dst = os.path.join(sd, f"slate_teal_eye_{d}")
        shutil.copytree(os.path.join(tmp, d), dst)
        for f in os.listdir(dst):                    # sequence id must equal the directory name
            if f.endswith(".json"):
                p = os.path.join(dst, f)
                m = json.load(open(p))
                m["sequence_id"] = f"slate_teal_eye_{d}"
                m["capture_set_id"] = "m004_pass"
                json.dump(m, open(p, "w"))
    rep = run4(sd)
    fl = failing(rep)
    assert fl and all(s.endswith("." + grp) for s in fl), fl


def test_M004_guard_declared_speed_mismatch(tmp_path):
    sd, _ = _one(tmp_path, "T2", "translate", 0.23, anchor="surface", declared_speed=0.26)
    rep, _ = run_metric("m004", [sd], images=False, options={"fixture": True})
    assert rep["verdict"] == "INVALID_INPUT" and "measured" in rep["contract_errors"][0]["message"]


def test_M004_guard_no_class(tmp_path):
    sd, _ = _one(tmp_path, "T9", "translate", 0.5, anchor="surface")
    rep, _ = run_metric("m004", [sd], images=False, options={"fixture": True})
    assert rep["verdict"] == "INVALID_INPUT" and "no M-004 class" in rep["contract_errors"][0]["message"]


def test_M004_guard_frozen(tmp_path):
    sd, _ = _one(tmp_path, "T3", "translate", 1.0, anchor="surface", freeze=True)
    rep, _ = run_metric("m004", [sd], images=False, options={"fixture": True})
    assert rep["verdict"] == "INVALID_INPUT" and any("frozen" in e["message"] for e in rep["contract_errors"])


def test_M004_guard_blank(tmp_path):
    sd, _ = _one(tmp_path, "S0", "static", 0.0, blank=True, scene="sky")
    rep, _ = run_metric("m004", [sd], images=False, options={"fixture": True})
    assert rep["verdict"] == "INVALID_INPUT" and "blank" in rep["contract_errors"][0]["message"]


def test_M004_guard_fully_masked_incomplete(tmp_path):
    sd, _ = _one(tmp_path, "T3", "translate", 1.0, anchor="surface", mask_all=True)
    rep, _ = run_metric("m004", [sd], images=False, options={"fixture": True, "profiles": "slate_teal"})
    assert sm(rep, "M-004.S_px.T3")["status"] == "NOT_RUN" and rep["verdict"] != "PASS"


def test_M004_short_sequences_incomplete_without_fixture_mode(pass_set):
    sd, _ = pass_set
    rep = run4(sd, fixture=False)
    assert rep["verdict"] == "INCOMPLETE" and any("< 180" in m for m in rep["coverage"]["missing"])


def test_M004_pitch_reported_without_verdict(tmp_path):
    sd = os.path.join(str(tmp_path), "set")
    GS.m004_sequence(sd, "P1", "pitch", 0.25, anchor="direction_bands", scene="sky")
    rep, _ = run_metric("m004", [sd], images=False, options={"fixture": True})
    assert rep["extra"]["no_verdict_sequences"][0]["motion"] == "pitch" and rep["verdict"] != "PASS"


def test_M004_coverage_three_profiles_two_viewpoints(pass_set):
    sd, _ = pass_set
    rep = run4(sd, profiles=None, viewpoints=None)
    assert rep["verdict"] == "INCOMPLETE" and rep["coverage"]["missing"]


def test_M004_origin_offset_50km_precision(tmp_path):
    a = GS.m004_sequence(os.path.join(str(tmp_path), "a"), "T3", "translate", 1.0, anchor="surface",
                         origin=np.array([0.0, 0.0, 0.0]))
    b = GS.m004_sequence(os.path.join(str(tmp_path), "b"), "T3", "translate", 1.0, anchor="surface",
                         origin=np.array([50000.0, 0.0, -50000.0]))
    ra, _ = run_metric("m004", [os.path.join(str(tmp_path), "a")], images=False, options={"fixture": True})
    rb, _ = run_metric("m004", [os.path.join(str(tmp_path), "b")], images=False, options={"fixture": True})
    sa, sb = ra["extra"]["sequences"][0], rb["extra"]["sequences"][0]
    assert sa["S"] == sb["S"] == 0.0 and sa["valid_share"] == sb["valid_share"]


def test_M004_parallel_identical(pass_set):
    sd, _ = pass_set
    from kaiju_metrics.report import strip_run
    a = run4(sd)
    b = run4(sd, jobs=3)
    a = strip_run(a); b = strip_run(b)
    a["invocation"].pop("options"); b["invocation"].pop("options")
    assert json.dumps(a, sort_keys=True) == json.dumps(b, sort_keys=True)
