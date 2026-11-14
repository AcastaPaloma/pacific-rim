# Contract examples (valid; checked by `tests/test_validators.py`)

| File | Contract | Produced by |
|---|---|---|
| `gait_log_example.gait.jsonl` | `gait_log@v1` (R-001 §8): 8 s of `walk_fr020` at a 33° heading, with `frame` records | `python fixtures/gen_gait.py examples/gait_log_example.gait.jsonl --example` |
| `example_set/` | `style_capture@v1` (R-002 §6): a 2-frame M-003 sequence, a `haze_cal` frame with `haze_targets`, a 3-frame M-004 yaw sequence | `python fixtures/gen_style.py examples` |
| `palette_set_example.json` | `palette_set@v1` (R-002 §3): trimmed to one profile (`slate_teal`) | trimmed copy of `research/R-002/palettes.json` |

Check an emitter's output: `/opt/miniconda3/envs/kaiju/bin/python Tools/Metrics/metrics.py validate gait_log <file.jsonl>`
(also `style_capture <capture-set dir>` and `palette_set <file.json>`). Exit 0 = valid, 2 = invalid; every error is listed
as `file:line or key path: what (expected ...; found ...)`. `palette_sha256` canonical form: SHA-256 of the compact JSON
list of the profile's lowercase hex strings in declared order, e.g. `["#243a43","#2c434d",...]`.
