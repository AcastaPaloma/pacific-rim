# T-190: M-003.c1 / M-003.e on real Bay frames, far haze lift

Result (one batched recapture, 22 frames, 5 sets, config `M3HazeFarLift`, flag `rendering.pixel.hazeFarLift` ON; log `unity_capture.log`, 0 errors 0 warnings, replay identical): **M-003.c1 PASS and M-003.e (sky share, horizon, contrast) PASS on all 5 sets, all frames** (`style/<set>/report.json`, per-frame numbers `per_frame.txt`; verdict label INCOMPLETE only because the five-profile frame-count coverage is not met by a smoke set). C7 and C11 re-measured with the flag ON (`c7/`, `c11_slab_results.json`, `unity_c7c11.log`, 0 errors, 1 expected warning): pass.

| set (profile) | c1 drop / rho / span (limit 2 / 0.9 / 15) before -> after | e sky (0.2-0.5) / horizon (<=0.45) / contrast (>=25) before -> after |
|---|---|---|
| p1_d scale proxy (slate_crimson) | 0.0 / 1.0 / 6.7 -> 0.0 / 1.0 / 24.0 | 0.461,0.526,22.6 -> 0.493,0.444,30.8 |
| p3_a Golden Gate (warm_amber_haze) | 0.0 / 1.0 / 12.6 -> 0.0 / 1.0 / 19.9 | 0.518,0.556,43.4 -> 0.475,0.441,34.5 |
| p4_a still (lavender_peach) | 0.56 / 0.5 / 1.4 -> 0.0 / 1.0 / 31.4 | 0.645,0.392,22.1 -> 0.482,0.437,30.5 |
| p4_a sunrise (lavender_peach) | 0.65 / 0.5 / 1.3 -> 0.0 / 1.0 / 30.5 | 0.647,0.392,22.1 -> 0.488,0.437,30.5 |
| p4_d dolly (slate_teal) | 0.0 / 1.0 / 20.8 (already c1 pass) -> 0.0 / 1.0 / 20.7 | 0.387,0.500,27.5 -> 0.468,0.418,27.5 |

(Worst frame per set, "before" = T-147 smoke set and a no-Unity re-analysis, the numbers in HANDOFF T-147 "1.3 to 12.6" were the span shortfall, not the drop: the drop part passed in all 5.)

## Diagnosis (existing captures, no Unity; then one re-render of the T-147 paths for images, since T-147 did not keep them)
1. c1 had three parts. Drop <= 2 passed everywhere. The failing parts were **span** (far - near mean L*, limit 15, Assumed) on 4 of 5 and **rho** on the two p4_a sets (3 bands, 33.4 / 35.4 / 34.8: rho 0.5).
2. **Palette ladder quantisation, not the haze count.** Output pixels are palette colours: for lavender_peach the 150-2500 m geometry sits on rungs 24.9 / 32.9 / 42.3 (80 % of the 1-2.5 km pixels on 32.9). The grade maps scene value to rung by p = 1.25 v - 0.25; with the log haze (start 48 m, full 20 km) the amount at 1-2.5 km is only 0.5-0.65, so the scene value never reaches the top rungs (45.9, 55.1) before the frame ends. Same for slate_crimson (rungs 27.1 / 36.3 / 49.8; 92 % of 1-2.5 km pixels on 36.3). The span cannot reach 15 inside a 150 m - 2.5 km view without the top rungs.
3. The sky is not the cause: horizon sky L 55 (rung top) is lighter than the geometry, i.e. far geometry never converges to the horizon colour (visible in the frames: slate buildings against a salmon sky).
4. **e is mostly framing, not look.** `horizon` is a pure function of camera pitch (h = 0.5 - tan(pitch_up)/(2 tan(vfov/2)), vfov 36 deg): <= 0.45 needs pitch up >= 2 deg, and the paths had pitch -2 to +4 with 4 deg only on p4_a. Sky share 0.2-0.5 then needs skyline above the horizon: from the 146 m Coit Tower deck the sky is 0.62-0.65 at any pitch that satisfies the horizon; contrast >= 25 is structurally impossible on slate_crimson with a lit scene (ladder 27.1 - 49.8 = 22.7 range) unless 10 % of pixels use the 18.8 / 9.9 rungs (dark near foreground).
5. **Metric defects (found, not applied; gate not changed):** (a) c1 compares band means of whole-frame content, so it depends on albedo and lighting, not only haze. On the T-147 p3_a clip a 5 m move turned band 0 (50 m, in shade, L 30) into sunlit slope (L 41) and failed all three parts (`drop 1.2, rho 0.5, span 9.1`); p4_d at 180 m closer put no band beyond 1 km in view (span 12.4 at bands 0-3). (b) span >= 15 is not scaled to the log-depth range the frame actually covers (bands 2-4 span a factor of 6, the validation scene 4 bands over a factor of 50+). (c) the 1 % band floor lets a 1.6 % sliver (150-400 m) decide the near end. Proposed correction (M-003 v1.2, to decide in the style panel, not implemented): evaluate c1 per frame only when the frame holds geometry in >= 3 bands over >= 1.5 decades of depth, take band means over pixels of the same palette-index family (or median instead of mean), and scale span_min by log(far/near centre ratio)/log(reference ratio); keep drop 2 and rho 0.9 unchanged.

## Fix
- Flag `rendering.pixel.hazeFarLift` (default off, Config/FeatureFlags/Rendering.Pixel.json). Per profile (`PaletteProfileTuning.json`): `hazeFarStartM` (0 = no lift), `hazeFarFullM`, `hazeFarPower`. Beyond the start the haze level rises to the last band (pure airlight) at the full distance: `level += (bands - 1 - level) * w^power`, w = ln(d / start) / ln(full / start) (HazeMath.FarLift mirrors PixelHaze.shader). Continuous at the start, monotone, never lowers the level.
- Values (inert unless the flag is on): lavender_peach 900 / 1400 / 0.8, slate_crimson 900 / 1300 / 0.8, warm_amber_haze 400 / 4000 / 1.0; slate_teal and warm_rust_sepia none. Chosen from three stills grids (46 + 21 + 36 candidate poses) rendered with the T-147 driver.
- Framing (paths are authored content, thresholds untouched): pitch up 2.2-3.0 deg on all five paths (horizon <= 0.45); p4_a alt 92 m yaw 160 (sunrise 156 to 164), p1_d alt 88 m yaw 156 to 164, p3_a yaw 180 and a static camera, p4_d dolly 360 m (x 63300 to 62940). Each change is noted in the path file.
- Tools: `-captureConfig` and `-captureInstall` for `CaptureCli` and `-captureConfig` for `StyleCaptureSession` (default unchanged); `CaptureDriver` now writes a gap-free sequence index when `-captureStride` thins a clip (style_capture@v1 requires 0..n-1; before this a stride run was INVALID_INPUT).

## C7 and C11 with the flag on (min dL of the 80 m block over 9 far frames; limit 4 L*; T-187 flag off in brackets)
| profile | near 150 m | far 1 km | marker scale |
|---|---|---|---|
| warm_rust_sepia | 15.8 | 6.4 (6.4) | pass |
| warm_amber_haze | 23.6 | **4.3 (8.9)** | pass |
| slate_teal | 11.9 | 6.4 (6.4) | pass |
| lavender_peach | 9.4 | 11.8 (8.3) | pass |
| slate_crimson | 17.3 | 12.7 (4.5) | pass |
Layered off and on identical. **warm_amber_haze 1 km margin is thin (4.3 vs 4)**: binding parameter is `warm_amber_haze.hazeFarStartM` (400); raising it to ~550 buys margin, costs c1 span on p3_a (19.9 now, needs 15). C11 (layered slabs, rig near-edge depth): near / reference / far worst phase 0.958 / 0.957 / 0.959 px, 24 / 72 / 48 starts, all within 1 px: identical to T-187 (slab boundaries follow the unlifted bands; the binder warns once).

## Not done
Flag stays off in the shipped configuration (decision to ship the far lift, and the amber margin, belong to the style panel); the proposed c1 definition is not implemented; shadow-free scene (no sun shadows) limits the near darkness that crimson e.contrast depends on; kaiju/mech/coop paths still blocked; 22 frames, full 60-frame matrix not re-run.
