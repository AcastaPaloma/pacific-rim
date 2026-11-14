FAIL

# T-007 · eval-style · fix cycle 2 re-evaluation

Evaluator: `eval-style`, 2026-11-14. Spec `expectations/T-007.md` v1. One criterion still under its bar: **C16 at 7/10 (bar 8)**. Everything else in my scope now passes.
Artifacts: task worktree `.wt/T-007/kaiju` at `cc3acf3` (`E/` = its `evidence/T-007/`); integration checkout at `a1df4ec` for the mood frame (`I/` = `evidence/T-007/`, `K/` = `art/concepts/mech_frame_balanced/`).
`tasks/T-007.impl.md` not read. Unity and Blender not run. Own measurements: kaiju-env Python on the PNGs (scratch in `/private/tmp/claude-501/eval-T-007-style/c2/`). Unregistered measures are marked "observation" and fail nothing.

## Blind look (C7), done first
Answers written to `c2/blind_answers.txt` before `E/sealed/decoy_key.json` was opened; nothing else in the new evidence had been opened.
- 24 px sheet: mech = **B**; head = the single top-centre pixel on a flat-topped shoulder mass; arms = the two 1 px outer verticals; legs = the two lower columns with a 1 px gap, wider bottom row = feet.
- 48 px sheet: mech = **A**; head = centre top block between two lower blocks; arms = outer columns; legs = two lower columns.
- Key: 24 px B = mech, 48 px A = mech. Both correct. Decoys (lattice pylon with cross-arms, two-legged gantry) are humanoid-adjacent and the order differs between sheets, so the test now discriminates.

## Cycle-1 defects

| Defect | Status | Evidence |
|---|---|---|
| DEF-T007-style-1 (major, C27/C5 identity) | **closed** | 48 px front (`E/silhouette_front_48px.png`, own row scan): head 4 px, rows 0–1; two 4 px blocks rows 2–5, each 1 px from the head; chest taper 12 → 4 px rows 9–18; arm 2 px (rows 9–15) widening to 3 px (rows 16–21). 48 px side: chest 9–10 px against a 5 px thigh. All three signature forms findable. `E/overlay_concept_vs_greybox.png`: torso, head and blocks now sit on the concept's. See C27 below. |
| DEF-T007-style-2 (major, C16 weight) | **open, narrowed** (5 → 7) | Chest, waist and neck fixed; the arms got thinner. See C16 below. |
| DEF-T007-style-3 (major, C28 mood frame) | **closed** | New frame `K/layer4_mood-v2_af67b340…_0.png`, `I/mood_frame_check.json`; own check agrees. See C28 below. |
| DEF-T007-style-4 (minor, horned top, missile racks) | **closed** | 24 px top is one 7 px flat mass (rows 1–3) under a 1 px head: no spike-between-lumps. Blocks are plain square solids, no bevel, no launcher faces. Watch note kept for later plate work: pale cubes on a dark body still read as mounted equipment; keep their faces plain. |
| DEF-T007-style-5 (minor, C6 hands fuse with thighs) | **closed, PASS line withdrawn** | C6 as written is met (below). The hand-to-thigh gap was my own PASS suggestion, not spec text. Side chest is now distinct. |
| DEF-T007-style-6 (minor, concept hygiene) | **closed** | `E/concept_not_to_follow.md` lists all 8 cycle-1 files with follow / do-not-follow. Remainder moved to DEF-10. |
| DEF-T007-style-7 (minor, blind test) | **closed** | Blind look above. |

## Criteria (cycle 2)

| C# | Verdict | Score | Measurement |
|---|---|---|---|
| C1 | PASS | – | Four new prompts P4-d to P4-g in `K/PROMPTS.md` read; own term grep on PROMPTS and PROVENANCE: 0 hits. |
| C2 | PASS | – | New image references are own jobs 2fa84a4e img 2 and 6c9f1c93 img 1 (PROVENANCE line 33); nothing uploaded. |
| C3 | PASS | 7.5/10 | New front, side, 3/4, back and flat renders: no existing design I can name for head, chest, shoulders, colours or silhouette. Sunk cap between two pale cubes over a hull-section chest is the frame's own shape. |
| C4 | PASS | – | No crest, fins, core, face, livery, emblem, text. The cowl makes a dark V notch under the cap in front view: a collar, not a crest; do not let it grow into a chest V. |
| C5 | PASS | 3 of 3 forms | Row scan in the DEF-1 line. Form 3 reads by 1 px only. |
| C6 | PASS | – | Own scan, 48 px front: arm-to-torso gap on rows 9–21 and 24–25 = 15 of 22 arm rows (68 %, bar 40 %); crotch gap 2 px on rows 25–47; feet 6 px vs shank 4 px. Side: toe and heel project (foot cols 2–10, shank 4–8), chest distinct. |
| C7 | PASS | – | 24 px: head 1 px bump, shoulder mass widest solid run (7 px), leg gap rows 12–23. Blind look correct on both sheets. |
| C8 | PASS | – | `E/pixel_checks.json`: bands 40 / 80 / 88 / 184, unchanged. |
| C9 | PASS | IoU 0.524 | `E/pixel_checks.json` (was 0.494). |
| C10–C12 (visual) | PASS | – | 67 m line crosses the upper chest and shoulder balls; figure at the toe; thigh and shank still exactly on their lower limits. |
| C15 | PASS | – | Housings and rams as before; waist ball now inside the trunk (10.6 m wide at 51 m, 5.2 m ball band for 1.5 m); shoulder balls still read as small spheres at the chest corners (C16). |
| C16 | **FAIL** | **7/10** (bar 8) | DEF-2 below. |
| C25 | PASS | – | Layers, provenance and the per-file list. |
| C26 | PASS | 15 of 16 | 15 job rows in `K/PROVENANCE.md` = 11 + P4-d/e/f/g; no purchase, settings change, terms or block. |
| C27 | PASS as written | IoU 0.367 front (0.543 arms masked), 0.321 side | Below. |
| C28 | PASS (minor) | style match 6.5/10 | Below. |

## C16 weight read: 7/10
Gained since cycle 1 (measured on `E/silhouette_{front,side}_full.png`, 25 px/m):
- Side chest depth 16.0 m at 66 m (0.20 H), tapering to 11.5 m at 50 m; deeper than the pelvis (13.9 m). The bow is there.
- No neck stalk: cowl 8.4 m wide from 71 to 75 m, blocks 1.2 m from it; head cap 2.6 m from the blocks.
- No waist column: trunk 10.6 m wide down to 51 m.
- 3/4 and side views read heavy and upright.
Still against, all in the front and back views:
- **Arms are wires.** Upper arm 3.2 m wide in front (0.040 H; was 4.45 m), hung from a 3.8 m shoulder ball that is smaller than the 4.45 m fist. At 48 px the arm is 2 px, at 24 px 1 px. Against a 23 m chest it reads as a puppet's arms on a hull. For a melee-first frame (D-011) this is the limb that matters most.
- Feet are still thin sole plates under a drum; hip and shoulder housings are still bare spheres.
- Knee sliver, DEF-9.
One point short, and the point is the arms.

## C27 as written: met. The number should not gate.
The criterion asks for the same major masses at equal H, the IoU reported, and departures listed with reasons.
- Same major masses: yes for chest, head, blocks, pelvis, column legs, flat feet. No for arm bulk and leg spacing, both listed in `E/EVIDENCE.md` with the constraint that forces them (C7/C11 chain for forearm width; 13 m stance from `mech_frame@v1`).
- Why 0.6 is out of reach for this concept: only 63.5 % of the greybox front area lies inside the concept outline (own count on the overlay) because the legs stand inside the concept's leg gap and the concept's arms hang outside any outline C11 allows. The side figure is not a like-for-like test: the concept's middle view is a 3/4 perspective, the greybox covers 34 % of its area with 96.5 % of itself inside it.
- Recommendation: record the ≥ 0.6 expectation as not applicable to this concept, keep the reported values, and gate C27 on the three signature forms at 48 px plus the departures list.
- One departure is not listed and not forced: the 3.2 m upper-arm strut (DEF-8).

## C6 as written: met. No conflict in the criterion itself.
C6 asks for arms separated from the **torso**, a crotch gap, and feet wider than shanks. All measured above. It does not ask for a hand-to-thigh gap; that was my cycle-1 PASS line. The implementer's numbers hold: arm inner face at most 11.6 m, thigh outer face 10.0 m, so at most 1.6 m against the 1.67 m a full pixel needs. Hands fuse with the thighs on rows 26–29 at 48 px; accepted.

## C28 on mood frame v2: pass
Own check (k-means k = 8, RGB, ¼ downscale) against `I/mood_frame_check.json`:
- Hue families: 2 (cold slate-teal 64 %, warm cream 36 % by my split; stored 75 / 25). Pass.
- Accent: one orange pier lamp and its reflection, 0.007 % of pixels, off the mech. Pass.
- Luminance: sky 0.69–0.77, mech body 0.33. Pass.
- Water line: mech dark rows run 115 to 647 px; crotch at 420. With the design's 38–41 m from top to crotch, the water sits 9–11 m above the sole: mid-shin. Splash rings on both legs. Pass.
- Scale objects: people 20–27 px on the near pier (stored 20–24), cars about twice that long, so people and cars agree with each other. Against a 590–650 px mech that is 3.1–4.5 %: over 2.25 % at the mech's depth, true scale if the pier is 1.4–2.0× nearer. **Ruling on the declared open point: accepted.** The pier is plainly the nearest and darkest layer, the mech stands behind open water in haze, and that depth ratio needs a viewpoint roughly 15 m above the water, which the shore strip at the bottom of the frame supports. It cannot be proven in a generated image and it does not need to be: the frame sets composition and value only. Nothing reads oversized, and the mech reads at least 80 m.
- Mech in the frame: standing, two pale blocks, wedge chest panel, long arms, column legs. Closest reference: ref_01 top-right (slate-teal giant, base lost in mist).
- Observation: 25,981 colours, noisy sky texture, not ordered dither, not posterised.

## New defects
**DEF-T007-style-8 · minor · C27 list, C5 note.** The 3.2 m upper-arm strut and 3.8 m shoulder ball depart from the concept (armoured upper arm, large shoulder cluster) and are not in the departures list; C11 allows an upper arm up to forearm / 1.1. `K/DESIGN_NOTE.md` still states superseded targets (blocks level with the head top, shoulder width 0.33–0.37 H, form 3 wording). PASS: list the departure or remove it (DEF-2); update the design note.

**DEF-T007-style-9 · minor · C6 side, cross-reference C21.** Knee sliver: between 25.7 and 26.1 m the side silhouette is 1.4 m deep (`E/silhouette_side_full.png`); at 48 px side, row 32 is empty and the leg is cut in two; 48 px front row 32 shows the 1 px rams only. Present in cycle 1 as well. PASS: knee housing or shin top overlaps the thigh so no 48 px row is empty in any view.

**DEF-T007-style-10 · minor · C4, C25, C28.** Mood v2 keeps thin masts on the deck and blocks (one tall, central) and shows a deck, not a distinct head cap. Its do-not-follow note is in `K/PROVENANCE.md` only; `E/concept_not_to_follow.md` still says "being redone". The mood evidence lives in the integration checkout, not on `task/T-007`. PASS: one list covering the v2 frame; evidence index row C28 pointing at `mood_frame_check.json`.

## Still failing: where it goes
**DEF-T007-style-2 · major · C16 at 7/10.** Two parts.
1. **Orchestrator ruling first (spec conflict).** Arm bulk and rest-pose arm clearance pull against each other. Shoulder joints at ± 13.5 m, shoulder-width cap 0.40 H = 16.0 m outer edge, thigh outer face 10.0 m: the arm has 6.0 m in total. Cycle 2 spent 0.95 m of it on a hand-to-thigh gap that C6 does not ask for and 0.57 m on staying inside the cap. Please rule:
   a. the rest-pose hand-to-thigh gap is not a style requirement (my DEF-5 PASS line is withdrawn), so the arm may use the width out to 16.0 m and in to whatever stand-off eval-physics needs for DEF-T007-physics-1;
   b. whether C11 shoulder width is measured on torso and blocks only, as proposed in cycle 1. Recommended: yes, keeping C7. That is the only route to forearms like the concept's and it is a founder-visible look decision, so it also belongs in the founder question on the Balanced chassis look.
2. **Then implementer, cycle 3.** With ruling (a) alone: forearm about 5.0–5.5 m wide, upper arm 4.5–5.0 m (not a strut), shoulder housing at least as wide as the fist and read as part of the chest, DEF-9 closed. Arm volume fractions have room (upper arm 0.75, forearm 1.21 of 1.30); total volume is 9,645 of 10,000 m³. I expect that to reach 8. Re-measure C13, C19, C21.
Nothing here needs the founders before M0 continues, apart from the look question in 1b.
