"""T-102: bake_bay driver (injected runner), per-class source plan, T-144b name drop, placeholders, LOD decisions."""
import json
import os
import sys

import pytest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import bake_bay  # noqa: E402
from kaiju_geo import lod  # noqa: E402
from kaiju_geo.common import CACHE  # noqa: E402

try:
    from test_lod import feat, box, write_l0  # noqa: E402
except ImportError:  # pragma: no cover
    feat = None

TILES = [
    {"id": "L0_30_36", "class": "urban", "sources": {"cudem": ["a"], "dep3_1m": ["x1"], "osm": True, "datasf": True}},
    {"id": "L0_31_36", "class": "rural", "sources": {"cudem": [], "dep3_1m": ["x2"], "osm": True}},
    {"id": "L0_02_01", "class": "ocean_only", "sources": {"cudem": [], "osm": False}},
    {"id": "L0_10_10", "class": "water_only", "sources": {"cudem": ["b"], "osm": True}},
    {"id": "L0_50_50", "class": "outside", "sources": {}},
]


def test_plan_of_follows_manifest_sources():
    assert bake_bay.plan_of(TILES[0]) == {"cudem": True, "dep3": True, "osm": True, "datasf": True}
    assert bake_bay.plan_of(TILES[1]) == {"cudem": False, "dep3": True, "osm": True, "datasf": False}
    assert bake_bay.plan_of(TILES[2]) == {"cudem": False, "dep3": False, "osm": False, "datasf": False}


def test_load_tiles_drops_outside(tmp_path):
    p = tmp_path / "m.json"
    p.write_text(json.dumps({"tiles": TILES}))
    assert [t["id"] for t in bake_bay.load_tiles(p)] == ["L0_30_36", "L0_31_36", "L0_02_01", "L0_10_10"]


def mk(tmp_path, runner, free=1e12, stage="l0"):
    return bake_bay.Driver(stage, tmp_path / "out", status_dir=tmp_path / "st", workers=2, runner=runner, disk_usage=lambda: free)


def recs(tmp_path, stage="l0"):
    return [json.loads(x) for x in (tmp_path / "st" / f"bake_{stage}.jsonl").read_text().splitlines()]


def test_resume_skips_done_retries_failed_and_summarises(tmp_path):
    calls, fail = [], {"L0_31_36"}

    def runner(tid, plan):
        calls.append(tid)
        if tid in fail:
            return RuntimeError("source gap")
        return {"result": "baked", "secs": 1.0, "bytes": 100, "buildings": 3, "placeholder_nodes": 7 if tid == "L0_02_01" else 0}

    jobs = [(t["id"], bake_bay.plan_of(t)) for t in TILES[:4]]
    assert mk(tmp_path, runner).run(jobs) == 1
    assert sorted(calls) == ["L0_02_01", "L0_10_10", "L0_30_36", "L0_31_36"]
    s = json.loads((tmp_path / "st" / "bake_l0.summary.json").read_text())
    assert s["status"]["done"] == 3 and s["status"]["failed"] == 1 and s["tiles_with_placeholder_nodes"] == 1
    assert s["bytes_done"] == 300 and s["failed"] == {"L0_31_36": "RuntimeError: source gap"}
    calls.clear()
    fail.clear()
    assert mk(tmp_path, runner).run(jobs) == 0       # only the failed tile runs again, now it succeeds
    assert calls == ["L0_31_36"]
    calls.clear()
    assert mk(tmp_path, runner).run(jobs) == 0 and calls == []   # everything done: a no-op
    assert mk(tmp_path, runner).run(jobs, force=True) == 0 and len(calls) == 4


def test_stale_currency_reruns_done_tile(tmp_path):
    calls = []
    jobs = [("L0_30_36", {})]
    ok = lambda tid, plan: calls.append(tid) or {"result": "baked", "secs": 0.1}  # noqa: E731
    mk(tmp_path, ok).run(jobs)
    mk(tmp_path, ok).run(jobs, currency=lambda tid: False)
    assert calls == ["L0_30_36", "L0_30_36"]


def test_disk_guard_stops_cleanly_with_exit_3(tmp_path):
    calls = []
    jobs = [(t["id"], {}) for t in TILES[:4]]
    d = mk(tmp_path, lambda tid, plan: calls.append(tid) or {"result": "baked", "secs": 0.1}, free=10e9)
    assert d.run(jobs) == 3 and calls == []
    s = json.loads((tmp_path / "st" / "bake_l0.summary.json").read_text())
    assert s["stop_reason"].startswith("disk_low") and s["pending_left"] == 4


def test_torn_status_line_is_tolerated(tmp_path):
    (tmp_path / "st").mkdir()
    (tmp_path / "st" / "bake_l0.jsonl").write_text('{"tile": "L0_30_36", "status": "done"}\n{"tile": "L0_3')
    assert list(bake_bay.read_status(tmp_path / "st" / "bake_l0.jsonl")) == ["L0_30_36"]


def test_refuses_streamingassets_output(tmp_path):
    with pytest.raises(SystemExit):
        bake_bay.main(["--stage", "l0", "--out", str(tmp_path / "Assets" / "StreamingAssets" / "Geo"), "--dry-run"])


# ---------------------------------------------------------------- LOD decisions (build lead, after T-111b)
def test_footprint_tolerance_override_applies_to_the_named_tile_and_form_only(tmp_path, monkeypatch):
    assert lod.FOOTPRINT_TOL_OVERRIDE_M == {("L1_14_18", "b1"): 2.0} and lod.GENERATOR == "bake_lod.py@5"
    root = tmp_path / "src"
    write_l0(root, 0, 0, feats=[feat("osm:w1", box(100, 100, 140, 130), 24.0)])
    src = lod.L0Source(root)
    monkeypatch.setitem(lod.FOOTPRINT_TOL_OVERRIDE_M, ("L1_00_00", "b1"), 3.0)
    lod.bake_level_tile(1, 0, 0, src, tmp_path / "o", rtin=False)
    man = json.loads((tmp_path / "o" / "L1" / "00_00" / "manifest.json").read_text())
    assert man["errors"]["buildings"]["forms"]["b1"]["footprint_tol_m"] == 3.0


def test_tile_over_cap_is_baked_at_its_cap_threshold(tmp_path, monkeypatch):
    root = tmp_path / "src"
    write_l0(root, 0, 0, func=lambda x, z: 20.0 + 8.0 * (x / 300.0) ** 1.0 * ((z / 700.0) % 1.0) + 3.0 * ((x / 37.0) % 1.0))
    src = lod.L0Source(root)
    lod.bake_level_tile(1, 0, 0, src, tmp_path / "a")
    nom = json.loads((tmp_path / "a" / "L1" / "00_00" / "manifest.json").read_text())["terrain"]
    cap = max(2, nom["rtin_triangles_at_nominal"] // 3)
    monkeypatch.setitem(lod.BUDGET, "terrain_tris_cap", cap)
    lod.bake_level_tile(1, 0, 0, src, tmp_path / "b")
    t = json.loads((tmp_path / "b" / "L1" / "00_00" / "manifest.json").read_text())["terrain"]
    assert t["rtin_threshold_nominal_m"] == 0.2 and t["rtin_threshold_m"] == t["rtin_threshold_for_cap_m"] > 0.2
    assert t["rtin_triangles"] == t["rtin_triangles_at_cap_threshold"] < t["rtin_triangles_at_nominal"]


# ---------------------------------------------------------------- real cache (skipped when the git-ignored fetch cache is absent)
needs_cache = pytest.mark.skipif(not (CACHE / "osm" / "tiles" / "L0_30_36.osm.pbf").exists(), reason="git-ignored fetch cache absent")


@needs_cache
def test_shipped_names_are_dropped_from_baked_buildings_and_roads(tmp_path):
    from kaiju_geo import bake
    from kaiju_geo.common import parse_tile
    bake.bake(parse_tile("L0_30_36"), out_root=tmp_path, threads=2)
    for layer in ("buildings", "roads"):
        for f in json.loads((tmp_path / "L0" / "30_36" / f"{layer}.geojson").read_text())["features"]:
            assert f["properties"]["name"] is None


@needs_cache
def test_ocean_tile_without_cudem_is_a_flagged_placeholder(tmp_path):
    t = next((t for t in bake_bay.load_tiles() if t["class"] == "ocean_only" and not t["sources"].get("cudem")), None)
    if t is None or not (CACHE / "osm" / "water-polygons-split-4326.zip").exists():
        pytest.skip("no such tile / water polygons")
    from kaiju_geo import bake
    from kaiju_geo.common import parse_tile
    r = bake.bake(parse_tile(t["id"]), out_root=tmp_path, plan=bake_bay.plan_of(t))
    ter = next(L for L in r["manifest"]["layers"] if L["layer"] == "terrain")
    assert ter["placeholder_nodes"] == 2049 * 2049 and ter["placeholder_nodes_sea"] + ter["placeholder_nodes_land"] == 2049 * 2049
    assert ter["h_min_m"] == ter["h_max_m"] == bake.PLACEHOLDER_SEA_M or ter["placeholder_nodes_land"] > 0
