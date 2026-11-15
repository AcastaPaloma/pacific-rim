# T-005 r4 step 9.3b-3 (escalated, §14): feasible-transition reference v2, stick-slip plant, criterion A
**Result:** reference v2 **FEASIBLE**. Criterion A walks **20/20 steps** and meets 6 of 8 items. It **FAILS** two: plant friction ratio vs 0.4, and solver success 99.85 %. B: **no fall** either way, but the recovery test **FAILS**: the unpushed gait already alternates ±5.5 % per step. Not a go yet.
## 1. Reference v2 (`stride_opt_tc.py --trans`; without the flag = old behaviour)
Rows only ADDED (linear in z and node outputs). All old rows, limits, μ_eff 0.37 (`--lat`), Fr 0.10 and the objective are unchanged.
- **(a) Complementarity:** Fn = 0 at the first and last loaded node of each contact interval (4 rows). The central-difference velocity there is nonzero because the neighbour node is airborne. A loaded point now has zero velocity; a touching-down or lifting point carries no force.
- **(b) Fn rate:** |ΔFn| ≤ 0.5 BW per 0.11 s for every point and node pair, including across node N (150 rows). That is 4.5 BW/s, so an unload from 1 BW takes ≥ 0.22 s = 13 ticks at 60 Hz. Comparable to the 9.3b-2 controller's fmax ramp (3.3 BW/s); 2-3x a Froude-scaled human heel-strike loading rate.
- **(c) Lift-off speed:** |P(k+1) − P(k)| ≤ 0.5 m/s · h at the last loaded node, x both signs and z up (6 rows); same value as the touchdown `--vlim`.
**Runs:** `bash Tools/GaitOpt/run_9_3b3.sh` (N 40 and N 80 in parallel, warm start out/stride_fr010_lat.json). Then `run_9_3b3.sh polish N warm out` re-solves from its own output. Logs: out/run_9_3b3*.log.

| file | verdict (residual) | peak τ/LIM | note |
|---|---|---|---|
| stride_fr010_v2_n40 | FEASIBLE (9e-14) | 1.000 ankle_B | iteration cap; swing-foot zigzag at nodes 36-39 (qdd 78 rad/s²) |
| stride_fr010_v2_n40p | NOT CONVERGED (3.8e-5) | 0.895 | polish 1 |
| **stride_fr010_v2_n40p2 = v2** | **FEASIBLE (5.4e-6)**, optimality 2.4e-4 | **0.859 ankle_B** | polish 2, 183 s |
| stride_fr010_v2_n80 / _n80p | FEASIBLE (4e-13, xtol) / NOT CONVERGED (1.4e-5) | 0.999 ankle_B, 0.996 hip_A | other local optimum: late-swing braking at the torque limit; unused |

v2 vs v1, objective / step / peak ΣFn / 3D friction use: v2 0.252 / 4.42 s, 28.5 m / 1.54 BW / 0.88; v1 0.125 / 4.55 s, 29.3 m / 3.88 BW / 1.00.
Binding in v2: 5° ROM margins (knee, ankle, hip), clearance at node 18, rate rows (0.499 of 0.5), B-heel lift-off 0.498 m/s. N 40 can express the ramps (they span ≥ 2 nodes).
**The 9.3b-2 binding constraint is gone:** at the exact reference state the QP reproduces the reference torques to ≤ 0.086 LIM at every node. v1 missed by 1.1 LIM at nodes 10 and 36 (out/proto/check_fid_v2.log).
## 2. Plant `PlantSim(friction="stick")` (planar_sim.py; default stays "tanh"; μ_plant 0.8)
- Tangential spring-damper anchor per loaded point: kt = kn = 2e9 N/m, ζ_t 0.7, ω·dt ≤ 0.17.
- One friction patch per foot, as in PhysX: |ΣFt| ≤ μ ΣFn, shared in proportion to Fn.
- On slip, anchors move so the springs give the cap; on lift, dropped. (A per-point variant made the old reference fall at 1.2 steps.)
## 3. Criterion A (node 0, speed x1.02, trunk +1°, 20 steps)
One gain round: 14 settings on v2 with the stick plant (out/proto/sweep_9_3b3.log). Only **kvs 20 → 5** (stance-point velocity damping) helps; all others fell at 1.2-6.9 steps.

| run | steps | speed/v | CoM z %l | pitch ° | foot %step | sat % | fr>1 % ticks (max vs 0.4/0.8) | solver % |
|---|---|---|---|---|---|---|---|---|
| **A_v2_final (v2, stick, kvs 5)** | **20** | **1.000** | **0.31** | **0.22** | **2.65** | **0.04** | **11.2 (2.0/1.00) FAIL** | **99.85 FAIL** |
| A_v2_kvs5_tanh (v2, old plant, kvs 5) | 20 | 0.986 | 0.47 | 0.38 | 5.03 FAIL | 0.04 | 15.7 (2.0/1.00) FAIL | 99.96 FAIL |
| A_v2_stick_s0 / _tanh_s0 (9.3b-2 gains) | 2.67 / 4.29 FALL | 1.33 / 1.08 | 4.9 / 4.0 | 0.61 / 0.91 | 9.6 / 60 | 0.0 / 1.0 | 17 / 26 | 99.86 / 99.65 |
| A_v1_kvs5_stick_s0 / _tanh_s1 (v1 ref, kvs 5) | 2.00 / 4.05 FALL | 1.15 / 1.03 | 5.4 / 2.4 | 1.0 / 1.1 | 61 / 49 | 0.5 / 0.3 | - | - |

- v2 is necessary: with v1, kvs 5 still falls.
- QP tick p50/p99: 0.41/0.81 ms single runs, 0.77/1.38 ms for A_v2_final on a loaded machine. Plant slip: 7.6 % of loaded foot sub-steps.
## 4. Criterion B (v2, stick, kvs 5; ±0.15 v impulse at mid-stance of step 4)
- Forward / backward: no fall; speed 0.993 / 0.999, CoM z 0.31 / 0.33 %l, pitch 0.25 / 0.25°, foot 2.98 / 2.76 %.
- Recovery "every later step within 5 % in 4 steps": no. The unpushed run alternates 0.946/1.033 (2-step mean within 1 %); the push leaves no lasting trace.
## 5. Remaining binding (out/proto/A_v2_final.npz)
- **Friction:**
  - Commanded shear ≤ 1.006 · 0.4 Fn; realised exceeds 0.4 Fn on 11 % of ticks.
  - Share of ticks in each phase window that exceed it: nodes 0-1 (heel strike) 49-56 %, 8-9 (before toe-off) 31-48 %, 15-16 (toe-down) 22 %, 36-39 (push-off) 25-38 %.
  - With Fn > 0.4 W the p95 ratio is 0.98: small-load plan/plant drift (1-tick delay plus contact springs). Against μ 0.8: ≤ 1 by construction.
- **Solver:** 8 infeasible ticks (0.15 %), all on the touchdown tick (phase re-anchor). Their torque rows are dropped and the torques clipped, which gives the 0.04 % saturated ticks.
- **B:** left/right step-speed alternation.
- All three are controller-side; not tuned beyond the one gain round. Config/Mech/gait_reference_opt.json was not written.
**Paths:** Tools/GaitOpt/{stride_opt_tc.py, run_9_3b3.sh, planar_sim.py, qp_proto/run_fid.py (--ref, --friction), check_fid.py (ref=), diag_fid.py}; out/stride_fr010_v2_*; out/proto/{A_v2_*, A_v1_kvs5_*, B_v2_kvs5_stick_p±1, A_fid_v1ref_stick, sweep_9_3b3.log, check_fid_v2.log}.
