#!/usr/bin/env python
"""T-111b: terrain and B1 error of the geo_lod@v1 pyramid at the RTIN thresholds of the 150 k cap ladder.

Measurements only; no cap, threshold or gate is changed. For every tile and every ladder threshold
(nominal 0.1 x 2^n m x {1, 1.25, 1.5, 2, 2.5, 3, 4, ...} up to the first that fits 150 k):
  * triangles of the RTIN mesh;
  * mesh error against the pyramid node field (exact at every node: R - h, <= threshold by construction);
  * total error against the L0 truth, same definition as manifest errors.terrain (bilinear of the node values on
    every L0 node, `lod.ErrAccumulator`) with the mesh heights as the node values;
  * projected error at the inner edge of the level's ring (the closest the level is drawn) for the 1080p / 50 deg
    camera of R-101 (f = 1158 px): err_px = err_m x 1158 / d_in, and the distance where err reaches tau = 1/4 px.
B1: triangles and footprint error vs the footprint simplification tolerance (the only B1 lever; independent of the
terrain threshold).
Usage: /opt/miniconda3/envs/kaiju/bin/python -I Pipelines/geo/scripts/measure_lod_error.py --pyr DIR --out evidence/T-111b
"""
import argparse
import csv
import json
import os
import sys
import time
from pathlib import Path

import numpy as np

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
from kaiju_geo import lod  # noqa: E402
from kaiju_geo.common import SAMPLES, TILES_ROOT  # noqa: E402
from kaiju_geo.terrain import decode_r16  # noqa: E402

F_PX = 1158.0                       # 1080p, 50 deg vertical fov (R-101)
RING_IN_M = {1: 1536.0, 2: 3072.0, 3: 6144.0, 4: 12288.0}   # StreamingConfig.RenderRings radius of level n-1 = inner edge of level n
LADDER = [1, 1.25, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10, 12, 16, 24, 32, 64]
CAP = lod.BUDGET["terrain_tris_cap"]


def read_field(d: Path) -> np.ndarray:
    man = json.loads((d / "manifest.json").read_text())
    t = man["terrain"]
    v = np.fromfile(d / t["file"], dtype="<u2").reshape(SAMPLES, SAMPLES)
    return decode_r16(v, t["h_min"], t["h_max"]), man


def rtin_render_heights(h: np.ndarray, thr: float, mi, E) -> np.ndarray:
    """Heights of the RTIN mesh at every node: each leaf triangle is rasterised with its own plane."""
    S = h.shape[0] - 1
    W = S + 1
    hf = np.ascontiguousarray(h, dtype=np.float64).ravel()
    R = np.full(W * W, np.nan)
    a = np.array([[0, 0], [S, S]], dtype=np.int64)
    b = np.array([[S, S], [0, 0]], dtype=np.int64)
    c = np.array([[S, 0], [0, S]], dtype=np.int64)

    def raster(a, b, c):
        if len(a) == 0:
            return
        da, db = a - c, b - c
        K = int(np.max(np.abs(da)))
        ea = da // K
        eb = db // K
        diag = abs(int(ea[0, 0])) == 1 and abs(int(ea[0, 1])) == 1
        u2, v2 = np.meshgrid(np.arange(2 * K + 1), np.arange(2 * K + 1), indexing="ij")
        keep = (u2 + v2 <= 2 * K) & ((((u2 + v2) % 2) == 0) if diag else ((u2 % 2 == 0) & (v2 % 2 == 0)))
        u2, v2 = u2[keep].astype(np.int64), v2[keep].astype(np.int64)
        step = max(1, 4_000_000 // len(u2))
        for s in range(0, len(a), step):
            aa, bb, cc = a[s:s + step], b[s:s + step], c[s:s + step]
            eaa, ebb = (aa - cc) // K, (bb - cc) // K
            px = cc[:, 0:1] + (u2[None, :] * eaa[:, 0:1] + v2[None, :] * ebb[:, 0:1]) // 2
            py = cc[:, 1:2] + (u2[None, :] * eaa[:, 1:2] + v2[None, :] * ebb[:, 1:2]) // 2
            ha, hb, hc = (hf[aa[:, 1] * W + aa[:, 0]], hf[bb[:, 1] * W + bb[:, 0]], hf[cc[:, 1] * W + cc[:, 0]])
            val = hc[:, None] + (u2[None, :] * (ha - hc)[:, None] + v2[None, :] * (hb - hc)[:, None]) / (2.0 * K)
            R[(py * W + px).ravel()] = val.ravel()

    ids = np.arange(2)
    for d in range(len(mi)):
        m = (a + b) // 2
        split = E[mi[d][ids]] > thr
        raster(a[~split], b[~split], c[~split])
        a, b, c, m, ids = a[split], b[split], c[split], m[split], ids[split]
        ids = np.repeat(2 * ids, 2) + np.tile([0, 1], len(ids))
        na = np.empty((2 * len(a), 2), dtype=np.int64)
        nb, nc = np.empty_like(na), np.empty_like(na)
        na[0::2], nb[0::2], nc[0::2] = c, a, m
        na[1::2], nb[1::2], nc[1::2] = b, c, m
        a, b, c = na, nb, nc
    raster(a, b, c)
    R = R.reshape(W, W)
    if np.isnan(R).any():
        raise RuntimeError(f"{int(np.isnan(R).sum())} nodes not covered by the rasteriser")
    return R


def total_error(level, I, J, src, R):
    s = 1 << level
    per = lod.N // s
    acc = lod.ErrAccumulator()
    for (a, b) in lod.contributing(level, I, J, src):
        r0, c0 = (b - J * s) * per, (a - I * s) * per
        acc.add(src.heights(a, b), s, R[r0:r0 + per + 1, c0:c0 + per + 1])
    return acc.result()


def terrain_rows(pyr: Path, src, tile: str, levels_filter=None):
    lv, I, J = (int(x) for x in tile[1:].split("_"))
    d = pyr / f"L{lv}" / f"{I:02d}_{J:02d}"
    h, man = read_field(d)
    nominal = man["terrain"]["rtin_threshold_m"]
    mi, E = lod.rtin_error_pyramid(h)
    rows = []
    fe = total_error(lv, I, J, src, h)                    # field only (no RTIN) = the manifest figure
    for f in LADDER:
        thr = round(nominal * f, 6)
        n = lod.rtin_count(mi, E, thr)
        R = rtin_render_heights(h, thr, mi, E)
        node_err = np.abs(R - h)
        te = total_error(lv, I, J, src, R)
        d_in = RING_IN_M[lv]
        row = {"tile": tile, "level": lv, "factor": f, "threshold_m": thr, "triangles": n, "within_150k": n <= CAP,
               "is_nominal": f == 1, "is_cap_threshold": False,
               "mesh_vs_field_node_max_m": round(float(node_err.max()), 4),
               "mesh_vs_field_node_rms_m": round(float(np.sqrt((node_err ** 2).mean())), 4),
               "field_vs_L0_max_m": fe["max_m"], "field_vs_L0_rms_m": fe["rms_m"], "field_vs_L0_p99_m": fe["p99_m"],
               "total_vs_L0_max_m": te["max_m"], "total_vs_L0_rms_m": te["rms_m"], "total_vs_L0_p99_m": te["p99_m"],
               "ring_inner_m": d_in,
               "px_max_at_ring_inner": round(te["max_m"] * F_PX / d_in, 3),
               "px_rms_at_ring_inner": round(te["rms_m"] * F_PX / d_in, 4),
               "px_p99_at_ring_inner": round(te["p99_m"] * F_PX / d_in, 4),
               "dist_rms_quarter_px_m": round(te["rms_m"] * F_PX * 4, 1),
               "dist_p99_quarter_px_m": round(te["p99_m"] * F_PX * 4, 1)}
        rows.append(row)
        print(f"{tile} x{f:<5} thr {thr:7.3f} tris {n:8d} node max {row['mesh_vs_field_node_max_m']:.3f} "
              f"total max {te['max_m']:.3f} rms {te['rms_m']:.3f} p99 {te['p99_m']:.3f} px(rms@ring) {row['px_rms_at_ring_inner']}", flush=True)
        if n <= CAP:
            row["is_cap_threshold"] = True
            break
    return rows


def b1_rows(src, tile: str, tmp: Path):
    lv, I, J = (int(x) for x in tile[1:].split("_"))
    rows = []
    saved = dict(lod.FOOTPRINT_TOL_M)
    try:
        for fac in (1.0, 1.5, 2.0, 3.0, 4.0, 6.0):
            lod.FOOTPRINT_TOL_M[lv] = saved[lv] * fac
            r = lod.bake_level_tile(lv, I, J, src, tmp / f"b1_{tile}_{fac}", rtin=False, prefilter=False, ground=False)
            b = r["forms"].get("b1")
            if not b:
                continue
            rows.append({"tile": tile, "footprint_tol_m": lod.FOOTPRINT_TOL_M[lv], "factor": fac, "b1_triangles": b["triangles"],
                         "b1_cap": b["tri_cap"], "within_cap": b["within_cap"], "footprint_max_m": b["footprint_max_m"],
                         "footprint_rms_m": b["footprint_rms_m"]})
            print(f"B1 {tile} tol {lod.FOOTPRINT_TOL_M[lv]:.2f} m: {b['triangles']} tris (cap {b['tri_cap']}) max {b['footprint_max_m']} rms {b['footprint_rms_m']}", flush=True)
    finally:
        lod.FOOTPRINT_TOL_M.update(saved)
    return rows


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--pyr", type=Path, required=True, help="baked pyramid root (holds L1..L4)")
    ap.add_argument("--src", type=Path, default=TILES_ROOT)
    ap.add_argument("--out", type=Path, required=True)
    ap.add_argument("--tiles", nargs="*", default=["L1_14_18", "L1_15_18", "L1_12_19", "L1_13_18", "L1_13_19", "L2_06_09", "L3_03_04"])
    ap.add_argument("--b1-tiles", nargs="*", default=["L1_14_18", "L2_07_09"])
    ap.add_argument("--tmp", type=Path, required=True)
    a = ap.parse_args(argv)
    src = lod.L0Source(a.src)
    a.out.mkdir(parents=True, exist_ok=True)
    rows = []
    for t in a.tiles:
        t0 = time.time()
        rows += terrain_rows(a.pyr, src, t)
        print(f"  {t}: {time.time() - t0:.0f} s", flush=True)
    if rows:
        with open(a.out / "terrain_error_vs_threshold.csv", "w", newline="") as fh:
            w = csv.DictWriter(fh, fieldnames=list(rows[0]))
            w.writeheader()
            w.writerows(rows)
    brow = []
    for t in a.b1_tiles:
        brow += b1_rows(src, t, a.tmp)
    if brow:
        with open(a.out / "b1_error_vs_footprint_tolerance.csv", "w", newline="") as fh:
            w = csv.DictWriter(fh, fieldnames=list(brow[0]))
            w.writeheader()
            w.writerows(brow)
    return 0


if __name__ == "__main__":
    sys.exit(main())
