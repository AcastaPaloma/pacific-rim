# DESIGN_NOTE.md · Balanced chassis, first original mech frame (T-007)

Sources: Layer 1 job 030477f2 image 0 (silhouette, top row middle figure), Layer 2 job 2fa84a4e image 2 (final turnaround) and job 4d4eda80 image 1, Layer 3 jobs 77faf417 image 2 and b1c655d7 image 3, Layer 4 job af67b340 image 0 (mood frame v2; earlier mood frames 6ba8d2aa and c3ce3ab4 are superseded). All AI-generated (see `PROVENANCE.md`). Dimensions: `research/R-005/mech_frame_balanced.json` (`mech_frame@v1`) wins over anything below.

**Revised after the greybox (T-007 fix cycles 2 and 3).** The built greybox (`art/models/mech_frame_balanced/`, script `Pipelines/blender/mech_frame_balanced/build_greybox.py`) is now the reference for the forms. Where this note and the images disagree, follow this note; where this note and `mech_frame@v1` disagree, follow the JSON. Measured values: `evidence/T-007/dimensions.json`.

## The design in one sentence
A shipyard-built 80 m walking machine with a gunmetal hull-section chest like a ship's bow, a small flat sensor-cap head sunk in a neck cowl between two square off-white shoulder blocks, armoured arms ending in heavy box forearms and closed fists, and plain column legs on wide flat feet.

## Signature forms (C5): each must read at 48 px
1. **Twin shoulder blocks around a sunk head.** Two off-white, square 5.8 m blocks standing on the chest deck either side of the head (inner faces 5.5 m from the centreline), tops at 77 m, 3 m below the head top. Seen from the front, blocks, neck cowl and head make one flat-topped mass with the head cap as a low bump: not ears, not horns. Block faces stay plain (no hatches or launcher faces).
2. **Bow chest.** The torso is a ship's hull section: wide at the shoulders, narrowing to a 10 m trunk at 50 m; in side view the keel line runs forward to 10 m in front of the joint line at 66 m and the back closes to a point, 16 m deep (0.20 H). The cockpit hatch is in the upper chest, flush, not lit.
3. **Box forearms.** Each forearm is a long 5.4 m-wide armoured box, wider than the 4.6 m upper arm and the 4.75 m fist; its outer face carries the off-white hardpoint plate (D-011 socket).

Head: a low, wide, flat cap with one narrow horizontal sensor slit; no eyes, no face, no crest, no masts. Only the cap shows above the cowl; no neck is visible.

## Proportions (C10–C12; H = 80.0 m; measured on the greybox)
| Item | Value |
|---|---|
| Height H | 80.0 m sole to head top; shoulder-block tops 77 m |
| Shoulder line | chest top 69.5 m; the 67 m deck marker crosses the upper chest |
| Shoulder width (C11, torso and shoulder armour only, spec v1.1 ruling b) | 28.3 m = 0.354 H; shoulder/hip 1.42 |
| Arms | shoulder housing 5.0 m ball seated in a chest cup; upper arm 4.6 m; forearm 5.4 m; fist 4.75 m (≥ 0.055 H), flat knuckle face ≥ 6 m² |
| Head | cap 5.0 m above the cowl (0.0625 H), 6.4 m wide |
| Legs | leg length 0.525 H; thigh 8.0 m (≥ 0.10 H), shank 6.4–6.8 m (≥ 0.08 H) in front and side views, no thin ankle linkages |
| Feet | 15 × 10 m flat sole, ≈ 150 m² contact (toe corners chamfered), no toes or claws |
| Stance | 13 m between foot centrelines (JSON); outer stance 0.80 × shoulder width |

## Plate-to-segment plan (C25): one rigid part per R-005 segment
| Segment (part) | Plates and forms that belong to it |
|---|---|
| pelvis | narrow keel block between the thighs (the chest keel continued down), back plate behind the hip balls; no front skirt (it would block hip flexion) |
| torso | hull-section chest with keel seam, cockpit hatch, waist ball inside the trunk, neck cowl, shoulder cups around the shoulder balls, outer shoulder armour plates, pedestals and the **off-white shoulder blocks** (rigidly on the torso), one orange accent panel |
| head | flat sensor cap with horizontal slit; neck hidden in the cowl |
| upper_arm L/R | shoulder ball (housing), armoured upper arm, upper anchor of the elbow rams |
| forearm L/R | the long box (gunmetal with off-white outer hardpoint plate), elbow drum, lower anchor of the elbow rams |
| hand L/R | closed fist block with a flat knuckle face; wrist ring |
| thigh L/R | hip ball (housing), off-white front thigh slab over a gunmetal core, knee guard over the knee drum, upper anchor of the knee rams |
| shank L/R | knee drum at top, gunmetal shin, paired hydraulic rams on the front of the knee (joint colour) |
| foot L/R | wide flat sole block, ankle drum |

Seams: plates meet at straight welded seams; no greeble smaller than 1 m (C8).

## Joints (C15)
Spherical joints (shoulder, hip, waist) have balls on the child part; hinge and two-axis joints (elbow, knee, ankle, wrist) have drums on the child part; the neck sits in a cowl with a cone-shaped opening. Every housing contains its pivot and keeps 0.35 m clearance to the neighbouring part, so the frame reaches the R-005 ranges (`evidence/T-007/rom_report.json`). Knee and elbow: paired rams spanning the joint. Load path: shoulder cups into the chest hull, down the keel into the pelvis, out through the hip balls.

## Colours by palette role (C24: ≤ 4 flat material slots)
- **primary:** gunmetal / slate grey (most of the body).
- **secondary:** off-white (shoulder blocks, thigh slabs, forearm hardpoint plates).
- **joint:** dark steel (balls, drums, rams, collars), darker than primary.
- **accent:** one small safety-orange panel (a single square on the left chest side, under 1% of the surface). No stripes, emblems, text or numbers.

## Do not follow (from the kept concept images; full per-file list in `evidence/T-007/concept_not_to_follow.md`)
- Thin masts, rods or antennas anywhere, including the tall central mast and the masts on the deck and blocks in mood frame v2 (C4).
- Mood frame v2: the head shown as a low deck between the blocks (the head is a distinct cap); mid-stride poses; warm cream as the dominant body or sky colour of the design.
- Thin ankle linkages, toed or clawed feet, open hands.
- The plate sheet's different head and shoulders, and its two orange knee dampers (one accent only).
- Arms wider than the shoulders or hanging far outside the legs: the forearm box stays inside the shoulder outline (C7).
- Any glowing chest light or round core, V-shaped chest panel, stripe or emblem (C4).

## Departures from the chosen concept (C27; also listed in `evidence/T-007/EVIDENCE.md`)
- Arms narrower than the concept's (forearm 5.4 m, not about twice the upper arm and wider than the shoulders): C7 requires the shoulders to be the widest mass at 24 px, and the `mech_frame@v1` shoulder joints are 27 m apart.
- Legs closer together than in the concept: the JSON stance is 13 m between foot centrelines.
- Full-width shanks and ankles, flat untoed feet, closed fists, no masts.
- Shoulder blocks 3 m below the head top (C7 head bump) and moved inboard next to the head.
- Joint housings on the child part; no pelvis front skirt (range of motion, C21).
