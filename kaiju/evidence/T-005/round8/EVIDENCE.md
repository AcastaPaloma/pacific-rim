# T-005 r8 evidence
| artifact | command | commit |
|---|---|---|
| Tools/GaitOpt/out/r8/{s,u}_{005,010,015}.{json,txt,log} | `bash Tools/GaitOpt/chain_r8.sh s Tools/GaitOpt/out/r7/d_020.json '{}' -- 0.015 0.010 0.005`; same with `u Tools/GaitOpt/out/r7/t7_020.json '{"tau_frac":0.7}'` | d386f28 |
| ladder_rows.md | `python evidence/T-005/round8/scripts/headroom_r8.py s_005 s_010 s_015 d_020 u_005 u_010 u_015 t7_020` | d386f28 |
| Assets/Game/Config/Mech/gait_reference_qp[_ladder]_v6{,d,e,g}.json | `python scripts/export_ladder_r8.py --tol 5e-5 --suffix <s> <stems>` | d386f28 / final |
| cycle1..3/{summary,variants}.txt | `RUN=n VAR=<variants> bash scripts/run98.sh` (variants via `scripts/variants_r8.py q0|q1|q2`), summaries via `scripts/summ.py` | f152bc5, d386f28, final |
