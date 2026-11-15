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
