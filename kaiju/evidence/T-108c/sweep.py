"""T-108c: (final, height-clamped fill) sweep GROUND_DILATE_M x GROUND_MAX_COMPONENT_M on the downtown tiles (RTIN triangles at the nominal threshold)."""
import sys, time
sys.path.insert(0, "Pipelines/geo")
from kaiju_geo import lod
from kaiju_geo.common import TILES_ROOT
src = lod.L0Source(TILES_ROOT)
tiles = [(1,14,17),(1,14,18),(1,15,17),(1,15,18),(2,7,8),(2,7,9)]
print("dilate_m,max_component_m,tile,tris,replaced_pct,footprint_kept_pct,max_drop_m,seconds", flush=True)
for dil in (2.0, 4.0, 6.0, 8.0):
    for comp in (256.0, 512.0, 1024.0):
        lod.GROUND_DILATE_M, lod.GROUND_MAX_COMPONENT_M = dil, comp
        for (lv, I, J) in tiles:
            t = time.time()
            h, _, _, st = lod.shaped_terrain(lv, I, J, src, None, prefilter=True, ground=True)
            v, a, b = lod.quantise_r16(h)
            n = lod.rtin_triangles(lod.decode_r16(v, a, b), 0.1 * (1 << lv))
            g = st["ground"]
            print(f"{dil},{comp},L{lv}_{I:02d}_{J:02d},{n},{g['replaced_pct']},{g['footprint_nodes_kept_pct']},{g['max_drop_m']},{time.time()-t:.1f}", flush=True)
