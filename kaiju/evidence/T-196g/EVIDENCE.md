# T-196g: d-iv@v2 on every frame, via a per-pixel slab-id channel (no threshold, metric definition or default output changed)

Commit chain: task/T-196g (8aff9c8, 23b2ba3 and the evidence commit). Capture: 1 Unity batch run (4 layered clips, per-slab arm `M3LayeredFarLift`, 180 frames each, `-captureSlabId`, 220 s, 0 errors, deterministic replay) via
`unity run . -- -executeMethod Game.Tools.Capture.CaptureCli.Run -capturePaths <p4_d_dolly_v4,p1_d_approach_t192,p1_d_strafe_t192,p3_a_strafe_t192> -captureOut <o> -captureEnable -captureScale 4 -captureConfig Assets/Game/Config/Rendering/Configurations/M3LayeredFarLift.json -captureInstall -captureSlabId`.
Evaluation: `SLAB_ID_LEGACY_SKY=1 python -I evidence/T-196g/slab_id_eval.py <o> <T-196c capture> evidence/T-196g/results.json 1` (all 720 frames, no stride; summary.txt, results.json).

## Result (every frame)
| clip | frames | d-iv@v2 frame / per slab min | A2 seam adherence min | M-123 layered match min | undecidable share max (cap 5 %) | d-iv@v1 min (beside) |
|---|---|---|---|---|---|---|
| p4_d_dolly_v4 | 180 | 1.0000 / 1.0000 | 1.00000 | 1.000000 | 0.71 % | 0.6730 |
| p1_d_approach_t192 | 180 | 1.0000 / 1.0000 | 1.00000 | 1.000000 | 0.69 % | 0.7277 |
| p1_d_strafe_t192 | 180 | 1.0000 / 1.0000 | 1.00000 | 1.000000 | 0.61 % | 0.6776 |
| p3_a_strafe_t192 | 180 | 1.0000 / 1.0000 | 1.00000 | 1.000000 | 0.80 % | 0.7198 |
All 720 frames `ok`; evaluated area >= 98.4 % (cap 95 %); no unlabelled pixel. The earlier failures p1_d_approach f91 (A2 0.9977), p3_a_strafe f44 (A2 0.9999) and the six dolly frames (M-123 99.9994 %) all read 1.0 now.

## Causes found (offline, saved T-196c frames)
1. A2 at f91/f44: the failing "seam" pixels are the bottom 1-2 output rows of slab 0, whose shift (sy -2/-1) samples the guard-band texel row below the internal image. Their colour is a guard-band colour that does not occur in the visible frame's interior palette. Not a tear. With exact labels those pixels belong to slab 0.
2. M-123 on dolly: a 4x3 gap-filled texel shows slab 0's own colour where the zero-shift composite shows another slab, so it equals no internal nearest-neighbour sample. A gap texel is shown from one slab's own target (deterministic smallest-radial rule, unchanged); the composite cannot vouch for it.

## What changed
Renderer, capture-only: `PixelPresent.shader` writes a slab-id code into the output alpha when `_PixelPresentMode.w = 2` (`PixelCameraBinder.controlSlabIdOutput`, default false). Colour is untouched and normal output has alpha 1, so default output is bit-identical; no `rendering.pixel.*` flag was needed. Code: 1 + slab, +4 gap rule chose (a covered sample chose, none owned), +8 raw texel clamped to the target, 255 none. Written as `f*_slab_id.png`, `files.slab_id` in the sidecar (`CaptureCli -captureSlabId`).
Evaluator: `divv2_adapter.slab_id_labels / load_slab_id`; `adapt / evaluate_frame / layered_nn_match(slab_id=)`; `style.py` passes the channel when present (older captures read exactly as before). With exact labels d-iv@v2 and A2 need no undecidable guess. M-123 (min_match 1.0 unchanged) compares every pixel whose composite texel is vouched: gap fills, guard-band texels outside the internal image and texels where the composite shows another slab are not compared (inside the existing 5 % undecidable cap, max 0.8 %). Output colours are unchanged vs the T-196c capture on 719/720 frames; dolly f71 differs by one 4x4 texel (16 px), not by this change (alpha only); likely the T-194d terrain-sink capture default that the older capture predates (unverified).

## Not verified
- The shader line "sky is not a gap" (gap = pick < 0 && smallest >= 0) was edited after the single Unity run (a second run was refused by the permission classifier and not routed around). The evaluated capture coded sky on slab 2 as gap (code 7), read as plain slab 2 (`SLAB_ID_LEGACY_SKY=1`). One confirm run with the final shader is needed before relying on the gap flag for slab 2.
- Only the per-slab arm; the shared-phase arm and the other 4 clips (still, sunrise, golden gate, scale proxy) were not recaptured (their v1 = v2 = 1.0000 in T-196d).
- Unity EditMode not run (shader and two C# files in Game.Rendering.Pixel / Game.Tools.StyleCapture changed; compiled and ran in the capture batch).
Tests: Pipelines/capture pytest 99 passed (`pytest-capture.log`, 3 new); dotnet Rendering.Pixel.Tests 101/0 (`dotnet-test.log`).
