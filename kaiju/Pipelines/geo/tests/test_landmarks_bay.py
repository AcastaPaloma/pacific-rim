"""T-104: Bay-wide landmark measurement and registry (synthetic point clouds; the committed data file is checked when present)."""
import json
import math

import numpy as np
import pytest

from kaiju_geo import bridge_measure as bm
from kaiju_geo import landmarks_bay as lb
from kaiju_geo.common import CONFIG

LINE = [(0.0, 0.0), (400.0, 0.0), (1000.0, 0.0)]


def _cloud(deck=60.0, tower_at=None, tower_top=200.0, seed=1):
    rng = np.random.default_rng(seed)
    n = 40000
    x = rng.uniform(0, 1000, n)
    z = rng.uniform(-15, 15, n)
    h = np.where(np.abs(z) <= 8, deck + rng.uniform(0, 3, n), 1.0)  # deck + vehicles, water elsewhere
    pc = {"x": x, "z": z, "h": h}
    if tower_at is not None:
        tx = rng.uniform(tower_at - 3, tower_at + 3, 4000)
        tz = rng.uniform(-3, 3, 4000)
        th = rng.uniform(deck + 30, tower_top, 4000)
        th[0] = tower_top
        pc = {k: np.concatenate([pc[k], v]) for k, v in (("x", tx), ("z", tz), ("h", th))}
    return pc


def test_locate_arclength_and_side():
    u, v = bm.locate(LINE, [250.0, 700.0], [5.0, -4.0])
    assert u[0] == pytest.approx(250.0) and u[1] == pytest.approx(700.0)
    assert v[0] == pytest.approx(5.0) and v[1] == pytest.approx(-4.0)


def test_deck_profile_p25_ignores_vehicles_and_water():
    rows = bm.deck_profile(LINE, _cloud(deck=60.0))
    d = [r["deck"] for r in rows if r["deck"] is not None]
    assert len(d) >= 45 and all(60.0 <= x <= 61.5 for x in d)
    s = lb.summarise_deck(rows)
    assert 60.0 <= s["deck_median_navd88_m"] <= 61.5 and s["n_bins"] == len(d)


def test_towers_found_with_top_and_position():
    pc = _cloud(tower_at=500.0, tower_top=200.0)
    t = bm.towers(LINE, pc, deck_ref=61.0, min_above=25.0)
    assert len(t) == 1 and t[0]["top_max_m"] == pytest.approx(200.0)
    assert t[0]["centroid_baytm"][0] == pytest.approx(500.0, abs=3.0)


def test_no_towers_on_flat_bridge():
    assert bm.towers(LINE, _cloud(), deck_ref=61.0) == []


def test_corridor_windows_cover_line():
    w = bm.corridor_windows([(0.0, 0.0), (1500.0, 800.0)], 100.0, 600.0)
    assert len(w) == 3 and min(b[0] for b in w) <= -100 and max(b[2] for b in w) >= 1600
    for a, b in zip(w[:-1], w[1:]):
        assert b[0] <= a[2]  # consecutive windows overlap or touch


def test_height_rule_and_pick():
    assert lb.height_tol(160.0) == 2.0 and lb.height_tol(326.0) == pytest.approx(3.26)
    assert lb.pick_height(160.0, 161.5)[:3] == (160.0, "official", "A")
    v, src, conf, d = lb.pick_height(160.0, 143.4)
    assert (v, src, conf) == (143.4, "lidar_3dep", "B") and d == pytest.approx(-16.6)
    assert lb.pick_height(160.0, None)[:3] == (160.0, "official", "B")


def test_point_in_poly_and_building_heights():
    poly = [(0, 0), (20, 0), (20, 20), (0, 20)]
    rng = np.random.default_rng(3)
    x = rng.uniform(-30, 50, 20000)
    z = rng.uniform(-30, 50, 20000)
    inside = lb.point_in_poly(poly, x, z)
    assert inside.sum() == int(((x >= 0) & (x <= 20) & (z >= 0) & (z <= 20)).sum())
    h = np.where(inside, 100.0, 5.0)
    m = lb.building_heights(poly, {"x": x, "z": z, "h": h})
    assert m["height_p99_m"] == pytest.approx(95.0, abs=0.01) and m["ground_ring_p10_navd88_m"] == pytest.approx(5.0)
    assert lb.building_heights(poly, {"x": np.array([100.0]), "z": np.array([100.0]), "h": np.array([1.0])}) is None


def test_runway_report_converts_feet_exactly():
    runs = [{"id": "w1", "tags": {"ref": "10L/28R"}, "pts": [(0.0, 0.0), (3618.0, 0.0)]}]
    r = lb.runway_report(runs, {"10L/28R": 11870, "1L/19R": 7650})
    assert r[0]["faa_length_m"] == pytest.approx(3617.98, abs=0.01) and r[0]["delta_m"] == pytest.approx(0.02, abs=0.01)
    assert r[1]["osm_ways"] == [] and r[1]["osm_length_m"] is None


def test_runway_pieces_and_zero_padded_refs_join():
    runs = [{"id": "w1", "tags": {"ref": "01L/19R"}, "pts": [(0.0, 0.0), (1000.0, 0.0)]},
            {"id": "w2", "tags": {"ref": "01L/19R"}, "pts": [(1000.0, 0.0), (2331.0, 0.0)]}]
    r = lb.runway_report(runs, {"1L/19R": 7650})
    assert r[0]["osm_ways"] == ["w1", "w2"] and r[0]["osm_length_m"] == pytest.approx(2331.0)


def test_group_towers_merges_legs_and_keeps_max():
    comps = [{"centroid_baytm": [0.0, 0.0], "top_max_m": 150.0, "extent_m": [5, 5], "u_m": 10.0},
             {"centroid_baytm": [12.0, 0.0], "top_max_m": 148.0, "extent_m": [5, 5], "u_m": 11.0},
             {"centroid_baytm": [700.0, 0.0], "top_max_m": 160.0, "extent_m": [5, 5], "u_m": 700.0}]
    g = bm.group_towers(comps, 40.0)
    assert len(g) == 2 and g[0]["n_legs"] == 2 and g[0]["top_max_m"] == 150.0 and g[0]["centroid_baytm"][0] == pytest.approx(6.0)


def test_peak_near_height_above_local_surface():
    rng = np.random.default_rng(5)
    x, z = rng.uniform(-30, 30, 30000), rng.uniform(-30, 30, 30000)
    h = np.full(x.shape, 40.0)
    h[np.hypot(x - 14, z) < 2] = 68.0  # tower 14 m off the nominal point
    m = lb.peak_near({"x": x, "z": z, "h": h}, (0.0, 0.0), 30.0, 10.0)
    assert m["peak_navd88_m"] == 68.0 and m["ground_navd88_m"] == pytest.approx(40.0) and m["height_m"] == pytest.approx(28.0)
    assert abs(m["peak_baytm"][0] - 14) < 3


def test_crane_clusters_and_decimate():
    cr = [{"pos": [0, 0]}, {"pos": [100, 0]}, {"pos": [5000, 0]}, {"pos": [5100, 50]}, {"pos": [200, 0]}]
    assert [len(c) for c in lb.crane_clusters(cr, 300.0)] == [3, 2]
    prof = [{"u": 10.0 + 20 * i, "deck": 50.0 + i, "n": 99} for i in range(10)] + [{"u": 300.0, "deck": None, "n": 1}]
    dec = lb.decimate(prof, 40.0)
    assert [r["u"] for r in dec] == [10.0, 50.0, 90.0, 130.0, 170.0]


# ------------------------------------------------------------------ registry and committed data
REG = json.loads((CONFIG / "landmarks_bay_registry.json").read_text())


def test_registry_every_figure_has_a_source():
    def walk(o):
        if isinstance(o, dict):
            if "v" in o and "unit" in o:
                yield o
            for v in o.values():
                yield from walk(v)
        elif isinstance(o, list):
            for v in o:
                yield from walk(v)
    figs = list(walk(REG))
    assert len(figs) > 30
    for f in figs:
        assert f["src"] in REG["sources"], f
    for s in REG["sources"].values():
        assert {"licence", "attribution", "url", "item", "fetched_utc"} <= set(s)


def test_registry_covers_r100_section4():
    slugs = {b["slug"] for b in REG["bridges"]}
    assert {"bay_bridge_w", "bay_bridge_e", "san_mateo_hayward", "richmond_san_rafael", "dumbarton", "dumbarton_rail", "carquinez", "benicia_martinez", "antioch"} <= slugs
    assert {a["slug"] for a in REG["airports"]} == {"sfo", "oak"}
    assert all(b["lidar"] for b in REG["bridges"])
    assert REG["cranes"]["pos_tol_m"] == 50 and len(REG["buildings"]) >= 5


DOC = CONFIG / "landmarks_bay.json"


@pytest.mark.skipif(not DOC.exists(), reason="landmarks_bay.json not baked yet")
def test_committed_doc_is_consistent_with_registry():
    d = json.loads(DOC.read_text())
    assert d["spec"] == "landmarks_bay@v1"
    assert {b["slug"] for b in d["bridges"]} == {b["slug"] for b in REG["bridges"]}
    for b in d["bridges"]:
        for w in b["ways"]:
            assert w["length_m"] > 0
        for t in b["towers"]:
            assert {"slug", "pos_baytm", "top_navd88_m", "height_status"} <= set(t)
            assert math.isfinite(t["top_navd88_m"])
    rows = lb.check_rows(d)
    assert len({r["slug"] for r in rows}) == len(rows)
    assert all(r["tol_pos_m"] in (5.0, 50) for r in rows)
    json.dumps(d)
