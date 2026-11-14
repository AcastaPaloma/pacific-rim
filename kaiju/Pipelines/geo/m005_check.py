#!/usr/bin/env python
"""M-005@v1 checker (R-004 §6) plus the T-003 tile checks, from the Unity as-built export.

  prepare: writes evidence/T-003/control_points.json (NGS marks, 10,000 land, 10,000 wet points,
           50 shoreline profiles) that the Unity export samples on the as-built surfaces.
  check:   reads the export(s), the committed geo_tile@v1 tile, the references and the source cache
           (fetched on demand) and writes evidence/T-003/m005_report.json: every sub-measure with value,
           threshold and pass / fail / not_applicable_in_tile (with reason). Nothing out of tile is passed.

Usage: /opt/miniconda3/envs/kaiju/bin/python -I Pipelines/geo/m005_check.py prepare|check [--tile L0_30_36]
"""
import argparse
import json
import math
import os
import sys
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np  # noqa: E402
import shapely  # noqa: E402
from shapely.geometry import box, shape  # noqa: E402

from kaiju_geo import fetch, manifest as manifest_mod, skyline, terrain, vector  # noqa: E402
from kaiju_geo.common import (CACHE, GLOBAL_ROOT, PIPE, PROJECT, TILES_ROOT, apply_std, baytm_to_lonlat,  # noqa: E402
                              lonlat_to_baytm, load_config, parse_tile, std_args)

EVID = PROJECT / "evidence" / "T-003"
SEED = 20261114
NA = "not_applicable_in_tile"
REPORT_PATH = EVID / "m005_report.json"


def J(p):
    return json.loads(Path(p).read_text())


def res(value, threshold, ok, **kw):
    d = {"value": value, "threshold": threshold, "result": "pass" if ok else "fail"}
    d.update(kw)
    return d


def na(reason, due="M1", **kw):
    d = {"result": NA, "reason": reason, "due": due}
    d.update(kw)
    return d


def not_run(reason, **kw):
    d = {"result": "not_run", "reason": reason}
    d.update(kw)
    return d


def r(x, n=4):
    return None if x is None or (isinstance(x, float) and not math.isfinite(x)) else round(float(x), n)


# ---------------------------------------------------------------- shared loaders
def tile_paths(tile):
    return TILES_ROOT / tile.folder


def load_tile(tile):
    d = tile_paths(tile)
    man = J(d / "manifest.json")
    import rasterio
    with rasterio.open(d / "terrain.tif") as src:
        tif = src.read(1).astype(np.float64)       # row 0 = north
    hs = np.flipud(tif)                              # row 0 = south: hs[k_z, k_x]
    from PIL import Image
    water = np.flipud(np.array(Image.open(d / "water.png")) >= 128)   # row 0 = south cells
    b = J(d / "buildings.geojson")["features"]
    return man, hs, water, b


def bilinear(hs, tile, x, z):
    from scipy.ndimage import map_coordinates
    fx = (np.asarray(x) - tile.x0) / tile.spacing
    fz = (np.asarray(z) - tile.z0) / tile.spacing
    return map_coordinates(hs, [fz, fx], order=1, mode="nearest", prefilter=False)


def source_rasters(tile):
    cud = fetch.fetch_cudem(tile)
    dep = fetch.fetch_3dep(tile)
    cp = [PIPE / m["file"] for m in sorted(cud, key=lambda m: m["item"])]
    dp = [PIPE / m["file"] for m in sorted(dep, key=lambda m: (m.get("published", ""), m["item"]), reverse=True)]
    return cp, dp


def piers_union(tile):
    o = vector.read_osm(CACHE / "osm" / "tiles" / f"{tile.id}.osm.pbf")
    ps = [g for g in (vector.clean_polygonal(p["geom"]) for p in o["piers"]) if g is not None]
    return (shapely.union_all(ps) if ps else shapely.geometry.Polygon()), o


# ---------------------------------------------------------------- prepare
def ngs_marks(tile):
    m = fetch.fetch_ngs(tile)
    marks = J(CACHE / "ngs" / f"ngs_radial__{tile.id}.json")
    bad_setting = ("TUNNEL", "STRUCTURE", "BUILDING", "BRIDGE", "ROOF", "WALL", "PIER")
    bad_cond = ("NOT FOUND", "DESTROYED", "POOR")
    keep, seen = [], 0
    for k in marks:
        try:
            lat, lon, H = float(k["lat"]), float(k["lon"]), float(k["orthoHt"])
        except (KeyError, ValueError, TypeError):
            continue
        x, z = lonlat_to_baytm(lon, lat)
        if not tile.contains(x, z):
            continue
        seen += 1
        ok = (k.get("vertSource", "").strip() == "ADJUSTED" and k.get("posSource", "").strip() in ("ADJUSTED", "HD_HELD1", "HD_HELD2")
              and not any(b in (k.get("setting") or "").upper() for b in bad_setting)
              and not any(b in (k.get("condition") or "").upper() for b in bad_cond))
        keep.append({"pid": k["pid"], "x": x, "z": z, "orthoHt": H, "qualifies": ok, "vertSource": k.get("vertSource"),
                     "posSource": k.get("posSource"), "setting": k.get("setting"), "condition": k.get("condition")})
    return keep, seen, m


def prepare(tile):
    cfg = load_config("bake.json")
    man, hs, water, feats = load_tile(tile)
    cp, dp = source_rasters(tile)
    piers, _ = piers_union(tile)
    rng = np.random.default_rng(SEED)
    x0, z0, x1, z1 = tile.bounds
    mhhw = cfg["datum"]["mhhw_navd88_m"]
    fp = shapely.union_all([shape(f["geometry"]) for f in feats]).buffer(2.0)
    gz, gx = np.gradient(hs, tile.spacing)
    from scipy.ndimage import maximum_filter
    slope = maximum_filter(np.degrees(np.arctan(np.hypot(gx, gz))), size=3)

    # merge weight of 3DEP on the node grid (with the bake's margin), so feather bands are excluded exactly
    M = cfg["terrain"]["margin_nodes"]
    gxs, gzs = terrain.node_axes(tile, M)
    GX, GZ = np.meshgrid(gxs, gzs)
    _, w3 = terrain.merge_surface(terrain.sample_rasters(cp, GX, GZ), terrain.sample_rasters(dp, GX, GZ),
                                  cfg["terrain"]["use_3dep_above_navd88_m"], cfg["terrain"]["feather_m"], tile.spacing)
    from scipy.ndimage import maximum_filter as _mx, minimum_filter as _mn
    w_min = _mn(w3, size=5)[M:M + 2049, M:M + 2049]   # all nodes within 2 m
    w_max = _mx(w3, size=5)[M:M + 2049, M:M + 2049]

    def interior(xs, zs, want_land):
        """Land: 3DEP weight exactly 1 on every node within 2 m (no feather band); wet: weight 0 (pure CUDEM)."""
        i = np.clip(np.rint(xs - x0).astype(int), 0, 2048)
        j = np.clip(np.rint(zs - z0).astype(int), 0, 2048)
        return (w_min[j, i] >= 1.0) if want_land else (w_max[j, i] <= 0.0)

    def draw(n, want_land):
        out = []
        while len(out) < n:
            xs = rng.uniform(x0 + 15, x1 - 15, 4 * n)
            zs = rng.uniform(z0 + 15, z1 - 15, 4 * n)
            ci = np.clip(((xs - x0) // 1).astype(int), 0, 2047)
            cj = np.clip(((zs - z0) // 1).astype(int), 0, 2047)
            wet = water[cj, ci]
            sel = (~wet if want_land else wet)
            sel &= ~shapely.contains_xy(piers, xs, zs)
            if want_land:
                sel &= ~shapely.contains_xy(fp, xs, zs)
                sel &= slope[np.rint((zs - z0)).astype(int), np.rint((xs - x0)).astype(int)] <= 45
            xs, zs = xs[sel], zs[sel]
            ok = interior(xs, zs, want_land)
            for a, b in zip(xs[ok], zs[ok]):
                out.append((float(a), float(b)))
        return out[:n]

    land = draw(10000, True)
    wet = draw(10000, False)
    la, lz = np.array(land).T
    wa, wz = np.array(wet).T
    land_src = terrain.sample_rasters(dp, la[None, :], lz[None, :])[0]
    wet_src = terrain.sample_rasters(cp, wa[None, :], wz[None, :])[0]

    # shoreline profiles: 50 crossings evenly along the OSM water boundary inside the tile
    from kaiju_geo.bake import read_water_polygons
    wp = read_water_polygons(tile, 64.0)
    tb = box(x0 + 45, z0 + 45, x1 - 45, z1 - 45)
    coast = wp.boundary.intersection(tb)
    L = coast.length
    shore, shore_meta = [], []
    for k in range(50):
        p = coast.interpolate((k + 0.5) * L / 50)
        a = coast.interpolate(max((k + 0.5) * L / 50 - 2, 0))
        b = coast.interpolate(min((k + 0.5) * L / 50 + 2, L))
        tx, tz = b.x - a.x, b.y - a.y
        n = math.hypot(tx, tz) or 1
        nx, nz = -tz / n, tx / n
        if not wp.contains(shapely.Point(p.x + 3 * nx, p.y + 3 * nz)):
            nx, nz = -nx, -nz                 # normal points to the water
        in_pier = bool(piers.buffer(5).contains(p))
        shore_meta.append({"x": p.x, "z": p.y, "nx": nx, "nz": nz, "near_pier": in_pier})
        for s in range(-40, 41):
            shore.append((p.x + s * nx, p.y + s * nz))

    ngs, ngs_seen, _ = ngs_marks(tile)
    pts = [(m["x"], m["z"]) for m in ngs] + land + wet + shore
    groups = {"ngs": [0, len(ngs)], "land": [len(ngs), len(land)], "wet": [len(ngs) + len(land), len(wet)],
              "shore": [len(ngs) + len(land) + len(wet), len(shore)]}
    doc = {"spec": "geo_control_points@v1", "tile": tile.id, "seed": SEED, "groups": groups,
           "points": [[round(a, 4), round(b, 4)] for a, b in pts], "ngs": ngs, "ngs_in_tile": ngs_seen,
           "land_source_navd88_m": [r(v) for v in land_src], "wet_source_navd88_m": [r(v) for v in wet_src],
           "shore_profiles": shore_meta, "shore_offsets_m": list(range(-40, 41)),
           "rules": {"land": "uniform random, dry, outside piers, > 2 m from footprints, slope <= 45 deg, all probes within 12 m in the 3DEP region (no feather band); source = 3DEP 1 m bilinear",
                     "wet": "uniform random water cells outside piers, all probes in the CUDEM region; source = CUDEM bilinear",
                     "shore": "50 points evenly along the OSM water-polygon boundary inside the tile (45 m inset), profile along the normal from -40 m (land) to +40 m (water), 1 m step"}}
    EVID.mkdir(parents=True, exist_ok=True)
    (EVID / "control_points.json").write_text(json.dumps(doc) + "\n")
    print(f"[prepare] {len(pts)} control points: ngs {len(ngs)} ({sum(m['qualifies'] for m in ngs)} qualify), land {len(land)}, wet {len(wet)}, shore {len(shore)}")


# ---------------------------------------------------------------- check
def centroid(geom):
    c = geom.centroid
    return c.x, c.y


def check(tile, export_path):
    rep = {"spec": "M-005@v1", "tile": tile.id, "source": "research/R-004.md §6", "m005": {}, "t003": {}}
    E = J(export_path)
    man, hs, water, feats = load_tile(tile)
    cps = J(EVID / "control_points.json")
    ref = J(GLOBAL_ROOT / "landmarks.json")
    cfg = load_config("bake.json")
    eb = {b["bid"]: b for b in E["buildings"]}
    sb = {f["id"]: f for f in feats}
    samples = np.array([[np.nan if v is None else v for v in s] for s in E["terrain_samples"]], dtype=np.float64)
    G = cps["groups"]

    def grp(name):
        a, n = G[name]
        return samples[a:a + n]

    def top_of(bid):
        """As-built top of a building or structure (max over its parts)."""
        b = eb[bid]
        tops = [b["top_navd88_m"]] if b["top_navd88_m"] is not None else []
        tops += [p["top_navd88_m"] for p in E["buildings"] if p["part_of"] == bid and p["top_navd88_m"] is not None]
        return max(tops) if tops else None

    # ---------- M-005.1 landmarks
    in_tile = [l for l in ref["landmarks"] if l["tile"] == tile.id]
    out_tile = [l for l in ref["landmarks"] if l["tile"] != tile.id]
    rows, da, db = [], [], []
    for l in in_tile:
        bid = l["osm"]
        if bid not in eb:
            rows.append({"n": l["n"], "name": l["name"], "present": False})
            da.append(float("inf")); db.append(float("inf"))
            continue
        ax, az = eb[bid]["anchor_baytm"]
        sx, sz = centroid(shape(sb[bid]["geometry"]))
        d_src = math.hypot(ax - sx, az - sz)
        d_ref = math.hypot(ax - l["baytm_x"], az - l["baytm_z"])
        base = eb[bid]["base_navd88_m"]
        top = top_of(bid)
        h = None if top is None else top - base
        rows.append({"n": l["n"], "name": l["name"], "bid": bid, "anchor_baytm": [ax, az], "d_vs_source_m": r(d_src), "d_vs_reference_m": r(d_ref),
                     "base_navd88_m": r(base, 3), "top_navd88_m": r(top, 3), "height_above_base_m": r(h, 3), "height_ref_m": l["height_ref_m"],
                     "gate_height": l["gate_height"], "height_basis": l["height_basis"]})
        da.append(d_src); db.append(d_ref)
    da, db = np.array(da), np.array(db)
    m1 = {"landmarks": rows,
          "a_vs_source": res(r(da.max()), "<= 0.5 m all", bool(np.all(da <= 0.5))),
          "b_vs_reference": res({"p95_m": r(np.percentile(db, 95)), "max_m": r(db.max())}, "p95 <= 5 m, max <= 10 m",
                                bool(np.percentile(db, 95) <= 5 and db.max() <= 10),
                                note="reference = R-004 §5 OSM centre (Overpass out center) positions; the anchor is the footprint area centroid"),
          "c_bridge_spans": na("Golden Gate towers (#1, #2) are outside L0_30_36"),
          "out_of_tile_landmarks": na("outside L0_30_36: " + ", ".join(f"#{l['n']} {l['name']}" for l in out_tile))}
    hrows = []
    for row in rows:
        if row.get("height_ref_m") is None or row.get("height_above_base_m") is None:
            continue
        tol = max(2.0, 0.01 * row["height_ref_m"])
        dh = row["height_above_base_m"] - row["height_ref_m"]
        hrows.append({"name": row["name"], "as_built_m": row["height_above_base_m"], "ref_m": row["height_ref_m"], "delta_m": r(dh, 3),
                      "tol_m": tol, "gated": row["gate_height"], "result": ("pass" if abs(dh) <= tol else "fail") if row["gate_height"] else "reported"})
    gated = [h for h in hrows if h["gated"]]
    tallest = max(E["buildings"], key=lambda b: -1e9 if b["top_navd88_m"] is None else b["top_navd88_m"])
    tall_bid = tallest["part_of"] or tallest["bid"]
    m1["d_heights"] = res(hrows, "within max(2 m, 1 %) above base (gated rows: Salesforce, Transamerica, Millennium, Coit)",
                          all(h["result"] == "pass" for h in gated) and len(gated) == 4,
                          salesforce_tallest=tall_bid == "osm:w431972186", tallest_bid=tall_bid, tallest_top_navd88_m=r(tallest["top_navd88_m"], 3))
    if tall_bid != "osm:w431972186":
        m1["d_heights"]["result"] = "fail"
    m1["result"] = "pass" if all(m1[k]["result"] == "pass" for k in ("a_vs_source", "b_vs_reference", "d_heights")) else "fail"
    rep["m005"]["M-005.1"] = m1

    # ---------- M-005.2 terrain at control points
    ngs = cps["ngs"]
    ng = grp("ngs")
    q = [i for i, m in enumerate(ngs) if m["qualifies"]]
    dq = np.array([ng[i, 2] - ngs[i]["orthoHt"] for i in q]) if q else np.array([])
    all_d = [{"pid": m["pid"], "qualifies": m["qualifies"], "orthoHt": m["orthoHt"], "game_m": r(ng[i, 2]), "delta_m": r(ng[i, 2] - m["orthoHt"]),
              "vertSource": m["vertSource"], "posSource": m["posSource"], "setting": m["setting"], "condition": m["condition"]} for i, m in enumerate(ngs)]
    land = grp("land")
    lsrc = np.array(cps["land_source_navd88_m"], dtype=np.float64)
    e_phys = land[:, 2] - lsrc
    e_rend = land[:, 3] - lsrc
    pipe2 = res({"rmse_m": r(np.sqrt(np.nanmean(e_phys ** 2))), "max_m": r(np.nanmax(np.abs(e_phys))), "n": int(np.isfinite(e_phys).sum())},
                "RMSE <= 0.10 m, max <= 0.5 m (physics surface vs 3DEP source)",
                bool(np.sqrt(np.nanmean(e_phys ** 2)) <= 0.10 and np.nanmax(np.abs(e_phys)) <= 0.5 and np.isfinite(e_phys).all()),
                render_surface={"rmse_m": r(np.sqrt(np.nanmean(e_rend ** 2))), "max_m": r(np.nanmax(np.abs(e_rend)))})
    m2 = {"ngs_marks_in_tile": len(ngs), "ngs_qualifying": len(q), "marks": all_d, "pipeline": pipe2}
    if len(q) >= 5:
        med = float(np.median(np.abs(dq)))
        frac = float(np.mean(np.abs(dq) > 2))
        m2["ngs"] = res({"median_abs_m": r(med), "share_over_2m": r(frac)}, "median |d| <= 0.5 m and <= 10 % beyond 2 m", med <= 0.5 and frac <= 0.10)
        m2["result"] = "pass" if m2["ngs"]["result"] == "pass" and pipe2["result"] == "pass" else "fail"
    else:
        m2["ngs"] = {"result": "insufficient_marks", "qualifying": len(q), "rule": "fewer than 5 qualifying marks: the pipeline check gates (T-003 C3); thresholds unchanged",
                     "independent_check_all_marks_median_abs_m": r(np.median(np.abs([d["delta_m"] for d in all_d if d["delta_m"] is not None]))) if all_d else None}
        m2["result"] = pipe2["result"]
    rep["m005"]["M-005.2"] = m2

    # ---------- M-005.3 bathymetry
    wet = grp("wet")
    wsrc = np.array(cps["wet_source_navd88_m"], dtype=np.float64)
    ew = wet[:, 2] - wsrc
    cp_, _ = source_rasters(tile)
    import rasterio
    srcmin = []
    x0, z0, x1, z1 = tile.bounds
    for p in cp_:
        with rasterio.open(p) as d:
            a = d.read(1).astype(np.float64)
            a[a == d.nodata] = np.nan
            rows_, cols_ = np.mgrid[0:a.shape[0], 0:a.shape[1]]
            xs_, ys_ = rasterio.transform.xy(d.transform, rows_.ravel(), cols_.ravel())
            bx, bz = lonlat_to_baytm(np.array(xs_), np.array(ys_))
            inside = (bx >= x0) & (bx <= x1) & (bz >= z0) & (bz <= z1)
            srcmin.append(np.nanmin(a.ravel()[inside]))
    smin = float(np.min(srcmin))
    gmin = float(E["stats"]["terrain.h_min_m"])
    m3 = {"pipeline_rmse": res({"rmse_m": r(np.sqrt(np.nanmean(ew ** 2))), "max_m": r(np.nanmax(np.abs(ew))), "n": int(np.isfinite(ew).sum())},
                               "RMSE <= 0.25 m vs CUDEM on 10,000 wet points", bool(np.sqrt(np.nanmean(ew ** 2)) <= 0.25)),
          "tile_minimum": res({"as_built_min_m": r(gmin, 3), "cudem_min_in_tile_m": r(smin, 3), "delta_m": r(gmin - smin, 3)}, "within 1 m", abs(gmin - smin) <= 1.0),
          "golden_gate": na("box -122.484..-122.472 W, 37.814..37.823 N is outside L0_30_36", preview=gg_preview()),
          "oakland_inner_harbor": na("box -122.300..-122.285 W is outside L0_30_36"),
          "san_pablo_shoal": na("box 38.04..38.06 N is outside L0_30_36"),
          "bay_wide_shallowness": na("needs the Bay polygon (~1200 km^2); one tile is 4.2 km^2", due="M1/M2")}
    m3["result"] = "pass" if m3["pipeline_rmse"]["result"] == "pass" and m3["tile_minimum"]["result"] == "pass" else "fail"
    rep["m005"]["M-005.3"] = m3

    # ---------- M-005.4 building heights
    rendered = [b for b in E["buildings"] if b["height_as_built_m"] is not None]
    hb = np.array([b["height_as_built_m"] for b in rendered])
    hsrc = np.array([b["source_height_m"] for b in rendered])
    dd = np.abs(hb - hsrc)
    from scipy.stats import ks_2samp
    hb, hsrc = np.round(hb, 3), np.round(hsrc, 3)   # compare at 1 mm (float32 mesh noise would split ties)
    ks = ks_2samp(hb, hsrc)
    qs = {}
    okq = True
    for p in (50, 90, 99):
        a, b = float(np.percentile(hb, p)), float(np.percentile(hsrc, p))
        tol = max(0.5, 0.01 * b)
        qs[f"p{p}"] = {"as_built": r(a, 3), "source": r(b, 3), "tol": r(tol, 3)}
        okq &= abs(a - b) <= tol
    shares = {}
    for b in E["buildings"]:
        shares[b["height_source"]] = shares.get(b["height_source"], 0) + 1
    n_all = len(E["buildings"])
    surf_b = [b for b in E["buildings"] if not b.get("excluded")]
    n_surf = len(surf_b)
    def_surf = sum(1 for b in surf_b if b["height_source"] == "default")
    area_def = sum(b["area_m2"] for b in E["buildings"] if b["height_source"] == "default") / sum(b["area_m2"] for b in E["buildings"])
    m4 = {"a_per_building": res({"share_within_0.5m": r(np.mean(dd <= 0.5)), "n": len(dd), "max_abs_m": r(dd.max(), 3)}, ">= 99 %", bool(np.mean(dd <= 0.5) >= 0.99)),
          "b_distribution": res({"ks_d": r(ks.statistic, 5), "quantiles": qs}, "KS D <= 0.02; p50/p90/p99 within max(0.5 m, 1 %)", bool(ks.statistic <= 0.02 and okq)),
          "c_fallback_validation": fallback_validation(tile, cfg),
          "height_source_shares": {k: {"count": v, "share": r(v / n_all)} for k, v in sorted(shares.items())},
          "default_share": res(r(shares.get("default", 0) / n_all), "< 10 % of buildings (T-003 C5)", shares.get("default", 0) / n_all < 0.10,
                               area_weighted_share=r(area_def), surface_buildings_only={"default": def_surf, "of": n_surf, "share": r(def_surf / n_surf)},
                               excluded_records=sum(1 for b in E["buildings"] if b.get("excluded")),
                               parts_capped_to_parent=sum(1 for f in feats if f["properties"].get("height_capped_to_parent")),
                               mitigation="see t003.C5_default_share_mitigation"),
          "roof_rule": "as-built max(y) = base_navd88_m + height_m for every roof_shape; flat roofs except pyramidal/spire/cone with roof:height (apex at height_m). Parents with building:part render only through their parts and are excluded from (a)/(b).",
          "rendered_buildings": len(rendered), "structures_rendered_by_parts": sum(1 for b in E["buildings"] if b["has_parts"])}
    m4["result"] = "pass" if all(m4[k]["result"] == "pass" for k in ("a_per_building", "b_distribution")) else "fail"
    rep["m005"]["M-005.4"] = m4

    # ---------- M-005.5 footprints
    rep["m005"]["M-005.5"] = footprints(tile, E, feats, water)

    # ---------- M-005.6 scale
    rep["m005"]["M-005.6"] = scale_check(tile, E, ref, eb, sb, man, top_of)

    # ---------- M-005.7 skyline
    rep["m005"]["M-005.7"] = skyline_check(tile, ref, hs, water, feats)

    # ---------- M-005.8 licences / attribution
    errs = manifest_mod.validate(man, tile_paths(tile))
    att = J(EVID / "export" / "attribution_asset_dump.json") if (EVID / "export" / "attribution_asset_dump.json").exists() else None
    need = set(manifest_mod.attributions(man)) | {s["attribution"] for s in J(GLOBAL_ROOT / "faults_bay.manifest.json")["sources"]}
    missing = sorted(need - set(att["lines"])) if att else sorted(need)
    m8 = {"manifest_complete": res({"problems": errs, "layers": len(man["layers"])}, "100 % complete", not errs),
          "attribution_diff": res({"required": sorted(need), "missing_from_asset": missing, "asset": att["asset"] if att else None},
                                  "every manifest attribution in the Unity attribution asset", not missing and att is not None),
          "odbl_layers": [L["layer"] for L in man["layers"] if any(s.get("derivative_database_candidate") for s in L["sources"])],
          "licences": sorted({s["licence"] for L in man["layers"] for s in L["sources"]})}
    m8["result"] = "pass" if m8["manifest_complete"]["result"] == "pass" and m8["attribution_diff"]["result"] == "pass" else "fail"
    rep["m005"]["M-005.8"] = m8

    t003(rep, tile, E, cps, man, hs, water, feats, samples, cfg)

    # ---------- summary
    subs = []
    for k, v in rep["m005"].items():
        for kk, vv in v.items():
            if isinstance(vv, dict) and "result" in vv:
                subs.append((f"{k}.{kk}", vv["result"]))
    counts = {}
    for _, s in subs:
        counts[s] = counts.get(s, 0) + 1
    rep["summary"] = {"sub_measures": dict(subs), "counts": counts,
                      "m005_overall": "fail" if any(s == "fail" for _, s in subs) else ("incomplete" if any(s in (NA, "not_run") for _, s in subs) else "pass"),
                      "note": "M-005 passes only if every sub-measure passes (R-004 §6); not_applicable_in_tile items are due at M1 and are not passes."}
    v1 = {"metric_version": "M-005@v1", "source": "research/R-004.md §6", "m005": rep["m005"], "summary": rep["summary"]}
    v11 = check_v11(tile, E, man, hs, water, feats, cfg, ref, v1)
    rep = {"tile": tile.id, "note": "two metric versions side by side: v1 (R-004 §6) and v1_1 (research/R-006.md §5, Assumed pending founder calibration); t003 = acceptance checks outside M-005",
           "v1": v1, "v1_1": v11, "t003": rep["t003"]}
    out = REPORT_PATH
    out.write_text(json.dumps(rep, indent=1, ensure_ascii=False) + "\n")
    print("v1", json.dumps(v1["summary"]["counts"]), "| v1.1", json.dumps(v11["summary"]["counts"]), "->", out)
    for k, s in subs:
        if s == "fail":
            print("FAIL v1", k)
    for k, s in v11["summary"]["sub_measures"].items():
        if s == "fail":
            print("FAIL v1.1", k)


def summarise(block):
    subs = []
    for k, v in block.items():
        if not isinstance(v, dict):
            continue
        if "result" in v and k.startswith("M-005.9"):
            subs.append((k, v["result"]))
        for kk, vv in v.items():
            if isinstance(vv, dict) and "result" in vv:
                subs.append((f"{k}.{kk}", vv["result"]))
    counts = {}
    for _, x in subs:
        counts[x] = counts.get(x, 0) + 1
    return {"sub_measures": dict(subs), "counts": counts,
            "m005_overall": "fail" if any(x == "fail" for _, x in subs) else ("incomplete" if any(x in (NA, "not_run", "threshold pending noise floor") for _, x in subs) else "pass")}


def v11_skyline_spec(tile, feats, water, msl, tags, ref):
    """Change mask + v1.1 views for the Unity capture (written by `prepare_v11`)."""
    from kaiju_geo import m005_v11 as V
    landmarks = {l["osm"] for l in ref["landmarks"] if l["osm"]}
    surf, dtm, st, _ = V.lidar_surfaces(tile, feats, water, msl, landmarks, tags)
    cm = V.change_mask(tile, feats, tags, surf, dtm)
    vp = {v["id"]: v for v in ref["viewpoints"]}
    views = []
    for name, vid, eye, heading, hfov, gate in V.V11_VIEWS:
        obs = [V.OBSERVER[vid]] if vid in V.OBSERVER else []
        for suffix, hide in (("", obs), ("_cm", obs + [c["structure"] for c in cm])):
            views.append({"name": f"skyline_v11_{name}{suffix}", "x": vp[vid]["baytm"][0], "z": vp[vid]["baytm"][1], "eye_navd88_m": eye,
                          "heading_deg": heading, "hfov_deg": hfov, "width": 1024, "height": 576, "hide": hide, "gating": gate})
    return {"spec": "skyline_v11_spec", "metric_version": "M-005@v1.1", "views": views, "change_mask": cm, "recipe_stats": st}


def check_v11(tile, E, man, hs, water, feats, cfg, ref, v1):
    from kaiju_geo import m005_v11 as V
    from kaiju_geo.bake import read_water_polygons
    from PIL import Image
    msl = man["water_level_navd88_m"]
    tags = V.osm_tags(tile)
    wp = read_water_polygons(tile, 64.0)
    B = {}
    carried = {"M-005.1": v1["m005"]["M-005.1"], "M-005.2": v1["m005"]["M-005.2"], "M-005.3": v1["m005"]["M-005.3"],
               "M-005.6": v1["m005"]["M-005.6"], "M-005.8": v1["m005"]["M-005.8"]}
    for k, v in carried.items():
        B[k] = dict(v, metric_version="M-005@v1.1", carried_from="v1 (unchanged in v1.1)")
    m4 = {kk: vv for kk, vv in v1["m005"]["M-005.4"].items() if kk in ("a_per_building", "b_distribution", "c_fallback_validation")}
    m4["d_default_share"] = V.default_share(tile, feats, E, wp, msl, cfg, tags)
    B["M-005.4"] = dict(m4, metric_version="M-005@v1.1", note="(a-c) carried from v1 on the fixed building set; (d) new in v1.1")
    st = V.structures(feats)
    excl_ids = set()
    for sid, elems in st.items():
        if V.v11_exclusion(sid, elems, tags, wp, msl):
            excl_ids |= {e["id"] for e in elems}
    B["M-005.5"] = dict(footprints(tile, E, feats, water, keep=lambda f: f["id"] not in excl_ids), metric_version="M-005@v1.1",
                        note="R-006 §2 exclusions applied to source and game")
    # .7 skyline
    spec_p = EVID / "skyline_v11_spec.json"
    spec = J(spec_p) if spec_p.exists() else None
    sky = {"V1": na("outside L0_30_36"), "V2": na("outside L0_30_36"), "V3": na("outside L0_30_36")}
    if spec is None or not (CACHE / "lidar" / f"rind_v11__{tile.id}.npz").exists():
        sky["result"] = "not_run"
        sky["reason"] = "run `m005_check.py prepare_v11` (needs lidar_ref_v11.py products) and GeoBatch.All"
    else:
        sky.update(skyline_v11(tile, feats, water, msl, tags, ref, spec, hs))
    B["M-005.7"] = sky
    B["M-005.9 shoreline continuity"] = V.shoreline_continuity(tile, cfg, *source_rasters(tile), piers_union(tile)[0])
    cv = J(EVID / "export" / "capture_views.json") if (EVID / "export" / "capture_views.json").exists() else {}
    B["C20 support (draw calls, R-006 §4)"] = {"metric_version": "M-005@v1.1", "result": "reported",
                                               "measured": cv.get("measured_render_stats"), "pipeline": cv.get("pipeline")}
    return {"metric_version": "M-005@v1.1", "source": "research/R-006.md §5 (Assumed, pending founder calibration)", "m005": B, "summary": summarise(B)}


def skyline_v11(tile, feats, water, msl, tags, ref, spec, hs):
    from kaiju_geo import m005_v11 as V
    from PIL import Image
    landmarks = {l["osm"] for l in ref["landmarks"] if l["osm"]}
    cm = spec["change_mask"]
    cm_ids = {c["structure"] for c in cm}
    surf_w = hs.copy()
    wet_nodes = np.zeros_like(surf_w, bool)
    wet_nodes[:-1, :-1] |= water; wet_nodes[1:, :-1] |= water; wet_nodes[:-1, 1:] |= water; wet_nodes[1:, 1:] |= water
    surf_w = np.where(wet_nodes, np.maximum(surf_w, msl), surf_w)

    def node_sampler(h):
        def s(x, z):
            inside = (x >= tile.x0) & (x <= tile.x0 + tile.size) & (z >= tile.z0) & (z <= tile.z0 + tile.size)
            v = bilinear(h, tile, np.clip(x, tile.x0, tile.x0 + tile.size), np.clip(z, tile.z0, tile.z0 + tile.size))
            return np.where(inside, v, np.nan)
        return s

    rind, dtm, rstats, _ = V.lidar_surfaces(tile, feats, water, msl, landmarks, tags)
    rind_max, _, _, _ = V.lidar_surfaces(tile, feats, water, msl, landmarks, tags, recipe="max")
    rind_1m, dtm1, _, _ = V.lidar_surfaces(tile, feats, water, msl, landmarks, tags, res=1.0)

    def masked(surf, d, res, ids):
        from rasterio.features import rasterize
        from rasterio.transform import Affine
        geoms = [shape(f["geometry"]).buffer(5.0) for f in feats if (f["properties"]["part_of"] or f["id"]) in ids]
        if not geoms:
            return surf, geoms
        n = surf.shape[0]
        m = rasterize([(g, 1) for g in geoms], out_shape=(n, n), transform=Affine(res, 0, tile.x0, 0, res, tile.z0), dtype="uint8").astype(bool)
        return np.where(m, d, surf), geoms

    out = {"recipe": {"points": "first returns, classes 7/18/22 dropped (filters.expression); withheld flag not exposed by the EPT reader (not applied)",
                      "cell": "p95 of Z per 0.5 m BayTM cell", "dtm": "geo_tile bare-earth surface (3DEP 1 m from the same B23 ground class), MSL over water",
                      "vegetation": "cells with > 50 % multi-return first returns set to DTM (Assumed)", "cranes": "3 x 3 m grey opening above 20 m, R-004 §5 landmark footprints + 5 m exempt (Assumed)",
                      "stats": rstats, "change_mask": cm,
                      "contains": "buildings and rooftop plant, bridges, residual non-canopy vegetation, ships; NOT canopy (masked), thin cranes/poles (opened), masked change structures"},
           "views": {}}
    for name, vid, eye, heading, hfov, gate in V.V11_VIEWS:
        vp = next(v for v in ref["viewpoints"] if v["id"] == vid)
        cam = skyline.Cam(vp["baytm"][0], vp["baytm"][1], eye, heading, 1024, 576, hfov)
        obs = V.OBSERVER.get(vid)
        fts = [f for f in feats if obs is None or (f["id"] != obs and f["properties"]["part_of"] != obs)]
        fts = [f for f in fts if not f["properties"].get("excluded")]
        src_rows = skyline.first_rows(np.minimum(skyline.profile_heightfield(cam, node_sampler(surf_w)), skyline.profile_prisms(cam, building_edges(fts, tile))))
        ids = cm_ids | ({obs} if obs else set())
        r_ind, mgeoms = masked(rind, dtm, 0.5, ids)
        r_ind2 = V.datasf_surface(tile, dtm, 0.5, mgeoms)

        def prof(arr, res, c=cam):
            return skyline.first_rows(skyline.profile_heightfield(c, V.grid_sampler(tile, arr, res), step=0.4))
        ind_rows = prof(r_ind, 0.5)
        ind2_rows = prof(r_ind2, 0.5)
        e = {"camera": {"eye_navd88_m": eye, "heading_deg": heading, "hfov_deg": hfov, "size": [1024, 576], "mrad_per_px": round(1000 / cam.f, 3)},
             "gating": gate, "hidden_structures": sorted(ids)}
        gpath = EVID / "captures" / f"skyline_v11_{name}_mask.png"
        gcm = EVID / "captures" / f"skyline_v11_{name}_cm_mask.png"
        if not gpath.exists() or not gcm.exists():
            e["result"] = "not_run"; e["reason"] = "Unity v1.1 masks missing"
            out["views"][name] = e
            continue
        g_rows = skyline.mask_profile(np.array(Image.open(gpath).convert("L")) >= 128)
        gcm_rows = skyline.mask_profile(np.array(Image.open(gcm).convert("L")) >= 128)
        c_src = V.compare_v11(g_rows, src_rows, cam)
        e["vs_r_src"] = res(c_src, "IoU >= 0.98, MAE <= 1 px (R-src gate unchanged)", c_src["iou"] >= 0.98 and c_src["mae_px"] <= 1.0)
        c_ind = V.compare_v11(gcm_rows, ind_rows, cam)
        floor = V.compare_v11(ind2_rows, ind_rows, cam)
        noise = {"p95_vs_max_cell": V.compare_v11(prof(masked(rind_max, dtm, 0.5, ids)[0], 0.5), ind_rows, cam),
                 "cell_0.5_vs_1.0_m": V.compare_v11(prof(masked(rind_1m, dtm1, 1.0, ids)[0], 1.0), ind_rows, cam)}
        for dz in (-1.5, 1.5):
            c2 = skyline.Cam(vp["baytm"][0], vp["baytm"][1], eye + dz, heading, 1024, 576, hfov)
            noise[f"eye_{dz:+.1f}m"] = V.compare_v11(prof(r_ind, 0.5, c2), ind_rows, cam)
        ok_floor = c_ind["iou"] >= floor["iou"] - 0.02 and c_ind["iou"] >= 0.80 and c_ind["mae_px"] <= floor["mae_px"] + 1
        gates = c_ind["skyline_column_share"] >= 0.25
        e["vs_r_ind"] = res(c_ind, "IoU >= IoU(F_v) - 0.02 and >= 0.80; MAE <= MAE(F_v) + 1 px (Assumed, pending calibration)", ok_floor,
                            noise_floor_F_v=floor, F_v_reference="R-ind vs R-ind2 (DataSF 2010 extrusions on the same DTM, same change mask)",
                            within_reference_noise=noise, gates_this_view=gates)
        if c_ind["shift_px"] != 0 or c_src["shift_px"] != 0:
            e["registration_note"] = "best column shift within +-1 px (allowed); beyond +-1 px would be a registration defect"
        if not gate or not gates:
            for k in ("vs_r_src", "vs_r_ind"):
                if e[k]["result"] == "fail" and not gate:
                    e[k]["result"] = "reported"
            if gate and not gates:
                e["vs_r_ind"]["result"] = "reported"
                e["vs_r_ind"]["reason"] = "< 25 % skyline columns: the view reports, it does not gate"
        overlay(np.array(Image.open(gcm).convert("L")) >= 128, ind_rows, {}, EVID / "skyline" / f"skyline_v11_{name}_overlay_rind.png", ind2_rows)
        overlay(np.array(Image.open(gpath).convert("L")) >= 128, src_rows, {}, EVID / "skyline" / f"skyline_v11_{name}_overlay_rsrc.png")
        out["views"][name] = e
        out[name] = {"result": "pass" if all(e[k]["result"] in ("pass", "reported") for k in ("vs_r_src", "vs_r_ind")) and gate else
                     ("reported" if not gate else "fail")}
    return out


def gg_preview():
    """Informational: the Golden Gate box measured on the second tile L0_26_37 (pipeline only, not a pass)."""
    p = CACHE / "extra_tiles" / "L0" / "26_37" / "terrain.tif"
    if not p.exists():
        return None
    import rasterio
    with rasterio.open(p) as d:
        a = d.read(1).astype(np.float64)
        rows_, cols_ = np.mgrid[0:a.shape[0], 0:a.shape[1]]
        xs_, zs_ = rasterio.transform.xy(d.transform, rows_.ravel(), cols_.ravel())
    lon, lat = baytm_to_lonlat(np.array(xs_), np.array(zs_))
    m = (lon >= -122.484) & (lon <= -122.472) & (lat >= 37.814) & (lat <= 37.823)
    v = a.ravel()[m]
    return {"tile": "L0_26_37", "box_fraction_inside_tile": "partial (the box extends east of x = 55296; the deepest cells R-004 lists lie outside this tile)", "min_m": r(v.min(), 2), "p5_m": r(np.percentile(v, 5), 2), "threshold": "min -113 +/- 4 m; p5 <= -100 m",
            "would_pass": bool(abs(v.min() + 113) <= 4 and np.percentile(v, 5) <= -100), "cells": int(m.sum())}


def fallback_validation(tile, cfg):
    o = vector.read_osm(CACHE / "osm" / "tiles" / f"{tile.id}.osm.pbf")
    datasf = vector.read_datasf(CACHE / "datasf" / f"ynuv-fyni__{tile.id}.geojson")
    tree = shapely.STRtree([d["geom"] for d in datasf])
    recs = []
    for rr in o["buildings"] + o["parts"]:
        g = vector.clean_polygonal(rr["geom"])
        h = vector.parse_length_m(rr["tags"].get("height"))
        if g is None or h is None or h <= 0:
            continue
        rp = g.point_on_surface()
        if not tile.contains(rp.x, rp.y):
            continue
        recs.append((rr, g, h))
    rng = np.random.default_rng(5)
    idx = rng.choice(len(recs), size=len(recs) // 5, replace=False)
    pred, true, src = [], [], {}
    for i in sorted(idx):
        rr, g, h = recs[i]
        tags = {k: v for k, v in rr["tags"].items() if k != "height"}
        hp, s, _ = vector.resolve_height(tags, g, tree, datasf, cfg["buildings"])
        pred.append(hp); true.append(h); src[s] = src.get(s, 0) + 1
    from scipy.stats import ks_2samp
    ks = ks_2samp(pred, true).statistic
    med = float(np.median(np.abs(np.array(pred) - np.array(true))))
    d = res({"ks_d": r(ks, 4), "median_abs_err_m": r(med, 3), "hidden": len(idx), "fallback_used": src},
            "KS D <= 0.15 and median |e| <= 3 m (Assumed, pending calibration)", ks <= 0.15 and med <= 3.0)
    d["status"] = "Assumed"
    return d


def footprints(tile, E, feats, water, keep=None):
    from rasterio.features import rasterize
    from rasterio.transform import Affine
    x0, z0, x1, z1 = tile.bounds
    m = 64
    res_ = 0.5
    n = int((tile.size + 2 * m) / res_)
    tr = Affine(res_, 0, x0 - m, 0, -res_, z1 + m)

    def poly_of(fp):
        polys = []
        for p in fp:
            rings = [[tuple(pt) for pt in ring] for ring in p]
            polys.append(shapely.geometry.Polygon(rings[0], rings[1:]))
        return shapely.geometry.MultiPolygon(polys) if len(polys) > 1 else polys[0]

    keep = keep or (lambda f: True)
    surface = [f for f in feats if not f["properties"].get("excluded") and keep(f)]
    kept_ids = {f["id"] for f in surface}
    game = [poly_of(b["footprint"]) for b in E["buildings"] if b["height_as_built_m"] is not None and b["bid"] in kept_ids]
    src = [shape(f["geometry"]) for f in surface]
    G = rasterize([(g, 1) for g in game], out_shape=(n, n), transform=tr, dtype="uint8").astype(bool)
    S = rasterize([(g, 1) for g in src], out_shape=(n, n), transform=tr, dtype="uint8").astype(bool)
    inter = np.logical_and(G, S).sum()
    recall, precision = inter / S.sum(), inter / G.sum()
    ebids = {b["bid"] for b in E["buildings"]}
    big = [f for f in surface if shape(f["geometry"]).area >= 20]
    present = [f for f in big if f["id"] in ebids]
    cnt = len(present) / len(big)
    absent_from_source = sorted(ebids - {f["id"] for f in feats})
    # buildings over water cells (water.png) and over OSM water polygons
    from kaiju_geo.bake import read_water_polygons
    wp = read_water_polygons(tile, 64.0)
    over_osm_water = [f["id"] for f in surface if shape(f["geometry"]).intersection(wp).area > 0.5 * shape(f["geometry"]).area]
    W = np.zeros((n, n), bool)
    wcells = np.flipud(water)   # north-up cells at 1 m -> 0.5 m
    W[2 * m:2 * m + 4096, 2 * m:2 * m + 4096] = np.kron(wcells, np.ones((2, 2), bool))
    lab = rasterize([(g, i + 1) for i, g in enumerate(game)], out_shape=(n, n), transform=tr, dtype="int32")
    tot = np.bincount(lab.ravel(), minlength=len(game) + 1)
    wet_ = np.bincount(lab[W].ravel(), minlength=len(game) + 1)
    share = np.where(tot[1:] > 0, wet_[1:] / np.maximum(tot[1:], 1), 0)
    over_cells = int((share > 0.01).sum())
    sliver_m2 = float(wet_[1:].sum() * res_ * res_)
    ds = vector.read_datasf(CACHE / "datasf" / f"ynuv-fyni__{tile.id}.geojson")
    D = rasterize([(d["geom"], 1) for d in ds], out_shape=(n, n), transform=tr, dtype="uint8").astype(bool)
    d = {"area": res({"recall": r(recall, 5), "precision": r(precision, 5)}, ">= 0.98 each (0.5 m raster)", bool(recall >= 0.98 and precision >= 0.98)),
         "count": res({"recall": r(cnt, 5), "source_ge_20m2": len(big)}, ">= 0.99 for source buildings >= 20 m^2 (matched by bid)", cnt >= 0.99),
         "no_invented_buildings": res({"bids_not_in_source": absent_from_source}, "0", not absent_from_source),
         "none_over_water": res({"buildings_with_gt_1pct_footprint_on_water_surface": over_cells, "edge_sliver_area_m2": round(sliver_m2, 2),
                                 "max_share": r(share.max(), 4), "footprints_mostly_on_osm_water": over_osm_water},
                                "no building with > 1 % of its footprint on the water surface (0.5 m raster vs 1 m water cells; edge slivers reported)", over_cells == 0,
                                note="footprints listed on OSM water are bridge supports (W2) or pier structures; the water surface is cut out under them"),
         "cross_source_osm_vs_datasf": {"result": "reported", "datasf_area_covered_by_osm": r(np.logical_and(S, D).sum() / D.sum(), 4),
                                         "osm_area_covered_by_datasf": r(np.logical_and(S, D).sum() / S.sum(), 4), "gate": "informational at M0, >= 0.85 at M2"}}
    d["result"] = "pass" if all(d[k]["result"] == "pass" for k in ("area", "count", "no_invented_buildings", "none_over_water")) else "fail"
    return d


def scale_check(tile, E, ref, eb, sb, man, top_of):
    from pyproj import Geod
    geod = Geod(ellps="GRS80")
    by_n = {l["n"]: l for l in ref["landmarks"]}
    pairs = []
    for a, b in ref["scale_pairs"]:
        la, lb = by_n.get(a), by_n.get(b)
        if la is None or lb is None:
            continue
        if la["osm"] in eb and lb["osm"] in eb:
            pa, pb = eb[la["osm"]]["anchor_baytm"], eb[lb["osm"]]["anchor_baytm"]
            d_game = math.hypot(pb[0] - pa[0], pb[1] - pa[1])
            # geodesic between the same anchors' source positions (source centroid -> lon/lat)
            sa = centroid(shape(sb[la["osm"]]["geometry"])); sbb = centroid(shape(sb[lb["osm"]]["geometry"]))
            loa, laa = baytm_to_lonlat(*sa); lob, lab = baytm_to_lonlat(*sbb)
            kind = "as_built"
        else:
            loa, laa, lob, lab = la["lon"], la["lat"], lb["lon"], lb["lat"]
            xa, za = lonlat_to_baytm(loa, laa); xb, zb = lonlat_to_baytm(lob, lab)
            d_game = math.hypot(xb - xa, zb - za)
            kind = "projection_only (out of tile)"
        d_geo = geod.inv(loa, laa, lob, lab)[2]
        ppm = (d_game / d_geo - 1) * 1e6
        ok = abs(ppm) <= 60 and (d_geo >= 2000 or abs(d_game - d_geo) <= 0.1)
        pairs.append({"pair": [a, b], "names": [la["name"], lb["name"]], "kind": kind, "d_game_m": r(d_game, 4), "d_geo_m": r(d_geo, 4),
                      "ppm": r(ppm, 2), "delta_m": r(d_game - d_geo, 4), "result": "pass" if ok else "fail"})
    asb = [p for p in pairs if p["kind"] == "as_built"]
    vert = []
    for bid in ("osm:w431972186", "osm:w72963089"):
        b = eb[bid]
        hb = top_of(bid) - b["base_navd88_m"]
        vert.append({"bid": bid, "as_built_m": r(hb, 4), "source_m": b["source_height_m"], "ratio": r(hb / b["source_height_m"], 6)})
    crs_txt = (tile_paths(tile) / "manifest.json").read_text() + (PROJECT / "Assets/StreamingAssets/Geo/crs/baytm_v1.wkt").read_text()
    d = {"pairs": pairs,
         "horizontal": res({"pairs": len(pairs), "as_built_pairs": len(asb), "max_abs_ppm": r(max(abs(p["ppm"]) for p in pairs), 2)},
                           ">= 10 pairs, |ratio - 1| <= 60 ppm, |delta| <= 0.1 m under 2 km", len(pairs) >= 10 and all(p["result"] == "pass" for p in pairs)),
         "vertical_scale": res(vert, "exactly 1 (as-built height / source height)", all(abs(v["ratio"] - 1) < 1e-5 for v in vert)),
         "unit_and_crs": res({"spacing_m": next(L["spacing_m"] for L in man["layers"] if L["layer"] == "terrain"), "crs": man["crs"], "utm_mentioned": "UTM" in crs_txt},
                             "1 unit = 1 m; BayTM@v1 stored; no UTM", "UTM" not in crs_txt and man["crs"].startswith("kaiju:BayTM@v1"))}
    d["result"] = "pass" if all(d[k]["result"] == "pass" for k in ("horizontal", "vertical_scale", "unit_and_crs")) else "fail"
    return d


def building_edges(feats, tile):
    """Roof outline segments (and apex edges) of every rendered building, world coords."""
    edges = []
    for f in feats:
        p = f["properties"]
        if p["has_parts"] and not p.get("remainder_geometry"):
            continue
        top = p["base_navd88_m"] + p["height_m"]
        g = shape(p["remainder_geometry"]) if p["has_parts"] else shape(f["geometry"])
        rh = p.get("roof_height_m") or 0
        apex = (not p["has_parts"]) and rh and rh < p["height_m"] - p["min_height_m"] and p.get("roof_shape") in ("pyramidal", "spire", "cone") and g.geom_type == "Polygon" and not g.interiors
        eave = top - rh if apex else top
        for poly in getattr(g, "geoms", [g]):
            for ring in [poly.exterior] + list(poly.interiors):
                c = np.asarray(ring.coords)
                for i in range(len(c) - 1):
                    edges.append((c[i, 0], eave, c[i, 1], c[i + 1, 0], eave, c[i + 1, 1]))
            if apex:
                # apex at the area centroid (as the Unity extruder)
                cx, cz = poly.centroid.x, poly.centroid.y
                for pt in np.asarray(poly.exterior.coords)[:-1]:
                    edges.append((pt[0], eave, pt[1], cx, top, cz))
    return np.array(edges)


def skyline_check(tile, ref, hs, water, feats):
    from PIL import Image
    cap = EVID / "captures"
    msl = J(tile_paths(tile) / "manifest.json")["water_level_navd88_m"]
    surf = hs.copy()
    # water surface over water cells (cell-centred) lifts the node surface to MSL where any adjoining cell is wet
    wet_nodes = np.zeros_like(surf, bool)
    wet_nodes[:-1, :-1] |= water; wet_nodes[1:, :-1] |= water; wet_nodes[:-1, 1:] |= water; wet_nodes[1:, 1:] |= water
    surf = np.where(wet_nodes, np.maximum(surf, msl), surf)

    def sampler(h):
        def s(x, z):
            inside = (x >= tile.x0) & (x <= tile.x0 + tile.size) & (z >= tile.z0) & (z <= tile.z0 + tile.size)
            v = bilinear(h, tile, np.clip(x, tile.x0, tile.x0 + tile.size), np.clip(z, tile.z0, tile.z0 + tile.size))
            return np.where(inside, v, np.nan)
        return s

    hide = {"V5": "osm:w28824850"}     # the observer stands on Coit Tower: its own structure is excluded (all renders)
    ind = None
    lid = CACHE / "lidar" / f"dsm_first_return_v2__{tile.id}.tif"
    if lid.exists():
        xs = tile.x0 + np.arange(2049) * tile.spacing
        X, Z = np.meshgrid(xs, tile.z0 + np.arange(2049) * tile.spacing)
        dsm = terrain.sample_rasters([lid], X, Z)
        ind = np.where(np.isfinite(dsm), dsm, surf)
        ind = np.where(wet_nodes & ~np.isfinite(dsm), np.maximum(hs, msl), ind)
    out = {"V1": na("Treasure Island viewpoint is outside L0_30_36"), "V2": na("Twin Peaks is outside L0_30_36"),
           "V3": na("Vista Point, Marin is outside L0_30_36")}
    plates = {}
    for v in ref["viewpoints"]:
        if v["tile"] != tile.id:
            continue
        for suffix, eye in (("", v["eye_navd88_m"]), ("_eye70", 70.0)):
            if suffix and v["id"] != "V4":
                continue
            for size, tag in (((1024, 576), ""), ((480, 270), "_270rows")):
                if suffix and tag:
                    continue
                name = f"skyline_{v['id']}{suffix}{tag}"
                mpath = cap / f"{name}_mask.png"
                w, h = size
                cam = skyline.Cam(v["baytm"][0], v["baytm"][1], eye, v["heading_deg"], w, h)
                hid = hide.get(v["id"])
                fts = [f for f in feats if hid is None or (f["id"] != hid and f["properties"]["part_of"] != hid)]
                src = np.minimum(skyline.profile_heightfield(cam, sampler(surf)), skyline.profile_prisms(cam, building_edges(fts, tile)))
                ind_v = ind
                if ind is not None and hid is not None:
                    hg = shapely.union_all([shape(f["geometry"]) for f in feats if f["id"] == hid or f["properties"]["part_of"] == hid]).buffer(3.0)
                    xs = tile.x0 + np.arange(2049) * tile.spacing
                    X, Z = np.meshgrid(xs, tile.z0 + np.arange(2049) * tile.spacing)
                    inside = shapely.contains_xy(hg, X.ravel(), Z.ravel()).reshape(X.shape)
                    ind_v = np.where(inside, surf, ind)

                src_rows = skyline.first_rows(src)
                entry = {"eye_navd88_m": eye, "size": [w, h], "excluded_own_structure": hid}
                if not mpath.exists():
                    entry["vs_r_src"] = not_run("Unity mask capture missing: " + mpath.name)
                else:
                    gm = np.array(Image.open(mpath).convert("L")) >= 128
                    g_rows = skyline.mask_profile(gm)
                    c = skyline.compare(g_rows, src_rows, h, cam.cy)
                    entry["vs_r_src"] = res(c, "IoU >= 0.98, MAE <= 1 px", c["iou"] is not None and c["iou"] >= 0.98 and c["mae_px"] <= 1.0)
                    if ind is not None:
                        ir = skyline.first_rows(skyline.profile_heightfield(cam, sampler(ind_v)))
                        ci = skyline.compare(g_rows, ir, h, cam.cy)
                        entry["vs_r_ind"] = res(ci, "IoU >= 0.90, MAE <= 3 px (Assumed)", ci["iou"] is not None and ci["iou"] >= 0.90 and ci["mae_px"] <= 3.0,
                                                reference="3DEP CA_SanFrancisco_1_B23 first-return DSM v2 (PDAL, EPT resolution 0.7 m, 1 m max grid): first returns only; ASPRS noise classes 7 and 18 removed; statistical outliers (mean_k 8, 2.5 sigma) removed; clipped to the tile; water (no returns) filled at MSL. Still CONTAINS vegetation (trees on Telegraph Hill, street trees), cranes, poles, rooftop plant, bridge structure; it is not a buildings-only surface (the point cloud has no building class)")
                    else:
                        entry["vs_r_ind"] = not_run("lidar DSM not fetched (fetch_refs.py needs python-pdal)")
                    (EVID / "skyline").mkdir(exist_ok=True)
                    overlay(gm, src_rows, entry, EVID / "skyline" / f"{name}_overlay.png", ir if ind is not None else None)
                gate = (suffix == "" and tag == "")
                entry["gating"] = gate
                rs = [entry[k].get("result") for k in ("vs_r_src", "vs_r_ind") if k in entry]
                entry["result"] = "pass" if rs and all(x == "pass" for x in rs) else ("not_run" if "not_run" in rs else "fail")
                if not gate:
                    entry["result"] = "reported"
                plates[name] = entry
        out[v["id"]] = plates.get(f"skyline_{v['id']}")
    out["reported_variants"] = {k: v for k, v in plates.items() if not v.get("gating")}
    gated = [out["V4"], out["V5"]]
    ok = all(e and e["vs_r_src"].get("result") == "pass" for e in gated)
    ok_ind = all(e and e.get("vs_r_ind", {}).get("result") == "pass" for e in gated)
    out["result"] = "pass" if ok and ok_ind else "fail"
    return out


def overlay(gm, src_rows, entry, path, ind_rows=None):
    from PIL import Image
    h, w = gm.shape
    img = np.zeros((h, w, 3), np.uint8)
    img[gm] = (90, 90, 90)
    for u in range(w):
        v = int(np.clip(src_rows[u], 0, h - 1))
        img[v, u] = (255, 60, 60)
        if ind_rows is not None:
            v2 = int(np.clip(ind_rows[u], 0, h - 1))
            img[v2, u] = (60, 200, 255)
    Image.fromarray(img).save(path)


# ---------------------------------------------------------------- T-003 criteria outside M-005
def t003(rep, tile, E, cps, man, hs, water, feats, samples, cfg):
    T = rep["t003"]
    msl = man["water_level_navd88_m"]
    G = cps["groups"]
    a, n = G["shore"]
    sh = samples[a:a + n].reshape(50, 81, 5)
    meta = cps["shore_profiles"]
    offs = np.array(cps["shore_offsets_m"], dtype=np.float64)
    # C10: where does the as-built surface cross MSL along each profile (negative = land side)?
    cross, jumps, items = [], [], []
    cp, dp = source_rasters(tile)
    piers, _ = piers_union(tile)
    pts = np.array(cps["points"][a:a + n], dtype=np.float64).reshape(50, 81, 2)
    for k in range(50):
        phys = sh[k, :, 2]
        idx = np.where(np.diff(np.sign(phys - msl)) != 0)[0]
        off = float(offs[idx[np.argmin(np.abs(offs[idx]))]] + 0.5) if len(idx) else None
        j = np.abs(np.diff(phys))
        worst = int(np.argmax(j))
        # the source surface along the same profile: 3DEP 1 m where valid and above MHHW, else CUDEM
        px, pz = pts[k, :, 0], pts[k, :, 1]
        s3 = terrain.sample_rasters(dp, px[None, :], pz[None, :])[0]
        sc = terrain.sample_rasters(cp, px[None, :], pz[None, :])[0]
        srcp = np.where(np.isfinite(s3) & (sc > cfg["terrain"]["use_3dep_above_navd88_m"]), s3, sc)
        js = np.abs(np.diff(srcp))
        lo, hi = max(worst - 1, 0), min(worst + 2, len(js))
        src_jump = float(np.nanmax(js[lo:hi]))
        mid = shapely.Point((px[worst] + px[worst + 1]) / 2, (pz[worst] + pz[worst + 1]) / 2)
        at_pier = bool(piers.distance(mid) <= 2.0)
        exc = None
        if j[worst] >= 1.5:
            if at_pier:
                exc = "pier edge (deck raised by the documented pier rule)"
            elif src_jump >= 1.5:
                exc = "seawall/step present in the 1 m source"
        items.append({"x": r(meta[k]["x"], 1), "z": r(meta[k]["z"], 1), "msl_crossing_offset_m": off,
                      "max_adjacent_jump_m": r(j[worst], 3), "source_jump_there_m": r(src_jump, 3), "at_pier": at_pier, "exception": exc})
        if off is not None:
            cross.append(abs(off))
        jumps.append(j[worst])
    bad = [it for it in items if it["max_adjacent_jump_m"] >= 1.5 and not it["exception"]]
    T["C10_shoreline"] = res({"median_offset_m": r(np.median(cross), 2) if cross else None, "profiles_with_crossing": len(cross), "water_level_navd88_m": msl},
                             "median offset <= 5 m between the terrain/water intersection and the OSM coastline", bool(cross) and np.median(cross) <= 5,
                             piers_dry=pier_dryness(tile, water))
    seam = seam_check(tile, cfg)
    js_all = np.array(jumps)
    seam["built_surface_steps_on_50_shoreline_profiles"] = {
        "definition": "max adjacent 1 m step of the built physics surface per profile (-40..+40 m across the OSM coastline)",
        "median_of_profile_max_m": r(np.median(js_all), 3), "max_m": r(js_all.max(), 3),
        "profiles_ge_1.5m": int((js_all >= 1.5).sum()), "of_which_documented": sum(1 for it in items if it["exception"]),
        "undocumented_ge_1.5m": len([it for it in items if it["max_adjacent_jump_m"] >= 1.5 and not it["exception"]])}
    T["C11_continuous_surface"] = res({"profiles": 50, "max_jump_m": r(max(jumps), 3), "undocumented_jumps_ge_1.5m": len(bad), "profiles_detail": items},
                                      "max adjacent jump < 1.5 m except documented seawalls/piers; seam median <= 0.1 m and no blend step > 0.5 m", not bad and seam["result"] == "pass", seam=seam,
                                      water_cells_above_ground=water_above_ground(hs, water, msl))
    T["C12_buildings"] = buildings_sit(tile, E, hs)
    T["C13_roads_faults"] = roads_faults(tile)
    T["C14_removal"] = removal_check()
    T["C16_spec"] = spec_check(tile, man, hs)
    T["C17_floating_origin"] = origin_check(E)
    T["C20_budgets"] = budgets(E, man, tile)
    T["C5_default_share_mitigation"] = default_mitigation(tile, cfg, E)
    T["wading"] = wading(hs, water, msl, tile)


def pier_dryness(tile, water):
    piers, _ = piers_union(tile)
    x0, z0 = tile.x0, tile.z0
    from rasterio.features import rasterize
    from rasterio.transform import Affine
    P = rasterize([(piers, 1)], out_shape=(2048, 2048), transform=Affine(1, 0, x0, 0, -1, z0 + 2048), dtype="uint8").astype(bool)
    return {"pier_cells": int(P.sum()), "pier_cells_with_water_surface": int(np.logical_and(P, np.flipud(water)).sum())}


def water_above_ground(hs, water, msl):
    cell = 0.25 * (hs[:-1, :-1] + hs[1:, :-1] + hs[:-1, 1:] + hs[1:, 1:])
    return {"water_cells_with_ground_above_msl": int(np.logical_and(water, cell > msl).sum()), "dry_cells_below_msl_not_water": int(np.logical_and(~water, cell < msl).sum()),
            "note": "dry cells below MSL are diked/hydro-flattened land or OSM land; no water surface is drawn there"}


def seam_check(tile, cfg):
    """3DEP/CUDEM seam = the feather band of the merge (0 < w < 1, inside land). Median |3DEP - CUDEM| there, and the
    extra step the blend adds over the larger of the two sources' own adjacent-node steps."""
    cp, dp = source_rasters(tile)
    M = cfg["terrain"]["margin_nodes"]
    gx, gz = terrain.node_axes(tile, M)
    X, Z = np.meshgrid(gx, gz)
    hc = terrain.sample_rasters(cp, X, Z)
    h3 = terrain.sample_rasters(dp, X, Z)
    h, w = terrain.merge_surface(hc, h3, cfg["terrain"]["use_3dep_above_navd88_m"], cfg["terrain"]["feather_m"], tile.spacing)
    core = (slice(M, M + 2049), slice(M, M + 2049))
    h, w, hc, h3 = h[core], w[core], hc[core], h3[core]
    band = (w > 0) & (w < 1)
    d = np.abs(h3 - hc)[band]
    extra = []
    for ax in (0, 1):
        dm = np.abs(np.diff(h, axis=ax)); d3 = np.abs(np.diff(h3, axis=ax)); dc = np.abs(np.diff(hc, axis=ax))
        bb = band[:-1, :] | band[1:, :] if ax == 0 else band[:, :-1] | band[:, 1:]
        extra.append((dm - np.fmax(d3, dc))[bb])
    extra = np.concatenate(extra)
    land = np.isfinite(h3) & np.isfinite(hc) & (hc > cfg["terrain"]["use_3dep_above_navd88_m"])
    dl = (h3 - hc)[land]
    return {"all_land_nodes": int(land.sum()), "all_land_median_3dep_minus_cudem_m": r(np.median(dl), 3), "all_land_median_abs_m": r(np.median(np.abs(dl)), 3),
            "band_nodes": int(band.sum()), "median_abs_3dep_minus_cudem_m": r(np.median(d), 3), "p95_m": r(np.percentile(d, 95), 3),
            "max_extra_step_from_blend_m": r(np.nanmax(extra), 3),
            "result": "pass" if np.median(d) <= 0.1 and np.nanmax(extra) <= 0.5 else "fail",
            "threshold": "median |3DEP - CUDEM| <= 0.1 m in the feather band; the blend adds no step > 0.5 m beyond the sources' own steps"}


def buildings_sit(tile, E, hs):
    from scipy.ndimage import map_coordinates  # noqa: F401
    devs, thin, overlaps, floating = [], [], 0, []
    for b in E["buildings"]:
        if b["height_as_built_m"] is None:
            continue
        polys = [shapely.geometry.Polygon(p[0], p[1:]) for p in b["footprint"]]
        g = shapely.union_all(polys)
        if not box(*tile.bounds).contains(g):
            continue   # straddler: base sampled on the bake's margin grid, outside this raster
        rx, rz = vector.ring_points(g, 1.0)
        ring_base = float(np.percentile(bilinear(hs, tile, rx, rz), 10))
        xs, zs = vector.sample_points_in_polygon(g)
        gmin = float(np.min(bilinear(hs, tile, xs, zs)))
        own = b["part_of"] is None and not b.get("footprint_is_remainder")
        devs.append(abs(ring_base - b["base_navd88_m"]) if own else 0.0)
        if b["mesh_min_y_m"] is not None and b["mesh_min_y_m"] > gmin + 1e-3 and b.get("min_height_m", 0) == 0:
            floating.append(b["bid"])
        mrr = g.minimum_rotated_rectangle
        c = np.asarray(mrr.exterior.coords) if mrr.geom_type == "Polygon" else None
        if c is not None:
            w = min(math.dist(c[0], c[1]), math.dist(c[1], c[2]))
            if w < 1.0:
                thin.append(b["bid"])
    rend = [b for b in E["buildings"] if b["height_as_built_m"] is not None]
    geoms = [shapely.union_all([shapely.geometry.Polygon(p[0], p[1:]) for p in b["footprint"]]) for b in rend]
    tree = shapely.STRtree(geoms)
    for i, g in enumerate(geoms):
        for j in tree.query(g):
            if j <= i:
                continue
            if abs(rend[i]["top_navd88_m"] - rend[j]["top_navd88_m"]) < 0.01 and g.intersection(geoms[int(j)]).area > 1.0:
                overlaps += 1
    devs = np.array(devs)
    zf = J(EVID / "export" / "capture_views.json")["zfight"] if (EVID / "export" / "capture_views.json").exists() else None
    return res({"max_base_vs_terrain_m": r(devs.max(), 3), "share_within_0.5m": r(np.mean(devs <= 0.5), 4), "floating_buildings": len(floating),
                "base_definition": "10th percentile of the surface along the footprint ring (DEF-T003-bay-4); walls reach the minimum under the footprint minus 2 m",
                "thin_footprints_lt_1m": len(thin),
                "coplanar_overlapping_roofs": overlaps, "zfight_series": zf},
               "every base within 0.5 m of the local ground (ring p10); no building floats; watertight (EditMode test); no z-fighting at 10 m-3 km", bool(np.all(devs <= 0.5)) and not floating,
               thin_bids=thin[:30], skirt="walls extend 2 m below base so the LOD terrain (max error 0.2 m) never shows a gap")


def roads_faults(tile):
    rf = J(tile_paths(tile) / "roads.geojson")["features"]
    classes = {}
    for f in rf:
        classes[f["properties"]["class"]] = classes.get(f["properties"]["class"], 0) + 1
    bb = [f for f in rf if f["properties"]["bridge"] and "Eisenhower" in (f["properties"].get("name") or "")]
    fc = J(tile_paths(tile) / "faults.geojson")
    g = J(GLOBAL_ROOT / "faults_bay.geojson")
    fields = {"fault_name", "section", "linetype", "slip_rate", "slip_sense", "age"}
    return res({"roads": len(rf), "with_width": sum(1 for f in rf if f["properties"]["width_m"] > 0), "classes": classes,
                "bridges": sum(f["properties"]["bridge"] for f in rf), "tunnels": sum(f["properties"]["tunnel"] for f in rf),
                "bay_bridge_segments_flagged_bridge": len(bb), "faults_global": len(g["features"]),
                "faults_global_fields_ok": all(fields <= set(f["properties"]) for f in g["features"]),
                "faults_tile": len(fc["features"]), "empty_by_design": fc["empty_by_design"], "nearest_faults": fc["nearest_faults"][:4]},
               "roads with class/width; bridges/tunnels flagged (decks not draped on the ground); global faults baked; tile clip empty by design",
               len(rf) > 0 and len(bb) > 0 and fc["empty_by_design"] and len(g["features"]) > 3000,
               rendering="roads are painted into the terrain albedo (no coplanar geometry, no flicker); bridge and tunnel ways are not drawn at M0 (deck heights are not in the data; F-041)")


def removal_check():
    a, b = EVID / "export" / "geo_export.json", EVID / "export" / "geo_export_after_removal.json"
    if not b.exists():
        return not_run("after-removal export missing")
    A, B = J(a), J(b)
    rem = set(B["removed_bids"])
    ba = {x["bid"]: x for x in A["buildings"]}
    bb = {x["bid"]: x for x in B["buildings"]}
    gone = set(ba) - set(bb)
    changed = [k for k in bb if json.dumps(bb[k], sort_keys=True) != json.dumps(ba[k], sort_keys=True)]
    terrain_same = A["terrain_samples"] == B["terrain_samples"]
    return res({"requested": len(rem), "removed_total": len(gone), "others_changed": len(changed), "terrain_identical": terrain_same,
                "removal_seconds": B["stats"].get("removal_seconds")},
               "20 random buildings removed at runtime by bid; all others and the terrain identical in the export", rem <= gone and not changed and terrain_same)


def spec_check(tile, man, hs):
    nb = CACHE / "extra_tiles" / "L0" / "30_35" / "terrain.tif"
    seam = None
    if nb.exists():
        import rasterio
        with rasterio.open(tile_paths(tile) / "terrain.tif") as a, rasterio.open(nb) as b:
            seam = a.read(1)[-1].tobytes() == b.read(1)[0].tobytes()
    second = CACHE / "extra_tiles" / "L0" / "26_37" / "manifest.json"
    t = next(L for L in man["layers"] if L["layer"] == "terrain")
    return res({"samples": t["samples"], "spacing_m": t["spacing_m"], "origin": man["origin_baytm_m"], "origin_multiple_of_2048": all(o % 2048 == 0 for o in man["origin_baytm_m"]),
                "level_field": man.get("level"), "shape": list(hs.shape), "seam_with_L0_30_35_byte_identical": seam,
                "second_tile_L0_26_37_baked": second.exists() and not manifest_mod.validate(J(second))},
               "2049^2 pixel-is-point, float64 origin multiple of 2048 m, LOD level field, shared edge rows byte-identical, second tile bakes",
               t["samples"] == 2049 and hs.shape == (2049, 2049) and bool(seam) and second.exists())


def origin_check(E):
    p = EVID / "export" / "floating_origin_check.json"
    if not p.exists():
        return not_run("floating_origin_check.json missing")
    s = J(p)["shifts"]
    mx = max(max(x["max_anchor_delta_m"], x["max_terrain_delta_m"]) for x in s)
    pos = max(x["max_abs_scene_position_m"] for x in s + [{"max_abs_scene_position_m": E["stats"]["max_abs_scene_position_m"]}])
    return res({"max_delta_m": mx, "max_abs_scene_position_m": pos, "shifts": s}, "<= 1 mm; |scene position| <= 4096 m", mx <= 0.001 and pos <= 4096)


def budgets(E, man, tile):
    st = E["stats"]
    bake_s = json.loads((tile_paths(tile) / "build_info.json").read_text())["bake_seconds"]
    import subprocess
    files = subprocess.run(["git", "ls-files", "-s", "--", str(tile_paths(tile))], cwd=PROJECT, capture_output=True, text=True).stdout
    disk = sum((tile_paths(tile) / f["file"]).stat().st_size for f in man["layers"]) + (tile_paths(tile) / "manifest.json").stat().st_size
    views = J(EVID / "export" / "capture_views.json")["views"] if (EVID / "export" / "capture_views.json").exists() else []
    dc = max((v["draw_calls_visible_renderers"] for v in views), default=st["renderers"])
    tri = st["triangles.total"]
    ok = tri <= 1.5e6 and dc <= 1500 and disk <= 100e6 and bake_s <= 300 and st.get("time.editor_build_s", 0) <= 60
    m1 = 70
    return res({"triangles_tile": tri, "triangles_terrain": st.get("triangles.terrain"), "triangles_buildings": st.get("triangles.buildings"), "triangles_water": st.get("triangles.water"),
                "draw_calls_max_view": dc, "draw_call_target": 200, "renderers": st["renderers"], "memory_mb": r(st.get("memory.bytes", 0) / 1e6, 1),
                "committed_tile_mb": r(disk / 1e6, 2), "bake_seconds": bake_s, "editor_build_seconds": r(st.get("time.editor_build_s"), 2),
                "git_tracked_files": len(files.splitlines()),
                "extrapolation": {"M1_70_tiles": {"committed_gb": r(70 * disk / 1e9, 2), "bake_minutes": r(70 * bake_s / 60, 1)},
                                  "bay_4624_tiles": {"committed_gb_if_all_L0": r(4624 * disk / 1e9, 1), "bake_hours": r(4624 * bake_s / 3600, 1),
                                                     "note": "Q-027 default: 1 m only for urban/shoreline tiles, 2 m (L1) elsewhere; streaming loads ~9 tiles around the focus"}}},
               "<= 1.5 M triangles, <= 1,500 draw calls (target 200), <= 100 MB committed, bake <= 5 min, editor build <= 60 s (Assumed budgets)", ok)


def default_mitigation(tile, cfg, E):
    """Informational only (not applied): what a containment-based DataSF match would give for the default share."""
    o = vector.read_osm(CACHE / "osm" / "tiles" / f"{tile.id}.osm.pbf")
    datasf = vector.read_datasf(CACHE / "datasf" / f"ynuv-fyni__{tile.id}.geojson")
    tree = shapely.STRtree([d["geom"] for d in datasf])
    defaults = {b["bid"] for b in E["buildings"] if b["height_source"] == "default"}
    rescued = 0
    for rr in o["buildings"] + o["parts"]:
        if rr["oid"] not in defaults:
            continue
        g = vector.clean_polygonal(rr["geom"])
        for k in tree.query(g):
            d = datasf[int(k)]
            if g.intersection(d["geom"]).area >= 0.8 * g.area and d["hgt_median_m"] and d["peak_1st_m"] - d["gnd_min_m"] > 3:
                rescued += 1
                break
    n = len(E["buildings"])
    return {"result": "reported", "rule_in_use": "R-004 §1.4 (DataSF only if IoU > 0.6)", "default_now": len(defaults),
            "would_be_default_with_containment_80pct": len(defaults) - rescued, "share_now": r(len(defaults) / n), "share_with_mitigation": r((len(defaults) - rescued) / n),
            "proposal": "Q-NEW (T-003): allow a DataSF match when >= 80 % of the OSM footprint lies inside one DataSF footprint (row houses split in OSM); not applied without founder approval"}


def wading(hs, water, msl, tile):
    cell = 0.25 * (hs[:-1, :-1] + hs[1:, :-1] + hs[:-1, 1:] + hs[1:, 1:])
    depth = np.where(water, msl - cell, np.nan)
    d = depth[np.isfinite(depth)]
    bands = {"0-2": (0, 2), "2-5": (2, 5), "5-10": (5, 10), "10-20": (10, 20), ">20": (20, 1e9)}
    hist = {k: r(np.mean((d >= a) & (d < b)), 4) for k, (a, b) in bands.items()}
    k = np.nanargmax(depth)
    zr, xc = np.unravel_index(k, depth.shape)
    return {"result": "reported", "water_cells": int(d.size), "share_by_depth_m": hist, "max_depth_m": r(d.max(), 2),
            "deepest_baytm": [tile.x0 + xc + 0.5, tile.z0 + zr + 0.5], "median_depth_m": r(np.median(d), 2),
            "wadable_by_80m_mech_everywhere": bool(d.max() < 40), "plate": "evidence/T-003/plates/wading_depth.png"}


def main(argv=None):
    ap = std_args(argparse.ArgumentParser(description=__doc__.splitlines()[0]))
    ap.add_argument("cmd", choices=["prepare", "prepare_v11", "check"])
    ap.add_argument("--tile", default="L0_30_36")
    ap.add_argument("--export", default=str(EVID / "export" / "geo_export.json"))
    ap.add_argument("--out", default=None, help="report path (default evidence/T-003/m005_report.json)")
    a = ap.parse_args(argv)
    global REPORT_PATH
    if a.out:
        REPORT_PATH = Path(a.out)
    apply_std(a)
    t = parse_tile(a.tile)
    if a.cmd == "prepare":
        prepare(t)
    elif a.cmd == "prepare_v11":
        from kaiju_geo import m005_v11 as V
        man, hs, water, feats = load_tile(t)
        spec = v11_skyline_spec(t, feats, water, man["water_level_navd88_m"], V.osm_tags(t), J(GLOBAL_ROOT / "landmarks.json"))
        (EVID / "skyline_v11_spec.json").write_text(json.dumps(spec, indent=1) + "\n")
        print(f"[prepare_v11] {len(spec['views'])} views, change mask {len(spec['change_mask'])} structures: {[c['name'] or c['structure'] for c in spec['change_mask']][:12]}")
    else:
        check(t, a.export)
    return 0


if __name__ == "__main__":
    sys.exit(main())
