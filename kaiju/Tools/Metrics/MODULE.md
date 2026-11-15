# Module: Tools/Metrics

- **Owner:** `impl-metrics` (task T-002). Milestone M0 · Foundations.
- **Purpose:** headless, deterministic measurement of `M-001@v1` (feels heavy), `M-002@v1` (no foot sliding),
  `M-003@v1` (matches the style references), `M-004@v1` (pixel stability), so evaluators can pass or fail other
  slices and founders can calibrate thresholds (orchestrator §4.2-§4.4). Pillars P1 and P4.
- **Engine dependency:** none (Python, kaiju conda env). Never imported by Unity code.

## Contracts it reads (never writes)
| Contract | Owner | Defined in | Validator |
|---|---|---|---|
| `gait_log@v1` | mech implementation (emitter) | `research/R-001.md` §8 | `metrics.py validate gait_log` |
| `style_capture@v1` | renderer capture tool (T-004) | `research/R-002.md` §6 (+ extensions in `SPEC_READINGS.md` S6, S17) | `metrics.py validate style_capture` |
| `palette_set@v1` | research (R-002) | `research/R-002.md` §3, `research/R-002/palettes.json` | `metrics.py validate palette_set` |
| `panel_ratings@v1` | founders / panel | this module (`kaiju_metrics/ratings.py`, README) | loaded by `m001`, `m003`, `calibrate` |

## What it writes
Only to the `--out` directory it is given: `report.json` (`metric_report@v1`), `summary.txt`, `diag_*.png`, and for
`calibrate` a proposal (`calibration.json`, `registry_proposal.diff`, a plot). It never writes `registry/`, research
files, captures or logs.

## Layout
| Path | Role |
|---|---|
| `metrics.py` | CLI entry point |
| `kaiju_metrics/contracts/` | validators and loaders for the three contracts |
| `kaiju_metrics/gait/` | M-001 (`m001.py`), M-002 (`m002.py`), shared segmentation (`segment.py`) |
| `kaiju_metrics/style/` | M-003 (`m003.py`), M-004 (`m004.py`), frame measures (`measures.py`) |
| `kaiju_metrics/registry.py`, `report.py`, `runner.py` | versioned thresholds, verdict rules, report output |
| `kaiju_metrics/ratings.py`, `calibrate.py`, `rescore.py`, `baseline.py` | panel items, calibration helper, re-scoring, reference baseline |
| `kaiju_metrics/colour.py`, `png.py`, `diagnostics.py` | CIEDE2000 / sRGB-Lab, lossless PNG I/O, diagnostic images |
| `registry/` | `M-001@v1.yaml` .. `M-004@v1.yaml` (revision v1.0) |
| `fixtures/` | deterministic generators (`gen_gait.py`, `gen_style.py`), evidence builder (`make_evidence.py`) |
| `examples/` | small valid example of each contract |
| `tests/` | pytest suite (known answers, boundaries, negatives, verdict rules, determinism, CLI) |

## Interfaces for other modules
- Emitters check their output with `validate` before anything else; exit 0 = valid, 2 = invalid with every error listed.
- Evaluators run one metric command and read `verdict`, `headless_verdict` and `failing_gating` from `report.json`.
- Feature flags: none (a tool, not a runtime feature). Tuning values: `registry/` only.
