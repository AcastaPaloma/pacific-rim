"""T-108c: where do the high-laplacian (> 6 cm) nodes of the tiles that stay over cap sit? share on steep slope, mean slope, within 5 nodes of water edge."""
import sys
sys.path.insert(0, "Pipelines/geo")
import numpy as np
from scipy import ndimage as ndi
from kaiju_geo import lod
from kaiju_geo.common import TILES_ROOT
src = lod.L0Source(TILES_ROOT)
print("tile,hi_lap_pct,hi_on_slope_gt15deg_pct,hi_on_slope_gt30deg_pct,tile_mean_slope_deg,over_water_pct")
for (lv, I, J) in [(1,12,19),(1,13,18),(1,13,19),(2,6,9),(3,3,4)]:
    h, cov = lod.subsample_terrain(lv, I, J, src)
    p = np.pad(h, 1, mode="edge")
    L = np.abs(p[:-2, 1:-1] + p[2:, 1:-1] + p[1:-1, :-2] + p[1:-1, 2:] - 4 * h); hi = L > 0.06
    gz, gx = np.gradient(h, float(1 << lv)); sl = np.degrees(np.arctan(np.hypot(gx, gz)))
    w = np.pad(lod.downsample_water(lv, I, J, src) > 127, ((0, 1), (0, 1)), mode="edge")
    n = max(hi.sum(), 1)
    print(f"L{lv}_{I:02d}_{J:02d},{100*hi.mean():.1f},{100*(hi&(sl>15)).sum()/n:.1f},{100*(hi&(sl>30)).sum()/n:.1f},{sl.mean():.1f},{100*w.mean():.1f}", flush=True)
