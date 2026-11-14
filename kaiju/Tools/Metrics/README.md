# Tools/Metrics: headless metric tools for M-001 .. M-004 (T-002)

Pure Python in the `kaiju` conda env (numpy, scipy, opencv, scikit-image, pillow, pyyaml, matplotlib). No Unity, no
display, no GPU, no network. One CLI, `metrics.py`, with a subcommand per metric plus `validate`, `calibrate`,
`rescore`, `list-metrics` and `baseline`. Every threshold lives in `registry/` (never in code). The metric specs are
`research/R-001.md` §9 (`M-001@v1`, `M-002@v1`) and `research/R-002.md` §4-§5 (`M-003@v1`, `M-004@v1`); where they are
silent the tool's readings are listed in `SPEC_READINGS.md`.

## Quick start (this block is executed by `tests/test_docs.py`)
Run from the project root (`kaiju/`). `$PY` is the kaiju interpreter, `$TMP` any scratch directory.

```sh
$PY Tools/Metrics/metrics.py list-metrics                                                   # exit 0
$PY Tools/Metrics/metrics.py validate gait_log Tools/Metrics/examples/gait_log_example.gait.jsonl  # exit 0
$PY Tools/Metrics/metrics.py validate style_capture Tools/Metrics/examples/example_set      # exit 0
$PY Tools/Metrics/metrics.py validate palette_set research/R-002/palettes.json              # exit 0
$PY Tools/Metrics/fixtures/gen_gait.py $TMP/gait                                            # exit 0
$PY Tools/Metrics/metrics.py m002 $TMP/gait --out $TMP/m002                                 # exit 0
$PY Tools/Metrics/metrics.py m001 $TMP/gait --out $TMP/m001                                 # exit 3
$PY Tools/Metrics/metrics.py rescore $TMP/m002/report.json                                  # exit 0
$PY Tools/Metrics/fixtures/gen_style.py $TMP/style --m003                                   # exit 0
$PY Tools/Metrics/metrics.py m003 $TMP/style/m003_pass --profiles slate_teal --out $TMP/m003  # exit 3
$PY Tools/Metrics/fixtures/gen_style.py $TMP/style --m004                                   # exit 0
$PY Tools/Metrics/metrics.py m004 $TMP/style/m004_pass --profiles slate_teal --viewpoints eye --fixture --out $TMP/m004  # exit 0
$PY Tools/Metrics/metrics.py baseline --out $TMP/baseline --panels ref_03D                  # exit 0
```
`m001` exits 3 (INCOMPLETE) on the synthetic set because the panel score M-001.13 is PENDING without a ratings file;
its `headless_verdict` is PASS. Same for `m003` (M-003 f).

## Commands
| Command | Input | Output |
|---|---|---|
| `m001 <logs or dirs>` | `gait_log@v1` `.jsonl` (captures `walk_fr010`, `walk_fr020`, `walk_fr030`, `start`, `stop`, `estop`, `turn90_inplace`, `curve`); `--ratings` for M-001.13 | report |
| `m002 <logs or dirs>` | the M-001 captures plus `stomp`; optional `walk_fr020_terrain` | report |
| `m003 <capture sets>` | `style_capture@v1` set: >= 12 `M-003` frames per profile and a `haze_cal` sequence; `--profiles a,b` narrows the required profiles; `--palettes` other `palette_set@v1`; `--ratings` for M-003 f | report |
| `m004 <capture sets>` | `M-004` sequences S0, T1, T2, T3, Y1, Y2 per profile and viewpoint (default 3 x 2); `--profiles`, `--viewpoints`, `--fixture` (60-frame sequences, labelled), `--jobs N` (same result for any N) | report |
| `validate gait_log|style_capture|palette_set <paths>` | contract files | every error as `file:line or key: what (expected; found)`; `--json` for machines |
| `calibrate --ratings R --reports DIR...` | `panel_ratings@v1` + the reports of the rated captures (report `label` = rating `capture`) | rater agreement, anchors, value-vs-rating table and plot, proposed thresholds with false-pass / false-fail counts, registry diff with a revision bump. Never writes `registry/`; refuses with < 8 captures or misbehaving anchors |
| `rescore <report.json>` | an old report | re-runs it on its inputs (hash-checked) with the registry revision it names (found by SHA-256); `--registry X --allow-different-registry` to re-score under another revision explicitly |
| `list-metrics` | - | metrics, registry revisions, sub-measures (`--json`) |
| `baseline --out DIR` | `art/style_refs/` + `research/R-002/` | the still-applicable M-003 measures on the 11 reference panels and a comparison with R-002's tables |

Every metric command also takes `--registry FILE`, `--out DIR` (default `metrics_out/<cmd>_<label>`), `--label`,
`--no-images`, `--quiet`. `calibrate`, `validate` mutants and the exit-code table are exercised by
`tests/test_tools.py` and `tests/test_validators.py`.

## Exit codes and verdicts
| Exit | Verdict | Meaning |
|---|---|---|
| 0 | PASS | every gating sub-measure passed; reads `PASS (provisional: n assumed)` while n decisive thresholds are still `assumed_pending_calibration` |
| 1 | FAIL | at least one gating sub-measure failed (wins over missing evidence) |
| 2 | INVALID_INPUT | contract violation (truncated file, NaN, gap in `n`, float32 positions, wrong units / schema / bit depth, hash or speed mismatch, frozen or blank capture) or bad arguments. An emitter error is never a metric failure |
| 3 | INCOMPLETE | valid input but required evidence absent (missing capture or profile, < 3 steady strides, no evaluated dither frame, < 3 depth bands, ratings pending, too few valid pixels) |
| 4 | TOOL_ERROR | a bug in the tool, never a verdict |

Sub-measure statuses: PASS, FAIL, NOT_RUN, PENDING, NOT_APPLICABLE, REPORTED (informational). Kinds: `gating`,
`advisory` (never flips a verdict; shown as a warning), `info`. `headless_verdict` applies the same rules without the
panel items (M-001.13, M-003.f), so a task loop can gate on the numbers while ratings are pending.

## Report (`report.json`, `metric_report@v1`) and summary
`metric`, `metric_version`, `label`, `registry` (path, revision, SHA-256, status), `tool` (version, git commit),
`invocation` (command, inputs, options: what `rescore` replays), `inputs` (path + SHA-256 of every file read),
`normalisers` (header values used), `flags` (`NON_REFERENCE_NORMALISERS`, ...), `warnings`, `contract_errors`,
`coverage` (required / provided / missing / optional_not_provided), `submeasures` (id, title, kind, status, value,
unit, op, threshold, margin, threshold_status, parts, diagnostics: worst contacts, frames, pixel counts, images),
`verdict`, `verdict_label`, `headless_verdict`, `exit_code`, `images`, `run` (wall time, timestamp, host: the only
block that changes between identical runs). `summary.txt` (<= 40 lines) lists failing gating IDs first. Diagnostic
images: `diag_slip.png`, `diag_gait.png`, `diag_M-003a_dE00_heatmap.png` (plus the worst off-palette colours in the
JSON), `diag_M-003b_dither_mask.png`, `diag_M-003d4_nonuniform_blocks.png`, `diag_M-004_shimmer_map.png`.

## Panel ratings (`panel_ratings@v1`)
```json
{"schema": "panel_ratings@v1", "metric": "M-001",
 "raters": [{"rater_id": "founder_a", "rater_type": "human", "blind": true}],
 "ratings": [{"rater_id": "founder_a", "build": "<report label>", "capture": "panel_strip_01",
              "scenario": "overall", "anchor": null, "score": 8}]}
```
M-001.13: `scenario` in cruise, start, stop, turn90_inplace, curve, overall; >= 3 blind raters. M-003 f: `profile` per
row, 2 `positive` + 2 `negative` anchor rows per rater, >= 8 included raters, >= 12 captures. The tool never defaults
a score.

## Registry and versions
`registry/M-00x@v1.yaml` is revision v1.0 of spec `M-00x@v1`. Each threshold entry has `value`, `unit`, `status`
(`derived` | `assumed_pending_calibration` | `calibrated`) and `source`; `procedure` holds the other spec numbers
(smoothing widths, depth bands, window sizes). `metric_status: draft` means the metric cannot fail anything.

**Add a registry revision (calibration):** run `calibrate`, review its diff, save the edited copy as a new file
`registry/M-00x@v1.1.yaml` with `revision: v1.1` (never edit a published revision in place). New runs use the newest
revision; old reports keep naming theirs, and `rescore` finds them by hash.
**Add a metric version (new spec):** add `registry/M-00x@v2.yaml` with `spec: M-00x@v2`, `metric_status: draft` until
the research / tool / calibration steps (orchestrator §4) are done; implement any new sub-measure in the metric module
and its ID in the registry (`tests/test_registry.py` fails on any ID mismatch); flip to `registered`.
Worked example: `tests/test_registry.py::test_second_version_coexists_and_draft_cannot_fail`.

## Fixtures and tests
Fixtures are generated by `fixtures/gen_gait.py` and `fixtures/gen_style.py` (seeded, bit-identical, independent of
the package: closed-form truth or the generator's own scene geometry). Small contract examples are in `examples/`.
Run `$PY -m pytest Tools/Metrics/tests -q` (a few minutes). Evidence for T-002 is rebuilt by
`$PY Tools/Metrics/fixtures/make_evidence.py`.
