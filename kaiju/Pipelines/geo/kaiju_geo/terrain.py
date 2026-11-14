"""Terrain + seabed: one continuous surface on the geo_tile@v1 node grid (R-004 §1.2, §3).

Nodes sit at x0 + k*spacing, z0 + k*spacing, k = 0..2048 (pixel-is-point), so neighbouring tiles
share edge rows exactly: every node value is a pure function of its world position and the
sources, never of the tile it is baked in (C16 seam rule).
"""
from __future__ import annotations

import numpy as np

from .common import SAMPLES, Tile, baytm_crs, transformer


def node_axes(tile: Tile, margin_nodes: int = 0):
    k = np.arange(-margin_nodes, SAMPLES + margin_nodes, dtype=np.float64)
    return tile.x0 + k * tile.spacing, tile.z0 + k * tile.spacing


def sample_rasters(paths, xs, zs, label=""):
    """Bilinear sample of the first raster (in priority order) that is valid at each node.
    xs, zs: 2D BayTM node coordinates. Returns float64 array with NaN where no source is valid.
    A node is valid only if all four surrounding source cells are valid (no nodata bleed)."""
    import rasterio
    from scipy.ndimage import map_coordinates
    out = np.full(xs.shape, np.nan)
    for p in paths:
        with rasterio.open(p) as src:
            data = src.read(1).astype(np.float64)
            nod = src.nodata
            valid = np.isfinite(data)
            if nod is not None:
                valid &= data != nod
            t = transformer(baytm_crs(), src.crs.to_wkt())
            ex, ny = t.transform(xs.ravel(), zs.ravel())
            inv = ~src.transform
            col, row = inv * (np.asarray(ex), np.asarray(ny))
            # pixel centres are at +0.5: convert to centre-based index space
            col = np.asarray(col) - 0.5
            row = np.asarray(row) - 0.5
        filled = np.where(valid, data, 0.0)
        v = map_coordinates(filled, [row, col], order=1, mode="constant", cval=0.0, prefilter=False)
        # validity: all four bilinear neighbours valid
        r0, c0 = np.floor(row).astype(np.int64), np.floor(col).astype(np.int64)
        ok = (r0 >= 0) & (c0 >= 0) & (r0 + 1 < data.shape[0]) & (c0 + 1 < data.shape[1])
        okv = np.zeros_like(ok)
        idx = np.where(ok)[0]
        rr, cc = r0[idx], c0[idx]
        okv[idx] = valid[rr, cc] & valid[rr + 1, cc] & valid[rr, cc + 1] & valid[rr + 1, cc + 1]
        v = v.reshape(xs.shape)
        okv = okv.reshape(xs.shape)
        take = okv & np.isnan(out)
        out[take] = v[take]
    return out


def merge_surface(cudem, dep3, mhw: float, feather_m: float, spacing: float):
    """R-004 §1.2 merge rule: 3DEP 1 m where valid and CUDEM > MHW, CUDEM elsewhere, feathered
    linearly over `feather_m` metres inside the 3DEP region (Euclidean distance on the node grid).
    Returns (surface float64, weight of 3DEP)."""
    from scipy.ndimage import distance_transform_edt
    use3 = np.isfinite(dep3) & np.isfinite(cudem) & (cudem > mhw)
    d = distance_transform_edt(use3) * spacing
    w = np.clip(d / feather_m, 0.0, 1.0)
    w[~use3] = 0.0
    base = np.where(np.isfinite(cudem), cudem, dep3)
    h = np.where(use3, w * np.nan_to_num(dep3) + (1.0 - w) * np.nan_to_num(cudem), base)
    return h, w


def quantise_r16(h: np.ndarray):
    """geo_tile@v1 terrain.r16: uint16 LE, row 0 = south, col 0 = west,
    h = h_min + v * (h_max - h_min) / 65535 (R-004 §3.2). h is indexed [row=z, col=x] south-up."""
    h_min = float(np.min(h))
    h_max = float(np.max(h))
    span = h_max - h_min if h_max > h_min else 1.0
    v = np.rint((h - h_min) / span * 65535.0)
    v = np.clip(v, 0, 65535).astype("<u2")
    return v, h_min, h_max


def decode_r16(v: np.ndarray, h_min: float, h_max: float) -> np.ndarray:
    return h_min + v.astype(np.float64) * (h_max - h_min) / 65535.0
