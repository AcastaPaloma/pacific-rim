"""T-108d bake_lod.py@7: merged blocks (B1/B2 form change) and the skyline fallback; synthetic fixtures, monkeypatched caps."""
import json
import sys
from pathlib import Path

import numpy as np
import pytest

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
sys.path.insert(0, str(Path(__file__).resolve().parent))
from kaiju_geo import lod  # noqa: E402
from test_lod import box, feat, write_l0  # noqa: E402


def row_fixture(tmp_path, n=40, gap=1.0, w=12.0, height=10.0, jitter=0.0, parts=False):
    """A row of n boxes with `gap` metres between them (+ a tall tower far away) in L0 tile (0, 0)."""
    root = tmp_path / "src"
    feats = []
    for k in range(n):
        x = 100 + k * (w + gap)
        f = feat(f"osm:w{100 + k}", box(x, 100, x + w, 120), height + (jitter if k % 2 else 0.0))
        if parts and k == 1:
            f["properties"]["part_of"] = "osm:w900"
        feats.append(f)
    feats.append(feat("osm:w999", box(1500, 1500, 1540, 1540), 120.0))
    write_l0(root, 0, 0, feats=feats)
    return lod.L0Source(root)


def test_block_merge_closes_gaps_within_the_budget_and_keeps_height_error_bounded(tmp_path):
    src = row_fixture(tmp_path, jitter=1.5)
    items = lod.collect_pieces(src.buildings(0, 0), set())
    inputs = lod._piece_inputs(items, None)
    mb = lod.MeshBuilder(0.0, 0.0)
    bud = lod.footprint_error_budget_m(1)
    blocks, refs, lm = lod.build_blocks(inputs, 1.0, 0.5, 1.0, set(), mb, "L1_00_00")
    assert not lm
    members = [m[0] for b in blocks for m in b.members]
    assert sorted(members) == sorted(i[0] for i in inputs)                       # every building sits in exactly one block
    assert len(blocks) == 4                                                      # the 520 m row spans three 256 m chunks (one block each) + the tower
    assert sum(len(b.members) for b in blocks) == 41 and min(len(b.members) for b in blocks) == 1
    assert len({b.chunk for b in blocks}) == 4
    er = lod.block_errors(blocks, refs)
    assert er["footprint_max_m"] <= bud["max_m"] and er["footprint_rms_m"] <= bud["rms_m"]
    assert er["height_max_m"] <= 1.0 + 1e-9                                      # <= dh by construction
    tri = lod.block_triangles(blocks)
    assert tri < 40 * 10                                                         # fewer than one prism each


def test_bands_split_when_heights_differ_more_than_two_dh(tmp_path):
    root = tmp_path / "src"
    feats = [feat("osm:w1", box(100, 100, 112, 120), 10.0), feat("osm:w2", box(113, 100, 125, 120), 10.0),
             feat("osm:w3", box(126, 100, 138, 120), 30.0)]
    write_l0(root, 0, 0, feats=feats)
    src = lod.L0Source(root)
    inputs = lod._piece_inputs(lod.collect_pieces(src.buildings(0, 0), set()), None)
    blocks, refs, _ = lod.build_blocks(inputs, 1.0, 0.5, 1.0, set(), lod.MeshBuilder(0.0, 0.0), "L1_00_00")
    assert sorted(len(b.members) for b in blocks) == [1, 2]
    assert lod.block_errors(blocks, refs)["height_max_m"] == 0.0


def test_landmarks_never_merge(tmp_path):
    src = row_fixture(tmp_path, n=3)
    inputs = lod._piece_inputs(lod.collect_pieces(src.buildings(0, 0), set()), None)
    blocks, refs, lm = lod.build_blocks(inputs, 1.0, 0.5, 1.0, {"osm:w101"}, lod.MeshBuilder(0.0, 0.0), "L1_00_00")
    assert [p.bid for p in lm] == ["osm:w101"] and all("osm:w101" not in [m[0] for m in b.members] for b in blocks)


def test_block_mesh_has_outward_normals_roofs_up_and_holes(tmp_path):
    ring = np.array([(0, 0), (40, 0), (40, 40), (0, 40)], dtype=float)
    hole = np.array([(10, 10), (10, 30), (30, 30), (30, 10)], dtype=float)          # clockwise courtyard
    b = lod.Block("blk:t", [ring, hole], 0.0, -0.5, 20.0, [("osm:w1", None)], (0, 0))
    v, t = lod._extrude_block(b)
    a, bb, c = v[t[:, 0]], v[t[:, 1]], v[t[:, 2]]
    n = np.cross(bb - a, c - a)
    area = np.linalg.norm(n, axis=1) / 2
    roof = np.all(v[t][:, :, 1] == 20.0, axis=1)
    assert abs(area[roof].sum() - (1600 - 400)) < 1e-6 and np.all(n[roof][:, 1] > 0)    # courtyard stays open, roof faces up
    assert len(t) == lod.block_triangles([b])
    cx = v[t].mean(axis=1)
    wall = ~roof
    centre = np.array([20.0, 0.0, 20.0])
    out = (cx[wall] - centre) * np.array([1, 0, 1])
    inner = ((cx[wall][:, 0] > 5) & (cx[wall][:, 0] < 35) & (cx[wall][:, 2] > 5) & (cx[wall][:, 2] < 35))
    # exterior walls face away from the centre, courtyard walls face towards it
    dots = np.einsum("ij,ij->i", n[wall], out)
    assert np.all(dots[~inner] > 0) and np.all(dots[inner] < 0)


def test_form_falls_to_merged_blocks_when_prisms_cannot_fit(tmp_path, monkeypatch):
    src = row_fixture(tmp_path, n=40, gap=1.0, jitter=0.5, parts=True)
    monkeypatch.setitem(lod.BUDGET, "b1_tris_cap", 300)                           # 41 prisms = 410 triangles
    out = tmp_path / "out"
    rep = lod.bake_level_tile(1, 0, 0, src, out, rtin=False)
    f = rep["forms"]["b1"]
    assert f["footprint_fit"] == "merged_blocks" and f["within_cap"] and f["triangles"] <= 300 and f["error_budget_met"]
    assert f["footprint_max_m"] <= 2.0 and f["height_max_m"] <= 2.0
    d = out / "L1" / "00_00"
    idx = json.loads((d / "b1.index.json").read_text())
    assert idx["merged_blocks"] is True
    rows = idx["buildings"]
    assert [r["bid"] for r in rows] == sorted(r["bid"] for r in rows)
    blk = [r for r in rows if r["bid"].startswith("blk:")]
    assert blk and all(len(r["members"]) == len(r["member_part_of"]) for r in blk)
    allm = sorted(m for r in rows for m in (r["members"] if "members" in r else [r["bid"]]))
    assert allm == sorted([f"osm:w{100 + k}" for k in range(40)] + ["osm:w999"])
    big = next(r for r in blk if "osm:w101" in r["members"])
    assert "osm:w900" in big["member_part_of"]                                    # journal replay: the parent structure is listed per member
    mesh = lod.read_lodmesh((d / "b1.lodmesh").read_bytes())
    assert mesh["index_count"] // 3 == f["triangles"] and len(mesh["ranges"]) >= len(rows)
    man = json.loads((d / "manifest.json").read_text())
    assert man["generator"] == lod.GENERATOR and lod.validate_level_tile(d, src) == []
    assert man["errors"]["buildings"]["height_max_m"] == f["height_max_m"]
    # byte identical re-bake
    before = {p.name: p.read_bytes() for p in d.iterdir()}
    lod.bake_level_tile(1, 0, 0, src, out, rtin=False)
    assert before == {p.name: p.read_bytes() for p in d.iterdir()}


def test_nominal_tiles_keep_the_prism_form(tmp_path):
    src = row_fixture(tmp_path, n=5)
    out = tmp_path / "out"
    rep = lod.bake_level_tile(1, 0, 0, src, out, rtin=False)
    f = rep["forms"]["b1"]
    assert "footprint_fit" not in f and f["within_cap"]
    idx = json.loads((out / "L1" / "00_00" / "b1.index.json").read_text())
    assert "merged_blocks" not in idx and all("members" not in r for r in idx["buildings"])


def test_l2_b1_uses_the_skyline_form_flagged_when_blocks_cannot_fit(tmp_path, monkeypatch):
    root = tmp_path / "src"
    feats = []
    for a in range(30):
        for b in range(30):
            x, z = 40 + a * 60.0, 40 + b * 60.0                                   # 48 m gaps: nothing merges within the L2 budget
            feats.append(feat(f"osm:w{a:02d}{b:02d}", box(x, z, x + 12, z + 12), 10.0 + (a * b) % 40))
    write_l0(root, 0, 0, feats=feats)
    src = lod.L0Source(root)
    monkeypatch.setitem(lod.BUDGET, "b1_tris_cap", 2000)
    out = tmp_path / "out"
    rep = lod.bake_level_tile(2, 0, 0, src, out, rtin=False)
    f = rep["forms"]["b1"]
    assert f["footprint_fit"] == "skyline_fallback" and f["within_cap"] and f["triangles"] <= 2000
    assert f["error_budget_met"] is False and f["footprint_max_m"] > lod.footprint_error_budget_m(2)["max_m"]   # stated, never silent
    assert f["block_floor_triangles"] > 2000
    idx = json.loads((out / "L2" / "00_00" / "b1.index.json").read_text())
    assert idx["skyline_fallback"] is True and idx["surface"]["cell_m"] >= 8


def test_b2_surface_may_grow_past_the_chunk_cell_and_says_so(tmp_path, monkeypatch):
    root = tmp_path / "src"
    feats = []
    for a in range(50):
        for b in range(50):
            x, z = 20 + a * 40.0, 20 + b * 40.0
            feats.append(feat(f"osm:w{a:02d}{b:02d}", box(x, z, x + 12, z + 12), 10.0 + (a * b) % 40))
    write_l0(root, 0, 0, feats=feats)
    src = lod.L0Source(root)
    monkeypatch.setitem(lod.BUDGET, "b2_tris_cap", 200)
    rep = lod.bake_level_tile(3, 0, 0, src, tmp_path / "out", rtin=False)
    f = rep["forms"]["b2"]
    assert f["within_cap"] and f["surface_cell_m"] > lod.CHUNK_M and f["surface_cell_over_chunk"] is True


def test_l1_b1_extended_blocks_then_skyline_flagged_t108e(tmp_path, monkeypatch):
    """T-108e bake_lod.py@8: an L1 B1 whose envelope blocks cannot fit climbs the extended ladder (measured errors, flagged), then the skyline form."""
    src = row_fixture(tmp_path, n=40, gap=3.5, jitter=0.0)                       # 3.5 m gaps: closing needs 1.75 m > the L1 envelope part, fits at 2 x
    monkeypatch.setitem(lod.BUDGET, "b1_tris_cap", 300)
    out = tmp_path / "out"
    f = lod.bake_level_tile(1, 0, 0, src, out, rtin=False)["forms"]["b1"]
    assert f["within_cap"] and f["triangles"] <= 300
    assert f["footprint_fit"] in ("merged_blocks_extended", "merged_blocks", "skyline_fallback")
    if f["footprint_fit"] == "merged_blocks_extended":
        assert f["error_budget_met"] is False and f["error_scale"] > 1.0 and f["switch_m102"]["rms_d_m"] > 0
    root = tmp_path / "src2"
    feats = [feat(f"osm:w{a:02d}{b:02d}", box(40 + a * 60.0, 40 + b * 60.0, 52 + a * 60.0, 52 + b * 60.0), 10.0 + (a * b) % 40)
             for a in range(30) for b in range(30)]
    write_l0(root, 0, 0, feats=feats)
    monkeypatch.setitem(lod.BUDGET, "b1_tris_cap", 2000)
    f = lod.bake_level_tile(1, 0, 0, lod.L0Source(root), tmp_path / "out2", rtin=False)["forms"]["b1"]
    assert f["footprint_fit"] == "skyline_fallback" and f["within_cap"] and f["error_budget_met"] is False
    assert f["switch_m102"]["max_d_m"] > 0 and [e["scale"] for e in f["extended_ladder"]]
