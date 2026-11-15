# M2/M3 progress (lead orchestrator state; reload from here)

Orchestrator: M2/M3 lead, session 1 (2026-11-15, session `local_8f501ad8-71e5-4377-bc8c-7411583fbe4b`). Founder asleep: §15, every
decision logged in `ledger/HANDOFF.md` under "M2/M3 lead orchestrator, session 1" as "decided autonomously (founder asleep)".
Plans: `plans/M2.md`, `plans/M3.md`. IDs: `ledger/IDS.md` (M2 T-100–T-139 …, M3 T-140–T-159 …).
Integration branch `claude/fable-orchestrator-prompt-83a0a8` (git root = the worktree above `kaiju/`). Never `main`.
Worker rules: side-session notes folder `m2-track-rules.md` (worktree `.wt/<TASK>`, merge lock, Unity < 2, §13/§14, hand-over).
M1 (lead `local_979e0962…`) and T-005 (own orchestrator) are running: never touch their tasks or files.

## How tracks run
Session starts are refused on turns begun by another session, so tracks run as **Agent-tool workers** (Sonnet/medium,
background), one purpose each, launched by the lead. A successor lead re-launches the next step of each track from the
table below. Unity: M1 + T-005 hold the slots; M2/M3 steps are Python/dotnet until a slot frees (check `pgrep`).

## Tracks and next steps (in order; ✓ = merged)
| Track | Step | State |
|---|---|---|
| M2-1 Bay bake | R-100 Bay coverage/sources/storage + T-100 `bay_manifest@v1` | W1 launched |
| | T-101 bulk fetch (background, status files) → T-102 bake → T-103 heights → T-104 landmarks → T-105 infra → T-106 M-005@v2 | pending R-100 |
| M2-2 Streaming | R-101 streaming/LOD research | W1 launched |
| | T-107 `World.Streaming` core (dotnet) → T-108 `bake_lod.py` → T-109 Unity loader → T-110 persistence → T-111 flythrough + metrics | pending R-101 |
| M2-3 Species/pack | R-102 species biomechanics; R-103 pack + M-007@v2 + M-008 Bay scenario | W1 launched |
| | T-112 species specs → T-113 `Game.AI.Pack` → T-114 emergence → T-115 m007_bay; T-116/T-117 staged on T-041/T-005 | pending R-102/R-103 |
| M2-4 Economy | R-104 design spec | W1 launched |
| | T-118 Economy → T-119 Salvage → T-120 Roster → T-121 Refit → T-122 installers → T-123 campaign sim | pending R-104 |
| M2-5 Gate metrics | T-124 M-008@v1 Bay scenario, T-125 calibration plan | after tracks 1–3 |
| M3-1 Build | T-141 Platform contracts + null impl (dotnet) | W1 launched |
| | T-142 attribution + tile copy, T-143 VDF templates | W1 launched (one worker) |
| | T-140 build entry + smoke (needs a Unity slot) | pending slot |
| M3-2 Disclosure | R-120 (+ R-121 capture research, same worker) | W1 launched |
| | T-144 provenance scanner → T-145 disclosure draft | pending R-120 |
| M3-3 Capture | T-146 capture plan → T-147 driver (Unity) → T-148 checks | pending R-121 |
| M3-4 Polish/demo | staged until M2 closes | — |

## Launched workers (session 1)
(filled in as they launch; agent ids are session-local — a successor relies on branches `task/<ID>` and `tasks/<ID>.progress.md`)
