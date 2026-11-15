# T-005 r4 step 9.3b-4 (escalated, §14): onset/landing/shear fixes, full A-D set, two more fix cycles
**Result:** with preset **b4**, A meets **7 of 8** items. Solver success is now **100 %**, B passes both ways, and C passes 3 of 5 (4 of 5 if the controller is told the 80 % limit). **Two failures remain:** the plant friction ratio against the 0.4 budget, in A and in every C run, and a **fall with model mass +10 %**. The two extra cycles (r2) fixed neither. **NO-GO.**
## 1. b4 controller (`qp_proto/controller_fid.py`, `run_fid.py --preset b4`; every new option defaults to off, so b3 behaviour is unchanged)
- `onset 1`: the complementarity onset node counts as scheduled-loaded, so a touching-down point no longer gets fmax = 0.
- `land 1, land_act 1, land_k0 36`: from node 36, the swing heel (x, z) and foot pitch follow a quintic. It starts from the MEASURED heel state and ends at the reference landing point with v = (0, -0.3 m/s). It is re-planned once per step.
- `fr_meas 1, fr_margin 0.9`: the QP cone is 0.9 μ, plus a per-foot shear row |ΣFt_cmd| ≤ 0.9 μ ΣFn_meas (last tick's read-back; floor 1e-3 m g against GI degeneracy). The budget μ 0.4 and the metric are unchanged.
- `kvs 5` as in b3. `solver_ok` counts only the full QP with every hard row; a fallback solve without the torque rows counts as a failure.
- Design round: out/proto/sweep_9_3b4.log. land_k0 35.5 is better on A/B/D but worse on C (1/5); 35 gives speed 0.938.
## 2. b4 set (`bash Tools/GaitOpt/run_9_3b4.sh b4`; table `qp_proto/summ_9_3b4.py b4`; log out/proto/b4.log)

| run | fell | steps | speed | CoM z %l | pitch ° | foot % | sat % | fr>1 % ticks | fr max | solver % | A items |
|---|---|---|---|---|---|---|---|---|---|---|---|
| A / Amir | F | 20 | 0.999 | 0.18 | 0.32 | 4.56 | 0 | 3.81 | 2.00 | 100 | 7/8 |
| Bp / Bm (0.15 v) | F | 20 | 0.998 / 1.000 | 0.67 / 0.18 | 0.32 | 4.00 / 4.12 | 0 | 4.45 / 4.09 | 2.00 | 100 | rec = 1 step, **B PASS** |
| Cm09 (model mass/inertia 0.9) | F | 20 | 1.009 | 0.17 | 0.33 | 2.52 | 0 | 3.87 | 2.00 | 100 | 7/8 |
| **Cm11 (x1.1)** | **T** | 1.7 | 1.486 | 5.88 | 0.70 | 13.3 | 0 | 10.6 | 2.00 | 100 | 3/8 |
| Cmu06 (plant μ 0.6) | F | 20 | 1.000 | 0.18 | 0.31 | 4.36 | 0 | 5.22 | 1.50 | 100 | 7/8 |
| Clim08 (plant limit 0.8, controller unaware) | **T** | 2.2 | 1.346 | 5.69 | 2.02 | 34.5 | 50.4 | 7.50 | 2.00 | 100 | 2/8 |
| Clim08aw (same, controller lim_frac 0.784) | F | 20 | 1.004 | 0.12 | 0.27 | 3.48 | 0 | 3.07 | 2.00 | 100 | 7/8 |
| Cdel2 (2-tick delay) | F | 20 | 1.008 | 0.22 | 0.35 | 4.23 | 0 | 3.21 | 2.00 | 100 | 7/8 |
| Dp / Dm (0.30 v, informational) | T / F | 5.5 / 20 | 1.134 / 0.996 | 3.16 / 0.20 | | | | | | 99.93 / 100 | Dm rec = 1 |

Every 7/8 row fails only `friction_le_1`. b3 reference set: b3ref_* in out/proto/b3ref.log. Fr 0.15 (D) with b3 falls at 1.8 steps (b3_Dfr015); Fr 0.20 was not run. QP p50 0.56 ms, GI iterations p99 8-11.
## 3. Remaining binding 1: friction (b4_A, ratio counted per point on tick-mean plant force at Fn > 1e-3 W)
- 202 ticks over the limit, all at transitions. Heel strike (nodes 37-3): 97. Toe-down (14-17 / 54-57): 77. Trailing-toe unload (9-11): 26.
- **Heel strike:** the swing heel touches down 0.2 s early (step 4.217 vs 4.418 s) while still moving at **vx 16-17 m/s, vz -6 m/s**. The patch slips at μ_plant 0.8 for that tick (ratio 2.0). The heel tracks the landing quintic to 0.1 m. The quintic itself reaches z ≈ 0 at node 38 because it starts from the measured heel state (vz -18 m/s at node 36, following the reference's node-scale zigzag z 3.85 / 3.02 / 0.97 / 2.52 / 0.05 m at nodes 35-39).
- **Toe-down:** when the toe enters the set, the QP shifts the heel load from 1.0 to 0.16 W in 5 ticks. The soft contact (6 cm penetration at 1.2 W) cannot follow without moving the foot, so the foot rocks: the heel lifts at 0.75 m/s and total Fn falls to 0.1 W. Then the patch slips.
- **Toe unload:** realised toe load 0.13 W against 0.07 W commanded; ratio 1.04.
- At Fn > 0.05 W the max is still 2.0, so this is not a small-load counting artefact.
## 4. Remaining binding 2: Cm11 fall
- The fall happens before the landing logic matters, in step 1. The hip runs ahead of the reference: vx error +1 → +3.3 m/s at nodes 18-36, stance-hip error -13°.
- Single support ends with zero plant load at nodes 34-36. The stance toe leaves the ground before the swing heel lands at node 41, and CoM z then drops 2 m.
- CoM x feedback is weak (om_x 0.8 rad/s). With om_x 2, A improves but Cm11 falls sooner (1.4 steps).
## 5. Cycles r2 (this worker; out/proto/sweep_9_3b4_r2.log; each run on A and Cm11)
- Cycle 1:
  - `land_vclip` (bisection clip of the quintic's initial z-velocity so z ≥ 0 before T): no effect. The quintic has no undershoot; it only gets low early. fr>1 3.90 %.
  - `rate_bw` 0.1 / 0.2 (Fn command within ±r of the measured load): A falls at 2.0 / 1.6 steps.
- Cycle 2:
  - `land_xlead` 0.11 / 0.2 / 0.3 (x and pitch reach rest before z touches): touchdown on time, heel vx ≈ 0 and vz -2.8 m/s at contact, and **toe-down exceedances gone** (57 → 0 at 0.2). But A falls at 5.5 / 19.9 / 18.2 steps, with more strike exceedances (128) after the gait degrades.
  - `w_smooth` 0.1: neutral (3.73 %).
  - `om_x` 2: see §4.
- Cm11 falls at 1.4-1.8 steps in every variant.
- New options stay in the code, default off. `test_controller_fid.py::test_mirrored_start_is_the_mirror_trajectory` fails (6.8e-5 vs 1e-6), also at the WIP commit (pre-existing).
## 6. Best next step
1. **Fix the reference, not the controller.** Re-solve v2 with a swing-heel smoothness / monotone-descent row over nodes 34-40, tangential heel speed ≤ 0.4 × normal at the touchdown node, and toe-down pitch-rate rows. Then re-run `land_xlead 0.2` on it: it already removes the toe-down slips and the tangential strike speed.
2. **Mass robustness:** add an online mass/gravity-scale estimate from read-back ΣFn against CoM z̈ (or an integral term on CoM x/z). Also a support guard: the stance fmax does not ramp out while the swing foot is unloaded.
3. Founders' decision: is a ratio ≤ 1 on every tick at Fn > 1e-3 W attainable for impacts on a stick-slip plant, or should impact ticks be counted against μ_plant?
**Paths:** Tools/GaitOpt/{run_9_3b4.sh, qp_proto/{controller_fid.py, run_fid.py, run_A.py, summ_9_3b4.py}}; out/proto/{b4_*, b3ref_*, b4.log, sweep_9_3b4.log, sweep_9_3b4_r2.log}.
