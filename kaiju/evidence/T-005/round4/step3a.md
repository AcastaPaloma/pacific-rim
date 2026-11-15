# T-005 r4 step 9.3a: planar plant, @v2 reference fields, PD replay sanity

Files: Tools/GaitOpt/planar_model.py (model + reference loader, API note at top), Tools/GaitOpt/planar_sim.py (plant + PD replay; `python Tools/GaitOpt/planar_sim.py --steps 20`),
Tools/GaitOpt/out/proto/{ref_fr010_v2.json, pd_replay_summary.json, pd_replay_pd_ff.npz, pd_replay_pd_only.npz}.

## Model checks (SI, numpy, analytic; no torch)
- M(q) vs stride_opt Mfun (x1e6 unit), 3 random q: rel err <= 4e-17 (abs 1e-9 criterion met). M q'' + h vs stride_opt inv_dyn: rel err <= 2e-16.
- Point Jacobian / CoM velocity vs finite difference 5e-9; A_G linear rows = m * CoM velocity (1e-16); A_G-dot qd vs finite difference rel 3e-9.
- Cost: M,h,kinematics 0.05 ms; full sim 2.7e3 sub-steps per wall-second (spec 600).

## Reference @v2 (stride_fr010_lat.json, Fr 0.10, N 40, step 4.546 s, v 6.449 m/s, mirrored to 80 nodes)
- Force consistency sum(fn,ft over 4 points) = m (cdd + g), asserted: max residual 1.6e-6 m g (x 4.8e-7, z 1.6e-6). Threshold 5 %.
- Extra check: dL/dt about CoM = sum (p - c) x F: max residual 1.4e-7 m g l (confirms L sign, ccw positive, and A_G).
- qd in JSON = central difference of q: 2e-14. Fields written to ref_fr010_v2.json: com x,z (+ x rel hip), com v, com a, L about CoM, Ldot.
- Total mass 1.0e7 kg, W = 98.1 MN, leg length 38 m (LT 19 + LS 19; stride_opt l = 42.4 is the Froude length).

## Plant (R-009 section 7)
9 DOF, 4 sole points (heel/toe x 2), spring k 2e9 N/m, damping zeta 0.7 on the point effective mass, regularised Coulomb mu 0.8 (tanh, eps 0.02 m/s, capped so one
sub-step cannot reverse slip), semi-implicit Euler 1/600 s, controller 60 Hz, ZOH, 1 tick delay, torque limit hip 1.2e9 / knee 0.8e9 / ankle 1.0e9 N m (body_tuning),
only the 6 joints are actuated (trunk lean th is driven by the sum of the hip torques, as in stride_opt; base unactuated). Fall: hip z < 0.7 ref or |lean| > 30 deg.

## PD replay (omega 2 pi 1.5 Hz, critically damped, K = w^2 Lambda_j with Lambda_j = 1/(CJ M^-1 CJ^T)_jj at the reference pose), from reference initial state, 20 steps requested
| variant | result | mean speed / v | peak knee err stance / swing | best-fit knee time lag stance / swing | CoM z RMS | trunk RMS | sat ticks |
| PD + ref ID torque | FALL at 18.1 s = 3.99 steps | 0.35 | 22.4 / 33.2 deg | 0.00 / 0.02 s | 1.60 m = 4.2 % l | 12 deg | 31 % |
| PD only            | FALL at 10.2 s = 2.24 steps | 0.46 | 22.5 / 29.6 deg | 0.06 / 0.06 s | 1.62 m = 4.3 % l | 8.6 deg | 40 % |
(l = 38 m leg.) Over the first 2 steps (separate run) PD + ff: speed ratio 0.83, knee errors 5.8 / 3.5 deg, CoM z RMS 1.95 % l, no fall.
Reading: the plant reproduces "all fall" for joint PD replay: the error grows from step 1 (lag/sag at the knees, speed loses ~17 % in two steps) and the gait collapses
within 2 to 4 steps; ff torque helps but does not hold. I did not read the round-3 REPORT, so the match of the round-3 lag magnitude is not checked numerically; the
pure time-lag fit is small (0 to 0.06 s), the loss is mostly amplitude (sag), not a clean delay. Gains are my choice (Lambda is the free-floating joint inertia,
so stance gains are soft); step 9.3b/orchestrator should compare with the imp 2 gains before calling the plant representative.
