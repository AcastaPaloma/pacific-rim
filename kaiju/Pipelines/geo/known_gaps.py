#!/usr/bin/env python
"""Candidate buildings missing from the open data: lidar first-return objects (> 15 m above the bare surface,
> 300 m^2, median > 15 m) that no OSM footprint within 6 m covers, outside water. Writes evidence/T-003/known_gaps_candidates.json (input for known_gaps.md).
Usage: /opt/miniconda3/envs/kaiju/bin/python -I Pipelines/geo/known_gaps.py [--tile L0_30_36]
"""
import argparse, json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np  # noqa: E402
import shapely  # noqa: E402
from shapely.geometry import shape  # noqa: E402
from kaiju_geo import terrain, vector  # noqa: E402
from kaiju_geo.common import CACHE, PROJECT, TILES_ROOT, parse_tile  # noqa: E402


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--tile", default="L0_30_36")
    t = parse_tile(ap.parse_args(argv).tile)
    import rasterio
    from rasterio.features import rasterize
    from rasterio.transform import Affine
    from scipy.ndimage import label
    d = TILES_ROOT / t.folder
    hs = np.flipud(rasterio.open(d / "terrain.tif").read(1).astype(np.float64))
    xs = t.x0 + np.arange(2049) * t.spacing
    X, Z = np.meshgrid(xs, t.z0 + np.arange(2049) * t.spacing)
    dsm = terrain.sample_rasters([CACHE / "lidar" / f"dsm_first_return__{t.id}.tif"], X, Z)
    feats = json.loads((d / "buildings.geojson").read_text())["features"]
    tr = Affine(1, 0, t.x0 - 0.5, 0, 1, t.z0 - 0.5)   # south-up node grid
    osm = rasterize([(shape(f["geometry"]).buffer(6), 1) for f in feats], out_shape=(2049, 2049), transform=tr, dtype="uint8").astype(bool)
    from PIL import Image
    wc = np.flipud(np.array(Image.open(d / "water.png")) >= 128)
    wet = np.zeros_like(osm); wet[:-1, :-1] |= wc; wet[1:, 1:] |= wc; wet[:-1, 1:] |= wc; wet[1:, :-1] |= wc
    tall = np.isfinite(dsm) & (dsm - hs > 15) & ~osm & ~wet
    lab, n = label(tall)
    out = []
    for k in range(1, n + 1):
        m = lab == k
        a = int(m.sum())
        if a < 300 or np.nanmedian((dsm - hs)[m]) < 15:
            continue
        zz, xx = np.nonzero(m)
        out.append({"x": round(float(t.x0 + xx.mean()), 1), "z": round(float(t.z0 + zz.mean()), 1), "area_m2": a,
                    "max_height_above_ground_m": round(float(np.nanmax((dsm - hs)[m])), 1),
                    "median_height_above_ground_m": round(float(np.nanmedian((dsm - hs)[m])), 1)})
    out.sort(key=lambda o: -o["area_m2"] * o["median_height_above_ground_m"])
    for o in out:
        lon, lat = __import__("kaiju_geo.common", fromlist=["x"]).baytm_to_lonlat(o["x"], o["z"])
        o["lon"], o["lat"] = round(lon, 6), round(lat, 6)
    names = sorted({f["properties"]["name"] for f in feats if f["properties"]["name"] and "Fremont" in f["properties"]["name"]})
    doc = {"tile": t.id, "rule": "lidar first-return DSM - bare surface > 15 m, connected area >= 300 m^2 with median > 15 m, not within 6 m of any OSM footprint, not water (may also be trees, cranes, construction)",
           "candidates": out, "osm_names_containing_Fremont": names}
    (PROJECT / "evidence" / "T-003" / "known_gaps_candidates.json").write_text(json.dumps(doc, indent=1) + "\n")
    print(len(out), "candidates;", names)
    for o in out[:12]:
        print(o)


if __name__ == "__main__":
    sys.exit(main())
