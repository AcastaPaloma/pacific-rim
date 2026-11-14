PASS
# T-002 eval-arch, cycle 3 final re-check (branch task/T-002 @ 4e29ee9)
DEF-T002-arch-8 and arch-9 are closed. All wrong-pass attempts from cycles 1 and 2 plus 5 new ones give INVALID_INPUT, INCOMPLETE or FAIL; none gives PASS or exit 0 where it should not. Pytest: **386 passed in 278 s** (0:04:38, my run; matches the reported 386). Scratch: `/private/tmp/claude-501/eval-T-002-arch/` (`wp/c3_*.txt`, `old3/`).

## Defects
| Defect | State | Evidence |
|---|---|---|
| arch-8 (major): M-004 PASS with C_px.Y NOT_APPLICABLE | closed | The old stock-fixture reading is now INCOMPLETE exit 3: `wp/c3_m004_base.txt` ("C_px.Y NOT_RUN ... no dithered pixels"). One T1 sequence with no dither (`c3_m4_flatT1`, `c3_m4_stripesT1`): INCOMPLETE exit 3. The report model has one shared rule: PASS only if every gating sub-measure of every member is PASS; NOT_RUN, PENDING and NOT_APPLICABLE without a cited `spec_clause` give INCOMPLETE (`test_arch8_shared_rule_gating_not_applicable_is_incomplete[M-001..M-004]`). The regenerated M-004 pass fixture (yaw sequences now dithered) legitimately gives PASS exit 0 (`c3_new_m004`). |
| arch-9 (minor): v1.0 rescore not reproduced and unexplained | closed | `rescore` of my cycle-1 M-004 report: exit 3, "DIFFERS (2)" (verdict PASS -> INCOMPLETE, C_px.Y PASS -> NOT_RUN). The next line reads "CAUSE: tool semantic change, not the registry (tool semantics 1 -> 3, kaiju_metrics 1.0.0; registry revision unchanged)", then lists each semantic change by version. The cycle-1 M-002 report is still "REPRODUCED". `rescore.py: explain_semantics` and the new `tool.semantics` field. |
| arch-1 to arch-7 | closed (re-verified) | Every cycle-1 and cycle-2 attempt below still behaves as in cycle 2. |

Tests against the old code: `Tools/Metrics` from ec5ffba plus the new `tests/test_fix3.py` gives **22 failed**, 0 passed. That covers the arch-8, arch-9, tolerance and per-frame-offset tests, so they detect the old behaviour.

## Wrong-pass attempts (all re-run with the cycle-3 code)
| Attempts | Result |
|---|---|
| Missing path, M-001 to M-004 | INVALID_INPUT, exit 2 |
| Empty, truncated, NaN, step gap, wrong schema (gait) | INVALID_INPUT, exit 2 |
| Zero contacts (1 of 9 and 8 of 9 logs), m001 and m002 | INCOMPLETE, exit 3 |
| Short `walk_fr020` or `curve` (< 3 steady strides), m001 | INCOMPLETE, exit 3 |
| M-003: missing sidecar, black colour and all-black, truncated PNG | INVALID_INPUT, exit 2, no traceback |
| M-003: fully masked frames (all or one frame, `mask_animated` is not an M-003 input) | headless PASS and verdict INCOMPLETE, exit 3 (rating pending) |
| M-003: second profile with flat depth (< 3 bands) or no dither | c1 NOT_RUN / b.DIG FAIL, exit 1 |
| M-004: frozen, black colour, black all channels, speed mismatch, poses off by 25 %, truncated index, missing sidecar, blank S0, blank T2, `mask_animated` all 255 | INVALID_INPUT, exit 2 |
| M-004: sequence class missing (T2 or T3 or Y1+Y2) | INCOMPLETE, exit 3 |
| M-004: 60 frames without `--fixture` | INCOMPLETE, exit 3 |
| **M-004: yaw sequences with 0 and 15 dithered pixels** (old stock fixture) | **INCOMPLETE, exit 3** (was PASS in cycle 2) |
| **M-004: one T1 sequence with no dither (T2 and T3 dithered)** | **INCOMPLETE, exit 3** (was PASS) |
| New 1: M-003 sidecars declare `output_block_offset_px` [1,1] against images aligned at 0, on all frames | FAIL exit 1, M-003.d-iv 0.5242 < 0.995 |
| New 2: per-frame offsets varying per frame ([k%4, 3k%4]) against content built for 0 | FAIL exit 1, d-iv 0.5242 |
| New 3: one frame (k=3) declares [2,0], others [0,0] | FAIL exit 1, d-iv 0.742 |
| New 4: M-004 `S_px.Y` value 0.0059624062822, threshold set to value x (1 - 1e-6) with `<=` | **FAIL** exit 1 (margin -5.96e-09) |
| New 5: same threshold at value x (1 - 1e-10), and at value x (1 + 1e-6) | PASS, exit 0 in both cases; the first is the documented 1e-9 equality band (margin -6e-13 printed honestly), the second is a true pass |

The tolerance therefore does not loosen anything at 1e-6 relative; it only absorbs about 1e-9 relative, as documented.

## Registry and tolerance
- Registry files changed between ec5ffba and 4e29ee9: only the new `tool_settings.yaml`. No threshold value, unit, status or source changed in any `M-00x@v1.yaml` or `M-00x@v1.1.yaml`.
- Direct diff v1.0 against v1.1 (values only): M-001 and M-002 add `min_contacts_per_foot` 1; M-004 adds `index_colour_tol_de00` 2.0 (derived) and `crawl_min_interleaved_share` 0.01 (`assumed_pending_calibration`); M-003 only the `b.O` kind (`info` -> `advisory`). v1.0 files still load.
- `registry/tool_settings.yaml` holds `compare_rel_tol: 1.0e-9` with an explanatory comment (rounding only, zero thresholds exact). The README documents it (cycle 3 section, "Comparison tolerance") and reports carry the new field `tool.settings` (test `test_feel3_settings_recorded_in_report`).

## Other checks
- README quick start: all 13 commands give 0,0,0,0,0,0,3,0,0,3,0,0,0, as stated. WT `git status` is clean after my runs.
- Renames: none. The README "Changes in fix cycle 3" section lists the new fields (`tool.settings`, `tool.semantics`, `rescore`, optional `spec_clause`) and the validator change (per-frame output offsets; `internal_wh`, `output_wh` and `integer_scale` constant per sequence).
- Scope: `git diff --stat ec5ffba..4e29ee9` shows 69 files: `Tools/Metrics` 11, `evidence/T-002` 57, `tasks/` 1. Nothing else.
- Merge dry run: `git merge-tree --write-tree origin/claude/fable-orchestrator-prompt-83a0a8 task/T-002` printed tree `5a4ab39c3e3216fefc2fd3a542a761e6c8c92f48`, exit 0, no conflicts.

## New defects
None blocker, major or minor found in this cycle. Two residual remarks, neither against a criterion: (a) when a value falls inside the 1e-9 band the row can print a tiny negative margin beside PASS; it is correct and visible, so left as is; (b) the old M-004 fixtures made in earlier cycles no longer pass, which is intended and is explained by the rescore CAUSE line.

## Counts
Closed 9 of 9 (arch-1 to arch-9). Open 0. New 0. Wrong passes remaining: none found.
