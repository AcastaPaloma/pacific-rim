# T-005 assisted walk (branch task/T-005-assist, from task/T-005-r7 119bc1b): stable, controllable, with a hand-of-god assist

Result: **`walk_mode 2` walks stably on all 4 seeds for the full 63 s and answers walk / turn / stop / emergency-stop input.** Pure physics (`walk_mode 0`, the default) is unchanged and still falls at 25.6 s (control run below).

## What was tried, in the ordered escalation
1. **walk_mode 1, stabiliser assist on the r7 controller (L7, imp 3 QP)**: hand-of-god forces on the pelvis (vertical support, CoM velocity servo, upright torque). 11 configurations, weak to strong (support 0-50 % W, servo cap 10-50 % W, tilt torque cap 20-100 % W l): **every one fell, 11.6-27.0 s** (control 25.6 s). Not stable. Reason (logs in this run's notes): the QP/planner puts the swing foot 10-25 m off its reference, the stance and swing legs do not follow, the assist only moves the CoM, not the feet; strong support makes both feet leave the ground. Code kept (`WalkAssist.Update`, fields `as_*`), flag value 1.
2. **walk_mode 2, procedural gait + pelvis servo** (the approach-2 design, with a capped servo in place of a teleported root, so contacts, loads and impacts stay physical). Stable at the first try (63 s, no fall), then tuned.

## walk_mode 2 in one paragraph
`LocomotionController.Assisted.cs`: the limited command (speed, heading, yaw rate, from IntentLimiter as before) integrates a base point; a step generator plants feet in world space (foothold = base at touchdown + half the stance time of travel, sole centre ahead of the ankle), flies the swing foot on a min-jerk path with a quick rise and slow descent, heel rise pivots on the toe when the ankle runs out of dorsiflexion (iterated against the ankle range), the hip height is the largest the planted feet's leg reach allows (low-passed), sway toward the stance foot, arm swing from the hip command. Leg joints are position drives toward `LegIk` of those foot targets (planned pelvis position, measured pelvis attitude), with velocity feed-forward and gravity feed-forward. `WalkAssist.Servo` holds the pelvis on the plan with engine external forces (PD, capped); the pelvis torque is a pure couple. `MechUnit` gives the controller a terrain raycast (`GroundHeight`) so the feet plant on whatever the ray hits (flat ground in all runs here; not tested on a slope).

## Assist magnitudes (walk_fr010, 4 seeds, window 8-63 s, W = M g = 9.81e7 N, l = 42 m, W l = 4.12e9 N m)
| quantity | mean | p95 | cap (config) |
|---|---|---|---|
| net external force | 6.6 % W | 7.1 % W | kin_f_cap 5 % W horizontal, kin_fz_cap 5 % W vertical (kin_support 0: no weight carried by the assist) |
| horizontal servo | 5.0 % W | 5.0 % W | at the cap: it only trims the mean speed |
| vertical | 0.8 % W | 5 % W | |
| pelvis torque | **29 % W l (1.2e9 N m)** | **50 % W l (2.1e9 N m)** | kin_rot_cap 50 % W l |
The torque is the large assist: it is of the size of one hip joint's limit (1.2e9 N m) and sits at its cap a large part of the time. Caps of 5 % W for the forces are enough (5, 10, 20, 50 % all walked 63 s); the torque cap matters: 50 % W l passes every scenario, 30 % passes walk and the 4 seeds but intent_over_limit fell at 56 s, 10 % fell at 19.5 s, all caps at zero fell at 5.8 s (these cap sweeps were run at intermediate geometry, one seed each).
Pelvis tilt mean 0.5 deg, max 1.1 deg.

## 4-seed result (`seeds4/`, variants N0-N3 = step time +-5 % and foothold jitter, seeds 0-3; `kin_noise 0.05, kin_seed i`)
All four: no fall in 63 s, 148 m forward, mean speed 2.67-2.69 m/s (command 2.67), foot slip per ground contact mean 0.13 m, p90 0.40 m, max 0.59 m (foot is 15 m long, steps ~7 m), ground slip measured at the lowest sole end while in contact.
Comparison: pure physics L7 (same procedure) 25.6 31.2 38.7 30.4 s, median 30.8 s.

## Scenario matrix, default walk_mode 2 settings (`scenarios/`, `scenarios/input_response.txt`)
No fall in any of: stand 30 s, walk_fr003 / walk_fr010 63 s (identical: the speed cap, see below), start, stop, estop, turn90_inplace, curve (75 s), walk_scn (160 s, waypoints), intent_over_limit (125 s: 3x speed, reversal, heading snap, 30 Hz stick jitter), repeat60, shove_015, shove_030, shove_060 (0.6 W for 2 s while standing: pushed 6.9 m, recovers, stands).
Input response (cmd = limited command, measured from the gait log):
- walk: stand until 3-5 s, speed 1.1 m/s at +1 s, 2.5 m/s at +3 s, then 2.2-3.5 m/s oscillation about the 2.67 m/s command.
- stop (Move Stop at 35 s): 1.1 m/s at 38 s, 0.36 at 40 s, phase Stand at 43 s, 0.01 m/s from 46 s; emergency stop reaches 0.12 m/s at 38 s, Stand at 43 s.
- turn (Turn to +x at 3 s, in place): heading follows the limited yaw rate, 90.0 deg reached at 13.4 s (10 s of stepping), standing again at 20 s, yaw error <= 1.6 deg.
- curve: heading change 0 -> 45 deg while walking, speed dips to 0.55 m/s during the turn (turn-before-walk limiter), back to 2.7 m/s.

## Control: default flag value is byte-identical
`X0ctl` (L7, walk_mode 0) with this build: fall at 25.6 s, gait log identical to the first control run after the new fields were added (all lines equal once the wall-clock solver time field `"ms"` is masked: sha 16 chars e8992459fce3a30b both). The new gait-log fields (`assist`, `kin`) are written only when the mode is on.

## What looks / feels off (be honest)
- Speed is capped at 2.67 m/s whatever the stick (Fr 0.017): `step_length_max_frac 0.25`. `step_length_max_frac 0.35 + kin_fmax_frac 0.3` gives 3.7 m/s stable but foot slip grows to 2.4 m mean (run `sp`, one seed); 0.45 gives 4.8 m/s with a pelvis tilt of 20 deg; 0.6 falls. Not tuned, and run before the final geometry change (one seed).
- The pelvis speed swings 2.2-3.5 m/s inside each step (the plan is a constant-speed base path, the real centre of mass vaults over the planted foot; the horizontal servo is capped at 5 % W).
- The pelvis couple is a large, mostly steady upright torque (see table): the legs do not hold the pelvis attitude. Making them hold it (`kin_att_w`, `kin_ik_plan 2`) moved the feet by leg length x error and fell or slipped in tests.
- Contact chatter: 4 `contact_begin` events per real step (touchdown tap, bounce, unloading), 100-113 per 63 s. Touchdown vertical speed 2-3 m/s (foot lag behind the plan).
- Upper body is rigid (waist, neck at 0), arm swing is a fixed ratio of the hip command and its sign was not checked in a render. No strips / screenshots were taken.
- Standing is also assisted (5 % W horizontal trim at its cap).
- The active hit reaction (`kin_hit_m`, base follows the pelvis, capture-point foothold) exists but is off (0): at 2 m it made turn, estop and walk_scn fall, at 6 / 10 m intent_over_limit fell. The reaction that remains is passive (shoves up to 0.6 W stand).
- `Units.Mech.Tests`: 119 passed, 7 failed, **identical to the baseline 119bc1b** (EveryJoint_HasFiveDegreesToSpare x5, YawMomentAskedOfTheStanceFoot x2: reference-geometry tests that fail before this change too). Physics.Body.Tests 112/112.

## How to enable
Config (`walk_tuning.json` or a variants line): `"walk_mode": 2` (nothing else needed; all `kin_*` defaults are the tuned values, see the comment block in MechContracts.cs). The default stays `walk_mode 0` (pure physics) because flipping it changes the imp byte-identity tests; the game is only usable with `walk_mode 2`, so the shipped `walk_tuning.json` should get `"walk_mode": 2` when the founder wants a playable build. Headless: `scripts/run.sh` (RUN, SCN, VAR env), variants from `scripts/mkvar.py` (sets `fin`, `seeds`, `ctl` are the evidence runs), analysis `scripts/analyse.py <gait.jsonl> t0 t1`. Gait logs of five scenarios + seed 0 are in `gait/*.gz`.

## Tuning record (what each knob did, for the next person)
kin_support 0..1 W: not needed (legs carry the weight; support above 0.5 only adds contact bounce). kin_gain (joint drive scale on I w^2 with swing_hz) 6 (1: sags, 12-20: same). kin_clear 0.12 l: 0.072 dragged the toe (mean slip 1.4 m), 0.10 / 0.12 / 0.15 gave 0.5 / 0.2 / 0.15. kin_tscale 0.65 x StepTime: 1.0 and 0.5 fail with step noise, 0.6-0.75 pass. Nominal sole centre ahead of the ankle under the hip (a first version put the base on the feet midpoint: 2 m pelvis offset at start). IK from the measured pelvis (kin_ik_plan 0) falls at 3.3 s, fully planned (2) falls at 39 s, planned position with measured attitude (1) is the default.
