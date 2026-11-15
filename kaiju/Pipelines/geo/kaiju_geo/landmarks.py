"""Sourced landmark prisms (T-022 step 1; R-020 §2). Pure function of the baked building features and
config/landmarks.json: each landmark is a part stacked on the existing OSM prism (base = baked top of the
structure at the landmark position), emitted only when the sourced top exceeds the baked top by more than
tol_height_m. Bridge items are T-023."""
from __future__ import annotations

import math

import shapely
from shapely.geometry import Point, Polygon, mapping, shape
from shapely.geometry.polygon import orient

from .common import load_config, sha256_bytes, canonical_json

FIELDS = ("lid", "bid", "part_of", "kind", "part_idx", "base_navd88_m", "top_navd88_m", "height_m", "min_height_m",
          "roof_shape", "height_source", "pos_source", "source_ids", "confidence", "tol_height_m", "tol_pos_m")
TILE = 2048.0
GGB_SLUGS = ("ggb_tower_s", "ggb_tower_n", "ggb_deck", "ggb_cable_a", "ggb_cable_b")


def _top(p):
    return p["base_navd88_m"] + p["height_m"]


def build(tile_id: str, bfeats: list, cfg: dict | None = None):
    """-> (features, checked). `checked` has one dict per configured landmark of the tile (emitted or already right)."""
    cfg = cfg or load_config("landmarks.json")
    by_id = {f["id"]: f for f in bfeats}
    feats, checked = [], []
    for lm in cfg["landmarks"]:
        if lm["tile"] != tile_id:
            continue
        par = by_id.get(lm["part_of"])
        if par is None:
            checked.append({"slug": lm["slug"], "status": "skipped", "reason": f"{lm['part_of']} not in this tile's buildings"})
            continue
        pp = par["properties"]
        pt = Point(lm["pos_baytm"])
        cands = [f for f in bfeats if (f["id"] == par["id"] or f["properties"].get("part_of") == par["id"])
                 and not f["properties"]["excluded"] and (f["id"] in lm.get("footprint_osm", ()) or shape(f["geometry"]).buffer(lm["tol_pos_m"]).contains(pt))]
        if not cands:
            cands = [par]
        base = pp["base_navd88_m"]
        baked_top = max(f["properties"]["base_navd88_m"] + f["properties"]["height_m"] for f in cands)
        sourced_top = round(base + lm["height_above_base_m"], 3)
        rec = {"slug": lm["slug"], "baked_top_navd88_m": round(baked_top, 3), "sourced_top_navd88_m": sourced_top,
               "delta_m": round(sourced_top - baked_top, 3), "tol_height_m": lm["tol_height_m"]}
        if sourced_top - baked_top <= lm["tol_height_m"]:
            rec["status"] = "already right in buildings" if abs(sourced_top - baked_top) <= lm["tol_height_m"] else \
                "baked above sourced (not lowered; reported)"
            checked.append(rec)
            continue
        # footprint: the baked feature (structure or building:part) that contains the position, topmost first
        fp = sorted(cands, key=lambda f: (-(f["properties"]["height_m"]), f["id"]))[0]
        geom = fp["geometry"]
        spire = lm.get("spire_m")
        pieces = []
        if spire:
            pieces.append((0, "crown", baked_top, sourced_top - spire, "flat"))
            pieces.append((1, "spire", sourced_top - spire, sourced_top, "spire"))
        else:
            pieces.append((0, lm["kind"], baked_top, sourced_top, lm["roof_shape"]))
        for idx, kind, b, t, rs in pieces:
            if t - b <= 0:
                continue
            feats.append({"type": "Feature", "id": f"lm:{lm['slug']}:p{idx}", "properties": {
                "lid": f"lm:{lm['slug']}:p{idx}", "bid": f"landmark:{lm['slug']}:p{idx}", "part_of": lm["part_of"],
                "kind": kind, "part_idx": idx, "base_navd88_m": round(b, 3), "top_navd88_m": round(t, 3),
                "height_m": round(t - b, 3), "min_height_m": round(b - base, 3), "roof_shape": rs,
                "height_source": lm["height_source"], "pos_source": lm["pos_source"], "source_ids": lm["source_ids"],
                "confidence": lm["confidence"], "tol_height_m": lm["tol_height_m"], "tol_pos_m": lm["tol_pos_m"],
                "footprint_from": fp["id"]}, "geometry": geom})
        rec["status"] = "emitted"
        checked.append(rec)
    if cfg is None or "bridge" in cfg:
        bf, bc = _bridge(tile_id, cfg)
        feats += bf
        checked += bc
    feats.sort(key=lambda f: f["id"])
    checked.sort(key=lambda r: r["slug"])
    return feats, checked


def _tile_of(x, z):
    return f"L0_{int(math.floor(x / TILE))}_{int(math.floor(z / TILE))}"


def cut_polyline(pts):
    """pts: [(x, z, y)] -> [(tile_id, [(x, z, y)])]. The line is cut where it crosses a tile edge (multiples of 2048 m); the crossing
    node is computed once (edge coordinate exact, the other by linear interpolation) and shared by both pieces (float64)."""
    nodes = [pts[0]]
    for a, b in zip(pts[:-1], pts[1:]):
        cr = []
        for k in (0, 1):
            ka, kb = math.floor(a[k] / TILE), math.floor(b[k] / TILE)
            if ka != kb:
                lo, hi = (ka, kb) if ka < kb else (kb, ka)
                for e in range(lo + 1, hi + 1):
                    t = ((e * TILE) - a[k]) / (b[k] - a[k])
                    n = [a[i] + t * (b[i] - a[i]) for i in range(3)]
                    n[k] = e * TILE
                    cr.append((t, tuple(n)))
        cr.sort()
        nodes += [n for _, n in cr] + [b]
    pieces, cur, ctile = [], [nodes[0]], None
    for a, b in zip(nodes[:-1], nodes[1:]):
        t = _tile_of((a[0] + b[0]) / 2, (a[1] + b[1]) / 2)
        if ctile is not None and t != ctile:
            pieces.append((ctile, cur))
            cur = [a]
        ctile = t
        cur.append(b)
    pieces.append((ctile, cur))
    return pieces


def _bridge(tile_id, cfg):
    """T-023: Golden Gate Bridge features of `tile_id` from config/ggb.json (geometry measured from 3DEP lidar by measure_ggb.py)
    and the sourced facts of R-020 §3. A feature belongs to the tile of its representative point; lines are cut at tile edges."""
    cfg = cfg or load_config("landmarks.json")
    br, g = cfg["bridge"], load_config("ggb.json")
    ox, oz = g["axis_origin_baytm"]
    dx, dz = g["axis_unit"]
    nx, nz = -dz, dx
    feats, checked = [], []

    def base(slug, kind, idx, lid_tail, geom, base_m, top_m, hs, conf, extra):
        p = {"lid": f"lm:{slug}:{lid_tail}", "bid": f"landmark:{slug}:{lid_tail}", "part_of": None, "kind": kind, "part_idx": idx,
             "base_navd88_m": round(base_m, 3), "top_navd88_m": round(top_m, 3), "height_m": round(top_m - base_m, 3), "min_height_m": 0.0,
             "roof_shape": "flat", "height_source": hs, "pos_source": "lidar_3dep", "source_ids": br["source_ids"],
             "confidence": conf, "tol_height_m": br["tol_height_m"], "tol_pos_m": br["tol_pos_m"]}
        p.update(extra)
        return {"type": "Feature", "id": p["lid"], "properties": p, "geometry": geom}

    for tw in ("s", "n"):
        slug = f"ggb_tower_{tw}"
        legs = [l for l in g["legs"] if l["tower"] == tw.upper()]
        meas = max(l["top_max_m"] for l in legs)
        agree = abs(meas - br["tower_top_navd88_m"]) <= br["tower_top_tol_m"]
        top, hs, conf = (br["tower_top_navd88_m"], "official", "A") if agree else (meas, "lidar_3dep", "B")
        n_here = 0
        for l in legs:
            cx, cz = l["centroid_baytm"]
            if _tile_of(cx, cz) != tile_id:
                continue
            a = max(l["extent_along_m"], br["leg_base_m"][1]) / 2
            b = max(l["extent_across_m"], br["leg_base_m"][0]) / 2
            ring = [(cx + su * a * dx + sv * b * nx, cz + su * a * dz + sv * b * nz) for su, sv in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
            geom = mapping(orient(Polygon(ring), 1.0))
            feats.append(base(slug, "bridge_tower", l["leg"], f"l{l['leg']}", geom, br["seabed_envelope_navd88_m"], top, hs, conf,
                              {"leg_measured_top_m": l["top_max_m"], "footprint_measured_m": [l["extent_along_m"], l["extent_across_m"]]}))
            n_here += 1
        if n_here:
            checked.append({"slug": slug, "status": "emitted", "legs_in_tile": n_here, "measured_top_navd88_m": meas,
                            "sourced_top_navd88_m": br["tower_top_navd88_m"], "delta_m": round(meas - br["tower_top_navd88_m"], 3),
                            "top_used": "sourced" if agree else "measured", "base_navd88_m": br["seabed_envelope_navd88_m"]})
    lines = [("ggb_deck", "bridge_deck", 0, g["deck_v_m"], "deck", {"width_m": br["deck_width_m"]})]
    lines += [(f"ggb_cable_{'ab'[i]}", "bridge_cable", i, g["cable_v_m"][i], ("cable", i), {"diameter_m": br["cable_diameter_m"]}) for i in (0, 1)]
    for slug, kind, idx, v, key, extra in lines:
        pts = []
        for r in g["profile"]:
            y = r["deck"] if key == "deck" else r["cable"][key[1]]
            if y is not None:
                u = r["u"]
                pts.append((ox + u * dx + v * nx, oz + u * dz + v * nz, float(y)))
        n_here = 0
        for k, (t, piece) in enumerate(cut_polyline(pts)):
            if t != tile_id or len(piece) < 2:
                continue
            ys = [q[2] for q in piece]
            geom = {"type": "LineString", "coordinates": [[q[0], q[1]] for q in piece]}
            feats.append(base(slug, kind, idx, f"{t[3:]}", geom, min(ys), max(ys), "lidar_3dep", "B",
                              {**extra, "y_navd88_m": ys, "piece": k}))
            n_here += 1
        if n_here:
            checked.append({"slug": slug, "status": "emitted", "pieces_in_tile": n_here})
    return feats, checked


def source_entries(feats: list, checked: list, cfg: dict | None = None):
    cfg = cfg or load_config("landmarks.json")
    ids = {s for lm in cfg["landmarks"] if lm["slug"] in {c["slug"] for c in checked} for s in lm["source_ids"]}
    if any(c["slug"] in GGB_SLUGS for c in checked):
        ids |= set(cfg["bridge"]["source_ids"])
    ids = sorted(ids)
    out = []
    for sid in ids:
        s = cfg["sources"][sid]
        e = {"product": f"landmarks:{sid}", "item": s["item"], "url": s["url"], "licence": s["licence"],
             "attribution": s["attribution"], "fetched_utc": s["fetched_utc"],
             "source_sha256": sha256_bytes(canonical_json(s).encode())}
        if s["licence"] == "ODbL-1.0":
            e["derivative_database_candidate"] = True
        out.append(e)
    return out
