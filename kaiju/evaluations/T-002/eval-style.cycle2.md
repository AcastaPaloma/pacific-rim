PASS

# T-002 eval-style, cycle 2 re-check (numerical validity of M-003 / M-004)

- **Subject:** `task/T-002` at `ec5ffba`, tested on a clean `git archive` copy
  (`/private/tmp/claude-501/eval-T-002-validity/clean2/kaiju`).
- **Registry:** the default revision is now v1.1, and every report names it.
- **Inputs:** my own cycle-1 inputs and scripts, unchanged (`gen_m003.py`, `gen_m004.py`, `c2_run_m003.sh`). The only
  new input is the two-profile set `style/multi`.

## Defects from cycle 1
| Defect | Status | Evidence |
|---|---|---|
| DEF-T002-style-1: c1 crashed when all band means were equal | closed | `flat` set: exit 1 (was 4). c1 FAIL on span 0.0 < 15, rho NOT_RUN (undefined), max drop 0.0 |
| DEF-T002-style-2: O read 0.0 when undefined | closed | `poster`, `smooth`, `flat`: M-003.b.O NOT_RUN, note "O undefined: no frame has enough interleaved windows (R-002: '-')". Where O is defined (Bayer 1.1656, noise 0.1014) it is unchanged |

## Regression: truth vs cycle 1 vs cycle 2
| Input | Truth (mine) | Cycle 1 | Cycle 2 |
|---|---|---|---|
| M-003.a internal / output, `inject` frame 5 | 0.967448 | 0.967448 / 0.967448 | same; worst frame and #595974 (4800 px) still named |
| M-003.a, unquantised ramp | 0.638889 | 0.6389 FAIL | 0.6389 FAIL |
| M-003.a output, bilinear | 0.680725 | 0.6807 FAIL | 0.6807 FAIL |
| M-003.d-iv point / bilinear | 1.0 / 0.073676 | 1.0 / 0.0737 | 1.0 / 0.0737 |
| M-003.b DIG / O, Bayer / noise (R-002 0.70 / 1.12, 0.58 / 0.06) | as R-002 | 0.755 / 1.166, 0.525 / 0.101 | same |
| M-003.b DIG, posterised / ramp | 0 / ~0 | 0 / 0 FAIL | 0 / 0 FAIL |
| M-003.c1 monotone / reversed | span 43.69 / drop 11.82 | PASS / FAIL 11.824 | same |
| M-003.c2 pass / rising / signed-inverted | pass / fail 4.28 / signed pass | PASS / FAIL 4.281 / PASS (abs in info) | same |
| M-003.d-i / d-ii | 8 / 1 | 8 / 1 | 8 / 1 |
| M-003 flat, one depth band | b, c1 not run | INCOMPLETE | INCOMPLETE |
| **Two profiles** (`multi`: clean slate_teal + warm_rust_sepia with 300 px at dE00 14.54 in frame 5) | a internal 0.967448 at rust frame 5 (worst member); c1 spans 43.69 (slate), 63.24 (rust) | not run | a.internal FAIL 0.967448 at `gameplay_rust/f000005`, per-frame values listed for both profiles; DIG reported per profile (1.0 and 1.0); c1 parts 43.688 and 63.240 |
| M-004 S0 injected | 0.0065104 / F 0.0054253 | same | same |
| M-004 S0 temporal noise | 0.500167 / 0.250595 | same | same |
| M-004 S0 clean | 0 / 0 / 0 | PASS | PASS |
| M-004 T3, 1 px/frame | S_px 0.0043745 / F 0.0044092 | same | same; C_px 0.0043345 unchanged |
| Baseline ref_01A, ref_03B, ref_06A | R-002 tables | match | `comparison.md` rows byte-identical to cycle 1 |
| CIEDE2000, 13 Sharma pairs | table | 4.1e-5 | colour code unchanged; every A_2 above identical |

**No value moved.** The new pooling gives the correct worst member and per-profile parts on my two-profile input.

## Registry v1.1 vs v1.0
- **Thresholds:** every threshold value is identical in M-003 and M-004.
- **Additions in M-004 v1.1:** procedure entry `index_colour_tol_de00` 2.0 (palette_index cross-check) and threshold
  `crawl_min_interleaved_share` 0.01 (assumed_pending_calibration; below it, C_px is NOT_APPLICABLE). None of my inputs
  falls below it.
- **M-003.b.O:** kind changed from `info` to `advisory`. It has no threshold and cannot flip a verdict.
- **Cross-check in action:** it correctly rejected my first two-profile attempt, where 300 px marked 255 actually lay
  within dE00 2 of a warm_rust_sepia colour. I fixed the input; this was not a tool defect.

## New defects
None.
