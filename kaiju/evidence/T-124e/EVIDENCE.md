# T-124e evidence (stage-0 hitch attribution, M008BayRun seed 5)
Code: branch task/T-124e (commit in git log; adds `schedphases=`, `gcd=`, `cpu=`/`wall=` to the hitch log and `sched_phase_ms_max` to the Bay JSON; no behaviour change).
Command: `evidence/T-124e/run.sh <label> 5` (batch, `-nographics` so no audio; defaults, no flags; tiles Pipelines/geo/cache/bake/tiles/v1; bay_cost@v2; prefetch 768 + slice; 60 s warm-up + 600 s measured).
Machine was NOT quiet: other workers' multi-process Python bakes ran throughout (15-core M5 Pro).
| label | wall clock | load average (1/5/15 min) at start | artifacts |
|---|---|---|---|
| seed5_phases | 18:28-18:34 | 8.7/8.3/7.3 at launch, 34/19/12 mid-run | `seed5_phases/m008_bay_seed5.json`, `machine_before.txt`, `load_samples.txt` |
| seed5_cpu | 18:36-18:48 | 39/23/14 at launch, 24-31 during | `seed5_cpu/m008_bay_seed5.json`, `machine_before.txt`, `load_samples.txt` |
Summaries: `python summarize.py <dir>`; hitch lines are in each JSON under `streaming.item_profile.hitches`.
