"""T-022 step 1: sourced landmark prisms (R-020 §2)."""
import copy
import json

import pytest
from shapely.geometry import box, mapping

from kaiju_geo import landmarks, manifest
from kaiju_geo.common import TILES_ROOT, load_config, sha256_file

M0 = TILES_ROOT / "L0" / "30_36"
SCHEMA = set(landmarks.FIELDS)


def _b(bid, g, base, h, part_of=None):
    return {"type": "Feature", "id": bid, "geometry": mapping(g), "properties": {
        "bid": bid, "part_of": part_of, "base_navd88_m": base, "height_m": h, "excluded": None}}


def _cfg(top=326.0, spire=None):
    lm = {"slug": "t", "kind": "crown", "tile": "T", "part_of": "osm:w1", "pos_baytm": [5.0, 5.0],
          "height_above_base_m": top, "roof_shape": "flat", "height_source": "ctbuh", "source_ids": ["ctbuh"],
          "pos_source": "osm", "confidence": "A", "tol_height_m": 3.0, "tol_pos_m": 5.0}
    if spire:
        lm["spire_m"] = spire
    return {"sources": {}, "landmarks": [lm]}


def test_part_stacks_on_baked_top_and_has_schema():
    b = [_b("osm:w1", box(0, 0, 10, 10), 4.0, 200.0)]
    f, chk = landmarks.build("T", b, _cfg())
    assert len(f) == 1 and chk[0]["status"] == "emitted"
    p = f[0]["properties"]
    assert SCHEMA <= set(p)
    assert p["part_of"] in {x["id"] for x in b}
    assert p["base_navd88_m"] == 204.0 and abs(p["top_navd88_m"] - (4.0 + 326.0)) <= 3.0
    assert p["height_m"] == pytest.approx(p["top_navd88_m"] - p["base_navd88_m"])


def test_within_tolerance_not_emitted():
    f, chk = landmarks.build("T", [_b("osm:w1", box(0, 0, 10, 10), 4.0, 324.0)], _cfg())
    assert f == [] and chk[0]["status"] == "already right in buildings"


def test_spire_split():
    f, _ = landmarks.build("T", [_b("osm:w1", box(0, 0, 10, 10), 0.0, 100.0)], _cfg(260.0, 64.6))
    assert [x["properties"]["roof_shape"] for x in f] == ["flat", "spire"]
    assert f[1]["properties"]["height_m"] == pytest.approx(64.6)
    assert f[1]["properties"]["top_navd88_m"] == pytest.approx(260.0)


def test_tile_without_landmark_has_no_output():
    assert landmarks.build("L0_0_0", [], load_config("landmarks.json")) == ([], [])


def test_baked_tile_layer():
    man = json.loads((M0 / "manifest.json").read_text())
    assert manifest.validate(man, M0) == []
    L = next(x for x in man["layers"] if x["layer"] == "landmarks")
    assert sha256_file(M0 / L["file"]) == L["sha256"] and L["count"] == len(json.loads((M0 / L["file"]).read_text())["features"])
    assert {c["slug"] for c in L["checked"]} == {"salesforce_tower", "transamerica_pyramid", "ferry_building_clock_tower"}
    bids = {f["id"] for f in json.loads((M0 / "buildings.geojson").read_text())["features"]}
    for f in json.loads((M0 / L["file"]).read_text())["features"]:
        assert f["properties"]["part_of"] in bids
    for c in L["checked"]:
        if c["status"] == "emitted":
            assert abs(c["sourced_top_navd88_m"] - c["baked_top_navd88_m"]) > c["tol_height_m"]


def test_manifest_without_layer_validates():
    man = json.loads((M0 / "manifest.json").read_text())
    m = copy.deepcopy(man)
    m["layers"] = [x for x in m["layers"] if x["layer"] != "landmarks"]
    assert manifest.validate(m) == []


def test_bake_is_deterministic():
    import subprocess, sys
    from kaiju_geo.common import PIPE
    h1 = sha256_file(M0 / "landmarks.geojson")
    f, c = landmarks.build("L0_30_36", json.loads((M0 / "buildings.geojson").read_text())["features"])
    assert c == json.loads((M0 / "manifest.json").read_text())["layers"][[x["layer"] for x in json.loads((M0 / "manifest.json").read_text())["layers"]].index("landmarks")]["checked"]
    assert sha256_file(M0 / "landmarks.geojson") == h1


# ---- M1-cleanup: sourced heights win (Ferry Building lowered, GGB N tower top, cable <= tower)
def _ferry_cfg():
    lm = {"slug": "f", "kind": "clock_tower", "tile": "T", "part_of": "osm:w1", "footprint_osm": ["osm:w2", "osm:w3"],
          "pos_baytm": [5.0, 5.0], "height_above_base_m": 71.6, "roof_shape": "flat", "height_source": "nrhp",
          "source_ids": ["x"], "pos_source": "osm", "confidence": "B", "tol_height_m": 3.0, "tol_pos_m": 5.0,
          "lower_baked_to_sourced": True}
    return {"sources": {}, "landmarks": [lm]}


def test_lower_to_sourced_caps_only_the_overshooting_part():
    b = [_b("osm:w1", box(0, 0, 20, 20), 3.0, 15.0), _b("osm:w2", box(0, 0, 5, 5), 3.0, 70.0, "osm:w1"),
         _b("osm:w3", box(5, 5, 10, 10), 3.0, 83.1, "osm:w1")]
    rec = landmarks.lower_to_sourced("T", b, _ferry_cfg())
    assert [(r["bid"], r["from_m"], r["to_m"]) for r in rec] == [("osm:w3", 83.1, 71.6)]
    assert b[2]["properties"]["height_m"] == 71.6 and b[2]["properties"]["height_lowered_from_m"] == 83.1
    assert b[2]["properties"]["height_lowered_to_source"] == "nrhp" and b[1]["properties"]["height_m"] == 70.0
    f, chk = landmarks.build("T", b, _ferry_cfg())
    assert f == [] and chk[0]["status"] == "lowered to sourced in buildings" and chk[0]["lowered_from_m"] == 83.1


def test_lower_requires_flag_and_tile():
    b = [_b("osm:w1", box(0, 0, 20, 20), 3.0, 15.0), _b("osm:w3", box(5, 5, 10, 10), 3.0, 83.1, "osm:w1")]
    cfg = _ferry_cfg()
    assert landmarks.lower_to_sourced("OTHER", b, cfg) == []
    del cfg["landmarks"][0]["lower_baked_to_sourced"]
    assert landmarks.lower_to_sourced("T", b, cfg) == [] and b[1]["properties"]["height_m"] == 83.1


def test_baked_ferry_tower_at_sourced_height():
    feats = {f["id"]: f["properties"] for f in json.loads((M0 / "buildings.geojson").read_text())["features"]}
    p = feats["osm:w406710839"]
    assert p["base_navd88_m"] + p["height_m"] == pytest.approx(p["base_navd88_m"] + 71.6, abs=1e-6)
    assert p["height_lowered_from_m"] == 83.1
    L = next(x for x in json.loads((M0 / "manifest.json").read_text())["layers"] if x["layer"] == "landmarks")
    assert any(c["slug"] == "ferry_building_clock_tower" and c["status"] == "lowered to sourced in buildings" for c in L["checked"])


def _tower_cfg(tops):
    br = {"source_ids": ["x"], "tower_top_navd88_m": 229.18, "tower_top_tol_m": 3.0, "leg_base_m": [10.1, 16.5],
          "seabed_envelope_navd88_m": -20.0, "deck_width_m": 18.9, "cable_diameter_m": 0.924, "tol_height_m": 3.0, "tol_pos_m": 3.0}
    g = {"axis_origin_baytm": [4096.0 + 100, 4096.0 + 100], "axis_unit": [0.0, 1.0], "legs": [
        {"tower": "N", "leg": i, "centroid_baytm": [4096.0 + 100 + 10 * i, 4096.0 + 100], "extent_along_m": 12.0, "extent_across_m": 8.0, "top_max_m": t}
        for i, t in enumerate(tops)], "deck_v_m": 0.0, "cable_v_m": [0.0, 5.0], "profile": [
        {"u": 0.0, "deck": 70.0, "cable": [240.0, 240.0]}, {"u": 10.0, "deck": 70.0, "cable": [100.0, 100.0]}]}
    return br, g


def test_tower_top_uses_median_of_leg_tops(monkeypatch):
    br, g = _tower_cfg([233.25, 230.42])                    # the real N tower: max is 4.07 m over, median is 1.2 m over
    real = landmarks.load_config
    monkeypatch.setattr(landmarks, "load_config", lambda n: g if n == "ggb.json" else real(n))
    f, chk = landmarks._bridge("L0_2_2", {"bridge": br})
    tw = [x for x in f if x["properties"]["kind"] == "bridge_tower"]
    assert len(tw) == 2 and {x["properties"]["top_navd88_m"] for x in tw} == {229.18}
    assert {x["properties"]["height_source"] for x in tw} == {"official"}
    assert [c for c in chk if c["slug"] == "ggb_tower_n"][0]["top_used"] == "sourced"
    cab = [x for x in f if x["properties"]["kind"] == "bridge_cable"]
    assert cab and all(max(x["properties"]["y_navd88_m"]) <= 229.18 for x in cab)   # 240 m lidar sample capped at the tower top


def test_tower_far_from_sourced_still_uses_measured(monkeypatch):
    br, g = _tower_cfg([240.0, 241.0])
    real = landmarks.load_config
    monkeypatch.setattr(landmarks, "load_config", lambda n: g if n == "ggb.json" else real(n))
    f, chk = landmarks._bridge("L0_2_2", {"bridge": br})
    assert [c for c in chk if c["slug"] == "ggb_tower_n"][0]["top_used"] == "measured"
