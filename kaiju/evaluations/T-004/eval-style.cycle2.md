PASS

# T-004 eval-style, cycle 2 re-check (branch task/T-004 at e431b0a)

Judged from artifacts only: cycle-1 verdict, `evidence/T-004/EVIDENCE.md` (Fix cycle 2 section and tables), `sheets/*.png` (looked at `panel_board.png`), `metrics/checks/*.json`, `tasks/T-004.hazeshape.md`, and four stills from the committed slim `captures/m003_stills` shown at 2x (`warm_rust_sepia_street/f6`, `lavender_peach_eye70/f4`, `slate_crimson_eye70/f9`, `warm_amber_haze_eye70/f4`). Unity not run; the stored metric values were read, not re-run (thresholds Assumed, pending founder calibration). The blind panel is still the founders' gate; my look judgement is a proxy.

## Look, plainly
Better than cycle 1, and it now reads as dithered limited-palette pixel art. Box faces and the ground are flat single colours (rust street tower and ground, lavender tank and block faces, crimson ground, amber tank). Dither lives only in the sky bands and the haze seams between layers (rust sky at 0-480 x 80-230, lavender sky and far block silhouettes, crimson sky seam, the dome shading). The moiré weave and full-face lattices of cycle 1 are gone; the default is `direction`. Layers lighten with distance and the far ones merge into the sky band. The 1.8 m marker is one visible texel in all four. DIG range is 0.158-0.383 on 180 stills (cycle 1 max 0.649), within the advisory 0.5. Remaining softness: the dither is a uniform Bayer lattice, flatter than the painted references (panels 01A, 03D); slate_crimson has a very wide flat zenith band and a muted accent; the ground is a featureless slab in street views; the primitives-only scene still caps the look near 7. Proxy by eye about 7 for warm_amber_haze, lavender_peach and slate_teal, 6-7 for rust and crimson (cycle 1: 5.9 mean). This is a judgement, not a measurement.
Legibility at 1 km: the 80 m block reads in lavender (block against dark band, clear) and in the amber and crimson frames I looked at it is a darker slab against hazy sky; the stored numbers below say it is not 4 L* in every view for rust, amber, crimson.

## Defects
| Defect | Status | Evidence |
|---|---|---|
| style-1 C6 seam purity | closed | Seam purity 1 on all five profiles, modal lightness never falls across a seam in 12 of 12 frames, layers 1/3/10/30 km successively lighter 12 of 12 (`metrics/checks/c6_layers_*.json`; EVIDENCE cycle-2 table). Stills show two-band seams, no hue-noisy middle colour in lavender. Limit: I trust the stored purity figure; the checker was extended by the implementer, not independent |
| style-2 C7 80 m block at 1 km | routed | Marker 5/5 (dL* 22.6-33.8), 150 m 5/5 (min 9.36). 1 km: teal 9/9, lavender 9/9 (cycle 1: 3/9, 0/9); rust 6/9, amber 6/9, crimson 1/9 (cycle 1: 8/9, 9/9, 9/9 by my measure, 5/9 stored for crimson). Two tuning iterations (`tasks/T-004.hazeshape.md`) each bought 1 km at the cost of C6 c1 (3.24 to 5.56 L*) or the scale marker; stopped at 2 of 5 iterations on budget. Trade-off is real and recorded; needs a per-profile bound or a band layout defined in L* (research/founders). Not a regression I can blame on a loose implementation, but crimson 1/9 is a worse number than cycle 1's by the stored check |
| style-3 C11 off the reference plane | routed | T1: near 10.56 px, reference 1.04, far 3.42 (limit 1); Y1 reference 0.27 px. Check now reports all three depth classes (`c11_smooth_T1.json`). Inherent to single-plane snap: research/founders |
| style-4 dither only in gradients | closed | Flat faces one colour in the four stills; DIG 0.158-0.383 (cycle 1 0.649); default `direction`; no interference arcs. Proxy about 7 is my eye, not the panel |
| style-5 C9/C10 calibration | routed | Default `direction`: S_px.Y 0.002, C_px.Y 0.0051, F.Y 0.0002; T12 0.0276 / 0.0871 / 0; T3 0.025 / 0.1025 / 0.0168; M-004 verdict PASS, 12 of 12 gating, failing none. Cycle 1: yaw 0.0013-0.0020 but translation 0.196/0.302. Improvement is large; the cause (sub-pixel compensation on in the capture) changes the set, so the founders' T1/T2 clip judgement still applies |
| style-6 crimson seam monotone | closed | Modal lightness across seams 12 of 12 frames, all profiles (same checks as style-1) |
| style-7 negative anchors greyscale | closed | `sheets/panel_board.png` now has a colour `control_continuous_tone` tile beside the bilinear and unquantised negatives. Unquantised is still greyscale (identifiable) but a colour continuous-tone control is present. Board shows `direction` captures |
| style-8 evidence gaps | open (minor, carried, not looped) | EVIDENCE states "Not done in cycle 2: style-8"; `orbit_slate_teal` still INVALID (36 of 37 sets valid). Does not affect any gating criterion: see DEF-9 |

## Criteria movement (cycle 1 to now)
C4 5 to about 8 (flat faces fixed); C6 6 to 9 (seams, ordering); C7 6 to 6 (marker and 150 m full, 1 km 2 profiles full, 3 profiles partial); C9/C10 5/5 to pass in default mode; C11 unchanged (routed). M-003 verdict PASS, gating failing none; advisories still failing: accent share 0.0006, sky share 0.1778, horizon 0.4741, contrast min 21.7.

## New defects
- DEF-T004-style-9 · minor · evidence · to implementer (later task, not another fix cycle). Orbit and dolly captured in shipping mode `direction` with compensation on, passing the validator; dolly path clear of the near block; at least one camera cut with motion before it into a moving pose. PASS: those sets VALID and in `metrics/validate/summary.txt`.
- DEF-T004-style-10 · minor · look · to research/founders with the panel. slate_crimson: zenith is one 120+ px flat band and the crimson accent is under 0.2 % of the frame. PASS: founders accept the profile, or the ramp gains a gradient step at the zenith and an accent that survives street views.
- DEF-T004-style-11 · minor · look · to founders. Uniform Bayer lattice reads mechanical against the painted references at the dither edges (sky seams). PASS: panel board shows clustered or blue-noise mask beside Bayer and the founders pick.

## Why PASS
No open implementer-owned defect against a gating criterion remains unrouted. C7 1 km, C11 and the C9/C10 thresholds are research or founder items with numbers above; style-8 is a carried minor evidence gap. The founders' blind panel (M-003.f) remains pending.
