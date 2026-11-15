"""Parameter sweep of the T-108b pre-filter: RTIN triangles per urban LOD tile for (sigma, eps) in units of the nominal RTIN threshold."""
import sys; sys.path.insert(0, "Pipelines/geo")
from kaiju_geo import lod
from kaiju_geo.common import TILES_ROOT
src = lod.L0Source(TILES_ROOT)
tiles = [(1,12,18),(1,12,19),(1,13,18),(1,13,19),(1,14,17),(1,14,18),(1,15,17),(1,15,18),(2,6,9),(2,7,8),(2,7,9),(3,3,4),(4,1,2)]
combos = [(0,0)] + [(s, e) for e in (0.5, 1.0) for s in (2, 4, 8)]
print("tile," + ",".join(f"sig{s}_eps{e}" for s, e in combos), flush=True)
for (lv, I, J) in tiles:
    row = []
    for s, e in combos:
        lod.PREFILTER_SIGMA, lod.PREFILTER_EPS = float(s), float(e)
        if s == 0:
            h, _ = lod.subsample_terrain(lv, I, J, src)
        else:
            h = lod.prefiltered_terrain(lv, I, J, src)[0]
        v, a, b = lod.quantise_r16(h)
        row.append(lod.rtin_triangles(lod.decode_r16(v, a, b), 0.1 * (1 << lv)))
    print(f"L{lv}_{I:02d}_{J:02d}," + ",".join(map(str, row)), flush=True)
