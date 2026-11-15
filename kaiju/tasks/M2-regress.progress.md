# M2-regress progress
Batched full Unity run per §13 on integration head 68deb4f (merges after M1-merge-7/M2-batch-1: T-188 audio.slice, T-104, T-115e/f, T-140, T-148, T-187, T-105 World.Infrastructure + infrastructure.enabled).
- Clone worktree `.wt/M2-regress`, Library `cp -cR` of an idle worktree's. Batch Unity count was 1 (<2).
- `unity run . -- -executeMethod Game.Core.Unity.CoreConfigAsset.CreateOrUpdate`: rc 0, 25 flag files, GameCoreConfig.asset +1 reference (guid 69dc3f31..., stale vs new flag file); no conflict markers. Log: evidence/regression/M2-merge-1.coreconfig.log
- `unity test . --mode EditMode`: 795 total, 791 passed, 0 failed, 4 skipped (261 s). Run after regeneration. Log/xml: evidence/regression/M2-merge-1.editmode.{log,xml}. No bisect needed.

## HANDOFF entry (draft)
M2-regress: EditMode 795/791 pass/0 fail/4 skipped on 68deb4f; GameCoreConfig.asset regenerated (+1 flag reference) and merged. Evidence evidence/regression/M2-merge-1.*. Next batched run after 3 more merges.
