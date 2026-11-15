# GaitOpt: offline sagittal stride optimisation (T-005 round 3, step 8.1)

Run from the project root: `/opt/miniconda3/envs/kaiju/bin/python Tools/GaitOpt/stride_opt.py --fr 0.10` (deterministic, about 4 min). Options: `--nodes`, `--maxiter`, `--rounds`, `--vlim`, `--plot`.

- Model: planar, 9 DOF (hip x, y, upper-body lean, thigh/shank/foot per leg). Masses, COMs, joint positions from `mech_frame_balanced.json`; the JSON has no inertias, so thin-rod I = m L^2/12 (box for the foot) and the upper body (pelvis, torso, head, both arms hanging) is lumped.
- Discretisation: N = 40 nodes over one step, central differences, periodic mirrored extension (leg A and B swap, hip x + v T_step). Joint torques are eliminated analytically from the Lagrange rows (3 rows stay as equality constraints). Torque and ROM limits are constraints on the eliminated torques/angles.
- Contacts: heel and toe point of each foot, fixed schedule from R-008 (heel strike, flat at node 15, heel rise at node 37, double support 0..10). Active points: y = 0, no slip, force cone mu = friction_dynamic / friction_safety = 0.6 / 1.5 = 0.4. Touchdown ground speed limited to 0.5 m/s (impact-free assumption).
- Limits: hip/knee/ankle ROM from the JSON with a 5 deg margin; torque limits from `body_tuning.json` kind hip, knee, ankle; swing sole clearance >= 0.5 m for swing phase 0.25..0.75; T_step free in +-20 % of R-008.
- Solver: SLSQP with torch-autograd Jacobians (per-node jacobians assembled sparsely), restarted up to 5 times from the last iterate.
- Output: `out/stride_fr010.json` (per node q, q_dot, tau, joint angles, contact forces, plus summary and binding list) and `out/stride_fr010.txt`.
