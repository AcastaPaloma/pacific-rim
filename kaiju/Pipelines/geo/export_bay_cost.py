#!/usr/bin/env python
"""T-115d: export the Bay route-planner cost raster `bay_cost@v1` (250 m class, 256 m cells) from the baked L3 terrain/water tiles (T-102).

Reads (git-ignored cache, KAIJU_GEO_CACHE for a task worktree):  <cache>/bake/tiles/v1/L3/II_JJ/{terrain.r16,water.png,manifest.json}
Writes: bay_cost_256m_v1.bin  (little endian)
    magic 'BCF1', int32 nx, int32 ny, float32 originX, float32 originY, float32 cellM,
    uint16 depth[ny*nx]  (decimetres of water depth, area mean over the cell's 32x32 terrain nodes where water.png = water, row 0 = south),
    uint8  soft[ny*nx]   (0..255 = softness 0..1)
and bay_cost_256m_v1.json (provenance, counts). Frame: BayTM@v1 metres, origin (0, 0), east = +x, north = +y (same as the tiles).

Depth: max(0, -h) in metres (NAVD88 against MSL, offset ignored) on water nodes; tiles missing from the pyramid are treated as deep (200 m).
Softness (PROXY, Assumed): no liquefaction-susceptibility layer is baked (USGS OFR 2006-1037 is not fetched; R-103 sec. 9). Proxy = fraction of the
cell's land nodes with h <= 3 m NAVD88 (reclaimed fill, Bay mud margin, delta), 1.0 when all land nodes are that low. A tracked requirement replaces it.
Usage: python export_bay_cost.py [--out DIR]
"""
import argparse, hashlib, json, struct, sys
from pathlib import Path
import numpy as np
from PIL import Image
sys.path.insert(0, str(Path(__file__).resolve().parent))
from kaiju_geo import common

N = 2049; BLOCK = 32; CELL = 256.0; TILE_M = 16384.0; TILES = 9; DEEP_MISSING_M = 200.0; SOFT_H_M = 3.0


def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--out", default=str(Path(__file__).resolve().parents[2] / "evidence" / "T-115"))
    a = ap.parse_args()
    base = common.CACHE / "bake" / "tiles" / "v1" / "L3"
    cpt = int(TILE_M / CELL); nx = ny = TILES * cpt
    depth = np.full((ny, nx), DEEP_MISSING_M, np.float32); soft = np.zeros((ny, nx), np.float32)
    stats = dict(tiles=0, missing=0, placeholder_sea_cells=0, water_cells=0, soft_cells=0)
    h = hashlib.sha256()
    for j in range(TILES):
        for i in range(TILES):
            d = base / f"{i:02d}_{j:02d}"
            if not (d / "terrain.r16").exists(): stats["missing"] += 1; continue
            m = json.load(open(d / "manifest.json")); t = m["terrain"]; hmin, hmax = t["h_min"], t["h_max"]
            raw = np.fromfile(d / "terrain.r16", "<u2").reshape(N, N).astype(np.float32)
            hh = hmin + raw * ((hmax - hmin) / 65535.0)                       # row 0 = south, col 0 = west
            w = np.asarray(Image.open(d / "water.png"))[::-1] >= 128          # 2048x2048, flip to row 0 = south
            hb = hh[:2048, :2048]
            dep = np.where(w, np.maximum(0.0, -hb), 0.0)
            land = ~w
            low = land & (hb <= SOFT_H_M)
            def blk(x): return x.reshape(cpt, BLOCK, cpt, BLOCK).mean(axis=(1, 3))
            dc = blk(dep); lc = blk(land.astype(np.float32)); lowc = blk(low.astype(np.float32))
            sc = np.where(lc > 0.05, lowc / np.maximum(lc, 1e-6), 0.0)
            depth[j * cpt:(j + 1) * cpt, i * cpt:(i + 1) * cpt] = dc; soft[j * cpt:(j + 1) * cpt, i * cpt:(i + 1) * cpt] = sc
            stats["tiles"] += 1; stats["placeholder_sea_cells"] += int((np.abs(blk(np.where(w & (np.abs(hb + 50.0) < 1e-3), 1.0, 0.0)) - 1.0) < 1e-6).sum())
            h.update(m.get("source_fingerprint", "").encode())
    stats["water_cells"] = int((depth > 0.5).sum()); stats["soft_cells"] = int((soft > 0.5).sum())
    dq = np.clip(np.rint(depth * 10.0), 0, 65535).astype("<u2"); sq = np.clip(np.rint(soft * 255.0), 0, 255).astype(np.uint8)
    out = Path(a.out); out.mkdir(parents=True, exist_ok=True)
    binp = out / "bay_cost_256m_v1.bin"
    with open(binp, "wb") as f:
        f.write(b"BCF1" + struct.pack("<iifff", nx, ny, 0.0, 0.0, CELL)); f.write(dq.tobytes()); f.write(sq.tobytes())
    info = dict(spec="bay_cost@v1", nx=nx, ny=ny, cell_m=CELL, origin_baytm_m=[0.0, 0.0], source="Pipelines/geo/cache/bake/tiles/v1/L3 (T-102, bake_lod)",
                source_fingerprint_sha256=h.hexdigest(), file_sha256=hashlib.sha256(binp.read_bytes()).hexdigest(), stats=stats,
                depth_note="area mean of max(0,-h) over water nodes; open-ocean/uncovered tiles carry the bake's -50 m placeholder (Q-NEW-b), missing tiles 200 m",
                softness_note="PROXY: land nodes <= 3 m NAVD88 (fill / Bay mud margin); not USGS liquefaction susceptibility (OFR 2006-1037 not fetched), Assumed")
    json.dump(info, open(out / "bay_cost_256m_v1.json", "w"), indent=1)
    img = np.zeros((ny, nx, 3), np.uint8)
    img[..., 0] = np.clip(soft * 200, 0, 255); img[..., 1] = np.where(depth > 0.5, np.clip(255 - depth * 2.5, 40, 255), 0); img[..., 2] = np.where(depth > 0.5, 255, 0)
    Image.fromarray(img[::-1]).save(out / "bay_cost_256m_v1.png")
    print(json.dumps(info["stats"]), info["file_sha256"][:12])


if __name__ == "__main__":
    main()
