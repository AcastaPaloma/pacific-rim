# T-005 r3 step 8.1: sagittal stride trajectory optimisation at Fr 0.10 (evidence)

Code `Tools/GaitOpt/stride_opt.py`, result `Tools/GaitOpt/out/stride_fr010.{json,txt}`. No Unity. No body-data or threshold edits.

## Model and constraints (sources)
- Planar, 9 DOF: hip x, y, upper-body lean (pelvis + torso + head + arms lumped), thigh/shank/foot per leg. Masses, COMs, segment lengths from `mech_frame_balanced.json` (leg 42 m hip height, thigh = shank = 19 m, ankle height 4 m, foot 15 x 10 m). No inertia in the JSON: rod m L^2/12 (assumed). Conventions as R-008 `model.py`. v = sqrt(0.10 g 42.4) = 6.449 m/s.
- One step, N = 40 nodes, central differences, periodic mirrored boundary (legs swap, hip x + v T). Schedule from R-008 solve (phs 25, kmin 5.6, flat 76 %): double support nodes 0..10 (0.26 of step), heel then flat from node 15, heel rise node 37. Step time free in +-20 % of R-008 (3.789 s).
- Contacts: heel and toe points, y = 0 and no slip while active, |Ft| <= mu Fn with mu = 0.6/1.5 = 0.4 (`body_tuning` friction_dynamic, `walk_tuning` friction_safety). Touchdown ground speed <= 0.5 m/s (my assumption, impact-free). CoP is inside the active support by construction (point contacts).
- Joint ROM with 5 deg margin: hip [-15, 115], knee [5, 130], ankle [-15, 35] deg (JSON ROM). Torque: hip 1.2e9, knee 0.8e9, ankle 1.0e9 N m (`body_tuning` kinds hip, knee, ankle). Swing sole clearance >= 0.5 m for swing phase 0.25..0.75 (>= 0 elsewhere). Average speed fixed by step length = v T.
- Objective: mean over nodes of sum (tau/limit)^2. Solver: SLSQP, torch-autograd Jacobians, 5 restarts x 120 iterations (attempt 1: one 250-iteration run, attempt 2: restarts; stop rule applied).

## Result: NOT CONVERGED (SLSQP status 9, iteration limit; 600 iterations, 224 s)
Best iterate: max equality (dynamics) residual 6.4e-3 (force rows in body weights, torque row in 1e9 N m; contact y/no-slip rows 0), max inequality violation 0 (no limit violated). Objective still falling (0.285, 0.306, 0.285, 0.244, 0.217 per round); SLSQP oscillates between rounds. Not a feasibility proof and not an infeasibility proof.

| item | value | limit | note |
|---|---|---|---|
| peak tau hip A / B | 4.4e8 / 6.1e8 N m | 1.2e9 | 0.36 / 0.51 |
| peak tau knee A / B | 4.1e8 / 2.9e8 | 0.8e9 | 0.52 / 0.36 |
| peak tau ankle A / B | 5.0e8 / 5.2e8 | 1.0e9 | 0.50 / 0.52 |
| peak normal force, both feet | 2.67 BW (node 36, late flat/heel rise) | none | one point 1.67 BW |
| peak friction utilisation | 0.87 | 1.0 (mu 0.4) | no point at the cone edge |
| knee range | 5.4 to 39.3 deg | >= 5 | |
| step time / length | 4.35 s (+15 %) / 28.1 m | +-20 % | |

Binding (within 2 % of full ROM, about 3 deg): none of torque or friction. ROM margins bind: knee lower (5.7-7.7 deg) in early stance 0-4 and 23-27, 34-39; ankle dorsiflexion (-14 to -15 deg) in stance 0-2 and swing 18-26 and 36-39; hip extension (-12.3 to -13.3) of the old stance leg at nodes 6-7 and 15-18. Clearance and touchdown speed are near their limits at the best iterate (slack 0.01-0.03 m, m/s).

## Versus round-2 reference (R-008, step1.md)
Reference: heel strike 24 deg, duty 0.63, LIPM 0.092 g, knee >= 5.6, joint margin 5.3 deg. Here the schedule is the same, but a forward-simulated dynamics now needs torque: peak joint torques are at or under 0.52 of the limits, so torque does not look like the constraint; the 5 deg ROM margins (knee, ankle dorsiflexion, hip extension) and the 0.4 friction cone (0.87 used) are the tight ones. Peak vertical load 2.67 BW is high (impact-free step, double support only 0.26 of step).

## Verdict: NOT CONVERGED
The residual is small and every limit holds at the best iterate, which suggests Fr 0.10 is attainable, but SLSQP did not converge, so it is not a verdict. Next, if wanted: trust-constr or a sparse IPM-like solver, or more restarts (about 4 min each), and a separate impact model. Hand-off for 8.2 should wait for a converged point.
