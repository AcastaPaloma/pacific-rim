#!/usr/bin/env python
"""T-115g: real softness for the Bay route-planner cost raster (`bay_cost@v2`), from USGS OFR 2006-1037 liquefaction susceptibility.

Replaces the T-115d proxy (land nodes <= 3 m NAVD88) inside the dataset's coverage; depth is copied unchanged from `bay_cost_256m_v1.bin`.
Source: USGS OFR 2006-1037 v1.0 (Witter et al.), `sfq2py.shp` (polygons, attribute LIQ), geographic (lon/lat, NAD83), downloaded by `--fetch` from
https://pubs.usgs.gov/of/2006/1037/of06-1037_4b.shp.zip into `<cache>/liquefaction_of2006_1037/raw` (sha256 recorded, no redistribution).
Class -> softness (Assumed, monotone, gameplay scale 0..1; the report gives only the ordinal classes): VH 1.0, H 0.75, M 0.5, L 0.25, VL 0.10, NM 0.0;
W (water polygon) is not land and is ignored. Cell softness = mean over the cell's mapped land pixels (32 m raster, 8x8 per 256 m cell).
Cells with < 5 % polygon coverage (outside the dataset and the holes in it, e.g. most of the SF peninsula) keep the v1 proxy;
covered cells with no mapped land (water) get 0. Mud (Qhbm) is rated M by the report, so Bay margin land is not 'very soft' any more.
Usage: python export_bay_softness.py [--fetch] [--v1 PATH] [--out DIR]
"""
import argparse, hashlib, json, struct, sys, urllib.request, zipfile
from pathlib import Path
import numpy as np
sys.path.insert(0, str(Path(__file__).resolve().parent))
from kaiju_geo import common

URL = "https://pubs.usgs.gov/of/2006/1037/of06-1037_4b.shp.zip"
CLASS_SOFT = {"VH": 1.0, "H": 0.75, "M": 0.5, "L": 0.25, "VL": 0.10, "NM": 0.0}
CELL = 256.0; SUB = 8; NX = NY = 576


def fetch(raw: Path):
    raw.mkdir(parents=True, exist_ok=True)
    z = raw / "of06-1037_4b.shp.zip"
    if not z.exists():
        urllib.request.urlretrieve(URL, z)
    with zipfile.ZipFile(z) as f:
        f.extractall(raw)
    return hashlib.sha256(z.read_bytes()).hexdigest()


def read_bin(p):
    b = Path(p).read_bytes(); assert b[:4] == b"BCF1"
    nx, ny, ox, oy, cell = struct.unpack("<iifff", b[4:24]); n = nx * ny
    depth = np.frombuffer(b, "<u2", n, 24); soft = np.frombuffer(b, np.uint8, n, 24 + 2 * n)
    return nx, ny, ox, oy, cell, depth.copy(), soft.copy()


def rasterize(shp, nx, ny, cell):
    import geopandas as gpd
    from rasterio import features
    from rasterio.transform import from_origin
    g = gpd.read_file(shp).set_crs("EPSG:4269", allow_override=True)
    cov = tuple(g.total_bounds)
    g = g.to_crs(common.baytm_crs())
    g = g[g["LIQ"].isin(list(CLASS_SOFT) + ["W"])]
    px = cell / SUB; W = nx * SUB; H = ny * SUB
    tr = from_origin(0.0, ny * cell, px, px)                     # row 0 = north
    shapes = [(geom, 100.0 if c == "W" else CLASS_SOFT[c] + 1.0) for geom, c in zip(g.geometry, g["LIQ"]) if geom is not None and not geom.is_empty]
    # paint large to small so small detailed units win over their enclosing polygons
    order = np.argsort([-s[0].area for s in shapes]); shapes = [shapes[i] for i in order]
    r = features.rasterize(shapes, out_shape=(H, W), transform=tr, fill=0.0, dtype="float32")
    covered = r > 0; mapped = covered & (r < 50.0); val = np.where(mapped, r - 1.0, 0.0)
    def cells(a): return a.reshape(ny, SUB, nx, SUB)[::-1].transpose(0, 2, 1, 3).sum(axis=(2, 3))   # row 0 = south
    return cells(mapped), cells(val), cells(covered), cov, g


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    root = Path(__file__).resolve().parents[2]
    ap.add_argument("--fetch", action="store_true"); ap.add_argument("--v1", default=str(root / "evidence/T-115/bay_cost_256m_v1.bin"))
    ap.add_argument("--out", default=str(root / "evidence/T-115g"))
    a = ap.parse_args()
    raw = common.CACHE / "liquefaction_of2006_1037" / "raw"
    zsha = fetch(raw) if a.fetch or not (raw / "sfq2py.shp").exists() else hashlib.sha256((raw / "of06-1037_4b.shp.zip").read_bytes()).hexdigest()
    nx, ny, ox, oy, cell, depth, soft1 = read_bin(a.v1)
    assert (nx, ny, cell) == (NX, NY, CELL) and ox == 0 and oy == 0
    cnt, sm, cvr, cov, g = rasterize(raw / "sfq2py.shp", nx, ny, cell)
    cnt = cnt.reshape(-1).astype(np.float32); sm = sm.reshape(-1); cvr = cvr.reshape(-1)
    from pyproj import Transformer
    x0, y0, x1, y1 = cov
    t = Transformer.from_crs("EPSG:4269", common.baytm_crs(), always_xy=True)
    xs = (np.arange(nx) + 0.5) * cell; ys = (np.arange(ny) + 0.5) * cell
    X, Y = np.meshgrid(xs, ys); lon, lat = Transformer.from_crs(common.baytm_crs(), "EPSG:4269", always_xy=True).transform(X, Y)
    inside = cvr >= 0.05 * SUB * SUB                                # cell has database coverage (land or water polygon); SF peninsula has holes
    land = cnt >= 0.05 * SUB * SUB                                  # at least 5 % of the cell mapped land
    real = np.where(land, sm / np.maximum(cnt, 1.0), 0.0)
    out = np.where(inside, real, soft1 / 255.0)
    sq = np.clip(np.rint(out * 255.0), 0, 255).astype(np.uint8)
    o = Path(a.out); o.mkdir(parents=True, exist_ok=True)
    binp = o / "bay_cost_256m_v2.bin"
    with open(binp, "wb") as f:
        f.write(b"BCF1" + struct.pack("<iifff", nx, ny, 0.0, 0.0, cell)); f.write(depth.tobytes()); f.write(sq.tobytes())
    changed = int((np.abs(sq.astype(int) - soft1.astype(int)) > 25).sum())
    info = dict(spec="bay_cost@v2", nx=nx, ny=ny, cell_m=cell, depth="copied unchanged from bay_cost_256m_v1.bin", v1_sha256=hashlib.sha256(Path(a.v1).read_bytes()).hexdigest(),
                file_sha256=hashlib.sha256(binp.read_bytes()).hexdigest(), source=URL, source_zip_sha256=zsha,
                class_to_softness=CLASS_SOFT, class_mapping_status="Assumed (ordinal source classes)", coverage_lonlat=[float(v) for v in cov],
                stats=dict(cells_inside=int(inside.sum()), cells_mapped_land=int((land & inside).sum()), cells_proxy_uncovered=int((~inside).sum()),
                           soft_gt_half=int((sq > 127).sum()), v1_soft_gt_half=int((soft1 > 127).sum()), cells_changed_gt_0p1=changed,
                           class_counts={c: int((g["LIQ"] == c).sum()) for c in CLASS_SOFT}),
                note="Inside coverage the v1 proxy is replaced; outside it is kept. Bay mud (Qhbm) is class M in OFR 2006-1037.")
    json.dump(info, open(o / "bay_cost_256m_v2.json", "w"), indent=1)
    from PIL import Image
    img = np.zeros((ny, nx, 3), np.uint8); img[..., 0] = sq.reshape(ny, nx); img[..., 2] = np.where(depth.reshape(ny, nx) > 5, 255, 0)
    Image.fromarray(img[::-1]).save(o / "bay_cost_256m_v2.png")
    print(json.dumps(info["stats"]), info["file_sha256"][:12])


if __name__ == "__main__":
    main()
