# IDS.md: ID registry (next free numbers)

Only the scribe and the orchestrator allocate IDs. IDs never change and are never reused.

| Prefix | Meaning | Highest used | Next free |
|---|---|---|---|
| D- | Design decision | D-027 | D-028 |
| F- | Feature | F-064 | F-065 |
| M- | Quality metric | M-020 | M-021 |
| Q- | Open question | Q-067 | Q-068 |
| R- | Research finding | R-008 (R-006, R-007, R-008 recorded) | R-009 |
| T- | Task (feature slice; ledger-only, not a breakdown ID) | T-007 | T-008 |

Defects allocate themselves: `DEF-<task>-<evaluator>-<n>`.
Metric specs are versioned: `M-003@v1`, `M-003@v2`, …; evaluators cite the version they used.
