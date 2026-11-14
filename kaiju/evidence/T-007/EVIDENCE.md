# T-007 evidence map (Phases A, B, C; updated for fix cycle 3)

Spec: `expectations/T-007.md` (C1–C31). Branch `task/T-007`. All paths below are relative to `kaiju/`.
Single source of dimensions: `research/R-005/mech_frame_balanced.json` (`mech_frame@v1`), copied verbatim by the
integration branch to `Assets/Game/Config/Mech/mech_frame_balanced.json` (tests read it from there).

**Blind look:** `silhouettes_x8_48px_blind.png` and `silhouettes_x8_24px_blind.png` show the mech among two
humanoid-adjacent decoys in a random order. The key is in `sealed/decoy_key.json`. **Do not open it before the blind look.**

**Tools and commands** (from `kaiju/`; `BL=/Applications/Blender.app/Contents/MacOS/Blender`, `P=Pipelines/blender/mech_frame_balanced`, `M=art/models/mech_frame_balanced/mech_frame_balanced.blend`):

| Key | Tool | Command | Writes |
|---|---|---|---|
| BUILD | Blender 5.3 headless | `$BL -b --factory-startup --python $P/build_greybox.py` | `.blend`, `.fbx` |
| MEAS | Blender 5.3 headless | `$BL -b $M --python $P/measure.py` | `dimensions.json` |
| REND | Blender 5.3 Workbench | `$BL -b $M --python $P/render_views.py` | `raw/turnaround_*`, `raw/silhouette_*`, `raw/flat_front.png` |
| ROM | Blender 5.3 (boolean, Manifold solver) | `$BL -b $M --python $P/rom_check.py` | `rom_report.json`, `raw/pose_*` |
| COMP | kaiju Python | `/opt/miniconda3/envs/kaiju/bin/python $P/compose_evidence.py` | turnarounds with markers, silhouettes, blind sheets and sealed key, overlays, `pixel_checks.json`, `rom_contact_sheet.png` |
| UIMP | Unity 6000.6.9f1 CLI | `unity run "$PWD" --no-tail --log-file evidence/T-007/unity_import_run{1,2}.log --timeout 1500 -- -executeMethod Game.Units.Mech.Art.Editor.MechFrameImport.Run` | prefab, map, materials, `unity_hierarchy.txt` |
| UTEST | Unity CLI | `unity test "$PWD" --mode EditMode -o evidence/T-007/unity_editmode.xml` | `unity_editmode.xml` |
| UCAP | Unity CLI + kaiju Python | `unity run "$PWD" --no-tail --log-file evidence/T-007/unity_capture.log --timeout 1500 -- -executeMethod Game.Units.Mech.Art.Editor.MechFrameCapture.Run` then `python $P/compose_unity.py` | `raw/unity_*`, `unity_4view.png`, `unity_pixel_proxy.png` |

Commits (fix cycle 3): geometry `4060fc2`; Unity reimport `77d4e6f`; second-run diff, EditMode XML, Unity captures and the refreshed design note `510a6f0`; this file and the hand-off rule in the commit after it. (Cycle 2: geometry `7cf374b`, tests `9fd7edc`.)
Blender and Unity evidence regenerated in each cycle (see the git log of `evidence/T-007/`).
Blender ran **headless**: the Blender MCP servers were not reachable (connection refused on port 9876).

## Criteria (re-measured after fix cycle 3)

| C# | Artifact | Measured value | Result | Tool |
|---|---|---|---|---|
| C1 | `banned_terms_check.txt`, `PROMPTS.md`, `PROVENANCE.md` | final grep 0 matches; 11 prompts read | pass (Phase A) | grep |
| C2 | `PROVENANCE.md` | nothing uploaded; only this task's own outputs used as image references | pass (Phase A) | — |
| C3 | `turnaround_*.png`, `silhouettes_x8_*` | no crest, horns, cape, helm or face. Top at 24 px: one 8 px flat mass (blocks + cowl + shoulder armour) with a 1 px head bump. Blocks are plain square solids with no launcher faces | eval-style to judge | REND, COMP |
| C4 | same, `flat_front_180px_x4.png` | no V-crest, fins, round chest core, face, livery, emblem or text; one 2.2 m orange panel | pass (self-check) | REND, COMP |
| C5 | `silhouette_front_48px.png`, `silhouette_side_48px.png`, `DESIGN_NOTE.md` | head sunk between the two square blocks; bow chest 16 m deep in side view; box forearm 5.4 m wide (3.2 px) over a 4.6 m upper arm and 4.75 m fist; design-note signature forms rewritten to match | eval-style to judge | COMP |
| C6 | `pixel_checks.json` | 48 px front: arm rows with ≥ 3 runs (arms apart from the torso) 21 of 22 (95 %); crotch gap 19/19; feet 6 px vs shanks 4 px. Side: foot cols 0–8 vs shank 2–6; no empty row (knee row now 2 px, 3.7 m) | pass | COMP |
| C7 | `silhouettes_x8_24px_blind.png`, `pixel_checks.json` | 24 px: widest extent 10 px from the shoulder row (65 m) down (arms hang inside the shoulder line); top mass 8 px; head 1 px bump; leg gap on 10 rows | measured pass; blind look for eval-style (key sealed) | COMP |
| C8 | `flat_front_180px*.png` | 4 value bands ≥ 1 % (luminance 40, 80, 88, 184) | pass | COMP |
| C9 | `overlay_human_iou.png` | IoU with an equal-height human 0.522 (48 px), 0.507 (180 px) | pass (≤ 0.75) | COMP |
| C10 | `dimensions.json`, `turnaround_*.png` | H 80.000 m; chest-shell top 69.5 m; 67 m line crosses the chest (50–69.5 m); head from 70.1 m (cap from 75.4 m); foot 8.3 × the 1.8 m human | pass | MEAS |
| C11 | `dimensions.json` | shoulder width on torso and shoulder armour only (spec v1.1 ruling b): 28.3 m = 0.354 H; shoulder/hip 28.3/20.0 = 1.415; head height 5.0 m = 0.0625 H; head width/shoulder 0.226; forearm 5.40 m = 1.125 × upper arm 4.80 m (shoulder ball) and 1.14 × fist; fist 4.75 m = 0.0594 H, knuckle face 10.3 m² | pass (shoulder/hip 0.015 over its limit) | MEAS |
| C12 | `dimensions.json` | leg 0.525 H; thigh min 8.0 m = 0.100 H, shank min 6.4 m = 0.080 H (narrower of front and side); mid widths 8.0 / 6.8 ≥ hip ball and knee/ankle drums; foot 0.1875 / 0.125 H, foot/shank 1.56; stance/shoulder 23.0/28.3 = 0.813 | pass (thigh and shank exactly at their limits) | MEAS |
| C13 | `dimensions.json` | volume centroid 43.52 m vs config CoM 42.54 m (+0.0123 H, limit ±0.03 H); L/R difference 0 % | pass | MEAS |
| C14 | `dimensions.json` | sole 148.56 m² per foot, flat at y 0.000, L/W 1.5, ankle 14.54 % of foot length from sole centroid; 660 kPa on one foot, 330 kPa on two | pass | MEAS |
| C15 | `dimensions.json` joints[].pivot_inside, renders | all 14 pivots inside housings; 4.8 m shoulder balls seated in chest cups under the shoulder armour; waist ball inside the trunk; paired rams across knees and elbows; knee guard over the knee drum | pass (eval to confirm) | MEAS, REND |
| C16 | `turnaround_sheet.png` | side chest depth 16.0 m = 0.200 H > pelvis 13.9 m; trunk 10 m; no neck; arms armoured (upper arm 4.6 m, forearm 5.4 m, fist 4.75 m, shoulder housing 4.8 m) | panel re-score for eval-style | MEAS, REND |
| C17 | `dimensions.json`, `raw/unity_views.json`, test `Height_EqualsConfig_WithSoleOnGround` | Blender H 80.000; Unity renderer bounds y 0.00–80.00 (80.0000 m), x ±15.91 (shoulder balls) | pass | MEAS, UCAP, UTEST |
| C18 | `dimensions.json`; test `Joints_And_SegmentLengths_MatchConfigWithin2Percent` | 14 joints and 15 origins at 0.000 m deviation; segment lengths 0.00 %; L/R mirror 0.000 m | pass | MEAS, UTEST |
| C19 | `dimensions.json` | 15/15 closed manifold; total 9,721.3 m³ (cycle 1 9,762.3, cycle 2 9,644.6; under 10,000); 12.0 kt at 1,232 kg/m³; volume/mass fraction ratios pelvis 0.83, torso 1.02, head 0.91, upper arm 1.02, forearm 1.28, thigh 0.78, shank 1.25, foot 1.25 (hand 1.40, exempt) | pass | MEAS |
| C20 | `unity_hierarchy.txt`, part map; tests `Mapping_*`, `Hierarchy_*`, `Prefab_HoldsOnly15PartsAnd8MapSockets` | 15 parts `MechBalanced_<segment id>` + 8 socket empties, nothing else (now asserted) | pass | UIMP, UTEST |
| C21 | `rom_report.json`, `rom_contact_sheet.png` | AC-21 rest, guard, crouch, arms overhead: non-adjacent penetration 0.00 m, no adjacent overlap. Every R-005 DOF of the waist, neck and both sides at both range ends (116 runs): neighbour overlap ≤ 2 % in all, worst 1.62 % (shoulder abduction 150°); neck flexion 0.11 % / 0.004 %. B7 core DOFs: no non-neighbour contact. Non-neighbour contacts in other DOFs: hand-off rule below | AC-21 pass; B7 pass | ROM |
| C22 | tests `Sockets_LeftRightMirrorSymmetric`, `ForearmSwap_ThroughSocketAndMappingOnly` | sockets mirrored < 1 cm; swap uses an independent cube mesh on the elbow socket, takes over hand and hardpoint socket through `Remap`; hardpoint moves with the new limb; 14 other parts unmoved | pass | UTEST |
| C23 | `dimensions.json` | convex-hull ratio max 1.589 (torso), pelvis 1.559, foot 1.542, shank 1.527, thigh 1.449, forearm 1.32, upper arm 1.19 | pass (≤ 1.6) | MEAS |
| C24 | `dimensions.json`, test `Budget_*` | 3,080 triangles; no textures; 4 palette-role slots | pass | MEAS, UTEST |
| C25 | `art/concepts/…`, `concept_contact_sheet.png`, `concept_not_to_follow.md`, `DESIGN_NOTE.md` § Do not follow | four layers with provenance; per-file list now covers mood frame v2 and is merged into the design note | pass | — |
| C26 | `PROVENANCE.md` | 11 of 16 generations; no purchase, setting change or terms | pass (Phase A) | — |
| C27 | `overlay_concept_vs_greybox.png`, `pixel_checks.json`, `DESIGN_NOTE.md` § Departures | front IoU 0.395, arm-masked 0.550, side 0.320 (reported; spec v1.1: not a gate); every departure listed below and in the design note, the arm departure included | pass as written (spec v1.1) | COMP |
| C28 | `mood_frame_check.json`, `mood_frame_histogram.png`, `mood_frame_masks.png`, `art/concepts/…/layer4_mood-v2_af67b340…_0.png` | produced by the concept agent (fix cycle 2); eval-style cycle 2: pass | pass (not T-007 B/C work) | concept agent |
| C29 | `unity_capture.log` (forced reimport), `unity_import_run1.log`; test `Axes_*` | model import 0 warnings, 0 errors; identity rotations, scale 1, Y up, toes at +z, L at −x | pass | UIMP, UCAP, UTEST |
| C30 | `unity_second_run_diff.txt` | a further `MechFrameImport.Run` on the committed state leaves `git status -- Assets` empty; metas committed; FBX/blend in LFS | pass | UIMP |
| C31 | `unity_editmode.xml` | `MechFrameBalancedArtTests` 10/10 pass; whole suite 111/111 | pass | UTEST |

## Departures, conflicts and hand-off (numbers, no criterion relaxed)
- **Arms (C27 departure, DEF-T007-style-8).** Forearm 5.4 m, upper arm 4.6 m, shoulder ball 4.8 m, fist 4.75 m. The concept's
  forearms are about twice the upper arm and hang outside the shoulders; here C7 keeps the shoulders the widest mass at 24 px
  (forearm outer face 15.85 m, shoulder ball 15.90 m) and the `mech_frame@v1` shoulder joints are 27 m apart. Per spec v1.1
  ruling (a) the arms now use the width down to 0.45 m from the thigh.
- **Other departures:** leg spacing (JSON stance 13 m; the concept's legs stand wider); no masts; full-width shins and ankles;
  flat untoed feet; closed fists; legs 0.525 H; shoulder blocks 3 m below the head top and inboard; joint housings on the child
  part; no pelvis front skirt. The concept's middle view is a perspective 3/4, so the side IoU is not like for like.
- **Hand-off rule for T-005 (DEF-T007-physics-1, -4), also in `Assets/Game/Units/Mech/Art/ART_ASSETS.md`:** with the arms
  hanging at rest (forearm/hand 0.45 m from the thigh, feet 3.0 m apart), these joints driven at M0 push a limb into a
  non-neighbour part at their R-005 range ends (isolated runs, depth m / volume m³, L = R): waist lateral bend ±20° forearm–thigh
  2.90/27.9, hand–thigh 4.44/48.5; waist axial rotation ±30° pelvis–forearm 0.20/0.6; shoulder adduction −10° forearm–thigh
  3.72/34.3, hand–thigh 4.44/64.5, pelvis–forearm 0.20/0.2; shoulder external rotation −60° forearm–thigh 2.15/9.9; hip
  abduction +40° hand–thigh 4.44/41.3, forearm–thigh 2.09/9.3; hip internal rotation −40°/+35° hand–thigh 1.08/5.6 and
  0.40/1.9, foot–foot at +35° 1.00/3.1; hip adduction −25° leg on leg (shank–shank 6.80/292); wrist ulnar deviation −20°
  (locked at M0) hand–thigh 1.48/13.2. **Rule (proposed Q-NEW-b, Assumed):** T-005 ignores collisions between
  `upper_arm`/`forearm`/`hand` and `pelvis`/`thigh` of the same side, or limits shoulder adduction to ≥ 0° and couples shoulder
  abduction to waist lateral bend plus hip abduction; leg-on-leg contact stays enabled.
- **Hardpoint socket position is not in the config (DEF-T007-arch-2):** it comes from `FOREARM_PLATE` in `mf_common.py`;
  proposed Q-NEW-c for a socket field in a later `mech_frame` version.

## Fix cycle 2: defect → change → artifact → measured value

| Defect | Change | Artifact | Measured value |
|---|---|---|---|
| DEF-T007-style-2 (C16, weight read) | chest rebuilt as a ship's hull section: bow keel to z +10 m at 66 m, pointed back at −6 m; trunk carried down to 50 m (10 m wide) so only a 1.5 m band of the waist ball shows; neck hidden in a torso cowl (top 75.0 m, sloping to 72.8 m in front); shoulder balls seated in chest cups; upper arm a 3.2 m strut, forearm a 4.45 × 5.6 m box | `build_greybox.py`, `turnaround_*.png`, `dimensions.json` | chest side depth 16.0 m = 0.200 H > pelvis 13.9 m; exposed neck 0 m; total volume 9,644.6 m³ (−118 m³) |
| DEF-T007-style-1 (C27, C5) | head sunk in the cowl between square 6 m shoulder blocks moved inboard (inner face 5.4 m, 2.2 m from the head), bevel removed; bow chest in side view; box forearm vs strut upper arm | `overlay_concept_vs_greybox.png`, `silhouette_*_48px.png` | front IoU 0.367 (arm-masked 0.543), side 0.321 (was 0.393 / 0.288); not ≥ 0.6, reasons above |
| DEF-T007-style-4 (C3, C7 watch items) | blocks square, no bevel, top 3 m below the head; no launcher faces | `silhouettes_x8_24px*.png`, `pixel_checks.json` | 24 px top: 7 px flat mass with a 1 px head bump |
| DEF-T007-style-5 (C6) | thighs moved 0.5 m inboard of the hip joint (8 m kept), arm moved outboard, fist narrowed to the C11 minimum | `dimensions.json` | hand–thigh clearance 0.95 m (was 0.15–0.35 m); still < 1.67 m: conflict above. Side chest 16 m vs thigh 8 m (≈ 4.8 px deeper at 48 px) |
| DEF-T007-style-6 (C4, C25) | per-file "follow / do not follow" list; concept files untouched | `concept_not_to_follow.md` | 8 kept files listed |
| DEF-T007-style-7 (C7 evidence) | decoys now a lattice pylon with cross-arms and a two-legged gantry; order drawn with `SystemRandom` per size; key moved to `sealed/` with a do-not-open note | `silhouettes_x8_{48,24}px_blind.png`, `sealed/decoy_key.json` | — (blind look pending) |
| DEF-T007-physics-1 | contacts listed above with depths and volumes; handoff rule proposed (Q-NEW-b) | this file, `rom_report.json`, `tasks/T-007.impl.md` | arm stand-off 0.95 m (was 0.15 m) |
| DEF-T007-physics-2 | neck opening made a cone (r 1.95 m at 69.4 m to 3.9 m at 75.2 m), cap tapered at its base, cowl top sloping to the front | `rom_report.json` | neck flexion −30°: 0.11 %, +40°: 0.004 % (was 2.54 / 2.70 %) |
| DEF-T007-physics-3 | ROM sweep runs both sides; cleared pose moves the contralateral leg for either side | `rom_report.json` | 116 range-end runs, L and R results identical |
| DEF-T007-arch-1 | test `Prefab_HoldsOnly15PartsAnd8MapSockets` | `unity_editmode.xml` | pass |
| DEF-T007-arch-2 | recorded as proposed Q-NEW-c (socket field in a later schema version) | `tasks/T-007.impl.md` | — |
| DEF-T007-arch-3 | swap test uses a built-in cube mesh, moves the hardpoint socket to the stand-in and checks it moves with it | `MechFrameBalancedArtTests.cs`, `unity_editmode.xml` | pass |
| DEF-T007-arch-4 | test `Remap_RejectsUnknownSegment_AndReRootsOnlyExactPrefix` (KeyNotFoundException; `forearm_L2` not re-rooted) | `unity_editmode.xml` | pass |
| DEF-T007-style-3 (C28) | not in this cycle (separate concept agent) | — | — |

## Fix cycle 3: defect → change → artifact → measured value

| Defect | Change | Artifact | Measured value |
|---|---|---|---|
| DEF-T007-style-2 (C16, arms read as wires) | upper arm 3.2 → 4.6 m; shoulder ball 3.8 → 4.8 m (≥ fist; 12 × 8 facets with a true equator ring), seated in a chest cup under a new outer shoulder-armour plate (torso, out to 14.15 m, set back to z ≤ −1 m for overhead reach); forearm box 4.45 → 5.4 m with a hidden inner-face recess; fist 4.45 → 4.75 m; arm moved in to 0.45 m from the thigh (ruling a). Volume paid by thinner upper-arm and forearm depth, a slimmer fist, a smaller chest section at 56 m, slightly smaller blocks, a wider hamstring and calf channel | `build_greybox.py`, `turnaround_*.png`, `dimensions.json` | upper arm 4.6 m, housing 4.8 m, forearm 5.4 m, fist 4.75 m; total volume 9,721.3 m³ (≤ 10,000); worst ROM overlap 1.62 %; C11 shoulder width 28.3 m on torso and armour (ruling b), shoulder/hip 1.415 |
| DEF-T007-style-9 (knee sliver) | thigh knee guard: the thigh front carried down outside the knee clearance circle and stepped back over the top of the drum | `silhouette_side_48px.png`, `silhouette_side_full.png` | thinnest side row at the knee 3.7 m (2 px at 48 px; was 1.4 m and an empty row); no empty row in front, side or 3/4 |
| DEF-T007-style-8 (departures, design note) | arm departure listed above; design note rewritten to the built forms (blocks, shoulder width, form 3, joints, plates) | `EVIDENCE.md`, `art/concepts/mech_frame_balanced/DESIGN_NOTE.md` | — |
| DEF-T007-style-10 (concept hygiene, C28 index) | mood frame v2 row added to `concept_not_to_follow.md`; the list merged into the design note; C28 row points at `mood_frame_check.json` | `concept_not_to_follow.md`, `DESIGN_NOTE.md` | — |
| DEF-T007-physics-4 (hand-off rule) | rule stated here and in `ART_ASSETS.md`, with every contact and its depth | `EVIDENCE.md`, `Assets/Game/Units/Mech/Art/ART_ASSETS.md` | arm-to-thigh rest clearance 0.45 m |
| Regeneration | model, FBX, prefab via `MechFrameImport.Run`, dimensions, ROM (both sides), turnarounds, silhouettes with a fresh random decoy order and sealed key, overlays, Unity captures | `unity_second_run_diff.txt`, `unity_editmode.xml`, `unity_4view.png` | second import run: empty diff; EditMode 111/111 (art 10/10); Unity H 80.0000 m; import 0 warnings |
