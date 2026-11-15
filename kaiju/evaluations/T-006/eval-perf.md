VERDICT: PASS

Scope: C15 (frame cost) from `evidence/T-006/timing/timing.md`, EVIDENCE C15 row and tasks/T-006.impl.md "Step 5 timing". Not re-run (no Unity).

C15 checklist:
- Hardware named: GPU and CPU Apple M5 Pro (Metal), macOS 26.6.2, Unity 6000.6.9f1. Present.
- "M-008 unverified (not the target PC)" and "Relative numbers only": stated in timing.md and EVIDENCE. Present.
- p50 and p95 at both resolutions: pipeline_full 1080p render 0.54 / 0.80 ms, 1440p 0.52 / 0.69 ms. Present. Baseline (pixel off) rows also given, 0.27 / 0.41 and 0.26 / 0.44 ms.
- Draw calls at both resolutions: 119 (p95 121) in both pipeline_full rows. Present. Method is 120 warm-up and 600 measured frames per row.
- Zero GC after warm-up: GC.Alloc samples in the render call 0, GC collections 0 in every row. Present.
- The numbers are CPU submission of the render call plus a one-pixel readback, not GPU pass time. timing.md says so, and notes it matches T-004 perf-1 (Metal batch). Nearly identical wall and render figures (wall = render in every row) suggest the readback does not wait on the GPU, so no GPU frame time is evidenced at all. The 1440p row (0.52) being no costlier than 1080p (0.54) is consistent with that. It is a stated limit of the method, not a failure of the criterion, which asks for relative numbers only.

| ID | Severity | Criterion | Text |
|---|---|---|---|
| DEF-T006-perf-1 | minor | C15 | The "(internal)" column in timing.md is stale, read one row late: shows 0x0 and 512x288 for 1080p, 480x270 for 1440p. True values are 480x270 at 1080p and 512x288 at 1440p. The orchestrator note admits this as a known defect and leaves it. The reader of the table sees the wrong internal size. |
| DEF-T006-perf-2 | minor | C15 | All timings are CPU submission, not GPU time. "wall" equals "render" in all four rows, so the one-pixel readback adds no measurable GPU wait on Metal. The table cannot be read as frame cost on the GPU, and the 1080p versus 1440p comparison says nothing about fill cost. Disclosed in timing.md. |
| DEF-T006-perf-3 | minor | C15 | Baseline rows report draw calls p50 of 0 (p95 114): a ProfilerRecorder sampling artefact, disclosed in the orchestrator note but left in the table. Batches read 0 and SetPass reads 0 (p95 5) in the baseline rows for the same reason. |
| DEF-T006-perf-4 | minor | C15 | Measured in the Editor in batchmode, not a player build, so Editor overhead and the Editor-only camera path may differ from the game. Not stated in timing.md. EVIDENCE marks C15 PARTIAL, which is fair. |
| DEF-T006-perf-5 | minor | C15 | The "GC.Alloc samples in render" figure covers only the render call. Whole-editor-frame GC is only in timing.json, and timing.md does not state its values. Zero-GC is shown for the render path only. |
