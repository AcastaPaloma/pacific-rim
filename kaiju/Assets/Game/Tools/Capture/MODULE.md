# Tools.Capture (T-147, M3 Track 3)
Purpose: deterministic, headless store captures. A `capture_path@v1` JSON file scripts a camera over a real Bay viewpoint (landmarks.json V1-V5, M-005); the driver renders it through the shipping pixel pipeline and writes lossless integer-scaled frames. Design: `docs/store/capture_plan.md` sections 3-4.

## Assemblies
| Assembly | Folder | Engine | Contents |
|---|---|---|---|
| `Game.Tools.Capture.Core` | `Core/` | none | `CapturePath` (parse + `Lint`, rules L01-L15), `CapturePathSampler` (interpolation, stateless-hash shake). Also built by `DotnetTests/Tools.Capture.Core`. |
| `Game.Tools.Capture` | `Editor/` | Editor only | `CaptureScene` (tile + sun + pixel cameras, saved to `Generated/`, git-ignored), `CaptureDriver` (frame loop, replay self-check, hashes), `CaptureCli` (batch entry). Extends `Game.Tools.StyleCapture` through its public `StyleCaptureSession` and `SequenceWriter`; no file of StyleCapture or StyleFrame is edited. |
| `Game.Tools.Capture.Tests` | `Tests/EditMode/` | test | lint rules, sampler maths; also run by `DotnetTests/Tools.Capture.Tests`. |

## Run
Flag `tools.capture.enabled` (`Config/FeatureFlags/Tools.Capture.json`) is off; a run turns it on for itself with `-captureEnable`. With neither, the entry point logs and does nothing.
```
unity run . --no-tail --log-file <log> --timeout 1800 -- -executeMethod Game.Tools.Capture.CaptureCli.Run \
  -capturePaths docs/store/paths -captureOut <dir> -captureEnable [-captureScale 4,8] [-captureMaxFrames N] [-captureStride K] [-captureFailOnBlocked]
```
Exit 0 ok, 1 error (lint failure, log errors, replay differs), 3 a path was blocked and `-captureFailOnBlocked` was given. Check first: `capture.py lint --paths docs/store/paths` (Pipelines/capture).

## Output
`<out>/<path_id>/capture_path.json`, `<out>/<path_id>/x4/` (and `x8/` for stills): per frame `fNNNNNN_color_internal|color_output|index|depth|mask_animated.png` and a `style_capture@v1` sidecar (extra member `capture_path` with the sampled pose, seed, tile, scale), `sequence.json`, `hashes.sha256`. `<out>/capture_run.json`, `capture_log_summary.txt`, `capture_log_warnings.txt`. Frame check: `capture.py style --set <out> --out <dir>`.

## Rules and limits
- Time is the frame index (`n / fps`); shake is a stateless hash of (seed, frame, axis), so frames can be rendered in any order. The driver replays the first 10 frames from a cut and compares the presented pixels bit for bit.
- Poses go through the rig's scripted pose, so position and rotation snap exactly like the shipping camera. `hfov_deg` in a keyframe is informational: the rig sets the field of view from the pixel grid.
- Integer scale is pinned per output (`minScale = maxScale = scale`, `targetLines = internal height`) so x8 (3840x2160) keeps the 480x270 internal frame; the pipeline default would clamp to x6.
- A path with unmet `requires` tags, or whose tile is not in StreamingAssets, is blocked, never replaced by a stand-in.
- Sidecars use `dt = 1/60` (StyleCapture constant); a 30 fps path still samples at `n / 30` but its `sim_time_s` is nominal (only 60 fps paths are shipped).
- One scene and session per tile; a path stays inside its tile.

## M-004 motion sets, HUD pass, shots (T-191)
- **Motion sets:** a path with `"purpose": "M-004"` and `"motion_set": {"classes": [...]}` (rule L17) renders each class from the first keyframe pose: S0 static (60 frames), T1/T2/T3 translation along the camera's right axis at 0.1/0.25/1.0 internal px per frame (at the reference depth), Y1/Y2 yaw at 0.25/1.0 px per frame (180 frames). Same formulas as the StyleCapture M-004 job; sequences are `<out>/<path_id>/<viewpoint>_<class>/`, purpose `M-004`. Frame limits (`-captureMaxFrames`, stride) do not apply to them. Files: `docs/store/paths_motion/` (V4 and V5 x three palette profiles = the metric's 3 profiles x 2 viewpoints). Check: `capture.py motion --set <out> --out <dir>` (one M-004 run over all sets).
- **HUD pass:** `docs/store/paths_hud/` are x4 stills; `capture.py hud --captures <out> --scenes docs/store/hud_scenes.json --out <dir>` composites the feel-check HUD strings (same text as `SliceBootstrap.OnGUI`, help text read from `SliceFeelRig.Help`) at the IMGUI position. It is a composite, not a live IMGUI render (batch editors cannot screen-capture, T-189), and the scene state values are definitions.
- **`capture.py shots`** writes `docs/store/shots.json` (22 shots, status per shot). `capture.py matrix` takes `--select-pix-fmt` / `--select-scale` (selection only; the whole grid is measured) and writes `docs/store/encode_settings.json` with `--settings-out`.
