# T-201b: walk_mode 2 speed, 7 to 9 m/s

**Result: last stable rung = 7.5 m/s step-reach cap (about 7.4 m/s walked, Froude 0.13), up from 5.8 m/s.** All gates unchanged (no fall, pelvis tilt <= 5 deg, mean foot slip <= 0.5 m, 8 seeds x 63 s plus start, stop, estop, turn90_inplace, curve). 8 m/s misses the slip gate by 0.05 to 0.1 m, 9 m/s does not hold. Override: `Assets/Game/Config/Mech/walk_tuning_fast.json` (the T-201 6 m/s file is kept as `walk_tuning_fast6.json`). `walk_tuning.json` and walk_mode 0 untouched. T-005 files untouched.

## Diagnosis (why 7 m/s failed stop/estop)
Gait logs of stop at cap 7 (`gait/diag_stop_cap7_baseline_s0.gait.jsonl.gz`, `scripts/slipt.py`):
- It is not the step-reach on deceleration. A decel-aware foot target (`kin_dpred`, below) changed nothing.
- The pelvis servo is force-capped (`kin_f_cap` 5 % W horizontal, at its cap the whole walk) and the pelvis lags the planned pelvis (`kin` log field, kB minus root) by about 10.7 m at 7 m/s. At the stop the plan halts, the lag collapses (10.7 m to 0 in about 5 s), the pelvis moves over a planted foot and drags it: one contact slips 3.5 m, the next 1.4 m, the rest creep 0.5 m each; these three to four contacts add 0.12 to the per-contact mean, which in cruise is already 0.4 to 0.47. Hence stop 0.52 / estop 0.57.
- Fix: a bigger force cap (`kin_f_cap` 0.07 to 0.10) lets the pelvis follow the plan, so the lag collapse is smaller and faster.

## Runs (9 Unity batch runs; summaries `summary_run_*.txt`, variants `variants/`)
| run | what | result |
|---|---|---|
| a | cap 7, stop/estop, 4 seeds: baseline (tscale .58, gain 1.75), `kin_dpred 1`, `kin_dpred` on the plain config | baseline slip 0.52/0.57 (reproduces T-201); dpred no help (0.53/0.67) |
| b, c | `kin_ikmeas 1` (leg IK from the measured hip, planted feet held at a lag-shifted world point) | b: collapse in stand (height from the measured hip); c (plan height, measured horizontal): worse, speed 3 m/s, slip up to 20 m, tilt 17 deg, one fall. The legs then brake the pelvis, the assist only tows it. Rejected, flag stays off |
| d | cap 7, stop/estop, 4 seeds: `kin_f_cap` 0.07 / 0.10 | slip 0.40-0.42 (stop and estop), tilt <= 4.2: pass |
| e | cap 7, `kin_f_cap` 0.07, 8 seeds, walk_v7 + start/stop/estop/turn90/curve | all pass: v 6.89 m/s, slip 0.43 / 0.43 / 0.40 / 0.46 / 0.19 / 0.42 (walk, start, stop, estop, turn, curve), tilt <= 4.5, no fall. 7.5 at 2 seeds: not yet (0.5 to 0.6 with 0.07; 0.1 gives estop 0.52, walk 0.55) |
| f | 7.5 factorial (f_cap 0.1 or 0.12; gain 1.2; tscale 0.5; clear 0.15; fz_cap 0.08), 2 seeds | f_cap 0.1 + gain 1.2 passes walk, stop, estop (0.48 / 0.40 / 0.45) |
| g | 7.5 with f_cap 0.1, gain 1.2, 8 seeds, all scenarios; 8 m/s scouts | **7.5 passes all**: v 7.39 m/s, slip walk 0.48, start 0.45, stop 0.41, estop 0.45, turn 0.17, curve 0.48; tilt <= 3.9; no fall. 8 m/s: slip 0.54 to 0.61 (fail) |
| h | 8 m/s variants (f_cap 0.1/0.12, gain 1.2, clear 0.15, ds 0.1), 2 seeds | none passes (best walk 0.60, estop 0.55); ds 0.1 worse (0.78) |
| i | the real override file, `-walkoverlay walk_tuning_fast.json`, no noise, 6 scenarios | all pass (slip 0.32 to 0.38, tilt <= 3.5); `gait/` holds stop and walk logs |
Earlier (run e, d, g, h): 8 m/s with f_cap 0.1/0.15, gain 1.2/1.0: walk slip 0.56 to 0.70; 9 m/s with f_cap 0.1/0.15: slip 1.6 to 1.9 (v 8.6 m/s walked, stop 1.6, tilt 5.1 to 5.9): not stable.

## Rung table, final config (worst over seeds, 8 seeds, noise 0.05), cap 7.5
| scenario | fell | mean slip / max (m) | tilt max (deg) | speed (m/s) | assist F mean/p95 %W | T mean/p95 %Wl |
|---|---|---|---|---|---|---|
| walk 63 s | 0 | 0.48 / 1.3 | 3.9 | 7.39 | 11.1 / 11.2 | 40.9 / 50 |
| start | 0 | 0.45 / 1.4 | 3.8 | 7.14 | 11.1 / 11.2 | 40.6 / 50 |
| stop | 0 | 0.41 / 1.5 | 3.9 | - | 10.7 / 11.2 | 29.9 / 50 |
| estop | 0 | 0.45 / 1.6 | 3.9 | - | 10.7 / 11.2 | 28.4 / 50 |
| turn90_inplace | 0 | 0.17 / 2.4 | 3.3 | - | 10.2 / 11.2 | 15.5 / 50 |
| curve | 0 | 0.48 / 2.0 | 3.9 | 6.94 | 11.1 / 11.2 | 41.2 / 50 |
Margins are thin (walk and curve 0.48 against 0.5).

## Costs, honestly
- The hand-of-god got bigger: horizontal servo force cap 10 % W (was 5 %), net force 11 % W at its cap, pelvis couple unchanged at 41 % W l. The gait is still a procedural walk towed by the assist (TEMPORARY, T-005 stays open).
- New default-off tuning keys: `kin_f_cap` was already there; added `kin_dpred` (0 = off) and `kin_ikmeas` (0 = off), neither helps, kept off for the record.
- Not run: dotnet suites (the code change is flag-guarded, defaults give the previous behaviour; the T-201 overlay tests only read the keys). Not run: `walk_mode 0` control hashes.

## Best idea for 8 m/s and beyond
The swing foot and pelvis lag the plan by about 1.5 s at any speed; the lag collapse at every stop drags a planted foot. Next step candidates: (1) command deceleration ramps that keep the plan stop point equal to the pelvis stop point (plan base slaved to the measured pelvis in the last two steps); (2) shorter stance during deceleration (step time from the measured pelvis speed, not the command) so the collapse is shared by more contacts; (3) a hip-torque or leg-assist for the swing leg (T-201's swing-lag finding). 9 m/s needs a different gait (pure physics, T-005).
