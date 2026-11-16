# T-196: M-003.d-iv pixel grid on layered translating clips (shared slab lattice phase)

Escalated under §14 (Opus 5.5 high). Gates and the M-003 reading unchanged. Flag `rendering.pixel.layerSharedPhase` (new, ships false); measurement config `Assets/Game/Config/Rendering/Configurations/M3LayeredSharedPhase.json` = M3LayeredFarLift + the flag. One Unity cycle (two batch runs: StyleCapture c11slab/c7layers/hazecal, then CaptureCli on 8 clips), 0 errors, 1 known warning (hazeFarLift slab boundaries), replay identical.

## What the T-192 failure is
- Slab shifts are already whole output pixels (`PixelLayerHints.ShiftOutputPx` = round(remainder * k)); the sidecar `output_block_offset_px` is the main slab's shift mod k. On lateral motion the near / main / far remainders evolve at rates proportional to 1/reference depth, so their shifts differ mod k on most frames and the frame holds three block lattices (d-iv 0.61-0.73).
- Fix built: `PixelSharedPhase` (Maths) puts every slab's shift on one phase p (mod k), slabs differing only by whole texels. Per axis p minimises the largest deviation of any slab from its own ideal shift, measured against that slab's deviation at the last cut (relative motion is what C11 measures). Guard band 2 texels with the flag; sidecar `layers.shared_phase: true`; `PresentShift` = applied main shift, so the declared offset is the applied phase.

## Result (measured, `m003_moving.md`, `m003_moving/*/summary.txt`, `C11_SHARED_PHASE.md`)
| measure | gate | per-slab path (T-192, M3LayeredFarLift) | shared phase (T-196, M3LayeredSharedPhase) |
|---|---|---|---|
| M-003.d-iv, p4_d dolly / p1_d strafe / p1_d approach / p3_a strafe | >= 0.995 | 0.673 / 0.678 / 0.728 / 0.606 | **1.000 / 1.000 / 1.000 / 1.000** |
| M-003.d-iv, p1_d scale, p4_a sunrise, p4_a still, p3_a Golden Gate | >= 0.995 | 1.000 (yaw / still) | 1.000 |
| A2 output / internal (all 8 sets, every frame) | 0.95 / 0.995 | 1 / 1 | 1 / 1 |
| c1 worst drop / rho / span (store sets p1_d, p3_a GG, p4_a still, sunrise, p4_d) | 2 / 0.9 / 15 | pass | pass: 0/1/24.0, 0/1/15.9, 0/1/31.3, 0/1/30.5, 0/1/20.7 |
| c1 on the seam clips (approach / strafe / p3_a strafe) | | 4.21/0.7/16.3, 3.91/0.8/19.9, 8.41/0.5/1.2 (fail, whole-frame metric defect) | 4.20/0.7/17.1, 3.95/0.8/19.9, 8.32/-0.5/-2.8 (same failure, not a slab effect) |
| c2 (haze_cal, 5 profiles) | drop <= 0 | pass | pass (worst lavender_peach 0 L*) |
| C7 near 150 m / 1 km dL min (rust, amber, teal, lavender, crimson) | >= 4 | 15.8/6.45, 17.5/13.31, 11.9/6.35, 9.4/11.76, 17.3/12.72 | 15.41/6.45, 17.54/13.25, 11.91/6.28, 9.36/11.76, 17.28/12.86, scale marker pass x5 |
| **C11 near / reference / far worst (rigedge_layersnap)** | **<= 1 px** | 0.96 / 0.96 / 0.96, 24/72/48 starts within 1 px | **1.49 / 1.49 / 1.48 FAIL, 0/24, 0/72, 0/48 starts within 1 px** |
Sidecar check (p1_d strafe, 180 frames): 180 layered, 180 shared_phase, 0 frames whose slab shifts differ mod 4.

## Why no lattice scheme holds both (the shortfall is structural)
- d-iv at one declared offset means every texel edge of the frame lies on one k-pixel output lattice. Two slabs at different reference depths drift apart on any lateral move, so the difference of their ideal shifts sweeps every residue mod k. When it is k/2 with a half-pixel fraction, no integer phase keeps both edges within 1.5 px. Minimax 1.5 px is therefore the best possible for any common lattice, against C11's 1 px. Measured 1.49 / 1.49 / 1.48 equals the bound.
- Model over T1 (`phase_model.py`, `phase_model_windows.py`, `phase_model_out.txt`, 24 starts x 16 phases): per-slab 0.99 px; common = main shift (T-192 option B) 3.7-3.9 px for near/far (T-192 estimated 2); common = near 3.7-4.0; minimax 1.47-1.49 (1.7-2.3 when the edge enters after the cut).
- So d-iv >= 0.995 and C11 <= 1 px on the same layered translating frame are incompatible as both are now read. The decision is still the panel's: (A) per-slab-region d-iv reading (T-192 recommendation), (B') this flag, d-iv 1.000 with C11 at 1.5 px (a C11 change), or keep per-slab shifts and leave d-iv failing on translation. Nothing relaxed here.

## Not done
Flag not enabled in feel-check or capture configs (it fails C11). Seam report (tears/blink) not re-run for the shared arm: no single-render baseline in this cycle (T-192 raw frames are gone). Raw frames in the scratchpad only (218 MB).
