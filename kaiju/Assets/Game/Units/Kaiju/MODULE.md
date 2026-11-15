# Units.Kaiju
Purpose: F-023, F-024, D-026, D-009 (original IP), M-103. `KaijuSpeciesSpec@v1`: engine-free, sourced species data (STRIDER light 60 m, BREACHER mid 100 m, BASTION heavy 160 m) and the M-103 physical-consistency validator. Research `research/R-102.md` sections 3-10. Task T-112. Layer L2.

## Assemblies
| Assembly | Folder | Contents |
|---|---|---|
| `Game.Units.Kaiju.Contracts` | `Contracts/` | spec records (`KaijuSpeciesSpec`, `SpecNumber` with unit/source/status), `KaijuTissue`, `IKaijuSpeciesCatalog`, `KaijuFlags`, `KaijuContractVersions` |
| `Game.Units.Kaiju` | `Runtime/` | `KaijuSpeciesLoader` (JSON, via `Game.Core.Json`), `KaijuSpeciesCatalog`, `KaijuModule.Install`, `SpeciesPhysics`, `SpeciesValidator` (M-103) |

## Data (`species/`)
`strider.json`, `breacher.json`, `bastion.json`, `kaiju_tissue_v1.json` (shared Assumed tissue: bone 600 MPa, muscle 6.0 MPa, with the real values 200 MPa / 300 kPa). Every number is `{value, unit, source, status}` in SI base units; status is `Sourced | Derived | Assumed`. Derived numbers are stored and recomputed by the validator, so an edited input that is not re-derived fails M-103. No shape data (art owns shapes).

## M-103
Python `Tools/kaiju_spec_check.py` (pytest `Tools/tests/test_kaiju_spec_check.py`) and mirror `SpeciesValidator` (`DotnetTests/Units.Kaiju.Tests`). Thresholds are constants in both, never read from a spec. M-103.6's measured half (`gait_log@v1` through the M-001 tool) waits on T-117 bodies; the spec-side half is gated now.

## Deviations from the R-102 sketch (recorded)
- Flat numeric blocks (`fr_band_min/max`, `k_a/k_b`, `role_affinity_*`) instead of nested pairs; `damage` block (R-102 section 9 phi values) added; `swim_depth_min_m` replaced by `belly_clearance_m` + `swim_speed_max_ms`; attack record gains `target_mass_kg`, `restitution`, `hold_force_N`, `muscle_area_m2` so Rush and Grapple are recomputable.
- Recomputed attack figures differ slightly from the R-102 prose where the prose was inconsistent with its own inputs: BASTION tail sweep 641 MJ (prose 661), BASTION head ram 480 MJ (476), BREACHER swipe KE 165 MJ (160); grapple hold force kept at R-102's 196 MN (5.1 MPa x 108 m2 would be 551 MN).
- `com_height_m` (not in R-102) is back-derived from its lateral limits; yaw inertia is a trunk-ellipsoid approximation; elephant recipe primitives other than trunk and limbs, and `elephant_extra_volume_m3`, are T-112 choices that reproduce 5.23 m3. All Assumed.

## Feature flag
`units.kaiju.species` (`Config/FeatureFlags/Units.Kaiju.json`, default off). `KaijuModule.Install` registers `IKaijuSpeciesCatalog` only when on. Not wired into `Game.Composition`.

## Not here
Physics bodies and locomotion (T-117, staged on T-005), brains and packs (T-113, T-116), silhouette metric M-NEW-1, soil sinkage (Q-NEW-5).
