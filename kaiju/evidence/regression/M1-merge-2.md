# Regression: M1 merge 2 (T-024b, T-035 step 1, T-021 steps 1-2, T-027), 2026-11-15

Merges: bbb684c (T-024b, destruction model_rev 2), 0cab1a3 (T-035 step 1), f004d1e (T-021 steps 1-2), 367aec4 (T-027 steps 1-4). `GameCoreConfig.asset` regenerated for the new `WorldSeismic.json` flag file.

| Suite | Result |
|---|---|
| dotnet Core / WorldGeo / WorldDestruction / WorldSeismic | 96 / 53 / 78 / 40, all pass |
| pytest Pipelines/geo | 89/89 (after T-035) |
| `CoreConfigAsset.CreateOrUpdate` | exit 0 (asset updated, committed here) |
| Unity EditMode (batch, integration checkout, after the regeneration) | 427 passed, 0 failed, 2 skipped of 429 |

Not in this merge: T-021 step 3 (collider corner defect being fixed), T-020b (district re-bake running), T-036 step 2 (full suite re-run pending).
