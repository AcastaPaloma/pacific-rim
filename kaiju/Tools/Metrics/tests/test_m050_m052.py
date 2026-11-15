"""M-050@v1 and M-052@v1: pass and fail fixtures, the committed golden as a real input, no silent pass."""
import copy
import json
import os

import pytest

from conftest import METRICS, PROJECT
from fixtures import gen_destruction as G
from kaiju_metrics.runner import run_metric
from test_tools import cli

GOLDEN = os.path.join(PROJECT, "Assets", "Game", "World", "Destruction", "Tests", "EditMode", "Golden",
                      "destruction_v1.json")


def sm(rep, sid):
    return next(s for s in rep["submeasures"] if s["id"] == sid)


def run50(tmp_path, a, b):
    return run_metric("m050", [a, b], out_dir=str(tmp_path / "o"), images=False)[0]


@pytest.fixture()
def save(tmp_path):
    return G.make_save(), tmp_path


def test_m050_identical_passes(save):
    s, tp = save
    a, b = G.write_json(tp / "a.json", s), G.write_json(tp / "b.json", s)
    rep = run50(tp, str(a), str(b))
    assert rep["verdict"] == "PASS" and rep["exit_code"] == 0
    assert rep["extra"]["m050"]["differing_bytes"] == 0


def test_m050_golden_vs_itself_passes(tmp_path):
    assert os.path.isfile(GOLDEN)
    rep = run50(tmp_path, GOLDEN, GOLDEN)
    assert rep["verdict"] == "PASS"
    assert rep["extra"]["m050"]["records_before"] > 100


def test_m050_negative_control_one_flipped_byte_fails_on_golden(tmp_path):
    raw = open(GOLDEN, "rb").read()
    i = raw.index(b'"e_cum_j":{"f64":"') + len(b'"e_cum_j":{"f64":"')
    flipped = bytearray(raw)
    flipped[i] = ord("0") if flipped[i] != ord("0") else ord("1")
    p = tmp_path / "flip.json"
    p.write_bytes(bytes(flipped))
    rep = run50(tmp_path, GOLDEN, str(p))
    assert rep["verdict"] == "FAIL" and rep["exit_code"] == 1
    assert sm(rep, "M-050.bytes")["value"] == 1
    f = sm(rep, "M-050.fields")
    assert f["status"] == "FAIL" and f["value"] == 1 and f["decisive_part"] == "e_cum_j_bit_mismatch"
    first = rep["extra"]["m050"]["first_differing_ids"]
    assert len(first) == 1 and first[0].startswith("bld:")
    r = cli("m050", GOLDEN, str(p), "--out", str(tmp_path / "c"), "--quiet")
    assert r.returncode == 1 and "FAIL" in r.stdout


def test_m050_state_change_lists_first_ids(save):
    s, tp = save
    t = copy.deepcopy(s)
    recs = t["sections"][0]["records"]
    for r in recs[3:6]:
        r["fields"]["state"] = (r["fields"]["state"] % 4) + 1
    rep = run50(tp, str(G.write_json(tp / "a.json", s)), str(G.write_json(tp / "b.json", t)))
    f = sm(rep, "M-050.fields")
    assert rep["verdict"] == "FAIL" and f["parts"][0]["value"] == 3
    assert rep["extra"]["m050"]["first_differing_ids"] == sorted(r["key"] for r in recs[3:6])


def test_m050_missing_record_and_other_section_fail(save):
    s, tp = save
    t = copy.deepcopy(s)
    removed = t["sections"][0]["records"].pop(5)["key"]
    t["sections"][1]["records"][0]["fields"]["step"] = 78
    rep = run50(tp, str(G.write_json(tp / "a.json", s)), str(G.write_json(tp / "b.json", t)))
    assert rep["verdict"] == "FAIL"
    assert sm(rep, "M-050.fields")["parts"][3]["value"] == 1 and removed in rep["extra"]["m050"]["first_differing_ids"]
    assert sm(rep, "M-050.other_sections")["status"] == "FAIL"


@pytest.mark.parametrize("case", ["empty", "missing", "garbage", "no_section", "empty_section", "one_input"])
def test_m050_no_silent_pass(tmp_path, case):
    good = str(G.write_json(tmp_path / "g.json", G.make_save()))
    bad = tmp_path / "bad.json"
    ins = [good, str(bad)]
    if case == "empty":
        bad.write_bytes(b"")
    elif case == "missing":
        pass
    elif case == "garbage":
        bad.write_text("{not json")
    elif case == "no_section":
        s = G.make_save()
        s["sections"] = s["sections"][1:]
        G.write_json(bad, s)
    elif case == "empty_section":
        s = G.make_save()
        s["sections"][0]["records"] = [s["sections"][0]["records"][0]]
        G.write_json(bad, s)
    else:
        ins = [good]
    rep = run_metric("m050", ins, out_dir=str(tmp_path / "o"), images=False)[0]
    assert rep["verdict"] == "INVALID_INPUT" and rep["exit_code"] == 2
    assert [x["id"] for x in rep["submeasures"]] == ["M-050.bytes", "M-050.fields", "M-050.other_sections"]
    assert cli("m050", *ins, "--out", str(tmp_path / "c"), "--quiet").returncode == 2


# ------------------------------------------------------------------ M-052
def run52(tmp_path, d, name="d.json"):
    p = G.write_json(tmp_path / name, d)
    return run_metric("m052", [str(p)], out_dir=str(tmp_path / "o"), images=False)[0]


def test_m052_pass(tmp_path):
    rep = run52(tmp_path, G.make_dump())
    assert rep["verdict"].startswith("PASS") and rep["exit_code"] == 0
    assert rep["extra"]["m052"]["collapsed"] == 3
    assert rep["verdict_label"] == "PASS (provisional: 1 assumed)"


@pytest.mark.parametrize("mut,sid,part", [
    (lambda d: d["buildings"][0].update(m_standing_kg=d["buildings"][0]["m_standing_kg"] * 1.001), "M-052.mass", "mass_resid_rel"),
    (lambda d: d["buildings"][3].update(debris_kg=9e9, m_standing_kg=0.0), "M-052.mass", "debris_over_m0"),
    (lambda d: d["buildings"][0].update(piece_mass_sum_kg=d["buildings"][0]["piece_mass_sum_kg"] * 0.99), "M-052.pieces", "piece_sum_rel"),
    (lambda d: d["buildings"][0].update(piece_count=65), "M-052.pieces", "pieces_max"),
    (lambda d: d["buildings"][0]["energy"].update(e_ground_j=d["buildings"][0]["energy"]["e_ground_j"] * 2), "M-052.energy", "e_res_over_dpe_min"),
    (lambda d: d["buildings"][0]["energy"].update(ke_ej_j=0.2 * d["buildings"][0]["energy"]["dpe_j"]), "M-052.energy", "ke_ej_over_dpe_max"),
    (lambda d: d["buildings"][0]["energy"].update(e_frac_j=0.3 * d["buildings"][0]["energy"]["dpe_j"]), "M-052.energy", "e_frac_over_dpe_max"),
    (lambda d: d["transition_checks"][1]["two_step"].update(debris_kg=1.001e5), "M-052.path", "two_step_vs_direct_rel"),
])
def test_m052_each_rule_fails(tmp_path, mut, sid, part):
    d = G.make_dump()
    mut(d)
    rep = run52(tmp_path, d)
    assert rep["verdict"] == "FAIL" and rep["exit_code"] == 1
    s = sm(rep, sid)
    assert s["status"] == "FAIL"
    assert any(p["name"] == part and p["status"] == "FAIL" for p in s["parts"]), s["parts"]


def test_m052_energy_creation_fails(tmp_path):
    d = G.make_dump()
    e = d["buildings"][0]["energy"]
    e["w_ext_j"] = 0.0
    e["e_ground_j"] = e["dpe_j"]          # E_frac + E_ground + KE_ej > dPE
    rep = run52(tmp_path, d)
    s = sm(rep, "M-052.energy")
    assert rep["verdict"] == "FAIL" and any(p["name"] == "energy_created_j_max" and p["status"] == "FAIL" for p in s["parts"])


@pytest.mark.parametrize("case", ["empty", "missing", "no_buildings", "no_energy_block", "no_transitions", "nan"])
def test_m052_no_silent_pass(tmp_path, case):
    p = tmp_path / "d.json"
    d = G.make_dump()
    if case == "empty":
        p.write_bytes(b"")
    elif case == "missing":
        pass
    elif case == "no_buildings":
        d["buildings"] = []
        G.write_json(p, d)
    elif case == "no_energy_block":
        del d["buildings"][0]["energy"]
        G.write_json(p, d)
    elif case == "no_transitions":
        del d["transition_checks"]
        G.write_json(p, d)
    else:
        p.write_text(json.dumps(d, separators=(",", ":")).replace('"H":8.0', '"H":NaN'))
        assert "NaN" in p.read_text()
    rep = run_metric("m052", [str(p)], out_dir=str(tmp_path / "o"), images=False)[0]
    assert rep["verdict"] == "INVALID_INPUT" and rep["exit_code"] == 2
    assert cli("m052", str(p), "--out", str(tmp_path / "c"), "--quiet").returncode == 2


def test_m052_no_collapse_in_dump_is_incomplete(tmp_path):
    d = G.make_dump()
    d["buildings"] = [b for b in d["buildings"] if b["state"] < 3]
    rep = run52(tmp_path, d)
    assert rep["verdict"] == "INCOMPLETE" and rep["exit_code"] == 3


def test_list_metrics_includes_new():
    r = cli("list-metrics")
    assert "M-050@v1" in r.stdout and "M-052@v1" in r.stdout
