"""T-106c: neighbouring tiles read the same cached 3DEP projects at a shared edge (M-005 terrain_seam_all_edges)."""
import json
import sys
from pathlib import Path

import numpy as np
import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
rasterio = pytest.importorskip("rasterio")
from rasterio.transform import from_origin  # noqa: E402

from kaiju_geo import common, fetch, terrain  # noqa: E402
from kaiju_geo.common import parse_tile, transformer, baytm_crs  # noqa: E402


def _write(cache, name, nid, tile, off_east_m, item, published, value, size=300):
    """A 1 m raster (EPSG:26910) `size` m wide whose west edge sits `off_east_m` east of the tile's east edge."""
    x0, z0, x1, z1 = tile.bounds
    t = transformer(baytm_crs(), "EPSG:26910")
    e, n = t.transform(x1 + off_east_m, (z0 + z1) / 2)
    path = cache / "3dep" / f"{item}__{nid}.tif"
    path.parent.mkdir(parents=True, exist_ok=True)
    with rasterio.open(path, "w", driver="GTiff", width=size, height=size, count=1, dtype="float32", crs="EPSG:26910",
                       transform=from_origin(e, n + size / 2, 1.0, 1.0), nodata=-999999.0) as dst:
        dst.write(np.full((size, size), value, "float32"), 1)
    common.meta_path(path).write_text(json.dumps({"item": item, "published": published, "sha256": f"sha-{item}-{nid}", "url": "u"}))
    return path


def test_donor_windows_that_reach_the_tile_are_added_and_own_items_are_not(tmp_path, monkeypatch):
    monkeypatch.setattr(fetch, "CACHE", tmp_path)
    monkeypatch.setattr(fetch, "pipe_rel", lambda p: str(p))
    tile = parse_tile("L0_57_47")
    # neighbour 58_47 holds an older project that starts 100 m INSIDE tile 57_47's east side (its window reaches 320 m into it)
    _write(tmp_path, "old", "L0_58_47", tile, -100, "OLD_2018", "2018-01-01", 40.0)
    _write(tmp_path, "own", "L0_58_47", tile, -100, "OWN_2022", "2022-01-01", 30.0)      # already in the tile's own list
    _write(tmp_path, "far", "L0_58_47", tile, 5000, "FAR_2019", "2019-01-01", 20.0)       # does not reach the tile
    _write(tmp_path, "notnb", "L0_60_47", tile, -100, "NOTNEIGHBOUR", "2019-01-01", 10.0)  # two tiles away: not a neighbour
    got = fetch.neighbour_3dep(tile, [{"item": "OWN_2022"}], reach_m=260.0)
    assert [(m["item"], m["donor"]) for m in got] == [("OLD_2018", "L0_58_47")]


def test_a_donor_fills_the_hole_the_tiles_own_project_leaves_so_both_sides_of_the_edge_agree(tmp_path):
    """Two tiles, one raster hole: the filled side and the donor-fed side sample identical edge nodes."""
    a, b = parse_tile("L0_57_47"), parse_tile("L0_58_47")
    pa = _write(tmp_path, "old", "L0_58_47", a, -100, "OLD_2018", "2018-01-01", 40.0, size=400)
    xs = np.array([[a.bounds[2]]]); zs = np.array([[(a.bounds[1] + a.bounds[3]) / 2]])
    from_a_without = terrain.sample_rasters([], xs, zs)
    from_both = terrain.sample_rasters([pa], xs, zs)
    from_b = terrain.sample_rasters([pa], xs, zs)
    assert np.isnan(from_a_without).all()          # before: tile A had no source on its east edge node -> CUDEM fallback, up to 8 m off
    assert from_both[0, 0] == pytest.approx(40.0) and from_both[0, 0] == from_b[0, 0]
    assert b.i == a.i + 1
