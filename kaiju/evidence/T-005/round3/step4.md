# T-005 round 3 · step 8.4a: optimised reference as data, read at imp = 2

Files: `Tools/GaitOpt/export_reference.py` writes `Assets/Game/Config/Mech/gait_reference_opt.json` (schema `gait_reference_opt@v1`, 195 kB) from `stride_fr010_lat`, `stride_fr015_lat2`, `stride_fr020_lat2` (solver commit 7d79db8, all FEASIBLE). C#: `Runtime/GaitReferenceData.cs` (serialisable data classes + `Validate`), `Runtime/OptimisedReference.cs` (Froude blend and phase sampler), `ReferenceGait.cs` (imp = 2 branch). Test: `DotnetTests/Units.Mech.Tests/OptimisedReferenceTests.cs`.

## Format
`source` (solver commit, files, verdicts), `conventions` (text), `rows[]` sorted by Fr. Row: fr, step_time_s, step_length_m, v, leg_length_m 42.4, duty 0.63, schedule {k_ds, k_ff, k_hr}, 40 `nodes`. Node: phase = k/40 over one step, pelvis x (absolute and in the mean-speed frame), z, pitch; `stance` and `swing` leg: hip/knee/ankle deg and deg/s, foot pitch, ankle x and height, joint torques (N m), heel/toe Fn and Ft (N), heel/toe contact flags. Stance = solver leg B (heel strike at node 0, toe at node N); swing = leg A (toe rocker to k_ds, then swing). Node N = node 0 with the two legs swapped.

## Loading and use
Same library as walk_tuning.json (`Repo.Load<T>`: JsonUtility in Unity, System.Text.Json with fields in dotnet). `new ReferenceGait(geo, tuning, g, data)`; with imp >= 1.5, data present and speed > 0, `Configure` blends the rows linearly in Froude (clamped 0.10..0.20) and `Sample` interpolates linearly in phase. StepTime and StepLength come from the data, scaled by Froude similarity if the leg length is not 42.4 m. Left leg is stance for stride phase 0..0.5. `TryOptimised(phase, out OptimisedSample)` gives joint rates, feed-forward torques, heel/toe forces and contact flags per leg for 8.5. `MechConfig` does not yet load the file (8.5 wires it).
imp 0/1 code paths are untouched: one `_opt = null` and one `if (_opt != null)` were added at the top of Configure and Sample.

## Conventions (as the optimiser defines them)
hip = a_thigh + trunk lean (flexion +), knee = a_thigh - a_shank (flexion +), ankle = a_shank + foot pitch (plantarflexion +, toes down); torque is conjugate to these angles (power = tau x rate). These are read as identical to ReferenceGait's HipFlexion, Knee, AnklePlantar, FootPitch. NOT verified against the Unity joint axes and sign of torque: 8.5 does that.

## Tests
`dotnet test DotnetTests/Units.Mech.Tests`: 87 pass / 7 fail of 94 (before: 77 / 84). The 7 reds are the same KnownGap tests (5 joint-range, 2 yaw budget). 10 new tests all pass: file contents, sorted rows, periodic wrap, rates equal derivative of angles, mean vertical force = body weight (2 %), exact reproduction of every node of the 3 rows (angles, torques, forces, flags), linear in Fr and clamped, linear in phase and wrap, imp 0/1 outputs equal with and without data and after imp-2 use (shared state, tuning not mutated), scaling.

## Open risks
- Planar model: no pelvis yaw, sway, waist or arm motion at imp 2 (those GaitSample fields are 0).
- Angle/torque sign mapping to the Unity joints is unverified.
- Forces and torques are not rescaled for other body sizes; the Fr 0.10..0.20 data are one mech (leg 42.4 m).
- Contact flags are step-wise (from the nearer row); the nodes are 0.1 s apart at a 4 s step.
- 0.125 was not solved; the blend between 0.10 and 0.15 is linear, not optimal.
