# T-005 r4 step 9.3b: offline two-stage QP controller on the planar plant, criterion A

Result: **criterion A FAILS before and after the one tuning round** (falls at 1.6 and 2.7 steps). Not a go. Stage QPs themselves are healthy.
Files: Tools/GaitOpt/qp_proto/{gi_qp.py (numpy Goldfarb-Idnani), controller.py, run_A.py, check_consistency.py}; out/proto/A_pre.{npz,json}, A_post.{npz,json}.
Run: `python Tools/GaitOpt/qp_proto/run_A.py --tag pre` and `--tag post --set w_post=100 om_j=4 om_com=0.9` (project root, kaiju python; each run < 1 s wall, ends at the fall).

## Solver
Numpy dense Goldfarb-Idnani (equality + inequality), self-test vs scipy SLSQP on 20 random QPs (n 4-14, up to 3 equalities): objective rel diff <= 5.5e-13, KKT residual <= 3.3e-13 (`python gi_qp.py`).
Stage 1: 2 pyramid generators per loaded point, beta >= 0 (GI). Stage 2: equalities eliminated by SVD (rank-safe), GI on the null space with 12 torque rows (u = pinv(CJ^T)(M qdd + h - Jp^T F), +-LIM; if infeasible the rows are dropped, flagged "tdrop", clamp after).
mu_eff = 0.40 (the stride optimisation value, not the plant 0.8). Loaded set = reference schedule (fn_ref > 1 % W) and point below 1.5 m.

## Metrics (A, initial state = node 0, hip speed x1.02, trunk +1 deg; run stops at the fall; l = 42.4 m)
| metric (limit) | pre-tuning | post-tuning |
| steps completed (20, no fall) | 1.59 FALL | 2.66 FALL |
| mean speed / v (0.95-1.05) | 0.981 | 0.864 |
| CoM z RMS % l (<= 1) | 4.78 | 2.66 |
| pelvis pitch RMS deg (<= 3) | 7.1 | 11.4 |
| foot placement RMS % step (<= 5), touchdowns | 12.9 (1) | 48.3 (3) |
| saturated-torque ticks % (<= 1) | 52.6 | 33.7 |
| friction ratio plant, vs mu 0.4 / vs plant 0.8 (<= 1) | 2.00 / 1.00 | 2.00 / 1.00 |
| friction ratio of the command (vs 0.4) | 1.00 | 1.00 |
| solver success (100 %) | 100 % | 100 % |
| torque rows dropped (stage 2) | 14.5 % ticks | 16.6 % |
| controller time per tick p50 / p99 | 0.42 / 0.70 ms | 0.42 / 0.69 ms |
Pre = R-009 weights: w_com 1e3, w_L 10, prior 1e-2, smooth 1e-3, w_swing 1e2, w_trunk 1e2, w_post 1, reg 1e-4; om_com 0.577 rad/s (1.2 sqrt(g/l)), kL 2 om_com, om_j 3 rad/s (swing, trunk, posture, stance-foot damping 2 om_j).
Post (one round, weights/gains only): w_post 1 -> 100, om_j 3 -> 4, om_com 0.577 -> 0.9. No change to limits, friction, reference. Every other metric passes only trivially because the run ends early.

## Findings
1. Data bug (not fixed in planar_model.py): `ref.at()["qdd"]` is the 2nd derivative of the q spline and is NOT consistent with ref F / comdd (which use the node central-difference qdd); at toe-off and landing nodes the residual of A_G qdd = wrench is up to 0.7 m g. The controller uses its own spline of `ref.qdd` (the PD replay in 9.3a also used the inconsistent one). With that fix, `check_consistency.py` (controller at the exact reference state, 20 nodes) reproduces the reference joint torques and F to <= 1e-3 LIM, so stages 1 and 2 are formulated consistently with the plant model (ID residual 1e-14 m g l).
2. Stance-point rows use the reference point acceleration + velocity damping (not 0): the reference foot is not at rest at toe-off/heel-strike nodes, hard 0 made stage 2 demand 1 LIM torques there (first attempt).
3. Failure mode (identical in both runs, also with delay 0 and 300 Hz, so it is not latency): at toe-off of the old stance foot (t ~ 1.1-1.3 s) the reference drops the toe force 0.41 -> 0 W and lifts the toe 1 m within one 0.11 s node while the foot pitch flicks 36 -> 25 deg (ref qdd -16 / +6 rad/s^2). The plant toe (spring 2e9 N/m) keeps 0.2-0.45 W for ~0.15 s after the command is 0; this unmodelled toe force pitches the foot about the toe, the ankle error grows to 12-17 deg, the swing heel/toe tasks (errors of metres, lever 24 m) then ask for 1 LIM hip torques (saturation 33-53 % of ticks), the swing leg is thrown, and the trunk and CoM z diverge; fall after the first touchdown (t = 4.5 s, on time) or one step later.
4. Friction: the plant tangential force reaches 0.8 Fn (ratio 2.0 vs mu 0.4) at the toe/heel during this transient, i.e. the foot slips although the command is within 0.4 by construction (ratio 1.0).
5. Not tried (outside "one tuning round"): measured-contact set (added as option meas_contact, no gain), force-ramped corners, task weights per phase, a model of the contact spring in stage 1, a smoother toe-off in the reference. These point to the reference's one-node toe force/pitch discontinuity as the main obstacle, together with the plant contact lag; criterion A not met, so B-D were not run.
