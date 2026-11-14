"""M-005@v1.1 (research/R-006.md §5): the changed sub-measures, reported beside v1.

.4(d) default-height share by footprint area over structures (R-006 §2), .5 with the §2 exclusions, .7 with the
R-006 §3 reference recipe (vegetation by return count, 3 m crane opening, change mask, V4 257.6 deg / 75 deg, skyline
column band, +-1 px registration, noise floor F_v from R-ind2 = DataSF 2010 extrusions) and .9 shoreline continuity
(excess jump of the built surface over both sources). Unchanged sub-measures keep their v1 results.
"""
from __future__ import annotations

import json
import math

import numpy as np
import shapely
from shapely.geometry import shape

from . import skyline, terrain, vector
from .common import CACHE, GLOBAL_ROOT, PROJECT, Tile

VERSION = "M-005@v1.1"
SURVEY_DATE = "2023-04-20"


def row(d):
    d["metric_version"] = VERSION
    return d


# ---------------------------------------------------------------- shared helpers
def osm_tags(tile):
    o = vector.read_osm(CACHE / "osm" / "tiles" / f"{tile.id}.osm.pbf")
    return {r["oid"]: r["tags"] for r in o["buildings"] + o["parts"]}


def structures(feats):
    """structure id -> list of element features (outline + parts); unmatched parts are their own structure."""
    s = {}
    for f in feats:
        sid = f["properties"]["part_of"] or f["id"]
        s.setdefault(sid, []).append(f)
    return s


def v11_exclusion(sid, elems, tags, water_poly, msl):
    """R-006 §2 exclusions (as built and as counted)."""
    root = next((e for e in elems if e["id"] == sid), elems[0])
    p, t = root["properties"], tags.get(sid, {})
    if p.get("excluded"):
        return p["excluded"]
    if t.get("building") == "roof" or t.get("building:part") == "roof":
        return "roof"
    g = shape(root["geometry"])
    if p["base_navd88_m"] < msl and g.intersection(water_poly).area > 0.5 * g.area:
        return "base_below_msl_in_water"
    if g.area < 20:
        return "lt_20m2"
    return None


# ---------------------------------------------------------------- .4(d) default share
def default_share(tile, feats, E, water_poly, msl, cfg, tags):
    eb = {b["bid"]: b for b in E["buildings"]}
    st = structures(feats)
    rows, excl = [], {}
    for sid, elems in st.items():
        why = v11_exclusion(sid, elems, tags, water_poly, msl)
        if why:
            excl[why] = excl.get(why, 0) + 1
            continue
        rendered = [e for e in elems if eb.get(e["id"], {}).get("height_as_built_m") is not None]
        if not rendered:
            continue
        tallest = max(rendered, key=lambda e: eb[e["id"]]["top_navd88_m"])
        root = next((e for e in elems if e["id"] == sid), elems[0])
        g = shape(root["geometry"])
        rows.append({"sid": sid, "area": g.area, "perim": g.length, "src": tallest["properties"]["height_source"],
                     "tallest": tallest["id"], "elems": elems})
    A = sum(r["area"] for r in rows); P = sum(r["perim"] for r in rows)

    def shares(is_def):
        d = [r for r in rows if is_def(r)]
        return {"structures": len(rows), "default_structures": len(d), "area_share": round(sum(r["area"] for r in d) / A, 4),
                "count_share": round(len(d) / len(rows), 4), "perimeter_share": round(sum(r["perim"] for r in d) / P, 4)}

    as_built = shares(lambda r: r["src"] == "default")
    # projection of the R-006 chain on the source data (not applied in the build): parts inherit their structure's
    # known height; DataSF accepted by IoU > 0.6 OR >= 0.8 containment (relief > 3 m)
    datasf = vector.read_datasf(CACHE / "datasf" / f"ynuv-fyni__{tile.id}.geojson")
    tree = shapely.STRtree([d["geom"] for d in datasf])
    byid = {f["id"]: f for f in feats}

    def r006_default(r):
        e = byid[r["tallest"]]
        p = e["properties"]
        if p["height_source"] != "default":
            return False
        par = byid.get(p["part_of"]) if p["part_of"] else None
        if par is not None and par["properties"]["height_source"] in ("osm_height", "datasf"):
            return False                                    # inherits H_parent (source "parent")
        g = shape(e["geometry"])
        for k in tree.query(g):
            d = datasf[int(k)]
            if d["hgt_median_m"] and d["peak_1st_m"] is not None and d["gnd_min_m"] is not None and d["peak_1st_m"] - d["gnd_min_m"] > 3:
                inter = g.intersection(d["geom"]).area
                if inter / (g.area + d["geom"].area - inter) > 0.6 or inter >= 0.8 * g.area:
                    return False                            # datasf / datasf_contain
        return True

    proj = shares(r006_default)
    # lidar fill (R-006 §2 table, cached 3DEP B23 products): a default structure is filled when its footprint eroded
    # 1 m has >= 20 lidar cells with a clean roof (p90 of DSM - DTM > 2 m)
    lid = CACHE / "lidar" / f"rind_v11__{tile.id}.npz"
    lidar = None
    if lid.exists():
        z = np.load(lid)
        dsm = z["p95_05"]
        n = dsm.shape[0]
        from scipy.ndimage import map_coordinates
        xs = tile.x0 + (np.arange(n) + 0.5) * 0.5
        X, Z = np.meshgrid(xs, tile.z0 + (np.arange(n) + 0.5) * 0.5)
        hs = _terrain_nodes(tile)
        dtm = map_coordinates(hs, [(Z - tile.z0).ravel(), (X - tile.x0).ravel()], order=1, mode="nearest").reshape(X.shape)
        hag = dsm - dtm

        def lidar_filled(r):
            if not r006_default(r):
                return False
            g = shape(byid[r["sid"]]["geometry"]).buffer(-1.0)
            if g.is_empty:
                return False
            minx, miny, maxx, maxy = g.bounds
            c0, c1 = max(int((minx - tile.x0) / 0.5), 0), min(int((maxx - tile.x0) / 0.5) + 1, n)
            r0, r1 = max(int((miny - tile.z0) / 0.5), 0), min(int((maxy - tile.z0) / 0.5) + 1, n)
            if c1 <= c0 or r1 <= r0:
                return False
            sub = hag[r0:r1, c0:c1]
            inside = shapely.contains_xy(g, X[r0:r1, c0:c1], Z[r0:r1, c0:c1]) & np.isfinite(sub)
            return inside.sum() >= 20 and np.percentile(sub[inside], 90) > 2.0

        lidar = shares(lambda r: r006_default(r) and not lidar_filled(r))
    ok = as_built["area_share"] <= 0.10
    return row({"value": {"as_built": as_built, "excluded_structures": excl},
                "threshold": "M0: default area share <= 10 % (Assumed, pending calibration); M1+: <= 2 % (Assumed)",
                "result": "pass" if ok else "fail",
                "unit": "structure = outline + its parts; default if its tallest rendered element's height_source is default",
                "projection_r006_chain_not_applied_in_build": proj,
                "projection_r006_chain_plus_lidar_fill_not_applied": lidar if lidar else "lidar products not cached",
                "assessor_storeys_fill": "not applied: DataSF wv5m-vpq2 is not cached (no new downloads in this cycle)"})


_HS = {}


def _terrain_nodes(tile):
    if tile.id not in _HS:
        import rasterio
        from .common import TILES_ROOT
        with rasterio.open(TILES_ROOT / tile.folder / "terrain.tif") as d:
            _HS[tile.id] = np.flipud(d.read(1).astype(np.float64))
    return _HS[tile.id]


# ---------------------------------------------------------------- .9 shoreline continuity
def shoreline_continuity(tile, cfg, cp, dp, piers):
    from scipy.ndimage import binary_dilation
    M = cfg["terrain"]["margin_nodes"]
    gx, gz = terrain.node_axes(tile, M)
    X, Z = np.meshgrid(gx, gz)
    hc = terrain.sample_rasters(cp, X, Z)
    h3 = terrain.sample_rasters(dp, X, Z)
    _, w = terrain.merge_surface(hc, h3, cfg["terrain"]["use_3dep_above_navd88_m"], cfg["terrain"]["feather_m"], tile.spacing)
    core = (slice(M, M + 2049), slice(M, M + 2049))
    w, hc, h3 = w[core], hc[core], h3[core]
    hb = _terrain_nodes(tile)                                   # the built surface (geo_tile terrain, physics = r16 of it)
    band = (w > 0) & (w < 1)
    dom = binary_dilation(band, iterations=5)
    pier_nodes = shapely.contains_xy(piers.buffer(2.0), (tile.x0 + np.arange(2049))[None, :].repeat(2049, 0),
                                     (tile.z0 + np.arange(2049))[:, None].repeat(2049, 1))
    dom &= ~pier_nodes
    E = []
    for ax in (0, 1):
        db = np.abs(np.diff(hb, axis=ax))
        ds = np.fmax(np.abs(np.diff(h3, axis=ax)), np.abs(np.diff(hc, axis=ax)))
        pair = (dom[:-1, :] & dom[1:, :]) if ax == 0 else (dom[:, :-1] & dom[:, 1:])
        e = (db - ds)[pair & np.isfinite(ds)]
        E.append(e)
    E = np.concatenate(E)
    d = np.abs(h3 - hc)[band & np.isfinite(h3) & np.isfinite(hc)]
    delta_over_L = d / cfg["terrain"]["feather_m"]
    p99, mx = float(np.percentile(E, 99)), float(E.max())
    return row({"value": {"pairs": int(E.size), "p99_excess_m": round(p99, 4), "max_excess_m": round(mx, 4), "median_excess_m": round(float(np.median(E)), 4)},
                "threshold": "p99 E <= 0.20 m and max E <= 0.50 m (piers excluded as documented structures)",
                "result": "pass" if p99 <= 0.20 and mx <= 0.50 else "fail",
                "domain": "adjacent 1 m node pairs (both axes) inside the 3DEP/CUDEM blend band (0 < w < 1) dilated 5 m, minus pier polygons + 2 m",
                "informational": {"D_median_abs_3dep_minus_cudem_m": round(float(np.median(d)), 3), "D_p95_m": round(float(np.percentile(d, 95)), 3),
                                  "envelope_95pct_m": 0.98, "share_band_nodes_delta_over_L_gt_0.2": round(float(np.mean(delta_over_L > 0.2)), 4)}})


# ---------------------------------------------------------------- .7 skyline (R-006 §3)
V11_VIEWS = [  # name, viewpoint, eye (m NAVD88), heading, hFOV, gating
    ("V4", "V4", 10.0, 257.6, 75.0, True),
    ("V4m", "V4", 70.0, 257.6, 75.0, False),
    ("V5", "V5", 146.0, 170.0, 60.0, True),
]
OBSERVER = {"V5": "osm:w28824850"}   # the observer stands on Coit Tower


def lidar_surfaces(tile, feats, water, msl, landmarks, tags, recipe="p95", res=0.5):
    """R-ind (R-006 §3 recipe) on a BayTM grid; returns (surface, dtm, change-mask list)."""
    z = np.load(CACHE / "lidar" / f"rind_v11__{tile.id}.npz")
    tag = "05" if res == 0.5 else "10"
    dsm = z[("p95_" if recipe == "p95" else "max_") + tag].astype(np.float64)
    multi = z["multi_" + tag]
    n = dsm.shape[0]
    hs = _terrain_nodes(tile)
    from scipy.ndimage import map_coordinates, grey_opening
    c = (np.arange(n) + 0.5) * res
    CX, CZ = np.meshgrid(c, c)
    dtm = map_coordinates(hs, [CZ.ravel(), CX.ravel()], order=1, mode="nearest").reshape(n, n)
    wet = np.kron(water, np.ones((int(1 / res), int(1 / res)), bool)) if res < 1 else water
    if wet.shape[0] != n:
        wet = np.pad(wet, ((0, n - wet.shape[0]), (0, n - wet.shape[1])))
    dtm = np.where(wet, np.maximum(dtm, msl), dtm)
    hag = np.where(np.isfinite(dsm), dsm - dtm, 0.0)
    veg = np.isfinite(multi) & (multi > 0.5)                 # canopy: most first returns are followed by more returns
    hag[veg] = 0.0
    hag = np.maximum(hag, 0.0)
    k = max(int(round(3.0 / res)), 1)
    opened = grey_opening(hag, size=(k, k))
    exempt = np.zeros((n, n), bool)
    ex = [shape(f["geometry"]).buffer(5.0) for f in feats if f["id"] in landmarks]
    if ex:
        exempt = shapely.contains_xy(shapely.union_all(ex), (tile.x0 + CX).ravel(), (tile.z0 + CZ).ravel()).reshape(n, n)
    tall = (hag > 20) & ~exempt
    changed = tall & (opened < hag - 0.5)
    hag = np.where(tall, opened, hag)
    surf = dtm + hag
    stats = {"vegetation_cells_share": round(float(veg.mean()), 4), "opening_cells_lowered_gt_0.5m": int(changed.sum()),
             "cells_without_returns_set_to_dtm": int((~np.isfinite(dsm)).sum())}
    return surf, dtm, stats, (CX, CZ)


def change_mask(tile, feats, tags, surf, dtm, res=0.5):
    """Structures removed from both renders (R-006 §3 step 5): |lidar p90 - build p90| > 10 m on >= 300 m^2,
    building=construction, or start_date after the survey."""
    st = structures(feats)
    n = surf.shape[0]
    out = []
    from rasterio.features import rasterize
    from rasterio.transform import Affine
    tr = Affine(res, 0, tile.x0, 0, res, tile.z0)            # south-up
    order = sorted([f for f in feats if not f["properties"]["has_parts"] or f["properties"].get("remainder_geometry")],
                   key=lambda f: f["properties"]["base_navd88_m"] + f["properties"]["height_m"])
    shapes_ = []
    for f in order:
        p = f["properties"]
        if p.get("excluded"):
            continue
        g = shape(p["remainder_geometry"]) if p["has_parts"] else shape(f["geometry"])
        shapes_.append((g, p["base_navd88_m"] + p["height_m"]))
    build = rasterize(shapes_, out_shape=(n, n), transform=tr, fill=np.nan, dtype="float64") if shapes_ else np.full((n, n), np.nan)
    build = np.where(np.isfinite(build), build, dtm)
    for sid, elems in st.items():
        root = next((e for e in elems if e["id"] == sid), elems[0])
        t = tags.get(sid, {})
        g = shape(root["geometry"])
        why = None
        if t.get("building") == "construction":
            why = "building=construction"
        sd = (t.get("start_date") or "")[:10]
        if sd and sd > SURVEY_DATE:
            why = f"start_date {sd} after survey"
        if why is None and g.area >= 300:
            ge = g.buffer(-1.0)
            if not ge.is_empty:
                minx, miny, maxx, maxy = ge.bounds
                c0, c1 = max(int((minx - tile.x0) / res), 0), min(int((maxx - tile.x0) / res) + 1, n)
                r0, r1 = max(int((miny - tile.z0) / res), 0), min(int((maxy - tile.z0) / res) + 1, n)
                if c1 > c0 and r1 > r0:
                    xs = tile.x0 + (np.arange(c0, c1) + 0.5) * res
                    zs = tile.z0 + (np.arange(r0, r1) + 0.5) * res
                    XX, ZZ = np.meshgrid(xs, zs)
                    inside = shapely.contains_xy(ge, XX, ZZ)
                    if inside.sum() >= 20:
                        lp = np.percentile((surf - dtm)[r0:r1, c0:c1][inside], 90)
                        bp = np.percentile((build - dtm)[r0:r1, c0:c1][inside], 90)
                        if abs(lp - bp) > 10:
                            why = f"|lidar p90 {lp:.1f} - build p90 {bp:.1f}| > 10 m"
        if why:
            out.append({"structure": sid, "name": root["properties"].get("name"), "area_m2": round(g.area), "reason": why})
    return out


def datasf_surface(tile, dtm, res, masked_geoms):
    """R-ind2: DataSF 2010 footprints extruded to hgt_median_m above their ring-p10 ground, on the same DTM."""
    from rasterio.features import rasterize
    from rasterio.transform import Affine
    n = dtm.shape[0]
    ds = vector.read_datasf(CACHE / "datasf" / f"ynuv-fyni__{tile.id}.geojson")
    hs = _terrain_nodes(tile)
    from scipy.ndimage import map_coordinates
    shapes_ = []
    for d in sorted(ds, key=lambda d: d["hgt_median_m"] or 0):
        if not d["hgt_median_m"] or d["hgt_median_m"] <= 0:
            continue
        rx, rz = vector.ring_points(d["geom"], 1.0)
        base = float(np.percentile(map_coordinates(hs, [rz - tile.z0, rx - tile.x0], order=1, mode="nearest"), 10))
        shapes_.append((d["geom"], base + d["hgt_median_m"]))
    tr = Affine(res, 0, tile.x0, 0, res, tile.z0)
    top = rasterize(shapes_, out_shape=(n, n), transform=tr, fill=np.nan, dtype="float64")
    surf = np.where(np.isfinite(top), np.maximum(top, dtm), dtm)
    if masked_geoms:
        m = rasterize([(g, 1) for g in masked_geoms], out_shape=(n, n), transform=tr, fill=0, dtype="uint8").astype(bool)
        surf = np.where(m, dtm, surf)
    return surf


def grid_sampler(tile, arr, res):
    n = arr.shape[0]

    def s(x, z):
        i = np.floor((z - tile.z0) / res).astype(np.int64)
        j = np.floor((x - tile.x0) / res).astype(np.int64)
        ok = (i >= 0) & (i < n) & (j >= 0) & (j < n)
        v = np.full(x.shape, np.nan)
        v[ok] = arr[i[ok], j[ok]]
        return v
    return s


def compare_v11(game_rows, ref_rows, cam):
    """R-006 band: skyline columns (either profile >= 2 px above the horizon), rows from max(top) + 2 px to the
    horizon; +-1 px column registration (best reported); MAE in px and mrad."""
    h, w = cam.h, cam.w
    hr = int(cam.cy)
    best = None
    for sh in (-1, 0, 1):
        g = np.roll(game_rows, sh)
        if sh == 1:
            g[0] = game_rows[0]
        if sh == -1:
            g[-1] = game_rows[-1]
        cols = (g <= hr - 2) | (ref_rows <= hr - 2)
        if cols.sum() == 0:
            continue
        top = int(max(0, min(g[cols].min(), ref_rows[cols].min()) - 2))
        rows = np.arange(top, hr)[:, None]
        G = rows >= np.clip(g[cols], 0, h)[None, :]
        R = rows >= np.clip(ref_rows[cols], 0, h)[None, :]
        iou = float(np.logical_and(G, R).sum() / max(np.logical_or(G, R).sum(), 1))
        mae = float(np.mean(np.abs(np.clip(g[cols], top, hr) - np.clip(ref_rows[cols], top, hr))))
        cand = {"iou": round(iou, 4), "mae_px": round(mae, 3), "mae_mrad": round(mae * 1000.0 / cam.f, 3), "shift_px": sh,
                "skyline_columns": int(cols.sum()), "skyline_column_share": round(float(cols.mean()), 4), "band_rows": [top, hr]}
        if best is None or (cand["iou"], -cand["mae_px"]) > (best["iou"], -best["mae_px"]):
            best = cand
    return best
