PASS

# T-007 eval-arch (module rules, import, tests, evidence index)

Scope: C20, C22, C24, C29-C31 plus the module-rule checks of the brief. Judged from `task/T-007` (diff vs the integration branch), the stored evidence, and my own run.
Independent run: `unity test $WT --mode EditMode` (queue count was 1, so no wait) gave **109 / 109 passed, 0 failed**, 8 of them `MechFrameBalancedArtTests`; XML at `/private/tmp/claude-501/eval-T-007-arch/editmode.xml`. `git status --short` in the worktree was empty afterwards (C30 reproducibility holds; nothing to restore).

## Criteria
| Criterion | Verdict | Support |
|---|---|---|
| Module rules: own asmdefs | PASS | `Art/Runtime/Game.Units.Mech.Art.asmdef` (references: none, engine-using, autoReferenced), `Art/Editor/...Art.Editor.asmdef` (Editor platform only, references the runtime asmdef), `Art/Tests/EditMode/...Tests.EditMode.asmdef` (Editor, `UNITY_INCLUDE_TESTS`). No dependency on any other game module. |
| No gameplay code, colliders, ArticulationBody | PASS | `MechArtRoot.cs` is 14 lines; test `Budget_...` asserts no `Collider` / `ArticulationBody` in the prefab; `unity_hierarchy.txt` shows none |
| No `Units/Mech/MODULE.md`; `ART_ASSETS.md` present | PASS | `Assets/Game/Units/Mech/Art/ART_ASSETS.md` lists assets, IDs, names, regeneration; no MODULE.md in the diff |
| Config unchanged | PASS | `git diff origin/... task/T-007 -- Assets/Game/Config/Mech Assets/Game/Config/Mech.meta` = 0 lines; `research/R-005/...json` is byte-identical (`cmp`) |
| Dimensions from config, none hard-coded | PASS (minor note) | Unity side: `MechFrameConfig.Parse` of the JSON; tests compare against `cfg.*`. Literals only for spec limits (15, 20000, 4 slots, 0.25 m tolerance) and material smoothness 0.1 (`MechFrameImport.cs:114,120`). Blender side reads `FRAME_JSON` (`mf_common.py:23`). See DEF-T007-arch-2 |
| Every new asset and folder has a `.meta` | PASS | scripted check over all added `Assets/` files and their directories: no missing |
| No integration-branch folder meta changed | PASS | `git diff --name-status` shows only `A` entries outside evidence; `Units/Mech.meta` pre-exists on the integration branch and is untouched |
| `.blend` / `.fbx` in LFS | PASS | `git lfs ls-files`: `art/models/.../*.blend`, `*.fbx`, `Assets/.../Models/mech_frame_balanced.fbx` |
| No text-edited `.prefab`/`.unity` | PASS | only `Prefabs/MechFrameBalanced.prefab` (new, a variant of the FBX), written by `MechFrameImport.Run`; second run left `git status -- Assets` empty (`unity_second_run_diff.txt`) |
| C20 15 parts, naming, tree | PASS | `unity_hierarchy.txt`: 15 `MechBalanced_<segment id>` parts, tree pelvis > torso > head / upper_arm > forearm > hand and pelvis > thigh > shank > foot; 8 `MechBalanced_socket_*` empties only. Tests `Mapping_Has15Unique...`, `Hierarchy_FollowsKinematicTree` |
| C22 Sockets, L/R identical, swap | PASS | sockets shoulder, hip, elbow, hardpoint x L/R (hierarchy file); `Sockets_LeftRightMirrorSymmetric` checks position, forward, up, local position and parent mirror; `ForearmSwap_ThroughSocketAndMappingOnly` swaps forearm_L via `MechPartMap.Remap` and checks 14 other parts unmoved, hand re-parented, 15 entries resolve |
| C24 Budget | PASS | 2,952 triangles (`dimensions.json`; per-part `tris=` in `unity_hierarchy.txt`), 4 materials `mech_primary/secondary/joint/accent`, test asserts <= 20000, <= 4, role names, `_BaseMap` null |
| C29 Import clean, axes, H | PASS | `unity_import_run1.log`/`unity_capture.log` per EVIDENCE; test `Axes_YUp_...` (root rotation identity, all lossy scales 1, left at -x, toe at +z); `Height_EqualsConfig_WithSoleOnGround` H = cfg.total_height_m within 1 cm, sole at y 0 |
| C30 Script-made, reproducible, LFS | PASS | see rows above; my run left the worktree clean |
| C31 Mapping and named tests | PASS (with notes) | 15 unique ids equal to the config set, resolve to mesh transforms, names equal `partName`; tests cover mapping, hierarchy, H, joints (origin within min(2 % length, 0.25 m); child-joint distance; limb lengths within 2 %), axes/scale, socket symmetry, swap. Each can fail (they compare against config values, not copies of the prefab's own). MODULE.md deliberately absent per brief |
| Evidence index | PASS | `evidence/T-007/EVIDENCE.md` maps C1-C31 to artifact, measured value, tool/command and commit (`53c2804`, `bed3961`, `a4356f0`). It states honestly C27 not met and C28 open (outside my scope; for eval-style/the orchestrator) |
| Pipeline README rebuilds the model | PASS | `Pipelines/blender/mech_frame_balanced/README.md` and ART_ASSETS list BUILD, MEAS, REND, ROM, COMP, then `unity run ... MechFrameImport.Run`, then `unity test`; paths and flags complete |

## Defects
**DEF-T007-arch-1 (minor)** Test gap for "nothing else exported except named helper empties" (C20).
Evidence: `MechFrameBalancedArtTests.cs` `Mapping_...` counts 15 `MeshFilter`s and `Sockets_...` checks only mirrored pairs; no assertion that every non-part transform is a `MechBalanced_socket_*` and that the socket set equals the map's sockets (8). A stray extra empty or mesh-less child would pass. PASS would look like: an assertion that all transforms are root, 15 parts or map-listed sockets.

**DEF-T007-arch-2 (minor)** Hardpoint/socket placement is not in config.
Evidence: `mf_common.py:90-97` and `build_greybox.FOREARM_PLATE` hold the forearm hardpoint offset (-2.25, -5.5, -2.0) as literals; `mech_frame@v1` has no sockets. Unity tests check hip/shoulder sockets against the config joints but the hardpoint only for mirror symmetry. Acceptable for M0 (sockets are not in the schema), but a future schema field or a ledger note (Q) should own it.

**DEF-T007-arch-3 (minor)** Swap test mounts on the parent of the elbow socket and positions at `socket.position`, never uses the hardpoint socket nor a distinct stand-in mesh (it reuses forearm_R's mesh). It proves the mapping/re-rooting logic, not that a different limb prefab fits the hardpoint. PASS would look like: stand-in built from an independent mesh and an assertion on the hardpoint socket moving with it.

**DEF-T007-arch-4 (minor)** `MechPartMap.Remap` re-roots by path prefix string; `ResolvePart` is a linear `Find` per call. Fine at 15 parts, but a hand-edited asset with duplicated `path` prefixes (e.g. `forearm_L` vs `forearm_L2`) is guarded only by the trailing slash. No test covers a failing Remap (`KeyNotFoundException`). Low risk.

No blocker or major defects against C20, C22, C24, C29-C31 or the module rules.
