#!/usr/bin/env python
"""Bake the geo_lod@v1 pyramid (L1..L4: terrain node subsample, B1/B2 building forms, water) from baked L0 tiles.

Reads  <--src>/L0/{ii}_{jj}/ (default Assets/StreamingAssets/Geo/tiles/v1), writes <--out>/L{n}/{ii}_{jj}/
(default Pipelines/geo/cache/tiles/v1, git-ignored). Deterministic: a re-run rewrites nothing.
Usage: /opt/miniconda3/envs/kaiju/bin/python -I Pipelines/geo/bake_lod.py [--levels 1 2 3 4] [--tile L2_07_09 ...]
       [--no-rtin] [--no-prefilter] [--no-ground] [--src DIR] [--out DIR] [--report FILE]
"""
import argparse
import json
import os
import sys
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from kaiju_geo import lod  # noqa: E402
from kaiju_geo.common import CACHE, TILES_ROOT, canonical_json, parse_tile  # noqa: E402


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--src", type=Path, default=TILES_ROOT, help="folder holding L0/ (baked geo_tile@v1)")
    ap.add_argument("--out", type=Path, default=CACHE / "tiles" / "v1", help="output root (git-ignored cache)")
    ap.add_argument("--levels", type=int, nargs="*", default=[1, 2, 3, 4])
    ap.add_argument("--tile", nargs="*", default=None, help="restrict to pyramid tiles, e.g. L2_07_09")
    ap.add_argument("--no-rtin", action="store_true", help="skip the RTIN triangle count (faster)")
    ap.add_argument("--no-prefilter", action="store_true", help="skip the T-108b noise pre-filter (the T-108 output)")
    ap.add_argument("--no-ground", action="store_true", help="skip the T-108c ground-only footprint replacement (the T-108b output)")
    ap.add_argument("--report", type=Path, default=None, help="write the per-tile size/time report here (JSON)")
    args = ap.parse_args(argv)
    out = args.out.resolve()
    if "Assets" in out.parts and "StreamingAssets" in out.parts:
        raise SystemExit("refusing to write geo_lod output under StreamingAssets (cache only)")
    src = lod.L0Source(args.src)
    if not src.tiles:
        raise SystemExit(f"no baked L0 tiles under {args.src}/L0")
    want = {parse_tile(t) for t in args.tile} if args.tile else None
    reports = []
    for level in sorted(args.levels):
        for (I, J) in lod.pyramid_tiles(level, src):
            if want is not None and not any(w.level == level and (w.i, w.j) == (I, J) for w in want):
                continue
            r = lod.bake_level_tile(level, I, J, src, out, rtin=not args.no_rtin, prefilter=not args.no_prefilter, ground=not args.no_ground)
            reports.append(r)
            print(f"{r['tile']}: {r['bytes_total'] / 1e6:7.2f} MB  terrain tris {r['terrain_triangles']}  "
                  + "  ".join(f"{k} {v['triangles']} tris" for k, v in r["forms"].items())
                  + f"  gpu~{r['gpu_mb_est']} MB  {r['seconds']['total']} s", flush=True)
    if args.report:
        args.report.write_text(canonical_json(reports, indent=1) + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
