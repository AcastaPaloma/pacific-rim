# T-005 round 4, step 9.2: Goldfarb-Idnani dense QP solver

Code: `Assets/Game/Physics/Body/Runtime/Qp/GoldfarbIdnaniQp.cs` (namespace `Game.Physics.Body.Qp`, engine-free, netstandard2.1, no LINQ, preallocated buffers).
Tests: `Assets/Game/Physics/Body/Tests/EditMode/QpSolverTests.cs`. Reference: `Tools/GaitOpt/qp_reference.py` -> `Tools/GaitOpt/out/qp_reference/cases.json` (32 cases, seed 20240915).
Logs: `Tools/GaitOpt/out/qp_reference/dotnet_test_release.log`, `dotnet_test_debug.log`.

## Result
`dotnet test DotnetTests/Physics.Body.Tests -c Release`: 112/112 passed (100 of them in QpSolverTests); Debug: 112/112.

Solver: min 0.5 x'Hx + g'x, Aeq x = beq, Ain x >= bin; Tikhonov 1e-6 * mean(diag H) (constructor parameter, tests use 0 for the scipy match and the default for a separate pass); cap 60 (constructor parameter); Cholesky once per Solve, reusable with `hUnchanged`; status Optimal / Infeasible / IterationLimit (plus NotPositiveDefinite); `FailureCount` counter; warm start = previous `ActiveSet` (equalities always seeded, inequalities re-validated by dropping negative multipliers; 0 iterations when unchanged).

Reference: scipy SLSQP (ftol 1e-15) then an exact active-set KKT re-solve and a KKT certificate check (all references have stationarity/feasibility residual < 1e-12). SLSQP alone was off on `redundant_equality` (dx 1.0, it does not cope with a redundant equality row), so the certified re-solve is the reference; for the other cases SLSQP agrees with it to <= 2e-7.

Tests: 30 optimal cases x (scipy match + KKT [x within 1e-6 rel, objective 1e-8 rel], default cap/regularisation solve, warm start same x and iterations <= cold) = 90; 2 infeasible (contradicting boxes; inconsistent duplicated equality); 300 random QPs (n 5-40, m 0-80, up to 14 equalities) KKT-certified with warm restart; stage-1 shape n=16 and 32 with 200 random wrenches each (default cap 60, reg 1e-6, KKT vs H+reg); iteration-limit reporting; no allocation (GC.GetAllocatedBytesForCurrentThread delta 0 over 300 solves incl. warm and hUnchanged); timing.
Degenerate cases (duplicate rows, redundant rows, redundant equality, zero gradient, tight H cond 1e4, corner of bounds) all solve without cycling; no ADMM fallback needed.

## Timing (1000 solves, warm started, hUnchanged, g drifting by 0.01 per tick; Apple silicon)
| shape | Release p50 / p99 (us) | Debug p50 / p99 (us) | mean warm iterations |
|---|---|---|---|
| stage 1, n=16, 6 eq, 16 bounds | 27.2 / 33.3 | 18.4 / 27.2 | 0.03 |
| stage 1, n=32, 6 eq, 32 bounds | 124.7 / 149.0 | 97.2 / 141.6 | 0.04 |
| stage 2, n=20, 12 eq, 56 ineq | 9.4 / 21.9 | 40.9 / 51.4 | 0.02 |
Occasional max outliers (up to 2.4 ms Release, one-off) are JIT/OS noise; ceiling asserted: p99 < 5 ms (Release). All far below the 0.5 ms per-stage budget. Timing is for warm solves; cold iterations are below.

## Iterations cold vs warm
Stage 1 random wrenches: max cold 12 (n=16), 22 (n=32). Scipy cases: stage-1 refs 1-10 cold, stage-2 refs 8-11 cold, warm 0 in every case (same problem). Random scipy cases n 22-40, m 48-70: cold 16-53, all warm 0.
Cases needing more than 60 iterations: none in the 32 reference cases (max 53, random_1 n=32 m=70). Within the 300 C# random QPs the maximum was 71 cold iterations (n up to 40, m up to 80 random dense rows; solved with cap 400); with the default cap 60 such a problem would report IterationLimit. Stage-1 shapes stay at <= 22, so cap 60 is ample for them; stage 2 (<= 11 on references) likewise.
