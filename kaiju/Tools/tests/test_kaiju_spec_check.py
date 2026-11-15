"""pytest for Tools/kaiju_spec_check.py (M-103, T-112). Positive run on the three species plus mutation controls that must fail."""
import copy, json, os, shutil, sys
import pytest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
import kaiju_spec_check as K

SPECIES = ["strider", "breacher", "bastion"]


def test_all_pass():
    passed, rows = K.run()
    assert passed, [r for r in rows if not r[2]]
    assert {r[0] for r in rows if r[1] == "M-103.1"} == {"strider@1", "breacher@1", "bastion@1"}


def by(rows, sid, m):
    return next(r for r in rows if r[0] == sid and r[1] == m)


def test_r102_numbers():
    specs, tissue = K.load(K.DEFAULT_DIR)
    d = {s["id"]: s for s in specs}
    # mass (t), R-102 section 3
    for sid, t in (("strider@1", 4190), ("breacher@1", 35000), ("bastion@1", 163900)):
        assert K.volume(d[sid]) * K.v(d[sid]["mass"], "rho_eff_kg_m3") / 1e3 == pytest.approx(t, rel=0.01)
    # SF at charge cap: 2.1 / 4.2 / 2.5 (section 4.3)
    for sid, sf in (("strider@1", 2.1), ("breacher@1", 4.2), ("bastion@1", 2.5)):
        s = d[sid]
        lc = K.limb(s, K.v(s["mass"], "mass_kg"), 0.30, K.v(s["gait"], "fr_charge_cap"))
        assert 600e6 / lc["sigma_bone"] == pytest.approx(sf, abs=0.06)
    # muscle utilisation: BASTION is the maximum at 0.76
    s = d["bastion@1"]
    lc = K.limb(s, K.v(s["mass"], "mass_kg"), 0.30, 0.15)
    assert lc["sigma_muscle"] / 6e6 == pytest.approx(0.76, abs=0.01)
    # stride periods 5.1 / 6.8 / 11.2 s
    for sid, T in (("strider@1", 5.1), ("breacher@1", 6.8), ("bastion@1", 11.2)):
        assert K.v(d[sid]["gait"], "stride_period_cruise_s") == pytest.approx(T, abs=0.06)


def _write(tmp_path, mutate):
    for n in SPECIES + ["kaiju_tissue_v1"]:
        shutil.copy(os.path.join(K.DEFAULT_DIR, n + ".json"), tmp_path)
    p = tmp_path / "strider.json"
    d = json.load(open(p))
    mutate(d)
    json.dump(d, open(p, "w"))
    return str(tmp_path)


@pytest.mark.parametrize("name,mutate,measure", [
    ("mass off 5pct", lambda d: d["mass"]["mass_kg"].__setitem__("value", d["mass"]["mass_kg"]["value"] * 1.05), "M-103.1"),
    ("thin bone", lambda d: d["limb_model"]["bone_area_share"].__setitem__("value", 0.05), "M-103.3"),
    ("charge cap too high", lambda d: d["gait"]["fr_charge_cap"].__setitem__("value", 1.5), "M-103.3"),
    ("lower sf threshold", lambda d: d["material"]["bone_sf_charge_min"].__setitem__("value", 1.0), "M-103.3"),
    ("species tissue differs", lambda d: d["material"]["bone_yield_Pa"].__setitem__("value", 700e6), "M-103.5"),
    ("tissue not declared", lambda d: d["provenance"].__setitem__("assumed_fields", []), "M-103.5"),
    ("cruise Fr out of band", lambda d: d["gait"]["fr_cruise"].__setitem__("value", 0.5), "M-103.6"),
    ("stride off 30pct", lambda d: d["gait"]["stride_period_cruise_s"].__setitem__("value", 7.0), "M-103.6"),
    ("attack energy over bound", lambda d: d["attacks"][0]["E_cap_J"].__setitem__("value", 1e9), "M-103.7"),
    ("gravity attack without drop", lambda d: d["attacks"][3]["E_cap_J"].__setitem__("value", 1e9), "M-103.7"),
    ("floats flag wrong", lambda d: d["water"]["floats"].__setitem__("value", 0), "M-103.8"),
    ("pressure over cap", lambda d: d["geometry"]["foot_area_m2"].__setitem__("value", 5.0), "M-103.9"),
    ("height out of class", lambda d: d["geometry"]["height_m"].__setitem__("value", 80.0), "M-103.1"),
    ("missing status", lambda d: d["geometry"]["hip_height_m"].__setitem__("status", "Guess"), "M-103.5"),
])
def test_mutations_fail(tmp_path, name, mutate, measure):
    passed, rows = K.run(_write(tmp_path, mutate))
    assert not passed
    assert not by(rows, "strider@1", measure)[2], name


def test_cross_species_ratio_control():
    specs, _ = K.load(K.DEFAULT_DIR)
    big = copy.deepcopy(specs[0])
    big["mass"]["mass_kg"]["value"] *= 10
    assert not all(r[1] for r in K.check_cross([specs[0], big]))
