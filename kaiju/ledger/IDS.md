# IDS.md: ID registry (next free numbers)

Only the scribe and the orchestrator allocate IDs. IDs never change and are never reused.

| Prefix | Meaning | Highest used | Next free |
|---|---|---|---|
| D- | Design decision | D-027 | D-028 |
| F- | Feature | F-064 | F-065 |
| M- | Quality metric | M-008 | M-009 |
| Q- | Open question | Q-005 | Q-006 |
| R- | Research finding | R-005 (commissioned) | R-006 |
| T- | Task (feature slice; ledger-only, not a breakdown ID) | T-007 | T-008 |

Defects allocate themselves: `DEF-<task>-<evaluator>-<n>`.
Metric specs are versioned: `M-003@v1`, `M-003@v2`, …; evaluators cite the version they used.
