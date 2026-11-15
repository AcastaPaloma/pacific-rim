# T-113c research item R-NEW-a (continued): what is left of M-007 P4, P5, P6 and I3 after the evaluator rev 2 and three timing iterations

Author: T-113c worker. Inputs: research/T-113b-pack-clauses.md, tasks/T-113b.progress.md, tasks/T-113c.progress.md. Evidence: evidence/T-113c/compare.txt (hold-out seeds 3001-3030 hunt and hunt_mv@v2, 3001-3010 mixed and raid, never used for tuning; tuning seeds 1-30).
No threshold, clause definition or @v1 definition changed. The evaluator change is a versioned revision (Tools/m007_bay.py `rev`, default 2; `--rev 1` reproduces every T-113b number exactly: C1 0.818, P1 0.705, P4 +0.001, P5 0.926, P6 0.724, I3 1.001).

## 1. Done: evaluator rev 2 (Q-NEW-a = yes, decided autonomously (founder asleep))
Infrastructure entity ids are removed from the combat-target readings only (FF/C2, Engaged for C1/P1/P6, engaged-decision minutes); the idle/progress reading (I2) and the C4 leak reading still see the raw target. Justification: R-024 3.1-3.2 define FF and Engaged over the kaiju's combat targets (mechs); in hunt ticks the LL Bait and Flanker share their target 99.4 % of the time (T-113b), the C2 shortfall was a raiding Raider's infrastructure id counted as a second fire target. Hold-out, same logs, side by side:

| | rev 1 | rev 2 |
|---|---|---|
| C2 (hunt_mv@v2, LL guard) | FAIL 0.845 | PASS 0.913 |
| P1 (min member fit time) | PASS 0.705 | PASS 0.916 |
| P6 (participation) | FAIL 0.724 | FAIL 0.797 |

Unit test `test_rev2_infrastructure_targets_are_not_combat_targets`. C1, P4, P5, I3 do not move (no raid ticks in their readings).

## 2. Pack hunt timing: three design iterations, hold-out result, why the clauses still fail
Dev seeds 1-12 / 1-30, hunt arms scriptedpack, noroles, nobait (Tools/m007_bay_dev.sh; the `--pack k=v,...` option of the runner overrides any PackConfig field for sweeps).

| it | change (flags, all OFF by default) | dev C1 | P4 | P5 | P6 |
|---|---|---|---|---|---|
| base | HEAD | 0.758 | +0.118 | 0.905 | 0.745 |
| 1 | BaitFirst, gate does not wait for the Bait's 4 s draw (BaitDrawSeconds 0); strict Anchor hold | 0.903 | +0.029 | 0.918 | 0.667 |
| 2 | `AnchorLead` (the heavy Anchor commits at the gate, Striders are held back so all reach melee together, Anchor first), `BaitTimed` (the Bait steps in only when the Anchor is `BaitLeadS` from weapon range), `AnchorHoldM` 340 | 0.48 with BaitTimed (0.88 without) | +0.045 | 1.036 with BaitTimed (0.81 without) | 0.68 |
| 3 | iteration 1 + Anchor hold 340 m, 1 s draw, `AvoidOtherMechsM` 450 (best compromise, the hold-out candidate) | 0.848 | +0.035 | 0.892 | 0.632 |

Hold-out (rev 2): the candidate lifts C1 0.818 -> 0.932 and P1 0.916 -> 0.957 but P4 +0.001 -> -0.066 (FAIL), P5 0.926 -> 0.854, P6 0.797 -> 0.661. Not every clause improves, so `PackConfig.BaitFirst` stays OFF and the default output is bit-identical to HEAD.

What the evidence says (the diagnosis that carries to the next attempt):
- **P5 is an hp-fraction measure, so who takes the fire matters.** `kaiju_loss_frac` is damage over each kaiju's own max hp: the same 6 hp/s costs a Strider (200 hp) 5x what it costs a Bastion (1000 hp). Noroles lets the Bastion take 48 % of the damage (165 of 347 hp per run); the pack concentrates it on Strider Bait and Flankers (Bait + Flanker 228 of 310). Making the Anchor lead and holding the Bait back until the Anchor is 9 s from weapon range (`AnchorLead` + `BaitTimed`) cut kaiju deaths 0.37 -> 0.21 and raised P5 0.905 -> 1.036 (dev, 12 seeds), the only change that moved it, but it collapses C1 (the first Charge then sees only same-side holders engaged: unflanked) and P4. C1 needs the Bait engaged (inside 300 m) at the first Charge, P5 wants the Bait out of range until the Anchor is in front of it: these two readings pull in opposite directions at the first Charge.
- **P6 is structural in the engagement radius.** D_eng = 5 H is 300 m for a Strider (= the mech weapon range) and 800 m for a Bastion, so an Anchor starts the engagement clock about 40 s of walking before a Strider is allowed to be inside its own radius without being shot. Failing pairs are Striders and Baits (L share 0.445, Bait 0.30, Anchor 0.95, dev). Holding Striders outside their radius (the safe thing) makes it worse (0.745 -> 0.63-0.68). Not fixable without either shooting while engaged or a different reading: **proposal (lead decision)**: P6's interval should start at the first member within its own weapon/melee reach or at the first damage exchanged, not at the first member inside 5 H. That is a definition change and was not applied.
- **P4 role-agnostic part**: every >= 3 s aim episode is an opportunity; bait-first removes the high-yield "holder shot while waiting inside range" episodes the base layout produces by accident, so the any-rate falls relative to nobait. Strict rate stays 0.43-0.6.
- **Hunt dynamics defects found (fixed behind flags)**: the base hold slot (280 m) is inside the 300 m weapon range, so a Flanker waiting for the Anchor is shot alone; the Bait's stage ring (315 m) is reached by a patrolling mech that drifts in (BaitTimed keeps it at >= 1.08 x range); with the default loose Anchor tolerance (2 x 0.4 x 560 m) the Bait stepped in while the Anchor was still ~900 m away, exposed for 60-80 s (`StrictAnchorTol` + `AnchorHoldM`).

Next attempt (research): a tank-led strike with a **C1-compatible first Charge** (the Bait enters the band exactly when the Anchor commits, so the first Charge sees Bait + Anchor flanked and the Bait's exposure is the Anchor's closing time only, about 10 s), needs the Anchor's closing time to be short (hold 340 m) and the Bait to leave when the Anchor is nearer than it; alternatively a role-specific damage soak (a Breacher/Bastion as a second Bait) once P1's fit table allows it. Flags `AnchorLead`, `BaitTimed`, `BaitLeadS`, `AnchorHoldM`, `AnchorLeadLagS` are in place for it.

## 3. I3: scenario variant mx2 (new variant, the original mixed is unchanged)
`--variant mx2` on the mixed scenario spawns the pack on the Peninsula at (62000, 62000) BayTM. Every brain takes Potrero first; the second target is where nearest-first (guarded Port cranes, 2 mechs) and utility (the undefended Golden Gate bridge, weight 3) differ, and the 3600 s horizon is long enough to reach it. Mixed alone on dev seeds 1-6: ratio 1.12 (original: 1.00). Hold-out 3001-3010, pooled with the unchanged raid scenario (rev 2): original **1.001** (CI lower 0.98), mx2 **1.032** (CI lower 1.006); still below 1.20 because the raid scenario dominates the pooled ratio and it is throughput bound (T-113b section 4). I3 therefore stays FAIL; the variant removes the non-discriminating mixed dilution (the ratio lower bound now exceeds 1.0). The raid throughput item stays open: only Raider slots damage (1-2 of n members), so the +20 % has to come from escorts demolishing when no defender is known (raises both arms) or from a longer horizon.
- Proposed breakdown entries: R-NEW-a continued (this item); Q-NEW-c: P6 interval start (reading change, lead decision); Q-NEW-d: raid escorts demolish when no defender is known (throughput).
