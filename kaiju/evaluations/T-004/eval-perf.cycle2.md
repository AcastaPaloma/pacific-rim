PASS

# eval-perf, T-004, cycle 2 re-check of C20. Artifacts and StyleTiming.cs read; Unity not run.
Machine: Apple M5 Pro, Metal, Unity 6000.6.9f1, batch Editor. `supportsGpuRecorder` is false, so GPU ms is 0 everywhere. M-008 is NOT verified (no target-class Windows PC, Q-019).

| Defect | Status | Evidence |
|---|---|---|
| DEF-T004-perf-1 (per-pass GPU p95) | routed to founders | GPU ms is 0 in `pass_timing_*.csv` (checked) and `supports_gpu_recorder:false` is stated in the JSON, the EVIDENCE table header and perf-1/-2/-3 row. Not hidden or faked. Needs the target PC with GPU markers, or a written re-scope of C20 to CPU/relative. Not an implementer defect |
| DEF-T004-perf-2 (Present isolated) | closed (CPU only) | `Pixel.Present` is its own sampler series: CPU mean 0.0091 ms (1080p) / 0.0089 (1440p), p95 0.0107 / 0.0100. Separate from the stage chain. The old "whole second camera" number is no longer used as the pass figure. GPU cost of Present stays under perf-1 |
| DEF-T004-perf-3 (chain p95, CI) | closed (CPU); GPU routed | 1000 frames per size, 95% CI per pass in the JSON and table (e.g. Haze 0.0143-0.0145 ms). Chain CPU p95 about 0.06 ms, far under 0.5. CI caveat (normal approximation, ignores autocorrelation) is disclosed. A GPU p95 figure remains under perf-1 |
| DEF-T004-perf-4 (GC witness) | closed, narrow | 600 frames per size, capture off, 0 GC.Alloc samples, 0 collections, after 30 warm-up frames (`CountAllocations`). Still Editor main thread (`CollectOnlyOnCurrentThread`), render calls only, not a player loop. Residual, not a defect. Mono-used delta 16 KB / 112 KB has no GC.Alloc samples; Rig.Tick runs outside the recorder, so the growth is unattributed. Disclosed in EVIDENCE |
| DEF-T004-perf-5 (target PC) | routed to founders | Unchanged. M-008 stays unverified and is a blocker on M-008, not on T-004 |
| DEF-T004-perf-6 (stale C20 text) | open, minor, non-blocking | EVIDENCE.md C20 row (line 99) still quotes the cycle-1 wall-time figures and "GC over 100 frames"; limitation 5 (line 115) says the GPU recorder "needs a player loop", but the run shows `supportsGpuRecorder` is false on Metal. PASS: update both to match `pass_timing_summary.json` |

## Method audit
- Warm-up: 3 settle frames (`Settle`) plus 120 warm-up discarded (123 stated), then exactly 1000 frames kept per size (CSV has 1000 rows each). Sound. No vsync (vsync 0, target frame rate -1), the camera pans each frame so no frame is a cached repeat.
- Recorder read: `enableRecording` set at `Settle`, each frame is read one tick later, when the previous frame is complete. No same-frame read. Sound.
- Isolation: capture is off and nothing is read back, so readback stalls no longer leak into the numbers (the cycle-1 flaw). `Pixel.Present` has its own series. Downsample and Outline read 0 because they are not in the M0 chain, and that is stated.
- Numbers are credible. CPU per pass is about 0.014-0.017 ms, tight (stdev 0.001 ms, apart from a single 1.08 ms Dither outlier at 1080p that widens its CI). They are command-recording cost of one fullscreen triangle each, so a few tens of microseconds is expected. Per-pass CPU is also steady across sizes (as expected, since CPU recording does not depend on pixels). The CSV samples I read are consistent with the summary.
- What these numbers are not: GPU cost, Windows cost, or 60 fps evidence. They show only that the CPU side of the chain is negligible on this machine.
- Honesty: the GPU limitation is stated in four places, GPU columns are labelled "not measurable", and nothing implies a GPU pass. Good.

## Result
No implementer-fixable defect blocks C20 as far as this machine can measure. C20 GPU p95 and M-008 are routed to the founders (target PC, or re-scope C20 in writing). perf-6 is a doc fix that does not change the verdict.
