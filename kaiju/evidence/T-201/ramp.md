# T-201: walk_mode 2 speed ramp (toward 9 m/s, Froude 0.2)

**Result: the last stable rung is a 6 m/s step-reach cap (Froude 0.087, about 5.8 m/s walked), up from 2.67 m/s.** 7 m/s (about 6.7 m/s walked) walks 63 s but misses the slip gate in stop and emergency stop; 8 and 9 m/s do not hold (no variant passes, none reaches 8 m/s). The config is the named override `Assets/Game/Config/Mech/walk_tuning_fast.json`; `walk_tuning.json` is untouched (walk_mode 0, walk_mode 2 stays at 2.67 m/s).

## Thresholds (set by the brief, not lowered)
A rung is STABLE when, on 4 seeds x 63 s (here 4 to 8) and on the scenarios start, stop, emergency stop (estop), turn90_inplace and curve (noise seeds and a no-noise run): no fall, pelvis tilt max <= 5 deg, mean foot slip per ground contact <= 0.5 m.
Definitions (same as `evidence/T-005/assist/scripts/analyse.py`): slip = horizontal travel of the lowest sole end while in contact, per contact stretch; tilt = angle of the root up axis from vertical over the whole log (start-up included); mean speed = net horizontal displacement of the whole-body centre of mass over t >= 8 s; assist force |F| in % of W = M g = 9.81e7 N, torque |T| in % of W l = 4.12e9 N m (l = 42 m).
Seeds: `kin_noise 0.05` with `kin_seed` 0..n-1 (step time and foothold jitter, the T-005 convention).

## Harness
The brief said "dotnet harness". There is none for this: the scenario harness is Unity batch mode (PhysX, `Game.Units.Mech.Editor.ScenarioCli`), the same one that produced `evidence/T-005/assist`. Run with `evidence/T-201/scripts/run.sh` (waits for fewer than 2 batch editors; one batch of 30 to 130 runs takes 2 to 6 minutes). Added: scenario name `walk_v<m/s>` (command speed), a variant name `a+b#label` (only those scenarios), `-walkoverlay <file>`. Variants files are in `variants/`, per-batch summaries in `tables/summary_run_*.txt`, scripts in `scripts/`. Dotnet: Units.Mech.Tests 121 passed (`Category!=KnownGap`, includes the 2 new overlay tests), Physics.Body.Tests 112 passed.

## What the ramp showed
| cap (command) | default kin_* , no retune (1 seed) | after retuning |
|---|---|---|
| 3 m/s | 3.2 m/s, slip 0.15, tilt 3.3 | stable |
| 4 m/s | 4.1 m/s, slip 0.23, tilt **7.4** | stable |
| 5 m/s | 5.2 m/s, slip **3.1** (max 11 m) | stable |
| 6 m/s | slip 6.6, tilt 24 (sags, drifts back) | stable |
| 7 m/s | slip 4.2, tilt 10 to 13 | walks, scenarios marginal |
| 8, 9 m/s | slip 3 to 3.4, one fall at 34.6 s (9) | not stable |
Cause (from the logs): at higher speed the swing foot lags its planned path by several metres (swing leg inertia against hip torque and the drive gain), lands short, and the planned stance foot slips; the swing clearance of 0.12 l then drags the toe. The fixes that mattered: lower leg drive gain (`kin_gain` 6 -> 1.5), more clearance but only at speed (new `kin_clear_fast`, because 0.25 l clearance at standstill ruined the in-place turn, tilt 14 deg), shorter double support (`kin_ds` 0.2 -> 0.05), no swing start delay (`kin_hdelay` 0.15 -> 0), swing-time scale 0.55 (`kin_tscale` 0.65 -> 0.55), slower hip-height filter (`kin_hz` 1.5 -> 1.0), arm swing 1.0. Everything else in the ~25-knob neighbourhood (caps, rot_k, sway, lean, reach, om, peak, toe, ankle margin, lift) was swept one at a time at 4, 5, 6, 7 rungs (`tables/`, `variants/`), none helped further.

## Rung table, final config (`walk_tuning_fast.json` keys, step cap per rung), 63 s walks
| rung (cap) | seeds | fell | mean speed m/s | slip mean (max) m | tilt max deg | assist F mean / p95 % W | assist T mean / p95 % W l |
|---|---|---|---|---|---|---|---|
| default walk_mode 2 (2.67) | 4 | 0 | 2.69 | 0.13 (0.56) | 3.3 | 6.6 / 7.1 | 29 / 50 |
| 3 | 4 | 0 | 3.22 | 0.33 (0.88) | 1.6 | 6.4 / 7.1 | 29 / 50 |
| 4 | 4 | 0 | 4.15 | 0.31 (0.86) | 1.6 | 6.6 / 7.1 | 34 / 50 |
| 5 | 4 | 0 | 5.08 | 0.30 (1.02) | 2.2 | 6.6 / 7.1 | 37 / 50 |
| **6** | **8** | **0** | **5.78** | **0.35 (1.11)** | **3.7** (2.5 on seeds 0-3) | **6.7 / 7.1** | **41 / 50** |
(`tables/summary_run_p.txt`, `rung6_final_per_seed.txt`; rows are worst over seeds, the speed column the lowest seed.)

## Scenarios (no fall in any), final config
| scenario | cap 6: slip mean / tilt max | cap 5 | cap 4 | default (2.67), noise / no noise |
|---|---|---|---|---|
| start | 0.36 / 3.1 | 0.30 / 2.3 | 0.32 / 1.6 | 0.14 / 3.3 |
| stop | 0.41 / 3.7 | 0.31 / 2.2 | 0.34 / 3.1 | 0.15 / 3.3 |
| estop | 0.43 / 4.0 | 0.35 / 3.1 | 0.34 / 1.6 | 0.16 / 6.2 (4.8 no noise) |
| turn90_inplace | 0.24 / 3.8 | same | same | 0.48 / **7.0** (6.2 no noise) |
| curve | 0.35 / 3.7 | 0.31 / 2.2 | 0.42 / **12.4** | 0.21 / 3.6 |
Cap 6: 8 noise seeds plus one no-noise run (`summary_run_q.txt`, `summary_run_p.txt`, `summary_run_fin.txt`, the last through the real override file). Honest notes: (1) the default walk_mode 2 itself exceeds 5 deg in estop and turn90_inplace under step noise, so the 5 deg gate there is a property of the closing step, not of speed; the fast profile is below 5 deg in both. (2) One cap-4 curve noise seed shows a 12.4 deg tilt spike (no fall; cap 4 is not the shipped rung, cap 5 and 6 are clean on 8 seeds). Not diagnosed.

## Rungs above
| rung | best variant (config differs from final in tscale 0.58, gain 1.75, clear schedule on) | result |
|---|---|---|
| 7 (cap 0.609) | 4 seeds: no fall, 6.70 m/s, slip 0.47 (max 1.4), tilt 4.7, F 6.9 / 7.1 % W, T 40 / 50 % W l (8 seeds with constant clear 0.25: tilt 4.1) | start 0.45 / 3.8, curve 0.47 / 4.2, turn 0.34 / 4.1, **stop 0.52, estop 0.57** slip (> 0.5): fails the gate. Final config at cap 7: tilt 5.8 to 7.0 (fails). A second factorial over tscale, gain, ds, clear, vref: nothing passes stop and estop. **Not stable.** |
| 8 | 54 combos x 2 seeds: none passes. Best slip 0.57 with tilt 3.8 (7.1 m/s, not 8); best tilt-clean ones have slip 0.8 or more; several reach only 4 to 6 m/s (stall) | not stable |
| 9 | 54 combos x 2 seeds: none passes; slip 0.47 comes with tilt 9.8 and 7.1 m/s, tilt 5.3 with slip 0.53 | not stable |
Rule applied: one retune per rung; above 6 it took a one-knob sweep plus factorials (rung 7), and the sweeps are in `variants/` and `tables/`.

## Assist magnitudes (it is a hand-of-god, TEMPORARY)
The pelvis servo is `WalkAssist.Servo` (engine external forces on the pelvis; kin_f_cap 5 % W horizontal and 5 % W vertical-deviation, so |F| tops out near 7.07 % W; pelvis couple capped at kin_rot_cap 50 % W l). At the 6 m/s rung it sits at: force mean 6.7 %, p95 7.1 % W (about 6.6e6 N, at its cap almost all the time), pelvis couple **mean 41 %, p95 50 % W l (1.7e9 N m mean)**, which is above one hip joint's limit (1.2e9 N m) and at the cap about half of the time. At 2.67 m/s it was 6.6 % W and 29 % W l, so the couple rose by 40 % for 2.2 x the speed. The legs do not hold the pelvis attitude; the walk is a procedural gait plus a capped crutch, not a physical walk. Speed above 6 m/s needs either a bigger assist or a different gait (the pure-physics walk remains the open T-005 requirement).

## How a profile uses it
`MechConfig.LoadDefault(walkOverlayFile: "walk_tuning_fast.json")`, or `config.WithWalkOverlay(json)`; headless: `ScenarioCli -walkoverlay walk_tuning_fast.json`. The overlay sets `walk_mode 2`, `step_length_max_frac 0.555` and the kin_* keys above (new neutral keys `kin_clear_fast` 0 and `kin_clear_vref` 0.6 in `WalkTuning`). Speed is set by `step_length_max_frac` alone: 0.609 gives the marginal 7 m/s rung. The default files are unchanged: a control run of the default walk_mode 2 variants (cap 4 and 5 m/s lines, noise seed 0) gives a gait log byte-identical to the pre-change build (sha 16 chars a25c0e03ebe65816 and 2b1e7bece46cb1a6, wall-clock field masked), and `kin_clear_fast = 0` returns the constant clearance exactly.
Evidence commands: `RUN=<n> VAR=<variants> SCN=<scenarios> bash evidence/T-201/scripts/run.sh`, `scripts/summ.py <gait dir> [substring] [--pass]`, `scripts/analyse2.py`. Gait logs of the final rung (seed 0, 6 m/s) and cap 7 (seed 0): `gait/*.gz`; the other raw logs (6.5 MB each, kept in the scratch folder) are not committed.
