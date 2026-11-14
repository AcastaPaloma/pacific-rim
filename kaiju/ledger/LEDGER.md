# LEDGER.md: state of every requirement

> One row per `GAME_BREAKDOWN.md` ID. Maintained by the scribe and the orchestrator only (`ledger/AGENT_PROTOCOL.md` §3).
> Status: `todo` / `in-progress` / `eval` / `accepted` / `rejected`. `accepted` needs evidence links, every linked metric passing with stored values, and a passed evaluation panel (`FABLE_ORCHESTRATOR_PROMPT.md` §10).
> Decisions (`D-`) are constraints: their row tracks the tasks that must honour them. Questions (`Q-`) stay `todo` until a founder answers.

## Requirements

| ID | Status (todo/in-progress/eval/accepted/rejected) | Tasks | Evidence links | Open defects | Last update |
|---|---|---|---|---|---|
| D-001 | todo |  | | | 2026-11-14 |
| D-002 | todo |  | | | 2026-11-14 |
| D-003 | in-progress | T-001 | | | 2026-11-14 |
| D-004 | in-progress | T-001 | | | 2026-11-14 |
| D-005 | todo |  | | | 2026-11-14 |
| D-006 | in-progress | T-004 | | | 2026-11-14 |
| D-007 | in-progress | T-005 | | | 2026-11-14 |
| D-008 | in-progress | R-005, T-005 | | | 2026-11-14 |
| D-009 | in-progress | T-007 (all tasks) | | | 2026-11-14 |
| D-010 | in-progress | T-003 | | | 2026-11-14 |
| D-011 | todo |  | | | 2026-11-14 |
| D-012 | todo |  | | | 2026-11-14 |
| D-013 | in-progress | T-005 | | | 2026-11-14 |
| D-014 | todo |  | | | 2026-11-14 |
| D-015 | todo |  | | | 2026-11-14 |
| D-016 | todo |  | | | 2026-11-14 |
| D-017 | todo |  | | | 2026-11-14 |
| D-018 | in-progress | T-005 | | | 2026-11-14 |
| D-019 | todo |  | | | 2026-11-14 |
| D-020 | todo |  | | | 2026-11-14 |
| D-021 | todo |  | | | 2026-11-14 |
| D-022 | in-progress | R-002, R-003, T-004, T-006 | | | 2026-11-14 |
| D-023 | in-progress | T-007 | | | 2026-11-14 |
| D-024 | todo |  | | | 2026-11-14 |
| D-025 | todo |  | | | 2026-11-14 |
| D-026 | todo |  | | | 2026-11-14 |
| D-027 | in-progress | T-004 | | | 2026-11-14 |
| F-001 | in-progress | R-005, T-005 | | | 2026-11-14 |
| F-002 | in-progress | T-005 | | | 2026-11-14 |
| F-003 | in-progress | T-005 | | | 2026-11-14 |
| F-004 | todo |  | | | 2026-11-14 |
| F-005 | todo |  | | | 2026-11-14 |
| F-006 | todo |  | | | 2026-11-14 |
| F-007 | todo |  | | | 2026-11-14 |
| F-008 | todo |  | | | 2026-11-14 |
| F-009 | in-progress | T-001, T-005 | | | 2026-11-14 |
| F-010 | todo |  | | | 2026-11-14 |
| F-011 | todo |  | | | 2026-11-14 |
| F-012 | todo |  | | | 2026-11-14 |
| F-020 | todo |  | | | 2026-11-14 |
| F-021 | todo |  | | | 2026-11-14 |
| F-022 | todo |  | | | 2026-11-14 |
| F-023 | todo |  | | | 2026-11-14 |
| F-024 | todo |  | | | 2026-11-14 |
| F-025 | todo |  | | | 2026-11-14 |
| F-026 | todo |  | | | 2026-11-14 |
| F-040 | in-progress | R-004, T-003 | | | 2026-11-14 |
| F-041 | todo |  | | | 2026-11-14 |
| F-042 | todo |  | | | 2026-11-14 |
| F-043 | in-progress | T-001 | | | 2026-11-14 |
| F-044 | todo |  | | | 2026-11-14 |
| F-045 | todo |  | | | 2026-11-14 |
| F-046 | in-progress | T-004 | | | 2026-11-14 |
| F-047 | todo |  | | | 2026-11-14 |
| F-060 | todo |  | | | 2026-11-14 |
| F-061 | todo |  | | | 2026-11-14 |
| F-062 | todo |  | | | 2026-11-14 |
| F-063 | todo |  | | | 2026-11-14 |
| F-064 | todo |  | | | 2026-11-14 |
| M-001 | in-progress | R-001, T-002, T-005 | | | 2026-11-14 |
| M-002 | in-progress | R-001, T-002, T-005 | | | 2026-11-14 |
| M-003 | in-progress | R-002, T-002, T-004, T-006 | | | 2026-11-14 |
| M-004 | in-progress | R-002, T-002, T-004 | | | 2026-11-14 |
| M-005 | in-progress | R-004, T-003 | | | 2026-11-14 |
| M-006 | in-progress | R-005, T-005 | | | 2026-11-14 |
| M-007 | todo |  | | | 2026-11-14 |
| M-008 | in-progress | R-003 | | | 2026-11-14 |
| Q-001 | todo |  | | | 2026-11-14 |
| Q-002 | todo |  | | | 2026-11-14 |
| Q-003 | todo |  | | | 2026-11-14 |
| Q-004 | todo |  | | | 2026-11-14 |
| Q-005 | todo |  | | | 2026-11-14 |

## Research findings (R-###)

| ID | Topic | Status | File | Last update |
|---|---|---|---|---|
| R-001 | Weight and gait metric specs: M-001, M-002 | commissioned | research/R-001.md | 2026-11-14 |
| R-002 | Style metric specs: M-003, M-004; palettes extracted from the references | commissioned | research/R-002.md | 2026-11-14 |
| R-003 | Pixel-pipeline technique survey for URP 17 (Unity 6.6); M-008 render budget | commissioned | research/R-003.md | 2026-11-14 |
| R-004 | GIS data sources for the M0 tile and the Bay; M-005 spec | commissioned | research/R-004.md | 2026-11-14 |
| R-005 | Articulated biped at 10,000 t: mass properties, PD drives, balance, walking control; M-006 spec | commissioned | research/R-005.md | 2026-11-14 |

## Tasks (T-###)

| Task | Slice | Module(s) | Milestone | State | Branch | Cycle | Last update |
|---|---|---|---|---|---|---|---|
| T-001 | Core: events bus, service registry, IDs, feature flags | Core | M0 | planned | task/T-001 | 0 | 2026-11-14 |
| T-002 | Headless metric tools for M-001 to M-004 | Tools/Metrics | M0 | planned | task/T-002 | 0 | 2026-11-14 |
| T-003 | One real SF tile: terrain, bathymetry, OSM buildings, faults | Pipelines/geo, World.Geo | M0 | planned | task/T-003 | 0 | 2026-11-14 |
| T-004 | Pixel pipeline renderer features + third-person rig stabilisation | Rendering.Pixel, Camera | M0 | planned | task/T-004 | 0 | 2026-11-14 |
| T-005 | One mech body that walks with physics and intent control | Physics.Body, Control, Units.Mech | M0 | planned | task/T-005 | 0 | 2026-11-14 |
| T-006 | M0 style frame: SF tile + static mech through the pixel pipeline | integration of T-003/4/5/7 | M0 | planned | task/T-006 | 0 | 2026-11-14 |
| T-007 | Original greybox mech asset at true scale + concept prompt sheets | Units.Mech (asset) | M0 | planned | task/T-007 | 0 | 2026-11-14 |

## Defects

One line per evaluator defect until it is fixed and re-evaluated, or a founder rejects it.

| Defect | Task | Severity | Summary | Evidence | State (open/fixed-pending-eval/closed/rejected) | Last update |
|---|---|---|---|---|---|---|

## Founder questions raised by the swarm

Mirrors `GAME_BREAKDOWN.md` §11 for questions raised after the initial breakdown. Each has a recommended default in use (`Assumed`).

| ID | Question | Recommended default | Raised by | Blocking? |
|---|---|---|---|---|
