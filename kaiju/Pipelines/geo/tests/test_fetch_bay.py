"""T-101: fetch_bay driver (mocked fetchers) and per-cell CUDEM versions."""
import json
import threading

import pytest

import fetch_bay
from kaiju_geo import fetch
from kaiju_geo.common import parse_tile

TILES = [
    {"id": "L0_30_36", "class": "urban", "osm_buildings": 900,
     "sources": {"cudem": ["ncei19_n37x75_w122x50_2022v1"], "dep3_1m": ["x55y419"], "osm": True, "lidar_ept": ["P"]}},
    {"id": "L0_31_36", "class": "rural", "osm_buildings": 0,
     "sources": {"cudem": ["a"], "dep3_1m": ["x1y1"], "osm": True}},
    {"id": "L0_02_01", "class": "ocean_only", "osm_buildings": 0, "sources": {"cudem": [], "osm": False, "gaps": ["no_cudem"]}},
    {"id": "L0_10_10", "class": "ocean_only", "osm_buildings": 0, "sources": {"cudem": ["b"], "osm": False}},
    {"id": "L0_40_40", "class": "rural", "osm_buildings": 5, "sources": {"osm": True, "dep3_1m": ["x2y2"]}},
    {"id": "L0_50_50", "class": "outside", "sources": {}},
]


class Rec:
    def __init__(self, fail=()):
        self.calls, self.fail, self.lock = [], set(fail), threading.Lock()

    def __call__(self, source, tid):
        with self.lock:
            self.calls.append((source, tid))
        if tid in self.fail:
            raise RuntimeError("HTTP 503 boom")


def drv(tmp_path, source, rec, free=1e12, **kw):
    return fetch_bay.Driver(source, status_dir=tmp_path, workers=3, fetcher=rec, preparer=lambda s: None,
                            disk_usage=lambda: free, **kw)


def lines(tmp_path, source):
    return [json.loads(x) for x in (tmp_path / f"{source}.jsonl").read_text().splitlines()]


def test_classify_rules():
    c = fetch_bay.classify
    assert c("cudem", TILES[0]) is None and c("cudem", TILES[2]) == "no_cudem" and c("cudem", TILES[3]) is None
    assert c("3dep", TILES[0]) is None and c("3dep", TILES[3]) == "ocean_only" and c("3dep", TILES[4]) is None
    assert c("osm", TILES[0]) is None and c("osm", TILES[3]) == "ocean_only"
    assert c("lidar", TILES[0]) is None and c("lidar", TILES[1]) == "no_buildings" and c("lidar", TILES[4]) == "no_lidar_ept"
    assert all(c(s, TILES[5]) == "outside" for s in ("cudem", "3dep", "osm", "lidar"))


def test_ocean_only_gets_terrain_not_osm_or_3dep(tmp_path):
    rec = Rec()
    drv(tmp_path, "osm", rec).run(TILES)
    assert ("osm", "L0_10_10") not in rec.calls and ("osm", "L0_02_01") not in rec.calls
    rec2 = Rec()
    drv(tmp_path, "cudem", rec2).run(TILES)
    assert ("cudem", "L0_10_10") in rec2.calls and ("cudem", "L0_02_01") not in rec2.calls


def test_statuses_summary_and_exit_code(tmp_path):
    rec = Rec(fail={"L0_31_36"})
    d = drv(tmp_path, "3dep", rec)
    assert d.run(TILES) == 1
    last = fetch_bay.read_status(tmp_path / "3dep.jsonl")
    assert last["L0_30_36"]["status"] == "done"
    assert last["L0_31_36"]["status"] == "failed" and "HTTP 503" in last["L0_31_36"]["reason"]
    assert last["L0_10_10"] == {**last["L0_10_10"], "status": "skipped", "reason": "ocean_only"}
    s = json.loads((tmp_path / "3dep.summary.json").read_text())
    assert s["status"] == {"done": 2, "failed": 1, "skipped": 3} and s["failed_total"] == 1 and s["final"] is True


def test_rerun_is_noop_for_done_and_retries_failed(tmp_path):
    drv(tmp_path, "cudem", Rec(fail={"L0_31_36"})).run(TILES)
    n = len(lines(tmp_path, "cudem"))
    rec = Rec()
    assert drv(tmp_path, "cudem", rec).run(TILES) == 0
    assert rec.calls == [("cudem", "L0_31_36")]  # only the failed tile
    assert len(lines(tmp_path, "cudem")) == n + 1  # no new skip lines, no repeats of done tiles
    rec3 = Rec()
    assert drv(tmp_path, "cudem", rec3).run(TILES) == 0 and rec3.calls == []
    assert len(lines(tmp_path, "cudem")) == n + 1


def test_no_retry_failed(tmp_path):
    drv(tmp_path, "cudem", Rec(fail={"L0_31_36"})).run(TILES)
    rec = Rec()
    drv(tmp_path, "cudem", rec).run(TILES, retry_failed=False)
    assert rec.calls == []


def test_disk_guard_stops_cleanly(tmp_path):
    rec = Rec()
    d = drv(tmp_path, "cudem", rec, free=59e9, min_free_gb=60)
    assert d.run(TILES) == 3
    assert rec.calls == []
    s = json.loads((tmp_path / "cudem.summary.json").read_text())
    assert s["stop_reason"].startswith("disk_low") and s["pending_left"] == 3
    last = fetch_bay.read_status(tmp_path / "cudem.jsonl")
    assert "L0_30_36" not in last  # left pending, not recorded as failed


def test_disk_guard_trips_midrun(tmp_path):
    free = iter([1e12, 1e12] + [10e9] * 50)
    rec = Rec()
    d = fetch_bay.Driver("cudem", status_dir=tmp_path, workers=1, fetcher=rec, preparer=lambda s: None,
                         disk_usage=lambda: next(free))
    assert d.run(TILES) == 3
    assert len(rec.calls) == 1 and d.stop_reason.startswith("disk_low")


def test_water_is_single_global_task(tmp_path):
    rec = Rec()
    assert drv(tmp_path, "water", rec).run([]) == 0
    assert rec.calls == [("water", "_global")]
    rec2 = Rec()
    drv(tmp_path, "water", rec2).run([])
    assert rec2.calls == []


def test_only_and_limit(tmp_path):
    rec = Rec()
    drv(tmp_path, "cudem", rec).run(TILES, only={"L0_30_36", "L0_10_10"}, limit=1)
    assert len(rec.calls) == 1


def test_torn_status_line_tolerated(tmp_path):
    (tmp_path / "osm.jsonl").write_text('{"tile": "L0_30_36", "status": "done"}\n{"tile": "L0_31')
    rec = Rec()
    drv(tmp_path, "osm", rec).run(TILES)
    assert ("osm", "L0_30_36") not in rec.calls and ("osm", "L0_31_36") in rec.calls


def test_real_manifest_plan_counts():
    tiles = fetch_bay.load_manifest()
    assert len(tiles) == 4311
    run, skips, _ = fetch_bay.make_plan("osm", tiles, {})
    assert len(run) + len(skips) == 4311 and all(w == "ocean_only" for _, w in skips) and len(skips) == 1108
    run, _, _ = fetch_bay.make_plan("cudem", tiles, {})
    assert 0 < len(run) < 4311


# ---------------------------------------------------------------- CUDEM versions
def test_cudem_version_parsing():
    assert fetch.cudem_version("ncei19_n37x50_w122x00_2022v1") == "2022v1"
    assert fetch.cudem_version("ncei19_n37x25_w122x50_2023v1") == "2023v1"
    assert fetch.cudem_version("ncei19_n38x50_w122x25_2025v1") == "2025v1"
    with pytest.raises(ValueError):
        fetch.cudem_version("ncei19_n37x50_w122x00")


def test_catalog_has_all_three_versions_and_urls_match_items():
    cat = fetch.cudem_catalog()
    assert len(cat) == 26
    assert {e["version"] for e in cat.values()} == {"2022v1", "2023v1", "2025v1"}
    assert all(e["url"].endswith(e["item"] + ".tif") for e in cat.values())


def test_items_use_the_catalog_version():
    cat = {"n37x50_w122x25": {"item": "ncei19_n37x50_w122x25_2023v1", "url": "u1", "version": "2023v1"},
           "n37x75_w122x25": {"item": "ncei19_n37x75_w122x25_2025v1", "url": "u2", "version": "2025v1"}}
    bb = (-122.2, 37.4, -122.1, 37.6)  # spans the two cells
    assert fetch.cudem_items(bb, cat) == ["ncei19_n37x50_w122x25_2023v1", "ncei19_n37x75_w122x25_2025v1"]
    assert fetch.cudem_url("ncei19_n37x50_w122x25_2023v1", cat) == "u1"
    assert fetch.cudem_items((-121.9, 37.4, -121.8, 37.6), cat) == []  # no CUDEM in those cells


def test_legacy_fallback_without_catalog():
    items = fetch.cudem_items((-122.2, 37.55, -122.1, 37.6), {})
    assert items == ["ncei19_n37x75_w122x25_2022v1"]
    assert fetch.cudem_url(items[0], {}).endswith("ncei19_n37x75_w122x25_2022v1.tif")
    assert fetch.cudem_url("ncei19_n37x75_w122x25_2023v1", {}).endswith("ncei19_n37x75_w122x25_2023v1.tif")


def test_m1_district_cells_stay_2022v1():
    # the 15 committed M1 tiles must keep resolving to the same 2022v1 files (cached window names unchanged)
    t = parse_tile("L0_30_36")
    items = fetch.cudem_items(t.bounds_lonlat(fetch.FETCH_MARGIN_M))
    assert items and all(i.endswith("_2022v1") for i in items)


def test_fetch_cudem_records_version(tmp_path, monkeypatch):
    seen = {}

    def fake_window(url, dest, bounds, crs, label, extra):
        seen[dest.name] = (url, extra)
        return {"url": url, "sha256": "x", "fetched_utc": "t", "bytes": 1}
    monkeypatch.setattr(fetch, "_window_read", fake_window)
    monkeypatch.setattr(fetch, "cudem_catalog", lambda: {
        "n38x00_w122x50": {"item": "ncei19_n38x00_w122x50_2023v1", "url": "https://x/ncei19_n38x00_w122x50_2023v1.tif", "version": "2023v1"}})
    t = parse_tile("L0_30_36")
    monkeypatch.setattr(type(t), "bounds_lonlat", lambda self, m=0.0: (-122.45, 37.76, -122.40, 37.79), raising=False)
    out = fetch.fetch_cudem(t)
    assert [m["version"] for m in out] == ["2023v1"] and out[0]["item"].endswith("_2023v1")
    (name, (url, extra)), = seen.items()
    assert name == "ncei19_n38x00_w122x50_2023v1__L0_30_36.tif" and url.endswith("_2023v1.tif") and extra["version"] == "2023v1"
