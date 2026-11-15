# T-005 r4 step 9.3b-5: reference v3, online mass estimate, support guard; full A-D set
**Result:** preset **b5** on reference **v3**: A is **7 of 8** (friction only), **B passes both ways**, C: **no fall in any of the 5 runs** (incl. mass +10 % and the 80 % limit with the controller told), each **7 of 8** (friction only). The friction item (plant ratio vs 0.4, every tick, per point, impact ticks counted) still fails everywhere: max 2.00, 2.8-6.0 % of ticks. **NO-GO.**
## 1. Reference v3 (`stride_opt_tc.py --lat --trans --v3`, `run_9_3b5.sh solve`; out/stride_fr010_v3_n40p2.json, FEASIBLE: dyn residual 2.4e-6, ineq 1.4e-9; same body data, mu 0.40)
- Added rows only (nothing else relaxed): swing heel (A heel) from node 35: y monotone, y(k+1) >= 0.3 y(k), convex; heel x from node 36 forward and decelerating.
- Touchdown (node 0, B heel): |vx| <= 0.4 (-vy), -vy >= 0.1 m/s. Fn rate <= 0.25 BW / 0.11 s within +-3 nodes of each complementarity node (38 rows; was 0.5).
- Result: heel y at nodes 35-40 = 3.60, 1.90, 0.58, 0.18, 0.055, 0 m (v2: 3.85, 3.02, 0.97, 2.52, 0.05, 0). Heel x steps 1.9, 2.3, 1.9, 1.0, 0.02 m. Heel load 0.25, 0.5, 0.75, 0.96 BW over nodes 41-44. Solved in 3 passes of 500 iterations (v3_n40, n40p, n40p2).
## 2. Controller b5 (`controller_fid.py`; every new option defaults off, so b4 is unchanged)
- `mass_est 0.05`: ms <- sum Fn_meas / (m (g + d comd_z/dt)) of the last tick, filtered, clipped 0.7-1.5. M, h, W, m and L are scaled by ms. Controller mass 1.1: ms settles at 0.909 = 1/1.1 (pytest `test_mass_estimate_converges_to_plant_mass`).
- `sg_floor 0.3`: while the swing foot has no measured contact, the stance leg's fmax is not below 0.3 m g (`sg_fmin`: matching lower bound on summed Fn_cmd, not used).
- b5 = b4 with `land_k0 35` (36 falls on v3 at 4-6 steps) plus the two options above. `land_xlead` stays 0: with 0.1-0.3 the toe-down and rel +10 ticks improve (fr>1 0.8-1.3 %) but the step is slow (speed 0.94-0.96, 4.7 % off), B(-) fails and Clim08aw falls (set b5x). Sweeps: out/proto/sweep_9_3b5_s1..s4.log.
## 3. b5 set (`REF=out/stride_fr010_v3_n40p2.json bash Tools/GaitOpt/qp_proto/run_9_3b5.sh b5 --preset b5`; log out/proto/b5.log)

| run | fell | steps | speed | CoM z %l | pitch ° | foot % | sat % | fr>1 % ticks | fr max | solver % | A items |
|---|---|---|---|---|---|---|---|---|---|---|---|
| A / Amir | F | 20 | 1.019 | 0.14 | 0.24 | 1.40 | 0 | 3.07 | 2.00 | 100 | 7/8 |
| Bp / Bm (0.15 v) | F | 20 | 1.019 / 1.017 | 0.14 / 0.17 | 0.25 / 0.27 | 1.42 / 1.43 | 0 | 3.09 / 3.65 | 2.00 | 100 | rec = 1 step both, **B PASS** |
| Cm09 (model mass/inertia 0.9) | F | 20 | 1.019 | 0.14 | 0.24 | 1.40 | 0 | 2.94 | 2.00 | 100 | 7/8 |
| **Cm11 (x1.1)** | F | 20 | 1.019 | 0.14 | 0.24 | 1.40 | 0 | 3.14 | 2.00 | 100 | 7/8 (was a fall) |
| Cmu06 (plant mu 0.6) | F | 20 | 1.019 | 0.15 | 0.24 | 1.37 | 0 | 2.84 | 1.50 | 100 | 7/8 |
| Clim08 (limit 0.8, controller unaware) | F | 20 | 1.019 | 0.18 | 0.19 | 1.53 | 16.9 | 4.07 | 2.00 | 100 | 6/8 (sat) |
| Clim08aw (controller lim_frac 0.784) | F | 20 | 1.020 | 0.07 | 0.19 | 1.14 | 0 | 5.98 | 2.00 | 100 | 7/8 |
| Cdel2 (2-tick delay) | F | 20 | 1.023 | 0.14 | 0.18 | 1.09 | 0 | 2.99 | 2.00 | 100 | 7/8 |
| Dp / Dm (0.30 v, informational) | F / T | 20 / 5.9 | 1.015 / 0.930 | | | | | 3.18 / 7.33 | | 100 | Dp rec = 1; Dm falls |

Every 7/8 row fails only `friction_le_1`. GI iterations p99 8-11. The b5x set (xlead 0.15, late_vz 8): out/proto/b5x.log (A 7/8 speed 0.958, fr>1 1.2 %; Bm fails; Clim08aw falls at 3.2 steps).
## 4. What binds now (b5_A: periodic, ~13 bad ticks per tenth of the run, ~6 per step)
- Strike tick (rel -1): heel Fn 1.16 W, Ft 0.89 W (ratio 2.0), the heel lands with the QP sending shear at impact.
- Tick after touchdown (rel 0): the old toe unloads 0.87 -> 0.11 W while its anchored tangential spring still carries 0.05 W (ratio 1.4); same for the toe in the next ticks (rel +48/49: toe 0.19-0.02 W with Ft 0.13-0.02 W).
- Two ticks late in single support (rel 41-42): stance heel at 0.09 W while Ft 0.16 W (ratio 1.8-2.0), the heel-off unload.
- Start transient: 43 of ~530 bad ticks in the first 10 % of the run.
- Not mass, not the heel path (the v3 touchdown is now vertical in the reference), not the toe-down: it is the impact and the near-zero-load ticks of a stick-slip patch with an anchored spring, counted per point at Fn > 1e-3 W.
## 5. Mirror test (item 4): not a model asymmetry, float noise
- Mirror and normal runs differ by 2.6e-11 at tick 2 and 6.8e-5 at 0.6 step. A 1e-16 relative perturbation of the initial state alone gives the same 1e-8 relative tick-0 command difference (ill-conditioned QP, weights 1e-6..1e6) and 0.13 in q after 0.6 step.
- The test now compares 20 ticks at 1e-6 plus the tick-0 command at 1e-6 relative (a real asymmetry shows at 1e-3 already on tick 0); the tolerance is not loosened.
## 6. Best next step
- The friction item cannot reach 0 ticks with soft impact contact + anchored tangential spring unless the shear is zero while Fn falls and rises: a per-point shear row |Ft_cmd| <= 0.4 Fn_pred on the foot that unloads or lands, and a landing with a lower vertical speed than 0.3 m/s (v_td 0.08-0.15 did not help). Otherwise the founders decide whether ticks with Fn < 0.15 W or the strike tick count.
**Paths:** Tools/GaitOpt/{stride_opt_tc.py (--v3), run_9_3b5.sh, qp_proto/{controller_fid.py, run_fid.py (b5, b5x), run_9_3b5.sh, test_controller_fid.py}}; out/{stride_fr010_v3_n40{,p,p2}.json, run_9_3b5_*.log, proto/{b5_*, b5x_*, b5.log, sweep_9_3b5_s*.log}}.
