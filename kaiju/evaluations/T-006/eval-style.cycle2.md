VERDICT: PASS

eval-style, T-006 re-check cycle 2 (recapture a867e67). Judged only the cycle-1 defects. Seen: hero.png, hero_thumb_test.png, review_board.png, waterfront color_output; numbers from M003_TABLES, EVIDENCE rows, scale_cues.json, banned_terms.json, build_log waterline. Profile and other alt frames only via the board.

## Cold test (C1, DEF-T006-style-6)
- 96 px: tile A holds the giant machine (dark upright humanoid mass, centre-left of the skyline). Tile B is the decoy, no mass. Named correctly from the 96 px tiles. 480 px: still a humanoid giant robot seen from behind.

## Cycle-1 defects
| ID | cycle 2 status | evidence |
|---|---|---|
| style-1 (C2 skyline) | routed (Q-069) | not judged |
| style-2 (C11 four profiles) | routed (Q-070) | not judged; board still shows the crimson/rust mech blending into buildings, same cause |
| style-3 (C12 waterfront) | closed | c1 rho 1.00, span 17.4, sky 0.46 <= 0.50. Image: mech centred at about 0.4 of frame height, clearly the main mass, no monolith in front. Residual, minor: lower half is flat dark water, advisory horizon 0.511 > 0.45; C8-C9 still not run on any alt (EVIDENCE says out of scope) |
| style-4 (C3 scale) | still open, downgraded to minor | car 24.0 L* and boat 10.9 L* now pass (were 7.0 for the boat). Figures still 0.27 / -1.27 / 0.10 L*, so 3 of 5 cues fail and are invisible in the frame (the value 1.0 tint did not survive haze and quantisation). Waterline is now measured: water 1.20 m below the ankle, feet only wet, real depth reported not edited; legs in the image meet the water with no visible waterline. Towers still only about as tall as the mech (tallest about 550 px vs mech about 500 px). A readable cue exists (car, boat), so not major |
| style-5 (C6 accent) | routed (Q-070) | the 23 px buoy is a pale grey-blue cube in the foreground, no accent scored, d-iii N/A. Note it reads as a stray block, not a buoy |
| style-6 (C1 thumb test) | closed | hero_thumb_test.png shows both 96 px tiles whole, A/B labelled, answer in json |
| style-7 (C4 mech vs surround) | still open, minor | mech L* vs buildings and darkest-5% share still unmeasured (empty mask, no class mask). By eye the legs fade into foreground water of similar value; hard shore seam remains. c1 passes |
| style-8 (C14 table contradiction) | closed | M004 line 1 says static exact in all three modes; no stale MISS claim. Orbit 2.4x rate is disclosed (calibration, routed to style-10) |
| style-9 (C7 banned terms) | closed | banned_terms.json PASS, 5916 names, 0 hits; text-in-image honestly stated as not scanned (no text rendered) |
| style-10, style-11 | routed (calibration) | not judged |
| style-12, style-13 | routed (founders, Q-071) | not judged |

## New defects
None major.

## Open after cycle 2
- Unrouted: style-4 (minor, figures fail contrast, waterline not visible on legs), style-7 (minor, C4 mech contrast unmeasured).
- Routed: style-1, style-2, accent (style-5), style-10, style-11, style-12, style-13.
