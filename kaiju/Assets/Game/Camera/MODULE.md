# Camera
Purpose: the swappable camera rig (D-006). `ThirdPersonRig` follows a target at mech scale, renders from a pose snapped to the pixel grid, offers the remainder to the pixel pipeline as stabilisation hints, and answers footfall-style impulse events from the Core bus. A cockpit rig can replace it later behind the same contract.

Breakdown IDs: D-006, F-009 (footfall camera coupling, camera side), M-004, M-011, GAME_BREAKDOWN §9 "snap the camera position to the texel grid and compensate for sub-pixel offset", MODULARITY §4.2, §3 layer L3. Research: R-002 §5, R-003 §7. Task: T-004.

## Assemblies (layer L3: depends on Core contracts and Rendering.Pixel contracts only)
| Assembly | Folder | Engine | Contents |
|---|---|---|---|
| `Game.Camera.Contracts` | `Contracts/` | none | `ICameraRig`, `ICameraTarget`, `CameraPose`, `CameraVec3`, `CameraShakeImpulse` (event), `CameraFlags`, `CameraContractVersions` |
| `Game.Camera.Maths` | `Maths/` | none | `CameraStabiliser` (+ `SnapState`, `StabiliserSettings`), `CameraLayerStabiliser` (+ `LayerSettings`, `LayerSnapState`), `CameraImpulse`, `RigFraming` |
| `Game.Camera` | `Runtime/` | UnityEngine | `ThirdPersonRig`, `ThirdPersonRigSettings`, `TransformCameraTarget` |
| `Game.Camera.Tests` | `Tests/EditMode/` | test | engine-free NUnit sources, also run by `dotnet test DotnetTests/Camera.Tests` |
| `Game.Camera.Unity.Tests` | `Tests/UnityOnly/` | test | rig tests with the engine and a real Core event bus |

Namespace is `Game.Cameras` (a namespace called `Game.Camera` would hide `UnityEngine.Camera` in every `Game.*` namespace).

## Contracts
- `ICameraRig` v1: `RigId`, `WantedPose`, `RenderedPose`, `SetTarget(ICameraTarget)`, `Cut()`.
- `ICameraTarget`: ground position and height of what the rig follows. The unit module implements it; the rig knows no unit types.
- Provides `IPixelStabilisationSource` (defined in `Rendering.Pixel.Contracts`): snapped pose, sub-texel remainder, dither origin, reference depth, cut flag. Reads `IPixelGridSource` from the same GameObject by interface.
- Consumes event `CameraShakeImpulse { EntityId Source; float OriginX, OriginY, OriginZ; float ImpulseNs; }` (version 1) on the Core bus. **Integration note:** the mech task's foot-contact event is not referenced here. An adapter in the composition layer should publish one `CameraShakeImpulse` per footfall with the contact point in world metres and the contact impulse in N·s.
- Emits events: none.

## Behaviour
- Follow: the focus is the target's ground position plus `focusHeightFraction` × height, approached with time constant `followLagSeconds`; the camera sits `distanceInTargetHeights` × height away, looking `pitchDeg` up (an 80 m target fills about half the frame height, horizon low). `SetOrbitYaw` turns the rig around the target. `SetScriptedPose` renders a given pose instead (capture sequences).
- Stabilisation (`CameraStabiliser`): yaw and pitch are quantised to whole texel angles (`camera.quantiseRotation`); the position steps in whole texels at the reference depth along the camera's right and up axes, starting from the previous snapped position (`camera.snapPosition`), so turning on the spot never moves the camera; the forward axis is continuous. The remainders (each within one texel in total) go to the present stage. `Cut()` restarts from the wanted pose: the first frame after a cut equals a static frame of that pose.
- Layer snap (T-181c, flag `rendering.pixel.layerSnap`, default off; `ConfigureLayers`): `CameraLayerStabiliser.TryStabilise` runs `Stabilise` once per layer (near = the rig's focus depth, main = `skylineDepthM` 664.874, far = `farDepthM` 10353; `SetScriptedLayerReferences` overrides main/far for captures) with its own `SnapState`, and fills hints v2: the v1 fields are the main layer's result bit for bit; near and far carry their own offset from it along the rendered right/up axes, own remainder and grid. A layer whose reference depth changes by more than 1e-4 (relative) restarts its lattice and reports `Cut` on that layer only. Unusable layering (snap off, depths not increasing, no grid) falls back to the v1 path at the rig's own depth (`LayerCount` 0). Slab cameras and the layered Present are T-181d.
- Impulse (`CameraImpulse`, `camera.footfallImpulse`): each event adds a decaying sine in height and pitch, proportional to the impulse and to falloff with distance, capped (`impulseMaxMetres`, `impulseMaxDegrees`), exactly zero after about 2.8 s for the default 1.2 s decay. Closed form: independent of the step size, reproducible, no allocation per event. The offset is applied to the wanted pose before stabilisation, so the image moves as a whole through the snap and the sub-pixel shift.

Config assets: `Assets/Game/Config/Camera/ThirdPersonRig.json` (schema 1: field of view, framing, follow lag, impulse envelope). Feature flags (`Assets/Game/Config/FeatureFlags/Camera.json`, all `false`): `camera.thirdPersonRig`, `camera.snapPosition`, `camera.quantiseRotation`, `camera.footfallImpulse`. `ThirdPersonRig.Bind(IServiceRegistry)` reads the flags, subscribes to the bus and registers the rig as `ICameraRig` and `IPixelStabilisationSource`; tools pass the values with `Configure`.

## Tests
- Engine-free: texel grid numbers of the capture protocol; cut equals static; lateral translation renders in whole-texel jumps and the remainder restores the motion; turning never moves the camera; forward motion is continuous; yaw steps and dither origin; stabilisers off leave the pose untouched; remainders stay inside one texel along a random path; quaternion agrees with the basis; impulse proportional, bounded, decays to exactly zero, reproducible, step-size independent, falls off with distance; architecture (no unit or mech reference, pixel only through contracts; the event is an unmanaged Core event).
- Unity EditMode: the rig frames an 80 m target at about half the frame height; following a 10 m/s target keeps it steady within one internal texel; no grid means no hints; cut; a `CameraShakeImpulse` on a real Core bus moves the camera in proportion, bounded, and framing returns; `Bind` through the registry.
- Metrics: M-004@v1 sequences, the cut check, the smoothness check and the 20 s orbit with one impulse are captured through this rig (`evidence/T-004/`).

Owner role: implementation/pixel (`impl-pixel`) for T-004.

## Known limits / TODO
- The snap locks one plane (the reference depth: the focus distance in follow mode). Other depths drift by parallax (R-003 §7); output smoothness is exact only on that plane.
- Impulse envelope and framing defaults are Assumed (feel panel). Rotation quantisation is behind a flag (Q-021).
- No cockpit rig yet (later module; a rig that returns no hints is already handled by the pipeline).
- No player input: orbit yaw is set by code. No collision or terrain avoidance.
- Positions are single-precision world coordinates; floating origin (F-042) will need `origin_offset` handling in the rig and the sidecar.
