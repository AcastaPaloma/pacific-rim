FAIL

# T-003 · eval-bay · tile `L0_30_36` (branch `task/T-003`, evidence at `3dfc6b0`, code `9e1d5e1`)

Evaluator: eval-bay (geographic truth, M-005@v1). Spec: `expectations/T-003.md` C1–C13 plus reported items. Artifacts below are under `.wt/T-003/kaiju/` unless marked. My own scripts and outputs are in `/private/tmp/claude-501/eval-T-003-bay/` (`scripts/`, `rerun/`).

**Checker re-run.** I copied `Pipelines/geo`, the baked tile and `evidence/T-003` to scratch and ran `m005_check.py check` offline (`KAIJU_GEO_OFFLINE=1`). Result: 18 pass, 3 fail, 9 n/a, 1 insufficient_marks, 1 reported. It matches the stored `m005_report.json` field for field; the only difference is `t003.C20_budgets.git_tracked_files`, which is a scratch-copy artefact.
**Checker review.**
- The 9 `not_applicable_in_tile` items really are outside the tile: the GG, Oakland and San Pablo boxes, the Bay-wide box, eyes V1–V3 and landmarks #1, #2, #4, #5 and #12–19 all lie outside lon −122.4076…−122.3843 / lat 37.7869…37.8053. None is counted as a pass, and `m005_overall` = fail.
- Several measures check only against the pipeline's own data:
  - M-005.1(a/b): the R-004 table is itself OSM centroids.
  - M-005.4(a/b): as-built heights are compared with `source_height_m` from the same bake.
  - M-005.2: 0 of 29 NGS marks qualify, so the gate falls to the 3DEP-vs-game pipeline check.
- None of these passes vacuously, but none is independent evidence either. The independent checks below fill that gap.
- The M-005.7 R-ind reference is defective (DEF-T003-bay-3).

## Criteria
| C | Verdict | Stored value | My value / observation | Artifact |
|---|---|---|---|---|
| C1 landmark positions | pass | src max 0.0001 m; ref p95 1.12, max 1.15 m | vs NGS survey: Coit 1.5 m. vs lidar mass centroid: Salesforce 2.0, Transamerica 1.6, Millennium 4.3 m. Ferry 17 m vs NGS flagstaff, explained by the anchor definition (see table) | `m005_report.json` M-005.1; `scripts/spot.py`, `lidtop.py` |
| C2 landmark heights | pass (caveat DEF-4) | 326 / 260 / 197 / 64 m above base; Salesforce tallest | Same values; Wikidata 326 / 260 / 196.6 / 64. Salesforce is clearly tallest in `oblique_235.png` and V5. Millennium base −1.21 m (pit), so its roof is 5.3 m low | M-005.1 d_heights; `plates/height_relations_mech.png` |
| C3 terrain | pass (pipeline gate) | 0 qualifying marks; pipeline RMSE 0.0095 m, max 0.137 m | Independent: EPQS 3 points Δ +0.48 (slope), +0.02, −0.01 m; NGS HT0729 (pavement, vert ADJUSTED) Δ −0.06 m | M-005.2; `scripts/spot.py` |
| C4 bathymetry | pass | wet RMSE 0.0062 m; min −29.481 vs CUDEM −29.485 | `terrain.tif` min −29.481 (R-004: −29.5). Deepest point 30.45 m below MSL, consistent | M-005.3; `plates/wading_depth.png` |
| C5 building heights | **fail** | default 334 / 2,503 = 13.3 % | Count reproduced. Default share is mainly a data gap (DEF-5), but DEF-1 and DEF-2 are real height errors that the self-referential (a)/(b) cannot see | M-005.4 |
| C6 footprints | pass | recall 0.9997, precision 1.000 | Not re-derived. Underground and ship features are "in the source" but should not render (DEF-1) | M-005.5 |
| C7 1:1 scale | pass | 15 pairs, max 3.6 ppm | 4 pairs at 0.95–0.98 ppm, Δ ≤ 0.002 m (pyproj BayTM + GRS80 geodesic). NGS Coit–Ferry geodesic 1323.741 vs BayTM 1323.742 m | M-005.6; `scripts/spot.py` |
| C8 skyline | **fail** | V4 R-ind IoU 0.845 / MAE 11.9 px; V5 0.635 / 12.0 px; R-src 0.997 / 0.995 pass | Reproduced. The reference is contaminated (DEF-3), the build has wrong-height parts (DEF-2), and the V5 band is fragile (DEF-7). See reading (b) | M-005.7; `skyline/*_overlay.png` |
| C9 licences / attribution | pass | 0 problems; 0 missing | Every source has licence, attribution, URL, fetch time and sha256. OSM, water and DataSF lines are in `GeoAttribution.asset`, with the ODbL notice and URL. ODbL layers (water, buildings, roads) flagged. No unclear licence: DataSF is PDDL, the rest US-Gov-PD. Unused ODbL sources listed with reasons | `tiles/.../manifest.json`; `export/attribution_asset_dump.json`; `config/sources.json` |
| C10 shoreline, sea level | pass | water +0.969 m; coast offset median 2.5 m; 0 wet pier cells | NOAA CO-OPS 9414290 datums API: MSL − NAVD88 = 2.773 − 1.804 = **0.969 m** (epoch 1983–2001). Piers dry and solid in `shoreline_embarcadero.png`; waterfront and pier fingers shaped right in `topdown.png` | manifest; captures |
| C11 continuous surface | fail as measured (metric issue) | band median 0.457 m; blend extra step max 0.489 m | No seam, trench or wall at the shoreline in `shoreline_embarcadero.png`, `oblique_*.png` or the depth map. The 0.457 m is the disagreement between the two sources inside the feather band, not a step the blend creates (DEF-6) | t003.C11 |
| C12 buildings sit right | **fail** | bases within 0.001 m of the terrain minimum; z-fight ≤ 0.036 % | 6 underground stations and platforms, and one ship, extruded as 12.66 m surface slabs. Three of them are stacked coplanar slabs on Market Street (DEF-1). 38 buildings ≥ 300 m² have their roofs dropped 5–13 m by DEM pits (DEF-4) | `captures/topdown.png`; `scripts/mill.py` |
| C13 roads, faults | pass | 4,131 roads; 66 bridges, 173 tunnels; fault clip empty, nearest 11.6 km | Bay Bridge deck flagged and not drawn. W2 stands alone in the water (documented). Market Street still has its road under the DEF-1 slab | t003.C13; `plates/fault_map.png` |
| Reported plates | reviewed | — | Plate heights consistent. Mech head at 58 m in 22 m of water: wading reads right. Depth histogram median 18.9 m (deep waterfront tile) | plates |

**Blind "which city" read** (done on `skyline_V4.png` and `skyline_V5.png` before reading any labels; decoys Seattle, Vancouver, Sydney, Chicago):
- **V4 reads as San Francisco**: a waterfront, a fluted column on a green hill (Coit on Telegraph Hill) and a stepped spire. But the dominant tower in V4 is Embarcadero Center 4, built 16 m too tall (DEF-2). Salesforce is out of frame: bearing 227°, against a field of view of 240–300° (DEF-7).
- **V5 is only "a US downtown on a hill"**. Salesforce shows as a plain round shaft. Transamerica is a 5-step wedding cake that reads as a stepped tower rather than a pyramid. 555 California's serrated slab is recognisable to a local. Transamerica is stepped, not a plain box, which the spec accepts at M0 (F-041 later).
- **Founder checklist**: Coit stands on a real hill (base 90.9 m). The grid is not rotated or mirrored. Market Street and Columbus are diagonal, but Market is cut by the DEF-1 slab. Piers are dry. No floating buildings seen. Pier edges are 1 m stair-steps (documented; watch them under the pixel pipeline).

## Independent spot checks
Positions are converted with BayTM@v1 (R-004 §2.1 PROJ string, NAD83(2011) → `pyproj`). "As-built" means the export `anchor_baytm` and the top minus base.
| Item | Reference (source) | Ref BayTM / value | As-built | Δ |
|---|---|---|---|---|
| Coit Tower | NGS HT3007 COIT MONUMENT, ADJUSTED (geodesy.noaa.gov NDE radial API) | 61592.9, 75449.9 | 61591.5, 75450.4 | **1.5 m** |
| Ferry Building | NGS HT3092 FERRY BLDG FLAGSTAFF, ADJUSTED | 62668.8, 74678.8 | 62682.2, 74689.9 | 17.4 m (flagstaff on the west-side clock tower vs footprint centroid; consistent) |
| Salesforce | 3DEP B23 lidar >250 m mass centroid / Wikidata Q14684154 (4 dp) | 62372.4, 74051.5 / 62368.0, 74065.0 | 62374.4, 74051.2 | 2.0 m / 15.2 m (Wikidata ±10 m) |
| Transamerica | lidar >180 m centroid / Wikidata Q216865 | 61857.6, 74649.5 / 61862.3, 74646.6 | 61859.0, 74650.1 | 1.6 m / 4.8 m |
| Millennium | lidar >150 m centroid / Wikidata Q1833555 | 62435.2, 74122.2 / 62446.3, 74106.9 | 62439.4, 74121.5 | 4.3 m / 16.1 m |
| Heights | Wikidata P2048: 326 / 260 / 196.6 / 64 m; lidar Transamerica top 265.1 m NAVD88 | | 326.0 / 260.0 / 197.0 / 64.0; Transamerica top 263.0 | ≤ 0.4 m; lidar −2.1 m (spire tip) |
| Pairs | GRS80 geodesic: Salesforce–Transamerica, Coit–Ferry, Salesforce–Coit, Millennium–Coit | 790.119 / 1329.725 / 1603.358 / 1576.430 | 790.120 / 1329.726 / 1603.360 / 1576.431 | ≤ 0.98 ppm |
| Water | NOAA CO-OPS 9414290 datums API, MSL − NAVD88 | 0.969 | 0.969 | 0 |
| Terrain | USGS EPQS (3DEP 1 m) at (−122.40530, 37.80265), (−122.40360, 37.79250), (−122.39570, 37.79380); NGS HT0729 (pavement) | 68.651 / 14.583 / 3.224 / 3.289 | 69.131 / 14.601 / 3.217 / 3.233 | +0.48 / +0.02 / −0.01 / −0.06 m |
| Deepest | CUDEM min in tile (raw cache) / R-004 | −29.485 / −29.5 | −29.481 | 0.004 m |
| Building roofs vs lidar | 1,920 rendered buildings, median of (lidar − roof) over own roof cells | | | median +2.4 m (rooftop plant); \|Δ\| > 5 m: osm_height 17 %, datasf 21 %, osm_levels 69 %, default 29 % |
| Registration | lidar vs game tall-mask shift search | | | best shift 1–2 m (within ±1.5 m datum): no registration error |

Worst independent error: Millennium at 4.3 m against the lidar mass centroid. The window may include neighbours, and it is within the 5 m tolerance. The larger Wikidata gaps (15–17 m) are within Wikidata's 4-decimal precision.

## Defects
- **DEF-T003-bay-1 (major, C12; brief "no invented buildings"; C5).** Underground features are built as surface buildings.
  - Affected features, all extruded with the 12.66 m default:
    - `location=underground` with layer −1…−3: `osm:w550652205` Montgomery St Station, whose parts are the Concourse `w155378773` (5,453 m²), the Muni platform `w512981834` and the BART platform `w512981835`.
    - `w184956226` Union Square/Market St Station (6,714 m²).
    - `w773030142` Embarcadero Station (6,056 m²).
    - The ship `w281243626` San Francisco Belle (`building=ship`), based on the seabed at −7.46 m.
  - The Montgomery parts are coplanar stacked roofs at 17.03 m NAVD88, a z-fighting risk.
  - `vector.py` has `layer_of()`, but buildings are never filtered on it.
  - Visible as a slab across Market Street in `captures/topdown.png` (crop at about px 1000–1250, 1150–1450).
  - PASS: buildings and parts with `location=underground`, or `layer<0` together with `building:levels:underground`/`level<0`, are excluded (listed in known_gaps). Ships are excluded or flagged. Market Street is clear in the top-down. The default share is re-reported.
- **DEF-T003-bay-2 (major, C5 truth / C8).** A part with only `building:levels` gets levels × 4.22 m even when its parent has a tagged `height`, so parts rise above their own building.
  - 37 parts in 5 structures:
    - Embarcadero Center 4: 189.9 m vs 174 m (it is the dominant tower in V4).
    - Embarcadero Center 1: 177.2 m vs 173 m.
    - 333 Bush: 30 parts at 177.2 m vs 151 m (+26 m).
    - Pasquale's Tower and one unnamed structure (90 m tagged).
  - Found with `scripts/dsm_cmp.py` and a buildings.geojson query.
  - PASS: part heights from levels are capped at the parent's tagged height, or scaled by parent height ÷ parent levels. 0 parts exceed the parent's tagged height.
- **DEF-T003-bay-3 (major, C8 evidence).** The independent lidar reference is built with a PDAL filter that removes nothing.
  - `fetch.py` uses `filters.range` `"ReturnNumber[1:1],Classification![7:7],Classification![18:18]"`. PDAL ORs ranges on the same dimension, so the class test is always true.
  - Tested in the kaiju env on synthetic points: classes 1, 2, 6, 7 and 18 are all kept.
  - The DSM `cache/lidar/dsm_first_return__L0_30_36.tif` has 150 cells at 340–357 m over the Salesforce crown, whose real top is 331 m NAVD88.
  - The reported R-ind IoU/MAE therefore rest on an unfiltered reference. A 3–5 px despike moves IoU only +0.01/+0.02, so noise is not the whole story.
  - PASS: noise classes are excluded (for example `Classification[0:6],Classification[8:17]`, or `filters.expression`), and V4/V5 are recomputed.
- **DEF-T003-bay-4 (minor, C2/C12).** Base = terrain minimum under the footprint picks up pits in the 3DEP bare-earth surface.
  - 38 buildings ≥ 300 m² have bases more than 5 m below the median ground under their footprints.
  - Examples: Millennium base −1.21 m vs median 4.09 m, so its roof is 5.3 m low. `w421816061` is a 132 m tower with base −6.49 m, so its roof is 12.9 m low.
  - PASS: a robust base (for example a low percentile of the footprint ring) with "no floating" kept by a skirt. Roof tops within 1 m of base_street + height.
- **DEF-T003-bay-5 (major as a criterion gate, routed to research/founders).** C5 default share is 13.3 % (10.1 % by area).
  - The measurement is right.
  - The defaults are mostly small structures: median 117 m², p90 739 m². Of those, 26 are parts and 7 are the DEF-1 features.
  - OSM has no height or levels for them, and R-004's IoU > 0.6 DataSF rule rejects row houses that OSM splits.
  - This is a data gap plus a research rule, not a coding defect. The containment mitigation (→ 6.4 %) needs a rule change.
- **DEF-T003-bay-6 (minor, C11).** The seam figure measures the wrong thing.
  - The 0.457 m is the median |3DEP − CUDEM| inside the feather band, which hugs seawalls and the +1.8 m contour. That is disagreement between the sources, not a step in the built surface.
  - The step the blend itself adds is ≤ 0.489 m, and the captures show no step.
  - PASS: research restates C11 as the step the blend adds, or the adjacent-node jump, against each source.
- **DEF-T003-bay-7 (minor, C8 / R-004 §6.7).** The viewpoint and band definitions make the gate fragile.
  - V4 (heading 270°, hFOV 60°) leaves Salesforce out of frame at bearing 227°, so the founder scene "Salesforce towering" cannot be judged there.
  - At V5 (eye 146 m) most of the city lies below the horizon row. IoU then covers only rows 161–288, so a few-pixel difference costs a large share of IoU.
  - PASS: research recalibrates the band and threshold, with V4 heading about 250° or a V4b.

## The three reported failures
- **(a) Default share 13.3 %.** The measurement is correct. It is mostly a data gap, inflated by DEF-1 (go to the implementer). The remainder is the rule question: send it to **research** (the R-004 §1.4 IoU rule versus containment) and to the **founders** to approve. Not a pipeline bug apart from DEF-1.
- **(b) Skyline vs R-ind.** Not camera or registration error: R-src matches at IoU ≥ 0.995, and the lidar-vs-game shift is ≤ 2 m.
  - In 43–57 % of columns the reference sits more than 3 px higher, and the game more than 3 px higher in 3–8 %.
  - Causes in the reference: unfiltered noise (DEF-3), trees (Pioneer Park, Redwood Park), cranes and construction, rooftop plant (+2.4 m median), and tower outlines wider than the OSM parts (`known_gaps.md`).
  - Causes in the build: DEF-2 overshoot, and roofs lowered by DEF-4.
  - Routing: the **implementer** for DEF-2 and DEF-3 (both in their code); **research** for the R-ind recipe (vegetation and crane mask) and the Assumed 0.90 / 3 px threshold and band (DEF-7). Re-run after the fixes before judging the threshold.
- **(c) Seam 0.457 m.** No visible or physical step in the shoreline close-up, the obliques or the depth map. The pier edges are 1 m heightfield stair-steps, which are documented. Send it to **research** to redefine the metric (DEF-6). Nothing goes back to the implementer for this one.

Counts: blocker 0, major 4 (DEF-1, 2, 3, 5), minor 3 (DEF-4, 6, 7).
