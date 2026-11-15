# T-020 step 2: district tile bake (GG, SoMa)

14 new L0 tiles baked on the T-003 pipeline (Pipelines/geo/bake_tile.py). All 14 bake_tile rc=0, 8 files each, manifests present.
Rasters (terrain.tif, terrain.r16) route to Git LFS (`git check-attr filter` = lfs). water.png is not LFS (plain git, small).
Sources per manifest: terrain = CUDEM + 3DEP (+ extra tiles in some), buildings = OSM + DataSF. Reference 30_36: 37.9 % water, 2503 buildings, 23.7 MB.

| tile | bake s | buildings (rendered) | MB | water % |
|---|---|---|---|---|
| 25_36 | 9 | 1 (1) | 19.4 | 98.0 |
| 25_37 | 8 | 0 (0) | 19.5 | 97.0 |
| 25_38 | 9 | 30 (30) | 21.2 | 4.9 |
| 26_36 | 10 | 423 (423) | 21.3 | 70.1 |
| 26_37 | 9 | 28 (27) | 19.5 | 96.4 |
| 26_38 | 18 | 113 (112) | 21.2 | 11.3 |
| 27_36 | 9 | 978 (972) | 22.4 | 0.0 |
| 27_37 | 9 | 115 (108) | 19.8 | 90.1 |
| 27_38 | 17 | 28 (28) | 19.5 | 91.5 |
| 29_35 | 14 | 4137 (4091) | 25.3 | 0.0 |
| 29_36 | 28 | 7520 (7483) | 27.6 | 0.0 |
| 30_35 | 37 | 1976 (1921) | 24.1 | 10.1 |
| 31_35 | 32 | 3 (3) | 17.1 | 98.7 |
| 31_36 | 9 | 3 (3) | 19.3 | 99.9 |

Water % = manifest `water_fraction` (water layer).

## Mostly water (> 60 %)
25_36, 25_37, 26_36, 26_37, 27_37, 27_38, 31_35, 31_36 (8 of 14). Nearly empty of buildings; still ~17-20 MB each since terrain is fixed-size.

## Bake time (answers R-020 section 5 open item)
Bake step 8-37 s per tile (median 9.5 s, total 220 s); time scales with building count and pier/land area, not water. Fetch steps add 0-14 s per tile (3DEP slowest). Whole 14-tile district is under 10 minutes end to end.
Observation: 27_36 reports 0.0 % water, plausible for Presidio land but worth a glance in the viewer.
