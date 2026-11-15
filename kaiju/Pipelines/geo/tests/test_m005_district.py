"""M-005@v1.2 district checks (R-020 §4): synthetic pass / fail fixture per check, no-silent-pass, and one real run."""
import json

import numpy as np
import pytest

import m005_check as M
from kaiju_geo.common import TILES_ROOT, parse_tile

N = 101


def write_tile(root, tid, h=None, buildings=(), bounds_shift=(0.0, 0.0)):
    t = parse_tile(tid)
    d = root / t.folder
    d.mkdir(parents=True, exist_ok=True)
    h = np.zeros((N, N)) if h is None else h
    lo, hi = float(h.min()), float(h.max() + 1.0)
    raw = np.round((h - lo) / (hi - lo) * 65535).astype("<u2")
    raw.tofile(d / "terrain.r16")
    x0, z0, x1, z1 = t.bounds
    man = {"tile": tid, "bounds_baytm": [x0 + bounds_shift[0], z0 + bounds_shift[1], x1 + bounds_shift[0], z1 + bounds_shift[1]],
           "layers": [{"layer": "terrain", "samples": N}, {"layer": "terrain_r16", "h_min_m": lo, "h_max_m": hi}]}
    (d / "manifest.json").write_text(json.dumps(man))
    feats = []
    for k, b in enumerate(buildings):
        x, z, w, hgt, src = b[:5]
        extra = b[5] if len(b) > 5 else {}
        props = {"bid": f"osm:{tid}:{k}", "height_m": hgt, "height_source": src, "base_navd88_m": 1.0, "excluded": None,
                 "part_of": None, "has_parts": False}
        props.update(extra)
        feats.append({"type": "Feature", "id": props["bid"], "properties": props,
                      "geometry": {"type": "Polygon", "coordinates": [[[x, z], [x + w, z], [x + w, z + w], [x, z + w], [x, z]]]}})
    (d / "buildings.geojson").write_text(json.dumps({"type": "FeatureCollection", "features": feats}))
    return t


def rows(rep, check, scope=None):
    return [x for x in rep["rows"] if x["check"] == check and (scope is None or x["scope"] == scope)]


def mk(tmp_path, a=None, b=None, ba=(), bb=(), shift=(0.0, 0.0)):
    write_tile(tmp_path, "L0_30_36", a, ba)
    write_tile(tmp_path, "L0_31_36", b, bb, bounds_shift=shift)
    return M.district(["L0_30_36", "L0_31_36"], tmp_path)


def test_every_row_carries_metric_version(tmp_path):
    rep = mk(tmp_path, ba=[(61500, 73800, 120, 10, "osm_height")])
    assert rep["metric_version"] == "M-005@v1.2" and all(x["metric_version"] == "M-005@v1.2" for x in rep["rows"])


# ---- default-height share
def test_default_share_pass_and_union_fail(tmp_path):
    rep = mk(tmp_path, ba=[(61500, 73800, 120, 10, "osm_height")] * 1 + [(61700, 73800, 10, 10, "default")])
    u = rows(rep, "default_height_share_union")[0]
    assert u["result"] == "pass" and u["value"] == pytest.approx(100 / (14400 + 100), abs=1e-4)
    rep = mk(tmp_path, ba=[(61500, 73800, 120, 10, "osm_height"), (61700, 73800, 60, 10, "default")])
    assert rows(rep, "default_height_share_union")[0]["result"] == "fail"
    assert rows(rep, "default_height_share_tile", "L0_30_36")[0]["result"] == "fail"       # 3600/(14400+3600) = 0.2 > 0.05


def test_default_share_small_tile_reported_not_gated(tmp_path):
    rep = mk(tmp_path, ba=[(61500, 73800, 120, 10, "osm_height"), (61700, 73800, 20, 10, "osm_height")],
             bb=[(63600, 73800, 50, 10, "default")])                                      # 2,500 m2, 100 % default
    t = rows(rep, "default_height_share_tile", "L0_31_36")[0]
    assert t["result"] == "reported" and t["gated"] is False and t["value"] == 1.0


def test_default_share_uses_tallest_part_source_and_drops_excluded(tmp_path):
    root = (61500, 73800, 120, 10, "default", {"has_parts": True, "bid": "osm:s"})
    part = (61500, 73800, 60, 40, "osm_height", {"part_of": "osm:s", "bid": "osm:p"})
    gone = (61700, 73800, 100, 10, "default", {"excluded": "underground"})
    rep = mk(tmp_path, ba=[root, part, gone])
    assert rows(rep, "default_height_share_union")[0]["value"] == 0.0


# ---- terrain seam and vertex gap
def test_seam_pass_exact_and_fail_p99_and_max(tmp_path):
    a = np.zeros((N, N)); b = np.zeros((N, N))
    rep = mk(tmp_path, a, b, ba=[(61500, 73800, 120, 10, "osm_height")])
    assert rows(rep, "terrain_seam_all_edges")[0]["result"] == "pass"
    b2 = np.zeros((N, N)); b2[:, 0] = 0.3                                                # whole edge steps 0.3 m: p99 fails
    rep = mk(tmp_path, a, b2, ba=[(61500, 73800, 120, 10, "osm_height")])
    assert rows(rep, "terrain_seam_all_edges")[0]["result"] == "fail"
    b3 = np.zeros((N, N)); b3[50, 0] = 0.6                                                # one node 0.6 m: p99 ok, max fails
    rep = mk(tmp_path, a, b3, ba=[(61500, 73800, 120, 10, "osm_height")])
    s = rows(rep, "terrain_seam_all_edges")[0]
    assert s["result"] == "fail" and s["value"]["p99_dE_m"] <= 0.2 < 0.5 < s["value"]["max_dE_m"]


def test_edge_vertex_gap_fail(tmp_path):
    ok = mk(tmp_path, ba=[(61500, 73800, 120, 10, "osm_height")])
    assert rows(ok, "edge_vertex_gap_all_edges")[0]["result"] == "pass"
    bad = mk(tmp_path, ba=[(61500, 73800, 120, 10, "osm_height")], shift=(0.01, 0.0))
    g = rows(bad, "edge_vertex_gap_all_edges")[0]
    assert g["result"] == "fail" and g["value"] == pytest.approx(0.01)


# ---- buildings: ownership, clipped area, collider overlap
def test_ownership_pass_and_fail(tmp_path):
    rep = mk(tmp_path, ba=[(61500, 73800, 120, 10, "osm_height")])
    assert rows(rep, "bid_ownership")[0]["result"] == "pass"
    inb = (63600, 73800, 40, 10, "osm_height", {"bid": "osm:dup"})                      # inside 31_36
    write_tile(tmp_path, "L0_30_36", None, [inb])
    write_tile(tmp_path, "L0_31_36", None, [inb])
    rep = M.district(["L0_30_36", "L0_31_36"], tmp_path)
    o = rows(rep, "bid_ownership")[0]
    assert o["result"] == "fail" and o["value"]["duplicate_bids"] == 1 and o["value"]["not_in_owner_tile"] == 1


def test_clipped_area_pass_for_edge_straddler(tmp_path):
    rep = mk(tmp_path, ba=[(63400, 73800, 200, 10, "osm_height")])                       # straddles x = 63488 (shared edge)
    c = rows(rep, "clipped_area")[0]
    assert c["result"] == "pass" and c["footprints_crossing_an_edge"] == 1


def test_clipped_area_fail_when_pieces_overlap(tmp_path, monkeypatch):
    real = M.box
    monkeypatch.setattr(M, "box", lambda x0, z0, x1, z1: real(x0 - 5, z0, x1, z1))      # tile squares overlap 5 m: area counted twice
    rep = mk(tmp_path, ba=[(63400, 73800, 200, 10, "osm_height")])
    assert rows(rep, "clipped_area")[0]["result"] == "fail"


def test_collider_overlap_pass_and_fail(tmp_path):
    a = (63400, 73800, 50, 10, "osm_height")                                             # owned by 30_36, ends at x 63450
    rep = mk(tmp_path, ba=[a], bb=[(63500, 73800, 50, 10, "osm_height")])                 # disjoint
    assert rows(rep, "collider_overlap")[0]["result"] == "pass"
    big = (63400, 73800, 200, 10, "osm_height")                                          # reaches into 31_36 to x 63600
    rep = mk(tmp_path, ba=[big], bb=[(63500, 73800, 50, 10, "osm_height")])              # 2,500 m2 overlap
    c = rows(rep, "collider_overlap")[0]
    assert c["result"] == "fail" and c["value"] == pytest.approx(2500.0)


def test_collider_overlap_ignores_same_structure(tmp_path):
    root = (63400, 73800, 200, 10, "osm_height", {"bid": "osm:s"})
    part = (63500, 73800, 50, 10, "osm_height", {"bid": "osm:p", "part_of": "osm:s"})
    write_tile(tmp_path, "L0_30_36", None, [root])
    write_tile(tmp_path, "L0_31_36", None, [part])
    assert rows(M.district(["L0_30_36", "L0_31_36"], tmp_path), "collider_overlap")[0]["result"] == "pass"


# ---- no silent PASS, landmarks
def test_no_input_is_error_never_pass(tmp_path):
    assert M.district([], tmp_path)["result"] == "error"
    rep = M.district(["L0_30_36", "L0_31_36"], tmp_path)                                   # tiles absent
    assert rep["result"] == "error" and not rows(rep, "default_height_share_union")
    write_tile(tmp_path, "L0_30_36"); write_tile(tmp_path, "L0_31_36")                     # no buildings anywhere
    rep = M.district(["L0_30_36", "L0_31_36"], tmp_path)
    assert rep["result"] == "error" and {x["check"] for x in rep["rows"] if x["result"] == "error"} >= {"default_height_share", "buildings"}


def test_single_tile_has_no_seam_error(tmp_path):
    write_tile(tmp_path, "L0_30_36", None, [(61500, 73800, 120, 10, "osm_height")])
    rep = M.district(["L0_30_36"], tmp_path)
    assert rep["result"] == "error" and any(x["check"] == "terrain_seam" and x["result"] == "error" for x in rep["rows"])


def test_truncated_r16_is_error(tmp_path):
    mk(tmp_path, ba=[(61500, 73800, 120, 10, "osm_height")])
    (tmp_path / "L0/30_36/terrain.r16").write_bytes(b"\0\0")
    assert M.district(["L0_30_36", "L0_31_36"], tmp_path)["result"] == "error"


def test_landmarks_not_evaluated(tmp_path):
    rep = mk(tmp_path, ba=[(61500, 73800, 120, 10, "osm_height")])
    lm = rows(rep, "landmarks")[0]
    assert lm["result"] == "not_evaluated" and rep["result"] == "pass"


def test_internal_edge_count_of_the_district():
    ts = [parse_tile(t) for t in M.DISTRICT_TILES]
    assert len(ts) == 15 and len(M.v12_internal_edges(ts)) == 19


# ---- real tiles (about 1 s)
def test_real_district_run(capsys):
    if not all((TILES_ROOT / parse_tile(t).folder / "terrain.r16").is_file() for t in M.DISTRICT_TILES):
        pytest.fail("baked district tiles missing: no input is an error, not a skip")
    rep = M.district(M.DISTRICT_TILES)
    print("\n" + M.district_summary(rep))
    assert rep["metric_version"] == "M-005@v1.2" and len(rows(rep, "terrain_seam")) == 19
    assert rows(rep, "terrain_seam_all_edges")[0]["result"] == "pass"                       # seam numbers are not a finding
    assert rows(rep, "edge_vertex_gap_all_edges")[0]["result"] == "pass"
    assert rows(rep, "landmarks")[0]["result"] == "not_evaluated"
    assert not [x for x in rep["rows"] if x["result"] == "error"]
