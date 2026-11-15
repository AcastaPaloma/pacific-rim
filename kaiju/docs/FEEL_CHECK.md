# Founders' feel check (T-189)

A playable slice of everything built so far: the San Francisco Golden Gate / SoMa district, a walking mech you drive, a kaiju that hunts you, a cannon and an arm swing, earthquakes, destructible buildings, sound, and a hangar to repair and redeploy. It is a **feel** check, not a content check: the bodies are placeholders (see the end).

## Launch

Editor (Unity 6, project `kaiju/`): open `Assets/Game/Slice/Scenes/M1Slice.unity` and press Play. The scene loads the 15 tiles (about a minute in the editor; one tile per frame), then the HUD shows the controls.
macOS player: `unity run kaiju -- -executeMethod Game.Slice.Editor.SliceEditorTools.BuildMac`, then open `kaiju/Builds/M1Slice/M1Slice.app`.

The scene uses the full flag set `Assets/Game/Slice/Config/slice_feelcheck.flags.json` by default. The committed feature flags stay off everywhere else, so nothing else in the game changes. To run the older, leaner slice (no weapons, no hangar, no audio) start with `-sliceConfig m1`.

## Controls (also shown on screen, top left)

| Key | Action |
|---|---|
| W A S D (or left stick) | walk, relative to the camera |
| Space | stop |
| F or left mouse | fire the cannon (20 rounds, 3 s reload; aims at the kaiju when it is in front of you, otherwise straight ahead) |
| R | overhead arm swing (good against buildings; weak against the kaiju) |
| Q | earthquake on the nearest mapped fault; buildings take damage, the camera shakes |
| I | drop an impactor on the nearest building ahead |
| H | go to the hangar: full repair, ammo refill, redeploy at Crissy Field with a fresh kaiju ahead |

If the kaiju hits you ten times you are sent to the hangar automatically. Kill the kaiju with about eight cannon hits; a new one walks in four seconds later.

## What to judge

Walk feel (speed, turning), camera (third person, pixel-snapped), cannon weight and sound, whether destruction reads, whether the kaiju is threatening, the earthquake, and the audio. Footsteps, cannon, impacts, collapses, the quake and emergence all sound (flag `audio.slice`).

## What is placeholder (do not judge)

- **The mech body is a stand-in** (`Units.Placeholder`: a kinematic capsule on a flat height). Real legs, gait, falling, recoil and foot contact arrive with T-005. Until then it does not collide with buildings, does not follow terrain slopes, does not stumble, and the footfall sounds are a fixed cadence (one step per 20 m).
- The kaiju is a capsule that walks through buildings and melees from a distance; it is driven by the scripted brain, not the learned one.
- Weapons: real ballistics and contact law, but recoil is not applied to the body, and building and kaiju hit volumes are simple cylinders. Kaiju hit points (400) and building mass are guesses tagged ASSUMED in the code.
- The hangar repairs instantly and for free; no refit, parts or economy yet.
- Not in this build (off): pack AI and species, the meta economy, co-op networking, streaming beyond the 15 tiles.

Evidence of a headless run of this exact configuration: `kaiju/evidence/T-189/` (`smoke_feelcheck.json` and `SUMMARY.md`).
