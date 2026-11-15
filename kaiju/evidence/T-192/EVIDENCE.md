# T-192: far lift in the feel-check and capture configs, C1 / C2 / A2 seam report on moving layered sequences, amber C7 margin, regression

Captures: Game.Tools.Capture.CaptureCli (T-147 driver, x4, 1920x1080 output, full clips, replay identical, 0 errors, 0 warnings: `moving_*_log_summary.txt`), six moving clips x four configurations. Gates unchanged. Analysis: `Tools/Rendering/Pixel/seam_report.py` (new), `capture.py style` (M-003@v1.1), `seam_summary.py` (new). Raw frames stay in the scratchpad (not committed, 4 x ~0.3 GB).

## 1. Configurations (committed default unchanged: every rendering flag stays off in FeatureFlags/)
| config (Assets/Game/Config/Rendering/Configurations) | layerSnap (slabs) | hazeFarLift |
|---|---|---|
| M0StyleFrame | off | off |
| M3HazeFarLift (T-190) | off | on |
| M3Layered (new) | on | off |
| M3LayeredFarLift (new) | on | on |
`slice_feelcheck.flags.json` now turns `rendering.pixel.hazeFarLift` on (feel-check only; `slice_m1.flags.json` and FeatureFlags/Rendering.Pixel.json unchanged).

**Capture-tool bug found and fixed first:** the layered configs rendered the single-snap path in every Bay capture and every stills job. The capture path's `reference_depth_m` (600 m, a scene scale) became the near layer; near 600 m and main 664.874 m are too close for slabs, so the binder warned once ("reference depths 600 m and 664.8740234375 m are too close") and fell back. The first layered capture therefore was a no-op comparison. Fix (capture side only): with layerSnap on, `CaptureDriver.FocusDepth` and `StyleCaptureSession.LayerFocus` use the shipping focus depth 124.2 m (`-captureFocusM` overrides); layerSnap off keeps the path value. Layered frames 100 % in all layered sets afterwards (sidecar `layers` present, boundaries 320-358 m and 2.6-2.9 km).

Clips (60 fps): p4_d dolly (slate_teal, 120 m/s, 180 frames), p1_d scale proxy (slate_crimson, yaw pan, 300), p4_a sunrise (lavender_peach, yaw, 360), and three new translating clips authored for this report in `Tools/Rendering/Pixel/seam_paths/` (not store paths): p1_d_strafe (15 m/s lateral, 88 m altitude), p1_d_approach (30 m/s toward the skyline), p3_a_strafe (20 m/s, Golden Gate, warm_amber_haze), 180 frames each. p1_b, p3_b, p3_c blocked (kaiju actor / M2 streaming).

## 2. C1 / A2 / d-iv per frame on the moving sequences (`m003_moving/`, `seam_report.json`)
c1 per frame = drop <= 2, rho >= 0.9, span >= 15 (registry depth bands), counted over every frame; grid = M-003.d-iv blocks (4x4 output pixels uniform) in the slab-owned regions at the sidecar's one declared block offset and at each region's own best offset.

| path (profile) | frames | config | c1 frames passing | worst drop / min rho / min span | grid d-iv near / main at declared offset | same at each slab's own offset | frames with slab offsets differing |
|---|---|---|---|---|---|---|---|
| p1_d_approach_t192 (slate_crimson) | 180 | M0StyleFrame | 101 / 180 | 4.04 / 0.70 / 8.6 | - | - | - |
| p1_d_approach_t192 (slate_crimson) | 180 | M3HazeFarLift | 158 / 180 | 4.04 / 0.70 / 16.5 | - | - | - |
| p1_d_approach_t192 (slate_crimson) | 180 | M3Layered | 105 / 180 | 4.21 / 0.70 / 8.6 | 0.900 / 0.994 | 1.000 / 0.994 | 33 / 36 |
| p1_d_approach_t192 (slate_crimson) | 180 | M3LayeredFarLift | 155 / 180 | 4.21 / 0.70 / 16.3 | 0.900 / 0.994 | 1.000 / 0.994 | 33 / 36 |
| p1_d_scale_proxy_v5 (slate_crimson) | 300 | M0StyleFrame | 300 / 300 | 0.00 / 1.00 / 16.4 | - | - | - |
| p1_d_scale_proxy_v5 (slate_crimson) | 300 | M3HazeFarLift | 300 / 300 | 0.00 / 1.00 / 24.0 | - | - | - |
| p1_d_scale_proxy_v5 (slate_crimson) | 300 | M3Layered | 300 / 300 | 0.00 / 1.00 / 16.4 | 1.000 / 1.000 | 1.000 / 1.000 | 0 / 60 |
| p1_d_scale_proxy_v5 (slate_crimson) | 300 | M3LayeredFarLift | 300 / 300 | 0.00 / 1.00 / 24.0 | 1.000 / 1.000 | 1.000 / 1.000 | 0 / 60 |
| p1_d_strafe_t192 (slate_crimson) | 180 | M0StyleFrame | 78 / 180 | 3.42 / 0.80 / 12.8 | - | - | - |
| p1_d_strafe_t192 (slate_crimson) | 180 | M3HazeFarLift | 78 / 180 | 3.42 / 0.80 / 20.4 | - | - | - |
| p1_d_strafe_t192 (slate_crimson) | 180 | M3Layered | 78 / 180 | 3.91 / 0.80 / 12.3 | 0.855 / 0.994 | 1.000 / 0.994 | 32 / 36 |
| p1_d_strafe_t192 (slate_crimson) | 180 | M3LayeredFarLift | 81 / 180 | 3.91 / 0.80 / 19.9 | 0.855 / 0.994 | 1.000 / 0.994 | 32 / 36 |
| p3_a_strafe_t192 (warm_amber_haze) | 180 | M0StyleFrame | 11 / 154 | 8.71 / -0.50 / -3.6 | - | - | - |
| p3_a_strafe_t192 (warm_amber_haze) | 180 | M3HazeFarLift | 20 / 154 | 8.71 / 0.50 / 1.0 | - | - | - |
| p3_a_strafe_t192 (warm_amber_haze) | 180 | M3Layered | 11 / 147 | 8.41 / -0.50 / -3.3 | 0.674 / 1.000 | 0.997 / 1.000 | 29 / 36 |
| p3_a_strafe_t192 (warm_amber_haze) | 180 | M3LayeredFarLift | 19 / 147 | 8.41 / 0.50 / 1.2 | 0.674 / 0.999 | 0.997 / 0.999 | 29 / 36 |
| p4_a_sunrise_v5 (lavender_peach) | 360 | M0StyleFrame | 284 / 360 | 0.34 / 0.50 / 17.7 | - | - | - |
| p4_a_sunrise_v5 (lavender_peach) | 360 | M3HazeFarLift | 360 / 360 | 0.00 / 1.00 / 30.5 | - | - | - |
| p4_a_sunrise_v5 (lavender_peach) | 360 | M3Layered | 284 / 360 | 0.34 / 0.50 / 17.7 | 1.000 / 1.000 | 1.000 / 1.000 | 0 / 72 |
| p4_a_sunrise_v5 (lavender_peach) | 360 | M3LayeredFarLift | 360 / 360 | 0.00 / 1.00 / 30.5 | 1.000 / 1.000 | 1.000 / 1.000 | 0 / 72 |
| p4_d_dolly_v4 (slate_teal) | 180 | M0StyleFrame | 180 / 180 | 0.00 / 1.00 / 20.6 | - | - | - |
| p4_d_dolly_v4 (slate_teal) | 180 | M3HazeFarLift | 180 / 180 | 0.00 / 1.00 / 20.6 | - | - | - |
| p4_d_dolly_v4 (slate_teal) | 180 | M3Layered | 180 / 180 | 0.00 / 1.00 / 20.5 | 0.819 / 0.998 | 1.000 / 0.998 | 32 / 36 |
| p4_d_dolly_v4 (slate_teal) | 180 | M3LayeredFarLift | 180 / 180 | 0.00 / 1.00 / 20.5 | 0.819 / 0.998 | 1.000 / 0.998 | 32 / 36 |

**A2 (palette adherence dE00 <= 2, output and internal): 1.000 on every frame of every set in all four configurations** (0 off-palette pixels). DIG share 1.0 everywhere.

Reading:
- **c1 does not depend on the slabs.** Layered and single give the same pass counts and the same worst values within 0.5 L* (p4_d 180/180 both, p1_d scale 300/300 both, sunrise 360/360 with the lift both; drop 3.42 -> 3.91 and 4.04 -> 4.21 on the strafe/approach clips, which fail without the slabs too). The lift fixes the span (79 -> 0 span failures on p1_d approach, 89 -> 0 on strafe, 76 sunrise rho failures -> 0).
- **The p1_d strafe/approach and p3_a strafe clips fail c1 on 55-90 % of frames with and without layering and with and without the lift** (drop 3.4-8.7 L*, rho 0.5-0.8). That is the metric defect T-190 diagnosed (whole-frame band means follow albedo and lighting: the moving camera walks across sunlit and shaded slopes), not a seam. Not changed; the M-003 v1.2 proposal stands (T-190 HANDOFF). The three authored store paths (scale proxy, sunrise, dolly) pass on every frame with the lift on, layered or not.
- **C2 (M-003.c2, haze_cal, 5 profiles, layered and single, `c2/`):** pass in all four configurations; worst profile lavender_peach: rise 0.0 (limit 0.5), far term -8.76 L* single and layered (-17.80 with the lift; limit <= 0), pair-mean drop 0.0. Layered = single to the digit (static cut frames; the layered path engaged, no warning). The stills set (180 frames) shows the same c1 (drop 2.2, rho 0.964, span 42.7) layered and single.
- **The pixel grid (d-iv) fails on layered translating clips: 0.61-0.73 against 0.995** (the yaw-only clips p1_d scale proxy and p4_a sunrise stay 1.000: slab shifts equal) (`m003_moving/*/M3Layered*`, table: near region 0.82-0.90 on slate, 0.67 on amber at the declared offset). Cause measured, not a rendering error: each slab region is a perfect 4x4 block grid (1.000 at its own offset, 0.997 for amber), but the three slabs are presented with different sub-pixel output shifts (`layers.shift_output_px`, up to 3-4 px apart, differing in 29-33 of 36 sampled frames), so their block lattices are offset against each other while the sidecar declares one offset. The slab-owner junctions (slab seam on the ground, near building over far skyline) are 2.2-2.9 % of the texels. Still frames (cuts, all shifts equal) are 1.000. **Trade-off, not fixable inside the slab design:** a global block grid needs one common output shift, which leaves up to +-0.5 texel (2 px) of near/far edge phase error and fails the C11 gate (1 px, passes now: 0.96 / 0.96 / 0.96, `C11_SLAB.md`); keeping the C11 gate keeps three lattices. Options for the style panel: (A) keep per-slab shifts and read d-iv per slab region from `layers.shift_output_px` (a metric reading change, not made here); (B) one common shift and C11 at about 2 px; (C) subPixelPresent off in layered mode (grid 1.0, no sub-texel smoothing). Recommendation A. Logged, gate and sidecar left unchanged.

## 3. Seam checks on the slab boundary and haze bands (`seam_table2.md`; definitions in `seam_report.py`)
| path | config | seam purity (C6 ground, pure share / 3+-index window share) | slab-boundary pairs: differ / hard (2+ ranks) | outside-zone pairs: differ / hard | A-B-A blink, zone / rest |
|---|---|---|---|---|---|
| p1_d_approach_t192 | M0StyleFrame | 0.793 / 0.030 | 0.314 / 0.168 | 0.111 / 0.037 | 0.001 / 0.001 |
| p1_d_approach_t192 | M3HazeFarLift | 0.793 / 0.030 | 0.314 / 0.168 | 0.124 / 0.007 | 0.001 / 0.001 |
| p1_d_approach_t192 | M3Layered | 0.800 / 0.037 | 0.309 / 0.180 | 0.112 / 0.037 | 0.000 / 0.001 |
| p1_d_approach_t192 | M3LayeredFarLift | 0.800 / 0.037 | 0.309 / 0.180 | 0.124 / 0.007 | 0.000 / 0.001 |
| p1_d_scale_proxy_v5 | M0StyleFrame | 1.000 / 0.000 | 0.312 / 0.297 | 0.115 / 0.106 | 0.001 / 0.002 |
| p1_d_scale_proxy_v5 | M3HazeFarLift | 1.000 / 0.000 | 0.312 / 0.297 | 0.149 / 0.032 | 0.001 / 0.002 |
| p1_d_scale_proxy_v5 | M3Layered | 1.000 / 0.000 | 0.313 / 0.298 | 0.117 / 0.107 | 0.001 / 0.002 |
| p1_d_scale_proxy_v5 | M3LayeredFarLift | 1.000 / 0.000 | 0.313 / 0.298 | 0.150 / 0.032 | 0.001 / 0.002 |
| p1_d_strafe_t192 | M0StyleFrame | 1.000 / 0.000 | 0.271 / 0.236 | 0.101 / 0.033 | 0.000 / 0.000 |
| p1_d_strafe_t192 | M3HazeFarLift | 1.000 / 0.000 | 0.271 / 0.236 | 0.123 / 0.007 | 0.000 / 0.000 |
| p1_d_strafe_t192 | M3Layered | 1.000 / 0.000 | 0.302 / 0.256 | 0.103 / 0.034 | 0.001 / 0.000 |
| p1_d_strafe_t192 | M3LayeredFarLift | 1.000 / 0.000 | 0.302 / 0.256 | 0.126 / 0.007 | 0.001 / 0.000 |
| p3_a_strafe_t192 | M0StyleFrame | 0.843 / 0.133 | 0.689 / 0.014 | 0.421 / 0.005 | 0.000 / 0.000 |
| p3_a_strafe_t192 | M3HazeFarLift | 0.902 / 0.130 | 0.689 / 0.014 | 0.404 / 0.005 | 0.000 / 0.000 |
| p3_a_strafe_t192 | M3Layered | 0.877 / 0.062 | 0.684 / 0.013 | 0.420 / 0.005 | 0.002 / 0.002 |
| p3_a_strafe_t192 | M3LayeredFarLift | 0.922 / 0.060 | 0.684 / 0.013 | 0.404 / 0.005 | 0.002 / 0.002 |
| p4_a_sunrise_v5 | M0StyleFrame | 1.000 / 0.000 | - / - | 0.119 / 0.014 | - / 0.000 |
| p4_a_sunrise_v5 | M3HazeFarLift | 1.000 / 0.000 | - / - | 0.099 / 0.006 | - / 0.000 |
| p4_a_sunrise_v5 | M3Layered | 1.000 / 0.000 | - / - | 0.119 / 0.014 | - / 0.000 |
| p4_a_sunrise_v5 | M3LayeredFarLift | 1.000 / 0.000 | - / - | 0.099 / 0.006 | - / 0.000 |
| p4_d_dolly_v4 | M0StyleFrame | 0.761 / 0.130 | 0.930 / 0.317 | 0.134 / 0.027 | 0.021 / 0.010 |
| p4_d_dolly_v4 | M3HazeFarLift | 0.761 / 0.130 | 0.930 / 0.317 | 0.134 / 0.027 | 0.021 / 0.010 |
| p4_d_dolly_v4 | M3Layered | 0.759 / 0.134 | 0.913 / 0.342 | 0.131 / 0.026 | 0.024 / 0.010 |
| p4_d_dolly_v4 | M3LayeredFarLift | 0.759 / 0.134 | 0.913 / 0.342 | 0.131 / 0.026 | 0.024 / 0.010 |

- Slab boundary pairs (same-surface neighbours straddling the 320-358 m or 2.6-2.9 km boundary): the share that differs is the haze band edge itself (0.27-0.93, coincident with the slab boundary by design) and is within 0.03 of the single render; two-rank "hard" mismatches rise by 0.5-2.5 points (p4_d 31.7 -> 34.2 %, p1_d strafe 23.6 -> 25.6 %, approach 16.8 -> 18.0 %), p3_a unchanged (1.4 -> 1.3 %). No tear lines, no new impure ground seam texels (C6 pure share 0.76 single vs 0.76 layered on p4_d, 0.84 vs 0.88 amber: below 0.99 on real Bay ground in both, a property of the C6 test, calibrated on the test scene, not of the slabs).
- Haze band seams: the far lift reduces hard mismatches outside the zone (p1_d scale 10.6 -> 3.2 %, approach 3.7 -> 0.7 %) and raises amber ground seam purity 0.877 -> 0.922. No band seam artefact from the lift.
- A-B-A shimmer: the slab seams do not blink (zone rate within 0.03 points of rest). **The near slab does:** inside the near slab beyond its own reference depth (120-320 m) the share of same-surface texels that flip A-B-A is 0.5-1.0 % against 0.01-0.3 % single (p3_a strafe 0.0052 vs 0.0002 at 120-320 m; p1_d strafe 0.0110 vs 0.0032; none beyond the 320 m boundary, none at the 2.6-2.9 km boundary). This is the documented any-depth bound (R-131 / T-181 "exact or nothing": surfaces at 2-3 x the layer reference are not on its texel lattice), roughly 0.1 % of all texels per frame; no fix available inside the three-slab design (a fourth slab or a larger near reference are design changes affecting the T-187 C11 near result), recorded for the panel.
- Image check (p4_d frame 100, p3_a frame 90, p1_d strafe frame 90, layered against single, side by side with the index-difference map): the same picture up to 1-texel edge shifts and sky dither phase; no visible line at either boundary (images not committed, scratchpad).

## 4. Amber C7 margin (`amber_far_start_sweep.json`; 11 + 4 candidates, one Unity run each with `-captureInstall`; C7 = 80 m block dL min over 9 far frames, c1 = p3_a span)
| hazeFarStartM | hazeFarFullM | C7 near 150 m dL min | C7 1 km dL min (limit 4) | c1 span on p3_a (limit 15) |
|---|---|---|---|---|
| 400 | 4000 | 23.64 | 4.30 | 19.94 |
| 450 | 4000 | 18.65 | 4.18 | 18.47 |
| 500 | 4000 | 17.53 | 3.99 | 17.67 |
| 550 | 4000 | 17.53 | 3.63 | 16.29 |
| 575 | 4000 | 17.53 | 3.81 | 16.11 |
| 600 | 4000 | 17.53 | 10.55 | 15.99 |
| 625 | 4000 | 17.53 | 13.31 | 15.91 |
| 650 | 4000 | 17.53 | 13.25 | 15.85 |
| 700 | 4000 | 17.53 | 13.25 | 15.74 |
| 800 | 4000 | 17.53 | 11.73 | 15.68 |
| 1000 | 4000 | 17.53 | 9.49 | 15.68 |
| 625 | 2500 | 17.53 | 6.37 | 16.01 |
| 600 | 3000 | 17.53 | 4.18 | 16.05 |
| 625 | 3000 | 17.53 | 4.18 | 15.97 |
| 625 | 5000 | 17.53 | 12.41 | 15.89 |

The two bind exactly as T-190 expected for 450-575 (1 km dL falls below the limit at 500-575: 3.99, 3.63, 3.81) and then cross a rung cliff at 600 (3.81 -> 10.55), while the span falls steadily (19.94 at 400 -> 15.74 at 700). Chosen **hazeFarStartM 625, hazeFarFullM 4000: C7 1 km 13.31 L* (margin 9.3, was 4.3), span 15.91 (margin 0.91; the span threshold is Assumed), e unchanged (0.475 / 0.441 / 34.5).** 600 is at the cliff edge (575 fails); 625 sits one step into the plateau. Raising the full distance (5000) or lowering it (2500) moves C7 to 12.41 / 6.37 and the span by 0.03: no better. Near-150 m dL falls 23.6 -> 17.5 (still 13.5 above the limit).

## 5. Final confirmation (merged values, `final_*_log_summary.txt`, `style_final/`, `c7/`, `C11_SLAB.md`)
- M-003.c1 / e / A2 on the 5 T-147 sets with the lift on (M3HazeFarLift): all pass: p1_d scale 0 / 1.0 / 24.0, e 0.493 / 0.444 / 30.8; p3_a 0 / 1.0 / 15.91, 0.475 / 0.441 / 34.5; p4_a still 0 / 1.0 / 31.4, 0.482 / 0.437 / 30.5; p4_a sunrise 0 / 1.0 / 30.5, 0.488 / 0.437 / 30.5; p4_d 0 / 1.0 / 20.7, 0.468 / 0.418 / 27.5 (drop / rho / span; sky / horizon / contrast).
- C7 (layerSnap off = on, lift on, five profiles): near 150 m / far 1 km dL min: rust 15.8 / 6.45, amber 17.5 / 13.31, teal 11.9 / 6.35, lavender 9.4 / 11.76, crimson 17.3 / 12.72; marker scale check pass in all five.
- C11 (rigedge layerSnap): near / reference / far worst 0.96 / 0.96 / 0.96 px, 24 / 72 / 48 starts within 1 px. Unchanged.

## 6. Regression (Unity EditMode, whole project, merged tree): see `evidence/regression/T-192.md`.

## Not done
d-iv on layered moving frames fails the gate as measured (panel decision A/B/C above; nothing relaxed); near-slab shimmer 120-320 m recorded; c1 stays whole-frame (v1.2 not implemented); the far lift is on in the feel-check and capture configs only (shipping default still off); only three authored store clips plus three seam clips moved (kaiju/mech/coop paths blocked); layered capture is 180-360 frame clips at x4, not the 60-frame five-profile matrix.
