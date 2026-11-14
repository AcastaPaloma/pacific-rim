PASS

# T-007 · eval-physics, fix cycle 2 re-check

Under test: branch `task/T-007` at `cc3acf3` (`.wt/T-007/kaiju`): `art/models/mech_frame_balanced/mech_frame_balanced.{fbx,blend}`, `evidence/T-007/{EVIDENCE.md,dimensions.json,rom_report.json}` (`dimensions.json` `commit_at_run` 69546b9). Spec `expectations/T-007.md`; config `research/R-005/mech_frame_balanced.json` (`mech_frame@v1`, unchanged).
Method: the same scripts as cycle 1, re-run in `/private/tmp/claude-501/eval-T-007-physics/c2/`. Headless Blender imported the FBX into a factory scene and opened a scratch copy of the `.blend` (never saved). The world-space triangles were measured in kaiju Python (trimesh/numpy). **FBX and `.blend` give identical results.** All 23 objects have scale 1 and rotation 0; scene unit scale is 1. ROM overlaps were spot-checked by winding-number Monte Carlo sampling.

## Cycle-1 defects

| Defect | Status | Evidence |
|---|---|---|
| DEF-T007-physics-1 (arm into thigh in M0-active DOFs) | **closed as recorded.** The contacts are smaller but still there. | `EVIDENCE.md` "Not met" lists every contact with depth and volume, L = R, plus the rest clearances (arm to thigh 0.95 m, was 0.15 m; feet 3.0 m apart). These match `rom_report.json` `summary.b7_contacts_isolated`. Remaining worst cases: shoulder adduction −10° hand–thigh 83.5 m³ / 4.64 m (mine 83.1 m³); waist lateral bend ±20° hand–thigh 72.1 m³ / 4.64 m; already 42.4 m³ at 10° (mine 43.0). Waist axial rotation now clears (no entry). These DOFs are outside B7's list, so C21 as written is met. The handoff rule itself sits only in `tasks/T-007.impl.md`; see DEF-4. |
| DEF-T007-physics-2 (neck flexion > 2 %) | **closed** | `rom_report.json` neck flexion −30° 0.11 %, +40° 0.004 % (mine 0.09 % and 0.00 %); neck rotation ±70° 0.0 %. Cone-shaped collar opening per `EVIDENCE.md` fix table. |
| DEF-T007-physics-3 (right side not swept) | **closed** | `rom_report.json`: 174 runs over isolated and cleared variants; all 29 R-005 DOFs at both range ends (58 per variant, 116 total); L and R entries are identical. Vertex mirror is still exact (0.00000 m), which supports the symmetry. |

## Criteria (stored → mine)

| C | Quantity | Stored | Re-measured | Threshold | Verdict |
|---|---|---|---|---|---|
| C10 | H; deck 67 m on torso; foot / human | 80.000; true; 8.33 | 80.0000 (y 0–80.0000); torso 45.5–77.0 m; 15.0 / 1.8 = 8.33 | 80 ± 0.5; crosses; ≥ 7 | pass |
| C11 | shoulder / H; shoulder / hip | 0.385; 1.54 | 30.8 (upper-arm outer ±15.38) / 80 = 0.385; 30.8 / 20.0 = 1.54 | 0.30–0.40; 1.4–2.0 | pass |
| C11 | head h / H; head w / shoulder | 0.0625; 0.208 | 5.0 m above the 75.0 m cowl (neck 3.09 m wide inside it) → 0.0625; 6.4 / 30.8 = 0.208 | 0.05–0.09; ≤ 0.30 | pass |
| C11 | forearm / upper arm; forearm / fist; fist / H | 1.171; 1.00; 0.0556 | 4.45 / 3.8 = 1.17; 4.45 / 4.45 = 1.00; 4.45 / 80 = 0.0556 | ≥ 1.1; ≥ 1; ≥ 0.055 | pass (zero and 0.0006 margins) |
| C12 | leg; thigh min; shank min (/ H) | 0.525; 0.100; 0.080 | 42 → 0.525; shaft 8.0 → 0.100 (hip ball 5.9–6.6 is the joint); 6.4 (depth) → 0.080 | 0.44–0.53; ≥ 0.10; ≥ 0.08 | pass (at limits) |
| C12 | foot L, W / H; W / shank; stance / shoulder | 0.1875, 0.125; 1.56; 0.747 | 15.0, 10.0 m; 10 / 6.4 = 1.56; 23.0 / 30.8 = 0.747 | 0.16–0.21, 0.10–0.14; ≥ 1.2; 0.70–0.90 | pass |
| C13 | volume centroid height vs CoM 42.54 m | 43.12 m, +0.0072 H | 43.120 m = 0.539 H, Δ +0.58 m = +0.72 % H; x 0.000; L/R volume diff 0.0000 % | ± 3 % H; < 1 % | pass |
| C14 | sole area per foot; flatness; L/W; ankle offset | 148.56 m²; 0.000; 1.5; 14.54 % | 148.56 m² L = R; y-spread 0.0000 m; 1.50; 2.181 m = 14.54 % of 15.0 m | 150 ± 10 %; ± 0.05 m; 1.3–2.2; ≤ 15 % | pass (feet unchanged) |
| C14 | m·g/A at 10,000 t | 660 / 330 kPa | 1e7 × 9.81 / 148.56 = 660.3 kPa one foot, 330.2 kPa two (§4: ≈ 650 kPa at 150 m², g = 9.8) | printed | pass |
| C17 | units, scales | 1, applied | unit scale 1.0; all scales 1.000, rotations 0 (FBX and `.blend`) | 1 u = 1 m | pass (Unity side not re-run) |
| C18 | joints, origins, segment lengths, mirror | 0.000 m; 0.00 %; 0.000 m | max 0.0000 m for 14 joints and 15 origins; all lengths 0.00 % (foot sole 15.0); mirror vertices and origins 0.00000 m | 2 %, 0.25 m, 1 cm | pass |
| C19 | closed manifold; total volume | 15/15; 9,644.6 m³ | 15/15 watertight, winding consistent; **9,644.6 m³** (3.6 % below the cap) | all; 5,000–10,000 | pass |
| C19 | implied mass 1,232.3 / 1,000 / 2,500 | 11.89 / 9.64 / 24.11 kt | 11.89 / 9.64 / 24.11 kt | config density in 6–15 kt; overlaps | pass |
| C19 | volume / mass fraction | 0.83, 1.04, 0.91, 0.75, 1.21, 1.42*, 0.80, 1.297, 1.26 | pelvis 0.832, torso 1.039, head 0.912, upper arm 0.753, forearm 1.212, hand 1.415 (*exempt, 1.0 % mass), thigh 0.804, shank 1.297, foot 1.257 | 0.70–1.30 | pass (shank 0.003, upper arm 0.053 margin) |
| C23 | hull / mesh volume | max 1.559 pelvis | pelvis 1.559, foot 1.542, torso 1.530, shank 1.480, thigh 1.314, head 1.368, others 1.139–1.236 | ≤ 1.6 | pass |
| C21 | AC-21 four poses | 0.00 m | pose angles meet AC-21 (crouch knees 110°, overhead 155°, guard shoulder 45° + elbow 90°); depths not re-run | ≤ 0.5 m | pass (stored) |
| C21 | B7 DOFs at R-005 ends; neighbour overlap | all ≤ 2 %, max 1.49 % (hip adduction −25°) | report confirms 116/116 ≤ 2 %, no B7-DOF non-neighbour contact; spot checks within 2 % of the stored volumes | ≤ 2 % | pass |

**Density at 10,000 t:** 1.0e7 kg / 9,644.6 m³ = **1,036.8 kg/m³**. That is 1.2 % above sea water (1,025); in cycle 1 it was 1,024. Fully submerged, the mech now sinks, but its apparent weight is only (1,036.8 − 1,025) × 9,644.6 × 9.81 ≈ 1.1 MN, about 1.2 % of its 98 MN weight. A buoyancy model that takes its volume from this mesh would feel almost weightless in the Golden Gate channel. Buoyancy should come from the config `hull_volume_m3` (8,115 m³), or this near-neutral behaviour should be accepted on purpose. This is an observation for the buoyancy and wading task, not a T-007 defect.

## New defects
**DEF-T007-physics-4 (minor, evidence).** The T-005 handoff rule for the arm–leg contacts (Q-NEW-b: collision filtering or range limits) appears only in `tasks/T-007.impl.md`, which evaluators may not read. `EVIDENCE.md` names it but does not state it. PASS: state the rule in `EVIDENCE.md` or as a ledger question, for example "T-005 ignores collisions between upper_arm/forearm/hand and pelvis/thigh on the same side, or limits shoulder adduction to ≥ 0°".

No blocker or major defects. Zero-margin items to protect in later edits: forearm = fist width, fist 0.0556 H, thigh and shank widths at their limits, shank fraction 1.297 / 1.30, ankle offset 14.54 % / 15 %.
