# M1 progress (orchestrator state; reload from here)

Orchestrator: M1 session started from the M0 orchestrator. The user is offline, so decisions are made autonomously and logged in `ledger/HANDOFF.md`.
Integration branch: `claude/fable-orchestrator-prompt-83a0a8`. Task worktrees: `.wt/T-0xx` (at the worktree root), branches `task/T-0xx`.
Never merge to `main`. T-005 (`.wt/T-005`, its QUEUE section) belongs to orchestrator session 4. Unity: at most 1 batch process from M1, and only while fewer than 2 run machine-wide.

## Done
- W1/W2 research R-020..R-026 and `research/M1_W1_SUMMARY.md` (9d16027). Decisions are logged in HANDOFF.md (2741a29).

## Running (W2)
| Task | Step | Where | State |
|---|---|---|---|
| T-020 district bake | 1/2: fetch + bake 14 tiles (background script, cache symlinked from the integration worktree) | `.wt/T-020` | running. Script and status: `$SCRATCH/t020/bake.sh`, `status.txt`, per-tile logs |
| T-024 damage model | 1/4: contracts + capacity model + dotnet tests | `.wt/T-024` | worker running |
| T-029 Net core | 1/4: contracts, loopback, handshake + dotnet tests | `.wt/T-029` | worker running |

`$SCRATCH` = `/private/tmp/claude-501/-Users-kuanw-Me-code-pacific-rim--claude-worktrees-fable-orchestrator-prompt-83a0a8/82a000e8-e3b9-4808-b557-7401587b69e2/scratchpad` (session-local; bake outputs land in `.wt/T-020/kaiju/Assets/StreamingAssets/Geo/tiles/v1/L0/`).

## Next
- T-020 step 2 (Sonnet/low): read `status.txt` and the logs, check the manifests, commit the tiles, report the bake time per tile (R-020 §5 open item).
- T-020 add-on (decided): optional `year_built`, `stories`, `struct_class` from DataSF `wv5m-vpq2`, once the columns are confirmed.
- T-024 steps 2–4: shaking route (R-021 §2.4, R-022 §3 BSSA14), debris/collapse (§3), then evaluation prep. Then T-025 save section and T-027 seismic core (W3).
- T-029 steps 2–4: snapshot codec and interpolation, intent buffer, `net_log@v1`. T-030 and T-032 wait for Q-100 contracts from session 4.
- R-026 follow-ups: M-060 as a fourth T-035 step; registry blocks go in with T-035..T-038 (W3).
