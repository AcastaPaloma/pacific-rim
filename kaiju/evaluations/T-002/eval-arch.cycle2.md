FAIL
# T-002 eval-arch, cycle 2 re-check (branch task/T-002 @ ec5ffba)
All 7 cycle-1 defects are closed. One new major defect, DEF-T002-arch-8, keeps the verdict at FAIL: M-004 PASSes with a gating sub-measure (C_px.Y) NOT_APPLICABLE. Pytest: **364 passed in 338 s** (0:05:38, my run; matches the reported 364). Scratch: `/private/tmp/claude-501/eval-T-002-arch/` (`wp/c2_*.txt`, `old/`).

## Cycle-1 defects
| Defect | State | Evidence |
|---|---|---|
| 1 blocker: M-002 passes with 8 of 9 captures contactless | closed | `c2_m002_zero8`: INCOMPLETE exit 3, M-002.1 NOT_RUN. Also one contactless capture (`c2_m002_zero1`): exit 3. The same pooled-count pattern in M-001 is fixed too: contactless 1 of 9 and 8 of 9 give INCOMPLETE exit 3. Test `test_fix2.py::test_arch1_*` (4 tests) fails on the old code (see below). |
| 2 major: truncated colour PNG gives a stack trace and exit 4 | closed | `validate style_capture` exits non-zero with `files.color_output: PNG truncated inside chunk IDAT`. `m003` on the same set: INVALID_INPUT exit 2, no traceback. Truncated `palette_index` through m004: exit 2. Tests `test_arch2_*` (corrupt CRC, truncated colour, depth and mask, unexpected-error guard). |
| 3 minor: M-004 does not cross-check index against colour | closed | The colour-black capture with a real index (cycle-1 PASS) now gives INVALID_INPUT exit 2. Registry v1.1 adds `index_colour_tol_de00` 2.0. |
| 4 minor: `pixel_origin` not validated | closed | `test_arch4_conventions_enforced` covers `pixel_origin`, `up` and `forward`; it fails on the old code. |
| 5 minor: hard-coded min depth bands | closed | `test_arch5_min_bands_from_registry` fails on the old code. |
| 6 minor: M-003.b.O kind `info` | closed | v1.1 kind advisory (diff below). The draft summary now prints "suppressed: draft metric, verdict withheld", with a `failures_suppressed` flag. |
| 7 minor: generators treat `--help` as an output directory | closed | `gen_gait.py --help` and `gen_style.py --help` exit 0 and write nothing (WT `git status` clean). `test_arch7_*` fails on the old code. |

Old-code check: `Tools/Metrics` from 0db318e plus the new `tests/test_fix2.py` gives **31 failed, 2 passed**. The arch tests therefore detect the cycle-1 defects.

## New wrong-pass attempts (cycle 2)
| # | Attempt | Command | Verdict | Exit |
|---|---|---|---|---|
| 1 | M-002: 8 of 9 captures contactless | m002 | INCOMPLETE | 3 |
| 2 | M-002 and M-001: one contactless capture among good ones | m002, m001 | INCOMPLETE | 3 |
| 3 | M-001: `walk_fr020` cut to 250 lines (< 3 steady strides), others good | m001 | INCOMPLETE (M-001.1-.3 NOT_RUN) | 3 |
| 4 | M-001: `curve` cut to 250 lines | m001 | INCOMPLETE | 3 |
| 5 | M-003: one profile (`warm_amber_haze`) with flat depth (< 3 bands), good profile alongside | m003 | c1 NOT_RUN for that profile; verdict FAIL (a different gating failure) | 1 |
| 6 | M-003: second profile with no dither | m003 | b.DIG FAIL, 0 of 12 | 1 |
| 7 | M-003: every `mask_animated` set to 255 (M-003 ignores that mask; it is an M-004 input, R-002:321) | m003 | headless PASS, as without the mask; correct | 3 (f pending) |
| 8 | M-004: sequence with `mask_animated` all 255 | m004 | INVALID_INPUT "blank frame" | 2 |
| 9 | M-004: blank T2 | m004 | INVALID_INPUT "frozen capture" | 2 |
| 10 | M-004: T3 sequence missing | m004 | INCOMPLETE ("no T3 sequence") | 3 |
| 11 | M-004: Y1 and Y2 missing | m004 | INCOMPLETE | 3 |
| 12 | M-004: T1 with no dithered pixels (`pattern=flat` or `stripes`); T2 and T3 dithered | m004 | **PASS** (provisional: 8 assumed), exit 0 | 0 |
| 13 | M-004: stock pass set; its yaw sequences have 0 and 15 dithered pixels | m004 | **PASS**, `C_px.Y` NOT_APPLICABLE, only a warning | 0 |
| 14 | Cycle-1 repeats: frozen, speed mismatch, black colour, truncated PNG, missing sidecar, NaN, gap, schema, empty file | m001-m004 | INVALID_INPUT | 2 |

Rows 12 and 13 are the new defect. Row 12 passes mostly because the class pools T1 and T2 (T2 is still dithered); row 13 is the whole-class case.

## New defects
**DEF-T002-arch-8 (major, C7).** An M-004 gating sub-measure that cannot be measured is NOT_APPLICABLE, and the verdict still reads PASS exit 0.
- Evidence: `wp/c2_m004_base.txt`, `wp/c2_m4_flatT1.txt`. `C_px.Y` is `NOT_APPLICABLE` because the sequences have fewer dithered pixels than `crawl_min_interleaved_share` (1 %, assumed). The warning is `... no dithered pixels to measure crawl on (0 px); C_px NOT_APPLICABLE`. The verdict is `PASS (provisional: 8 assumed)`, exit 0. The same for a single sequence with no dither among others in its class.
- C7 treats "no evaluated dither frames" as INCOMPLETE. M-003.b guards dither presence in M-003, but M-004 alone can pass with crawl never measured. The documented reading is `SPEC_READINGS.md` S20.
- PASS looks like this: a class where C_px cannot be measured on any sequence gives NOT_RUN and verdict INCOMPLETE. A per-sequence NA stays acceptable only when another sequence of that class measured it. The tool's own pass fixture then needs dithered yaw content.

**DEF-T002-arch-9 (minor, C11).** A rescore under v1.0 does not reproduce a cycle-1 M-004 report. `rescore out_m004_base/report.json` gives DIFFERS: `M-004.C_px.Y PASS 0.0 -> NOT_APPLICABLE`, and the label changes from 9 to 8 assumed. The README says that behaviour fixes apply under any revision. The difference is visible and the verdict is still PASS, so nothing is silently reinterpreted. PASS looks like: behaviour changes gated by registry revision, or an explicit tool-version field the rescore reports. M-002 cycle-1 rescore: REPRODUCED.

## Registry v1.0 vs v1.1 (my diff)
- No threshold value, unit, status or source changed in M-001..M-004. `diff M-00x@v1.yaml M-00x@v1.1.yaml` shows only the changes below. v1.0 files still load, and the default is v1.1 (revision v1.1 in new reports).
- M-001: `steady_captures` list (walk_fr010, walk_fr020, walk_fr030, curve) and `min_contacts_per_foot` 1.
- M-002: `min_contacts_per_foot` 1.
- M-003: `b.O` kind `info` -> `advisory` (title edit).
- M-004: `index_colour_tol_de00` 2.0 (derived) and `crawl_min_interleaved_share` 0.01 (`assumed_pending_calibration`). The second one is a new assumed constant that feeds DEF-8.

## Other checks
- Determinism: m003 twice and m001 twice give identical reports with `run` stripped.
- README: all 13 quick-start commands give the stated exit codes (0,0,0,0,0,0,3,0,0,3,0,0,0). Rescore, baseline and the three validators exit 0.
- Added report fields: `failures_suppressed`, `registry.defaults_for_later_constants`. The README lists them. No command or field was renamed.
- Scope: `git diff --stat 0db318e..ec5ffba` shows 134 files, all in `Tools/Metrics` (25), `evidence/T-002` (108) and `tasks/` (1). Nothing outside those.
- Merge dry run: `git merge-tree --write-tree origin/claude/fable-orchestrator-prompt-83a0a8 task/T-002` printed tree `0f7297c59204f7b3ae63dd35441e14df9c9df618`, exit 0, no conflicts.

## Counts
Closed 7, open 0, new 2 (1 major, 1 minor). Wrong passes: none for M-001, M-002 and M-003. One remains for M-004 (DEF-8, a vacuous crawl measurement).
