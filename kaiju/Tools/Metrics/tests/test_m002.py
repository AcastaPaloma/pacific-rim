"""M-002@v1 (R-001 §5, §9): slip known answers, heel-to-toe roll and sinkage not counted as slip, boundaries,
negative controls failing only their ID, worst contact never averaged away (C6), diagnostics location (C12)."""
import math

import pytest

from fixtures import gen_gait as G
from gaitkit import failing, run_full, run_specs, sm

LF = G.REF["foot_length_m"]
H_TOL = 0.01 * LF


def walk(**kw):
    sp = G.spec_walk(0.20, strides=4, capture=kw.pop("capture", "walk_fr020"), **kw)
    return sp


def test_M002_1_known_slip_non_axis(tmp_path):
    d = (0.031, -0.047)            # metres, injected linear drift over the stance of L contact #2
    rep = run_specs(tmp_path, [walk(slips={(0, 2): d})], metric="m002")
    s = sm(rep, "M-002.1")
    assert abs(s["parts"][0]["slip_m"] - math.hypot(*d)) < 1e-6
    w = s["diagnostics"]["worst_contacts"][0]
    assert w["foot"] == "L" and abs(w["slip_m"] - math.hypot(*d)) < 1e-6


def test_M002_1_heel_to_toe_roll_not_slip(tmp_path):
    """Roll about the toe line: a point d from the toe stays loaded while d sin(th) <= h_tol; its horizontal
    motion is d (1 - cos th). Closed-form truth: max over the points and the sampled angles while loaded."""
    th_max = 0.05
    sp = walk(roll={(0, 2): th_max})
    recs, truth = G.build(sp)
    b, e = [(c[1], c[2]) for c in truth["contacts"] if c[0] == "L"][2]
    m = e - b
    exp = 0.0
    for dist in (0.9 * LF, 0.45 * LF):                  # heel corners and sole centre (toe corners do not move)
        for j in range(m):
            s = j / (m - 1)
            th = th_max * (s - 0.7) / 0.3 if s > 0.7 else 0.0
            if dist * math.sin(th) <= H_TOL:
                exp = max(exp, dist * (1 - math.cos(th)))
    rep = run_specs(tmp_path, [sp], metric="m002")
    got = [c for c in rep["extra"]["contacts"] if c["foot"] == "L"][2]["slip_m"]
    assert abs(got - exp) < 1e-6 and got < 0.01


def test_M002_1_sinkage_not_slip(tmp_path):
    rep = run_specs(tmp_path, [walk(sink_m={(1, 1): 0.4})], metric="m002")
    got = [c for c in rep["extra"]["contacts"] if c["foot"] == "R"][1]["slip_m"]
    assert got < 1e-9


@pytest.mark.parametrize("f,exp", [(0.98, "PASS"), (1.02, "FAIL")])
def test_M002_1_boundary(tmp_path, f, exp):
    s = f * 0.005 * LF
    rep = run_specs(tmp_path, [walk(slips={(1, 1): (s * 0.6, s * 0.8)})], metric="m002")
    assert sm(rep, "M-002.1")["status"] == exp


@pytest.mark.parametrize("f,exp", [(0.98, "PASS"), (1.02, "FAIL")])
def test_M002_2_boundary(tmp_path, f, exp):
    s = f * 0.0025 * LF
    sl = {(i, k): (0.0, s) for i in range(2) for k in range(12)}
    rep = run_specs(tmp_path, [walk(slips=sl)], metric="m002")
    assert abs(sm(rep, "M-002.2")["value"] - f * 0.0025) < 1e-6
    assert sm(rep, "M-002.2")["status"] == exp and sm(rep, "M-002.1")["status"] == "PASS"


@pytest.mark.parametrize("dt,exp", [(0.0196, "PASS"), (0.0204, "FAIL")])
def test_M002_3_boundary(tmp_path, dt, exp):
    sp = walk()
    sp.dt = dt
    assert sm(run_specs(tmp_path, [sp], metric="m002"), "M-002.3")["status"] == exp


@pytest.mark.parametrize("every,exp", [(51, "PASS"), (49, "FAIL")])
def test_M002_4_boundary(tmp_path, every, exp):
    """Threshold 0.99 agreement: boundary on the disagreement share 1 % (0.98 % pass / 1.02 % fail):
    flipping foot L on every k-th step disagrees on 1 / (2 k) of the foot-steps."""
    sp = walk()
    sp.flag_flip_every = every
    recs, truth = G.build(sp)
    dis = len(range(0, truth["N"], every)) / (2 * truth["N"])
    assert abs(dis - 0.01 * (0.98 if exp == "PASS" else 1.02)) < 0.0003
    rep = run_specs(tmp_path, [sp], metric="m002")
    assert abs(sm(rep, "M-002.4")["value"] - (1 - dis)) < 1e-12 and sm(rep, "M-002.4")["status"] == exp


def test_M002_4_known_agreement(tmp_path):
    sp = walk()
    sp.flag_flip_every = 40
    recs, truth = G.build(sp)
    N = truth["N"]
    flips = len(range(0, N, 40))
    rep = run_specs(tmp_path, [sp], metric="m002")
    assert abs(sm(rep, "M-002.4")["value"] - (1 - flips / (2 * N))) < 1e-9


def test_M002_5_boundary_and_known(tmp_path):
    ok = run_specs(tmp_path, [walk()], metric="m002", name="a")
    assert sm(ok, "M-002.5")["value"] == 0 and sm(ok, "M-002.5")["status"] == "PASS"
    bad = run_specs(tmp_path, [walk(ext_f=[(10.0, 10.5, 0.06 * G.REF["mass_kg"] * G.G)])], metric="m002", name="b")
    assert sm(bad, "M-002.5")["value"] >= 1 and sm(bad, "M-002.5")["status"] == "FAIL"


# ------------------------------------------------------------------ verdict rules
def test_M002_reference_set_passes(tmp_path):
    rep = run_full(tmp_path, metric="m002")
    assert rep["verdict"] == "PASS" and rep["exit_code"] == 0
    assert rep["coverage"]["optional_not_provided"] == ["walk_fr020_terrain"]


NEG = {
    "M-002.1": {"walk_fr020": G.spec_walk(0.2, strides=5, slips={(0, 2): (0.0, 0.15)})},
    "M-002.3": {"walk_fr020": None},
    "M-002.4": {"walk_fr020": G.spec_walk(0.2, strides=5, flag_flip_every=25)},
    "M-002.5": {"stomp": G.spec_stomp(ext_f=[(5.0, 5.5, 0.08 * G.REF["mass_kg"] * G.G)])},
}


@pytest.mark.parametrize("sid", sorted(NEG))
def test_M002_negative_fails_only_that_id(tmp_path, sid):
    o = dict(NEG[sid])
    if sid == "M-002.3":
        sp = G.spec_walk(0.2, strides=5)
        sp.dt = 0.025
        o["walk_fr020"] = sp
    rep = run_full(tmp_path, over=o, metric="m002")
    assert failing(rep) == [sid], failing(rep)
    assert rep["verdict"] == "FAIL"


def test_M002_2_advisory_only_still_passes(tmp_path):
    sl = {(i, k): (0.06, 0.0) for i in range(2) for k in range(12)}     # 0.31 % L_f: above p95 0.25 %, below 0.5 %
    rep = run_full(tmp_path, over={"walk_fr020": G.spec_walk(0.2, strides=5, slips=sl)}, metric="m002")
    assert rep["verdict"] == "PASS" and failing(rep, ("advisory",)) == ["M-002.2"]
    assert any("ADVISORY M-002.2" in w for w in rep["warnings"])


def test_M002_one_bad_contact_among_200_fails(tmp_path):
    """C6: one 0.12 m slip among > 200 good contacts fails; the worst contact is named first."""
    sp = G.spec_walk(0.2, strides=1, capture="walk_fr020")
    sp.stride_s, sp.duty = 1.0, 0.66
    sp.duration_s = 110.0
    sp.slips = {(1, 77): (0.12, 0.0)}
    rep = run_specs(tmp_path, [sp], metric="m002")
    s = sm(rep, "M-002.1")
    assert s["diagnostics"]["contacts_used"] >= 200
    assert s["status"] == "FAIL" and abs(s["parts"][0]["slip_m"] - 0.12) < 1e-6
    w = s["diagnostics"]["worst_contacts"][0]
    assert w["foot"] == "R" and "contact" in s["diagnostics"]["where"]


def test_M002_zero_contacts_incomplete(tmp_path):
    from kaiju_metrics.runner import run_metric
    recs, _ = G.build(walk())
    recs[0]["mass_kg"] = 1.0e9            # F_on = 0.03 m g now exceeds every logged force: no contacts
    p = G.write(recs, str(tmp_path / "z" / "walk_fr020.gait.jsonl"))
    rep, _ = run_metric("m002", [p], images=False)
    assert sm(rep, "M-002.1")["status"] == "NOT_RUN" and rep["verdict"] in ("INCOMPLETE", "FAIL")
    assert rep["verdict"] != "PASS"


def test_M002_truncated_contacts_kept_and_tagged(tmp_path):
    rep = run_specs(tmp_path, [walk()], metric="m002")
    tags = [c["tags"] for c in rep["extra"]["contacts"]]
    assert any("truncated_start" in t for t in tags) and any("truncated_end" in t for t in tags)


def test_M002_scuff_tagged_not_exempt(tmp_path):
    sp = walk(scuff=[(1, 2.0, 2.4)])
    rep = run_specs(tmp_path, [sp], metric="m002")
    assert any("scuff" in c["tags"] for c in rep["extra"]["contacts"])


def test_M002_header_thresholds_ignored(tmp_path):
    sp = walk(header={"thresholds": {"F_on_N": 9.0e7, "F_off_N": 1.0e7}})
    rep = run_specs(tmp_path, [sp], metric="m002")
    assert any(f["code"] == "NON_SPEC_CONTACT_THRESHOLDS" for f in rep["flags"])
    assert sm(rep, "M-002.4")["status"] == "PASS"
