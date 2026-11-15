# IDS.md: ID registry (next free numbers)

Only the scribe and the orchestrator allocate IDs. IDs never change and are never reused.

| Prefix | Meaning | Highest used | Next free |
|---|---|---|---|
| D- | Design decision | D-028 | D-029 |
| F- | Feature | F-064 | F-065 |
| M- | Quality metric | M-020 | M-021 |
| Q- | Open question | Q-074 | Q-075 |
| R- | Research finding | R-009 (R-009 reserved: T-005 round-4 QP design) | R-010 |
| T- | Task (feature slice; ledger-only, not a breakdown ID) | T-007 | T-008 |

Defects allocate themselves: `DEF-<task>-<evaluator>-<n>`.
Metric specs are versioned: `M-003@v1`, `M-003@v2`, …; evaluators cite the version they used.
Reserved for M1 walk-independent planning (plans/M1.md, 2026-11-14): T-020–T-039, R-020–R-029, Q-100–Q-129, M-050–M-069; allocate in these blocks only for M1 Tracks 1–6.
M1 overflow (M1 lead session 5 allocated T-040, T-041 outside its block): T-040–T-099 stay with the M1 lead.
Reserved for M2 (plans/M2.md, 2026-11-15, M2/M3 lead): T-100–T-139, R-100–R-119, Q-200–Q-229, M-100–M-119, F-065–F-069.
Reserved for M3 (plans/M3.md, 2026-11-15, M2/M3 lead): T-140–T-159, R-120–R-129, Q-230–Q-249, M-120–M-129.
Allocate inside these blocks only for the matching milestone; the M2/M3 lead owns both.
