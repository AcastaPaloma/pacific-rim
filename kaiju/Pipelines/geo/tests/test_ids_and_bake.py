"""Building ID determinism, height rules, seam rows and idempotence (T-003 C5, C14, C15, C16)."""
import hashlib
import json
import re
import subprocess
import sys
from pathlib import Path

import numpy as np
import pytest
import shapely

from kaiju_geo import terrain, vector
from kaiju_geo.common import PIPE, TILES_ROOT, Tile, load_config

M0 = TILES_ROOT / "L0" / "30_36"
CORE_ID = re.compile(r"^bld:(osm-way|osm-relation):[1-9][0-9]*$")
PY = sys.executable


def sha(p):
    return hashlib.sha256(Path(p).read_bytes()).hexdigest()


def feats(path):
    return json.loads(Path(path).read_text())["features"]


def test_bids_derive_from_source_ids_only():
    for f in feats(M0 / "buildings.geojson"):
        bid = f["properties"]["bid"]
        assert bid == f["id"] and re.fullmatch(r"osm:[wr][1-9][0-9]*", bid)
        assert CORE_ID.match(vector.core_building_id(bid))
    assert vector.core_building_id("osm:w431972186") == "bld:osm-way:431972186"
    assert vector.core_building_id("osm:r13543125") == "bld:osm-relation:13543125"


def test_bids_unique_and_sorted_and_parts_resolve():
    fs = feats(M0 / "buildings.geojson")
    ids = [f["id"] for f in fs]
    assert len(ids) == len(set(ids))
    assert ids == sorted(ids, key=vector.oid_key)
    idset = set(ids)
    for f in fs:
        po = f["properties"]["part_of"]
        assert po is None or po in idset


def test_transamerica_parts_form_one_structure():
    fs = {f["id"]: f["properties"] for f in feats(M0 / "buildings.geojson")}
    parts = sorted(p["height_m"] for p in fs.values() if p["part_of"] == "osm:w24222973")
    assert len(parts) >= 4 and max(parts) == pytest.approx(260.0)
    assert fs["osm:w24222973"]["has_parts"] is True


def test_every_building_has_a_height_source():
    allowed = {"osm_height", "datasf", "osm_levels", "default"}
    for f in feats(M0 / "buildings.geojson"):
        assert f["properties"]["height_source"] in allowed


@pytest.mark.parametrize("v,m", [("326", 326.0), ("226 m", 226.0), ("12.5m", 12.5), ("100'", 30.48),
                                 ("10'6\"", 3.2004), ("~20", 20.0), ("tall", None), (None, None)])
def test_parse_length(v, m):
    r = vector.parse_length_m(v)
    assert (r is None and m is None) or abs(r - m) < 1e-9


def test_height_fallback_order():
    cfg = load_config("bake.json")["buildings"]
    sq = shapely.box(0, 0, 10, 10)
    ds = [{"id": "d", "geom": shapely.box(0, 0, 10, 10.5), "hgt_median_m": 9.0, "peak_1st_m": 12.0, "gnd_min_m": 2.0}]
    tree = shapely.STRtree([d["geom"] for d in ds])
    assert vector.resolve_height({"height": "30"}, sq, tree, ds, cfg)[:2] == (30.0, "osm_height")
    assert vector.resolve_height({"building:levels": "5"}, sq, tree, ds, cfg)[:2] == (9.0, "datasf")
    stale = [dict(ds[0], peak_1st_m=4.0)]  # relief 2 m <= 3 m: rejected (post-2010 site)
    t2 = shapely.STRtree([stale[0]["geom"]])
    assert vector.resolve_height({"building:levels": "5"}, sq, t2, stale, cfg)[1] == "osm_levels"
    h, src, _ = vector.resolve_height({}, sq, None, [], cfg)
    assert src == "default" and h == pytest.approx(3 * 4.22)


def test_r16_decodes_to_master_within_half_step():
    import rasterio
    man = json.loads((M0 / "manifest.json").read_text())
    L = next(x for x in man["layers"] if x["layer"] == "terrain_r16")
    v = np.frombuffer((M0 / "terrain.r16").read_bytes(), dtype="<u2").reshape(2049, 2049)
    h = terrain.decode_r16(v, L["h_min_m"], L["h_max_m"])
    tif = rasterio.open(M0 / "terrain.tif").read(1).astype(np.float64)
    assert np.max(np.abs(h - np.flipud(tif))) <= L["step_m"] / 2 + 1e-6  # r16 row 0 = south


def test_vertical_scale_is_one():
    """No vertical exaggeration: Coit Tower hill reads ~91 m NAVD88 (EPQS 91.7 m), seabed ~ -29.5 m."""
    import rasterio
    with rasterio.open(M0 / "terrain.tif") as d:
        a = d.read(1)
        r, c = d.index(61591.4, 75450.7)
        assert 89.0 < a[r, c] < 93.0
        assert -30.5 < a.min() < -28.5


def test_seam_rows_identical_with_neighbour():
    """South neighbour L0_30_35 baked from the same sources shares the edge row byte for byte."""
    nb = PIPE / "cache" / "extra_tiles" / "L0" / "30_35" / "terrain.tif"
    if not nb.exists():
        pytest.skip("neighbour not baked: run bake_tile.py --tile L0_30_35 --out cache/extra_tiles")
    import rasterio
    a = rasterio.open(M0 / "terrain.tif").read(1)
    b = rasterio.open(nb).read(1)
    assert a[-1].tobytes() == b[0].tobytes()


def test_owner_rule_no_building_in_two_tiles():
    nb = PIPE / "cache" / "extra_tiles" / "L0" / "30_35" / "buildings.geojson"
    if not nb.exists():
        pytest.skip("neighbour not baked")
    a = {f["id"] for f in feats(M0 / "buildings.geojson")}
    b = {f["id"] for f in feats(nb)}
    assert not (a & b)
    straddle = [f for f in feats(M0 / "buildings.geojson") if shapely.geometry.shape(f["geometry"]).bounds[1] < 73728]
    assert straddle, "owner rule not exercised: expected buildings crossing the south edge"
    for f in straddle:
        rp = shapely.geometry.shape(f["geometry"]).point_on_surface()
        assert Tile(0, 30, 36).contains(rp.x, rp.y) or f["properties"]["part_of"]


@pytest.mark.slow
def test_rebake_is_byte_identical_and_order_independent(tmp_path):
    """Independent bake with 4 threads and shuffled OSM input order == committed tile (C14/C15)."""
    r = subprocess.run([PY, "-I", str(PIPE / "bake_tile.py"), "--tile", "L0_30_36", "--offline", "--force",
                        "--threads", "4", "--shuffle-seed", "11", "--out", str(tmp_path)], capture_output=True, text=True)
    assert r.returncode == 0, r.stderr[-2000:]
    for f in ("terrain.tif", "terrain.r16", "water.png", "buildings.geojson", "roads.geojson", "faults.geojson"):
        assert sha(tmp_path / "L0" / "30_36" / f) == sha(M0 / f), f


def test_second_run_is_up_to_date_and_offline():
    r = subprocess.run([PY, "-I", str(PIPE / "bake_tile.py"), "--tile", "L0_30_36", "--offline"], capture_output=True, text=True)
    assert r.returncode == 0, r.stderr[-2000:]
    assert "up to date" in r.stdout


def test_offline_missing_source_fails_loudly(tmp_path, monkeypatch):
    from kaiju_geo import common
    from kaiju_geo.common import NetworkForbidden
    common.set_offline(True)
    try:
        with pytest.raises(NetworkForbidden):
            common.http_get("https://example.invalid/x.bin", tmp_path / "x.bin", "test")
        assert not (tmp_path / "x.bin").exists()
    finally:
        common.set_offline(False)


def test_faults_tile_clip_empty_by_design_and_nearest_distances():
    fc = json.loads((M0 / "faults.geojson").read_text())
    assert fc["features"] == [] and fc["empty_by_design"] is True
    near = {n["fault_name"]: n["distance_m"] for n in fc["nearest_faults"]}
    assert 11000 < near["Golden Gate fault"] < 12200       # R-004 §7: 11.6 km
    assert 13200 < near["Hayward fault zone"] < 14400       # R-004 §7: 13.8 km
    g = json.loads((PIPE.parent.parent / "Assets/StreamingAssets/Geo/global/v1/faults_bay.geojson").read_text())
    assert len(g["features"]) == 3232
    names = {f["properties"]["fault_name"] for f in g["features"]}
    for n in ("San Andreas fault zone", "Hayward fault zone", "Calaveras fault zone", "Rodgers Creek fault", "San Gregorio fault zone"):
        assert n in names
    for f in g["features"][:50]:
        assert set(("fault_name", "section", "linetype", "slip_rate", "slip_sense", "age")) <= set(f["properties"])
