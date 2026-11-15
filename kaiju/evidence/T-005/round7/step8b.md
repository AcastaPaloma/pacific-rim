# T-005 r7 8b: foothold from the reference row + planner reach on the hard-limit rows: best 4-seed median 30.8 s (ties the r4 baseline 30.8 s, does not beat it); no stable walk on any seed
New flag `qp_r7_fp` (default 0): the landing heel keeps the reference row's DCM offset at the touchdown node, x_fp = xi_pred - ((1-f) b + f (xi_ref - heel_ref)); the round-7 rows have the CoM above the landing heel (com - heel -0.6 .. +0.6 m) and the DCM 6-10 m ahead of it, the point-foot LIPM b of the planner is 2.7-7.5 m, so the old rule lands the heel 3-6 m too far ahead of the CoM. Code in `SagittalQpController.PlanEval`, `QpReference.NodeDcmOffset`.
Config names (`scripts/variants98.py`, all on the 7b base qp_foot 1 / qp_foot_meas 1 / qp_foot_drive 0): V5 = qp_ref_v5 1; C7 = V5 + qp_r7_clock 1; L7 = C7 + qp_plan_lmax 1.0. 4 seeds, fall time s:
| config | seeds | median |
|---|---|---|
| control (v4 rows, 89fa14f) | 28.5 22.3 21.9 27.3 | 24.8 |
| Zfp1 (V5 + fp 1) | 17.8 20.1 23.1 18.9 | 19.5 |
| Zfp05 (V5 + fp 0.5) | 18.9 24.8 26.3 28.4 | 25.6 |
| Zfp1 + clock | 17.1 42.0 25.0 19.9 | 22.4 (one 42.0 s seed, slow shuffle near the start, no forward progress) |
| C7 (V5 + clock) | 26.1 24.6 29.6 29.0 | 27.6 |
| C7 + fp 0.5 | 23.8 24.6 37.5 25.3 | 25.0 |
| C7 + lmax 0.8 / 0.9 / 1.1 (default 1.3: 27.6) | | 26.0 / 26.3 / 26.8 |
| **L7 (C7 + qp_plan_lmax 1.0)** | **25.6 31.2 38.7 30.4** | **30.8** |
| L7 + lmin 0.8 / 0.7 | 29.8 31.6 27.2 25.5 / 26.2 33.1 25.7 33.5 | 28.5 / 29.6 |
| L7 + fp 0.5 | 34.2 30.1 24.1 26.2 | 28.1 |
| L7 + fp 0.25 / cop window / fcap 1.5 / noforce | | 26.0 / 25.8 / 24.1 / 18.5 |
| C7 + kxi 2 / swing PD 0.1 / apex 0.5 | | 24.4 / 27.4 / 22.2 |
| L7 + qp_ladder_up 0.4-0.7 | identical to L7 | 30.8 (the planner ladder follows the mean speed of the last step, qp_plan_ladder, not qp_ladder_up) |
| derated rows v5t (all torque limits x 0.7, rows 0.02/0.03 only) | | 16.6-21.0 (worse) |
Seed-to-seed spread of one config is 21-29 s (up to 10 s), so every difference below ~5 s is noise; L7 is the best of ~25 configurations and sits at the top of that spread (selection effect), not a verified gain. The 38.7 s and 42.0 s seeds are single runs.
## Diagnosis (why no stable walk, even on rows that satisfy the hard limits)
1. No configuration, old or new rows, ever leaves ladder row 0: the walker never reaches the row speed (row 0: v = 2.88 m/s; measured mean speed of a step 0.5-2.5 m/s, `steps98.py`, `ls97.py`), so the fall at 17-30 s is the fall of a walker that takes 4-9 steps at the slowest row. Control (v4 row 0, T 6.3 s) takes 3 steps in 25 s, the v5 row 0 (T 3.0 s) 5-9.
2. The step does not execute the row: durations 3.3-3.9 s against 3.0 s, swing apex 0.6-1.8 m against a commanded 2.7-3.4 m (IK out of reach 3-250 ticks), landing 2-7 m behind the target x_fp, speed collapses to 0.2-0.5 m/s in mid-step and the foot lands 8-14 m ahead of the CoM at the end (feet 10-15 m apart in a long double support), after which the CoM runs away backwards (-5 .. -12 m/s) when the rear foot lifts. `qp_r7_clock` makes the load transfer 0.6 s as in the reference (was 2.5 s) but the survival does not follow.
3. The reference rows are at their hip / knee torque limits during the swing (peak 1.00 of the limit in every row; derating the limits to 0.7 in the planner gives feasible rows at Fr 0.02 and 0.03 (residual 2e-8, 3e-5) but they track worse), so the swing leg has no torque margin to catch up when the landing is late: row execution (swing leg timing) is the open problem, not the reference geometry or the contact model.
## Next approach (not done)
Make the swing leg track a row that is planned with torque margin and from the measured swing state: (i) add rows of lower Froude (0.005-0.015, steps of 2-4 m) so row 0 is within what the swing hip can execute, and start the ladder there; (ii) plan the swing as a min-jerk curve with the measured lift-off state and re-time the step to the measured swing progress (landing time instead of landing place: the swing foot lands at the reference time at x_fp(t) and the CoM/DCM is re-planned), instead of waiting for the foot at an x_fp computed 1-2 s earlier; (iii) add a whole-body QP row for the swing hip torque saturation (the swing hip sits at its limit) and test it with the rows derated to 0.7-0.8 (v5t) before the full ladder.
