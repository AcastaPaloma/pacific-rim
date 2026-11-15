# Regression: M1 merge 1 (T-020, T-024, T-025, T-029 into the integration branch), 2026-11-15

Merges: 2dbf718 (T-020), 9178dd0 (T-024), 9480080 (T-025), fc90ec9 (T-029); fixes d549bf9 (untrack the `Pipelines/geo/cache` symlink T-020 committed; ignore the path itself), 2cbb131 (Unity has no `Random.NextInt64`; NetSnapshotTests).

| Suite | Result |
|---|---|
| dotnet Core.Tests | 96/96 |
| dotnet WorldGeo.Tests | 28/28 |
| dotnet WorldDestruction.Tests | 60/60 |
| dotnet Net.Tests | 62/62 (+1 Explicit: M-057 N=2 ≤ 80 % of N=4, per-client ratio 0.865) |
| dotnet Control / Physics.Body / Camera / Rendering.Pixel | 7 / 12 / 17 / 45, all pass |
| pytest Pipelines/geo | 72/72 |
| Unity EditMode (batch, integration checkout) | 369 passed, 0 failed, 2 skipped of 371 |
| `CoreConfigAsset.CreateOrUpdate` | exit 0; `GameCoreConfig.asset` unchanged (git auto-merge was already right) |

Notes:
- The merge of T-020 replaced the integration checkout's git-ignored raw cache directory with T-020's committed symlink (it pointed back at itself). The cache was re-cloned from `.wt/T-003` (1.7 GB, APFS clone) and the Assessor extract for L0_30_36 re-fetched from DataSF `wv5m-vpq2`; its content hash equals the committed manifest's, and only `fetched_utc` was reset to the committed value (2026-11-15T00:55:10Z) so the byte-identical rebake test holds. Assessor extracts for the other 14 tiles are not in the cache (only needed to re-bake them).
- Unity created `.meta` files for the 14 new tile folders; committed with this note.
