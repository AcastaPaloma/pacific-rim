"""T-106: M-005@v2 Bay-scale checker (pure aggregation functions and a two-tile synthetic end-to-end run)."""
import json
import os
import sys

import numpy as np
import pytest
from shapely.geometry import box, mapping

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import m005_bay as B  # noqa: E402
import m005_check as M  # noqa: E402
from kaiju_geo.common import load_config, parse_tile  # noqa: E402

CFG = load_config("m005_bay.json")
TH = M.V12_TH


# ---------------------------------------------------------------- pure functions
def test_thresholds_are_the_r100_values():
    t = CFG["thresholds"]
    assert t["default_height_share_bay"] == 0.02 and t["default_height_share_county"] == 0.02
    assert (t["landmark_a_vs_import_m"], t["landmark_b_p95_m"], t["landmark_b_max_m"]) == (0.5, 5.0, 10.0)
    assert t["crane_pos_m"] == 50.0 and t["summit_pos_m"] == 15.0 and t["shallow_share"] == 0.66 and t["shallow_tol"] == 0.03
    assert TH["default_height_share_union"] == 0.02 and TH["seam_terrain_p99_E_m"] == 0.20 and TH["seam_terrain_max_E_m"] == 0.50


def test_every_bathymetry_box_has_a_source_and_unsourced_ones_are_not_gated():
    names = [b["name"] for b in CFG["bathymetry_boxes"]]
    assert len(names) == 9 and len(set(names)) == 9
    for b in CFG["bathymetry_boxes"]:
        w, s, e, n = b["box"]
        assert w < e and s < n and b["source"]
        assert b["gated"] == ("rule" in b)
    assert {b["name"] for b in CFG["bathymetry_boxes"] if b["gated"]} == {"golden_gate_channel", "oakland_inner_harbor", "san_pablo_shoal"}


def test_hist_quantile_and_summit_parse():
    h = np.zeros(B.HB_N, np.int64)
    vals = np.array([-20.0, -15.0, -10.0, -5.0, -1.0])
    np.add.at(h, ((vals - B.HB_MIN) / B.HB_STEP).astype(int), 1)
    assert B.hist_quantile(h, 0.5) == pytest.approx(-10.0, abs=0.02)
    assert B.hist_quantile(h, 0.05) == pytest.approx(-20.0, abs=0.02)
    assert B.hist_quantile(np.zeros(B.HB_N, np.int64), 0.5) is None
    assert B.summit_elev("summit 277.8 m NAVD88") == 277.8 and B.summit_elev("OSM") is None


def _tile(tid, area, dflt, by_county, h=0.0, bounds=None):
    t = parse_tile(tid)
    b = bounds or t.bounds
    e = {k: np.full(2049, h) for k in "wesn"}
    return {"tile": tid, "structures": 3, "area": area, "default_area": dflt, "by_county": by_county, "bounds": b, "edges": e}


def test_share_rows_gate_bay_county_and_tile():
    R = [_tile("L0_30_36", 1e6, 1e4, {"A": [1e6, 1e4, 3]}), _tile("L0_31_36", 2e6, 1e4, {"A": [1.5e6, 1e4, 2], "B": [5e5, 0.0, 1]}),
         _tile("L0_32_36", 5e3, 5e3, {"C": [5e3, 5e3, 1]})]
    rows, table = B.share_rows(R, TH, CFG["thresholds"])
    by = {x["check"]: x for x in rows}
    assert by["default_height_share_bay"]["result"] == "pass"            # 25,000 / 3,005,000 = 0.83 %
    assert by["default_height_share_county"]["result"] == "pass"
    cs = {c["county"]: c for c in by["default_height_share_county"]["counties"]}
    assert cs["C"]["result"] == "reported" and cs["A"]["result"] == "pass"   # < 10,000 m2: reported, never gated
    assert by["default_height_share_tile"]["value"] == {"tiles_gated": 2, "tiles_over": 0}
    assert len(table) == 3
    R[0] = _tile("L0_30_36", 1e6, 5e4, {"A": [1e6, 5e4, 3]})            # 5 % in county A and the tile; Bay stays below 2 %
    R[1]["by_county"]["A"] = [1.5e6, 1e4, 2]
    rows, _ = B.share_rows(R, TH, CFG["thresholds"])
    by = {x["check"]: x for x in rows}
    assert by["default_height_share_county"]["result"] == "fail" and by["default_height_share_county"]["value"]["counties_over"] == ["A"]
    assert by["default_height_share_tile"]["result"] == "pass"              # exactly 5 % is not over the per-tile threshold
    R[0] = _tile("L0_30_36", 1e6, 6e4, {"A": [1e6, 6e4, 3]})
    by = {x["check"]: x for x in B.share_rows(R, TH, CFG["thresholds"])[0]}
    assert by["default_height_share_tile"]["result"] == "fail" and by["default_height_share_tile"]["failing"][0]["tile"] == "L0_30_36"


def test_bay_wide_share_fails_above_2_percent():
    R = [_tile("L0_30_36", 1e6, 3e4, {"A": [1e6, 3e4, 3]})]
    assert {x["check"]: x for x in B.share_rows(R, TH, CFG["thresholds"])[0]}["default_height_share_bay"]["result"] == "fail"


def test_seam_rows_pass_and_fail():
    ok = B.seam_rows([_tile("L0_30_36", 1, 0, {}), _tile("L0_31_36", 1, 0, {}), _tile("L0_30_37", 1, 0, {})], TH)
    assert {x["check"]: x["result"] for x in ok} == {"terrain_seam_all_edges": "pass", "edge_vertex_gap_all_edges": "pass"} and ok[0]["edges"] == 2
    a, b = _tile("L0_30_36", 1, 0, {}, h=0.0), _tile("L0_31_36", 1, 0, {}, h=0.0)
    b["edges"]["w"] = np.full(2049, 0.6)                                     # 0.6 m step at the shared edge > 0.50 m max
    bad = B.seam_rows([a, b], TH)
    assert bad[0]["result"] == "fail" and bad[0]["failing_edges"][0]["edge"] == "L0_30_36|L0_31_36"
    c = _tile("L0_31_36", 1, 0, {}, bounds=(1.0 + 31 * 2048.0, 36 * 2048.0, 32 * 2048.0, 37 * 2048.0))   # vertex gap 1 m
    assert B.seam_rows([a, c], TH)[1]["result"] == "fail"
    assert B.seam_rows([a], TH)[0]["result"] == "error"                       # no shared edge: never a pass


def test_collider_overlap_helpers():
    from shapely import wkb
    a = (("L0_30_36", "osm:1", box(0, 0, 10, 10).wkb))
    b = (("L0_31_36", "osm:2", box(5, 5, 15, 15).wkb))
    c = (("L0_32_36", "osm:3", box(100, 100, 110, 110).wkb))
    ov = B.pair_overlaps([a, b, c], 0.05)
    assert len(ov) == 1 and ov[0]["area_m2"] == 25.0
    assert B.pair_overlaps([a, ("L0_30_36", "osm:9", box(1, 1, 3, 3).wkb)], 0.05) == []     # same owner tile: not a cross-tile overlap
    w, n = B.clipped_area_worst([("L0_00_00", "osm:x", box(2040.0, 100.0, 2060.0, 120.0).wkb), ("L0_00_00", "osm:y", box(10, 10, 20, 20).wkb)])
    assert n == 1 and w < 1e-9
    assert wkb.loads(a[2]).area == 100.0


def test_licence_rows():
    good = {"n_missing": 0, "missing": [], "licences": ["ODbL-1.0"], "attributions": ["OpenStreetMap contributors", "USGS 3DEP", "NOAA NCEI CUDEM", "DataSF"]}
    R = [{"tile": "L0_30_36", "licence": good, "height_source_counts": {"datasf": 3}}]
    assert B.licence_rows(R)["result"] == "pass"
    R[0]["licence"] = dict(good, n_missing=1, missing=[["buildings", "x", ["licence"]]])
    assert B.licence_rows(R)["result"] == "fail"
    R[0]["licence"] = dict(good, attributions=["OpenStreetMap contributors"])
    assert B.licence_rows(R)["result"] == "fail"


def test_shallow_row():
    R = [{"bay": {"cells_in_polygon": 10, "wet": 1000, "wet_below_limit": 640}}, {"bay": {"cells_in_polygon": 10, "wet": 1000, "wet_below_limit": 700}}]
    assert B.shallow_row(R, CFG)["result"] == "pass"           # 67 %
    R[0]["bay"]["wet_below_limit"] = 100
    assert B.shallow_row(R, CFG)["result"] == "fail"
    assert B.shallow_row([{}], CFG)["result"] == "error"


def test_bathymetry_rows_gated_and_reported():
    def reg(vals):
        h = np.zeros(B.HB_N, np.int64)
        np.add.at(h, ((np.array(vals) - B.HB_MIN) / B.HB_STEP).astype(int), 1)
        return {"hist": h, "min": float(min(vals)), "n": len(vals)}
    R = [{"tile": "T", "regions": {"golden_gate_channel": reg([-110.0] * 20 + [-105.0] * 80), "oakland_inner_harbor": reg([-15.2] * 50),
                                   "san_pablo_shoal": reg([-2.8] * 50), "sf_bar": reg([-12.0] * 10)}}]
    by = {x["check"]: x for x in B.bathy_rows(R, CFG)}
    assert by["bathymetry_golden_gate_channel"]["result"] == "pass" and by["bathymetry_oakland_inner_harbor"]["result"] == "pass"
    assert by["bathymetry_sf_bar"]["result"] == "reported" and by["bathymetry_carquinez_strait"]["result"] == "error"
    R[0]["regions"]["oakland_inner_harbor"] = reg([-12.0] * 50)
    R[0]["regions"]["golden_gate_channel"] = reg([-90.0] * 50)             # not deep enough: min above -109 and p5 above -100
    by = {x["check"]: x for x in B.bathy_rows(R, CFG)}
    assert by["bathymetry_oakland_inner_harbor"]["result"] == "fail" and by["bathymetry_golden_gate_channel"]["result"] == "fail"


def test_landmark_rows_summary_with_synthetic_bake():
    doc = load_config("landmarks_bay.json")
    ref = json.loads((B.GLOBAL_ROOT / "landmarks.json").read_text())
    rows = B.lb.check_rows(doc)
    t = next(c for c in rows if c["kind"] == "tower")
    tid = f"L0_{int(t['pos_baytm'][0] // 2048):02d}_{int(t['pos_baytm'][1] // 2048):02d}"
    R = [{"tile": tid, "landmarks": [{"lid": f"lm:{t['slug']}:t", "bid": f"landmark:{t['slug']}:t", "kind": "bridge_tower", "part_of": None, "cx": t["pos_baytm"][0],
                                      "cz": t["pos_baytm"][1], "base": -20.0, "top": t["height_ref_m"], "gtype": "Polygon"}], "bld": {}, "summits": {}}]
    lrows, summ = B.landmark_m0051(R, doc, ref, CFG)
    got = {r_["id"]: r_ for r_ in lrows}
    assert got[t["slug"]]["result"] == "pass" and got[t["slug"]]["a_vs_import_m"] == 0.0 and got[t["slug"]]["height_ok"]
    assert sum(1 for r_ in lrows if not r_["present"]) == len(lrows) - 1        # everything not in the synthetic bake is reported missing
    assert {x["check"]: x["result"] for x in summ}["M-005.1_present"] == "fail"
    R[0]["landmarks"][0]["cx"] += 3.0                                              # 3 m off the import: (a) <= 0.5 m fails
    lrows, summ = B.landmark_m0051(R, doc, ref, CFG)
    assert {x["check"]: x["result"] for x in summ}["M-005.1_a_vs_import"] == "fail"


# ---------------------------------------------------------------- synthetic end-to-end
def _write_tile(root, tid, h16=0, feats=(), sources=None):
    t = parse_tile(tid)
    d = root / t.folder
    d.mkdir(parents=True, exist_ok=True)
    np.full((2049, 2049), h16, dtype="<u2").tofile(d / "terrain.r16")
    from PIL import Image
    Image.fromarray(np.zeros((2048, 2048), np.uint8), mode="L").save(d / "water.png")
    src = sources or [{"product": "p", "item": "i", "url": "u", "licence": "ODbL-1.0", "attribution": "OpenStreetMap contributors USGS NOAA DataSF", "fetched_utc": "t", "source_sha256": "s"}]
    man = {"tile": tid, "bounds_baytm": list(t.bounds), "water_level_navd88_m": 0.8, "layers": [
        {"layer": "terrain", "samples": 2049, "h_min_m": 0.0, "h_max_m": 10.0}, {"layer": "terrain_r16", "h_min_m": 0.0, "h_max_m": 10.0},
        {"layer": "water", "water_fraction": 0.0}, {"layer": "buildings", "count": len(feats), "height_source_counts": {"osm_height": len(feats)}, "sources": src}]}
    (d / "manifest.json").write_text(json.dumps(man))
    (d / "buildings.geojson").write_text(json.dumps({"type": "FeatureCollection", "features": list(feats)}))


def _bf(bid, g, src="osm_height"):
    return {"type": "Feature", "id": bid, "geometry": mapping(g), "properties": {
        "bid": bid, "part_of": None, "base_navd88_m": 1.0, "height_m": 10.0, "height_source": src, "excluded": None, "has_parts": False}}


def _manifest(tmp_path, ids):
    p = tmp_path / "bay_manifest.json"
    p.write_text(json.dumps({"tiles": [{"id": i, "class": "urban"} for i in ids], "world_box_lonlat": [-123.0, 37.0, -121.0, 38.5]}))
    return p


@pytest.fixture
def one_county(monkeypatch):
    monkeypatch.setattr(B, "load_counties", lambda world=None: [("Test County", box(-1e6, -1e6, 1e6, 1e6))])


def _run(tmp_path, root, ids):
    return B.run(root=root, workers=1, only=ids, manifest_path=_manifest(tmp_path, ids), progress=False)


def test_end_to_end_two_tiles(tmp_path, one_county):
    root = tmp_path / "tiles"
    a, b = parse_tile("L0_40_40"), parse_tile("L0_41_40")
    _write_tile(root, "L0_40_40", feats=[_bf("osm:w1", box(a.x0 + 100, a.z0 + 100, a.x0 + 150, a.z0 + 150)),
                                         _bf("osm:w2", box(a.x0 + 300, a.z0 + 100, a.x0 + 400, a.z0 + 200), src="default"),
                                         _bf("osm:w3", box(a.x0 + 2030, a.z0 + 500, a.x0 + 2060, a.z0 + 540))])  # crosses the east edge
    _write_tile(root, "L0_41_40", feats=[_bf("osm:w4", box(b.x0 + 5, b.z0 + 505, b.x0 + 15, b.z0 + 515))])      # inside the overhang of w3
    rep = _run(tmp_path, root, ["L0_40_40", "L0_41_40"])
    by = {x["check"]: x for x in rep["rows"]}
    assert rep["tiles"] == 2 and by["terrain_seam_all_edges"]["result"] == "pass" and by["bid_ownership"]["result"] == "pass"
    assert by["collider_overlap"]["result"] == "fail" and by["collider_overlap"]["pairs_over_threshold"] == 1     # w3 overhangs onto w4
    assert by["clipped_area"]["result"] == "pass" and by["clipped_area"]["footprints_crossing_an_edge"] == 1
    sh = by["default_height_share_bay"]
    assert sh["structures"] == 4 and sh["value"] == pytest.approx(10000.0 / (2500.0 + 10000.0 + 1200.0 + 100.0), abs=1e-4) and sh["result"] == "fail"
    assert by["default_height_share_county"]["counties"][0]["county"] == "Test County"
    assert by["landmarks"]["result"] == "not_evaluated"
    assert by["licence_credits"]["result"] == "pass"
    assert by["M-005.1_present"]["result"] == "fail"                    # no landmark of the registry is in these two tiles: never a pass
    assert all(by[k]["result"] == "not_run" for k in CFG["not_run"])
    assert rep["result"] == "error" and by["bathymetry_golden_gate_channel"]["result"] == "error"   # boxes outside the tile set have no data: never a pass


def test_end_to_end_unbaked_tile_is_an_error_not_a_pass(tmp_path, one_county):
    rep = _run(tmp_path, tmp_path / "empty", ["L0_40_40"])
    assert rep["result"] == "error" and rep["rows"][0]["check"] == "input"


def test_end_to_end_bad_seam_and_misowned_building(tmp_path, one_county):
    root = tmp_path / "tiles"
    a = parse_tile("L0_40_40")
    _write_tile(root, "L0_40_40", h16=0)
    _write_tile(root, "L0_41_40", h16=20000)                              # 3 m step at the shared edge
    _write_tile(root, "L0_42_40", h16=20000, feats=[_bf("osm:w9", box(a.x0, a.z0, a.x0 + 10, a.z0 + 10))])   # footprint owned by another tile
    rep = _run(tmp_path, root, ["L0_40_40", "L0_41_40", "L0_42_40"])
    by = {x["check"]: x for x in rep["rows"]}
    assert by["terrain_seam_all_edges"]["result"] == "fail" and by["terrain_seam_all_edges"]["failing_edges"][0]["edge"] == "L0_40_40|L0_41_40"
    assert by["bid_ownership"]["result"] == "fail" and by["bid_ownership"]["value"]["not_in_owner_tile"] == 1
    assert rep["result"] != "pass" and rep["counts"]["fail"] >= 2


def test_diagnostics_in_rows():
    a, b = _tile("L0_30_36", 1e6, 1e4, {"A": [1e6, 1e4, 1]}), _tile("L0_31_36", 1, 0, {})
    a["src_by_county"] = {"A": {"default": 1e4, "lidar_3dep": 9.9e5}}
    a["terrain"], b["terrain"] = {"placeholder_nodes": 12}, {}
    b["edges"]["w"] = np.full(2049, 0.6)
    assert B.seam_rows([a, b], TH)[0]["failing_edges"][0]["placeholder_nodes"] == [12, 0]
    c = {x["check"]: x for x in B.share_rows([a], TH, CFG["thresholds"])[0]}["default_height_share_county"]["counties"][0]
    assert c["area_share_by_height_source"] == {"lidar_3dep": 0.99, "default": 0.01}
    R = [{"bay": {"cells_in_polygon": 5, "wet": 100, "wet_below_limit": 66, "surface_below_msl": 200, "surface_below_msl_shallow": 150}}]
    v = B.shallow_row(R, CFG)["value"]
    assert v["share_below_5m"] == 0.66 and v["supplementary_not_gated"]["share_below_5m"] == 0.75
