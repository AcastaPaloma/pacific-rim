"""T-103: older-3DEP-project fill for tiles whose newest project leaves holes (fetch_3dep(fill=True), plan, config list)."""
import json
import os
import sys
import types

import numpy as np
import pytest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import bake_bay  # noqa: E402
from kaiju_geo import fetch, terrain  # noqa: E402
from kaiju_geo.common import load_config, parse_tile, pipe_rel  # noqa: E402

rasterio = pytest.importorskip("rasterio")
from rasterio.transform import from_origin  # noqa: E402

U = "https://example.invalid/Projects/{p}/TIFF/USGS_1M_10_x61y415_{p}.tif"
ITEMS = [{"title": "n", "url": U.format(p="CA_Old_2020_A20"), "pub": "2021-06-11", "bbox": None},
         {"title": "n", "url": U.format(p="CA_New_2021_B21"), "pub": "2022-08-10", "bbox": None},
         {"title": "n", "url": U.format(p="CA_Mid_2020_B20"), "pub": "2021-09-01", "bbox": None}]


def write_tif(path, value, hole_cols=0):
    d = np.full((20, 20), value, dtype="float32")
    if hole_cols:
        d[:, :hole_cols] = -999999.0
    with rasterio.open(path, "w", driver="GTiff", width=20, height=20, count=1, dtype="float32", crs="EPSG:26910",
                       transform=from_origin(500000, 4100000, 1, 1), nodata=-999999.0) as dst:
        dst.write(d, 1)


@pytest.fixture
def fake(tmp_path, monkeypatch):
    calls = []
    state = {"holes": True}
    cache = tmp_path / "cache"
    monkeypatch.setattr(fetch, "CACHE", cache)
    monkeypatch.setattr(fetch, "_tnm_items", lambda tile, bb: ITEMS)

    def win(url, dest, bounds, crs, label, extra):
        calls.append(extra["item"])
        dest.parent.mkdir(parents=True, exist_ok=True)
        write_tif(dest, 10.0 if "New" in url else 5.0, hole_cols=8 if "New" in url and state["holes"] else 0)
        return {"sha256": "x" + extra["item"], "bytes": dest.stat().st_size}
    monkeypatch.setattr(fetch, "_window_read", win)
    return types.SimpleNamespace(calls=calls, state=state)


def test_fill_reads_older_projects_newest_first_when_newest_has_holes(fake):
    out = fetch.fetch_3dep(parse_tile("L0_59_17"), fill=True)
    assert [m["item"].split("_", 4)[4] for m in out] == ["CA_New_2021_B21", "CA_Mid_2020_B20", "CA_Old_2020_A20"]
    assert [bool(m.get("fill")) for m in out] == [False, True, True]
    assert out[1]["published"] if "published" in out[1] else True


def test_no_fill_by_default_and_none_when_newest_is_full(fake):
    assert [m["item"] for m in fetch.fetch_3dep(parse_tile("L0_59_17"))] == ["USGS_1M_10_x61y415_CA_New_2021_B21"]
    fake.state["holes"] = False
    fake.calls.clear()
    out = fetch.fetch_3dep(parse_tile("L0_59_17"), fill=True)
    assert len(out) == 1 and not out[0].get("fill") and len(fake.calls) == 1


def test_window_valid_share(tmp_path):
    write_tif(tmp_path / "a.tif", 1.0, hole_cols=5)
    assert fetch.window_valid_share(tmp_path / "a.tif") == pytest.approx(0.75)


def test_sample_rasters_takes_older_project_only_in_the_holes(tmp_path):
    write_tif(tmp_path / "new.tif", 10.0, hole_cols=8)
    write_tif(tmp_path / "old.tif", 5.0)
    from kaiju_geo.common import transformer, baytm_crs
    t = transformer("EPSG:26910", baytm_crs())
    x1, z1 = t.transform(500003.5, 4099990.5)   # inside the hole columns
    x2, z2 = t.transform(500015.5, 4099990.5)   # valid in the newest
    h = terrain.sample_rasters([tmp_path / "new.tif", tmp_path / "old.tif"], np.array([[x1, x2]]), np.array([[z1, z2]]))
    assert h[0, 0] == pytest.approx(5.0) and h[0, 1] == pytest.approx(10.0)


def test_plan_of_marks_only_listed_tiles(tmp_path, monkeypatch):
    monkeypatch.setattr(bake_bay, "CACHE", tmp_path)
    monkeypatch.setattr(bake_bay, "dep3_fill_tiles", lambda: {"L0_59_17"})
    p = bake_bay.plan_of({"id": "L0_59_17", "class": "rural", "sources": {"dep3_1m": ["x"], "osm": True}})
    q = bake_bay.plan_of({"id": "L0_59_18", "class": "rural", "sources": {"dep3_1m": ["x"], "osm": True}})
    assert p["dep3_fill"] is True and "dep3_fill" not in q


def test_config_list_is_the_375_land_placeholder_tiles():
    tiles = load_config("dep3_fill.json")["tiles"]
    assert len(tiles) == 375 and tiles == sorted(set(tiles))
    for t in tiles:
        parse_tile(t)


def test_pipe_rel_survives_a_cache_outside_the_pipeline(tmp_path):
    assert pipe_rel(tmp_path / "x.tif") == str(tmp_path / "x.tif")
    assert pipe_rel(fetch.PIPE / "cache" / "x.tif") == "cache/x.tif"
