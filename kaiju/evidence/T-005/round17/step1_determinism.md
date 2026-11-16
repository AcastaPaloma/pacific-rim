# T-005 r17 step 1: batch-order nondeterminism fixed (isolated PhysX scene per rollout)

Symptom (r16): the same config + seed gave different results depending on what ran before it in the same Unity process.
Diagnosis (evidence det_test.log, det2.log): control (L7) was order independent (bit-identical gait-log hashes, `ms` field stripped, in orders fwd/rev/alone), but the Y8 plant (N1 body + qp_om_y 0.8) was not: Y8 seed 2 = 23.1 s as first run of a process, 19.7 s after other runs; seeds 0/1 28.0/28.0 in order 0,1,2,3 but 20.0/19.2 in order 3,2,1,0. The first gait-log tick already differs (com_v 2e-4..6e-4 m/s after ONE step), so the cause is hidden history in the native physics scene (the one default PhysX scene is reused across `NewScene` calls; its internal handle/pool state depends on the rollouts before). No managed static state exists in Physics.Body / Control / Units.Mech (grep).
Fix (default off): `BodyWorld.IsolatedScene` (static, invalid = old behaviour). ScenarioCli `-isolated` creates an `EditorSceneManager.NewPreviewScene()` per rollout and sets it; BodyWorld.CreateGround / ArticulatedBody root are placed in it and `BodyWorld.Simulate` steps its own `PhysicsScene`, so every rollout starts in a fresh PhysX scene.
Proof (det3.log, `-isolated`, same four Y8 seeds + control): hashes identical in every order.
| set | y0 | y1 | y2 | y3 | c0 | c2 |
|---|---|---|---|---|---|---|
| fwd 0,1,2,3 | 28.0 | 28.0 | 23.1 | 25.8 | | |
| rev 3,2,1,0 | 28.0 | 28.0 | 23.1 | 25.8 | | |
| y2 alone | | | 23.1 | | | |
| c0 then y2 | | | 23.1 | | 25.6 | |
| y2 then c0 | | | 23.1 | | 25.6 | |
| c2 then y2 | | | 23.1 | | | 38.7 |
Gait-log hashes (sha256 of the log minus `ms`) equal across all orders (y0 9030756e9472, y1 7b58aaea27ba, y2 da4286a71ac1, y3 6bcc2f12d6b9, c0 00d066fc017d); the control hash equals the non-isolated one, so the control is unchanged (25.6 / 38.7 reproduced; 31.2 / 30.4 in the 8-seed baseline). Y8 values 28.0 28.0 23.1 25.8: the r16 Y8 row had 19.7 for seed 2 because it was run in a batch after other runs; the fresh-scene value is 23.1.
Command: `python evidence/T-005/round17/scripts/det2.py <out.json> --iso` (code commit see EVIDENCE.md).
