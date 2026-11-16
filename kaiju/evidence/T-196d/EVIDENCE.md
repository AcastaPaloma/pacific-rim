# T-196d: d-iv@v2 with exact presented-frame slab labels (advisory; no gate, threshold, verdict or registry changed)

Tools: `Pipelines/capture/capture_tools/divv2_adapter.py` (`presented_labels`, `gap_fill_ambiguous`, `internal_labels` with depth quantum), `capture_tools/divv2.py` (`seam_adherence` palette), `divv2_clips.py`, `divv2_report_d.py`. Tests: `Pipelines/capture/tests` 94 pass (new: presented-label mapping against the shader rule, gap-fill ambiguity). Frames: the T-196c saved capture (8 clips x 2 arms, every 3rd frame, 580 per arm; no Unity run, no audio). `results.json` here holds per-frame numbers and the capture hashes. Threshold 0.995, 5 % excluded-area cap, blind offset == declared and A2 1.000 unchanged; the guard is 0 everywhere (default `guard_px=0`, no guard option used).

## Cause (T-196c finding 2, corrected)
The T-196c labels used only each slab's shift modulo k (a lattice phase) and put the slab label on that lattice; the renderer (`PixelPresent.shader` `PixelLayeredColour`) instead samples slab i at internal texel (floor((c + sx_i)/k), floor((r - sy_i)/k)) (image rows; guard offset cancels because the capture internal image is the unpadded crop; derivation checked against `output_block_offset_px` = ((-sx) mod k, sy mod k)) and takes the FIRST slab, near to far, that is covered and owns the sample. The capture `depth` is the zero-shift composite (`PixelComposite.shader`), so its label L(t) = first slab covered and owned at texel t. Exact mapping used now, per output pixel, slabs near to far: L(t_i) == i -> slab i; L(t_i) > i -> slab i proven not owned, continue; L(t_i) < i, a gap (nothing owned), or t_i outside the internal image -> undecidable, pixel is seam (-1). No px guard.

Second cause found while doing this: the composite takes a gap from the covered slab with the SMALLEST radial distance, so a slab i+1 sample inside slab i's range (clip ranges overlap, `layers.slab_clip_eye_m`) can fill a texel slab i does not cover and then reads as slab i from its depth. Seen on dolly f114: slab 0 lattice rows 662-665 were presented from slab 1 although the depth said slab 0. The composite depth cannot tell the two apart, so texels that are inside the overlap of two clip ranges AND within one texel of a label edge are undecidable (-1, counted as excluded area; derived from the sidecar clip ranges, not tuned). Also undecidable: texels within one png16_log step (ln(far/near)/65535 relative) of a slab boundary radial. Excluded area is reported below (cap 5 %).

## Results (guard 0, presented-label mapping, stride 3)

| clip | arm | frames | d-iv@v2 clip min | near / main / far min | d-iv@v1 min | seam share mean / max | A2 seam adherence min | blind-offset failures | failing frames |
|---|---|---|---|---|---|---|---|---|---|
| p1_d_approach_t192 | perslab | 60 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 0.7287 | 0.0118 / 0.0200 | 1.0000 | 0 | 0 |
| p1_d_approach_t192 | shared | 60 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 1.0000 | 0.0055 / 0.0133 | 1.0000 | 0 | 0 |
| p1_d_scale_proxy_v5 | perslab | 100 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 1.0000 | 0.0041 / 0.0080 | 1.0000 | 0 | 0 |
| p1_d_scale_proxy_v5 | shared | 100 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 1.0000 | 0.0042 / 0.0080 | 1.0000 | 0 | 0 |
| p1_d_strafe_t192 | perslab | 60 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 0.6776 | 0.0118 / 0.0226 | 1.0000 | 0 | 0 |
| p1_d_strafe_t192 | shared | 60 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 1.0000 | 0.0057 / 0.0135 | 1.0000 | 0 | 0 |
| p3_a_golden_gate_v3 | perslab | 120 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 1.0000 | 0.0105 / 0.0105 | 1.0000 | 0 | 0 |
| p3_a_golden_gate_v3 | shared | 120 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 1.0000 | 0.0105 / 0.0105 | 1.0000 | 0 | 0 |
| p3_a_strafe_t192 | perslab | 60 | 0.9996 | 0.9996 / 1.0000 / 1.0000 | 0.7208 | 0.0183 / 0.0292 | 1.0000 | 0 | 0 |
| p3_a_strafe_t192 | shared | 60 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 1.0000 | 0.0126 / 0.0221 | 1.0000 | 0 | 0 |
| p4_a_still_v5 | perslab | 1 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 1.0000 | 0.0058 / 0.0058 | 1.0000 | 0 | 0 |
| p4_a_still_v5 | shared | 1 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 1.0000 | 0.0058 / 0.0058 | 1.0000 | 0 | 0 |
| p4_a_sunrise_v5 | perslab | 120 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 1.0000 | 0.0053 / 0.0058 | 1.0000 | 0 | 0 |
| p4_a_sunrise_v5 | shared | 120 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 1.0000 | 0.0053 / 0.0058 | 1.0000 | 0 | 0 |
| p4_d_dolly_v4 | perslab | 60 | 0.9997 | 0.9997 / 1.0000 / 1.0000 | 0.6730 | 0.0154 / 0.0194 | 1.0000 | 0 | 0 |
| p4_d_dolly_v4 | shared | 60 | 1.0000 | 1.0000 / 1.0000 / 1.0000 | 1.0000 | 0.0118 / 0.0153 | 1.0000 | 0 | 0 |

Columns: d-iv@v2 = clip min over frames of the whole-frame and every slab score (gate 0.995); d-iv@v1 = whole-frame value at the sidecar `output_block_offset_px` (registered reading, unchanged); A2 = `seam_adherence` (excluded seam pixels use a colour present in slab pixels; gate 1.000); seam share = 1 - evaluated area (cap 0.05, max over frames in the table; no frame exceeds it).
Before (T-196c, literal lattice labels): per-slab translating clips 0.958 / 0.964 / 0.985 / 0.976. Now: 1.0000 / 1.0000 / 0.9996 / 0.9997.

- **Literal d-iv@v2 now passes on all 8 clips in both arms**, 0 failing frames, blind offset == declared in every counted slab-frame, A2 1.000 everywhere, seam share max 2.9 % (p3_a_strafe perslab; mean 0.4-1.8 %).
- d-iv@v1 and the shared-phase result are unchanged (per-slab 0.67-0.73 on translating clips, as T-192; shared 1.000). v2 says the per-slab lattices are internally exact; v1 still says the whole frame is not one lattice.
- **A2 on still/sunrise (T-196c 0.957-0.967 at guard 4):** the palette is now every pixel in the interior of a slab label region (no other label within one block), not only counted blocks. At guard 0 A2 is 1.000. At the old guard 4 it reads 0.977-0.983 (still < 1): the pixels left over are guard-zone haze/dither colours that occur only at region edges; no longer relevant since no guard is needed, and not tearing.
- **C11:** not re-measured (per-configuration rig measurement, not per clip; renderer unchanged). T-196 numbers stand: per-slab 0.96 px pass, shared phase 1.49 px fail (`evidence/T-196/C11_SHARED_PHASE.md`).

## Not verified
Every 3rd frame; the gap-fill explanation was confirmed by the score going to 0.9999-1.0 on the worst frames, not by a per-pixel slab-id channel (the capture has none; one would remove the undecidable set). `slab_clip_eye_m` overlap is used as the ambiguity band. A2 at guard 4 not 1.000. No human 2AFC. No Unity run.
