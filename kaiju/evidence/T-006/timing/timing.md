# T-006 C15: frame cost of the composed style-frame scene

Command (no -quit; the job self-ticks and exits itself):

```
/Applications/Unity/Hub/Editor/6000.6.9f1/Unity.app/Contents/MacOS/Unity -batchmode -projectPath <kaiju> -logFile <log> -executeMethod Game.Tools.StyleFrame.StyleFrameTiming.TimingCli
```

Hardware: GPU Apple M5 Pro (Metal), CPU Apple M5 Pro, Mac OS X 26.6.2, Unity 6000.6.9f1; M-008 unverified (not the target PC). Relative numbers only.

Scene: tile + mech + props + pixel pipeline, hero camera, profile slate_teal, 120 warm-up frames then 600 measured frames per row. render = CPU submission of the render call; wall = render + one-pixel readback (GPU finished). Draw calls, batches, SetPass: ProfilerRecorder, p50 (p95). baseline_pixel_off = dither, haze, quantise off and no present blit.

| config | output (internal) | render p50 / p95 ms | wall p50 / p95 ms | draw calls | batches | SetPass | GC.Alloc samples in render | GC collections |
|---|---|---|---|---|---|---|---|---|
| pipeline_full | 1920x1080 (0x0) | 0.54 / 0.80 | 0.54 / 0.80 | 119 (121) | 0 (0) | 8 (8) | 0 | 0 |
| pipeline_full | 2560x1440 (480x270) | 0.52 / 0.69 | 0.52 / 0.69 | 119 (121) | 0 (0) | 8 (8) | 0 | 0 |
| baseline_pixel_off | 1920x1080 (512x288) | 0.27 / 0.41 | 0.27 / 0.41 | 0 (114) | 0 (0) | 0 (5) | 0 | 0 |
| baseline_pixel_off | 2560x1440 (480x270) | 0.26 / 0.44 | 0.26 / 0.44 | 0 (114) | 0 (0) | 0 (5) | 0 | 0 |

Zero per-frame GC after warm-up: GC.Alloc samples in the render call must be 0 in every row (GC collections over the measured window should also be 0). Full per-run statistics, including GC allocated in the whole editor frame, are in timing.json.

Orchestrator notes (run 2, after fixing the probe texture lifetime): the "(internal)" column is read one row late (0x0, 512x288 are stale values; the internal size is 480x270 at 1080p and 512x288 at 1440p) — known defect, not fixed. "render" is CPU submission + 1-px readback, not GPU per-pass time (Metal batch; see T-004 perf-1). Baseline draw-call p50 of 0 is a ProfilerRecorder sampling artefact; p95 114 is the real count. Relative only; M-008 unverified.
