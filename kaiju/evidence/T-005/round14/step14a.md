# T-005 round 14: body variant, sole shorter than the step (body_r14_sole_m); NOT STABLE, acceptance failed, 2 Unity cycles (48 runs, about 30 s each)

Branch task/T-005-r14 (from r13 06147f8), code commit 1a5d38d. Scenario walk_fr010 (63 s), walk_mode 0, L7 control, 4 seeds (qp_pd_scale + k x 1e-4). No threshold, limit, gate or requirement changed.

## Change (default 0 = bit-identical; L7 control reproduced 25.6 31.2 38.7 30.4 s, median 30.8, DS a -0.79)
Walk-tuning fields `body_r14_sole_m` (L) and `body_r14_width_m` (W), applied by `MechConfig.WithExperiments` (same route as exp_toe_m; mech_frame_balanced.json and body_tuning.json untouched). The foot becomes L x W with the ankle kept at the same fraction of the sole (0.35 L behind, 0.65 L ahead; 15 m gives the original -5.25/+9.75), foot mass kept, solid-box inertia, hull volume scaled, area = L x W; the controller reads the sole from the frame, so its geometry follows. Width is capped at 12 m: stance width is 13 m, so area-preserving widths (15 m at L 10) would overlap the feet. Test: R14SoleTests (default off). dotnet Units.Mech (Category!=KnownGap) 127/0, Physics.Body 112/0.

## Results
Cycle 1 (L7 vs L 10/9/8 m, W 12 m, same L7 flags): all 12 sole runs fall at 20.9 / 20.8 / 16.4 s on every seed (seed-independent), and the QP never engages (0 gait records with `qp`; `qp_match` hand-off gate compares the measured state to reference rows built for the 15 m foot, the legacy imp-2 walk with the short foot never matches). So these are legacy-walk-only falls, not a test of the QP on the short foot.
Cycle 2: (a) `qp_match_thr` 3 and 100: still no engagement for L 10 and 8 (20.9 / 16.4 s); L 9 engages at thr 100 only, falls 13.8 s. (b) `qp_match 0, qp_match_ss 0` (hand-off at the first touchdown at speed fraction 0.3, no row match), the only configs where the QP runs on the short foot:

| config | DS mean a seeds 0-3 (m/s^2) | mean | rear CoP - CoM (m) | rear share | DS length (s) | falls (s) | median |
|---|---|---|---|---|---|---|---|
| L7 control, 15 x 10 m | -0.67 -0.96 -0.74 -0.77 | -0.79 | +4.46 | 0.53 | 1.49 | 25.6 31.2 38.7 30.4 | 30.8 |
| L 10 x 12 m, no row match | -0.43 -0.50 -0.10 -0.50 | -0.38 | +0.01 | 0.26 | 1.52 | 19.2 22.1 25.8 21.3 | 21.7 |
| L 9 x 12 m, no row match | -0.48 -0.41 -0.59 -0.49 | -0.49 | -3.23 | 0.15 | 1.33 | 19.1 19.7 18.7 19.2 | 19.1 |

Ground pressure (M-006.4 uses weight / (L x W)): 15 x 10 = 150 m^2, 327 kPa double stance (the gate value); 10 x 12 = 120 m^2, 409 kPa (+25 %); 9 x 12 = 108 m^2, 454 kPa (+39 %); 8 x 12 = 96 m^2, 511 kPa (+56 %). Single stance 654 kPa becomes 818 / 908 / 1022. M-006.4 (327 +/- 5 %, 654 +/- 2 %) would fail on any of them unless the stance is widened so the sole keeps 150 m^2 (needs a frame change, hips and feet 15+ m apart, not done; threshold not touched).

## Acceptance (DS a within +-0.2 of command and >= -0.2 on all seeds, DS <= 0.9 s): FAILED
- DS a: -0.43..-0.50 (L 10) and -0.41..-0.59 (L 9) on every seed except one (L 10 seed 2, -0.10). Better than control (-0.79) but not within 0.2.
- DS length 1.3-1.5 s, unchanged from control (1.49 s); target 0.9 s.
- Survival: median 21.7 s (L 10) and 19.1 s (L 9) vs control 30.8 s. Worse; none survives 63 s.

## Findings
1. The r11 geometry diagnosis is confirmed: with a sole shorter than the step the rear CoP is no longer 4.5 m ahead of the CoM (+0.01 m at 10 m, -3.2 m at 9 m), the rear load share falls from 0.53 to 0.26 / 0.15 and DS braking halves (-0.79 to -0.38 / -0.49).
2. It is not enough: braking remains, DS length is unchanged, and survival drops because the control stack (row references, hand-off match, planar QP model with heel_x/toe_x -5.25/9.75 in gait_reference_qp*.json, foothold and CoP windows) was built and tuned for the 15 m foot. The hand-off never engages with the row match on, and with it off the QP runs on a model whose sole is 5 m longer than the plant's.
3. r7's matched-rows toe-trim result (rows re-optimised for the foot, 21 s) was the same kind of failure. A fair test of A needs the reference rows (Tools/GaitOpt, `heel_x_m`/`toe_x_m`, CoP window, foothold limits) re-optimised for the short foot, so the QP model, rows and plant agree. That is a GaitOpt re-run plus new row files, not a controller loop; it was outside this round's rule (unchanged controller, <= 2 cycles).
4. Even with matched rows A costs M-006.4 (+25 % ground pressure at 10 m) unless the stance is widened; that is a founder-level body contract change.

## Verdict and routing (§13)
NOT STABLE; acceptance not met; no merge (default 0 flags only on the branch). Routing for r15 (lead decision, not a loop of r14):
- (A2) one matched-rows round: re-run Tools/GaitOpt for L 10 x 12 m (heel -3.5, toe +6.5 from the ankle; CoP window and DS time re-fitted, row files v7s10), run the same L7 flags with qp_match on, 4 seeds, same acceptance. One cycle is about 30 s of Unity time, so it is cheap; Sonnet worker, GaitOpt owned by T-005. Stop after it.
- (B) learned residual on the QP/PD only if A2 also fails the DS rule. Note the short-foot data above is a better training plant (rear load not ahead of the CoM) than the 15 m one.
- Not recommended: more controller variants on the unmatched short foot; relaxing the hand-off gate.
