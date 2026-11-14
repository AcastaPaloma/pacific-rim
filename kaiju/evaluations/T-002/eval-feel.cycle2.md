PASS

# T-002 eval-feel, cycle 2 re-check (numerical validity of M-001 / M-002)

- **Subject:** `task/T-002` at `ec5ffba`, tested on a clean `git archive` copy
  (`/private/tmp/claude-501/eval-T-002-validity/clean2/kaiju`, default registry v1.1).
- **Inputs:** my own cycle-1 logs (`gen_gait.py`, `make_gait.py`, `c2_run_gait.sh`), plus two new closed-form estop
  logs, `gait/estop300` (exactly 0.30 g) and `gait/estop306` (2% harder).

## Defects from cycle 1
| Defect | Status | Evidence |
|---|---|---|
| DEF-T002-feel-1: M-001.12 compared unlike quantities | closed | The floor is now (v0 - 0.3)/a_ref = 2.99721 s, matching the measured time to < 0.3 m/s. `estop300` (truth 2.99721 s): 3.00 PASS. `estop306` (truth 2.93844 s): 2.94 FAIL. `estop294` (0.2997 g): now PASS 3.02 |
| DEF-T002-feel-2: calibrate diff kept the "Assumed" source | closed | `registry_proposal`: `status: calibrated, source: "calibrated 2026-11-14 from .../cal10.json (previously: R-001 §9 assumed then)"`. Proposal and refusals unchanged (below) |

## Regression: truth vs cycle 1 vs cycle 2
| Input | Truth (mine) | Cycle 1 | Cycle 2 |
|---|---|---|---|
| M-001.1 Fr, 3 walks (A) | 0.12 / 0.215 / 0.28 | same | same |
| M-001.2 / .3 (A) | 1.05104, 1.04881, 1.05058 / 0.65104, 0.65103, 0.65123, curve 0.64848 | same | same |
| M-001.4 / .5 / .9 (A) | 2.5 / 0.63675 / 0.63675 | 2.5 / 0.63674 / 0.63675 | same |
| M-001.6 net / power (A, V) | 1.0 / V FAIL, about 0.806 | 1.0 / 0.80463 FAIL | same |
| M-001.7 / .8 (A; V thresholds 24.587, 3.392) | 15.0 / 3.0 | same | same |
| M-001.10 steps (A / V) | 2.48749 / 2.47611 | 2.49091 / 2.47778 | same |
| M-001.11 t* (A / V) | 2.82858 / 2.83611 | 2.83794 / 2.8437 | same |
| M-001.12 estop 2.5 m/s^2 (A / V) | 3.5283 / 3.8418 s | 3.54 / 3.86, floors 3.0991 / 3.3655 | 3.54 / 3.86, floors 2.99721 / 3.26352 (= (v0 - 0.3)/0.30 g, ruling) |
| **Multi-capture M-002.1** (A: 9 captures, 300 contacts, 0.12 m on 1) | 0.0061224 FAIL, walk_fr020 R n 19607 | same | same; decisive part now names `walk_fr020` |
| M-002.1 0.097 / 0.099 m, L_f 19.6 | PASS / FAIL | 0.00495 / 0.00505 | same |
| M-002.1 0.0742 / 0.0758 m, L_f 15 | PASS / FAIL | 0.00495 / 0.00505 | same |
| M-002.1 / .4, `tail` | 0.12 m FAIL / 0.996344 | same | same |
| M-002.4, `lag` | 0.985375 FAIL | 0.98537 | 0.98537 |
| M-001.13 panels: none / 2 raters / non-blind / pass / curve median 6 | 3 / 3 / 3 / 0 / 1 (exit codes) | same | same |
| M-003 f: 8 + 1 anchor-misbehaving / 7 + 2 | PASS (1 excluded) / PENDING | exit 0 / 3 | exit 0 / 3 |
| calibrate, 10 captures | M-002.2 cut midpoint 0.00137755 | 0.00137755, FP 0 FF 0 | same; 7 captures exit 2; bad anchor exit 2; registry SHA unchanged |

**Only the M-001.12 floors moved**, as the ruling intends. Every other value is identical to cycle 1.

## Registry v1.1 vs v1.0
- **Thresholds:** every threshold value is identical in M-001 and M-002. **Additions in v1.1:** `steady_captures` (walks + curve) and `min_contacts_per_foot` 1 (derived).
- **M-001.12 floor:** changed by its formula, not by its registry value (a_ref 0.30 g is unchanged).

**New defect:**
- **DEF-T002-feel-3 (minor; pre-existing, also present at `0db318e`): M-001.4 fails a capture at exactly the limit.**
  - What happens: `estop300` brakes at exactly a = 0.30 g = 2.943 m/s^2, which the spec allows (<= 0.30 g). The
    smoothed peak comes out as 2.943 + 2.6e-14, so the margin is -2.6e-14 and M-001.4 is FAIL. The capture therefore
    fails as a whole, even though M-001.12 now passes it.
  - Repro: `estop300` (the closed-form log above), `metrics.py m001 gait/estop300`.
  - PASS would look like: values within about 1e-9 relative of the threshold compare as equal, or the margin is
    rounded before the status is set. A 2%-harder stop still fails.
  - Not a wrong pass, and the error is 1e-14, so it does not change my verdict.
