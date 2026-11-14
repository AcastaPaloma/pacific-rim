# Pipelines/geo: open GIS data to `geo_tile@v1` (T-003)

Fetches public open data for one BayTM@v1 tile, bakes the `geo_tile@v1` spec of `research/R-004.md` §3,
and checks the result against `M-005@v1` (R-004 §6). Python: always the kaiju conda env
(`/opt/miniconda3/envs/kaiju/bin/python -I ...`). Every command is idempotent: a second run with a populated
cache prints `up to date`, does no network I/O (add `--offline` to prove it: any socket connect then raises) and
writes nothing. A source that cannot be reached fails loudly and leaves no partial cache entry.

## Commands (one per layer, then the bake)
| Step | Command | Source (licence) |
|---|---|---|
| terrain + seabed | `fetch_cudem.py --tile L0_30_36` | NOAA NCEI CUDEM 1/9" topobathy 2022v1, windowed COG read (US-Gov-PD) |
| land detail | `fetch_3dep.py --tile L0_30_36` | USGS 3DEP 1 m DEM via TNM API, newest project per UTM tile (US-Gov-PD) |
| buildings, roads, piers | `fetch_osm.py --tile L0_30_36` | Geofabrik NorCal PBF (656 MB, once) + `osmium extract --strategy smart` per tile (ODbL-1.0) |
| height fallback | `fetch_datasf.py --tile L0_30_36` | DataSF Building Footprints `ynuv-fyni` (PDDL-1.0) |
| water | `fetch_water.py` | osmdata water polygons, split 4326 (908 MB, once; ODbL-1.0) + NOAA CO-OPS 9414290 datums (US-Gov-PD) |
| faults (global) | `fetch_faults.py`, then `bake_faults.py` | USGS Qfaults ArcGIS REST layers 4 + 5 (US-Gov-PD) |
| references (not shipped) | `fetch_refs.py --tile L0_30_36` | NGS NDE benchmarks; 3DEP lidar EPT first-return DSM via PDAL (US-Gov-PD) |
| **bake** | `bake_tile.py --tile L0_30_36 [--offline] [--force] [--threads N]` | all of the above, from the cache only |
| M-005 | `m005_check.py prepare`, Unity export, `m005_check.py check` | writes `evidence/T-003/m005_report.json` |
| plates | `plates.py` | heights vs 80 m mech, wading depth, fault map |

`bake_tile.py` is parameterised only by the tile id: any of the 68 x 68 Bay tiles bakes with no code change once
its sources are fetched (proved with `L0_26_37`, Golden Gate, and `L0_30_35`, the south neighbour). Raw downloads
go to `cache/` (git-ignored, never committed). Total raw download for the three tiles: about 1.6 GB (the two
one-time bulk files) plus ~40 MB of windows.

## Where the baked data lives
`kaiju/Assets/StreamingAssets/Geo/` (committed; rasters in Git LFS via `.gitattributes` there and at the repo root):
```
crs/baytm_v1.wkt              WKT2 BayTM@v1 (NAD83(2011) TM, k = 1) + NAVD88
global/v1/faults_bay.geojson  3232 Qfaults pieces in BayTM, normalised fields; faults_bay.manifest.json
global/v1/landmarks.json      R-004 §5 landmark table + §6.7 viewpoints, with BayTM coordinates
tiles/v1/L0/30_36/            terrain.tif, terrain.r16, water.png, buildings.geojson, roads.geojson, faults.geojson, manifest.json
```
StreamingAssets ships with players and is read by the Unity importer by path; R-004 put tiles under the git-ignored
cache, the reconciled spec moved them to a committed folder (expectations/T-003.md readings).

## Bake rules (all in `config/bake.json`)
- Nodes at x0 + k, z0 + k (pixel-is-point, k = 0..2048): neighbours share edge rows byte for byte.
- Surface: 3DEP 1 m where valid and CUDEM > +1.798 m NAVD88, CUDEM elsewhere, linear feather centred on the region edge over `terrain.feather_m` = 50 m (R-004 said 10 m inside; changed for M-005@v1.1 M-005.9, numbers in config/bake.json `_blend_note`).
  Piers (OSM `man_made=pier`) are solid: raised to a flat deck (median of 3DEP deck samples, else the 75th percentile
  of dry land at the pier root), never lowered.
- Water mask: OSM water polygon AND cell height < MSL (+0.969 m NAVD88), minus every cell touched by a pier or a footprint.
- Buildings: one feature per OSM building and `building:part`; `bid` = `osm:w<id>` / `osm:r<id>` (Core
  `bld:osm-way:<id>`); parts linked by `part_of` (smallest containing outline); structure owned by the tile holding its
  `point_on_surface`, parts follow it, geometry not clipped. Heights: OSM `height` -> DataSF `hgt_median_m` (IoU > 0.6
  and relief > 3 m) -> `building:levels` x 4.22 m -> 3 levels, flagged per building in `height_source`.
  `base_navd88_m` = minimum surface under the footprint. A structure with parts renders through its parts plus
  `remainder_geometry` (outline minus parts) at its own height.
- Roads: clipped to the tile, global `wid`, width = OSM width, else lanes x 3.5 m, else class default; `bridge`,
  `tunnel`, `layer` kept.
- Determinism: sorted ids, mm-rounded coordinates, 0.1 mm-rounded heights, canonical JSON; an input fingerprint
  (source hashes + code + config) makes re-runs no-ops. Thread count and input order do not change a byte.

## Tests
`/opt/miniconda3/envs/kaiju/bin/python -I -m pytest Pipelines/geo/tests` (projection maths, tile indexing, ID
determinism, manifest completeness and doctored manifests, seams, re-bake identity, offline behaviour).
