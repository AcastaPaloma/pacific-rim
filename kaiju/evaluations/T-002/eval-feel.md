PASS

# T-002 eval-feel: are the M-001 / M-002 tools numerically valid?

Evaluator: eval-feel, 2026-11-14. Subject: branch `task/T-002`, commit `0db318e`. Question: metric validity only
(criteria C3, C4, C6, C8, C9, C12 and C16 for M-001 / M-002, plus the gait readings against `expectations/T-002.md`).

**Code under test.** During testing the shared worktree `.wt/T-002` gained uncommitted edits (`gait/m001.py`,
`gait/m002.py`, `registry.py`) and new `registry/M-00x@v1.1.yaml` files, all at 11:41-11:42. I did not use them.
Every result below comes from a clean `git archive 0db318e` copy
(`/private/tmp/claude-501/eval-T-002-validity/clean/kaiju`, registry v1.0).

**Inputs (my own, independent of `fixtures/gen_gait.py`).** Everything is in `/private/tmp/claude-501/eval-T-002-validity/`:
- `gen_gait.py` writes `gait_log@v1` from closed-form motion. It uses Unity's left-handed, y-up frame with
  fwd(psi) = (sin psi, 0, cos psi) and right(psi) = (cos psi, 0, -sin psi). The CoM is integrated exactly from the
  analytic speed and heading. Feet are planted at the mid-stance CoM +- 4.8 m along `right`. Positions are float64 at
  a world offset of about 5.5 km / 10.2 km. Slips are applied exactly as linear drift over the loaded samples.
- `make_gait.py` defines the sets, and `run_gait.sh` runs `metrics.py m001|m002 <dir>`.
- **Set A:** the reference unit with all 9 captures, heading 37 deg (walks) and 160 deg (curve, which crosses
  +-180). The header declares emitter thresholds of 0.05 / 0.04 m g, which differ from the spec's 0.03 / 0.01 m g.
  `walk_fr020` has 100 strides (202 contacts, 300 over the whole set), with one 0.12 m slip on foot R (contact
  starting n = 19607) at 97 deg.
- **Set V:** a variant unit (l = 50 m, h = 52.8 m, L_f = 23 m, W_f = 9 m, m = 1.5e7 kg, P = 1e8 W, T_nom = 7.2 s).
- All logs pass `validate gait_log` (exit 0).

## Results: truth vs tool (relative error)
| Sub-measure | My truth | Tool | Err | Verdict |
|---|---|---|---|---|
| M-001.1 Fr, walk_fr010 / fr020 / fr030 (A) | 0.12 / 0.215 / 0.28 | 0.12 / 0.215 / 0.28 | 0 | ok |
| M-001.1 cruise vs `fr_cruise_nom` 0.20 | 0.215, within 0.03 | 0.215 PASS | 0 | ok |
| M-001.1 curve, v = 9.1209 m/s at 4 deg/s | chord rule (spec: displacement / stride time) gives v_bar below the arc speed: Fr about 0.197 vs 0.200 | 0.19714 | literal | ok |
| M-001.2 T / T_Alexander, three walks | 1.05104 / 1.04881 / 1.05058 | identical | 0 | ok |
| M-001.3 duty beta (stance 250/384, 222/341, 211/324, curve 214/330) | 0.65104 / 0.65103 / 0.65123 / 0.64848 | identical, per foot | 0 | ok |
| M-001.4 peak fore-aft, start 1.0 / stop 1.5 / estop 2.5 m/s^2 along a 37 deg heading | 1.0 / 1.5 / 2.5; max 2.5 | 1.0 / 1.5 / 2.5 (unsmoothed printed beside: same) | 0 | ok |
| M-001.5 peak lateral, curve v x omega | 9.1209 x 0.069813 = 0.63675 | 0.63674 (unsmoothed 0.636751) | 2e-5 | ok |
| M-001.5 threshold, V (0.085 g x [(W_f/2)/h] / ref ratio) | 0.83236 | 0.832364 | 0 | ok |
| M-001.6 net start accel (A) | 1.0 | 1.0 PASS | 0 | ok |
| M-001.6 power limit, V (P = 1e8 from the header): a = 1.0 vs (P - c m g v)/(m v) | fails above v = 4.47 m/s; worst excess about 0.806 (at the top of the smoothed ramp) | 0.80463 FAIL | 0.2% | ok |
| M-001.7 peak yaw rate (turn: 3 deg/s^2 to 15 deg/s plateau, 90 deg total) | 15.0 | 15.0 (curve 4.0, unwrapped across +-180) | 0 | ok |
| M-001.7 / .8 thresholds, V | 26.7 x sqrt(42.4/50) = 24.587; 4.0 x 42.4/50 = 3.392 | 24.5872 / 3.392 | 0 | ok |
| M-001.8 peak yaw accel | 3.0 (curve 2.0) | 3.0 / 2.0 | 0 | ok |
| M-001.9 abs(omega) x v_bar, curve | 0.63675 | 0.63675 | 0 | ok |
| M-001.10 start, in steps of T_nom/2 = 3.3 s (A) / 3.6 s (V) | 2.48749 / 2.47611 | 2.49091 / 2.47778 | 0.14% / 0.07% | ok |
| M-001.11 stop at 1.5 m/s^2: steps and t* (A) | 1.78198 / 2.82858 | 1.78788 / 2.83794 | 0.33% | ok |
| M-001.11 t*, V | 2.83611 | 2.8437 | 0.27% | ok |
| M-001.12 estop at 2.5 m/s^2 (A / V), floor v/(0.30 g) | 3.5283 s, floor 3.0991 / 3.8418 s, floor 3.3655 | 3.54 / 3.86; floors 3.09915 / 3.36546 | 0.33% / 0.47% | ok |
| M-001.12 estop at 0.2997 g | time to stop 3.102 s >= 3.1, physically allowed | FAIL 3.02 (time to < 0.3 m/s) | reading | DEF-feel-1 |
| M-002.1, 0.12 m on 1 of 300 contacts (A) | 0.12/19.6 = 0.0061224, FAIL | 0.00612245 FAIL; worst = walk_fr020 foot R, n_begin 19607, t 392.14 s, 0.612 % L_f, named first | 0 | ok |
| M-002.1, 0.097 vs 0.099 m, L_f 19.6 (threshold 0.098) | 0.004949 PASS / 0.005051 FAIL | 0.00495 PASS / 0.00505 FAIL | 0 | ok |
| M-002.1, 0.0742 vs 0.0758 m, L_f 15 from the header (threshold 0.075) | PASS / FAIL | PASS / FAIL, slip 0.0758 m reported | 0 | ok |
| M-002.1, slip only in the force tail F_off <= fn < F_on, emitter flag 0 there (`tail`) | spec hysteresis keeps the contact on, s = 0.12 m, FAIL | 0.120000 m FAIL, contact L n_begin 1364 | 0 | ok |
| M-002.4, `tail` (20 disagreeing foot-steps of 5470) | 0.996344 PASS | 0.99634 PASS | 0 | ok |
| M-002.4, emitter flag 5 steps late at 16 contacts (`lag`) | 1 - 80/5470 = 0.985375 FAIL | 0.98537 FAIL | 0 | ok |
| C9 header thresholds 0.05 / 0.04 m g (A) | flagged, spec values used | `NON_REFERENCE_NORMALISERS` + `NON_SPEC_CONTACT_THRESHOLDS`, spec 2.943 / 0.981 MN used | - | ok |
| C9 variant normalisers (V) | warn, thresholds from the header | flagged (11 keys), all thresholds above scale | - | ok |

**Largest truth-vs-tool error: 0.47%** (M-001.12 on V, 3.86 s vs 3.8418 s). It comes from the 0.02 s sampling of
"first sample below 0.3 m/s" plus the smoothing kink, and it is inside the 1% tolerance. Every closed-form quantity
that does not depend on sampling (Fr, Alexander ratio, beta, accelerations, yaw rate and acceleration, slips,
agreement) matches to 2e-5 or better.

## Panel and calibration (C8, C16)
- **M-001.13 (C8), set A.** No file: PENDING, INCOMPLETE (exit 3), `headless_verdict` PASS. 2 blind raters, or 3
  with one not blind: PENDING (exit 3). 3 blind raters at 8 / 8 / 9: PASS, `PASS (provisional: 7 assumed)`. Curve
  scenario median 6: M-001.13 FAIL, exit 1, `headless_verdict` still PASS.
- **M-003 f (C8, anchor exclusion).**
  - 8 good raters plus 1 rater who scores a negative anchor 7 and every capture 1: that rater is excluded
    ("scored a negative anchor >= 6"), mean 8.0, PASS. With the rater kept, the mean would be 7.2.
  - 7 good raters plus 2 such raters: both excluded, 7 < 8, PENDING / INCOMPLETE.
- **calibrate (C16).** Inputs: 10 single-walk M-002 reports with one slip each (0.02-0.11 m), rated 9 when the slip
  is below 0.06 m and 5 otherwise, by 3 blind raters plus anchors.
  - The tunable (assumed) sub-measure is M-002.2 (p95 over 12 contacts = 0.45 x slip). The known cut lies between
    0.0012628 and 0.0014923 L_f. The tool proposes 0.00137755, which is exactly the midpoint, with FP 0 and FF 0
    (current value: FP 4). It also prints rater agreement 1.0 and a v1.0 to v1.1 diff.
  - It refuses with 7 captures (exit 2) and with a negative anchor at median 7 (exit 2), and refuses `--out` inside
    `registry/` (exit 2).
  - Registry SHA-256 is unchanged after all runs.
  - M-002.1 (`derived`) is not proposed. This is consistent with "tunable" meaning Assumed.

## Spec readings reviewed (gait G1-G20)
- **M-001.12 / M-001.6 (reconciled-spec reading): agree.** The registry holds a_ref 0.30 g and c 0.05 as
  `assumed_pending_calibration`, and P comes from header `reactor_power_W`. Set V shows the power term is computed
  from the header. The mass-variant clause is `info.mass_variants` PENDING, non-gating.
- **Steps (G1): agree.** Steps are T_nom/2 from the header, and both the steps and t* bounds of M-001.11 are gated
  (G12 correctly notes that t* is binding at the reference).
- **G11: disagree (DEF-feel-1).** The emergency floor is a time to v = 0, v/(0.30 g), but it is compared with a
  time to < 0.3 m/s. A stop exactly at the allowed 0.30 g therefore fails by 3%. This makes the metric harder to
  pass, not easier, and the reading is listed.
- **G8:** scaling M-001.5 / .9 by (W_f/2)/h and M-001.8 by 42.4/l. Not in the table, but listed and consistent with
  R-001 §2/§3 and procedure step 4. Agree.
- G3, G4, G5, G6, G7 (unwrap verified across +-180), G10, G14, G15, G17 (no grace step) and G20 (spec hysteresis
  over the emitter flag, verified by `tail` and `lag`) are literal. None of them made a gate easier to pass on my
  inputs. No gait reading departs from the spec without being listed.

## Defects
- **DEF-T002-feel-1 (minor): M-001.12 compares unlike quantities.** The measured time ends at speed < 0.3 m/s, but
  the floor is v_cruise / a_ref, a time to zero. Repro (clean copy): build an `estop` capture with
  `gen_gait.build(..., sp = clip(vc - (t-20) x 2.940057, 0, vc))` (`make_gait.py`, case `estop294`), then run
  `metrics.py m001 gait/estop294`. Result: M-001.4 PASS (2.94006 <= 2.943) and M-001.12 FAIL (3.02 < 3.099).
  PASS would look like: either the floor becomes (v0 - 0.3)/a_ref, or the time is measured to v = 0. Then this
  capture passes and 0.31 g still fails. Report as a spec question to the scribe (R-001 §4 does not say where an
  emergency stop ends).
- **DEF-T002-feel-2 (minor): the calibrate diff keeps an outdated source.** When the proposed diff flips an entry to
  `status: calibrated`, it keeps `source: "R-001 §9 Assumed"`, so a reviewer who applies it as is records a
  contradictory source. Repro: `metrics.py calibrate --ratings ratings/cal10.json --reports out/cal` and read
  `registry_proposal.diff`. PASS would look like: the diff also rewrites `source` to name the ratings file and date.

No blocker or major defect: no value is wrong beyond 1% and no wrong pass was found; a single bad contact fails
M-002 and is named.
