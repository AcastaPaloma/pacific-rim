# T-005 round 3 · step 8.1b: stride re-solve with trust-constr (Fr 0.10)

Solver `Tools/GaitOpt/stride_opt_tc.py` (9648def): same model, constraints, limits and objective as 8.1 (`stride_opt.py`), solved with scipy `trust-constr`, variables and constraints scaled to O(1), warm-started from `out/stride_fr010.json`. 21 equality rows were dropped from the solve as redundant: foot B toe stay_x during flat foot, implied by the other rows. They made the Jacobian singular. Residuals are still checked over all rows. Run: `Tools/GaitOpt/run_8_1b.sh`; log `out/run_8_1b.log`. One attempt was enough (N=40, 174 s).

## Verdict: FEASIBLE
- Max dynamics residual 1.6e-7; max equality residual 1.6e-7; inequality violation 0. Tolerance 1e-5.
- The solver stopped at its 500-iteration limit with optimality 4.0e-4. The point is feasible but is not proven to be a local optimum. The objective is 0.1334, against 0.217 at the unconverged 8.1 point.
- Gait: step time 4.55 s (reference 3.79), step 29.3 m, v 6.45 m/s, which is √(0.10·g·l).

## Limits at the solution
- Peak τ/limit: hip 0.20 / 0.32, knee 0.36 / 0.34, ankle 0.43 / 0.55 (legs A / B). No torque limit binds.
- Peak total Fn 3.28 BW; peak point Fn 2.25 BW.
- **Friction binds:** toe use is 0.997 (A, node 10) and 0.994 (B, node 36) of μ = 0.6/1.5 = 0.40 at push-off.
- ROM (5° margin) binds:
  - knee lower bound on both legs (min knee 5.0°);
  - swing ankle dorsiflexion (−14 to −15°);
  - old-stance hip extension (−12.4 to −12.7°).
- Touchdown velocity (strike_vx/vy) and swing clearance are near their limits.

## Reading
A periodic sagittal stride at Fr 0.10 exists under the shipped body data and limits, with about 45 % torque headroom. The tight items are kinematic (ROM margins) and push-off friction, not actuator strength. That matches round 2's stance-knee collapse: the knee sits on its 5° bound and must be held there by control. Torque capacity is not the problem. Next: 8.2 (yaw/lateral check of this point).
