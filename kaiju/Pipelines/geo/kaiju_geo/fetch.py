"""One fetch function per layer (T-003 scope 1). Each writes into Pipelines/geo/cache/ with a
`.meta.json` sidecar (url, fetched_utc, sha256, upstream ETag/Last-Modified) and is a no-op
("up to date") when the cache entry exists. Any failure raises: never a partial cache entry.
"""
from __future__ import annotations

import json
import math
import re
import subprocess
import sys
from pathlib import Path

from .common import (CACHE, PIPE, Tile, canonical_json, finish_fetch, http_get, http_json,
                     is_offline, load_config, meta_path, NetworkForbidden, up_to_date, utc_now)

SRC = load_config("sources.json")
FETCH_MARGIN_M = 320.0  # covers footprints owned by the tile that cross its edge + road clipping


# ---------------------------------------------------------------- CUDEM (terrain + seabed)
def cudem_items(lonlat_bbox):
    """CUDEM 1/9" tiles are 0.25 deg, named by their NW corner (R-004 §1.2)."""
    w, s, e, n = lonlat_bbox
    items = []
    lat_n = math.ceil(n * 4) / 4
    while lat_n - 0.25 < n and lat_n > s:
        lon_w = math.floor(w * 4) / 4
        while lon_w < e:
            lat_s_ = f"{int(lat_n):02d}x{int(round((lat_n % 1) * 100)):02d}"
            lw = -lon_w
            lon_s_ = f"{int(lw):03d}x{int(round((lw % 1) * 100)):02d}"
            items.append(f"ncei19_n{lat_s_}_w{lon_s_}_2022v1")
            lon_w += 0.25
        lat_n -= 0.25
    return sorted(set(items))


def _window_read(url: str, dest: Path, bounds, bounds_crs: str, label: str, extra: dict) -> dict:
    """Windowed COG read over HTTP (/vsicurl/) of `bounds` (in bounds_crs) into a local GeoTIFF."""
    m = up_to_date(dest, label)
    if m is not None:
        return m
    if is_offline():
        raise NetworkForbidden(f"[{label}] {dest.name} missing from cache and --offline is set")
    import rasterio
    from rasterio.windows import from_bounds
    from rasterio.warp import transform_bounds
    dest.parent.mkdir(parents=True, exist_ok=True)
    tmp = dest.with_name(dest.name + ".part")
    env = dict(GDAL_HTTP_MAX_RETRY="5", GDAL_HTTP_RETRY_DELAY="2", GDAL_DISABLE_READDIR_ON_OPEN="EMPTY_DIR",
               CPL_VSIL_CURL_ALLOWED_EXTENSIONS=".tif")
    with rasterio.Env(**env):
        with rasterio.open("/vsicurl/" + url) as src:
            b = transform_bounds(bounds_crs, src.crs, *bounds, densify_pts=21)
            fw = from_bounds(*b, transform=src.transform)
            c0, r0 = math.floor(fw.col_off), math.floor(fw.row_off)
            c1, r1 = math.ceil(fw.col_off + fw.width), math.ceil(fw.row_off + fw.height)
            c0, r0, c1, r1 = max(c0, 0), max(r0, 0), min(c1, src.width), min(r1, src.height)
            if c1 <= c0 or r1 <= r0:
                raise ValueError("window outside source")
            win = rasterio.windows.Window(c0, r0, c1 - c0, r1 - r0)
            data = src.read(1, window=win)
            prof = dict(driver="GTiff", width=data.shape[1], height=data.shape[0], count=1, dtype=data.dtype,
                        crs=src.crs, transform=src.window_transform(win), nodata=src.nodata,
                        compress="deflate", predictor=3, tiled=True, blockxsize=256, blockysize=256)
            src_info = {"source_crs": src.crs.to_string(), "source_size": [src.width, src.height],
                        "window": [int(win.col_off), int(win.row_off), int(win.width), int(win.height)]}
    with rasterio.open(tmp, "w", **prof) as dst:
        dst.write(data, 1)
    src_info.update(extra)
    meta = finish_fetch(dest, tmp, url, src_info)
    print(f"[{label}] fetched window {data.shape} -> {dest.relative_to(PIPE)}")
    return meta


def fetch_cudem(tile: Tile) -> list[dict]:
    bb = tile.bounds_lonlat(FETCH_MARGIN_M)
    out = []
    for item in cudem_items(bb):
        lat = item.split("_")[1][1:]
        lon = item.split("_")[2][1:]
        url = SRC["cudem"]["url_template"].format(lat=lat, lon=lon)
        dest = CACHE / "cudem" / f"{item}__{tile.id}.tif"
        m = _window_read(url, dest, bb, "EPSG:4269", "cudem", {"item": item})
        m.update(item=item, file=str(dest.relative_to(PIPE)))
        out.append(m)
    return out


# ---------------------------------------------------------------- 3DEP 1 m (land detail)
def fetch_3dep(tile: Tile) -> list[dict]:
    """TNM discovery (cached per tile) -> newest project per UTM 10 km tile -> windowed reads."""
    bb = tile.bounds_lonlat(FETCH_MARGIN_M)
    q = CACHE / "3dep" / f"tnm_query__{tile.id}.json"
    m = up_to_date(q, "3dep-tnm")
    if m is None:
        url = SRC["dep3_1m"]["tnm_api"].format(w=bb[0], s=bb[1], e=bb[2], n=bb[3])
        res = http_json(url, "3dep-tnm")
        items = sorted(({"title": it["title"], "url": it["downloadURL"], "pub": it.get("publicationDate", ""),
                         "bbox": it.get("boundingBox")} for it in res.get("items", [])
                        if it.get("downloadURL", "").endswith(".tif")), key=lambda d: d["url"])
        if not items:
            raise RuntimeError(f"TNM returned no 1 m DEM items for {tile.id}")
        q.parent.mkdir(parents=True, exist_ok=True)
        tmp = q.with_name(q.name + ".part")
        tmp.write_text(canonical_json(items, indent=1) + "\n")
        finish_fetch(q, tmp, url)
    items = json.loads(q.read_text())
    # newest project per UTM tile key (e.g. x55y419); ties broken by URL for determinism
    best = {}
    for it in items:
        name = it["url"].rsplit("/", 1)[1]
        mk = re.search(r"_(x\d+y\d+)_", name)
        key = mk.group(1) if mk else name
        cand = (it["pub"], it["url"])
        if key not in best or cand > best[key][0]:
            best[key] = (cand, it)
    out = []
    for key in sorted(best):
        it = best[key][1]
        name = it["url"].rsplit("/", 1)[1].removesuffix(".tif")
        dest = CACHE / "3dep" / f"{name}__{tile.id}.tif"
        try:
            m = _window_read(it["url"], dest, bb, "EPSG:4269", "3dep", {"item": name, "published": it["pub"]})
        except ValueError:  # window does not intersect this item
            continue
        m.update(item=name, file=str(dest.relative_to(PIPE)))
        out.append(m)
    return out


# ---------------------------------------------------------------- OSM (buildings, roads, coastline)
def fetch_osm_pbf() -> dict:
    dest = CACHE / "osm" / "norcal-latest.osm.pbf"
    m = http_get(SRC["osm"]["url"], dest, "osm-pbf", timeout=600)
    if "osm_base_utc" not in m:
        import osmium
        hdr = osmium.io.Reader(str(dest), osmium.osm.osm_entity_bits.NOTHING).header()
        m["osm_base_utc"] = hdr.get("osmosis_replication_timestamp") or hdr.get("timestamp") or "unknown"
        meta_path(dest).write_text(canonical_json(m, indent=1) + "\n")
    return m


def fetch_osm_tile(tile: Tile) -> dict:
    """Clip the cached PBF to the tile + margin with osmium (smart strategy keeps whole ways and
    complete multipolygon relations, so straddling buildings are intact)."""
    pbf = fetch_osm_pbf()
    dest = CACHE / "osm" / "tiles" / f"{tile.id}.osm.pbf"
    m = up_to_date(dest, "osm-extract")
    if m is not None and m.get("parent_sha256") == pbf["sha256"]:
        return m
    w, s, e, n = tile.bounds_lonlat(FETCH_MARGIN_M)
    dest.parent.mkdir(parents=True, exist_ok=True)
    tmp = dest.with_name(dest.name + ".part.osm.pbf")
    cmd = [str(Path(sys.executable).parent / "osmium"), "extract", "-b", f"{w:.6f},{s:.6f},{e:.6f},{n:.6f}", "--strategy", "smart",
           "--overwrite", "-o", str(tmp), str(CACHE / "osm" / "norcal-latest.osm.pbf")]
    subprocess.run(cmd, check=True)
    m = finish_fetch(dest, tmp, "osmium extract of " + SRC["osm"]["url"],
                     {"parent_sha256": pbf["sha256"], "osm_base_utc": pbf.get("osm_base_utc"), "bbox_wgs84": [w, s, e, n],
                      "command": "osmium " + " ".join(cmd[1:6])})
    print(f"[osm-extract] {tile.id}: {m['bytes'] / 1e6:.2f} MB")
    return m


def fetch_osm_water() -> dict:
    dest = CACHE / "osm" / "water-polygons-split-4326.zip"
    return http_get(SRC["osm_water"]["url"], dest, "osm-water", timeout=900)


# ---------------------------------------------------------------- DataSF footprints (height fallback)
def fetch_datasf(tile: Tile) -> dict:
    w, s, e, n = tile.bounds_lonlat(FETCH_MARGIN_M)
    dest = CACHE / "datasf" / f"ynuv-fyni__{tile.id}.geojson"
    url = SRC["datasf"]["url_template"].format(n=f"{n:.6f}", w=f"{w:.6f}", s=f"{s:.6f}", e=f"{e:.6f}")
    m = http_get(url, dest, "datasf", expect="FeatureCollection")
    lic = CACHE / "datasf" / "ynuv-fyni.meta.json"
    http_get(SRC["datasf"]["meta_url"], lic, "datasf-licence")
    return m


# ---------------------------------------------------------------- Assessor roll (optional building attributes)
def assessor_url(tile: Tile) -> str:
    w, s, e, n = tile.bounds_lonlat(FETCH_MARGIN_M)
    a = SRC["assessor"]
    return a["url_template"].format(roll_year=a["roll_year"], limit=a["row_limit"],
                                    n=f"{n:.6f}", w=f"{w:.6f}", s=f"{s:.6f}", e=f"{e:.6f}")


def fetch_assessor(tile: Tile) -> dict:
    """Newest-roll parcel centroids with year built, stories and construction type for a tile (cached, idempotent)."""
    dest = CACHE / "assessor" / f"wv5m-vpq2__{tile.id}.json"
    m = http_get(assessor_url(tile), dest, "assessor", expect="[")
    rows = json.loads(dest.read_text(encoding="utf-8"))
    if not isinstance(rows, list):
        raise RuntimeError(f"assessor: unexpected payload for {tile.id}: {str(rows)[:200]}")
    if len(rows) >= SRC["assessor"]["row_limit"]:
        for p in (dest, meta_path(dest)):
            p.unlink(missing_ok=True)
        raise RuntimeError(f"assessor: {tile.id} hit the row limit ({len(rows)}); the result may be truncated")
    http_get(SRC["assessor"]["meta_url"], CACHE / "assessor" / "wv5m-vpq2.meta.json", "assessor-licence")
    return m


# ---------------------------------------------------------------- Qfaults (global layer)
WORLD_BOX_WGS84 = (-123.05, 37.15, -121.55, 38.35)  # R-004 §2.1 world box


def fetch_faults() -> list[dict]:
    out = []
    for layer in (4, 5):
        dest = CACHE / "qfaults" / f"qfaults_layer{layer}.geojson"
        m = up_to_date(dest, f"qfaults-{layer}")
        if m is None:
            feats, offset = [], 0
            w, s, e, n = WORLD_BOX_WGS84
            while True:
                url = SRC["qfaults"]["url_template"].format(layer=layer, w=w, s=s, e=e, n=n, offset=offset)
                page = http_json(url, f"qfaults-{layer}")
                if "features" not in page:
                    raise RuntimeError(f"Qfaults layer {layer}: unexpected response {str(page)[:200]}")
                feats += page["features"]
                print(f"[qfaults-{layer}] offset {offset}: +{len(page['features'])}")
                if len(page["features"]) < 2000:
                    break
                offset += 2000
            dest.parent.mkdir(parents=True, exist_ok=True)
            tmp = dest.with_name(dest.name + ".part")
            tmp.write_text(canonical_json({"type": "FeatureCollection", "features": feats}) + "\n")
            m = finish_fetch(dest, tmp, SRC["qfaults"]["url_template"].split("/query")[0].format(layer=layer),
                             {"features": len(feats), "world_box_wgs84": list(WORLD_BOX_WGS84)})
        out.append(m)
    return out


# ---------------------------------------------------------------- tides, NGS, lidar (reference)
def fetch_tides() -> dict:
    return http_get(SRC["tides"]["url"], CACHE / "noaa" / "tides_9414290_datums.json", "tides", expect="datums")


def fetch_ngs(tile: Tile) -> dict:
    from .common import baytm_to_lonlat
    cx, cz = tile.x0 + tile.size / 2, tile.z0 + tile.size / 2
    lon, lat = baytm_to_lonlat(cx, cz)
    r_mi = round(tile.size * 0.75 / 1609.344, 2)  # circle covering the square
    url = SRC["ngs"]["url_template"].format(lat=f"{lat:.6f}", lon=f"{lon:.6f}", radius_mi=r_mi)
    return http_get(url, CACHE / "ngs" / f"ngs_radial__{tile.id}.json", "ngs", expect="pid")


LIDAR_FILTER = [
    # first returns only; drop ASPRS noise classes 7 (low noise) and 18 (high noise). One expression, because
    # filters.range ORs ranges on the same dimension (DEF-T003-bay-3: the old filter kept every class).
    {"type": "filters.expression", "expression": "ReturnNumber == 1 && Classification != 7 && Classification != 18"},
    # isolated points / outliers (birds, multipath spikes): statistical outlier removal marks class 7 ...
    {"type": "filters.outlier", "method": "statistical", "mean_k": 8, "multiplier": 2.5},
    # ... which is then dropped
    {"type": "filters.expression", "expression": "Classification != 7"},
]


def fetch_lidar_dsm(tile: Tile) -> dict:
    """First-return DSM v2 (max z of first returns per 1 m cell, noise classes and statistical outliers removed) in
    UTM 10N + NAVD88 from the 3DEP EPT, clipped to the tile + 64 m. Reference only (M-005.7 R-ind).
    Contains buildings, rooftop plant, vegetation, cranes, poles, bridges; water returns are sparse (nodata).
    Needs python-pdal."""
    dest = CACHE / "lidar" / f"dsm_first_return_v2__{tile.id}.tif"
    m = up_to_date(dest, "lidar-dsm")
    if m is not None:
        return m
    if is_offline():
        raise NetworkForbidden(f"[lidar-dsm] {dest.name} missing from cache and --offline is set")
    import pdal
    from .common import transformer, baytm_crs
    x0, z0, x1, z1 = tile.bounds
    t = transformer(baytm_crs(), "EPSG:3857")
    xs, ys = zip(*[t.transform(x, z) for x in (x0 - 64, x1 + 64) for z in (z0 - 64, z1 + 64)])
    bounds = f"([{min(xs):.2f},{max(xs):.2f}],[{min(ys):.2f},{max(ys):.2f}])"
    dest.parent.mkdir(parents=True, exist_ok=True)
    tmp = dest.with_name(dest.name + ".part.tif")
    pipe = [{"type": "readers.ept", "filename": SRC["lidar_ept"]["url"], "bounds": bounds, "resolution": 0.7, "threads": 8}] + LIDAR_FILTER + [
        {"type": "filters.reprojection", "out_srs": "EPSG:26910+5703"},
        {"type": "writers.gdal", "filename": str(tmp), "resolution": 1.0, "output_type": "max",
         "data_type": "float32", "nodata": -9999, "gdalopts": "COMPRESS=DEFLATE,PREDICTOR=3,TILED=YES"},
    ]
    p = pdal.Pipeline(json.dumps(pipe))
    n = p.execute()
    m = finish_fetch(dest, tmp, SRC["lidar_ept"]["url"], {"points_kept": int(n), "ept_bounds_3857": bounds,
                                                          "pdal_filters": LIDAR_FILTER})
    print(f"[lidar-dsm] {n} first-return points kept -> {dest.relative_to(PIPE)}")
    return m


LAYERS = {
    "cudem": fetch_cudem, "3dep": fetch_3dep, "osm": fetch_osm_tile, "datasf": fetch_datasf, "assessor": fetch_assessor,
    "ngs": fetch_ngs, "lidar": fetch_lidar_dsm,
}
