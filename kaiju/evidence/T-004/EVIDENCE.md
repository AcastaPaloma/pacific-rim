# T-004 evidence: pixel pipeline renderer features and third-person rig stabilisation

Branch `task/T-004`. All paths are relative to the project root (`kaiju/`). Thresholds are those of `research/R-002.md` (`M-003@v1`, `M-004@v1`) as registered in the T-002 tools; none was changed. Captures come straight from the engine's render targets (PNG encoding only); nothing was post-processed outside the engine.

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

## Results at a glance
- **M-003@v1** (T-002 tool, 180 stills + haze calibration): all eight measured gating sub-measures pass for all five profiles (a.output, a.internal, b.DIG, c1, c2, d-i, d-ii, d-iv); M-003.f (blind panel) is pending by design (milestone gate). Tool verdict `INCOMPLETE` (panel missing), headless `PASS (provisional: 2 assumed)`. Five advisory sub-measures miss (b.DIG_max, d-iii, e.sky, e.horizon, e.contrast): listed under "Not met".
- **M-004@v1** (T-002 tool, five profiles × two viewpoints, configured default anchoring `hybrid`): 8 of 12 gating sub-measures pass; tool verdict `FAIL`. Static: exactly 0. Translation: S_px, F and T3 pass; C_px.T12 misses by 0.0007 (0.1507 against 0.15, warm_rust_sepia street T1). Yaw: all three miss (S_px 0.0351, C_px 0.0505, F 0.0131 against 0.01 / 0.01 / 0.001).
- **M-004@v1 with `direction` anchoring** (same five profiles × two viewpoints): 7 of 12 pass; tool verdict `FAIL`. Static 0; yaw passes with margin (S_px 0.0013, C_px 0.0020, F 0.0001); translation misses (S_px.T12 0.1961, C_px.T12 0.3023, S_px.T3 0.1933, C_px.T3 0.2955, F.T3 0.0863; worst in the warm profiles at street level).
- **Anchoring and stabiliser A/B** (slate_teal, eye-height): `direction` 10 of 12, `hybrid` 9 of 12, `screen` 7 of 12; each stabiliser switched off makes its sequence worse (table below). No mode meets both the yaw and the translation thresholds. Per the reconciled spec ("How a miss on C9/C10 is handled") this goes to calibration; nothing was relaxed.
- Tests: `dotnet test` 41 + 16 (+ Core 89) pass; Unity EditMode 170 of 170 pass (54 Rendering.Pixel, 22 Camera, the rest Core).

## Criterion → artifact
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

## Not met, and why
1. **C9 yaw (M-004.S_px.Y, C_px.Y, F.Y) with the configured default `hybrid`.** Achieved 0.0351 / 0.0505 / 0.0131 against 0.01 / 0.01 / 0.001. The surface-anchored pattern near the camera is resampled at a different sub-texel phase after each rotation step away from the screen centre. `direction` meets all three on every profile (worst 0.0013 / 0.0020 / 0.0001).
2. **C10 translation crawl.** `hybrid`: C_px.T12 0.1507 against 0.15 (one sequence, warm_rust_sepia street T1; the other nine profile-viewpoints are 0.062–0.096). `direction`: S_px up to 0.196 and C_px up to 0.302 over the five profiles, because a screen pattern cannot follow parallax. No mode meets yaw and translation together; A/B numbers for every mode and stabiliser are stored. Thresholds were not changed (they are Assumed pending calibration, R-002 §5).
3. **C7 at 1 km for three profiles.** One haze band of separation is there (the block and the ground and forms behind it are one or two bands apart), but with `ditherSpread` 0.4 neighbouring bands often land on the same palette colour in slate_teal, lavender_peach and slate_crimson, whose palettes have few mid-value entries, so ΔL* falls below 4. Raising `ditherSpread` restores the mean value step and raises DIG_max further; left for the style panel.
4. **C6 seam indices in slate_crimson** (32 of 44 ground seams): a few seam pixels carry the next lighter index.
5. **C20.** Present p95 above 0.5 ms as measured (0.74 / 0.60 ms): the number includes the full second camera and the capture path's copy, not only the present pass. Stage chain p95 0.52 ms at 1080p. Per-pass GPU markers could not be read in a batch run (the GPU recorder needs a player loop), so costs are paired wall-time differences. M-008 unverified.
6. **M-003 advisories:** DIG_max 0.6489 (> 0.5: Bayer with a 0.75-band haze seam and spread 0.4 dithers much of the ground and sky); accent share below 0.1 % in frames where the beacon is small; sky share min 0.178 (< 0.2) and horizon max 0.474 (> 0.45) in the elevated views; frame contrast min 12.7 (< 25; slate_crimson's whole ramp spans 30 L*).
7. **Validator and sub-pixel compensation.** The T-002 validator rejects sequences whose frames differ in `resolution.output_block_offset_px` / `subpixel_offset_internal_px`; R-002 §6 defines them per frame. The M-004 sets were therefore captured with `rendering.pixel.subPixelPresent` off (applied offset zero). The internal image, index and depth do not depend on that flag; the compensated output is in `c11_subpixel_on` (invalid by that validator rule only). Proposed as a contract question in `tasks/T-004.impl.md`.
8. **Not built:** cockpit rig, quantisation hysteresis, light shafts and smoke (later slices); no PlayMode test (EditMode capture tests and rig tests cover the behaviour); the blind panel is the founders' gate.

## Stored values
### M-003@v1 on the 180 stills (5 profiles × 3 viewpoints × 12 frames) and the haze calibration set

**M-003@v1, `m003_stills` + `haze_cal`** — verdict `PASS`, registry v1.1 (sha256 7a63afd06c66), failing gating: none

| Sub-measure | Kind | Value | Rule | Status | Decisive frame / sequence |
|---|---|---|---|---|---|
| M-003.a.output | gating | 1 share | >= 0.95 | PASS | A_2 output worst frame m003_stills/lavender_peach_elevated300/f000000 |
| M-003.a.internal | gating | 1 share | >= 0.995 | PASS | A_2 internal worst frame m003_stills/lavender_peach_elevated300/f000000 |
| M-003.a.A3 | info | 1 share |  - | REPORTED |  |
| M-003.a.A5 | info | 1 share |  - | REPORTED |  |
| M-003.b.DIG | gating | 1 share of frames | >= 0.8 | PASS | lavender_peach: share of evaluated frames with DIG >= dig_min |
| M-003.b.DIG_max | advisory | 0.6489 DIG | <= 0.5 | FAIL | max DIG m003_stills/warm_rust_sepia_elevated300/f000009 |
| M-003.b.O | advisory | 0.7528 O |  - | REPORTED |  |
| M-003.c1 | gating | 0.9643 rho | >= 0.9 | PASS | m003_stills/lavender_peach_street/f000003: Spearman rho(band, L*) |
| M-003.c2 | gating | 0 L* | <= 0 | PASS | haze_cal/lavender_peach/f000000: max drop of pair mean L* |
| M-003.d-i | gating | 8 levels | <= 12 | PASS | value levels m003_stills/warm_amber_haze_street/f000000 |
| M-003.d-ii | gating | 2 families | <= 3 | PASS | hue families m003_stills/lavender_peach_elevated300/f000008 |
| M-003.d-iii | advisory | 0.0004 share | in 0.001 | FAIL | accent share m003_stills/lavender_peach_elevated300/f000009 |
| M-003.d-iv | gating | 1 share of blocks | >= 0.995 | PASS | uniform blocks m003_stills/lavender_peach_elevated300/f000000 |
| M-003.e.sky | advisory | 0.1778  | in 0.2 | FAIL | sky_share m003_stills/lavender_peach_street/f000005 |
| M-003.e.horizon | advisory | 0.4741  | <= 0.45 | FAIL | horizon m003_stills/lavender_peach_elevated300/f000000 |
| M-003.e.contrast | advisory | 12.7418  | >= 25 | FAIL | contrast m003_stills/slate_crimson_elevated300/f000002 |
| M-003.f | gating | -  |  - | PENDING | no panel_ratings@v1 file given (the tool never defaults a score) |

| Profile | Frames | a: min A₂ internal | a: min A₂ output | b: frames DIG ≥ 0.15 / evaluated | b: DIG min / max | b: median O | c1: max drop L* | c1: min ρ | c1: min span L* | d-i: max levels | d-ii: max hue families | d-iii: accent share min / max | d-iv: min uniform blocks | e: sky share min / max | e: horizon max | e: contrast min |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| lavender_peach | 36 | 1 | 1 | 36 / 36 | 0.3154 / 0.6034 | 0.5981 | 0.0624 | 0.9643 | 29.5671 | 7 | 2 | 0 / 0.0016 | 1 | 0.1778 / 0.4837 | 0.4741 | 15.2939 |
| slate_crimson | 36 | 1 | 1 | 36 / 36 | 0.2804 / 0.5419 | 1.0568 | 0.1366 | 0.9643 | 17.5387 | 5 | 1 | 0 / 0.0016 | 1 | 0.1778 / 0.4837 | 0.4741 | 12.7418 |
| slate_teal | 36 | 1 | 1 | 36 / 36 | 0.3234 / 0.5166 | 1.0525 | 0 | 1 | 23.1397 | 6 | 2 | no accent | 1 | 0.1778 / 0.4837 | 0.4741 | 21.1027 |
| warm_amber_haze | 36 | 1 | 1 | 36 / 36 | 0.3832 / 0.623 | 0.5732 | 0 | 1 | 28.5092 | 8 | 1 | 0 / 0.0016 | 1 | 0.1778 / 0.4837 | 0.4741 | 20.9153 |
| warm_rust_sepia | 36 | 1 | 1 | 36 / 36 | 0.3495 / 0.6489 | 0.6606 | 0.688 | 0.9643 | 25.0265 | 7 | 1 | 0 / 0.0016 | 1 | 0.1778 / 0.4837 | 0.4741 | 19.717 |

### M-003@v1, detail views (sky gradient, gradient-lit surface, scale view), 3 frames per profile: fewer than the 12 the metric requires, so the tool's verdict is about coverage; the values are what C4 asks for individually

**m003_details** — verdict `INCOMPLETE`, registry v1.1 (sha256 7a63afd06c66), failing gating: none

| Sub-measure | Kind | Value | Rule | Status | Decisive frame / sequence |
|---|---|---|---|---|---|
| M-003.a.output | gating | 1 share | >= 0.95 | PASS | A_2 output worst frame m003_details/lavender_peach_detail_gradient/f000000 |
| M-003.a.internal | gating | 1 share | >= 0.995 | PASS | A_2 internal worst frame m003_details/lavender_peach_detail_gradient/f000000 |
| M-003.a.A3 | info | 1 share |  - | REPORTED |  |
| M-003.a.A5 | info | 1 share |  - | REPORTED |  |
| M-003.b.DIG | gating | 1 share of frames | >= 0.8 | PASS | lavender_peach: share of evaluated frames with DIG >= dig_min |
| M-003.b.DIG_max | advisory | 0.61 DIG | <= 0.5 | FAIL | max DIG m003_details/slate_crimson_detail_sky/f000000 |
| M-003.b.O | advisory | 0.7464 O |  - | REPORTED |  |
| M-003.c1 | gating | 1 rho | >= 0.9 | PASS | m003_details/lavender_peach_detail_sky/f000000: Spearman rho(band, L*) |
| M-003.c2 | gating | 0 L* | <= 0 | PASS | haze_cal/lavender_peach/f000000: max drop of pair mean L* |
| M-003.d-i | gating | 8 levels | <= 12 | PASS | value levels m003_details/warm_amber_haze_detail_scale/f000000 |
| M-003.d-ii | gating | 2 families | <= 3 | PASS | hue families m003_details/lavender_peach_detail_gradient/f000000 |
| M-003.d-iii | advisory | 0.0013 share | in 0.001 | PASS | accent share m003_details/lavender_peach_detail_scale/f000000 |
| M-003.d-iv | gating | 1 share of blocks | >= 0.995 | PASS | uniform blocks m003_details/lavender_peach_detail_gradient/f000000 |
| M-003.e.sky | advisory | 0.8314  | in 0.2 | FAIL | sky_share m003_details/lavender_peach_detail_sky/f000000 |
| M-003.e.horizon | advisory | 0.4444  | <= 0.45 | PASS | horizon m003_details/lavender_peach_detail_gradient/f000000 |
| M-003.e.contrast | advisory | 12.7418  | >= 25 | FAIL | contrast m003_details/slate_crimson_detail_sky/f000000 |
| M-003.f | gating | -  |  - | PENDING | no panel_ratings@v1 file given (the tool never defaults a score) |

| Frame | DIG | O | gradient share | A₂ internal | grid uniform |
|---|---|---|---|---|---|
| m003_details/lavender_peach_detail_gradient/f000000 | 0.5079 | 0.2806 | 0.1358 | 1 | 1 |
| m003_details/lavender_peach_detail_scale/f000000 | 0.313 | 0.6129 | 0.2508 | 1 | 1 |
| m003_details/lavender_peach_detail_sky/f000000 | 0.5521 | 0.7464 | 0.1969 | 1 | 1 |
| m003_details/slate_crimson_detail_gradient/f000000 | 0.33 | 1.0131 | 0.0992 | 1 | 1 |
| m003_details/slate_crimson_detail_scale/f000000 | 0.3254 | 0.8964 | 0.2198 | 1 | 1 |
| m003_details/slate_crimson_detail_sky/f000000 | 0.61 | 0.868 | 0.1328 | 1 | 1 |
| m003_details/slate_teal_detail_gradient/f000000 | 0.5144 | 0.5031 | 0.1431 | 1 | 1 |
| m003_details/slate_teal_detail_scale/f000000 | 0.3248 | 0.9326 | 0.2482 | 1 | 1 |
| m003_details/slate_teal_detail_sky/f000000 | 0.5314 | 1.2104 | 0.2152 | 1 | 1 |
| m003_details/warm_amber_haze_detail_gradient/f000000 | 0.3945 | -0.0849 | 0.1923 | 1 | 1 |
| m003_details/warm_amber_haze_detail_scale/f000000 | 0.3782 | 0.6565 | 0.2747 | 1 | 1 |
| m003_details/warm_amber_haze_detail_sky/f000000 | 0.5319 | 0.9242 | 0.1901 | 1 | 1 |
| m003_details/warm_rust_sepia_detail_gradient/f000000 | 0.3953 | 0.6406 | 0.1476 | 1 | 1 |
| m003_details/warm_rust_sepia_detail_scale/f000000 | 0.3159 | 0.3718 | 0.2753 | 1 | 1 |
| m003_details/warm_rust_sepia_detail_sky/f000000 | 0.5175 | 0.9234 | 0.1893 | 1 | 1 |

### M-003@v1, 2560×1440 output (k = 5, 512×288 internal), one frame per profile

**m003_1440p** — verdict `INCOMPLETE`, registry v1.1 (sha256 7a63afd06c66), failing gating: none

| Sub-measure | Kind | Value | Rule | Status | Decisive frame / sequence |
|---|---|---|---|---|---|
| M-003.a.output | gating | 1 share | >= 0.95 | PASS | A_2 output worst frame m003_stills_1440p/lavender_peach_eye70/f000000 |
| M-003.a.internal | gating | 1 share | >= 0.995 | PASS | A_2 internal worst frame m003_stills_1440p/lavender_peach_eye70/f000000 |
| M-003.a.A3 | info | 1 share |  - | REPORTED |  |
| M-003.a.A5 | info | 1 share |  - | REPORTED |  |
| M-003.b.DIG | gating | 1 share of frames | >= 0.8 | PASS | lavender_peach: share of evaluated frames with DIG >= dig_min |
| M-003.b.DIG_max | advisory | 0.6141 DIG | <= 0.5 | FAIL | max DIG m003_stills_1440p/warm_amber_haze_eye70/f000000 |
| M-003.b.O | advisory | 0.3771 O |  - | REPORTED |  |
| M-003.c1 | gating | 1 rho | >= 0.9 | PASS | m003_stills_1440p/lavender_peach_eye70/f000000: Spearman rho(band, L*) |
| M-003.c2 | gating | 0 L* | <= 0 | PASS | haze_cal/lavender_peach/f000000: max drop of pair mean L* |
| M-003.d-i | gating | 7 levels | <= 12 | PASS | value levels m003_stills_1440p/warm_amber_haze_eye70/f000000 |
| M-003.d-ii | gating | 1 families | <= 3 | PASS | hue families m003_stills_1440p/lavender_peach_eye70/f000000 |
| M-003.d-iii | advisory | -  |  - | NOT_APPLICABLE | no accent present in any frame (applies only when present) |
| M-003.d-iv | gating | 1 share of blocks | >= 0.995 | PASS | uniform blocks m003_stills_1440p/lavender_peach_eye70/f000000 |
| M-003.e.sky | advisory | 0.3794  | in 0.2 | PASS | sky_share m003_stills_1440p/lavender_peach_eye70/f000000 |
| M-003.e.horizon | advisory | 0.4201  | <= 0.45 | PASS | horizon m003_stills_1440p/lavender_peach_eye70/f000000 |
| M-003.e.contrast | advisory | 18.436  | >= 25 | FAIL | contrast m003_stills_1440p/slate_crimson_eye70/f000000 |
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
| M-003.b.DIG_max | advisory | 0.51 DIG | <= 0.5 | FAIL | max DIG m003_stills_2160p/slate_teal_eye70/f000000 |
| M-003.b.O | advisory | 0.9084 O |  - | REPORTED |  |
| M-003.c1 | gating | 1 rho | >= 0.9 | PASS | m003_stills_2160p/slate_teal_eye70/f000000: Spearman rho(band, L*) |
| M-003.c2 | gating | 0 L* | <= 0 | PASS | haze_cal/lavender_peach/f000000: max drop of pair mean L* |
| M-003.d-i | gating | 6 levels | <= 12 | PASS | value levels m003_stills_2160p/slate_teal_eye70/f000000 |
| M-003.d-ii | gating | 1 families | <= 3 | PASS | hue families m003_stills_2160p/slate_teal_eye70/f000000 |
| M-003.d-iii | advisory | -  |  - | NOT_APPLICABLE | no accent present in any frame (applies only when present) |
| M-003.d-iv | gating | 1 share of blocks | >= 0.995 | PASS | uniform blocks m003_stills_2160p/slate_teal_eye70/f000000 |
| M-003.e.sky | advisory | 0.3791  | in 0.2 | PASS | sky_share m003_stills_2160p/slate_teal_eye70/f000000 |
| M-003.e.horizon | advisory | 0.4194  | <= 0.45 | PASS | horizon m003_stills_2160p/slate_teal_eye70/f000000 |
| M-003.e.contrast | advisory | 32.8015  | >= 25 | PASS | contrast m003_stills_2160p/slate_teal_eye70/f000000 |
| M-003.f | gating | -  |  - | PENDING | no panel_ratings@v1 file given (the tool never defaults a score) |

### Negative controls on the same tool (slate_teal, eye70, 12 frames each): the tests can fail

| Control | Verdict | Failing gating sub-measures | a.internal | a.output | b.DIG share | c1 value | d-iv |
|---|---|---|---|---|---|---|---|
| bilinear | FAIL | M-003.a.output, M-003.d-iv | 1 (PASS) | 0.8065 (FAIL) | 1 (PASS) | 1 (PASS) | 0.4486 (FAIL) |
| dither_off | FAIL | M-003.b.DIG | 1 (PASS) | 1 (PASS) | 0 (FAIL) | 1 (PASS) | 1 (PASS) |
| haze_off | FAIL | M-003.c1 | 1 (PASS) | 1 (PASS) | 1 (PASS) | -0.0857 (FAIL) | 1 (PASS) |
| reversed_haze | FAIL | M-003.c1 | 1 (PASS) | 1 (PASS) | 1 (PASS) | 12.728 (FAIL) | 1 (PASS) |
| subpixel_offset | INCOMPLETE | none | 1 (PASS) | 1 (PASS) | 1 (PASS) | 1 (PASS) | 1 (PASS) |
| unquantised | FAIL | M-003.a.output, M-003.a.internal, M-003.b.DIG | 0 (FAIL) | 0 (FAIL) | 0 (FAIL) | 1 (PASS) | 1 (PASS) |

### M-004@v1, five profiles × two viewpoints, configured default anchoring (`hybrid`), position snap and rotation quantisation on

**M-004@v1, `m004_<profile>`** — verdict `FAIL`, registry v1.1 (sha256 5f39e5531f9e), failing gating: M-004.S_px.Y, M-004.C_px.Y, M-004.F.Y, M-004.C_px.T12

| Sub-measure | Kind | Value | Rule | Status | Decisive frame / sequence |
|---|---|---|---|---|---|
| M-004.S0 | gating | 0 share | <= 0 | PASS | eye70_S0 (S0, warm_rust_sepia/eye70, lag 1) |
| M-004.C_px.S0 | gating | 0 share | <= 0 | PASS | eye70_S0 (S0, warm_rust_sepia/eye70, lag 1) |
| M-004.F.S0 | gating | 0 share | <= 0 | PASS | eye70_S0 (S0, warm_rust_sepia/eye70, lag 1) |
| M-004.S_px.Y | gating | 0.0351 share | <= 0.01 | FAIL | street_Y1 (Y1, slate_teal/street, lag 4) |
| M-004.C_px.Y | gating | 0.0505 share | <= 0.01 | FAIL | eye70_Y1 (Y1, warm_amber_haze/eye70, lag 4) |
| M-004.F.Y | gating | 0.0131 share | <= 0.001 | FAIL | street_Y2 (Y2, slate_teal/street, lag 1) |
| M-004.S_px.T12 | gating | 0.0974 share | <= 0.12 | PASS | street_T1 (T1, warm_rust_sepia/street, lag 10) |
| M-004.C_px.T12 | gating | 0.1507 share | <= 0.15 | FAIL | street_T1 (T1, warm_rust_sepia/street, lag 10) |
| M-004.F.T12 | gating | 0 share | <= 0.005 | PASS | eye70_T1 (T1, warm_rust_sepia/eye70, lag 10) |
| M-004.S_px.T3 | gating | 0.0858 share | <= 0.12 | PASS | street_T3 (T3, warm_rust_sepia/street, lag 1) |
| M-004.C_px.T3 | gating | 0.1303 share | <= 0.15 | PASS | street_T3 (T3, warm_rust_sepia/street, lag 1) |
| M-004.F.T3 | gating | 0.031 share | <= 0.08 | PASS | street_T3 (T3, warm_rust_sepia/street, lag 1) |

| Profile | Viewpoint | Sequence | Lag | S_px | C_px | F | Valid share | S pair p95 |
|---|---|---|---|---|---|---|---|---|
| lavender_peach | eye70 | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| lavender_peach | eye70 | T1 | 10 | 0.0339 | 0.0697 | 0 | 0.9931 | 0.038 |
| lavender_peach | eye70 | T2 | 4 | 0.033 | 0.0635 | 0 | 0.9934 | 0.0496 |
| lavender_peach | eye70 | T3 | 1 | 0.0242 | 0.0439 | 0.0137 | 0.9956 | 0.0355 |
| lavender_peach | eye70 | Y1 | 4 | 0.0195 | 0.0376 | 0 | 0.997 | 0.0209 |
| lavender_peach | eye70 | Y2 | 1 | 0.0217 | 0.041 | 0.0058 | 0.9972 | 0.0259 |
| lavender_peach | street | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| lavender_peach | street | T1 | 10 | 0.0167 | 0.0644 | 0 | 0.9327 | 0.0217 |
| lavender_peach | street | T2 | 4 | 0.0197 | 0.0598 | 0 | 0.949 | 0.0285 |
| lavender_peach | street | T3 | 1 | 0.0105 | 0.0297 | 0.0046 | 0.9643 | 0.0229 |
| lavender_peach | street | Y1 | 4 | 0.0054 | 0.0176 | 0 | 0.997 | 0.0057 |
| lavender_peach | street | Y2 | 1 | 0.0076 | 0.0253 | 0.002 | 0.9972 | 0.0134 |
| slate_crimson | eye70 | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| slate_crimson | eye70 | T1 | 10 | 0.0236 | 0.0615 | 0 | 0.9931 | 0.0281 |
| slate_crimson | eye70 | T2 | 4 | 0.0227 | 0.0557 | 0 | 0.9934 | 0.0398 |
| slate_crimson | eye70 | T3 | 1 | 0.0112 | 0.0265 | 0.0069 | 0.9956 | 0.0243 |
| slate_crimson | eye70 | Y1 | 4 | 0.0092 | 0.0181 | 0 | 0.997 | 0.0099 |
| slate_crimson | eye70 | Y2 | 1 | 0.0121 | 0.0258 | 0.0036 | 0.9972 | 0.0178 |
| slate_crimson | street | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| slate_crimson | street | T1 | 10 | 0.0154 | 0.0579 | 0 | 0.9327 | 0.0202 |
| slate_crimson | street | T2 | 4 | 0.0179 | 0.0574 | 0 | 0.949 | 0.0269 |
| slate_crimson | street | T3 | 1 | 0.0086 | 0.0256 | 0.0038 | 0.9643 | 0.0203 |
| slate_crimson | street | Y1 | 4 | 0.0041 | 0.0134 | 0 | 0.997 | 0.0044 |
| slate_crimson | street | Y2 | 1 | 0.0066 | 0.0221 | 0.0018 | 0.9972 | 0.0127 |
| slate_teal | eye70 | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| slate_teal | eye70 | T1 | 10 | 0.0399 | 0.0772 | 0 | 0.9931 | 0.044 |
| slate_teal | eye70 | T2 | 4 | 0.0374 | 0.0717 | 0 | 0.9934 | 0.0601 |
| slate_teal | eye70 | T3 | 1 | 0.0231 | 0.0478 | 0.0132 | 0.9956 | 0.0402 |
| slate_teal | eye70 | Y1 | 4 | 0.0214 | 0.0334 | 0 | 0.997 | 0.0227 |
| slate_teal | eye70 | Y2 | 1 | 0.0207 | 0.0406 | 0.0061 | 0.9972 | 0.0248 |
| slate_teal | street | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| slate_teal | street | T1 | 10 | 0.0376 | 0.0863 | 0 | 0.9327 | 0.0518 |
| slate_teal | street | T2 | 4 | 0.0315 | 0.0822 | 0 | 0.949 | 0.0534 |
| slate_teal | street | T3 | 1 | 0.013 | 0.0354 | 0.0062 | 0.9643 | 0.034 |
| slate_teal | street | Y1 | 4 | 0.0351 | 0.0352 | 0 | 0.997 | 0.0381 |
| slate_teal | street | Y2 | 1 | 0.0345 | 0.0406 | 0.0131 | 0.9972 | 0.0382 |
| warm_amber_haze | eye70 | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| warm_amber_haze | eye70 | T1 | 10 | 0.0549 | 0.0959 | 0 | 0.9931 | 0.0615 |
| warm_amber_haze | eye70 | T2 | 4 | 0.0507 | 0.0869 | 0 | 0.9934 | 0.0727 |
| warm_amber_haze | eye70 | T3 | 1 | 0.0383 | 0.068 | 0.0275 | 0.9956 | 0.055 |
| warm_amber_haze | eye70 | Y1 | 4 | 0.03 | 0.0505 | 0 | 0.997 | 0.0323 |
| warm_amber_haze | eye70 | Y2 | 1 | 0.0266 | 0.0469 | 0.009 | 0.9972 | 0.0315 |
| warm_amber_haze | street | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| warm_amber_haze | street | T1 | 10 | 0.0536 | 0.0945 | 0 | 0.9327 | 0.0595 |
| warm_amber_haze | street | T2 | 4 | 0.0534 | 0.0849 | 0 | 0.949 | 0.0823 |
| warm_amber_haze | street | T3 | 1 | 0.0426 | 0.0637 | 0.0199 | 0.9643 | 0.0589 |
| warm_amber_haze | street | Y1 | 4 | 0.0099 | 0.0263 | 0 | 0.997 | 0.0136 |
| warm_amber_haze | street | Y2 | 1 | 0.0109 | 0.0277 | 0.0031 | 0.9972 | 0.0177 |
| warm_rust_sepia | eye70 | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| warm_rust_sepia | eye70 | T1 | 10 | 0.0428 | 0.0788 | 0 | 0.9931 | 0.0482 |
| warm_rust_sepia | eye70 | T2 | 4 | 0.0394 | 0.0712 | 0 | 0.9934 | 0.0574 |
| warm_rust_sepia | eye70 | T3 | 1 | 0.0278 | 0.0442 | 0.0184 | 0.9956 | 0.0427 |
| warm_rust_sepia | eye70 | Y1 | 4 | 0.0233 | 0.037 | 0 | 0.997 | 0.025 |
| warm_rust_sepia | eye70 | Y2 | 1 | 0.0224 | 0.0415 | 0.007 | 0.9972 | 0.0252 |
| warm_rust_sepia | street | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| warm_rust_sepia | street | T1 | 10 | 0.0974 | 0.1507 | 0 | 0.9327 | 0.1201 |
| warm_rust_sepia | street | T2 | 4 | 0.0958 | 0.1418 | 0 | 0.949 | 0.1414 |
| warm_rust_sepia | street | T3 | 1 | 0.0858 | 0.1303 | 0.031 | 0.9643 | 0.1019 |
| warm_rust_sepia | street | Y1 | 4 | 0.0128 | 0.0237 | 0 | 0.997 | 0.0159 |
| warm_rust_sepia | street | Y2 | 1 | 0.0168 | 0.0318 | 0.0033 | 0.9972 | 0.0286 |

Worst value per profile and threshold group (S_px / C_px / F; limits: S0 0 / 0 / 0, Y 0.01 / 0.01 / 0.001, T12 0.12 / 0.15 / 0.005, T3 0.12 / 0.15 / 0.08):

| Profile | S0 | Y1–Y2 | T1–T2 | T3 | Sub-measures passed |
|---|---|---|---|---|---|
| lavender_peach | 0 / 0 / 0 | 0.0217 / 0.041 / 0.0058 | 0.0339 / 0.0697 / 0 | 0.0242 / 0.0439 / 0.0137 | 9 of 12 |
| slate_crimson | 0 / 0 / 0 | 0.0121 / 0.0258 / 0.0036 | 0.0236 / 0.0615 / 0 | 0.0112 / 0.0265 / 0.0069 | 9 of 12 |
| slate_teal | 0 / 0 / 0 | 0.0351 / 0.0406 / 0.0131 | 0.0399 / 0.0863 / 0 | 0.0231 / 0.0478 / 0.0132 | 9 of 12 |
| warm_amber_haze | 0 / 0 / 0 | 0.03 / 0.0505 / 0.009 | 0.0549 / 0.0959 / 0 | 0.0426 / 0.068 / 0.0275 | 9 of 12 |
| warm_rust_sepia | 0 / 0 / 0 | 0.0233 / 0.0415 / 0.007 | 0.0974 / 0.1507 / 0 | 0.0858 / 0.1303 / 0.031 | 8 of 12 |

### M-004@v1, five profiles × two viewpoints, `direction` anchoring (same sequences, same stabilisers), position snap and rotation quantisation on

**M-004@v1, `m004direction_<profile>`** — verdict `FAIL`, registry v1.1 (sha256 5f39e5531f9e), failing gating: M-004.S_px.T12, M-004.C_px.T12, M-004.S_px.T3, M-004.C_px.T3, M-004.F.T3

| Sub-measure | Kind | Value | Rule | Status | Decisive frame / sequence |
|---|---|---|---|---|---|
| M-004.S0 | gating | 0 share | <= 0 | PASS | eye70_S0 (S0, warm_rust_sepia/eye70, lag 1) |
| M-004.C_px.S0 | gating | 0 share | <= 0 | PASS | eye70_S0 (S0, warm_rust_sepia/eye70, lag 1) |
| M-004.F.S0 | gating | 0 share | <= 0 | PASS | eye70_S0 (S0, warm_rust_sepia/eye70, lag 1) |
| M-004.S_px.Y | gating | 0.0013 share | <= 0.01 | PASS | eye70_Y1 (Y1, lavender_peach/eye70, lag 4) |
| M-004.C_px.Y | gating | 0.002 share | <= 0.01 | PASS | street_Y1 (Y1, slate_teal/street, lag 4) |
| M-004.F.Y | gating | 0.0001 share | <= 0.001 | PASS | eye70_Y2 (Y2, warm_rust_sepia/eye70, lag 1) |
| M-004.S_px.T12 | gating | 0.1961 share | <= 0.12 | FAIL | street_T1 (T1, warm_rust_sepia/street, lag 10) |
| M-004.C_px.T12 | gating | 0.3023 share | <= 0.15 | FAIL | street_T1 (T1, warm_rust_sepia/street, lag 10) |
| M-004.F.T12 | gating | 0 share | <= 0.005 | PASS | eye70_T1 (T1, warm_rust_sepia/eye70, lag 10) |
| M-004.S_px.T3 | gating | 0.1933 share | <= 0.12 | FAIL | street_T3 (T3, warm_rust_sepia/street, lag 1) |
| M-004.C_px.T3 | gating | 0.2955 share | <= 0.15 | FAIL | street_T3 (T3, warm_rust_sepia/street, lag 1) |
| M-004.F.T3 | gating | 0.0863 share | <= 0.08 | FAIL | street_T3 (T3, warm_rust_sepia/street, lag 1) |

| Profile | Viewpoint | Sequence | Lag | S_px | C_px | F | Valid share | S pair p95 |
|---|---|---|---|---|---|---|---|---|
| lavender_peach | eye70 | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| lavender_peach | eye70 | T1 | 10 | 0.0968 | 0.1639 | 0 | 0.9931 | 0.105 |
| lavender_peach | eye70 | T2 | 4 | 0.1001 | 0.1667 | 0 | 0.9934 | 0.1366 |
| lavender_peach | eye70 | T3 | 1 | 0.1217 | 0.211 | 0.0825 | 0.9956 | 0.1396 |
| lavender_peach | eye70 | Y1 | 4 | 0.0013 | 0.0017 | 0 | 0.997 | 0.0011 |
| lavender_peach | eye70 | Y2 | 1 | 0.0008 | 0.0007 | 0 | 0.9972 | 0.0011 |
| lavender_peach | street | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| lavender_peach | street | T1 | 10 | 0.0281 | 0.101 | 0 | 0.9327 | 0.0423 |
| lavender_peach | street | T2 | 4 | 0.0384 | 0.109 | 0 | 0.949 | 0.0561 |
| lavender_peach | street | T3 | 1 | 0.0434 | 0.1354 | 0.0332 | 0.9643 | 0.0561 |
| lavender_peach | street | Y1 | 4 | 0.0009 | 0.0013 | 0 | 0.997 | 0.0011 |
| lavender_peach | street | Y2 | 1 | 0.0007 | 0.001 | 0.0001 | 0.9972 | 0.001 |
| slate_crimson | eye70 | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| slate_crimson | eye70 | T1 | 10 | 0.0487 | 0.1168 | 0 | 0.9931 | 0.0508 |
| slate_crimson | eye70 | T2 | 4 | 0.0515 | 0.1207 | 0 | 0.9934 | 0.0981 |
| slate_crimson | eye70 | T3 | 1 | 0.0649 | 0.1773 | 0.0547 | 0.9956 | 0.0726 |
| slate_crimson | eye70 | Y1 | 4 | 0.0007 | 0.0015 | 0 | 0.997 | 0.0007 |
| slate_crimson | eye70 | Y2 | 1 | 0.0005 | 0.0009 | 0 | 0.9972 | 0.0007 |
| slate_crimson | street | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| slate_crimson | street | T1 | 10 | 0.0178 | 0.0659 | 0 | 0.9327 | 0.0249 |
| slate_crimson | street | T2 | 4 | 0.023 | 0.0713 | 0 | 0.949 | 0.0333 |
| slate_crimson | street | T3 | 1 | 0.0275 | 0.1027 | 0.0217 | 0.9643 | 0.0326 |
| slate_crimson | street | Y1 | 4 | 0.0003 | 0.0007 | 0 | 0.997 | 0.0003 |
| slate_crimson | street | Y2 | 1 | 0.0002 | 0.0006 | 0 | 0.9972 | 0.0003 |
| slate_teal | eye70 | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| slate_teal | eye70 | T1 | 10 | 0.1043 | 0.1898 | 0 | 0.9931 | 0.1067 |
| slate_teal | eye70 | T2 | 4 | 0.0988 | 0.1812 | 0 | 0.9934 | 0.1379 |
| slate_teal | eye70 | T3 | 1 | 0.0971 | 0.2026 | 0.0719 | 0.9956 | 0.107 |
| slate_teal | eye70 | Y1 | 4 | 0.0008 | 0.0013 | 0 | 0.997 | 0.0006 |
| slate_teal | eye70 | Y2 | 1 | 0.0004 | 0.0006 | 0 | 0.9972 | 0.0005 |
| slate_teal | street | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| slate_teal | street | T1 | 10 | 0.1194 | 0.1587 | 0 | 0.9327 | 0.1473 |
| slate_teal | street | T2 | 4 | 0.082 | 0.1491 | 0 | 0.949 | 0.1442 |
| slate_teal | street | T3 | 1 | 0.0422 | 0.1144 | 0.0247 | 0.9643 | 0.13 |
| slate_teal | street | Y1 | 4 | 0.0007 | 0.002 | 0 | 0.997 | 0.0005 |
| slate_teal | street | Y2 | 1 | 0.0003 | 0.001 | 0 | 0.9972 | 0.0004 |
| warm_amber_haze | eye70 | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| warm_amber_haze | eye70 | T1 | 10 | 0.1757 | 0.2829 | 0 | 0.9931 | 0.1893 |
| warm_amber_haze | eye70 | T2 | 4 | 0.1618 | 0.2575 | 0 | 0.9934 | 0.2003 |
| warm_amber_haze | eye70 | T3 | 1 | 0.1751 | 0.2955 | 0.0849 | 0.9956 | 0.188 |
| warm_amber_haze | eye70 | Y1 | 4 | 0.0011 | 0.0013 | 0 | 0.997 | 0.0009 |
| warm_amber_haze | eye70 | Y2 | 1 | 0.0007 | 0.0007 | 0 | 0.9972 | 0.0009 |
| warm_amber_haze | street | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| warm_amber_haze | street | T1 | 10 | 0.1259 | 0.2144 | 0 | 0.9327 | 0.1559 |
| warm_amber_haze | street | T2 | 4 | 0.1228 | 0.1837 | 0 | 0.949 | 0.1543 |
| warm_amber_haze | street | T3 | 1 | 0.1216 | 0.1913 | 0.0453 | 0.9643 | 0.1503 |
| warm_amber_haze | street | Y1 | 4 | 0.001 | 0.0014 | 0 | 0.997 | 0.001 |
| warm_amber_haze | street | Y2 | 1 | 0.0007 | 0.0009 | 0.0001 | 0.9972 | 0.0009 |
| warm_rust_sepia | eye70 | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| warm_rust_sepia | eye70 | T1 | 10 | 0.1138 | 0.205 | 0 | 0.9931 | 0.1187 |
| warm_rust_sepia | eye70 | T2 | 4 | 0.1059 | 0.1899 | 0 | 0.9934 | 0.1503 |
| warm_rust_sepia | eye70 | T3 | 1 | 0.107 | 0.2034 | 0.0622 | 0.9956 | 0.1141 |
| warm_rust_sepia | eye70 | Y1 | 4 | 0.0011 | 0.0016 | 0 | 0.997 | 0.0009 |
| warm_rust_sepia | eye70 | Y2 | 1 | 0.0006 | 0.0009 | 0.0001 | 0.9972 | 0.0009 |
| warm_rust_sepia | street | S0 | 1 | 0 | 0 | 0 | 1 | 0 |
| warm_rust_sepia | street | T1 | 10 | 0.1961 | 0.3023 | 0 | 0.9327 | 0.22 |
| warm_rust_sepia | street | T2 | 4 | 0.1913 | 0.2805 | 0 | 0.949 | 0.2197 |
| warm_rust_sepia | street | T3 | 1 | 0.1933 | 0.2955 | 0.0863 | 0.9643 | 0.2181 |
| warm_rust_sepia | street | Y1 | 4 | 0.0008 | 0.0011 | 0 | 0.997 | 0.0007 |
| warm_rust_sepia | street | Y2 | 1 | 0.0004 | 0.0006 | 0.0001 | 0.9972 | 0.0006 |

Worst value per profile and threshold group (S_px / C_px / F; limits: S0 0 / 0 / 0, Y 0.01 / 0.01 / 0.001, T12 0.12 / 0.15 / 0.005, T3 0.12 / 0.15 / 0.08):

| Profile | S0 | Y1–Y2 | T1–T2 | T3 | Sub-measures passed |
|---|---|---|---|---|---|
| lavender_peach | 0 / 0 / 0 | 0.0013 / 0.0017 / 0.0001 | 0.1001 / 0.1667 / 0 | 0.1217 / 0.211 / 0.0825 | 8 of 12 |
| slate_crimson | 0 / 0 / 0 | 0.0007 / 0.0015 / 0 | 0.0515 / 0.1207 / 0 | 0.0649 / 0.1773 / 0.0547 | 11 of 12 |
| slate_teal | 0 / 0 / 0 | 0.0008 / 0.002 / 0 | 0.1194 / 0.1898 / 0 | 0.0971 / 0.2026 / 0.0719 | 10 of 12 |
| warm_amber_haze | 0 / 0 / 0 | 0.0011 / 0.0014 / 0.0001 | 0.1757 / 0.2829 / 0 | 0.1751 / 0.2955 / 0.0849 | 7 of 12 |
| warm_rust_sepia | 0 / 0 / 0 | 0.0011 / 0.0016 / 0.0001 | 0.1961 / 0.3023 / 0 | 0.1933 / 0.2955 / 0.0863 | 7 of 12 |

### M-004@v1 A/B: every anchoring mode and each stabiliser off (slate_teal, eye70; C12)

Worst S_px / C_px / F per group. `snap` = position snap, `rotq` = rotation quantisation.

| Variant | S0 | Y1–Y2 | T1–T2 | T3 | Sub-measures passed |
|---|---|---|---|---|---|
| control_frame_noise | 0.1571 / 0.2856 / 0.0789 | - | - | - | 0 of 3 |
| direction | 0 / 0 / 0 | 0.0008 / 0.0013 / 0 | 0.1043 / 0.1898 / 0 | 0.0971 / 0.2026 / 0.0719 | 10 of 12 |
| direction_norotq | 0 / 0 / 0 | 0.3539 / 0.7184 / 0.266 | 0.1045 / 0.1898 / 0 | 0.0979 / 0.2043 / 0.0721 | 7 of 12 |
| direction_nosnap | 0 / 0 / 0 | 0.0008 / 0.0013 / 0 | 0.1049 / 0.1905 / 0.0759 | 0.0971 / 0.2026 / 0.0719 | 9 of 12 |
| hybrid | 0 / 0 / 0 | 0.0214 / 0.0406 / 0.0061 | 0.0399 / 0.0772 / 0 | 0.0231 / 0.0478 / 0.0132 | 9 of 12 |
| hybrid_norotq | 0 / 0 / 0 | 0.131 / 0.3026 / 0.093 | 0.0391 / 0.076 / 0 | 0.0226 / 0.045 / 0.0128 | 9 of 12 |
| hybrid_nosnap | 0 / 0 / 0 | 0.0214 / 0.0406 / 0.0061 | 0.0398 / 0.0769 / 0.032 | 0.0231 / 0.0478 / 0.0132 | 8 of 12 |
| screen | 0 / 0 / 0 | 0.3498 / 0.7177 / 0.2659 | 0.1039 / 0.189 / 0 | 0.0969 / 0.2028 / 0.0718 | 7 of 12 |
| screen_nostab | 0 / 0 / 0 | 0.3539 / 0.7184 / 0.266 | 0.105 / 0.1906 / 0.0761 | 0.0979 / 0.2043 / 0.0721 | 6 of 12 |

### Dolly toward the 10 km layer (reported, B15; the tool gives dolly sequences no verdict)

```
[{"motion": "dolly", "note": "validated and reported without a verdict", "sequence": "eye70_D1"}]
```

### Checks outside the metric tools (`Tools/Rendering/Pixel/pixel_checks.py`)

| Check | File | Pass | Key values |
|---|---|---|---|
| c11_smooth_T1 | `metrics/checks/c11_smooth_T1.json` | yes | compensation on: max displacement error 0.9869 px, max step error 0.6947 px (pass yes); off: 1.9874 px, 3.6919 px (pass no); commanded -0.361 px/frame |
| c11_smooth_Y1 | `metrics/checks/c11_smooth_Y1.json` | yes | compensation on: max displacement error 0.3527 px, max step error 0.0649 px (pass yes); off: 3.3527 px, 3.087 px (pass no); commanded -1.0005 px/frame |
| c13_cut | `metrics/checks/c13_cut.json` | yes | internal, output, index, depth, pose and offsets identical: yes; differs from the frame before the cut: yes |
| c19_depth | `metrics/checks/c19_depth.json` | yes | Block80_Near: 109.1492 m vs 109.147 m (0.002 %); Block80_Far: 954.6437 m vs 954.6978 m (0.006 %); Layer_3km_B: 3114 m vs 3114 m (0.003 %); Layer_10km_A: 9484 m vs 9485 m (0.012 %); Layer_30km_B: 28656 m vs 28655 m (0.002 %) |
| c19_identical_c11_subpixel_on | `metrics/checks/c19_identical_c11_subpixel_on.json` | yes | 1800 PNG and 362 JSON files compared, 0 differ |
| c19_identical_control_reversed_haze | `metrics/checks/c19_identical_control_reversed_haze.json` | yes | 60 PNG and 13 JSON files compared, 0 differ |
| c19_identical_control_unquantised | `metrics/checks/c19_identical_control_unquantised.json` | yes | 60 PNG and 13 JSON files compared, 0 differ |
| c19_identical_cut_check | `metrics/checks/c19_identical_cut_check.json` | yes | 65 PNG and 15 JSON files compared, 0 differ |
| c19_identical_haze_cal | `metrics/checks/c19_identical_haze_cal.json` | yes | 5 PNG and 2 JSON files compared, 0 differ |
| c19_identical_m003_stills | `metrics/checks/c19_identical_m003_stills.json` | yes | 180 PNG and 39 JSON files compared, 0 differ |
| c19_identical_stages_ab | `metrics/checks/c19_identical_stages_ab.json` | yes | 115 PNG and 46 JSON files compared, 0 differ |
| c2_grid_bilinear_control | `metrics/checks/c2_grid_bilinear_control.json` | no | uniform blocks at the declared offset [0, 0]: min 0.4486; with the offset ignored: min 0.4486 |
| c2_grid_declared_offset | `metrics/checks/c2_grid_declared_offset.json` | yes | uniform blocks at the declared offset [3, 0]: min 1; with the offset ignored: min 0.7401 |
| c6_layers_lavender_peach | `metrics/checks/c6_layers_lavender_peach.json` | yes | frame 0 mean L*: 1km 33.4783, 3km 38.029, 10km 46.4213, 30km 51.5982; successively lighter in 12 of 12 frames; far layer minus sky at horizon 3.7608 L*; near field darkest in 12 of 12; ground seams within adjacent bands 44 of 44 |
| c6_layers_slate_crimson | `metrics/checks/c6_layers_slate_crimson.json` | no | frame 0 mean L*: 1km 24.8928, 3km 29.8443, 10km 36.8852, 30km 38.7044; successively lighter in 12 of 12 frames; far layer minus sky at horizon 1.3115 L*; near field darkest in 12 of 12; ground seams within adjacent bands 32 of 44 |
| c6_layers_slate_teal | `metrics/checks/c6_layers_slate_teal.json` | yes | frame 0 mean L*: 1km 46.0026, 3km 54.0349, 10km 57.789, 30km 67.0855; successively lighter in 12 of 12 frames; far layer minus sky at horizon 6.2055 L*; near field darkest in 12 of 12; ground seams within adjacent bands 44 of 44 |
| c6_layers_warm_amber_haze | `metrics/checks/c6_layers_warm_amber_haze.json` | yes | frame 0 mean L*: 1km 51.4769, 3km 59.7254, 10km 67.9528, 30km 73.967; successively lighter in 12 of 12 frames; far layer minus sky at horizon 3.926 L*; near field darkest in 12 of 12; ground seams within adjacent bands 44 of 44 |
| c6_layers_warm_rust_sepia | `metrics/checks/c6_layers_warm_rust_sepia.json` | yes | frame 0 mean L*: 1km 40.9195, 3km 50.5922, 10km 54.8223, 30km 64.2273; successively lighter in 12 of 12 frames; far layer minus sky at horizon 6.3358 L*; near field darkest in 12 of 12; ground seams within adjacent bands 44 of 44 |
| c7_blocks_lavender_peach | `metrics/checks/c7_blocks_lavender_peach.json` | no | near_150m: in view in 4 frames, dL* min 13.4092, median 17.9996, frames with dL* >= 4: 4; far_1km: in view in 9 frames, dL* min 0.1008, median 2.4077, frames with dL* >= 4: 0 |
| c7_blocks_slate_crimson | `metrics/checks/c7_blocks_slate_crimson.json` | no | near_150m: in view in 4 frames, dL* min 8.5246, median 10.2504, frames with dL* >= 4: 4; far_1km: in view in 9 frames, dL* min 2.7956, median 4.2002, frames with dL* >= 4: 5 |
| c7_blocks_slate_teal | `metrics/checks/c7_blocks_slate_teal.json` | no | near_150m: in view in 4 frames, dL* min 10.2219, median 13.3999, frames with dL* >= 4: 4; far_1km: in view in 9 frames, dL* min 1.2552, median 3.5512, frames with dL* >= 4: 3 |
| c7_blocks_warm_amber_haze | `metrics/checks/c7_blocks_warm_amber_haze.json` | yes | near_150m: in view in 4 frames, dL* min 15.2036, median 18.9202, frames with dL* >= 4: 4; far_1km: in view in 9 frames, dL* min 4.0516, median 6.5197, frames with dL* >= 4: 9 |
| c7_blocks_warm_rust_sepia | `metrics/checks/c7_blocks_warm_rust_sepia.json` | no | near_150m: in view in 4 frames, dL* min 10.5008, median 12.9549, frames with dL* >= 4: 4; far_1km: in view in 9 frames, dL* min 3.8394, median 6.0748, frames with dL* >= 4: 8 |
| c7_scale_lavender_peach | `metrics/checks/c7_scale_lavender_peach.json` | yes | marker 1 px, height 1 px, dL* 35.5947; 80 m block height 31.9496 px at 1043 m, dL* to background 2.7946 |
| c7_scale_slate_crimson | `metrics/checks/c7_scale_slate_crimson.json` | yes | marker 1 px, height 1 px, dL* 21.0661; 80 m block height 31.9496 px at 1043 m, dL* to background 4.8871 |
| c7_scale_slate_teal | `metrics/checks/c7_scale_slate_teal.json` | yes | marker 1 px, height 1 px, dL* 27.605; 80 m block height 31.9496 px at 1043 m, dL* to background 5.8257 |
| c7_scale_warm_amber_haze | `metrics/checks/c7_scale_warm_amber_haze.json` | yes | marker 1 px, height 1 px, dL* 32.8846; 80 m block height 31.9496 px at 1043 m, dL* to background 7.6703 |
| c7_scale_warm_rust_sepia | `metrics/checks/c7_scale_warm_rust_sepia.json` | yes | marker 1 px, height 1 px, dL* 31.4537; 80 m block height 31.9496 px at 1043 m, dL* to background 8.3022 |

### Frame timing (development Mac: relative regression only; M-008 is not verified)

Hardware: Apple M5 Pro, Metal, Mac OS X 26.6.2, Unity 6000.6.9f1, batch mode. Method: wall time of render + one-pixel readback (GPU finished), configurations interleaved per frame, stage cost = paired difference to the M0 frame. 300 frames per configuration.

| Output | Internal | Item | mean ms | p50 ms | p95 ms | max ms |
|---|---|---|---|---|---|---|
| 1920x1080 | 480x270 (k = 4) | m0_world_and_present | 1.2691 | 1.1762 | 1.6708 | 7.6003 |
| 1920x1080 | 480x270 (k = 4) | m0_world | 0.8338 | 0.7888 | 1.1325 | 1.5947 |
| 1920x1080 | 480x270 (k = 4) | world_no_dither | 0.6963 | 0.6651 | 0.9674 | 1.1715 |
| 1920x1080 | 480x270 (k = 4) | world_no_haze | 0.7599 | 0.7232 | 1.0388 | 1.6571 |
| 1920x1080 | 480x270 (k = 4) | world_no_quantise | 0.7277 | 0.6976 | 0.975 | 1.5135 |
| 1920x1080 | 480x270 (k = 4) | world_store_only | 0.582 | 0.559 | 0.7475 | 1.3897 |
| 1920x1080 | 480x270 (k = 4) | cost: Pixel.Present (world+present minus world) | 0.4354 | 0.3832 | 0.7411 | 6.0056 |
| 1920x1080 | 480x270 (k = 4) | cost: Pixel.Dither | 0.1375 | 0.1326 | 0.3516 | 0.4773 |
| 1920x1080 | 480x270 (k = 4) | cost: Pixel.Haze | 0.0739 | 0.0819 | 0.3349 | 0.5139 |
| 1920x1080 | 480x270 (k = 4) | cost: Pixel.Quantise | 0.1061 | 0.0968 | 0.3797 | 0.7559 |
| 1920x1080 | 480x270 (k = 4) | cost: Pixel.Dither+Haze+Quantise (M0 world minus store only) | 0.2517 | 0.2368 | 0.5152 | 0.9842 |
| 2560x1440 | 512x288 (k = 5) | m0_world_and_present | 1.0666 | 1.051 | 1.3101 | 1.9311 |
| 2560x1440 | 512x288 (k = 5) | m0_world | 0.6087 | 0.6022 | 0.8681 | 1.3259 |
| 2560x1440 | 512x288 (k = 5) | world_no_dither | 0.4707 | 0.4553 | 0.609 | 0.863 |
| 2560x1440 | 512x288 (k = 5) | world_no_haze | 0.4501 | 0.4361 | 0.5688 | 0.7467 |
| 2560x1440 | 512x288 (k = 5) | world_no_quantise | 0.48 | 0.455 | 0.6571 | 0.9917 |
| 2560x1440 | 512x288 (k = 5) | world_store_only | 0.4087 | 0.397 | 0.5164 | 0.6373 |
| 2560x1440 | 512x288 (k = 5) | cost: Pixel.Present (world+present minus world) | 0.4578 | 0.4639 | 0.601 | 0.9403 |
| 2560x1440 | 512x288 (k = 5) | cost: Pixel.Dither | 0.138 | 0.1405 | 0.3087 | 0.7913 |
| 2560x1440 | 512x288 (k = 5) | cost: Pixel.Haze | 0.1586 | 0.1477 | 0.3509 | 0.8182 |
| 2560x1440 | 512x288 (k = 5) | cost: Pixel.Quantise | 0.1288 | 0.1344 | 0.3646 | 0.8685 |
| 2560x1440 | 512x288 (k = 5) | cost: Pixel.Dither+Haze+Quantise (M0 world minus store only) | 0.2 | 0.1887 | 0.3985 | 0.9205 |

GC.Alloc samples over 100 frames: 1920x1080: {"m0_world_and_present": 0, "m0_world": 0, "world_store_only": 0}; 2560x1440: {"m0_world_and_present": 0, "m0_world": 0, "world_store_only": 0}
