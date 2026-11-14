# T-003 evidence: one real San Francisco tile `L0_30_36`

Branch `task/T-003`. Code for every artifact below: commit `9e1d5e1` (`logs/final_run.log` records the full run).
Baked tile data: `kaiju/Assets/StreamingAssets/Geo/tiles/v1/L0/30_36/` built by `Pipelines/geo/bake_tile.py` at
pipeline commit `650c7f6` (`manifest.json` `build.git`, `git_dirty_pipeline: false`). Python = `/opt/miniconda3/envs/kaiju/bin/python -I`
(written `PY` below); Unity = 6000.6.9f1 batch via the Unity CLI on this worktree. All paths relative to `kaiju/`.

Main commands (in this order, each reproducible from a clean checkout once the fetch scripts have filled `Pipelines/geo/cache/`):
1. Fetch: `PY Pipelines/geo/fetch_cudem.py|fetch_3dep.py|fetch_osm.py|fetch_datasf.py|fetch_refs.py --tile L0_30_36`, `PY Pipelines/geo/fetch_water.py`, `PY Pipelines/geo/fetch_faults.py`
2. Bake: `PY Pipelines/geo/bake_faults.py`, `PY Pipelines/geo/bake_tile.py --tile L0_30_36`
3. `PY Pipelines/geo/m005_check.py prepare` (control points)
4. `unity run <worktree>/kaiju --no-tail --log-file <log> --timeout 1800 -- -executeMethod Game.World.Geo.Editor.GeoBatch.All`
5. `PY Pipelines/geo/m005_check.py check` -> `m005_report.json`; `PY Pipelines/geo/plates.py`
6. Tests: `PY -m pytest Pipelines/geo/tests`, `dotnet test DotnetTests/WorldGeo.Tests`, `unity test <worktree>/kaiju --mode EditMode -o tests/unity_editmode.xml`

**Test totals:** pytest 58/58 (`tests/pytest.xml`), dotnet 22/22 (`tests/dotnet_worldgeo.trx`), Unity EditMode 124/124 (30 World.Geo + 94 Core) (`tests/unity_editmode.xml`).
**M-005@v1 sub-measures** (`m005_report.json` `summary`): 18 pass, 3 fail, 9 not_applicable_in_tile, 1 insufficient_marks (gate moves to the pipeline check, which passes), 1 reported. Overall M-005: **fail** (needs every sub-measure).

## A. Geographic truth (M-005@v1)
| C | Result | Artifact and value |
|---|---|---|
| C1 landmark positions (M-005.1 a, b) | pass | `m005_report.json` `m005.M-005.1`: 7 in-tile landmarks; vs import source max 0.0001 m (<= 0.5); vs R-004 reference p95 1.124 m, max 1.152 m (<= 5 / 10). Out-of-tile rows and GG span: `not_applicable_in_tile` (due M1). |
| C2 landmark heights (M-005.1 d) | pass | same, `d_heights`: Salesforce 326.0, Transamerica 260.0, Millennium 197.0, Coit 64.0 m above base (all within max(2 m, 1 %)); reported only: 555 California 237.0 (OSM parts; outline 226), W2 160.0, Ferry Building 83.1. Salesforce is the tallest object (`salesforce_tallest: true`, top 331.2 m NAVD88). Plate `plates/height_relations_mech.png`. |
| C3 terrain (M-005.2) | pass (pipeline gate) | 29 NGS marks in tile, 0 qualify (SCALED / structure settings) -> `insufficient_marks`; pipeline gate on 10,000 land points: physics surface vs 3DEP RMSE 0.0095 m, max 0.137 m (<= 0.10 / 0.5); render surface RMSE 0.034, max 0.153. Info: all-29-mark median |d| 0.60 m. |
| C4 bathymetry (M-005.3) | pass (tile-scoped) | 10,000 wet points RMSE 0.0062 m (<= 0.25); as-built minimum -29.481 m vs CUDEM -29.485 m (within 1 m). GG / Oakland / San Pablo / Bay-wide: `not_applicable_in_tile` (GG preview on L0_26_37 recorded, box only partly inside that tile, not a pass). |
| C5 building heights (M-005.4) | **fail** (default share) | per building 100 % within 0.5 m (2,423 rendered records, max |d| 0.000); KS D 0.000; p50/p90/p99 equal; fallback validation (Assumed) KS 0.1125, median |e| 0.32 m: pass. `height_source`: osm_height 1556, datasf 281, osm_levels 332, **default 334 = 13.3 % (> 10 %)**, 10.1 % by area. Mitigation measured, not applied: `t003.C5_default_share_mitigation` (containment rule -> 6.4 %). Roof rule in `M-005.4.roof_rule`. |
| C6 footprints (M-005.5) | pass | area recall 0.9997, precision 1.000 (0.5 m raster); count recall 1.000 (2,443 source buildings >= 20 m^2); 0 bids not in source; 0 buildings with > 1 % of footprint on the water surface (edge slivers 0 m^2); footprints on OSM water listed (W2, pier sheds). Cross-source OSM vs DataSF reported (0.818 / 0.878). |
| C7 1:1 scale (M-005.6) | pass | 15 pairs (10 as-built, 5 projection-only out of tile), max 3.6 ppm; as-built pairs under 2 km within 0.001 m; vertical ratio 1.000000 (Salesforce, Millennium); CRS `kaiju:BayTM@v1`, no UTM (`unit_and_crs`). pytest `test_projection.py` (UTM would fail the gate). |
| C8 skyline (M-005.7) | **fail** (independent reference) | V4: vs R-src IoU 0.997, MAE 0.18 px (pass); vs R-ind IoU 0.845, MAE 11.9 px (fail). V5: vs R-src IoU 0.995, MAE 0.12 px (pass); vs R-ind IoU 0.635, MAE 12.0 px (fail). R-ind = 3DEP first-return lidar DSM (PDAL; trees, cranes and towers missing/different in OSM: `known_gaps.md`). V5 excludes Coit Tower itself (the observer stands on it; same rule in all renders). V1-V3 `not_applicable_in_tile`. Captures `captures/skyline_V4.png`, `skyline_V5.png`, `*_mask.png`; overlays `skyline/*_overlay.png` (red R-src, blue R-ind). |
| C9 licences / attribution (M-005.8) | pass | manifest complete (0 problems); attribution asset `Assets/Game/World/Geo/Attribution/GeoAttribution.asset` (dump `export/attribution_asset_dump.json`) holds all 7 strings incl. "© OpenStreetMap contributors (ODbL)" and the ODbL notice; diff in `M-005.8.attribution_diff` (0 missing). ODbL layers flagged as derivative-database candidates (Q-023): `water`, `buildings`, `roads`. Unused sources and why: `Pipelines/geo/config/sources.json` `unused`. |

## B. Reads as San Francisco, at giant scale
| C | Result | Artifact and value |
|---|---|---|
| C10 shoreline, sea level | pass | water at +0.969 m NAVD88 (manifest `water_level_navd88_m`); median offset of the as-built MSL crossing from the OSM coastline 2.5 m over 47 of 50 profiles (`t003.C10_shoreline`); pier cells with water surface 0 of 143,172; water cells with ground above MSL 0. Capture `captures/shoreline_embarcadero.png`. |
| C11 one continuous surface | **fail** (seam median) | 50 shoreline profiles: max adjacent jump 7.0 m, 23 jumps >= 1.5 m all documented (pier deck edges or steps present in the 1 m source), 0 undocumented. Seam: blend adds at most 0.489 m (<= 0.5) beyond the sources' own steps; median |3DEP - CUDEM| in the 10 m feather band 0.457 m (> 0.1; the band hugs the +1.8 m contour, seawalls and 3DEP holes); over all land 0.061 m. `t003.C11_continuous_surface`. Seam with neighbour tile byte-identical (C16). |
| C12 buildings sit right | pass | every base within 0.001 m of the local terrain minimum (in-tile footprints); watertight, outward, consistent winding: EditMode `Extrusion_IsWatertight_ConsistentlyWound_AndOutward`, `PyramidalRoof_IsClosed_ApexAtHeight`; z-fighting series `captures/zfight_{10,100,1000,3000}m.png` with flip fractions 0.036 %, 0.015 %, 0.020 %, 0.005 % (1/4-res: 0.028 %, 0.014 %, 0.023 %, 0.005 %) for a 1.5 mm camera move (`export/capture_views.json`, reversed-Z); roads are terrain decals (no coplanar geometry); 10 overlapping coplanar roof pairs and 4 footprints < 1 m wide reported. |
| C13 roads and faults | pass | 4,131 roads with class and width; 66 bridges (incl. 2 Bay Bridge `Eisenhower` segments) and 173 tunnel ways flagged and not draped; `global/v1/faults_bay.geojson` 3,232 pieces with normalised fields; tile clip empty by design, nearest: Golden Gate fault 11.6 km, Serra 12.7 km, San Andreas SF section 13.6 km, Hayward Northern 13.8 km (`plates/fault_map.png`, pytest `test_faults_tile_clip_empty_by_design_and_nearest_distances`). |
| Reported plates | reported | `plates/height_relations_mech.png` (80 m mech on the seabed at V4, head at 58 m NAVD88), `plates/wading_depth.png` (median 18.9 m, deepest 30.45 m at BayTM 63480.5, 74430.5; 0-2 m 1.4 %, 2-5 8.2 %, 5-10 12.0 %, 10-20 33.7 %, > 20 44.8 %; wadable everywhere: max < 40 m), `captures/skyline_V4.png`, `skyline_V4_270rows.png`, `skyline_V4_eye70.png`, `skyline_V5.png`, `skyline_V5_270rows.png`. Blind "which city" check: for eval-bay. |

## C. Built to extend
| C | Result | Artifact and value |
|---|---|---|
| C14 identity, separability | pass | bids from source ids only (pytest `test_bids_derive_from_source_ids_only`, Core `bld:osm-way:<id>` via `BuildingsLayer.ParseBid`); two independent bakes, 1 vs 4 threads with shuffled input, byte-identical (`logs/determinism_hashes.md`); owner rule across the L0_30_35 boundary (31 straddlers owned here, 20 there, 0 shared; pytest `test_owner_rule_no_building_in_two_tiles`); `part_of` (Transamerica: 10 parts, stepped to 260 m, removed as one structure; EditMode + Unity tests); runtime removal of 20 random bids in 17.5 ms, other buildings and terrain identical in the export (`export/geo_export_after_removal.json`, `t003.C14_removal`). |
| C15 deterministic, idempotent | pass | one command per layer + bake (README); second run with `--offline` (any socket connect raises) prints "up to date" for every step (`logs/second_run_up_to_date.log`); byte-identical outputs (`logs/determinism_hashes.md`); unreachable source fails loudly, writes nothing (`logs/unreachable_source_fails_loudly.log`, pytest `test_offline_missing_source_fails_loudly`). |
| C16 geo_tile@v1, fit for the Bay | pass | 2049^2 pixel-is-point, 1 m, origin (61440, 73728) multiple of 2048, `level: 0` (`tiles/L0_30_36_manifest.json`); shared edge row byte-identical with the real south neighbour L0_30_35 baked from the same sources (`t003.C16_spec`, pytest `test_seam_rows_identical_with_neighbour`); second tile L0_26_37 (Golden Gate) bakes with no code change (`logs/bake_L0_26_37.log`, `tiles/L0_26_37_manifest.json`, valid). r16 rows differ by <= 1 quantisation step between tiles (per-tile range). |
| C17 floating origin | pass | rebasing to each tile-corner origin: anchors unchanged (0 m), terrain samples within 0.0001 m (export rounding); max |scene position| 2,194 m (`export/floating_origin_check.json`); Unity test `FloatingOrigin_ShiftByTileMultiple_KeepsWorldPositionsWithin1mm`. |
| C18 manifest, hygiene | pass | manifest fields per R-004 §3.3 for every layer (`M-005.8`); importer refuses doctored manifests: 10 EditMode cases + tampered file + Unity `Builder_RefusesDoctoredManifest`; pytest `test_doctored_manifest_is_rejected` (7 cases); `Pipelines/geo/cache/` never committed (`logs/cache_not_committed.txt`: 0 files, 0 in history); committed tile 23.5 MB. |
| C19 measurement export | pass | `GeoBatch.All` writes `export/geo_export.json` (float64 BayTM: bids, base/top/height, anchors, footprints; 24,079 terrain samples on physics/render/data surfaces; stats); `m005_check.py check` emits every sub-measure with value, threshold, result and reason. |
| C20 budgets (Assumed) | pass | `tile_stats.json`: 685,685 triangles (terrain 589,457, buildings 87,924, water 8,304) <= 1.5 M; draw calls 118 (one per visible renderer, max over views; target 200); memory 71.2 MB; committed 23.5 MB <= 100; bake 14.0 s <= 300; editor build 2.6 s <= 60. RTIN stated error: max 0.196 m, RMS 0.033 m. Extrapolation: M1 70 tiles 1.65 GB / 16 min bake; Bay 4,624 tiles 109 GB if all at 1 m (Q-027 default: 1 m urban/shoreline only). |
| C21 module rules | pass | `logs/asmdef_dependencies.txt` (only `Game.Core.Contracts` + own assemblies), Unity test `ModuleRules_AsmdefsReferenceOnlyCoreContractsAndOwnAssemblies`; `IGeoLayer` for terrain+bathymetry, water, buildings, roads, faults; flag `world.geo.enabled` default off (`Assets/Game/Config/FeatureFlags/WorldGeo.json`, tests `FeatureFlag_FileExists_AndDefaultsOff`, `Module_InstallsOnlyWhenFlagOn`); config `Assets/Game/Config/WorldGeo.json`; `Assets/Game/World/Geo/MODULE.md`; `Pipelines/geo/README.md`; scenes built through the Editor API only (no scene file committed); `.meta` files committed. |

## Other artifacts
- Captures (unstyled; T-004 not available): `captures/topdown.png`, `oblique_{270,235,305}.png`, `shoreline_embarcadero.png`, skyline set, z-fight series.
- Control points: `control_points.json` (seeded; rules inside). Known gaps: `known_gaps.md`, `known_gaps_candidates.json`.
- Unity batch log summary: `logs/unity_geobatch.log`; bake logs `logs/bake_L0_26_37.log`, `logs/bake_L0_30_35.log`.

## Not met (honest list)
1. M-005.4 default share 13.3 % > 10 % under the R-004 fallback rule (mitigation measured, needs founder approval).
2. M-005.7 vs the independent lidar reference (Assumed thresholds): V4 IoU 0.845 / MAE 11.9 px, V5 IoU 0.635 / MAE 12.0 px.
3. C11 seam median |3DEP - CUDEM| in the feather band 0.457 m > 0.1 m (no blend step > 0.5 m; all-land median 0.061 m).
4. Out-of-tile sub-measures (9) are due at M1; M-005.2 NGS has 0 qualifying marks in this tile.

## Fix cycle 2 (2026-11-14)
**Read this section first:** the tables above give the cycle-1 values; the values below replace them where they differ.
- Code: `d48c70c`..HEAD on `task/T-003`. Tile rebaked at `61aa55e` (`manifest.json` `build.git`, clean).
- `m005_report.json` now holds two blocks side by side:
  - `v1`: M-005@v1, R-004 §6, as before.
  - `v1_1`: M-005@v1.1, `research/R-006.md` §5, Assumed pending founder calibration. Every row carries `metric_version`.
  - `t003`: the acceptance checks outside M-005.
- Tests: pytest 71/71 (`tests/pytest.xml`); dotnet 28/28 (`tests/dotnet_worldgeo.trx`); Unity EditMode 131/131 (`tests/unity_editmode.xml`); Unity PlayMode 1/1 (`tests/unity_playmode.xml`).
- Unity launches waited on the machine queue (`logs/fix_cycle2_unity_queue.log`).

| Defect | Change | Artifact | Measured value |
|---|---|---|---|
| DEF-T003-bay-1 (major) | `vector.exclusion_reason`: `location=underground`; `layer<0` with underground levels or negative `level`, unless the source also gives above-ground levels or a height; `building=ship`. Parts inherit. Records are kept with `excluded`, not extruded, not solid in the water mask. | `buildings.geojson`, manifest `count_excluded`, pytest `test_stations_and_ship_kept_flagged_not_surface`, EditMode `UndergroundStationsAndShip_AreRecordsWithoutGeometry`, `captures/topdown.png` | 12 records excluded: 3 stations + 3 Montgomery parts, 2 ships + 4 ship parts. Salesforce Transit Center (layer -1, 5 levels, 30 m) kept. Default share re-measured below. |
| DEF-T003-bay-2 (major) | Fallback part heights (datasf/levels/default) are capped at their structure's `osm_height`/`datasf` height; `height_capped_to_parent` flag; R-004 order per part. | pytest `test_no_fallback_part_exceeds_its_structure`, EditMode `FallbackPartHeights_NeverExceedTheirStructure` | 53 parts capped; 0 fallback parts above their structure. Tagged-height parts are left as the source gives them (data conflicts, e.g. 555 California parts 237 m vs outline 226 m, reported). |
| DEF-T003-bay-3 (major) | R-ind v2: one `filters.expression` (first return, not class 7/18), then a statistical outlier filter (`mean_k` 8, 2.5 sigma), then a drop of the marked points. Synthetic-point test: classes 7 and 18, second returns and a 300 m spike are all removed. v1.1 products follow the R-006 recipe (`lidar_ref_v11.py`). | `cache/lidar/dsm_first_return_v2__L0_30_36.tif` (meta), `m005_report.json` `v1.m005.M-005.7` reference text | DSM max 357.4 -> 335.4 m; 64.2 M of 66.1 M first returns kept. **v1 vs R-ind:** V4 IoU 0.911 / MAE 6.19 px (was 0.845 / 11.9); V5 IoU 0.797 / MAE 4.77 px (was 0.635 / 12.0). Both still fail the v1 gate (IoU >= 0.90 and MAE <= 3 px). The reference still contains vegetation, cranes and rooftop plant (written into the report). Overlays: `skyline/skyline_V4_overlay.png`, `skyline_V5_overlay.png`. |
| DEF-T003-bay-4 (minor) | `base_navd88_m` = 10th percentile of the surface along the footprint ring. Walls reach `ground_min_navd88_m` - 2 m skirt. | `t003.C12_buildings`, EditMode `NoBuildingFloats_WallsReachBelowTheLowestGround` | Millennium base -0.71 m (was -1.21). w421816061 base 4.06 m (was -6.49). Bases within 0.5 m of the ring p10: pass. Floating buildings: 0. |
| bay-6 (seam) | Surface and threshold unchanged. The built-surface step along the 50 shoreline profiles is reported beside the seam figure. | `t003.C11_continuous_surface.seam` | Seam median \|3DEP - CUDEM\| in the band 0.457 m (as defined, fail). Built-surface steps on the 50 profiles: max 7.0 m, 23 at >= 1.5 m, all documented, 0 undocumented. v1.1 M-005.9 below. |
| DEF-T003-arch-1 | `bake_seconds` moved to `build_info.json`. A forced in-place re-bake with identical content keeps the build block. Determinism hashes the manifest minus `build.{built_utc,git,git_dirty_pipeline}`. | `logs/determinism_hashes.md`, pytest `test_rebake_is_byte_identical_and_order_independent`, `test_manifest_content_is_input_determined` | 6 data files + the manifest content identical across committed / 1 thread / 4 threads with shuffled input. |
| DEF-T003-arch-2 | Unity test: export, remove 20 random no-part buildings, export again; compare. | `Remove20RandomBuildings_OthersAndTerrainIdenticalInExport` | Exactly 20 removed; every other record and all 300 terrain samples byte-identical. |
| DEF-T003-arch-3 | Cache-free tests added: a synthetic-neighbour seam from generated CUDEM-like and 3DEP-like rasters (with nodata holes); the owner rule on 200 synthetic straddlers; a real refused connection (127.0.0.1:9). `m005_check.py --out` added. | `tests/test_fix_cycle2.py` | All pass with no cache. |
| DEF-T003-arch-4 | `WorldGeo.json` is the single source of truth: missing file or key throws. Palette, albedo ramps, road minimum width and smoothness moved there. `GeoSettings.Require` replaces the code defaults. | EditMode `Config_MissingKey_FailsLoudly`, `Importer_WithoutSettings_RefusesInsteadOfUsingCodeDefaults` | The 0.5 m fallback is gone; the importer refuses to build without the config. |
| DEF-T003-arch-5 | MODULE.md uses the §6 labelled lines, states the wiring status (`GeoModule.Install` is called by tests only) and links Q-IDs instead of implementer files. | `Assets/Game/World/Geo/MODULE.md` | — |
| DEF-T003-perf-1 | Draw calls come from the engine's Rendering profiler counters in Play mode: Unity Test Framework PlayMode, Metal device, 1920x1080, soft-shadow main light, 30 warm-up + 120 recorded frames per view. Explicit `Camera.Render` calls inside a batch method read 0 on every counter, so they are not used. | `export/render_stats_playmode.json`, PlayMode test `TileRenderStats_FromProfilerCounters`, pipeline in `export/capture_views.json` (PC_RPAsset, SRP Batcher on, shadow distance 50, 4 cascades) | Draw calls (max over views): obliques 242, V4 v1.1 206, V5 134 (renderer-count estimate was 118). SetPass 8–12. Triangles submitted per frame, all passes: 1.37 M (tile geometry 0.685 M). Within the 1,500 budget, **above the 200 target**. RTIN error is about 2x its setting because the split error accumulates (MODULE.md). |
| DEF-T003-perf-2 | Rendering is per 256 m chunk; every building has its own GameObject and collider; a removal rebuilds one chunk. | `export/geo_export_after_removal.json` stats | 10.2 ms for 20 removals = 0.51 ms per building, chunk rebuild included. |
| DEF-T003-perf-3 | Whole-process memory deltas before vs after the build, after GC; frame time. | `export/tile_stats_unity.json`, `export/render_stats_playmode.json` | Tile objects 71.1 MB. Native allocated +91.5 MB, reserved +269 MB, graphics driver +16.8 MB, managed +94.7 MB. Editor-batch frame 0.8–1.5 ms median on the M5 Pro (not the target PC). |
| DEF-T003-perf-4 | Target PC still undefined (Q-019); all timings are dev-Mac numbers. | — | — |

### M-005@v1 vs M-005@v1.1 (`m005_report.json` `v1.summary` / `v1_1.summary`)
| Sub-measure | v1 | v1.1 | v1.1 value |
|---|---|---|---|
| .1 landmarks | pass (a, b, d) | carried | unchanged (p95 1.12 m vs reference) |
| .2 terrain | pipeline pass, NGS insufficient (0 qualify) | carried | unchanged |
| .3 bathymetry | pass in tile, 4 n/a | carried | unchanged |
| .4 (a–c) heights | pass (100 % within 0.5 m, KS 0.000, fallback validation pass) | carried | — |
| .4 default share | **fail**: 13.3 % by count (13.1 % surface only) | .4(d) **pass** | Area share over structures 6.07 % (count 13.9 %, perimeter 9.4 %; 1,988 structures; excluded: 39 roof, 13 < 20 m², 3 underground, 2 ship, 1 base below MSL in water). If R-006's inheritance + DataSF containment were applied (not in the build): 3.63 %. Adding the cached lidar fill as well: 0.07 %. Assessor storeys: not applied (not cached). |
| .5 footprints | pass (area recall 0.9997, precision 1.000, count 1.000) | pass (R-006 exclusions on both sides) | recall 0.9997 / precision 1.000 / count 1.000 |
| .6 scale | pass | carried | — |
| .7 V4 | **fail** vs R-ind (IoU 0.911, MAE 6.19 px); R-src pass (0.997 / 0.17) | **pass** | Heading 257.6°, hFOV 75°. R-src: IoU 0.996, MAE 0.26 px. R-ind: IoU 0.901, MAE 6.50 px (9.7 mrad), shift 0. Noise floor F_v (R-ind vs R-ind2): IoU 0.804, MAE 12.3 px. Gate: IoU >= 0.784 and >= 0.80; MAE <= 13.3 px. |
| .7 V5 | **fail** vs R-ind (IoU 0.797, MAE 4.77 px) | **pass** | R-src: 0.995 / 0.23 px. R-ind: IoU 0.828, MAE 5.99 px (6.8 mrad). F_v: IoU 0.552, MAE 14.1 px. Skyline columns 43.8 % (>= 25 %, so V5 gates). |
| .7 V4m (70 m eye) | — | reported | R-ind: IoU 0.857, MAE 6.03 px |
| .8 attribution | pass | carried | — |
| .9 shoreline continuity | (C11 seam: 0.457 m, fail) | **fail** | p99 excess step E = 0.276 m (> 0.20); max 0.4895 m (<= 0.50); 181,042 node pairs. Information: D median 0.457 m, p95 4.41 m against a 0.98 m envelope; 31 % of band nodes have \|Δ\|/L > 0.2 (a wider feather would help: R-006 §1). |
| C20 draw calls | renderer estimate 118 | measured | 242 max (Play-mode counters) |
| **Counts** | 18 pass, 3 fail, 9 n/a, 1 insufficient, 1 reported | 21 pass, 1 fail, 9 n/a, 1 insufficient, 2 reported | |

**Caveats on v1.1 .7. Read these before reading those passes as strong:**
1. **The change mask removes 72 structures from both renders**, Transamerica, 181 Fremont and One Market Plaza among them (`skyline_v11_spec.json`).
   - R-006's rule is |lidar p90 − build p90| > 10 m.
   - Many of those structures differ because of the vegetation step, not because of change.
2. **The return-count vegetation test does not separate canopy from roofs in B23.**
   - It flags 40 % of Pioneer Park and 38 % of Redwood Park cells, but also 8.2 % of all roof cells, and about 30 % on tower roofs (`v1_1.m005.M-005.7.recipe.vegetation_calibration`).
   - A diagnostic variant that applies the test only outside footprints gives V4 IoU 0.910 / MAE 5.92 px and V5 0.857 / 5.00 px, with a 53-structure change mask.
3. **The noise floor is weak.** R-ind2 (DataSF 2010 extrusions) agrees with R-ind only at IoU 0.55–0.80, which makes the floor-relative gate lenient.
4. Within-reference noise (V4): p95 vs max cell IoU 0.998; 0.5 vs 1 m cells IoU 0.973; eye ±1.5 m IoU 0.986–0.987.

All of these are R-006 calibration questions (its Q-NEW-d). Overlays: `skyline/skyline_v11_*_overlay_rind.png` (grey = game mask, red = R-ind, blue = R-ind2) and `skyline_v11_*_overlay_rsrc.png`.

### Not fixed in this cycle
- v1 default share 13.3 % by count. This is a rule question for research and the founders (DEF-bay-5). Under v1.1 it passes by area.
- v1 skylines vs R-ind still fail at MAE 6.19 / 4.77 px. Research owns the thresholds, band and viewpoints (DEF-bay-7); this cycle changed only the reference.
- v1.1 M-005.9 p99 0.276 m. The surface was not changed, per this cycle's instruction (the feather width is a pipeline parameter: R-006 §1 suggests L >= |Δ| / 0.2).
- The measured draw calls (242) are above the 200 target. That target is a proposal, not a budget.
