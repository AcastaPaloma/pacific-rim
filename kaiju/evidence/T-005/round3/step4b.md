8.4b DEF-T005r2-arch-2: imp=1 five-row stance schedule moved from ReferenceGait.cs to walk_tuning.json key imp_stance_schedule (named-field rows).
Contract: WalkTuning.imp_stance_schedule (StanceScheduleRow[]) in MechContracts.cs; ReferenceGait throws InvalidOperationException if missing or < 2 rows (no literal fallback).
Values copied verbatim; StanceScheduleConfigTests asserts loaded rows == old literals (exact) and that a missing key throws.
Also added one Compile line for the new test file to Units.Mech.Tests.csproj (explicit-include project).
dotnet test Units.Mech.Tests: 89 pass, 7 fail (the 7 known-gap reds, unchanged); 87 prior + 2 new.
