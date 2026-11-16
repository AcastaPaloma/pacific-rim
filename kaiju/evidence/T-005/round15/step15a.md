# T-005 round 15: 10 x 15 m sole, stance 16 m, rows re-fitted (v7s10); NOT STABLE, acceptance failed, 2 Unity cycles (40 runs, ~35 s per cycle); routing to option B

Branch task/T-005-r15 (from r14 5e91a45). Scenario walk_fr010 (63 s), walk_mode 0, L7 flags, 4 seeds (qp_pd_scale + k x 1e-4). No threshold, limit or gate changed. Control (all new flags 0) reproduced bit-identical: 25.6 31.2 38.7 30.4 s, median 30.8, DS a -0.77.

## Change (all default 0)
- `body_r15_stance_m` (half stance width; WithExperiments): hips, knees, ankles, feet of both legs to x = +/- value. 8 m gives a 16 m stance, so a 10 x 15 m sole keeps 1 m between feet. With `body_r14_sole_m` 10, `body_r14_width_m` 15: area 150 m^2, ground pressure 327 kPa double stance, 654 kPa single (the M-006.4 gate values, unchanged). Lateral cost: hip joints sit 0.0 m outside the pelvis box edge (8 m), CoM must travel 8 m (was 6.5) to the stance foot centre.
- GaitOpt: the six v5 rows (Fr 0.02 .. 0.10) re-optimised with the frame of the new foot/stance (`evidence/T-005/round15/scripts/mkframe.py`, `rows15.sh`, R7 `frame` override, same hard limits: T <= 3 s, CoP [heel+1.5, toe-2.5], F_z <= 1.5 W, flat touchdown). Planar planar model heel/toe now -3.5/+6.5 (Assets/Game/Config/Mech/gait_reference_qp_v7s10.json and _ladder_v7s10.json, selected by `qp_ref_v5` = 10). Solver residuals 1e-5 .. 5.1e-5 (trust-constr stopped on the iteration limit; row 0.07 FEASIBLE; exported with tol 6e-5, same practice as v5). Step lengths 8.65 .. 12.2 m.
- `qp_r15_ref` 1: the legacy imp 2 reference is the new main row instead of gait_reference_opt.json (foot assumption).
- Tests: R15StanceTests (defaults, suffix). Units.Mech 129/0, Physics.Body 112/0.

## Results (`agg.txt`; DS a per seed, mean; falls; median)
| config | DS a | DS len (s) | falls (s) | median |
|---|---|---|---|---|
| L7 control, 15 x 10, stance 13 | -0.73 -0.92 -0.77 -0.65 (-0.77) | 1.46 1.44 1.49 1.48 | 25.6 31.2 38.7 30.4 | 30.8 |
| N1 new geometry + v7s10 rows, hand-off match on | +0.38 -0.16 -0.44 -0.06 (-0.07) | 1.63 1.29 1.30 1.30 | 14.4 17.1 16.8 16.8 | 16.8 |
| N2 N1 + legacy ref | +0.51 +1.11 +0.26 -0.61 | | 15.1 12.8 12.2 12.8 | 12.8 |
| N3 N2, no match | +0.86 +1.08 +1.13 +0.82 | | 16.1 16.2 15.1 15.8 | 16.0 |
| N1 + qp_om_y 0.4 / 0.5 / 0.6 / 0.8 | mean -0.21 / -0.25 / -0.09 / -0.30 | | median 18.1 / 20.0 / 20.5 / 26.9 | |
| N1 om_y 0.8 | -0.01 -0.86 -0.16 -0.15 | 2.09 1.45 1.51 1.91 | 28.0 28.0 22.5 25.8 | 26.9 |
| N1 om_y 0.5 + k_roll 4 / 1 | mean 0.00 / -0.33 | | median 23.6 / 19.9 | |

Verdict: **NOT STABLE; acceptance failed** (DS a >= -0.2 on all seeds: no config; DS <= 0.9 s: 1.3-2.1 s; survival above 30.8 s: best 26.9 s). Not merged.

## Findings
1. The hand-off now engages on the short foot (QP first record at 8.25 s in N1, as in the control at 7.6 s; in r14 it never did) so item 2 of the r15 plan is fixed by the re-fitted rows, no gate change was needed. DS braking is gone on average (N1 mean -0.07, rear CoP - CoM -3.7 m, rear share 0.43) but not on every seed.
2. The new binding failure is lateral: at the first single support (10.0-10.5 s) the CoM is at x = -2.2 while the stance foot is centred at +7.5 (foot spans -0.3 .. 14.7); the weight shift is about 2 m short of the foot edge before the swing foot lifts, then the CoM drifts sideways at -0.6 .. -5 m/s and the mech falls laterally at 14-17 s (yaw error small, 7-16 deg). The lateral CoM controller (qp_om_y 0.5 in the whole-body QP, reference = mean of loaded contact corners) was tuned to a 13 m stance and 10 m wide foot. Speeding it up helps monotonically (om_y 0.3 -> 0.8: median 16.8 -> 26.9 s) but still falls short of the control, and DS length stays above 1.3 s.
3. Even with matched rows the DS length is not reduced (1.3-2.1 s vs target 0.9 s): the double support is set by the load transfer in the whole stack, not by the reference rows.
4. Cost of option A: a body contract change (stance 13 -> 16 m, sole 15 x 10 -> 10 x 15 m), a re-fitted row family, and a re-tuned lateral controller. It removed the r11 mechanism (rear load ahead of the CoM) but the stack as a whole has not been re-tuned for it, and further hand tuning is the loop §13 forbids.

## Routing (§13): option B, learned residual on the best body config
Best body config: N1 with `qp_om_y` 0.8 (body_r14_sole_m 10, width 15, body_r15_stance_m 8, qp_ref_v5 10; 27 s median, DS mean -0.30 with the rear load behind the CoM). Plan:
1. Data: the short-foot plant is a better training plant (rear CoP behind the CoM). Log (state, QP command, plant response) from N1/Y8 runs across seeds, qp_pd_scale and shoves; each 63 s run is about 1.6 s of wall time, so 2000 runs per hour are cheap. The Y8 and N1 gait logs of this round carry the per-tick state.
2. Policy: a small residual MLP (inputs: CoM, CoM velocity, lateral CoM minus stance foot centre, phase, row; outputs: delta CoP target, delta lateral reference, delta swing-lift-off gate), added to the QP command, so the QP and rows remain the safe backbone. Train offline (kaiju conda env, torch) by behaviour-cloning a successful short-foot episode set (best-of-N perturbation, rejection of falls < 40 s), then CMA-ES / PPO fine-tune with reward = survival time + speed tracking (M-001/M-002 targets unchanged), evaluated in the batch scenario CLI.
3. Gate: same T-005 stability gate (4 seeds x 63 s, no fall, M-001/M-002/M-006 untouched; M-006.4 holds at 150 m^2 / 327 kPa). Decision point after the first trained policy (budget one Sonnet round to build the data and policy hook, one to train); if it also fails, the founder is asked whether walk_mode 2 (assisted) stays the shipped locomotion.
