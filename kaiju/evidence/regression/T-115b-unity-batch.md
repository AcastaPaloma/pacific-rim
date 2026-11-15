# T-115b Unity batch (EditMode without -nographics + GameCoreConfig regeneration), 2026-11-15
First Unity run over the new M2 asmdefs (World.Emergence + tests, Game.AI.PackHarness, AI.Pack changes, Units.Kaiju->SpeciesRole adapter, T-111b Host reader, Composition refs). Worktree `.wt/T-115b` (fresh import) at integration HEAD d68b9f4; 0 other Unity batches were running (a T-005 batch started later).
- `unity test <wt>/kaiju --mode EditMode`: 734 tests, 729 pass, 4 skipped, 1 fail (`evidence/regression/T-115b-editmode.xml`). All new asmdefs compiled.
- The one failure, `Game.Core.Unity.Tests.AdapterTests.Adapter_ConfigAsset_PointsAtCommittedFiles` (expected 23 flag files), is the known stale `GameCoreConfig.asset` (the ai.pack / emergence flag files had not been regenerated).
- `unity run ... -executeMethod Game.Core.Unity.CoreConfigAsset.CreateOrUpdate`: regenerated `Assets/Game/Core/Unity/Resources/GameCoreConfig.asset` (+1 flag file reference). `AdapterTests` re-run after it: 5/5 pass.
- No URP/graphics noise committed; `Assets/StreamingAssets/Geo/global/v1/bay_manifest.json.meta` committed (missing meta of a tracked file).
