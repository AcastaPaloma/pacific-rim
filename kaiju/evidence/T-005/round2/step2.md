# T-005 round 2, step 2: stance-leg joint impedance (imp = 1) (evidence)

Branch `task/T-005`, on dd936b9. Not run in Unity (compiled and unit-tested engine-free only). `imp` stays 0 in walk_tuning.json; no change to mech_frame_balanced.json, thresholds or assist.

## What changed (all behind `imp > 0` and ref_track > 0, not stomping, ref speed > 0, single or double support; imp = 0 takes the untouched legacy call path)
- `LocomotionController.WriteDrives`: for each non-swinging leg the stance hip flexion (Hip[0]), knee and ankle pitch (Ankle[0]) are driven by joint PD toward `_ref.Sample(phase)` HipFlexion / Knee / AnklePlantar of that leg (L if Side < 0, else R). Phase is the one the swing code uses: (stance==left ? 0 : 0.5) + 0.5 min(1, time/stepT). The trailing leg in double support is sampled at the same phase, so it gets its late-stance (toe) reference.
- Gains: k = I w^2, c = 2 zeta I w with I = `DofInertia(d, stance)`, w = 2 pi hz; hz and zeta from config per joint and per sub-phase keyed on LegSample.Support (Heel = loading, Flat, Toe = heel rise/push-off; Swing treated as Flat). Pure logic in new `public static class StanceImpedance` (Gains, PdGains, CopRange), same file.
- Virtual force: on the three tracked DOFs the Jacobian-transpose torque (ground force moment + distal weight) is the feed-forward times `imp_vf_scale`; the trunk-attitude torque on the hip is unscaled. Hip internal rotation/abduction and ankle roll stay on the legacy force-loop law (frontal plane, CoP lateral).
- CoP clamp (`ClampToSoleImp`, used for the stance CoP and the trailing CoP in double support): Toe allows the toe line (full sole half length, no margin) ahead, Heel allows the heel line behind, Flat is the legacy margined sole. `_soleFullHalfLength` is new.
- Target is clamped to the DOF range; velocity feed-forward from the target's change, capped at `target_velocity_max_rps`.

## New config keys (walk_tuning.json and WalkTuning), defaults
`imp_vf_scale` 0.25. Hz: `imp_{hip,knee,ankle}_hz_{heel,flat,toe}` = hip 0.6/0.5/0.6, knee 0.6/0.5/0.5, ankle 0.3/0.5/0.8. Damping ratio: `imp_{hip,knee,ankle}_zeta_{heel,flat,toe}` all 1.0. Untuned first guesses; the orchestrator sets them per run.

## Tests
`DotnetTests/Units.Mech.Tests/StanceImpedanceTests.cs` (4 new, csproj gets the Compile line): imp default 0 and flat CoP range equals the legacy margined sole; gain lookup returns the configured values for Heel/Flat/Toe on all joints, PD formula; shipped gains positive; CoP range Toe reaches the toe line, Flat stays inside, Heel reaches the heel line.
`dotnet build` clean. `dotnet test`: 72 total, 66 pass, 6 fail = the known KnownGap reds (EveryJoint_HasFiveDegreesToSpare x5, YawMomentAskedOfTheStanceFoot..AtCruiseAndAbove(0.3)).

## Could not do / open
- Not exercised in Unity: the mapping reference angle -> DOF target assumes the DOF coordinate equals the anatomical angle (hip flexion, knee flexion, ankle plantarflexion), as the swing IK path and the arm code imply (IK component x unity_sign). Check sign on the first run (hip_L flexion sign -1, knee/ankle +1).
- With vf scale 0.25 and 0.5 Hz joint PD the body weight support relies on the first-guess gains; expect tuning.
- The nominal CoP path (cop_toe_frac) is unchanged; the clamp now permits the toe line but the planned CoP only goes as far as that config says.
