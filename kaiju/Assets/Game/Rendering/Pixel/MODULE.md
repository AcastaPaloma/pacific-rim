# Rendering.Pixel
Purpose: the painted-pixel look (P4): a 3D world rendered at a low internal resolution, pulled back in depth by posterised haze, mapped to a limited district palette with ordered dithering, and shown at an exact integer scale. Each stage is a separate URP 17.6 Render Graph renderer feature that can be reordered and switched on or off by feature flag.

Breakdown IDs: D-022, D-027, F-046, M-003, M-004, M-008 (render-side share), M-011 (output smoothness), M-012 (ramp uniformity), GAME_BREAKDOWN §9, MODULARITY §4.7, §3 layer L1. Research: R-002, R-003. Task: T-004.

## Assemblies (layer L1: depends on Core contracts only; never on Camera, Units or World)
| Assembly | Folder | Engine | Contents |
|---|---|---|---|
| `Game.Rendering.Pixel.Contracts` | `Contracts/` | none | `PixelStageId`, `PixelDitherAnchor`, `PixelFlags`, `PixelGrid` + `IPixelGridSource`, `PixelStabilisationHints` + `IPixelStabilisationSource`, `PixelScaleRule`, `PixelStageOrder`, `PixelContractVersions` |
| `Game.Rendering.Pixel.Maths` | `Maths/` | none | `ColourMath` (sRGB, CIELAB, OKLab, CIEDE2000, palette hash), `DitherMasks`, `PaletteLutBaker`, `HazeMath`, `MiniJson`, `PaletteSetReader` |
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
- `Assets/Game/Config/FeatureFlags/Rendering.Pixel.json`: the eight flags, all `false`.
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
- Engine-free (`dotnet test DotnetTests/Rendering.Pixel.Tests`, also Unity EditMode): CIEDE2000 against published test data, Lab, hex, palette hash, Bayer matrices and alternative masks, LUT bake (determinism, grey axis along the ramp, accent, edits), palette set reader and the five canonical hashes, haze bands to 30 km, stage-order rules, scale rule.
- Unity EditMode (`Tests/UnityOnly/PixelPipelineTests.cs`): every internal pixel is an exact palette colour with its index; output is an exact integer upscale; two renders bit-identical; depth in metres within 1 %; Quantise off gives index 255; all 16 combinations of the optional stages render valid frames; each stage changes the frame; a sixth profile from a JSON fixture needs no code change; editing a colour and re-baking; generated profiles equal the palette set and a fresh bake; installer keeps feature list and map in step; illegal orders rejected; a legal reorder renders the identical frame; an illegal run-time order is skipped with a warning.
- Metrics: M-003@v1 and M-004@v1 through the capture tool and the T-002 tools; results in `evidence/T-004/EVIDENCE.md`.

Owner role: implementation/pixel (`impl-pixel`).

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
