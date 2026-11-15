# T-005 round 2, step 6.6a: imp = 1 step length from the reference, angle log, Froude ladder (evidence)

Branch `task/T-005`, from d5ac14c. `imp` stays 0 in walk_tuning.json; imp = 0 path unchanged (cap helper returns `step_length_max_frac` for imp = 0); no change to mech_frame_balanced.json, thresholds, Physics.Body, assist.

## Change
- `LocomotionController.StepCapFrac`: with imp > 0 (and ref_track > 0) the three step-length caps (limiter reach speed, per-step speed, reference plan speed) use `imp_step_length_max_frac` instead of `step_length_max_frac`. Step time already is the stride-law time at the commanded speed (`_lim.StepTime`, the same law `ReferenceGait.Configure` uses), and the reference is reconfigured at every step with the commanded (capped) speed, so its Froude follows the command (log field `fr_ref`).
- New key `imp_step_length_max_frac` = 0.66 (reference step at Fr 0.15 = 0.5 x 2.3 x 0.15^0.3 = 0.651 l, rounded up). Legacy cap 0.25 l.
- gait_log step record, optional `angles` (null when imp is off or not walking): `fr_ref`, `st_ref`, `st_meas`, `sw_ref`, `sw_meas`, each [hip flexion, knee, ankle plantar] in deg (stance leg and swing leg).
- Scenarios `walk_fr003, walk_fr005, walk_fr0075, walk_fr0125, walk_fr015, walk_fr0175` added (existing unchanged).

## Runs (run 5, 63 s scenarios; Froude = v10^2 / (9.81 x 42); variants.txt run5)
| scenario | variant | fell at s | v10 m/s | Fr | yawErrMax deg | footfalls |
|---|---|---|---|---|---|---|
| walk_fr005 | imp1 | 16.8 | 0 | - | 15.7 | 14 |
| walk_fr005 | imp1_cap50 (cap 0.5 l) | 16.8 | 0 | - | 15.7 | 14 (identical: 0.05 is below both caps) |
| walk_fr010 | imp1 | 27.3 | 0 | - | 29.7 | 30 |
| walk_fr010 | imp1_cap50 | 24.1 | 0.01 | - | 55.4 | 13 |
Before (step 5): walk_fr010 imp1 with the 0.25 l cap walked 63 s at Fr 0.016. The longer step falls instead; it was not tuned further (stop rule).

## Which joint diverges first (from the new angle fields, tools: /private/tmp/claude-501/t005r2/dbg/ang.py)
- walk_fr005 (fall 16.8 s): the stance KNEE buckles: measured minus reference rises 0, +10, +26, +32, +44 deg through single support (10.5 to 16.5 s), in both stance legs, while the swing HIP stays 18 to 46 deg short of its reference flexion (reference 39, measured about 0 to 20). Both start in the same single support; the knee is the one that reaches fall height (CoM 41 -> 39 m, then backward fall). The step is long because the swing hip does not reach the reference, so the step does not end on schedule (reference stance angles hold at their end values for seconds).
- walk_fr010 (fall 27.3 s): stance HIP is 20 to 36 deg behind (less flexed than) the reference in the double support after the first long step (t 14-26 s), then the swing HIP lags 38-50 deg; the CoM runs backward from t 23 s (v 2 -> 7 m/s, drift z +80 m, x -64 m). So the hip (swing flexion, stance extension) saturates first at the longer step, then knee.
- Ankle: stance ankle error is small except at the heel-strike transient (+22 deg for ~0.3 s), swing ankle is up to +48 deg off during early swing (reference -47).
- Hip torque limit or the swing hip PD gain is the likely limiter (hip flexion reference 39 to 46 deg in 0.4 s of swing); not tuned, per the stop rule.

## Tests
`dotnet build` clean; `dotnet test DotnetTests/Units.Mech.Tests`: 83 total, 77 pass, 6 fail (the known KnownGap reds).
## Open
- Swing hip cannot follow the reference swing at step lengths above the old cap; check the hip force limit and swing PD (imp_ff_accel clamp) before raising the cap, or limit swing angular speed by raising step time.
