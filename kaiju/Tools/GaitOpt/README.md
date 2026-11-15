# GaitOpt: offline sagittal stride optimisation (T-005 round 3, step 8.1)

Run from the project root: `/opt/miniconda3/envs/kaiju/bin/python Tools/GaitOpt/stride_opt.py --fr 0.10` (deterministic, about 4 min). Options: `--nodes`, `--maxiter`, `--rounds`, `--vlim`, `--plot`.

- Model: planar, 9 DOF (hip x, y, upper-body lean, thigh/shank/foot per leg). Masses, COMs, joint positions from `mech_frame_balanced.json`; the JSON has no inertias, so thin-rod I = m L^2/12 (box for the foot) and the upper body (pelvis, torso, head, both arms hanging) is lumped.
- Discretisation: N = 40 nodes over one step, central differences, periodic mirrored extension (leg A and B swap, hip x + v T_step). Joint torques are eliminated analytically from the Lagrange rows (3 rows stay as equality constraints). Torque and ROM limits are constraints on the eliminated torques/angles.
- Contacts: heel and toe point of each foot, fixed schedule from R-008 (heel strike, flat at node 15, heel rise at node 37, double support 0..10). Active points: y = 0, no slip, force cone mu = friction_dynamic / friction_safety = 0.6 / 1.5 = 0.4. Touchdown ground speed limited to 0.5 m/s (impact-free assumption).
- Limits: hip/knee/ankle ROM from the JSON with a 5 deg margin; torque limits from `body_tuning.json` kind hip, knee, ankle; swing sole clearance >= 0.5 m for swing phase 0.25..0.75; T_step free in +-20 % of R-008.
- Solver: SLSQP with torch-autograd Jacobians (per-node jacobians assembled sparsely), restarted up to 5 times from the last iterate.
- Output: `out/stride_fr010.json` (per node q, q_dot, tau, joint angles, contact forces, plus summary and binding list) and `out/stride_fr010.txt`.

## Step 8.1b: trust-constr re-solve (`stride_opt_tc.py`, `run_8_1b.sh`)

8.1 (SLSQP) hit the iteration limit (max dynamics residual 6.4e-3), so the same problem is re-solved with `scipy.optimize.minimize(method="trust-constr")`. `stride_opt_tc.py` exec's `stride_opt.py` up to its solver call, so model, constraints, limits, schedule and objective are identical (nothing relaxed, `stride_opt.py` untouched). Differences: variables scaled to O(1) (`x = (z - z0)/s`), ROM rows scaled by 1/30 deg, sparse analytic (torch) Jacobians, warm start from a previous result json (`--warm`, any node count, linear periodic-mirrored interpolation, so `--nodes 80` works), and 21 exactly redundant equality rows dropped from the solve (toe `stay_x` of foot B while heel and toe are both planted: implied by heel stay_x plus both y = 0; the Jacobian had rank 243/264 and the singular case made trust-constr slow and erratic). Residuals are still reported over all rows.
- Run from the project root: `/opt/miniconda3/envs/kaiju/bin/python Tools/GaitOpt/stride_opt_tc.py [--nodes 40] [--warm out/stride_fr010.json] [--maxiter 500] [--budget_s 0] [--hess bfgs|sr1] [--tr0 0.05] [--mu0 1e-3] [--out Tools/GaitOpt/out/stride_fr010_tc]`
- Verdict: FEASIBLE if max dynamics/eq residual and max inequality violation <= 1e-5; INFEASIBLE if the solver stops at a stationary point (gtol/xtol) with violations above 1e-5 (local statement); otherwise NOT CONVERGED.
- Output: `out/stride_fr010_tc.json` (stride_fr010.json schema plus solver info and ROM margins) and `.txt` (verdict, residuals, objective, peak tau/limit, Fn, friction use, ROM margins, bindings within 2 %).
- `Tools/GaitOpt/run_8_1b.sh`: attempt 1 at N = 40 (`_tc`), then, only if not FEASIBLE, attempt 2 at N = 80 warm-started from attempt 1 (`_tc80`); log `out/run_8_1b.log`.

### 8.3 lateral-shear friction option (`--lat`) and continuation
- `stride_opt_tc.py --lat`: friction rows use mu_eff(k) = sqrt(mu^2 - (a_lat(k)/g)^2) per node (linear cone |Ft| <= mu_eff Fn), a_lat from the lateral LIPM of `yaw_check.py` (hip half-width, CoM height zc from the warm start, omega = sqrt(g/zc), step time of the iterate, node times k*Ts/N). Tightens the cone only; every other row is unchanged. Without `--lat` output is as before.
- a_lat depends on the step time (a variable): after the solve a_lat is recomputed and the problem re-solved once from the iterate if mu_eff moved by more than 1 %. The .txt reports mu_eff min, 3D friction use max sqrt(Ft^2+Fy^2)/(mu Fn) with Fy = Fn a_lat/g, and the verdict (FEASIBLE is downgraded to NOT CONVERGED if the 3D use exceeds 1.001).
- Average speed v = sqrt(Fr g l) follows `--fr`. A warm start from another Fr uses the previous step length for the periodic hip-x shift.
- `run_8_3.sh`: Fr 0.10 --lat (from stride_fr010_tc.json), then 0.15 (fallback via 0.125), then 0.20 if 0.15 is FEASIBLE; log `out/run_8_3.log`; outputs `out/stride_fr{010,015,0125,020}_lat`.
