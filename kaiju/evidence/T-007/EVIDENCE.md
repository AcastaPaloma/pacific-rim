# T-007 evidence map (Phases A, B, C)

Spec: `expectations/T-007.md` (C1–C31). Branch `task/T-007`. All paths below are relative to `kaiju/`.
Single source of dimensions: `research/R-005/mech_frame_balanced.json` (`mech_frame@v1`), copied verbatim by the
integration branch to `Assets/Game/Config/Mech/mech_frame_balanced.json` (tests read it from there).

**Tools and commands** (from `kaiju/`; `BL=/Applications/Blender.app/Contents/MacOS/Blender`, `P=Pipelines/blender/mech_frame_balanced`, `M=art/models/mech_frame_balanced/mech_frame_balanced.blend`):

| Key | Tool | Command | Writes |
|---|---|---|---|
| BUILD | Blender 5.3 headless | `$BL -b --factory-startup --python $P/build_greybox.py` | `.blend`, `.fbx` |
| MEAS | Blender 5.3 headless | `$BL -b $M --python $P/measure.py` | `dimensions.json` |
| REND | Blender 5.3 Workbench | `$BL -b $M --python $P/render_views.py` | `raw/turnaround_*`, `raw/silhouette_*`, `raw/flat_front.png` |
| ROM | Blender 5.3 (boolean, Manifold solver) | `$BL -b $M --python $P/rom_check.py` | `rom_report.json`, `raw/pose_*` |
| COMP | kaiju Python | `/opt/miniconda3/envs/kaiju/bin/python $P/compose_evidence.py` | turnarounds with markers, silhouettes, overlays, `pixel_checks.json`, `rom_contact_sheet.png` |
| UIMP | Unity 6000.6.9f1 CLI | `unity run "$PWD" --no-tail --log-file evidence/T-007/unity_import_run{1,2}.log --timeout 1500 -- -executeMethod Game.Units.Mech.Art.Editor.MechFrameImport.Run` | prefab, map, materials, `unity_hierarchy.txt` |
| UTEST | Unity CLI | `unity test "$PWD" --mode EditMode -o evidence/T-007/unity_editmode.xml` | `unity_editmode.xml` |
| UCAP | Unity CLI + kaiju Python | `unity run "$PWD" --no-tail --log-file evidence/T-007/unity_capture.log --timeout 1500 -- -executeMethod Game.Units.Mech.Art.Editor.MechFrameCapture.Run` then `python $P/compose_unity.py` | `raw/unity_*`, `unity_4view.png`, `unity_pixel_proxy.png` |

Commits: model geometry final at `53c2804` (Z-up axis change; frame-space geometry unchanged since `fc93dc7`);
Unity import settings final at `bed3961`; MEAS/REND/ROM/COMP/UCAP/UTEST outputs regenerated and committed in `a4356f0`
(`dimensions.json` records `commit_at_run`). Blender ran **headless**: the Blender MCP servers were not reachable
(connection refused on port 9876), so no live session was touched.

## Criteria

| C# | Artifact | Measured value | Result | Tool / cmd |
|---|---|---|---|---|
| C1 | `banned_terms_check.txt`, `art/concepts/mech_frame_balanced/PROMPTS.md`, `PROVENANCE.md` | final grep 0 matches; manual read of 11 prompts | pass (Phase A, `afbb93c`) | grep (Phase A) |
| C2 | `PROVENANCE.md` §"What was and was not done" | no image uploaded; only own outputs as image refs | pass (Phase A) | — |
| C3 | `turnaround_{front,side,three_quarter}.png`, `silhouettes_x8_{48,24}px.png` | no crest, horns, cape, helm, face; flat sensor cap, bow chest, box shoulder blocks | for eval-style (blind panel) | REND+COMP |
| C4 | same, `flat_front_180px_x4.png` | no V-crest, fins, round chest core, face, livery, emblem, text; one 2.2 m orange panel, no lettering | pass (self-check), eval-style to confirm | REND+COMP |
| C5 | `art/concepts/.../DESIGN_NOTE.md` signature forms; `silhouettes_x8_48px.png` | twin shoulder blocks (top row has 3 runs: block, head, block), bow wedge chest, box forearms | for eval-style | COMP |
| C6 | `silhouette_front_48px.png`, `silhouettes_x8_48px.png`, `pixel_checks.json` | arms separated (≥3 runs) on 15 of 22 arm rows = 68 % of arm length; crotch gap on 18/19 leg rows; feet 6 px vs shanks 4 px; side 48 px: foot cols 0–8 vs shank 2–5 (toe and heel project); head separate in the top rows | pass | COMP |
| C7 | `silhouettes_x8_24px_blind.png` (key in `silhouette_decoy_key.json`), `pixel_checks.json` | 24 px: widest row at 65 m (shoulders), top row 1 px (head bump), 10 leg rows with a gap | measured pass; 3 blind looks for eval-style | COMP |
| C8 | `flat_front_180px.png`, `flat_front_180px_x4.png`, `pixel_checks.json` | 4 value bands ≥ 1 % (luminance 40, 80, 88, 184); smallest forms: 0.3–0.4 m chamfers only, slit 1.0 m, accent 2.2 m; read carried by blocks ≥ 2 m | pass | REND+COMP |
| C9 | `overlay_human_iou.png` | IoU with an equal-height human silhouette 0.494 (48 px), 0.489 (180 px) | pass (≤ 0.75) | COMP |
| C10 | `dimensions.json` whole, `turnaround_*.png` | H 80.000 m; chest-shell top (shoulder line) 69.5 m; 67 m deck crosses the chest (53–69.5 m), head from 70.1 m; foot 15 m = 8.3 × 1.8 m human (red figure at the left toe) | pass | MEAS, COMP |
| C11 | `dimensions.json` ratios | shoulder 31.6 m = 0.395 H; shoulder/hip 31.6/20.6 = 1.53; head height 6.2 m = 0.078 H (collar top 73.8 to 80), head width/shoulder 0.20; forearm 5.10 m = 1.15 × upper arm 4.45 m and 1.11 × fist; fist 4.6 m = 0.0575 H, flat knuckle face 13.7 m² | pass | MEAS |
| C12 | `dimensions.json` | leg 42 m = 0.525 H; thigh min 8.0 m = 0.100 H, shank min 6.4 m = 0.080 H (narrower of front and side views); mid widths 8.0 / 6.8 ≥ joint housings (hip ball 7.2, knee and ankle drums 6.8); foot 15 × 10 m = 0.1875 / 0.125 H, foot/shank 1.56; stance 23 m / shoulder = 0.73 | pass (thigh and shank exactly at the limits) | MEAS |
| C13 | `dimensions.json` | volume centroid y 42.464 m vs config CoM 42.541 m (Δ −0.001 H); L/R volume difference 0.000 % | pass | MEAS |
| C14 | `dimensions.json` feet | sole 148.56 m² each (L = R), flat at y 0.000; L/W 1.5; ankle 2.18 m from sole centroid = 14.5 % of foot length; m·g/A 660 kPa one foot, 330 kPa two feet; rectangular sole, toe corners chamfered 1.2 m, no heel or claws | pass | MEAS |
| C15 | `dimensions.json` joints[].pivot_inside, `turnaround_*.png` | every pivot (14 joints) lies inside a housing (balls: shoulder, hip, waist; drums: elbow, knee, ankle, wrist; neck cylinder in a collar ring); paired rams across knees (front) and elbows (back); side-view line shoulder → hip runs through chest, waist column, keel core | pass (eval to confirm the visual read) | MEAS, REND |
| C16 | `turnaround_sheet.png`, `turnaround_*.png` | panel score | for eval-style | — |
| C17 | `dimensions.json`, `unity_4view.png`, `raw/unity_views.json`, test `Height_EqualsConfig_WithSoleOnGround` | Blender H 80.000; Unity renderer bounds y 0.00–80.00 (height 80.0000 m); scales 1, scene unit 1 | pass | MEAS, UCAP, UTEST |
| C18 | `dimensions.json` joints, parts[].segment_length, mirror; test `Joints_And_SegmentLengths_MatchConfigWithin2Percent` | all 14 joints and 15 origins deviate 0.000 m; all segment lengths 0.00 %; L/R origin and vertex mirror deviation 0.000 m | pass | MEAS, UTEST |
| C19 | `dimensions.json` parts[].closed_manifold, volume_fractions | 15/15 closed manifold; total 9,762 m³; 12.0 kt at the config mean hull density 1,232 kg/m³ (9.8–24.4 kt over 1,000–2,500); volume/mass fraction ratios: pelvis 0.91, torso 0.89, head 1.07, upper arm 0.94, forearm 1.22, thigh 0.90, shank 1.28, foot 1.24 (hand 1.65, exempt < 2 %) | pass | MEAS |
| C20 | `dimensions.json`, `unity_hierarchy.txt`, `MechFrameBalanced_PartMap.asset`; tests `Mapping_*`, `Hierarchy_FollowsKinematicTree` | 15 parts `MechBalanced_<segment id>` in `.blend`, `.fbx`, prefab and map; export = 15 meshes + 8 named socket empties; tree from the JSON parents | pass | BUILD, UIMP, UTEST |
| C21 | `rom_report.json`, `rom_contact_sheet.png`, `raw/pose_*` | AC-21 poses rest, guard (R shoulder 45° + elbow 90°), crouch (hips 80°, knees 110°, ankles −20°), arms overhead (155°): non-adjacent penetration 0.00 m in all four, no adjacent overlap; joint gaps ≤ 0.4 m (< 1 px at 48 px, 1.67 m/px). B7 core DOFs at R-005 range ends (hip flex −20/120, knee 0/135, ankle −20/40, shoulder flex −50/160, elbow 0/145, neck yaw ±70): worst neighbour overlap 0.68 % (knee 135°), no non-neighbour contact. All 34 R-005 DOF range ends: 64/68 runs ≤ 2 %; exceptions neck flexion −30° 2.54 %, +40° 2.70 % (neck locked at M0). Non-neighbour contacts in the cleared runs: shoulder adduction −10° and external rotation −60° (hanging forearm/hand into the thigh) | AC-21 pass; B7 core pass; full R-005 sweep **not met** for neck flexion and shoulder adduction/external rotation (see below) | ROM |
| C22 | `unity_hierarchy.txt`, map sockets; tests `Sockets_LeftRightMirrorSymmetric`, `ForearmSwap_ThroughSocketAndMappingOnly` | sockets shoulder, hip, elbow, hardpoint × L/R; positions mirrored to < 1 cm, forward/up mirrored; swap test replaces forearm_L with a stand-in on the elbow socket through `MechPartMap.Remap` only, other 14 parts unmoved | pass | UIMP, UTEST |
| C23 | `dimensions.json` parts[].convex_hull_ratio | max 1.58 (torso), pelvis 1.56, foot 1.54, shank 1.48, others 1.11–1.26 | pass (≤ 1.6) | MEAS |
| C24 | `dimensions.json`, test `Budget_Triangles_And_PaletteRoleMaterials` | 2,952 triangles; 0 textures; 4 slots `mech_primary/secondary/joint/accent` (`palette_roles.json`, R-002 entries) | pass | MEAS, UTEST |
| C25 | `art/concepts/mech_frame_balanced/` (4 layers), `PROVENANCE.md`, `concept_contact_sheet.png` | four layers kept with prompts, job IDs, rationale, AI-generated: yes; layer 3 plate breakup + plate-to-segment table in `DESIGN_NOTE.md` | pass (Phase A) | Phase A |
| C26 | `PROVENANCE.md` "Generation count: 11 of 16" | 11 generations; no purchase, settings change or terms; no block | pass (Phase A) | Phase A |
| C27 | `overlay_concept_vs_greybox.png`, `pixel_checks.json` | front IoU 0.393, side IoU 0.288 (concept middle view is a perspective 3/4) | **not met** (≥ 0.6 expected); departures below | COMP |
| C28 | `art/concepts/.../layer4_mood-cold_*.png` | no cluster-histogram artifact found in `evidence/T-007/` | open (Phase A item, not produced) | — |
| C29 | `unity_import_run1.log`, `unity_capture.log` (forced reimport section), `unity_hierarchy.txt`; test `Axes_YUp_FacingPlusZ_IdentityRotation_UnitScale` | model import: 0 warnings, 0 errors; root and all part rotations identity, lossy scale 1.000, Y up, toes at +z, L parts at −x, H 80 m | pass | UIMP, UCAP, UTEST |
| C30 | `unity_second_run_diff.txt`, `.gitattributes` LFS (`*.fbx`, `*.blend`) | second `MechFrameImport.Run` leaves `git status --porcelain -- Assets` empty; all new assets have `.meta`; prefab built by script (variant of the FBX), never text-edited; no colliders or ArticulationBody (asserted by test) | pass | UIMP |
| C31 | `MechFrameBalanced_PartMap.asset`, `unity_editmode.xml` (`MechFrameBalancedArtTests`, 8/8 pass; suite 109/109), `Assets/Game/Units/Mech/Art/ART_ASSETS.md` | 15 unique entries resolving to prefab transforms, IDs as in `mech_frame@v1`; test reads every dimension from the config | pass; `MODULE.md` deliberately not created (owned by T-005), assets and IDs listed in `ART_ASSETS.md` | UTEST |

## Not met, and why (values not relaxed)
- **C27 (IoU 0.39 front).** The concept's arms hang far out with forearms wider than the shoulder blocks. The JSON puts
  the shoulder joints 27 m apart (0.34 H); C11 caps the shoulder width at 0.40 H; C7 needs the shoulders to be the
  widest mass at 24 px. So the forearms stay inside 31.6 m and the arms hang 0.15 m beside 8 m-wide thighs (C12).
  Intentional departures from the concept: no masts or antennas; full-width shins (6.8 m) and ankles instead of thin
  linkages; flat 15 × 10 m feet instead of toed feet; closed fists; shoulder blocks set 3 m back, bevelled and 3 m below
  the head top (overhead reach, C21; head bump, C7); joint housings on the child part; no pelvis front skirt (hip flexion);
  hamstring and calf channels (C12 widths with C19 fractions); proportions from the JSON (legs 0.525 H, shoulder joints 0.34 H).
- **C21, R-005 DOFs outside B7's own list.** Neck flexion −30/+40° overlaps the collar ring by 2.5–2.7 % of the head
  (neck is a fixed joint at M0; fix when unlocked: cone-shaped collar opening). Shoulder adduction −10° and external
  rotation −60° drive the hanging forearm into the thigh: a stance consequence of the JSON joint spacing and the C12
  thigh width. Hip adduction/internal rotation contact the other leg only in the isolated run (feet 3 m apart by the
  JSON stance); with the other leg moved clear they pass. Proposed Q-NEW-a in `tasks/T-007.impl.md`.
- **C28** has no histogram artifact from Phase A; it is outside Phases B/C and was not produced here.

## Conflicts between the drafts' windows and R-005 (reported, R-005 kept)
- Design note shoulder width 0.33–0.37 H vs measured 0.395 H: the JSON shoulder joints are 27 m apart; any arm ≥ 4.4 m wide gives ≥ 0.39 H.
- Design note shoulder blocks level with the head top: kept 3 m lower for C7.
- R-005 foot sole 15 × 10 m rectangle: toe corners chamfered 1.2 m (area −0.96 %, inside the JSON's 2 % allowance) so the
  ankle sits within 15 % of foot length of the sole centroid (C14: exactly 15.0 % for the plain rectangle).
