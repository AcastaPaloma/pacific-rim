PASS

# M1 destruction and seismic: combined evaluator (arch + physics)
Head b7c7779. Read-only; no Unity run. `dotnet test` WorldDestruction.Tests 82/0/0, WorldSeismic.Tests 40/0/0; Python M-050..M-054 tests 58 passed.

## Scores (0-5)
| Criterion | Score | Note |
|---|---|---|
| Module boundaries | 5 | Seismic asmdef refs Core/Geo/Seismic contracts only; Destruction refs Seismic.Contracts (bridge on Destruction side); engine-free (noEngineReferences). |
| Flags default off | 5 | WorldDestruction.json / WorldSeismic.json false; Install and TryCreate return null and register nothing when off (bridge needs both). |
| Determinism | 5 | No DateTime/Random/Stopwatch hits; hash-drawn u from (BuildingId, eventIndex); seeded xorshift slip; bridge ordinal order, once per EventId. |
| Energy / debris accounting | 4 | E_frac capped 0.25 dPE, ejecta <=0.10, residual 2 % of remainder (in [0,5 %]); cumulative debris; heap clamp keeps dPE >= 0. Not run on real exported dumps (DEF-3). |
| Rupture geometry | 4 | Vertices are polyline vertices, trace never stretched, bridges excluded from mean slip and emergence. Per-vertex Slip[] nonzero at bridge ends (DEF-4). |
| Fragility chain vs R-021 | 5 | PGA->Sa(min(2.5,1.5/T))->Sd->drift/(0.75H)->Phi(ln/beta_tot 0.64); NaN/inf/zero PGA, H, T give 0; k bounds handled; monotone, never lowers state. |
| ModelRevision / golden | 4 | Destruction rev 2 matches golden `model_rev:2` and test; Seismic rev 1. Destruction MODULE.md stale (DEF-1). |
| Honest metric status | 3 | M-050..M-054 and M-005 v1.2 all `registered`, none claimed gated; but no run artifacts for M-050..054 (DEF-3). |

## Defects
- DEF-M1ds-1 (minor): `Assets/Game/World/Destruction/MODULE.md` says ModelRevision 1 and "39 tests", "Unity compile and EditMode run are not done", assemblies table omits SeismicShakingBridge / DestructionSaveParticipant / DestructionDumpExporter, and layer line says it references only Core/Geo contracts while the asmdef also refs Seismic.Contracts. Actual: rev 2 (DestructionContracts.cs:12), 82 tests, EditMode 468/0/2 (evidence/regression/M1-merge-6.md). PASS: doc matches code.
- DEF-M1ds-2 (minor): `Assets/Game/World/Seismic/MODULE.md` "References only Core/Geo contracts" is true for runtime, but the Seismic.Tests assembly references Destruction (stated in table); acceptable for an integration test, but a dedicated integration test assembly would keep the Seismic test asmdef clean. PASS: integration test lives outside Seismic.Tests or is explicitly accepted by the architect.
- DEF-M1ds-3 (minor): No run artifacts of M-050, M-051, M-052, M-053, M-054 exist under evidence/ (only T-035 M-005 json, T-036b editmode.xml); evidence/T-024*, T-027, T-036 directories absent. Tools are tested only by synthetic fixtures (58 pytest). PASS: one committed run per metric on exporter dumps (destruction_dump@v1, fault_rupture_dump@v1) with the verdict recorded as registered-not-gated or gated.
- DEF-M1ds-4 (minor): `RuptureSolver.Slip[]` holds nonzero values on vertices that touch a bridged segment (mean excludes bridge length, documented at RuptureSolver.cs "Bridged segments carry no slip"). A consumer reading per-vertex slip sees slip at bridge ends. PASS: per-segment slip or a documented/ tested guarantee that consumers must use MeanSlipM / segment flags.
- DEF-M1ds-5 (minor): `SeismicShakingBridge` applies damage at FaultRupture publication; S-wave delay absent (documented limit) and no composition root calls Install for either module (documented). PASS: tracked as open items in the M1 handoff; no change needed for M1 gate.

## Blocking
None found. Edge probes (by code reading): NaN/zero PGA and unknown building ids are no-ops; NaN energy ignored in Apply; invalid save data rejected before mutation; NaN hypocentre falls back to vertex 0 deterministically; duplicate EventId and unknown FaultId emit nothing.
