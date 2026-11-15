"""T-108 geo_lod@v1: synthetic-fixture tests for kaiju_geo.lod / bake_lod.py (R-101 sections 4 and 8.2)."""
import hashlib
import json
import math
import os
import sys
from pathlib import Path

import numpy as np
import pytest
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from kaiju_geo import lod  # noqa: E402
from kaiju_geo.common import SAMPLES, TILES_ROOT  # noqa: E402
from kaiju_geo.terrain import decode_r16, quantise_r16  # noqa: E402

N = 2048


def surf(x, z):
    return 20.0 + 8.0 * np.sin(x / 650.0) + 5.0 * np.cos(z / 410.0) + 0.002 * x


def box(x0, z0, x1, z1):
    return {"type": "Polygon", "coordinates": [[[x0, z0], [x1, z0], [x1, z1], [x0, z1], [x0, z0]]]}


def feat(bid, geom, height, base=20.0, **kw):
    p = {"bid": bid, "height_m": height, "base_navd88_m": base, "ground_min_navd88_m": base - 0.5,
         "min_height_m": 0.0, "excluded": None, "has_parts": False, "part_of": None, "roof_shape": None}
    p.update(kw)
    return {"type": "Feature", "id": bid, "properties": p, "geometry": geom}


def write_l0(root: Path, i: int, j: int, feats=None, water_land_block=None, func=surf):
    d = root / "L0" / f"{i:02d}_{j:02d}"
    d.mkdir(parents=True)
    xs = i * 2048.0 + np.arange(SAMPLES)
    zs = j * 2048.0 + np.arange(SAMPLES)
    h = func(xs[None, :], zs[:, None])
    v, hmin, hmax = quantise_r16(h)
    v.astype("<u2").tofile(d / "terrain.r16")
    w = np.full((N, N), 255, dtype=np.uint8)
    if water_land_block is not None:
        r0, r1, c0, c1 = water_land_block
        w[r0:r1, c0:c1] = 0
    Image.fromarray(w, "L").save(d / "water.png")
    (d / "buildings.geojson").write_text(json.dumps({"type": "FeatureCollection", "features": feats or []}))
    man = {"spec": "geo_tile@v1", "tile": f"L0_{i:02d}_{j:02d}", "layers": [
        {"layer": "terrain_r16", "file": "terrain.r16", "h_min_m": hmin, "h_max_m": hmax,
         "sources": [{"licence": "US-Gov-PD", "attribution": "USGS test"}]}]}
    (d / "manifest.json").write_text(json.dumps(man))
    return decode_r16(v, hmin, hmax)


@pytest.fixture()
def strip(tmp_path):
    """Four L0 tiles in a row (0..3, 0) of one smooth surface, plus (0,1) with buildings."""
    root = tmp_path / "src"
    H = {}
    for i in range(4):
        H[(i, 0)] = write_l0(root, i, 0)
    return root, H


def sha_tree(root: Path) -> dict:
    return {str(p.relative_to(root)): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in sorted(root.rglob("*")) if p.is_file()}


# ---------------------------------------------------------------- terrain
def test_node_subsample_is_exact_l0_nodes(strip):
    root, H = strip
    src = lod.L0Source(root)
    for level in (1, 2):
        s = 1 << level
        h, cov = lod.subsample_terrain(level, 0, 0, src)
        # level node (r, c) at global x = c*s (tile 0..), z = r*s; L0 nodes of tile (i,0) are exact there
        for c in (0, 1, 7, 100, 500):
            x = c * s
            i, lc = (x // 2048, x % 2048)
            if (i, 0) in H:
                assert abs(h[0, c] - H[(i, 0)][0, lc]) < 1e-9
        assert cov[0, 0] and cov[0, 2048 if level == 1 else 1024]
        # rows above z = 2048 have no L0 (tile (0,1) absent): ocean placeholder, not covered
        assert not cov[N // s + 1:, :].any()
        assert np.all(h[N // s + 1:, :] == lod.WATER_NAVD88_M)


def test_children_nodes_equal_parent_nodes(strip):
    root, _ = strip
    src = lod.L0Source(root)
    h1, _ = lod.subsample_terrain(1, 0, 0, src)
    h2, _ = lod.subsample_terrain(2, 0, 0, src)
    # level-2 node c is at x = 4c, level-1 node c' at x = 2c': h2[0, c] == h1[0, 2c] on the first L1 tile,
    # and the second half of the level-2 tile comes from L1 tile (1, 0)
    assert np.array_equal(h2[0, :1025], h1[0, 0:2049:2])
    h1b, _ = lod.subsample_terrain(1, 1, 0, src)
    assert np.array_equal(h2[0, 1024:], h1b[0, 0:2049:2])


def test_shared_edges_agree_between_neighbours(strip, tmp_path):
    root, _ = strip
    src = lod.L0Source(root)
    out = tmp_path / "out"
    for I in (0, 1):
        lod.bake_level_tile(1, I, 0, src, out, rtin=False)
    a, b = out / "L1" / "00_00", out / "L1" / "01_00"
    assert lod.check_edge_agreement(a, b, "x") == []


def test_edge_check_detects_a_seam(strip, tmp_path):
    root, _ = strip
    src = lod.L0Source(root)
    out = tmp_path / "out"
    for I in (0, 1):
        lod.bake_level_tile(1, I, 0, src, out, rtin=False)
    p = out / "L1" / "01_00" / "terrain.r16"
    v = np.fromfile(p, "<u2").reshape(SAMPLES, SAMPLES)
    v[:, 0] = np.clip(v[:, 0].astype(int) + 4000, 0, 65535).astype("<u2")
    v.tofile(p)
    assert lod.check_edge_agreement(out / "L1" / "00_00", out / "L1" / "01_00", "x")


def test_error_accumulator_plane_and_quadratic():
    x = np.arange(SAMPLES, dtype=np.float64)
    plane = 3.0 + 0.01 * x[None, :] + 0.02 * x[:, None]
    acc = lod.ErrAccumulator()
    acc.add(plane, 4)
    assert acc.result()["max_m"] < 1e-9
    quad = np.tile(0.001 * (x - 1024.0) ** 2, (SAMPLES, 1))
    acc = lod.ErrAccumulator()
    acc.add(quad, 8)
    r = acc.result()
    # bilinear error of a parabola over cells of width s is a*s^2/4 (at the cell middle)
    assert abs(r["max_m"] - 0.001 * 64 / 4) < 1e-6
    assert 0 < r["rms_m"] < r["max_m"] and r["p99_m"] >= r["rms_m"]


def test_rtin_plane_is_two_triangles_and_monotone():
    n = 65
    x = np.arange(n, dtype=np.float64)
    plane = 0.3 * x[None, :] - 0.1 * x[:, None]
    assert lod.rtin_triangles(plane, 0.01) == 2
    rng = np.random.default_rng(3)
    bumpy = plane + rng.normal(0, 1.0, (n, n))
    counts = [lod.rtin_triangles(bumpy, t) for t in (0.05, 0.5, 2.0, 8.0)]
    assert counts == sorted(counts, reverse=True) and counts[0] > counts[-1] >= 2
    assert counts[0] <= 2 * (n - 1) ** 2


def test_rtin_for_cap_meets_cap_or_reports_ladder_end():
    n = 129
    rng = np.random.default_rng(5)
    h = rng.normal(0, 1.0, (n, n))
    nominal, t, tri = lod.rtin_for_cap(h, 0.1, 3000)
    assert nominal > 3000 and tri <= 3000 and t > 0.1


def test_slope_u8():
    x = np.arange(SAMPLES, dtype=np.float64)
    s = lod.slope_u8(np.tile(x, (SAMPLES, 1)), 1.0)           # 45 degrees
    assert abs(int(s[10, 10]) - 128) <= 1


# ---------------------------------------------------------------- water
def test_water_majority_and_orientation(tmp_path):
    root = tmp_path / "src"
    # land block at the SW corner of L0 (0,0): rows 1536..2047 (south, row 0 = north), cols 0..511
    write_l0(root, 0, 0, water_land_block=(1536, 2048, 0, 512))
    src = lod.L0Source(root)
    w = lod.downsample_water(2, 0, 0, src)
    assert w.shape == (N, N) and w.dtype == np.uint8
    per = N // 4                                               # 512 reduced cells per L0 tile edge
    # L0 (0,0) is the SW block of the level-2 tile: bottom-left (south = high row index in a north-up image)
    sw = w[N - per:, :per]
    assert sw[-1, 0] == 0 and sw[-1, 100] == 0                 # 512/4 = 128 reduced cells of land at the SW
    assert sw[-1, 127] == 0 and sw[-1, 128] == 255 and sw[0, 0] == 255
    assert w[0, N - 1] == 255                                  # uncovered region is open ocean placeholder


# ---------------------------------------------------------------- buildings and meshes
def building_fixture(tmp_path):
    root = tmp_path / "src"
    feats = [
        feat("osm:w1", box(100, 100, 140, 130), 24.0),                                  # short box
        feat("osm:w2", box(500, 500, 560, 540), 150.0),                                 # tall box
        feat("osm:w3", box(900, 900, 950, 950), 30.0, excluded="underground"),          # excluded
        feat("osm:w4", {"type": "MultiPolygon", "coordinates": [box(1200, 100, 1230, 130)["coordinates"],
                                                               box(1300, 100, 1330, 130)["coordinates"]]}, 18.0),
        feat("osm:w5", box(700, 700, 800, 760), 40.0, has_parts=True),                  # outline, no remainder
        feat("osm:w6", box(700, 700, 750, 760), 40.0, part_of="osm:w5"),
        feat("osm:w7", box(750, 700, 800, 760), 90.0, part_of="osm:w5"),
        feat("osm:w8", {"type": "Polygon", "coordinates": [[[1500, 1500], [1560, 1500], [1560, 1520], [1520, 1520],
                                                             [1520, 1560], [1500, 1560], [1500, 1500]]]}, 33.0),   # L shape
    ]
    write_l0(root, 0, 0, feats=feats)
    return root


def test_collect_pieces_follows_l0_rules(tmp_path):
    root = building_fixture(tmp_path)
    src = lod.L0Source(root)
    got = sorted(b for _, _, b in lod.collect_pieces(src.buildings(0, 0), set()))
    assert got == ["osm:w1", "osm:w2", "osm:w4", "osm:w6", "osm:w7", "osm:w8"]    # w3 excluded, w5 via parts only


def test_ear_clip_concave_area():
    ring = [(0, 0), (60, 0), (60, 20), (20, 20), (20, 60), (0, 60)]
    tris = lod._ear_clip(ring)
    area = sum(abs((ring[b][0] - ring[a][0]) * (ring[c][1] - ring[a][1]) -
                   (ring[b][1] - ring[a][1]) * (ring[c][0] - ring[a][0])) / 2 for a, b, c in tris)
    assert len(tris) == 4 and abs(area - (60 * 20 + 20 * 40)) < 1e-9


def test_b1_b2_forms_meshes_and_ranges(tmp_path):
    root = building_fixture(tmp_path)
    src = lod.L0Source(root)
    out = tmp_path / "out"
    rep = lod.bake_level_tile(2, 0, 0, src, out, rtin=False)
    d = out / "L2" / "00_00"
    man = json.loads((d / "manifest.json").read_text())
    assert set(man["buildings"]) == {"form_b1", "form_b2"}
    b1 = lod.read_lodmesh((d / "b1.lodmesh").read_bytes())
    i1 = json.loads((d / "b1.index.json").read_text())
    bids = [b["bid"] for b in i1["buildings"]]
    assert bids == sorted(bids) == ["osm:w1", "osm:w2", "osm:w4", "osm:w6", "osm:w7", "osm:w8"]
    # every range points inside its chunk's index block and lies on triangle boundaries
    for bi, ci, first, cnt in b1["ranges"]:
        c = b1["chunks"][ci]
        assert cnt % 3 == 0 and first + cnt <= c["ic"]
    assert [r[0] for r in b1["ranges"]] == sorted(r[0] for r in b1["ranges"])
    # tops are exact: max y over vertices of the tall box range equals base + height
    k = bids.index("osm:w2")
    ys = []
    for bi, ci, first, cnt in b1["ranges"]:
        if bi == k:
            c = b1["chunks"][ci]
            idx = b1["indices"][c["if"] + first:c["if"] + first + cnt].astype(int) + c["vf"]
            ys.append(b1["vertices"][idx, 1])
    assert abs(np.concatenate(ys).max() - (20.0 + 150.0)) < 0.05
    # B2: only the >= 60 m prisms are indexed, plus the pooled surface
    i2 = json.loads((d / "b2.index.json").read_text())
    assert [b["bid"] for b in i2["buildings"]] == ["osm:w2", "osm:w7"] and i2["surface"]["columns"] > 0
    assert rep["forms"]["b2"]["within_cap"] and rep["forms"]["b1"]["within_cap"]
    e = man["errors"]["buildings"]
    assert e["height_max_m"] == 0.0 and e["footprint_max_m"] <= lod.FOOTPRINT_TOL_M[2] + 1e-6


def test_normals_point_outward_and_roofs_up(tmp_path):
    root = tmp_path / "src"
    write_l0(root, 0, 0, feats=[feat("osm:w1", box(100, 100, 140, 130), 24.0)])
    src = lod.L0Source(root)
    out = tmp_path / "out"
    lod.bake_level_tile(1, 0, 0, src, out, rtin=False)
    m = lod.read_lodmesh((out / "L1" / "00_00" / "b1.lodmesh").read_bytes())
    V, I = m["vertices"], m["indices"].astype(int)
    cen = np.array([120.0, 31.5, 115.0])
    up = down = 0
    for ch in m["chunks"]:
        idx = I[ch["if"]:ch["if"] + ch["ic"]].reshape(-1, 3) + ch["vf"]
        a, b, c = V[idx[:, 0]], V[idx[:, 1]], V[idx[:, 2]]
        n = np.cross(b - a, c - a)
        mid = (a + b + c) / 3
        out_dir = mid - cen
        for ni, oi in zip(n, out_dir):
            if abs(ni[1]) < 1e-6:                           # wall: normal horizontal and outward
                assert np.dot(ni[[0, 2]], oi[[0, 2]]) > 0
            elif ni[1] > 0:
                up += 1
            else:
                down += 1
    assert up >= 2 and down == 0
    nrm = m["normals"]
    assert np.allclose(np.linalg.norm(nrm, axis=1), 1.0, atol=1e-6)


def test_b2_surface_is_adaptive_and_never_exceeds_cap(tmp_path):
    root = tmp_path / "src"
    feats = []
    for a in range(60):
        for b in range(60):
            x, z = 20 + a * 33.0, 20 + b * 33.0
            feats.append(feat(f"osm:w{a:02d}{b:02d}", box(x, z, x + 12, z + 12), 10.0 + (a * b) % 40))
    write_l0(root, 0, 0, feats=feats)
    src = lod.L0Source(root)
    out = tmp_path / "out"
    rep = lod.bake_level_tile(3, 0, 0, src, out, rtin=False)
    b2 = rep["forms"]["b2"]
    assert b2["triangles"] <= lod.BUDGET["b2_tris_cap"] and b2["within_cap"]
    idx = json.loads((out / "L3" / "00_00" / "b2.index.json").read_text())
    assert idx["surface"]["cell_m"] >= 2 * 8 and idx["surface"]["columns"] > 0


# ---------------------------------------------------------------- determinism, validator, CLI
def test_two_bakes_are_byte_identical(tmp_path):
    root = building_fixture(tmp_path)
    src = lod.L0Source(root)
    runs = []
    for k in range(2):
        out = tmp_path / f"out{k}"
        for level in (1, 2, 3, 4):
            for (I, J) in lod.pyramid_tiles(level, src):
                lod.bake_level_tile(level, I, J, src, out, rtin=True)
        runs.append(sha_tree(out))
    assert runs[0] == runs[1] and sum(k.endswith('manifest.json') for k in runs[0]) == 4 and len(runs[0]) >= 26


def test_validator_accepts_and_refuses(tmp_path):
    root = building_fixture(tmp_path)
    src = lod.L0Source(root)
    out = tmp_path / "out"
    lod.bake_level_tile(1, 0, 0, src, out, rtin=False)
    d = out / "L1" / "00_00"
    assert lod.validate_level_tile(d, src) == []
    man = json.loads((d / "manifest.json").read_text())
    for k in ("spec", "tile", "level", "errors", "source_fingerprint", "files", "attribution", "licence_ok"):
        assert k in man
    for k in ("max_m", "rms_m", "p99_m"):
        assert k in man["errors"]["terrain"]
    # tamper with a file
    (d / "water.png").write_bytes((d / "water.png").read_bytes() + b"x")
    assert any("sha256 mismatch" in e for e in lod.validate_level_tile(d))
    # changed source -> fingerprint mismatch
    lod.bake_level_tile(1, 0, 0, src, out, rtin=False)
    mp = root / "L0" / "00_00" / "manifest.json"
    m0 = json.loads(mp.read_text())
    m0["note"] = "changed"
    mp.write_text(json.dumps(m0))
    assert any("source_fingerprint" in e for e in lod.validate_level_tile(d, lod.L0Source(root)))
    # missing errors block
    man2 = dict(man)
    man2["errors"] = {"terrain": {}, "buildings": {}}
    (d / "manifest.json").write_text(json.dumps(man2))
    assert any("errors.terrain.max_m" in e for e in lod.validate_level_tile(d))


def test_cli_writes_cache_layout_and_refuses_streamingassets(tmp_path, monkeypatch):
    sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
    import bake_lod
    root = building_fixture(tmp_path)
    out = tmp_path / "out"
    rc = bake_lod.main(["--src", str(root), "--out", str(out), "--levels", "1", "--no-rtin",
                        "--report", str(tmp_path / "rep.json")])
    assert rc == 0 and (out / "L1" / "00_00" / "manifest.json").exists()
    assert json.loads((tmp_path / "rep.json").read_text())[0]["tile"] == "L1_00_00"
    with pytest.raises(SystemExit):
        bake_lod.main(["--src", str(root), "--out", str(tmp_path / "Assets" / "StreamingAssets" / "x"), "--levels", "1"])


def test_pyramid_tile_selection():
    class S:                                  # quad-tree indexing identical to World.Streaming GeoTileId
        tiles = {(30, 36): 1, (31, 36): 1, (25, 38): 1}
    assert lod.pyramid_tiles(1, S) == [(12, 19), (15, 18)]
    assert lod.pyramid_tiles(4, S) == [(1, 2)]


# ---------------------------------------------------------------- real baked district tiles
REAL = TILES_ROOT / "L0" / "30_36" / "terrain.r16"


@pytest.mark.skipif(not REAL.exists() or REAL.stat().st_size < 1_000_000, reason="baked L0 tiles (LFS) not present")
def test_real_tiles_l1_and_rtin_matches_m1_count(tmp_path):
    src = lod.L0Source(TILES_ROOT)
    rep = lod.bake_level_tile(1, 15, 18, src, tmp_path, rtin=True)
    H = src.heights(30, 36)
    h, cov = lod.subsample_terrain(1, 15, 18, src)
    assert np.array_equal(h[0:1024, 0:1024], H[0:2048:2, 0:2048:2]) and cov[0:1024, 0:1024].all()
    # R-101 [P9]: the M1 RTIN at 0.1 m on L0_30_36 had 589 k triangles (max 0.196 m); this implementation must agree
    n = lod.rtin_triangles(H, 0.1)
    assert abs(n - 589_000) / 589_000 < 0.05, n
    assert rep["bytes"]["terrain.r16"] == SAMPLES * SAMPLES * 2
    assert lod.validate_level_tile(tmp_path / "L1" / "15_18", src) == []


# ---------------------------------------------------------------- T-108b noise pre-filter
def test_prefilter_is_bounded_edge_preserving_and_noise_reducing():
    rng = np.random.default_rng(7)
    h = np.zeros((200, 200))
    h[:, 100:] = 5.0                                     # a 5 m step (an edge) between two flat halves
    noisy = h + rng.normal(0, 0.03, h.shape)
    eps, sig = 0.1, 0.4
    f = lod.bilateral_bounded(noisy, eps, sig)
    assert np.abs(f - noisy).max() <= eps + 1e-12        # hard error bound
    assert np.abs(f[:, :90] - 0).std() < 0.6 * np.abs(noisy[:, :90]).std()   # noise reduced
    assert abs(f[:, 105:].mean() - 5.0) < 0.01 and abs(f[:, 95].mean()) < 0.05
    assert (f[:, 100:].min() - f[:, :100].max()) > 4.7   # the step survives (no smearing across the edge)


def test_prefilter_neighbours_agree_on_shared_edge_and_bound_holds(strip):
    root, _ = strip
    src = lod.L0Source(root)
    thr = 0.2
    a, ha, _, st = lod.prefiltered_terrain(1, 0, 0, src, thr)
    b, hb, _, _ = lod.prefiltered_terrain(1, 1, 0, src, thr)
    assert np.array_equal(a[:, -1], b[:, 0])
    assert st["max_abs_change_m"] <= lod.PREFILTER_EPS * thr + 1e-9
    assert np.abs(a - ha).max() <= lod.PREFILTER_EPS * thr + 1e-9
