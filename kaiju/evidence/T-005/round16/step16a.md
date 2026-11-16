# T-005 round 16: learned lateral residual (option B), CMA-ES, 21 generations; NOT STABLE, acceptance failed, not merged

Branch task/T-005-r16 (from r15 2e3b433). Plant = N1 body (10 x 15 m sole, stance 16 m, v7s10 rows) + qp_om_y 0.8, walk_fr010, 63 s, walk_mode 0, L7 flags. No threshold, limit or gate changed.

## Hook (default off, bit-identical)
`qp_r16` (flag) in SagittalQpController.LateralRows. Linear policy, 5 features f = [1, (ys-y)/l, (ys-y_ref)/l, vy/sqrt(g l), single-support flag] (ys = mean lateral position of the loaded sole corners; y = CoM, y_ref = midpoint of both feet). Outputs: lateral reference shift dy = qp_r16_dymax (4 m) tanh(wy.f), lateral gain scale exp(clamp(wg.f, +-0.5)). 10 weights `qp_r16_wy0..4, qp_r16_wg0..4` (policy file = the weight JSON; passed as tuning variants). Only controller targets change; no external force. Verified: flag off = control (25.6 31.2 38.7 30.4); flag on with zero weights = flag off (4 seeds, standalone batch). Units.Mech 129 pass / 7 fail and Physics.Body 112/0; the 7 Mech failures (EveryJoint_HasFiveDegreesToSpare x5, YawMoment...FrictionBudget x2: reference-family tests) are identical on the r15 commit without my change, i.e. pre-existing, not touched.
Finding: the QP lateral reference y_ref is the mean of all 8 sole corners (both feet), i.e. the midpoint between the feet even in single support; the CoM is never asked to go over the stance foot by the lateral task. That is the mechanism behind the r15 lateral failure; the residual feature (ys - y_ref) lets the policy supply the missing shift (a hand fix, dy = ys - y_ref in single support, would be the obvious r17 test, but is a controller change, not done here).

## Method and why
Plant is Unity physics (no dotnet equivalent of the same plant exists), so rollouts are batch Unity runs of ScenarioCli with per-candidate variants: 48 rollouts per ~2 min generation (~2 s per 63 s run). Separable CMA-ES (diagonal covariance, CSA step size), lambda 12, sigma0 0.5, 10 weights, fitness = mean over seeds 0-3 of min(fall time, 63). Chosen because the policy is small and linear, there is no gradient through Unity, rollouts are cheap and deterministic per batch, and a deep RL / BC pipeline needs a differentiable or vectorised plant that does not exist. Tools/GaitLearn/{train.py, final_eval.py, metrics.py}; resumable state evidence/T-005/round16/train1/state.json.
Caveat found: results depend on batch composition (the same variant gives different fall times in different batches, e.g. Y8 seed 2: 22.5, 23.1, 19.7 s; identical variants inside one batch reproduce). So fitness has about +-3 s noise per seed, which limits CMA-ES.

## Learning curve (train seeds 0-3, candidate 0 = distribution mean; baseline Y8 = 25.8 mean)
gen 0..20 mean-candidate fitness: 25.4 31.5 27.1 .. 29.2 29.4 32.0 25.1 36.0 28.8 28.8 .. 34.8 27.4 30.1 27.5 22.2 .. 27.3 27.5 30.6; best-of-gen 31-40, median-of-gen 22.8 -> 26-30. A slow rise of about 3-4 s, noise-dominated; sigma 0.46 -> 0.29. Full log train1.log, per-gen summaries train1/gen*_summary.txt.

## Final evaluation (standalone batches, gait logs; DS a over DS ticks 8.2 s..end, DS len = mean DS episode)
| config | seeds | DS a | DS len (s) | survival (s) | median |
|---|---|---|---|---|---|
| control (train 0-3) | 0-3 | -0.73 -0.92 -0.77 -0.65 | 1.48 1.46 1.51 1.50 | 25.6 31.2 38.7 30.4 | 30.8 |
| control (held-out) | 4-7 | -0.80 -0.58 -1.09 -0.76 | 1.57 1.43 1.55 1.54 | 25.8 30.1 26.8 30.6 | 28.5 |
| Y8 (om_y 0.8, no residual) | 0-3 | -0.05 -0.92 -0.48 -0.20 | 2.11 1.47 1.39 1.92 | 28.0 28.0 19.7 25.8 | 26.9 |
| policy mean gen 21 | 0-3 | -0.27 -0.36 -0.53 +0.01 | 1.72 1.58 1.47 1.56 | 27.9 24.1 41.0 24.7 | 26.3 |
| policy mean gen 21 (held-out) | 4-7 | -0.30 -0.78 -0.43 -0.50 | 1.61 1.55 1.57 1.46 | 20.7 32.0 31.9 38.6 | 31.9 |
| best candidate gen 14 | 0-3 | -0.54 -0.16 +0.09 -0.40 | 1.55 1.48 1.67 1.50 | 22.9 26.8 33.4 32.7 | 29.8 |
| best candidate gen 14 (held-out) | 4-7 | -0.46 +0.12 +0.03 +0.11 | 1.59 1.34 1.40 1.14 | 23.3 26.3 21.8 21.9 | 22.6 |
Policy mean weights (wy0..4, wg0..4): 0.31 -0.12 -0.37 0.78 -0.05 | -0.11 0.47 -1.48 -0.25 -1.34. No fall-free run in any config (all fall before 42 s).

## Verdict
NOT STABLE; acceptance failed: DS a >= -0.2 on all seeds (policy mean: 3 of 8 seeds ok, worst -0.78), DS <= 0.9 s (1.46-1.72 s), survival above control 30.8 s (train median 26.3, held-out median 31.9 vs control held-out 28.5; mean 29.4 train / 30.8 held-out vs control 31.5 / 28.3). The residual gives at most a marginal, statistically unsupported survival gain on held-out seeds; the full stability gate and M-001/M-002/M-006 were not run (no candidate earned it). Not merged; HANDOFF line appended only.

## What limited it
1. Policy class: lateral-only, 10 weights, one linear layer; DS length (1.5 s vs 0.9 s) and the DS braking are sagittal / load-transfer properties that the lateral task cannot change. CoP target and lift-off gate outputs of the r15 plan were not implemented.
2. Evaluation noise (batch-order dependence, +-3 s per seed) with 4 seeds and 12 candidates swamps the 3 s signal.
3. Objective = survival only, no DS terms.

## r17 plan
(a) Fix the evaluation first: find the cross-run state leak (batch position changes outcomes; suspect static caches in the scenario/reference loader) or run one candidate per process batch of fixed order; (b) add the sagittal outputs (CoP target offset, swing lift-off gate threshold) and DS a / DS length terms to the fitness, 8 seeds per candidate, 40+ generations (about 2 h per 30 generations here); (c) test the one-line structural fix y_ref = stance-foot centre in single support as a flag before more learning, since the policy cannot easily synthesise it from tanh-bounded features; (d) per §14, escalate this stall to Opus-high if (a)-(c) fail, and ask the founders whether walk_mode 2 stays shipped.
