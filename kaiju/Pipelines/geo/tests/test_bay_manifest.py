"""bay_manifest@v1 (T-100): classification rules, committed-manifest invariants, determinism (M-100 draft)."""
import json
from pathlib import Path

import pytest

import bay_manifest as bm
from kaiju_geo.common import CACHE, Tile, lonlat_to_baytm, tile_of

CFG = bm.load_bay()
MAN_PATH = bm.OUT_PATH
needs_cache = pytest.mark.skipif(not bm.WATER_ZIP.exists(), reason="geo cache (OSM water polygons) not present")


@pytest.fixture(scope="module")
def man():
    return json.loads(MAN_PATH.read_text(encoding="utf-8"))


@pytest.fixture(scope="module")
def by_id(man):
    return {t["id"]: t for t in man["tiles"]}


# ---- pure rules
@pytest.mark.parametrize("land,bld,bay,cls", [
    (1.0, 5000, 0.0, "urban"), (1.0, 100, 0.0, "urban"), (1.0, 99, 0.0, "rural"), (0.001, 0, 0.0, "rural"),
    (0.0009, 500, 1.0, "water_only"), (0.0, 0, 0.5, "water_only"), (0.0, 0, 0.49, "ocean_only"), (0.0, 0, 0.0, "ocean_only")])
def test_classify_thresholds(land, bld, bay, cls):
    assert bm.classify(land, bld, bay, CFG) == cls


def test_dep3_key_is_upper_left_utm_tile():
    x, z = lonlat_to_baytm(-122.396914, 37.789775)  # Salesforce Tower, R-004 section 5
    assert bm.dep3_key_polygon_baytm("x55y419").contains(__import__("shapely").geometry.Point(x, z))
    assert not bm.dep3_key_polygon_baytm("x55y420").contains(__import__("shapely").geometry.Point(x, z))


def test_world_polygon_matches_r004_extent():
    minx, minz, maxx, maxz = bm.world_polygon_baytm(tuple(CFG["world_box_lonlat"])).bounds
    assert 4000 < minx < 4600 and 137000 < maxx < 138000 and 2900 < minz < 3500 and 136000 < maxz < 137000  # R-004 2.1


def test_snapshots_pinned():
    sn = CFG["snapshots"]
    assert len(sn["cudem"]) == 26 and len(sn["dep3_1m"]) == 151 and len(sn["lidar_ept"]) == 25
    assert 4.9e9 < sum(c["bytes"] for c in sn["cudem"]) < 5.1e9           # R-004 1.2: 5.00 GB
    assert 33.0e9 < sum(d["bytes"] for d in sn["dep3_1m"].values()) < 34.5e9  # R-004 1.3: 33.8 GB
    assert sum(sn["osm_buildings"].values()) > 1_800_000


# ---- committed manifest
def test_schema_and_counts(man):
    assert man["spec"] == "bay_manifest@v1" and man["schema_version"] == 1 and man["crs"] == "kaiju:BayTM@v1"
    c = man["counts"]
    assert sum(c[k] for k in bm.CLASSES) == c["total"] == len(man["tiles"])
    assert c["total"] + c["outside"] == c["grid"] == 68 * 68
    assert len(man["fingerprint"]) == 64


def test_tiles_sorted_unique_and_bbox_exact(man):
    ids = [t["id"] for t in man["tiles"]]
    assert ids == sorted(ids) and len(set(ids)) == len(ids)
    for t in man["tiles"]:
        tt = Tile(0, int(t["id"][3:5]), int(t["id"][6:8]))
        assert t["id"] == tt.id and t["bbox_baytm"] == list(tt.bounds)
        assert t["class"] in bm.CLASSES and t["bake"] == {"status": "not_baked"}
        assert 0.0 <= t["land_fraction"] <= 1.0 and 0.0 < t["in_box_fraction"] <= 1.0


def test_class_consistent_with_rules(man):
    for t in man["tiles"]:
        if t["class"] in ("urban", "rural"):
            assert t["land_fraction"] >= CFG["classes"]["land_min_fraction"]
            assert (t["class"] == "urban") == (t["osm_buildings"] >= CFG["classes"]["urban_min_osm_buildings"])
        else:
            assert t["land_fraction"] < CFG["classes"]["land_min_fraction"]


def test_sources_follow_class(man):
    for t in man["tiles"]:
        s = t["sources"]
        assert s["osm_water"] is True and s["osm"] == (t["class"] != "ocean_only")
        assert ("dep3_1m" in s) == (t["class"] in ("urban", "rural"))
        if t["class"] == "ocean_only":
            assert "lidar_ept" not in s and "datasf" not in s
        if "lidar_ept" in s:
            assert t["osm_buildings"] > 0
        assert ("no_cudem" in s.get("gaps", [])) == (not s["cudem"])


def test_known_tiles(by_id):
    m0 = by_id["L0_30_36"]                      # M0 downtown tile (R-004 section 4)
    assert m0["class"] == "urban" and m0["osm_buildings"] > 2000
    assert "x55y419" in m0["sources"]["dep3_1m"] and m0["sources"]["datasf"] is True
    assert m0["sources"]["cudem"] == ["ncei19_n38x00_w122x50_2022v1"]
    assert "CA_SanFrancisco_1_B23" in m0["sources"]["lidar_ept"]
    x, z = lonlat_to_baytm(-123.0, 37.70)       # Southeast Farallon: land, no CUDEM, no 3DEP
    farallon = by_id[tile_of(x, z).id]
    assert farallon["class"] == "rural" and "no_cudem" in farallon["sources"]["gaps"]
    x, z = lonlat_to_baytm(-122.39, 38.03)      # San Pablo Bay: Bay water, bathymetry only
    assert by_id[tile_of(x, z).id]["class"] == "water_only"
    x, z = lonlat_to_baytm(-122.9, 37.5)        # open Pacific
    assert by_id[tile_of(x, z).id]["class"] == "ocean_only"


def test_every_m1_district_tile_is_listed(by_id):
    for i in range(25, 28):
        for j in range(36, 39):
            assert f"L0_{i}_{j}" in by_id           # Golden Gate district
    for i in range(29, 32):
        for j in (35, 36):
            assert f"L0_{i}_{j}" in by_id           # SoMa district


def test_building_total_matches_scan(man):
    assert sum(t["osm_buildings"] for t in man["tiles"]) == sum(CFG["snapshots"]["osm_buildings"].values())


# ---- determinism: M-100 draft (two runs, byte-identical, per fingerprint)
@needs_cache
def test_rebuild_is_byte_identical_to_committed(tmp_path):
    a = bm.manifest_bytes(bm.build_manifest())
    b = bm.manifest_bytes(bm.build_manifest())
    assert a == b
    assert a == MAN_PATH.read_bytes(), "committed manifest is stale: run bay_manifest.py build"


def test_fingerprint_changes_with_config(man):
    import copy
    from kaiju_geo.common import canonical_json, sha256_bytes
    cfg = {k: v for k, v in CFG.items() if not k.startswith("_")}
    f1 = sha256_bytes(canonical_json({"config": cfg, "water_polygons_sha256": man["inputs"]["water_polygons_sha256"],
                                      "code_sha256": __import__("kaiju_geo.common", fromlist=["x"]).sha256_file(Path(bm.__file__))}).encode())
    assert f1 == man["fingerprint"]
    cfg2 = copy.deepcopy(cfg)
    cfg2["classes"]["urban_min_osm_buildings"] += 1
    f2 = sha256_bytes(canonical_json({"config": cfg2, "water_polygons_sha256": man["inputs"]["water_polygons_sha256"],
                                      "code_sha256": __import__("kaiju_geo.common", fromlist=["x"]).sha256_file(Path(bm.__file__))}).encode())
    assert f2 != f1
