# T-005 r4 9.5l: joint-space swing task (qp_swj); 3 Unity cycles: NO stable walk (best 4-seed median 30.8 s, single best 36.7 s, vs 24.2 s for the 9.5k config)
Code (imp 3, default 0, none in a json): `qp_swj` w (joint-space swing task: swing hip + knee, + ankle with `qp_swj_ankle` 1, track the reference q / qd / qdd of the QP clock, PD `qp_swj_om`, commanded joint accel clamp `qp_swj_amax` rad/s2); the world heel task is smoothstep-blended in over the last `qp_swj_td_frac` of the swing (1 when late; `qp_sw_amax` kept) and the joint task fades to 50 % while it is on. Diag[15] = heel blend. Variants `l1 l2 l3` in `scripts/variants95f.py` (validated), `swstat95l.py` (swing duration + hip / knee saturation), `phase95l.py`. C = 9.5k best (amax 20 + air 0.3 + heel-only + om_sw 2 on stack B); Ubase = C and reproduces 24.2 exactly.
## Swing-hip authority check (reference, v4 Fr 0.10 row, ts 4.41 s)
The reference swing leg needs hip 3.3e8 N m peak (0.28 of force_limit_nm 1.2e9; 0.28 of 0.98 LIM too), knee 0.31, ankle 0.72 of its limit; |qdd| max in the swing hip 1.8, knee 4.6 rad/s2 (hip ~1.1e8 N m per rad/s2, so ~10 rad/s2 is available). Well under 0.9: the reference itself is NOT the reason for the 1.18e9 hip torques; the demand was (heel task 90-960 m/s2). The joint task demand is clamped at 6 rad/s2 (hip ~0.6 LIM).
## Results (4 seeds, fall s; swing = swing duration median over swings after t 9 s; sat = swing hip ticks at >= 0.97 LIM)
| config | seeds | median | best | swing s | hip sat | knee sat |
|---|---|---|---|---|---|---|
| Ubase (9.5k best) | 23.5 25.0 24.9 23.3 | 24.2 | 25.0 | 1.23 | 15.4 % | 1.2 % |
| swj 1e3 om 2 | 17.5 17.9 27.8 24.4 | 21.1 | 27.8 | 1.23 | 54.7 % | 0.3 % |
| **swj 1e3 om 3** | 16.1 31.2 34.0 30.4 | **30.8** | 34.0 | 1.67 | 6.0 % | 15.4 % |
| swj 1e3 om 2 + ankle | 21.3 30.7 26.2 36.7 | 28.4 | **36.7** | 1.31 | 14.0 % | 1.7 % |
| swj 1e3 om 3 + ankle | 29.0 23.7 25.0 22.6 | 24.4 | 29.0 | 2.63 | 3.1 % | 0.0 % |
| swj 1e3 om 2.5 + ankle | 27.6 20.9 27.6 25.0 | 26.3 | 27.6 | 1.93 | 16.4 % | 0.2 % |
| swj 3e3 om 3 + ankle | 24.0 24.2 24.2 28.1 | 24.2 | 28.1 | 3.33 | 7.8 % | 12.7 % |
Other 21 variants (td_frac 0.1 / 0.3, joint amax 3 / 10, weight 100, no / 40 / 30 sw_amax, om 4, sw_follow 0.5, xoff_max 8): median 18.2-24.9 s (`summary_l1..l3.txt`, `swstat_l*.txt`). The seed spread of one config is 6-18 s, so 30.8 vs 24.2 is about one spread; no config is clearly better.
## Diagnosis of what is left
The hip saturation is mostly gone (3-8 % vs 15 % in the best configs) and the swings that do happen last 2.6-3.3 s (ref 3.1 s) in the quietest configs, but the walk still stalls: CoM speed 0.5-1.4 m/s (ref 6.4), swings become late (tau 5-9 s, late up to 4 s) with the heel 12-22 m short of its target when the heel task takes over, then the leg lands off-schedule and the body falls at 23-37 s (`Uj1om3s2.gait.jsonl.gz`). The swing is no longer the limiter; the stance side / speed regulation is (not tried: row selection from measured Froude in the ladder, stance push-off).
## Not done
No stable walk (no 3 steady strides at Fr 0.10, no config without a fall by 63 s), so M-001@v1.1 / M-002@v1.1 were not computed. dotnet Units.Mech (Category!=KnownGap) 119/0; EditMode `ImpByteIdentityTests` 1/1 pass (`step5l/editmode.xml`, Unity binary run directly). No assist, no threshold / mech_frame / body_tuning / walk_tuning.json edit, no lower Froude target.
