PASS

# T-007 · eval-style · fix cycle 3 re-evaluation (last cycle)

Evaluator: `eval-style`, 2026-11-14. Spec `expectations/T-007.md` v1.1 (rulings a and b applied). No blocker or major defect remains in my scope; C16 is at its bar (8/10), not above it. Two new minor defects.
Artifacts: task worktree `.wt/T-007/kaiju` at `662ce6c` (`E/` = `evidence/T-007/`, `K/` = `art/concepts/mech_frame_balanced/`). `tasks/T-007.impl.md` not read. Unity and Blender not run. Own measurements: kaiju-env Python on the PNGs (scratch in `/private/tmp/claude-501/eval-T-007-style/c3/`).

## Blind look (C7), done first
Answers written to `c3/blind_answers.txt` before `E/sealed/decoy_key.json` was opened; only the two blind sheets had been opened.
- 24 px sheet: mech = **B**; head = top-centre dash between two block lumps; arms = the two outer verticals ending in wider hands at hip height; legs = two lower columns with a gap.
- 48 px sheet: mech = **B**; head = centre top block between two lower blocks; arms = outer 3 px columns; legs = two lower columns.
- Key: mech is B on both sheets. Both correct.

## Defects

| Defect | Status | Evidence |
|---|---|---|
| DEF-T007-style-2 (major, C16 arms) | **closed** | `E/silhouette_front_full.png`, own scan at 25 px/m: upper arm 4.6 m (11.2–15.8 m), forearm 5.3–5.4 m (10.5–15.8 m), fist 4.7 m; `E/dimensions.json` shoulder ball 4.8 m ≥ fist 4.75 m. At 48 px the arm is 3 px for its whole length (was 2 px). C16 re-scored 8/10, below. |
| DEF-T007-style-8 (minor, departures, design note) | **closed** | `K/DESIGN_NOTE.md` § "Departures from the chosen concept" lists arms, stance, shanks, feet, fists, masts, block height and position, housings, skirt, each with its constraint. Signature forms, proportions and head text rewritten to the built forms; no "level with the head" or 0.33–0.37 H text left. |
| DEF-T007-style-9 (minor, knee sliver) | **closed** | `E/silhouette_side_full.png`: thinnest side depth between 22 and 30 m is 3.7 m at 25.7 m (was 1.4 m). `E/silhouette_side_48px.png` row 32 has 2 px; no empty row in front or side. |
| DEF-T007-style-10 (minor, concept hygiene, C28 index) | **closed** | Mood frame v2 and its check files are on `task/T-007`; `K/DESIGN_NOTE.md` § "Do not follow" covers the v2 masts and deck-head; `E/EVIDENCE.md` row C28 points at `mood_frame_check.json`. |
| DEF-T007-style-1, 3, 4, 5, 6, 7 | closed in cycle 2 | No regression found, except the 24 px top (DEF-11). |

## Criteria (cycle 3)

| C# | Verdict | Score | Measurement |
|---|---|---|---|
| C1, C2 | PASS | – | Prompt files unchanged since cycle 2 (`K/PROVENANCE.md`: 15 of 16). |
| C3 | PASS | 7.5/10 | Front, 3/4, side, back and flat renders: no existing design I can name for head, chest, shoulders, colours or silhouette. Watch items: blocks stay plain square solids (no launcher faces); 24 px top, see DEF-11. |
| C4 | PASS | – | No crest, fins, core, face, livery, emblem, text; one orange panel; cowl V notch unchanged. |
| C5 | PASS (minor) | 2.5 of 3 | 48 px front: head 3 px rows 0–1, blocks 3 px rows 2–5, chest taper 11 → 3 px rows 9–18; side chest 9–10 px vs 5 px thigh. Form 3: the arm is a 3 px box column, but the forearm step over the upper arm (0.8 m) is under a pixel at 48 px, so the forearm reads by the elbow band and pale plate in colour only (DEF-11). |
| C6 | PASS | – | Own scan, 48 px front: arm-to-torso gap on rows 9–29 = 21 of 22 arm rows (95 %); crotch gap 3 px rows 25–46, 1 px row 47; feet 6 px vs shank 4 px. Side: toe and heel project, chest distinct. |
| C7 | PASS (marginal) | – | Blind look correct. 24 px: head 2 px bump, narrower than the 8 px shoulder mass; legs separate by 2 px on rows 12–23. Shoulder clause: the outline is 10 px wide from the shoulder row down and nothing exceeds it, but the widest solid run is now the hip band (DEF-11). Same reading as cycles 1 and 2. |
| C8 | PASS | – | `E/pixel_checks.json`: bands 40 / 80 / 88 / 184. |
| C9 | PASS | IoU 0.522 | `E/pixel_checks.json`. |
| C10, C12 (visual) | PASS | – | Markers as before; thigh and shank still on their lower limits. |
| C11 under ruling (b) | PASS (thin margin) | 0.354 H; 1.415 | Own scan: the outer shoulder-armour plate reaches ± 14.1 m between 69.3 and 72.2 m, so 28.2–28.3 m on torso and blocks; hips ± 10.0 m. Ratio 1.41 against 1.4. The pass rests on a plate about 3 m tall; the chest shell itself is 22.8 m. Overall width with arms 31.8 m. |
| C15 | PASS | – | Shoulder balls seated under the armour plate; elbow drums, knee guard over the knee drum, ankle drums, waist ball in the trunk. |
| C16 | **PASS** | **8/10** (bar 8) | Below. |
| C25, C26 | PASS | – | Layers, provenance, merged list. `E/EVIDENCE.md` row C26 still says "11 of 16" (DEF-12). |
| C27 | PASS as written (v1.1) | IoU 0.395 front, 0.550 arms masked, 0.320 side | Same major masses and three signature forms; every departure listed with its constraint, arms included. |
| C28 | PASS | 6.5/10 style match | Same frame and check as cycle 2 (`E/mood_frame_check.json` points at `layer4_mood-v2_af67b340…_0.png`). |

## C16 weight read: 8/10
- **Arms now carry weight.** Upper arm 0.058 H, forearm 0.068 H, fist 0.059 H, each an armoured box, hung from a 4.8 m housing under a shoulder plate. In front and back views the arms are in proportion to the 8 m thighs; the cycle-2 "wires on a hull" read is gone.
- Kept from cycle 2: side chest depth 16.0 m at 66 m (0.20 H), deeper than the pelvis (13.9 m); no neck stalk; trunk 10.6 m wide to 51 m; upright in every view.
- The knee no longer breaks the leg in side view.
- Read across the four turnaround views: a heavy, upright, shipyard-built biped; heroic V from 31.8 m across the arms to a 20 m hip.
Why not higher (not defects, notes for the plate and detail pass):
- Feet are still thin sole plates under an exposed ankle drum; the foot looks under-built for the mass above it.
- Thigh (0.100 H) and shank (0.080 H) sit exactly on the minimum widths; the legs are the lightest-looking part now.
- Hip and shoulder housings are still bare spheres at greybox level.

## New defects
**DEF-T007-style-11 · minor · C3 watch item, C5, C7.** Small-scale read is sensitive to sampling phase now that the frame is an even number of pixels wide.
- 24 px top (`E/silhouette_front_24px.png` rows 0–2): a 2 px head dash on row 0, an empty centre on row 1, two 2 px block lumps on rows 1–2. That is three lumps, not the "one flat-topped mass" the design note describes, and it leans back toward the eared read closed in cycle 2. Cause: the 1.3 m slots between blocks and cowl are under a pixel and appear or vanish with phase.
- 24 px rows 10–11: forearms and hands fuse with the pelvis into a 10 px solid bar (`E/pixel_checks.json` `24px_widest_row_m` = 45.0), as wide as the shoulder outline. Expected under ruling (a); C7's "shoulders widest" holds on outline only.
- 48 px: forearm and upper arm are the same 3 px.
PASS (later art pass, not this task): join the blocks to the cowl below about 75 m so the top is one mass at any phase; give the forearm a full-pixel step at 48 px (≥ 1.7 m) in depth or by a plate that breaks the outline; test silhouettes at two sampling phases.

**DEF-T007-style-12 · minor · evidence index.** `E/EVIDENCE.md` row C26 says 11 of 16 generations (PROVENANCE: 15 of 16); row C7 says head 1 px (JSON: 2 px); `E/pixel_checks.json` `24px_leg_rows_with_gap` is 0 while the image shows a 2 px gap on 12 rows and the index says 10. PASS: index and JSON agree with the files.

## Not lost
- C16 passes at the bar with no margin. A second panel member could fairly score it 7 on the feet and legs alone. If the founders want margin, the reachable levers inside `mech_frame@v1` are foot block height and shank depth (shank and foot volume fractions are at 1.25 of a 1.30 limit, total volume 9,721 of 10,000 m³), so the room is small; a real gain needs the leg-width floors or the volume cap revisited, which is a founder-level look question for the Balanced chassis, together with the open one on how far outboard the arms may hang.
- Proposed metric from cycle 1 still stands (unregistered): side-view chest depth / H.
