# Units.Mech art assets (T-007)

Art-side assets of the Units.Mech module. This is not the module's `MODULE.md` (that belongs to the body task, T-005);
it lists what T-007 put here, the IDs it serves and how to regenerate it. Nothing here is edited by hand.

IDs: F-001 (one rigid part per body segment, pivots at joint centres), D-025 (Balanced chassis of the shared frame),
D-007 / D-008 (80 m, physics-derived proportions), D-009 / D-022 (original design), D-011 (forearm hardpoints).

## Assets
| Path | What |
|---|---|
| `Models/mech_frame_balanced.fbx` | copy of `art/models/mech_frame_balanced/mech_frame_balanced.fbx` (LFS); 15 meshes + 8 socket empties |
| `Prefabs/MechFrameBalanced.prefab` | variant of the model: socket orientations + `MechArtRoot` (points at the part map). Meshes, sockets, mapping only: no colliders, no ArticulationBody (T-005) |
| `Data/MechFrameBalanced_PartMap.asset` | `MechPartMap`: 15 entries `segmentId → partName, path` (IDs spelled as in `mech_frame@v1`), 8 sockets, reference to the config |
| `Materials/mech_{primary,secondary,joint,accent}.mat` | URP Lit, flat colour by palette role from `Pipelines/blender/mech_frame_balanced/palette_roles.json` (R-002 entries) |
| `Runtime/` (`Game.Units.Mech.Art`) | `MechPartMap`, `MechArtRoot`, `MechFrameConfig` (reader for `mech_frame@v1`, refuses other schemas) |
| `Editor/` (`Game.Units.Mech.Art.Editor`) | `MechFrameImport` (import settings, materials, map, prefab; idempotent), `MechFrameCapture` (4-view capture) |
| `Tests/EditMode/` | `MechFrameBalancedArtTests` (10): mapping, prefab contents (15 parts + 8 sockets only), hierarchy, height, joints vs config, axes/scale, socket symmetry, forearm swap (independent mesh, hardpoint follows), Remap guard, budget |

Config (single source of dimensions, owned by the integration branch): `Assets/Game/Config/Mech/mech_frame_balanced.json`
(`mech_frame@v1`, verbatim copy of `research/R-005/mech_frame_balanced.json`). Tests read every reference value from it.

## Names
- Parts: `MechBalanced_<segment id>`: `pelvis, torso, head, upper_arm_L/R, forearm_L/R, hand_L/R, thigh_L/R, shank_L/R, foot_L/R`.
  Kinematic tree = the JSON `parent` field (pelvis → torso → head / upper arm → forearm → hand; pelvis → thigh → shank → foot).
  Every part origin is its proximal joint centre; all rotations identity, all scales 1, Y up, facing +Z, mech's left at −x.
- Sockets: `MechBalanced_socket_<kind>_<L|R>`, kind `shoulder` (on torso), `hip` (on pelvis), `elbow` (on upper arm, mount of
  the forearm), `hardpoint` (on the forearm's outer plate, D-011). Local +Z points the way a mounted part extends
  (shoulder, hardpoint: laterally outward; hip, elbow: down). L and R are exact mirrors.
- Swapping a limb: parent the new part to the socket's parent at the socket position, move the old part's children,
  then `MechPartMap.Remap(segmentId, newPath, newName)` (re-roots the paths below it). See the EditMode swap test.

## Regenerate
From `kaiju/` (Blender 5.3, kaiju env, Unity CLI; at most one Unity batch process per worktree):
```bash
BL=/Applications/Blender.app/Contents/MacOS/Blender; P=Pipelines/blender/mech_frame_balanced
$BL -b --factory-startup --python $P/build_greybox.py            # art/models/... .blend + .fbx
unity run "$PWD" --no-tail --log-file /tmp/mech_import.log --timeout 900 -- -executeMethod Game.Units.Mech.Art.Editor.MechFrameImport.Run
unity test "$PWD" --mode EditMode -o /tmp/editmode.xml           # MechFrameBalancedArtTests
unity run "$PWD" --no-tail --log-file /tmp/mech_capture.log --timeout 900 -- -executeMethod Game.Units.Mech.Art.Editor.MechFrameCapture.Run
/opt/miniconda3/envs/kaiju/bin/python $P/compose_unity.py      # evidence/T-007/unity_4view.png, unity_pixel_proxy.png
```
Running `MechFrameImport.Run` twice leaves `git diff` empty (evidence/T-007/unity_second_run_diff.txt).
Blender-side evidence and measurement: `Pipelines/blender/mech_frame_balanced/README.md`.

## Hand-off rule for the body task (T-005): arm and leg contacts
The greybox reaches every `mech_frame@v1` range without neighbour overlap above 2 % (`evidence/T-007/rom_report.json`),
but with the arms hanging at rest some joints driven at M0 push one limb into a non-neighbour part. Clearances at rest:
forearm/hand to thigh 0.45 m, feet 3.0 m apart. Contacts at R-005 range ends with all other joints at rest (depth m):
- waist lateral bend ±20°: forearm–thigh 2.9, hand–thigh 4.4; waist axial rotation ±30°: pelvis–forearm 0.2
- shoulder adduction −10°: forearm–thigh 3.7, hand–thigh 4.4, pelvis–forearm 0.2; shoulder external rotation −60°: forearm–thigh 2.2
- hip abduction +40°: hand–thigh 4.4, forearm–thigh 2.1; hip internal rotation −40°/+35°: hand–thigh 1.1 / 0.4
- hip adduction −25°: leg on leg (shank–shank 6.8); hip internal rotation +35°: foot–foot 1.0; wrist ulnar deviation −20° (locked at M0): hand–thigh 1.5

**Rule (proposed Q-NEW-b, Assumed until the founders or T-005 decide):** T-005 ignores collisions between
`upper_arm`/`forearm`/`hand` and `pelvis`/`thigh` of the same side, or limits shoulder adduction to ≥ 0° and couples shoulder
abduction to waist lateral bend plus hip abduction when the balance controller uses those strategies. Leg-on-leg contact at hip
adduction and internal rotation is real contact and stays enabled (feet 3 m apart by the JSON stance).
