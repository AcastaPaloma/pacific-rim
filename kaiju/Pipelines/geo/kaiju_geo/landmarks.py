"""Sourced landmark prisms (T-022 step 1; R-020 §2). Pure function of the baked building features and
config/landmarks.json: each landmark is a part stacked on the existing OSM prism (base = baked top of the
structure at the landmark position), emitted only when the sourced top exceeds the baked top by more than
tol_height_m. Bridge items are T-023."""
from __future__ import annotations

import shapely
from shapely.geometry import Point, mapping, shape

from .common import load_config, sha256_bytes, canonical_json

FIELDS = ("lid", "bid", "part_of", "kind", "part_idx", "base_navd88_m", "top_navd88_m", "height_m", "min_height_m",
          "roof_shape", "height_source", "pos_source", "source_ids", "confidence", "tol_height_m", "tol_pos_m")


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
    feats.sort(key=lambda f: f["id"])
    checked.sort(key=lambda r: r["slug"])
    return feats, checked


def source_entries(feats: list, checked: list, cfg: dict | None = None):
    cfg = cfg or load_config("landmarks.json")
    ids = sorted({s for lm in cfg["landmarks"] if lm["slug"] in {c["slug"] for c in checked} for s in lm["source_ids"]})
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
