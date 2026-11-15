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
| building attributes (optional) | `fetch_assessor.py --tile L0_30_36` | DataSF Assessor Historical Secured Property Tax Rolls `wv5m-vpq2`, roll year pinned in `config/sources.json` (PDDL-1.0); adds `year_built`, `stories`, `struct_class` (raw construction_type) to buildings, absent when unknown; if the fetch is unavailable the bake skips it and writes nothing new |
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

## LOD pyramid `geo_lod@v1` (T-108, R-101 sections 4 and 8.2)
`bake_lod.py` builds L1..L4 pyramid tiles from the baked L0 tiles under `Assets/StreamingAssets/Geo/tiles/v1/L0/` and
writes them to the git-ignored `cache/tiles/v1/L{n}/{ii}_{jj}/` (it refuses to write under StreamingAssets). Code in
`kaiju_geo/lod.py`, tests in `tests/test_lod.py`. `python -I bake_lod.py [--levels 1 2 3 4] [--tile L2_07_09] [--no-rtin]
[--report FILE]`; a re-run rewrites nothing (byte-identical per L0 input, no clocks in the files).
Per tile: `terrain.r16` (node subsample of the L0 field, spacing 2^n m, same 2049^2 layout), `slope.u8`, `water.png`
(majority of the 2^n x 2^n L0 cells), `b1.lodmesh`/`b1.index.json` (merged blocks per 256 m chunk, per-building index
ranges; L1, L2), `b2.lodmesh`/`b2.index.json` (prisms for >= 60 m buildings + max-pooled rooftop surface; L2..L4) and
`manifest.json` (errors, stats, budget, file hashes, source fingerprint, attribution). `.lodmesh` layout: module docstring. Index rows of building parts carry `part_of` (bake_lod.py@4, T-111b). C# reader: `Game.World.Streaming.Host/GeoLodTileReader.cs`; error measurement at the cap thresholds: `scripts/measure_lod_error.py` (evidence/T-111b).
The manifest records the RTIN triangle count at the nominal threshold 0.1 x 2^n m (`rtin_threshold_nominal_m`, `rtin_triangles_at_nominal`)
and the smallest threshold of a fixed ladder that fits the 150 k cap (`terrain.rtin_threshold_for_cap_m`). Since bake_lod.py@5 (build lead
decision after T-111b) a tile that misses the cap at the nominal threshold is BAKED at its cap threshold: `rtin_threshold_m` and
`rtin_triangles` are the baked ones, the cap is never changed. `FOOTPRINT_TOL_OVERRIDE_M` sets B1 footprint tolerance 2 m for L1_14_18 only
(L2_07_09 stays over the 100k B1 cap, staged until M-102 render evidence).
Missing L0 tiles are an open-ocean placeholder (`coverage` < 1 in the manifest). `lod.validate_level_tile` and
`lod.check_edge_agreement` are the validators.

## Bay bake (T-102): `bake_bay.py`
Resumable, backgrounded driver over `bay_manifest@v1` (4311 tiles; the 313 `outside` grid tiles are not listed). Run it from the
checkout whose git-ignored `cache/` holds the T-101 fetches, with `nohup` (it costs no tokens), one stage per run:
`bake_bay.py --stage datasf|l0|lod [--workers 6] [--class ...] [--tile ...] [--limit N] [--force] [--min-free-gb 60] [--dry-run]`.
- Output (git-ignored, Q-200): `cache/bake/tiles/v1/L0/{ii}_{jj}/` and `.../L{n}/{ii}_{jj}/`. It refuses to write under StreamingAssets. The 15
  committed district tiles are baked again here (same code, same bytes) so the pyramid sees one contiguous source.
- Status: `cache/status/bake_<stage>.jsonl` (last line per tile wins) + `bake_<stage>.summary.json` (counts, bytes, rate, ETA, tiles with
  placeholder nodes, failures). Re-runs skip done tiles whose outputs are still current (`l0`: the bake's input fingerprint; `lod`: generator +
  contributing L0 manifests) and retry failed ones. Disk guard exit 3, SIGTERM clean stop, offline guard on `l0` and `lod`.
- `datasf` is the only online stage: DataSF footprints + Assessor windows for the 146 manifest tiles flagged `datasf` (T-101 did not bulk-fetch them).
- Source plan per tile comes from the manifest: `bake.bake(..., plan={cudem, dep3, osm, datasf})`. A source the manifest says is absent is not
  requested: no OSM (ocean_only) = empty buildings/roads layers; no DataSF = height fallback chain without it. The default path (`bake_tile.py`) is
  unchanged. Nodes no source covers (458 ocean tiles without CUDEM, Delta / east Contra Costa holes in the newest 3DEP project) hold a flat
  PLACEHOLDER (sea -50 m inside the OSM coastline polygons, land +2 m elsewhere); the terrain layer carries `placeholder_nodes*`. Tracked in
  plans/M2_PROGRESS.md until real bathymetry (Q-NEW-b) / 3DEP gap fill (T-103) replaces it.
- T-144b: OSM `name` is not shipped. `buildings` and `roads` keep the property key with value `null`; the names stay in the cache extracts
  (M-126: no OSM venue name in shipped text). Tools that matched on baked names (`m005_check` bridge test, `known_gaps`) must read the OSM cache.
- `Pipelines/build/copy_tiles.py` accepts `bay_manifest.json` as its tile set (`--class` filter) for the M3 player-build copy.
