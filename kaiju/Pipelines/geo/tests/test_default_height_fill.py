"""T-020b: default building heights filled from 3DEP lidar, then Assessor stories; nothing else changes."""
import json

import numpy as np
import pytest
import shapely
from shapely.geometry import box

from kaiju_geo import vector
from kaiju_geo.common import CACHE, PIPE, TILES_ROOT, load_config

X0, Z0, N = 1000.0, 2000.0, 64
CFG = load_config("bake.json")["buildings"]
GROUND = 5.0


def terrain_at(x, z):
    return np.full(np.shape(x), GROUND)


def grid(top=GROUND + 20.0, covered=None, multi=0.0):
    """64 x 64 m grid of 1 m cells; `covered` = boolean (z, x) mask of cells with returns (default all)."""
    cov = np.ones((N, N), bool) if covered is None else covered
    p = np.where(cov, top, np.nan).astype(np.float32)
    return {"bounds": [X0, Z0, X0 + N, Z0 + N], "p95": p, "multi": np.full((N, N), multi, np.float32),
            "count": cov.astype(np.int32)}


FOOT = box(X0 + 10, Z0 + 10, X0 + 40, Z0 + 40)


def test_lidar_first_with_median_of_p95_minus_terrain():
    g = grid()
    g["p95"][20:30, 20:30] = GROUND + 20.0 + 6.0          # a rooftop box does not move the median
    assert vector.fill_default_height(FOOT, 3, g, terrain_at, CFG) == (20.0, "lidar_3dep")


def test_stories_when_no_lidar_grid_or_lidar_rejected():
    assert vector.fill_default_height(FOOT, 3, None, terrain_at, CFG) == (pytest.approx(3 * 4.22), "assessor_stories")
    assert vector.fill_default_height(FOOT, 2.5, grid(covered=np.zeros((N, N), bool)), terrain_at, CFG)[1] == "assessor_stories"


def test_default_when_neither():
    assert vector.fill_default_height(FOOT, None, None, terrain_at, CFG) is None
    assert vector.fill_default_height(FOOT, 0, grid(multi=0.9), terrain_at, CFG) is None


def test_coverage_rule_70_percent_of_eroded_cells():
    # eroded footprint = 28 x 28 cells (columns 11..38); drop the first `cut` columns
    for cut, ok in ((8, True), (9, False)):               # 20/28 = 0.714 passes, 19/28 = 0.679 fails
        cov = np.ones((N, N), bool)
        cov[:, 11:11 + cut] = False
        h = vector.lidar_height(FOOT, grid(covered=cov), terrain_at, CFG["lidar_fill"])
        assert (h is not None) is ok, (cut, h)
    assert shapely.buffer(FOOT, -1.0).area == pytest.approx(784.0)


def test_vegetation_share_rejects_trees_and_low_heights():
    assert vector.lidar_height(FOOT, grid(multi=0.5), terrain_at, CFG["lidar_fill"]) is None
    assert vector.lidar_height(FOOT, grid(multi=0.1), terrain_at, CFG["lidar_fill"]) == 20.0
    assert vector.lidar_height(FOOT, grid(top=GROUND + 1.0), terrain_at, CFG["lidar_fill"]) is None


def test_footprint_too_small_for_erosion_and_outside_grid():
    assert vector.lidar_height(box(X0 + 5, Z0 + 5, X0 + 7, Z0 + 7), grid(), terrain_at, CFG["lidar_fill"]) is None
    assert vector.lidar_height(box(5000, 5000, 5030, 5030), grid(), terrain_at, CFG["lidar_fill"]) is None


def test_missing_or_bad_cache_is_none(tmp_path):
    assert vector.read_lidar_grid(tmp_path / "nope.npz") is None
    (tmp_path / "bad.npz").write_bytes(b"x")
    assert vector.read_lidar_grid(tmp_path / "bad.npz") is None


def test_read_lidar_grid_roundtrip(tmp_path):
    g = grid()
    np.savez(tmp_path / "r.npz", bounds_baytm=np.array(g["bounds"]), p95_10=g["p95"], multi_10=g["multi"], count_10=g["count"])
    r = vector.read_lidar_grid(tmp_path / "r.npz")
    assert r["bounds"] == g["bounds"] and vector.fill_default_height(FOOT, None, r, terrain_at, CFG) == (20.0, "lidar_3dep")


def test_only_default_buildings_reach_the_fill():
    src = (PIPE / "kaiju_geo" / "bake.py").read_text()
    assert 'if hsrc == "default":' in src and src.count("fill_default_height(") == 1


@pytest.mark.slow
def test_rebake_changes_only_formerly_default_buildings(tmp_path):
    """Real tile L0_30_36 (needs its cache): changed features were default before and are lidar/stories now."""
    import subprocess
    import sys
    if not (CACHE / "lidar" / "rind_v11__L0_30_36.npz").exists() or not (TILES_ROOT / "L0" / "30_36" / "buildings.geojson").exists():
        pytest.skip("cache or committed tile missing")
    r = subprocess.run([sys.executable, "-I", str(PIPE / "bake_tile.py"), "--tile", "L0_30_36", "--offline", "--force",
                        "--threads", "4", "--out", str(tmp_path)], capture_output=True, text=True)
    assert r.returncode == 0, r.stderr[-2000:]
    new = {f["id"]: f for f in json.loads((tmp_path / "L0" / "30_36" / "buildings.geojson").read_text())["features"]}
    old = {f["id"]: f for f in json.loads((TILES_ROOT / "L0" / "30_36" / "buildings.geojson").read_text())["features"]}
    assert new.keys() == old.keys()
    for k, f in new.items():
        if f != old[k]:
            assert old[k]["properties"]["height_source"] == "default"
            assert f["properties"]["height_source"] in ("lidar_3dep", "assessor_stories")
