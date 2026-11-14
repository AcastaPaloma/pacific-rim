# T-005 round 2, step 4: inverse-dynamics feed-forward (imp = 1) (evidence)

Branch `task/T-005`, on 63f0450. Not run in Unity. `imp` stays 0 in walk_tuning.json; imp = 0 takes the untouched legacy calls (Track gets `ffQdd = null`, StanceDof's tracked branch is imp-only). No change to mech_frame_balanced.json, thresholds, Physics.Body or assist.

## API basis
No new engine call. The body already exposes `DofGravityTorque`, `DofCoriolisTorque`, `DofTorqueForAcceleration`, built on ArticulationBody GetJointGravityForces / GetJointCoriolisCentrifugalForces / GetJointForcesForAcceleration, verified against 6000.6.9f1 (comment above those methods in ArticulatedBody.cs). Engine facts: the acceleration torque ignores gravity and drives; contact forces are not available. Sign: the body already applies the anatomical DOF sign inside these methods, and the stance path uses the anatomical angle as the DOF target (step 2 mapping, unchanged), so qdd_ref is anatomical.

## What changed (imp > 0 path only)
tau_ff = imp_ff_gravity * share * DofGravityTorque + imp_ff_coriolis * DofCoriolisTorque + imp_ff_accel * DofTorqueForAcceleration(dof, qdd_ref), clamped to the DOF's force limit (`InverseDynamicsFeedForward.Torque`, pure, in LocomotionController.cs).
- DOFs: stance hip flexion, knee, ankle pitch (added to the step-2 force-loop ff); swing leg (all hip/ankle axes + knee, pitch DOFs get the reference leg's qdd, roll/yaw DOFs 0); waist yaw; both shoulder flexions.
- share = imp_ff_stance_gravity_share on stance-leg DOFs only (1.0 = full; contact carries part of the weight, so values below 1 are the knob; first guess 1.0, no tuning). Swing, waist, arms use share 1.
- On swing/waist/arm DOFs (Track) the imp ff replaces the legacy distal-weight term and the differenced-target term `swing_ff_gain * I * ffAccel` (no contact there; avoids counting gravity twice and differencing the targets). On stance DOFs the step-2 force-loop torque (x imp_vf_scale) stays and the inverse dynamics is added.
- qdd_ref: central second difference in phase of the reference gait's SAMPLE (phase -h, 0, +h, h = 1/128 stride, stride = 2 StepTime), i.e. the model trajectory, not the drive targets, not a difference of commanded targets. Sampled once per WriteDrives call.
- Clamp: ff is limited to +-ForceLimitNm. ArticulatedBody additionally clamps ff to that limit and cuts the PD drive's force limit to (limit - |ff|) (ArticulatedBody.cs ~310-317), so PD + ff never exceeds the limit; nothing raised.

## Keys (WalkTuning defaults and walk_tuning.json)
imp_ff_gravity 1.0, imp_ff_coriolis 1.0, imp_ff_accel 1.0, imp_ff_stance_gravity_share 1.0.

## Tests
`DotnetTests/Units.Mech.Tests/FeedForwardTests.cs` (4 new): sum with gains and stance share; zero gains give zero and shipped defaults are 1; clamp at the limit both signs; second difference of a sinusoid equals -w^2 q. Pure helper used instead of a fake body (the interface is large).
`dotnet build` clean. `dotnet test`: 83 total, 77 pass, 6 fail = the known KnownGap reds. One fix en route: ArchitectureTests forbids a 0.01 literal in controller code (now 1.0 / 128).

## Open
- Stance ff double counts weight partly: the step-2 SupportTorque (distal weight, x imp_vf_scale 0.25) plus engine gravity x share 1.0. Watch the first run; the share or vf scale is the knob.
- Swing qdd_ref is the reference leg's, while the swing target comes from the foot-path IK; they differ, so the acceleration term is approximate there.
- Reference sample has discontinuities (support changes, heel-rise), so qdd_ref can spike at those phases; the force-limit clamp bounds it. Not exercised in Unity.
