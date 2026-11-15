PASS

# T-002 eval-style, cycle 3 final re-check (numerical validity of M-003 / M-004)

- **Subject:** `task/T-002` at `4e29ee9`, tested on a clean `git archive` copy (`.../eval-T-002-validity/clean3`).
- **Registry:** the v1.1 files are byte-identical to cycle 2.
- **Inputs:** my own cycle-1 and cycle-2 inputs (`c3_run_m003.sh`), plus three new inputs:
  - `offsets.py` shifts each frame's point-upscaled output by (i mod 4, (i div 4) mod 4) output px and declares
    the offset per frame, or wrongly declares (0, 0).
  - `m004_nodither`: two flat halves, no dither.

## Regression and new checks: truth vs cycle 2 vs cycle 3
| Input | Truth (mine) | Cycle 2 | Cycle 3 |
|---|---|---|---|
| All 11 cycle-1 M-003 sets (a, b, c1, c2, d, e) | as in eval-style.md | per eval-style.cycle2.md | summary text byte-identical to cycle 2 |
| Two profiles `multi` | a.internal 0.967448 at rust frame 5 | 0.967448 FAIL | 0.967448 FAIL; c1 PASS |
| Baseline ref_01A, ref_03B, ref_06A | R-002 tables | match | rows byte-identical |
| M-004 S0 injected / temporal noise | 0.0065104, F 0.0054253 / 0.500167, F 0.250595 | same | same |
| M-004 T3, 1 px/frame | S_px 0.0043745 / F 0.0044092 | same (C_px 0.0043345) | same |
| M-004 S0 clean, dithered texture | 0 / 0 / 0 | PASS | S0, C_px, F PASS |
| **New:** M-003 d-iv, per-frame offsets declared (12 frames, offsets 0-3 px) | 1.0 (my block count) | - | 1.0 PASS; a.output 1.0; validator accepts |
| **New:** M-003 d-iv, same frames, offsets wrongly declared (0, 0) | worst frame 0.20497 (FAIL) | - | 0.205 FAIL, verdict FAIL |
| **New:** M-004 T3 with per-frame output offsets | unchanged: 0.0043745 / 0.0044092 | - | 0.0043745 / C_px 0.0043345 / F 0.0044092 |
| **New:** M-004 S0, no dither (`m004_nodither`) | C_px unmeasurable, so not a pass | - | S0 0 PASS, F 0 PASS, C_px.S0 NOT_RUN, verdict INCOMPLETE |

**No value moved.** The dithered S0 measures C_px (PASS, 0.0); the undithered one leaves C_px NOT_RUN, so the result
is INCOMPLETE rather than PASS.

**Limit of this check.** Every M-004 set I built is a single sequence, so each one is INCOMPLETE on coverage (the six
motion classes). I did not see a set-level M-004 PASS; I only confirmed that the dithered sub-measures pass.

## Defect status
| Defect | Status |
|---|---|
| DEF-T002-style-1 (c1 crash when band means are equal) | closed (flat set: c1 FAIL on span, exit 1; unchanged since cycle 2) |
| DEF-T002-style-2 (O reported as 0.0 when undefined) | closed (O NOT_RUN with the R-002 "-" note; unchanged) |

**New defects:** none.
