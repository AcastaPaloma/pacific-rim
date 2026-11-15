# T-005 round 3 · step 8.5: imp = 2 tracks the optimised stride (code only, not run in Unity)

## What imp = 2 does
- `MechConfig.GaitReferenceFor()` (MechUnit.cs) loads and validates `Config/Mech/gait_reference_opt.json` only when `Walk.imp >= 1.5` (throws if missing or invalid); `MechUnit` hands it to `LocomotionController(..., gaitReference)`, which throws if imp >= 1.5 and none is given (no silent fall back to imp 1). Shipped config keeps imp = 0.
- Step timing, length, stance/swing angles, foot pitch and ankle path come from the data through `ReferenceGait` (8.4a). The imp = 1 machinery (joint PD around the reference angles, swing IK to the reference foot path, DCM/capture-point placement, waist/arm yaw compensation) is reused unchanged.
- Stance hip/knee/ankle: PD to the optimised angle (imp = 1 gains), velocity target = optimised joint rate, feed-forward = imp2_ff_scale x sign x optimised torque (clamped to the DOF force limit) + the trunk-attitude/yaw hip term. The virtual-force correction is scaled by `imp2_vf_scale` (default 0: the optimised torque already carries the load; set 1 to add it back). Inverse-dynamics FF is not used at imp 2.
- Swing hip/knee/ankle (pitch): imp = 1 PD to IK targets of the reference foot path, feed-forward replaced by the same scaled, signed optimised torque. No external force or torque is applied.
- imp 0 / 1 paths are not edited: all new code is behind `_o2` (imp 2 and data present) or the new constructor argument (default null).

## Keys (walk_tuning.json, WalkTuning)
`imp2_ff_scale` 1.0, `imp2_vf_scale` 0.0, `imp2_ff_sign_hip` / `_knee` / `_ankle` 1.0. Variants: `scripts/variants85.txt` (imp2, imp2_ff05, imp2_ff0, imp2_ankle_flip). Scenarios walk_fr010 / walk_fr015 / walk_fr020 exist already.

## Sign mapping and confidence
Optimiser angles are read as identical to ReferenceGait HipFlexion / Knee / AnklePlantar, which the controller already uses as DOF coordinates (stance targets without DOF Sign), so a torque conjugate to those angles maps with sign +1. Confidence: hip and knee medium-high; ankle medium (action/reaction convention of the foot torque unverified), hence the flip variant on the ankle. Torques are not rescaled for other body sizes (reference mech 42.4 m).

## Logging
`Debug.TauFfSt` / `TauFfSw` (hip, knee, ankle, N m) are filled at imp 2; `GaitLogWriter` is outside this step's write list and not changed, so tau_ff is not yet written to gait_log (fr_ref, st_ref/st_meas, sw_ref/sw_meas are logged as at imp 1).

## dotnet
`dotnet test DotnetTests/Units.Mech.Tests`: 92 pass / 7 fail of 99 (before 89 / 7). The 7 reds are the same KnownGap tests. 3 new tests (Imp2ConfigTests): imp 0 and imp2 keys shipped, reference file valid, scenarios present. LocomotionController compiles in dotnet; MechUnit.cs (Unity-only) was not compiled.
