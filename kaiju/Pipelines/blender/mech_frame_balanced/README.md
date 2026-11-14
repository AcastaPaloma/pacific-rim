# Balanced mech greybox pipeline (T-007)

Scripted greybox of the Balanced chassis (D-025): 15 rigid parts, one per `mech_frame@v1` segment, built from
`research/R-005/mech_frame_balanced.json` (the single source of dimensions). Everything here is re-runnable and
deterministic; nothing is modelled by hand.

| File | Runs in | Does |
|---|---|---|
| `mf_common.py` | Blender + Python | paths, frame↔Blender axis mapping, part / socket naming, socket table |
| `palette_roles.json` | both | the four flat material colours by palette role (entries of `research/R-002/palettes.json`) |
| `build_greybox.py` | Blender | builds the parts (boolean union of convex primitives, clearance cuts), sockets, saves `.blend`, exports `.fbx` |
| `measure.py` | Blender | `evidence/T-007/dimensions.json` (C10–C20, C23, C24 values and pass flags) |
| `render_views.py` | Blender | Workbench renders to `evidence/T-007/raw/` (turnarounds, silhouettes, flat colours) + `views.json` |
| `rom_check.py` | Blender | `evidence/T-007/rom_report.json` + pose renders (C21) |
| `compose_unity.py` | kaiju env | Unity 4-view with markers and the pixel-proxy capture from `MechFrameCapture` output |
| `compose_evidence.py` | kaiju env | markers on the turnarounds, 180/48/24 px silhouettes (×8, blind decoys), human and concept overlays, `pixel_checks.json` |

## Commands (from `kaiju/`)
```bash
BL=/Applications/Blender.app/Contents/MacOS/Blender
P=Pipelines/blender/mech_frame_balanced
M=art/models/mech_frame_balanced/mech_frame_balanced.blend
$BL -b --factory-startup --python $P/build_greybox.py      # -> .blend + .fbx (≈2 s)
$BL -b $M --python $P/measure.py                             # -> evidence/T-007/dimensions.json
$BL -b $M --python $P/render_views.py                        # -> evidence/T-007/raw/*.png, views.json
$BL -b $M --python $P/rom_check.py                           # -> evidence/T-007/rom_report.json, raw/pose_*.png
/opt/miniconda3/envs/kaiju/bin/python $P/compose_evidence.py # -> evidence/T-007/*.png, pixel_checks.json
```
Then re-import in Unity: `unity run <kaiju> --no-tail --log-file <log> --timeout 900 -- -executeMethod Game.Units.Mech.Art.Editor.MechFrameImport.Run`
(see `Assets/Game/Units/Mech/Art/ART_ASSETS.md`).

## Conventions
- Authoring frame = `mech_frame@v1`: x right, y up, z forward (Unity), metres; mech's left is −x.
  Blender = (x, z, y) (a reflection; Unity is left-handed): the mech faces Blender +Y, its left at −X. FBX export:
  forward Y, up Z (file stays Z-up), `FBX_SCALE_ALL`; Unity imports with `bakeAxisConversion`, giving identity
  rotations, scale 1 and positions equal to the JSON.
- Names: part `MechBalanced_<segment id>` (e.g. `MechBalanced_forearm_L`), socket `MechBalanced_socket_<kind>_<side>`
  (shoulder, hip, elbow, hardpoint). Materials `mech_primary|secondary|joint|accent`.
- Origins at the proximal joint centre from the JSON; parent = the JSON `parent` field; R parts are exact mirrors of L.
- Joint housings: balls at the spherical joints (shoulder, hip), drums at the revolute and two-axis joints (elbow,
  knee, ankle, wrist), ball + column at the waist, collar ring + neck at the neck; 0.35 m clearance to the
  neighbouring part. Clearance cuts (pelvis cups, block and thigh bevels) were sized with `rom_check.py`.
