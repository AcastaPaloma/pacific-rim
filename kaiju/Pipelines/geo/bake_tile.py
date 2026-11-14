#!/usr/bin/env python
"""Bake geo_tile@v1 for one or more tile indices (terrain+seabed, water, buildings, roads, faults, manifest).

Also writes the shared CRS file (crs/baytm_v1.wkt) and the global landmark table. Parameterised only by
tile id: any tile of the 68 x 68 Bay grid bakes with no code change once its sources are fetched.
Usage: /opt/miniconda3/envs/kaiju/bin/python -I Pipelines/geo/bake_tile.py --tile L0_30_36 [--offline] [--force]
       [--threads N] [--out DIR] [--shuffle-seed S]
"""
import argparse
import json
import os
import sys
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kaiju_geo import bake  # noqa: E402
from kaiju_geo.common import (BAYTM_ID, BAYTM_PROJ, CRS_DIR, GLOBAL_ROOT, TILES_ROOT, apply_std,  # noqa: E402
                              baytm_compound_crs, canonical_json, lonlat_to_baytm, load_config, parse_tile,
                              std_args, tile_of, write_if_changed)


def write_globals():
    wkt = baytm_compound_crs().to_wkt(version="WKT2_2019", pretty=True)
    write_if_changed(CRS_DIR / "baytm_v1.wkt", (wkt + "\n").encode())
    write_if_changed(CRS_DIR / "baytm_v1.proj4.txt", (f"{BAYTM_ID}\n{BAYTM_PROJ}\n"
                     "base geodetic CRS: NAD83(2011) EPSG:6318; vertical: NAVD88 EPSG:5703 (GEOID18)\n").encode())
    ref = load_config("landmarks_ref.json")
    lms = []
    for r in ref["landmarks"]:
        x, z = lonlat_to_baytm(r["lon"], r["lat"])
        t = tile_of(x, z)
        lms.append(dict(r, baytm_x=round(x, 3), baytm_z=round(z, 3), tile=t.id))
    vps = []
    for v in ref["viewpoints"]:
        v = dict(v)
        if "baytm" not in v:
            x, z = lonlat_to_baytm(v["lon"], v["lat"])
            v["baytm"] = [round(x, 3), round(z, 3)]
        v["tile"] = tile_of(*v["baytm"]).id
        vps.append(v)
    doc = {"spec": "geo_landmarks@v1", "crs": BAYTM_ID, "source": "research/R-004.md §5, §6.7 (transcribed in Pipelines/geo/config/landmarks_ref.json)",
           "landmarks": lms, "viewpoints": vps, "scale_pairs": ref["scale_pairs"], "skyline_camera": ref["skyline_camera"]}
    write_if_changed(GLOBAL_ROOT / "landmarks.json", (canonical_json(doc, indent=1) + "\n").encode())


def main(argv=None):
    ap = std_args(argparse.ArgumentParser(description=__doc__.splitlines()[0]))
    ap.add_argument("--tile", action="append", required=True)
    ap.add_argument("--force", action="store_true", help="re-bake even if the input fingerprint matches")
    ap.add_argument("--threads", type=int, default=1)
    ap.add_argument("--out", default=None, help="output root (default Assets/StreamingAssets/Geo/tiles/v1)")
    ap.add_argument("--shuffle-seed", type=int, default=None, help="test hook: shuffle OSM input order")
    a = ap.parse_args(argv)
    apply_std(a)
    write_globals()
    out = Path(a.out) if a.out else TILES_ROOT
    for t in a.tile:
        r = bake.bake(parse_tile(t), force=a.force, threads=a.threads, out_root=out, shuffle_seed=a.shuffle_seed)
        print(json.dumps({"tile": t, "status": r["status"]}))
    return 0


if __name__ == "__main__":
    sys.exit(main())
