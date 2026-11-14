# T-005 round 2, step 3: trunk and arm yaw compensation + yaw budget log (imp = 1) (evidence)

Branch `task/T-005`, on 9f0c6f9. Not run in Unity (compiled and unit-tested engine-free only). `imp` stays 0 in walk_tuning.json. No change to mech_frame_balanced.json, thresholds or assist. imp = 0 takes the same calls as before (Track gets impHz = 0, which leaves its gain code untouched).

## What changed
- `LocomotionController.WriteDrives`, upper body, only when `ImpActive` (imp > 0, tracking, not stomping, walking support phases): trackUpper = 1 regardless of ref_track / ref_track_upper. Waist yaw follows -(WaistYaw + PelvisYaw) fully; both shoulder flexions follow ArmFlexionL/R fully (velocity feed-forward on). Gains k = I w^2, c = 2 zeta I w on the distal inertia `DofInertia(d,false)`. `Track` got optional `impHz`, `impZeta`.
- New keys (WalkTuning defaults and walk_tuning.json): `imp_waist_hz` 0.8, `imp_waist_zeta` 1.0, `imp_arm_hz` 0.6, `imp_arm_zeta` 1.0.
- gait_log step records get four optional top-level fields (always written, null when unavailable; the writer maps NaN to null): `yaw_moment_nm`, `yaw_budget_nm`, `yaw_util`, `trunk_share`. They are filled for imp = 0 too (logging only). No gait_log schema/validator file exists under Assets/Game/Units/Mech or Tools (grep "gait_log"); Tools/PhysicsMetrics/plots.py and m006.py read the log and were not touched, so nothing to update. The `Debug` struct (in LocomotionController, not MechContracts) carries the values.
- Pure helpers `YawCompensation` (link yaw momentum, grip budget, util) and `YawShareWindow` (share estimator), same file as StanceImpedance.

## How the logged quantities are computed (body contract has no contact torque)
- Vertical angular momentum L about the CoM = sum over links of m (r x v)_y + (R diag(I) R^T w)_y, from LinkState (Com, Velocity, AngularVelocity, Rotation), LinkMassKg and the frame's principal inertias (assumption: principal axes along the link axes).
- `yaw_moment_nm` = minus the backward-difference rate of L, low-passed with `yaw_filter` (0.2 per step) because the raw rate spikes at heel strike and toe-off.
- `yaw_budget_nm` = sum over both feet of the existing yawGrip formula: friction_dynamic / friction_safety x filtered load x yaw_grip_radius_frac x leg length. It follows the measured load, so it is the same for flat and edge support; the reference model numbers are 1.65e8 (flat) and 0.85e8 N m (heel or toe line) = yaw_budget_flat_wl / yaw_budget_edge_wl x W x l. Compare the log against both.
- `yaw_util` = |moment| / budget (null if the budget is 0).
- `trunk_share` = -sum(U L_lower) / sum(L_lower^2) over the last stride (2 x step time samples). U = links below the waist yaw DOF (torso, head, arms); lower = all other links (legs plus pelvis). Null when not walking or before one stride of samples exists.

## Tests
`DotnetTests/Units.Mech.Tests/YawCompensationTests.cs` (7 new, csproj Compile line added): share pure cancel 1.0, half 0.5, null until window full or with still legs, last-window only; util and grip budget; link momentum (spin, tipped link, orbit); shipped config gains and imp = 0.
`dotnet build` clean. `dotnet test`: 79 total, 73 pass, 6 fail = the known KnownGap reds (EveryJoint_HasFiveDegreesToSpare x5, YawMoment..AtCruiseAndAbove(0.3)). One transient fail was fixed: ArchitectureTests forbids a 8.0 literal in controller code.

## Open
- Not run in Unity: signs of the arm and waist targets (waist negated as before, arms passed as-is) need a check on the first run, as does whether 0.8 / 0.6 Hz suffice at the waist (torque limit 5e8, reference needs 2.08e8 at Fr 0.10).
- The moment is the whole-body vertical momentum rate, not the foot's free moment alone: horizontal-force lever arms about the CoM are not subtracted.
- The share window resets when the stride changes the sample count only by shortfall (null until it refills); pelvis counts on the leg side.
