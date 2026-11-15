#!/usr/bin/env python
"""T-020b: fetch the first-return grid used to fill default building heights in the bake.

Reuses lidar_ref_v11's EPT reading (3DEP CA_SanFrancisco_1_B23, first returns, noise classes dropped, 1 m cells:
p95, max, multi-return share, count) and writes the same cache file, Pipelines/geo/cache/lidar/rind_v11__<tile>.npz
(git-ignored), which bake.py reads when present. A tile outside the project's extent yields an all-empty grid and a
"lidar unavailable" note; the bake then skips the lidar fill for it.

For the tiles listed in FILL_TILES (Marin and the Golden Gate shore, north of CA_SanFrancisco_1_B23's extent) the
fill source `lidar_ept_fill` (ARRA-CA_GoldenGate_2010, same filter and grid) is fetched to rind_fill__<tile>.npz and
merged into rind_v11__<tile>.npz: cells with no primary first return take the fill source's values. The primary-only
grid is kept as rind_primary__<tile>.npz so the merge is repeatable.
Usage: /opt/miniconda3/envs/kaiju/bin/python -I Pipelines/geo/fetch_lidar_dsm.py --tile L0_26_38 [--offline]
"""
import os
import shutil
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np  # noqa: E402

import lidar_ref_v11  # noqa: E402
from kaiju_geo.common import CACHE, canonical_json, meta_path, parse_tile, read_meta, sha256_file, utc_now  # noqa: E402

FILL_TILES = {"L0_25_38", "L0_26_38", "L0_27_38", "L0_27_37", "L0_26_36"}
TAGS = ("05", "10")


def coverage(path):
    """Share of 1 m cells with at least one first return."""
    z = np.load(path)
    return float(np.count_nonzero(z["count_10"]) / z["count_10"].size)


def merge(primary, fill, dest):
    """Cells without a primary first return take the fill grid's p95/max/multi/count (both grids share bounds)."""
    out = dict(np.load(fill)) if primary is None else dict(np.load(primary))
    if primary is not None:
        f = np.load(fill)
        for tag in TAGS:
            empty = out[f"count_{tag}"] == 0
            for k in ("p95", "max", "multi", "count"):
                out[f"{k}_{tag}"] = np.where(empty, f[f"{k}_{tag}"], out[f"{k}_{tag}"])
    tmp = dest.with_name(dest.name + ".part.npz")
    np.savez_compressed(tmp, **out)
    os.replace(tmp, dest)
    meta = {"url": "merge", "fetched_utc": utc_now(), "sha256": sha256_file(dest), "bytes": dest.stat().st_size,
            "primary": None if primary is None else (read_meta(primary) or {}).get("url"),
            "fill": (read_meta(fill) or {}).get("url"),
            "rule": "cells with count_10/count_05 == 0 in the primary take the fill grid's values"}
    meta_path(dest).write_text(canonical_json(meta, indent=1) + "\n")


def main(argv=None):
    argv = list(sys.argv[1:] if argv is None else argv)
    tid = argv[argv.index("--tile") + 1] if "--tile" in argv else "L0_30_36"
    tile_id = parse_tile(tid).id
    lid = CACHE / "lidar"
    dest = lid / f"rind_v11__{tile_id}.npz"
    prim = lid / f"rind_primary__{tile_id}.npz"
    if tile_id in FILL_TILES and prim.exists():
        rc = 0  # already merged once; the primary grid is kept separately
    else:
        try:
            rc = lidar_ref_v11.main(argv)
        except Exception as e:  # noqa: BLE001  (no points in the window raises inside PDAL on some versions)
            print(f"[fetch_lidar_dsm] {tid}: primary EPT read failed: {e}")
            rc = 3
    if tile_id in FILL_TILES:
        if rc == 0 and dest.exists() and not prim.exists():
            shutil.copyfile(dest, prim)
            shutil.copyfile(meta_path(dest), meta_path(prim))
        rc_fill = lidar_ref_v11.main(argv + ["--source", "lidar_ept_fill", "--out-prefix", "rind_fill"])
        fill = lid / f"rind_fill__{tile_id}.npz"
        if rc_fill == 0 and fill.exists():
            merge(prim if prim.exists() else None, fill, dest)
            rc = 0
            print(f"[fetch_lidar_dsm] {tid}: merged fill source into {dest.name}")
    if rc == 0 and dest.exists():
        cov = coverage(dest)
        print(f"[fetch_lidar_dsm] {tid}: {cov * 100:.1f} % of 1 m cells have first returns"
              + ("" if cov > 0.0 else " -> lidar unavailable for this tile (outside the 3DEP project?); the bake will skip the lidar fill"))
    return rc


if __name__ == "__main__":
    sys.exit(main())
