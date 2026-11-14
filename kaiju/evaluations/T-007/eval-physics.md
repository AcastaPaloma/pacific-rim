PASS

# T-007 · eval-physics (Balanced mech greybox as the visual body of a 10,000 t biped)

Spec: `expectations/T-007.md` C10–C14, C17–C19, C21, C23 (with AC-10..22, B1–B5, B7, B9, B10, B14, B17). Config: `research/R-005/mech_frame_balanced.json` (`mech_frame@v1`).
Artifacts under test (branch `task/T-007`): `art/models/mech_frame_balanced/mech_frame_balanced.fbx` and `.blend`, `evidence/T-007/{EVIDENCE.md,dimensions.json,rom_report.json}`.
Method: no FBX loader in the kaiju env, so Blender 5.3 headless imported the **FBX** into a factory-startup scene (and, separately, opened a scratch copy of the `.blend`; never saved) and dumped world-space triangles and object transforms. All measurements below were computed in kaiju Python (trimesh/numpy) from that dump, not from `dimensions.json`. FBX and `.blend` give identical numbers. ROM overlaps were spot-checked by Monte Carlo winding-number sampling (60k points per pair). Scripts and outputs are in `/private/tmp/claude-501/eval-T-007-physics/` (`dump.py`, `analyse.py`, `slices.py`, `rom.py`). Frame: Blender (X, Y, Z) → Unity (x, y = Z, z = Y); toes at +z, L at −x.

## Criteria

| C | Quantity | Stored | Re-measured | Threshold | Verdict |
|---|---|---|---|---|---|
| C10 | H (sole to head top) | 80.000 m | 80.0000 m (y 0.0000–80.0000) | 80.0 ± 0.5 | pass |
| C10 | shoulder line / deck / head | chest top 69.5; deck crosses torso; head from 70.1 | torso part 45.5–77.0 m (shoulder blocks 73.8–77.0 sit on it); chest shell top 69.5; head part 70.1–80.0 | 62–70; 67 m crosses upper chest; head above | pass (shoulder line read as chest shell, as AC-10 defines it) |
| C10 | foot length / 1.8 m human | 8.33 | 15.00 / 1.8 = 8.33 | ≥ 7 | pass |
| C11 | shoulder width / H; shoulder / hip | 0.395; 1.534 | 31.6 / 80 = 0.395; 31.6 / 20.6 = 1.534 | 0.30–0.40; 1.4–2.0 | pass (0.005 margin) |
| C11 | head h / H; head w / shoulder | 0.0775; 0.203 | visible head 73.8–80 = 6.2 m → 0.0775; 6.4 / 31.6 = 0.203 | 0.05–0.09; ≤ 0.30 | pass |
| C11 | forearm / upper arm; forearm / fist; fist / H | 1.146; 1.109; 0.0575 | 5.10 / 4.40–4.45 = 1.15–1.16; 5.10 / 4.60 = 1.11; 4.60 / 80 = 0.0575 | ≥ 1.1; ≥ 1; ≥ 0.055 | pass |
| C12 | leg / H; thigh min / H; shank min / H | 0.525; 0.100; 0.080 | 42.0 / 80 = 0.525; shaft 8.0 m = 0.100 (hip ball 6.6–7.2 is the joint); 6.4 m (depth) = 0.080; mid ≥ joints | 0.44–0.53; ≥ 0.10; ≥ 0.08 | pass (zero margin on both widths) |
| C12 | foot L/H, W/H, W/shank; stance / shoulder | 0.1875, 0.125, 1.56; 0.728 | 15.0, 10.0 m → 0.1875, 0.125; 10 / 6.4 = 1.56; 23.0 / 31.6 = 0.728 | 0.16–0.21, 0.10–0.14, ≥ 1.2; 0.70–0.90 | pass |
| C13 | volume centroid height vs R-005 CoM | 42.464 m, Δ −0.001 H | 42.464 m = 0.5308 H; config 42.5405; Δ −0.077 m = −0.10 % H | ± 3 % H (± 2.4 m) | pass |
| C13 | mid-plane (L/R volume difference) | 0.000 % | 0.0000 %; centroid x = 0.000 | < 1 % | pass |
| C14 | sole area per foot (down-facing faces at y = 0) | 148.56 m² | 148.56 m² L and R (4 triangles, chamfered rectangle) | 150 ± 10 %, L/R ≤ 1 % | pass |
| C14 | flatness; L/W; ankle over sole centroid | 0.000; 1.5; 2.181 m = 14.54 % | sole y-spread 0.0000 m; 15.0 / 10.0 = 1.50; centroid 2.181 m toe-ward of ankle = 14.54 % of 15.0 m | ± 0.05 m; 1.3–2.2; ≤ 15 % | pass (0.46 % margin) |
| C17 | units, scales | scale 1, H 80.000 | unit scale 1.0; all 23 objects scale 1.000, rotation 0 (FBX and `.blend`) | 1 u = 1 m, scales applied | pass (Unity side not re-run: no Unity for evaluators here) |
| C18 | joint positions (14) and origins (15) | 0.000 m | max deviation 0.0000 m | 2 % / 0.25 m | pass |
| C18 | segment lengths (15) | 0.00 % | all 0.00 % (joint-to-joint from child origins; head 9.0, hand 7.5 from mesh extent; foot sole 15.0) | ≤ 2 % | pass |
| C18 | L/R mirror (vertices, origins) | 0.000 m | max nearest-neighbour 0.00000 m, origins 0.00000 m (6 pairs) | ≤ 1 cm | pass |
| C19 | closed, manifold | 15/15 | 15/15 watertight and winding-consistent | all | pass |
| C19 | total volume | 9,762.3 m³ | 9,762.3 m³ | 5,000–10,000 | pass (2.4 % below the cap) |
| C19 | implied mass at 1,232.3 / 1,000 / 2,500 kg/m³ | 12.03 / 9.76 / 24.41 kt | 12.03 / 9.76 / 24.41 kt | config-density mass in 6–15 kt; range overlaps 6–15 | pass |
| C19 | volume / mass fraction | 0.91, 0.89, 1.07, 0.94, 1.22, 1.65*, 0.90, 1.28, 1.24 | pelvis 0.914, torso 0.893, head 1.065, upper arm 0.937, forearm 1.217, hand 1.649 (*1.0 % mass, exempt), thigh 0.900, shank 1.281, foot 1.242 | 0.70–1.30 (< 2 % exempt) | pass (shank 0.019 margin) |
| C23 | convex-hull / mesh volume, max | 1.58 torso | torso 1.578, pelvis 1.562, foot 1.542, shank 1.480, rest 1.105–1.257 | ≤ 1.6 | pass (0.022 margin) |
| C21 | AC-21 four poses | 0.00 m non-adjacent | angles meet AC-21 (crouch knees 110°, overhead 155°, guard fist ≈ 67 m high); depths not re-run | ≤ 0.5 m | pass (stored) |
| C21 | B7 list at R-005 ends (hip flex −20/120, knee 0/135, ankle −20/40, shoulder flex −50/160, elbow 0/145, neck yaw ±70) | worst 0.68 %, no non-neighbour | report entries confirm; spot-checked overlaps reproduce stored values within 4 % (below) | ≤ 2 %, no non-neighbour | pass |

## Physics the report must state (recomputed, g = 9.81)
- **Static ground pressure, 10,000 t:** one foot 1.0e7 × 9.81 / 148.56 = **660.3 kPa**; two feet **330.2 kPa**. Stored 660.3 / 330.2. GAME_BREAKDOWN §4 gives ≈ 650 kPa for 150 m² with g = 9.8 (653 kPa); the 1 % smaller sole explains the difference. Any of these is 2.2–6.6× typical soil bearing (100–300 kPa), so sinking and cratering are expected, as §4 says.
- **Implied mass:** 9,762.3 m³ × 1,232.3 = 12.03 kt; × 1,000 = 9.76 kt; × 2,500 = 24.41 kt. The 9.8–24.4 kt range overlaps 6–15 kt, and 12.0 kt at the config density lies inside it.
- **Observation (not a defect: no criterion covers it):** the mesh holds 20 % more volume than the R-005 hull (9,762 vs 8,115 m³). At the simulated 10,000 t the mean density over the visible volume is **1,024 kg/m³**, the same as sea water (1,025). If T-005 or a later buoyancy task takes the displaced volume from the mesh instead of from `hull_volume_m3`, a fully submerged mech in the Golden Gate channel is neutrally buoyant (R-005 §1.4, F-045). Set buoyancy from the config hull, or revise mass or volume on purpose.

## Range of motion: spot checks (mine vs stored)
neck flexion +40°: 5.85 m³ = 2.8 % of the head (stored 2.70 %); −30°: 5.45 m³ = 2.6 % (stored 2.54 %). Shoulder_L adduction 10°: forearm–thigh 45.3 m³ (stored 45.0), hand–thigh 113.6 m³ (113.8). Waist lateral bend 10°: forearm–thigh 10.4 m³ (10.3), hand–thigh 61.5 m³ (60.8). The ROM tool's numbers hold up.
Coverage: `rom_report.json` has all four AC-21 poses and both range ends of the 17 left-side and central DOFs in the R-005 file. It also covers the 6 B7 joints in isolated and cleared variants. The 12 right-side DOFs were not run, but the exact mirror (0.00000 m) makes their results identical.

## Defects

**DEF-T007-physics-1 (minor; outside C21 as written, which tests B7's DOF list).** At rest the arms hang 0.15 m beside the 8 m thighs, so several **M0-active** DOFs push arm into leg: shoulder adduction −10° (hand into thigh 113.8 m³, depth 4.9 m; forearm 45 m³, depth 3.8 m), shoulder external rotation −60° (forearm 11.3 m³, depth 2.1 m). In the isolated runs, contact also appears for waist lateral bend ±20° (hand 105.6 m³, depth 4.9 m; already 61 m³ at 10°), waist axial rotation ±30° (pelvis–forearm depth 0.73 m) and hip abduction +40° (hand 99.4 m³, depth 4.6 m) (`rom_report.json` b7 entries). Waist lateral bend and hip abduction are the lateral balance strategies T-005's controller will use. With arm–leg link collisions enabled, these contacts would put thousands-of-tonne contact impulses into the balance loop. `EVIDENCE.md` C21 lists only the shoulder cases and the hip adduction/rotation leg-on-leg cases; it leaves out the waist and hip-abduction arm contacts. PASS looks like either (a) the stated rule for T-005 to disable collisions between upper-arm/forearm/hand and pelvis/thigh, or to couple shoulder abduction to waist bend and hip abduction, or (b) R-005 ranges narrowed (shoulder adduction ≥ 0°), or (c) the arm stand-off widened. Whichever is chosen, it is recorded as a Q-NEW / handoff note.

**DEF-T007-physics-2 (minor; neck flexion is not in B7, which lists neck yaw, and the neck is `locked` at M0).** Neck flexion −30°/+40° overlaps the collar by 2.54/2.70 % of the head (mine 2.6/2.8 %), against the 2 % neighbour limit. No effect on the M0 simulation. PASS: ≤ 2 % at both R-005 ends (the cone-shaped collar opening proposed in `EVIDENCE.md`) before any task unlocks the neck.

**DEF-T007-physics-3 (minor, evidence completeness).** `rom_report.json` sweeps only the left and central joints (17 of 29 DOFs). The right side follows by exact mirror symmetry, which I verified, but neither the report nor `EVIDENCE.md` says the right side was inferred. PASS: run the right-side DOFs, or state the mirror inference with the 0.000 m mirror check as its basis.

No blocker or major defects. Margins worth tracking for later edits: thigh and shank widths at exactly 0.100/0.080 H, ankle offset 14.54 % of a 15 % limit, total volume 2.4 % under 10,000 m³, shank fraction 1.281 of a 1.30 limit, torso hull ratio 1.578 of a 1.6 limit.
