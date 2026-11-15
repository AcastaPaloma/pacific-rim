# ReferenceGaitTests: 7 red of 109 (T-005 round 4)

- First bad commit: ed0e7be (T-005 fix cycle 3, introduced ReferenceGait and its tests). Re-run in a detached worktree at ed0e7be: the same 7 fail (5 EveryJoint_HasFiveDegreesToSpare, 2 YawMoment...AtCruiseAndAbove). Not a regression: 2beb69c and the round-3/4 data changes did not cause it.
- Cause: these are the tests marked `[Category("KnownGap")]` in ReferenceGaitTests.cs, "Kept red on purpose" (doc comments there; tasks/T-005.impl.md line 74: "7 red KnownGap tests"; research/R-008.md section 6 option A: "KnownGap tests kept").
- They test the legacy analytic ReferenceGait (imp=1 path, Make() = Repo.Walk() + mech_frame_balanced.json), not gait_reference_opt.json (imp=2 data). That file is never read by them.
- Spec violations are real and recorded, not test bugs: 5 deg margin fails on ankle dorsiflexion (-18.4 to -24.5 deg against a -15 deg margin, all five Froudes); yaw moment 3.03e8 (Fr 0.20) and 7.02e8 N m (Fr 0.30) against the 1.65e8 budget.
- Fix: none. No test, threshold, band or data edited (rules forbid lowering; tests are not reading wrong rows or fields).
- Recommendation: gates that need green should run `dotnet test DotnetTests/Units.Mech.Tests --filter "Category!=KnownGap"` (102 of 102 pass). The 7 stay as the documented gap register; they close only if the legacy reference is regenerated or retired.
