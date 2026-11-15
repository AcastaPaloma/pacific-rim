# T-005 r4 step 9.5: first Unity walk runs with the whole-body QP (imp 3): NOT STABLE after 3 cycles
Harness = round 3 (`ScenarioCli.Run`, scenario `walk_fr010`, `-variants`, `-physlist none`; `scripts/run95.sh`, `variants95*.txt`, `diag95.py`). Batch Unity only, 1 other Unity (the editor) running. Imp 2 control run in this harness reproduces round 3 (falls 14.8 s). No assist (extMax 0), no threshold / mech_frame / body_tuning / walk_tuning.json edits. ImpByteIdentityTests green after the code changes (dotnet Units.Mech 115/0, Physics.Body 112/0).
QP cost per tick is fine: p50 0.04 ms, p99 0.05-0.07 ms, 0 failed solves in every run (status Optimal every tick).

| run | what | QP from | falls at | footfalls | realised foot friction / 0.4 (p95, frac > 1, QP ticks) |
|---|---|---|---|---|---|
| baseline | imp 3 as built (QP from the end of Stand, node 0) | 3.0 s | 10.7 s | 13 | 1.69, 21 % |
| diag (no code) | qp_pd_scale 0 / 1, no mass est | 3.0 s | 10.0 / 13.5 / 10.1 s | 16 / 61 / 16 | 1.73, 17 % (pd 0) |
| diag (no code) | imp 2 pipeline, phase sync (control) | - | 14.8 s | 21 | - |
| cycle 1 | hand-off start: imp 2 pipeline on the @v1 reference first, QP (own @v2 reference) takes over at a touchdown once forward speed >= frac x 6.45 m/s; QP re-anchored (Xoff) on the landed heel; both leg assignments built | 11.4 s (frac 0.45) | 15.5 s | 31 | 2.09, 38 % |
| cycle 2 | + time-warped reference: reference clock runs at s = clamp(v / v_ref, smin, 1) of real time (velocities x s, accelerations x s^2) | 8.2 s (0.30, smin 0.3) | 19.1 s | 25 | 2.08, 24 % |
| cycle 3 | config sweep on cycle 2: qp_fr_margin 0.5 / 0.7, qp_om_x 0.4, qp_pd_scale 1, engage 0.45 | 8.2-11.4 s | 15.1 / 19.6 / 17.2 / 15.6 / 15.6 s | 17-42 | 1.37 (margin 0.5), 15 % |
(Cycle 1 took 3 compile-and-run iterations of one design: the first single-row reference launched badly, then the speed test counted touchdowns of one foot only. Raw logs: `step5/` summaries and two trimmed gait logs.)

## Cause per failed cycle
- Baseline: the periodic Fr 0.10 stride cannot be entered from standing. Node 0 is a heel strike at 6.45 m/s with the feet 25 m apart and a 28 m step; the QP starts at the first non-Stand tick, commands node-0 torques on a body at rest, then saturates the sagittal friction cone (commanded ratio 0.9) chasing a 6.4 m/s CoM velocity error. The legacy Shift phase lasts 5.5 s in parallel and its PD fights the QP.
- Cycle 1: with the imp 2 launch the mech reaches 3-4 m/s by ~11 s (a good start: height 42.3, yaw 1.5 deg). After the hand-off the CoM velocity error still pulls friction to the cap (frc 0.9 for ~2 s), the CoM then drifts laterally (x +16 m, vx up to 8 m/s) with shear up to 0.8-1.9 x mu fn, yaw -5 to -14 deg, and it falls forward at 15.5 s. Lateral shear and yaw are outside the sagittal QP (stage 2 of the plan, not built): the QP spends the whole friction budget sagittally and nothing is left for the legacy lateral law.
- Cycle 2: the warp removes the speed mismatch (QP engages at 1.9 m/s, step times stretch with s). The failure moves to lateral and yaw anyway: CoM stalls at z about 4 m for 6 s while x drifts 4 m and yaw goes to -20 deg; the legacy lateral law has no authority over the QP-loaded stance leg, then the slow single support collapses at 19 s.
- Cycle 3: margins that leave shear room (0.5) lower the realised ratio (p95 1.37) but not the falls; larger PD, lower om_x and margin 0.7 do not change the outcome. No trend toward stability.

## Metrics
No run is stable (every walk falls within 10-20 s, never 3 steady strides), so M-001@v1.1 and M-002@v1.1 were not computed (M-001.1 would be below the band by construction: steady Fr 0). Both stay FAIL / not evaluated for imp 3.

## Findings for the next round (not fixed here)
1. A single Fr 0.10 row cannot start a walk from rest; the QP needs reference rows at low Froude (or a start trajectory) so the hand-off or the time-warp has a matching stride. The time-warp is built (`qp_scale_min`, off by default) but a slowed stride is only as good as the lateral law beneath it.
2. The missing piece is lateral and yaw in the QP (3D tangential generators plus yaw momentum, the plan's stage 2): every failure shows shear 1-2 x mu fn from the lateral/yaw side while the sagittal part sits at 0.9.
3. Plan assumptions confirmed in closed loop: solver cost 0.04 ms, no solver failures, torque writes land (the imp 2 pipeline up to the hand-off is unchanged).

## Code changes (all behind imp >= 2.5; defaults keep behaviour; commit sha in the report)
`qp_engage_speed_frac`, `qp_scale_min`, `qp_scale_tau` (WalkTuning, default 0 / 1 / 1), `SagittalQpController.AnchorXoff/TimeScale`, `WholeBodyQpDriver.Reset(anchor)/TimeScale`, `LocomotionController` hand-off + second driver + QP log fields (`qp` block in gait_log, only when the QP runs: status, iterations, ms, failures, scale, mass scale, friction cmd, tau), `MechConfig.QpReferenceFor`.
