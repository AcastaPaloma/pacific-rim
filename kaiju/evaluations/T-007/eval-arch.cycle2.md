PASS

# T-007 eval-arch, fix cycle 2 re-check (branch task/T-007 at cc3acf3)
Not independently re-run: Unity was not started; judged from the stored `evidence/T-007/unity_editmode.xml` (suite 111/111 passed, 0 failed) and a read of the two new tests plus the diff of `Assets` since a4356f0 (only `ART_ASSETS.md`, the re-imported FBX and `MechFrameBalancedArtTests.cs` changed; no runtime, editor or asmdef code changed).

| Cycle-1 defect | Status | Evidence |
|---|---|---|
| DEF-T007-arch-1 prefab contents test gap | closed | new `Prefab_HoldsOnly15PartsAnd8MapSockets`: every non-root transform must be one of the 15 map parts or one of 8 map sockets (HashSets, null-checked), sockets must have the `MechBalanced_socket_` prefix, no children, and only a Transform component. A stray empty, mesh child or component fails it |
| DEF-T007-arch-2 hardpoint offset not in config | closed as a recorded limitation (accepted at minor) | `EVIDENCE.md` lines 83-84 and 100: offset stays in `mf_common.py` `FOREARM_PLATE`; proposed Q-NEW-c for a socket field in a later `mech_frame` version. Mirror symmetry of the hardpoint is still tested; no config change, as required |
| DEF-T007-arch-3 swap test weak | closed | `ForearmSwap_...` now uses Unity's built-in cube mesh (not a mech part), re-parents the hardpoint socket onto the stand-in, asserts `ResolveSocket("hardpoint","L")` resolves to it after `Remap`, its parent is the stand-in, and that moving the stand-in by 3 m carries the socket; the 14 other parts are still checked unmoved |
| DEF-T007-arch-4 Remap untested | closed | new `Remap_RejectsUnknownSegment_AndReRootsOnlyExactPrefix`: `Assert.Throws<KeyNotFoundException>` for an unknown id; a sibling `a/forearm_L2/hand` is asserted NOT re-rooted while `a/forearm_L/hand_L` and the socket are |

Could the new tests fail? Yes. The prefab test fails on any extra transform, component or child, or an unresolved map socket. The swap test fails if `Remap` does not re-root the hardpoint (the null/equality asserts) or if the socket is not parented to the new limb. The Remap test fails if the `StartsWith` guard lost its trailing slash (the `forearm_L2` sibling would move) or the exception were dropped.

Other checks
- Config: `git diff origin/claude/fable-orchestrator-prompt-83a0a8 task/T-007 -- Assets/Game/Config/Mech Assets/Game/Config/Mech.meta` = 0 lines.
- Metas: no Assets file was added since a4356f0; the only changed binary is the existing FBX (LFS, its `.meta` already present). All earlier files still have `.meta` (cycle-1 scripted check; no new assets).
- Scope: changes since cycle 1 are limited to `Assets/Game/Units/Mech/Art/`, `Pipelines/blender/mech_frame_balanced/`, `art/models/mech_frame_balanced/`, `evidence/T-007/`, `tasks/T-007.impl.md`. The other paths in the branch diff (Core, ledger, T-001 and T-003 files) come from merges of the integration branch, not T-007 edits. No `MODULE.md` for Units.Mech, no colliders or ArticulationBody (still asserted by `Budget_...`).
- Worktree `git status --short`: empty.

New defects: none.
