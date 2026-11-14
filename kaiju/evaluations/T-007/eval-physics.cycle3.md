PASS

# T-007 · eval-physics, fix cycle 3 re-check

Under test: branch `task/T-007` at `662ce6c` (`.wt/T-007/kaiju`): `art/models/mech_frame_balanced/mech_frame_balanced.{fbx,blend}`, `evidence/T-007/{EVIDENCE.md,dimensions.json,rom_report.json}` (`dimensions.json` `commit_at_run` 510a6f0), `Assets/Game/Units/Mech/Art/ART_ASSETS.md`. Spec `expectations/T-007.md` including v1.1 rulings (a) and (b); config `mech_frame@v1`, unchanged.
Method: the cycle-2 scripts re-run in `/private/tmp/claude-501/eval-T-007-physics/c3/`. Headless Blender imported the FBX and opened a scratch copy of the `.blend` (never saved); the measurements ran in kaiju Python (trimesh/numpy). **FBX and `.blend` give identical results.** All 23 objects have scale 1 and rotation 0; scene unit scale is 1. ROM spot checks used winding-number Monte Carlo.

## Criteria (stored → mine)

| C | Quantity | Stored | Re-measured | Threshold | Verdict |
|---|---|---|---|---|---|
| C10 | H; deck on torso; foot / human | 80.000; true; 8.33 | 80.0000 (y 0–80.0000); torso 45.5–77.0 m; 15.0 / 1.8 = 8.33 | 80 ± 0.5; crosses; ≥ 7 | pass |
| C11 | shoulder width (ruling b: torso + shoulder blocks) / H; shoulder / hip | 28.3 m = 0.354; 1.415 | torso ±14.15 → 28.3 m = 0.354; 28.3 / 20.0 = 1.415. Across the arms: 31.83 m = 0.398 (for reference only) | 0.30–0.40; ≥ 1.4 | pass (0.015 margin on shoulder/hip) |
| C11 | upper arm; forearm; fist (widths) | 4.80; 5.40; 4.75 m | shoulder ball 4.82 (shaft 4.60); forearm box 5.40; fist 4.75 → forearm / upper arm 1.12, forearm / fist 1.14, fist / H 0.0594 | ≥ 1.1; ≥ 1; ≥ 0.055 | pass (forearm / upper arm 0.025 margin) |
| C11 | head h / H; head w / shoulder | 0.0625; 0.226 | 5.0 m above the 75.0 m cowl → 0.0625; 6.4 / 28.3 = 0.226 | 0.05–0.09; ≤ 0.30 | pass |
| C12 | leg; thigh min; shank min (/ H); stance / shoulder | 0.525; 0.100; 0.080; 0.813 | 42 → 0.525; shaft 8.0 m (hip ball 5.5–6.6 is the joint; knee guard now reaches 23.3 m) → 0.100; 6.4 m → 0.080; 23.0 / 28.3 = 0.813 | 0.44–0.53; ≥ 0.10; ≥ 0.08; 0.70–0.90 | pass (widths at their limits) |
| C13 | volume centroid height vs CoM 42.54 m | 43.523 m, +0.0123 H | 43.523 m = 0.544 H, Δ +0.98 m = +1.23 % H; x 0.000; L/R volume diff 0.0000 % | ± 3 % H; < 1 % | pass (has risen +0.7 % H since cycle 1) |
| C14 | sole per foot; flatness; L/W; ankle offset; m·g/A | 148.56 m²; 0; 1.5; 14.54 %; 660 / 330 kPa | 148.56 m² L = R; 0.0000 m; 1.50; 14.54 %; 1e7 × 9.81 / 148.56 = 660.3 / 330.2 kPa | 150 ± 10 %; ± 0.05; 1.3–2.2; ≤ 15 % | pass (feet unchanged) |
| C17 | units, scales | 1, applied | unit scale 1.0; scales 1.000; rotations 0 | 1 u = 1 m | pass (Unity not re-run) |
| C18 | joints, origins, lengths, mirror | 0.000 m; 0.00 %; 0.000 m | 0.0000 m for 14 joints and 15 origins; lengths 0.00 % (foot sole 15.0); mirror vertices and origins 0.00000 m | 2 %, 0.25 m, 1 cm | pass |
| C19 | closed manifold; total volume | 15/15; 9,721.3 m³ | 15/15 watertight, winding consistent; **9,721.3 m³** (largest per-part difference: torso 3,267.57 vs 3,267.59) | all; 5,000–10,000 | pass (2.8 % below the cap) |
| C19 | implied mass 1,232.3 / 1,000 / 2,500 | 11.98 / 9.72 / 24.30 kt | 11.98 / 9.72 / 24.30 kt | in 6–15 kt; overlaps | pass |
| C19 | volume / mass fraction | in `volume_fractions`, all within | pelvis 0.825, torso 1.019, head 0.905, upper arm 1.017, **forearm 1.276**, hand 1.401 (exempt, 1.0 % mass), thigh 0.776, shank 1.247, foot 1.247 | 0.70–1.30 | pass (forearm 0.024 margin; thigh 0.076) |
| C23 | hull / mesh volume | max 1.589 torso | torso 1.589, pelvis 1.559, foot 1.542, shank 1.527, thigh 1.449, head 1.368, forearm 1.320, others 1.15–1.19 | ≤ 1.6 | pass (0.011 margin) |
| C21 | AC-21 four poses | 0.00 m, no adjacent overlap | `ac21_poses`: depth 0.0 and no adjacent overlaps in all four; angles meet AC-21 | ≤ 0.5 m | pass (stored) |
| C21 | R-005 DOFs both sides, both ends | 116/116, worst 1.62 % (shoulder abduction 150°) | report: 174 runs, all 29 DOFs at both ends; `b7_failures_adjacent` empty; worst 1.618 % confirmed in the entries. Spot checks: shoulder adduction 10° forearm–thigh 34.6 m³ (stored 34.3), hand–thigh 63.8 (64.5); neck flexion −30° 0.09 % (0.11) | ≤ 2 % | pass (0.38 % margin) |

**Density at 10,000 t:** 1.0e7 / 9,721.3 = **1,028.7 kg/m³**, 0.36 % above sea water (1,025). Fully submerged, the apparent weight is (1,028.7 − 1,025) × 9,721.3 × 9.81 ≈ 0.35 MN, 0.36 % of the 98 MN weight. If buoyancy is taken from this mesh, the mech is effectively neutrally buoyant. The buoyancy task should use the config `hull_volume_m3` (8,115 m³) or accept this on purpose. This is an observation, not a T-007 criterion.

## Defect status

| Defect | Status | Evidence |
|---|---|---|
| DEF-T007-physics-1 (arm into thigh, M0-active DOFs) | closed (recorded) | `EVIDENCE.md` "Not met" and `ART_ASSETS.md` §"Hand-off rule" list every contact. Their depths and volumes match `rom_report.json` isolated entries: waist lateral bend ±20° hand–thigh 4.44 m / 48.5 m³; shoulder adduction −10° 4.44 / 64.5; hip abduction +40° 4.44 / 41.3; external rotation −60° forearm 2.15 / 9.9; waist axial ±30° pelvis–forearm 0.20 / 0.6. Rest clearance 0.45 m, which I confirm: forearm inner face 10.45 m against thigh outer 10.0 m. The contacts are real and somewhat smaller than in cycle 2 (adduction hand 83.5 → 64.5 m³). They are honestly disclosed. |
| DEF-T007-physics-2 (neck flexion) | closed | 0.11 % / 0.004 % stored, 0.09 % / 0.00 % mine |
| DEF-T007-physics-3 (right side) | closed | all 29 DOFs, L = R entries |
| DEF-T007-physics-4 (rule only in impl notes) | **closed** | The rule is now stated in full in `EVIDENCE.md` (lines 78–80) and `ART_ASSETS.md` (lines 55–58): ignore same-side arm–pelvis/thigh collisions, or limit shoulder adduction to ≥ 0° and couple abduction to lateral bend and hip abduction; leg-on-leg contact stays enabled. |

New defects: none. Minor housekeeping, not a defect: the cycle-2 fix-table row in `EVIDENCE.md` (line 94) still says "arm stand-off 0.95 m". It is history; the current value is 0.45 m (line 72).
Tightest margins: forearm fraction 1.276 / 1.30, torso hull 1.589 / 1.6, shoulder/hip 1.415 / 1.4, ROM 1.62 % / 2 %, volume 2.8 % under the cap, thigh and shank widths at their limits.
