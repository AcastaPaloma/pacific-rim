"""Fix cycle 2 (T-003): synthetic-neighbour seam and owner rule without the cache, a real connection failure,
non-surface features excluded, part heights capped by their structure (DEF-T003-arch-3, bay-1, bay-2)."""
import json
from pathlib import Path

import numpy as np
import pytest
import shapely

from kaiju_geo import bake, common, terrain, vector
from kaiju_geo.common import TILES_ROOT, Tile, transformer, baytm_to_lonlat, load_config

M0 = TILES_ROOT / "L0" / "30_36"


def _synthetic_sources(tmp_path, tiles):
    """CUDEM-like (EPSG:4269, ~3 m) and 3DEP-like (EPSG:26910, 2 m, with nodata holes) rasters over the tiles."""
    import rasterio
    from rasterio.transform import from_origin
    bbs = [t.bounds_lonlat(400) for t in tiles]
    w, s, e, n = min(b[0] for b in bbs), min(b[1] for b in bbs), max(b[2] for b in bbs), max(b[3] for b in bbs)
    rng = np.random.default_rng(3)
    res = 3.0864e-5
    nx, ny = int((e - w) / res) + 1, int((n - s) / res) + 1
    lon = w + (np.arange(nx) + 0.5) * res
    lat = n - (np.arange(ny) + 0.5) * res
    LON, LAT = np.meshgrid(lon, lat)
    cud = (40 * np.sin((LON + 122.4) * 300) + 30 * np.cos((LAT - 37.79) * 250) - 5 + rng.normal(0, 0.3, LON.shape)).astype("float32")
    pc = tmp_path / "cudem.tif"
    with rasterio.open(pc, "w", driver="GTiff", width=nx, height=ny, count=1, dtype="float32", crs="EPSG:4269",
                       transform=from_origin(w, n, res, res), nodata=-9999) as d:
        d.write(cud, 1)
    t = transformer("EPSG:4269", "EPSG:26910")
    xs, ys = t.transform([w, e, e, w], [s, s, n, n])
    x0, y1 = min(xs), max(ys)
    mx, my = int((max(xs) - x0) / 2) + 1, int((y1 - min(ys)) / 2) + 1
    X = x0 + (np.arange(mx) + 0.5) * 2
    Y = y1 - (np.arange(my) + 0.5) * 2
    XX, YY = np.meshgrid(X, Y)
    dep = (20 * np.sin(XX / 97.0) + 15 * np.cos(YY / 61.0) + 8).astype("float32")
    dep[rng.random(dep.shape) < 0.02] = -999999      # nodata holes
    pd = tmp_path / "dep.tif"
    with rasterio.open(pd, "w", driver="GTiff", width=mx, height=my, count=1, dtype="float32", crs="EPSG:26910",
                       transform=from_origin(x0, y1, 2, 2), nodata=-999999) as d:
        d.write(dep, 1)
    return [pc], [pd]


def _surface(tile, cp, dp, margin=192):
    xs, zs = terrain.node_axes(tile, margin)
    X, Z = np.meshgrid(xs, zs)
    h, _ = terrain.merge_from_cfg(terrain.sample_rasters(cp, X, Z), terrain.sample_rasters(dp, X, Z), load_config("bake.json"), tile.spacing)
    return np.round(h, 4).astype(np.float32)[margin:margin + 2049, margin:margin + 2049]


def test_synthetic_neighbour_shares_edge_rows_byte_for_byte(tmp_path):
    a, b = Tile(0, 30, 36), Tile(0, 30, 35)
    cp, dp = _synthetic_sources(tmp_path, [a, b])
    ha, hb = _surface(a, cp, dp), _surface(b, cp, dp)
    assert np.isfinite(ha).all() and np.isfinite(hb).all()
    assert ha[0].tobytes() == hb[2048].tobytes()          # south edge of a == north edge of b (rows south-first)


def test_owner_rule_synthetic_straddlers():
    a, b = Tile(0, 30, 36), Tile(0, 31, 36)
    edge = 63488.0
    rng = np.random.default_rng(1)
    for _ in range(200):
        cz = rng.uniform(74000, 75500)
        left, right = rng.uniform(1, 60), rng.uniform(1, 60)
        g = shapely.box(edge - left, cz, edge + right, cz + rng.uniform(5, 40))
        assert bake.owns(a, g) != bake.owns(b, g)        # exactly one owner


def test_real_unreachable_source_fails_and_leaves_nothing(tmp_path):
    dest = tmp_path / "x.bin"
    with pytest.raises(RuntimeError):
        common.http_get("http://127.0.0.1:9/nothing.bin", dest, "unreachable-test", retries=1, timeout=3)
    assert not dest.exists() and not (tmp_path / "x.bin.part").exists()


@pytest.mark.parametrize("tags,reason", [({"building": "train_station", "location": "underground", "layer": "-2"}, "underground"),
                                         ({"building": "yes", "layer": "-1", "building:levels:underground": "2"}, "underground"),
                                         ({"building": "yes", "layer": "-1", "level": "-1"}, "underground"),
                                         ({"building": "ship"}, "ship"), ({"building": "yes", "layer": "1"}, None),
                                         ({"building": "yes", "layer": "-1"}, None),
                                         ({"building": "transportation", "layer": "-1", "building:levels": "5", "building:levels:underground": "2", "height": "30"}, None)])
def test_exclusion_reason(tags, reason):
    assert vector.exclusion_reason(tags) == reason


def feats():
    return json.loads((M0 / "buildings.geojson").read_text())["features"]


def test_stations_and_ship_kept_flagged_not_surface():
    fs = {f["id"]: f["properties"] for f in feats()}
    for bid, why in [("osm:w550652205", "underground"), ("osm:w184956226", "underground"), ("osm:w773030142", "underground"), ("osm:w281243626", "ship")]:
        assert fs[bid]["excluded"] == why, bid
    for p in fs.values():
        if p["part_of"] == "osm:w550652205":
            assert p["excluded"] == "underground"
    assert fs["osm:w542375290"]["excluded"] is None        # Salesforce Transit Center stands above ground


def test_no_fallback_part_exceeds_its_structure():
    fs = {f["id"]: f["properties"] for f in feats()}
    over = [b for b, p in fs.items() if p["part_of"] and p["height_source"] != "osm_height"
            and fs[p["part_of"]]["height_source"] in ("osm_height", "datasf") and p["height_m"] > fs[p["part_of"]]["height_m"] + 1e-6]
    assert over == []
    assert any(p["height_capped_to_parent"] for p in fs.values())


def test_manifest_content_is_input_determined():
    man = json.loads((M0 / "manifest.json").read_text())
    c = bake.manifest_content(man)
    assert not set(bake.VOLATILE_BUILD_KEYS) & set(c["build"])
    assert c["build"]["inputs_sha256"] == man["build"]["inputs_sha256"]


def test_blend_weight_gradient_is_bounded_and_config_driven():
    """The 3DEP weight changes by at most 1/feather_m per metre (so the blend adds <= |delta|/feather_m per 1 m
    step) and is centred on the region edge when configured so (M-005@v1.1 M-005.9)."""
    cfg = load_config("bake.json")
    n = 201
    xx = np.arange(n, dtype=float)
    cud = np.tile(np.where(xx < 100, 5.0, -5.0), (n, 1))       # land (> +1.798) west of x = 100
    dep = np.tile(np.where(xx < 100, 8.0, -1.07), (n, 1))       # lidar land 3 m higher; hydro-flattened water
    h, w = terrain.merge_from_cfg(cud, dep, cfg, 1.0)
    L = cfg["terrain"]["feather_m"]
    row = w[n // 2]
    assert np.max(np.abs(np.diff(row))) <= 1.0 / L + 1e-9
    if cfg["terrain"].get("feather_mode") == "centred":
        assert abs(row[99] - 0.5) <= 1.0 / L and abs(row[100] - 0.5) <= 1.0 / L
    assert np.all(np.isfinite(h))
