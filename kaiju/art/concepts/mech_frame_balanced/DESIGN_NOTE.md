# DESIGN_NOTE.md · Balanced chassis, first original mech frame (T-007)

Sources: Layer 1 job 030477f2 image 0 (silhouette, top row middle figure), Layer 2 job 2fa84a4e image 2 (final turnaround) and job 4d4eda80 image 1, Layer 3 jobs 77faf417 image 2 and b1c655d7 image 3, Layer 4 job 6ba8d2aa image 1. All AI-generated (see `PROVENANCE.md`). Dimensions: `research/R-005/mech_frame_balanced.json` (`mech_frame@v1`) wins over anything below.

## The design in one sentence
A shipyard-built 80 m walking machine with a gunmetal wedge-shaped chest like a ship's bow, a small flat sensor-cap head sunk between two raised off-white box shoulder blocks, heavy box forearms, and plain column legs on wide flat feet.

## Signature forms (C5): each must read at 48 px
1. **Twin shoulder blocks.** Two off-white, near-cubic blocks sitting on short posts above each shoulder joint, their tops level with (or slightly above) the top of the head. Gives the silhouette a flat "two-tower" top edge with the head as a low notch between them. At 48 px this is the main identifier.
2. **Bow chest.** The torso shell is a wedge: wide and flat at the shoulders, narrowing downward to a vertical keel line at the sternum, like the bow of a steel ship seen head-on. The cockpit hatch is in the upper chest, flush, not lit.
3. **Box forearms.** Each forearm is a long rectangular armoured box, visibly wider than the upper arm, ending in a closed fist; the outer face carries the hardpoint plate (D-011 socket).

Head: a low, wide, flat cap with a narrow horizontal sensor slit; no eyes, no face, no crest, no masts.

## Proportions to hit (C10–C12; H = 80.0 m)
| Item | Target |
|---|---|
| Height H | 80.0 m sole to head top (shoulder-block tops may reach H; they are part of the torso part, not extra height above the head; keep them ≤ H) |
| Shoulder line | 62–70 m; 67 m deck marker crosses the upper chest at the cockpit hatch |
| Shoulder width (outer edge of shoulder blocks) | 0.33–0.37 H ≈ 26–30 m; shoulder/hip width 1.5–1.8 |
| Head | height 0.06–0.07 H (≈ 5 m), width ≤ 0.28 × shoulder width |
| Forearm | max width ≥ 1.15 × upper arm and ≥ fist width; fist width ≥ 0.055 H (≈ 4.5 m) with a flat knuckle face ≥ 6 m² |
| Legs | leg length 0.46–0.50 H; thigh min width ≥ 0.10 H (8 m); shank ≥ 0.08 H (6.4 m), constant or widening toward the ankle (do **not** copy the thin ankle linkages of the concept) |
| Feet | length 0.18–0.20 H (≈ 15 m), width 0.12–0.13 H (≈ 10 m), flat sole, ≈ 150 m² contact, no toes or claws |
| Stance | foot-centre spacing 0.75–0.85 × shoulder width |

## Plate-to-segment plan (C25): one rigid part per R-005 segment
| Segment (part) | Plates and forms that belong to it |
|---|---|
| pelvis | lower keel of the chest wedge continues as a narrow pelvic block; front and back skirt plates; hip joint housings (drums) on each side |
| torso | bow-chest shell (front wedge with keel seam), back hull plates, cockpit hatch, both shoulder-block posts and the **off-white shoulder blocks** (rigidly on the torso, they do not move with the arm), neck collar |
| head | flat sensor cap with horizontal slit; sits in the neck collar |
| upper_arm L/R | shoulder joint drum, one gunmetal sleeve plate, the upper anchor of the elbow ram |
| forearm L/R | the long box (gunmetal with off-white outer face), hardpoint plate on the outer face, lower anchor of the elbow ram |
| hand L/R | closed fist block with a flat knuckle face; wrist ring |
| thigh L/R | off-white front thigh slab over a gunmetal core, upper anchor of the knee ram |
| shank L/R | round knee housing at top, gunmetal shin plate, paired hydraulic rams exposed on the front-side of the knee (joint colour), lower anchor of the ram |
| foot L/R | wide flat sole block, ankle drum, a raised plate over the instep |

Seams: plates meet at straight welded seams; no greeble smaller than 1 m (C8).

## Joints (C15)
Hip, shoulder, knee, ankle: round drum housings containing the pivot. Knee and elbow: paired hydraulic rams spanning the joint (from Layer 3 job b1c655d7). Wrist and neck: collar rings. Load path: shoulder posts into the chest wedge, down the keel into the pelvis, out through the hip drums.

## Colours by palette role (C24: ≤ 4 flat material slots)
- **primary:** gunmetal / slate grey (most of the body).
- **secondary:** off-white (shoulder blocks, thigh slabs, forearm outer faces).
- **joint:** dark steel (drums, rams, collars), slightly darker than primary.
- **accent:** one small safety-orange panel (a single square on the left chest side, under 1% of the surface). No stripes, emblems, text or numbers.

## Guidance for the Blender modeller (15-part greybox, Phase B)
- Build from boxes and cylinders; keep the three signature forms big and simple. The shoulder blocks are the most important silhouette element: model them as near-cubes about 6 × 6 × 6 m on 2 m posts, parented in the torso part.
- Chest: an extruded wedge (trapezoid in front view, wide top, keel at the bottom) with a slight forward prow in side view.
- Fists closed, one rigid block per side. Feet flat rectangles with chamfered toes.
- Drop every mast, antenna and thin rod seen in the concepts; drop the concepts' thin ankle linkages and toed feet.
- Origins at proximal joint centres from `mech_frame@v1`; the drum housings are centred on those pivots.
- Departures from the concept to record in `EVIDENCE.md` (C27): thicker shanks and ankles; shoulder blocks ≤ H; no masts.
