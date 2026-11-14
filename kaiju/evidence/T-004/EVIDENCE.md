# T-004 evidence: pixel pipeline renderer features and third-person rig stabilisation

Branch `task/T-004`. All paths are relative to the project root (`kaiju/`). Thresholds are those of `research/R-002.md` (`M-003@v1`, `M-004@v1`) as registered in the T-002 tools; none was changed. Captures come straight from the engine's render targets (PNG encoding only); nothing was post-processed outside the engine.

## Fix cycle 2 (task/T-004-wip)
Everything under "Stored values" below is regenerated at this cycle (tuning commits c1/c2 53ccabf, C6/C7 c106648; tests at 44ab92e) and replaces the cycle-1 tables. Text in "Results at a glance", "Criterion to artifact" and "Not met, and why" is cycle-1 prose: where it disagrees with this section or with the tables, this section and the tables apply. Cycle-1 numbers of the M-004 `hybrid` run, the 300-frame wall-time table and the 41/16/170 test counts are superseded.

### Defect, change, evidence
| Defect | Change | Evidence |
|---|---|---|
| style-4: dither only in gradients | Design per the progress notes; dither confined to gradients | M-003 PASS, failing gating none; A2 1.0; DIG 0.168 to 0.460 over all 180 stills (`tasks/T-004.c1c2.md`, `tasks/T-004.c6c7.md`, tables below). Tuning c1/c2 53ccabf; C6/C7 c106648 |
| style-1: C6 seams | `hazeBands` 16 by default (rust 32, amber 20, crimson 5), ladder steps in `PaletteProfileTuning.json` | Seam purity at least 0.9991 on all five profiles; modal lightness never falls across a seam in 12 of 12 frames for every profile (`metrics/checks/c6_layers_*.json`) |
| style-6: modal lightness across seams | same tuning | 12 of 12 frames, all five profiles |
| style-2: C7 at 1 km | rust ladder step 5.5 and `hazeFullM` 5200; crimson 5 bands and rung step 6.5 | Block against background at 1 km: dL* at least 4 in 9 of 9 views on every profile (min dL* 4.29 slate_teal to 8.29 lavender_peach; `metrics/checks/c7_blocks_*.json`). See the note on `c7_scale_slate_teal` in the checks table |
| item 2: default anchoring | Default `direction` | M-004 below |
| item 7 / arch-2: M-004 sets | Now captured with the sub-pixel present on | M-004 default mode: 11 of 12 gating sub-measures pass (cycle 1: 7 of 12 in `direction`, 8 of 12 in `hybrid`). Remaining miss: C_px.T12 0.1579 against Assumed 0.15 (slate_crimson, eye70, T1): calibration item |
| style-3: C11 per-depth smoothness | Check extended (b698897) | Cycle-1 smoke numbers near 10.6 output px (reference 1.04, far 3.4) against 1 px: not met; research/founders item. Fresh `smooth` values are in `metrics/checks/c11_smooth_*.json` (table below) |
| style-7: colour negative anchor | `control_continuous_tone` added | On `sheets/panel_board.png`, `captures/control_continuous_tone` |
| perf-1/-2/-3: per-pass cost | Per-pass `Recorder` timing at 1080p and 1440p, 1000 frames, 123 warm-up frames discarded, vsync 0 | `timing/pass_timing_summary.json`; table below. `supportsGpuRecorder` is false on Metal in batch mode: only CPU ms per pass, `Pixel.Present` isolated. GPU ms per pass is not measurable on this Mac: needs the target PC (perf-5) or a founder re-scope of C20 |
| perf-4: GC | Measured over 600 frames, capture off | 0 allocation samples and 0 collections at both resolutions |
| arch-1 / arch-4 | Asmdef and flag-file tests | af18ea4 |
| arch-5 | Loud tuning loaders | af18ea4 |
| arch-6 | README | 423eeb9 |
| arch-7 | Stored log warnings | `captures/capture_log_warnings.txt`: 0 lines this run |
| arch-3 | Slimmed evidence | `evidence/T-004/captures/` keeps sidecars and every 10th frame; full sets are in `evidence/T-004/captures_local/` (git-ignored) |

Validation (`metrics/validate/summary.txt`): 36 of 37 sets VALID; `orbit_slate_teal` INVALID (no `color_output` files; same in cycle 1; style-8 open).

Tests at 44ab92e: Unity EditMode 174 of 174 pass; dotnet Rendering.Pixel 44 of 44, Camera 17 of 17.

Not done in cycle 2: style-8 (orbit in shipping mode, dolly path, cuts, A/B coverage), style-5, perf-5.

### Regeneration commands (from the project root)
1. `unity run . ... -- -executeMethod Game.Tools.StyleCapture.StyleCaptureCli.InstallAll` (bakes assets and palette profiles from the tuning file).
2. `unity run . --no-tail --log-file <log> --timeout 5400 -- -executeMethod Game.Tools.StyleCapture.StyleCaptureCli.Run -captureOut evidence/T-004/captures_local`
3. The same with `-captureJobs m004direction`.
4. Pass timing must use the Unity binary directly, without `-quit` (`unity run` adds `-quit`, which ends the run before the frame loop): `<Unity> -batchmode -projectPath <kaiju> -logFile <log> -executeMethod Game.Tools.StyleCapture.StyleTiming.PassTimingCli -timingOut evidence/T-004/timing -timingFrames 1000 -timingWarmup 120 -gcFrames 600`
5. `Tools/Rendering/Pixel/run_evidence.sh <integration>/kaiju/Tools/Metrics`
6. `/opt/miniconda3/envs/kaiju/bin/python Tools/Rendering/Pixel/evidence_tables.py evidence/T-004/metrics`
7. Tests: `unity test . --mode EditMode`; `dotnet test` in `DotnetTests/Rendering.Pixel.Tests` and `DotnetTests/Camera.Tests`.


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
The capture tool writes about 2.8 GB (mostly 1920×1080 `color_output` PNGs of the motion sequences) into `evidence/T-004/captures_local/`, which is git-ignored. The metrics were run on that full output. `evidence/T-004/captures/` (149 MB, committed) is a copy of part of it:
- in full: `m003_stills` (180 frames), `m003_details`, `m003_stills_1440p`, `m003_stills_2160p`, `haze_cal`, the six `control_*` sets, `stages_ab`, `cut_check`, and one profile-viewpoint M-004 set: `m004_slate_teal/eye70_{S0,T1,T2,T3,Y1,Y2}` (960 frames);
- `orbit_slate_teal/orbit_20s`: `sequence.json`, the sidecars of all 1200 frames and every tenth `color_internal` frame (the orbit is written without `color_output`; the presented frame is the same image at ×4);
- not committed as frames (metric JSON in `metrics/`, contact sheets in `sheets/`): the other nine `m004_<profile>` profile-viewpoints, the five `m004direction_<profile>` sets, the eight `m004ab_*` variants and the frame-noise control, `c11_subpixel_on`, `c11_subpixel_off`, `dolly_slate_teal`.

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

## Not met, and why (cycle 1)
1. **C9 yaw (M-004.S_px.Y, C_px.Y, F.Y) with the configured default `hybrid`.** Achieved 0.0351 / 0.0505 / 0.0131 against 0.01 / 0.01 / 0.001. The surface-anchored pattern near the camera is resampled at a different sub-texel phase after each rotation step away from the screen centre. `direction` meets all three on every profile (worst 0.0013 / 0.0020 / 0.0001).
2. **C10 translation crawl.** `hybrid`: C_px.T12 0.1507 against 0.15 (one sequence, warm_rust_sepia street T1; the other nine profile-viewpoints are 0.062–0.096). `direction`: S_px up to 0.196 and C_px up to 0.302 over the five profiles, because a screen pattern cannot follow parallax. No mode meets yaw and translation together; A/B numbers for every mode and stabiliser are stored. Thresholds were not changed (they are Assumed pending calibration, R-002 §5).
3. **C7 at 1 km for three profiles.** One haze band of separation is there (the block and the ground and forms behind it are one or two bands apart), but with `ditherSpread` 0.4 neighbouring bands often land on the same palette colour in slate_teal, lavender_peach and slate_crimson, whose palettes have few mid-value entries, so ΔL* falls below 4. Raising `ditherSpread` restores the mean value step and raises DIG_max further; left for the style panel.
4. **C6 seam indices in slate_crimson** (32 of 44 ground seams): a few seam pixels carry the next lighter index.
5. **C20.** Present p95 above 0.5 ms as measured (0.74 / 0.60 ms): the number includes the full second camera and the capture path's copy, not only the present pass. Stage chain p95 0.52 ms at 1080p. Per-pass GPU markers could not be read in a batch run (the GPU recorder needs a player loop), so costs are paired wall-time differences. M-008 unverified.
6. **M-003 advisories:** DIG_max 0.6489 (> 0.5: Bayer with a 0.75-band haze seam and spread 0.4 dithers much of the ground and sky); accent share below 0.1 % in frames where the beacon is small; sky share min 0.178 (< 0.2) and horizon max 0.474 (> 0.45) in the elevated views; frame contrast min 12.7 (< 25; slate_crimson's whole ramp spans 30 L*).
7. **Validator and sub-pixel compensation.** The T-002 validator rejects sequences whose frames differ in `resolution.output_block_offset_px` / `subpixel_offset_internal_px`; R-002 §6 defines them per frame. The M-004 sets were therefore captured with `rendering.pixel.subPixelPresent` off (applied offset zero). The internal image, index and depth do not depend on that flag; the compensated output is in `c11_subpixel_on` (invalid by that validator rule only). Proposed as a contract question in `tasks/T-004.impl.md`.
8. **Not built:** cockpit rig, quantisation hysteresis, light shafts and smoke (later slices); no PlayMode test (EditMode capture tests and rig tests cover the behaviour); the blind panel is the founders' gate.

## Stored values (cycle 2, regenerated)
### M-003@v1 on the 180 stills (5 profiles × 3 viewpoints × 12 frames) and the haze calibration set

**M-003@v1, `m003_stills` + `haze_cal`** — verdict `PASS`, registry v1.1 (sha256 7a63afd06c66), failing gating: none

| Sub-measure | Kind | Value | Rule | Status | Decisive frame / sequence |
|---|---|---|---|---|---|
| M-003.a.output | gating | 1 share | >= 0.95 | PASS | A_2 output worst frame m003_stills/lavender_peach_elevated300/f000000 |
| M-003.a.internal | gating | 1 share | >= 0.995 | PASS | A_2 internal worst frame m003_stills/lavender_peach_elevated300/f000000 |
| M-003.a.A3 | info | 1 share |  - | REPORTED |  |
| M-003.a.A5 | info | 1 share |  - | REPORTED |  |
| M-003.b.DIG | gating | 1 share of frames | >= 0.8 | PASS | lavender_peach: share of evaluated frames with DIG >= dig_min |
| M-003.b.DIG_max | advisory | 0.4603 DIG | <= 0.5 | PASS | max DIG m003_stills/slate_teal_eye70/f000002 |
| M-003.b.O | advisory | 1.1912 O |  - | REPORTED |  |
| M-003.c1 | gating | 0.9429 rho | >= 0.9 | PASS | m003_stills/slate_crimson_eye70/f000009: Spearman rho(band, L*) |
| M-003.c2 | gating | 0 L* | <= 0 | PASS | haze_cal/lavender_peach/f000000: max drop of pair mean L* |
| M-003.d-i | gating | 8 levels | <= 12 | PASS | value levels m003_stills/warm_amber_haze_street/f000004 |
| M-003.d-ii | gating | 2 families | <= 3 | PASS | hue families m003_stills/lavender_peach_elevated300/f000000 |
| M-003.d-iii | advisory | 0 share | in 0.001 | FAIL | accent share m003_stills/lavender_peach_eye70/f000000 |
| M-003.d-iv | gating | 1 share of blocks | >= 0.995 | PASS | uniform blocks m003_stills/lavender_peach_elevated300/f000000 |
| M-003.e.sky | advisory | 0.1778  | in 0.2 | FAIL | sky_share m003_stills/lavender_peach_street/f000005 |
| M-003.e.horizon | advisory | 0.4741  | <= 0.45 | FAIL | horizon m003_stills/lavender_peach_elevated300/f000000 |
| M-003.e.contrast | advisory | 19.127  | >= 25 | FAIL | contrast m003_stills/warm_rust_sepia_elevated300/f000002 |
| M-003.f | gating | -  |  - | PENDING | no panel_ratings@v1 file given (the tool never defaults a score) |

| Profile | Frames | a: min A₂ internal | a: min A₂ output | b: frames DIG ≥ 0.15 / evaluated | b: DIG min / max | b: median O | c1: max drop L* | c1: min ρ | c1: min span L* | d-i: max levels | d-ii: max hue families | d-iii: accent share min / max | d-iv: min uniform blocks | e: sky share min / max | e: horizon max | e: contrast min |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| lavender_peach | 36 | 1 | 1 | 36 / 36 | 0.2297 / 0.3809 | 1.2636 | 0.0533 | 0.9643 | 33.4288 | 7 | 2 | 0 / 0.0001 | 1 | 0.1778 / 0.4837 | 0.4741 | 22.0888 |
| slate_crimson | 36 | 1 | 1 | 36 / 36 | 0.2444 / 0.3829 | 1.2679 | 0.1347 | 0.9429 | 28.1771 | 5 | 1 | 0 / 0.0016 | 1 | 0.1778 / 0.4837 | 0.4741 | 22.5531 |
| slate_teal | 36 | 1 | 1 | 36 / 36 | 0.2527 / 0.4603 | 1.0452 | 0 | 1 | 27.7485 | 6 | 1 | no accent | 1 | 0.1778 / 0.4837 | 0.4741 | 21.1027 |
| warm_amber_haze | 36 | 1 | 1 | 36 / 36 | 0.2407 / 0.4159 | 1.2337 | 1.8353 | 0.9643 | 28.8185 | 8 | 1 | 0 / 0.0016 | 1 | 0.1778 / 0.4837 | 0.4741 | 20.1896 |
| warm_rust_sepia | 36 | 1 | 1 | 36 / 36 | 0.1683 / 0.3707 | 1.0604 | 0 | 1 | 26.0531 | 7 | 1 | 0 / 0 | 1 | 0.1778 / 0.4837 | 0.4741 | 19.127 |

### M-003@v1, detail views (sky gradient, gradient-lit surface, scale view), 3 frames per profile: fewer than the 12 the metric requires, so the tool's verdict is about coverage; the values are what C4 asks for individually

**m003_details** — verdict `INCOMPLETE`, registry v1.1 (sha256 7a63afd06c66), failing gating: none

| Sub-measure | Kind | Value | Rule | Status | Decisive frame / sequence |
|---|---|---|---|---|---|
| M-003.a.output | gating | 1 share | >= 0.95 | PASS | A_2 output worst frame m003_details/lavender_peach_detail_gradient/f000000 |
| M-003.a.internal | gating | 1 share | >= 0.995 | PASS | A_2 internal worst frame m003_details/lavender_peach_detail_gradient/f000000 |
| M-003.a.A3 | info | 1 share |  - | REPORTED |  |
| M-003.a.A5 | info | 1 share |  - | REPORTED |  |
| M-003.b.DIG | gating | 1 share of frames | >= 0.8 | PASS | lavender_peach: share of evaluated frames with DIG >= dig_min |
| M-003.b.DIG_max | advisory | 0.4803 DIG | <= 0.5 | PASS | max DIG m003_details/slate_teal_detail_gradient/f000000 |
| M-003.b.O | advisory | 1.1592 O |  - | REPORTED |  |
| M-003.c1 | gating | 1 rho | >= 0.9 | PASS | m003_details/lavender_peach_detail_sky/f000000: Spearman rho(band, L*) |
| M-003.c2 | gating | 0 L* | <= 0 | PASS | haze_cal/lavender_peach/f000000: max drop of pair mean L* |
| M-003.d-i | gating | 7 levels | <= 12 | PASS | value levels m003_details/warm_amber_haze_detail_scale/f000000 |
| M-003.d-ii | gating | 2 families | <= 3 | PASS | hue families m003_details/lavender_peach_detail_gradient/f000000 |
| M-003.d-iii | advisory | 0.0013 share | in 0.001 | PASS | accent share m003_details/slate_crimson_detail_scale/f000000 |
| M-003.d-iv | gating | 1 share of blocks | >= 0.995 | PASS | uniform blocks m003_details/lavender_peach_detail_gradient/f000000 |
| M-003.e.sky | advisory | 0.8314  | in 0.2 | FAIL | sky_share m003_details/lavender_peach_detail_sky/f000000 |
| M-003.e.horizon | advisory | 0.4444  | <= 0.45 | PASS | horizon m003_details/lavender_peach_detail_gradient/f000000 |
| M-003.e.contrast | advisory | 14.3164  | >= 25 | FAIL | contrast m003_details/warm_rust_sepia_detail_gradient/f000000 |
| M-003.f | gating | -  |  - | PENDING | no panel_ratings@v1 file given (the tool never defaults a score) |

| Frame | DIG | O | gradient share | A₂ internal | grid uniform |
|---|---|---|---|---|---|
| m003_details/lavender_peach_detail_gradient/f000000 | 0.4414 | 1.1148 | 0.1624 | 1 | 1 |
| m003_details/lavender_peach_detail_scale/f000000 | 0.2928 | 1.1163 | 0.2642 | 1 | 1 |
| m003_details/lavender_peach_detail_sky/f000000 | 0.4481 | 1.25 | 0.2135 | 1 | 1 |
| m003_details/slate_crimson_detail_gradient/f000000 | 0.3996 | 1.1954 | 0.1482 | 1 | 1 |
| m003_details/slate_crimson_detail_scale/f000000 | 0.288 | 1.1592 | 0.2098 | 1 | 1 |
| m003_details/slate_crimson_detail_sky/f000000 | 0.386 | 1.3897 | 0.184 | 1 | 1 |
| m003_details/slate_teal_detail_gradient/f000000 | 0.4803 | 1.0215 | 0.1822 | 1 | 1 |
| m003_details/slate_teal_detail_scale/f000000 | 0.3812 | 1.0084 | 0.2858 | 1 | 1 |
| m003_details/slate_teal_detail_sky/f000000 | 0.4279 | 1.2292 | 0.2109 | 1 | 1 |
| m003_details/warm_amber_haze_detail_gradient/f000000 | 0.4319 | 1.1729 | 0.153 | 1 | 1 |
| m003_details/warm_amber_haze_detail_scale/f000000 | 0.2625 | 1.0611 | 0.3008 | 1 | 1 |
| m003_details/warm_amber_haze_detail_sky/f000000 | 0.3842 | 1.3519 | 0.1501 | 1 | 1 |
| m003_details/warm_rust_sepia_detail_gradient/f000000 | 0.4054 | 1.0391 | 0.1729 | 1 | 1 |
| m003_details/warm_rust_sepia_detail_scale/f000000 | 0.2646 | 1.007 | 0.2856 | 1 | 1 |
| m003_details/warm_rust_sepia_detail_sky/f000000 | 0.4172 | 1.2651 | 0.1746 | 1 | 1 |

### M-003@v1, 2560×1440 output (k = 5, 512×288 internal), one frame per profile

**m003_1440p** — verdict `INCOMPLETE`, registry v1.1 (sha256 7a63afd06c66), failing gating: none

| Sub-measure | Kind | Value | Rule | Status | Decisive frame / sequence |
|---|---|---|---|---|---|
| M-003.a.output | gating | 1 share | >= 0.95 | PASS | A_2 output worst frame m003_stills_1440p/lavender_peach_eye70/f000000 |
| M-003.a.internal | gating | 1 share | >= 0.995 | PASS | A_2 internal worst frame m003_stills_1440p/lavender_peach_eye70/f000000 |
| M-003.a.A3 | info | 1 share |  - | REPORTED |  |
| M-003.a.A5 | info | 1 share |  - | REPORTED |  |
| M-003.b.DIG | gating | 1 share of frames | >= 0.8 | PASS | lavender_peach: share of evaluated frames with DIG >= dig_min |
| M-003.b.DIG_max | advisory | 0.3735 DIG | <= 0.5 | PASS | max DIG m003_stills_1440p/slate_teal_eye70/f000000 |
| M-003.b.O | advisory | 1.2411 O |  - | REPORTED |  |
| M-003.c1 | gating | 1 rho | >= 0.9 | PASS | m003_stills_1440p/lavender_peach_eye70/f000000: Spearman rho(band, L*) |
| M-003.c2 | gating | 0 L* | <= 0 | PASS | haze_cal/lavender_peach/f000000: max drop of pair mean L* |
| M-003.d-i | gating | 5 levels | <= 12 | PASS | value levels m003_stills_1440p/lavender_peach_eye70/f000000 |
| M-003.d-ii | gating | 2 families | <= 3 | PASS | hue families m003_stills_1440p/lavender_peach_eye70/f000000 |
| M-003.d-iii | advisory | -  |  - | NOT_APPLICABLE | no accent present in any frame (applies only when present) |
| M-003.d-iv | gating | 1 share of blocks | >= 0.995 | PASS | uniform blocks m003_stills_1440p/lavender_peach_eye70/f000000 |
| M-003.e.sky | advisory | 0.3794  | in 0.2 | PASS | sky_share m003_stills_1440p/lavender_peach_eye70/f000000 |
| M-003.e.horizon | advisory | 0.4201  | <= 0.45 | PASS | horizon m003_stills_1440p/lavender_peach_eye70/f000000 |
| M-003.e.contrast | advisory | 20.9153  | >= 25 | FAIL | contrast m003_stills_1440p/warm_amber_haze_eye70/f000000 |
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
| M-003.b.DIG_max | advisory | 0.3554 DIG | <= 0.5 | PASS | max DIG m003_stills_2160p/slate_teal_eye70/f000000 |
| M-003.b.O | advisory | 1.0385 O |  - | REPORTED |  |
| M-003.c1 | gating | 1 rho | >= 0.9 | PASS | m003_stills_2160p/slate_teal_eye70/f000000: Spearman rho(band, L*) |
| M-003.c2 | gating | 0 L* | <= 0 | PASS | haze_cal/lavender_peach/f000000: max drop of pair mean L* |
| M-003.d-i | gating | 5 levels | <= 12 | PASS | value levels m003_stills_2160p/slate_teal_eye70/f000000 |
| M-003.d-ii | gating | 1 families | <= 3 | PASS | hue families m003_stills_2160p/slate_teal_eye70/f000000 |
| M-003.d-iii | advisory | -  |  - | NOT_APPLICABLE | no accent present in any frame (applies only when present) |
| M-003.d-iv | gating | 1 share of blocks | >= 0.995 | PASS | uniform blocks m003_stills_2160p/slate_teal_eye70/f000000 |
| M-003.e.sky | advisory | 0.3791  | in 0.2 | PASS | sky_share m003_stills_2160p/slate_teal_eye70/f000000 |
| M-003.e.horizon | advisory | 0.4194  | <= 0.45 | PASS | horizon m003_stills_2160p/slate_teal_eye70/f000000 |
| M-003.e.contrast | advisory | 34.352  | >= 25 | PASS | contrast m003_stills_2160p/slate_teal_eye70/f000000 |
| M-003.f | gating | -  |  - | PENDING | no panel_ratings@v1 file given (the tool never defaults a score) |

### Negative controls on the same tool (slate_teal, eye70, 12 frames each): the tests can fail

| Control | Verdict | Failing gating sub-measures | a.internal | a.output | b.DIG share | c1 value | d-iv |
|---|---|---|---|---|---|---|---|
| bilinear | FAIL | M-003.a.output, M-003.d-iv | 1 (PASS) | 0.8166 (FAIL) | 1 (PASS) | 1 (PASS) | 0.6116 (FAIL) |
| dither_off | FAIL | M-003.b.DIG | 1 (PASS) | 1 (PASS) | 0 (FAIL) | 1 (PASS) | 1 (PASS) |
| haze_off | FAIL | M-003.b.DIG, M-003.c1 | 1 (PASS) | 1 (PASS) | 0 (FAIL) | -0.1715 (FAIL) | 1 (PASS) |
| reversed_haze | FAIL | M-003.c1 | 1 (PASS) | 1 (PASS) | 1 (PASS) | 9.5012 (FAIL) | 1 (PASS) |
| subpixel_offset | INCOMPLETE | none | 1 (PASS) | 1 (PASS) | 1 (PASS) | 1 (PASS) | 1 (PASS) |
| unquantised | FAIL | M-003.a.output, M-003.a.internal, M-003.b.DIG | 0 (FAIL) | 0 (FAIL) | 0 (FAIL) | 1 (PASS) | 1 (PASS) |

### M-004@v1, five profiles × two viewpoints, configured default anchoring (`direction`), position snap and rotation quantisation on

**M-004@v1, `m004_<profile>`** — verdict `FAIL`, registry v1.1 (sha256 5f39e5531f9e), failing gating: M-004.C_px.T12

| Sub-measure | Kind | Value | Rule | Status | Decisive frame / sequence |
|---|---|---|---|---|---|
| M-004.S0 | gating | 0 share | <= 0 | PASS | eye70_S0 (S0, warm_rust_sepia/eye70, lag 1) |
| M-004.C_px.S0 | gating | 0 share | <= 0 | PASS | eye70_S0 (S0, warm_rust_sepia/eye70, lag 1) |
| M-004.F.S0 | gating | 0 share | <= 0 | PASS | eye70_S0 (S0, warm_rust_sepia/eye70, lag 1) |
| M-004.S_px.Y | gating | 0.0023 share | <= 0.01 | PASS | eye70_Y1 (Y1, slate_teal/eye70, lag 4) |
| M-004.C_px.Y | gating | 0.0072 share | <= 0.01 | PASS | eye70_Y1 (Y1, slate_teal/eye70, lag 4) |
| M-004.F.Y | gating | 0.0002 share | <= 0.001 | PASS | eye70_Y2 (Y2, slate_teal/eye70, lag 1) |
| M-004.S_px.T12 | gating | 0.0421 share | <= 0.12 | PASS | eye70_T1 (T1, slate_teal/eye70, lag 10) |
| M-004.C_px.T12 | gating | 0.1579 share | <= 0.15 | FAIL | eye70_T1 (T1, slate_crimson/eye70, lag 10) |
| M-004.F.T12 | gating | 0 share | <= 0.005 | PASS | eye70_T1 (T1, warm_rust_sepia/eye70, lag 10) |
| M-004.S_px.T3 | gating | 0.0277 share | <= 0.12 | PASS | eye70_T3 (T3, lavender_peach/eye70, lag 1) |
| M-004.C_px.T3 | gating | 0.1214 share | <= 0.15 | PASS | eye70_T3 (T3, slate_crimson/eye70, lag 1) |
| M-004.F.T3 | gating | 0.0231 share | <= 0.08 | PASS | eye70_T3 (T3, slate_teal/eye70, lag 1) |

| Profile | Viewpoint | Sequence | Lag | S_px | C_px | F | Valid share | S pair p95 |
|---|---|---|---|---|---|---|---|---|
| lavender_peach | eye70 | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| lavender_peach | eye70 | T1 | 10 | 0.0325 | 0.1301 | 0 | 0.9931 | 0.0345 |
| lavender_peach | eye70 | T2 | 4 | 0.0269 | 0.1089 | 0 | 0.9934 | 0.0344 |
| lavender_peach | eye70 | T3 | 1 | 0.0277 | 0.1121 | 0.0213 | 0.9956 | 0.0355 |
| lavender_peach | eye70 | Y1 | 4 | 0.0019 | 0.005 | 0 | 0.997 | 0.002 |
| lavender_peach | eye70 | Y2 | 1 | 0.0012 | 0.0033 | 0.0002 | 0.9972 | 0.002 |
| lavender_peach | street | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| lavender_peach | street | T1 | 10 | 0.0135 | 0.0538 | 0 | 0.9327 | 0.0192 |
| lavender_peach | street | T2 | 4 | 0.0112 | 0.0428 | 0 | 0.949 | 0.0181 |
| lavender_peach | street | T3 | 1 | 0.0064 | 0.0213 | 0.0043 | 0.9643 | 0.0178 |
| lavender_peach | street | Y1 | 4 | 0.0012 | 0.0039 | 0 | 0.997 | 0.0017 |
| lavender_peach | street | Y2 | 1 | 0.0007 | 0.0026 | 0.0001 | 0.9972 | 0.0014 |
| slate_crimson | eye70 | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| slate_crimson | eye70 | T1 | 10 | 0.0304 | 0.1579 | 0 | 0.9931 | 0.032 |
| slate_crimson | eye70 | T2 | 4 | 0.0263 | 0.1277 | 0 | 0.9934 | 0.0326 |
| slate_crimson | eye70 | T3 | 1 | 0.025 | 0.1214 | 0.019 | 0.9956 | 0.0327 |
| slate_crimson | eye70 | Y1 | 4 | 0.0014 | 0.0051 | 0 | 0.997 | 0.0015 |
| slate_crimson | eye70 | Y2 | 1 | 0.0008 | 0.0029 | 0.0001 | 0.9972 | 0.0017 |
| slate_crimson | street | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| slate_crimson | street | T1 | 10 | 0.016 | 0.1138 | 0 | 0.9327 | 0.0207 |
| slate_crimson | street | T2 | 4 | 0.0138 | 0.0848 | 0 | 0.949 | 0.0214 |
| slate_crimson | street | T3 | 1 | 0.0076 | 0.0376 | 0.005 | 0.9643 | 0.0209 |
| slate_crimson | street | Y1 | 4 | 0.0007 | 0.0034 | 0 | 0.997 | 0.0014 |
| slate_crimson | street | Y2 | 1 | 0.0005 | 0.0024 | 0.0001 | 0.9972 | 0.0009 |
| slate_teal | eye70 | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| slate_teal | eye70 | T1 | 10 | 0.0421 | 0.1353 | 0 | 0.9931 | 0.0443 |
| slate_teal | eye70 | T2 | 4 | 0.0323 | 0.1049 | 0 | 0.9934 | 0.0442 |
| slate_teal | eye70 | T3 | 1 | 0.0273 | 0.085 | 0.0231 | 0.9956 | 0.0429 |
| slate_teal | eye70 | Y1 | 4 | 0.0023 | 0.0072 | 0 | 0.997 | 0.0027 |
| slate_teal | eye70 | Y2 | 1 | 0.0013 | 0.0037 | 0.0002 | 0.9972 | 0.0024 |
| slate_teal | street | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| slate_teal | street | T1 | 10 | 0.0214 | 0.1054 | 0 | 0.9327 | 0.0294 |
| slate_teal | street | T2 | 4 | 0.0175 | 0.0877 | 0 | 0.949 | 0.0287 |
| slate_teal | street | T3 | 1 | 0.0076 | 0.0329 | 0.0058 | 0.9643 | 0.0263 |
| slate_teal | street | Y1 | 4 | 0.0011 | 0.0048 | 0 | 0.997 | 0.0018 |
| slate_teal | street | Y2 | 1 | 0.0006 | 0.0023 | 0.0001 | 0.9972 | 0.0014 |
| warm_amber_haze | eye70 | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| warm_amber_haze | eye70 | T1 | 10 | 0.0216 | 0.0456 | 0 | 0.9931 | 0.0228 |
| warm_amber_haze | eye70 | T2 | 4 | 0.0194 | 0.0416 | 0 | 0.9934 | 0.026 |
| warm_amber_haze | eye70 | T3 | 1 | 0.0194 | 0.0467 | 0.0089 | 0.9956 | 0.0229 |
| warm_amber_haze | eye70 | Y1 | 4 | 0.0015 | 0.0025 | 0 | 0.997 | 0.0016 |
| warm_amber_haze | eye70 | Y2 | 1 | 0.001 | 0.0018 | 0.0001 | 0.9972 | 0.0015 |
| warm_amber_haze | street | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| warm_amber_haze | street | T1 | 10 | 0.0088 | 0.0181 | 0 | 0.9327 | 0.0121 |
| warm_amber_haze | street | T2 | 4 | 0.0078 | 0.0137 | 0 | 0.949 | 0.012 |
| warm_amber_haze | street | T3 | 1 | 0.0041 | 0.006 | 0.0027 | 0.9643 | 0.0118 |
| warm_amber_haze | street | Y1 | 4 | 0.0009 | 0.002 | 0 | 0.997 | 0.001 |
| warm_amber_haze | street | Y2 | 1 | 0.0007 | 0.0016 | 0.0001 | 0.9972 | 0.001 |
| warm_rust_sepia | eye70 | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| warm_rust_sepia | eye70 | T1 | 10 | 0.0157 | 0.0584 | 0 | 0.9931 | 0.0181 |
| warm_rust_sepia | eye70 | T2 | 4 | 0.013 | 0.0506 | 0 | 0.9934 | 0.018 |
| warm_rust_sepia | eye70 | T3 | 1 | 0.0174 | 0.0648 | 0.0117 | 0.9956 | 0.0234 |
| warm_rust_sepia | eye70 | Y1 | 4 | 0.0013 | 0.0027 | 0 | 0.997 | 0.0017 |
| warm_rust_sepia | eye70 | Y2 | 1 | 0.0008 | 0.0018 | 0.0001 | 0.9972 | 0.0013 |
| warm_rust_sepia | street | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| warm_rust_sepia | street | T1 | 10 | 0.0068 | 0.0279 | 0 | 0.9327 | 0.0087 |
| warm_rust_sepia | street | T2 | 4 | 0.0048 | 0.019 | 0 | 0.949 | 0.0084 |
| warm_rust_sepia | street | T3 | 1 | 0.0056 | 0.0222 | 0.0046 | 0.9643 | 0.0097 |
| warm_rust_sepia | street | Y1 | 4 | 0.0009 | 0.0036 | 0 | 0.997 | 0.0017 |
| warm_rust_sepia | street | Y2 | 1 | 0.0005 | 0.0019 | 0.0001 | 0.9972 | 0.0013 |

Worst value per profile and threshold group (S_px / C_px / F; limits: S0 0 / 0 / 0, Y 0.01 / 0.01 / 0.001, T12 0.12 / 0.15 / 0.005, T3 0.12 / 0.15 / 0.08):

| Profile | S0 | Y1–Y2 | T1–T2 | T3 | Sub-measures passed |
|---|---|---|---|---|---|
| lavender_peach | 0 / 0 / 0 | 0.0019 / 0.005 / 0.0002 | 0.0325 / 0.1301 / 0 | 0.0277 / 0.1121 / 0.0213 | 12 of 12 |
| slate_crimson | 0 / 0 / 0 | 0.0014 / 0.0051 / 0.0001 | 0.0304 / 0.1579 / 0 | 0.025 / 0.1214 / 0.019 | 11 of 12 |
| slate_teal | 0 / 0 / 0 | 0.0023 / 0.0072 / 0.0002 | 0.0421 / 0.1353 / 0 | 0.0273 / 0.085 / 0.0231 | 12 of 12 |
| warm_amber_haze | 0 / 0 / 0 | 0.0015 / 0.0025 / 0.0001 | 0.0216 / 0.0456 / 0 | 0.0194 / 0.0467 / 0.0089 | 12 of 12 |
| warm_rust_sepia | 0 / 0 / 0 | 0.0013 / 0.0036 / 0.0001 | 0.0157 / 0.0584 / 0 | 0.0174 / 0.0648 / 0.0117 | 12 of 12 |

### M-004@v1, five profiles × two viewpoints, `direction` anchoring, `m004direction_<profile>` sets (default is now `direction`, so these repeat the default run), position snap and rotation quantisation on

**M-004@v1, `m004direction_<profile>`** — verdict `FAIL`, registry v1.1 (sha256 5f39e5531f9e), failing gating: M-004.C_px.T12

| Sub-measure | Kind | Value | Rule | Status | Decisive frame / sequence |
|---|---|---|---|---|---|
| M-004.S0 | gating | 0 share | <= 0 | PASS | eye70_S0 (S0, warm_rust_sepia/eye70, lag 1) |
| M-004.C_px.S0 | gating | 0 share | <= 0 | PASS | eye70_S0 (S0, warm_rust_sepia/eye70, lag 1) |
| M-004.F.S0 | gating | 0 share | <= 0 | PASS | eye70_S0 (S0, warm_rust_sepia/eye70, lag 1) |
| M-004.S_px.Y | gating | 0.0023 share | <= 0.01 | PASS | eye70_Y1 (Y1, slate_teal/eye70, lag 4) |
| M-004.C_px.Y | gating | 0.0072 share | <= 0.01 | PASS | eye70_Y1 (Y1, slate_teal/eye70, lag 4) |
| M-004.F.Y | gating | 0.0002 share | <= 0.001 | PASS | eye70_Y2 (Y2, slate_teal/eye70, lag 1) |
| M-004.S_px.T12 | gating | 0.0421 share | <= 0.12 | PASS | eye70_T1 (T1, slate_teal/eye70, lag 10) |
| M-004.C_px.T12 | gating | 0.1579 share | <= 0.15 | FAIL | eye70_T1 (T1, slate_crimson/eye70, lag 10) |
| M-004.F.T12 | gating | 0 share | <= 0.005 | PASS | eye70_T1 (T1, warm_rust_sepia/eye70, lag 10) |
| M-004.S_px.T3 | gating | 0.0277 share | <= 0.12 | PASS | eye70_T3 (T3, lavender_peach/eye70, lag 1) |
| M-004.C_px.T3 | gating | 0.1214 share | <= 0.15 | PASS | eye70_T3 (T3, slate_crimson/eye70, lag 1) |
| M-004.F.T3 | gating | 0.0231 share | <= 0.08 | PASS | eye70_T3 (T3, slate_teal/eye70, lag 1) |

| Profile | Viewpoint | Sequence | Lag | S_px | C_px | F | Valid share | S pair p95 |
|---|---|---|---|---|---|---|---|---|
| lavender_peach | eye70 | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| lavender_peach | eye70 | T1 | 10 | 0.0325 | 0.1301 | 0 | 0.9931 | 0.0345 |
| lavender_peach | eye70 | T2 | 4 | 0.0269 | 0.1089 | 0 | 0.9934 | 0.0344 |
| lavender_peach | eye70 | T3 | 1 | 0.0277 | 0.1121 | 0.0213 | 0.9956 | 0.0355 |
| lavender_peach | eye70 | Y1 | 4 | 0.0019 | 0.005 | 0 | 0.997 | 0.002 |
| lavender_peach | eye70 | Y2 | 1 | 0.0012 | 0.0033 | 0.0002 | 0.9972 | 0.002 |
| lavender_peach | street | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| lavender_peach | street | T1 | 10 | 0.0135 | 0.0538 | 0 | 0.9327 | 0.0192 |
| lavender_peach | street | T2 | 4 | 0.0112 | 0.0428 | 0 | 0.949 | 0.0181 |
| lavender_peach | street | T3 | 1 | 0.0064 | 0.0213 | 0.0043 | 0.9643 | 0.0178 |
| lavender_peach | street | Y1 | 4 | 0.0012 | 0.0039 | 0 | 0.997 | 0.0017 |
| lavender_peach | street | Y2 | 1 | 0.0007 | 0.0026 | 0.0001 | 0.9972 | 0.0014 |
| slate_crimson | eye70 | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| slate_crimson | eye70 | T1 | 10 | 0.0304 | 0.1579 | 0 | 0.9931 | 0.032 |
| slate_crimson | eye70 | T2 | 4 | 0.0263 | 0.1277 | 0 | 0.9934 | 0.0326 |
| slate_crimson | eye70 | T3 | 1 | 0.025 | 0.1214 | 0.019 | 0.9956 | 0.0327 |
| slate_crimson | eye70 | Y1 | 4 | 0.0014 | 0.0051 | 0 | 0.997 | 0.0015 |
| slate_crimson | eye70 | Y2 | 1 | 0.0008 | 0.0029 | 0.0001 | 0.9972 | 0.0017 |
| slate_crimson | street | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| slate_crimson | street | T1 | 10 | 0.016 | 0.1138 | 0 | 0.9327 | 0.0207 |
| slate_crimson | street | T2 | 4 | 0.0138 | 0.0848 | 0 | 0.949 | 0.0214 |
| slate_crimson | street | T3 | 1 | 0.0076 | 0.0376 | 0.005 | 0.9643 | 0.0209 |
| slate_crimson | street | Y1 | 4 | 0.0007 | 0.0034 | 0 | 0.997 | 0.0014 |
| slate_crimson | street | Y2 | 1 | 0.0005 | 0.0024 | 0.0001 | 0.9972 | 0.0009 |
| slate_teal | eye70 | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| slate_teal | eye70 | T1 | 10 | 0.0421 | 0.1353 | 0 | 0.9931 | 0.0443 |
| slate_teal | eye70 | T2 | 4 | 0.0323 | 0.1049 | 0 | 0.9934 | 0.0442 |
| slate_teal | eye70 | T3 | 1 | 0.0273 | 0.085 | 0.0231 | 0.9956 | 0.0429 |
| slate_teal | eye70 | Y1 | 4 | 0.0023 | 0.0072 | 0 | 0.997 | 0.0027 |
| slate_teal | eye70 | Y2 | 1 | 0.0013 | 0.0037 | 0.0002 | 0.9972 | 0.0024 |
| slate_teal | street | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| slate_teal | street | T1 | 10 | 0.0214 | 0.1054 | 0 | 0.9327 | 0.0294 |
| slate_teal | street | T2 | 4 | 0.0175 | 0.0877 | 0 | 0.949 | 0.0287 |
| slate_teal | street | T3 | 1 | 0.0076 | 0.0329 | 0.0058 | 0.9643 | 0.0263 |
| slate_teal | street | Y1 | 4 | 0.0011 | 0.0048 | 0 | 0.997 | 0.0018 |
| slate_teal | street | Y2 | 1 | 0.0006 | 0.0023 | 0.0001 | 0.9972 | 0.0014 |
| warm_amber_haze | eye70 | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| warm_amber_haze | eye70 | T1 | 10 | 0.0216 | 0.0456 | 0 | 0.9931 | 0.0228 |
| warm_amber_haze | eye70 | T2 | 4 | 0.0194 | 0.0416 | 0 | 0.9934 | 0.026 |
| warm_amber_haze | eye70 | T3 | 1 | 0.0194 | 0.0467 | 0.0089 | 0.9956 | 0.0229 |
| warm_amber_haze | eye70 | Y1 | 4 | 0.0015 | 0.0025 | 0 | 0.997 | 0.0016 |
| warm_amber_haze | eye70 | Y2 | 1 | 0.001 | 0.0018 | 0.0001 | 0.9972 | 0.0015 |
| warm_amber_haze | street | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| warm_amber_haze | street | T1 | 10 | 0.0088 | 0.0181 | 0 | 0.9327 | 0.0121 |
| warm_amber_haze | street | T2 | 4 | 0.0078 | 0.0137 | 0 | 0.949 | 0.012 |
| warm_amber_haze | street | T3 | 1 | 0.0041 | 0.006 | 0.0027 | 0.9643 | 0.0118 |
| warm_amber_haze | street | Y1 | 4 | 0.0009 | 0.002 | 0 | 0.997 | 0.001 |
| warm_amber_haze | street | Y2 | 1 | 0.0007 | 0.0016 | 0.0001 | 0.9972 | 0.001 |
| warm_rust_sepia | eye70 | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| warm_rust_sepia | eye70 | T1 | 10 | 0.0157 | 0.0584 | 0 | 0.9931 | 0.0181 |
| warm_rust_sepia | eye70 | T2 | 4 | 0.013 | 0.0506 | 0 | 0.9934 | 0.018 |
| warm_rust_sepia | eye70 | T3 | 1 | 0.0174 | 0.0648 | 0.0117 | 0.9956 | 0.0234 |
| warm_rust_sepia | eye70 | Y1 | 4 | 0.0013 | 0.0027 | 0 | 0.997 | 0.0017 |
| warm_rust_sepia | eye70 | Y2 | 1 | 0.0008 | 0.0018 | 0.0001 | 0.9972 | 0.0013 |
| warm_rust_sepia | street | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| warm_rust_sepia | street | T1 | 10 | 0.0068 | 0.0279 | 0 | 0.9327 | 0.0087 |
| warm_rust_sepia | street | T2 | 4 | 0.0048 | 0.019 | 0 | 0.949 | 0.0084 |
| warm_rust_sepia | street | T3 | 1 | 0.0056 | 0.0222 | 0.0046 | 0.9643 | 0.0097 |
| warm_rust_sepia | street | Y1 | 4 | 0.0009 | 0.0036 | 0 | 0.997 | 0.0017 |
| warm_rust_sepia | street | Y2 | 1 | 0.0005 | 0.0019 | 0.0001 | 0.9972 | 0.0013 |

Worst value per profile and threshold group (S_px / C_px / F; limits: S0 0 / 0 / 0, Y 0.01 / 0.01 / 0.001, T12 0.12 / 0.15 / 0.005, T3 0.12 / 0.15 / 0.08):

| Profile | S0 | Y1–Y2 | T1–T2 | T3 | Sub-measures passed |
|---|---|---|---|---|---|
| lavender_peach | 0 / 0 / 0 | 0.0019 / 0.005 / 0.0002 | 0.0325 / 0.1301 / 0 | 0.0277 / 0.1121 / 0.0213 | 12 of 12 |
| slate_crimson | 0 / 0 / 0 | 0.0014 / 0.0051 / 0.0001 | 0.0304 / 0.1579 / 0 | 0.025 / 0.1214 / 0.019 | 11 of 12 |
| slate_teal | 0 / 0 / 0 | 0.0023 / 0.0072 / 0.0002 | 0.0421 / 0.1353 / 0 | 0.0273 / 0.085 / 0.0231 | 12 of 12 |
| warm_amber_haze | 0 / 0 / 0 | 0.0015 / 0.0025 / 0.0001 | 0.0216 / 0.0456 / 0 | 0.0194 / 0.0467 / 0.0089 | 12 of 12 |
| warm_rust_sepia | 0 / 0 / 0 | 0.0013 / 0.0036 / 0.0001 | 0.0157 / 0.0584 / 0 | 0.0174 / 0.0648 / 0.0117 | 12 of 12 |

### M-004@v1 A/B: every anchoring mode and each stabiliser off (slate_teal, eye70; C12)

Worst S_px / C_px / F per group. `snap` = position snap, `rotq` = rotation quantisation.

| Variant | S0 | Y1–Y2 | T1–T2 | T3 | Sub-measures passed |
|---|---|---|---|---|---|
| control_frame_noise | 0.107 / 0.3763 / 0.0537 | - | - | - | 0 of 3 |
| direction | 0 / 0 / 0 | 0.0023 / 0.0072 / 0.0002 | 0.0421 / 0.1353 / 0 | 0.0273 / 0.085 / 0.0231 | 12 of 12 |
| direction_norotq | 0 / 0 / 0 | 0.1942 / 0.6613 / 0.1447 | 0.0421 / 0.1357 / 0 | 0.0277 / 0.0872 / 0.0235 | 9 of 12 |
| direction_nosnap | 0 / 0 / 0 | 0.0023 / 0.0072 / 0.0002 | 0.0434 / 0.1394 / 0.0013 | 0.0273 / 0.085 / 0.0231 | 12 of 12 |
| hybrid | 0 / 0 / 0 | 0.008 / 0.0167 / 0.0017 | 0.0095 / 0.0199 / 0 | 0.0039 / 0.0076 / 0.001 | 10 of 12 |
| hybrid_norotq | 0 / 0 / 0 | 0.1596 / 0.6066 / 0.1178 | 0.0095 / 0.0203 / 0 | 0.0039 / 0.0082 / 0.0009 | 9 of 12 |
| hybrid_nosnap | 0 / 0 / 0 | 0.008 / 0.0167 / 0.0017 | 0.0093 / 0.0192 / 0.0098 | 0.0039 / 0.0076 / 0.001 | 9 of 12 |
| screen | 0 / 0 / 0 | 0.1941 / 0.6615 / 0.1445 | 0.0419 / 0.1354 / 0 | 0.0275 / 0.0864 / 0.0235 | 9 of 12 |
| screen_nostab | 0 / 0 / 0 | 0.1942 / 0.6613 / 0.1447 | 0.0434 / 0.1399 / 0.0013 | 0.0277 / 0.0872 / 0.0235 | 9 of 12 |

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
| c2_grid_bilinear_control | `metrics/checks/c2_grid_bilinear_control.json` | no | uniform blocks at the declared offset [0, 0]: min 0.6116; with the offset ignored: min 0.6116 |
| c2_grid_declared_offset | `metrics/checks/c2_grid_declared_offset.json` | yes | uniform blocks at the declared offset [3, 0]: min 1; with the offset ignored: min 0.8002 |
| c6_layers_lavender_peach | `metrics/checks/c6_layers_lavender_peach.json` | no | frame 0 mean L*: 1km 33.6341, 3km 45.6838, 10km 54.9944, 30km 54.9944; successively lighter in 5 of 12 frames; far layer minus sky at horizon 8.6303 L*; near field darkest in 12 of 12; pure share of ground seam texels 0.9991 (154156 of 154296); modal lightness never falls across a seam in 12 of 12 frames |
| c6_layers_slate_crimson | `metrics/checks/c6_layers_slate_crimson.json` | no | frame 0 mean L*: 1km 27.4157, 3km 36.3917, 10km 49.7243, 30km 49.7243; successively lighter in 1 of 12 frames; far layer minus sky at horizon 7.5038 L*; near field darkest in 12 of 12; pure share of ground seam texels 1 (69834 of 69834); modal lightness never falls across a seam in 12 of 12 frames |
| c6_layers_slate_teal | `metrics/checks/c6_layers_slate_teal.json` | no | frame 0 mean L*: 1km 45.9828, 3km 54.4944, 10km 67.0855, 30km 67.0855; successively lighter in 3 of 12 frames; far layer minus sky at horizon 10.4921 L*; near field darkest in 12 of 12; pure share of ground seam texels 1 (144815 of 144815); modal lightness never falls across a seam in 12 of 12 frames |
| c6_layers_warm_amber_haze | `metrics/checks/c6_layers_warm_amber_haze.json` | no | frame 0 mean L*: 1km 58.4812, 3km 69.3535, 10km 78.4001, 30km 78.4001; successively lighter in 1 of 12 frames; far layer minus sky at horizon 9.627 L*; near field darkest in 12 of 12; pure share of ground seam texels 1 (137914 of 137914); modal lightness never falls across a seam in 12 of 12 frames |
| c6_layers_warm_rust_sepia | `metrics/checks/c6_layers_warm_rust_sepia.json` | no | frame 0 mean L*: 1km 46.5474, 3km 54.0466, 10km 65.6744, 30km 65.6744; successively lighter in 2 of 12 frames; far layer minus sky at horizon 9.7568 L*; near field darkest in 12 of 12; pure share of ground seam texels 1 (71882 of 71882); modal lightness never falls across a seam in 12 of 12 frames |
| c7_blocks_lavender_peach | `metrics/checks/c7_blocks_lavender_peach.json` | yes | near_150m: in view in 4 frames, dL* min 11.925, median 17.9162, frames with dL* >= 4: 4; far_1km: in view in 9 frames, dL* min 8.2851, median 9.7385, frames with dL* >= 4: 9 |
| c7_blocks_slate_crimson | `metrics/checks/c7_blocks_slate_crimson.json` | yes | near_150m: in view in 4 frames, dL* min 16.662, median 19.5572, frames with dL* >= 4: 4; far_1km: in view in 9 frames, dL* min 5.4051, median 5.723, frames with dL* >= 4: 9 |
| c7_blocks_slate_teal | `metrics/checks/c7_blocks_slate_teal.json` | yes | near_150m: in view in 4 frames, dL* min 7.5598, median 15.2789, frames with dL* >= 4: 4; far_1km: in view in 9 frames, dL* min 4.2895, median 6.0257, frames with dL* >= 4: 9 |
| c7_blocks_warm_amber_haze | `metrics/checks/c7_blocks_warm_amber_haze.json` | yes | near_150m: in view in 4 frames, dL* min 14.2443, median 20.926, frames with dL* >= 4: 4; far_1km: in view in 9 frames, dL* min 4.8725, median 6.6383, frames with dL* >= 4: 9 |
| c7_blocks_warm_rust_sepia | `metrics/checks/c7_blocks_warm_rust_sepia.json` | yes | near_150m: in view in 4 frames, dL* min 11.1834, median 14.9018, frames with dL* >= 4: 4; far_1km: in view in 9 frames, dL* min 5.6064, median 6.0582, frames with dL* >= 4: 9 |
| c7_scale_lavender_peach | `metrics/checks/c7_scale_lavender_peach.json` | yes | marker 2 px, height 2 px, dL* 19.0972; 80 m block height 31.9496 px at 1043 m, dL* to background 11.2077 |
| c7_scale_slate_crimson | `metrics/checks/c7_scale_slate_crimson.json` | yes | marker 1 px, height 1 px, dL* 22.5531; 80 m block height 31.9496 px at 1043 m, dL* to background 8.9899 |
| c7_scale_slate_teal | `metrics/checks/c7_scale_slate_teal.json` | no | marker 12 px, height 2 px, dL* 4.2019; 80 m block height 31.9496 px at 1043 m, dL* to background 6.9608 |
| c7_scale_warm_amber_haze | `metrics/checks/c7_scale_warm_amber_haze.json` | yes | marker 1 px, height 1 px, dL* 23.9371; 80 m block height 31.9496 px at 1043 m, dL* to background 8.688 |
| c7_scale_warm_rust_sepia | `metrics/checks/c7_scale_warm_rust_sepia.json` | yes | marker 1 px, height 1 px, dL* 19.127; 80 m block height 31.9496 px at 1043 m, dL* to background 7.3903 |

### Per-pass timing (development Mac; GPU ms not measurable here; M-008 is not verified)

Hardware: Apple M5 Pro, Metal, Mac OS X 26.6.2, Unity 6000.6.9f1, batch mode, vsync 0. Method: `Recorder` on each `Pixel.*` sampler, 1000 frames measured after 123 warm-up frames discarded, capture off. `SystemInfo.supportsGpuRecorder` is false on Metal in batch mode, so GPU ms is 0 in the CSV and only CPU ms per pass is meaningful (`timing/pass_timing_summary.json`, `timing/pass_timing_*.csv`). CI is a normal approximation and ignores autocorrelation.

| Output | Internal | Pass | CPU mean ms | CPU p95 ms | CPU 95 % CI of mean (ms) | CPU max ms | GPU ms |
|---|---|---|---|---|---|---|---|
| 1920x1080 | 480x270 | Pixel.Dither | 0.0165 | 0.0193 | 0.0163 to 0.0167 | 0.0681 | not measurable (recorder unsupported) |
| 1920x1080 | 480x270 | Pixel.Downsample | 0 | 0 | 0 to 0 | 0 | not measurable (recorder unsupported) |
| 1920x1080 | 480x270 | Pixel.Haze | 0.0155 | 0.017 | 0.0152 to 0.0158 | 0.111 | not measurable (recorder unsupported) |
| 1920x1080 | 480x270 | Pixel.Outline | 0 | 0 | 0 to 0 | 0 | not measurable (recorder unsupported) |
| 1920x1080 | 480x270 | Pixel.Present | 0.0098 | 0.013 | 0.0096 to 0.01 | 0.0577 | not measurable (recorder unsupported) |
| 1920x1080 | 480x270 | Pixel.Quantise | 0.0149 | 0.0166 | 0.0145 to 0.0152 | 0.1064 | not measurable (recorder unsupported) |
| 1920x1080 | 480x270 | Pixel.Store | 0.0168 | 0.0183 | 0.0164 to 0.0173 | 0.1242 | not measurable (recorder unsupported) |
| 2560x1440 | 512x288 | Pixel.Dither | 0.0185 | 0.0296 | 0.018 to 0.019 | 0.0976 | not measurable (recorder unsupported) |
| 2560x1440 | 512x288 | Pixel.Downsample | 0 | 0 | 0 to 0 | 0 | not measurable (recorder unsupported) |
| 2560x1440 | 512x288 | Pixel.Haze | 0.0167 | 0.0248 | 0.0162 to 0.0171 | 0.107 | not measurable (recorder unsupported) |
| 2560x1440 | 512x288 | Pixel.Outline | 0 | 0 | 0 to 0 | 0 | not measurable (recorder unsupported) |
| 2560x1440 | 512x288 | Pixel.Present | 0.0118 | 0.0225 | 0.0112 to 0.0123 | 0.1107 | not measurable (recorder unsupported) |
| 2560x1440 | 512x288 | Pixel.Quantise | 0.016 | 0.0232 | 0.0156 to 0.0164 | 0.0928 | not measurable (recorder unsupported) |
| 2560x1440 | 512x288 | Pixel.Store | 0.0177 | 0.0266 | 0.0172 to 0.0181 | 0.1041 | not measurable (recorder unsupported) |

`Pixel.Downsample` and `Pixel.Outline` read 0: those passes are not part of the M0 chain. `Pixel.Present` is isolated.

GC with capture off, 600 frames: 1920x1080: 0 allocation samples, 0 collections, mono used delta 0 bytes; 2560x1440: 0 allocation samples, 0 collections, mono used delta 0 bytes.
