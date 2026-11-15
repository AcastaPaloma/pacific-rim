#!/usr/bin/env python
"""M-005@v2 (Bay scale) checker (T-106; research/R-100.md section 6, plans/M2.md Track 1). Run through `m005_check.py bay`.

Streams every baked geo_tile@v1 tile of the Bay once (workers read manifest, buildings, three edges of terrain.r16, landmarks layer; tiles
that touch a bathymetry box, the Bay polygon, a summit or a landmark row also read the full surface and the water mask) and reports
per sub-measure pass / fail / reported / not_run:

  M-005@v1.2 over every tile : default_height_share (per tile, per county, Bay-wide <= 2 % by area), terrain_seam, edge_vertex_gap,
                               bid_ownership, clipped_area, collider_overlap, landmarks (lid / ownership, sourced heights, cut continuity,
                               cable <= tower), unchanged thresholds (m005_check.V12_TH)
  M-005.1 landmark rows      : every row of landmarks_bay.json (30) plus the R-004 section 5 rows: (a) baked vs import <= 0.5 m,
                               (b) p95 <= 5 m / max <= 10 m (summits 15 m, cranes 50 m), (d) heights within max(2 m, 1 %)
  M-005.3 bathymetry         : R-004 section 6.3 boxes (gated), the six new R-100 spots (reported: no sourced depth), Bay-wide shallowness
  M-005.8 licence            : every source entry of every manifest carries the required fields; attribution coverage
  not_run (listed with owner): M-005.2 NGS and pipeline, M-005.3 pipeline, .4 KS, .5, .6, .7  (config/m005_bay.json "not_run")

Thresholds are the R-100 values (config/m005_bay.json, m005_check.V12_TH) and are never lowered: a failing sub-measure is a finding.
Counties are the OSM admin_level=6 relations of the cached NorCal PBF (ODbL, no download); a structure belongs to the county that
contains its point_on_surface."""
from __future__ import annotations

import csv
import gzip
import json
import math
import multiprocessing as mp
import os
import subprocess
import sys
import time
from collections import defaultdict
from concurrent.futures import ProcessPoolExecutor
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np  # noqa: E402
import shapely  # noqa: E402
from shapely import wkb as shp_wkb  # noqa: E402
from shapely.geometry import box, shape  # noqa: E402

from kaiju_geo import landmarks_bay as lb  # noqa: E402
from kaiju_geo.common import CACHE, GLOBAL_ROOT, PROJECT, TILES_ROOT, lonlat_to_baytm, load_config, parse_tile  # noqa: E402

V2 = "M-005@v2"
HB_MIN, HB_MAX, HB_STEP = -130.0, 10.0, 0.01
HB_N = int(round((HB_MAX - HB_MIN) / HB_STEP))
REQUIRED_SOURCE_FIELDS = ("product", "item", "url", "licence", "attribution", "fetched_utc", "source_sha256")
EVID_DEFAULT = PROJECT / "evidence" / "T-106"
OUT_DEFAULT = CACHE / "bake" / "tiles" / "v1"


def J(p):
    return json.loads(Path(p).read_text())


def r(x, n=4):
    return None if x is None or (isinstance(x, float) and not math.isfinite(x)) else round(float(x), n)


def row(check, result, scope="bay", **kw):
    d = {"metric_version": V2, "check": check, "scope": scope, "result": result}
    d.update(kw)
    return d


def th_v12():
    import m005_check as M
    return M.V12_TH


# ---------------------------------------------------------------- reference data
def counties_geojson() -> Path:
    """OSM admin_level=6 relations of the cached NorCal PBF as GeoJSON (lon/lat), built once with osmium (offline)."""
    out = CACHE / "m005_bay" / "counties_osm_adm6.geojson"
    if out.exists():
        return out
    out.parent.mkdir(parents=True, exist_ok=True)
    pbf = CACHE / "osm" / "norcal-latest.osm.pbf"
    osmium = str(Path(sys.executable).parent / "osmium")
    t1, t2 = out.with_name("adm6.osm.pbf"), out.with_name("adm6b.osm.pbf")
    subprocess.run([osmium, "tags-filter", str(pbf), "r/admin_level=6", "-o", str(t1), "--overwrite"], check=True)
    subprocess.run([osmium, "tags-filter", str(t1), "r/boundary=administrative", "-o", str(t2), "--overwrite"], check=True)
    tmp = out.with_suffix(".part")
    subprocess.run([osmium, "export", str(t2), "--geometry-types=polygon", "-f", "geojson", "-o", str(tmp), "--overwrite"], check=True)
    tmp.replace(out)
    t1.unlink()
    t2.unlink()
    return out


def load_counties(world_lonlat=None):
    """-> [(name, shapely geometry in BayTM)] for the counties that meet the Bay world box (all of them when no box is given)."""
    from kaiju_geo import vector
    out = []
    for f in J(counties_geojson())["features"]:
        p = f["properties"]
        name = p.get("name") or p.get("ref")
        g = shape(f["geometry"])
        if world_lonlat is not None:
            w, s, e, n = world_lonlat
            if not g.intersects(box(w, s, e, n)):
                continue
        out.append((name, vector._to_baytm_geom(g)))
    return sorted(out)


def ll_poly(pts_lonlat, dens=12):
    """Polygon (lon/lat vertices, edges densified) -> BayTM vertex list."""
    ring = list(pts_lonlat) + [pts_lonlat[0]]
    lon, lat = [], []
    for (a, b), (c, d) in zip(ring[:-1], ring[1:]):
        for k in range(dens):
            lon.append(a + (c - a) * k / dens)
            lat.append(b + (d - b) * k / dens)
    x, z = lonlat_to_baytm(np.array(lon), np.array(lat))
    return [(float(a), float(b)) for a, b in zip(x, z)]


def box_poly(b):
    w, s, e, n = b
    return ll_poly([(w, s), (e, s), (e, n), (w, n)])


def poly_bbox(P):
    a = np.asarray(P, float)
    return float(a[:, 0].min()), float(a[:, 1].min()), float(a[:, 0].max()), float(a[:, 1].max())


def hist_quantile(counts, q):
    """Quantile (0..1) of a value histogram on [HB_MIN, HB_MAX) with step HB_STEP (resolution 0.01 m)."""
    c = np.cumsum(counts)
    if c[-1] == 0:
        return None
    k = int(np.searchsorted(c, q * c[-1], side="left"))
    return HB_MIN + (k + 0.5) * HB_STEP


def summit_elev(basis: str):
    import re
    m = re.search(r"summit ([0-9.]+) m NAVD88", basis or "")
    return float(m.group(1)) if m else None


# ---------------------------------------------------------------- worker side (module level: picklable under spawn)
_W: dict = {}


def _init(root, counties_wkb, regions, bay_poly):
    for k in ("OMP_NUM_THREADS", "OPENBLAS_NUM_THREADS", "VECLIB_MAXIMUM_THREADS", "MKL_NUM_THREADS"):
        os.environ[k] = "1"
    _W["root"] = Path(root)
    cs = []
    for name, w in counties_wkb:
        g = shp_wkb.loads(w)
        shapely.prepare(g)
        cs.append((name, g))
    _W["counties"] = cs
    _W["regions"] = regions
    _W["bay_poly"] = bay_poly


def _crosses(b, bounds):
    return b[0] < bounds[0] or b[1] < bounds[1] or b[2] > bounds[2] or b[3] > bounds[3]


def _structures(feats):
    """One unit per structure (m005_check.v12_structures convention) with its owner point: root outline area, source of the tallest element."""
    groups = {}
    for f in feats:
        groups.setdefault(f["properties"].get("part_of") or f["id"], []).append(f)
    rows = []
    for sid, els in groups.items():
        root = next((e for e in els if e["id"] == sid), els[0])
        if root["properties"].get("excluded"):
            continue
        top = max(els, key=lambda e: (e["properties"].get("base_navd88_m") or 0.0) + (e["properties"].get("height_m") or 0.0))
        g = shape(root["geometry"])
        p = g.point_on_surface()
        rows.append((sid, g.area, top["properties"].get("height_source"), p.x, p.y))
    return rows


def _county_of(xs, zs):
    names = np.full(len(xs), "", dtype=object)
    todo = np.ones(len(xs), bool)
    for name, g in _W["counties"]:
        if not todo.any():
            break
        m = shapely.contains_xy(g, xs, zs) & todo
        names[m] = name
        todo &= ~m
    names[names == ""] = "(outside OSM county polygons)"
    return names


def _read_surface(d, man):
    lay = next(l for l in man["layers"] if l["layer"] == "terrain_r16")
    n = next(l for l in man["layers"] if l["layer"] == "terrain")["samples"]
    raw = np.fromfile(d / "terrain.r16", dtype="<u2")
    if raw.size != n * n:
        raise ValueError(f"terrain.r16 has {raw.size} samples, expected {n * n}")
    return lay["h_min_m"] + raw.reshape(n, n).astype(np.float64) * (lay["h_max_m"] - lay["h_min_m"]) / 65535.0


def _water(d):
    from PIL import Image
    return np.flipud(np.array(Image.open(d / "water.png")) >= 128)   # row 0 = south, cells


def _cell_h(h):
    return 0.25 * (h[:-1, :-1] + h[1:, :-1] + h[:-1, 1:] + h[1:, 1:])


def _window(t, bb, ncell):
    s = t.spacing
    c0 = max(0, int(math.floor((bb[0] - t.x0) / s)))
    c1 = min(ncell, int(math.ceil((bb[2] - t.x0) / s)))
    r0 = max(0, int(math.floor((bb[1] - t.z0) / s)))
    r1 = min(ncell, int(math.ceil((bb[3] - t.z0) / s)))
    return r0, r1, c0, c1


def _cells_inside(t, P, win):
    r0, r1, c0, c1 = win
    s = t.spacing
    xs = t.x0 + (np.arange(c0, c1) + 0.5) * s
    zs = t.z0 + (np.arange(r0, r1) + 0.5) * s
    X, Z = np.meshgrid(xs, zs)
    return lb.point_in_poly(P, X, Z)


def scan_tile(args):
    """Phase 1: one tile -> compact summary (no geometry except edge-crossing footprints)."""
    tid, task = args
    t = parse_tile(tid)
    d = _W["root"] / t.folder
    out = {"tile": tid}
    try:
        man = J(d / "manifest.json")
        feats = J(d / "buildings.geojson")["features"]
        lay = {l["layer"]: l for l in man["layers"]}
        bounds = tuple(man["bounds_baytm"])
        out["bounds"] = bounds
        out["water_level"] = man["water_level_navd88_m"]
        out["terrain"] = {k: lay["terrain"].get(k) for k in ("h_min_m", "h_max_m", "placeholder_nodes", "placeholder_nodes_sea", "placeholder_nodes_land")}
        out["water_fraction"] = lay["water"]["water_fraction"]
        out["height_source_counts"] = lay["buildings"].get("height_source_counts", {})
        # ---- licence / credits
        miss, lic, att = [], set(), set()
        for L in man["layers"]:
            for s in L.get("sources", []):
                m = [k for k in REQUIRED_SOURCE_FIELDS if not s.get(k)]
                if m:
                    miss.append([L["layer"], s.get("product"), m])
                lic.add(s.get("licence"))
                att.add(s.get("attribution"))
        out["licence"] = {"missing": miss[:4], "n_missing": len(miss), "licences": sorted(x for x in lic if x), "attributions": sorted(x for x in att if x)}
        # ---- terrain edges (memmap; row 0 = south, col 0 = west)
        n = lay["terrain"]["samples"]
        rl = lay["terrain_r16"]
        raw = np.memmap(d / "terrain.r16", dtype="<u2", mode="r", shape=(n, n))
        sc = (rl["h_max_m"] - rl["h_min_m"]) / 65535.0
        dec = lambda a: rl["h_min_m"] + np.asarray(a, dtype=np.float64) * sc  # noqa: E731
        out["edges"] = {"w": dec(raw[:, 0]), "e": dec(raw[:, -1]), "s": dec(raw[0, :]), "n": dec(raw[-1, :])}
        del raw
        # ---- structures, counties, ownership
        st = _structures(feats)
        out["structures"] = len(st)
        a = sum(s[1] for s in st)
        da = sum(s[1] for s in st if s[2] == "default")
        out["area"], out["default_area"] = a, da
        by = defaultdict(lambda: [0.0, 0.0, 0])
        bysrc = defaultdict(lambda: defaultdict(float))
        if st:
            cn = _county_of(np.array([s[3] for s in st]), np.array([s[4] for s in st]))
            for s, c in zip(st, cn):
                v = by[c]
                v[0] += s[1]
                v[1] += s[1] if s[2] == "default" else 0.0
                v[2] += 1
                bysrc[c][str(s[2])] += s[1]
        out["by_county"] = {k: v for k, v in by.items()}
        out["src_by_county"] = {k: dict(v) for k, v in bysrc.items()}
        bids, mis = [], []
        cross, ccross = [], []
        for f in feats:
            p = f["properties"]
            bids.append(p["bid"])
            g = None
            if not p.get("part_of"):
                g = shape(f["geometry"])
                q = g.point_on_surface()
                if not t.contains(q.x, q.y):
                    mis.append(p["bid"])
            if p.get("excluded"):
                continue
            if g is None:
                g = shape(f["geometry"])
            if not p.get("part_of") and _crosses(g.bounds, bounds):
                cross.append((tid, p["bid"], g.wkb))
            geom = p.get("remainder_geometry") if p.get("has_parts") else f["geometry"]
            if geom:
                cg = shape(geom)
                if _crosses(cg.bounds, bounds):
                    ccross.append((tid, p.get("part_of") or f["id"], cg.wkb))
        out["bids"], out["misowned"], out["cross_roots"], out["cross_colliders"] = bids, mis, cross, ccross
        # ---- landmarks layer
        lf = []
        if "landmarks" in lay:
            for f in J(d / lay["landmarks"]["file"])["features"]:
                p = f["properties"]
                g = shape(f["geometry"])
                c = g.centroid
                lf.append({"lid": p["lid"], "bid": p["bid"], "kind": p["kind"], "part_of": p.get("part_of"), "cx": c.x, "cz": c.y,
                           "base": p.get("base_navd88_m"), "top": p.get("top_navd88_m"), "gtype": g.geom_type})
        out["landmarks"] = lf
        # ---- structure info for landmark rows
        info = {}
        for bid in task.get("bld", ()):
            root = next((f for f in feats if f["id"] == bid), None)
            if root is None:
                continue
            kids = [f for f in feats if f["properties"].get("part_of") == bid]
            tops = [(root["properties"]["base_navd88_m"] + root["properties"]["height_m"])] + [
                f["properties"]["base_navd88_m"] + f["properties"]["height_m"] for f in kids if not f["properties"].get("excluded")]
            tops += [x["top"] for x in lf if x.get("part_of") == bid and x["top"] is not None]
            c = shape(root["geometry"]).centroid
            info[bid] = {"cx": c.x, "cz": c.y, "base": root["properties"]["base_navd88_m"], "top": max(tops), "excluded": root["properties"].get("excluded")}
        out["bld"] = info
        # ---- full-surface sampling (bathymetry boxes, Bay polygon, summits)
        if task.get("regions") or task.get("bay") or task.get("summits"):
            h = _read_surface(d, man)
            ncell = h.shape[0] - 1
            reg = {}
            if task.get("regions") or task.get("bay"):
                wet = _water(d)
                ch = _cell_h(h)
                for name in task.get("regions", ()):
                    P = _W["regions"][name]
                    win = _window(t, poly_bbox(P), ncell)
                    if win[0] >= win[1] or win[2] >= win[3]:
                        continue
                    ins = _cells_inside(t, P, win) & wet[win[0]:win[1], win[2]:win[3]]
                    v = ch[win[0]:win[1], win[2]:win[3]][ins]
                    if v.size:
                        idx = np.clip(((v - HB_MIN) / HB_STEP).astype(np.int64), 0, HB_N - 1)
                        reg[name] = {"hist": np.bincount(idx, minlength=HB_N).astype(np.int64), "min": float(v.min()), "n": int(v.size)}
                out["regions"] = reg
                if task.get("bay"):
                    P = _W["bay_poly"]
                    win = _window(t, poly_bbox(P), ncell)
                    ins = _cells_inside(t, P, win)
                    w_ = wet[win[0]:win[1], win[2]:win[3]] & ins
                    depth = man["water_level_navd88_m"] - ch[win[0]:win[1], win[2]:win[3]][w_]
                    chp = ch[win[0]:win[1], win[2]:win[3]]
                    below = ins & (chp < man["water_level_navd88_m"])
                    out["bay"] = {"cells_in_polygon": int(ins.sum()), "wet": int(w_.sum()), "wet_below_limit": int((depth < task["shallow_below_m"]).sum()),
                                  "surface_below_msl": int(below.sum()), "surface_below_msl_shallow": int((below & (chp > man["water_level_navd88_m"] - task["shallow_below_m"])).sum())}
            sm = {}
            for s in task.get("summits", ()):
                ix, iz = (s["x"] - t.x0) / t.spacing, (s["z"] - t.z0) / t.spacing
                R = int(math.ceil(s["tol"] / t.spacing)) + 1
                i0, i1 = max(0, int(ix) - R), min(h.shape[1], int(ix) + R + 1)
                j0, j1 = max(0, int(iz) - R), min(h.shape[0], int(iz) + R + 1)
                gx, gz = np.meshgrid(t.x0 + np.arange(i0, i1) * t.spacing, t.z0 + np.arange(j0, j1) * t.spacing)
                dm = np.hypot(gx - s["x"], gz - s["z"]) <= s["tol"]
                sub = h[j0:j1, i0:i1]
                if dm.any():
                    k = np.argmax(np.where(dm, sub, -np.inf))
                    sm[str(s["n"])] = {"max_navd88_m": float(sub.ravel()[k]), "at": [float(gx.ravel()[k]), float(gz.ravel()[k])]}
            out["summits"] = sm
    except Exception as e:  # noqa: BLE001  (a tile that cannot be read is an input error, never a pass)
        out["error"] = f"{type(e).__name__}: {e}"[:300]
    return out


def overlap_tile(args):
    """Phase 2: edge-crossing colliders of other owners against the interior colliders of this tile."""
    tid, foreign, th = args
    t = parse_tile(tid)
    d = _W["root"] / t.folder
    feats = J(d / "buildings.geojson")["features"]
    bounds = t.bounds
    items = []
    for f in feats:
        p = f["properties"]
        if p.get("excluded"):
            continue
        geom = p.get("remainder_geometry") if p.get("has_parts") else f["geometry"]
        if not geom:
            continue
        g = shape(geom)
        if _crosses(g.bounds, bounds):
            continue
        items.append((p.get("part_of") or f["id"], g))
    res = []
    if items:
        tree = shapely.STRtree([g for _, g in items])
        for owner, sid, w in foreign:
            g = shp_wkb.loads(w)
            for m in tree.query(g):
                m = int(m)
                if items[m][0] == sid:
                    continue
                a = g.intersection(items[m][1]).area
                if a > th:
                    res.append({"tiles": [owner, tid], "bids": [sid, items[m][0]], "area_m2": round(a, 3)})
    return {"tile": tid, "overlaps": res, "tested": len(foreign)}


# ---------------------------------------------------------------- aggregation
def seam_rows(R, th):
    by = {x["tile"]: x for x in R}
    pos = {(parse_tile(k).i, parse_tile(k).j): k for k in by}
    rows, allE, gaps, edges, bad = [], [], [], 0, []
    for (i, j), a in sorted(pos.items()):
        for dx, dz in ((1, 0), (0, 1)):
            b = pos.get((i + dx, j + dz))
            if b is None:
                continue
            A, B = by[a], by[b]
            ha, hb = (A["edges"]["e"], B["edges"]["w"]) if dx else (A["edges"]["n"], B["edges"]["s"])
            E = np.abs(ha - hb)
            ax0, az0, ax1, az1 = A["bounds"]
            bx0, bz0, bx1, bz1 = B["bounds"]
            gap = math.hypot(ax1 - bx0, max(abs(az0 - bz0), abs(az1 - bz1))) if dx else math.hypot(max(abs(ax0 - bx0), abs(ax1 - bx1)), az1 - bz0)
            p99, mx = float(np.percentile(E, 99)), float(E.max())
            allE.append(E)
            gaps.append(gap)
            edges += 1
            if p99 > th["seam_terrain_p99_E_m"] or mx > th["seam_terrain_max_E_m"] or gap > th["edge_vertex_gap_m"]:
                bad.append({"edge": f"{a}|{b}", "p99_dE_m": r(p99), "max_dE_m": r(mx), "gap_m": r(gap, 6),
                            "placeholder_nodes": [(A.get("terrain") or {}).get("placeholder_nodes") or 0, (B.get("terrain") or {}).get("placeholder_nodes") or 0]})
    if not edges:
        return [row("terrain_seam_all_edges", "error", reason="no shared tile edge among the tiles given")]
    E = np.concatenate(allE)
    p99, mx = float(np.percentile(E, 99)), float(E.max())
    ok_s = p99 <= th["seam_terrain_p99_E_m"] and mx <= th["seam_terrain_max_E_m"]
    nbad = len([x for x in bad if x["p99_dE_m"] > th["seam_terrain_p99_E_m"] or x["max_dE_m"] > th["seam_terrain_max_E_m"]])
    return [row("terrain_seam_all_edges", "pass" if ok_s and not nbad else "fail", value={"p99_dE_m": r(p99), "max_dE_m": r(mx), "edges_over_threshold": nbad},
                threshold={"p99": th["seam_terrain_p99_E_m"], "max": th["seam_terrain_max_E_m"]}, edges=edges, nodes=int(E.size),
                surface="terrain.r16 (built surface), shared edge nodes, every edge of the tile set; the pooled p99/max and each edge must pass",
                failing_edges=[x for x in bad if x["p99_dE_m"] > th["seam_terrain_p99_E_m"] or x["max_dE_m"] > th["seam_terrain_max_E_m"]][:25]),
            row("edge_vertex_gap_all_edges", "pass" if max(gaps) <= th["edge_vertex_gap_m"] else "fail", value=r(max(gaps), 6),
                threshold=th["edge_vertex_gap_m"], edges=edges)]


def share_rows(R, th, th2):
    tot_a = sum(x["area"] for x in R)
    tot_d = sum(x["default_area"] for x in R)
    rows = []
    if tot_a <= 0:
        return [row("default_height_share", "error", reason="no building area in any tile")], []
    sh = tot_d / tot_a
    rows.append(row("default_height_share_bay", "pass" if sh <= th2["default_height_share_bay"] else "fail", value=r(sh, 5),
                    threshold=th2["default_height_share_bay"], building_area_m2=r(tot_a, 1), default_area_m2=r(tot_d, 1),
                    structures=sum(x["structures"] for x in R), unit="structure root-outline area, excluded structures dropped"))
    cnt = defaultdict(lambda: [0.0, 0.0, 0])
    for x in R:
        for c, v in x["by_county"].items():
            for k in range(3):
                cnt[c][k] += v[k]
    srcs = defaultdict(lambda: defaultdict(float))
    for x in R:
        for c, v in (x.get("src_by_county") or {}).items():
            for k, a_ in v.items():
                srcs[c][k] += a_
    crow = []
    for c, (a, d, n) in sorted(cnt.items()):
        gated = a >= th2["county_min_area_m2"]
        s = d / a if a > 0 else None
        crow.append({"county": c, "building_area_m2": r(a, 1), "default_area_m2": r(d, 1), "structures": n, "share": r(s, 5), "gated": gated,
                     "area_share_by_height_source": {k: r(v / a, 4) for k, v in sorted(srcs[c].items(), key=lambda kv: -kv[1])} if a > 0 else {},
                     "result": ("pass" if s <= th2["default_height_share_county"] else "fail") if gated else "reported"})
    failing = [c["county"] for c in crow if c["result"] == "fail"]
    rows.append(row("default_height_share_county", "fail" if failing else "pass", value={"counties_over": failing},
                    threshold=th2["default_height_share_county"], counties=crow,
                    rule=f"every county with >= {th2['county_min_area_m2']:.0f} m2 of structure area is gated at <= 2 %; the rest are reported"))
    gated_t, bad = 0, []
    tiles = []
    for x in R:
        g = x["area"] >= th["per_tile_min_area_m2"]
        s = x["default_area"] / x["area"] if x["area"] > 0 else None
        tiles.append([x["tile"], x["structures"], r(x["area"], 1), r(x["default_area"], 1), r(s, 5)])
        if g:
            gated_t += 1
            if s > th["per_tile_share"]:
                bad.append({"tile": x["tile"], "share": r(s, 4), "area_m2": r(x["area"], 1)})
    rows.append(row("default_height_share_tile", "fail" if bad else "pass", value={"tiles_gated": gated_t, "tiles_over": len(bad)},
                    threshold=th["per_tile_share"], failing=sorted(bad, key=lambda b: -b["share"])[:40],
                    note=f"tiles with < {th['per_tile_min_area_m2']:.0f} m2 of buildings are reported, not gated (v1.2)"))
    return rows, tiles


def pair_overlaps(items, th):
    """Edge-crossing x edge-crossing colliders of different owners (the third-tile case phase 2 cannot see)."""
    if not items:
        return []
    geoms = [shp_wkb.loads(w) for _, _, w in items]
    tree = shapely.STRtree(geoms)
    out = []
    for k, g in enumerate(geoms):
        for m in tree.query(g):
            m = int(m)
            if m <= k or items[m][0] == items[k][0] or items[m][1] == items[k][1]:
                continue
            a = g.intersection(geoms[m]).area
            if a > th:
                out.append({"tiles": [items[k][0], items[m][0]], "bids": [items[k][1], items[m][1]], "area_m2": round(a, 3)})
    return out


def clipped_area_worst(items):
    worst, n = 0.0, 0
    for owner, bid, w in items:
        g = shp_wkb.loads(w)
        x0, z0, x1, z1 = g.bounds
        hit = []
        for i in range(int(math.floor(x0 / 2048.0)), int(math.floor(x1 / 2048.0)) + 1):
            for j in range(int(math.floor(z0 / 2048.0)), int(math.floor(z1 / 2048.0)) + 1):
                b = box(i * 2048.0, j * 2048.0, (i + 1) * 2048.0, (j + 1) * 2048.0)
                if g.intersects(b):
                    hit.append(b)
        if len(hit) < 2:
            continue
        n += 1
        inside = g.intersection(shapely.union_all(hit)).area
        if inside > 0:
            worst = max(worst, abs(sum(g.intersection(b).area for b in hit) - inside) / inside)
    return worst, n


def bathy_rows(R, cfg):
    rows = []
    for b in cfg["bathymetry_boxes"]:
        hist = np.zeros(HB_N, np.int64)
        mn, n = None, 0
        tiles = []
        for x in R:
            g = (x.get("regions") or {}).get(b["name"])
            if g:
                hist += g["hist"]
                n += g["n"]
                mn = g["min"] if mn is None else min(mn, g["min"])
                tiles.append(x["tile"])
        if n == 0:
            rows.append(row("bathymetry_" + b["name"], "error", reason="no wet cell of the box in the baked tiles (tile missing or box on land)", box=b["box"]))
            continue
        stats = {"min_navd88_m": r(mn, 3), "p5_navd88_m": r(hist_quantile(hist, 0.05), 2), "median_navd88_m": r(hist_quantile(hist, 0.5), 2),
                 "p95_navd88_m": r(hist_quantile(hist, 0.95), 2), "wet_cells": n}
        if not b["gated"]:
            rows.append(row("bathymetry_" + b["name"], "reported", value=stats, box=b["box"], tiles=tiles, reference=b["source"],
                            note="no sourced reference depth: measured statistics only, never a pass (R-100 section 6: USACE project depths to source)"))
            continue
        rl, ok = b["rule"], True
        if "min_navd88_m" in rl:
            ok &= rl["min_navd88_m"][0] <= mn <= rl["min_navd88_m"][1]
        if "p5_max_navd88_m" in rl:
            ok &= stats["p5_navd88_m"] <= rl["p5_max_navd88_m"]
        if "median_navd88_m" in rl:
            ok &= rl["median_navd88_m"][0] <= stats["median_navd88_m"] <= rl["median_navd88_m"][1]
        rows.append(row("bathymetry_" + b["name"], "pass" if ok else "fail", value=stats, threshold=rl, box=b["box"], tiles=tiles, reference=b["source"],
                        note="seabed = baked surface at wet cell centres (OSM water AND below MSL), NAVD88; histogram resolution 0.01 m"))
    return rows


def shallow_row(R, cfg):
    th = cfg["thresholds"]
    tot = sum(x["bay"]["wet"] for x in R if x.get("bay"))
    shal = sum(x["bay"]["wet_below_limit"] for x in R if x.get("bay"))
    if tot == 0:
        return row("bathymetry_bay_wide_shallowness", "error", reason="no wet cell in the Bay polygon")
    f = shal / tot
    ok = abs(f - th["shallow_share"]) <= th["shallow_tol"]
    sb = sum(x["bay"].get("surface_below_msl", 0) for x in R if x.get("bay"))
    sbs = sum(x["bay"].get("surface_below_msl_shallow", 0) for x in R if x.get("bay"))
    return row("bathymetry_bay_wide_shallowness", "pass" if ok else "fail", value={"share_below_5m": r(f, 4), "wet_area_km2": r(tot / 1e6, 1),
               "tiles": sum(1 for x in R if x.get("bay")),
               "supplementary_not_gated": {"basis": "every baked cell below MSL inside the polygon, wet mask ignored (the R-004 CUDEM-overview basis, diked cells included)",
                                           "share_below_5m": r(sbs / sb, 4) if sb else None, "area_km2": r(sb / 1e6, 1)}}, threshold=f"{th['shallow_share']} +- {th['shallow_tol']} of wet area < {th['shallow_below_m']} m below MSL",
               note="wet = OSM water AND baked surface below MSL, cell centres inside the R-004 6.3 Bay polygon; the R-004 reference (66.4 %) counted diked cells below MSL")


def licence_rows(R):
    miss = sum(x["licence"]["n_missing"] for x in R)
    att = sorted({a for x in R for a in x["licence"]["attributions"]})
    lic = sorted({a for x in R for a in x["licence"]["licences"]})
    low = " ".join(att).lower()
    have = {"OSM": "openstreetmap" in low, "USGS": "usgs" in low or "u.s. geological" in low, "NOAA": "noaa" in low or "ncei" in low, "DataSF": "datasf" in low or "city and county of san francisco" in low}
    sf_missing = [x["tile"] for x in R if x["height_source_counts"].get("datasf") and not any("datasf" in a.lower() or "san francisco" in a.lower() for a in x["licence"]["attributions"])]
    ok = miss == 0 and all(have.values()) and not sf_missing
    return row("licence_credits", "pass" if ok else "fail", value={"source_entries_missing_fields": miss, "credits_present": have, "licences": lic,
               "tiles_with_datasf_heights_but_no_datasf_credit": sf_missing[:5]}, threshold="0 missing fields; credits include OSM, USGS, NOAA, DataSF",
               examples=[x["licence"]["missing"] for x in R if x["licence"]["n_missing"]][:3], attributions=att)


def landmark_m0051(R, doc, ref, cfg):
    """M-005.1 rows -> (list of per-row dicts, summary rows)."""
    th = cfg["thresholds"]
    byt = {x["tile"]: x for x in R}
    lmf = {f["lid"]: f for x in R for f in x["landmarks"]}
    bld = {}
    for x in R:
        bld.update(x["bld"])
    rows = []

    def tile_of(x, z):
        return f"L0_{int(math.floor(x / 2048.0)):02d}_{int(math.floor(z / 2048.0)):02d}"

    for c in lb.check_rows(doc):
        d = {"id": c["slug"], "name": c["name"], "kind": c["kind"], "tile": tile_of(*c["pos_baytm"]), "tol_pos_m": c["tol_pos_m"], "status_registry": c["status"]}
        found = None
        if c["kind"] == "tower":
            f = lmf.get(f"lm:{c['slug']}:t")
            if f:
                found = {"pos": [f["cx"], f["cz"]], "top": f["top"], "height": f["top"]}
        elif c["kind"] == "crane_cluster":
            fs = [lmf.get(f"lm:port_of_oakland_crane_{i}:p") for i in c["crane_ids"]]
            got = [f for f in fs if f]
            if got:
                found = {"pos": [float(np.mean([f["cx"] for f in got])), float(np.mean([f["cz"] for f in got]))], "n": len(got), "expected_n": len(fs)}
        else:
            b = bld.get(f"osm:{c['osm_id']}") if c.get("osm_id") else None
            if b and not b["excluded"]:
                found = {"pos": [b["cx"], b["cz"]], "top": b["top"], "height": b["top"] - b["base"]}
        if found is None:
            d.update(present=False, a_vs_import_m=None, result="fail", reason="not found in the baked tiles")
            rows.append(d)
            continue
        ref_pos = c["pos_baytm"]
        if c.get("footprint_baytm"):
            ref_pos = [float(v) for v in np.array(shape({"type": "Polygon", "coordinates": [c["footprint_baytm"]]}).centroid.coords[0])]
        dist = math.hypot(found["pos"][0] - ref_pos[0], found["pos"][1] - ref_pos[1])
        d.update(present=True, baked_pos=[r(found["pos"][0], 3), r(found["pos"][1], 3)], a_vs_import_m=r(dist, 3))
        if c["kind"] == "crane_cluster":
            d["cranes_found"] = f"{found['n']}/{found['expected_n']}"
            d["a_ok"] = dist <= th["crane_pos_m"] and found["n"] == found["expected_n"]
        else:
            d["a_ok"] = dist <= (th["landmark_a_vs_import_m"] if c.get("footprint_baytm") or c["kind"] == "tower" else c["tol_pos_m"])
        if c["height_ref_m"] is not None and c["height_tol_m"] is not None and "height" in found:
            dh = found["height"] - c["height_ref_m"] if c["kind"] != "tower" else found["top"] - c["height_ref_m"]
            d.update(height_ref_m=c["height_ref_m"], height_baked_m=r(found["height"] if c["kind"] != "tower" else found["top"], 3),
                     height_delta_m=r(dh, 3), height_tol_m=c["height_tol_m"], height_ok=abs(dh) <= c["height_tol_m"])
        elif "height" in found:
            d.update(height_baked_m=r(found["height"], 3), height_ref_m=c["height_ref_m"], note="height reported only (registry has no gated height)")
        d["result"] = "pass" if d["a_ok"] and d.get("height_ok", True) else "fail"
        rows.append(d)
    # R-004 section 5 rows (independent reference positions)
    for l in ref["landmarks"]:
        d = {"id": f"R004_{l['n']}", "name": l["name"], "kind": "r004", "tile": l["tile"], "tol_pos_m": l["pos_tol_m"], "gate_height": l["gate_height"]}
        refxz = (l["baytm_x"], l["baytm_z"])
        pos = None
        if l["osm"] and l["osm"].startswith("osm:n"):
            s = byt.get(l["tile"], {}).get("summits", {}).get(str(l["n"]))
            el = summit_elev(l["height_basis"])
            if s and el is not None:
                dv = s["max_navd88_m"] - el
                d.update(present=True, kind="summit", summit_max_navd88_m=r(s["max_navd88_m"], 2), summit_ref_navd88_m=el, vertical_delta_m=r(dv, 2),
                         b_vs_reference_m=0.0, result="pass" if abs(dv) <= th["summit_vertical_m"] else "fail", tol_vertical_m=th["summit_vertical_m"])
            else:
                d.update(present=False, result="fail", reason="summit surface not sampled")
            rows.append(d)
            continue
        if l["osm"]:
            b = bld.get(l["osm"])
            if b and not b["excluded"]:
                pos = (b["cx"], b["cz"])
                hb = b["top"] - b["base"]
        else:   # Golden Gate towers: the landmark prisms
            slug = "ggb_tower_s" if l["n"] == 1 else "ggb_tower_n"
            fs = [f for f in lmf.values() if f["lid"].startswith(f"lm:{slug}:")]
            if fs:
                pos = (float(np.mean([f["cx"] for f in fs])), float(np.mean([f["cz"] for f in fs])))
                hb = max(f["top"] for f in fs) - min(f["base"] for f in fs)
        if pos is None:
            d.update(present=False, result="fail", reason="not found in the baked tiles")
            rows.append(d)
            continue
        dist = math.hypot(pos[0] - refxz[0], pos[1] - refxz[1])
        d.update(present=True, b_vs_reference_m=r(dist, 3), baked_height_m=r(hb, 3), height_ref_m=l["height_ref_m"])
        ok = dist <= l["pos_tol_m"]
        if l["gate_height"] and l["height_ref_m"] is not None:
            tol = max(2.0, 0.01 * l["height_ref_m"])
            d.update(height_delta_m=r(hb - l["height_ref_m"], 3), height_tol_m=tol, height_ok=abs(hb - l["height_ref_m"]) <= tol)
            ok &= d["height_ok"]
        d["result"] = "pass" if ok else "fail"
        rows.append(d)
    # summary rows
    a = [x["a_vs_import_m"] for x in rows if x.get("a_vs_import_m") is not None and x["kind"] not in ("crane_cluster",)]
    miss = [x["id"] for x in rows if not x.get("present", True)]
    bpos = [x["a_vs_import_m"] for x in rows if x["kind"] in ("tower", "building", "alcatraz", "airport_tower") and x.get("a_vs_import_m") is not None] + \
           [x["b_vs_reference_m"] for x in rows if x["kind"] == "r004" and x.get("b_vs_reference_m") is not None]
    summ = [
        row("M-005.1_present", "pass" if not miss else "fail", value={"rows": len(rows), "missing": miss}, threshold="every row found in the baked tiles"),
        row("M-005.1_a_vs_import", "pass" if a and max(a) <= th["landmark_a_vs_import_m"] and not [x for x in rows if x["kind"] != "crane_cluster" and not x.get("a_ok", True)] else "fail",
            value={"max_m": r(max(a), 3) if a else None, "rows": len(a), "over": [x["id"] for x in rows if x.get("a_ok") is False and x["kind"] != "crane_cluster"]},
            threshold=f"<= {th['landmark_a_vs_import_m']} m vs import (airport tower / R-004 position rows: <= 5 m)",
            note="Bay rows: baked prism / structure centroid vs the T-104 OSM position; airport tower and Alcatraz use the 5 m position tolerance"),
        row("M-005.1_b_vs_reference", "pass" if bpos and float(np.percentile(bpos, 95)) <= th["landmark_b_p95_m"] and max(bpos) <= th["landmark_b_max_m"] else "fail",
            value={"p95_m": r(float(np.percentile(bpos, 95)), 3) if bpos else None, "max_m": r(max(bpos), 3) if bpos else None, "rows": len(bpos)},
            threshold=f"p95 <= {th['landmark_b_p95_m']} m, max <= {th['landmark_b_max_m']} m (summits {th['summit_pos_m']} m, cranes {th['crane_pos_m']} m, own rows)"),
        row("M-005.1_cranes", "pass" if all(x.get("a_ok") for x in rows if x["kind"] == "crane_cluster") and any(x["kind"] == "crane_cluster" for x in rows) else "fail",
            value=[{"id": x["id"], "d_m": x.get("a_vs_import_m"), "found": x.get("cranes_found")} for x in rows if x["kind"] == "crane_cluster"], threshold=f"cluster centroid <= {th['crane_pos_m']} m, every crane present"),
        row("M-005.1_summits", "pass" if all(x["result"] == "pass" for x in rows if x["kind"] == "summit") and any(x["kind"] == "summit" for x in rows) else "fail",
            value=[{"id": x["id"], "dz_m": x.get("vertical_delta_m")} for x in rows if x["kind"] == "summit"], threshold=f"|baked max within {th['summit_pos_m']} m - summit elevation| <= {th['summit_vertical_m']} m"),
        row("M-005.1_heights", "pass" if all(x.get("height_ok", True) for x in rows) else "fail",
            value={"over": [{"id": x["id"], "delta_m": x.get("height_delta_m"), "tol_m": x.get("height_tol_m")} for x in rows if x.get("height_ok") is False],
                   "gated_rows": sum(1 for x in rows if "height_ok" in x)}, threshold="within max(2 m, 1 %) of the sourced / measured height (R-004 rows: the gate_height ones)"),
    ]
    return rows, summ


# ---------------------------------------------------------------- driver
def tile_ids(manifest_path=None, only=None):
    d = J(manifest_path or (GLOBAL_ROOT / "bay_manifest.json"))
    ids = [t["id"] for t in d["tiles"] if t.get("class") != "outside"]
    meta = {t["id"]: t for t in d["tiles"]}
    if only:
        ids = [i for i in ids if i in set(only)]
    return sorted(ids), meta, d


def run(root=None, workers=4, only=None, evid=None, manifest_path=None, doc=None, cfg=None, progress=True):
    t_start = time.time()
    root = Path(root or OUT_DEFAULT)
    cfg = cfg or load_config("m005_bay.json")
    th2 = cfg["thresholds"]
    doc = doc or load_config("landmarks_bay.json")
    ref = J(GLOBAL_ROOT / "landmarks.json")
    ids, meta, mdoc = tile_ids(manifest_path, only)
    th = th_v12()
    missing = [i for i in ids if not (root / parse_tile(i).folder / "manifest.json").is_file()]
    if missing:
        return {"metric_version": V2, "result": "error", "rows": [row("input", "error", reason=f"{len(missing)} tiles of the manifest are not baked under {root}", examples=missing[:8])], "tiles": len(ids)}
    # ---- plan the per-tile tasks
    regions = {b["name"]: box_poly(b["box"]) for b in cfg["bathymetry_boxes"]}
    bay_poly = ll_poly([tuple(p) for p in cfg["bay_polygon_lonlat"]])
    tasks = {i: {} for i in ids}

    def tiles_hit(bb):
        x0, z0, x1, z1 = bb
        return [f"L0_{i:02d}_{j:02d}" for i in range(int(math.floor(x0 / 2048.0)), int(math.floor(x1 / 2048.0)) + 1)
                for j in range(int(math.floor(z0 / 2048.0)), int(math.floor(z1 / 2048.0)) + 1)]
    for name, P in regions.items():
        for tid in tiles_hit(poly_bbox(P)):
            if tid in tasks:
                tasks[tid].setdefault("regions", []).append(name)
    for tid in tiles_hit(poly_bbox(bay_poly)):
        if tid in tasks:
            tasks[tid]["bay"] = True
            tasks[tid]["shallow_below_m"] = th2["shallow_below_m"]
    for c in lb.check_rows(doc):
        if c.get("osm_id"):
            tid = f"L0_{int(math.floor(c['pos_baytm'][0] / 2048.0)):02d}_{int(math.floor(c['pos_baytm'][1] / 2048.0)):02d}"
            if tid in tasks:
                tasks[tid].setdefault("bld", []).append(f"osm:{c['osm_id']}")
    for l in ref["landmarks"]:
        if l["osm"] and l["osm"].startswith("osm:n"):
            if l["tile"] in tasks:
                tasks[l["tile"]].setdefault("summits", []).append({"n": l["n"], "x": l["baytm_x"], "z": l["baytm_z"], "tol": th2["summit_pos_m"]})
        elif l["osm"] and l["tile"] in tasks:
            tasks[l["tile"]].setdefault("bld", []).append(l["osm"])
    counties = load_counties(mdoc.get("world_box_lonlat"))
    cw = [(n, g.wkb) for n, g in counties]
    ctx = mp.get_context("spawn")
    R, errs = [], []
    with ProcessPoolExecutor(max_workers=workers, mp_context=ctx, initializer=_init, initargs=(str(root), cw, regions, bay_poly)) as ex:
        done = 0
        for res in ex.map(scan_tile, [(i, tasks[i]) for i in ids], chunksize=4):
            done += 1
            if "error" in res:
                errs.append(row("input", "error", scope=res["tile"], reason=res["error"]))
            else:
                R.append(res)
            if progress and done % 250 == 0:
                print(f"[m005_bay] scanned {done}/{len(ids)} ({time.time() - t_start:.0f} s)", flush=True)
        rows = list(errs)
        if errs:
            return {"metric_version": V2, "result": "error", "rows": rows, "tiles": len(ids)}
        # ---- buildings: ownership, clipped area, collider overlap
        seen, dup = set(), []
        for x in R:
            for b in x["bids"]:
                (dup.append(b) if b in seen else seen.add(b))
        mis = [b for x in R for b in x["misowned"]]
        rows.append(row("bid_ownership", "pass" if not dup and not mis else "fail", value={"duplicate_bids": len(dup), "not_in_owner_tile": len(mis)},
                        threshold={"duplicate_bids": 0, "not_in_owner_tile": 0}, bids=len(seen), examples=(dup + mis)[:5]))
        worst, ncross = clipped_area_worst([c for x in R for c in x["cross_roots"]])
        rows.append(row("clipped_area", "pass" if worst <= th["clipped_area_rel"] else "fail", value=r(worst, 6), threshold=th["clipped_area_rel"],
                        footprints_crossing_an_edge=ncross))
        crossers = [c for x in R for c in x["cross_colliders"]]
        by_tile = defaultdict(list)
        baked = set(ids)
        for owner, sid, w in crossers:
            g = shp_wkb.loads(w)
            for tid in tiles_hit(g.bounds):
                if tid != owner and tid in baked and g.intersects(box(*parse_tile(tid).bounds)):
                    by_tile[tid].append((owner, sid, w))
        ov = pair_overlaps(crossers, th["collider_overlap_m2"])
        tested = 0
        for res in ex.map(overlap_tile, [(tid, f, th["collider_overlap_m2"]) for tid, f in sorted(by_tile.items())], chunksize=2):
            ov += res["overlaps"]
            tested += res["tested"]
        seenp, uniq = set(), []
        for o in ov:
            k = (tuple(sorted(o["tiles"])), tuple(sorted(o["bids"])))
            if k not in seenp:
                seenp.add(k)
                uniq.append(o)
        worst_ov = max([o["area_m2"] for o in uniq], default=0.0)
        rows.append(row("collider_overlap", "pass" if not uniq else "fail", value=r(worst_ov, 4), threshold=th["collider_overlap_m2"], pairs_over_threshold=len(uniq),
                        colliders_crossing_edges=len(crossers), crossing_pairs_tested=tested, examples=sorted(uniq, key=lambda o: -o["area_m2"])[:6],
                        note="every edge-crossing collider is tested against all colliders of the other tiles it overlaps"))
    # ---- shares, seams
    srows, tile_table = share_rows(R, th, th2)
    rows += srows
    rows += seam_rows(R, th)
    # ---- landmarks (v1.2 row over the landmark tiles and their neighbours)
    import m005_check as M
    lm_tiles = sorted(x["tile"] for x in R if x["landmarks"])
    want = set(lm_tiles)
    for tid in lm_tiles:
        t = parse_tile(tid)
        for di in (-1, 0, 1):
            for dj in (-1, 0, 1):
                n = f"L0_{t.i + di:02d}_{t.j + dj:02d}"
                if n in baked:
                    want.add(n)
    loaded = [M.v12_load(parse_tile(i), root) for i in sorted(want)]
    try:
        lm = M.v12_landmarks(loaded, th)
    except M.NoInput as e:
        lm = M.v12row("landmarks", "error", reason=str(e))
    lm = dict(lm, metric_version=V2, scope="bay", landmark_tiles=len(lm_tiles))
    rows.append(lm)
    lids = [f["lid"] for x in R for f in x["landmarks"]]
    bid_all = seen
    clash = [f["bid"] for x in R for f in x["landmarks"] if f["bid"] in bid_all]
    dupl = sorted({l for l in lids if lids.count(l) > 1}) if len(lids) < 20000 else []
    rows.append(row("landmarks_lid_bay_wide", "pass" if not dupl and not clash else "fail", value={"landmark_features": len(lids), "duplicate_lids": len(dupl), "bid_clash": len(clash)},
                    threshold="lids unique across the Bay; no landmark bid equals a building bid"))
    lrows, lsumm = landmark_m0051(R, doc, ref, cfg)
    rows += lsumm
    rows += bathy_rows(R, cfg)
    rows.append(shallow_row(R, cfg))
    rows.append(licence_rows(R))
    for k, why in cfg["not_run"].items():
        rows.append(row(k, "not_run", reason=why))
    res_ = {x["result"] for x in rows}
    overall = "error" if "error" in res_ else "fail" if "fail" in res_ else "pass"
    rep = {"metric_version": V2, "result": overall, "tiles": len(ids), "root": str(root), "seconds": round(time.time() - t_start, 1),
           "counts": {"pass": sum(x["result"] == "pass" for x in rows), "fail": sum(x["result"] == "fail" for x in rows),
                      "reported": sum(x["result"] == "reported" for x in rows), "not_run": sum(x["result"] == "not_run" for x in rows)},
           "thresholds": {"v1.2": th, "bay": th2}, "rows": rows, "landmark_rows": lrows,
           "totals": {"buildings_structures": sum(x["structures"] for x in R), "building_area_m2": r(sum(x["area"] for x in R), 1),
                      "placeholder_terrain_tiles": sum(1 for x in R if x["terrain"].get("placeholder_nodes"))}}
    if evid:
        evid = Path(evid)
        evid.mkdir(parents=True, exist_ok=True)
        (evid / "m005_bay.json").write_text(json.dumps(rep, indent=1) + "\n")
        with gzip.open(evid / "m005_bay_tiles.csv.gz", "wt", newline="") as fh:
            w = csv.writer(fh)
            w.writerow(["tile", "structures", "structure_area_m2", "default_area_m2", "default_share"])
            w.writerows(tile_table)
    return rep


def summary(rep):
    out = [f"{rep['metric_version']} Bay: {rep['result'].upper()}  tiles {rep['tiles']}  {rep.get('counts')}  ({rep.get('seconds')} s)"]
    for x in rep["rows"]:
        v = x.get("value", x.get("reason", ""))
        s = json.dumps(v)[:150] if not isinstance(v, str) else v[:150]
        out.append(f"  {x['result']:<9}{x['check']:<40}{s}")
    return "\n".join(out)
