# Audio.SliceBridge (T-188)
Purpose: plays the physical audio runtime (`Game.Audio`, T-190) in the M1 slice scene. Subscribes to slice bus events, builds `AudioEvent`s, plans (distance, absorption, delay, pan, Doppler), allocates voices, synthesises procedurally (modal + noise + infrasonic beds), mixes stereo, limits. Behind `audio.slice` (default off, `Config/FeatureFlags/AudioSlice.json`); `SliceBootstrap` installs it only when the slice flag set turns it on (add `"audio.slice": true` to `Slice/Config/slice_m1.flags.json` to hear it).

| Assembly | Folder | Engine | Contents |
|---|---|---|---|
| `Game.Audio.SliceBridge` | `Runtime/` | none | `SliceAudioBridge` (bus events to `AudioEvent`, `IAudioEmitter`), `PlaceholderStepCadence`, `SliceAudioAssumptions`, `SliceAudioEngine` (planner + allocator + voices + limiter), `SliceAudioCapture` (headless WAV + metrics) |
| `Game.Audio.SliceBridge.Unity` | `Unity/` | UnityEngine | `SliceAudioBehaviour` (AudioSource carrier + `OnAudioFilterRead`, per-frame drain and listener), `SliceAudioInstaller` |
| tests | `DotnetTests/Audio.SliceBridge.Tests` | test | NUnit (26) |

## Event map
`BuildingDamaged` (state increased; Moderate+ or direct impact) to StructureFailure + DebrisRain; `QuakeBegan` to QuakeP / QuakeS / QuakeRumble at the hypocentre (energy from Mw, arrival at 6000 / 3500 m/s, not the speed of sound); `EmergencePoint` to a rock Impact; `WeaponFired` to MuzzleBlast (E = 1/2 m v^2, v = 1000 m/s assumed); `ImpactImpulse` to Impact (mass = p^2 / 2E, material by target kind). Footfalls: `PlaceholderStepCadence` (TEMPORARY: one footfall per 20 m walked, alternating feet) until Physics.Body publishes `FootContact`; `SliceAudioBridge.OnFootContact` is the ready hookup (T-005 era), then set `SliceAudioBehaviour.PlaceholderFootfalls = false`.
Frame: world metres; the behaviour adds the floating origin to Unity-frame positions. Per-drain cap 12 events (quake collapses): quake, footfall and muzzle kinds are always kept, the rest by energy.

## Metric / capture
`SliceAudioCapture.Run(FullScript(), 28 s)` renders walk, shots, impacts, a failing building, a quake, a kaiju rise and a 120-event burst to WAV; `SoloMetrics` renders each class alone. Checks (dotnet tests): peak out <= limiter ceiling (0.89), peak active voices <= 32 with the burst over capacity, each class RMS > 1e-4, identical bytes across two runs. Evidence: `evidence/T-188/` (set `KAIJU_AUDIO_EVIDENCE_DIR` and run the tests to regenerate the WAV and metrics).

## Known limits
No ITD applied in the voice (pan only), no occlusion (blockers = 0), no reverb, listener = camera; not heard on a device in this task (headless). Quake beds are infrasonic (15-25 Hz plus an octave): inaudible on small speakers. Assumed constants in `SliceAudioAssumptions` (muzzle velocity, debris drop height, stride, foot mass).
