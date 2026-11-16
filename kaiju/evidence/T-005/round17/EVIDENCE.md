# T-005 round 17: EVIDENCE (NOT STABLE; not merged)
Branch task/T-005-r17 (from r16 4adc3ea). Code commit bc1ece0 (flags, isolation); trainer/eval commits caff698 + final. Plant: N1 body (10x15 m sole, stance 16 m, v7s10 rows) + qp_om_y 0.8 ("Y8"), walk_fr010, 63 s, walk_mode 0. All rollouts `-isolated` (deterministic). Seed k = qp_pd_scale 0.25 + 1e-4 k. No threshold or reading changed.

## Step 1: determinism (evidence/T-005/round17/step1_determinism.md, det_test.log, det2.log, det3.log)
Cause: history in the single default PhysX scene reused across rollouts (first gait-log tick already differs). Fix: ScenarioCli `-isolated` = fresh preview-scene PhysX scene per rollout (BodyWorld.IsolatedScene, default off). Proof: Y8 seeds 0-3 + control in 6 different orders/mixes: identical gait-log hashes and fall times. Control (flags off, new code, isolated or not) = 25.6 31.2 38.7 30.4 on seeds 0-3 (bit-identical), seeds 4-7 25.8 30.1 26.8 30.6; 8-seed median 30.2, mean 29.9, DS a -0.79. (r16 "Y8" seed 2 = 19.7 was a history artefact; fresh = 23.1.)

## Step 2: y_ref = stance-foot centre in single support (qp_r17_yref_ss blend; step2.log, step2b.log; 8 seeds each, survival s)
| config | seeds 0-7 | median | mean | DS a mean |
|---|---|---|---|---|
| control | 25.6 31.2 38.7 30.4 25.8 30.1 26.8 30.6 | 30.2 | 29.9 | -0.79 |
| control + yref 1.0 | 22.3 23.2 27.4 28.0 23.6 21.4 24.6 23.5 | 23.6 | 24.2 | -0.67 |
| control + yref 0.5 / 0.25 | | 24.5 / 23.1 | 24.0 / 23.5 | -0.85 / -0.86 |
| Y8 | 28.0 28.0 23.1 25.8 22.9 21.9 22.6 21.6 | 23.0 | 24.2 | -0.17 |
| Y8 + yref 1.0 / 0.75 / 0.5 / 0.25 | 17.2 18.5 19.9 16.8 16.9 17.9 21.3 18.3 (1.0) | 18.1 / 20.4 / 27.2 / 22.5 | 18.4 / 20.8 / 26.5 / 25.1 | -0.67 / -0.58 / -0.50 / -0.55 |
| Y8 + yref 1.0 + om_y 1.2 | | 18.9 | 19.5 | -0.38 |
Result: the flag alone makes survival WORSE at every blend (the step in the lateral reference at lift-off, 8 m with the 16 m stance, is not tracked); kept default 0.

## Step 3: learned residual, lateral (10 weights, qp_r16) + sagittal (12 weights, qp_r17: forward CoM acceleration offset da = 1.5 tanh(wa.g) m/s^2, rear-unload lift-off threshold 0.1 W exp(clamp(wu.g, +-1.6))), g = [1, v_x/sqrt(g l), (DCM - CoP target)/l, DS flag, rear load share, step phase]
Trainer Tools/GaitLearn/train2.py (separable CMA-ES, lam 14, sigma0 0.4, 13 generations of 14 x 8 seeds = 1456 rollouts, stopped for time: Unity slots shared with other leads, 2-20 min/generation). Fitness per seed = T/63 + 0.5 clip((DSa+1)/0.8) + 0.5 clip((2-DSlen)/1.1); mean over TRAIN seeds 0-7. HELD-OUT seeds 8-15 never used in training. Zero weights = Y8 exactly (gen 0 check). Final eval final.log / final.json (weights in final.json: mean_w, best_w from gen 10).
| set | config | survival per seed (s) | median | mean | DS a mean | DS len mean (s) |
|---|---|---|---|---|---|---|
| train 0-7 | control | 25.6 31.2 38.7 30.4 25.8 30.1 26.8 30.6 | 30.2 | 29.9 | -0.79 | 1.50 |
| train | Y8 | 28.0 28.0 23.1 25.8 22.9 21.9 22.6 21.6 | 23.0 | 24.2 | -0.17 | 1.73 |
| train | policy (CMA mean) | 20.3 29.6 35.8 37.2 31.4 39.2 28.9 50.2 | 33.6 | 34.1 | -0.43 | 1.60 |
| train | policy (best cand gen 10) | 35.7 60.4 27.3 34.6 34.5 26.5 47.5 31.4 | 34.5 | 37.2 | -0.38 | 1.55 |
| held-out 8-15 | control | 26.0 38.9 25.1 26.3 24.9 25.0 25.1 23.2 | 25.1 | 26.8 | -0.71 | 1.49 |
| held-out | Y8 | 23.8 21.4 35.1 38.3 32.6 27.3 22.2 23.4 | 25.6 | 28.0 | -0.26 | 1.58 |
| held-out | policy (CMA mean) | 34.1 31.1 29.0 63.0 48.7 35.1 35.3 44.0 | 35.2 | 40.0 | -0.40 | 1.61 |
| held-out | policy (best cand) | 23.5 24.6 25.0 32.2 46.1 38.3 40.1 28.0 | 30.1 | 32.2 | -0.50 | 1.62 |
Policy mean weights (wy0-4, wg0-4, wa0-5, wu0-5): 0.37 0.39 -0.19 -0.35 -0.80 | -0.35 -0.18 1.00 -0.52 -0.54 | 0.79 0.22 0.79 0.00 0.13 0.58 | 0.19 0.35 -0.08 0.31 0.00 -0.66.
Verdict: best result of T-005 so far (held-out median +10 s over control, one fall-free seed 63.0 s of 8; generalises, unlike r16) but NOT STABLE: 7/8 held-out seeds fall (29-49 s), DS a -0.40 (rule >= -0.2, 2 of 8 held-out seeds pass), DS length 1.6 s (rule <= 0.9). Acceptance failed; M-001/M-002/M-006 not run.

## Tests
Units.Mech dotnet 129 pass / 7 fail (the 7 pre-existing reference-family failures, same as r15/r16), Physics.Body 112/0 (logs dotnet_mech.log, dotnet_body.log).
