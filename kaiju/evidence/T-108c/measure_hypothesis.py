"""T-108c step 1: does removing buildings/structure from the terrain nodes bring the RTIN count under the 150k cap?
Per urban LOD tile: RTIN triangles at the nominal threshold (0.1 x 2^n m) for (a) the raw node subsample, (b) the nodes with every
node inside a building footprint (dilated by D nodes) replaced by a harmonic fill from the surrounding ground ("ground-only proxy",
the best case for the hypothesis), plus where the high-laplacian nodes sit (inside footprint / within 3 nodes / water edge / elsewhere).
Usage: /opt/miniconda3/envs/kaiju/bin/python -I evidence/T-108c/measure_hypothesis.py > evidence/T-108c/hypothesis.csv"""
import sys, json
sys.path.insert(0, "Pipelines/geo")
import numpy as np
from scipy import ndimage as ndi
import shapely
from shapely.geometry import shape
from rasterio import features, transform
from kaiju_geo import lod
from kaiju_geo.common import TILES_ROOT, Tile, SAMPLES

src = lod.L0Source(TILES_ROOT)
TILES = [(1,12,18),(1,12,19),(1,13,18),(1,13,19),(1,14,17),(1,14,18),(1,15,17),(1,15,18),(2,6,9),(2,7,8),(2,7,9),(3,3,4),(4,1,2)]
CAP = 150_000

def footprint_mask(level, I, J):
    t = Tile(level, I, J); s = t.spacing
    geoms = []
    for (a, b) in lod.contributing(level, I, J, src):
        for f in src.buildings(a, b):
            geoms.append(shape(f["geometry"]))
    # row r = z0 + r*s (south first); rasterio wants north-up, so rasterize flipped then flip back
    tr = transform.from_origin(t.x0 - s / 2, t.z0 + (SAMPLES - 0.5) * s, s, s)
    m = np.zeros((SAMPLES, SAMPLES), bool)
    if geoms:
        m = features.rasterize([(g, 1) for g in geoms], out_shape=(SAMPLES, SAMPLES), transform=tr, fill=0, dtype="uint8", all_touched=True).astype(bool)[::-1]
    return m

def harmonic_fill(h, mask, iters=300):
    idx = ndi.distance_transform_edt(mask, return_distances=False, return_indices=True)
    g = h[tuple(idx)].copy(); g[~mask] = h[~mask]
    ys, xs = np.where(ndi.binary_dilation(mask)); y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
    sub = g[y0:y1, x0:x1]; mk = mask[y0:y1, x0:x1]
    for _ in range(iters):
        p = np.pad(sub, 1, mode="edge")
        avg = 0.25 * (p[:-2, 1:-1] + p[2:, 1:-1] + p[1:-1, :-2] + p[1:-1, 2:])
        sub = np.where(mk, avg, sub)
    g[y0:y1, x0:x1] = sub
    return g

def lap(h):
    p = np.pad(h, 1, mode="edge")
    return np.abs(p[:-2, 1:-1] + p[2:, 1:-1] + p[1:-1, :-2] + p[1:-1, 2:] - 4 * h)

def tri(h, thr):
    v, a, b = lod.quantise_r16(h)
    return lod.rtin_triangles(lod.decode_r16(v, a, b), thr)

print("tile,tris_raw,tris_ground_d1,tris_ground_d3,cap_ok_raw,cap_ok_ground,bldg_area_pct,hi_lap_pct,hi_lap_in_bldg_pct,hi_lap_near_bldg_pct,hi_lap_water_edge_pct,hi_lap_other_pct", flush=True)
for (lv, I, J) in TILES:
    thr = 0.1 * (1 << lv)
    h, cov = lod.subsample_terrain(lv, I, J, src)
    m = footprint_mask(lv, I, J)
    m1 = ndi.binary_dilation(m, iterations=1); m3 = ndi.binary_dilation(m, iterations=3)
    raw = tri(h, thr)
    g1 = tri(harmonic_fill(h, m1), thr) if m1.any() else raw
    g3 = tri(harmonic_fill(h, m3), thr) if m3.any() else raw
    L = lap(h); hi = L > 0.06
    w = np.pad(lod.downsample_water(lv, I, J, src) > 127, ((0, 1), (0, 1)), mode="edge")
    wedge = ndi.binary_dilation(w, iterations=2) & ~ndi.binary_erosion(w, iterations=2)
    nh = max(int(hi.sum()), 1)
    inb = (hi & m1).sum() / nh; near = (hi & m3 & ~m1).sum() / nh; we = (hi & wedge & ~m3).sum() / nh
    oth = 1 - inb - near - we
    print(f"L{lv}_{I:02d}_{J:02d},{raw},{g1},{g3},{raw<=CAP},{g3<=CAP},{100*m.mean():.2f},{100*hi.mean():.2f},{100*inb:.1f},{100*near:.1f},{100*we:.1f},{100*oth:.1f}", flush=True)
