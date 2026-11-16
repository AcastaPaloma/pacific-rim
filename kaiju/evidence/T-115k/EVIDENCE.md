# T-115k evidence (Anchor reaches its slot earlier, R-NEW-a-v3)
Code (default-off): `PackConfig.AnchorSkipStage` (Anchor slot not widened by the ring-up stage ring), `PackConfig.AnchorApproachFrac` (> 0: Anchor last-stretch speed floor, replaces SlotApproachFrac for the Anchor). Default output bit-identical: 9 same-seed logs (`--map real --scenario hunt --brain scriptedpack@1 --comps LH,LLMH,LLH --seeds 1-3`), `diff -r` empty vs a build of the T-115j HEAD (03983bc). Tests: AI.Pack.Tests 28/28, AI.PackHarness.Tests 11/11.
Finding: the Anchor is already time-optimal in transit (GridRoutePlanner uses the harness's own soft-ground cost, full speed, started at t=0, d_slot falls ~1.6 m/s); levers left are slot geometry / stage ring / last-stretch speed.
Dev seeds 1-12 (`Tools/m007_bay_dev.sh`): C1 / P1 / P4 / P5
- default 0.688 / FAIL .571 / .015 / .708
- AnchorOffsetRad 0.3: .611 / PASS .701 / .034 / .644 ; 0: .304 / .710 / .060 / .650 (C1 collapses, rejected)
- AnchorSkipStage: .650 / PASS .857 / .051 / .811
- AnchorApproachFrac=1: .571 / .582 / -.010 / .679
- both (S1): .714 / PASS .857 / .076 / .961 (best on dev)
Hold-out rev 2 (bay_cost@v2, seeds 3001-3030/3001-3010; `M007_PACK="AnchorSkipStage=true,AnchorApproachFrac=1" Tools/m007_bay_holdout.sh evidence/T-115k/s1`), base = evidence/T-115j/base: `clause_v1.txt`
- P4 bs_rate_minus_k_nobait -0.2542 -> -0.1023 (FAIL, better but not passing); P5 0.992 -> 0.696 (CI lower 0.886 -> 0.598); P6 0.663 -> 0.467; C1 0.919 -> 0.836 (PASS); P1 min fit 0.832 -> 0.934; I3 unchanged FAIL; mv2 C1 0.638 -> 0.683, C2 PASS.
Fresh seeds 4001-4030 and 900 s not run: the candidate does not pass P4 and costs P5/P6, so the gate to run them (clearly helps) was not met.
