# T-007 evidence map (Phases A, B, C; updated for fix cycle 2)

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

Commits (fix cycle 2): geometry final at `7cf374b`; reimport and new tests `9fd7edc`; EditMode XML `69546b9`;
Blender and Unity evidence regenerated in `5c96036` and after it (see the git log of `evidence/T-007/`).
Blender ran **headless**: the Blender MCP servers were not reachable (connection refused on port 9876).

## Criteria (re-measured after fix cycle 2)

| C# | Artifact | Measured value | Result | Tool |
|---|---|---|---|---|
| C1 | `banned_terms_check.txt`, `PROMPTS.md`, `PROVENANCE.md` | final grep 0 matches; 11 prompts read | pass (Phase A) | grep |
| C2 | `PROVENANCE.md` | nothing uploaded; only this task's own outputs used as image references | pass (Phase A) | — |
| C3 | `turnaround_*.png`, `silhouettes_x8_*` | no crest, horns, cape, helm or face. Top at 24 px: one 7 px flat mass (blocks + cowl) with a 1 px head bump, not ears. Blocks are solid square armour with no launcher faces | eval-style to judge | REND, COMP |
| C4 | same, `flat_front_180px_x4.png` | no V-crest, fins, round chest core, face, livery, emblem or text; one 2.2 m orange panel | pass (self-check) | REND, COMP |
| C5 | `silhouette_front_48px.png`, `silhouette_side_48px.png` | head sunk between the two square blocks; bow chest 16 m deep in side view; box forearm 4.45 m wide (2.7 px) vs upper-arm strut 3.2 m. The design-note wording of form 3 is in `art/concepts/` and was not edited here (see impl notes) | eval-style to judge | COMP |
| C6 | `pixel_checks.json` | 48 px: arms separated from the torso on 15 of 22 arm rows (68 %); crotch gap 19/19; feet 6 px vs shanks 4 px; side: foot cols 0–8 vs shank 2–6. Hands still touch the thighs at 48 px (clearance 0.95 m, below 1 px): see conflict below | pass as written in C6; DEF-style-5 hand gap not met | COMP |
| C7 | `silhouettes_x8_24px_blind.png`, `pixel_checks.json` | 24 px: widest extent 9 px from the shoulder row (65 m) down; top flat mass 7 px; head 1 px bump; leg gap on 10 rows | measured pass; blind look for eval-style | COMP |
| C8 | `flat_front_180px*.png` | 4 value bands ≥ 1 % (luminance 40, 80, 88, 184) | pass | COMP |
| C9 | `overlay_human_iou.png` | IoU with an equal-height human 0.524 (48 px), 0.514 (180 px) | pass (≤ 0.75) | COMP |
| C10 | `dimensions.json`, `turnaround_*.png` | H 80.000 m; chest-shell top 69.5 m; 67 m line crosses the chest (50–69.5 m); head from 70.1 m (cap from 75.4 m); foot 8.3 × the 1.8 m human | pass | MEAS |
| C11 | `dimensions.json` | shoulder 30.8 m = 0.385 H; shoulder/hip 30.8/20.0 = 1.54; head height 5.0 m (cowl top 75.0 to 80) = 0.0625 H; head width/shoulder 0.208; forearm 4.45 m = 1.17 × upper arm 3.80 m (shoulder ball) and = fist; fist 4.45 m = 0.0556 H, knuckle face 10.95 m² | pass | MEAS |
| C12 | `dimensions.json` | leg 0.525 H; thigh min 8.0 m = 0.100 H, shank min 6.4 m = 0.080 H (narrower of front and side); mid widths 8.0 / 6.8 ≥ hip ball 6.6 and knee/ankle drums 6.8; foot 0.1875 / 0.125 H, foot/shank 1.56; stance/shoulder 0.747 | pass (thigh and shank exactly at their limits) | MEAS |
| C13 | `dimensions.json` | volume centroid 43.12 m vs config CoM 42.54 m (+0.0072 H); L/R difference 0 % | pass | MEAS |
| C14 | `dimensions.json` | sole 148.56 m² per foot, flat at y 0.000, L/W 1.5, ankle 14.54 % of foot length from sole centroid; 660 kPa on one foot, 330 kPa on two | pass | MEAS |
| C15 | `dimensions.json` joints[].pivot_inside, renders | all 14 pivots inside housings; shoulder balls seated in cups on the chest; waist ball inside the trunk; paired rams across knees and elbows; shoulder → hip line runs through chest, trunk, keel | pass (eval to confirm) | MEAS, REND |
| C16 | `turnaround_sheet.png` | side chest depth 16.0 m = 0.200 H > pelvis 13.9 m; trunk 10 m wide at its base (50 m) over a 1.5 m waist-ball band; no exposed neck; arms strut + box | panel re-score for eval-style | MEAS, REND |
| C17 | `dimensions.json`, `raw/unity_views.json`, test `Height_EqualsConfig_WithSoleOnGround` | Blender H 80.000; Unity renderer bounds y 0.00–80.00 (80.0000 m) | pass | MEAS, UCAP, UTEST |
| C18 | `dimensions.json`; test `Joints_And_SegmentLengths_MatchConfigWithin2Percent` | 14 joints and 15 origins at 0.000 m deviation; segment lengths 0.00 %; L/R mirror 0.000 m | pass | MEAS, UTEST |
| C19 | `dimensions.json` | 15/15 closed manifold; total 9,644.6 m³ (was 9,762.3: −118 m³, no net volume added); 11.9 kt at 1,232 kg/m³; volume/mass fraction ratios pelvis 0.83, torso 1.04, head 0.91, upper arm 0.75, forearm 1.21, thigh 0.80, shank 1.297, foot 1.26 (hand 1.42, exempt) | pass (shank 0.003 under its limit) | MEAS |
| C20 | `unity_hierarchy.txt`, part map; tests `Mapping_*`, `Hierarchy_*`, `Prefab_HoldsOnly15PartsAnd8MapSockets` | 15 parts `MechBalanced_<segment id>` + 8 socket empties, nothing else (now asserted) | pass | UIMP, UTEST |
| C21 | `rom_report.json`, `rom_contact_sheet.png` | AC-21 rest, guard, crouch, arms overhead: non-adjacent penetration 0.00 m, no adjacent overlap. Every R-005 DOF of the waist, neck and **both** sides at both range ends (116 runs): neighbour overlap ≤ 2 % in all, max 1.49 % (hip adduction −25°); neck flexion −30°/+40° now 0.11 % / 0.004 %. B7 core DOFs: no non-neighbour contact. Non-neighbour contacts in other DOFs are listed below | AC-21 pass; B7 pass; arm-into-leg contacts listed, not hidden | ROM |
| C22 | tests `Sockets_LeftRightMirrorSymmetric`, `ForearmSwap_ThroughSocketAndMappingOnly` | sockets mirrored < 1 cm; swap uses an independent cube mesh on the elbow socket, takes over hand and hardpoint socket through `Remap`; hardpoint moves with the new limb; 14 other parts unmoved | pass | UTEST |
| C23 | `dimensions.json` | convex-hull ratio max 1.559 (pelvis), foot 1.542, torso 1.53, shank 1.48 | pass (≤ 1.6) | MEAS |
| C24 | `dimensions.json`, test `Budget_*` | 2,846 triangles; no textures; 4 palette-role slots | pass | MEAS, UTEST |
| C25 | `art/concepts/…`, `concept_contact_sheet.png`, `concept_not_to_follow.md` | four layers with provenance; per-file "do not follow" list added | pass (Phase A + list) | — |
| C26 | `PROVENANCE.md` | 11 of 16 generations; no purchase, setting change or terms | pass (Phase A) | — |
| C27 | `overlay_concept_vs_greybox.png`, `pixel_checks.json` | front IoU 0.367, arm-masked front IoU 0.543, side IoU 0.321 | **not met** (≥ 0.6 expected); constraint-tied departures below | COMP |
| C28 | — | being redone by a separate concept agent | not in this cycle | — |
| C29 | `unity_capture.log` (forced reimport), `unity_import_run1.log`; test `Axes_*` | model import 0 warnings, 0 errors; identity rotations, scale 1, Y up, toes at +z, L at −x | pass | UIMP, UCAP, UTEST |
| C30 | `unity_second_run_diff.txt` | a further `MechFrameImport.Run` on the committed state leaves `git status -- Assets` empty; metas committed; FBX/blend in LFS | pass | UIMP |
| C31 | `unity_editmode.xml` | `MechFrameBalancedArtTests` 10/10 pass; whole suite 111/111 | pass | UTEST |

## Conflicts and items not met (numbers, no criterion relaxed)
- **DEF-T007-style-5, hand-to-thigh gap ≥ 1 px (1.67 m) at 48 px: not reachable with C7 + C11 + C12 + the JSON hip spacing.**
  The forearm and fist must end inside the shoulder outline (C7: shoulders widest; shoulder ball outer at 15.43 m, and C11 caps
  it at 0.40 H = 16.0 m). The fist must be ≥ 0.055 H = 4.4 m wide and the forearm at least as wide (C11), so the arm's inner
  face is at most 15.43 − 4.45 = 10.98 m from the centreline (11.6 m even at the 16.0 m cap). The thigh must be ≥ 0.10 H
  = 8.0 m wide (C12) around a hip joint at 6.5 m (JSON); with its inner face at 2.0 m (keel clearance), its outer face is at
  10.0 m. Clearance now 0.95 m (was 0.15–0.35 m); the most C7/C11/C12 allow is 1.6 m < 1.67 m.
  The evaluator's founder question (measure the C11 shoulder width on the torso and blocks only) would lift this limit.
- **C27 IoU 0.367 front (arm-masked 0.543), 0.321 side.** Forced departures: forearm front width (C7/C11 chain above;
  the concept forearms are about twice the upper arm and wider than the shoulders); leg spacing (JSON stance 13 m; the concept's
  legs stand wider, which leaves the arm-masked band below 0.6 too); no masts; full-width shins and ankles; flat untoed feet;
  closed fists; legs 0.525 H. The side view of the concept is a perspective 3/4, not a true side.
- **Arm-into-leg contacts in M0-active DOFs (DEF-T007-physics-1).** Isolated runs at range ends, other joints at rest
  (`rom_report.json` summary.b7_contacts_isolated; depth m / volume m³): waist lateral bend ±20° forearm–thigh 2.43/30.6 and
  hand–thigh 4.64/72.1; shoulder adduction −10° forearm–thigh 3.23/32.2, hand–thigh 4.64/83.5, pelvis–forearm 0.63/0.6;
  shoulder external rotation −60° forearm–thigh 2.50/12.4; hip abduction +40° hand–thigh 4.45/62.9, forearm–thigh 1.59/5.7;
  hip internal rotation −40°/+35° hand–thigh 0.88/4.2 and 0.20/0.6; wrist ulnar deviation −20° hand–thigh 1.29/12.0
  (wrist locked at M0). Leg on leg: hip adduction −25° (shank–shank 6.8/307) and hip internal rotation +35° (foot–foot 1.0/3.1).
  Waist axial rotation no longer touches. Same values on L and R. Clearance at rest: arm to thigh 0.95 m, feet 3.0 m apart.
  Handoff to T-005: proposed Q-NEW-b in `tasks/T-007.impl.md`.
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
