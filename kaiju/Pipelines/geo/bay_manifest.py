#!/usr/bin/env python
"""bay_manifest@v1: which BayTM@v1 L0 tiles make up "the Bay", their class and expected sources (T-100, R-100).

Q-201 (decided): every L0 tile that intersects the R-004 world box and holds land or Bay/estuary water; open-ocean-only
tiles are listed (terrain + water only, no OSM). Tiles outside the box are not listed (counted in `counts.outside`).
Classes: urban / rural / water_only / ocean_only (see config/bay.json `classes`).

Commands (kaiju env: /opt/miniconda3/envs/kaiju/bin/python -I Pipelines/geo/bay_manifest.py <cmd>):
  build            compute the manifest from config/bay.json + the cached OSM water polygons (no network); writes
                   Assets/StreamingAssets/Geo/global/v1/bay_manifest.json; byte-identical on re-run; `--check` compares
                   instead of writing (exit 1 on difference).
  refresh-indexes  HEAD/index queries only (TNM 1 m DEM index, CUDEM url list, 3DEP lidar EPT boundaries); rewrites
                   `snapshots.{cudem,dep3_1m,lidar_ept}` in config/bay.json. No bulk downloads (that is T-101).
  scan-osm         one pass over the cached NorCal PBF counting building ways per tile; rewrites
                   `snapshots.osm_buildings` in config/bay.json (about 3 min).
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from kaiju_geo.common import (CACHE, CONFIG, GLOBAL_ROOT, TILE_SIZE_M, Tile, baytm_crs, canonical_json,  # noqa: E402
                              sha256_bytes, sha256_file, transformer, write_if_changed)

SPEC = "bay_manifest@v1"
SCHEMA_VERSION = 1
BAY_JSON = CONFIG / "bay.json"
OUT_PATH = GLOBAL_ROOT / "bay_manifest.json"
WATER_ZIP = CACHE / "osm" / "water-polygons-split-4326.zip"
WATER_SHP = "/vsizip/{zip}/water-polygons-split-4326/water_polygons.shp"
GRID_N = 68
CLASSES = ("urban", "rural", "water_only", "ocean_only")
FETCH_MARGIN_M = 320.0  # same margin as kaiju_geo.fetch


def load_bay() -> dict:
    return json.loads(BAY_JSON.read_text(encoding="utf-8"))


# ---------------------------------------------------------------- geometry helpers
def _ring(w, s, e, n, k=24):
    pts = []
    for a in range(k):
        pts.append((w + (e - w) * a / k, s))
    for a in range(k):
        pts.append((e, s + (n - s) * a / k))
    for a in range(k):
        pts.append((e - (e - w) * a / k, n))
    for a in range(k):
        pts.append((w, n - (n - s) * a / k))
    return pts


def world_polygon_baytm(box):
    """The world box (lon/lat, densified) as a polygon in BayTM."""
    from shapely.geometry import Polygon
    w, s, e, n = box
    pts = _ring(w, s, e, n, 200)
    t = transformer("EPSG:6318", baytm_crs())
    xs, zs = t.transform([p[0] for p in pts], [p[1] for p in pts])
    return Polygon(list(zip(xs, zs)))


def lonlat_polygon_to_baytm(coords):
    from shapely.geometry import Polygon
    t = transformer("EPSG:6318", baytm_crs())
    xs, zs = t.transform([c[0] for c in coords], [c[1] for c in coords])
    return Polygon(list(zip(xs, zs)))


def tile_square(t: Tile, margin: float = 0.0):
    from shapely.geometry import box
    x0, z0, x1, z1 = t.bounds
    return box(x0 - margin, z0 - margin, x1 + margin, z1 + margin)


def tile_lonlat_poly(t: Tile, margin: float = FETCH_MARGIN_M):
    from shapely.geometry import Polygon
    x0, z0, x1, z1 = t.bounds
    pts = _ring(x0 - margin, z0 - margin, x1 + margin, z1 + margin, 8)
    tr = transformer(baytm_crs(), "EPSG:6318")
    lo, la = tr.transform([p[0] for p in pts], [p[1] for p in pts])
    return Polygon(list(zip(lo, la)))


def cudem_file_box(item: dict):
    from shapely.geometry import box
    return box(item["lon_w"], item["lat_n"] - 0.25, item["lon_w"] + 0.25, item["lat_n"])


def dep3_key_polygon_baytm(key: str):
    """3DEP 1 m project tiles are named by the upper-left corner in UTM 10N (x = E/10 km, y = N/10 km)."""
    from shapely.geometry import Polygon
    m = re.fullmatch(r"x(\d+)y(\d+)", key)
    x, y = int(m.group(1)) * 10000, int(m.group(2)) * 10000
    pts = _ring(x, y - 10000, x + 10000, y, 4)
    tr = transformer("EPSG:26910", baytm_crs())
    xs, zs = tr.transform([p[0] for p in pts], [p[1] for p in pts])
    return Polygon(list(zip(xs, zs)))


# ---------------------------------------------------------------- water / land fraction
def read_water_baytm(box_lonlat, margin_deg=0.1):
    import numpy as np
    import pyogrio
    import shapely
    w, s, e, n = box_lonlat
    df = pyogrio.read_dataframe(WATER_SHP.format(zip=WATER_ZIP), bbox=(w - margin_deg, s - margin_deg, e + margin_deg, n + margin_deg))
    tr = transformer("EPSG:6318", baytm_crs())
    out = []
    for g in df.geometry:
        out.append(shapely.transform(g, lambda a: np.column_stack(tr.transform(a[:, 0], a[:, 1]))))
    return out


def water_inputs_sha() -> str:
    mp = WATER_ZIP.with_name(WATER_ZIP.name + ".meta.json")
    if mp.exists():
        return json.loads(mp.read_text())["sha256"]
    return sha256_file(WATER_ZIP)


# ---------------------------------------------------------------- classification (pure; unit-tested)
def classify(land_frac: float, osm_buildings: int, bay_frac: float, cfg: dict) -> str:
    """urban / rural when land is present; otherwise water_only (Bay/estuary) or ocean_only."""
    c = cfg["classes"]
    if land_frac >= c["land_min_fraction"]:
        return "urban" if osm_buildings >= c["urban_min_osm_buildings"] else "rural"
    return "water_only" if bay_frac >= c["bay_water_min_region_fraction"] else "ocean_only"


def expected_sources(cls: str, tile: Tile, ctx: dict, osm_buildings: int = 0) -> dict:
    """Expected inputs for one tile (names only; nothing is fetched here). lidar_ept (T-020b height fill, Q-202) and datasf
    apply to any non-ocean tile holding OSM buildings / lying in San Francisco."""
    cfg = ctx["cfg"]
    src: dict = {}
    sq_m = tile_square(tile, FETCH_MARGIN_M)
    ll = tile_lonlat_poly(tile)
    cud = sorted(i["item"] for i in ctx["cudem"] if cudem_file_box(i).intersects(ll) and cudem_file_box(i).intersection(ll).area > 0)
    src["cudem"] = cud
    src["osm_water"] = True
    if cls == "ocean_only":
        src["osm"] = False
    else:
        src["osm"] = True
    if cls in ("urban", "rural"):
        dep = sorted(k for k, p in ctx["dep3_polys"].items() if p.intersects(sq_m) and p.intersection(sq_m).area > 1.0)
        src["dep3_1m"] = dep
    if cls != "ocean_only" and osm_buildings > 0:
        ept = sorted(n for n, g in ctx["ept_geoms"].items()
                     if g.intersects(ll) and g.intersection(ll).area / ll.area >= cfg["classes"]["ept_min_overlap_fraction"])
        src["lidar_ept"] = ept
    if cls != "ocean_only":
        from shapely.geometry import box as _box
        if _box(*cfg["datasf_bbox_lonlat"]).intersects(ll):
            src["datasf"] = True
    gaps = []
    if not cud:
        gaps.append("no_cudem")
    if cls in ("urban", "rural") and not src.get("dep3_1m"):
        gaps.append("no_3dep_1m")
    if cls != "ocean_only" and osm_buildings > 0 and not src.get("lidar_ept"):
        gaps.append("no_lidar_ept")
    if gaps:
        src["gaps"] = gaps
    return src


# ---------------------------------------------------------------- build
def build_manifest(cfg: dict | None = None, water_geoms=None, water_sha: str | None = None) -> dict:
    import shapely
    from shapely import STRtree
    cfg = cfg or load_bay()
    sn = cfg["snapshots"]
    box_ll = tuple(cfg["world_box_lonlat"])
    world = world_polygon_baytm(box_ll)
    if water_geoms is None:
        water_geoms = read_water_baytm(box_ll)
    water_sha = water_sha or water_inputs_sha()
    tree = STRtree(water_geoms)
    bay_regions = [lonlat_polygon_to_baytm(p) for p in cfg["bay_region_polygons_lonlat"]]
    bay_region = shapely.union_all(bay_regions)
    from shapely import wkt
    ctx = {
        "cfg": cfg, "cudem": sn["cudem"],
        "dep3_polys": {k: dep3_key_polygon_baytm(k) for k in sorted(sn["dep3_1m"])},
        "ept_geoms": {e["name"]: wkt.loads(e["wkt"]) for e in sn["lidar_ept"]},
    }
    osm_b = sn["osm_buildings"]
    tiles = []
    outside = 0
    for i in range(GRID_N):
        for j in range(GRID_N):
            t = Tile(0, i, j)
            sq = tile_square(t)
            inter = world.intersection(sq).area if world.intersects(sq) else 0.0
            if inter <= 1.0:
                outside += 1
                continue
            idx = tree.query(sq)
            parts = [g for g in (water_geoms[k].intersection(sq) for k in idx) if not g.is_empty]
            wat = shapely.union_all(parts) if parts else None
            wa = wat.area if wat is not None else 0.0
            area = TILE_SIZE_M * TILE_SIZE_M
            land = max(0.0, 1.0 - wa / area)
            bay_frac = 0.0
            if wa > 0 and bay_region.intersects(wat):
                bay_frac = bay_region.intersection(wat).area / wa
            nb = int(osm_b.get(f"{i}_{j}", 0))
            cls = classify(round(land, 4), nb, bay_frac, cfg)
            tiles.append({
                "id": t.id,
                "class": cls,
                "bbox_baytm": [t.x0, t.z0, t.x0 + t.size, t.z0 + t.size],
                "in_box_fraction": round(inter / area, 4),
                "land_fraction": round(land, 4),
                "osm_buildings": nb,
                "sources": expected_sources(cls, t, ctx, nb),
                "bake": {"status": "not_baked"},
            })
    tiles.sort(key=lambda d: d["id"])
    counts = {c: sum(1 for t in tiles if t["class"] == c) for c in CLASSES}
    counts["total"] = len(tiles)
    counts["outside"] = outside
    counts["grid"] = GRID_N * GRID_N
    cfg_for_fp = {k: v for k, v in cfg.items() if not k.startswith("_")}
    fp = sha256_bytes(canonical_json({
        "config": cfg_for_fp, "water_polygons_sha256": water_sha,
        "code_sha256": sha256_file(Path(__file__).resolve())}).encode())
    return {
        "spec": SPEC, "schema_version": SCHEMA_VERSION,
        "crs": "kaiju:BayTM@v1", "tile_scheme": {"level": 0, "size_m": TILE_SIZE_M, "grid": [GRID_N, GRID_N]},
        "world_box_lonlat": list(box_ll),
        "fingerprint": fp,
        "inputs": {"water_polygons_sha256": water_sha, "config": "Pipelines/geo/config/bay.json",
                   "osm_pbf_sha256": cfg["snapshots"]["provenance"].get("osm_pbf_sha256"),
                   "snapshots_fetched": cfg["snapshots"]["provenance"].get("fetched_utc")},
        "counts": counts,
        "tiles": tiles,
    }


def manifest_bytes(man: dict) -> bytes:
    return (canonical_json(man) + "\n").encode("utf-8")


# ---------------------------------------------------------------- refresh-indexes (network, index only)
def refresh_indexes() -> None:
    import requests
    from shapely.geometry import box, shape, mapping  # noqa: F401
    import shapely
    cfg = load_bay()
    W, S, E, N = cfg["world_box_lonlat"]
    # 3DEP 1 m DEM index via TNM: newest project per UTM tile (same rule as kaiju_geo.fetch.fetch_3dep)
    items, off = [], 0
    while True:
        u = ("https://tnmaccess.nationalmap.gov/api/v1/products?datasets=Digital%20Elevation%20Model%20(DEM)%201%20meter"
             f"&bbox={W},{S},{E},{N}&outputFormat=JSON&max=200&offset={off}")
        j = requests.get(u, timeout=120).json()
        items += j.get("items", [])
        off += 200
        if off >= j.get("total", 0):
            break
    best = {}
    for it in items:
        d = it.get("downloadURL") or ""
        mk = re.search(r"_(x\d+y\d+)_", d)
        if not d.endswith(".tif") or not mk:
            continue
        cand = (it.get("publicationDate") or "", d)
        if mk.group(1) not in best or cand > best[mk.group(1)][0]:
            best[mk.group(1)] = (cand, it)
    dep3 = {k: {"url": v[1]["downloadURL"], "bytes": int(v[1].get("sizeInBytes") or 0), "published": v[1].get("publicationDate", "")}
            for k, v in sorted(best.items())}
    # CUDEM file list (urllist8483.txt) with HEAD sizes
    base = "https://coast.noaa.gov/htdata/raster2/elevation/NCEI_ninth_Topobathy_2014_8483/urllist8483.txt"
    urls = [l.strip() for l in requests.get(base, timeout=60).text.splitlines() if "/CA/" in l]
    cud = []
    for u in urls:
        m = re.search(r"(ncei19_n(\d+)x(\d+)_w(\d+)x(\d+)_(\d{4})v1)\.tif", u)
        if not m:
            continue
        lat = int(m.group(2)) + int(m.group(3)) / 100
        lon = -(int(m.group(4)) + int(m.group(5)) / 100)
        if lat - 0.25 < N and lat > S and lon < E and lon + 0.25 > W:
            sz = int(requests.head(u, timeout=60, allow_redirects=True).headers.get("Content-Length", 0))
            cud.append({"item": m.group(1), "url": u, "lat_n": lat, "lon_w": lon, "year": m.group(6), "bytes": sz})
    cud.sort(key=lambda d: d["item"])
    # 3DEP lidar EPT boundaries
    res = requests.get("https://raw.githubusercontent.com/hobuinc/usgs-lidar/master/boundaries/resources.geojson", timeout=120).json()
    bb = box(W, S, E, N)
    ept = []
    for f in res["features"]:
        g = shape(f["geometry"])
        if g.intersects(bb):
            g2 = shapely.set_precision(g.intersection(box(W - .1, S - .1, E + .1, N + .1)).simplify(0.0005), 1e-4)
            p = f["properties"]
            ept.append({"name": p["name"], "url": p["url"], "points": int(p["count"]), "wkt": shapely.to_wkt(g2, rounding_precision=4)})
    ept.sort(key=lambda d: d["name"])
    sn = cfg.setdefault("snapshots", {})
    sn["cudem"], sn["dep3_1m"], sn["lidar_ept"] = cud, dep3, ept
    from kaiju_geo.common import utc_now
    sn.setdefault("provenance", {})["fetched_utc"] = utc_now()
    BAY_JSON.write_text(canonical_json(cfg, indent=1) + "\n", encoding="utf-8")
    print(f"snapshots: cudem {len(cud)} ({sum(c['bytes'] for c in cud)/1e9:.2f} GB), 3dep {len(dep3)} "
          f"({sum(d['bytes'] for d in dep3.values())/1e9:.2f} GB), ept {len(ept)}")


# ---------------------------------------------------------------- scan-osm
def scan_osm() -> None:
    import collections
    import osmium
    pbf = CACHE / "osm" / "norcal-latest.osm.pbf"
    tr = transformer("EPSG:6318", baytm_crs())
    cnt = collections.Counter()
    fp = osmium.FileProcessor(str(pbf)).with_locations().with_filter(osmium.filter.KeyFilter("building"))
    for o in fp:
        if not o.is_way() or not o.nodes:
            continue
        loc = o.nodes[0].location
        if not loc.valid():
            continue
        x, z = tr.transform(loc.lon, loc.lat)
        cnt[f"{int(x // TILE_SIZE_M)}_{int(z // TILE_SIZE_M)}"] += 1
    cfg = load_bay()
    world = world_polygon_baytm(tuple(cfg["world_box_lonlat"]))
    keep = {}
    for k in sorted(cnt):
        i, j = (int(v) for v in k.split("_"))
        if 0 <= i < GRID_N and 0 <= j < GRID_N and world.intersects(tile_square(Tile(0, i, j))):
            keep[k] = cnt[k]
    cnt = keep
    cfg["snapshots"]["osm_buildings"] = {k: cnt[k] for k in sorted(cnt)}
    cfg["snapshots"].setdefault("provenance", {})["osm_pbf_sha256"] = json.loads(
        pbf.with_name(pbf.name + ".meta.json").read_text())["sha256"]
    cfg["snapshots"]["provenance"]["osm_scan_rule"] = "building ways, first node location, BayTM tile (relations ignored)"
    BAY_JSON.write_text(canonical_json(cfg, indent=1) + "\n", encoding="utf-8")
    print(f"osm_buildings: {sum(cnt.values())} ways in {len(cnt)} tiles")


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = ap.add_subparsers(dest="cmd")
    b = sub.add_parser("build")
    b.add_argument("--check", action="store_true")
    b.add_argument("--out", default=str(OUT_PATH))
    sub.add_parser("refresh-indexes")
    sub.add_parser("scan-osm")
    a = ap.parse_args(argv)
    if a.cmd in (None, "build"):
        out = Path(getattr(a, "out", OUT_PATH))
        data = manifest_bytes(build_manifest())
        if getattr(a, "check", False):
            same = out.exists() and out.read_bytes() == data
            print("bay_manifest: " + ("identical" if same else "DIFFERENT"))
            return 0 if same else 1
        ch = write_if_changed(out, data)
        m = json.loads(data)
        print(f"bay_manifest: {'written' if ch else 'unchanged'} {out} {m['counts']} fingerprint {m['fingerprint'][:12]}")
        return 0
    if a.cmd == "refresh-indexes":
        refresh_indexes()
    elif a.cmd == "scan-osm":
        scan_osm()
    return 0


if __name__ == "__main__":
    sys.exit(main())
