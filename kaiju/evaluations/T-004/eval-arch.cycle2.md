PASS

# T-004 eval-arch, cycle 2 re-check (task/T-004-wip @ e431b0a, diff from ebe049b)

Method: read the stat and code diffs, README and evidence sizes; ran `dotnet test` myself. Unity not run (orchestrator reports EditMode 175/175 at 4547e7a).
Runs: Rendering.Pixel.Tests 45/45, Camera.Tests 17/17 (matches report; +4 and +1 over cycle 1).

| Defect | Status | Evidence |
|---|---|---|
| arch-1 Pixel asmdef-reference test | Closed | `PixelArchitectureTests.PixelAsmdefs_ReferenceNoCameraUnitOrWorldAssembly_AndNoGuids`: reads every `Game.Rendering.Pixel*.asmdef`, fails on `"Game.Cameras`/`Camera`/`Units`/`World` and on `GUID:`; asserts at least 4 files found. Camera test gains the same `GUID:` guard (`CameraMathsTests.cs`). Runs under dotnet (Pixel total 41 to 45) |
| arch-2 validator / M-004 on shipped flag | Closed on my side (T-002 validator not in this diff) | `StyleCaptureJobs`: M-004 and C11 sets now use `baseFlags` (subPixelPresent on, as in `M0StyleFrame.json`); the doc comment records the varying per-frame offset. Whether the T-002 validator now accepts them is outside my read set; eval-metrics or the orchestrator should confirm `validate` on `m004_*` (see DEF-8) |
| arch-3 evidence size | Closed | `du`: captures 60 MB (was about 140), metrics 5.3 MB (was 52); `report.json` gzipped; rule (all frames of sets of at most 12, every 100th PNG and 10th sidecar otherwise) and regeneration order in `captures/README.md`; full 2.8 GB set stays git-ignored in `captures_local` and is bit-identically regenerable |
| arch-4 flag-file pins | Closed | `PixelFlagFile_AllFalse_AndMatchesPixelFlagsAll`, `CameraFlagFile_AllFalse_...` (Pixel project holds a literal copy of `CameraFlags.All` because that assembly is not compiled in; Camera.Tests has its own regex version). Both assert key set equal and every value false |
| arch-5 silent code-default fallback | Closed, weakly | Loaders warn per missing key: `PixelPipelineSettings.FromJson` and `ThirdPersonRigSettings.FromJson` (reflection over public fields vs raw JSON text), `PaletteProfileTuning` (`ExpectedDefaults` list, 24 keys, plus a warning when `defaults` is absent). Warn, not throw, which the PASS condition allows. No test exercises the warnings (DEF-9) |
| arch-6 StyleCapture README | Closed | `Assets/Game/Tools/StyleCapture/README.md` (61 lines, with `.meta`); `captures/README.md` points at its capture command; `StyleCaptureCli` doc now matches |
| arch-7 capture-run log | Closed | `captures/capture_log_summary.txt` ("log: 0 errors, 0 warnings", 445 s) and `capture_log_warnings.txt` (0 bytes: the warning grep is empty) |

## New code
- `hazeCurveGamma`: `HazeMath.Level(..., gamma = 1.0)`; `gamma == 1` takes the old log path, so the default is bit-identical to cycle 1. Test `HazeCurveGamma_OneIsTheLogCurve_BelowOneLiftsNearLevels` can fail (exact log curve at 1.0, strictly higher below 1, ends at 7.0 and starts at 0.0). Value flows config JSON to profile asset to shader (`_PixelHaze2.z`); no number in code or shader. Seam rework (`Band(level, bands, gradient, seamTexels, seamWidthMax, threshold)`) is covered by a tightened test (flat face gets no seam; only the two bands at the nearest boundary are used; exact boundary cases). Sound: guard on `halfWidth > 1e-5`, clamp to [0, bands-1].
- `PixelPipelineSettings`: `gradientDepthTolerance` added with config key; default `ditherAnchor` in code changed to "direction" to match config (removes a code/config mismatch).
- `StyleTiming.PassTimingCli`: async over editor frames, exits via `EditorApplication.Exit(0/1)`, exceptions logged and set exit 1, recorders disabled afterwards, `-quit` documented as forbidden, GPU-recorder support reported. Editor-only (`Assets/Game/Tools/StyleCapture/Editor`), no runtime reference. Has no test (DEF-9).
- `StyleCaptureJobs`: equal-size dark/light boxes, `control_continuous_tone` job (flag `controlContinuousTone` on the binder), shipped flags in M-004. Capture tooling only; no tuning in code.

## Boundaries and hygiene
- Rendering.Pixel asmdefs: no Camera, Units or World reference (the new test pins it). Tuning stays in `PixelPipeline.json`, `PaletteProfileTuning.json`, `ThirdPersonRig.json`; the new code adds keys, not literals.
- `research/R-002/palettes.json` and `Config/Rendering/palette_set.json`: not in the diff.
- Files changed outside the allowed folders (T-004 module folders, `Tools/Rendering/Pixel`, `Assets/Game/Tools/StyleCapture`, `Config/Rendering`, DotnetTests, `tasks/T-004*`, `evidence/T-004`): none. The `Camera` module (`Assets/Game/Camera`) is a T-004 module. `DotnetTests` has no diff (the projects link the Unity sources).

## New defects
- **DEF-T004-arch-8 (minor)** `evidence/T-004/EVIDENCE.md` line 72 still says "dotnet 41 + 16 ... EditMode 170 of 170" and conflicts with line 30 (175/175, 45, 17). PASS: line 72 updated or marked as cycle 1. Also confirm that the T-002 validator accepts the re-captured `m004_*` and `c11_subpixel_on` sets (arch-2 depends on it; its evidence is the metrics summary, not read here).
- **DEF-T004-arch-9 (minor)** No test for the new loud loaders or for `PassTimingCli`/`RunPassTiming` (the doc comment names a UnityTest callback, none exists). PASS: a test that loads a JSON with one key removed and asserts the warning (`LogAssert.Expect`), and a short-frame `RunPassTiming` smoke test, or a stated reason.

Verdict: no blocker or major open; the two open items are minor, so PASS.
