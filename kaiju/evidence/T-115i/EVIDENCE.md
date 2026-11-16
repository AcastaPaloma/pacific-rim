# T-115i evidence (P4 under bay_cost@v2)
- `cr45/`: hold-out matrix (seeds 3001-3030 / 3001-3010, evaluator rev 1 and 2) with `M007_PACK="CommitRangeFrac=4.5"` on bay_cost@v2, command `M007_PACK="CommitRangeFrac=4.5" Tools/m007_bay_holdout.sh evidence/T-115i/cr45` (code = task/T-115i base fc2bdbc, no code change). Baseline: evidence/T-115h/v2.
- Dev-seed (1-12) tools: `Tools/m007_bs_dev_count.py <log dir>` (bait-strike opportunity/event counts per arm and composition), `Tools/m007_bs_opportunity_diag.py <log dir> <name filter> <n>` (per opportunity: Bait/aim distance, other members' state, distance to the mech, first Charge delay).
- Clause table (rev 2): see ledger/HANDOFF.md T-115i. No threshold touched; default PackConfig unchanged.
