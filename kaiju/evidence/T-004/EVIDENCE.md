# T-004 evidence: pixel pipeline renderer features and third-person rig stabilisation

Branch `task/T-004`. All paths are relative to the project root (`kaiju/`). Thresholds are those of `research/R-002.md` (`M-003@v1`, `M-004@v1`) as registered in the T-002 tools; none was changed. Captures come straight from the engine's render targets (PNG encoding only); nothing was post-processed outside the engine.

## Fix cycle 2 (task/T-004-wip), final state
Everything under "Stored values" below is regenerated at the final state of this cycle: tuning 3849a5a plus the `hazeCurveGamma` code at its default 1.0 (43266c8). It replaces the cycle-1 tables and the first cycle-2 run. Text in "Results at a glance", "Criterion to artifact" and "Not met, and why (cycle 1)" is cycle-1 prose: where it disagrees with this section or with the tables, this section and the tables apply. Cycle-1 numbers of the M-004 `hybrid` run, the 300-frame wall-time table and the 41/16/170 test counts are superseded.

### Defect, change, evidence
| Defect | Change | Evidence |
|---|---|---|
| style-4: dither only in gradients | Design per the progress notes; dither confined to gradients | M-003 PASS, failing gating none; A2 1.0; DIG 0.158 to 0.383 over all 180 stills (`tasks/T-004.c1c2.md`, `tasks/T-004.c6c7.md`, tables below) |
| style-1: C6 seams | Tuning 3849a5a (`tasks/T-004.c6layers.md`): `hazeFullM` 20000 for all profiles, per-profile start, bands and ladder step in `PaletteProfileTuning.json` | Seam purity 1 on all five profiles; modal lightness never falls across a seam in 12 of 12 frames (`metrics/checks/c6_layers_*.json`) |
| style-6: modal lightness across seams | same tuning | 12 of 12 frames, all five profiles |
| C6 layers 1/3/10/30 km | same tuning (10 km and 30 km no longer saturate together) | Successively lighter in 12 of 12 frames, all five profiles; near field darkest in 12 of 12 |
| style-2: C7 | same tuning; `hazeCurveGamma` code kept at 1.0 (`tasks/T-004.hazeshape.md`) | Scale marker 5 of 5 profiles (1 px, dL* 22.6 to 33.8). Blocks at 150 m: pass on all five (dL* min 9.36 to 16.87). Blocks at 1 km (dL* at least 4, 9 views): pass for slate_teal and lavender_peach (9 of 9); **fail for warm_rust_sepia (6 of 9), warm_amber_haze (6 of 9), slate_crimson (1 of 9)**. The gain at 1 km cost C6 or the scale marker in two iterations (`tasks/T-004.hazeshape.md`); trade-off recorded and routed to research/founders |
| item 2: default anchoring | Default `direction` | M-004 below |
| item 7 / arch-2: M-004 sets | Captured with the sub-pixel compensation present on | M-004 PASS in the default `direction` mode and in the `m004_<profile>` and `m004direction_<profile>` sets: 12 of 12 gating sub-measures, failing gating none (cycle 1: 7 of 12 in `direction`, 8 of 12 in `hybrid`) |
| style-3: C11 per-depth smoothness | Check extended (b698897) | Not met (cycle-1 smoke numbers near 10.6 output px, reference 1.04, far 3.4, against 1 px): research/founders item. Fresh `c11_smooth` (reference depth): T1 max displacement error 1.0927 px, step error 0.7299 px; Y1 0.2686 px, 0.0073 px; compensation off 2.05/3.73 px (T1) and 3.27/3.00 px (Y1). Fresh per-depth (`metrics/checks/c11_smooth_T1.json`, sub-pixel on): near max 10.56 px (28 frames, 152 off-screen), reference 1.04 px, far 3.42 px; all fail ≤ 1 px |
| style-7: colour negative anchor | `control_continuous_tone` added | On `sheets/panel_board.png`, `captures/control_continuous_tone` |
| perf-1/-2/-3: per-pass cost | Per-pass `Recorder` timing at 1080p and 1440p, 1000 frames, 123 warm-up frames discarded, vsync 0 | `timing/pass_timing_summary.json`; table below. `supportsGpuRecorder` is false on Metal in batch mode: GPU ms per pass is not measurable; CPU ms per pass is reported with `Pixel.Present` isolated. GPU ms needs the target PC (perf-5) or a founder re-scope of C20 |
| perf-4: GC | Measured over 600 frames, capture off | 0 allocation samples and 0 collections at both resolutions (mono used delta 16 KB at 1080p, 112 KB at 1440p: heap growth without GC.Alloc samples, not investigated) |
| arch-1 / arch-4 | Asmdef and flag-file tests | af18ea4 |
| arch-5 | Loud tuning loaders | af18ea4 |
| arch-6 | README | 423eeb9 |
| arch-7 | Stored log warnings | `captures/capture_log_warnings.txt` |
| arch-3 | Slimmed evidence (DEF-T004-arch-3) | `evidence/T-004/captures/` 60 MB (cycle 1: 140.5 MB): all frames of sets of at most 12 frames, every 100th PNG and every 10th sidecar of long sets; `metrics/` 5.3 MB (cycle 1: 53.7 MB) with `report.json` gzipped. Full sets are regenerated bit-identically into `evidence/T-004/captures_local/` (git-ignored); see `captures/README.md` |

Validation (`metrics/validate/summary.txt`): 37 sets, 36 VALID; `orbit_slate_teal` INVALID (no `color_output` files; same in cycle 1; style-8 open).

Tests: Unity EditMode 175 of 175 pass; dotnet Rendering.Pixel 45 of 45, Camera 17 of 17.

Not done in cycle 2: style-8 (orbit in shipping mode, dolly path, cuts, A/B coverage), style-5, perf-5.

### Regeneration commands (from the project root; order as in the final run)
1. `unity run . ... -- -executeMethod Game.Tools.StyleCapture.StyleCaptureCli.InstallAll` (bakes assets and palette profiles from the tuning file).
2. `unity test . --mode EditMode`; `dotnet test` in `DotnetTests/Rendering.Pixel.Tests` and `DotnetTests/Camera.Tests`.
3. `unity run . --no-tail --log-file <log> --timeout 5400 -- -executeMethod Game.Tools.StyleCapture.StyleCaptureCli.Run -captureOut evidence/T-004/captures_local`
4. The same with `-captureJobs m004direction`.
5. Pass timing must use the Unity binary directly, without `-quit` (`unity run` adds `-quit`, which ends the run before the frame loop): `<Unity> -batchmode -projectPath <kaiju> -logFile <log> -executeMethod Game.Tools.StyleCapture.StyleTiming.PassTimingCli -timingOut evidence/T-004/timing -timingFrames 1000 -timingWarmup 120 -gcFrames 600`
6. `Tools/Rendering/Pixel/run_evidence.sh <integration>/kaiju/Tools/Metrics` (metrics, sheets; gzips `report.json`).
7. `/opt/miniconda3/envs/kaiju/bin/python Tools/Rendering/Pixel/evidence_tables.py evidence/T-004/metrics`
8. Slim `captures/` from `captures_local` (rule in `captures/README.md`).


## How the evidence was produced
| Step | Command (from the project root) | Commit |
|---|---|---|
| Pipeline assets, palette profiles, test scenes (Editor script) | `unity run . --no-tail --log-file <log> --timeout 900 -- -executeMethod Game.Tools.StyleCapture.StyleCaptureCli.Setup` | 343fdba |
| All capture sets (full output, git-ignored `evidence/T-004/captures_local/`) | `unity run . --no-tail --log-file <log> --timeout 5400 -- -executeMethod Game.Tools.StyleCapture.StyleCaptureCli.Run -captureOut evidence/T-004/captures_local` (batch mode, Metal, no `-nographics`; about 15 minutes) | 343fdba for the first run; stills, controls and dolly were recaptured at c35ed9b (`-captureJobs stills,controls,dolly`); each sidecar carries its commit in `build.git_commit` |
| M-004 sets with `direction` anchoring for all profiles | same, with `-captureJobs m004direction` (about 8 minutes) | 3f95664 |
| Second runs for bit-identity | same method with `-captureOut <scratch>/rerun -captureJobs stills,hazecal,cut,subpixel,stages -captureProfiles slate_teal -captureCommit 343fdbab64fe87446d903584746ab5cd38b48782`, and `-captureOut <scratch>/rerun2 -captureJobs stills,controls -captureProfiles slate_teal` at c35ed9b | same renderer code and assets |
| Frame timing | `unity run . --no-tail --log-file <log> --timeout 1800 -- -executeMethod Game.Tools.StyleCapture.StyleTiming.Run -timingOut evidence/T-004/timing -timingFrames 300` | 124dae8 (renderer code of 343fdba) |
| Metrics, checks, sheets | `Tools/Rendering/Pixel/run_evidence.sh <T-002 checkout>/kaiju/Tools/Metrics <scratch>/rerun <scratch>/rerun2` (reads `captures_local`; falls back to the committed `captures`) | T-002 tools at ef3b834 (branch `task/T-002`, registry v1.1); the `m004_direction` report at 927590a (`metrics/tool_versions.txt`) |
| Tables below | `/opt/miniconda3/envs/kaiju/bin/python Tools/Rendering/Pixel/evidence_tables.py evidence/T-004/metrics` | this commit |
| Unity EditMode tests | `unity test . --mode EditMode -o evidence/T-004/tests/unity_editmode.xml` | this commit |
| Engine-free tests | `dotnet test --logger trx` in `DotnetTests/Rendering.Pixel.Tests`, `DotnetTests/Camera.Tests`, `DotnetTests/Core.Tests` | this commit |

Metric tools: `M-003@v1` and `M-004@v1` are computed by the T-002 tools (`Tools/Metrics/metrics.py m003|m004|validate`, read-only from the T-002 worktree, branch `task/T-002`, commit ef3b834, registry v1.1; the later `m004_direction` run used 927590a). Checks those tools do not have (output smoothness M-011, camera cut, bit-identity, grid integrity at a declared non-zero offset, scale legibility, layers, depth decode) are computed by `Tools/Rendering/Pixel/pixel_checks.py` from the R-002 / reconciled-spec text.

`capture_log_summary.txt` in the capture folder records the Unity log of the capture run: every run logged 0 errors and 0 warnings.

## What is in git and what is not
The capture tool writes about 2.8 GB (mostly 1920×1080 `color_output` PNGs of the motion sequences) into `evidence/T-004/captures_local/`, which is git-ignored. The metrics were run on that full output. `evidence/T-004/captures/` (60 MB, committed) keeps all frames of every set of at most 12 frames, and for longer sets every 100th PNG and every 10th sidecar. Metric JSON is in `metrics/` (5.3 MB, `report.json` gzipped), contact sheets in `sheets/`.

To regenerate everything: run the capture command above (and the `m004direction` one), then `run_evidence.sh`. A second run is bit-identical to the first (C19), so regenerated frames reproduce the stored numbers.

## Results at a glance (cycle 1; superseded where the cycle-2 section differs)
- **M-003@v1** (T-002 tool, 180 stills + haze calibration): all eight measured gating sub-measures pass for all five profiles (a.output, a.internal, b.DIG, c1, c2, d-i, d-ii, d-iv); M-003.f (blind panel) is pending by design (milestone gate). Tool verdict `INCOMPLETE` (panel missing), headless `PASS (provisional: 2 assumed)`. Five advisory sub-measures miss (b.DIG_max, d-iii, e.sky, e.horizon, e.contrast): listed under "Not met".
- **M-004@v1** (T-002 tool, five profiles × two viewpoints, configured default anchoring `hybrid`): 8 of 12 gating sub-measures pass; tool verdict `FAIL`. Static: exactly 0. Translation: S_px, F and T3 pass; C_px.T12 misses by 0.0007 (0.1507 against 0.15, warm_rust_sepia street T1). Yaw: all three miss (S_px 0.0351, C_px 0.0505, F 0.0131 against 0.01 / 0.01 / 0.001).
- **M-004@v1 with `direction` anchoring** (same five profiles × two viewpoints): 7 of 12 pass; tool verdict `FAIL`. Static 0; yaw passes with margin (S_px 0.0013, C_px 0.0020, F 0.0001); translation misses (S_px.T12 0.1961, C_px.T12 0.3023, S_px.T3 0.1933, C_px.T3 0.2955, F.T3 0.0863; worst in the warm profiles at street level).
- **Anchoring and stabiliser A/B** (slate_teal, eye-height): `direction` 10 of 12, `hybrid` 9 of 12, `screen` 7 of 12; each stabiliser switched off makes its sequence worse (table below). No mode meets both the yaw and the translation thresholds. Per the reconciled spec ("How a miss on C9/C10 is handled") this goes to calibration; nothing was relaxed.
- Tests: `dotnet test` 41 + 16 (+ Core 89) pass; Unity EditMode 170 of 170 pass (54 Rendering.Pixel, 22 Camera, the rest Core).

## Criterion → artifact (cycle 1 values; see cycle 2 section and tables)
Capture sets are under `evidence/T-004/captures/`, metric outputs under `evidence/T-004/metrics/`, images under `evidence/T-004/sheets/`. "m003" = `metrics/m003/report.json` (+ `metrics/m003.txt`), produced by step "Metrics" above.

| C# | Result | Artifact and value |
|---|---|---|
| C1 Palette adherence | **Met** | m003: A₂ internal 1.0 (≥ 0.995) and A₂ output 1.0 (≥ 0.95), worst frame of 180, all five profiles (per-profile table below). `palette_index` holds 255 only where not quantised: validator passes on all sets; Unity test `QuantiseOff_IsNotOnPalette_AndIndexIs255`. Controls fail on the same tool: `metrics/controls/unquantised` (a.internal 0, a.output 0), `metrics/controls/bilinear` (a.output 0.8065). |
| C2 Grid and resolution | **Met** | m003 d-iv 1.0 (≥ 0.995). 1080p → 480×270, k = 4 (every sidecar); 1440p → 512×288, k = 5: `captures/m003_stills_1440p`, `metrics/m003_1440p` (d-iv 1.0); 4K reported: 640×360, k = 6, `metrics/m003_2160p`. Declared non-zero offset: `metrics/checks/c2_grid_declared_offset.json` (offset [3, 0]: 1.0; ignored: 0.7401). Bilinear control fails: 0.4486. Scale rule incl. 1366×768: dotnet test `ScaleRule_GivesIntegerScale`; Unity test `Output_IsAnExactIntegerPointUpscale`. |
| C3 Posterisation | **Met** (accent share reported, misses) | m003 d-i max 8 levels (≤ 12), d-ii max 2 hue families (≤ 3). Accent share (advisory): 0 to 0.0016 per frame; below 0.1 % in frames where the beacon is small or hidden. |
| C4 Dither in gradients | **Met** | m003 b.DIG: 180 of 180 frames ≥ 0.15 (min 0.2804). Individually: sky gradient views DIG 0.52–0.61, gradient-lit surface views 0.33–0.51 (`metrics/m003_details`, table below). Dither off: `metrics/controls/dither_off` b.DIG share 0 (FAIL). Advisory: DIG_max 0.6489 (> 0.5), median O 0.75 (Bayer). Interleaved transitions: `sheets/stage_ab_strip.png`, `palette_index` PNGs. |
| C5 Value by depth | **Met** | m003 c1: max drop 0.688 L* (≤ 2), min ρ 0.9643 (≥ 0.9), min span 17.54 L* (≥ 15). Bands extended to 30 km and by radial distance: `metrics/checks/c6_layers_*.json` (1, 3, 10, 30 km successively lighter in 12 of 12 frames per profile); dotnet tests `HazeBands_*`. c2 (`haze_cal`): PASS for all five profiles. Ramp uniformity (M-012): CV of the renderer's haze ramp ≤ 0.35 for all five, dotnet test `RepoPaletteSet_HasTheFiveProfiles_WithCanonicalHashes`. Controls fail: `metrics/controls/reversed_haze` (c1 drop 12.73 L*), `metrics/controls/haze_off` (c1 FAIL). |
| C6 Layers and sky | **Met, one partial** | `metrics/checks/c6_layers_<profile>.json`: layers at 1, 3, 10, 30 km successively lighter in all 60 frames; far layer within 0.8–6.3 L* of the sky at the horizon and its indices are a subset of the horizon sky's; near field (< 150 m) darkest in all frames. Ground seam pixels use only the indices of the two adjacent bands in 44 of 44 seams for four profiles; **slate_crimson 32 of 44** (one seam per frame shows a few pixels of the next lighter index). `sheets/palette_swatches_depth_ramps.png` shows the 10 m – 30 km ramp per profile. |
| C7 Scale legibility | **Partly met** | Marker (1.8 m at 595 m): 1 internal pixel, ΔL* 21.1–35.6 to its surround, its own palette index, all five profiles (`metrics/checks/c7_scale_*.json`; frames `captures/m003_details/<profile>_detail_scale`). 80 m block at 1.04 km is 32 px tall. Block against its background (`metrics/checks/c7_blocks_*.json`, twelve eye-height views): at 150 m ΔL* ≥ 8.5 in every view and profile; **at 1 km the 4 L* reading of "one value band" holds in 9/9 views for warm_amber_haze, 8/9 warm_rust_sepia, 5/9 slate_crimson, 3/9 slate_teal, 0/9 lavender_peach** (median ΔL* 6.5, 6.1, 4.2, 3.6, 2.4). M-003e values: per-profile table (advisory). |
| C8 Static | **Met** | `metrics/m004`: M-004.S0 = C_px.S0 = F.S0 = 0 exactly, ten S0 sequences. Control detected: `metrics/m004ab/control_frame_noise` S 0.1571, C 0.2856, F 0.0789. |
| C9 Yaw | **Not met** | `metrics/m004`: S_px.Y 0.0351, C_px.Y 0.0505, F.Y 0.0131 (limits 0.01 / 0.01 / 0.001) with `hybrid`. With `direction` on all five profiles: 0.0013 / 0.0020 / 0.0001 (`metrics/m004_direction`), which meets all three. |
| C10 Translation | **Partly met** | `metrics/m004` (`hybrid`): S_px.T12 0.0974, F.T12 0, S_px.T3 0.0858, C_px.T3 0.1303, F.T3 0.031: pass. C_px.T12 0.1507 (limit 0.15): miss. With `direction` on all five profiles (`metrics/m004_direction`): S_px.T12 0.1961, C_px.T12 0.3023, S_px.T3 0.1933, C_px.T3 0.2955, F.T3 0.0863: five misses. |
| C11 Smooth output | **Met on the snapped plane** | `metrics/checks/c11_smooth_T1.json`: with compensation the tracked edge deviates at most 0.99 px from the commanded motion, step error 0.69 px; without: 1.99 px and 3.69 px (fails). `c11_smooth_Y1.json`: 0.35 px / 0.06 px with; 3.35 px / 3.09 px without. Measured at the reference depth of the snap (the tracked tower edge); off that plane parallax moves edges by up to half a texel (R-003 §7). Rig test `FollowingAMovingTarget_KeepsItSteadyWithinOneInternalTexel`. |
| C12 Stabilisers pay | **Met** | A/B table below (`metrics/m004ab/*`): rotation quantise off: yaw S_px 0.0008 → 0.354 (`direction`), 0.021 → 0.131 (`hybrid`). Position snap off: F.T12 0 → 0.076 (`direction`), 0 → 0.032 (`hybrid`). Anchoring: `screen` yaw 0.350 against `direction` 0.0008; `direction` translation 0.104 against `hybrid` 0.040. |
| C13 Camera cut | **Met** | `metrics/checks/c13_cut.json`: frame after the cut identical to the static capture of the new pose in internal, output, index, depth, pose, offsets and dither origin; differs from the frame before. Tests `ACut_RendersTheWantedPosition_SoItEqualsAStaticFrame`, `ACut_RendersExactlyTheStaticPose_NoResidualState`. |
| Reported: band pop | Reported | `captures_local/dolly_slate_teal`, `metrics/m004_dolly`: the tool validates dolly sequences and gives no verdict or values, so no number is claimed here. |
| C14 Separate, toggleable stages | **Met** | One feature class per stage in `Assets/Game/Rendering/Pixel/Runtime/Pixel*Feature.cs`; flags all `false` in `Assets/Game/Config/FeatureFlags/Rendering.Pixel.json`; enabled by `Assets/Game/Config/Rendering/Configurations/M0StyleFrame.json`. `sheets/stage_ab_strip.png`, `sheets/stage_combinations.png` (16 combinations), `captures/stages_ab` (valid by the validator). Unity tests `EveryCombinationOfTheOptionalStages_RendersAValidOnPaletteFrame`, `EachStage_ChangesTheFrame_WhenToggled`. Quantise off → C1 fails, Haze off → C5 fails, Dither off → C4 fails: controls table. |
| C15 Reorderable, safely | **Met** | Unity tests `Installer_RejectsIllegalOrders_AndWritesNothing`, `LegalReorder_GivesTheIdenticalFrame_AndAnIllegalRuntimeOrderIsSkippedWithAWarning`, `Installer_KeepsTheFeatureListAndMapInStep_InTheDefaultOrder`; dotnet tests `DefaultOrder_IsLegal`, `IllegalOrders_AreRejectedWithAReason`, `RunTimeCheck_BlocksAStageThatArrivesTooLate` (`tests/unity_editmode.xml`, `tests/Rendering.Pixel.Tests.trx`). `PC_RPAsset` untouched: `pc_rpasset_nodiff.txt` (empty diff of `Assets/Settings`, `ProjectSettings`, `Packages` against the integration branch). |
| C16 Palettes are data | **Met** | Five assets in `Assets/Game/Config/Rendering/PaletteProfiles/` generated from `Assets/Game/Config/Rendering/palette_set.json` (verbatim copy of R-002). Tests `GeneratedProfiles_MatchThePaletteSet_AndTheirLutIsAFreshBake`, `SixthProfile_FromAJsonFixture_NeedsNoCodeOrShaderChange` (A₂ = 1.0), `EditingOneColour_AndRebaking_ChangesTheCapture_AndStaysOnTheEditedPalette`; dotnet `PaletteLutTests`, `ColourMathTests`, `DitherMaskTests`. SHA-256 in every sidecar (`style.palette_sha256`), checked by the validator. |
| C17 Rig contract | **Met** | `Assets/Game/Camera/`; tests `Rig_ImplementsTheContracts_AndFramesAnEightyMetreTarget`, `FollowingAMovingTarget_KeepsItSteadyWithinOneInternalTexel`, `WithoutAGrid_TheRigOffersNoHints_*`, `Bind_WiresFlagsBusAndRegistry_ThroughCoreContractsOnly`, `Camera_ReferencesNoUnitOrMechCode_AndPixelOnlyThroughContracts`; Rendering.Pixel asmdefs list no Camera, Units or World assembly (`Assets/Game/Rendering/Pixel/*/*.asmdef`). No hints: every `PixelPipelineTests` frame is rendered without a rig and is on palette. |
| C18 Footfall impulse | **Met** | Tests `FootfallEvent_OnTheCoreBus_MovesTheCamera_InProportion_Bounded_AndDecaysBack`, `CameraImpulseTests` (proportional, bounded, zero after the decay, reproducible, step-size independent). 20 s orbit with one `CameraShakeImpulse` (3e7 N·s at frame 480): `captures/orbit_slate_teal/orbit_20s` (sidecars of all 1200 frames, every tenth internal frame; all frames in `captures_local`), `sheets/orbit_slate_teal.png`. Framing returns to within 0.05 m (1 texel = 1.44 m at 600 m) and is then static. |
| C19 Capture tool | **Met, with one contract note** | One CLI run wrote every set with 0 errors and 0 warnings (`captures/capture_log_summary.txt`). Validator: `metrics/validate/summary.txt`: valid for 33 of 36 sets: all except three captured with sub-pixel compensation on, where the T-002 validator requires one `resolution` block per sequence (`c11_subpixel_on`, `cut_check`, `orbit_slate_teal`; see "Not met"). Bit-identical second runs: `metrics/checks/c19_identical_*.json` (2285 PNG and 490 JSON files compared, 0 differ). Depth: `metrics/checks/c19_depth.json`, five known points within 0.012 %. No TAA, bloom, motion blur or post-processing (sidecar `build.feature_flags`). |
| C20 Render cost | **Partly met (relative, Mac only)** | `timing/timing_summary.json`, `timing/frame_timing_*.csv`. Dither + Haze + Quantise together: mean 0.25 ms, p95 0.52 ms at 1080p (limit 0.5: over by 0.02 ms on a wall-clock paired difference); mean 0.20, p95 0.40 ms at 1440p. Present (the whole second camera, including the render-request copy used for capture): p95 0.74 ms at 1080p, 0.60 ms at 1440p (limit 0.5: miss). GC.Alloc samples over 100 frames: 0. M-008 is **not verified**: Apple M5 Pro, Metal; no target-class Windows hardware. |
| C21 Module hygiene | **Met** | `Assets/Game/Rendering/Pixel/MODULE.md`, `Assets/Game/Camera/MODULE.md`; tuning in `Assets/Game/Config/Rendering/*.json`, `Config/Camera/ThirdPersonRig.json` and the profile assets; test totals above; capture log clean. No name, shape or text from the references: the scene is boxes, one cylinder and one sphere (`Assets/Game/Tools/StyleCapture/Editor/StyleTestScenes.cs`). `Assets/Game/Core/Unity/Resources/GameCoreConfig.asset` was regenerated by Core's own generator because two flag files were added (Core test `Adapter_ConfigAsset_PointsAtCommittedFiles` requires it). |
| Milestone: M-003f panel | Pending (founders) | Board for the panel: `sheets/panel_board.png` (five reference images, ten captures, two negative anchors). No agent score is claimed here. |

## Not met after fix cycle 2
1. **C7 at 1 km, three profiles:** warm_rust_sepia 6 of 9, warm_amber_haze 6 of 9, slate_crimson 1 of 9 views with dL* at least 4. Trade-off with C6 (`tasks/T-004.hazeshape.md`); research/founders: a haze curve defined in L* rather than log distance, or a per-profile 1 km bound.
2. **C11 per-depth smoothness (style-3):** per depth class on T1 (fresh): near 10.56, reference 1.04, far 3.42 output px against 1 px; Y1 reference 0.27 px. Research/founders.
3. **style-5** and **style-8** (orbit in shipping mode, dolly path, cuts, A/B coverage; `orbit_slate_teal` INVALID).
4. **perf-1/-2 GPU ms per pass** (not measurable on Metal batch) and **perf-5** (target PC, M-008).
5. M-003 advisories (DIG_max, accent share, sky share, horizon, contrast) and M-003.f (blind panel, founders) as in the tables; M-004 has no remaining miss.

## Not met, and why (cycle 1; superseded by the list above)
1. **C9 yaw (M-004.S_px.Y, C_px.Y, F.Y) with the configured default `hybrid`.** Achieved 0.0351 / 0.0505 / 0.0131 against 0.01 / 0.01 / 0.001. The surface-anchored pattern near the camera is resampled at a different sub-texel phase after each rotation step away from the screen centre. `direction` meets all three on every profile (worst 0.0013 / 0.0020 / 0.0001).
2. **C10 translation crawl.** `hybrid`: C_px.T12 0.1507 against 0.15 (one sequence, warm_rust_sepia street T1; the other nine profile-viewpoints are 0.062–0.096). `direction`: S_px up to 0.196 and C_px up to 0.302 over the five profiles, because a screen pattern cannot follow parallax. No mode meets yaw and translation together; A/B numbers for every mode and stabiliser are stored. Thresholds were not changed (they are Assumed pending calibration, R-002 §5).
3. **C7 at 1 km for three profiles.** One haze band of separation is there (the block and the ground and forms behind it are one or two bands apart), but with `ditherSpread` 0.4 neighbouring bands often land on the same palette colour in slate_teal, lavender_peach and slate_crimson, whose palettes have few mid-value entries, so ΔL* falls below 4. Raising `ditherSpread` restores the mean value step and raises DIG_max further; left for the style panel.
4. **C6 seam indices in slate_crimson** (32 of 44 ground seams): a few seam pixels carry the next lighter index.
5. **C20.** Present p95 above 0.5 ms as measured (0.74 / 0.60 ms): the number includes the full second camera and the capture path's copy, not only the present pass. Stage chain p95 0.52 ms at 1080p. Per-pass GPU markers could not be read in a batch run (the GPU recorder needs a player loop), so costs are paired wall-time differences. M-008 unverified.
6. **M-003 advisories:** DIG_max 0.6489 (> 0.5: Bayer with a 0.75-band haze seam and spread 0.4 dithers much of the ground and sky); accent share below 0.1 % in frames where the beacon is small; sky share min 0.178 (< 0.2) and horizon max 0.474 (> 0.45) in the elevated views; frame contrast min 12.7 (< 25; slate_crimson's whole ramp spans 30 L*).
7. **Validator and sub-pixel compensation.** The T-002 validator rejects sequences whose frames differ in `resolution.output_block_offset_px` / `subpixel_offset_internal_px`; R-002 §6 defines them per frame. The M-004 sets were therefore captured with `rendering.pixel.subPixelPresent` off (applied offset zero). The internal image, index and depth do not depend on that flag; the compensated output is in `c11_subpixel_on` (invalid by that validator rule only). Proposed as a contract question in `tasks/T-004.impl.md`.
8. **Not built:** cockpit rig, quantisation hysteresis, light shafts and smoke (later slices); no PlayMode test (EditMode capture tests and rig tests cover the behaviour); the blind panel is the founders' gate.

## Stored values (cycle 2, final)
### M-003@v1 on the 180 stills (5 profiles × 3 viewpoints × 12 frames) and the haze calibration set

**M-003@v1, `m003_stills` + `haze_cal`** — verdict `PASS`, registry v1.1 (sha256 7a63afd06c66), failing gating: none

| Sub-measure | Kind | Value | Rule | Status | Decisive frame / sequence |
|---|---|---|---|---|---|
| M-003.a.output | gating | 1 share | >= 0.95 | PASS | A_2 output worst frame m003_stills/lavender_peach_elevated300/f000000 |
| M-003.a.internal | gating | 1 share | >= 0.995 | PASS | A_2 internal worst frame m003_stills/lavender_peach_elevated300/f000000 |
| M-003.a.A3 | info | 1 share |  - | REPORTED |  |
| M-003.a.A5 | info | 1 share |  - | REPORTED |  |
| M-003.b.DIG | gating | 1 share of frames | >= 0.8 | PASS | lavender_peach: share of evaluated frames with DIG >= dig_min |
| M-003.b.DIG_max | advisory | 0.3828 DIG | <= 0.5 | PASS | max DIG m003_stills/slate_crimson_eye70/f000002 |
| M-003.b.O | advisory | 1.099 O |  - | REPORTED |  |
| M-003.c1 | gating | 0.9643 rho | >= 0.9 | PASS | m003_stills/slate_teal_street/f000003: Spearman rho(band, L*) |
| M-003.c2 | gating | 0 L* | <= 0 | PASS | haze_cal/lavender_peach/f000000: max drop of pair mean L* |
| M-003.d-i | gating | 8 levels | <= 12 | PASS | value levels m003_stills/warm_amber_haze_eye70/f000003 |
| M-003.d-ii | gating | 2 families | <= 3 | PASS | hue families m003_stills/lavender_peach_elevated300/f000000 |
| M-003.d-iii | advisory | 0.0006 share | in 0.001 | FAIL | accent share m003_stills/lavender_peach_elevated300/f000009 |
| M-003.d-iv | gating | 1 share of blocks | >= 0.995 | PASS | uniform blocks m003_stills/lavender_peach_elevated300/f000000 |
| M-003.e.sky | advisory | 0.1778  | in 0.2 | FAIL | sky_share m003_stills/lavender_peach_street/f000005 |
| M-003.e.horizon | advisory | 0.4741  | <= 0.45 | FAIL | horizon m003_stills/lavender_peach_elevated300/f000000 |
| M-003.e.contrast | advisory | 21.7308  | >= 25 | FAIL | contrast m003_stills/slate_teal_eye70/f000000 |
| M-003.f | gating | -  |  - | PENDING | no panel_ratings@v1 file given (the tool never defaults a score) |

| Profile | Frames | a: min A₂ internal | a: min A₂ output | b: frames DIG ≥ 0.15 / evaluated | b: DIG min / max | b: median O | c1: max drop L* | c1: min ρ | c1: min span L* | d-i: max levels | d-ii: max hue families | d-iii: accent share min / max | d-iv: min uniform blocks | e: sky share min / max | e: horizon max | e: contrast min |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| lavender_peach | 36 | 1 | 1 | 36 / 36 | 0.2236 / 0.3163 | 1.3068 | 0 | 1 | 30.206 | 7 | 2 | 0 / 0.0016 | 1 | 0.1778 / 0.4837 | 0.4741 | 26.5601 |
| slate_crimson | 36 | 1 | 1 | 36 / 36 | 0.2026 / 0.3828 | 1.2793 | 0 | 0.991 | 22.2197 | 5 | 2 | 0 / 0.0016 | 1 | 0.1778 / 0.4837 | 0.4741 | 22.5531 |
| slate_teal | 36 | 1 | 1 | 36 / 36 | 0.198 / 0.3535 | 1.0518 | 0.7171 | 0.9643 | 26.2966 | 6 | 2 | no accent | 1 | 0.1778 / 0.4837 | 0.4741 | 21.7308 |
| warm_amber_haze | 36 | 1 | 1 | 36 / 36 | 0.1582 / 0.2881 | 1.0382 | 0.0417 | 0.9643 | 37.7984 | 8 | 1 | 0 / 0.0016 | 1 | 0.1778 / 0.4837 | 0.4741 | 23.9371 |
| warm_rust_sepia | 36 | 1 | 1 | 36 / 36 | 0.2052 / 0.3685 | 1.0484 | 0.9407 | 0.9643 | 31.5217 | 7 | 1 | 0 / 0.0016 | 1 | 0.1778 / 0.4837 | 0.4741 | 22.0367 |

### M-003@v1, detail views (sky gradient, gradient-lit surface, scale view), 3 frames per profile: fewer than the 12 the metric requires, so the tool's verdict is about coverage; the values are what C4 asks for individually

**m003_details** — verdict `INCOMPLETE`, registry v1.1 (sha256 7a63afd06c66), failing gating: none

| Sub-measure | Kind | Value | Rule | Status | Decisive frame / sequence |
|---|---|---|---|---|---|
| M-003.a.output | gating | 1 share | >= 0.95 | PASS | A_2 output worst frame m003_details/lavender_peach_detail_gradient/f000000 |
| M-003.a.internal | gating | 1 share | >= 0.995 | PASS | A_2 internal worst frame m003_details/lavender_peach_detail_gradient/f000000 |
| M-003.a.A3 | info | 1 share |  - | REPORTED |  |
| M-003.a.A5 | info | 1 share |  - | REPORTED |  |
| M-003.b.DIG | gating | 1 share of frames | >= 0.8 | PASS | lavender_peach: share of evaluated frames with DIG >= dig_min |
| M-003.b.DIG_max | advisory | 0.465 DIG | <= 0.5 | PASS | max DIG m003_details/warm_rust_sepia_detail_gradient/f000000 |
| M-003.b.O | advisory | 1.1301 O |  - | REPORTED |  |
| M-003.c1 | gating | 1 rho | >= 0.9 | PASS | m003_details/lavender_peach_detail_sky/f000000: Spearman rho(band, L*) |
| M-003.c2 | gating | 0 L* | <= 0 | PASS | haze_cal/lavender_peach/f000000: max drop of pair mean L* |
| M-003.d-i | gating | 8 levels | <= 12 | PASS | value levels m003_details/warm_amber_haze_detail_scale/f000000 |
| M-003.d-ii | gating | 2 families | <= 3 | PASS | hue families m003_details/lavender_peach_detail_sky/f000000 |
| M-003.d-iii | advisory | 0.0013 share | in 0.001 | PASS | accent share m003_details/lavender_peach_detail_scale/f000000 |
| M-003.d-iv | gating | 1 share of blocks | >= 0.995 | PASS | uniform blocks m003_details/lavender_peach_detail_gradient/f000000 |
| M-003.e.sky | advisory | 0.8314  | in 0.2 | FAIL | sky_share m003_details/lavender_peach_detail_sky/f000000 |
| M-003.e.horizon | advisory | 0.4444  | <= 0.45 | PASS | horizon m003_details/lavender_peach_detail_gradient/f000000 |
| M-003.e.contrast | advisory | 17.4388  | >= 25 | FAIL | contrast m003_details/slate_crimson_detail_gradient/f000000 |
| M-003.f | gating | -  |  - | PENDING | no panel_ratings@v1 file given (the tool never defaults a score) |

| Frame | DIG | O | gradient share | A₂ internal | grid uniform |
|---|---|---|---|---|---|
| m003_details/lavender_peach_detail_gradient/f000000 | 0.3272 | 1.3096 | 0.1451 | 1 | 1 |
| m003_details/lavender_peach_detail_scale/f000000 | 0.2714 | 1.1317 | 0.2456 | 1 | 1 |
| m003_details/lavender_peach_detail_sky/f000000 | 0.4128 | 1.2465 | 0.2386 | 1 | 1 |
| m003_details/slate_crimson_detail_gradient/f000000 | 0.3187 | 1.1489 | 0.1268 | 1 | 1 |
| m003_details/slate_crimson_detail_scale/f000000 | 0.2641 | 1.1301 | 0.2074 | 1 | 1 |
| m003_details/slate_crimson_detail_sky/f000000 | 0.3646 | 1.381 | 0.2043 | 1 | 1 |
| m003_details/slate_teal_detail_gradient/f000000 | 0.4435 | 1.0463 | 0.218 | 1 | 1 |
| m003_details/slate_teal_detail_scale/f000000 | 0.3303 | 0.9853 | 0.2741 | 1 | 1 |
| m003_details/slate_teal_detail_sky/f000000 | 0.3906 | 1.228 | 0.2232 | 1 | 1 |
| m003_details/warm_amber_haze_detail_gradient/f000000 | 0.2369 | 0.8824 | 0.1461 | 1 | 1 |
| m003_details/warm_amber_haze_detail_scale/f000000 | 0.1825 | 0.613 | 0.2354 | 1 | 1 |
| m003_details/warm_amber_haze_detail_sky/f000000 | 0.3557 | 1.0522 | 0.2055 | 1 | 1 |
| m003_details/warm_rust_sepia_detail_gradient/f000000 | 0.465 | 0.891 | 0.1781 | 1 | 1 |
| m003_details/warm_rust_sepia_detail_scale/f000000 | 0.2594 | 0.9104 | 0.2622 | 1 | 1 |
| m003_details/warm_rust_sepia_detail_sky/f000000 | 0.3647 | 1.2535 | 0.2082 | 1 | 1 |

### M-003@v1, 2560×1440 output (k = 5, 512×288 internal), one frame per profile

**m003_1440p** — verdict `INCOMPLETE`, registry v1.1 (sha256 7a63afd06c66), failing gating: none

| Sub-measure | Kind | Value | Rule | Status | Decisive frame / sequence |
|---|---|---|---|---|---|
| M-003.a.output | gating | 1 share | >= 0.95 | PASS | A_2 output worst frame m003_stills_1440p/lavender_peach_eye70/f000000 |
| M-003.a.internal | gating | 1 share | >= 0.995 | PASS | A_2 internal worst frame m003_stills_1440p/lavender_peach_eye70/f000000 |
| M-003.a.A3 | info | 1 share |  - | REPORTED |  |
| M-003.a.A5 | info | 1 share |  - | REPORTED |  |
| M-003.b.DIG | gating | 1 share of frames | >= 0.8 | PASS | lavender_peach: share of evaluated frames with DIG >= dig_min |
| M-003.b.DIG_max | advisory | 0.3088 DIG | <= 0.5 | PASS | max DIG m003_stills_1440p/warm_rust_sepia_eye70/f000000 |
| M-003.b.O | advisory | 1.0372 O |  - | REPORTED |  |
| M-003.c1 | gating | 1 rho | >= 0.9 | PASS | m003_stills_1440p/lavender_peach_eye70/f000000: Spearman rho(band, L*) |
| M-003.c2 | gating | 0 L* | <= 0 | PASS | haze_cal/lavender_peach/f000000: max drop of pair mean L* |
| M-003.d-i | gating | 7 levels | <= 12 | PASS | value levels m003_stills_1440p/warm_amber_haze_eye70/f000000 |
| M-003.d-ii | gating | 1 families | <= 3 | PASS | hue families m003_stills_1440p/lavender_peach_eye70/f000000 |
| M-003.d-iii | advisory | -  |  - | NOT_APPLICABLE | no accent present in any frame (applies only when present) |
| M-003.d-iv | gating | 1 share of blocks | >= 0.995 | PASS | uniform blocks m003_stills_1440p/lavender_peach_eye70/f000000 |
| M-003.e.sky | advisory | 0.3794  | in 0.2 | PASS | sky_share m003_stills_1440p/lavender_peach_eye70/f000000 |
| M-003.e.horizon | advisory | 0.4201  | <= 0.45 | PASS | horizon m003_stills_1440p/lavender_peach_eye70/f000000 |
| M-003.e.contrast | advisory | 21.7308  | >= 25 | FAIL | contrast m003_stills_1440p/slate_teal_eye70/f000000 |
| M-003.f | gating | -  |  - | PENDING | no panel_ratings@v1 file given (the tool never defaults a score) |

### M-003@v1, 3840×2160 output (k = 6, 640×360 internal), slate_teal, reported only

**m003_2160p** — verdict `INCOMPLETE`, registry v1.1 (sha256 7a63afd06c66), failing gating: none

| Sub-measure | Kind | Value | Rule | Status | Decisive frame / sequence |
|---|---|---|---|---|---|
| M-003.a.output | gating | 1 share | >= 0.95 | PASS | A_2 output worst frame m003_stills_2160p/slate_teal_eye70/f000000 |
| M-003.a.internal | gating | 1 share | >= 0.995 | PASS | A_2 internal worst frame m003_stills_2160p/slate_teal_eye70/f000000 |
| M-003.a.A3 | info | 1 share |  - | REPORTED |  |
| M-003.a.A5 | info | 1 share |  - | REPORTED |  |
| M-003.b.DIG | gating | 1 share of frames | >= 0.8 | PASS | slate_teal: share of evaluated frames with DIG >= dig_min |
| M-003.b.DIG_max | advisory | 0.3051 DIG | <= 0.5 | PASS | max DIG m003_stills_2160p/slate_teal_eye70/f000000 |
| M-003.b.O | advisory | 1.0273 O |  - | REPORTED |  |
| M-003.c1 | gating | 1 rho | >= 0.9 | PASS | m003_stills_2160p/slate_teal_eye70/f000000: Spearman rho(band, L*) |
| M-003.c2 | gating | 0 L* | <= 0 | PASS | haze_cal/lavender_peach/f000000: max drop of pair mean L* |
| M-003.d-i | gating | 6 levels | <= 12 | PASS | value levels m003_stills_2160p/slate_teal_eye70/f000000 |
| M-003.d-ii | gating | 1 families | <= 3 | PASS | hue families m003_stills_2160p/slate_teal_eye70/f000000 |
| M-003.d-iii | advisory | -  |  - | NOT_APPLICABLE | no accent present in any frame (applies only when present) |
| M-003.d-iv | gating | 1 share of blocks | >= 0.995 | PASS | uniform blocks m003_stills_2160p/slate_teal_eye70/f000000 |
| M-003.e.sky | advisory | 0.3791  | in 0.2 | PASS | sky_share m003_stills_2160p/slate_teal_eye70/f000000 |
| M-003.e.horizon | advisory | 0.4194  | <= 0.45 | PASS | horizon m003_stills_2160p/slate_teal_eye70/f000000 |
| M-003.e.contrast | advisory | 21.7308  | >= 25 | FAIL | contrast m003_stills_2160p/slate_teal_eye70/f000000 |
| M-003.f | gating | -  |  - | PENDING | no panel_ratings@v1 file given (the tool never defaults a score) |

### Negative controls on the same tool (slate_teal, eye70, 12 frames each): the tests can fail

| Control | Verdict | Failing gating sub-measures | a.internal | a.output | b.DIG share | c1 value | d-iv |
|---|---|---|---|---|---|---|---|
| bilinear | FAIL | M-003.a.output, M-003.d-iv | 1 (PASS) | 0.8499 (FAIL) | 1 (PASS) | 1 (PASS) | 0.6954 (FAIL) |
| dither_off | FAIL | M-003.b.DIG | 1 (PASS) | 1 (PASS) | 0 (FAIL) | 0.9856 (PASS) | 1 (PASS) |
| haze_off | FAIL | M-003.b.DIG, M-003.c1 | 1 (PASS) | 1 (PASS) | 0 (FAIL) | -0.1715 (FAIL) | 1 (PASS) |
| reversed_haze | FAIL | M-003.c1 | 1 (PASS) | 1 (PASS) | 1 (PASS) | 11.7108 (FAIL) | 1 (PASS) |
| subpixel_offset | INCOMPLETE | none | 1 (PASS) | 1 (PASS) | 1 (PASS) | 1 (PASS) | 1 (PASS) |
| unquantised | FAIL | M-003.a.output, M-003.a.internal, M-003.b.DIG | 0 (FAIL) | 0 (FAIL) | 0 (FAIL) | 1 (PASS) | 1 (PASS) |

### M-004@v1, five profiles × two viewpoints, configured default anchoring (`direction`; the default changed in this cycle, so this repeats the `m004direction` run), position snap and rotation quantisation on

**M-004@v1, `m004_<profile>`** — verdict `PASS`, registry v1.1 (sha256 5f39e5531f9e), failing gating: none

| Sub-measure | Kind | Value | Rule | Status | Decisive frame / sequence |
|---|---|---|---|---|---|
| M-004.S0 | gating | 0 share | <= 0 | PASS | eye70_S0 (S0, warm_rust_sepia/eye70, lag 1) |
| M-004.C_px.S0 | gating | 0 share | <= 0 | PASS | eye70_S0 (S0, warm_rust_sepia/eye70, lag 1) |
| M-004.F.S0 | gating | 0 share | <= 0 | PASS | eye70_S0 (S0, warm_rust_sepia/eye70, lag 1) |
| M-004.S_px.Y | gating | 0.002 share | <= 0.01 | PASS | eye70_Y1 (Y1, warm_rust_sepia/eye70, lag 4) |
| M-004.C_px.Y | gating | 0.0051 share | <= 0.01 | PASS | eye70_Y1 (Y1, warm_rust_sepia/eye70, lag 4) |
| M-004.F.Y | gating | 0.0002 share | <= 0.001 | PASS | eye70_Y2 (Y2, warm_rust_sepia/eye70, lag 1) |
| M-004.S_px.T12 | gating | 0.0276 share | <= 0.12 | PASS | eye70_T1 (T1, warm_rust_sepia/eye70, lag 10) |
| M-004.C_px.T12 | gating | 0.0871 share | <= 0.15 | PASS | eye70_T1 (T1, warm_rust_sepia/eye70, lag 10) |
| M-004.F.T12 | gating | 0 share | <= 0.005 | PASS | eye70_T1 (T1, warm_rust_sepia/eye70, lag 10) |
| M-004.S_px.T3 | gating | 0.025 share | <= 0.12 | PASS | eye70_T3 (T3, warm_amber_haze/eye70, lag 1) |
| M-004.C_px.T3 | gating | 0.1025 share | <= 0.15 | PASS | eye70_T3 (T3, warm_amber_haze/eye70, lag 1) |
| M-004.F.T3 | gating | 0.0168 share | <= 0.08 | PASS | eye70_T3 (T3, warm_amber_haze/eye70, lag 1) |

| Profile | Viewpoint | Sequence | Lag | S_px | C_px | F | Valid share | S pair p95 |
|---|---|---|---|---|---|---|---|---|
| lavender_peach | eye70 | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| lavender_peach | eye70 | T1 | 10 | 0.0149 | 0.0738 | 0 | 0.9931 | 0.0158 |
| lavender_peach | eye70 | T2 | 4 | 0.0161 | 0.0793 | 0 | 0.9934 | 0.0201 |
| lavender_peach | eye70 | T3 | 1 | 0.021 | 0.1017 | 0.014 | 0.9956 | 0.0228 |
| lavender_peach | eye70 | Y1 | 4 | 0.001 | 0.0031 | 0 | 0.997 | 0.0011 |
| lavender_peach | eye70 | Y2 | 1 | 0.0008 | 0.0022 | 0.0001 | 0.9972 | 0.001 |
| lavender_peach | street | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| lavender_peach | street | T1 | 10 | 0.0027 | 0.0043 | 0 | 0.9327 | 0.0038 |
| lavender_peach | street | T2 | 4 | 0.0041 | 0.0116 | 0 | 0.949 | 0.0115 |
| lavender_peach | street | T3 | 1 | 0.0027 | 0.0052 | 0.0016 | 0.9643 | 0.005 |
| lavender_peach | street | Y1 | 4 | 0.0009 | 0.0032 | 0 | 0.997 | 0.001 |
| lavender_peach | street | Y2 | 1 | 0.0007 | 0.0024 | 0.0001 | 0.9972 | 0.0009 |
| slate_crimson | eye70 | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| slate_crimson | eye70 | T1 | 10 | 0.0096 | 0.0382 | 0 | 0.9931 | 0.0106 |
| slate_crimson | eye70 | T2 | 4 | 0.0119 | 0.0491 | 0 | 0.9934 | 0.0273 |
| slate_crimson | eye70 | T3 | 1 | 0.0128 | 0.0522 | 0.0039 | 0.9956 | 0.0172 |
| slate_crimson | eye70 | Y1 | 4 | 0.0008 | 0.0024 | 0 | 0.997 | 0.0008 |
| slate_crimson | eye70 | Y2 | 1 | 0.0005 | 0.0014 | 0.0001 | 0.9972 | 0.0007 |
| slate_crimson | street | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| slate_crimson | street | T1 | 10 | 0.0027 | 0.008 | 0 | 0.9327 | 0.004 |
| slate_crimson | street | T2 | 4 | 0.0039 | 0.0155 | 0 | 0.949 | 0.0122 |
| slate_crimson | street | T3 | 1 | 0.0029 | 0.0061 | 0.0014 | 0.9643 | 0.0074 |
| slate_crimson | street | Y1 | 4 | 0.0006 | 0.0034 | 0 | 0.997 | 0.0008 |
| slate_crimson | street | Y2 | 1 | 0.0004 | 0.0022 | 0 | 0.9972 | 0.0007 |
| slate_teal | eye70 | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| slate_teal | eye70 | T1 | 10 | 0.0202 | 0.0581 | 0 | 0.9931 | 0.021 |
| slate_teal | eye70 | T2 | 4 | 0.0203 | 0.0579 | 0 | 0.9934 | 0.0249 |
| slate_teal | eye70 | T3 | 1 | 0.0213 | 0.0666 | 0.0103 | 0.9956 | 0.024 |
| slate_teal | eye70 | Y1 | 4 | 0.0012 | 0.002 | 0 | 0.997 | 0.0013 |
| slate_teal | eye70 | Y2 | 1 | 0.0008 | 0.0014 | 0.0001 | 0.9972 | 0.0013 |
| slate_teal | street | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| slate_teal | street | T1 | 10 | 0.0069 | 0.0118 | 0 | 0.9327 | 0.0098 |
| slate_teal | street | T2 | 4 | 0.0072 | 0.0146 | 0 | 0.949 | 0.01 |
| slate_teal | street | T3 | 1 | 0.0035 | 0.0049 | 0.0013 | 0.9643 | 0.0096 |
| slate_teal | street | Y1 | 4 | 0.0008 | 0.0025 | 0 | 0.997 | 0.0009 |
| slate_teal | street | Y2 | 1 | 0.0005 | 0.0018 | 0.0001 | 0.9972 | 0.0008 |
| warm_amber_haze | eye70 | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| warm_amber_haze | eye70 | T1 | 10 | 0.0177 | 0.0755 | 0 | 0.9931 | 0.0188 |
| warm_amber_haze | eye70 | T2 | 4 | 0.0171 | 0.0715 | 0 | 0.9934 | 0.0202 |
| warm_amber_haze | eye70 | T3 | 1 | 0.025 | 0.1025 | 0.0168 | 0.9956 | 0.0313 |
| warm_amber_haze | eye70 | Y1 | 4 | 0.0009 | 0.0027 | 0 | 0.997 | 0.001 |
| warm_amber_haze | eye70 | Y2 | 1 | 0.0007 | 0.0018 | 0.0001 | 0.9972 | 0.001 |
| warm_amber_haze | street | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| warm_amber_haze | street | T1 | 10 | 0.0032 | 0.0092 | 0 | 0.9327 | 0.004 |
| warm_amber_haze | street | T2 | 4 | 0.0033 | 0.0089 | 0 | 0.949 | 0.008 |
| warm_amber_haze | street | T3 | 1 | 0.0034 | 0.0086 | 0.0023 | 0.9643 | 0.0054 |
| warm_amber_haze | street | Y1 | 4 | 0.0008 | 0.0027 | 0 | 0.997 | 0.0009 |
| warm_amber_haze | street | Y2 | 1 | 0.0006 | 0.0019 | 0.0001 | 0.9972 | 0.0009 |
| warm_rust_sepia | eye70 | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| warm_rust_sepia | eye70 | T1 | 10 | 0.0276 | 0.0871 | 0 | 0.9931 | 0.0296 |
| warm_rust_sepia | eye70 | T2 | 4 | 0.022 | 0.069 | 0 | 0.9934 | 0.0294 |
| warm_rust_sepia | eye70 | T3 | 1 | 0.0183 | 0.0576 | 0.0151 | 0.9956 | 0.0281 |
| warm_rust_sepia | eye70 | Y1 | 4 | 0.002 | 0.0051 | 0 | 0.997 | 0.0022 |
| warm_rust_sepia | eye70 | Y2 | 1 | 0.0011 | 0.0027 | 0.0002 | 0.9972 | 0.002 |
| warm_rust_sepia | street | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| warm_rust_sepia | street | T1 | 10 | 0.0152 | 0.07 | 0 | 0.9327 | 0.0221 |
| warm_rust_sepia | street | T2 | 4 | 0.0125 | 0.0571 | 0 | 0.949 | 0.0209 |
| warm_rust_sepia | street | T3 | 1 | 0.0061 | 0.0236 | 0.0044 | 0.9643 | 0.0185 |
| warm_rust_sepia | street | Y1 | 4 | 0.001 | 0.0043 | 0 | 0.997 | 0.0017 |
| warm_rust_sepia | street | Y2 | 1 | 0.0006 | 0.0021 | 0.0001 | 0.9972 | 0.0014 |

Worst value per profile and threshold group (S_px / C_px / F; limits: S0 0 / 0 / 0, Y 0.01 / 0.01 / 0.001, T12 0.12 / 0.15 / 0.005, T3 0.12 / 0.15 / 0.08):

| Profile | S0 | Y1–Y2 | T1–T2 | T3 | Sub-measures passed |
|---|---|---|---|---|---|
| lavender_peach | 0 / 0 / 0 | 0.001 / 0.0032 / 0.0001 | 0.0161 / 0.0793 / 0 | 0.021 / 0.1017 / 0.014 | 12 of 12 |
| slate_crimson | 0 / 0 / 0 | 0.0008 / 0.0034 / 0.0001 | 0.0119 / 0.0491 / 0 | 0.0128 / 0.0522 / 0.0039 | 12 of 12 |
| slate_teal | 0 / 0 / 0 | 0.0012 / 0.0025 / 0.0001 | 0.0203 / 0.0581 / 0 | 0.0213 / 0.0666 / 0.0103 | 12 of 12 |
| warm_amber_haze | 0 / 0 / 0 | 0.0009 / 0.0027 / 0.0001 | 0.0177 / 0.0755 / 0 | 0.025 / 0.1025 / 0.0168 | 12 of 12 |
| warm_rust_sepia | 0 / 0 / 0 | 0.002 / 0.0051 / 0.0002 | 0.0276 / 0.0871 / 0 | 0.0183 / 0.0576 / 0.0151 | 12 of 12 |

### M-004@v1, five profiles × two viewpoints, `direction` anchoring (same sequences, same stabilisers), position snap and rotation quantisation on

**M-004@v1, `m004direction_<profile>`** — verdict `PASS`, registry v1.1 (sha256 5f39e5531f9e), failing gating: none

| Sub-measure | Kind | Value | Rule | Status | Decisive frame / sequence |
|---|---|---|---|---|---|
| M-004.S0 | gating | 0 share | <= 0 | PASS | eye70_S0 (S0, warm_rust_sepia/eye70, lag 1) |
| M-004.C_px.S0 | gating | 0 share | <= 0 | PASS | eye70_S0 (S0, warm_rust_sepia/eye70, lag 1) |
| M-004.F.S0 | gating | 0 share | <= 0 | PASS | eye70_S0 (S0, warm_rust_sepia/eye70, lag 1) |
| M-004.S_px.Y | gating | 0.002 share | <= 0.01 | PASS | eye70_Y1 (Y1, warm_rust_sepia/eye70, lag 4) |
| M-004.C_px.Y | gating | 0.0051 share | <= 0.01 | PASS | eye70_Y1 (Y1, warm_rust_sepia/eye70, lag 4) |
| M-004.F.Y | gating | 0.0002 share | <= 0.001 | PASS | eye70_Y2 (Y2, warm_rust_sepia/eye70, lag 1) |
| M-004.S_px.T12 | gating | 0.0276 share | <= 0.12 | PASS | eye70_T1 (T1, warm_rust_sepia/eye70, lag 10) |
| M-004.C_px.T12 | gating | 0.0871 share | <= 0.15 | PASS | eye70_T1 (T1, warm_rust_sepia/eye70, lag 10) |
| M-004.F.T12 | gating | 0 share | <= 0.005 | PASS | eye70_T1 (T1, warm_rust_sepia/eye70, lag 10) |
| M-004.S_px.T3 | gating | 0.025 share | <= 0.12 | PASS | eye70_T3 (T3, warm_amber_haze/eye70, lag 1) |
| M-004.C_px.T3 | gating | 0.1025 share | <= 0.15 | PASS | eye70_T3 (T3, warm_amber_haze/eye70, lag 1) |
| M-004.F.T3 | gating | 0.0168 share | <= 0.08 | PASS | eye70_T3 (T3, warm_amber_haze/eye70, lag 1) |

| Profile | Viewpoint | Sequence | Lag | S_px | C_px | F | Valid share | S pair p95 |
|---|---|---|---|---|---|---|---|---|
| lavender_peach | eye70 | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| lavender_peach | eye70 | T1 | 10 | 0.0149 | 0.0738 | 0 | 0.9931 | 0.0158 |
| lavender_peach | eye70 | T2 | 4 | 0.0161 | 0.0793 | 0 | 0.9934 | 0.0201 |
| lavender_peach | eye70 | T3 | 1 | 0.021 | 0.1017 | 0.014 | 0.9956 | 0.0228 |
| lavender_peach | eye70 | Y1 | 4 | 0.001 | 0.0031 | 0 | 0.997 | 0.0011 |
| lavender_peach | eye70 | Y2 | 1 | 0.0008 | 0.0022 | 0.0001 | 0.9972 | 0.001 |
| lavender_peach | street | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| lavender_peach | street | T1 | 10 | 0.0027 | 0.0043 | 0 | 0.9327 | 0.0038 |
| lavender_peach | street | T2 | 4 | 0.0041 | 0.0116 | 0 | 0.949 | 0.0115 |
| lavender_peach | street | T3 | 1 | 0.0027 | 0.0052 | 0.0016 | 0.9643 | 0.005 |
| lavender_peach | street | Y1 | 4 | 0.0009 | 0.0032 | 0 | 0.997 | 0.001 |
| lavender_peach | street | Y2 | 1 | 0.0007 | 0.0024 | 0.0001 | 0.9972 | 0.0009 |
| slate_crimson | eye70 | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| slate_crimson | eye70 | T1 | 10 | 0.0096 | 0.0382 | 0 | 0.9931 | 0.0106 |
| slate_crimson | eye70 | T2 | 4 | 0.0119 | 0.0491 | 0 | 0.9934 | 0.0273 |
| slate_crimson | eye70 | T3 | 1 | 0.0128 | 0.0522 | 0.0039 | 0.9956 | 0.0172 |
| slate_crimson | eye70 | Y1 | 4 | 0.0008 | 0.0024 | 0 | 0.997 | 0.0008 |
| slate_crimson | eye70 | Y2 | 1 | 0.0005 | 0.0014 | 0.0001 | 0.9972 | 0.0007 |
| slate_crimson | street | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| slate_crimson | street | T1 | 10 | 0.0027 | 0.008 | 0 | 0.9327 | 0.004 |
| slate_crimson | street | T2 | 4 | 0.0039 | 0.0155 | 0 | 0.949 | 0.0122 |
| slate_crimson | street | T3 | 1 | 0.0029 | 0.0061 | 0.0014 | 0.9643 | 0.0074 |
| slate_crimson | street | Y1 | 4 | 0.0006 | 0.0034 | 0 | 0.997 | 0.0008 |
| slate_crimson | street | Y2 | 1 | 0.0004 | 0.0022 | 0 | 0.9972 | 0.0007 |
| slate_teal | eye70 | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| slate_teal | eye70 | T1 | 10 | 0.0202 | 0.0581 | 0 | 0.9931 | 0.021 |
| slate_teal | eye70 | T2 | 4 | 0.0203 | 0.0579 | 0 | 0.9934 | 0.0249 |
| slate_teal | eye70 | T3 | 1 | 0.0213 | 0.0666 | 0.0103 | 0.9956 | 0.024 |
| slate_teal | eye70 | Y1 | 4 | 0.0012 | 0.002 | 0 | 0.997 | 0.0013 |
| slate_teal | eye70 | Y2 | 1 | 0.0008 | 0.0014 | 0.0001 | 0.9972 | 0.0013 |
| slate_teal | street | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| slate_teal | street | T1 | 10 | 0.0069 | 0.0118 | 0 | 0.9327 | 0.0098 |
| slate_teal | street | T2 | 4 | 0.0072 | 0.0146 | 0 | 0.949 | 0.01 |
| slate_teal | street | T3 | 1 | 0.0035 | 0.0049 | 0.0013 | 0.9643 | 0.0096 |
| slate_teal | street | Y1 | 4 | 0.0008 | 0.0025 | 0 | 0.997 | 0.0009 |
| slate_teal | street | Y2 | 1 | 0.0005 | 0.0018 | 0.0001 | 0.9972 | 0.0008 |
| warm_amber_haze | eye70 | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| warm_amber_haze | eye70 | T1 | 10 | 0.0177 | 0.0755 | 0 | 0.9931 | 0.0188 |
| warm_amber_haze | eye70 | T2 | 4 | 0.0171 | 0.0715 | 0 | 0.9934 | 0.0202 |
| warm_amber_haze | eye70 | T3 | 1 | 0.025 | 0.1025 | 0.0168 | 0.9956 | 0.0313 |
| warm_amber_haze | eye70 | Y1 | 4 | 0.0009 | 0.0027 | 0 | 0.997 | 0.001 |
| warm_amber_haze | eye70 | Y2 | 1 | 0.0007 | 0.0018 | 0.0001 | 0.9972 | 0.001 |
| warm_amber_haze | street | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| warm_amber_haze | street | T1 | 10 | 0.0032 | 0.0092 | 0 | 0.9327 | 0.004 |
| warm_amber_haze | street | T2 | 4 | 0.0033 | 0.0089 | 0 | 0.949 | 0.008 |
| warm_amber_haze | street | T3 | 1 | 0.0034 | 0.0086 | 0.0023 | 0.9643 | 0.0054 |
| warm_amber_haze | street | Y1 | 4 | 0.0008 | 0.0027 | 0 | 0.997 | 0.0009 |
| warm_amber_haze | street | Y2 | 1 | 0.0006 | 0.0019 | 0.0001 | 0.9972 | 0.0009 |
| warm_rust_sepia | eye70 | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| warm_rust_sepia | eye70 | T1 | 10 | 0.0276 | 0.0871 | 0 | 0.9931 | 0.0296 |
| warm_rust_sepia | eye70 | T2 | 4 | 0.022 | 0.069 | 0 | 0.9934 | 0.0294 |
| warm_rust_sepia | eye70 | T3 | 1 | 0.0183 | 0.0576 | 0.0151 | 0.9956 | 0.0281 |
| warm_rust_sepia | eye70 | Y1 | 4 | 0.002 | 0.0051 | 0 | 0.997 | 0.0022 |
| warm_rust_sepia | eye70 | Y2 | 1 | 0.0011 | 0.0027 | 0.0002 | 0.9972 | 0.002 |
| warm_rust_sepia | street | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| warm_rust_sepia | street | T1 | 10 | 0.0152 | 0.07 | 0 | 0.9327 | 0.0221 |
| warm_rust_sepia | street | T2 | 4 | 0.0125 | 0.0571 | 0 | 0.949 | 0.0209 |
| warm_rust_sepia | street | T3 | 1 | 0.0061 | 0.0236 | 0.0044 | 0.9643 | 0.0185 |
| warm_rust_sepia | street | Y1 | 4 | 0.001 | 0.0043 | 0 | 0.997 | 0.0017 |
| warm_rust_sepia | street | Y2 | 1 | 0.0006 | 0.0021 | 0.0001 | 0.9972 | 0.0014 |

Worst value per profile and threshold group (S_px / C_px / F; limits: S0 0 / 0 / 0, Y 0.01 / 0.01 / 0.001, T12 0.12 / 0.15 / 0.005, T3 0.12 / 0.15 / 0.08):

| Profile | S0 | Y1–Y2 | T1–T2 | T3 | Sub-measures passed |
|---|---|---|---|---|---|
| lavender_peach | 0 / 0 / 0 | 0.001 / 0.0032 / 0.0001 | 0.0161 / 0.0793 / 0 | 0.021 / 0.1017 / 0.014 | 12 of 12 |
| slate_crimson | 0 / 0 / 0 | 0.0008 / 0.0034 / 0.0001 | 0.0119 / 0.0491 / 0 | 0.0128 / 0.0522 / 0.0039 | 12 of 12 |
| slate_teal | 0 / 0 / 0 | 0.0012 / 0.0025 / 0.0001 | 0.0203 / 0.0581 / 0 | 0.0213 / 0.0666 / 0.0103 | 12 of 12 |
| warm_amber_haze | 0 / 0 / 0 | 0.0009 / 0.0027 / 0.0001 | 0.0177 / 0.0755 / 0 | 0.025 / 0.1025 / 0.0168 | 12 of 12 |
| warm_rust_sepia | 0 / 0 / 0 | 0.002 / 0.0051 / 0.0002 | 0.0276 / 0.0871 / 0 | 0.0183 / 0.0576 / 0.0151 | 12 of 12 |

### M-004@v1 A/B: every anchoring mode and each stabiliser off (slate_teal, eye70; C12)

Worst S_px / C_px / F per group. `snap` = position snap, `rotq` = rotation quantisation.

| Variant | S0 | Y1–Y2 | T1–T2 | T3 | Sub-measures passed |
|---|---|---|---|---|---|
| control_frame_noise | 0.0926 / 0.4108 / 0.0464 | - | - | - | 0 of 3 |
| direction | 0 / 0 / 0 | 0.0012 / 0.002 / 0.0001 | 0.0203 / 0.0581 / 0 | 0.0213 / 0.0666 / 0.0103 | 12 of 12 |
| direction_norotq | 0 / 0 / 0 | 0.166 / 0.6611 / 0.1238 | 0.021 / 0.0618 / 0 | 0.022 / 0.0709 / 0.0088 | 9 of 12 |
| direction_nosnap | 0 / 0 / 0 | 0.0012 / 0.002 / 0.0001 | 0.022 / 0.0633 / 0.0055 | 0.0213 / 0.0666 / 0.0103 | 11 of 12 |
| hybrid | 0 / 0 / 0 | 0.0029 / 0.0033 / 0.0006 | 0.0065 / 0.0108 / 0 | 0.0039 / 0.0059 / 0.0016 | 12 of 12 |
| hybrid_norotq | 0 / 0 / 0 | 0.1496 / 0.6447 / 0.1108 | 0.0064 / 0.011 / 0 | 0.0038 / 0.0063 / 0.0018 | 9 of 12 |
| hybrid_nosnap | 0 / 0 / 0 | 0.0029 / 0.0033 / 0.0006 | 0.0066 / 0.0111 / 0.0039 | 0.0039 / 0.0059 / 0.0016 | 12 of 12 |
| screen | 0 / 0 / 0 | 0.1658 / 0.6616 / 0.1235 | 0.0205 / 0.0608 / 0 | 0.0216 / 0.07 / 0.0103 | 9 of 12 |
| screen_nostab | 0 / 0 / 0 | 0.166 / 0.6611 / 0.1238 | 0.0227 / 0.0673 / 0.0054 | 0.022 / 0.0709 / 0.0088 | 8 of 12 |

### Dolly toward the 10 km layer (reported, B15; the tool gives dolly sequences no verdict)

```
[{"motion": "dolly", "note": "validated and reported without a verdict", "sequence": "eye70_D1"}]
```

### Checks outside the metric tools (`Tools/Rendering/Pixel/pixel_checks.py`)

| Check | File | Pass | Key values |
|---|---|---|---|
| c11_smooth_T1 | `metrics/checks/c11_smooth_T1.json` | yes | compensation on: max displacement error 1.0927 px, max step error 0.7299 px (pass yes); off: 2.0498 px, 3.7263 px (pass no); commanded -0.361 px/frame |
| c11_smooth_Y1 | `metrics/checks/c11_smooth_Y1.json` | yes | compensation on: max displacement error 0.2686 px, max step error 0.0073 px (pass yes); off: 3.2685 px, 3.0025 px (pass no); commanded -1.0005 px/frame |
| c13_cut | `metrics/checks/c13_cut.json` | yes | internal, output, index, depth, pose and offsets identical: yes; differs from the frame before the cut: yes |
| c19_depth | `metrics/checks/c19_depth.json` | yes | Block80_Near: 109.1492 m vs 109.147 m (0.002 %); Block80_Far: 954.6437 m vs 954.6978 m (0.006 %); Layer_3km_B: 3114 m vs 3114 m (0.003 %); Layer_10km_A: 9484 m vs 9485 m (0.012 %); Layer_30km_B: 28656 m vs 28655 m (0.002 %) |
| c19_identical_c11_subpixel_on | `metrics/checks/c19_identical_c11_subpixel_on.json` | yes | 1800 PNG and 362 JSON files compared, 0 differ |
| c19_identical_control_reversed_haze | `metrics/checks/c19_identical_control_reversed_haze.json` | yes | 60 PNG and 13 JSON files compared, 0 differ |
| c19_identical_control_unquantised | `metrics/checks/c19_identical_control_unquantised.json` | yes | 60 PNG and 13 JSON files compared, 0 differ |
| c19_identical_cut_check | `metrics/checks/c19_identical_cut_check.json` | yes | 65 PNG and 15 JSON files compared, 0 differ |
| c19_identical_haze_cal | `metrics/checks/c19_identical_haze_cal.json` | yes | 5 PNG and 2 JSON files compared, 0 differ |
| c19_identical_m003_stills | `metrics/checks/c19_identical_m003_stills.json` | yes | 180 PNG and 39 JSON files compared, 0 differ |
| c19_identical_stages_ab | `metrics/checks/c19_identical_stages_ab.json` | yes | 115 PNG and 46 JSON files compared, 0 differ |
| c2_grid_bilinear_control | `metrics/checks/c2_grid_bilinear_control.json` | no | uniform blocks at the declared offset [0, 0]: min 0.6954; with the offset ignored: min 0.6954 |
| c2_grid_declared_offset | `metrics/checks/c2_grid_declared_offset.json` | yes | uniform blocks at the declared offset [3, 0]: min 1; with the offset ignored: min 0.8407 |
| c6_layers_lavender_peach | `metrics/checks/c6_layers_lavender_peach.json` | yes | frame 0 mean L*: 1km 25.692, 3km 42.2046, 10km 45.9599, 30km 54.9944; successively lighter in 12 of 12 frames; far layer minus sky at horizon 8.6303 L*; near field darkest in 12 of 12; pure share of ground seam texels 1 (88648 of 88648); modal lightness never falls across a seam in 12 of 12 frames |
| c6_layers_slate_crimson | `metrics/checks/c6_layers_slate_crimson.json` | yes | frame 0 mean L*: 1km 27.2361, 3km 36.157, 10km 36.4085, 30km 49.7243; successively lighter in 12 of 12 frames; far layer minus sky at horizon 7.5038 L*; near field darkest in 12 of 12; pure share of ground seam texels 1 (86456 of 86456); modal lightness never falls across a seam in 12 of 12 frames |
| c6_layers_slate_teal | `metrics/checks/c6_layers_slate_teal.json` | yes | frame 0 mean L*: 1km 39.5724, 3km 46.0345, 10km 54.4675, 30km 67.0855; successively lighter in 12 of 12 frames; far layer minus sky at horizon 10.4921 L*; near field darkest in 12 of 12; pure share of ground seam texels 1 (85125 of 85125); modal lightness never falls across a seam in 12 of 12 frames |
| c6_layers_warm_amber_haze | `metrics/checks/c6_layers_warm_amber_haze.json` | yes | frame 0 mean L*: 1km 54.5771, 3km 64.2013, 10km 69.5307, 30km 78.4001; successively lighter in 12 of 12 frames; far layer minus sky at horizon 8.2765 L*; near field darkest in 12 of 12; pure share of ground seam texels 1 (97624 of 97624); modal lightness never falls across a seam in 12 of 12 frames |
| c6_layers_warm_rust_sepia | `metrics/checks/c6_layers_warm_rust_sepia.json` | yes | frame 0 mean L*: 1km 39.6954, 3km 46.5474, 10km 53.9407, 30km 65.6744; successively lighter in 12 of 12 frames; far layer minus sky at horizon 9.7568 L*; near field darkest in 12 of 12; pure share of ground seam texels 1 (123655 of 123655); modal lightness never falls across a seam in 12 of 12 frames |
| c7_blocks_lavender_peach | `metrics/checks/c7_blocks_lavender_peach.json` | yes | near_150m: in view in 4 frames, dL* min 9.3628, median 12.8889, frames with dL* >= 4: 4; far_1km: in view in 9 frames, dL* min 8.2679, median 11.9108, frames with dL* >= 4: 9 |
| c7_blocks_slate_crimson | `metrics/checks/c7_blocks_slate_crimson.json` | no | near_150m: in view in 4 frames, dL* min 16.8684, median 17.6289, frames with dL* >= 4: 4; far_1km: in view in 9 frames, dL* min 0, median 3.8857, frames with dL* >= 4: 1 |
| c7_blocks_slate_teal | `metrics/checks/c7_blocks_slate_teal.json` | yes | near_150m: in view in 4 frames, dL* min 11.878, median 12.8515, frames with dL* >= 4: 4; far_1km: in view in 9 frames, dL* min 6.3529, median 6.8659, frames with dL* >= 4: 9 |
| c7_blocks_warm_amber_haze | `metrics/checks/c7_blocks_warm_amber_haze.json` | no | near_150m: in view in 4 frames, dL* min 13.8399, median 20.7114, frames with dL* >= 4: 4; far_1km: in view in 9 frames, dL* min 0, median 4.1314, frames with dL* >= 4: 6 |
| c7_blocks_warm_rust_sepia | `metrics/checks/c7_blocks_warm_rust_sepia.json` | no | near_150m: in view in 4 frames, dL* min 11.4655, median 13.9994, frames with dL* >= 4: 4; far_1km: in view in 9 frames, dL* min 1.3534, median 4.6998, frames with dL* >= 4: 6 |
| c7_scale_lavender_peach | `metrics/checks/c7_scale_lavender_peach.json` | yes | marker 1 px, height 1 px, dL* 30.1416; 80 m block height 31.9496 px at 1043 m, dL* to background 13.5012 |
| c7_scale_slate_crimson | `metrics/checks/c7_scale_slate_crimson.json` | yes | marker 1 px, height 1 px, dL* 22.5531; 80 m block height 31.9496 px at 1043 m, dL* to background 5.2854 |
| c7_scale_slate_teal | `metrics/checks/c7_scale_slate_teal.json` | yes | marker 1 px, height 1 px, dL* 27.605; 80 m block height 31.9496 px at 1043 m, dL* to background 6.5024 |
| c7_scale_warm_amber_haze | `metrics/checks/c7_scale_warm_amber_haze.json` | yes | marker 1 px, height 1 px, dL* 32.8846; 80 m block height 31.9496 px at 1043 m, dL* to background 5.1253 |
| c7_scale_warm_rust_sepia | `metrics/checks/c7_scale_warm_rust_sepia.json` | yes | marker 1 px, height 1 px, dL* 33.7734; 80 m block height 31.9496 px at 1043 m, dL* to background 5.7542 |

### Per-pass timing (development Mac; GPU ms not measurable here; M-008 is not verified)

Hardware: Apple M5 Pro, Metal, Mac OS X 26.6.2, Unity 6000.6.9f1, batch mode, vsync 0. Method: `Recorder` on each `Pixel.*` sampler, 1000 frames measured after 123 warm-up frames discarded, capture off, `Pixel.Present` isolated. `SystemInfo.supportsGpuRecorder` is false on Metal in batch mode, so GPU ms is 0 in the CSV and only CPU ms per pass is meaningful (`timing/pass_timing_summary.json`, `timing/pass_timing_*.csv`). CI is a normal approximation and ignores autocorrelation.

| Output | Internal | Pass | CPU mean ms | CPU p95 ms | CPU 95 % CI of mean (ms) | CPU max ms | GPU ms |
|---|---|---|---|---|---|---|---|
| 1920x1080 | 480x270 | Pixel.Dither | 0.0167 | 0.0179 | 0.0146 to 0.0188 | 1.0772 | not measurable (recorder unsupported) |
| 1920x1080 | 480x270 | Pixel.Downsample | 0 | 0 | 0 to 0 | 0 | not measurable (recorder unsupported) |
| 1920x1080 | 480x270 | Pixel.Haze | 0.0144 | 0.0158 | 0.0143 to 0.0145 | 0.0423 | not measurable (recorder unsupported) |
| 1920x1080 | 480x270 | Pixel.Outline | 0 | 0 | 0 to 0 | 0 | not measurable (recorder unsupported) |
| 1920x1080 | 480x270 | Pixel.Present | 0.0091 | 0.0107 | 0.0091 to 0.0092 | 0.0345 | not measurable (recorder unsupported) |
| 1920x1080 | 480x270 | Pixel.Quantise | 0.0141 | 0.0155 | 0.014 to 0.0141 | 0.0258 | not measurable (recorder unsupported) |
| 1920x1080 | 480x270 | Pixel.Store | 0.0155 | 0.017 | 0.0154 to 0.0155 | 0.033 | not measurable (recorder unsupported) |
| 2560x1440 | 512x288 | Pixel.Dither | 0.0151 | 0.0169 | 0.015 to 0.0152 | 0.0255 | not measurable (recorder unsupported) |
| 2560x1440 | 512x288 | Pixel.Downsample | 0 | 0 | 0 to 0 | 0 | not measurable (recorder unsupported) |
| 2560x1440 | 512x288 | Pixel.Haze | 0.0141 | 0.0151 | 0.014 to 0.0143 | 0.0862 | not measurable (recorder unsupported) |
| 2560x1440 | 512x288 | Pixel.Outline | 0 | 0 | 0 to 0 | 0 | not measurable (recorder unsupported) |
| 2560x1440 | 512x288 | Pixel.Present | 0.0089 | 0.01 | 0.0087 to 0.009 | 0.0748 | not measurable (recorder unsupported) |
| 2560x1440 | 512x288 | Pixel.Quantise | 0.0138 | 0.0148 | 0.0137 to 0.0139 | 0.0479 | not measurable (recorder unsupported) |
| 2560x1440 | 512x288 | Pixel.Store | 0.015 | 0.0164 | 0.015 to 0.0151 | 0.0223 | not measurable (recorder unsupported) |

`Pixel.Downsample` and `Pixel.Outline` read 0: those passes are not part of the M0 chain.

GC with capture off, 600 frames: 1920x1080: 0 allocation samples, 0 collections, mono used delta 16384 bytes; 2560x1440: 0 allocation samples, 0 collections, mono used delta 114688 bytes.
