# T-124c evidence (M-008 performance on the Bay)
All Unity runs: `evidence/T-124c/run.sh <label> <seeds> <args>` (batch, `-nographics` unless `NG=""`; no audio device use, `AudioListener` muted in the rendered run), tiles `Pipelines/geo/cache/bake/tiles/v1`, raster bay_cost@v2 (`evidence/T-115g/bay_cost_256m_v2.bin`), prefetch 768 + SliceRenderBuilds (shipping defaults), 60 s warm-up + 600 s measured, seeds 1-5. Machine busy (load average 4.7-6.5 from other jobs), so absolute ms are noisy.
Code commit for all runs: 01fc776 (+ the follow-up commit on task/T-124c; base_v2 ran the T-124b code with the raster switch only).
| label | command (args after the seeds) | artifacts |
|---|---|---|
| base_v2 | `run.sh base_v2 1,2,3,4,5` | `base_v2/m008_bay_seed1..5.json`, `report_base_v2/` |
| opt_v2 | `run.sh opt_v2 1,2,3,4,5 -m008BrainSlice 1 -m008SpawnTemplate 1 -m008LazyGo 1` | `opt_v2/`, `report_opt_v2/` |
| render_v2 | `NG="" run.sh render_v2 1 -m008Render 1 -m008Measure 120` | `render_v2/m008_bay_seed1.json` (1280x720 plain URP, Apple M5 Pro, 120 s measured: NOT scope-complete) |
Summaries: `python summarize.py <dir>`; metrics: `python -m kaiju_metrics m008 ... --out report_*`. Dotnet: `dotnet_tests.log` (PackHarness 12/0 incl. new PhaseHook test, WorldStreaming 39/0, Host 35 pass/2 skip, AI.Pack 28/0).
