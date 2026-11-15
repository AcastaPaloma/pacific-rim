# T-108e summary (2026-11-15): L1 B1 over-cap fix, bake_lod.py@8

Decision (lead, founder asleep): cap 100k and the accuracy gates are not lowered. Over-cap L1 B1 forms now climb an **extended merged-block ladder**: the same block generator as T-108d with closing gap, simplification, band half-range and the max/rms error limits scaled by 2, 3, 4, 6, 8 x the R-101 section 8 envelope (2.0 m / 0.6 m at L1); the smallest scale whose blocks reach the cap wins, and ALL errors are MEASURED (exact footprint max/rms vs the L0 footprints, height max/rms), written to the manifest and flagged (`footprint_fit: merged_blocks_extended`, `error_scale`, `error_budget_met: false`, `switch_m102`). Skyline form (as L2) is the last resort for L1 too; it was not needed. `.lodmesh` / index format and the C# reader are unchanged (block `members` and `skyline_fallback` rules already cover it). @7 manifests of tiles within cap stay valid (`GENERATOR_COMPAT`).

## Result (109 flagged tiles re-baked with 5 workers, 0 failed; other 1408 L1..L4 tiles untouched)
| | before (@7) | after (@8) |
|---|---|---|
| L1 B1 over cap | 109 (1.01x..5.3x, median 1.77x) | **0** |
| total over cap (all levels/forms) | 109 | **0** |
| GPU est over budget (those 109) | n/a | 0 (6.9 MB median, 7.2 max, budget 20 MB) |

Scales used: 2x 42 tiles, 3x 34, 4x 25, 6x 6, 8x 2 (no skyline). Measured errors, all 109 tiles (min / median / max): footprint max 2.17 / 4.25 / 8.21 m, footprint rms 0.20 / 0.55 / 1.03 m (the rms stays near the 0.6 m envelope; only max and height exceed it), height max up to 16.0 m (bands). Worst tile L1_14_16: 530k prisms, x6 blocks 99,436 tris, max 7.8 m, rms 0.77 m. Per-tile table: `after_tiles.json`; full budget report: `lod_budget_after.json` (over_cap_total 0, fit classes: nominal/adaptive 3/merged_blocks 11/merged_blocks_extended 109/skyline_fallback 54).

## Selector switch distances (M-102, k = 4, 1158 px/m at 1 m; manifest `switch_m102`)
Distance beyond which the form's measured error is below 1/k output px: rms_d = rms x 1158 x k, max_d = max x 1158 x k / 4 (the guard).
- 109 L1 extended tiles: rms_d 913 .. 4,752 m (median ~2.5 km), max_d 2,515 .. 9,507 m. Envelope tiles are 2.3 km (max) / 2.8 km (rms); L1 is selectable from ~2 km, so most extended tiles are within ~1 x .. 2 x of that, the worst (rms 1.03 m) need ~4.8 km. The streaming selector (`StreamingScheduler`, `ErrRmsM(level)`) still uses only the per-level terrain error: wiring these per-tile distances needs a manifest-fed cost model (M-102 render evidence / T-109), staged, not decided here.
- No L1 skyline tiles. The 54 L2 skyline tiles (T-108d, unchanged, @7 manifests valid): cell 32/64 m, max bound 45.3 / 90.5 m, rms est 13.1 / 26.2 m => max_d 52 / 105 km, rms_d 61 / 122 km, i.e. they are effectively never acceptable by the strict M-102 rule; the extended-block ladder is the intended fix for them (staged follow-up: set `EXTENDED_LEVELS = (1, 2)` and re-bake those 54 tiles).

## Alternatives considered
- B1 culling below a screen-size floor: culling is invisible only if the culled contour share is ~3 % of the tile (rms 0.6 m allows mean-square 0.36 m^2 against ~10 m^2 per vanished 10 m building); too little to cut 1.8 x median, so rejected.
- L1 skyline grid: any 4..16 m cell has a 6..22 m bound and rms 1.6..6.6 m (worse than blocks at the same triangle count), kept only as last resort.
- Raising the cap on measured GPU cost: GPU estimate is 7 MB against the 20 MB budget, so a cap rise is defensible, but T-109 has no measured frame cost yet and the lead rule forbids loosening thresholds; staged as a research item for T-109.
Reproduce: `evidence/T-108e/run_bake.sh` (KAIJU_GEO_CACHE = integration cache), `after_report.py`, `lod_budget_report.py`. Tests: pytest geo 212 passed (+1 test_lod_blocks), dotnet not touched (no C# change).
