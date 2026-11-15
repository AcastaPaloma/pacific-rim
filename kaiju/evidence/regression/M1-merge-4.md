# M1 merge 4 regression (M1 lead session 3, 2026-11-15)
Integration branch `claude/fable-orchestrator-prompt-83a0a8`; merged task/T-021 (step 4), task/T-030 (step 1), task/T-022 (T-022 step 1 + T-023 step 1), task/T-032 (steps A–C); plus the M-005 Marin fill (7a3b7c9).
- Bake outputs conflicted on the merge of task/T-022 (build_info/manifest/buildings of 26_37, 26_38, 27_37, 30_36): integration side taken, then all 15 tiles re-baked on the merged code.
- Fix found by this regression: World.Geo `GeoManifest.Licences` lacked `LicenseRef-facts-cited` (R-020 §2 contract-break check 2); added.
- GameCoreConfig.asset regenerated with `CoreConfigAsset.CreateOrUpdate` (new FeatureFlags/AI.json).

| Suite | Result |
|---|---|
| pytest Pipelines/geo | 114 passed |
| M-005@v1.2 district | PASS, union default share 0.0144 |
| dotnet Core, Control, Net, Net.ControlBridge, AI, WorldGeo (63), WorldDestruction, WorldSeismic, Physics.Body, Camera, Rendering.Pixel | all passed |
| Unity EditMode | 445 total, 443 passed, 0 failed, 2 skipped |
| Unity M-015 (UnityOnly, T-021 worktree) | 1/1 PASS |
| Unity M-018 PlayMode (T-021 worktree) | 1/1 (relative numbers) |
