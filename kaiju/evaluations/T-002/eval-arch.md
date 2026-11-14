FAIL
# T-002 eval-arch (branch task/T-002 @ 0db318e). Blocker: M-002 passes on logs with almost no contacts (DEF-T002-arch-1). Major: truncated colour PNG gives a stack trace and exit 4 (DEF-T002-arch-2).

All runs: `/opt/miniconda3/envs/kaiju/bin/python Tools/Metrics/metrics.py ...` from `WT`, outputs in `/private/tmp/claude-501/eval-T-002-arch/` (`wp/` = wrong-pass runs, `val/` = validator mutants, `reg/` = temp registries, `det/` = determinism and timing).
Own pytest run: `pytest Tools/Metrics/tests -q` -> **331 passed in 233.26 s (0:03:53)**, 0 failed, 0 skipped. Matches the reported 331 (`/private/tmp/claude-501/eval-T-002-arch/pytest.txt`).

## Criteria
| C | Verdict | Evidence |
|---|---|---|
| C1 Coverage | PASS | `list-metrics` gives M-001.1-.13, M-002.1-.5, 17 M-003 IDs, 12 M-004 IDs. They match R-001 §9 rows (`research/R-001.md:300-312`) and R-002 §4-§5 (a, b, c1, c2, d-i..iv, e, f; M-004 S0 and S_px/C_px/F per class). Gating and advisory flags follow R-002:109 (e advisory), :137 (O advisory, DIG<=0.5 advisory). M-003.b.O is kind `info`, so it never shows as a warning (minor, DEF-6). |
| C2 Verdict rules | PASS | Own flips: M-004.S_px.Y 0.004446 vs 0.01 -> 0.004 gives FAIL exit 1, only that ID failing. M-002.1 threshold 0.005 -> 0.0 gives FAIL exit 1, only that ID. Advisory M-002.2 kept PASS with a warning. The per-ID fail-alone tests are in the 331. |
| C7 Never passes by default | **FAIL** | 1 blocker found, see the table below and DEF-1. 22 of 24 attempts correct. |
| C9 Gaming guards (code) | PASS | Frozen M-004 -> exit 2 ("every frame identical to frame 0"). Blank S0 -> exit 2. Declared speed 1.0 vs poses 0.75 -> exit 2 ("differs ... by more than 10 %"). Declared speed edited in sidecars only -> exit 2 ("motion differs from sequence.json"). Colour-only black frames pass (minor, DEF-3). `NON_REFERENCE_NORMALISERS` not re-run by me; covered by `test_M001_non_reference_normalisers_flagged` in the 331. |
| C10 Registry only | PASS (minor) | Source scan below. Flips with a temp registry need no code change (M-004.S_px.Y and M-002.1 above). A draft copy of `M-002@v1.yaml` gives verdict INCOMPLETE exit 3, never FAIL (the sub-measure row still shows FAIL, so the summary reads "failing gating: 1" beside INCOMPLETE). `test_provisional_label_counts_assumed` passes. Gaps: DEF-5. |
| C11 Reports, rescore | PASS | Report keys: `metric`, `metric_version`, `registry{revision,sha256,spec,metric_status,path}`, `tool{version,git_commit}`, `inputs[{path,role,sha256}]` (2167 hashed for M-004), `normalisers`, `submeasures` (margins), `verdict`, `headless_verdict`, `exit_code`, `coverage{required,provided,missing}`, `run.wall_time_s`. `summary.txt` is at most 40 lines. Rescore on the same registry -> "REPRODUCED", exit 0. Rescore with a modified registry and no flag -> exit 2 "registry differs ... pass --allow-different-registry". With the flag -> exit 1, "old verdict PASS new verdict FAIL", `rescored_from` recorded. A report whose registry is not in `registry/` is refused (exit 2), not reinterpreted. |
| C13 Deterministic | PASS | Two runs of m001 and two of m003: reports identical with `run` stripped, summaries have the same sha256. m001 under `OMP_NUM_THREADS` 4 vs 8, `LC_ALL=de_DE.UTF-8` and DISPLAY unset is identical to the default run. |
| C14 Headless, fast | PASS | Wall times from `report.json run.wall_time_s`: M-001 0.84 s, M-002 0.71 s, M-003 (12 frames 480x270 + haze_cal, 1920x1080 output) 2.4 s, M-004 (6 x 180 frames 480x270, serial) 28.8 s. All inside the 60 s limits. Full M-004 set (10 min limit) not re-run; it is the implementer's 34.3 s with `--jobs 8`. No display needed. |
| C15 Validators | PASS (minor gaps) | Good examples exit 0 for gait_log, style_capture and palette_set. 13 gait mutants of mine (mass<0, g=9.8, dt in ms, non-unit root_q, contact=2, 2-vector com_p, t backwards, non-unit cn, 1 foot of 2, missing `yaw_rate`, blank line, 1 sole point, 3 faults at once): all exit 2 with `file:line key: msg (expected; found)`. The 3-fault log lists all 3. 10 of 12 style mutants exit 2 with a specific error. The 2 accepted: `pixel_origin`="bottom-left" (DEF-4) and `sim_paused`=false (not required by R-002). A truncated `color_output.png` is also accepted: validate says VALID, then m003 crashes (DEF-2). |
| C18 Usable by agents | **FAIL** | All 13 README quick-start commands give the stated exit codes (0,0,0,0,0,0,3,0,0,3,0,0,0). `--help` lists commands and exit codes; `bogus` and a missing argument give exit 2. Fails on "bad input gives a message, not a stack trace": a truncated PNG gives a full traceback (DEF-2). |
| Hygiene | PASS (minor) | The diff contains only `Tools/Metrics/` (95 files), `evidence/T-002/` (175) and `tasks/` (1). No file in the diff exceeds 1 MB; `Tools/Metrics` is 1.1 MB and `evidence/T-002` is 3.8 MB. Imports use only numpy, scipy, cv2, matplotlib, yaml, skimage and pillow, all in `environment.yml`. No network imports. `import kaiju_metrics` creates no files. `MODULE.md` is present, with layout and contracts matching the tree. A clean checkout stays clean after pytest. DEF-7. |

## Source scan for threshold literals (C10)
`grep -rnE "0.95|0.995|0.15|0.005|0.098|0.30|26.7|0.12|0.01|0.99|0.5|0.0025"` over `kaiju_metrics/` and `metrics.py`.
- No hit is a gating threshold in code. Every hit falls into one of these groups:
  - Formula constants: `colour.py` 0.5 and 25^7; `m004.py:40-41` and `measures.py:67,146` (pixel centre, ordered index and horizon formulas, 0.5).
  - Validator tolerances: `gait_log.py:358-363` 0.05 and 0.01; `style_capture.py:190` 1 % vfov; `palette_set.py:100` 0.01.
  - Baseline comparison tolerances: `baseline.py`.
  - Notes and docstring text: `m001.py:216,262`, `inputs.py:49,81`.
- The force hysteresis (0.03, 0.01 m g), `h_tol` 0.01 L_f and `ext_f` 0.05 m g sit in `registry/M-002@v1.yaml:17-21`, not in code.
- `measures.py:82` hard-codes `len(used) >= 3` although the registry has `min_bands` (DEF-5).
- The scan test skips registry values 0, 1, 0.5, 2, 3, 4 (`test_registry.py:195`). A threshold with one of those values could sit in code unseen.

## Wrong-pass attempts (C7)
| # | Attempt | Command | Verdict | Exit | Message |
|---|---|---|---|---|---|
| 1 | M-001 and M-002 missing path | `m001/m002 <nonexistent>` | INVALID_INPUT | 2 | clear |
| 2 | M-003 and M-004 missing path | `m003/m004 <nonexistent>` | INVALID_INPUT | 2 | clear |
| 3 | Empty gait file (set of 9) | m001, m002 | INVALID_INPUT | 2 | "no header record" |
| 4 | Gait log truncated at 100 kB | m001, m002 | INVALID_INPUT | 2 | clear |
| 5 | Gait log with a NaN | m001, m002 | INVALID_INPUT | 2 | clear |
| 6 | Gap in step `n` (line deleted) | m001, m002 | INVALID_INPUT | 2 | clear |
| 7 | Schema `gait_log@v2` | m001, m002 | INVALID_INPUT | 2 | clear |
| 8 | Gait set where one log has zero contacts | m001 | INCOMPLETE | 3 | ok |
| 8b | Same set | **m002** | **PASS** | **0** | **no warning (DEF-1)** |
| 8c | A single log with zero contacts | m001, m002 | INCOMPLETE | 3 | ok ("vacuous: not a pass") |
| 8d | Set where **8 of 9** logs have zero contacts | **m002** | **PASS** | **0** | **no warning (DEF-1)** |
| 8e | Same set | m001 | INCOMPLETE | 3 | ok |
| 9 | M-003 set with one sidecar deleted | m003 | INVALID_INPUT | 2 | lists both mismatches |
| 10 | M-004 sequence with one sidecar deleted | m004 | INVALID_INPUT | 2 | clear |
| 11 | M-004 with a whole sequence (T2) missing | m004 | INCOMPLETE | 3 | ok |
| 12 | M-004 with 60 frames, no `--fixture` | m004 | INCOMPLETE | 3 | ok |
| 13 | M-004 frozen (all frames = frame 0) | m004 | INVALID_INPUT | 2 | "frozen capture" |
| 14 | All-black capture, M-003 (colour only, and all channels) | m003 | INVALID_INPUT | 2 | "palette_index ... count differs from color_internal" |
| 15 | M-004 all-black (colour, index, depth and mask) | m004 | INVALID_INPUT | 2 | "frozen capture" |
| 15b | M-004 S0 only, all black | m004 | INVALID_INPUT | 2 | "blank frame" |
| 15c | M-004 colour PNGs black, index PNGs real | m004 | PASS (fixture mode) | 0 | colour is not read when an index exists (DEF-3, minor) |
| 16 | Sidecar declares speed 1.0, poses move 0.1 | m004 | INVALID_INPUT | 2 | per-frame motion mismatch |
| 17 | Poses scaled x0.75 against declared 1.0 | m004 | INVALID_INPUT | 2 | "differs ... by more than 10 %" |
| 18 | Truncated `palette_index` PNG | m004 | INVALID_INPUT | 2 | clear |
| 19 | Truncated `color_output` PNG | m003 | "INVALID_INPUT" in the stack trace, **TOOL_ERROR** | **4** | **stack trace (DEF-2)** |

## Defects
**DEF-T002-arch-1 (blocker, C7 and C6).** M-002 reports PASS, exit 0, "failing gating: 0", with no warning, when most of the set has no contacts.
- Evidence: `wp/m002_zero8.txt`. The 9-capture gait set has all `contact` flags and `fn` zeroed in the 8 captures other than `stomp`. Result: `VERDICT: PASS ... M-002.1 0 L_f`. The same set under m001 is INCOMPLETE. `wp/m002_g_zero.txt` (only `walk_fr020` contactless) also PASSes, and `report.json warnings` is `[]`.
- A single contactless log alone gives INCOMPLETE (`wp/m002_zero_only.txt`). The vacuous check therefore runs on the pooled contacts, not per capture. An emitter that never plants a foot in a capture passes M-002 as long as one other capture has contacts. M-002.4 and M-002.5 are likewise vacuously PASS for those captures.
- PASS looks like this: any required capture with zero usable contacts (or too few) gives NOT_RUN for M-002.1 and an INCOMPLETE verdict naming that capture, or at minimum a gating per-capture check. Add a test with 8 of 9 contactless.

**DEF-T002-arch-2 (major, C7, C15, C18).** A truncated or corrupt `color_output` or `color_internal` PNG is not detected, and the tool crashes.
- Evidence: `wp/m003_s3_trunc.txt`. `metrics.py m003 <set with f000002_color_output.png cut to half>` gives `TOOL_ERROR (bug in kaiju_metrics ...)`, a full traceback, exit 4 (`m003.py:92 frame_data` -> `ValueError: PNG could not be decoded`). `validate style_capture` on the same set prints `VALID` and exit 0.
- By C7 a truncated file is INVALID_INPUT (exit 2) and an emitter error must not look like a tool bug. M-004 handles a truncated `palette_index` correctly (exit 2); the colour and depth PNGs are not checked.
- PASS looks like this: the style validator decodes every file named in a sidecar (colour, depth, mask) and reports `file:key: PNG could not be decoded`. m003 and m004 then return exit 2, no traceback.

**DEF-T002-arch-3 (minor, C9).** M-004 never reads `color_internal` when `palette_index` is present, and does not cross-check them as M-003 does. A capture with black colour and a real index passes (`wp/m004_s4_black.txt`: PASS, exit 0). PASS looks like: the M-004 contract check includes the index versus colour consistency check.

**DEF-T002-arch-4 (minor, C15).** `conventions.pixel_origin` is checked only for presence (`style_capture.py:215`). "bottom-left" validates (`val` run `s_origin_conv`, exit 0), while handedness is enforced. PASS: expected string enforced, as for handedness.

**DEF-T002-arch-5 (minor, C10).** `measures.py:82` hard-codes the minimum of 3 depth bands, while `registry/M-003@v1.yaml:25` holds `min_bands` = 3. The literal-scan test skips registry values 0, 1, 0.5, 2, 3, 4 (`tests/test_registry.py:195`), so a threshold of one of those values could be hard-coded unnoticed. PASS: the code reads `reg.p("min_bands")`, and the scan covers all registry values or lists exemptions by file.

**DEF-T002-arch-6 (minor, C1 and C11).** `M-003.b.O` has kind `info`. The spec says advisory, and `info` never shows a warning. A draft registry gives verdict INCOMPLETE while the summary still says "failing gating: 1". PASS: O advisory, and the summary distinguishes a suppressed failure from a real one.

**DEF-T002-arch-7 (minor, hygiene).** `fixtures/gen_style.py --help` is taken as an output directory and writes `Tools/Metrics/fixtures/out/example_set/` into the module (git-ignored; I removed it). `MODULE.md` says the tool writes only to its output directory. PASS: the generators accept `-h` and require an explicit output directory.

## Counts
blocker 1, major 1, minor 5. No defect against C1, C2, C9-C11, C13, C14. Thresholds, rescore and determinism are sound. The two faults sit in contact-count handling (M-002) and image-file validation.
