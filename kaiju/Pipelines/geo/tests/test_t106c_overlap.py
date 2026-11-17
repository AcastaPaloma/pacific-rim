"""T-106c: overlapping OSM footprints are resolved identically by every tile that sees them (M-005 collider_overlap)."""
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from shapely.geometry import box  # noqa: E402

from kaiju_geo import vector  # noqa: E402

CFG = {"min_overlap_m2": 0.05, "contained_frac": 0.9, "metres_per_level": 4.22}


def b(oid, g, **tags):
    return {"oid": oid, "geom": g, "tags": {"building": "yes", **tags}}


def test_podium_under_tower_is_clipped_not_the_tower():
    tower = b("osm:w1", box(0, 0, 10, 10), height="98")
    podium = b("osm:w2", box(-20, -5, 15, 15), height="13")
    r = vector.resolve_overlaps([tower, podium], CFG)
    assert "osm:w1" not in r and "geom" in r["osm:w2"]
    assert r["osm:w2"]["geom"].intersection(tower["geom"]).area < 0.05
    assert r["osm:w2"]["geom"].area == pytest.approx(35 * 20 - 100, abs=0.01)


def test_duplicate_way_inside_relation_is_excluded():
    rel = b("osm:r5", box(0, 0, 10, 10))
    way = b("osm:w9", box(0, 0, 10, 10))
    r = vector.resolve_overlaps([rel, way], CFG)
    assert list(r) == ["osm:w9"] and r["osm:w9"]["excluded"] == "overlap:inside osm:r5"


def test_untagged_big_shed_loses_to_tagged_houses():
    shed = b("osm:w3", box(0, 0, 100, 40))
    house = b("osm:w4", box(10, 10, 20, 20), **{"building:levels": "1"})
    r = vector.resolve_overlaps([shed, house], CFG)
    assert set(r) == {"osm:w3"} and r["osm:w3"]["geom"].area == pytest.approx(4000 - 100, abs=0.01)


def test_decision_is_order_independent():
    a, c, d = b("osm:w1", box(0, 0, 10, 10), height="5"), b("osm:w2", box(5, 5, 15, 15), height="7"), b("osm:w3", box(12, 0, 20, 8))
    r1 = vector.resolve_overlaps([a, c, d], CFG)
    r2 = vector.resolve_overlaps([d, c, a], CFG)
    assert r1.keys() == r2.keys()
    for k in r1:
        assert r1[k].get("excluded") == r2[k].get("excluded")
        if "geom" in r1[k]:
            assert r1[k]["geom"].equals(r2[k]["geom"])


def test_touching_footprints_and_excluded_tags_are_left_alone():
    a, c = b("osm:w1", box(0, 0, 10, 10)), b("osm:w2", box(10, 0, 20, 10))
    assert vector.resolve_overlaps([a, c], CFG) == {}
