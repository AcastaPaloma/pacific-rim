# Physics.Body
Purpose: the articulated physical body (layer L1). Builds an ArticulationBody chain from a `mech_frame@v1` file and a `body_tuning@v1` file, exposes it to other modules only through `IArticulatedBody` in SI units, detects foot contact, publishes `FootContact` / `FootLiftOff` on the Core bus in the contact step, and writes `physics_log@v1`. It knows nothing about pilots, intent, gaits or units.

Breakdown IDs: F-001, F-009 (M0 part: the event), D-007, D-008, M-006, M-002 (contact definition). Research: R-005 (sections 1 to 4, 8), R-001 section 5. Task: T-005.

## Assemblies
| Assembly | Folder | Engine | Contents |
|---|---|---|---|
| `Game.Physics.Body.Contracts` | `Contracts/` | none | `MechFrameData` (+ segment, joint, DOF, sole) = `mech_frame@v1`; `MechFrameOps` (validate, R-005 1.4 scaling, mass sums, effective inertias); `BodyTuning` = `body_tuning@v1`; `IArticulatedBody`, `IBodyWorld`, `BodySpawn`, `LinkState`, `DofInfo`, `FootState`; events `FootContact`, `FootLiftOff`; `JsonLine` (log record writer); `BodyContractVersions` |
| `Game.Physics.Body` | `Runtime/` | none | `GainRules` (k = I w^2, d = 2 zeta I w), `ContactHysteresis` (R-001 F_on / F_off), `JointMath` (rotation vectors), `BodyEnergetics`, `PhysicsLogWriter` |
| `Game.Physics.Body.Unity` | `Unity/` | UnityEngine | `ArticulatedBody` (the only class that touches ArticulationBody), `BodyWorld` (contact routing, ground, scripted stepping, logs), `TruthScenes` (M-006 passive scenes) |
| `Game.Physics.Body.Editor` | `Editor/` | editor | `PhysicsProjectSettings` (TGS, enhanced determinism, improved patch friction), `TruthScenesCli`, `BodyProbe` (R-005 risk probes) |
| `Game.Physics.Body.Tests` / `.PlayTests` | `Tests/EditMode`, `Tests/PlayMode` | test | below |

References: Core contracts only. No reference to Control or Units (checked by `ArchitectureTests`).

## Contract (version 1)
- `IArticulatedBody`: link states, DOF list (anatomical signs, radians, N m), per-DOF drive (target, target velocity, stiffness, damping), per-DOF feed-forward torque, R-005 gain rule per DOF (`GetDofGains`, `DofInertia`), whole-body mass, CoM and CoM velocity, foot states, `AddExternalForce` (logged), counters. **There is no method that sets a pose or a velocity**: bodies move only by joint drives, joint torques, contacts, gravity and logged external forces.
- `IBodyWorld`: create and destroy bodies, start and stop `physics_log@v1`. A unit module creates its body here and never references this module's implementation.
- `FootContact { Unit, Foot, Step, ContactId, Position (CoP), Normal, VerticalImpulseNs, NormalForceN, PressurePa, ApproachVelocity }`: one per contact, published in the step in which the foot's summed normal force first reaches F_on = 0.03 m g. Its force and impulse are the values of that first step (small). `FootLoaded { Unit, Foot, Step, ContactId, PeakNormalForceN, ImpulseNs, WindowSeconds, PeakStep }` follows with the same ContactId when the loading window has passed (or the contact ended): the peak force and the impulse the contact really had. Effects that scale with how hard the foot came down use `FootLoaded`. `FootLiftOff` when it falls below F_off = 0.01 m g. Foot index and unit ID are data; the number of feet comes from the frame.
- Data specs: `mech_frame@v1` (unknown versions are refused), `body_tuning@v1`, `physics_log@v1`.

## What the engine really does (measured; `evidence/T-005/probes/`, test `SingleJoint_DriveUnitsAndSign_AreAsTheAdapterAssumes`)
- Drive stiffness is N m/rad, damping N m s/rad, target degrees, **targetVelocity degrees per second** (the scripting reference says rad/s), jointPosition radians; a positive value is a positive Unity rotation about the anchor axis.
- `jointForce` is N m, persists until rewritten, and for a spherical joint acts about the **child link's** joint axes. A spherical joint's coordinates are the **rotation vector** of child relative to parent.
- A link's reduced coordinates are its unlocked axes in the order twist, swing y, swing z, although `dofCount` reads 3 for a spherical joint with one axis locked.
- **PhysX mass limit, per solver.** Probe on a single ArticulationBody link resting on static ground (`P4m`): under **PGS** 1e5 kg rests and 1e6 kg and above get no contact impulse; under **TGS** (the solver in use) 1e6 kg rests (with 24 iterations) and 1e7 kg sinks. The 15-link mech in SI kilograms under TGS sinks through the ground (test `MassUnit_InKilogramsTheBodySinks_InTheConfiguredUnitItRests`). So bodies are simulated in a scaled mass unit, `body_tuning.sim_mass_unit_kg` (1e5 kg), declared once in the contract as `SimMassUnit` and exposed as `IBodyWorld.MassUnit`: **every module that creates a Rigidbody or ArticulationBody, or applies a force through the engine API, must convert through it** (`ToEngine` / `ToSi`). A body whose tuning names a different unit than its world is refused. Inside this module masses, inertias, torques and gains are divided by it in `ArticulatedBody` and nowhere else. The physics is unchanged (Newton's laws are invariant under a change of mass unit; g, lengths and times are untouched); every contract value and every log value is SI. Every body in the same PhysX scene must use the same unit: this is a project-wide fact for later modules (proposed Q in `tasks/T-005.impl.md`).
- Articulation links do not collide with each other (engine default), so feet can pass through each other: not handled at M0.
- `ContactPairPoint.impulse` holds the normal impulse only. The per-foot tangential force in the logs is an estimate: whole-body momentum balance, shared between loaded feet in proportion to normal force.
- Default damping (0.05 linear, 0.05 angular, 0.05 joint friction) is overwritten with the tuning's zeros and read back into the log header.

Consumes events: none. Emits events: `FootContact`, `FootLoaded`, `FootLiftOff`.
Joint-speed guard: `JointSpeedClampCount` and `DofSpeedClamped` report every step in which a joint moves at the engine's max joint velocity (R-005 R11); `physics_log@v1` writes them as `clamps` and `clamped`.
Config assets: `Config/Mech/mech_frame_balanced.json` (shared with T-007, not edited here), `Config/Mech/body_tuning.json`.
Feature flags (`Config/FeatureFlags/PhysicsBody.json`, default off): `physics.body.enabled` (a `BodyWorld` given a flag snapshot creates no body while it is off), `physics.body.physicsLog` (no physics log while off). Truth scenes and probes construct a world without a snapshot.

## Visual attach point (T-007)
Every segment is a GameObject named by its segment id (`pelvis`, `thigh_L`, ...) with its origin at the proximal joint and rest axes equal to the frame axes; `ArticulatedBody.PartTransform(segmentId)` returns it. A model whose parts are named by segment id attaches by parenting, with no code change. Primitive boxes (`visual_<id>`) stand in until then and are switched off with `BodySpawn.Visuals = false`.

## Tests
- Engine-free (Unity EditMode and `dotnet test DotnetTests/Physics.Body.Tests`): frame loads and validates, masses sum to the total, unknown schema refused, scaling rule, effective inertias against the R-005 table, gain rule, tuning scaling, contact hysteresis, rotation vectors, quaternion convention, JSON writer.
- PlayMode: single-joint drive units and sign (R-005 R1), body built from config with read-back, passive drop energy bound, one `FootContact` per contact in the contact step, the mass-unit limit.
- Metrics: M-006.1, .2, .3, .5 from `TruthScenesCli`; M-006.4, .6, .7, .8 from the unit's captures (`Tools/PhysicsMetrics/m006.py`).

Owner role: implementation/mech (`impl-mech`).

## Known limits / TODO
- Removing a part at runtime (R2 of the T-005 spec) is not built: destroying an ArticulationBody link makes Unity rebuild the articulation; needs a design in the Damage task.
- Only box colliders from the frame's envelope sizes. Hull density from collider boxes (877 kg/m3) is below the frame's hull density (1,232) because boxes ignore the fill factors.
- Self-collision is off (engine default).
- Mech independence: non-interacting bodies (standing mech, inert mech, a rigid mech landing elsewhere) leave a walking mech bit-identical; a second mech that steps does not (first difference while it is in single support, centimetres after 20 s). Cause not isolated: see T-005 evidence, C20.
