# T-005 evidence: one mech body that walks with physics and intent control

Code commit that produced every capture, log, strip and test result here: **955a865** (branch `task/T-005`). Config: 80 m, 10,000 t (`mech_frame_balanced.json`), rigid flat ground, dt 1/60 s, TGS solver, enhanced determinism on, **no assistance of any kind** (see section 3). T-002 metric tools used read-only from `.wt/T-002/kaiju` at commit **0f57e25** (that worktree had 2 uncommitted files at the time): tool 1.0.0, registry v1.0. M-006 has no T-002 tool: it is computed by `Tools/T005/m006.py` from R-005 section 8, thresholds copied unchanged.

**Headline, stated plainly.** The mech stands, shifts weight, starts, walks, stops, turns 90 degrees on the spot, stomps, holds single stance, takes capture steps and falls under physics, all from `IntentFrame` only and with zero external force. **The walk is stable only at 2.86 m/s (Froude 0.020) with steps of about 10 m. The M0 gate is not met: M-001 FAIL, M-002 FAIL, M-006 FAIL.** Details and causes below; nothing was faked or assisted to hide it.

Logs are gzip-compressed (`*.jsonl.gz`, 43 MB instead of 225 MB). Unpack before using the T-002 tools: `gunzip -k evidence/T-005/gait/*.gz evidence/T-005/physics/*.gz`. `Tools/T005/*.py` read either form.

## 1. Artifacts, commands, commit
All commands run from the project root (`kaiju/`); `unity` is the Unity CLI (adds `-batchmode -quit`).

| Artifact | Command | Commit |
|---|---|---|
| `gait/<capture>.gait.jsonl.gz` (19 captures), `physics/<capture>.physics.jsonl.gz` (stand, single_stance, walk_fr020, walk_fr025, shove_060, intent_over_limit, curve, stomp), `strips/<capture>.side.png`, `.quarter.png`, `.times.txt`, `summary_scenarios.txt` | `unity run . --no-tail --log-file <log> --timeout 1500 -- -executeMethod Game.Units.Mech.Editor.ScenarioCli.Run -scenarios all -out evidence/T-005 -strips -physlist stand,single_stance,walk_fr020,walk_fr025,shove_060,intent_over_limit,curve,stomp` | 955a865 |
| `gait/repeat60_run1..3.gait.jsonl.gz`, `summary_repeat60.txt` | same entry point, `-scenarios repeat60 -repeat 3 -nophys -out <tmp>`, files copied in | 955a865 |
| `truth_scenes.json`, `physics/{unit_sanity,drop,drop_x2km,pendulum,impact_*,topple_locked,topple_limp}.physics.jsonl.gz` | `unity run . ... -- -executeMethod Game.Physics.Body.Editor.TruthScenesCli.Run -out evidence/T-005` | 955a865 |
| `corners/h<H>_m<M>/summary.txt` (R1) | scenario entry point with `-scenarios stand,walk_fr020,stop -nophys -height <H> -mass <M>` (logs not kept: 49 MB) | 955a865 |
| `metrics/M-006.json`, `M-006.txt`, `footfall_latency.json`, `reported.json` | `/opt/miniconda3/envs/kaiju/bin/python Tools/T005/m006.py evidence/T-005 --commit 955a865` | 955a865 |
| `metrics/validate_gait_log.txt` | from `.wt/T-002/kaiju`: `python Tools/Metrics/metrics.py validate gait_log <evidence>/gait` | T-002 0f57e25 |
| `metrics/M-001/` (report.json, summary, images), `M-001.stdout.txt` | `python Tools/Metrics/metrics.py m001 --out <evidence>/metrics/M-001 --label T-005 <gait>/{walk_fr010,walk_fr020,walk_fr030,start,stop,estop,turn90_inplace,curve}.gait.jsonl` | T-002 0f57e25 |
| `metrics/M-001_without_curve/` | same without `curve` (information only: shows the values when the capture that ends in a fall is left out; tool exit 1) | T-002 0f57e25 |
| `metrics/M-002/`, `M-002.stdout.txt` | `python Tools/Metrics/metrics.py m002 --out <evidence>/metrics/M-002 --label T-005 <gait>/{walk_fr010,walk_fr020,walk_fr030,start,stop,estop,turn90_inplace,curve,stomp}.gait.jsonl` | T-002 0f57e25 |
| `metrics/M-002_without_curve/` | m002 on walk_fr010/020/030, start, stop, estop, turn90_inplace, stomp, walk_scn, shove_030 (information only) | T-002 0f57e25 |
| `plots/<capture>.com_vs_feet.png` | `python Tools/T005/plots.py evidence/T-005 walk_fr020 start stop turn90_inplace shove_030` | 955a865 |
| `tests/unity_editmode.xml`, `tests/unity_playmode.xml` | `unity test . --mode EditMode -o ...`, `unity test . --mode PlayMode -o ...` | 955a865 |
| `tests/*.trx` | `dotnet test DotnetTests/<Physics.Body.Tests|Control.Tests|Units.Mech.Tests>/<name>.csproj --logger trx` | 955a865 |
| `probes/physx_probe_raw.txt` | `unity run . ... -- -executeMethod Game.Physics.Body.Editor.BodyProbe.Run -probeOut <file>` (four runs concatenated: PGS, then TGS for the mass sweep) | 7431e5c |

Test totals: `dotnet test` 38 / 38 (12 + 7 + 19). Unity EditMode 132 / 132 (Core's suite plus the 38 shared tests). Unity PlayMode 18 / 20: the two failures are `NMechs_MechAIsUnchangedWithinOneCentimetre_WhetherOthersExistOrNot(2)` and `(4)`, category `KnownFailing`, which hold the C20 one-centimetre check (measured 2.0 cm and 3.8 cm after 20 s).

## 2. Metric values (every sub-measure, pass or fail)

### M-001@v1 Feels heavy: FAIL (T-002 tool exit 1; `metrics/M-001/report.json`)
| Sub-measure | Value | Threshold | Result | Note |
|---|---|---|---|---|
| M-001.1 Froude of steady walk | 0.0200 (2.86 m/s) | cruise in [0.17, 0.23] | **FAIL** | the same for the Fr 0.10, 0.20 and 0.30 commands: the step-reach limit caps all three (`limit_causes: StepReach`) |
| M-001.2 stride period / Alexander | 0.155 | [0.85, 1.15] | **FAIL** | the decisive (worst) part is `turn90_inplace`. Straight walking: stride 7.58 s against Alexander's 10.41 s at this Froude number, ratio 0.73 (also a fail: it steps too quickly for its speed) |
| M-001.3 duty factor | 0.404 | [0.60, 0.72] | **FAIL** | the decisive part is `turn90_inplace` (foot R). Straight walking: 0.594 to 0.62 per foot, double support 19 to 22 %: just under the band's lower edge |
| M-001.4 peak fore-aft CoM acceleration | 8.32 m/s2 | <= 2.943 | **FAIL** | peak is in `curve` while it falls (t = 48.7 s). Without `curve`: 2.909 (pass, margin 0.03) |
| M-001.5 peak lateral CoM acceleration | 8.82 m/s2 | <= 1.148 | **FAIL** | in `curve` during the fall. Without `curve`: 1.898 (still fail) |
| M-001.6 net start acceleration | not run | <= 0.14 g | **NOT RUN** | the tool finds no start reaching 90 % of a cruise in band |
| M-001.7 peak yaw rate | 300 deg/s | <= 26.83 | **FAIL** | body tumbling after the fall in `curve`. Without `curve`: 11.0 (pass) |
| M-001.8 peak yaw acceleration | 1047 deg/s2 | <= 4.04 | **FAIL** | fall in `curve`. Without `curve`: 80.6 (still fail: trunk yaw wobble at each step) |
| M-001.9 yaw rate x speed | 1.512 m/s2 | <= 1.148 | **FAIL** | `curve` |
| M-001.10 time to start | not run | 2 to 4 steps | **NOT RUN** | as M-001.6. Measured by `Tools/T005/m006.py`: 5.2 s from intent to 90 % of the 2.87 m/s actually reached (`metrics/reported.json`) |
| M-001.11 time to stop, normal | 2.67 t* | [2.4, 4.8] | pass | from 2.86 m/s, not from cruise |
| M-001.12 time to stop, emergency | 5.95 s | >= 3.08 | pass | |
| M-001.13 panel | pending | median >= 8 | **PENDING** | no panel was run by the implementer (independence); strips in `strips/` |

### M-002@v1 No foot sliding: FAIL (T-002 tool exit 1; `metrics/M-002/report.json`)
| Sub-measure | Value | Threshold | Result | Note |
|---|---|---|---|---|
| M-002.1 max slip / foot length | 0.827 (12.41 m) | < 0.005 (0.075 m) | **FAIL** | worst contact is in `curve` after balance is lost (foot R contact 32, t = 55.65 s). Without `curve`: 0.0913 (1.37 m: `stomp` foot R contact 3, t = 9.02 s; then `shove_030` L contact 3, 1.31 m) |
| M-002.2 p95 slip / foot length (advisory) | 0.0379 (0.57 m) | < 0.0025 | **FAIL** | planted feet creep and twist in ordinary walking too |
| M-002.3 sampling | 0.01667 s, every step, float64 | <= 0.02 s | pass | |
| M-002.4 contact flag agrees with F_on / F_off | 1.000 | >= 0.99 | pass | |
| M-002.5 excluded contacts in locomotion captures | 0 | 0 | pass | (2 excluded in `shove_030`, which has an external force by design) |

Worst five contacts with IDs: `metrics/M-002/report.json` and `M-002.stdout.txt`.

### M-006@v1 Physics is plausible: FAIL (.1, .2, .3, .8 pass; .4, .5, .6, .7 fail; `metrics/M-006.json` has every item with source and note)
| Sub-measure | Value | Threshold (R-005 section 8) | Result |
|---|---|---|---|
| .1 sum of link masses vs frame | 0 rel. error (1.0000e7 kg) | within 0.1 % | pass |
| .1 worst single link | 1.7e-8 | within 0.1 % | pass |
| .1 gravity | (0, -9.81, 0) | exact | pass |
| .1 fixed dt | 1/60 s | R-005 R2 | pass |
| .1 density over human-equivalent volume | 1,627 kg/m3 | [1000, 2500] | pass |
| .1 hull density from collider boxes | 877 kg/m3 (frame hull value 1,232) | [500, 2500], warn < 1,025 | pass, with the warning |
| .1 damping and joint friction read back | 0 | 0 | pass |
| .2 drop from 20 m: energy loss | 0.82 % | <= dt/t + 0.5 % = 1.33 % | pass |
| .2 drop: worst gain | 0 | <= 0.1 % | pass |
| .2 same at x = 2 km | 0.82 %, 0 | same | pass |
| .2 knee pendulum, 10 periods: drift | 0.38 % (period 8.49 s, small-angle 8.38 s) | <= 3 % (A) | pass |
| .3 head-on, e = 0: dp, dL, KE ratio | 1.5e-7, 3.4e-6, 0.9091 | 0.5 %, 2 %, 0.909 +/- 3 % | pass |
| .3 offset 5 m, e = 0: dp, dL, KE after/before | 2.9e-6, 5.3e-6, 0.912 | 0.5 %, 2 %, <= 1.001 | pass |
| .3 offset 5 m, e = 0.5: dp, dL, KE after/before | 7.7e-7, 1.2e-5, 0.934 | same | pass |
| .4 double stance: sum Fn / m g | 1.0001 | 1 +/- 1 % | pass |
| .4 double stance: pressure L / R | 325.7 / 328.4 kPa | 327 +/- 5 % | pass |
| .4 single stance (16.1 s hold): pressure | 653.7 kPa | 654 +/- 2 % | pass |
| .4 walking: stride impulse / (m g T) | worst error 0.07 % | +/- 2 % | pass |
| .4 walking: peak single-foot force | **4.49 body weights** | 1.0 to 2.0 | **FAIL** (raw per-step value at touchdown; rigid contact, feet arrive too fast when the trunk sags) |
| .5 locked topple from 5 degrees: KE / dPE at impact | 0.985 | 1 +/- 3 % | pass |
| .5 angular speed at impact | 0.575 rad/s | 0.57 +/- 5 % | pass |
| .5 time to fall | 8.03 s | 8.2 +/- 10 % | pass |
| .5 energy released to rest | 3.59 GJ (m g h standing 4.17 GJ) | 3.6 +/- 10 % | pass |
| .5 limp topple: (KE + drive dissipation) / dPE | **0.913** | 1 +/- 5 % (A) | **FAIL** (8.7 % of the released energy is not in kinetic energy or drive damping at first impact: joint-limit stops and the feet's ground contact during the fall are not counted) |
| .6 worst torque / forceLimit | 1.000 | <= 1 | pass |
| .6 most saturated joint, share of steps | **5.8 %** | <= 5 % | **FAIL** |
| .6 ankle peak torque | 7.5e8 N m | <= 1.0e9 | pass |
| .6 mean sum of positive power | **132 MW** | 30 to 120 MW | **FAIL** (at 2.86 m/s, not at Fr 0.25) |
| .6 peak sum of positive power | **2,485 MW** | <= 450 MW | **FAIL** (touchdown and swing-drive transients) |
| .6 standing still, mean sum of absolute power | 0.87 MW | <= 5 MW (A) | pass |
| .7 NaN records (28 physics logs) | 0 | 0 | pass |
| .7 max link speed outside falls | 26.8 m/s | <= 60 | pass |
| .7 max leg joint speed outside falls | **3.03 rad/s** (`intent_over_limit`, ankle_L, before its fall) | <= 3 | **FAIL** |
| .7 max arm / waist joint speed | 1.52 rad/s | <= 10 | pass |
| .7 largest one-step energy gain without external work | 5.1e7 J (`curve`, t = 48.6 s, before its fall) | <= 7.8e7 J | pass |
| .8 three runs of the 60 s sequence: CoM divergence at 60 s | 0 m | <= 1 cm | pass |
| .8 footfall step indices and order (30 events) | identical | identical | pass |

Walking values for .4 and .6 are from `walk_fr020` and `walk_fr025`, which are the same motion (both capped at 2.86 m/s); the Fr 0.25 stress case of the spec could not be run because that speed is not reached.

## 3. Declared assistance and cheats (C10, C11)
**None.** There is no assistance code and no assistance flag in the three modules.
- The body contract (`IArticulatedBody`) has no method that sets a pose or a velocity. The controller's only outputs are joint drive settings and joint torques, clamped with the drive to each joint's force limit.
- No body is pinned, teleported or made kinematic; no velocity is overwritten; linear damping, angular damping and joint friction are 0 and read back as 0 (M-006.1).
- `external_work` and `ext_f` are 0 in every locomotion capture (`metrics/reported.json`, key `C10_max_external_force_N_per_capture`): the only non-zero captures are the four shoves (4.9, 14.7, 29.4, 58.9 MN = 0.05, 0.15, 0.30, 0.60 m g), which are the test's real push.
- Truth scenes set initial conditions once before the first step (release height, tilt, pendulum angle, approach velocity) and are not locomotion captures.
- Declared modelling choices that an evaluator should know: (a) **mass unit**: PhysX holds masses in units of 1e5 kg (`body_tuning.sim_mass_unit_kg`) because articulation links of 1e6 kg and more get no contact impulse (PGS) or sink (TGS); all forces, torques and gains are divided by the same factor, g, lengths and times are untouched, every logged value is SI (probe: `probes/physx_probe_raw.txt`, lines `P4m`; test `MassUnit_InKilogramsTheBodySinks_InTheConfiguredUnitItRests`). (b) During a fall the drives keep 0.3 of their critical damping and zero stiffness (`walk_tuning.fall_damping_ratio`): a passive loss, logged in drive torque and power, not a recovery force. (c) Articulation self-collision is off (engine default).

## 4. Criteria map
| Criterion | Status | Artifact |
|---|---|---|
| C1 cruise gait | **not met** | M-001.1, .2, .3 above; `gait/walk_fr010/020/030` |
| C2 start | **not met** (weight shift then first step is there: `strips/start.side.png`, `plots/start.com_vs_feet.png`; M-001.6 / .10 not run because cruise is not reached) | `gait/start` |
| C3 stop | partly: M-001.11 and .12 pass from 2.86 m/s; M-001.4 passes only without `curve` | `gait/stop`, `gait/estop`, strips |
| C4 turn | partly: 90 degrees on the spot by foot placement, ends standing at 89.5 degrees (`gait/turn90_inplace`, strips); `curve` ends in a fall at 50.9 s; M-001.8 fails | |
| C5 commitment | met as far as tested: reversal and jitter never move a foot or the root except through the next footfalls (`gait/intent_over_limit` before its fall; `IntentLimiterTests`) | |
| C6 weight in the body | shown: `plots/walk_fr020.com_vs_feet.png`, `strips/walk_fr020.side.png` | |
| C7 panel | **pending** (not run) | strips; no real-time clip was made |
| C8 feet | **not met** | M-002 above |
| C9 M-006 | **not met** (.4, .5, .6, .7) | section 2 |
| C10 no hidden cheats | met | section 3 |
| C11 assistance rule | met: no assist exists; the gate result is the unassisted result | section 3 |
| C12 config-built body | met | M-006.1; `FrameTests`, `Body_IsBuiltFromConfig_MassesAndSettingsReadBack`; gains only through `GetDofGains` |
| C13 intent only, walk to a target | met at the reduced speed: `walk_scn` ends standing 7.0 m from the target (130, 0, 130), one step is about 10 m | `metrics/reported.json` `C13_walk_to_target`; scenario scripts in `Scenarios.cs` contain intents only |
| C14 over-commanded intent | partly: every clamp is logged with its cause (`reported.json` `C14_limit_causes_step_counts`: EffortRange, StepReach, Accel, Decel, YawRate, YawAccel, LateralAccel, TurnBeforeWalk); 3x speed, jitter and the unreachable target are carried out within limits; **the 180 degree reversal ends in a physical fall at 62.3 s** while turning on the spot | `gait/intent_over_limit`, `strips/intent_over_limit.*` |
| C15 shove | met with the push applied at CoM height on the pelvis: 0.05 m g and 0.15 m g absorbed without a step; 0.30 m g answered with capture steps and a return to standing; 0.60 m g ends in a fall | `gait/shove_*`, `summary_scenarios.txt`, strips |
| C16 falling | met for the fall itself (physics only, no recovery, `fall_start` / `fall_end` in `physics/shove_060`, comes to rest); fall time and energy as in M-006.5; get-up, brace and wading not built, hooks in `Units/Mech/MODULE.md` | |
| C17 footfall event | met: 391 contacts in 22 captures, 391 events in the contact step (100 %), 0 missed, 0 duplicates, 391 probe impulses in the same step | `metrics/footfall_latency.json`; tests `FootContact_IsPublishedOncePerContact_InTheContactStep`, `Walk_ComesFromIntentOnly_...` |
| C18 plausible footfalls | **not met**: peak 4.49 body weights (M-006.4); touchdown is flat-footed, not heel-first | |
| C19 control contract | met | `ControlContractTests`, `ArbiterSwap_...(2)`, `(3)`, `DevicePilot_...` |
| C20 N mechs | partly: 1, 2 and 4 mechs each with own seat, config, IDs and events pass; **mech A alone against in company differs by 2.0 cm (2 mechs) and 3.8 cm (4 mechs) after 20 s, above the 1 cm tolerance** | `tests/unity_playmode.xml` |
| C21 layers and docs | met | `ArchitectureTests` (5 tests), three `MODULE.md` |
| C22 visual attach point | met | `Physics/Body/MODULE.md` "Visual attach point"; strips carry the 100 m bar, the 1.8 m figure and an 80 m pole |
| C23 logs validate | gait logs: VALID, 22 inputs (`metrics/validate_gait_log.txt`). physics_log@v1 has no validator in T-002 yet | |
| C24 repeatability | met: 3 runs, CoM divergence 0 m at 60 s, identical footfall steps and order | M-006.8 |

## 5. Reported items
- **R1 corners** (config only, no retuning; `corners/*/summary.txt`): 76 m / 6,000 t, 76 m / 15,000 t, 88 m / 6,000 t, 88 m / 15,000 t all load, stand, walk (2.83, 2.83, 3.05, 3.05 m/s) and stop to standing without a fall. M-006.1 at the corners: test `MassAndHeightCorners_LoadByConfigOnly_AndStand`. 6,000 t hulls are lighter than sea water (R-005, Q-009): loads without complaint.
- **R2 parts removed**: not built (destroying an articulation link makes Unity rebuild the articulation; belongs with the Damage design).
- **R3 external impulse through the body contract**: not run as a separate case; `AddExternalForce` is exercised by the shoves (58.9 MN for 2 s = 1.2e8 N s on the pelvis) with logged work.
- **R4 slope**: not built (no ramp scene).
- **R5 latency** (`metrics/reported.json`): intent to first CoM motion 0.02 s (the weight shift starts in the next step), to first foot lift 2.5 s, to 90 % of steady speed 5.2 s, stop intent to below 0.3 m/s for 2 s: 5.5 s, to standing phase 3.3 s.
- **R6 footfall amplitude correlation**: not built (the camera rig is T-004's; the probe's amplitude is a fixed function of the impulse, so a correlation would be circular).
- Real-time clip for the panel: not built; frame strips only.

## 6. Notes for the evaluators
- Frame mismatch with R-001 (as the spec table says): foot 15 x 10 m, CoM 42.54 m, leg length 42.0 m, yaw inertia 6.2e8 kg m2; every threshold is recomputed by the T-002 tool from the log header (it flags `NON_REFERENCE_NORMALISERS`).
- Positions: PhysX works in float32; the origin offset is zero at M0 and nothing is farther than 300 m from the origin (float32 spacing 3e-5 m). The gait log's centre of mass and sole centres are computed in float64 from the link states; `root_p` is the engine value.
- Per-foot tangential force (`ft`) is an estimate (whole-body momentum balance shared by normal force); Unity's contact points carry the normal impulse only.
- A record with index n is the state after n physics steps; record 0 is the initial state (no contact force is known yet, so both feet show `contact_begin` at n = 1).
- `walk_fr010` declares `fr_cruise_nom` 0.10 and `walk_fr030` 0.30 in their headers (the commanded Froude number of the capture); the other captures declare 0.20.

## 7. Not met, and why (the short list)
1. **Speed and timing (M-001.1, .2, .3, .6, .10; C1, C2).** Steps longer than about 0.3 leg lengths (12.6 m) fall within 20 to 40 s, so the command is capped at the speed a 10.5 m step allows.
2. **Foot slip (M-002.1, .2; C8).** Planted feet creep and twist by decimetres per contact.
3. **Footfall force, joint power, saturation (M-006.4, .6; C18).** Touchdown peaks of 4.5 body weights and 2.5 GW power spikes.
4. **Curve walking and the 180 degree turn end in falls (C4, C14).**
5. **C20 independence at 1 cm** (2.0 and 3.8 cm), **limp-fall energy account** (M-006.5, 8.7 % unaccounted), **leg joint speed guard touched** (M-006.7, 3.03 rad/s).
6. Not built: panel run, real-time clip, R2, R4, R6, brace / wading / get-up (out of M0 scope).
