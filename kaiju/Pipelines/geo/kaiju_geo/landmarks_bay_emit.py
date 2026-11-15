"""T-104b: emit the T-104 Bay landmarks (config/landmarks_bay.json, landmarks_bay@v1) into the landmarks layer of a Bay tile.

Pure function of (tile id, baked buildings, terrain sampler, document). Used by bake.bake() only for the Bay driver's plan (never for
the 15 committed M1 tiles, so their bytes stay as reviewed). Same feature schema as T-022/T-023 (`landmarks.FIELDS` + extras), so the
C# `GeoLandmarks` reads towers (`bridge_tower`, Polygon) and decks (`bridge_deck`, LineString + `y_navd88_m`) unchanged; crane points
(`port_crane`, Point) are unknown kinds to the importer (ignored, kept for T-105 and M-005.1).

Ownership: a feature belongs to the tile of its representative position (towers/cranes: their position; building landmarks: the
OSM centroid of the structure; deck lines are cut at tile edges, shared node exact, like T-023). Nothing is invented: the width of a
deck line is the bake.json class default for its OSM `highway` (Assumed, flagged `width_source`), rail ways get no deck line."""
from __future__ import annotations

import math

import numpy as np
from shapely.geometry import Polygon
from shapely.geometry.polygon import orient

from . import landmarks
from .common import canonical_json, load_config, sha256_bytes
from .landmarks_bay import height_tol

TILE = landmarks.TILE
BASE_GRID = 5            # tower base = minimum baked surface over a BASE_GRID x BASE_GRID grid of the footprint
POS_TOL_M = 5.0


def tile_id_of(x: float, z: float) -> str:
    return f"L0_{int(math.floor(x / TILE)):02d}_{int(math.floor(z / TILE)):02d}"


def load(path_doc=None) -> dict:
    return path_doc or load_config("landmarks_bay.json")


def _osm_bid(osm_id: str) -> str:
    return f"osm:{osm_id}"


def building_entries(doc: dict) -> list[dict]:
    """Landmarks that are existing OSM structures (Oakland towers, Alcatraz lighthouse, airport towers) as T-022 config rows: a prism
    part is stacked only when the sourced top exceeds the baked top by more than tol_height_m."""
    out = []

    def add(slug, name, rec, hsrc):
        x, z = rec["pos_baytm"]
        h = rec.get("height_m")
        if not h or not rec.get("osm_id"):
            return
        srcs = ["osm"] + (["lidar_3dep"] if hsrc == "lidar_3dep" else [])
        off = rec.get("official_height_m") or rec.get("official") or {}
        if isinstance(off, dict) and off.get("src") and off["src"] in doc["sources"]:
            srcs.append(off["src"])
        out.append({"slug": slug, "kind": "crown", "tile": tile_id_of(x, z), "part_of": _osm_bid(rec["osm_id"]), "pos_baytm": [x, z],
                    "height_above_base_m": float(h), "roof_shape": "flat", "height_source": hsrc, "source_ids": sorted(set(srcs)),
                    "pos_source": "osm", "confidence": rec.get("confidence", "B"), "tol_height_m": round(height_tol(h), 3),
                    "tol_pos_m": POS_TOL_M, "footprint_osm": [_osm_bid(rec["osm_id"])]})

    for b in doc["buildings"]:
        add(b["slug"], b["name"], b, b.get("height_source", "official"))
    a = doc.get("alcatraz", {}).get("lighthouse")
    if a:
        add("alcatraz_lighthouse", "Alcatraz lighthouse", a, a.get("height_source", "lidar_3dep"))
    for ap in doc["airports"]:
        t = ap.get("tower")
        if t and t.get("osm_id"):
            add(f"{ap['slug']}_tower", f"{ap['name']} control tower", t, t.get("height_source", "osm"))
    return out


def tower_rows(doc: dict) -> list[dict]:
    rows = []
    for b in doc["bridges"]:
        for k, t in enumerate(b.get("towers", [])):
            rows.append(dict(t, bridge=b["slug"], idx=k))
    return rows


def deck_lines(doc: dict, rcfg: dict | None = None) -> list[dict]:
    """One 3-D polyline per OSM road way of every bridge group: [(x, z, y)] at the measured deck bins (decimated profile)."""
    rcfg = rcfg or load_config("bake.json")["roads"]
    out = []
    for b in doc["bridges"]:
        for w in b["ways"]:
            hw = w.get("highway")
            prof = [r for r in w.get("deck_profile", []) if r.get("deck") is not None]
            pts = w["pts_baytm"]
            if hw not in rcfg["class_width_m"] or len(prof) < 2 or len(pts) < 2:
                continue
            cum = np.concatenate([[0.0], np.cumsum([math.dist(p, q) for p, q in zip(pts[:-1], pts[1:])])])
            P = np.asarray(pts, float)
            u = np.array([r["u"] for r in prof], float)
            u = np.clip(u, 0.0, cum[-1])
            xs, zs = np.interp(u, cum, P[:, 0]), np.interp(u, cum, P[:, 1])
            line = [(round(float(x), 3), round(float(z), 3), float(r["deck"])) for x, z, r in zip(xs, zs, prof)]
            width = rcfg["class_width_m"].get(hw, rcfg["class_width_m"]["_default"])
            out.append({"slug": f"{b['slug']}_deck_{w['id']}", "bridge": b["slug"], "way": w["id"], "highway": hw, "width_m": width,
                        "pts": line})
    return out


def crane_rows(doc: dict) -> list[dict]:
    return list(doc["cranes"].get("cranes", []))


def plan(doc: dict | None = None) -> dict[str, list[str]]:
    """tile id -> sorted slugs this tile emits (also the set of tiles to re-bake when the document changes)."""
    doc = load(doc)
    out: dict[str, set] = {}
    for t in tower_rows(doc):
        out.setdefault(tile_id_of(*t["pos_baytm"]), set()).add(t["slug"])
    for e in building_entries(doc):
        out.setdefault(e["tile"], set()).add(e["slug"])
    for d in deck_lines(doc):
        for tid, _ in landmarks.cut_polyline(d["pts"]):
            out.setdefault(tid, set()).add(d["slug"])
    for c in crane_rows(doc):
        out.setdefault(tile_id_of(*c["pos_baytm"]), set()).add("port_of_oakland_crane_" + c["id"])
    return {k: sorted(v) for k, v in sorted(out.items())}


def affected_tiles(doc: dict | None = None) -> list[str]:
    return sorted(plan(doc))


def _surface_min(ground, x0, z0, x1, z1) -> float:
    gx, gz = np.meshgrid(np.linspace(x0, x1, BASE_GRID), np.linspace(z0, z1, BASE_GRID))
    return float(np.min(ground(gx.ravel(), gz.ravel())))


def _props(slug, kind, idx, lid_tail, base, top, hs, conf, src_ids, tol_h, extra):
    p = {"lid": f"lm:{slug}:{lid_tail}", "bid": f"landmark:{slug}:{lid_tail}", "part_of": None, "kind": kind, "part_idx": idx,
         "base_navd88_m": round(base, 3), "top_navd88_m": round(top, 3), "height_m": round(top - base, 3), "min_height_m": 0.0,
         "roof_shape": "flat", "height_source": hs, "pos_source": "lidar_3dep", "source_ids": src_ids, "confidence": conf,
         "tol_height_m": tol_h, "tol_pos_m": POS_TOL_M}
    p.update(extra)
    return p


def build(tile_id: str, bfeats: list, ground, doc: dict | None = None):
    """-> (features, checked). `ground(x_array, z_array)` is the baked surface (seabed under water) of this tile in NAVD88 m."""
    doc = load(doc)
    feats, checked = [], []
    # 1. existing OSM structures: stack a part only when the sourced top is higher (T-022 rule)
    ent = building_entries(doc)
    bf, bc = landmarks.build(tile_id, bfeats, cfg={"sources": doc["sources"], "landmarks": ent})
    feats += bf
    srcmap = {e["slug"]: e["source_ids"] for e in ent}
    for c in bc:
        c["source_ids"] = srcmap[c["slug"]]
        c["class"] = "building"
    checked += bc
    # 2. bridge towers: prism over the measured footprint, from the baked surface up to the used top
    for t in tower_rows(doc):
        x, z = t["pos_baytm"]
        if tile_id_of(x, z) != tile_id:
            continue
        ex, ez = t["extent_m"]
        x0, x1, z0, z1 = x - ex / 2, x + ex / 2, z - ez / 2, z + ez / 2
        ring = [(x0, z0), (x1, z0), (x1, z1), (x0, z1)]
        base = _surface_min(ground, x0, z0, x1, z1)
        top = float(t["height_used_m"])
        sid = ["osm", "lidar_3dep"] + ([s for s in (t.get("official_src"),) if s])
        feats.append({"type": "Feature", "id": f"lm:{t['slug']}:t", "properties": _props(
            t["slug"], "bridge_tower", t["idx"], "t", base, top, t["height_source"], t["confidence"], sorted(set(sid)),
            round(height_tol(top), 3), {"measured_top_navd88_m": t["top_navd88_m"], "official_height_m": t.get("official_height_m"),
                                        "deck_ref_navd88_m": t["deck_ref_navd88_m"], "way": t["way"], "n_legs": t["n_legs"],
                                        "footprint_measured_m": [ex, ez], "base_source": "baked surface (min over footprint)"}),
            "geometry": {"type": "Polygon", "coordinates": [[list(map(float, q)) for q in orient(Polygon(ring), 1.0).exterior.coords]]}})
        checked.append({"slug": t["slug"], "class": "bridge_tower", "status": "emitted", "top_navd88_m": top, "base_navd88_m": round(base, 3),
                        "source_ids": ["lidar_3dep", "osm"], "pos_baytm": [x, z]})
    # 3. deck lines cut at tile edges
    for d in deck_lines(doc):
        n = 0
        seen = {}
        for tid, piece in landmarks.cut_polyline(d["pts"]):
            if tid != tile_id or len(piece) < 2:
                continue
            k = seen.get(tid, 0)
            seen[tid] = k + 1
            ys = [float(q[2]) for q in piece]
            tail = tile_id[3:] + (f"p{k}" if k else "")
            geom = {"type": "LineString", "coordinates": [[q[0], q[1]] for q in piece]}
            feats.append({"type": "Feature", "id": f"lm:{d['slug']}:{tail}", "properties": _props(
                d["slug"], "bridge_deck", 0, tail, min(ys), max(ys), "lidar_3dep", "B", ["osm", "lidar_3dep"], 3.0,
                {"width_m": d["width_m"], "width_source": "bake.json roads.class_width_m (Assumed)", "y_navd88_m": ys, "piece": k, "way": d["way"],
                 "bridge": d["bridge"]}), "geometry": geom})
            n += 1
        if n:
            checked.append({"slug": d["slug"], "class": "bridge_deck", "status": "emitted", "pieces_in_tile": n, "source_ids": ["lidar_3dep", "osm"]})
    # 4. port cranes: points (position only; cranes move, 50 m tolerance in M-005.1)
    ctol = doc["cranes"].get("pos_tol_m", 50)
    for c in crane_rows(doc):
        x, z = c["pos_baytm"]
        if tile_id_of(x, z) != tile_id:
            continue
        base = float(ground(np.array([x]), np.array([z]))[0])
        top = c.get("stowed_top_navd88_m")
        slug = "port_of_oakland_crane_" + c["id"]
        feats.append({"type": "Feature", "id": f"lm:{slug}:p", "properties": {
            "lid": f"lm:{slug}:p", "bid": f"landmark:{slug}:p", "part_of": None, "kind": "port_crane", "part_idx": 0,
            "base_navd88_m": round(base, 3), "top_navd88_m": top, "height_m": None if top is None else round(top - base, 3),
            "min_height_m": 0.0, "roof_shape": "flat", "height_source": "lidar_3dep" if top is not None else "none", "pos_source": "osm",
            "source_ids": ["osm"] + (["lidar_3dep"] if top is not None else []), "confidence": "B" if top is not None else "C",
            "tol_height_m": None, "tol_pos_m": ctol, "osm_id": c["id"], "crane_type": c.get("tags", {}).get("crane:type")},
            "geometry": {"type": "Point", "coordinates": [x, z]}})
        checked.append({"slug": slug, "class": "port_crane", "status": "emitted", "source_ids": ["osm"], "pos_baytm": [x, z]})
    feats.sort(key=lambda f: f["id"])
    checked.sort(key=lambda r: r["slug"])
    return feats, checked


def source_entries(checked: list, doc: dict | None = None) -> list[dict]:
    doc = load(doc)
    ids = sorted({s for c in checked for s in c.get("source_ids", [])})
    out = []
    for sid in ids:
        s = doc["sources"].get(sid)
        if s is None:
            continue
        e = {"product": f"landmarks_bay:{sid}", "item": s["item"], "url": s["url"], "licence": s["licence"], "attribution": s["attribution"],
             "fetched_utc": s["fetched_utc"], "source_sha256": sha256_bytes(canonical_json(s).encode())}
        if s["licence"] == "ODbL-1.0":
            e["derivative_database_candidate"] = True
        out.append(e)
    return out
