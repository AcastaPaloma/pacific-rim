# T-187: C11 near-edge depth supplied by the rig, and C7 at 1 km with layered rendering on/off (T-185)

Run: one Unity batch capture, `unity run ... -captureJobs c11slab,c7layers -captureC11Configs rigedge` (log `evidence/T-187/capture.log`, 0 errors / 0 warnings, 85 s); captures in `evidence/T-187/captures_local`; analysis `Tools/Rendering/Pixel/c11_slab.py` and `pixel_checks.py blocks|scale` (outputs `c11_slab_results.json`, `C11_SLAB.md`, `checks/`). Gates unchanged: C11 1 px, C7 dL >= 4 L* (one value band), marker dL >= 10 and 1-2 px.

## C11 with the layer depths the rig supplies (config `rigedge_layersnap`)
Near layer = focus depth 124.2 m minus the near silhouette offset (`ThirdPersonRig.SetScriptedNearEdgeOffset`, `CameraLayerStabiliser.NearEdgeDepth`) = 123.49 m; main = rig skyline depth 664.874 m; far = rig far depth 10353 m. Layered frames 180/180.

| class | starts | worst px | median px | starts within 1 px | gate |
|---|---|---|---|---|---|
| near | 24 | 0.96 | 0.96 | 24 / 24 | pass |
| reference | 72 | 0.96 | 0.96 | 72 / 72 | pass |
| far | 48 | 0.96 | 0.96 | 48 / 48 | pass |

Before (T-184, near layer at the focus depth 124.2 m, same sequences): near 4.58 / 1.32 px, 0 / 24 starts within 1 px. The flip is gone because the near slab lattice is now the lattice of the edge the viewer tracks.

## C7 / T-185: layered rendering off vs on (twelve eye70 stations, 1920x1080, all five profiles, tuning of T-177)
Layered path taken in 12/12 frames and in the scale view with layerSnap on, 0/12 with it off (`captures_local/c7layers.json`).

| profile | block 150 m dL min (off / on) | block 1 km dL min, 9 frames (off / on) | marker scale check (off / on) |
|---|---|---|---|
| warm_rust_sepia | 15.8 / 15.8 | 6.4 / 6.4 | pass / pass |
| warm_amber_haze | 17.1 / 17.1 | 8.9 / 8.9 | pass / pass |
| slate_teal | 11.9 / 11.9 | 6.4 / 6.4 | pass / pass |
| lavender_peach | 9.4 / 9.4 | 8.3 / 8.3 | pass / pass |
| slate_crimson | 17.2 / 17.2 | 4.5 / 4.5 | pass / pass |

C7 passes (min dL_ring3 >= 4 L*, 4/4 near and 9/9 far frames) in all five profiles, identical with layering on and off: no haze or slab composite fix was needed (the 80 m block at about 1 km lies inside the main slab, MODULE.md). Note: the older T-004 numbers (three profiles failing at 1 km) predate the T-177 tuning.

## Not covered
Stills are cut frames (every layer restarts at the wanted pose), so the C7 comparison says the slab composite does not change contrast; it does not exercise seam crossing under motion (C1/C2/A2 on moving layered sequences are T-184 seam-report items). Only the near-edge offset is supplied; a real mech bounds provider (Units.Mech, not touched) still has to set `nearEdgeOffsetInTargetHeights` or `SetNearEdgeOffset`; default 0 keeps the focus depth.
