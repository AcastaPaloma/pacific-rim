#!/usr/bin/env python
"""R-ind point products for M-005@v1.1 (research/R-006.md §3): first returns of 3DEP CA_SanFrancisco_1_B23 (EPT),
classes 7, 18, 22 dropped (one filters.expression), gridded on BayTM at 0.5 m (and 1 m):
p95 and max of Z per cell, share of first returns with NumberOfReturns >= 2 (vegetation test), point count.
Writes Pipelines/geo/cache/lidar/rind_v11__<tile>.npz (git-ignored). Reference only; never shipped.
Usage: /opt/miniconda3/envs/kaiju/bin/python -I Pipelines/geo/lidar_ref_v11.py [--tile L0_30_36] [--offline]
"""
import argparse
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np  # noqa: E402

from kaiju_geo import fetch  # noqa: E402
from kaiju_geo.common import CACHE, apply_std, baytm_crs, finish_fetch, is_offline, NetworkForbidden, parse_tile, std_args, transformer, up_to_date  # noqa: E402

EXPR = "ReturnNumber == 1 && Classification != 7 && Classification != 18 && Classification != 22"


def grid_stats(cx, cz, z, multi, n, pct=95):
    """Per-cell p<pct>, max, multi-return share and count on an n x n grid (row = z index, south first)."""
    cid = cz.astype(np.int64) * n + cx.astype(np.int64)
    order = np.lexsort((z, cid))
    cid, zs, ms = cid[order], z[order], multi[order]
    uniq, start, counts = np.unique(cid, return_index=True, return_counts=True)
    k = start + np.clip(np.ceil(pct / 100.0 * counts).astype(np.int64) - 1, 0, None)
    p = np.full(n * n, np.nan, np.float32); mx = np.full(n * n, np.nan, np.float32)
    share = np.full(n * n, np.nan, np.float32); cnt = np.zeros(n * n, np.int32)
    p[uniq] = zs[k]
    mx[uniq] = zs[start + counts - 1]
    share[uniq] = np.add.reduceat(ms.astype(np.float64), start) / counts
    cnt[uniq] = counts
    return p.reshape(n, n), mx.reshape(n, n), share.reshape(n, n), cnt.reshape(n, n)


def main(argv=None):
    ap = std_args(argparse.ArgumentParser(description=__doc__.splitlines()[0]))
    ap.add_argument("--tile", default="L0_30_36")
    ap.add_argument("--source", default="lidar_ept", help="sources.json key of the EPT project (T-020b: lidar_ept_fill)")
    ap.add_argument("--out-prefix", default="rind_v11", help="cache file prefix (T-020b writes the fill source to rind_fill)")
    a = ap.parse_args(argv)
    apply_std(a)
    tile = parse_tile(a.tile)
    dest = CACHE / "lidar" / f"{a.out_prefix}__{tile.id}.npz"
    if up_to_date(dest, "lidar-v11") is not None:
        return 0
    if is_offline():
        raise NetworkForbidden(f"{dest.name} missing from cache and --offline is set")
    import pdal
    x0, z0, x1, z1 = tile.bounds
    t = transformer(baytm_crs(), "EPSG:3857")
    xs, ys = zip(*[t.transform(x, z) for x in (x0 - 16, x1 + 16) for z in (z0 - 16, z1 + 16)])
    bounds = f"([{min(xs):.2f},{max(xs):.2f}],[{min(ys):.2f},{max(ys):.2f}])"
    pipe = [{"type": "readers.ept", "filename": fetch.SRC[a.source]["url"], "bounds": bounds, "resolution": 0.7, "threads": 8},
            {"type": "filters.expression", "expression": EXPR},
            {"type": "filters.reprojection", "out_srs": "EPSG:26910+5703"}]
    p = pdal.Pipeline(json.dumps(pipe))
    n = p.execute()
    if n == 0 or not p.arrays:
        print(f"[lidar-v11] {tile.id}: no points from {a.source} (outside the project's extent)")
        return 3
    arr = p.arrays[0]
    print(f"[lidar-v11] {n} first returns kept; classes {sorted(set(np.unique(arr['Classification']).tolist()))}")
    bx, bz = transformer("EPSG:26910", baytm_crs()).transform(arr["X"], arr["Y"])
    bx, bz = np.asarray(bx), np.asarray(bz)
    zz = arr["Z"].astype(np.float64)
    multi = arr["NumberOfReturns"] >= 2
    out = {"bounds_baytm": np.array([x0, z0, x1, z1]), "points": np.array([n])}
    for res, tag in ((0.5, "05"), (1.0, "10")):
        nn = int(round(2048 / res))
        cx = np.floor((bx - x0) / res); cz = np.floor((bz - z0) / res)
        ok = (cx >= 0) & (cx < nn) & (cz >= 0) & (cz < nn)
        pp, mx, sh, cnt = grid_stats(cx[ok], cz[ok], zz[ok], multi[ok], nn)
        out.update({f"p95_{tag}": pp, f"max_{tag}": mx, f"multi_{tag}": sh, f"count_{tag}": cnt})
    dest.parent.mkdir(parents=True, exist_ok=True)
    tmp = dest.with_name(dest.name + ".part.npz")
    np.savez_compressed(tmp, **out)
    finish_fetch(dest, tmp, fetch.SRC[a.source]["url"], {"points_kept": int(n), "filter": EXPR, "ept_resolution": 0.7,
                                                             "grid": "BayTM cells 0.5 m and 1 m over the tile, row 0 = south"})
    print(f"[lidar-v11] -> {dest}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
