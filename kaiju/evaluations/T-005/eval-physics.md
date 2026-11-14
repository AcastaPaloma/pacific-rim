FAIL

# T-005 eval-physics, cycle 1 (build 17d2e9a; evidence produced at 955a865)

Scope: physics honesty and plausibility (P2), `GAME_BREAKDOWN` §4, `M-006@v1` (`research/R-005.md` §8), criteria C9–C12, C15–C18, C20, C24, R1–R3 of `expectations/T-005.md`. Every "mine" value was recomputed from the stored logs in `evidence/T-005/physics/` and `gait/` with my own scripts (`/private/tmp/claude-501/eval-T-005-physics/m006_mine.py`, `m006_b.py`, `m006_c.py`), not from `metrics/M-006.json`. Unity was not run. Verdict: **FAIL**. C9 (M-006: .4, .5, .6, .7 fail), C18 and C20 fail. The physics that exists is honest: no assistance, the mass unit is applied consistently, and the passive truth scenes pass.

## Scores
| Criterion / sub-measure | Stored | Mine | Threshold | Verdict |
|---|---|---|---|---|
| .1 link masses, damping, g, dt | 1.0000e7 kg; 0; (0,-9.81,0); 1/60 | same, from the `unit_sanity` header read-back | 0.1 %; 0; exact | PASS |
| .1 densities | 1,627 / 877 kg/m3 | 1,627 / 877 | [1000,2500] / [500,2500], warn < 1,025 | PASS (with the warning) |
| .2 drop 20 m: loss; worst gain | 0.82 %; 0 | 0.82 %; no gain at any step; v after 121 steps = 19.7835 m/s = g t exactly | ≤ 1.33 %; ≤ 0.1 % | PASS |
| .2 knee pendulum, 10 periods | 0.38 % | drift -5.4e4 J (span 1.4e5 J), consistent | ≤ 3 % (A) | PASS |
| .3 three impacts: dp, dL, KE ratio | ≤ 2.9e-6, ≤ 1.2e-5, 0.9091 / 0.912 / 0.934 | mech-side v after head-on e=0 = 4.54545 m/s = 5·m1/(m1+m2) | 0.5 %, 2 %, 0.909 ± 3 % | PASS |
| .4 double stance Σ Fn / m g; p L/R | 1.0001; 325.7 / 328.4 kPa | 1.0001; 325.6 / 328.4 | 1 ± 1 %; 327 ± 5 % | PASS |
| .4 single stance (16 s hold) | 653.7 kPa | 653.0 kPa | 654 ± 2 % | PASS |
| .4 stride impulse / (m g T) | error 0.07 % | 0.9996 over t > 15 s | ± 2 % | PASS |
| .4 walking peak force per contact | 4.49 BW | **4.49 BW**; median 3.26; 15 of 21 contacts > 2 BW; worst 3-step mean 2.55 BW | 1.0–2.0 BW | **FAIL** |
| .5 locked topple: KE/ΔPE; t_fall; released | 0.985; 8.03 s; 3.59 GJ | 0.985; 8.05 s; 3.59 GJ (CoM 43.47 → 6.87 m) | 1 ± 3 %; 8.2 ± 10 %; 3.6 ± 10 % | PASS |
| .5 locked ω at impact | 0.575 rad/s | not in the log (CoM speed 24.2 m/s against the 24.9 reference, consistent) | 0.57 ± 5 % | PASS (stored) |
| .5 limp: (KE + drive dissipation)/ΔPE | 0.913 | 0.913 (KE 0.895, drive 0.018) | 1 ± 5 % (A) | **FAIL** (DEF-8) |
| .6 worst \|τ\|/limit; ankle peak | 1.000; 7.5e8 | 1.000; 7.8e8 N·m over the whole capture | ≤ 1; ≤ 1e9 | PASS |
| .6 most saturated joint | 5.8 % (waist flexion, t > 20 s) | 5.8 % (t > 20 s); 5.1 % whole capture | ≤ 5 % | **FAIL** |
| .6 mean Σ P+ | 132 MW | 132 MW at 2.9 m/s: mechanical CoT **0.46**; 46 % of joint work is negative | 30–120 MW | **FAIL** (DEF-3) |
| .6 peak Σ P+ | 2,485 MW (t > 20 s only) | 2,485 (t > 20 s); **2,992 MW over the whole capture** (t = 5.55 s, first swing); 161 steps above 450 MW; peak 0.5 s mean 822 MW | ≤ 450 MW | **FAIL** (DEF-2) |
| .6 standing mean Σ\|P\| | 0.87 MW | 0.87 MW | ≤ 5 MW (A) | PASS |
| .7 NaN; link speed before falls; ΔE jump | 0; 26.8 m/s; 5.1e7 J | 0 in 17 physics logs; 26.7 m/s; 5.11e7 J (curve, 48.6 s) | 0; ≤ 60; ≤ 7.8e7 | PASS |
| .7 leg joint speed before falls | 3.03 rad/s | 3.028 rad/s (intent_over_limit, ankle_L, 61.1 s; fall_start 62.28 s), at the 3 rad/s engine cap | ≤ 3 | **FAIL** (DEF-7) |
| .7 clamp events | 0 | cannot be judged: the `clamps` field is never written | 0 outside falls | not verifiable (DEF-7) |
| .8 3 × 60 s: CoM divergence; contact steps | 0 m; identical | 0.0 m; 16 contact_begin events, identical steps and order | ≤ 1 cm; identical | PASS |
| C9 M-006 all in one commit | FAIL | FAIL (.4, .5, .6, .7) | all pass | **FAIL** |
| C10 no hidden cheats | met | met: code read, ext_f = 0 in all 18 non-shove gait logs, external_work = 0 in all non-shove physics logs | none | PASS |
| C11 assistance rule | met | met: no assist exists, so the gate result is the unassisted one | gate judged with assistance off | PASS |
| C12 config-built body (physics part) | met | masses, inertias and drives built from config / 1e5; gains via `GainRules` on config inertia | | PASS |
| C15 shove | met | 4.905 / 14.72 / 29.43 / 58.86 MN = 0.05/0.15/0.30/0.60 m g, 10.02–12.02 s; 0 / 0 / 4 / 7 contacts after the push; 0.6 falls | as spec | PASS |
| C16 fall | met | fall phase (ΔKE + drive dissipation)/ΔPE = 0.982; released 3.67 GJ; at rest (link speed ≤ 0.034 m/s for the last 2 s); ROM overshoot ≤ 0.014° | physics only, M-006.5 energy | PASS (see note) |
| C17 footfall event | met (391 / 391) | timing: 262 / 262 contacts in 17 physics logs, same step, 0 missed, 0 duplicates; **payload wrong** | one event, same step, physical payload | **FAIL** (DEF-5, 6) |
| C18 plausible footfalls | not met | 4.49 BW; touchdown at -1.50 m/s median (18 of 21 faster than 1 m/s) | 1–2 BW | **FAIL** (DEF-1) |
| C20 N-mech independence | 2.0 / 3.8 cm | 0.0205 / 0.0383 m in `tests/unity_playmode.xml` | ≤ 1 cm | **FAIL** (DEF-9) |
| C24 repeatability | met | as .8 | | PASS |
| R1 corners / R2 / R3 | load and walk / not built / not run | not judged again; R3 (1e7–1e8 N·s, momentum within 0.5 %) has no artifact | reported | reported, not gating |

C16 note: the 0.6 m·g fall is not slow (fall_start to first body contact 1.48 s, CoM at 16 m/s at fall_start). That is consistent physics: 1.18e8 N·s of push gives up to 11.8 m/s, and 8.7 m/s is measured at 12 s. The "slow topple" times are shown by the passive topple (8.05 s from 5°), which matches §7.

## No assistance: true
I read `ArticulatedBody.cs`, `BodyWorld.cs`, `TruthScenes.cs`, `LocomotionController.cs` (drive paths), `Scenarios.cs` and `PhysicsProjectSettings.cs`. Outside the truth scenes there is no write to root pose, velocity, `jointPosition` or `jointVelocity`. No body is kinematic, immovable or teleported. No force or torque is added except joint drives, joint feed-forward (clamped so that drive plus feed-forward ≤ the config `forceLimit`, `ArticulatedBody.cs:267-276`) and the declared shove. Linear and angular damping and joint friction are 0 and read back as 0. Gravity is set once to -9.81, and time scale is untouched. Friction is 0.8 / 0.6 with Minimum combine, restitution 0 (R-005 R15, R9). Drive damping is declared (stance knee 0.03, hip and ankle 0 of critical; falling 0.3). Truth scenes set initial conditions once: impact velocity, pendulum angle, tilt, and an immovable pelvis only in the pendulum, which the spec allows. One caveat: the PhysX `maxJointVelocity` guard of 3 rad/s on legs is reached (3.028 rad/s), and the log cannot show how often it limits motion (DEF-7).

## Mass unit (1e5 kg): consistent, physics unchanged
- **Conversion is complete.** Every mass-bearing engine input is divided by 1e5: link mass, inertia, k, d, forceLimit, jointForce, the external force, and the impact box's mass. Contact impulses and drive forces are multiplied back. The remaining engine settings carry no mass: the sleep threshold is mass-normalised (0), bounce and depenetration are speeds, the contact offset is a length, and friction and restitution are dimensionless. No break forces, contact modification or `SetMaxImpulse` are used. Gravity is an acceleration and unchanged.
- **Logged values come out in true SI** for 10,000 t:
  - standing Σ Fn = 1.0001 m g, from engine impulses × unit;
  - per-step Newton residual (ΣJn − m g Δt − Δp_y) is 0.03 % rms of m g Δt standing and 1.1 % rms walking (worst single step 15 %);
  - stride impulse is 0.9996 m g T;
  - PE = m g h_com to 2e-8; p = M·v_com;
  - pressures are 327 / 654 kPa;
  - the drop reaches v = g t exactly;
  - the head-on e = 0 impact leaves the mech at 4.54545 m/s, the exact SI mass ratio;
  - the topple releases 3.59 GJ.
- **Project-wide consequence.** The unit lives only in `body_tuning.json` (Physics.Body). Every other PhysX body that can touch a mech (debris, buildings, kaiju, water and thruster forces) must use the same unit, or contact mass ratios are wrong by 1e5. Nothing in Core or the contracts enforces this (DEF-10).
- **The probe claim is only partly right.** `probes/physx_probe_raw.txt`, P4m, single-link articulation at commit 7431e5c: under PGS, 1e6 and 1e7 get no impulse and fall through. Under **TGS, the solver in use, 1e6 rests** (Fn/mg 0.999), and only 1e7 fails: it sinks 3.3 m with 24 iterations and falls through with 6. A Rigidbody of 1e7 rests (P4v2). "1e6 kg and above get no contact impulse" is therefore true for PGS only (DEF-11). The 15-link mech itself standing at Σ Fn = m g is the decisive evidence that the scaled unit works.

## Cause of each M-006 failure
- **.4 (touchdown).** Controller fault. The foot lands heel-first by about 2.3°, edge-loads for about 11 steps (0.08 → 0.42 BW), then slaps flat in one step (4.49 BW, then 1.4–1.7 BW). It arrives at -1.5 m/s against the 0.61 m/s planned by `walk_tuning.touchdown_speed_tstar` and the R-005 bound of ≤ 1 m/s, while the CoM is falling at 0.5 m/s. The slow gait does not cause this. The per-step peak on rigid ground is filter-sensitive, but even a 3-step mean fails (2.55 BW).
- **.6 peak.** Controller fault at swing start, not at touchdown. Of the 161 steps above 450 MW, 130 fall within 1 s after a lift-off and 0 within 1 s after a footfall. The swing knee (1.66–1.96 rad/s at 8e8 N·m) and hip (0.85–1.07 rad/s at 1.2e9 N·m) are both saturated. R-005 §3.3 gives 0.6–0.9 rad/s peak joint speed, and `Track()` allows a 2.5 rad/s velocity feed-forward (`LocomotionController.cs`, near line 546).
- **.6 mean.** The threshold was derived for Fr 0.25 (10 m/s, CoT 0.05–0.1). At 2.9 m/s the same CoT gives 14–28 MW, so the band is lenient for this gait, and the failure shows a wasteful controller (CoT 0.46, joints fighting each other), not the slow gait.
- **.6 saturation.** Controller fault: the trunk attitude loop pins the waist at 5e8 N·m.
- **.5 limp.** Metric gap plus logging gap. The feet stay on the ground for 400 of 404 fall steps, and foot friction and joint-limit stops do work that is not logged. Energy never rises: the largest gain is 4.6e5 J per step.
- **.7 joint speed.** Loss of balance about 1.2 s before the controller's `fall_start`. Fall detection is late, and the engine cap is touched.

## Defects
- **DEF-T005-physics-1 (blocker, implementer).** Touchdown force. `physics/walk_fr020`: peak 4.49 BW (R, t = 38.6 s); 15 of 21 contacts above 2 BW; v_approach median -1.50 m/s, worst -1.78 m/s. PASS: every walking contact 1.0–2.0 BW, approach ≤ 1 m/s, no one-step flat-foot slap.
- **DEF-T005-physics-2 (blocker, implementer).** Swing-start power transients. Peak Σ P+ is 2,992 MW over the whole capture; `metrics/M-006.json` reports only t > 20 s (2,485 MW) and so omits the first step. PASS: ≤ 450 MW at every step of every walking capture, start included, and leg joint speeds near R-005's 0.6–0.9 rad/s.
- **DEF-T005-physics-3 (major, implementer; research to restate the threshold).** Mean Σ P+ is 132 MW at 2.9 m/s (CoT 0.46), with 46 % negative work. PASS: CoT within R-005's 0.05–0.1 at the speed reached. Proposed M-NEW-a: state M-006.6 mean as a CoT band so that it scales with speed.
- **DEF-T005-physics-4 (major, implementer).** Waist flexion saturated in 5.8 % of steps (t > 20 s). PASS: ≤ 5 % for every joint.
- **DEF-T005-physics-5 (major, implementer; the orchestrator clarifies C17).** The `FootContact` / footfall payload `peak_Fn` and `VerticalImpulseNs` are the values at the F_on crossing (`ArticulatedBody.cs:335-336`). Across 256 loaded events they are median 0.45 of the contact's true peak force and 2.3 % of its first-second impulse, and the real impact arrives about 11 steps later. The camera probe is driven by this small value. PASS: the event, or a follow-up event with the same ID, carries the contact's actual peak force and touchdown impulse (∫(Fn − share of m g) over the impact), matching the log.
- **DEF-T005-physics-6 (minor, implementer).** Spurious footfall events with Jn = 0 at step 1 in the three airborne impact scenes (6 events), because the contact hysteresis starts "in contact". PASS: no footfall without F ≥ F_on.
- **DEF-T005-physics-7 (major, implementer).** `PhysicsLog.Clamps` is never incremented (no writer in `Assets/Game`), so R-005 R11 ("log every clamp") and the M-006.7 clamp check cannot be verified. Leg joints reach the 3 rad/s engine cap (3.028 rad/s, `intent_over_limit` 61.1 s; 2.93 rad/s in `curve` and `shove_060`) before `fall_start`. PASS: clamps counted from engine read-back, zero outside falls, leg speed < 3 rad/s before the body starts to topple (fall detection made earlier, or the metric's "outside falls" boundary defined by research).
- **DEF-T005-physics-8 (major, research plus implementer).** Limp-fall energy account 0.913 (`physics/topple_limp`). Foot friction and joint-limit work are not logged, and the M-006.5 limp term counts only drive dissipation. PASS: an amended M-006.5 that includes contact and limit work, those terms logged, and the account closed within 5 %.
- **DEF-T005-physics-9 (major, implementer diagnostic; research if the cause is the engine).** C20: mech A moves 2.0 / 3.8 cm when 1 / 3 other mechs stand 300 m away without touching it. Repeat runs are bit-identical, so the code is deterministic, and the difference comes from scene content. PhysX enhanced determinism promises invariance to added non-interacting actors, so either that guarantee does not hold for articulations against a shared static ground (contact or event ordering), or there is shared state (bus or clock order, contact routing). The artifacts do not say which. PASS: the first divergent step located by diffing A-alone against A+B logs, and the cause fixed or documented, with ≤ 1 cm.
- **DEF-T005-physics-10 (major, founder or orchestrator decision plus research).** The project-wide mass unit is undeclared outside Physics.Body, and SI-kg Rigidbodies work at 1e7 kg, so a later module can silently mix units. PASS: one Core-level constant or contract for the PhysX mass unit, used by every module that creates bodies or applies forces, recorded as a D-entry, with a mixed-body contact test (mech against a 1e5–1e6 kg debris body: momentum within 0.5 %).
- **DEF-T005-physics-11 (minor, implementer).** `EVIDENCE.md` §3(a) and `Physics/Body/MODULE.md` state the probe limit as "1e6 kg and more" for both solvers. Under TGS the probe shows the limit lies between 1e6 and 1e7, measured on a single link at 7431e5c. PASS: the claim restated per solver, and the probe re-run at the evidence commit, including a 15-link articulation at 1e7 kg in SI units.

Implementer-fixable: 1, 2, 3 (controller part), 4, 5, 6, 7, 9 (diagnosis), 11. Research or founder: 3 (threshold form), 8 (metric definition), 9 (if engine-level), 10 (decision).
