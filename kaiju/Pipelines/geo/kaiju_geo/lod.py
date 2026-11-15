"""geo_lod@v1: the LOD pyramid (R-101 sections 4 and 8.2; T-108).

Builds pyramid tiles L1..L4 from baked `geo_tile@v1` L0 tiles (terrain.r16, water.png, buildings.geojson).
A level-n tile covers 2^n x 2^n L0 tiles, keeps 2049 x 2049 samples (spacing 2^n m) and the same quad-tree
indexing as `Game.World.Streaming` (`GeoTileId`: level, i, j; children of (n,I,J) are (n-1, 2I..2I+1, 2J..2J+1)).

Forms (R-101 section 4):
  T1..T4  terrain r16 = node subsample of the L0 field (pixel-is-point: level-n node (r,c) is the L0 node at
          global index (r,c) x 2^n, so shared edges and parent/child nodes are identical, shorelines and
          ridge tops keep their height) after the ground-only footprint fill (T-108c, `shaped_terrain`: nodes inside
          building footprints take the harmonic fill of the surrounding ground, one-sided, <= height_m + 1 m) and an
          edge-preserving, error-bounded noise pre-filter (T-108b,
          `prefiltered_terrain`: bilateral, change clamped to 0.5 x the RTIN threshold); slope.u8; RTIN triangle
          count measured at 0.1 x 2^n m on the filtered nodes.
  B1      merged building blocks per 256 m chunk, per-building index ranges (L1, L2).
  B2      skyline: exact-height prisms for buildings >= 60 m plus a max-pooled rooftop surface (L2, L3, L4).
  W       water.png: majority vote of the 2^n x 2^n L0 cells.
Everything is deterministic (no clocks, no random, sorted iteration, fixed zlib level); two runs over the same
L0 inputs give byte-identical files. Output goes to the git-ignored cache (`cache/tiles/v1/L{n}/{ii}_{jj}/`).

Placeholders (flagged, tracked): L0 tiles that do not exist (outside the baked Bay set) are filled with open
ocean (terrain = MSL, water = 255) and the manifest records the covered fraction (`coverage`); R-101 O11.

`.lodmesh` (little-endian): header 32 B = 'KLOD', u32 version(1), u32 vertex_count, u32 index_count,
u32 chunk_count, u32 range_count, u32 flags(0), u32 reserved(0); chunk table (48 B each) = i32 cx, i32 cz,
f32 min[3], f32 max[3], u32 vertex_first, u32 vertex_count, u32 index_first, u32 index_count; range table
(16 B each, sorted by (bid_index, chunk_index, first_index)) = u32 bid_index (position in the sorted `bid` list
of b*.index.json), u32 chunk_index, u32 first_index (chunk-local), u32 index_count; then, each padded to 4 B:
positions u16 x 3 per vertex quantised to the chunk AABB, normals oct-encoded int8 x 2 per vertex, u16 indices
(chunk-local). Winding: counter-clockwise normals point outward in the right-handed sense of the stored
(x east, y up, z north) positions, i.e. the Unity front face is the side `cross(b-a, c-a)` points to.
"""
from __future__ import annotations

import io
import json
import math
import struct
import time
from dataclasses import dataclass, field
from pathlib import Path

import numpy as np
import shapely
from PIL import Image
from shapely.geometry import shape
from shapely.geometry.polygon import orient

from .common import SAMPLES, TILE_SIZE_M, Tile, canonical_json, sha256_bytes, sha256_file
from .terrain import decode_r16, quantise_r16

SPEC = "geo_lod@v1"
GENERATOR = "bake_lod.py@3"
MAX_LEVEL = 4
CHUNK_M = 256.0
N = SAMPLES - 1                       # 2048 cells per tile edge
TALL_M = 60.0                         # B2 prism threshold (R-101 section 4, height >= 60 m)
SKIRT_M = 0.5                         # walls reach this far below the lowest ground under the footprint
WATER_NAVD88_M = 0.969

# Footprint simplification tolerance per level (m), R-101 "footprint_max_m 2.0" at B1; coarser levels scale 2x.
FOOTPRINT_TOL_M = {1: 1.0, 2: 2.0, 3: 4.0, 4: 8.0}
FORMS = {1: ("b1",), 2: ("b1", "b2"), 3: ("b2",), 4: ("b2",)}
# R-101 budget numbers (section 4 table, section 1 table). Terrain cap is a hard cap; B1/B2 are "about" numbers
# that this pipeline treats as caps and reports against (never relaxed).
BUDGET = {"terrain_tris_cap": 150_000, "b1_tris_cap": 100_000, "b2_tris_cap": 20_000,
          "gpu_mb_per_tile": {1: 20, 2: 15, 3: 10, 4: 6}}
# GPU byte estimate constants (documented, replaced by T-109 measurements): terrain vertex = float3 pos + float
# parent height + packed normal/slope (20 B), 32-bit indices; building vertex = pos+normal+colour (28 B), 16-bit indices.
TERRAIN_VERTEX_BYTES, TERRAIN_INDEX_BYTES = 20, 4
BUILDING_VERTEX_BYTES, BUILDING_INDEX_BYTES = 28, 2


# ---------------------------------------------------------------- source access
class L0Source:
    """Read-only view of baked L0 tiles under `<root>/L0/{ii}_{jj}/`."""

    def __init__(self, root: Path):
        self.root = Path(root)
        self.tiles: dict[tuple[int, int], Path] = {}
        base = self.root / "L0"
        if base.is_dir():
            for d in sorted(base.iterdir()):
                if d.is_dir() and (d / "manifest.json").exists() and (d / "terrain.r16").exists():
                    a, b = d.name.split("_")
                    self.tiles[(int(a), int(b))] = d

    def has(self, i: int, j: int) -> bool:
        return (i, j) in self.tiles

    def manifest(self, i: int, j: int) -> dict:
        return json.loads((self.tiles[(i, j)] / "manifest.json").read_text())

    def heights(self, i: int, j: int) -> np.ndarray:
        """float64 [row=z (south first), col=x] decoded from terrain.r16."""
        d = self.tiles[(i, j)]
        man = self.manifest(i, j)
        layer = next(L for L in man["layers"] if L["layer"] == "terrain_r16")
        v = np.fromfile(d / "terrain.r16", dtype="<u2").reshape(SAMPLES, SAMPLES)
        return decode_r16(v, layer["h_min_m"], layer["h_max_m"])

    def water(self, i: int, j: int) -> np.ndarray:
        """uint8 [row 0 = north] 2048 x 2048, 255 = water."""
        return np.asarray(Image.open(self.tiles[(i, j)] / "water.png").convert("L"), dtype=np.uint8)

    def buildings(self, i: int, j: int) -> list[dict]:
        return json.loads((self.tiles[(i, j)] / "buildings.geojson").read_text())["features"]

    def landmark_bids(self, i: int, j: int) -> set[str]:
        p = self.tiles[(i, j)] / "landmarks.geojson"
        out: set[str] = set()
        if p.exists():
            for f in json.loads(p.read_text()).get("features", []):
                pr = f.get("properties", {})
                for k in ("bid", "part_of"):
                    if pr.get(k):
                        out.add(pr[k])
        return out


def contributing(level: int, I: int, J: int, src: L0Source) -> list[tuple[int, int]]:
    f = 1 << level
    return [(a, b) for a in range(I * f, (I + 1) * f) for b in range(J * f, (J + 1) * f) if src.has(a, b)]


def pyramid_tiles(level: int, src: L0Source) -> list[tuple[int, int]]:
    f = 1 << level
    return sorted({(a // f, b // f) for (a, b) in src.tiles})


# ---------------------------------------------------------------- terrain
def _owner(src: L0Source, gx: int, gz: int):
    """Single owner of global L0 node (gx, gz): the tile with half-open ownership [x0, x1) x [z0, z1); a node
    whose owner is absent falls back to the west / south / south-west tile's far-edge node, so every level and
    every neighbouring pyramid tile read the same stored value for the same node."""
    ox, oz = gx // N, gz // N
    lx, lz = gx % N, gz % N
    cands = [(ox, oz, lx, lz)]
    if lx == 0:
        cands.append((ox - 1, oz, N, lz))
    if lz == 0:
        cands.append((ox, oz - 1, lx, N))
    if lx == 0 and lz == 0:
        cands.append((ox - 1, oz - 1, N, N))
    for (a, b, x, z) in cands:
        if src.has(a, b):
            return (a, b), x, z
    return None


def subsample_terrain(level: int, I: int, J: int, src: L0Source):
    """Node subsample (R-101 section 4). Returns (h float64 [2049,2049], covered bool [2049,2049]).
    Every node comes from exactly one L0 sample (see `_owner`), so coarser levels reuse finer nodes bit-for-bit."""
    s = 1 << level
    f = s
    h = np.full((SAMPLES, SAMPLES), WATER_NAVD88_M, dtype=np.float64)
    cov = np.zeros((SAMPLES, SAMPLES), dtype=bool)
    per = N // s                                   # pyramid cells per L0 tile edge
    for (a, b) in contributing(level, I, J, src):
        H = src.heights(a, b)
        r0 = (b - J * f) * per
        c0 = (a - I * f) * per
        h[r0:r0 + per, c0:c0 + per] = H[0:N:s, 0:N:s]          # half-open: the far edge belongs to the neighbour
        cov[r0:r0 + per, c0:c0 + per] = True
    # edge lines (first/last row and column of the pyramid grid) resolved through the owner rule
    edge = sorted({(r, c) for r in range(SAMPLES) for c in (0, N)} | {(r, c) for r in (0, N) for c in range(SAMPLES)})
    byTile: dict[tuple[int, int], list] = {}
    for (r, c) in edge:
        gx = I * N * f + c * s
        gz = J * N * f + r * s
        own = _owner(src, gx, gz)
        if own is None:
            h[r, c] = WATER_NAVD88_M
            cov[r, c] = False
            continue
        byTile.setdefault(own[0], []).append((r, c, own[1], own[2]))
    for key in sorted(byTile):
        H = src.heights(*key)
        rows = np.array([t[0] for t in byTile[key]])
        cols = np.array([t[1] for t in byTile[key]])
        lx = np.array([t[2] for t in byTile[key]])
        lz = np.array([t[3] for t in byTile[key]])
        h[rows, cols] = H[lz, lx]
        cov[rows, cols] = True
    return h, cov


class ErrAccumulator:
    """Max / rms / p99 of |L0 - bilinear(coarse nodes)| over every contributing L0 tile (fixed-bin histogram)."""
    BIN = 0.005
    NB = 40000

    def __init__(self):
        self.max = 0.0
        self.sum2 = 0.0
        self.n = 0
        self.hist = np.zeros(self.NB + 1, dtype=np.int64)

    def add(self, H: np.ndarray, s: int, C: np.ndarray | None = None, keep: np.ndarray | None = None):
        """`C` = the coarse nodes of this L0 tile's footprint (default: plain subsample H[::s, ::s]); `keep` = bool
        [2049, 2049] selecting the L0 nodes that count (T-108c: inside / outside the replaced ground zone)."""
        if C is None:
            C = H[::s, ::s]
        m = C.shape[0]
        idx = np.arange(SAMPLES)
        i0 = np.minimum(idx // s, m - 2)
        t = ((idx - i0 * s) / s)[:, None]
        R = C[i0, :] * (1 - t) + C[i0 + 1, :] * t                # rows interpolated -> (2049, m)
        tc = ((idx - i0 * s) / s)[None, :]
        F = R[:, i0] * (1 - tc) + R[:, i0 + 1] * tc
        e = np.abs(H - F)
        if keep is not None:
            e = e[keep]
            if e.size == 0:
                return
        self.max = max(self.max, float(e.max()))
        self.sum2 += float((e * e).sum())
        self.n += e.size
        self.hist += np.bincount(np.minimum((e / self.BIN).astype(np.int64).ravel(), self.NB), minlength=self.NB + 1)

    def result(self) -> dict:
        if self.n == 0:
            return {"max_m": 0.0, "rms_m": 0.0, "p99_m": 0.0}
        c = np.cumsum(self.hist)
        k = int(np.searchsorted(c, 0.99 * self.n))
        return {"max_m": round(self.max, 4), "rms_m": round(math.sqrt(self.sum2 / self.n), 4),
                "p99_m": round((k + 1) * self.BIN, 4)}


def slope_u8(h: np.ndarray, spacing: float) -> np.ndarray:
    """Slope angle, 0..90 deg mapped to 0..255 (step 0.353 deg)."""
    gz, gx = np.gradient(h, spacing)
    deg = np.degrees(np.arctan(np.hypot(gx, gz)))
    return np.clip(np.rint(deg * (255.0 / 90.0)), 0, 255).astype(np.uint8)


def rtin_error_pyramid(h: np.ndarray):
    """Martini-style RTIN error propagation over a (2^k + 1)^2 grid, vectorised per bintree depth.
    Returns (mid_index_per_depth, E) where E[node] is the propagated split error of the hypotenuse midpoint."""
    h = np.ascontiguousarray(h, dtype=np.float32)
    S = h.shape[0] - 1
    W = S + 1
    hf = h.ravel()
    # depth d has 2 * 2^d triangles; children of k are 2k and 2k+1 (left = (c,a,m), right = (b,c,m)).
    a = np.array([[0, 0], [S, S]], dtype=np.int32)
    b = np.array([[S, S], [0, 0]], dtype=np.int32)
    c = np.array([[S, 0], [0, S]], dtype=np.int32)
    ai, bi, mi = [], [], []
    while not np.any(((a + b) & 1) != 0):         # splittable only while the hypotenuse midpoint is a node
        m = (a + b) // 2
        ai.append((a[:, 1] * W + a[:, 0]).astype(np.int32))
        bi.append((b[:, 1] * W + b[:, 0]).astype(np.int32))
        mi.append((m[:, 1] * W + m[:, 0]).astype(np.int32))
        na = np.empty((2 * len(a), 2), dtype=np.int32)
        nb = np.empty_like(na)
        nc = np.empty_like(na)
        na[0::2], nb[0::2], nc[0::2] = c, a, m
        na[1::2], nb[1::2], nc[1::2] = b, c, m
        a, b, c = na, nb, nc
    D = len(mi)
    E = np.zeros(W * W, dtype=np.float32)
    for d in range(D - 1, -1, -1):
        own = np.abs(hf[mi[d]] - 0.5 * (hf[ai[d]] + hf[bi[d]]))
        if d + 1 < D:
            ch = E[mi[d + 1]]
            own = np.maximum(own, np.maximum(ch[0::2], ch[1::2]))
        np.maximum.at(E, mi[d], own)
    return mi, E


def rtin_count(mi, E, thr: float) -> int:
    """Triangles of the RTIN mesh at vertical error threshold `thr` (a triangle splits iff E[mid] > thr)."""
    active = np.ones(2, dtype=bool)
    leaves = 0
    for d in range(len(mi)):
        split = active & (E[mi[d]] > thr)
        leaves += int(np.count_nonzero(active & ~split))
        active = np.repeat(split, 2)
    return leaves + int(np.count_nonzero(active))     # unit triangles below the last splittable depth


def rtin_triangles(h: np.ndarray, thr: float) -> int:
    mi, E = rtin_error_pyramid(h)
    return rtin_count(mi, E, thr)


def rtin_for_cap(h: np.ndarray, thr: float, cap: int):
    """(triangles at thr, smallest threshold in the ladder thr x {1, 1.25, 1.5, 2, 2.5, 3, 4, ...} whose mesh
    fits `cap`, triangles there). The ladder is geometric so the result is deterministic and cheap."""
    mi, E = rtin_error_pyramid(h)
    nominal = rtin_count(mi, E, thr)
    if nominal <= cap:
        return nominal, thr, nominal
    t = thr
    n = nominal
    steps = [1.25, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10, 12, 16, 24, 32, 64]
    for f in steps:
        t = round(thr * f, 6)
        n = rtin_count(mi, E, t)
        if n <= cap:
            break
    return nominal, t, n


# ---------------------------------------------------------------- noise pre-filter (T-108b)
# Edge-preserving, error-bounded pre-filter of the 1 m lidar noise, applied to the pyramid terrain nodes before
# quantisation, slope and RTIN. Spatial radius PREFILTER_RADIUS nodes, range kernel sigma = PREFILTER_SIGMA x the
# nominal RTIN threshold (steps taller than that are edges and stay), and a hard clamp |filtered - node| <=
# PREFILTER_EPS x threshold, so the vertical error added at level n is <= 0.5 x 0.1 x 2^n m and the RTIN
# threshold itself is never raised. A pointwise function of a (2r+1)^2 window, so neighbouring tiles agree on
# shared edges once each tile is given an r-node halo from its neighbours.
PREFILTER_RADIUS = 2
PREFILTER_SIGMA = 4.0
PREFILTER_EPS = 0.5


def bilateral_bounded(h: np.ndarray, eps: float, sigma_r: float, radius: int = PREFILTER_RADIUS) -> np.ndarray:
    """Bilateral smooth of `h` (float64, any 2-D shape) with the change clamped to +-eps; the output has the
    same shape (the caller pads with a halo and crops, so no border handling is needed inside)."""
    r = radius
    ny, nx = h.shape
    p = np.pad(h, r, mode="edge")
    num = np.zeros_like(h)
    den = np.zeros_like(h)
    sp = 2.0 * (r / 1.5) ** 2
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            q = p[r + dy:r + dy + ny, r + dx:r + dx + nx]
            w = np.exp(-0.5 * ((q - h) / sigma_r) ** 2 - (dx * dx + dy * dy) / sp)
            num += w * q
            den += w
    return h + np.clip(num / den - h, -eps, eps)


def _halo_terrain(level: int, I: int, J: int, src: L0Source, r: int):
    """(big [2049 + 2r]^2, centre raw h, centre covered): the tile's node subsample with an r-node halo taken from
    the 8 neighbouring pyramid tiles' node subsamples (open ocean where nothing is baked)."""
    big = np.full((SAMPLES + 2 * r, SAMPLES + 2 * r), WATER_NAVD88_M, dtype=np.float64)
    h0 = cov0 = None
    for dj in (-1, 0, 1):
        for di in (-1, 0, 1):
            ni, nj = I + di, J + dj
            if ni < 0 or nj < 0:
                continue
            if di == 0 and dj == 0:
                h, cov = subsample_terrain(level, I, J, src)
                h0, cov0 = h, cov
            elif contributing(level, ni, nj, src):
                h, _ = subsample_terrain(level, ni, nj, src)
            else:
                continue
            # neighbour node (r, c) sits at big[r + R0, c + C0], tile origin = (r, r) in big
            R0 = r + dj * N
            C0 = r + di * N
            r_lo, r_hi = max(0, -R0), min(SAMPLES, big.shape[0] - R0)
            c_lo, c_hi = max(0, -C0), min(SAMPLES, big.shape[1] - C0)
            if r_lo >= r_hi or c_lo >= c_hi:
                continue
            blk = big[R0 + r_lo:R0 + r_hi, C0 + c_lo:C0 + c_hi]
            if di == 0 and dj == 0:
                blk[...] = h[r_lo:r_hi, c_lo:c_hi]
            else:
                # only nodes the centre tile does not already hold (the shared edge line is the centre's own)
                ok = np.ones(blk.shape, dtype=bool)
                rr = np.arange(R0 + r_lo, R0 + r_hi)
                cc = np.arange(C0 + c_lo, C0 + c_hi)
                ok &= ~(((rr >= r) & (rr < r + SAMPLES))[:, None] & ((cc >= r) & (cc < r + SAMPLES))[None, :])
                blk[ok] = h[r_lo:r_hi, c_lo:c_hi][ok]
    return big, h0, cov0


# ---------------------------------------------------------------- ground-only far field (T-108c)
# R-101 L1+ far-field terrain should carry the ground, not the buildings: the 3DEP "bare earth" DEM still holds
# building residue in dense blocks (measured, evidence/T-108c/hypothesis.csv: up to 70 % of the nodes with a
# laplacian > 6 cm sit in or within 3 nodes of a footprint on downtown tiles), and the B1/B2 forms already draw the
# buildings from their own base + height (roofs are absolute NAVD88 tops, not terrain + height). Nodes inside a
# footprint (rasterised on the node grid, every cell touched, dilated by GROUND_DILATE_M) form 4-connected
# components; each component whose bounding box is at most GROUND_MAX_COMPONENT_M on both axes is replaced by the
# harmonic (Laplace) interpolation of the ring of ground nodes around it, so by the maximum principle the fill never
# leaves the min / max of that ring; the change is one-sided and clamped: a node only goes down, by at most the height_m + 1 m
# of the tallest building covering it (a DEM cannot hold more building residue than the building is tall). Larger components (contiguous mega blocks) stay as in L0. The fill of a
# component is a function of the component and its ring only, solved per component in raster order, and the halo is
# larger than the component limit, so neighbouring pyramid tiles agree bit for bit on shared edges. L0 is not
# touched. Nodes outside the dilated footprints are identical to the pre-ground field.
GROUND_MAX_COMPONENT_M = 1024.0
GROUND_DILATE_M = 4.0           # swept 2 / 4 / 6 m x 512 / 1024 m on the downtown tiles (evidence/T-108c/sweep.csv)


def ground_params(level: int):
    """(max component extent in nodes, dilation in nodes)."""
    s = float(1 << level)
    return max(2, int(round(GROUND_MAX_COMPONENT_M / s))), max(1, int(math.ceil(GROUND_DILATE_M / s)))


def footprint_cap_window(level: int, I: int, J: int, src: L0Source, g: int, dilate: int) -> np.ndarray:
    """float [2049 + 2g]^2 (row = z south first, col = x): for a node inside a footprint of any L0 building whose
    tile overlaps the window (+ 400 m for straddlers), dilated by `dilate` nodes, the largest `height_m + 1 m` of
    the buildings covering it (the most building residue the DEM can hold there); 0 elsewhere."""
    from rasterio import features, transform
    from scipy import ndimage as ndi
    t = Tile(level, I, J)
    s = t.spacing
    W = SAMPLES + 2 * g
    xa, xb = t.x0 - g * s - 400.0, t.x0 + (N + g) * s + 400.0
    za, zb = t.z0 - g * s - 400.0, t.z0 + (N + g) * s + 400.0
    geoms = []
    for (a, b) in sorted(src.tiles):
        if a * TILE_SIZE_M > xb or (a + 1) * TILE_SIZE_M < xa or b * TILE_SIZE_M > zb or (b + 1) * TILE_SIZE_M < za:
            continue
        for f in src.buildings(a, b):
            hh = float(f["properties"].get("height_m") or 0.0)
            geoms.append((hh + 1.0, f["id"], shape(f["geometry"])))
    cap = np.zeros((W, W), dtype=np.float64)
    if geoms:
        geoms.sort(key=lambda q: (q[0], q[1]))         # ascending: where footprints overlap the tallest is burned last
        tr = transform.from_origin(t.x0 - g * s - s / 2, t.z0 + (N + g + 0.5) * s, s, s)
        cap = features.rasterize([(q[2], q[0]) for q in geoms], out_shape=(W, W), transform=tr, fill=0.0,
                                 dtype="float64", all_touched=True)[::-1]
        cap = np.ascontiguousarray(ndi.maximum_filter(cap, size=2 * dilate + 1, mode="constant", cval=0.0))
    return cap


def harmonic_fill_components(h: np.ndarray, mask: np.ndarray, max_extent: int):
    """Replace the 4-connected components of `mask` (bbox <= max_extent nodes on both axes, not touching the array
    border) by the harmonic fill of their ring. Returns (filled copy of h, bool mask of the nodes replaced)."""
    from scipy import ndimage as ndi
    from scipy.sparse import coo_matrix
    from scipy.sparse.linalg import spsolve
    out = h.copy()
    done = np.zeros(mask.shape, dtype=bool)
    lab, n = ndi.label(mask)                       # 4-connectivity
    H, W = mask.shape
    for k, sl in enumerate(ndi.find_objects(lab), start=1):
        y0, y1, x0, x1 = sl[0].start, sl[0].stop, sl[1].start, sl[1].stop
        if y0 == 0 or x0 == 0 or y1 == H or x1 == W or (y1 - y0) > max_extent or (x1 - x0) > max_extent:
            continue
        sub = lab[y0 - 1:y1 + 1, x0 - 1:x1 + 1] == k
        hv = h[y0 - 1:y1 + 1, x0 - 1:x1 + 1]
        inner = sub[1:-1, 1:-1]
        ids = -np.ones(sub.shape, dtype=np.int64)
        ys, xs = np.nonzero(sub)                        # raster order
        m = len(ys)
        ids[ys, xs] = np.arange(m)
        rhs = np.zeros(m)
        rows, cols, vals = [np.arange(m)], [np.arange(m)], [np.full(m, 4.0)]
        for dy, dx in ((-1, 0), (1, 0), (0, -1), (0, 1)):
            ny, nx = ys + dy, xs + dx
            nid = ids[ny, nx]
            inside = nid >= 0
            rows.append(np.nonzero(inside)[0])
            cols.append(nid[inside])
            vals.append(np.full(int(inside.sum()), -1.0))
            rhs[~inside] += hv[ny[~inside], nx[~inside]]
        A = coo_matrix((np.concatenate(vals), (np.concatenate(rows), np.concatenate(cols))), shape=(m, m)).tocsc()
        x = spsolve(A, rhs) if m > 1 else rhs / 4.0
        blk = out[y0 - 1:y1 + 1, x0 - 1:x1 + 1]
        blk[ys, xs] = x
        done[y0 - 1:y1 + 1, x0 - 1:x1 + 1] |= sub
    return out, done


def shaped_terrain(level: int, I: int, J: int, src: L0Source, threshold: float | None = None,
                   prefilter: bool = True, ground: bool = True):
    """(h final [2049,2049], raw h, covered, stats). Order: ground-only footprint replacement (T-108c), then the
    bounded bilateral pre-filter (T-108b, change measured against the ground field). stats["_mask"] is the centre
    replaced-node mask (not for the manifest)."""
    thr = 0.1 * (1 << level) if threshold is None else threshold
    r = PREFILTER_RADIUS if prefilter else 0
    C, D = ground_params(level)
    g = (C + 2 if ground else 0) + r
    big, h0, cov0 = _halo_terrain(level, I, J, src, g)
    stats: dict = {}
    mask_c = None
    if ground:
        capw = footprint_cap_window(level, I, J, src, g, D)
        mask = capw > 0
        filled, done = harmonic_fill_components(big, mask, C)
        # one-sided and bounded: the node only goes down, by at most the height of the building(s) over it + 1 m
        big = np.where(done, big - np.clip(big - filled, 0.0, capw), big)
        gc = big[g:g + SAMPLES, g:g + SAMPLES]
        mask_c = done[g:g + SAMPLES, g:g + SAMPLES] & cov0
        gc = np.where(mask_c, gc, h0)
        dh = gc - h0
        kept = mask[g:g + SAMPLES, g:g + SAMPLES] & ~mask_c
        stats["ground"] = {"method": "footprint_harmonic_fill", "max_component_m": C * (1 << level),
                           "dilate_nodes": D, "nodes_replaced": int(mask_c.sum()),
                           "replaced_pct": round(100.0 * float(mask_c.mean()), 3),
                           "footprint_nodes_kept_pct": round(100.0 * float(kept.sum()) / max(int(mask[g:g + SAMPLES, g:g + SAMPLES].sum()), 1), 2),
                           "max_drop_m": round(float(-dh.min()), 4), "bound": "drop <= height_m + 1 m of the covering building; never raised",
                           "mean_abs_change_m": round(float(np.abs(dh[mask_c]).mean()) if mask_c.any() else 0.0, 4)}
        big = big[g - r:g - r + SAMPLES + 2 * r, g - r:g - r + SAMPLES + 2 * r] if r else gc
        base = gc
    else:
        base = h0
    if prefilter:
        eps = PREFILTER_EPS * thr
        f = bilateral_bounded(big, eps, PREFILTER_SIGMA * thr)[r:r + SAMPLES, r:r + SAMPLES]
        f = np.where(cov0, f, base)
        d = np.abs(f - base)
        stats["prefilter"] = {"method": "bilateral_clamped", "radius_nodes": r, "sigma_m": round(PREFILTER_SIGMA * thr, 6),
                              "eps_m": round(eps, 6), "max_abs_change_m": round(float(d.max()), 6),
                              "mean_abs_change_m": round(float(d.mean()), 6)}
    else:
        f = base
    stats["_mask"] = mask_c
    return f, h0, cov0, stats


def prefiltered_terrain(level: int, I: int, J: int, src: L0Source, threshold: float | None = None):
    """T-108b entry point (no ground replacement): (filtered h, raw h, covered, prefilter stats)."""
    f, h0, cov0, st = shaped_terrain(level, I, J, src, threshold, prefilter=True, ground=False)
    return f, h0, cov0, st["prefilter"]


# ---------------------------------------------------------------- water
def downsample_water(level: int, I: int, J: int, src: L0Source) -> np.ndarray:
    s = 1 << level
    f = s
    per = N // s
    out = np.full((N, N), 255, dtype=np.uint8)     # missing L0 = open ocean placeholder
    for (a, b) in contributing(level, I, J, src):
        w = src.water(a, b) > 127
        cnt = w.reshape(per, s, per, s).sum(axis=(1, 3))
        blk = np.where(cnt * 2 > s * s, 255, 0).astype(np.uint8)
        row_blk = (J * f + f - 1 - b)              # block rows run north to south
        r0 = row_blk * per
        c0 = (a - I * f) * per
        out[r0:r0 + per, c0:c0 + per] = blk
    return out


def png_bytes(arr: np.ndarray) -> bytes:
    bio = io.BytesIO()
    Image.fromarray(arr, "L").save(bio, format="PNG", compress_level=9, optimize=False)
    return bio.getvalue()


# ---------------------------------------------------------------- meshing helpers
def _ear_clip(pts: list[tuple[float, float]]) -> list[tuple[int, int, int]]:
    """Triangulate a simple counter-clockwise ring (no holes). Falls back to a fan if no ear is found."""
    n = len(pts)
    idx = list(range(n))
    tris: list[tuple[int, int, int]] = []

    def cross(o, p, q):
        return (p[0] - o[0]) * (q[1] - o[1]) - (p[1] - o[1]) * (q[0] - o[0])

    guard = 0
    while len(idx) > 3 and guard < 4 * n * n:
        guard += 1
        m = len(idx)
        for k in range(m):
            i0, i1, i2 = idx[k - 1], idx[k], idx[(k + 1) % m]
            p0, p1, p2 = pts[i0], pts[i1], pts[i2]
            if cross(p0, p1, p2) <= 1e-12:
                continue
            ok = True
            for t in idx:
                if t in (i0, i1, i2):
                    continue
                q = pts[t]
                if cross(p0, p1, q) >= 0 and cross(p1, p2, q) >= 0 and cross(p2, p0, q) >= 0:
                    ok = False
                    break
            if ok:
                tris.append((i0, i1, i2))
                idx.pop(k)
                break
        else:
            break
    if len(idx) >= 3:
        for k in range(1, len(idx) - 1):
            tris.append((idx[0], idx[k], idx[k + 1]))
    return tris


def _oct_encode(nrm: np.ndarray) -> np.ndarray:
    n = nrm / np.maximum(np.abs(nrm).sum(axis=1, keepdims=True), 1e-12)
    x, y, z = n[:, 0].copy(), n[:, 1].copy(), n[:, 2].copy()
    neg = z < 0
    ox = np.where(neg, (1 - np.abs(y)) * np.where(x >= 0, 1.0, -1.0), x)
    oy = np.where(neg, (1 - np.abs(x)) * np.where(y >= 0, 1.0, -1.0), y)
    return np.clip(np.rint(np.stack([ox, oy], axis=1) * 127.0), -127, 127).astype(np.int8)


def oct_decode(q: np.ndarray) -> np.ndarray:
    f = q.astype(np.float64) / 127.0
    x, y = f[:, 0], f[:, 1]
    z = 1 - np.abs(x) - np.abs(y)
    t = np.clip(-z, 0, None)
    x = x - np.where(x >= 0, t, -t)
    y = y - np.where(y >= 0, t, -t)
    n = np.stack([x, y, z], axis=1)
    return n / np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-12)


@dataclass
class _Chunk:
    verts: list = field(default_factory=list)      # arrays (k,3) float64
    norms: list = field(default_factory=list)      # arrays (k,3)
    idx: list = field(default_factory=list)        # arrays (m,) chunk-local
    nv: int = 0
    ni: int = 0


class MeshBuilder:
    """Accumulates triangles per 256 m chunk and records per-building index ranges."""

    def __init__(self, x0: float, z0: float):
        self.x0, self.z0 = x0, z0
        self.chunks: dict[tuple[int, int], _Chunk] = {}
        self.ranges: list[tuple[str, tuple[int, int], int, int]] = []   # bid, chunk, first, count

    def chunk_of(self, x: float, z: float) -> tuple[int, int]:
        return (int(math.floor((x - self.x0) / CHUNK_M)), int(math.floor((z - self.z0) / CHUNK_M)))

    def add(self, ck, verts: np.ndarray, tris: np.ndarray, bid: str | None):
        """verts (k,3) world coordinates (x east, y up, z north); tris (m,3) local indices, any winding;
        normals are the flat face normals, windings are fixed so the normal is the cross-product direction."""
        ch = self.chunks.setdefault(ck, _Chunk())
        if ch.nv + len(verts) > 65535:
            raise ValueError(f"chunk {ck} exceeds 65535 vertices")
        a, b, c = verts[tris[:, 0]], verts[tris[:, 1]], verts[tris[:, 2]]
        nrm = np.cross(b - a, c - a)
        ln = np.linalg.norm(nrm, axis=1)
        keep = ln > 1e-9
        tris, nrm, ln = tris[keep], nrm[keep], ln[keep]
        if len(tris) == 0:
            return
        face = nrm / ln[:, None]
        # per-vertex flat normal: vertices of a face share it; walls and roofs are separate vertex sets, so
        # average over incident faces is exact for those and smooth for nothing else.
        vn = np.zeros((len(verts), 3))
        np.add.at(vn, tris[:, 0], face)
        np.add.at(vn, tris[:, 1], face)
        np.add.at(vn, tris[:, 2], face)
        vn /= np.maximum(np.linalg.norm(vn, axis=1, keepdims=True), 1e-12)
        first = ch.ni
        ch.verts.append(verts)
        ch.norms.append(vn)
        ch.idx.append((tris.ravel() + ch.nv).astype(np.uint16))
        ch.nv += len(verts)
        ch.ni += tris.size
        if bid is not None:
            self.ranges.append((bid, ck, first, tris.size))

    @property
    def triangles(self) -> int:
        return sum(c.ni for c in self.chunks.values()) // 3

    @property
    def vertices(self) -> int:
        return sum(c.nv for c in self.chunks.values())

    def encode(self, bids_sorted: list[str]) -> bytes:
        keys = sorted(self.chunks)
        cpos = {k: n for n, k in enumerate(keys)}
        bpos = {b: n for n, b in enumerate(bids_sorted)}
        table, pos_b, nrm_b, idx_b = [], [], [], []
        vf = idf = 0
        for k in keys:
            ch = self.chunks[k]
            v = np.concatenate(ch.verts).astype(np.float64)
            lo = v.min(axis=0).astype(np.float32)
            hi = v.max(axis=0).astype(np.float32)
            span = np.where(hi - lo > 0, hi.astype(np.float64) - lo.astype(np.float64), 1.0)
            q = np.clip(np.rint((v - lo.astype(np.float64)) / span * 65535.0), 0, 65535).astype("<u2")
            pos_b.append(q.tobytes())
            nrm_b.append(_oct_encode(np.concatenate(ch.norms)).tobytes())
            idx_b.append(np.concatenate(ch.idx).astype("<u2").tobytes())
            table.append(struct.pack("<ii3f3fIIII", k[0], k[1], *lo, *hi, vf, ch.nv, idf, ch.ni))
            vf += ch.nv
            idf += ch.ni
        rng = sorted((bpos[b], cpos[c], f, n) for b, c, f, n in self.ranges)
        out = bytearray(struct.pack("<4sIIIIIII", b"KLOD", 1, vf, idf, len(keys), len(rng), 0, 0))
        for t in table:
            out += t
        for r in rng:
            out += struct.pack("<IIII", *r)
        for blob in (b"".join(pos_b), b"".join(nrm_b), b"".join(idx_b)):
            out += blob + b"\0" * (-len(blob) % 4)
        return bytes(out)


def read_lodmesh(data: bytes) -> dict:
    """Decoder used by the validator and tests (mirrors the C# reader of T-109)."""
    magic, ver, vc, ic, cc, rc, _fl, _rs = struct.unpack_from("<4sIIIIIII", data, 0)
    if magic != b"KLOD" or ver != 1:
        raise ValueError("not a KLOD v1 file")
    off = 32
    chunks = []
    for _ in range(cc):
        cx, cz, *rest = struct.unpack_from("<ii3f3fIIII", data, off)
        chunks.append({"cx": cx, "cz": cz, "min": rest[0:3], "max": rest[3:6], "vf": rest[6], "vc": rest[7],
                       "if": rest[8], "ic": rest[9]})
        off += 48
    ranges = [struct.unpack_from("<IIII", data, off + 16 * k) for k in range(rc)]
    off += 16 * rc
    pos = np.frombuffer(data, "<u2", vc * 3, off).reshape(vc, 3)
    off += vc * 6 + (-vc * 6 % 4)
    nrm = np.frombuffer(data, np.int8, vc * 2, off).reshape(vc, 2)
    off += vc * 2 + (-vc * 2 % 4)
    idx = np.frombuffer(data, "<u2", ic, off)
    verts = np.empty((vc, 3))
    for c in chunks:
        lo, hi = np.array(c["min"], dtype=np.float64), np.array(c["max"], dtype=np.float64)
        span = np.where(hi - lo > 0, hi - lo, 1.0)
        verts[c["vf"]:c["vf"] + c["vc"]] = lo + pos[c["vf"]:c["vf"] + c["vc"]].astype(np.float64) / 65535.0 * span
    return {"vertices": verts, "normals": oct_decode(nrm), "indices": idx, "chunks": chunks, "ranges": ranges,
            "vertex_count": vc, "index_count": ic}


# ---------------------------------------------------------------- buildings
@dataclass
class Piece:
    bid: str
    ring: np.ndarray           # (n,2) ccw, closed ring without the repeated vertex
    ground: float              # lowest ground under the footprint (NAVD88 m)
    bottom: float
    top: float
    landmark: bool
    err_m: float               # Hausdorff distance original vs simplified exterior
    chunk: tuple[int, int] | None = None


def collect_pieces(feats: list[dict], landmarks: set[str]):
    """Renderable (geometry, props, bid) triples following the L0 rules: excluded skipped; a structure with parts
    renders through its parts plus remainder_geometry (with the structure's height); others render their outline."""
    out = []
    for f in feats:
        p = f["properties"]
        if p.get("excluded"):
            continue
        if p.get("has_parts"):
            rem = p.get("remainder_geometry")
            if rem:
                out.append((rem, p, p["bid"]))
            continue
        out.append((f["geometry"], p, p["bid"]))
    return out


def _simplify_pieces(items, tol: float, tall_only: bool | None, landmarks: set[str], mb: MeshBuilder):
    """-> list[Piece]. tall_only True: only height >= TALL_M; False: only below; None: all."""
    pieces: list[Piece] = []
    for geom, p, bid in sorted(items, key=lambda t: t[2]):
        h = float(p["height_m"])
        if tall_only is True and h < TALL_M:
            continue
        if tall_only is False and h >= TALL_M:
            continue
        g = shape(geom)
        polys = list(g.geoms) if g.geom_type == "MultiPolygon" else [g]
        base = float(p.get("base_navd88_m", 0.0))
        gmin = float(p.get("ground_min_navd88_m", base))
        top = base + h
        bottom = (gmin if float(p.get("min_height_m") or 0.0) <= 0 else base + float(p["min_height_m"])) - SKIRT_M
        for poly in polys:
            if poly.is_empty or poly.area < 1e-6:
                continue
            ext = shapely.Polygon(poly.exterior)
            s = shapely.simplify(ext, tol, preserve_topology=True) if tol > 0 else ext
            if s.is_empty or s.geom_type != "Polygon" or s.area < 1e-6:
                s = ext
            s = orient(s, 1.0)
            ring = np.asarray(s.exterior.coords)[:-1]
            if len(ring) < 3:
                continue
            err = float(shapely.hausdorff_distance(ext.exterior, s.exterior))
            pieces.append(Piece(bid, ring, gmin, bottom, top, bid in landmarks or p.get("part_of") in landmarks,
                                err))
            c = s.representative_point()
            pieces[-1].chunk = mb.chunk_of(c.x, c.y)
    return pieces


def _extrude(piece: Piece):
    ring = piece.ring
    n = len(ring)
    # walls: 4 vertices per edge
    p0 = ring
    p1 = np.roll(ring, -1, axis=0)
    v = np.empty((4 * n + n, 3))
    v[0:4 * n:4] = np.column_stack([p0[:, 0], np.full(n, piece.bottom), p0[:, 1]])
    v[1:4 * n:4] = np.column_stack([p1[:, 0], np.full(n, piece.bottom), p1[:, 1]])
    v[2:4 * n:4] = np.column_stack([p1[:, 0], np.full(n, piece.top), p1[:, 1]])
    v[3:4 * n:4] = np.column_stack([p0[:, 0], np.full(n, piece.top), p0[:, 1]])
    base = 4 * np.arange(n)
    # outward normal for a ccw (x east, z north) ring is (dz, -dx); pick the winding whose cross matches it
    wt = np.empty((2 * n, 3), dtype=np.int64)
    wt[0::2] = np.column_stack([base, base + 3, base + 2])
    wt[1::2] = np.column_stack([base, base + 2, base + 1])
    d = p1 - p0
    want = np.column_stack([d[:, 1], np.zeros(n), -d[:, 0]])
    a, b, c = v[wt[0::2, 0]], v[wt[0::2, 1]], v[wt[0::2, 2]]
    flip = np.einsum("ij,ij->i", np.cross(b - a, c - a), want) < 0
    for k in np.nonzero(flip)[0]:
        wt[2 * k] = wt[2 * k][[0, 2, 1]]
        wt[2 * k + 1] = wt[2 * k + 1][[0, 2, 1]]
    # roof
    v[4 * n:, 0] = ring[:, 0]
    v[4 * n:, 1] = piece.top
    v[4 * n:, 2] = ring[:, 1]
    rt = np.array(_ear_clip([tuple(q) for q in ring]), dtype=np.int64).reshape(-1, 3) + 4 * n
    if len(rt):
        a, b, c = v[rt[:, 0]], v[rt[:, 1]], v[rt[:, 2]]
        up = np.cross(b - a, c - a)[:, 1] < 0
        rt[up] = rt[up][:, [0, 2, 1]]
    return v, np.vstack([wt, rt]) if len(rt) else wt


def build_prisms(pieces: list[Piece], mb: MeshBuilder):
    for p in pieces:
        v, t = _extrude(p)
        mb.add(p.chunk, v, t, p.bid)


def building_index(pieces: list[Piece], form: str, tile: Tile) -> dict:
    by: dict[str, dict] = {}
    for p in pieces:
        e = by.setdefault(p.bid, {"bid": p.bid, "chunk": list(p.chunk), "h_min": p.ground,
                                  "h_max": p.top, "landmark": False})
        e["h_min"] = min(e["h_min"], p.ground)
        e["h_max"] = max(e["h_max"], p.top)
        e["landmark"] = e["landmark"] or p.landmark
    rows = [by[k] for k in sorted(by)]
    for r in rows:
        r["h_min"] = round(r["h_min"], 3)
        r["h_max"] = round(r["h_max"], 3)
    return {"spec": SPEC, "form": form, "tile": tile.id, "chunk_m": CHUNK_M, "buildings": rows}


def rooftop_surface(items, level: int, tile: Tile, hterr: np.ndarray, tri_cap: int, mb: MeshBuilder):
    """Max-pooled rooftop surface of the buildings below TALL_M (R-101 B2). Base cells are 2 x 2^level m (1024^2
    per tile); the cell size doubles while the mesh would exceed `tri_cap` (never above CHUNK_M). Returns
    (cell_m, columns, triangles_cap_ok)."""
    n0 = 1024
    cell0 = tile.size / n0
    top = np.full((n0, n0), -np.inf)
    gnd = np.full((n0, n0), np.inf)
    for geom, p, bid in sorted(items, key=lambda t: t[2]):
        h = float(p["height_m"])
        if h >= TALL_M:
            continue
        g = shape(geom)
        t_top = float(p.get("base_navd88_m", 0.0)) + h
        g_min = float(p.get("ground_min_navd88_m", p.get("base_navd88_m", 0.0)))
        minx, minz, maxx, maxz = g.bounds
        i0 = max(0, int(math.floor((minx - tile.x0) / cell0)))
        i1 = min(n0 - 1, int(math.floor((maxx - tile.x0) / cell0)))
        j0 = max(0, int(math.floor((minz - tile.z0) / cell0)))
        j1 = min(n0 - 1, int(math.floor((maxz - tile.z0) / cell0)))
        if i1 < i0 or j1 < j0:
            continue
        ii, jj = np.meshgrid(np.arange(i0, i1 + 1), np.arange(j0, j1 + 1), indexing="ij")
        boxes = shapely.box(tile.x0 + ii * cell0, tile.z0 + jj * cell0, tile.x0 + (ii + 1) * cell0,
                            tile.z0 + (jj + 1) * cell0)
        hit = shapely.intersects(g, boxes)
        for a, b in zip(ii[hit], jj[hit]):
            if t_top > top[b, a]:
                top[b, a] = t_top
            if g_min < gnd[b, a]:
                gnd[b, a] = g_min
    f = 1
    while True:
        if f > 1:
            T = top.reshape(n0 // f, f, n0 // f, f).max(axis=(1, 3))
            G = gnd.reshape(n0 // f, f, n0 // f, f).min(axis=(1, 3))
        else:
            T, G = top, gnd
        tris = _surface_triangles(T)
        if tris <= tri_cap or cell0 * f >= CHUNK_M:
            break
        f *= 2
    cell = cell0 * f
    m = T.shape[0]
    occ = np.isfinite(T)
    pad = np.pad(T, 1, constant_values=-np.inf)
    cols = int(occ.sum())
    for cj in range(m):
        for ci in np.nonzero(occ[cj])[0]:
            _emit_column(mb, tile, cell, int(ci), cj, T, G, pad)
    return cell, cols, tris <= tri_cap


def _surface_triangles(T: np.ndarray) -> int:
    occ = np.isfinite(T)
    pad = np.pad(T, 1, constant_values=-np.inf)
    n = int(occ.sum()) * 2
    c = pad[1:-1, 1:-1]
    for nb in (pad[1:-1, 2:], pad[1:-1, :-2], pad[2:, 1:-1], pad[:-2, 1:-1]):
        n += int((occ & (nb < c)).sum()) * 2
    return n


def _emit_column(mb: MeshBuilder, tile: Tile, cell: float, ci: int, cj: int, T, G, pad):
    x0 = tile.x0 + ci * cell
    z0 = tile.z0 + cj * cell
    x1, z1 = x0 + cell, z0 + cell
    top = float(T[cj, ci])
    gnd = float(G[cj, ci]) - SKIRT_M
    verts, tris = [], []

    def quad(p0, p1, p2, p3, want):
        k = len(verts)
        verts.extend([p0, p1, p2, p3])
        for t in ((k, k + 1, k + 2), (k, k + 2, k + 3)):
            a, b, c = (np.array(verts[i]) for i in t)
            if np.dot(np.cross(b - a, c - a), want) < 0:
                t = (t[0], t[2], t[1])
            tris.append(t)

    quad((x0, top, z0), (x1, top, z0), (x1, top, z1), (x0, top, z1), (0, 1, 0))
    nbs = ((1, 0, (1, 0, 0)), (-1, 0, (-1, 0, 0)), (0, 1, (0, 0, 1)), (0, -1, (0, 0, -1)))
    for dx, dz, nrm in nbs:
        nt = float(pad[cj + 1 + dz, ci + 1 + dx])
        if nt >= top:
            continue
        low = gnd if not np.isfinite(nt) else max(nt, gnd)
        if dx == 1:
            q = ((x1, low, z0), (x1, low, z1), (x1, top, z1), (x1, top, z0))
        elif dx == -1:
            q = ((x0, low, z0), (x0, top, z0), (x0, top, z1), (x0, low, z1))
        elif dz == 1:
            q = ((x0, low, z1), (x0, top, z1), (x1, top, z1), (x1, low, z1))
        else:
            q = ((x0, low, z0), (x1, low, z0), (x1, top, z0), (x0, top, z0))
        quad(*q, nrm)
    mb.add(mb.chunk_of(x0 + cell / 2, z0 + cell / 2), np.array(verts, dtype=np.float64),
           np.array(tris, dtype=np.int64), None)


# ---------------------------------------------------------------- one pyramid tile
def _attribution(src: L0Source, tiles) -> tuple[list[str], bool]:
    att: set[str] = set()
    ok = True
    from .manifest import LICENCES
    for (a, b) in tiles:
        for L in src.manifest(a, b)["layers"]:
            for s in L.get("sources") or []:
                if s.get("attribution"):
                    att.add(s["attribution"])
                if s.get("licence") not in LICENCES:
                    ok = False
    return sorted(att), ok


def bake_level_tile(level: int, I: int, J: int, src: L0Source, out_root: Path, rtin: bool = True,
                    prefilter: bool = True, ground: bool = True) -> dict:
    """Bake one pyramid tile into `<out_root>/L{level}/{I:02d}_{J:02d}/`; returns a report with sizes and timings."""
    assert 1 <= level <= MAX_LEVEL
    tile = Tile(level, I, J)
    d = Path(out_root) / tile.folder
    d.mkdir(parents=True, exist_ok=True)
    tiles = contributing(level, I, J, src)
    if not tiles:
        raise ValueError(f"no L0 source tile under {tile.id}")
    rep: dict = {"tile": tile.id, "level": level, "sources": len(tiles), "seconds": {}}
    files: dict[str, bytes] = {}
    t0 = time.perf_counter()

    def lap(name, t_start):
        rep["seconds"][name] = round(time.perf_counter() - t_start, 3)

    # terrain
    t = time.perf_counter()
    thr = 0.1 * (1 << level)
    shaped = prefilter or ground
    if shaped:
        h, h_raw, cov, sst = shaped_terrain(level, I, J, src, thr, prefilter=prefilter, ground=ground)
        pf, gr, gmask = sst.get("prefilter"), sst.get("ground"), sst["_mask"]
    else:
        h, cov = subsample_terrain(level, I, J, src)
        h_raw, pf, gr, gmask = h, None, None, None
    v, hmin, hmax = quantise_r16(h)
    files["terrain.r16"] = v.astype("<u2").tobytes()
    hq = decode_r16(v, hmin, hmax)
    files["slope.u8"] = slope_u8(hq, tile.spacing).tobytes()
    acc = ErrAccumulator()
    acc_in, acc_out = (ErrAccumulator(), ErrAccumulator()) if gmask is not None else (None, None)
    s_ = 1 << level
    per = N // s_
    for (a, b) in tiles:
        r0 = (b - J * s_) * per
        c0 = (a - I * s_) * per
        # coarse nodes of this L0 tile's footprint incl. the shared far edge (filtered field, so the error is real)
        H = src.heights(a, b)
        C = None
        if shaped:
            Cr = H[::s_, ::s_]
            # far-edge nodes owned by a neighbour (or a placeholder) keep this tile's own L0 value, as before
            same = h_raw[r0:r0 + per + 1, c0:c0 + per + 1] == Cr
            C = np.where(same, h[r0:r0 + per + 1, c0:c0 + per + 1], Cr)
        acc.add(H, s_, C)
        if gmask is not None:
            # replaced zone on the L0 grid: an L0 node counts as inside when any corner node of its coarse cell is
            idx = np.arange(SAMPLES)
            i0 = np.minimum(idx // s_, per - 1)
            Mz = gmask[r0:r0 + per + 1, c0:c0 + per + 1]
            Mc = Mz[i0][:, i0] | Mz[i0 + 1][:, i0] | Mz[i0][:, i0 + 1] | Mz[i0 + 1][:, i0 + 1]
            acc_in.add(H, s_, C, keep=Mc)
            acc_out.add(H, s_, C, keep=~Mc)
    terr = acc.result()
    if gmask is not None:
        terr_in, terr_out = acc_in.result(), acc_out.result()
    lap("terrain", t)
    t = time.perf_counter()
    tri_t, thr_cap, tri_cap = rtin_for_cap(hq, thr, BUDGET["terrain_tris_cap"]) if rtin else (None, None, None)
    tri_raw = tri_pf = tri_t
    if rtin and shaped:
        vr, hr0, hr1 = quantise_r16(h_raw)
        tri_raw = rtin_triangles(decode_r16(vr, hr0, hr1), thr)
        if ground and prefilter:      # the T-108b field (pre-filter only) for the before / after count
            hp, _, _, _ = shaped_terrain(level, I, J, src, thr, prefilter=True, ground=False)
            vp, hp0, hp1 = quantise_r16(hp)
            tri_pf = rtin_triangles(decode_r16(vp, hp0, hp1), thr)
        elif ground:
            tri_pf = tri_raw
    lap("rtin", t)

    # water
    t = time.perf_counter()
    files["water.png"] = png_bytes(downsample_water(level, I, J, src))
    lap("water", t)

    # buildings
    t = time.perf_counter()
    lm: set[str] = set()
    items = []
    for (a, b) in tiles:
        lm |= src.landmark_bids(a, b)
        items += collect_pieces(src.buildings(a, b), lm)
    forms = FORMS[level]
    tol = FOOTPRINT_TOL_M[level]
    bstats: dict = {}
    for form in forms:
        mb = MeshBuilder(tile.x0, tile.z0)
        if form == "b1":
            pieces = _simplify_pieces(items, tol, None, lm, mb)
            build_prisms(pieces, mb)
            cell = None
            cap_ok = mb.triangles <= BUDGET["b1_tris_cap"]
            cap = BUDGET["b1_tris_cap"]
            cols = 0
        else:
            pieces = _simplify_pieces(items, tol, True, lm, mb)
            build_prisms(pieces, mb)
            cap = BUDGET["b2_tris_cap"]
            cell, cols, cap_ok = rooftop_surface(items, level, tile, hq.reshape(SAMPLES, SAMPLES),
                                                 max(0, cap - mb.triangles), mb)
            cap_ok = cap_ok and mb.triangles <= cap
        idx = building_index(pieces, form, tile)
        if form == "b2":
            idx["surface"] = {"cell_m": cell, "columns": cols}
        sorted_bids = [r["bid"] for r in idx["buildings"]]
        files[f"{form}.lodmesh"] = mb.encode(sorted_bids)
        files[f"{form}.index.json"] = (canonical_json(idx, indent=1) + "\n").encode()
        errs = np.array([p.err_m for p in pieces]) if pieces else np.zeros(1)
        bstats[form] = {"triangles": mb.triangles, "vertices": mb.vertices, "buildings": len(sorted_bids),
                        "tri_cap": cap, "within_cap": bool(cap_ok), "footprint_tol_m": tol,
                        "footprint_max_m": round(float(errs.max()), 3),
                        "footprint_rms_m": round(float(np.sqrt((errs ** 2).mean())), 3),
                        "surface_cell_m": cell, "surface_columns": cols,
                        "file_bytes": len(files[f"{form}.lodmesh"])}
    lap("buildings", t)

    # manifest
    t = time.perf_counter()
    atts, lic_ok = _attribution(src, tiles)
    fp = sha256_bytes("\n".join(sorted(
        sha256_file(src.tiles[k] / "manifest.json") for k in tiles)).encode())
    bt = sum(s["triangles"] for s in bstats.values())
    bv = sum(s["vertices"] for s in bstats.values())
    ttri = tri_t
    gpu_t = 0 if ttri is None else int((ttri // 2 + 2) * TERRAIN_VERTEX_BYTES + ttri * TERRAIN_INDEX_BYTES)
    gpu_b = bv * BUILDING_VERTEX_BYTES + bt * 3 * BUILDING_INDEX_BYTES
    # an L(n) tile is resident with its neighbours (3 x 3, R-101 section 1 table): report per tile
    forms_meta = {}
    for form in forms:
        forms_meta[f"form_{form}"] = {"file": f"{form}.lodmesh", "index": f"{form}.index.json"}
    ferr = {k: max(s[k] for s in bstats.values()) for k in ("footprint_max_m", "footprint_rms_m")}
    man = {
        "spec": SPEC, "tile": tile.id, "level": level, "i": I, "j": J,
        "bounds_baytm": list(tile.bounds), "source_level": 0, "source_fingerprint": fp,
        "source_tiles": [f"L0_{a:02d}_{b:02d}" for (a, b) in tiles],
        "coverage": round(float(cov.mean()), 6),
        "coverage_note": "uncovered nodes are open-ocean placeholder (terrain = MSL, water = 255), R-101 O11",
        "terrain": {"file": "terrain.r16", "samples": SAMPLES, "spacing_m": tile.spacing,
                    "h_min": hmin, "h_max": hmax, "method": "node_subsample" + ("+ground" if ground else ""), "slope_q": "slope.u8",
                    "format": "uint16 LE RAW 2049x2049, row 0 = south, col 0 = west, h = h_min + v*(h_max-h_min)/65535",
                    "rtin_threshold_m": thr, "rtin_triangles": ttri,
                    "rtin_threshold_for_cap_m": thr_cap, "rtin_triangles_at_cap_threshold": tri_cap,
                    "prefilter": pf, "rtin_triangles_unfiltered": tri_raw,
                    "ground": gr, "rtin_triangles_before_ground": tri_pf},
        "buildings": forms_meta,
        "water": {"file": "water.png", "format": "PNG 8-bit 2048x2048 cell-centred, row 0 = north, 255 = water",
                  "method": "majority"},
        "errors": {
            "terrain": terr,
            "terrain_ground_zone": None if gmask is None else {"inside": terr_in, "outside": terr_out},
            "buildings": {"footprint_max_m": ferr["footprint_max_m"], "footprint_rms_m": ferr["footprint_rms_m"],
                          "height_max_m": 0.0, "forms": bstats},
            "switch_hint_m": {"k4_fov50_1080": round(max(terr["rms_m"], 1e-6) * 1158 * 4, 1)},
        },
        "stats": {"triangles": (ttri or 0) + bt, "vertices": ((ttri or 0) // 2 + 2) + bv,
                  "gpu_bytes_est": gpu_t + gpu_b, "cpu_bytes_est": sum(len(b) for b in files.values()),
                  "terrain_triangles": ttri, "building_triangles": bt,
                  "gpu_bytes_terrain": gpu_t, "gpu_bytes_buildings": gpu_b},
        "budget": {
            "terrain_tris_cap": BUDGET["terrain_tris_cap"],
            "terrain_within_cap": None if ttri is None else bool(ttri <= BUDGET["terrain_tris_cap"]),
            "gpu_mb_budget_per_tile": BUDGET["gpu_mb_per_tile"][level],
            "gpu_mb_est": round((gpu_t + gpu_b) / 1048576.0, 2),
            "gpu_within_budget": None if ttri is None else bool((gpu_t + gpu_b) / 1048576.0 <= BUDGET["gpu_mb_per_tile"][level]),
        },
        "chunk_m": CHUNK_M,
        "generator": GENERATOR,
        "attribution": atts,
        "licence_ok": lic_ok,
    }
    man["files"] = {k: sha256_bytes(v) for k, v in sorted(files.items())}
    files["manifest.json"] = (canonical_json(man, indent=2) + "\n").encode()
    for name, data in files.items():
        p = d / name
        if not p.exists() or p.read_bytes() != data:
            p.write_bytes(data)
    lap("manifest", t)
    rep["seconds"]["total"] = round(time.perf_counter() - t0, 3)
    rep["bytes"] = {k: len(v) for k, v in sorted(files.items())}
    rep["bytes_total"] = sum(len(v) for v in files.values())
    rep["terrain_triangles"] = ttri
    rep["forms"] = bstats
    rep["terrain_err"] = terr
    rep["gpu_mb_est"] = man["budget"]["gpu_mb_est"]
    rep["budget"] = man["budget"]
    return rep


# ---------------------------------------------------------------- validator
LOD_TOP_FIELDS = ("spec", "tile", "level", "i", "j", "bounds_baytm", "source_level", "source_fingerprint",
                  "terrain", "buildings", "water", "errors", "stats", "chunk_m", "files", "attribution", "licence_ok")


def validate_level_tile(tile_dir: Path, src: L0Source | None = None) -> list[str]:
    """Problems (empty = valid): completeness, file hashes, errors present, optional source fingerprint."""
    tile_dir = Path(tile_dir)
    errs: list[str] = []
    mp = tile_dir / "manifest.json"
    if not mp.exists():
        return ["manifest.json missing"]
    man = json.loads(mp.read_text())
    for k in LOD_TOP_FIELDS:
        if k not in man or man[k] in (None, "", [], {}):
            if not (k == "licence_ok" and man.get(k) is False) and not (k == "attribution" and man.get(k) == []):
                errs.append(f"missing top-level field '{k}'")
    if man.get("spec") != SPEC:
        errs.append(f"spec is {man.get('spec')!r}, expected {SPEC!r}")
    e = (man.get("errors") or {})
    for part, keys in (("terrain", ("max_m", "rms_m", "p99_m")),
                       ("buildings", ("footprint_max_m", "footprint_rms_m", "height_max_m"))):
        for k in keys:
            if k not in (e.get(part) or {}):
                errs.append(f"errors.{part}.{k} missing")
    for name, sha in (man.get("files") or {}).items():
        p = tile_dir / name
        if not p.exists():
            errs.append(f"file {name} missing")
        elif sha256_file(p) != sha:
            errs.append(f"sha256 mismatch for {name}")
    for form in (man.get("buildings") or {}).values():
        for k in ("file", "index"):
            if form.get(k) not in (man.get("files") or {}):
                errs.append(f"form file {form.get(k)} not in files")
    if src is not None:
        tiles = contributing(man["level"], man["i"], man["j"], src)
        fp = sha256_bytes("\n".join(sorted(sha256_file(src.tiles[k] / "manifest.json") for k in tiles)).encode())
        if fp != man.get("source_fingerprint"):
            errs.append("source_fingerprint does not match the contributing L0 manifests")
    return errs


def check_edge_agreement(dir_a: Path, dir_b: Path, axis: str) -> list[str]:
    """Adjacent same-level tiles must agree on their shared edge nodes (R-004 seam rule) to within the r16
    quantisation steps. axis 'x': B is east of A; 'z': B is north of A."""
    out = []
    ha, hb = (_read_r16(d) for d in (dir_a, dir_b))
    (ma, A), (mb, B) = ha, hb
    tol = (ma["h_max"] - ma["h_min"]) / 65535.0 + (mb["h_max"] - mb["h_min"]) / 65535.0
    ea, eb = (A[:, -1], B[:, 0]) if axis == "x" else (A[-1, :], B[0, :])
    dmax = float(np.max(np.abs(ea - eb)))
    if dmax > tol + 1e-9:
        out.append(f"shared edge differs by {dmax:.5f} m > quantisation tolerance {tol:.5f} m")
    return out


def _read_r16(d: Path):
    man = json.loads((Path(d) / "manifest.json").read_text())
    t = man["terrain"]
    v = np.fromfile(Path(d) / t["file"], dtype="<u2").reshape(SAMPLES, SAMPLES)
    return t, decode_r16(v, t["h_min"], t["h_max"])
