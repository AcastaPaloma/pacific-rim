# Slice (M1 integration scene, T-039b)
Purpose: the first playable. `Scenes/M1Slice.unity` loads the 15-tile district (GG + SoMa, Golden Gate Bridge landmark colliders), the pixel camera and a sun, installs the game through `Game.Composition` with the slice flag set, and adds TEMPORARY stand-ins so the pieces can be felt together. Used by M-018 on a target PC, the founders' feel sign-off and the M-007 C5 panel.
Layer: top (references every module; nothing references it). Behind `slice.m1` (default off in `Config/FeatureFlags/Slice.json`).

## Flags and config
- `Config/FeatureFlags/Slice.json`: committed, `slice.m1 = false`; the committed config installs nothing new.
- `Config/slice_m1.flags.json` (slice-only, NOT merged into the committed flags): the flag set the scene passes to `GameComposition.Install`: `slice.m1`, `world.geo.enabled`, `destruction.enabled`, `seismic.enabled`, `kaiju.scriptedBrain`, camera and pixel flags. The bootstrap reads it from a serialized `TextAsset`, so a player build carries it. With `slice.m1` false in that file the scene loads nothing.

## Assemblies
| Assembly | Folder | Engine | Contents |
|---|---|---|---|
| `Game.Slice.Logic` | `Logic/` | none | `SliceKaijuAgent` (brain + 2D-harness kinematics), `SliceTarget`, `SliceImpact`, `SliceBuildingIndex`, `SliceQuakePlan`; dotnet `DotnetTests/Slice.Tests` |
| `Game.Slice` | `Runtime/` | UnityEngine | `SliceBootstrap` (load, install, actors, camera, HUD, scripted smoke) |
| `Game.Slice.Editor` | `Editor/` | Editor | `SliceEditorTools.BuildScene` / `BuildMac` |
| `Game.Slice.PlayMode.Tests` | `Tests/PlayMode/` | test | headless smoke through the scene |

## Flow (`SliceBootstrap.Start`)
1. Parse the slice flags; stop if `slice.m1` is off. 2. Switch the active pipeline to the pixel URP asset (catalog) when `rendering.pixel.enabled`. 3. Build the 15 tiles one per frame (terrain + landmark colliders; per-building colliders off), rebase the floating origin to the SoMa tile corner. 4. `GameComposition.Install(registry, sliceFlags, errors, inputs)` with `Faults` built from the tiles' faults layers and `DistrictRefs` of the two reference tiles; the built tiles are added to the installed `GeoWorldService`. 5. Register every structure with Destruction (`BuildingAttributesFromGeo`) and the shaking bridge. 6. Spawn the stand-ins, camera rig (`ThirdPersonRig`, texel snap) and pixel binder.
- Quake: key Q (or smoke frame 120): `SliceQuakePlan` picks the longest mapped fault system in the district, `QuakeDirector.Run` publishes the Seismic events, the bridge applies PGA damage; a `CameraShakeImpulse` shakes the camera.
- Impactor: key I (or smoke frame 240): a 1.5e6 kg sphere at 150 m/s flies at the building nearest ahead of the mech; on arrival `DamageModel.Apply(Impact)` with its kinetic energy. Buildings reaching state Complete are removed from the scene (3 per frame) through `IGeoWorld.RemoveBuilding`.
- Kaiju: `SliceKaijuAgent` with the brain from `IKaijuBrainFactory`, observation = the mech; it walks through buildings (kinematic, TEMPORARY) and melee-hits the mech (mech hp resets).
- Smoke: `-sliceSmoke N [-sliceSmokeOut file]` (player) or the PlayMode test runs 60 warm-up + N frames with a scripted mech loop, records frame ms and draw calls (ProfilerRecorder, needs a development build) and the destruction counts, writes JSON, quits the player.

## TEMPORARY / known limits
- Mech: a WASD capsule (`SliceTempMech`) until `Units.Placeholder` (T-030c) is merged and wired through the seat; kaiju: a capsule. Neither collides with buildings.
- No per-building colliders (cost); hits use the data index, not physics.
- Frame numbers are Apple-silicon relative numbers (M-018 relative on Mac), not target-PC.
