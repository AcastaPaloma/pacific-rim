VERDICT: FAIL

eval-style, T-006 cycle 1. Judged from numbers in EVIDENCE/M003/M004 tables plus the hero, thumb test, scale plate and review board images. The profile and alt 1080p color_output files were seen through the review board (same frames), not opened separately.

## C1 cold test
- 480 px: I name it a humanoid giant robot (blocky shoulders, arms, two pillar legs, seen from behind), dark mass in the left panel (hero), centre-right of frame. The decoy panel has no such mass.
- 96 px: not judgeable. hero_thumb_test.png is cropped; only the top edge of the 96 px tiles shows. So the 96 px half of C1 is unevidenced (DEF-1). Mech height 0.463 visible is in range.
- Verdict: PARTIAL.

## C2 blind city check
Generic. Tallest tower is a plain slab with no Salesforce crown; no pyramid or pointed Transamerica silhouette (Transamerica is flat prisms, 6.2 px off); no Telegraph Hill or Coit. I could not name SF; it reads as a generic grey block city. FAIL.

## Criteria table
| Crit | Result | Reason |
|---|---|---|
| C3 | FAIL | No visible waterline on the legs: the water is a flat dark plane the legs fade into; ankle-to-mid-shin not checked. Scale cues are boxed in the plate but invisible in the frame: figures within 1.3 L* of their surround (need 10), boat 7.0, only the car passes (24). Boat footprint outside 1-3 px. Salesforce (516 px) barely exceeds the mech (500 px); most towers are shorter than the mech, so "towers visibly taller" is weak. |
| C4 | PARTIAL | c1 passes (drop 0, rho 1.00, span 20.3); sky share 0.364 ok. Mech L* vs buildings/median not measured (empty mask), darkest-5% share not reported. By eye the mech is darker than the buildings but the foreground water is as dark, so it is not clearly the darkest mass. A hard horizon band separates water from the far shore (the far shore does not melt). |
| C5 | PASS | d-iv 100% at s=4 and s=5, DIG 0.351 on, 0.016 off. By eye: dither confined to sky bands and a few building patches, flat regions clean. Foreground water has mechanical diagonal hatching and the sky has a curved ring band; minor. |
| C6 | FAIL | A2 1.000, 6 levels, 1 hue family; no accent at all (d-iii N/A), so the "one accent 0.1-15%" requirement is unmet. Unquantised control is rejected as invalid input, not failed on a metric (tool limitation, bilinear control does fail). |
| C7 | PARTIAL | No text, prefab-only mech, primitives listed. Banned-terms file-name check and text-in-image check not on file. |
| C11 | FAIL | c1 fails in 4 of 5 profiles at both resolutions (only slate_teal passes). Silhouettes identical. On the board the rust profile shows a ground shadow under the mech that reads as floating, and the crimson/amber mech blends into the buildings. |
| C12 | FAIL | Waterfront: c1 rho 0.80, sky 0.53 (>0.50), a huge dark monolith at right, mech tiny and low contrast. Closer and wider pass c1 but C8-C9 not run on them. |
| C13 | PASS | Board plus shuffled copy plus key; hero shown twice (rows 1 and 2), a near-duplicate; reference panels are tinted to the profile, not raw. Caption cropping unverified. |
| C14 | PARTIAL | Misses shown honestly: push-in C_px.T12 0.164 (direction, screen) and F.T12 0.0080 (hybrid) vs thresholds; orbit misses all three yaw sub-measures in every mode (rate 2.4x nominal, so a test-design issue as well). Static is exact in direction and hybrid. Internal inconsistency: the per-sequence table lists screen static S0 0.0106 MISS, while the header and EVIDENCE say screen static is exact after re-capture. Tables need reconciling. |

## Hero score: 5 / 10 (R-002 band 5-6)
Palette family correct and grid visible (clears the 3-4 band), haze ramp present. Held down by: dither confined to sky patches, flat foreground (water and seabed one value, hatched), muddy low-contrast mech silhouette seen from behind, no scale figures readable, no accent, generic skyline. It does not reach 7 because there is more than one clear miss (scale cues, accent, recognisability).
Single most important look defect: the skyline is generic slab blocks that do not read as San Francisco, which undercuts the whole "real city" promise (DEF-1 below).

## Defect table
| ID | severity | criterion | text |
|---|---|---|---|
| DEF-T006-style-1 | major | C2 | Skyline reads as a generic block city: no Salesforce crown, no pyramid, no hill and tower. Blind check cannot name SF. |
| DEF-T006-style-2 | major | C11 | c1 haze/value fails in 4 of 5 profiles at 1080p and 1440p; the strip is not five passing profiles. |
| DEF-T006-style-3 | major | C12 | Waterfront alt fails c1 (rho 0.80) and sky share 0.53; a monolith dominates; mech tiny. C8-C9 not run on any alt. |
| DEF-T006-style-4 | major | C3 | Scale cues invisible in the frame (figures 1.3 L*, boat 7 L* vs 10); waterline on legs not visible or not checked; towers barely or not taller than the mech. |
| DEF-T006-style-5 | major | C6 | No accent anywhere in the hero (criterion requires one, 0.1-15%, on something small or near). |
| DEF-T006-style-6 | minor | C1 | hero_thumb_test.png crops the 96 px tiles, so the 96 px cold test cannot be done from the artifact. |
| DEF-T006-style-7 | minor | C4 | Mech mean L* vs surround and darkest-5% share unmeasured; foreground water is as dark as the mech, and a hard shore seam is visible. |
| DEF-T006-style-8 | minor | C14 | M004_TABLES screen static row (MISS) contradicts the header and EVIDENCE (exact); orbit run at 2.4x nominal rate makes its misses uninformative. |
| DEF-T006-style-9 | minor | C7 | Banned-terms and text-in-image checks not on file. |
| DEF-T006-style-10 | calibration | C14 | Orbit yaw thresholds (Y group, Assumed) are missed by 7-500x in all modes; thresholds or the orbit rate need founder calibration, not an engine fix. |
| DEF-T006-style-11 | calibration | C9 | 3 px landmark tolerance is below one internal pixel (4 output px); Transamerica 6.2 px fails it by a cause that lies in the model, not the spec. |
| DEF-T006-style-12 | founders | C1/C16 | Mech faces away (yaw 270, back view); founders should decide whether the first image shows the back. |
| DEF-T006-style-13 | founders | C3 | Whether 46% visible height with the mech barely under the tallest tower gives the intended weight, or a closer framing is preferred. |
