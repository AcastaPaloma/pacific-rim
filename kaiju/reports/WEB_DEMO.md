# WEB_DEMO: the Kaiju demo in a browser (2026-11-16)

Branch `task/web-demo` (merged into `claude/fable-orchestrator-prompt-83a0a8`; never `main`). Local serving only: nothing is deployed or uploaded.

## Run it
```bash
cd kaiju
Tools/Build/build_web.sh      # ~5 min Unity batch build -> Builds/web_demo/KaijuGame (skip if the folder exists)
Tools/Build/serve_web.sh      # serves on http://localhost:8642 (127.0.0.1 only)
```
Open http://localhost:8642/ in Chrome and click the game once so it has keyboard focus.
Controls: WASD walk, Space stop, F (or left-click) cannon, R melee arm swing, Q quake, I impactor, H hangar.

URL options (the browser has no command line):
- `?sliceWalk=default` for the slow 2.67 m/s walk.
- `?sliceFlagOff=a,b` / `?sliceFlagOn=a,b` for flag overrides.
- `?webPixel=1` to try the real pixel pipeline (broken on WebGL2, see below).

## What it is
- A WebGL2 player (Unity 6000.6.9f1, IL2CPP to wasm, Gzip). The `demo` profile is baked in: on a WebGL player the slice defaults to `demo`. The desktop default (`feelcheck`) and `build.sh` are unchanged.
- Same content as the desktop demo:
  - the physics mech on `walk_mode 2` with the fast overlay;
  - melee and cannon;
  - the scripted kaiju;
  - destruction and seismic events;
  - all **15 real-data SF tiles with 16,615 buildings**.
- No tiles or buildings were cut.
- How the files get in: the geo loaders use System.IO, which cannot read StreamingAssets over http. `SliceWebPreload` downloads the 115 files in `StreamingAssets/web_files.txt` into the in-memory filesystem first, then `GeoModule.StreamingRootOverride` points the loaders there.

## Numbers (Chrome on this Mac, 1280x720 canvas, `Tools/Build/web_check.sh`; evidence/web_demo/final/)
| | |
|---|---|
| Load to playable (navigation to "[Slice] ready") | **17.6 s** (an earlier build: 26.7 s) |
| Tile preload | 115 files, 142 MB unpacked, 0.6 s from localhost |
| Frame rate (idle / walking / quake) | **120 / 120 / 120 fps** (the display's refresh cap) |
| Download, Unity build (gz) | 15.6 MB (wasm 11.4 MB, data 4.1 MB, framework 0.08 MB) |
| Download, tiles (gz) | 106 MB (terrain.tif dropped; it is not read at runtime) |
| Total download | **about 122 MB** |
| JS heap reported | 166 MB |
| Console errors | 0 |

The scripted input run (hold W, turn with D, Space, F x3, R, I, Q) recorded:
- the mech walking at up to 9.5 m/s centre-of-mass speed, then standing;
- shots 3, with hits on 1 kaiju and 2 buildings;
- 2 buildings destroyed;
- an impact of 16,875 MJ (state 2 to 4);
- a Mw 6.9 quake on the Chabot fault, with 1,126 damaged events;
- the kaiju reaching the mech and landing melee hits (hp 1.0 to 0.7).

Screenshots are `01_ready` to `08_after_quake.png` in that folder.

## What was cut or changed for the web (known issues)
- **The pixel URP pipeline does not work on WebGL2.** With any of its stages on, the frame is one flat colour or haze gradient, and the log shows no GL errors. Bisecting with flags (`evidence/web_demo/bis_*`) found:
  - with `rendering.pixel.enabled` off, the city renders correctly;
  - with haze off, the frame is one flat colour;
  - with quantise and dither off, it is a haze gradient only.

  So the low-res colour or depth targets are not read correctly on GLES (suspects: the R32_SFloat internal depth, or the sRGB camera target with a 32-bit depth buffer). This is not fixed. **The web profile turns the pixel pipeline off.** In its place it renders URP at a 0.34 render scale with point upscaling. That only gives a rough chunky look: no palette, dither or haze.
- WebGL2, not WebGPU. WebGPU in 6000.6 is still experimental for web, and the custom passes are untested on it.
- Gzip, not Brotli (HTTP localhost, faster builds). Exceptions are "explicitly thrown only".
- Several URP post shaders (depth of field, Panini, FSR) are stripped. They log "not supported" at start-up and are unused.
- Not tested: Safari and Firefox, a cold load over a real network (about 122 MB will take far longer than 0.6 s), long sessions or memory growth, and the mobile browser.
- Everything listed as stubbed in WAKE_UP.md still applies:
  - the hand-of-god assist;
  - falls on long walks, with respawn;
  - the combat proxy;
  - co-op off;
  - the capsule kaiju.

## Files
- `Tools/Build/build_web.sh`: the build, with StreamingAssets post-processing and the canvas/log patch.
- `Tools/Build/serve_web.sh` and `serve_web.py`: the local server (Content-Encoding and wasm MIME types).
- `Tools/Build/web_check.sh` and `web_check.py`: the automated Chrome check (CDP, throwaway profile).
- `PlayerBuild.cs`: `-buildTarget web`.
- `SliceWebPreload.cs`, plus small edits in `SliceBootstrap.cs`, `GeoModule.cs` and `GeoTileBuilder.cs`.
