# Rendering.Pixel
Purpose: the painted-pixel look (P4): a 3D world rendered at a low internal resolution, pulled back in depth by posterised haze, mapped to a limited district palette with ordered dithering, and shown at an exact integer scale. Each stage is a separate URP 17.6 Render Graph renderer feature that can be reordered and switched on or off by feature flag.

Breakdown IDs: D-022, D-027, F-046, M-003, M-004, M-008 (render-side share), M-011 (output smoothness), M-012 (ramp uniformity), GAME_BREAKDOWN §9, MODULARITY §4.7, §3 layer L1. Research: R-002, R-003. Task: T-004.

## Assemblies (layer L1: depends on Core contracts only; never on Camera, Units or World)
| Assembly | Folder | Engine | Contents |
|---|---|---|---|
| `Game.Rendering.Pixel.Contracts` | `Contracts/` | none | `PixelStageId`, `PixelDitherAnchor`, `PixelFlags`, `PixelGrid` + `IPixelGridSource`, `PixelStabilisationHints` + `IPixelStabilisationSource`, `PixelScaleRule`, `PixelStageOrder`, `PixelContractVersions` |
| `Game.Rendering.Pixel.Maths` | `Maths/` | none | `ColourMath` (sRGB, CIELAB, OKLab, CIEDE2000, palette hash), `DitherMasks`, `PaletteLutBaker`, `HazeMath`, `MiniJson`, `PaletteSetReader`, `HazeRungModel`/`HazeTuning` (R-131 haze model), `ParallaxShiftMath` (C11 model), `PixelLayerPlan` (layer slabs, T-181), `PixelLayerHints` (v2 hint validation) |
| `Game.Rendering.Pixel` | `Runtime/` | URP | `PixelCameraBinder`, `PixelPresentCamera`, one feature per stage, `PaletteProfile`, `PixelPipelineSettings`, `PixelConfiguration`, `PixelFlagState`, `PixelPipelineCatalog`, shaders |
| `Game.Rendering.Pixel.Editor` | `Editor/` | Editor | `PixelPipelineInstaller`, `PixelPipelineAssets`, `PaletteProfileGenerator`, `PixelPipelineScope` |
| `Game.Rendering.Pixel.Tests` | `Tests/EditMode/` | test | engine-free NUnit sources, also run by `dotnet test DotnetTests/Rendering.Pixel.Tests` |
| `Game.Rendering.Pixel.Unity.Tests` | `Tests/UnityOnly/` | test | capture tests through the installed pipeline |

## Stages (each a `ScriptableRendererFeature`; list order on the world renderer = execution order)
| Stage | Feature | Flag (all ship `false`) | What it does |
|---|---|---|---|
| 0 | `PixelCameraBinder` (component on the world camera, not a feature) | `rendering.pixel.enabled`, `rendering.pixel.lowRes` | Integer scale k = clamp(round(H / 270), 4, 6); internal size = ceil(output / k) plus a one-texel guard band; owns the camera target and the two persistent internal targets; pulls the rig's hints |
| Downsample | `PixelDownsampleFeature` | `rendering.pixel.supersample` | Box filter from the supersampled camera target (optional, off in M0) |
| Dither | `PixelDitherFeature` | `rendering.pixel.dither` | Writes the per-texel threshold: mask and anchoring mode are data |
| Haze | `PixelHazeFeature` | `rendering.pixel.haze` | Aerial perspective in bands by real radial distance, dithered seams, sky gradient |
| Outline | `PixelOutlineFeature` | `rendering.pixel.outline` | Relative depth edges in the near field (optional, off in M0) |
| Quantise | `PixelQuantiseFeature` | `rendering.pixel.quantise` | Palette LUT lookup: every output texel is an exact palette colour; palette index in alpha |
| Store | `PixelStoreFeature` | (with `enabled`) | Writes internal colour (sRGB bytes, index in alpha, 255 = not quantised) and linear depth (m) |
| Present | `PixelPresentFeature` (present renderer) | `rendering.pixel.subPixelPresent` for the shift | Point upscale at exactly k, shifted by round(remainder × k) output pixels |

Order rules (`PixelStageOrder`): Downsample before every internal-resolution stage; Dither before Haze and Quantise; Haze and Outline before Quantise; Store after every colour stage; Present last. `PixelPipelineInstaller.Install(order)` throws on an illegal order and writes nothing. At run time a stage that finds itself after a stage it must precede is skipped with one warning (`PixelLog.OrderViolations`), so an illegal order is never silently wrong. Stages share state only through `PixelFrameContext` in the Render Graph frame data and per-pass property blocks: no shader globals.

## Contracts
- `PixelStabilisationHints` v1 / `IPixelStabilisationSource`: rendered pose, sub-texel remainder (+X right, +Y up, in internal texels), dither origin (whole texels from quantised rotation), reference depth, grid sizes, `Cut`. The camera rig provides it (MODULARITY §4.2 reads as the pipeline reading the rig; the contract lives here so L1 never references L3, Q-020). The binder finds a source on its own GameObject by interface, or takes one from `SetStabilisationSource`. With no source the pipeline renders unsnapped and still on palette.
- `PixelGrid` / `IPixelGridSource`: what a rig needs to snap (internal size, k, texel angle, FOV tangents). The binder implements it.
- `PaletteProfile` v1 (asset): colours in declared order (hex of `palette_set@v1` is authoritative), roles, accent, haze ramp (`ramp_uniform_L`), haze start/full distance, band count, seam width, sky values, grade (value scale, bias, gamma, accent chroma range), dither spread, LUT (size³ × 4 bytes: index a, index b, ratio of b, 255) and its hashes. `paletteSha256` = SHA-256 of the compact JSON list of lowercase `#rrggbb` strings.
- `style_capture@v1` (R-002 §6) is written by the capture tool in `Assets/Game/Tools/StyleCapture` (not part of this module).

Consumes events: none. Emits events: none.

## Config assets (all tuning values; nothing is hard-coded in the stage code)
- `Assets/Game/Config/FeatureFlags/Rendering.Pixel.json`: the nine flags (incl. `rendering.pixel.layerSnap`), all `false`.
- `Assets/Game/Config/Rendering/Configurations/M0StyleFrame.json`: the M0 style-frame configuration; a list of flags it switches on (`PixelConfiguration`, merged over the session flags by `PixelFlagState.From`). Stages are enabled by this file, not by code.
- `Assets/Game/Config/Rendering/PixelPipeline.json` (`PixelPipelineSettings`, schema 1): target lines, scale limits, guard band, supersample factor, `ditherMask` (`bayer4` default; `bayer8`, `cluster8`, `bluenoise16`, `bluenoise32` selectable), `ditherAnchor` (`screen`, `direction`, `hybrid`), hybrid distance, outline thresholds, default profile.
- `Assets/Game/Config/Rendering/palette_set.json`: verbatim copy of `research/R-002/palettes.json` (`palette_set@v1`).
- `Assets/Game/Config/Rendering/PaletteProfileTuning.json`: per-profile renderer parameters (keys are `PaletteProfile` field names).
- `Assets/Game/Config/Rendering/PaletteProfiles/<id>.asset`: the five generated profiles with baked LUTs.
- `Assets/Game/Config/Rendering/Pipeline/PixelPipeline.asset`, `PixelWorld_Renderer.asset`, `PixelPresent_Renderer.asset`, and `PixelPipelineCatalog.asset`: created by Editor script only. `Assets/Settings/PC_RPAsset.asset` and `PC_Renderer.asset` are not touched.

## How to
- Install or re-install the assets: `unity run <project> -- -executeMethod Game.Rendering.Pixel.Editor.PixelPipelineInstaller.InstallAll` (menu: Game/Rendering/Pixel).
- New district palette: duplicate a `PaletteProfile` asset (or add a profile to the palette set and run `PaletteProfileGenerator.GenerateAll`), edit colours or parameters, run "Bake LUT" from the asset's context menu or Game/Rendering/Pixel/Bake selected palette profile. No code or shader change.
- Use on a camera: world camera with renderer index 0 of the pixel URP asset plus `PixelCameraBinder` (catalog, profile, output size); a second camera with culling mask Nothing, renderer index 1 and `PixelPresentCamera.source` = the binder; call `binder.Configure(settings, PixelFlagState.From(flags, configuration))`. UI belongs on the present camera at output resolution. The pixel URP asset must be the active pipeline (`QualitySettings.renderPipeline`); the Editor tools switch it with `PixelPipelineScope` and restore it.
- Reorder stages: `PixelPipelineInstaller.Install(order)`.

## Tests
- Engine-free (`dotnet test DotnetTests/Rendering.Pixel.Tests`, also Unity EditMode): CIEDE2000 against published test data, Lab, hex, palette hash, Bayer matrices and alternative masks, LUT bake (determinism, grey axis along the ramp, accent, edits), palette set reader and the five canonical hashes, haze bands to 30 km, stage-order rules, scale rule; R-131 haze model and C11 model (`HazeRungModelTests`); layer slab plan and C11 lock tolerance (`PixelLayerPlanTests`, against `evidence/T-181/layer_plan.json`).
- Unity EditMode (`Tests/UnityOnly/PixelPipelineTests.cs`): every internal pixel is an exact palette colour with its index; output is an exact integer upscale; two renders bit-identical; depth in metres within 1 %; Quantise off gives index 255; all 16 combinations of the optional stages render valid frames; each stage changes the frame; a sixth profile from a JSON fixture needs no code change; editing a colour and re-baking; generated profiles equal the palette set and a fresh bake; installer keeps feature list and map in step; illegal orders rejected; a legal reorder renders the identical frame; an illegal run-time order is skipped with a warning.
- Metrics: M-003@v1 and M-004@v1 through the capture tool and the T-002 tools; results in `evidence/T-004/EVIDENCE.md`.

Owner role: implementation/pixel (`impl-pixel`).

## Layer slabs: C11 off the reference plane (T-181 design note)
Status 2026-11-15, Opus design for `tasks/T-176.md` step T-181 (escalation point 1: slab boundaries, seam phase, perf budget). Decided autonomously (founder asleep). Inputs: `research/R-131.md` §4, `HazeRungModel`, `ParallaxShiftMath`, the study `evidence/T-181/layer_plan.py` → `layer_plan.json`. No threshold is lowered and no metric narrowed. **Q-260 stays open**: everything below is measured and reported at the C11 class depths *and* beside the any-depth figure; nothing here claims C11 for arbitrary depth.

### Why slabs, and what they cannot do
- A texel edge keeps a constant phase under lateral motion only when the camera steps by a whole texel *at that edge's depth* (R-131 §4.3). One snapped camera locks one depth; a slab is a separately snapped camera for a depth range, composited at present time, so three depths can be locked at once.
- New finding (`layer_plan.json`, `c11_lock_tolerance`): the lock is exact or nothing. With the slab reference 0.25 % off the surface depth the model's worst edge phase is 4.2-4.8 px for near and reference (a whole-texel tie flip after tens of snap steps); the median phase degrades smoothly (near 1.0 px at 0.1 %, 1.5 at 0.25 %, 3.4 at 1 %). The far slab snaps about once in T1 and stays at 0.65-0.69 px over ±1 %. Consequences: (1) the rig must supply exact depths, the binder rejects a grid that is not one texel at its reference depth (`PixelLayerPlan.GridMatches`, 1e-4); (2) the 3-slab result is a result *at the slab depths*; (3) T1 as configured puts the main slab at the rig depth 664.874 m while the reference-class edge is at 663.0 m (0.28 %): worst phase 4.84 px, median 0.93 px, the same as today. Both configurations are captured and reported (T-182); which one C11 is judged on is part of Q-260 (founders).
- Combining the T-180 depth-aware shift inside a slab does not help (`layer_plus_depth_shift`: worst phase changes by -0.9 to +1.0 px, median by -0.7 to +0.16 px against plain layering, and near/reference still pass only at the exact slab depth) and adds intra-slab disocclusion. **Rule: with `layerSnap` on, `depthShift` is ignored** (one warning).

### Contract (T-181b; `PixelStabilisationHints` v2, the rig fills it, Rendering.Pixel never references Camera)
- New blittable `PixelLayerHint { float ReferenceDepth, Grid, OffsetRight, OffsetUp, SubTexelX, SubTexelY; bool Cut; }` and on the hints `int LayerCount` (0 = v1 behaviour) plus fixed fields `LayerNear`, `LayerMain`, `LayerFar` (no arrays: zero allocation per frame).
- The rendered pose (v1 fields) is the **main** slab's snapped pose: `LayerMain.Offset* = 0`, `LayerMain.SubTexel* = SubTexel*`, `LayerMain.ReferenceDepth = ReferenceDepth`, `LayerMain.Grid = PositionGrid`. Near and far carry the offset of their own snapped position from the rendered one along the rendered right/up axes (m) and their own remainder in their own texels. Rotation quantisation is shared (one dither origin).
- Reference depths: near = the rig's focus/target depth (the mech the player watches; 124.2 m in T1), main = the skyline design depth (664.874 m in T1), far = the far design depth (10353 m in T1). Rig-side snapping (T-181c, **Camera module, separate brief**): `CameraStabiliser.Stabilise` once per layer with its own `SnapState`; a layer whose reference depth changes by more than `GridTolerance` restarts its lattice (`Cut` on that layer only).
- `PixelContractVersions.StabilisationHints` 1 → 2. A v1 source (LayerCount 0), a missing layer or a rejected grid renders the v1 single-snap path, counted in `PixelLog`, never silently layered with a wrong grid.

### Slab boundaries (implemented: `PixelLayerPlan`, step T-181a)
- Ownership is by **radial** distance (the haze bands are radial; `PixelHaze.shader` uses `z * |viewRay|`), so each boundary is exactly a band edge and the slab seam coincides with a haze band seam everywhere on screen, not only at the centre. Near owns [0, e1), main [e1, e2), far [e2, ∞).
- Boundary between references zA < zB: admissible radial range (zA · r_corner · (1 + o), zB / (1 + o)) with r_corner = √(1 + tan²h + tan²v) (1.332 at 75° hFOV, 480×270) and overlap o = 0.10, so a slab's reference surface is owned by that slab at every pixel; inside it the band edge (gamma-aware `HazeMath.BandStart`) nearest in log to the middle √(lo·hi); with haze off, the middle itself. Hysteresis 0.25 local band widths, so a drifting reference does not make the seam jump every frame. References closer than the admissible range allows are rejected with a reason (no silent merge).
- Each slab camera renders the eye-depth range [OwnFrom / r_corner / (1 + o), OwnTo · (1 + o)] (radial ≥ eye depth, so this covers the owned range at every pixel, plus the overlap that fills shift gaps).
- Committed tuning, T1 references (`layer_plan.json`; admissible 182-604 m and 974-9412 m):

| Profile | e1 near/main (band) | e2 main/far (band) |
|---|---|---|
| warm_rust_sepia | 336.9 m (18 of 64) | 3064.9 m (42) |
| warm_amber_haze | 320.0 m (7 of 32) | 2871.5 m (18) |
| slate_teal | 338.3 m (5 of 16) | 2601.2 m (10) |
| lavender_peach | 358.5 m (5 of 16) | 2677.7 m (10) |
| slate_crimson | 320.7 m (6 of 24, gamma 0.72) | 3373.0 m (17) |

The C7 80 m block (926-998 m, ring to about 2.5 km) is inside the main slab in every profile. The 3 km C6 block is close to e2 (2.6-3.4 km) and may straddle the seam (main in rust and crimson, far in the others); C6/C7 are re-measured with `layerSnap` on (T-184).

### Seam phase at a slab boundary
- Ground at e1/e2 is at neither slab's reference depth, so on each side its phase wanders by up to one texel (the any-depth error). Across the seam the two sides can differ by up to k output pixels laterally. Hiding it on a band edge means the value already steps there; seam purity holds by construction because every texel's band is computed from its own radial distance and the shared dither threshold, in whichever slab it is drawn.
- Two visible risks remain and are measured, not assumed away: (a) the dithered band seam (± `hazeSeamTexels` around the edge) is cut by the slab seam, so its Bayer pattern can be offset by a sub-texel amount along the seam line; (b) each slab is an exact k-grid, but at the seam the grids are offset by |shift_A − shift_B| output pixels, so partial texels (width < k) appear along the seam curve. T-180's per-pixel shift has the same property at depth edges. Both are checked by the existing C1/C2 grid integrity and A2 adherence (must stay 1.0) and a new seam report (T-184).
- Objects crossing a boundary (a kaiju at 300-350 m) are split along the band edge and can tear by up to k px. If the seam report shows tears on units, the fallback is whole-object slab assignment for renderers smaller than a slab (by bounds centre, rendering layer mask) with depth-split kept for terrain; not built unless needed (stop rule).

### Render architecture (T-181d, Unity code; compiled and run only in T-183)
- `PixelSlabCamera`: two hidden child cameras (near, far) created by the binder when `rendering.pixel.layerSnap` is on and the hints carry three valid layers; the world camera itself is the main slab with its clip planes set to the main range. Each copies FOV, culling mask, clear, renderer index 0 and the guard-padded projection, sits at rendered pose + its offset, renders before the world camera (camera depth order) into its own camera target, colour and depth targets. Per-camera culling keeps each object to the slabs its bounds overlap. Shadows: the pixel URP asset's shadow distance is 50 m, inside the near slab, so only the near slab renders shadows (`renderShadows` off on main and far); if the shadow distance ever exceeds e1 / r_corner / (1 + o), main must render them too (asserted in T-181d tests).
- Stages run unchanged per slab camera (the binder resolves a slab camera to itself and its slab index). Dither, haze and quantise are per texel, so every slab gets the same bands and palette rules.
- Present (layered path): samples each slab at `floor((o + shift_s) / k) + guard` with shift_s = round(k · SubTexel_s) clamped to the guard band, reconstructs radial = depth · |viewRay|, and applies `PixelLayerPlan.Composite`: the first slab near to far whose sample is covered and owned wins; a gap takes the covered sample with the smallest radial; nothing covered is the far slab (sky). A composite pass also writes the unshifted composite into the existing `InternalColour`/`InternalDepth`, so the capture tool and the metrics read one internal image as today (each slab's offset is under half its own texel, so the unshifted composite is registered to under half a texel).
- Guard band: slab shifts reuse the existing clamp (`guardTexels` · k); no change.

### Budgets (decided here; Assumed until target hardware, Q-019)
- Draw calls and batches (M-018 ProfilerRecorder): `layerSnap` on ≤ 1.6 × off on the T-004 test scene and the T1 c11 scene (new budget; the overlap ranges, about 220-390 m and 1.8-3.7 km of eye depth, are drawn by two slabs).
- C20 unchanged: `Pixel.*` CPU p95 summed over all slabs ≤ 0.5 ms and present ≤ 0.5 ms at 1080p and 1440p (relative, Mac, GPU ms not measurable on Metal batch); GC 0 over 600 frames; M-004 not worse than `evidence/T-004/metrics`; M-008 at risk is a stop (T-176 stop rule).
- Memory: two extra internal colour + depth + camera targets (482 × 272 at 1080p), negligible.

### Steps T-180..T-185 (each ends with its tests green and one commit; T-176 stop rules apply)
| Step | Work | Tests / acceptance |
|---|---|---|
| T-180 | `PixelPresent.shader`/feature: depth-aware whole-output-pixel shift (flag `rendering.pixel.depthShift`, default off), shift = `ParallaxShiftMath.DepthShiftPx(zRef/z, rem, k)`, disocclusion takes the farther sample; ignored when `layerSnap` is on | EditMode: shader constants and flag wiring; flag off renders bit-identical to today; compile in T-183. Model target near 4.5 / ref 4.8 (worst) / far 4.0 px, a known fail of C11 |
| **T-181a (done)** | `PixelLayerPlan` + gamma-aware `HazeMath.BandStart`; study `evidence/T-181/layer_plan.py` | dotnet/EditMode `PixelLayerPlanTests`: boundaries on band edges in all five profiles and equal to the Python mirror, one band step across each seam, ownership partition, reference owned at centre and corner, clip covers ownership, haze-off middle, illegal references rejected, hysteresis, composite rule, grid tolerance, C11 lock tolerance vs the mirror within 0.5 px |
| **T-181b (done)** | Contract v2 (`PixelLayerHint`, `LayerCount`), flag `rendering.pixel.layerSnap` (default off) in `PixelFlags` and the flags JSON | dotnet: v1 sources still valid, flag list and JSON in step, binder-side validation (GridMatches, increasing depths) as engine-free helpers |
| T-181c | Camera (separate brief, Camera owner): rig fills the three layers by per-layer `Stabilise`; scripted T1 sets 124.2 / 664.874 (and 663.0) / 10353 | Camera EditMode: one layer equals v1 bit for bit; layer offsets within half a layer grid; layer `Cut` on a reference change; zero allocation |
| T-181d | `PixelSlabCamera`, binder slab targets, layered Present + internal composite, shadows rule | UnityOnly (run in T-183): flag off bit-identical; layered output every pixel an exact palette colour; with all offsets 0 the composite equals the single render on ≥ 99.9 % of texels; band of every slab-seam texel equals the band of its own radial distance; draw-call and batch counts recorded |
| T-182 | Capture jobs: sidecar extension `layers` (versioned key: references, grids, boundaries, bands, per-slab shifts), c11 jobs at 124 m / 663 m / 10.4 km with `depthShift`, then `layerSnap` with main at 663.0 and at 664.874, haze jobs with the new tuning | dotnet/EditMode writer tests |
| T-183 | Unity batch run (queue rule §9): InstallAll, stills + hazecal, `m004direction`, the c11 jobs, pass timing and draw calls; logs to `evidence/T-176/` | no compile error; stop and report on any |
| T-184 | Metrics into the T-176 acceptance table, plus: C11 near/ref/far for both main depths, any-depth model figure beside it (R-131: N=12 5.4 px, N=20 4.9 px), C6/C7/c1/c2/DIG/A2/scale with `layerSnap` on, slab seam report (seam purity ≥ 0.99, partial-texel count, tears on units), draw-call ratio, `Pixel.*` CPU, GC | the T-176 acceptance numbers; model predictions 0.67 / 0.96 / 0.66 px at exact slab depths, 4.84 worst / 0.93 median at the rig main depth |
| T-185 | eval-style/eval-perf on the defect list; ledger rows C7, C11; merge per the GameCoreConfig rule if all pass | as T-176 |

## Known limits / TODO
- Dither look (Q-015): Bayer 4×4 is the default as §9 says; the references measure as clustered. Alternatives are one config value away.
- Anchoring (Q-016): no single mode meets both the yaw and the translation thresholds of M-004@v1 on the test scene; numbers for all modes are in the evidence. `direction` is the configured default (`PixelPipeline.json`); `hybrid` stays selectable.
- Rotation quantisation (Q-021) is a rig flag (`camera.quantiseRotation`); direction anchoring depends on it.
- 4K (Q-018): k = 6 (640×360) by the current rule; reported, not gated.
- Haze bands are spaced evenly in log distance between `hazeStartM` and `hazeFullM` (see `HazeMath`), not at equal transmittance steps as R-003 §5 derived: with a visual range of tens of km equal-transmittance bands leave everything inside several hundred metres unhazed, which fails the 80 m block against its background at 150 m. Assumed, pending the style panel.
- The hybrid surface pattern picks a power-of-two cell size per axis from screen derivatives; cell size changes are visible as faint contours on ground seen at grazing angles.
- Supersample and Outline are implemented and tested but off in M0. Light shafts, clustered smoke and dust are later slices; smoke should write coverage with the same threshold (screen-door), not blended colour.
- M-008 is not verified: timing here is from the development Mac only (Q-019).
- The pixel URP asset is activated through `QualitySettings.renderPipeline` at run time; no project quality level points at it yet (a ProjectSettings decision for integration).
