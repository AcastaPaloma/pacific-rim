# T-005 r4 step 9.3b-3 (escalated, §14): feasible-transition reference v2, stick-slip plant, criterion A

**Result.** The reference v2 is **FEASIBLE**. Criterion A walks **20/20 steps** and meets 6 of the 8 items; it **FAILS** on the plant friction ratio and on solver success (99.85 %). Criterion B: no fall for either push, but its recovery test **FAILS**, because the unpushed gait already alternates by ±5.5 % from step to step. Not a go yet.

## 1. Reference v2 (`stride_opt_tc.py --trans`; the old behaviour without the flag)
The new rows below are **added**, linear in z and in the node outputs. All existing rows, limits, μ_eff 0.37 (`--lat`), Fr 0.10 and the objective are unchanged.
- **(a) Complementarity.** Fn = 0 at the first and last loaded node of each contact interval (4 rows). At those nodes the central-difference velocity is not 0, because the neighbouring node is in the air. So a loaded point now always has zero velocity, and a point that touches down or lifts off carries no force.
- **(b) Normal-force rate.** |ΔFn| ≤ 0.5 BW per 0.11 s between all consecutive nodes of each point, across node N too (150 rows). That is 4.5 BW/s: an unload from 1 BW takes at least 0.22 s, or 13 ticks at 60 Hz. The 9.3b-2 controller's own fmax ramp was 3.3 BW/s. The bound is 2-3x a human heel-strike loading rate at the same Froude scaling.
- **(c) Lift-off speed.** |P(k+1) − P(k)| ≤ 0.5 m/s · h at the last loaded node, in x (both signs) and z (up), 6 rows. Same value as the touchdown `--vlim`.

**Runs.** `bash Tools/GaitOpt/run_9_3b3.sh` solves N 40 and N 80 in parallel, warm-started from out/stride_fr010_lat.json. `run_9_3b3.sh polish N warm out` re-solves a result from its own output. Logs: out/run_9_3b3*.log.

| file | verdict (max residual) | peak τ/LIM | note |
|---|---|---|---|
| stride_fr010_v2_n40 | FEASIBLE (9e-14) | 1.000 (ankle_B) | iteration cap; zigzag swing foot at nodes 36-39 (qdd 78 rad/s²) |
| stride_fr010_v2_n40p | NOT CONVERGED (3.8e-5) | 0.895 | polish 1 |
| **stride_fr010_v2_n40p2 = v2** | **FEASIBLE (5.4e-6)**, optimality 2.4e-4 | **0.859 (ankle_B)** | polish 2, 183 s |
| stride_fr010_v2_n80 / _n80p (N 80) | FEASIBLE (4e-13, xtol) / NOT CONVERGED (1.4e-5) | 0.999 (ankle_B), 0.996 (hip_A) | other local optimum (late-swing braking at the limit); not used |

| | v1 (stride_fr010_lat) | v2 |
|---|---|---|
| objective | 0.125 | 0.252 |
| step | 4.55 s / 29.3 m | 4.42 s / 28.5 m |
| peak total Fn | 3.88 BW | 1.54 BW |
| 3D friction use | 1.00 | 0.88 |
| QP torque miss at the exact reference state (out/proto/check_fid_v2.log) | 1.1 LIM at nodes 10 and 36 | ≤ 0.086 LIM at every node |

- **The 9.3b-2 binding constraint is gone.**
- **Binding in v2:**
  - the 5° ROM margins (knee, ankle, hip);
  - the clearance row at node 18;
  - the rate rows (max 0.499 of 0.5);
  - the B-heel lift-off row (0.498 m/s; A toe 0.45).
- **N 40 is fine:** the ramps span at least 2 nodes, so N 80 was not needed.

## 2. Plant: `PlantSim(friction="stick")` in planar_sim.py
The default stays "tanh"; μ_plant stays 0.8.
- **Anchors.** Each loaded point has a tangential spring-damper anchor: kt = kn = 2e9 N/m, ζ_t 0.7, ω·dt ≤ 0.17.
- **Patch.** One friction patch per foot, as in PhysX: |ΣFt| ≤ μ ΣFn, shared in proportion to Fn, so no heel-toe shear.
- **Slip and lift.** On slip the anchors move so that the springs alone give the cap; on lift the anchor is dropped.
- **Per-point variant (rejected).** With it, the old reference fell at 1.2 steps.

## 3. Criterion A (node 0, speed x1.02, trunk +1°, 20 steps)
One gain round: 14 settings in parallel, on v2 with the stick plant (out/proto/sweep_9_3b3.log). Only **kvs 20 → 5** helps (kvs = stance-point velocity damping); every other setting fell within 1.2-6.9 steps.

| run | steps | speed/v | CoM z %l | pitch ° | foot %step | sat % | fr > 1, % ticks (max vs 0.4/0.8) | solver % |
|---|---|---|---|---|---|---|---|---|
| **A_v2_final (v2, stick, kvs 5)** | **20** | **1.000** | **0.31** | **0.22** | **2.65** | **0.04** | **11.2 (2.0/1.00) FAIL** | **99.85 FAIL** |
| A_v2_kvs5_tanh (v2, old plant, kvs 5) | 20 | 0.986 | 0.47 | 0.38 | 5.03 FAIL | 0.04 | 15.7 (2.0/1.00) FAIL | 99.96 FAIL |
| A_v2_stick_s0 / A_v2_tanh_s0 (9.3b-2 gains) | 2.67 / 4.29 FALL | 1.33 / 1.08 | 4.9 / 4.0 | 0.61 / 0.91 | 9.6 / 60 | 0.0 / 1.0 | 17 / 26 | 99.86 / 99.65 |
| A_v1_kvs5_stick_s0 / _tanh_s1 (v1 reference, kvs 5) | 2.00 / 4.05 FALL | 1.15 / 1.03 | 5.4 / 2.4 | 1.0 / 1.1 | 61 / 49 | 0.5 / 0.3 | - | - |

- **v2 is necessary:** with v1, kvs 5 still falls (last row).
- **QP tick time** (p50/p99): 0.41/0.81 ms in single runs, 0.77/1.38 ms for A_v2_final on a loaded machine.
- **Plant slip:** 7.6 % of loaded foot sub-steps.

## 4. Criterion B (v2, stick, kvs 5; impulse ±0.15 v at mid-stance of step 4)

| push | fall | speed/v | CoM z %l | pitch ° | foot %step | recovered within 4 steps |
|---|---|---|---|---|---|---|
| forward | no | 0.993 | 0.31 | 0.25 | 2.98 | no |
| backward | no | 0.999 | 0.33 | 0.25 | 2.76 | no |

Recovery requires every later step within 5 %. The unpushed run already alternates 0.946 / 1.033 (2-step mean within 1 %), and the push leaves no lasting trace.

## 5. Remaining binding (out/proto/A_v2_final.npz)
- **Friction vs the 0.4 budget.** Commanded shear is ≤ 1.006 · 0.4 Fn, but the realised shear exceeds it on 11 % of ticks. With Fn > 0.4 W the p95 ratio is 0.98. The excess is small-load drift between plan and plant (one-tick delay plus the contact springs). Against μ 0.8 the ratio is ≤ 1 by construction. Share of ticks in each window that exceed it:

  | window | nodes | ticks |
  |---|---|---|
  | heel strike | 0-1 | 49-56 % |
  | before toe-off | 8-9 | 31-48 % |
  | toe-down | 15-16 | 22 % |
  | push-off | 36-39 | 25-38 % |

- **Solver.** 8 infeasible ticks (0.15 %), all on the touchdown tick (phase re-anchor). Their torque rows are dropped and the torques clipped, which gives the 0.04 % saturation.
- **B.** The left/right step-speed alternation.

All three are controller-side and were not tuned beyond the one gain round. Config/Mech/gait_reference_opt.json was not written.

**Paths:** Tools/GaitOpt/{stride_opt_tc.py, run_9_3b3.sh, planar_sim.py, qp_proto/run_fid.py (--ref, --friction), check_fid.py (ref=), diag_fid.py}; out/stride_fr010_v2_*; out/proto/{A_v2_*, A_v1_kvs5_*, B_v2_kvs5_stick_p±1, A_fid_v1ref_stick, sweep_9_3b3.log, check_fid_v2.log}.
