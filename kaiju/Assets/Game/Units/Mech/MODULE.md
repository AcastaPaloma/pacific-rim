# Units.Mech
Purpose: the mech assembly (layer L3): frame + body + pilot seat + locomotion controller, one instance per mech. Implements the M0 part of locomotion (stand, start, walk, turn, stop, emergency stop, stomp, capture step, fall under physics) with the R-005 section 5 control loop, driven only by `IntentFrame`.

Breakdown IDs: F-002, F-003 (M0 part), F-009 (consumer side: footfall probe), D-013, D-003, D-004, M-001, M-002. Research: R-005 section 5, R-001. Task: T-005.

## Assemblies
| Assembly | Folder | Engine | Contents |
|---|---|---|---|
| `Game.Units.Mech.Contracts` | `Contracts/` | none | `IMechUnit`, `LocoPhase`, `LocoCommand`, `LimitCause`, events `MechFallStarted`, `MechFallEnded`, `IntentLimited`, `WalkTuning` = `walk_tuning@v1` |
| `Game.Units.Mech` | `Runtime/` | none | `Lipm` (capture point / DCM mathematics), `LegIk`, `MechLimits`, `IntentLimiter`, `ReferenceGait` + `GaitGeometry` (R-007 whole-cycle reference pattern), `LocomotionController`, `GaitLogWriter` (`gait_log@v1`) |
| `Game.Units.Mech.Unity` | `Unity/` | UnityEngine | `MechConfig` (the three config files, scaling by data), `MechUnit` |
| `Game.Units.Mech.Scenarios` | `Scenarios/` | UnityEngine | composition root for headless runs and tests: `MechSimulation`, `ScenarioLibrary`, `ScenarioRunner`, `StripCapture` |
| `Game.Units.Mech.Editor` | `Editor/` | editor | `ScenarioCli` |
| `Game.Units.Mech.Tests` / `.PlayTests` | `Tests/` | test | below |

The runtime assemblies reference other modules only through `*.Contracts` (`ArchitectureTests`). `Scenarios`, `Editor` and the test assemblies are composition roots and may construct implementations; `Scenarios` is compiled only where tests are (`UNITY_INCLUDE_TESTS`), never into a player. The locomotion controller is a biped controller (`MechContractVersions.BipedFeet` = 2). Every tuning number of the controller and the limiter is in `walk_tuning.json` (a test scans the two files for literals).

## How the mech moves (all of it through `IArticulatedBody` drives and joint torques)
1. **Intent to command** (`IntentLimiter`): speed limited to the Froude band top and to what the step reach allows, ramped by the start / stop / emergency-stop accelerations, heading by yaw rate, yaw acceleration and lateral acceleration, a large heading change by slowing, turning on the spot, then walking, a walk target by the stopping-distance rule. Every difference between intent and command is a `LimitCause`, logged per step and published as `IntentLimited`.
2. **State**: whole-body CoM and velocity from the links, DCM xi = x + v / w0.
3. **State machine**: Stand, Shift (weight shift onto the first stance foot), SingleSupport, DoubleSupport, Falling, Fallen. Swing ends on the footfall contact, not on a timer.
4. **Foot placement**: capture-point law with steady-gait offsets; the stance CoP is steered (deadbeat on the DCM at the end of the step) inside the sole with a 10 % margin; the swing target is the predicted DCM minus the offsets, clamped to the reachable region.
5. **Stance legs**: virtual force on the body (pendulum force through the commanded CoP plus a height spring-damper on the pelvis) mapped to hip, knee and ankle torques by tau = -axis . (r x F), with the weight of the distal links added (gravity feed-forward); the ankle torque uses the measured load so the CoP lands where commanded; trunk attitude through the supporting hips (stance hip = -trunk torque - swing hip torque), yaw torque limited to what the loaded foot holds by friction.
6. **Swing leg and upper body**: PD joint drives toward inverse-kinematics targets with the R-005 gains (from `IArticulatedBody.GetDofGains`), velocity feed-forward and gravity feed-forward. Arms counter-swing the opposite leg.
7. **Reference gait** (`ReferenceGait`, fix cycle 3): an engine-free generator of one coordinated stride (heel strike, rollover, push-off pose, pre-swing knee, swing with retraction and bounded touchdown speed, pelvic rotation, arm swing, trunk yaw against the limbs' angular momentum) from speed, frame geometry and the `ref_*` values. The controller tracks parts of it through joint drives, weighted by `ref_track`, `ref_track_swing`, `ref_track_pitch`, `ref_track_upper`, `ref_height_blend`; shipped: arms and trunk at weight 0.5, the rest off (they made the walk worse: `evidence/T-005/progress_cycle3.md`). It is valid (joint ranges, friction budget) up to Froude 0.10; `ReferenceGaitTests` state where it is not.
8. **Fall**: when the trunk tilts past the limit, the hips drop too low or a non-foot link touches the ground, every drive goes limp (stiffness 0, a declared fraction of critical damping, no feed-forward); `MechFallStarted`, then `MechFallEnded` at rest. There is no recovery force and no get-up (later task; the hook is the `Fallen` phase).

Hook points for later tasks: `MoveId` values and a branch in `LocomotionController.StateMachine` (brace, get-up); `IArticulatedBody.AddExternalForce` (wading drag, thrusters: logged as external work); `LocoPhase.Fallen` (damage, get-up).

Consumes events: `FootContact`, `FootLiftOff` (logging and the footfall probe). Emits events: `MechFallStarted`, `MechFallEnded`, `IntentLimited`.
Config assets: `Config/Mech/walk_tuning.json` (dimensionless: leg lengths, t* = sqrt(l/g), g, body weights), `Config/Mech/body_tuning.json`, `Config/Mech/mech_frame_balanced.json`.
Feature flags (`Config/FeatureFlags/UnitsMech.json`, default off): `units.mech.enabled` (`MechUnit` refuses to be constructed while it is off), `units.mech.gaitLog` (no gait log while off). Headless runs and tests switch them on in their own snapshot (`MechSimulation.RunFlags`). There is no assistance flag because there is no assistance code. Gait options that exist but are off in `walk_tuning.json`: `vault` (near-straight stance knee with hip height from leg geometry, R-007 change 1) and `swing_toe_up_deg` / `heel_strike_deg`: both made the walk fall in fix cycle 2.

## Headless runs
`unity run <project> --no-tail --log-file <log> -- -executeMethod Game.Units.Mech.Editor.ScenarioCli.Run -scenarios <a,b|all> -out <dir> [-strips] [-repeat n] [-height m -mass kg] [-variants file]`
Scenarios (intent timelines only): `stand`, `walk_fr010/020/025/030`, `start`, `stop`, `estop`, `turn90_inplace`, `curve`, `walk_scn`, `stomp`, `intent_over_limit`, `shove_005/015/030/060` (the only ones with an external force), `repeat60`, `single_stance`. Output: `gait/<capture>.gait.jsonl`, `physics/<capture>.physics.jsonl`, `strips/<capture>.side.png` and `.quarter.png`, `summary.txt`.

## Tests
- Engine-free (EditMode and `dotnet test DotnetTests/Units.Mech.Tests`): the reference gait on its own (cadence, no ground penetration, stationary support point, continuity, touchdown speed, joint ranges, yaw moment; 7 tests in category `KnownGap` are red on purpose); DCM closed forms against numerical integration, deadbeat CoP, steady-gait offsets, Alexander step time, leg IK, limits from R-001, over-commanded speed, instant reversal, 30 Hz jitter, stopping-distance rule, emergency stop, architecture (references, engine-free assemblies, flags off, module docs).
- PlayMode: stand, walk from intent only with zero external work and footfall events in the contact step, 1 / 2 / 4 mechs, arbiter swap with 2 and 3 pilots, large shove ends in a fall with events, mass and height corners load and stand. Two tests in category `KnownFailing` hold the C20 one-centimetre independence check, which is not met.

Owner role: implementation/mech (`impl-mech`).

## Known limits / TODO (state at T-005 hand-off; values in `evidence/T-005/EVIDENCE.md`)
- **The walk is stable only at about 3 m/s (Froude about 0.02), with steps of about 10 m. The M-001 Froude band (0.10 to 0.30) is not reached.** Longer steps (above about 0.3 leg lengths) fall within 20 to 45 s, in fix cycle 2 as in cycle 1. Diagnosis in `tasks/T-005.impl.md`.
- A 180 degree turn on the spot ends in a fall (`intent_over_limit`); 90 degrees and curve walking work.
- Planted feet slide (p95 0.5 m per contact); touchdown peaks reach 4 body weights; the first step from standing draws 2 GW.
- The stance leg does not track the reference (force control only): this is what keeps steps at about 0.25 leg lengths. See `evidence/T-005/EVIDENCE.md` section 0.10.
- Contact chatter: some touchdowns produce two `contact_begin` events within a few steps.
- The biped controller needs exactly two legs; other body plans need their own controller.
- `height_hz` is 0.5 Hz where R-005 suggests 0.3 Hz (0.3 Hz was less stable).
