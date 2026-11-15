"""T-105: infra@v1 (F-044) classification, ownership, determinism and tile files (synthetic OSM tags; the cached PBF is not needed)."""
import json
import random

import pytest
from shapely.geometry import LineString, Point

from kaiju_geo import infra
from kaiju_geo.common import canonical_json, load_config, sha256_bytes

SRC = {"product": "OSM", "item": "x", "url": "u", "licence": "ODbL-1.0", "attribution": "a", "fetched_utc": "t", "source_sha256": "s"}


def test_classify_selects_the_r100_classes():
    c = infra.classify
    assert c({"power": "tower"}, True) == "power_tower"
    assert c({"power": "tower"}, False) is None
    assert c({"power": "pole"}, True) is None
    assert c({"power": "line"}, False) == "power_line" and c({"power": "minor_line"}, False) == "power_line"
    assert c({"power": "line", "location": "underground"}, False) is None
    assert c({"power": "substation"}, False) == "substation" and c({"power": "substation"}, True) == "substation"
    assert c({"power": "plant"}, False) == "power_plant"
    assert c({"power": "generator"}, True) is None
    assert c({"man_made": "crane"}, True) == "port_crane" and c({"man_made": "crane"}, False) == "port_crane"
    assert c({"aeroway": "runway"}, False) == "runway" and c({"aeroway": "taxiway"}, False) is None
    assert c({"aeroway": "terminal"}, False) == "terminal" and c({"aeroway": "hangar"}, False) == "hangar"
    assert c({"aeroway": "control_tower"}, True) == "control_tower"
    assert c({"bridge": "yes", "highway": "motorway"}, False) == "bridge"
    assert c({"bridge": "viaduct", "railway": "rail"}, False) == "bridge"
    assert c({"bridge": "no", "highway": "motorway"}, False) is None
    assert c({"bridge": "yes"}, False) is None                       # no road or rail on it
    assert c({"bridge": "yes", "highway": "path", "man_made": "bridge"}, False) is None


def test_kind_ids_match_the_csharp_enum_values():
    ids = {k: v["id"] for k, v in load_config("infra.json")["kinds"].items()}
    assert ids == {"bridge": 1, "power_tower": 2, "power_line": 3, "substation": 4, "power_plant": 5, "port_crane": 6,
                   "runway": 7, "terminal": 8, "hangar": 9, "control_tower": 10}
    assert set(ids) == set(infra.KINDS)


def test_values_only_from_tags_or_geometry():
    g = LineString([(0, 0), (30, 40)])
    p = infra.properties("bridge", {"bridge": "yes", "highway": "primary", "lanes": "4", "layer": "1", "name": "Secret Bridge"}, g, {"w5": "x"}, "w5")
    assert p["length_m"] == 50.0 and p["lanes"] == 4.0 and p["layer"] == 1 and p["landmark"] == "x" and p["width_m"] is None
    assert "name" not in p and "Secret" not in json.dumps(p)
    t = infra.properties("power_tower", {"power": "tower"}, Point(1, 2), None, "n1")
    assert t["height_m"] is None
    assert infra.voltage_v("115000;230000") == 230000.0 and infra.voltage_v("high") is None
    assert infra.length_m("12 m") == 12.0 and infra.length_m("100 ft") == pytest.approx(30.48)


def test_owner_rule_midpoint_of_line_and_tile_assignment():
    g = LineString([(2000, 100), (2100, 100), (6100, 100)])          # half the length is at x = 4050 (tile 1 | 2)
    t = infra.owner_tile(g)
    assert (t.i, t.j) == (1, 0)
    assert infra.owner_tile(Point(-5, 10)) is None and infra.owner_tile(Point(10, 68 * 2048 + 1)) is None


def _feats():
    f = []
    for i, (x, z) in enumerate([(100, 100), (3000, 100), (3010, 100), (100, 5000)]):
        f.append(infra.feature("power_tower", f"n{i}", {"power": "tower", "height": str(40 + i)}, Point(x, z)))
    f.append(infra.feature("bridge", "w9", {"bridge": "yes", "highway": "motorway"}, LineString([(0, 50), (900, 50)])))
    return f


def test_by_tile_is_order_independent_and_deterministic():
    feats = _feats()
    a = infra.by_tile(feats)
    shuffled = feats[:]
    random.Random(7).shuffle(shuffled)
    b = infra.by_tile(shuffled)
    assert a == b and set(a) == {"L0_00_00", "L0_01_00", "L0_00_02"}
    fp = infra.fingerprint("pbf", {})
    d1, m1 = infra.tile_files("L0_00_00", a["L0_00_00"], SRC, fp)
    d2, m2 = infra.tile_files("L0_00_00", b["L0_00_00"], SRC, fp)
    assert d1 == d2 and m1 == m2
    man = json.loads(m1)
    assert man["sha256"] == sha256_bytes(d1) and man["count"] == 2 and man["kind_counts"] == {"bridge": 1, "power_tower": 1}
    assert man["spec"] == "infra@v1" and man["sources"][0]["licence"] == "ODbL-1.0"
    doc = json.loads(d1)
    assert doc["spec"] == "infra@v1" and [x["id"] for x in doc["features"]] == sorted(x["id"] for x in doc["features"])
    assert d1.decode() == canonical_json(doc) + "\n"


def test_fingerprint_changes_with_inputs():
    assert infra.fingerprint("a", {}) != infra.fingerprint("b", {})
    assert infra.fingerprint("a", {}) != infra.fingerprint("a", {"w1": "s"})
    assert infra.fingerprint("a", {}) == infra.fingerprint("a", {})


def test_polygon_kinds_become_polygons_only_when_closed():
    ring = [(0, 0), (10, 0), (10, 10), (0, 10), (0, 0)]
    assert infra.to_geometry("substation", ring, True).geom_type == "Polygon"
    assert infra.to_geometry("substation", ring[:-1], False).geom_type == "LineString"
    assert infra.to_geometry("runway", ring, True).geom_type == "LineString"
    assert infra.to_geometry("power_tower", [(1, 2)], False).geom_type == "Point"
