"""T-104b: emission of config/landmarks_bay.json into the landmarks layer (pure functions, offline)."""
import json

import numpy as np
import pytest
from shapely.geometry import box, mapping

from kaiju_geo import landmarks_bay_emit as E
from kaiju_geo.common import canonical_json, load_config

DOC = load_config("landmarks_bay.json")


def flat(z):
    return lambda x, zz: np.full(np.shape(x), float(z))


def _b(bid, g, base, h, part_of=None):
    return {"type": "Feature", "id": bid, "geometry": mapping(g), "properties": {
        "bid": bid, "part_of": part_of, "base_navd88_m": base, "height_m": h, "excluded": None}}


def test_plan_covers_every_landmark_and_matches_build():
    plan = E.plan(DOC)
    assert len(plan) >= 30
    # every tile of the plan emits exactly the planned slugs (towers, decks, cranes) or checks the planned building slugs
    for tid, slugs in plan.items():
        feats, checked = E.build(tid, [], flat(-5.0), DOC)
        got = {c["slug"] for c in checked if c["status"] in ("emitted", "skipped")}
        assert got == set(slugs), tid
    # a tile outside every landmark emits nothing
    assert E.build("L0_01_01", [], flat(0.0), DOC) == ([], [])


def test_every_tower_deck_and_crane_is_emitted_once():
    seen = {}
    for tid in E.affected_tiles(DOC):
        feats, _ = E.build(tid, [], flat(-5.0), DOC)
        for f in feats:
            k = f["properties"]["kind"]
            if k in ("bridge_tower", "port_crane"):
                seen[f["id"]] = seen.get(f["id"], 0) + 1
    towers = [t["slug"] for t in E.tower_rows(DOC)]
    cranes = [c["id"] for c in E.crane_rows(DOC)]
    assert all(seen[f"lm:{s}:t"] == 1 for s in towers)
    assert all(seen[f"lm:port_of_oakland_crane_{c}:p"] == 1 for c in cranes)


def test_tower_prism_from_surface_to_used_top():
    t = E.tower_rows(DOC)[0]
    tid = E.tile_id_of(*t["pos_baytm"])
    feats, _ = E.build(tid, [], flat(-20.0), DOC)
    f = next(f for f in feats if f["id"] == f"lm:{t['slug']}:t")
    p = f["properties"]
    assert p["kind"] == "bridge_tower" and p["base_navd88_m"] == -20.0 and p["top_navd88_m"] == round(t["height_used_m"], 3)
    ring = f["geometry"]["coordinates"][0]
    xs, zs = [q[0] for q in ring], [q[1] for q in ring]
    assert max(xs) - min(xs) == pytest.approx(t["extent_m"][0]) and max(zs) - min(zs) == pytest.approx(t["extent_m"][1])
    assert p["tol_pos_m"] == 5.0 and p["confidence"] in "AB"


def test_deck_lines_are_cut_at_tile_edges_with_shared_nodes():
    ends = {}
    pieces = 0
    for tid in E.affected_tiles(DOC):
        feats, _ = E.build(tid, [], flat(0.0), DOC)
        for f in feats:
            p = f["properties"]
            if p["kind"] != "bridge_deck":
                continue
            pieces += 1
            c, ys = f["geometry"]["coordinates"], p["y_navd88_m"]
            assert len(c) == len(ys) >= 2 and p["width_m"] > 0
            for k in (0, -1):
                if c[k][0] % 2048.0 == 0.0 or c[k][1] % 2048.0 == 0.0:
                    ends.setdefault((p["way"], round(c[k][0], 6), round(c[k][1], 6)), []).append((tid, ys[k]))
    assert pieces > 40 and ends
    for k, v in ends.items():
        assert len(v) == 2 and v[0][0] != v[1][0] and v[0][1] == pytest.approx(v[1][1]), k   # both pieces, equal height at the cut


def test_rail_way_gets_no_deck():
    slugs = {d["slug"] for d in E.deck_lines(DOC)}
    assert not any("dumbarton_rail" in s and s.endswith("w154042093") for s in slugs)


def test_building_landmark_stacks_only_when_higher():
    e = next(x for x in E.building_entries(DOC) if x["slug"] == "ordway_building")
    tid, bid = e["tile"], e["part_of"]
    g = box(e["pos_baytm"][0] - 10, e["pos_baytm"][1] - 10, e["pos_baytm"][0] + 10, e["pos_baytm"][1] + 10)
    h = e["height_above_base_m"]
    f, c = E.build(tid, [_b(bid, g, 5.0, h)], flat(0.0), DOC)
    cc = next(x for x in c if x["slug"] == "ordway_building")
    assert cc["status"] == "already right in buildings" and not [x for x in f if x["id"].startswith("lm:ordway")]
    f, c = E.build(tid, [_b(bid, g, 5.0, h - 30.0)], flat(0.0), DOC)
    part = [x for x in f if x["id"].startswith("lm:ordway_building")]
    assert len(part) == 1 and part[0]["properties"]["top_navd88_m"] == pytest.approx(5.0 + h, abs=1e-3)
    assert part[0]["properties"]["part_of"] == bid
    assert next(x for x in c if x["slug"] == "ordway_building")["status"] == "emitted"


def test_missing_structure_is_recorded_not_silent():
    e = next(x for x in E.building_entries(DOC) if x["slug"] == "ordway_building")
    f, c = E.build(e["tile"], [], flat(0.0), DOC)
    assert next(x for x in c if x["slug"] == "ordway_building")["status"] == "skipped"


def test_output_is_deterministic_and_sorted():
    for tid in ("L0_30_36", "L0_33_37"):
        a = E.build(tid, [], flat(-3.0), DOC)
        b = E.build(tid, [], flat(-3.0), DOC)
        assert canonical_json(a[0]) == canonical_json(b[0]) and canonical_json(a[1]) == canonical_json(b[1])
        assert [f["id"] for f in a[0]] == sorted(f["id"] for f in a[0])
        assert len({f["properties"]["lid"] for f in a[0]}) == len(a[0])


def test_source_entries_resolve_to_registry():
    _, c = E.build("L0_36_37", [], flat(0.0), DOC)
    for e in E.source_entries(c, DOC):
        assert e["licence"] and e["attribution"] and e["url"] and e["product"].startswith("landmarks_bay:")
        assert (e["licence"] != "ODbL-1.0") or e["derivative_database_candidate"]


def test_bake_fingerprint_inputs_include_the_new_files():
    from kaiju_geo import bake
    assert "kaiju_geo/landmarks_bay_emit.py" in bake.CODE_FILES and "config/landmarks_bay.json" in bake.CONFIG_FILES
    json.dumps(DOC["_doc"])
