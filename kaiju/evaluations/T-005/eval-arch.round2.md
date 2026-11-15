VERDICT: FAIL

# T-005 round 2 eval-arch (head bd8c19e, diff cae49c0..bd8c19e, code files only)
Static review, Unity and dotnet not re-run. Round 2 reports "gate not met, stop rule hit"; this verdict covers architecture only.

## Checks
1. Boundaries: no asmdef changed; Units.Mech runtime still refs contracts only. Physics.Body WAS touched (BodyContracts.cs:8,132-140 three new IArticulatedBody members, version 1 to 2; ArticulatedBody.cs:254-285 engine inverse dynamics). Justified in step4.md (engine queries) and additive, but undocumented (D-1).
2. Config: all 60+ new gains are in walk_tuning.json and WalkTuning (MechContracts.cs:81-93); scripted check: every JSON key exists in the class, and the five in-class defaults (step cap 0.66, kload 0.5/1/0.5/1, waist 0.9/25) equal the JSON. Shipped `imp` = 0. Weak spots D-2, D-3.
3. Legacy (imp = 0): waist split c1ba9af is clean (imp_waist_* vs ref_waist_*, ReferenceGait.cs:122-139). Other legacy-path edits are gated or neutral (StepCapFrac, ClampToSoleImp, Tilt = 0, hipZ moved earlier). One unstated change: waist table K 240 to 480 applies to legacy (D-5).
4. Tests: 4 new engine-free files (FeedForward, StanceImpedance, YawCompensation, ReferenceValidity) cover InverseDynamicsFeedForward, StanceImpedance, YawCompensation, YawShareWindow, the reference. Not covered: ArticulatedBody ID wrapper (index mapping, sign, unit), ImpAngles, StancePosture.Solve directly (only via ReferenceValidity at Fr 0.10/0.15), ApplySchedule interpolation (D-4).
5. Log: gait_log additions (yaw_moment_nm, yaw_budget_nm, yaw_util, trunk_share, angles) are additive, null when unavailable; validator says VALID on 9 inputs (final/metrics/validate_gait_log.txt). Documented only in code comments, not in MODULE.md or a schema note (D-6).
6. YawDiagnostics runs for every unit including legacy (per-step loop over all links, GaitLogWriter); cost small, fields new for legacy logs too (additive).

## Cycle-1 defects
| cycle-1 | status | note |
|---|---|---|
| arch-1 literals in controller | CLOSED for LocomotionController/IntentLimiter | MechLogicTests.cs:257 scans decimal literals (only 0.5/1.5/2.0/1.0 allowed). Scan does not cover ReferenceGait/StancePosture (D-2) |
| arch-2 N-mech independence | OPEN | MechPlayTests.cs:144 still `[Category("KnownFailing")]`; nothing in round 2 addresses it (D-7) |
| arch-3 flags inert | OPEN | no `IsEnabled` reader in Units/Control |
| arch-4 out-of-module edits | not re-checked | round 2 edits none outside Physics.Body, Units.Mech |
| arch-5 Tools/T005 | CLOSED | now Tools/PhysicsMetrics |
| arch-6 scenarios/layer/exact-types | PARTIAL | CompositionRootAssemblies test added (UNITY_INCLUDE_TESTS); layer note and exact-types remain |
| arch-7 statics, 2-leg | OPEN (unchanged) | |
| arch-8 physics_log validator | CLOSED | validate_physics_log.py; 8 inputs VALID |

## Defects
| id | sev | defect | file:line |
|---|---|---|---|
| D-1 | major | Physics.Body contract changed (3 members, version 2) with no MODULE.md update (Physics/Body and Units/Mech MODULE.md last touched before round 2); MODULARITY 1.1 requires interfaces documented. Also any other IArticulatedBody implementer must add them | BodyContracts.cs:8,132-140; Physics/Body/MODULE.md |
| D-2 | major | The 5-row stance schedule (13 columns each: placements, hip height 41.147 m etc.) is a data table embedded in code and scaled by hard-coded `l / 42.0` instead of `reference_leg_length_m`; the literal test does not scan this file. Rule 3: tuning data in config | ReferenceGait.cs Schedule (~l.150-157), :173 |
| D-3 | minor | WalkTuning defaults are inconsistent: only 7 imp fields have in-class defaults, the rest default to 0 (imp_*_hz = 0 gives k = 0), so code and JSON differ if the JSON is absent. No test pins the defaults to the JSON | MechContracts.cs:81-93 |
| D-4 | minor | Engine wrapper for ID (index map, Sign, _unit, ReadState freshness flags) has no test; ImpAngles and ApplySchedule interpolation untested engine-free | ArticulatedBody.cs:254-285; LocomotionController.cs ImpAngles |
| D-5 | minor | Legacy waist table resolution K 240 to 480 changes the imp = 0 path, not stated in round 2 notes | ReferenceGait.cs:122 |
| D-6 | minor | gait_log optional fields documented only in code comments; MODULE.md lacks yaw_*/trunk_share/angles; `angles` in the log is a nested array object with no schema entry | GaitLogWriter.cs:93-97; Units/Mech/MODULE.md |
| D-7 | major | Cycle-1 arch-2 still open: KnownFailing PlayMode test keeps the suite red by design; flags (arch-3) inert | MechPlayTests.cs:144 |
| D-8 | minor | Dead/duplicated: `StancePosture.Solve` and `ReferenceGait.Posture` re-derive geometry that ReferenceGait.ShankAngle/Foot also compute (two-link IK duplicated 3 times: StancePosture.Evaluate, ShankAngle, LegIk); `ImpFf` computes the three terms with `> 0` guards that duplicate the clamp in `InverseDynamicsFeedForward.Torque` | StancePosture.cs:36-52; ReferenceGait.cs ShankAngle; LocomotionController.cs ImpFf |
| D-9 | minor | `imp` is a double flag compared `> 0` in 6+ places (StepCapFrac, ImpActive, ReferenceGait ctor); a named bool/property would be one point of truth. YawDiagnostics allocates nothing but runs per step in legacy | LocomotionController.cs StepCapFrac, ImpActive |

## Verdict basis
Counts: blocker 0, major 3 (D-1, D-2, D-7), minor 6. Module boundaries hold and new gains are config-driven; FAIL because a contract changed without module documentation, an embedded data table with a hard-coded reference length, and cycle-1 arch-2 is unresolved.
