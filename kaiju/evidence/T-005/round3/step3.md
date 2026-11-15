# T-005 round 3 · step 8.3: continuation to Fr 0.15 and 0.20 (lateral shear in the friction cone)

Solver: `Tools/GaitOpt/stride_opt_tc.py --lat` (7d79db8). The friction cone uses μ_eff(k) = √(μ² − (a_lat(k)/g)²), where a_lat(k) is the lateral LIPM acceleration at each node: HW 6.5 m, zc ≈ 42.3 m, ω 0.48 1/s, peak 0.154 g. This is tighter than 8.1b's cone. All other rows and limits are unchanged. N=40 and at most 500 trust-constr iterations per solve.

Runs:
- `Tools/GaitOpt/run_8_3.sh` (log `out/run_8_3.log`).
- Then, by the orchestrator, re-solves warm-started from each point's own output (log `out/run_8_3b.log`):
  ```
  stride_opt_tc.py --fr F --lat --warm out/stride_frF_lat.json --out out/stride_frF_lat2
  ```
  The first 0.125, 0.15 and 0.20 solves met every row of the solve. They failed only the post-check: 3D friction use 1.002–1.003 > 1.001, because μ_eff had been computed at the warm-start's step time (0.3–0.6 % stale, under the script's 1 % re-solve trigger). Re-solving from their own output computes μ_eff at their own step time.

| Fr | file | verdict | max residual | 3D friction use | step time / length | v | peak τ/limit (worst) | optimality |
|---|---|---|---|---|---|---|---|---|
| 0.10 | `stride_fr010_lat` | FEASIBLE | 2.1e-6 | 1.000 | 4.55 s / 29.3 m | 6.45 | ankle_B 0.55 | 1.1e-4 |
| 0.15 | `stride_fr015_lat2` | FEASIBLE | 1.1e-7 | 0.997 | 3.99 s / 31.5 m | 7.90 | ankle_B 0.60 | 1.7e-3 |
| 0.20 | `stride_fr020_lat2` | FEASIBLE | 1.0e-6 | 0.996 | 3.72 s / 33.9 m | 9.12 | ankle_B 0.70 | 2.3e-3 |
| 0.125 | `stride_fr0125_lat` | NOT CONVERGED (stale μ_eff, 1.002) | 4.7e-8 | 1.002 | 4.13 s / 29.8 m | 7.21 | ankle_B 1.00 | 4.7e-3 |

Every solve stopped at the iteration cap. The points are feasible to 1e-5 but not proven optimal. The unfinished first passes (0.125, `stride_fr015_lat`, `stride_fr020_lat`) were sitting at torque limits (hip 0.996, knee 1.000, ankle 1.000). The finished re-solves got off them, with torque ≤ 0.70 everywhere. The torque saturation was therefore a solver artefact, not a property of the body.

## What binds at all three speeds
- The 5° ROM margins on knee extension (min knee 5.0°), swing ankle dorsiflexion and stance hip extension.
- Toe friction at push-off. The cone is saturated: 3D use is 0.996–1.000.

Torque headroom is ≥ 30 % up to Fr 0.20.

## Reading
Under the shipped body data (no data change, no assist), a periodic sagittal stride is dynamically feasible up to Fr 0.20, with lateral sway shear included. The walk is limited by kinematics and friction, not by actuator strength. 8.4 exports these three points as reference data.

Not checked here:
- yaw at 0.15/0.20 (8.2 covered 0.10 only);
- impacts beyond the 0.5 m/s touchdown-speed cap;
- 3D swing-leg motion.
