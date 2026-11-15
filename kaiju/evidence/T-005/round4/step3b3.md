# T-005 r4 step 9.3b-3 (escalated, §14): feasible-transition reference v2, stick-slip plant, criterion A

Result: **reference v2 FEASIBLE**. **Criterion A walks 20/20 steps and meets 6 of 8 items. It FAILS on two: friction ratio and solver success (99.85 %).** **B: no fall in either push direction. Its recovery test FAILS because the unpushed gait already varies ±5.5 % from step to step.** Not a go yet.

## 1. Reference v2 (`stride_opt_tc.py --trans`; without the flag the old behaviour is unchanged)
Every existing row, limit, μ_eff 0.37 (`--lat`), Fr 0.10 and the objective are unchanged. The rows below are added and are linear in z and the node outputs.
- **(a) Complementarity.** Fn = 0 at the first and last loaded node of each contact interval (4 rows). The central-difference point velocity there is not zero (the neighbour node is airborne), so a loaded point now always has zero velocity, and a point that touches down or lifts off carries no force.
- **(b) Normal-force rate.** |ΔFn| ≤ 0.5 BW per 0.11 s between all consecutive nodes of each point, including across the mirrored node N (150 rows).
  - 4.5 BW/s means an unload from 1 BW takes ≥ 0.22 s (≥ 13 ticks at 60 Hz).
  - This is about the 9.3b-2 controller's own fmax ramp (3.3 BW/s) and 2-3x a Froude-scaled human heel-strike loading rate.
- **(c) Lift-off speed.** |P(k+1) − P(k)| ≤ 0.5 m/s · h at the last loaded node, x both signs and z up (6 rows). Same value as the touchdown `--vlim`.

Runs: `bash Tools/GaitOpt/run_9_3b3.sh` runs N 40 and N 80 in parallel, warm-started from out/stride_fr010_lat.json. `run_9_3b3.sh polish N warm out` re-solves a result from its own output. Logs: out/run_9_3b3*.log.

| file | N | verdict (max residual) | peak τ/LIM | note |
|---|---|---|---|---|
| stride_fr010_v2_n40 | 40 | FEASIBLE (9e-14) | 1.000 ankle_B, 0.999 knees | iteration cap; zigzag swing foot at nodes 36-39 (qdd 78 rad/s²) |
| stride_fr010_v2_n40p | 40 | NOT CONVERGED (3.8e-5) | 0.895 | polish 1 |
| **stride_fr010_v2_n40p2 = v2** | 40 | **FEASIBLE (5.4e-6)**, optimality 2.4e-4 | **0.859 ankle_B** | polish 2, 183 s |
| stride_fr010_v2_n80, _n80p | 80 | FEASIBLE (4e-13, xtol stop) / NOT CONVERGED (1.4e-5) | 0.999 ankle_B, 0.996 hip_A | another local optimum, with late-swing braking at the torque limit; not used |

**v2 against v1:**

| | v2 | v1 |
|---|---|---|
| objective | 0.252 | 0.125 |
| step time / length | 4.42 s / 28.5 m | 4.55 s / 29.3 m |
| peak total Fn | 1.54 BW | 3.88 BW |
| 3D friction use | 0.88 | 1.00 |

- **Binding set:** the 5° ROM margins (knee, ankle, hip), clearance at node 18, the rate rows (max 0.499 of 0.5) and B-heel lift-off (0.498 m/s; A toe 0.45). N 40 expresses the ramps, which span ≥ 2 nodes, so N 80 was not needed.
- **The 9.3b-2 binding constraint is gone.** At the exact reference state, the QP now reproduces the reference torques to ≤ 0.086 LIM at every node; v1 missed by 1.1 LIM at nodes 10 and 36 (out/proto/check_fid_v2.log).

## 2. Plant: `PlantSim(friction="stick")` in planar_sim.py (default stays "tanh"; μ_plant still 0.8)
- **Anchors.** One tangential spring-damper anchor per loaded point: kt = kn = 2e9 N/m, ζ_t 0.7, ω·dt ≤ 0.17.
- **Patch.** One friction patch per foot, as PhysX does: |ΣFt| ≤ μ ΣFn, shared by Fn, so the heel and toe cannot shear against each other.
- **Slip and lift.** On slip the anchors move so the springs alone give the cap; on lift the anchor is dropped.
- **Per-point variant (tried first).** The old reference fell at 1.2 steps with it.

## 3. Criterion A (node 0, speed x1.02, trunk +1°, 20 steps)
The one gain round: 14 settings in parallel on v2 with the stick plant (out/proto/sweep_9_3b3.log). Only **kvs 20 → 5** helps; kvs is the QP stance-point velocity damping. All other settings fell within 1.2-6.9 steps.

| run (ref, plant, gains) | steps | speed/v | CoM z %l | pitch ° | foot %step | sat % | fr>1 % ticks (max vs 0.4/0.8) | solver % |
|---|---|---|---|---|---|---|---|---|
| **A_v2_final (v2, stick, kvs 5)** | **20** | **1.000** | **0.31** | **0.22** | **2.65** | **0.04** | **11.2 (2.0/1.00) FAIL** | **99.85 FAIL** |
| A_v2_kvs5_tanh (v2, old plant, kvs 5) | 20 | 0.986 | 0.47 | 0.38 | 5.03 FAIL | 0.04 | 15.7 (2.0/1.00) FAIL | 99.96 FAIL |
| A_v2_stick_s0 / A_v2_tanh_s0 (9.3b-2 gains) | 2.67 / 4.29 FALL | 1.33 / 1.08 | 4.9 / 4.0 | 0.61 / 0.91 | 9.6 / 60 | 0.0 / 1.0 | 17 / 26 | 99.86 / 99.65 |
| A_v1_kvs5_stick_s0 / _tanh_s1 (v1 ref, kvs 5) | 2.00 / 4.05 FALL | 1.15 / 1.03 | 5.4 / 2.4 | 1.0 / 1.1 | 61 / 49 | 0.5 / 0.3 | - | - |

- **v2 is necessary:** with v1, kvs 5 still falls.
- **QP tick time** (p50/p99): 0.41/0.81 ms in single runs, 0.77/1.38 ms for A_v2_final on a loaded machine.
- **Plant slip:** 7.6 % of loaded foot sub-steps.

## 4. Criterion B (v2, stick, kvs 5; ±0.15 v impulse at mid-stance of step 4)
- **Forward push:** no fall; speed 0.993, CoM z 0.31 %l, pitch 0.25°, foot 2.98 %.
- **Backward push:** no fall; speed 0.999, CoM z 0.33 %l, pitch 0.25°, foot 2.76 %.
- **Recovery within 4 steps: no.** The test needs every later step within 5 %. The unpushed run already alternates 0.946 / 1.033 from step to step (2-step mean within 1 %), and the push leaves no lasting trace.

## 5. Remaining failures (out/proto/A_v2_final.npz)
**(1) Friction vs the 0.4 budget.**
- The commanded shear is ≤ 1.006 · 0.4 Fn; the realised plant shear goes above it on 11 % of ticks.
- Those ticks cluster at the load transfers (share of the ticks in each node window):

  | window | nodes | ticks above 0.4 Fn |
  |---|---|---|
  | heel strike | 0-1 | 49-56 % |
  | before toe-off | 8-9 | 31-48 % |
  | toe-down | 15-16 | 22 % |
  | push-off | 36-39 | 25-38 % |

- With Fn > 0.4 W the p95 ratio is 0.98. The excess is a small-load drift between planned and realised force, from the one-tick delay and the contact springs.
- Against μ_plant 0.8 the ratio is ≤ 1 by construction.

**(2) Solver.** 8 infeasible ticks (0.15 %), all on the touchdown tick (phase re-anchor). Their torque rows are dropped and the torques clipped, which gives the 0.04 % saturated ticks.

**(3) B.** The left/right step-speed alternation.

All three are controller-side (touchdown-tick handling, shear relative to the measured Fn, step-timing symmetry) and were not tuned here. Config/Mech/gait_reference_opt.json was not written.

Paths: Tools/GaitOpt/{stride_opt_tc.py, run_9_3b3.sh, planar_sim.py}, qp_proto/{run_fid.py --ref --friction, check_fid.py ref=, diag_fid.py}, out/stride_fr010_v2_*, out/proto/{A_v2_*, A_v1_kvs5_*, B_v2_kvs5_stick_p±1, A_fid_v1ref_stick, sweep_9_3b3.log, check_fid_v2.log}.
