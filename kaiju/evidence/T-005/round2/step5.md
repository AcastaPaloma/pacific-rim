# T-005 round 2, step 5: first Unity runs of imp = 1 (evidence)

Branch `task/T-005`, from f457759. Target walk_fr010 (63 s), stand fine in all variants. `imp` stays 0 in walk_tuning.json; no change to mech_frame_balanced.json, thresholds, Physics.Body, assist. imp = 0 path untouched.

## Timeline of the run-1 fall (imp1 log; the gait log has no per-DOF angles, so the mechanism is read from CoM, feet, CoP, DCM)
- t 3-5.5 s Shift: identical to imp0. t 5.9 s first single support (left stance): imp0 CoM climbs and moves forward (+0.4..+3 m/s); imp1 CoM drops 1.25 m/s, forward speed goes to -0.3..-1.3 m/s, DCM runs backward (z -3).
- t 7-9 s: CoM height 41.2-42.2 but sagging at each landing, yaw starts to grow (5.6 deg at 9 s, 14 deg at 10.5 s), CoM drifts to -x and -z instead of walking +z; stance CoP sits at the heel (-14 m behind the foot centre at t 11-14 s).
- t 14-15 s: yaw util > 1 (1.06 then 9), yaw -26 deg, height 34 m: fall.
## Cause
Not the DOF sign. Run 2 multiplied the stance reference angle by DOF Sign: fell earlier (8.7 s, backward), so the anatomical angle already is the DOF coordinate (kept unsigned). The cause is the stance torque budget (open issue 3 of step 4): the ankle and knee carry the body weight and set the CoP only through the virtual-force torque (SupportTorque, ground force at the CoP plus distal weight), and imp_vf_scale 0.25 left them with a quarter of it; the joint PD (0.5 Hz on the distal inertia) is far too soft to make up the rest. The ID gravity term (share 1.0) only holds the distal links and counted that weight twice on top of the 0.25. imp_vf_scale 0 (run 1) fell sooner than 0.25, consistent.
## Fix (imp_* keys only, in walk_tuning.json; the WalkTuning default in code unchanged)
imp_vf_scale 0.25 -> 1.0 (full virtual-force torque: this is the legacy stance law, plus the reference-angle PD and ID feed-forward) and imp_ff_stance_gravity_share 1.0 -> 0.0 (the distal weight is already in SupportTorque). Two default-value assertions in tests updated (StanceImpedanceTests, FeedForwardTests).
## Runs (all walk_fr010, 63 s; Froude = v^2 / (9.81 x 42))
| run | variant | hypothesis | fell | v10 m/s | Fr | yawErrMax |
| 1 | imp0 | baseline | no | 3.00 | 0.022 | 3.5 |
| 1 | imp1 / g075 / vf0 | as shipped | 14.9 / 14.9 / 13.8 s | 0 | - | 24 / 26 / 20 |
| 2 | imp1, imp1_vf0 with stance target x Sign | DOF sign inferred wrong | 8.7 / 8.8 s | 0 | - | 6.9 / 3.6 |
| 3 | raw_vf1 (vf 1.0, share 0, unsigned) | torque budget | no | 2.56 | 0.0159 | 11.3 |
| 3 | sgn_vf1, sgn_vf05 | sign with budget fix | 10.1 / 8.8 s | 0 | - | 5.8 / 8.6 |
| 4 | imp1 (new shipped defaults, `{"imp":1}`) | confirm | no | 2.56 | 0.0159 | 11.3 |
| 4 | imp1_w15 (waist 1.5 Hz) | yaw error 11 deg | 31.0 s | 0.12 | - | 12.4 |
| 4 | imp1_sh25 (gravity share 0.25) | partial ID gravity | 26.3 s | 0.03 | - | 15.7 |
Best: imp1 with the new defaults, walks 63 s, 44 footfalls, 153 m, 2.56 m/s = 85 % of the legacy speed.
## Commands
`RUN=N SKIPTEST=1 SCN=walk_fr010 /private/tmp/claude-501/t005r2/run65.sh` with /private/tmp/claude-501/t005r2/variantsN.txt; analysis script /private/tmp/claude-501/t005r2/dbg/tl.py; `dotnet test DotnetTests/Units.Mech.Tests`: 83 total, 77 pass, 6 fail = the known KnownGap reds.
## Open
- Slower than legacy (Fr 0.016 vs 0.022) and yawErrMax 11 deg (legacy 3.5); both waist and gravity-share changes made it worse (fall 31 s, 26 s), so the stance is sensitive: tune one gain at a time. Joint angles are not in the gait log (adding them would show the PD error per DOF).
- Sign of the waist and arm targets (unsigned, as shipped) not separately verified; the walk is stable with them.
