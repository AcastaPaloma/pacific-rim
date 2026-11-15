# T-005 round 2, step 1: corrected reference gait and stance posture (evidence)

Branch `task/T-005`, built on WIP d8b6e57. No Unity run. No change to `mech_frame_balanced.json`, thresholds or assist.

## What changed
- `ReferenceGait.cs` (imp = 1 path only): the stance is now the R-008 section 1 whole-stance schedule, heel strike pitch 25 deg (23.7 at Fr 0.10), foot flat, heel rise at 0.65-0.72 of stance, toe-off pitch 10.8-20.4 deg, pelvic tilt 10 deg, pelvis yaw 8 deg, hip height from the schedule's two Fourier terms. The five-row table (Fr 0.10 to 0.20, linear between, end rows held outside) is embedded in the class and comes from `research/R-008/model.py` re-solved here (leg 42 m, duty 0.63, knee >= 5.6 deg design value, LIPM and hip excursion checked on a 721-point grid, foot flat >= 76 %, LIPM <= 0.092 g, tilt fixed 10 deg): the coarse 61-point grid of the original lets the single-support LIPM peak (at the start of single support) through.
  - Swing: ankle angle runs smoothly from its toe-off value to its heel-strike value (convex, so inside the range), foot pitch follows from the shank, ankle height rides up for sole clearance at that pitch, reach guard keeps the knee >= 5.3 deg.
  - Waist: declared trunk share k = `ref_waist_ff` (0.90) cancels that share of the whole-body vertical angular momentum over eight stride harmonics (yaw.py), upper-body inertia = torso + head + both arms about the waist axis (new `GaitGeometry.UpperYawInertia`, `PelvisYawInertia`).
  - New `YawMomentProfile(n, harmonics)` (low-passed free moment, what a drive follows; raw `YawMoment` spikes at the foot's velocity jumps at heel strike and toe-off).
- `walk_tuning.json`: `ref_waist_ff` 0.7 -> 0.9 and `ref_waist_max_deg` 20 -> 25 (these also feed the legacy imp = 0 waist table: flag for the controller work), `imp_knee_min_deg` 6 -> 5, `imp_ankle_margin_deg` 6 -> 5.5. `imp` stays 0 (the controller is unchanged).
- `StancePosture.cs` is unchanged and still compiles (state-indexed form, kept for the controller's impedance targets in later steps; the time-indexed reference no longer calls it).
- WIP compiled as committed (the build was clean on the first run); no WIP fix was needed.

## M-NEW-c, whole cycle (4800 samples kinematics, 480 yaw profile), `ReferenceValidityTests`
Limits: joint margin >= 5 deg (hip, knee, ankle, whole cycle both legs), hip excursion <= 0.05 l = 2.10 m, single-support LIPM <= 0.1 g (0.981 m/s2) with the CoP in the active support (heel point, sole, toe line; CoM = pelvis centre, height hip + 1.5 m), foot flat >= 75 % of single support, free-moment utilisation <= 0.67 against 1.65e8 (flat) / 0.85e8 (heel or toe line) N m, stance knee >= 5 deg.
Asserted at Fr 0.10 and 0.15; the other rows are reported only. Columns: Fr | joint margin (binding joint, leg@phase) | hip excursion m | LIPM g (at phase, support) | foot flat % | yaw utilisation (peak N m of budget, at phase, support) | trunk share / waist peak deg / waist torque N m | min stance knee deg.

| Fr | joint margin deg | hip exc. m | LIPM g | flat % | yaw util. | share / waist / torque | knee |
|---|---|---|---|---|---|---|---|
| 0.050 | -6.18 (ankle, L@0.013) | 1.87 | 0.114 (0.456 Toe) | 76.0 | 0.113 (9.62E+006 of 8.49E+007, 0.174 Heel) | 0.90 / 8.3 / 9.08E+007 | 10.1 |
| 0.075 | 1.41 (ankle, L@0.018) | 1.87 | 0.088 (0.456 Toe) | 76.0 | 0.131 (1.11E+007 of 8.49E+007, 0.174 Heel) | 0.90 / 10.2 / 1.37E+008 | 8.8 |
| 0.100 | 5.30 (knee, R@0.428) | 1.87 | 0.092 (0.130 Heel) | 76.0 | 0.140 (2.31E+007 of 1.65E+008, 0.224 Flat) | 0.90 / 11.0 / 2.08E+008 | 5.6 |
| 0.125 | 5.30 (knee, R@0.131) | 1.91 | 0.092 (0.130 Heel) | 76.0 | 0.160 (2.64E+007 of 1.65E+008, 0.220 Flat) | 0.90 / 12.7 / 2.37E+008 | 5.6 |
| 0.150 | 5.30 (knee, R@0.131) | 1.75 | 0.081 (0.130 Heel) | 76.0 | 0.179 (2.95E+007 of 1.65E+008, 0.210 Flat) | 0.90 / 14.6 / 2.66E+008 | 5.6 |
| 0.175 | 4.25 (hip, R@0.141) | 1.83 | 0.089 (0.130 Heel) | 76.0 | 0.278 (4.58E+007 of 1.65E+008, 0.189 Flat) | 0.90 / 17.4 / 4.12E+008 | 5.6 |
| 0.200 | 0.62 (hip, R@0.168) | 1.67 | 0.089 (0.130 Flat) | 76.0 | 0.288 (4.75E+007 of 1.65E+008, 0.170 Flat) | 0.90 / 18.1 / 4.28E+008 | 5.6 |

Binding check at Fr 0.10: joint margin 5.30 deg (knee, the 5.3 deg reach guard in swing; the stance knee is 5.6) against 5.0, 0.30 deg to spare; LIPM 0.092 g against 0.1 (92 %). At Fr 0.15: joint margin 5.30 deg (same), LIPM 0.081 g. Yaw utilisation 0.14 and 0.18 against 0.67.
Not met above Fr 0.15 (reported, not asserted): Fr 0.175 joint margin 4.25 deg (hip), Fr 0.20 0.62 deg (hip extension in swing; the stance margin at 0.20 is 4.8 deg by the model, below 5 as R-008 says it is "just" met), and Fr 0.05 / 0.075 (rows below 0.10 hold the 0.10 row, ankle margin -6.2 / 1.4 deg). The waist torque stays under the 5e8 N m limit up to 0.20 (4.3e8).

## Tests
- `DotnetTests/Units.Mech.Tests/ReferenceValidityTests.cs` (new, 13 tests: 6 checks x 2 Froudes asserted + the report test), all pass.
- Whole project: 68 tests, 62 pass, 6 fail. The six are the existing tests marked KnownGap on the legacy (imp = 0) reference, red on purpose and unchanged in kind: `EveryJoint_HasFiveDegreesToSpare` (5 Froudes) and `YawMomentAskedOfTheStanceFoot_..._AtCruiseAndAbove(0.3)`. Before this step 7 failed: the same six plus `..._AtCruiseAndAbove(0.2)`, now passing because the waist share changed.

## Commands
```
cd kaiju
dotnet build DotnetTests/Units.Mech.Tests
dotnet test DotnetTests/Units.Mech.Tests
dotnet test DotnetTests/Units.Mech.Tests --filter "FullyQualifiedName~ReferenceValidityTests" --logger "console;verbosity=detailed"   # prints the table rows
/opt/miniconda3/envs/kaiju/bin/python research/R-008/model.py   # formula reference; the table was regenerated from a copy with a finer grid
```
