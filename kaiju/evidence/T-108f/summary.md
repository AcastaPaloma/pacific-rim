# T-108f summary (2026-11-15): L2 skyline tiles -> extended merged blocks (bake_lod.py@8, `EXTENDED_LEVELS = (1, 2)`)

Change: one constant in `Pipelines/geo/kaiju_geo/lod.py` (no version bump: @8 manifests stay valid, the tiles were re-baked with --force). Cap 100k and all accuracy gates unchanged; errors are measured and flagged exactly as for L1 (T-108e).
Re-baked: the 54 L2 B1 tiles that held the skyline form (`tiles.txt`), 0 failed (a disk-low guard stop at 46/54 was resumed for the last 8: `bake_part1.log`, `bake_part2.log`; scripts `run_bake.sh`, `run_bake2.sh`). Other tiles untouched.

| | before (@8, L1 fixed) | after |
|---|---|---|
| over cap (all levels/forms) | 0 | 0 |
| L2 B1 skyline_fallback tiles | 54 | **0** (all 54 fit with blocks, max 0.9995 x cap) |
| fit classes whole pyramid | nominal 1629, merged_blocks 11, merged_blocks_extended 109, adaptive 3, skyline 54 | nominal 1629, merged_blocks 11, merged_blocks_extended 163, adaptive 3, skyline 0 |
| outside error envelope (flagged) | 163 | 163 (54 swap skyline for blocks) |
| GPU est over budget | 0 | 0 (max 8.36 MB vs 15 MB per tile) |

Scales used: 2x 18 tiles, 3x 14, 4x 14, 6x 6, 8x 2 (no skyline needed, so nothing left to list).

## Measured errors (54 tiles, min / median / max)
- footprint max 3.46 / 9.69 / 23.82 m; footprint rms 0.47 / 1.30 / 2.78 m; height max 4.6 .. 32.0 m (bands).
- Skyline before: max bound 45.3 / 90.5 m, rms est 13.1 / 26.2 m (cell 32 / 64 m).

## M-102 switch distances (k = 4; manifest `switch_m102`)
- Before: max_d 52 / 105 km, rms_d 61 / 122 km (never acceptable).
- After: max_d 4.0 / 11.2 / 27.6 km (min / median / max), rms_d 2.2 / 6.0 / 12.9 km. A 4 .. 9x improvement in the rms-limited distance (about 10x median), now inside the reachable L2 range. The streaming selector still uses the per-level terrain error only; wiring per-tile distances stays staged under M-102/T-109 (as in T-108e).

Files: `lod_budget_before.json`, `lod_budget_after.json`, `after_tiles.json` (per tile), `after_report.py`. Tests: pytest Pipelines/geo 212 passed, 8 skipped.
