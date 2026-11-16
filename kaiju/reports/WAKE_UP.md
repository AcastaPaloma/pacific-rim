# WAKE_UP: the Kaiju demo (2026-11-16)

Branch `claude/fable-orchestrator-prompt-83a0a8` (never merged to `main`). Facts only; known failures are listed.

## Launch (macOS, Apple silicon)
```bash
cd kaiju
Builds/demo/KaijuGame.app/Contents/MacOS/KaijuGame -sliceConfig demo
```
- Add `-sliceWalk default` for the slow 2.67 m/s walk (the demo default is the fast ~5.8 m/s overlay).
- Windowed 1280x720 (the pixel look expects it): `-screen-width 1280 -screen-height 720 -screen-fullscreen 0 -popupwindow`. Smoke runs REQUIRE these (without them the end-of-run pixel check throws).
- Headless smoke: `... -sliceConfig demo -sliceSmoke 1200 -sliceSmokeOut /tmp/x/smoke.json <the window args>`.
- Rebuild: `Tools/Build/build.sh` (output `/tmp/kaiju-build/<commit>/b1/KaijuGame.app`, M-125 attribution + smoke gate). The build in `Builds/demo/` is from commit 6463b75 (T-202).
- Defaults are untouched: no argument = the `feelcheck` slice; `-sliceConfig m1` = the lean set.

## What works (player smoke, `evidence/T-202/smoke_demo_full.json`)
- The `demo` profile (`Assets/Game/Slice/Config/slice_demo.flags.json`): physics mech (Units.Mech, `walk_mode 2`), melee and cannon combat, scripted kaiju brain, destruction, seismic (quake Mw 6.9 on the Chabot fault), emergence, `audio.slice`, hangar stub, pixel pipeline (`lowRes`, `dither`, `haze`, `quantise`, `subPixelPresent`, `hazeFarLift`), `warm_amber_haze` palette. `rendering.pixel.layerSnap` is OFF (fails M-003.d-iv; T-196 is fixing it).
- Real-data San Francisco district (15 tiles, 16,615 buildings), 1128 building-damaged events, 4 buildings removed in the smoke, non-blank frames (pixel check PASS).
- The physics mech survives the 20 s scripted smoke (phase SingleSupport, 41 m centre-of-mass travel, max speed 6.5 m/s).
- Walk-speed ramp (T-201, `evidence/T-201/ramp.md`): last stable rung is a 6 m/s step-reach cap, about 5.8 m/s walked (default `walk_mode 2` is 2.67 m/s). Gate: no fall, pelvis tilt <= 5 deg, mean foot slip <= 0.5 m, 8 seeds x 63 s plus start/stop/estop/turn90/curve. 7 m/s misses the slip gate in stop and estop; 8 and 9 m/s never pass. The target of 9 m/s (Froude 0.2) is NOT reached.

## Stubbed, TEMPORARY, or broken (honest list)
- **Hand-of-god assist.** `walk_mode 2` holds the pelvis with a servo (net force ~7 % W, pelvis couple ~41 % W l, above one hip's limit). Pure-physics walking (`walk_mode 0`) is still unstable (best 30.8 s); T-005 is paused and stays an open requirement.
- **The mech still falls** on long unscripted straight walks over the terrain's 4 m height steps (about 26 s fast, about 55 s default). A TEMPORARY respawn after 2.5 s fallen exists (`-sliceNoRespawn 1` turns it off; the smoke JSON keeps `mech_fell_ever`/`mech_respawns` honest).
- **Combat is driven by an invisible placeholder proxy** (damage, cannon mount, aim, camera), placed on the mech each step; it does not use the mech's links. Hangar redeploy moves only the proxy (needs a body teleport API).
- **Co-op is OFF** (no in-process second seat; not faked).
- **Footfall audio shows 0 footfalls** (FootContact to audio mapping not checked).
- The mech covers only ~40 m in the 20 s smoke; it is a vertical slice, not the Bay-scale game.
- The player smoke needs the window flags above. `Tools/Build/build.sh` counts Unity batch processes with a `pgrep` that also matches shell wrapper lines containing `-batchmode`; a shim was needed once.

## Capture (100 s of the demo, recorded by the M3 lead with the new `-sliceRecord` mode)
- Master: `evidence/capture/demo/demo_100s_1080p30_yuv444p_crf6_MASTER.mp4` (48.9 MB, 100 s, 1920x1080 @ 30, H.264 4:4:4 CRF 6). Easy-to-play copy: `CASUAL_FALLBACK` yuv420p (20.7 MB; not a spec encode). Also `contact_sheet_5s.png`, `notes.md`, `frame_checks.json` in the same folder.
- Shows: the mech walking ~410 m at ~5.8 m/s with no fall and no respawn in the 100 s (other routes can fall), kaiju capsule, 4 duels, cannon and arm swings, 5 impacts, 4 quakes, 8 buildings removed.
- Honest flaws: destruction framing is weak (clearly visible only ~11-14 s); the kaiju is a capsule stand-in; non-blank frames 3000/3000, but the pixel grid minimum is 0.9945 (target 0.999) and the M-124 4:4:4 check FAILS on the worst frames (min 0.987 vs the 0.99 gate, mean 0.9965); M-123 and the lint were not run (the player writes no sidecars).
- 4:4:4 may not play in some players (QuickTime, browsers); use VLC or mpv, or the 4:2:0 copy.

## Next
M-007 (AI pack) -> Bay data (T-103b chain) -> T-124c performance -> M3 polish. Also open: walk speed toward 9 m/s, respawn API, co-op seat, footfall audio, layerSnap (T-196).
