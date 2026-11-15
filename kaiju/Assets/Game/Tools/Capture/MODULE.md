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
