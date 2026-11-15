"""T-023 step 1: Golden Gate Bridge landmark features measured from 3DEP lidar (R-020 §3)."""
import json
import math

import pytest

from kaiju_geo import landmarks
from kaiju_geo.common import TILES_ROOT, load_config

T = TILES_ROOT / "L0"
SCHEMA = set(landmarks.FIELDS)


def _feats(tile):
    p = T / tile / "landmarks.geojson"
    return json.loads(p.read_text())["features"] if p.exists() else []


def _all():
    return _feats("26_37") + _feats("26_38") + _feats("27_37")


def _kind(kind):
    return [f for f in _all() if f["properties"]["kind"] == kind]


def test_schema_and_ids():
    fs = _all()
    assert len(fs) == 10
    for f in fs:
        p = f["properties"]
        assert SCHEMA <= set(p)
        assert p["bid"].startswith("landmark:ggb_") and p["part_of"] is None
        assert p["confidence"] in ("A", "B") and p["height_source"] in ("official", "lidar_3dep")
        assert p["base_navd88_m"] <= p["top_navd88_m"]
    assert len({f["properties"]["lid"] for f in fs}) == len(fs)
    for f in _kind("bridge_deck"):
        assert f["properties"]["width_m"] == 18.9 and len(f["properties"]["y_navd88_m"]) == len(f["geometry"]["coordinates"])
    for f in _kind("bridge_cable"):
        assert f["properties"]["diameter_m"] == 0.924 and len(f["properties"]["y_navd88_m"]) == len(f["geometry"]["coordinates"])


def test_four_legs_with_sourced_minimum_footprint():
    legs = _kind("bridge_tower")
    assert len(legs) == 4
    for f in legs:
        p = f["properties"]
        ring = f["geometry"]["coordinates"][0]
        a = math.dist(ring[0], ring[1])
        b = math.dist(ring[1], ring[2])
        assert max(a, b) >= 16.5 - 1e-6 and min(a, b) >= 10.1 - 1e-6
        assert p["base_navd88_m"] == -20.0 and abs(p["top_navd88_m"] - 229.18) <= 5.0


def test_span_within_2m_of_sourced():
    cs = {}
    for f in _kind("bridge_tower"):
        ring = f["geometry"]["coordinates"][0][:4]
        tw = f["properties"]["lid"].split(":")[1]
        cs.setdefault(tw, []).append((sum(q[0] for q in ring) / 4, sum(q[1] for q in ring) / 4))
    c = {k: (sum(q[0] for q in v) / len(v), sum(q[1] for q in v) / len(v)) for k, v in cs.items()}
    span = math.dist(c["ggb_tower_s"], c["ggb_tower_n"])
    assert abs(span - 1280.2) <= 2.0, span


def test_deck_vs_sourced_at_towers_and_midspan_delta_reported():
    r = json.loads((TILES_ROOT.parents[4] / "evidence" / "T-023" / "ggb_lidar_measure.json").read_text())
    d = r["delta"]
    # towers agree with the sourced 76.78 m within 3 m; midspan deviates (reported, R-020 Q-NEW-d): 3DEP shows the roadway
    # at about 81.7 m, 12.8 m above the sourced 68.85 m, with the cable only about 4.5 m above it.
    assert abs(d["deck_tower_S"]) <= 3.0 and abs(d["deck_tower_N"]) <= 3.0
    assert d["deck_mid"] == pytest.approx(r["deck_midspan_m"] - 68.85, abs=0.01)
    assert abs(d["deck_mid"]) > 3.0 and 0 < r["midspan_cable_minus_deck_m"][0] < 8


def test_cut_nodes_shared_at_tile_edge():
    for slug in ("ggb_deck", "ggb_cable_a", "ggb_cable_b"):
        a = [f for f in _feats("26_37") if f["properties"]["lid"].startswith(f"lm:{slug}:")][0]
        b = [f for f in _feats("26_38") if f["properties"]["lid"].startswith(f"lm:{slug}:")][0]
        na, nb = a["geometry"]["coordinates"][-1], b["geometry"]["coordinates"][0]
        assert na == nb and na[1] == 77824.0
        assert a["properties"]["y_navd88_m"][-1] == b["properties"]["y_navd88_m"][0]
        for f, t in ((a, 26), (b, 26)):
            assert all(math.floor(x / 2048) == 26 for x, _ in f["geometry"]["coordinates"])
        assert all(z <= 77824.0 for _, z in a["geometry"]["coordinates"]) and all(z >= 77824.0 for _, z in b["geometry"]["coordinates"])


def test_cut_polyline_multi_edge_and_shared_node():
    pts = [(2000.0, 2000.0, 1.0), (2100.0, 2100.0, 3.0), (2200.0, 1900.0, 5.0)]
    pcs = landmarks.cut_polyline(pts)
    assert [t for t, _ in pcs] == ["L0_0_0", "L0_1_1", "L0_1_0"]
    assert pcs[0][1][-1] == pcs[1][1][0] and pcs[1][1][-1] == pcs[2][1][0]


def test_manifest_lists_bridge_sources():
    m = json.loads((T / "26_37" / "manifest.json").read_text())
    lay = [l for l in m["layers"] if l["layer"] == "landmarks"][0]
    assert lay["count"] == 5 and {s["licence"] for s in lay["sources"]} == {"LicenseRef-facts-cited", "US-Gov-PD"}
