# T-196c: d-iv@v2 on the real layered T-196 clips (advisory; no gate, threshold, verdict or registry changed)

Tools: `Pipelines/capture/capture_tools/divv2_adapter.py` (sidecar + depth -> d-iv@v2 inputs), `Pipelines/capture/divv2_clips.py` (per clip), `divv2_report.py` (tables). Tests: `Pipelines/capture/tests/test_divv2_adapter.py` (6, incl. a real sidecar fixture) + `test_divv2.py` (11): 17 pass.
Capture: CaptureCli, `-captureEnable -captureScale 4`, default terrain sink (0.02 m), 8 clips x 2 arms (2 Unity runs), 0 errors, 1 known warning, deterministic, replay identical on all. Arm per-slab = `M3LayeredFarLift` (layerSnap on, `rendering.pixel.layerSharedPhase` OFF; checked in sidecar flags, per-slab `shift_output_px` differ). Arm shared = `M3LayeredSharedPhase` (flag ON). Frames, depth and sidecars stay in the scratchpad (412 MB, not in git); `results.json` holds per-frame numbers and the sha256 of each clip's capture `hashes.sha256`.
The 4 translating clips are p1_d_approach, p1_d_strafe, p3_a_strafe, p4_d_dolly; the other four (scale proxy, golden gate, still, sunrise) are yaw/still. Every 3rd frame was evaluated (stride 3; 580 of 1741 frames per arm). "d-iv@v1" = whole-frame value at the sidecar `output_block_offset_px` (the registered reading); "A2 seam" = `seam_adherence` (palette check of the excluded seam pixels); "seam share" = 1 - evaluated area. "guard" = label pixels within g px of another slab's label are set to seam.

## Adapter facts (real sidecar, style_capture@v1)
- Depth is the `depth` PNG (png16_log, eye-space z); there is no `depth_linear` file. Decoded z = near*(far/near)^(v/65535); sky (65535) = far slab. Slab cuts are RADIAL (`layers.boundaries_radial_m`; `slab_clip_eye_m` ranges overlap, informational), so the adapter converts z to radial with the sidecar fov.
- Declared per-slab offset = ((-sx) mod k, sy mod k) from `layers.shift_output_px`, the capture writer's own formula for `output_block_offset_px` (main slab equals the frame offset; tested).
- Each slab's pixels are labelled on that slab's own lattice; pixels claimed by none or two slabs are seam.
- Bug found and fixed in `divv2.py` (T-196b): block extraction rolled the frame, so the wrapped strip made false non-uniform blocks at the right/bottom edge (0.9955 on a clean frame). Now cropped (partial edge blocks excluded, as v1 documents).


### d-iv@v2, label guard 0 px (literal capture labels)

| clip | arm | frames (stride 3) | d-iv@v2 clip min | slab near / main / far min | d-iv@v1 min | seam share mean / max | A2 seam adherence min | failing frames | worst frame |
|---|---|---|---|---|---|---|---|---|---|
| p1_d_approach_t192 | perslab | 60 | 0.9582 | 0.9995 / 0.9582 / 0.9909 | 0.7287 | 0.0159 / 0.0233 | 1.0000 | 30 | 18 |
| p1_d_approach_t192 | shared | 60 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 1.0000 | 0.0033 / 0.0058 | 1.0000 | 0 | 0 |
| p1_d_scale_proxy_v5 | perslab | 100 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 1.0000 | 0.0022 / 0.0058 | 1.0000 | 0 | 0 |
| p1_d_scale_proxy_v5 | shared | 100 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 1.0000 | 0.0023 / 0.0058 | 1.0000 | 0 | 0 |
| p1_d_strafe_t192 | perslab | 60 | 0.9637 | 1.0000 / 0.9637 / 0.9930 | 0.6776 | 0.0161 / 0.0278 | 1.0000 | 35 | 24 |
| p1_d_strafe_t192 | shared | 60 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 1.0000 | 0.0036 / 0.0058 | 1.0000 | 0 | 0 |
| p3_a_golden_gate_v3 | perslab | 120 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 1.0000 | 0.0000 / 0.0000 | 1.0000 | 0 | 0 |
| p3_a_golden_gate_v3 | shared | 120 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 1.0000 | 0.0000 / 0.0000 | 1.0000 | 0 | 0 |
| p3_a_strafe_t192 | perslab | 60 | 0.9851 | 0.9977 / 0.9911 / 0.9851 | 0.7208 | 0.0128 / 0.0258 | 1.0000 | 27 | 66 |
| p3_a_strafe_t192 | shared | 60 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 1.0000 | 0.0028 / 0.0058 | 1.0000 | 0 | 0 |
| p4_a_still_v5 | perslab | 1 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 1.0000 | 0.0058 / 0.0058 | 1.0000 | 0 | 0 |
| p4_a_still_v5 | shared | 1 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 1.0000 | 0.0058 / 0.0058 | 1.0000 | 0 | 0 |
| p4_a_sunrise_v5 | perslab | 120 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 1.0000 | 0.0053 / 0.0058 | 1.0000 | 0 | 0 |
| p4_a_sunrise_v5 | shared | 120 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 1.0000 | 0.0053 / 0.0058 | 1.0000 | 0 | 0 |
| p4_d_dolly_v4 | perslab | 60 | 0.9764 | 0.9997 / 0.9764 / 0.9915 | 0.6730 | 0.0124 / 0.0213 | 1.0000 | 35 | 21 |
| p4_d_dolly_v4 | shared | 60 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 1.0000 | 0.0032 / 0.0058 | 1.0000 | 0 | 0 |

### d-iv@v2, label guard 4 px (one block, review tolerance)

| clip | arm | frames (stride 3) | d-iv@v2 clip min | slab near / main / far min | d-iv@v1 min | seam share mean / max | A2 seam adherence min | failing frames | worst frame |
|---|---|---|---|---|---|---|---|---|---|
| p1_d_approach_t192 | perslab | 60 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 0.7287 | 0.0468 / 0.0589 | 1.0000 | 21 | 126 |
| p1_d_approach_t192 | shared | 60 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 1.0000 | 0.0339 / 0.0443 | 1.0000 | 0 | 0 |
| p1_d_scale_proxy_v5 | perslab | 100 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 1.0000 | 0.0416 / 0.0474 | 1.0000 | 0 | 0 |
| p1_d_scale_proxy_v5 | shared | 100 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 1.0000 | 0.0418 / 0.0474 | 1.0000 | 0 | 0 |
| p1_d_strafe_t192 | perslab | 60 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 0.6776 | 0.0486 / 0.0682 | 1.0000 | 27 | 96 |
| p1_d_strafe_t192 | shared | 60 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 1.0000 | 0.0365 / 0.0439 | 1.0000 | 0 | 0 |
| p3_a_golden_gate_v3 | perslab | 120 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 1.0000 | 0.0301 / 0.0301 | 1.0000 | 0 | 0 |
| p3_a_golden_gate_v3 | shared | 120 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 1.0000 | 0.0301 / 0.0301 | 1.0000 | 0 | 0 |
| p3_a_strafe_t192 | perslab | 60 | 0.9999 | 1.0000 / 0.9999 / 1.0000 | 0.7208 | 0.0430 / 0.0592 | 1.0000 | 12 | 51 |
| p3_a_strafe_t192 | shared | 60 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 1.0000 | 0.0324 / 0.0406 | 1.0000 | 0 | 0 |
| p4_a_still_v5 | perslab | 1 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 1.0000 | 0.0371 / 0.0371 | 0.9665 | 1 | 0 |
| p4_a_still_v5 | shared | 1 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 1.0000 | 0.0371 / 0.0371 | 0.9665 | 1 | 0 |
| p4_a_sunrise_v5 | perslab | 120 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 1.0000 | 0.0362 / 0.0372 | 0.9572 | 120 | 0 |
| p4_a_sunrise_v5 | shared | 120 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 1.0000 | 0.0362 / 0.0372 | 0.9575 | 120 | 0 |
| p4_d_dolly_v4 | perslab | 60 | 0.9999 | 1.0000 / 0.9999 / 0.9999 | 0.6730 | 0.0399 / 0.0504 | 1.0000 | 2 | 129 |
| p4_d_dolly_v4 | shared | 60 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 1.0000 | 0.0300 / 0.0357 | 1.0000 | 0 | 0 |
### Guard 2 px (4 translating clips only)
| clip | per-slab v2 min | seam mean / max | failing frames | shared v2 min |
|---|---|---|---|---|
| p1_d_approach_t192 | 0.9623 | 0.0386 / 0.0520 | 4 | 1.0000 |
| p1_d_strafe_t192 | 0.9805 | 0.0400 / 0.0518 | 5 | 1.0000 |
| p3_a_strafe_t192 | 0.9946 | 0.0366 / 0.0496 | 1 | 1.0000 |
| p4_d_dolly_v4 | 0.9758 | 0.0319 / 0.0399 | 12 | 1.0000 |

## Findings
1. **Literal d-iv@v2 FAILS on the real per-slab frames of all four translating clips** (clip min: approach 0.958, strafe 0.964, p3_a strafe 0.985, dolly 0.976; gate 0.995; 27-35 of 60 evaluated frames fail per clip, p3_a 27). The failing slabs are main and far (near >= 0.9977). Worst: p1_d_approach f18, main slab 0.9582. d-iv@v1 reproduces T-192 (0.729 / 0.678 / 0.721 / 0.673 here vs 0.728 / 0.678 / 0.606 / 0.673 recorded; p3_a differs, mine is a min over every 3rd frame). The shared arm reads 1.000 on every clip in v1 and v2; yaw/still clips are 1.000 in both arms.
2. **Where it fails:** on one sampled frame (p1_d_approach f90) every non-uniform counted block (0 near, 241 main, 311 far) has its centre exactly 2 px inside the slab label boundary, i.e. the block next to the edge, none in the interior. Probable cause (inferred, not isolated): the capture depth is internal-resolution and not shifted with each slab's 0-4 px output shift, so the capture label edge and the presented slab edge disagree by up to the shift, the "within one block" case of the review. With a 4 px (one block) guard every slab scores >= 0.9999 in all 8 clips; a 2 px guard is not enough (table above). Blind per-slab offset estimate equals the declared one in every counted slab-frame (0 blind failures), so no resampling inside a slab was found. I did NOT make the guard a default and changed no threshold: whether v2 is read with guard = k is the lead's decision.
3. **Seam share vs the review's 5 % cap:** guard 0: mean 1.2-1.6 % on translating per-slab clips (max up to 2.8 %), 0-0.6 % on others. Guard 4: mean 4.0-4.9 % (per-slab translating), 3.0-3.7 % (shared), still/yaw 3.0-4.2 %, but the MAX is 5.0-6.8 % on per-slab translating frames, so the >= 95 % evaluated-area rule fails in 2-27 of 60 frames per clip there (approach 21, strafe 27, p3_a 12, dolly 2) and in 0 shared frames. About 3 % of any frame is seam even without motion at guard 4 (rim, sky, slab edges). Cap not decided here.
4. **A2 seam adherence:** 1.000 on every frame of every translating clip, both arms, guards 0 and 4, and on all yaw clips at guard 0. At guard 4 it reads 0.957-0.967 on still and sunrise in BOTH arms: seam pixels carry haze/dither palette colours absent from the counted blocks, a limit of taking the palette from counted blocks (should be the profile palette), not tearing. Not changed.
5. **C11** not re-measured (a per-configuration synthetic rig measurement, not per clip; binder unchanged; Unity budget kept to 2 runs): per-slab 0.96 px pass, shared phase 1.49 px fail (`evidence/T-196/C11_SHARED_PHASE.md`).
6. **2AFC (P-3) pairs:** `evidence/T-196c/afc2/<clip>/f0000{40,90,140}.png` (640x360 crop of the 1080p output, per-slab vs shared side by side, order random, seed 196) for p1_d_strafe_t192, p3_a_strafe_t192, p4_d_dolly_v4; `key.json` in each folder maps left/right to the arm and must stay hidden from the viewer. No human has viewed them.

## Not verified
Every 3rd frame only; the displaced-label cause rests on one frame; no human 2AFC result; no C11 re-run; the 5 % cap and the guard are undecided; `divv2.py` default stays guard 0 (fails per-slab translation).
