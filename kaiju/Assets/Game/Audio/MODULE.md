# Audio
Purpose: physics-driven audio for 80 m / 6,000 t mechs and 10,000 t kaiju. Every sound derives from a physical event (energy, impulse, mass, material, distance); procedural synthesis first (modal for steel / concrete / glass, granular debris, filtered-noise blasts), sample layers later and only original IP.
Breakdown IDs: F-009 (audio at the exact contact frame, M-009), F-070 (proposed, R-132 section 12). Research: `research/R-132.md`. Task plan: `tasks/T-190.md`.
Layer: L3 consumer (reads events, emits none). Engine-free; no Unity adapter yet (T-195, queued behind the Unity queue rule).

## Assemblies
| Assembly | Folder | Engine | Contents |
|---|---|---|---|
| `Game.Audio.Contracts` | `Contracts/` | none | `AudioEvent`, `AudioEventKind`, `AudioMaterial`, `AudioSurface`, `Listener`, `VoiceCommand`, `IAudioEmitter`, `IAudioSink`, `IAudioMixer`, `AudioProfile`, `AudioFlags`, `AudioContractVersions` (EventApi 1, ProfileRevision 1) |
| `Game.Audio` | `Runtime/` | none | `AudioMath` (energy to SPL, spreading, ISO 9613-1 absorption, delay, Doppler, occlusion, perceptual map, pan/ITD, seismic helpers), `ModalSynth` (mode sets + reference render), `VoiceAllocator` (budget, priority, stealing), `Limiter`, `AudioPlanner` |
| tests | `DotnetTests/Audio.Tests/Dotnet/` | test | NUnit, `dotnet test DotnetTests/Audio.Tests` (59 tests) |

## Events it will consume (via the Unity adapter, T-195; none are subscribed in this module)
`Game.Physics.Body`: `FootContact` (exact step, position, `VerticalImpulseNs`), `FootLoaded` (peak force / impulse), `FootLiftOff`. `Game.Combat`: `WeaponFired`, `RecoilImpulse`, `ImpactImpulse` (`EnergyJ`, `ImpulseNs`). `Game.World.Destruction`: `BuildingDamaged` (`EnergyJ`, `DebrisKg`, `Cause`). `Game.World.Seismic`: `QuakeBegan` (Mw, origin), `FaultRupture`, `EmergencePoint`, and `IShakingField` (PGA at the listener). Emits events: none.

## Feature flag
`audio.physical` (`AudioFlags.Physical`), default off. Not added to a Core flags file (flags are per module: each module has a `*Flags` class and a JSON in `Config/FeatureFlags/`); the JSON `Config/FeatureFlags/Audio.json` is created by T-195 because that folder belongs to the Config module.

## Assumed (see R-132 section 13 for the full table)
Radiated-energy efficiencies and burst durations per kind, modal ratios / damping / contact times, kind priority bonuses, occlusion 8 dB per blocker (30 dB cap), 15 C / 75 percent RH defaults, Brune stress drop 3 MPa, vp 6000 / vs 3500 m/s, perceptual map 140 dB SPL to -3 dBFS and 40 dB to -60 dBFS. Changing any bumps `ProfileRevision`.

## Known limits
Linear acoustics only (no shock propagation: SPL clamped at 194 dB); no wind or temperature gradients, no reverb / city canyon echoes, no ground-impedance effect, occlusion is a blocker count supplied by the adapter, no HRTF (pan + ITD only), no sample layers.
