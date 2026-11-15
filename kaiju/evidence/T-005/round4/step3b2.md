# T-005 r4 step 9.3b-2 (escalated cycle, §14): contact-aware full-ID QP, criterion A

Result: **criterion A FAILS** (best: fall at 7.86 of 20 steps, from 1.59 / 2.66 in 9.3b). B not run (A is its precondition). Not a go.
Formulation: **single full inverse-dynamics QP** (R-009 §2 upgrade path), numpy Goldfarb-Idnani. Run: `python Tools/GaitOpt/qp_proto/run_fid.py --tag A_fid --set sigma=1` (project root, ~7 s wall).

## What changed and why
1. qdd data bug fixed (planar_model.py): `ref.at()["qdd"]` is now the spline of the node central-difference qdd; node wrench residual 1.6e-6 m g (was 2.49 m g at node 36). Old value kept as `qdd_qspline`.
2. One QP over x = [qdd (9), 2 pyramid generators per contact point, mu_eff 0.40]: hard = 3 unactuated dynamics rows (u = pinv(CJ^T)(M qdd + h - Jp^T F) then realises all 9), beta >= 0, fn <= fmax, |u| <= 0.98 LIM (12 rows); soft = CoM x/z PD + ff, L about CoM, stance-point accel = -kvs v (w 1e6), swing heel, foot pitch, trunk, posture, force prior/smoothness. Saturation is solved: 0.3 % saturated ticks (9.3b: 33-53 %), no clipping needed except on 0.05 % solver-failure ticks.
3. Contact awareness: contact set = measured load (tick-mean plant contact force = contact read-back, hysteresis 0.03 / 0.01 m g); a scheduled-loaded point near the ground (< 5 cm) is not released; fmax ramps to 0 over 0.3 s before the scheduled unload, rises <= 1 m g per 0.08 s after first contact; swing tasks blend in over 0.2 s after measured lift-off; touchdown = measured load on the swing foot after 0.6 ts -> re-anchor phase, swap, shift x (R-009 §5).
4. Controller-side reference smoothing, sigma = 1 node Gaussian over the periodic node data (data file unchanged; metrics against the raw reference). The raw reference has node-scale chatter of the swing foot (toe z 5.1/7.7/5.0/8.6 m at nodes 36-39) and a landing heel at 11 m/s.

## Criterion A runs (start = node 0, speed x1.02, trunk +1 deg; metrics as run_A.py, over the run up to the fall)
| metric (limit) | A_fid sigma 1 | A_fid_raw sigma 0 | A_fid_delay0 sigma 1, no delay | 9.3b pre / post |
|---|---|---|---|---|
| steps, fall (20, none) | 7.86 FALL | 2.75 FALL | 2.78 FALL | 1.59 / 2.66 FALL |
| mean speed / v (0.95-1.05) | 1.061 | 1.277 | 1.195 | 0.981 / 0.864 |
| CoM z RMS % l (<= 1) | 5.97 (1.95 before the fall, t < 28.5 s) | 5.04 | 4.61 | 4.78 / 2.66 |
| pelvis pitch RMS deg (<= 3) | 0.71 | 0.89 | 0.86 | 7.1 / 11.4 |
| foot placement RMS % step (<= 5), touchdowns | 6.7 (8) | 57.9 (3) | 57.4 (3) | 12.9 / 48.3 |
| saturated-torque ticks % (<= 1) | 0.33 | 0.27 | 0.13 | 52.6 / 33.7 |
| QP torque-limit row active, % ticks | 44 | 34 | 39 | - |
| plant friction ratio vs 0.4 max (<= 1); % ticks > 1 (Fn > 0.01 m g) | 2.0; 20.7 | 2.0; 17.3 | 2.0; 20.6 | 2.0 / 2.0 |
| solver success % (100) | 99.95 | 100 | 99.87 | 100 / 100 |
| tick time p50 / p99 ms | 0.36 / 0.57 | 0.37 / 0.57 | 0.38 / 0.56 | 0.42 / 0.70 |
Step speeds A_fid (touchdown to touchdown, / v): 0.96 1.02 1.00 0.95 1.04 1.09 1.23. Sweeps (weights, kvs, ramps, force-rate bounds vs measured force, sigma 1.5-3) gave 1.1-7.2 steps, none better; the outcome is very sensitive to small parameter changes.

## Remaining failure mode
- Every step: CoM z dips 0.7-1.0 m around the transition (reference nodes 36 -> 3): the reference push-off is a one-node spike (sum fn 2.9-3.9 m g at node 36, comdd z 2.9 g) with the loaded heel moving up at 3.1 m/s, and the heel-strike load 1.65 m g at node 0. Realised total fn there is ~1.0-1.1 m g. Pre-fall CoM z RMS is 1.95 % l, about twice the limit, about evenly split between the transition window and the rest.
- Landings give 2.3-3.1 m g impact peaks on the 2e9 N/m contact, then the stance foot bounces (empty contact set on 1 % of ticks before the fall). The fall at step 7-8 starts at such a bounce (t = 29.0 s, all four points lift, hip z 41.0 -> 35.9 m).
- Slip: plant |Ft| > 0.4 Fn on about 20 % of ticks, 12 % with Fn > 0.2 m g. The command is inside 0.4 by construction. The plant law has no static friction (tanh, eps 0.02 m/s), so ratio <= 1 against 0.4 means |v_t| < 0.011 m/s at every loaded point, at touchdown too. Report only: against the plant mu 0.8 the ratio is <= 1 by construction, so neither reading is a clean "friction demand" test. Unchanged here.

## Binding constraint (evidence: out/proto/check_fid.log)
At the exact reference state with the scheduled contacts, the QP reproduces the reference torques to <= 0.01 LIM on all nodes except the three transition nodes. Node 10 (toe-off) misses by 1.05-1.12 LIM and |dF| 0.72 m g. Nodes 14-15 (toe-down) miss by 0.25 LIM and 0.54 m g. Node 36 (push-off) misses by 1.09 LIM and 2.0 m g, with qdd off by 54 rad/s^2.
The reference's contact transitions are impulsive and kinematically inconsistent with a loaded unilateral point: the loaded point leaves the ground within one 0.11 s node. No controller that respects a stance-point constraint and |tau| <= LIM can track them, and the spring contact cannot unload or load that fast either.
With smoothing (sigma 1) the controller survives the transitions only with the CoM z loss above. What is left is the landing bounce on the soft contact. Latency is not the cause: with no delay the run falls at 2.8 steps, the same as with the 1-tick delay at sigma 0.
I read A as unreachable without a reference whose transitions are dynamically feasible over several nodes: force ramps, pitch and heel motion consistent with contact. The reference trajectory is a hard limit here, so it is not changed. The other option is a contact model in the controller: a spring-aware force prediction instead of rigid stance rows. Tried as a rate bound on fn around the measured force (rate_bw), it was worse (1.1-2.4 steps).

## Paths
Tools/GaitOpt/planar_model.py (qdd fix), Tools/GaitOpt/qp_proto/{controller_fid.py, run_fid.py (+PushSim for B, same physics), check_fid.py, diag_fid.py},
Tools/GaitOpt/out/proto/{A_fid, A_fid_raw, A_fid_delay0}.{json,npz,log}, out/proto/check_fid.log. The old two-stage controller.py and run_A.py are unchanged.
