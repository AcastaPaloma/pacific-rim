PASS

# T-003 · eval-bay · fix cycle 2 (branch `task/T-003` at `192012b`)

**Basis of the verdict.**
- **Redefined sub-measures use `M-005@v1.1`:** .4(d) default share, .7 skyline vs R-ind, and .9 shoreline continuity.
- **Unchanged sub-measures use `M-005@v1`:** .1, .2, .3, .4(a–c), .5, .6 and .8.
- Under those, every in-tile gate passes. The 9 out-of-tile items stay `not_applicable_in_tile` (due at M1), and M-005.2 NGS is `insufficient_marks`, so the pipeline check gates it.
- All my cycle-1 defects that belonged to the implementer are closed, except a minor residue of DEF-4.
- The v1.1 skyline pass is **weak evidence**. That is a defect in the research recipe, not in the build (DEF-8). It goes to research and does not fail the task.
- Cycle 1: `evaluations/T-003/eval-bay.md`. My scripts and outputs: `/private/tmp/claude-501/eval-T-003-bay/` (`scripts/`, `c2/`).

**Checker re-run.**
- I made a fresh scratch copy of `Pipelines/geo`, the baked tile and `evidence/T-003`, and ran `m005_check.py check --out` with `KAIJU_GEO_OFFLINE=1`.
- Result: v1 = 18 pass / 3 fail / 9 n/a / 1 insufficient / 1 reported. v1.1 = 22 pass / 0 fail / 9 n/a / 1 insufficient / 2 reported.
- It matches the stored `m005_report.json` (both `v1` and `v1_1` blocks) field for field. The only difference is `t003.C20_budgets.git_tracked_files` (16 vs 0), a scratch-copy artefact.
- The 3 v1 fails (default share by count, V4/V5 vs the v1 R-ind) are the items v1.1 redefines.

## Cycle-1 defects
| Defect | Status | Evidence |
|---|---|---|
| bay-1 underground and ship features extruded | **closed** | Montgomery St Station `w550652205` with Concourse `w155378773` and the platforms `w512981834`/`w512981835`; Union Sq/Market St `w184956226`; Embarcadero Station `w773030142`; ship `w281243626`: all have `height_as_built_m` = None in `export/geo_export.json`. Salesforce Transit Center `w542375290` is kept (30 m). The street-level entrance `w616479968` is kept (4.22 m, not underground). Market Street is a clear diagonal in `captures/topdown.png` (my crop at px 500–1250, 1100–1750). v1.1 .5 `no_invented_buildings` pass |
| bay-2 level-derived parts above parent | **closed** | Max part height is now 174.0 / 173.0 / 151.0 m for Embarcadero Center 4 / Embarcadero Center 1 / 333 Bush, each equal to the parent. 55 parts still exceed their outline, but all are `osm_height`-tagged towers on podium outlines (for example 172 m on a 28 m outline). That is correct OSM modelling, not a defect. EC4 no longer dominates V4 (`captures/skyline_v11_V4.png`) |
| bay-3 PDAL filter no-op | **closed** | `fetch.py` now uses `filters.expression` (`ReturnNumber == 1 && Classification != 7 && Classification != 18`). New DSM `dsm_first_return_v2`: max 335.35 m, 0 cells > 340 m (was 150 cells at 340–357 m) |
| bay-4 roofs lowered by DEM pits | **open, minor** | Base is now the p10 of the footprint ring plus a 2 m skirt (EVIDENCE C12; `w421816061` base 4.06 m, was −6.49). 30 buildings ≥ 300 m² still sit > 5 m below their median ground. Most are on slopes, where p10 is defensible. Millennium base is still −0.71 m against 4.09 m median ground, so its roof is about 4.8 m low (`scripts/mill.py`). PASS: Millennium-type pits (ring itself in a hole) use a street-level base |
| bay-5 default share | **routed; passes v1.1** | v1.1 .4(d): 6.07 % by area over 1,988 structures (≤ 10 %), after excluding 3 underground, 2 ship, 1 below-MSL, 39 roof and 13 < 20 m² features. **By count it is 13.9 % of structures**, which is higher than v1's 13.3 % because the unit changed. The R-006 containment chain is only projected (3.6 % area), not applied in the build. Founders should know that 1 structure in 7 is still a 12.66 m default box |
| bay-6 seam metric | **closed by v1.1** | M-005.9: p99 excess step 0.172 m, max 0.396 m (≤ 0.20 / 0.50). C11 profiles: 0 undocumented jumps ≥ 1.5 m. My check: 0 water cells with ground above MSL |
| bay-7 viewpoint/band fragility | **closed by v1.1** | V4 heading 257.6°, hFOV 75°: Salesforce, Transamerica and Coit are all in frame (`skyline_v11_V4.png`). V5 uses skyline columns (43.8 % share, gates) |

## Spot checks repeated (no regression)
| Check | Cycle 1 | Cycle 2 |
|---|---|---|
| Coit vs NGS HT3007 (survey) | 1.5 m | 1.5 m |
| Transamerica / Salesforce / Millennium vs Wikidata | 4.8 / 15.2 / 16.1 m | same; the anchors did not move |
| Heights Salesforce / Transamerica / Millennium / Coit | 326 / 260 / 197 / 64 m | same |
| Bases Salesforce / Transamerica / Millennium / Coit | 4.76 / 3.01 / −1.21 / 90.93 m | 4.89 / 5.30 / −0.71 / 91.07 m (p10-ring rule) |
| 4 pairs vs GRS80 geodesic | ≤ 0.98 ppm | ≤ 0.98 ppm |
| Sea level vs NOAA CO-OPS 9414290 (MSL − NAVD88) | 0.969 | 0.969 |
| EPQS ×3 / NGS HT0729 terrain Δ | +0.48 / +0.02 / −0.01 / −0.06 m | identical |
| Tile minimum vs CUDEM | −29.481 | −29.481 |

## v1.1 changes, read as the metric's user
- **M-005.9 seam: sounder.**
  - It measures what a body meets (the excess step the blend adds over both sources). The thresholds are derived from the sources' stated accuracy, and the 0.98 m envelope explains why v1's 0.1 m was impossible.
  - Gap: it gates steps but not **bias**. A blend that lifts or lowers the seabed smoothly passes, and the wet control points are pure CUDEM, so nothing covers the strip (see side effect, DEF-9).
- **M-005.4(d) area share: partly a relaxation.**
  - Weighting by area matches visual weight, and the exclusions are correct. Those exclusions are the bay-1 defects, now removed from both sides.
  - But it is a post-hoc change of unit that turns a count fail (13.9 %) into a pass. Keep the count share visible to the founders, and hold research to the M1 ≤ 2 % area target.
- **M-005.5 exclusions: sound.** Underground features, ships and roofs are not massing.
- **M-005.7 R-ind recipe: the noise fix is sound, but three parts let defects through (DEF-8).**
  1. The vegetation test flags tower roofs. Share of cells > 100 m flagged and set to DTM: Salesforce 43 %, Transamerica 37 %, 181 Fremont 26 %, Millennium 23 % (from `cache/lidar/rind_v11__L0_30_36.npz` `multi_05`). In `skyline/skyline_v11_V4_overlay_rind.png` the lidar line on Salesforce sits about 90 px below the true roof.
  2. The change mask is circular. Its trigger, |lidar p90 − build p90| > 10 m, compares the build with a reference that DEF-8(1) has already damaged. It removes 72 structures from *both* renders, including Transamerica (absent from the V5 overlay), 181 Fremont (lidar 128 vs 245 m) and Embarcadero Center 1. A real wrong-height building, like cycle 1's EC4 at +16 m or 333 Bush at +26 m, would have been masked rather than caught.
  3. The floor gate is degenerate. F_v is IoU 0.80 / 0.55 and MAE 12.3 / 14.1 px, so the gate collapses to "IoU ≥ 0.80, MAE ≤ 13–15 px". The game's MAE of 6 px would pass at twice that. Within-reference noise (IoU 0.97–0.99, MAE ≤ 2.4 px) shows a much tighter, achievable floor.
- **Conclusion on the skyline.** The v1.1 passes (V4 0.901 / 6.50 px, V5 0.828 / 5.99 px) show no gross projection, registration or scale error: R-src ≥ 0.995, best shift 0 px. They are **not** evidence that tower heights and outlines match the 2023 city.

## Blend side effect (centred 50 m feather)
- **What changed.** In a 25 m strip seaward of the land edge (3.8 % of the tile), the seabed is on average 0.68 m shallower than CUDEM (max 5.91 m; `logs/blend_side_effects.json`).
- **My check over all wet cells** (`scripts/c2_blend.py`):
  - built − CUDEM mean +0.011 m; |Δ| > 0.5 m on 4.6 % of wet cells and > 2 m on 1.1 %;
  - the largest shallowing is +5.74 m at BayTM (63418.5, 73996.5), on the Bay Bridge approach/anchorage edge;
  - 0 wet cells with ground above MSL; only 1 cell went from > 3 m deep to < 1 m.
- **Landmarks and piers.** Bases are unchanged (W2 −18.55 m), piers stay dry (0 of 143,172 cells), and `shoreline_embarcadero.png` and the depth plate show no new shoal, step or beach.
- **Judgement: acceptable at M0.** Nothing in this strip matters for an 80 m machine wading in about 19 m of water.
- **But it is fabricated data.** The strip blends hydro-flattened 3DEP (a constant −1.07 m water surface) into real bathymetry. It already moved the neighbour tile's minimum from −17.91 to −14.71 m. Fix before M1, when the deep-channel tiles arrive (DEF-9).

## Founder look (captures, including 270 rows)
- **V4 (`skyline_v11_V4.png`):** now the founder scene. Salesforce is the tallest shaft on the left, Transamerica's stepped spire is in the centre, and Coit stands on its green hill at the right. Reads as San Francisco.
- **V5 at 270 rows:** Transamerica reads as a spire, 555 California's serrated slab is recognisable, and Salesforce is a plain round shaft. Hand-crafting (F-041) will lift both.
- **Elsewhere:** Market and Columbus are diagonal, piers are dry with 1 m stair-stepped edges (documented), and no floating or sunk buildings are visible.

## New defects
- **DEF-T003-bay-8 (major for the metric; routed to research, not the implementer).**
  - The R-006 §3 R-ind recipe:
    1. its return-count vegetation test deletes 23–43 % of tower-roof cells;
    2. its change mask, keyed on build-vs-reference differences, removes 72 structures including a landmark;
    3. the R-ind2 floor is so low that the gate degenerates to IoU ≥ 0.80 / MAE ≤ 15 px.
  - PASS would look like:
    - vegetation detection restricted to outside footprints, or calibrated on tower roofs to ≤ 2 %;
    - a change mask keyed on dates and permits only, never on the build;
    - landmarks never masked;
    - the floor taken from within-reference perturbations, not 2010 DataSF.
  - Then the founders calibrate.
- **DEF-T003-bay-9 (minor; implementer, with a research gate).**
  - The seaward half of the blend mixes 3DEP's hydro-flattened −1.07 m surface into the seabed: +0.68 m mean, +5.9 m max over 3.8 % of the tile.
  - No gate covers it.
  - PASS: 3DEP weight = 0 where 3DEP is hydro-flattened or inside its water mask, so the seabed stays CUDEM within 0.1 m. Research adds a bias gate, for example the mean of built − CUDEM in the seaward strip ≤ 0.1 m.

Counts: closed 5 (bay-1, 2, 3, 6, 7); open 1 minor (bay-4); routed 1 (bay-5, founders: count share 13.9 %); new 2 (bay-8 to research, bay-9 minor to the implementer and research).
