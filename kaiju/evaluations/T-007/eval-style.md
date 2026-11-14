FAIL

# T-007 · eval-style · original mech frame (Balanced chassis)

Evaluator: `eval-style`, 2026-11-14. Spec `expectations/T-007.md` v1 (C1–C9, C10–C12/C15/C16 visual read, C25–C28).
Artifacts judged in worktree `.wt/T-007/kaiju` (branch `task/T-007`): `evidence/T-007/` (`E/` below), `art/concepts/mech_frame_balanced/` (`K/` below).
`tasks/T-007.impl.md` not read. Unity and Blender not run. Own measurements: kaiju-env Python on the evidence PNGs (scratch in `/private/tmp/claude-501/eval-T-007-style/`).
Registered metrics M-003/M-004 (`Tools/Metrics`) were not run: no pixel-pipeline frame exists for this task (T-004 pending). All FAILs below rest on spec criteria, not on unregistered metrics; unregistered measures are marked "observation".

## Criteria

| C# | Verdict | Score | Supporting artifact and measurement |
|---|---|---|---|
| C1 prompt hygiene | PASS | – | Read all 11 submitted prompts in `K/PROMPTS.md` and all of `K/PROVENANCE.md`, `K/DESIGN_NOTE.md`; own private-list grep: 0 true hits (only "horizon", "trapezoid"). Tool names (generator, browser) appear in PROVENANCE as required provenance fields only. `E/banned_terms_check.txt` final run 0 matches. |
| C2 no uploads | PASS | – | `K/PROVENANCE.md`: image references are own CDN URLs of jobs 4d4eda80 img 1 and 2fa84a4e img 2 only; prompts P2-c, P3-a2, P4-a2, P4-c match. No style ref described or sent. |
| C3 blind resemblance | PASS (watch) | 7/10 | Greybox (`E/turnaround_{front,side,three_quarter}.png`, `E/flat_front_180px_x4.png`): cannot name a specific existing design for head, chest, shoulders, colours or silhouette. It reads as a generic blocky ball-jointed humanoid robot (see DEF-2), which is a weakness of identity, not a copy. Two watch items in DEF-4. No cape, fur, tusks, banner, rider, helm. |
| C4 genre identifiers | PASS (minor) | – | Greybox: no V-crest, fins, glowing core, face (one visor slit only), livery, emblem, text. One 2.2 m orange square, viewer-right chest. Kept concepts carry thin mast rods and extra warm lights: DEF-6. |
| C5 own identity | FAIL (part of DEF-1) | 1.5 of 3 forms | `E/silhouette_front_48px.png` rows 0–4: blocks present (4 px each, cols 2–5 / 17–20) but 2 rows below the head top and 4 px away from it: "ears beside a stalk", not the note's "two-tower top edge with the head as a low notch". Bow chest: found (rows 9–15 taper 11 → 7 px). Box forearm: not findable, arm is 3 px wide for all of rows 9–23, upper arm and forearm identical. |
| C6 48 px separations | PASS (marginal) | – | Own row scan: head rows 0–1 alone; blocks rows 2–4; arm/torso gap on 15 of 23 arm rows (65 %); crotch gap 3 px rows 25–46; feet 6 px vs shank 4 px (one row only). Side: toe cols 8–10 and heel cols 2–4 project past shank cols 5–7. Weak points in DEF-5. |
| C7 24 px read | PASS (measured), blind look compromised | – | `E/silhouette_front_24px.png`: head 1 px (rows 0–1), widest continuous row = shoulders (9 px, row 4), leg gap rows 12–23. Blind look: see DEF-7. |
| C8 180 px bands | PASS | – | `E/flat_front_180px.png`, `E/pixel_checks.json`: luminance bands 40 / 80 / 88 / 184 (80 and 88 are one band to the eye, so 3 visible bands + accent). Read is carried by blocks ≥ 2 m. |
| C9 machine not person | PASS | IoU 0.494 | `E/overlay_human_iou.png` (viewed), `E/pixel_checks.json` 0.494 at 48 px. Note: passes on width only; limb proportions are human (DEF-2). |
| C10 markers (visual) | PASS | – | `E/turnaround_sheet.png`: 80 m line at head top; 67 m dashed line crosses upper chest and shoulder balls; red 1.8 m figure at the left toe. |
| C11 upper body (visual) | PASS numerically, weak read | – | `E/dimensions.json`: forearm 5.10 m vs upper arm 4.45 m (1.146×): 0.65 m, under half a pixel at 48 px; not a visible difference (DEF-1, DEF-2). |
| C12 legs (visual) | PASS | – | Columns read as columns; thigh 0.100 H and shank 0.080 H sit exactly on the lower limits. |
| C15 believable joints | PASS (minor note) | – | Ball housings at shoulder and hip, drums at elbow, knee, ankle, wrist band, neck collar all visible in front/side/back renders; paired knee rams visible front, paired strips on the back of the arms. Load path shoulder → hip passes through a 5.8 m × 7.0 m waist column (own scan, y = 50–52 m): continuous, but visually the weakest member (DEF-2). |
| C16 panel read | **FAIL** | **5/10** (bar 8) | DEF-2. |
| C25 four layers + provenance | PASS (minor) | – | 8 kept files, each with tool, prompt ID, parameters, job ID, date, rationale, AI-generated: yes. Layer 3 plate sheet shows plates and seams; plate-to-segment assignment is only in the `K/DESIGN_NOTE.md` table, and the sheet's mech has a different head and shoulders (DEF-6). |
| C26 generation count | PASS | 11 of 16 | 11 jobs in PROVENANCE = 11 submitted prompts in PROMPTS.md; no purchase, settings change, terms, block. |
| C27 traceability | **FAIL** | IoU 0.393 front, 0.288 side (≥ 0.6 expected) | DEF-1. |
| C28 mood frame | **FAIL** | style match 4/10 (cold), 5/10 (warm) | DEF-3. |

## C16 weight read: 5/10
For: upright; legs 0.525 H on 8 m thighs; 15 × 10 m flat feet; stance 0.73 of shoulder width; shoulder/hip 1.53 gives a heroic V in front view.
Against, each visible on `E/turnaround_sheet.png` and measured on `E/silhouette_{front,side}_full.png` (25 px/m):
- **Side profile is a plank.** Chest depth 9.2–10.8 m (0.12–0.135 H, a human's ratio), less than the pelvis (13.9 m). Nothing projects forward; there is no bow. At 48 px side the chest is 5–7 px against a 5–6 px thigh.
- **Pinched waist.** 5.8 m wide × 7.0 m deep column for 4.5 m of height, under a 24 m chest and over a 16 m pelvis.
- **Head on a stalk.** Neck/collar 5.8 m wide exposed from about 71.5 to 74 m with 6.6 m of open air on each side before the shoulder blocks; head top 3 m above the blocks.
- **Arms of human proportion** (0.056–0.064 H wide), dead straight, forearms and hands 0.35–0.7 m from the thighs (`E/dimensions.json` bounding boxes); at 48 px rows 24–29 the hands fuse with the thighs into a band as wide as the shoulders.
- **Toy cues.** Exposed sphere shoulders on a thin yoke bar, sphere hips, thigh and shank at the exact minimum widths, thin sole plates with the ankle drum sitting on top like a display stand.
Read: a posable artist's mannequin or boxed toy robot at 80 m, not thousands of tonnes. Lanky from the side, toy-like from the front.

## C27 judgement: identity partly lost
Still traceable: inverted-trapezoid chest in front view, pale blocks above the shoulders, pale thigh slabs, one orange square, column legs, flat feet. Not recognisable as the chosen concept (`K/layer2_turnaround-final_*`, chosen figure of `K/layer1_silhouettes-A_*`): the concept is a top-heavy brawler (deep prow chest, head sunk in the chest deck between blocks that nearly touch it, forearm boxes about twice the upper arm); the greybox is a slim upright figure with a neck. `E/overlay_concept_vs_greybox.png` shows the greybox as a thin core inside the concept mass in both views.
Departures I accept as forced by the dimension source and C7/C11/C12: no masts; full-width shanks and ankles; flat untoed feet; closed fists; legs 0.525 H; **forearm front width** (shoulder joints at ± 13.5 m, thigh edge at 10.3 m, C7 needs the shoulders widest: the arm has about 5–6 m of front width available).
Departures not forced by `mech_frame@v1` (joints stay where they are in all of the following):
1. **Sink the head.** Add a collar/cowl to the torso part between the blocks, top at about 75 m (neck joint is at 71.0 m), so only the sensor cap shows. Head stays the top row at 24 px (blocks stay ≤ 77 m), so C7 holds.
2. **Blocks to the head.** Move the two blocks inboard (inner faces about 2 m from the head, keeps a 1 px gap at 48 px), square them (drop the lower-outer bevel that makes them read as ears). Shoulder width unchanged (they move inward).
3. **Give the chest its bow.** Extend the upper chest forward from z = +6.0 m to about +9 to +10 m (toe line is 9.75 m) at 60–68 m height, tapering to the keel, so side-view chest depth is ≥ 16 m (0.20 H) and exceeds the pelvis. Shorten the exposed waist column by carrying the keel down to the pelvis top.
4. **Forearms deeper, not wider**, with the off-white outer face kept; then reword signature form 3 in the design note, or raise a founder question on shoulder-joint spacing (27 m) against the 0.40 H cap.
Budget: total volume is 9,762 of 10,000 m³, so 1–3 must be volume-neutral (torso is at 0.89 of its mass fraction and can grow to 1.30; pelvis rear box, shank, foot and forearm sit at 0.91–1.28 and can give volume). Re-measure C13, C19, C21 (overhead reach with inboard blocks) and C23 after the change.

## Defects
**DEF-T007-style-1 · major · C27, C5.** Greybox does not carry the chosen concept's identity. Evidence: front IoU 0.393, side 0.288 (`E/pixel_checks.json`, `E/overlay_concept_vs_greybox.png`); 48 px row scan above (C5 row). PASS: changes 1–3 above (or equivalent) so that head-between-blocks, bow chest in side view and box forearm are each findable at 48 px; front IoU reported again with every remaining departure tied to a named constraint; if ≥ 0.6 stays out of reach because of arm width, say so with the arm-masked IoU.

**DEF-T007-style-2 · major · C16.** Panel score 5/10 against ≥ 8. Evidence: measurements in the C16 section. PASS: side-view chest depth ≥ 0.20 H and greater than the pelvis; no exposed waist column narrower than about 10 m; no exposed neck stalk; shoulder housings read as part of the chest mass, not spheres on a bar; re-scored ≥ 8.

**DEF-T007-style-3 · major · C28.** Final mood frame `K/layer4_mood-cold_*` fails two of the four check items, and the required histogram artifact is absent from `E/` (EVIDENCE.md row C28: "open"). Own check (k-means k = 8 on a ¼ downscale):
- Hue families: warm cream 56.7 %, neutral 31.7 %, grey-teal 11.6 %: 3 families, pass. But the sky is warm cream (223, 215, 203), not the cold slate-teal family the spec names.
- Accent: one orange point, 0.003 % of pixels, pass (≤ 10 %), barely visible, and it sits on the torso centreline.
- Sky mean luminance 0.84 vs mech 0.14: pass.
- **Water line: fail.** Soles rest on the surface with a reflection; no part of the ankle or shin is submerged (spec: ankle to mid-shin).
- **True-scale objects: fail.** Mech about 749 px tall; people about 53 px (5.7 m at 80 m; 1.8 m would be 17 px); cars on the pier behind the foot about 83 px long (8.9 m). The frame reads as a 30–40 m machine.
- Mech differs from the turnaround: mid-stride, arms raised and forward, taller turret head with rods.
- Observation (unregistered): 25,801 unique colours, 144 luminance levels each ≥ 0.2 %, no alternating-pixel pattern in the sky (0.000): not posterised, not dithered. Closest reference is ref_01 top-right in value structure only.
The kept warm frame (`K/layer4_mood-warm_*`) has better scale cues and sky banding but shows the earlier design, not the final one.
PASS: one kept frame of the final design standing, water between ankle and mid-shin, people ≤ 2.5 % and cars ≤ 6.5 % of mech height at the mech's depth, cold slate-teal family with one visible off-centre accent, plus the cluster histogram in `evidence/T-007/`. 5 generations remain; a composite from the greybox pixel-proxy render is an acceptable route.

**DEF-T007-style-4 · minor · C3, C7.** At 24 px (`E/silhouettes_x8_24px_blind.png`, figure A) the top reads as a 1 px spike between two 2 px lumps: a horned or eared head, the one subject trait of the references the spec bans by name. Second watch item: the concept's pale shoulder boxes plus forearm pods lean toward the genre's missile-rack walker archetype (family resemblance, no single nameable design). PASS: fix 1–2 of DEF-1 gives a flat-topped shoulder mass with the head as the bump; keep the blocks as solid armour, no launcher faces.

**DEF-T007-style-5 · minor · C6.** 48 px front: hands fuse with thighs on rows 24–29 (clearance 0.35–0.7 m), making a hip band as wide as the shoulders; feet are distinct for one row only. 48 px side: chest not distinct from thigh width. PASS: ≥ 1 px (1.7 m) between hand and thigh at 48 px, or hands ending above the thigh's widest part; side chest ≥ 2 px deeper than the thigh.

**DEF-T007-style-6 · minor · C4, C25.** Kept concepts: thin mast rods on head and blocks (`layer2_turnaround_4d4eda80*`, `layer3_plate-breakup*`, `layer4_mood-cold*`); two orange coil dampers at the knees in the plate sheet (more than one accent); plate sheet's head and shoulders are a different design and the sheet itself does not mark plate-to-segment grouping. PROVENANCE flags the masts and the drift; the design note removes them. PASS: an annotated or regenerated layer 3 of the final design, or an explicit "not to be followed" list per kept file in the contact sheet.

**DEF-T007-style-7 · minor · C7 evidence.** The blind test does not discriminate: both decoys (`E/silhouette_decoy_key.json`: a crane and a tower) are non-humanoid, and the mech is label A on both sheets. Also my own look was not blind: I opened the key file while indexing the evidence, before viewing the sheet (evaluator error; the orchestrator may want a second blind look). With that caveat: head, arms and legs of A are pointed to correctly, but A reads as a thin stick figure with a spiked top, not a giant. PASS: decoys that are humanoid-adjacent (a lattice pylon with arms, a scaled person, a two-legged gantry), random label order, key stored outside the evidence folder index.

## Not lost
- Proposed founder question (for the scribe to number): the concept's arm mass cannot fit `mech_frame@v1` shoulder spacing (27 m) under the 0.40 H shoulder-width cap. Options: keep the cap and accept slim arms (current); measure C11 shoulder width on the torso and blocks only; or widen the cap for the Balanced chassis. Recommended: measure on torso and blocks only, keep C7.
- Proposed metric (unregistered, did not fail anything here): side-view chest depth / H as a companion to the front-view ratios in C11; the spec has no depth criterion, which is how a 0.13 H chest passed every numeric check.
