"""geo_tile@v1 bake for one tile index (R-004 §3; T-003 scope 1).

Deterministic and idempotent: outputs are pure functions of the cached sources, the config and
the pipeline code. A run whose input fingerprint matches the existing manifest reports
"up to date" and writes nothing. Parameterised only by the tile index (C16).
"""
from __future__ import annotations

import concurrent.futures as cf
import io
import json
import math
import time
import zipfile
from pathlib import Path

import numpy as np
import shapely
from shapely.geometry import box, mapping

from . import fetch, terrain, vector
from .common import (BAYTM_ID, CACHE, GLOBAL_ROOT, PIPE, SAMPLES, SPEC, TILES_ROOT, Tile,
                     baytm_compound_crs, canonical_json, git_head, load_config, sha256_bytes,
                     sha256_file, utc_now, write_if_changed)

CODE_FILES = ["kaiju_geo/common.py", "kaiju_geo/fetch.py", "kaiju_geo/terrain.py", "kaiju_geo/vector.py",
              "kaiju_geo/bake.py", "bake_tile.py"]
CONFIG_FILES = ["config/sources.json", "config/bake.json"]
REQUIRED_SOURCE_FIELDS = ("product", "item", "url", "licence", "attribution", "fetched_utc", "source_sha256")


def source_entry(kind, item, meta, extra=None):
    s = load_config("sources.json")[kind]
    e = {"product": s["product"], "item": item, "url": meta["url"], "licence": s["licence"],
         "attribution": s["attribution"], "fetched_utc": meta["fetched_utc"], "source_sha256": meta["sha256"]}
    for k in ("doi", "attribution_notice", "derivative_database_candidate"):
        if k in s:
            e[k] = s[k]
    for k in ("etag", "last_modified", "osm_base_utc", "window", "published"):
        if k in meta and meta[k]:
            e["upstream_" + k if k in ("etag", "last_modified") else k] = meta[k]
    e.update(extra or {})
    return e


# ---------------------------------------------------------------- rasters
def write_terrain_tif(path: Path, h_north_up: np.ndarray, tile: Tile) -> bytes:
    """COG float32 2049^2, pixel-is-point (pixel centres on the nodes), BayTM + NAVD88 WKT2."""
    import rasterio
    from rasterio.io import MemoryFile
    from rasterio.transform import Affine
    s = tile.spacing
    tr = Affine(s, 0, tile.x0 - s / 2, 0, -s, tile.z0 + tile.size + s / 2)
    prof = dict(driver="GTiff", width=SAMPLES, height=SAMPLES, count=1, dtype="float32",
                crs=rasterio.crs.CRS.from_wkt(baytm_compound_crs().to_wkt()), transform=tr,
                compress="deflate", predictor=3, tiled=True, blockxsize=512, blockysize=512, nodata=None)
    with MemoryFile() as mf:
        with mf.open(**prof) as dst:
            dst.write(h_north_up.astype(np.float32), 1)
            dst.update_tags(AREA_OR_POINT="Point", SPEC=SPEC, TILE=tile.id, VERTICAL="NAVD88 (EPSG:5703) metres")
        # COG layout: re-copy through the COG driver for overviews + ordering
        from rasterio.shutil import copy as rcopy
        with MemoryFile() as cog:
            rcopy(mf.name, cog.name, driver="COG", compress="DEFLATE", predictor="3", blocksize=512,
                  overview_resampling="average")
            data = cog.read()
    write_if_changed(path, data)
    return data


def png_bytes(mask_u8: np.ndarray) -> bytes:
    from PIL import Image
    buf = io.BytesIO()
    Image.fromarray(mask_u8, mode="L").save(buf, format="PNG", optimize=False, compress_level=9)
    return buf.getvalue()


# ---------------------------------------------------------------- water polygons
def read_water_polygons(tile: Tile, margin: float):
    """OSM split water polygons (EPSG:4326) intersecting the tile+margin, in BayTM."""
    import fiona
    zpath = CACHE / "osm" / "water-polygons-split-4326.zip"
    w, s, e, n = tile.bounds_lonlat(margin)
    with zipfile.ZipFile(zpath) as z:
        shp = [nm for nm in z.namelist() if nm.endswith(".shp")][0]
    geoms = []
    with fiona.open(f"zip://{zpath}!{shp}") as src:
        for f in src.filter(bbox=(w, s, e, n)):
            geoms.append(shapely.geometry.shape(f["geometry"]))
    if not geoms:
        return shapely.geometry.Polygon()
    u = shapely.union_all(geoms)
    u = vector._to_baytm_geom(u)
    x0, z0, x1, z1 = tile.bounds
    return shapely.make_valid(u).intersection(box(x0 - margin, z0 - margin, x1 + margin, z1 + margin))


def rasterize(geoms, tile: Tile, cells: int, margin_cells: int = 0, all_touched=False):
    """Rasterise onto the 1 m cell grid (cell (r,c) centre = x0+c+0.5, z1-r-0.5; north-up)."""
    from rasterio.features import rasterize as rr
    from rasterio.transform import Affine
    s = tile.spacing
    tr = Affine(s, 0, tile.x0 - margin_cells * s, 0, -s, tile.z0 + tile.size + margin_cells * s)
    n = cells + 2 * margin_cells
    shapes = [(g, 1) for g in geoms if g is not None and not g.is_empty]
    if not shapes:
        return np.zeros((n, n), dtype=bool)
    return rr(shapes, out_shape=(n, n), transform=tr, fill=0, dtype="uint8", all_touched=all_touched).astype(bool)


# ---------------------------------------------------------------- faults
def clip_faults(tile: Tile):
    g = json.loads((GLOBAL_ROOT / "faults_bay.geojson").read_text())
    x0, z0, x1, z1 = tile.bounds
    tb = box(x0, z0, x1, z1)
    feats = []
    nearest = []
    for f in g["features"]:
        geom = shapely.geometry.shape(f["geometry"])
        d = geom.distance(tb)
        nearest.append((d, f["properties"].get("fault_name") or "", f["properties"].get("section") or "", f["id"]))
        if d == 0:
            c = vector.round_geom(geom.intersection(tb))
            if not c.is_empty:
                feats.append({"type": "Feature", "id": f["id"], "properties": f["properties"], "geometry": mapping(c)})
    nearest.sort()
    by_name = {}
    for d, nm, sec, fid in nearest:
        key = (nm, sec)
        if key not in by_name:
            by_name[key] = (round(d, 1), fid)
    top = sorted(((v[0], k[0], k[1], v[1]) for k, v in by_name.items()))[:8]
    return feats, [{"distance_m": d, "fault_name": n, "section": s, "nearest_feature": fid} for d, n, s, fid in top]


# ---------------------------------------------------------------- the bake
def bake(tile: Tile, force=False, threads=1, out_root: Path = TILES_ROOT, shuffle_seed=None, layers_only=None):
    t_start = time.time()
    cfg = load_config("bake.json")
    out = out_root / tile.folder
    out.mkdir(parents=True, exist_ok=True)

    # ---- sources (cache only after first fetch; --offline proves it)
    cud = fetch.fetch_cudem(tile)
    dep = fetch.fetch_3dep(tile)
    osm = fetch.fetch_osm_tile(tile)
    osm_pbf = fetch.fetch_osm_pbf()
    wat = fetch.fetch_osm_water()
    dsf = fetch.fetch_datasf(tile)
    tide = fetch.fetch_tides()
    faults_global = GLOBAL_ROOT / "faults_bay.geojson"
    if not faults_global.exists():
        raise RuntimeError("global fault layer missing: run bake_faults.py first")

    # ---- fingerprint (inputs + code + config) for idempotence
    fp_parts = {"spec": SPEC, "tile": tile.id,
                "sources": sorted([m["sha256"] for m in cud + dep] + [osm["sha256"], wat["sha256"], dsf["sha256"], tide["sha256"]]),
                "faults_global": sha256_file(faults_global),
                "code": {f: sha256_file(PIPE / f) for f in CODE_FILES},
                "config": {f: sha256_file(PIPE / f) for f in CONFIG_FILES}}
    fingerprint = sha256_bytes(canonical_json(fp_parts).encode())
    man_path = out / "manifest.json"
    if man_path.exists() and not force:
        old = json.loads(man_path.read_text())
        if old.get("build", {}).get("inputs_sha256") == fingerprint and all(
                (out / L["file"]).exists() and sha256_file(out / L["file"]) == L["sha256"] for L in old["layers"]):
            print(f"[bake] {tile.id} up to date (inputs {fingerprint[:12]}); nothing written")
            return {"status": "up_to_date", "manifest": old}

    # ---- datum check
    dat = {d["name"]: d["value"] for d in json.loads((CACHE / "noaa" / "tides_9414290_datums.json").read_text())["datums"]}
    msl = round(dat["MSL"] - dat["NAVD88"], 3)
    if abs(msl - cfg["datum"]["msl_navd88_m"]) > 1e-6:
        raise RuntimeError(f"MSL from datums {msl} != config {cfg['datum']['msl_navd88_m']}")

    # ---- terrain on the extended node grid
    M = cfg["terrain"]["margin_nodes"]
    xs, zs = terrain.node_axes(tile, M)
    X, Z = np.meshgrid(xs, zs)  # [row = z ascending (south first), col = x]
    cud_paths = [PIPE / m["file"] for m in sorted(cud, key=lambda m: m["item"])]
    dep_paths = [PIPE / m["file"] for m in sorted(dep, key=lambda m: (m.get("published", ""), m["item"]), reverse=True)]
    hc = terrain.sample_rasters(cud_paths, X, Z)
    h3 = terrain.sample_rasters(dep_paths, X, Z)
    h, w3 = terrain.merge_surface(hc, h3, cfg["terrain"]["use_3dep_above_navd88_m"], cfg["terrain"]["feather_m"], tile.spacing)
    core = (slice(M, M + SAMPLES), slice(M, M + SAMPLES))
    if not np.all(np.isfinite(h[core])):
        raise RuntimeError(f"{tile.id}: terrain has {np.sum(~np.isfinite(h[core]))} unfilled nodes (source gap)")

    # ---- vectors
    o = vector.read_osm(PIPE / osm["file"] if "file" in osm else CACHE / "osm" / "tiles" / f"{tile.id}.osm.pbf")
    if shuffle_seed is not None:  # determinism test hook: input order must not matter
        rng = np.random.default_rng(shuffle_seed)
        for k in o:
            rng.shuffle(o[k])
    x0, z0, x1, z1 = tile.bounds

    # piers: solid decks (R-004 §1.5 "piers stay solid")
    piers = [g for g in (vector.clean_polygonal(p["geom"]) for p in o["piers"]) if g is not None]
    pier_union = shapely.union_all(piers) if piers else shapely.geometry.Polygon()
    land3 = np.isfinite(h3) & (h3 > cfg["datum"]["mhhw_navd88_m"])
    pier_log = []
    if piers:
        # node mask of piers on the extended grid
        node_mask = shapely.contains_xy(pier_union, X.ravel(), Z.ravel()).reshape(X.shape)
        r = cfg["water"]["pier_root_search_m"]
        for p in sorted(piers, key=lambda g: (round(g.bounds[0], 1), round(g.bounds[1], 1))):
            ring = p.buffer(r).difference(pier_union)
            sel = shapely.contains_xy(ring, X.ravel(), Z.ravel()).reshape(X.shape) & land3
            if np.count_nonzero(sel) < 20 and np.count_nonzero(shapely.contains_xy(p, X.ravel(), Z.ravel()).reshape(X.shape) & land3) < cfg["water"]["pier_min_deck_samples"]:
                pier_log.append({"bounds": [round(v, 1) for v in p.bounds], "deck_m": None, "reason": "no dry land samples near root"})
                continue
            inside = shapely.contains_xy(p, X.ravel(), Z.ravel()).reshape(X.shape)
            own = inside & land3
            if np.count_nonzero(own) >= cfg["water"]["pier_min_deck_samples"]:
                deck, how = float(np.median(h3[own])), "median of 3DEP deck samples on the pier"
            else:
                deck, how = float(np.percentile(h3[sel], cfg["water"]["pier_deck_percentile"])), "root-land percentile"
            raise_ = inside & (h < deck)
            h[raise_] = deck
            pier_log.append({"bounds": [round(v, 1) for v in p.bounds], "deck_m": round(deck, 3), "rule": how,
                             "nodes_raised": int(np.count_nonzero(raise_))})
    h = np.round(h, 4)  # 0.1 mm: removes platform-dependent last-bit noise before float32
    h32 = h.astype(np.float32)
    ht = h32[core]  # [z south->north, x west->east]

    # ---- buildings
    bcfg = cfg["buildings"]
    datasf = vector.read_datasf(CACHE / "datasf" / f"ynuv-fyni__{tile.id}.geojson")
    dtree = shapely.STRtree([d["geom"] for d in datasf]) if datasf else None
    outlines = []
    for r in o["buildings"]:
        g = vector.clean_polygonal(r["geom"])
        if g is not None:
            outlines.append(dict(r, geom=g))
    parts = []
    for r in o["parts"]:
        g = vector.clean_polygonal(r["geom"])
        if g is not None:
            parts.append(dict(r, geom=g))
    otree = shapely.STRtree([b["geom"] for b in outlines]) if outlines else None
    for p in parts:
        rp = p["geom"].point_on_surface()
        best = None
        if otree is not None:
            for k in otree.query(rp, predicate="within"):
                b = outlines[int(k)]
                if best is None or (b["geom"].area, vector.oid_key(b["oid"])) < (best["geom"].area, vector.oid_key(best["oid"])):
                    best = b
        p["part_of"] = best["oid"] if best else None
    parent_ids = {p["part_of"] for p in parts if p["part_of"]}
    parts_by_parent = {}
    for p in parts:
        if p["part_of"]:
            parts_by_parent.setdefault(p["part_of"], []).append(p["geom"])

    def remainder(b):
        """Outline area not covered by its building:part ways (rendered with the outline's own height).
        Slivers (< 4 m^2 or thinner than 0.6 m) are dropped."""
        rem = b["geom"].difference(shapely.union_all(parts_by_parent[b["oid"]]))
        keep = [q for q in getattr(rem, "geoms", [rem]) if q.geom_type == "Polygon" and q.area >= 4.0 and not q.buffer(-0.3).is_empty]
        if not keep:
            return None
        g = vector.clean_polygonal(shapely.geometry.MultiPolygon(keep) if len(keep) > 1 else keep[0])
        return mapping(g) if g is not None else None

    def owner_point(g):
        return g.point_on_surface()

    def base_of(g):
        bx, bz = vector.sample_points_in_polygon(g)
        # bilinear on the extended node grid
        fx = (bx - xs[0]) / tile.spacing
        fz = (bz - zs[0]) / tile.spacing
        from scipy.ndimage import map_coordinates
        v = map_coordinates(h, [fz, fx], order=1, mode="nearest", prefilter=False)
        return float(np.min(v))

    owned_outlines = [b for b in outlines if tile.contains(*owner_point(b["geom"]).coords[0])]
    owned_ids = {b["oid"] for b in owned_outlines}
    owned_parts = [p for p in parts if (p["part_of"] in owned_ids) or (p["part_of"] is None and tile.contains(*owner_point(p["geom"]).coords[0]))]

    def build_record(b, is_part, base_override=None):
        tags = b["tags"]
        hm, hsrc, extra = vector.resolve_height(tags, b["geom"], dtree, datasf, bcfg)
        mh = vector.resolve_min_height(tags, bcfg)
        lv = vector.parse_number(tags.get("building:levels"))
        base = base_override if base_override is not None else base_of(b["geom"])
        rs = tags.get("roof:shape")
        rh = vector.parse_length_m(tags.get("roof:height"))
        props = {"bid": b["oid"], "part_of": b.get("part_of") if is_part else None,
                 "height_m": round(hm, 3), "min_height_m": round(mh, 3), "levels": lv,
                 "roof_shape": rs, "base_navd88_m": round(base, 3), "height_source": hsrc,
                 "name": tags.get("name"), "has_parts": (not is_part) and b["oid"] in parent_ids,
                 "roof_height_m": round(rh, 3) if rh is not None else None}
        props.update(extra)
        if not is_part and b["oid"] in parent_ids:
            props["remainder_geometry"] = remainder(b)
        return {"type": "Feature", "id": b["oid"], "properties": props, "geometry": mapping(b["geom"])}

    def job(args):
        b, is_part, base = args
        return build_record(b, is_part, base)

    recs = [(b, False, None) for b in owned_outlines]
    feats_outline = list(cf.ThreadPoolExecutor(threads).map(job, recs)) if threads > 1 else [job(a) for a in recs]
    base_by_id = {f["id"]: f["properties"]["base_navd88_m"] for f in feats_outline}
    recs = [(p, True, base_by_id.get(p["part_of"])) for p in owned_parts]
    feats_parts = list(cf.ThreadPoolExecutor(threads).map(job, recs)) if threads > 1 else [job(a) for a in recs]
    bfeats = sorted(feats_outline + feats_parts, key=lambda f: vector.oid_key(f["id"]))

    # ---- water mask (cell-centred 2048^2, row 0 = north): OSM water AND surface < MSL, minus piers/buildings
    water_poly = read_water_polygons(tile, 64.0)
    cells = SAMPLES - 1
    wm = rasterize([water_poly], tile, cells)
    hn = np.flipud(ht.astype(np.float64))  # north-up nodes
    cell_h = 0.25 * (hn[:-1, :-1] + hn[1:, :-1] + hn[:-1, 1:] + hn[1:, 1:])
    solid = rasterize([pier_union] + [shapely.geometry.shape(f["geometry"]) for f in bfeats], tile, cells, all_touched=True)
    water = wm & (cell_h < msl) & ~solid
    water_u8 = np.where(water, 255, 0).astype(np.uint8)

    # ---- roads (clipped to the tile, global wid kept)
    rcfg = cfg["roads"]
    tb = box(x0, z0, x1, z1)
    rfeats = []
    for r in o["roads"]:
        g = r["geom"]
        if not g.intersects(tb):
            continue
        c = vector.round_geom(g.intersection(tb))
        if c.is_empty or c.length < 0.01:
            continue
        lines = [q for q in getattr(c, "geoms", [c]) if q.geom_type == "LineString" and q.length >= 0.01]
        if not lines:
            continue
        c = lines[0] if len(lines) == 1 else shapely.geometry.MultiLineString(lines)
        t = r["tags"]
        rfeats.append({"type": "Feature", "id": r["oid"], "properties": {
            "wid": r["oid"], "class": t.get("highway"), "lanes": vector.parse_number(t.get("lanes")),
            "width_m": round(vector.road_width(t, rcfg), 3), "bridge": vector.as_int_bool(t.get("bridge")),
            "tunnel": vector.as_int_bool(t.get("tunnel")), "layer": vector.layer_of(t), "name": t.get("name"),
            "drivable": t.get("highway") in rcfg["drivable"]}, "geometry": mapping(c)})
    rfeats.sort(key=lambda f: vector.oid_key(f["id"]))

    # ---- faults (clip of the global layer; empty by design for L0_30_36, R-004 §7)
    ffeats, nearest = clip_faults(tile)

    # ---- write outputs (bytes are deterministic: sorted keys, fixed float rounding)
    crs_member = {"type": "name", "properties": {"name": BAYTM_ID, "wkt2": "crs/baytm_v1.wkt"}}

    def fc(feats, extra=None):
        d = {"type": "FeatureCollection", "crs": crs_member, "features": feats}
        d.update(extra or {})
        return (canonical_json(d) + "\n").encode("utf-8")

    files = {}
    files["terrain.tif"] = write_terrain_tif(out / "terrain.tif", np.flipud(ht), tile)
    v16, hmin, hmax = terrain.quantise_r16(ht.astype(np.float64))
    files["terrain.r16"] = v16.tobytes(order="C")
    files["water.png"] = png_bytes(water_u8)
    files["buildings.geojson"] = fc(bfeats)
    files["roads.geojson"] = fc(rfeats)
    files["faults.geojson"] = fc(ffeats, {"empty_by_design": len(ffeats) == 0,
                                          "note": "no Quaternary fault crosses this tile (R-004 §7); see nearest_faults" if not ffeats else None,
                                          "nearest_faults": nearest})
    for name, data in files.items():
        if name != "terrain.tif":
            write_if_changed(out / name, data)

    # ---- manifest
    from .common import baytm_to_lonlat
    corners = [baytm_to_lonlat(x, z) for x, z in ((x0, z0), (x1, z0), (x1, z1), (x0, z1))]
    bb = [round(min(c[0] for c in corners), 6), round(min(c[1] for c in corners), 6),
          round(max(c[0] for c in corners), 6), round(max(c[1] for c in corners), 6)]
    osm_src = source_entry("osm", "norcal-latest.osm.pbf", osm_pbf, {"osm_base_utc": osm_pbf.get("osm_base_utc"),
                                                                     "extract_sha256": osm["sha256"]})
    ter_sources = [source_entry("cudem", m["item"], m) for m in sorted(cud, key=lambda m: m["item"])] + \
                  [source_entry("dep3_1m", m["item"], m) for m in sorted(dep, key=lambda m: m["item"])]
    hs = {}
    for f in bfeats:
        hs[f["properties"]["height_source"]] = hs.get(f["properties"]["height_source"], 0) + 1
    rendered = [f for f in bfeats if not f["properties"]["has_parts"]]
    layers = [
        {"layer": "terrain", "file": "terrain.tif", "sha256": sha256_bytes(files["terrain.tif"]),
         "format": "COG float32 2049x2049, pixel-is-point, row 0 = north, deflate + predictor 3",
         "samples": SAMPLES, "spacing_m": tile.spacing, "sources": ter_sources,
         "merge_rule": "3DEP 1 m where valid and CUDEM > +1.798 m NAVD88, CUDEM elsewhere, 10 m feather; piers raised to deck",
         "h_min_m": round(float(np.min(ht)), 4), "h_max_m": round(float(np.max(ht)), 4)},
        {"layer": "terrain_r16", "file": "terrain.r16", "sha256": sha256_bytes(files["terrain.r16"]),
         "format": "uint16 LE RAW 2049x2049, row 0 = south, col 0 = west, h = h_min + v*(h_max-h_min)/65535",
         "h_min_m": hmin, "h_max_m": hmax, "step_m": (hmax - hmin) / 65535.0, "sources": ter_sources},
        {"layer": "water", "file": "water.png", "sha256": sha256_bytes(files["water.png"]),
         "format": "PNG 8-bit 2048x2048 cell-centred, row 0 = north, 255 = water surface at MSL, 0 = land/pier",
         "water_fraction": round(float(water.mean()), 5), "water_level_navd88_m": msl,
         "sources": [source_entry("osm_water", "water-polygons-split-4326.zip", wat),
                     source_entry("tides", "9414290 datums", tide)] + ter_sources, "piers": pier_log},
        {"layer": "buildings", "file": "buildings.geojson", "sha256": sha256_bytes(files["buildings.geojson"]),
         "count": len(bfeats), "count_structures": len(owned_outlines), "count_parts": len(owned_parts),
         "count_rendered": len(rendered), "height_source_counts": dict(sorted(hs.items())),
         "owner_rule": "a structure is owned by the tile containing its shapely point_on_surface; its parts follow it; geometry is not clipped",
         "roof_rule": "max(y) = base_navd88_m + height_m for every roof_shape; M0 renders flat roofs except pyramidal/spire/cone with roof:height (apex at height_m)",
         "remainder_rule": "a structure with building:part ways renders through its parts plus remainder_geometry (outline minus parts, slivers < 4 m^2 or < 0.6 m wide dropped) extruded with the outline's own height_m",
         "extra_fields": ["has_parts", "roof_height_m", "remainder_geometry", "datasf_id", "datasf_iou"],
         "sources": [osm_src, source_entry("datasf", "ynuv-fyni", dsf)]},
        {"layer": "roads", "file": "roads.geojson", "sha256": sha256_bytes(files["roads.geojson"]), "count": len(rfeats),
         "clip_rule": "clipped to the tile square; wid is the global OSM way id", "sources": [osm_src]},
        {"layer": "faults", "file": "faults.geojson", "sha256": sha256_bytes(files["faults.geojson"]), "count": len(ffeats),
         "empty_by_design": len(ffeats) == 0, "nearest_faults": nearest[:4],
         "global_file": "global/v1/faults_bay.geojson", "global_sha256": sha256_file(faults_global),
         "sources": json.loads((GLOBAL_ROOT / "faults_bay.manifest.json").read_text())["sources"]},
    ]
    manifest = {
        "spec": SPEC, "tile": tile.id, "level": tile.level, "i": tile.i, "j": tile.j,
        "crs": f"{BAYTM_ID} (WKT2 in crs/baytm_v1.wkt)", "vertical": "NAVD88 (EPSG:5703), GEOID18",
        "origin_baytm_m": [tile.x0, tile.z0], "tile_size_m": tile.size,
        "bounds_baytm": [x0, z0, x1, z1], "bounds_wgs84": bb,
        "unity_axes": "x = BayTM easting, y = NAVD88 height, z = BayTM northing; 1 unit = 1 m; tile-local = world - origin",
        "water_level_navd88_m": msl,
        "build": dict({"script": "Pipelines/geo/bake_tile.py", "built_utc": utc_now(), "inputs_sha256": fingerprint,
                       "bake_seconds": None}, **git_head()),
        "layers": layers,
    }
    manifest["build"]["bake_seconds"] = round(time.time() - t_start, 2)
    write_if_changed(man_path, (canonical_json(manifest, indent=1) + "\n").encode("utf-8"))
    print(f"[bake] {tile.id}: {len(bfeats)} buildings ({len(owned_outlines)} structures, {len(owned_parts)} parts), "
          f"{len(rfeats)} roads, {len(ffeats)} fault pieces, water {water.mean() * 100:.1f} %, "
          f"h {np.min(ht):.2f}..{np.max(ht):.2f} m, {manifest['build']['bake_seconds']} s")
    return {"status": "baked", "manifest": manifest}
