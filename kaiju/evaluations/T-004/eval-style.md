FAIL

# T-004 · eval-style (pixel pipeline look, M-003@v1, M-004@v1)

Evaluator `eval-style`, 2026-11-14. Artifacts judged: `evidence/T-004/` of branch `task/T-004` at ebe049b (full local captures in `captures_local/`), against `expectations/T-004.md` C1–C13, `research/R-002.md` §4–§5 and `art/style_refs/`. `tasks/T-004.impl.md` was not read. Unity was not run.
Metric tools: T-002 worktree at ec5ffba; `kaiju_metrics`, `registry/` and `metrics.py` are byte-identical to ef3b834 and a175d2f (`git diff --stat` empty), registry v1.1. The stored M-003 report names a175d2f, `tool_versions.txt` names ef3b834 (same code).
Review images written by this evaluation: `evidence/T-004-review/style_board.png` (founder board), `motion_change_maps.png`, `proxy_panel_sheet.png`. Frame references are `captures_local/<set>/<sequence>/f<n>`; crops are internal-resolution pixels `(x0–x1, y0–y1)` of the 480×270 image.

**Why FAIL, in one paragraph.** The numbers the tools gate on are real and reproduce exactly, and the pipeline is a correct machine: exact palette, crisp integer grid, haze by true distance, far layers melting into the sky. But three criteria are only partly met with visible consequences (C6 seams, C7 at 1 km, C11 off the reference plane), and the M0 default configuration does not yet read as the references: dither lies over whole flat faces and the default `hybrid` anchoring adds a moiré weave on the ground and near faces. Agent proxy for the blind panel: **5.9 / 10** (milestone needs 8.0, no profile under 7). The founder's question "our game's look, or a palette filter over a 3D render?" currently gets: a well-built filter with the right colours and depth, not yet the painted look. C9/C10 are calibration items, not the reason for the verdict.

## 1. Look, per profile (default M0 config; stored M-003 values equal my re-run to every digit)
Re-run: `metrics.py m003 captures_local/m003_stills captures_local/haze_cal` (180 stills, 25 s): zero differences from `metrics/m003/report.json` in any sub-measure value or status; same for `m003_details` and `m003_stills_1440p`. Gating sub-measures a.internal / a.output / b.DIG / c1 / c2 / d-i / d-ii / d-iv: PASS for every profile.

| Profile (closest reference) | Look verdict | Proxy | A₂ int / out | DIG min / max (adv. ≤ 0.5) | c1 max drop / ρ / span L* | Levels / hues | d-iv |
|---|---|---|---|---|---|---|---|
| warm_rust_sepia (01A) | Palette and layering right; dither texture wrong everywhere | 5.0 | 1 / 1 | 0.350 / **0.649** | 0.69 / 0.964 / 25.0 | 7 / 1 | 1 |
| warm_amber_haze (05A) | Good haze glow; speckled ground, dithered faces | 6.0 | 1 / 1 | 0.383 / **0.623** | 0 / 1 / 28.5 | 8 / 1 | 1 |
| slate_teal (01B, 03B) | Cleanest; misty layering reads | 6.3 | 1 / 1 | 0.323 / **0.517** | 0 / 1 / 23.1 | 6 / 2 | 1 |
| lavender_peach (01C) | Best silhouettes; hue-noisy ground, 1 km block lost | 6.3 | 1 / 1 | 0.315 / **0.603** | 0.06 / 0.964 / 29.6 | 7 / 2 | 1 |
| slate_crimson (03D, 04A) | Layering right but murky; accent mostly absent | 5.7 | 1 / 1 | 0.280 / **0.542** | 0.14 / 0.964 / 17.5 | 5 / 1 | 1 |

What reads right (all profiles): limited hues, 5–8 value levels; near forms darkest, far forms lighter and flatter, the 10–30 km layer merges with the horizon sky with a dithered edge (e.g. `m003_stills/warm_amber_haze_eye70/f4` (360–480, 110–178)); crisp 4×4 output blocks; dithered sky-band seams, no hard contour lines; box faces are flat planes; the 1.8 m marker is one bright texel at (237, 206) in every `*_eye70/f4`.
What reads wrong, with frames:
- **Dither on flat faces, not only in gradients** (all profiles). A uniformly lit face whose value falls between two ramp entries becomes a full-face 50 % lattice: near tower `m003_stills/<profile>_street/f6` (188–274, 0–184) and `<profile>_eye70/f4` (309–420, 123–270); mid block `warm_rust_sepia_eye70/f4` (156–262, 136–174). In `stages_ab/off_dither` the same faces are flat and closer to panels 03D/04A. DIG up to 0.649 against 0.09–0.33 on the reference panels.
- **Moiré weave from `hybrid` anchoring.** Ground of `warm_rust_sepia_street/f6` (0–480, 190–270) and `warm_amber_haze_street/f6` (0–480, 195–270) shows curved interference arcs; near faces show an irregular woven clump pattern. The same view in `m004direction_warm_rust_sepia/street_S0/f0` is a calm regular checker (board, bottom row).
- **warm_rust_sepia:** `elevated300` frames are dither almost edge to edge (f9, DIG 0.649). Proxy 5/5/5.
- **slate_teal:** the light end of the ramp is coarse (L* 46, 54, 67, 75 in `palette_set@v1` against 2–6 L* steps at the dark end), so the horizon band steps abruptly: `slate_teal_eye70/f4` (about 320–480, 90–125).
- **lavender_peach:** foreground ground is a three-colour weave of plum #3b293c and two blues: `lavender_peach_eye70/f4` (150–480, 190–270); stray plum texels along a blue silhouette edge: `lavender_peach_eye70/f0` (264–266, 117–187); a dark teal cap at the zenith: `m003_details/lavender_peach_detail_sky/f0` (0–480, 0–95).
- **slate_crimson:** lowest frame contrast (L*₉₀−L*₁₀ 12.7 at `slate_crimson_elevated300/f2`, advisory ≥ 25); the crimson accent is 0–0.16 % of the frame and absent in street views, so the profile reads as plain slate.
- **Scene limits (not defects of this task):** primitives only; no clustered cloud or smoke masses, no light shafts (later slices). This caps any score near 7.

## 2. Agent proxy for the blind panel (M-003 f) — a proxy, not the founders' panel
19 tiles at internal resolution, shuffled with a fixed seed, numbered only, key read after scoring (`proxy_panel_sheet.png`; R-002 §4f bands). Anchors behaved: positives 9, 9 (crops of panels 03D, 01B); negatives 2, 2 (`control_unquantised/slate_teal_eye70/f4`, `f9`).

| Profile | street f6 | eye70 f4 | elevated300 f2 | Mean |
|---|---|---|---|---|
| warm_rust_sepia | 5 | 5 | 5 | 5.0 |
| warm_amber_haze | 6 | 6 | 6 | 6.0 |
| slate_teal | 7 | 6 | 6 | 6.3 |
| lavender_peach | 7 | 6 | 6 | 6.3 |
| slate_crimson | 6 | 6 | 5 | 5.7 |

**Overall mean 5.9 over 15 captures** (rubric band 5–6: "correct palette family and visible grid, but dithering mechanical everywhere, weak depth layering in places"). Best single captures (7): `slate_teal_street/f6`, `lavender_peach_street/f6`.

## 3. Controls and the two reported misses
- Negative controls re-run on the same tool (slate_teal, 12 frames each), all FAIL as stored: unquantised a.internal 0, a.output 0; bilinear a.output 0.8065, d-iv 0.4486; dither off b.DIG share 0; haze off c1 ρ −0.086; reversed haze c1 drop 12.73 L*. Frame-noise control S0 0.1571.
- **C7, 80 m block at 1 km.** Stored (`metrics/checks/c7_blocks_*.json`): ≥ 4 L* in 9/9, 8/9, 5/9, 3/9, 0/9 views (amber, rust, crimson, teal, lavender). My own measure (block face against a 3 px ring of strictly farther surfaces or sky, `m003_stills/<profile>_eye70`): rust 9/9 (median 7.8), amber 9/9 (7.6), crimson 9/9 (4.7, min 4.0), teal 8/9 (5.6, min 3.3), **lavender 4/9 (median 3.6, min 1.9; only 23 % of the silhouette has a ≥ 4 L* step)**. Visibility: the top of the block reads against the sky in `*_eye70/f9` (block top (259, 153), base y 186) but its lower half fuses with the ground band in teal, lavender and crimson. Cause: mostly palette data. Ramp neighbours closer than 4 L*: lavender 42/44/44/46/48, teal 27/29 and 33/34, crimson 39/39/40. The pipeline maps bands to them faithfully.
- **C6, seam pixels.** Stored: slate_crimson 32 of 44. I find it: on the ground of `slate_crimson_eye70/f0` index 4 appears at 4.6–5.4 km, vanishes, and returns beyond 6.4 km. It is one or two texel rows near the horizon with 7 L* difference: not visible in a still. The larger problem is elsewhere and the stored check misses it: share of ground texels inside a 4×4 window holding three or more palette indices is 14 % (hybrid) and 25 % (direction) in lavender_peach, 19–21 % in slate_teal, 9–14 % in warm_amber_haze, 0.7 % in slate_crimson. In teal the three colours are the same hue and it is invisible; in lavender the middle colour is plum between blues and it is the hue noise named above. Cause: dither spread (0.4 of a band) wider than the narrow bands, plus ramp order in the palette data.

## 4. Motion (M-004@v1): stored versus re-run, and what the eye sees
Re-run of all ten `m004_<profile>` and `m004direction_<profile>` sets and the nine `m004ab_*` sets: every value equals the stored reports. S0 is exactly 0 in all 28 static sequences (the frame-noise control scores 0.1571).

| Mode (worst of five profiles × two viewpoints) | S_px.Y / C_px.Y / F.Y (≤ 0.01 / 0.01 / 0.001) | S_px.T12 / C_px.T12 / F.T12 (≤ 0.12 / 0.15 / 0.005) | S_px.T3 / C_px.T3 / F.T3 (≤ 0.12 / 0.15 / 0.08) | What the eye sees (slate_teal eye70, f40–79, crops ×4) |
|---|---|---|---|---|
| `hybrid` (default) | **0.0351 / 0.0505 / 0.0131** | 0.0974 / **0.1507** / 0 | 0.0858 / 0.1303 / 0.0310 | Sky and layers beyond 2.5 km are rock steady in turns and strafes. In a turn 3–5 % of the texels on near and mid surfaces change per pixel of rotation: a fine sparkle on the near tower (130–225, 135–270) and across the ground. In a strafe the weave rides the surfaces but re-samples: 12–13 % of near-surface texels change per pixel. |
| `direction` | 0.0013 / 0.0020 / 0.0001 | **0.1961 / 0.3023** / 0 | **0.1933 / 0.2955 / 0.0863** | Turns move the picture as one rigid image (0.05–0.2 % change, silhouette edges only). In a strafe the pattern stays fixed on screen while geometry slides under it; on the ground this is a stationary band of checker and is not seen as motion; on near faces it is a faint shower-door, mild because the pattern is a uniform one-texel checker. |
| `screen` (slate_teal eye70 only) | 0.3498 / 0.7177 / 0.2659 | 0.1039 / 0.1890 / 0 | 0.0969 / 0.2028 / 0.0718 | Whole-frame crawl in every turn. Not shippable. |

No swimming sky bands and no popping haze bands in any yaw or translation set. The skyline does step in the presented image during slow strafes: see DEF-3. Dolly (`dolly_slate_teal/eye70_D1`): far-layer change per frame averages 0.4 %, no pop; frames 15–23 are spoiled because the path passes through the near block (S up to 0.50).
**Mode I would ship: `direction`.** It meets yaw with 5–8× margin (turning and orbiting are the dominant third-person motions), its stills are cleaner (no moiré), and most of its translation "crawl" is a pattern that does not move on screen. `hybrid` should stay selectable but needs a stable surface pattern before it can be the default.
**C12, A/B evidence: complete** (slate_teal eye70, re-run): rotation quantise off raises S_px.Y 0.0008 → 0.3539 (`direction`) and 0.0214 → 0.1310 (`hybrid`); position snap off raises F.T12 0 → 0.0759 and 0 → 0.0320; `screen` versus `direction` yaw 0.3498 versus 0.0008; `direction` versus `hybrid` translation 0.1043 versus 0.0399. All three anchoring modes, both stabilisers and the no-stabiliser case are stored.
**Calibration recommendations (research and founders):**
1. Yaw thresholds: keep. They separate a rigid turn (0.001–0.002) from a visibly sparkling one (0.012–0.035).
2. Translation S_px / C_px: mis-aimed for view-anchored dither. In `direction` T2 the screen image changes 0.4 % per frame against 4.8 % in `hybrid`, yet the metric scores it 2–3× worse, because it counts ground sliding under a haze seam that is static on screen (`motion_change_maps.png`, lower right: the red is in ground seam bands). Proposed, unregistered: measure C_px.T on object faces nearer than 400 m only, or exclude haze-seam texels. Judge with founders watching T1/T2 clips in both modes.
3. The same thresholds are too loose for `hybrid` near surfaces: 12–13 % change there still passes because sky and far layers dilute it. The 0.1507 miss (limit 0.15) is within noise of a pass and tells nothing.
4. F.T3 ≤ 0.08 at 130 m/s lateral is not a gameplay speed: make it advisory.
5. Promote DIG_max (or a flat-face dither share) from advisory to gating once the founders settle the dither look: it is the measure that tracks the most visible flaw.
**Sub-pixel compensation off in the M-004 sets: the numbers are still representative** of pattern stability. `c11_subpixel_on` and `c11_subpixel_off` have identical `color_internal` in 180 of 180 frames (T1 and Y1), and M-004 reads only internal index and depth. What M-004 cannot see is output smoothness; that is C11.
**C11, checked from frames.** Compensated `color_output` is exactly the ×4 point upscale shifted by the declared whole output pixels (0 mismatching interior pixels in 52 sampled frames; rounding to the remainder ≤ 0.5 px), so the grid stays crisp. Tracking depth edges in output space over f0–119: Y1: far, reference-plane and near edges all move 1.00 px/frame with 0.0 px deviation. T1: reference plane (450–800 m) within 0.49 px, step error 0.64 px (stored 0.99 / 0.69); **far edges (> 4 km) wobble ± 2.5 px with 4 px steps every 10 frames; near edges (< 300 m) run up to 9.2 px off smooth motion and jump 15 px every 10 frames.** The stored check tracks one edge on the reference plane only.

## 5. Camera cut (C13) and footfall impulse
- Cut: `cut_check/moving_then_cut/f11` (cut flag true, 127 m move, 32° yaw) is identical to `cut_check/static_at_new_pose/f0` in `color_internal` and `color_output` (0 differing pixels); frames 0–10 differ from it in 64–65 % of pixels; present shift goes [−1, −1] → [0, 0] and dither origin [4, 22] → [230, 53] on the cut frame. No wrong-offset frame. Limit of the evidence: one cut, into a static pose, sequence ends on the cut frame.
- Impulse: `orbit_slate_teal/orbit_20s`, event at f480. Rendered pitch steps −4.00 → −4.14 → −4.27 (f484–488) → −3.86 (f496–500) → −4.00 by f510, with one 0.6 m height step at f484–486; the wanted pose is unchanged. Reprojected change during f480–519: sky 0.0002, far 0.0000, mid 0.089, near 0.073 against 0.082 / 0.056 before and 0.079 / 0.055 after: the picture moves as a whole, no layer is left behind, and it settles. The shake is quantised to whole texel steps (two steps of 0.138°).

## 6. Criteria

| C | Verdict | Score /10 | Artifact and value |
|---|---|---|---|
| C1 Palette adherence | PASS | 10 | re-run A₂ 1.0 / 1.0 on 180 stills; controls fail (0; 0.8065) |
| C2 Grid and resolution | PASS | 10 | d-iv 1.0 at 1080p (480×270, k 4) and 1440p (k 5), re-run |
| C3 Posterisation | PASS | 8 | ≤ 8 levels, ≤ 2 hue families; accent share 0–0.16 % (advisory miss) |
| C4 Dither in gradients | PASS (gating); advisory miss | 5 | DIG ≥ 0.28 on 180/180; dither-off control 0; DIG_max 0.649 > 0.5; flat faces dithered (DEF-4) |
| C5 Value by depth | PASS | 9 | c1 drop ≤ 0.69, ρ ≥ 0.964, span ≥ 17.5; c2 pass; reversed and haze-off controls fail |
| C6 Layers and sky | PARTLY MET | 6 | layers and sky merge pass; seam purity fails (DEF-1, DEF-6) |
| C7 Scale legibility | PARTLY MET | 6 | marker and 150 m pass; 1 km fails in lavender_peach, marginal in teal and crimson (DEF-2) |
| C8 Static | PASS | 10 | S0 = C = F = 0 in 28 sequences; noise control 0.1571 |
| C9 Yaw | MISS with default, calibration | 5 | `hybrid` 0.0351 / 0.0505 / 0.0131; `direction` passes 0.0013 / 0.0020 / 0.0001 (DEF-5) |
| C10 Translation | MISS, calibration | 5 | `hybrid` C_px.T12 0.1507; `direction` 0.1961 / 0.3023 (DEF-5) |
| C11 Smooth output | PARTLY MET | 5 | reference plane within 0.49 px; far ± 2.5 px, near 15 px jumps on T1 (DEF-3) |
| C12 Stabilisers pay | PASS | 9 | A/B table above, re-run; one profile and viewpoint only |
| C13 Camera cut | PASS | 9 | cut frame identical to static capture; single case |

## 7. Defects
- **DEF-T004-style-1 · major · C6 · to implementer (dither spread, band mapping) with research (ramp order in `palette_set`).** Dither seams mix three bands; in lavender_peach the mix is hue-incoherent. Evidence: `m003_stills/lavender_peach_eye70/f4` (150–480, 190–270); indices 19, 9 and 10 co-occur from 444 to 864 m on the ground of `lavender_peach_eye70/f0`; 14–25 % of ground texels sit in three-index 4×4 windows; the stored `c6_layers_lavender_peach.json` reports 44 of 44. PASS: at least 99 % of seam texels in windows of two indices that are ramp neighbours, in every profile, and a check that detects the present case.
- **DEF-T004-style-2 · major · C7 · to implementer (per-profile haze and ramp selection) and research (ramp spacing, the 4 L* reading).** 80 m block at 1 km is under one value band from its background: stored 0/9 lavender, 3/9 teal, 5/9 crimson; mine 4/9 lavender (min 1.9 L*), 8/9 teal, 9/9 crimson. Crop `m003_stills/lavender_peach_eye70/f9` (210–306, 130–194). PASS: ≥ 4 L* in every eye-height view for all five profiles, or a founder-approved re-reading of "one value band".
- **DEF-T004-style-3 · major · C11 · to research and founders (inherent to single-plane position snap with whole-image compensation); implementer extends the check.** Off the reference plane the presented image is not smooth on T1 (8.7 m/s): far edges ± 2.5 output px in 4 px steps, near edges 15 px jumps every 10 frames (about 6 Hz), `c11_subpixel_on/eye70_tower_T1` rows 110–240. The evidence statement "up to half a texel" holds for the far field only. PASS: every depth class within ± 1 output px, or a calibrated bound per depth class accepted by the founders, with `pixel_checks.py smooth` reporting far, reference and near edges.
- **DEF-T004-style-4 · major for the milestone, not against a gating criterion (does not by itself fail the task) · to implementer (configuration) and founders (dither question).** The M0 default reads as mechanical dither: full-face lattice on flat faces and `hybrid` moiré. Evidence: `m003_stills/warm_rust_sepia_street/f6` (0–480, 190–270) and (188–274, 0–184); DIG_max 0.649; proxy 5.9. PASS: flat lit faces resolve to one band with dither confined to transitions, DIG ≤ 0.5 on every frame, no interference arcs on the ground, proxy ≥ 7 per profile. Suggested: default `direction`, narrower spread, and show the clustered or blue-noise mask beside Bayer on the panel board.
- **DEF-T004-style-5 · calibration item · C9, C10 · to research and the founders' calibration packet.** No mode meets yaw and translation together: `hybrid` 0.0351 / 0.0505 / 0.0131 and 0.1507; `direction` 0.1961 / 0.3023 and T3 0.1933 / 0.2955 / 0.0863. A/B evidence is complete. Recommendations in §4. Not a fix-cycle item.
- **DEF-T004-style-6 · minor · C6 · to implementer or research.** slate_crimson far ground seam is non-monotone (index 4 at 4.6–5.4 km, gone, back beyond 6.4 km; 32 of 44 seams). Not visible in stills. Crimson ramp entries 5, 6, 7 are within 1 L* of each other. PASS: 44 of 44.
- **DEF-T004-style-7 · minor · panel board · to implementer.** The negative anchors are greyscale (`control_unquantised/slate_teal_eye70/f4`: quantise off shows the value channel), so any rater identifies them at once and the panel is not blind. PASS: a continuous-tone colour render of the same view. The board also needs captures in the mode that will ship.
- **DEF-T004-style-8 · minor · evidence gaps · to implementer.** Orbit captured only in `hybrid` and one profile; dolly path passes through the near block (f15–f23) and yields no band-pop value; one cut case; A/B on one profile and viewpoint; tool commit differs between stored reports and `tool_versions.txt`.

Counts: major 4 (3 against criteria: two to the implementer with research, one to research and founders; 1 milestone-level to implementer and founders), calibration 1, minor 3, blocker 0.

## 8. Proposals (unregistered; cannot fail a task)
- M-NEW: output-space smoothness by depth class (far, reference, near) on T1/Y1 with compensation on.
- M-NEW: seam purity (share of texels in 4×4 windows with more than two indices or non-neighbour indices).
- Q-NEW: accept near-field judder and far wobble from position snap, or drop the snap and accept F.T12 ≈ 0.03–0.08? Ask with a T1 clip at street level in both settings.
- Q-NEW: validator rule on per-frame `output_block_offset_px` (R-002 §6 defines it per frame; the T-002 validator rejects it), so M-004 sets can be captured as presented.
