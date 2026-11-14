PASS

# T-002 eval-feel, cycle 3 final re-check (numerical validity of M-001 / M-002)

- **Subject:** `task/T-002` at `4e29ee9`, tested on a clean `git archive` copy (`.../eval-T-002-validity/clean3`).
- **Registry:** the v1.1 files are byte-identical to cycle 2.
- **Inputs:** my own closed-form logs (`c3_run_gait.sh`), plus three new ones for the comparison tolerance:
  - `estop_1e6`: a = 0.30 g x (1 + 1e-6).
  - `slip_eq`: one slip of exactly 0.098 m.
  - `slip_1e6`: one slip of 0.098 x (1 + 1e-6) m.

## Regression and tolerance checks: truth vs cycle 2 vs cycle 3
| Input | Truth (mine) | Cycle 2 | Cycle 3 |
|---|---|---|---|
| Set A (9 captures), V, p097, p099, f15_0742, f15_0758, tail, lag, estop294: all M-001 / M-002 sub-measures | as in eval-feel.md | per eval-feel.cycle2.md | summary text identical to cycle 2 (every value, status, threshold) |
| `estop300`, exactly 0.30 g = 2.943 m/s^2: M-001.4 / M-001.12 | <= 0.30 g PASS / 2.99721 s PASS | FAIL (2.943 + 2.6e-14) / PASS 3.00 | **PASS 2.943** / PASS 3.00 |
| `estop306`, 2% harder | FAIL / FAIL (2.93844 s) | FAIL 3.00186 / FAIL 2.94 | same |
| `estop_1e6`, 1e-6 above 0.30 g | M-001.4 FAIL | - | FAIL 2.943 |
| `slip_eq`, exactly 0.098 m on L_f 19.6 (spec: strictly < 0.005) | FAIL (equal is not below) | - | FAIL 0.0049999999999 (equal within 1e-9) |
| `slip_1e6`, 1e-6 above | FAIL | - | FAIL 0.00500000499998 |
| M-001.13 panels: none / 2 raters / non-blind / pass / curve median 6 | exit 3 / 3 / 3 / 0 / 1 | same | same |
| M-003 f: 8 + 1 excluded / 7 + 2 excluded | exit 0 / 3 | same | same |
| calibrate, 10 captures / 7 captures | M-002.2 cut midpoint 0.00137755 / refuse | 0.00137755, FP 0 FF 0 / exit 2 | same / exit 2 |

**No value moved.** The only change is the intended one: M-001.4 at exactly the limit now passes, and 1e-6 above
any threshold I tested still fails.

**Check on the strict "<" in M-002.1.** The tool treats a value equal to the threshold within 1e-9 as equal and fails
it (`slip_eq`). That matches the spec's "< 0.098 m" and is not a loosened gate.

## Defect status
| Defect | Status |
|---|---|
| DEF-T002-feel-1 (M-001.12 compared unlike quantities) | closed (unchanged since cycle 2) |
| DEF-T002-feel-2 (calibrate diff kept the "Assumed" source) | closed (unchanged) |
| DEF-T002-feel-3 (M-001.4 failed a stop at exactly 0.30 g) | **closed** (`estop300` PASS, `estop_1e6` FAIL) |

**New defects:** none.
