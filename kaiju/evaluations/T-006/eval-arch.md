VERDICT: PASS

Scope: C10 (module boundary, size, scene by script), C17-C19 packet structure, Constraints. Judged from the diff f4c2371..HEAD, EVIDENCE.md, tasks/T-006.impl.md and the packet files. No Unity run.

Findings that hold:
- Diff touches only `Assets/Game/Tools/StyleFrame/` (+ `Assets/Game/Tools/StyleFrame.meta`), `checkpoints/M0/`, `evidence/T-006/`, `tasks/T-006.impl.md`. No other module's code is edited. 176 files, 14,771 insertions.
- Committed size: `git ls-files checkpoints/M0 evidence/T-006` totals about 22 MB (du), against the 120 MB cap. Lossless motion frames sit in git-ignored `checkpoints/M0/bulk/`.
- asmdef `Game.Tools.StyleFrame`: includePlatforms Editor only, autoReferenced false. References Core, World.Geo (+Contracts, Unity), Rendering.Pixel (+Contracts, Editor), Camera (+Contracts), Tools.StyleCapture, URP Runtime. All are existing modules; nothing references it back.
- Scene is built by Editor script (`StyleFrameScene.BuildCli`). `Scenes/.gitignore` ignores `StyleFrame.unity` (confirmed with `git check-ignore`), so the tile-embedding scene is not committed.
- Duplication of StyleCapture code (AddCameras copy, `OnGrid` copy, timing copy) is a recorded module request in tasks/T-006.impl.md ("Module requests", steps 1, 2), not a silent copy. Judged a minor, tracked debt, not a defect that blocks.
- Packet: blind IDs (M003-nn, M004-nn, M005-nn, WALK-nn), separate `answer_key.json` (dict keyed by capture ID), `sheets/*_sheet.csv` (capture, founder_a, founder_b, notes) converted by `make_ratings.py` to the JSON `calibrate` reads, M-003 rubric quoted in `sheets/M-003_rubric.md`, one-page GUIDE.md that states the sign-off rule and the flag-on-weak-correlation rule, `spread.md` with Spearman (M-003 0.929, M-004 0.972), dryrun/ with stdout and outputs for M-001, M-003, M-004.
- M-005 calibrate gap: disclosed in GUIDE.md, spread.md, dryrun/ (exit 2, `ratings.py` accepts M-001..M-004 only) and EVIDENCE C19. It is a real gap in the Metrics tool, honestly stated, so not a defect of this task.

| ID | Severity | Criterion | Text |
|---|---|---|---|
| DEF-T006-arch-1 | minor | C10 | `StyleFrameTiming.cs.meta` is untracked (git status `??`) while `StyleFrameTiming.cs` is committed. A clean checkout makes Unity generate a new GUID and a meta diff. Commit the meta. |
| DEF-T006-arch-2 | minor | Constraints | No tests for StyleFrame code (no Editor or EditMode tests, no `*.Tests` asmdef). Pure planning maths in `StyleFrameShots.cs` (prop placement, pose solving, on-grid snap) is testable engine-free. Reproducibility rests only on the two-run hash check. |
| DEF-T006-arch-3 | minor | C10 | Duplicated StyleCapture logic (AddCameras, OnGrid, timing probe copied from `StyleTiming`) will drift if StyleCapture changes. Module requests are recorded in the impl notes, but not yet raised in a module-request file or queue. Confirm they are filed. |
| DEF-T006-arch-4 | minor | C10 | `shot_params.json` `commit` field is the literal "placeholder". The criterion lists commit as a required field. EVIDENCE says the commit is in every sidecar, which is acceptable but not what C10 says. |
| DEF-T006-arch-5 | minor | C17 | EVIDENCE C17 states per-metric counts and walk-strip labelling were "not re-audited". `GUIDE.md` and spread.md say M-003 has two hero slots (M003-11, 12) reserved and empty, so M-003 shows 10 with the hero slots TODO. Fewer than 10 is stated, but the packet is not final until the hero is added. |
| DEF-T006-arch-6 | minor | C19 | The dry run was done in the T-006 worktree, not from a clean checkout, on copied set-level reports (not per-capture), as C19 requires a clean checkout. Disclosed in dryrun/README.md. M-005 dry run is a failure by design (exit 2). |
| DEF-T006-arch-7 | minor | C18 | M-003 intended order partly rests on author judgement, and M-005 has no deliberately bad end, so it does not span good to bad. Both are disclosed in spread.md. |
