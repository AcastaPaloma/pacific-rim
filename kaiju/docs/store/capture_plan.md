# Store capture plan (T-146, M3 Track 3)

Author: M3 worker, 2026-11-15, founders asleep (§15). Inputs: `plans/M3.md` Track 3 and Q-234, `research/R-121.md`, `research/R-120.md` §5, `Assets/StreamingAssets/Geo/global/v1/landmarks.json`, `Assets/Game/Tools/StyleCapture/README.md`, `style_capture@v1` (`research/R-002.md` §6, `Tools/Metrics`). Consumers: T-147 (Unity driver + `capture_path@v1`), T-148 (Python checks, encode, sheets). No Unity was run for this document.

## 1. Decisions (decided autonomously, founder asleep)
| ID | Decision | Reason / alternative |
|---|---|---|
| D1 | Internal frame **480x270**; scale x4 = 1920x1080, x8 = 3840x2160 (both even, so 2x2 chroma blocks sit inside one source pixel). | R-121 Q-NEW-121a. The `style_capture@v1` example uses 320x180 (x6, x12); allowed if the style track's shipping size differs: the driver reads the size from the pipeline settings, never hardcodes it. Alt: larger internal frame plus crop. |
| D2 | Masters are lossless PNG sequences of `color_output` at 1920x1080 and (stills only) 3840x2160, plus the full `style_capture@v1` set at internal resolution. Outside git. | Style tools read PNG bit-exactly. |
| D3 | 60 fps trailer, hard cuts, no fades, no motion blur, no frame blending. | Fades are mixes that fail M-003; shots must also read at 30 fps. |
| D4 | Shots are real gameplay driven by a scripted camera plus scripted/recorded controllers. No mock scenes. | Steam screenshot rule (R-120 §5). |
| D5 | All new code is new files; `StyleCapture` files are not edited. | Track 3 brief, m2-track-rules 14. |

## 2. Shot list per pillar
Pillars: P1 towering weight, P2 physical truth, P3 real Bay, P4 painted-pixel beauty, P5 co-op vs a pack. Status key: **NOW** = capturable with what exists (pixel pipeline, StyleCapture rig, test scenes, terrain/landmark geometry as streamed today); **B-walk** = blocked on mech walk (T-005); **B-kaiju** = blocked on kaiju bodies (physics and meshes); **B-M2** = blocked on streaming of the real Bay beyond the test scenes (M2); **B-HUD/B-coop** = blocked on UI / second player. A shot may list several blockers; it is NOW only when none apply. BayTM is `kaiju:BayTM@v1` metres [east, north] from `landmarks.json` viewpoints V1-V5; landmark rows give lat/lon (WGS84) and T-147 converts them with the Geo transform (not hand-computed here).

Viewpoints (from `landmarks.json`): V1 Treasure Island SW shore [64572.6, 77348.4] eye 10 m hdg 235; V2 Twin Peaks summit [57904.5, 69908.6] +1.7 m hdg 45; V3 Vista Point, Marin [55004.6, 78793.2] +1.7 m hdg 160; V4 Bay east of Ferry Building [63300, 74900] eye 10 m NAVD88 hdg 270; V5 Coit Tower deck [61591.4, 75450.7] eye 146 m hdg 170.

### P1 Towering weight (trailer T1, T9; stills S1)
| ID | Location | Path type | Dur | Required systems | Status |
|---|---|---|---|---|---|
| P1-a | Street canyon, SoMa near Salesforce Tower (37.7898, -122.3969), eye 1.7 m, looking up at a foot plant | `static` + impact shake (existing orbit-style shake) | 4 s | kaiju foot + dust/debris VFX, scale cues (cars, people), pixel snap | B-kaiju |
| P1-b | V4 Bay east of Ferry Building, kaiju wading in from water toward the Ferry Building | `dolly` (slow push), eye 10 m | 6 s | kaiju body + wake, water pass, Ferry Building landmark | B-kaiju, B-M2 |
| P1-c | V5 Coit Tower deck, hdg 170, kaiju silhouette over Financial District, mech small in foreground | `orbit` 40 deg arc | 5 s | kaiju + mech bodies, haze depth bands | B-kaiju, B-walk, B-M2 |
| P1-d (placeholder) | V5 same framing with a scale-reference proxy (Transamerica Pyramid / Salesforce Tower only), no kaiju | `orbit` | 5 s | landmark geometry, haze | NOW (style-only fallback; not for the store) |
| P1-e | Pack emerges from the water at range (trailer climax, R-121 T9) | `crane` rise then hold | 7 s | 3+ kaiju types, mechs forming up, AI policies | B-kaiju, B-walk, B-M2 |

### P2 Physical truth (T2, T3, T10; S2, S8)
| ID | Location | Path type | Dur | Required systems | Status |
|---|---|---|---|---|---|
| P2-a | Mech foot strike on pavement (street test scene), heel-to-toe roll, debris settle | `follow` (locked profile, no cut) | 4 s | walk (T-005), gait log, ground deflection/debris | B-walk |
| P2-b | Mech arm hits kaiju limb, hit stop, plate dent, strain gauge visible | `static` close | 3 s | Combat, damage model, kaiju limb collider, HUD | B-walk, B-kaiju, B-HUD |
| P2-c | Overheat/ammo/power warning, vent, retreat | `follow` | 4 s | heat/power/ammo systems (D-019), HUD | B-walk, B-HUD |

### P3 Real Bay (T4, T5; S3)
| ID | Location | Path type | Dur | Required systems | Status |
|---|---|---|---|---|---|
| P3-a | Golden Gate (south tower 37.8140, -122.4779; north tower 37.8255, -122.4792) from V3 Vista Point Marin, hdg 160, haze to the 10 km layer | `dolly` toward the 10 km layer (existing `dolly` job geometry), profile `warm_amber_haze` | 6 s | Geo tiles for Marin headlands + bridge, haze | B-M2 (test scenes only hold station geometry). Fallback NOW: StyleCapture `dolly` over the test scene |
| P3-b | Bay Bridge W2/W6/SAS towers (37.7908,-122.3858 / 37.8056,-122.3699 / 37.8153,-122.3585) from V1 Treasure Island SW shore, hdg 235 | `dolly` lateral (truck) | 5 s | streamed terrain, bridge landmarks | B-M2 |
| P3-c | Twin Peaks V2 hdg 45 over the city grid to Salesforce Tower | `orbit` 60 deg | 6 s | streamed city, landmark heights | B-M2 |
| P3-d | SoMa canyon with a mech passing a landmark (Salesforce / Transamerica 37.7952,-122.4028) | `follow` | 5 s | walk, streamed city | B-walk, B-M2 |
| P3-e | Sutro Tower (37.7552, -122.4529) hazed skyline from V2 | `static` | 4 s (still) | landmark geometry | B-M2 |

### P4 Painted-pixel beauty (T6, T11; S3, S5)
| ID | Location | Path type | Dur | Required systems | Status |
|---|---|---|---|---|---|
| P4-a | Sunrise sky with dithered bands, bay and piers tiny below, from V5 hdg 170 (no actors) | `static` + slow `yaw` 0.25 px/frame (matches M-004 yaw set) | 6 s | pixel pipeline, haze, palette profile `lavender_peach` | NOW (V5 geometry in test scene scope to confirm; else B-M2) |
| P4-b | Palette tour: the five district profiles on the same fixed station | `static` x5, hard cuts | 2 s each | StyleCapture profiles | NOW |
| P4-c | Golden-hour close-up of a mech plated forearm (S5) | `static` close | 3 s | mech mesh with plates, outline off/on per style | B-walk (mesh: placeholder unit only) |
| P4-d | Dolly across haze layers (reuse of `dolly`, 180 frames) | `dolly` | 3 s | pixel pipeline | NOW |
| P4-e | Title card T11 (logotype layer, title only) | `static`, composited | 3 s | logotype asset | NOW (needs logotype; non-world frame, UI palette check) |

### P5 Co-op vs a pack (T7, T8, T9; S4, S6, S7)
| ID | Location | Path type | Dur | Required systems | Status |
|---|---|---|---|---|---|
| P5-a | Hangar: assembling a mech, hatch opens (D-023 frame) | `static` + `crane` | 5 s | hangar scene, assembly UI | B-HUD (UI not built) |
| P5-b | Two mechs flank one kaiju, callouts, one draws aggro | `orbit` | 6 s | two mech instances, kaiju, intent controls, ping/callouts | B-walk, B-kaiju, B-coop |
| P5-c | Mixed pack at range, tactical read from V4 | `crane` high | 6 s | 3+ kaiju types, mechs, AI | B-kaiju, B-walk, B-M2 |
| P5-d | Split-role 2-player screenshot | `static` | still | netcode co-op session, HUD | B-coop |

Counts (22 shots): capturable NOW 5 (P1-d, P4-a, P4-b, P4-d, P4-e; P4-a conditional on V5 being in loaded geometry), blocked 17 (P1 4, P2 3, P3 5, P4 1, P5 4; P3-a has a NOW fallback via the `dolly` test-scene job that is not store material). Authoritative counts live in the table above; T-147 regenerates `shots.json` with a `status` field and T-148 prints the same tally. Clearing order: T-005 walk unlocks P2-a/c, P3-d, P4-c; kaiju bodies unlock P1, P2-b, P5; M2 streaming unlocks all P3 and the V1-V5 viewpoints at full fidelity.

### Trailer cut (60-90 s, 60 fps; hook under 10 s; works muted)
Cold open T1/P1-a (4 s) -> P3-a Golden Gate (5 s) -> P5-a hangar (4 s) -> P2-a foot strike (4 s) -> P4-a sunrise (4 s) -> P2-b hit (3 s) -> P3-d SoMa (5 s) -> P5-b co-op flank (6 s) -> P2-c warning (3 s) -> P1-e pack climax (7 s) -> P4-e title (3 s). First 6 s = microtrailer loop (P1-a + P1-e teaser). Hard cuts at beat boundaries.

## 3. `capture_path@v1` data spec (JSON, ScriptableObject-free so Python can lint it)
```
{ "schema": "capture_path@v1", "path_id": "p3_a_golden_gate", "pillar": "P3",
  "viewpoint": {"id":"V3","baytm":[55004.615,78793.17],"eye_m_above_ground":1.7,"heading_deg":160},
  "profile_id": "warm_amber_haze", "district_id": "SF-GoldenGate", "time_of_day": "fixed_v1",
  "fps": 60, "duration_s": 6.0, "seed": 146001,
  "interpolation": "catmull_rom",   // linear | catmull_rom | ease_in_out (position); yaw/pitch: shortest-arc slerp
  "keyframes": [ {"t_s":0.0, "pos_baytm":[e,n], "alt_m":1.7, "yaw_deg":160, "pitch_deg":-2, "hfov_deg":60}, ... ],
  "shake": [ {"t_s":2.0, "amp_internal_px":2.0, "decay_s":0.4} ],
  "snap": {"enabled":true, "dither_anchor":"direction"},
  "outputs": [ {"internal_wh":[480,270],"output_wh":[1920,1080],"scale":4}, {"internal_wh":[480,270],"output_wh":[3840,2160],"scale":8} ],
  "mode": "clip" | "still", "still_frames": [int], "actors": "scripted_controller_id | null", "requires": ["walk","kaiju","m2_streaming"] }
```
Rules, all checked by `capture_path.py lint` (T-148) and by the driver at load:
1. `schema`, `path_id` (unique, `[a-z0-9_]`), `fps` in {60, 30}, `duration_s * fps` integer, `keyframes` sorted by `t_s`, first `t_s` = 0, last = `duration_s`, >= 2 keyframes (>= 4 for `catmull_rom`).
2. **Determinism.** Time is the frame index: sample at exactly `n / fps`, never wall clock. `seed` seeds one `System.Random`-free xorshift stream used for shake and any scripted-controller noise; the same path, build commit and flags produce bit-identical PNGs (the existing StyleCapture guarantee; the driver reruns the first 10 frames and compares sha256 as a self-check).
3. **Pixel-snapped camera.** After interpolation the pose passes through the pipeline's snap (position grid, yaw/pitch step) exactly as the shipping camera does; the sidecar writes the snapped pose and `snap.enabled = true`. Driving the camera with unsnapped poses is a lint error. Dither anchor must be `direction`.
4. **Resolutions.** Every `outputs[]` entry satisfies `output_wh == internal_wh * scale` with integer `scale` (Q-234); 1920x1080 and 3840x2160 are the only allowed targets for gameplay media. If a requested internal frame does not divide, the driver renders extra guard columns/rows and crops (never stretches).
5. Coordinates: BayTM metres in, floating-origin local pose out (F-042). `alt_m` is above ground unless `alt_navd88` is given.
6. `requires` lists capability tags; the driver refuses (exit 3, "blocked: <tag>") rather than render a stand-in, except for paths explicitly marked `"fallback_of": "<path_id>"`.

## 4. Unity driver design (T-147)
- **New files only**, in a new assembly `Game.Tools.Capture` (`Assets/Game/Tools/Capture/`, Editor-only asmdef referencing `Game.Tools.StyleCapture`, with `MODULE.md`). No file of `StyleCapture` is edited; the driver reuses its public `PixelCaptureRig`, `CapturedFrame`, PNG writers (`WriteRgbPng`, `WriteGreyPng`, `WriteGrey16Png`, `EncodeDepthLog16`) and mirrors the sidecar schema. If a needed member is `internal`, T-147 raises it with the lead instead of editing.
- Files: `CapturePath.cs` (parse and lint `capture_path@v1`), `CapturePathSampler.cs` (pure maths: interpolation, xorshift shake; dotnet-testable if kept in a Unity-free assembly `Game.Tools.Capture.Core`), `CaptureDriver.cs` (frame loop), `CaptureCli.cs` (batch entry).
- Entry: `unity run . --no-tail --log-file <log> --timeout 1800 -- -executeMethod Game.Tools.Capture.CaptureCli.Run -capturePaths <dir|a,b> -captureOut <dir> -captureScale 4,8 -captureStillsOnly`. Batch, headless (`-batchmode`, no `-nographics`: the rig needs a GPU context as StyleCapture does). Queue rule: fewer than 2 Unity batch processes; never port 7800.
- Frame loop: set `Time.captureFramerate = fps` and step the simulation with the project fixed-step driver; for each frame n sample the path at `n / fps`, set the pose, apply snap, `rig.Render(renderOutput: true)`, `rig.Read(true)`, write PNGs. UI off for style frames (`mask_ui` if it cannot be disabled); a second HUD-on pass writes `hud/` PNGs for screenshots that need the HUD.
- Output per path: `<out>/<path_id>/f%06d_color_output.png` (+ `_color_internal`, `_index`, `_depth`, `_mask_animated`, `f%06d.json`, `sequence.json`, plus `capture_path.json` copy, `hashes.sha256`). Each scale is a sibling folder `x4/`, `x8/`; `color_output` is produced by the pipeline's present stage, not by a resample. PNG: 8-bit sRGB, no alpha, no gAMA/ICC.
- Lossless guarantee: no TAA, bloom, or motion blur in the capture feature-flag set; flags recorded in sidecar `build.feature_flags`. The run throws if the log has any error, exits 3 for blocked paths, and writes `capture_log_summary.txt` like StyleCapture.
- New feature flag: `Tools.Capture.Enabled` default off in `Assets/Game/Config/FeatureFlags/` (committed config installs nothing).
- Evidence layout: masters under `evidence/T-147/captures_local/` (git-ignored, hash lists committed); store candidates copied to `docs/store/media/` only after M-123 passes.
- Scene and actors: paths use the street/eye70/tower test scenes until M2 streaming lands; `requires` tags select the scene loader. Scripted controllers come from `Game.AI.Scripted` (consume only).

## 5. Python checks (T-148, module `Pipelines/capture/`, env `/opt/miniconda3/envs/kaiju/bin/python`)
Reuses `Tools/Metrics` (`kaiju_metrics.style.m003`, `m004`, `colour.ciede2000`, `png`) by import; no copying of metric code. CLI `capture.py <cmd>`; report `capture_report@v1` JSON + `summary.txt`; exit 0 pass, 2 fail.
| Check | Command | Procedure | Threshold |
|---|---|---|---|
| **M-122** store spec | `capture.py spec --assets <dir> --trailer <mp4>` | Table from R-120 §5 version-stamped 2026-11-15. PNG/JPG header read for exact sizes (capsules 920x430, 462x174, 1232x706, 748x896; library 600x900, hero 3840x1240, logo 1280 wide or 720 tall with alpha; header 920x430; icons 184x184 jpg, 256x256; page bg 1438x810 optional), screenshots >= 5 (target 8) at >= 1920x1080 with ratio exactly 16:9; `ffprobe` trailer: h264, <= 1920x1080, fps in {30, 29.97, 60, 59.94}, bitrate >= 5000 kbps, mp4, AAC 44.1/48 kHz, 16:9. Demo: >= 5 demo screenshots plus trailer. | 100 % of rows |
| **M-123** every frame | `capture.py style --set <captures> --profile-from-sidecar` | For each frame run M-003@v1 gated groups on `color_internal`/`color_output` with the profile named in the sidecar (palette adherence ΔE00 <= 2.0 on >= 95 % of output and >= 99.5 % of internal pixels, index adherence exact, dither present, depth banding monotonic); for each clip run M-004@v1 (shimmer/crawl) in `direction` mode, skipping `mask_animated` pixels. Also verify integer scale (`output_wh == internal_wh * scale`), sha256 list, and that no frame needs a stretch (pixel blocks of exactly `scale x scale`). Non-world frames (logo, title) are exempt only if listed in the path with `"non_world": true` and must pass UI-palette index adherence. | 100 % of store frames; a failing frame is replaced, not excused |
| **M-124** encode fidelity | `capture.py fidelity --master <frames> --video <mp4>` | Decode (ffmpeg, same colour tags, to RGB), compare every 10th frame plus all cut frames (>= 60 frames, 100 % of those) to the master at the encoded resolution in CIELAB D65 2 deg with CIEDE2000; report share <= 2.0, min and mean over frames, 99.9th percentile ΔE, global mean shift. | >= 99 % of pixels at ΔE00 <= 2.0 for every sampled frame (99.9th percentile reported, not gated); never lowered to pass |
| Encode | `capture.py encode --frames <dir> --audio <wav> --out <mp4> --crf N` | The R-121 §5 ffmpeg command (scale already done in the master; `-tune animation`, yuv420p, bt709 tags, `-movflags +faststart`); if a scale is ever needed `flags=neighbor` and scale in RGB before conversion. | n/a |
| Control matrix | `capture.py matrix` | R-121 §6: master scale {1,2,3,4} x pix_fmt {yuv420p, yuv444p} x CRF {10,14,18,23} x tune {none, animation}, ~60 frames per cell from `stills` and `dolly`; select the cheapest cell with >= 99.5 % (0.5 % margin) and record it in `docs/store/encode_settings.json`. | choose cell, do not move the gate |
| Lint | `capture.py lint --paths <dir>` | `capture_path@v1` rules in §3. | all rules |
| Sheets | `capture.py sheets` | Contact sheets (nearest-neighbour thumbnails only), per-pillar coverage table, blocked tally. | n/a |
Fixtures for T-148 tests: 10 synthetic frames from `Tools/Metrics/fixtures/gen_style.py`, plus a deliberately bilinear-scaled frame (must fail M-123) and a yuv420p 1x encode (must fail M-124 in dither regions).

## 6. Store asset production list (R-120 §5 sizes)
All art is a composition on an integer-scaled oversize canvas (nearest neighbour, one integer per asset), then a crop; logotype is vector or hand-pixelled at target size; capsule rules: title only, no taglines/awards/quotes. AI-generated art is not used without provenance (M-120).
| Asset | Size | Source and method | Status |
|---|---|---|---|
| Header capsule / Library header | 920x430 | crop of P1/P5 hero still at x8 oversize + logotype | blocked on kaiju + logotype |
| Small capsule | 462x174 | logotype nearly filling, P4 palette backdrop (P4-b) | NOW once logotype exists |
| Main capsule | 1232x706 | S1 composition + logotype | blocked (P1-b/P5) |
| Vertical capsule | 748x896 | crop of a tall x8 render, V5 sky (P4-a) + logotype | NOW once logotype exists |
| Page background | 1438x810 (optional) | P3-a still, no actors | B-M2 |
| Library capsule | 600x900 | art + logo only | blocked (kaiju) |
| Library hero | 3840x1240 PNG | P3/P4 wide still, no words (x8 crop band of 3840x2160) | B-M2 (fallback NOW: P4-a) |
| Library logo | 1280 wide and/or 720 tall PNG, transparent | logotype only | needs logotype (founder/art) |
| App icon / shortcut icon | 184x184 jpg / 256x256 ico+png | mech silhouette in palette | placeholder NOW, final needs art |
| Screenshots | >= 5, target 8, >= 1920x1080 (also 3840x2160) PNG | S1-S8 from §2: S1 P1-b, S2 P2-a, S3 P3-a, S4 P5-d, S5 P4-c, S6 P5-c, S7 P5-a, S8 P2-c | 0 of 8 final (S3/S5 fallback frames NOW) |
| Trailer | mp4 H.264 1920x1080 60 fps >= 5 Mbps, AAC 48 kHz | §2 trailer cut from PNG masters | blocked; audio from Audio module |
| Custom poster | 1920x1080 PNG/JPG | still from trailer | after trailer |
| Demo set | >= 5 demo screenshots + trailer | subset of the above scoped to demo content | Track 4 |
| Bundle header | 707x232 | only if a bundle exists | skipped |
Hand-offs: founders review the final screenshot set (blind panel >= 8/10 by evaluators, not script); the all-ages rating decision waits on the content survey; the 4K YouTube asset is separate and outside M3 gating.

## 7. Open items
- Logotype and final icon art need a human/artist input (placeholders tracked, flagged).
- Confirm the shipping internal size with the style track (480x270 assumed).
- Whether V5 and the Golden Gate framing exist in test-scene geometry is checked by T-147's first dry run (`requires` tags).
- Landmarks carry lat/lon only; T-147 adds the BayTM conversion call and writes the result into path files.
