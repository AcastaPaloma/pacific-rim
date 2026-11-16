cd /Users/kuanw/Me/code/pacific-rim/.claude/worktrees/agent-abf9a33a7aa192d55/kaiju
PY=/opt/miniconda3/envs/kaiju/bin/python; R=evidence/T-005/round16/final; F=Tools/GaitLearn/final_eval.py
$PY $F $R CONTROL ctl 0 1 2 3
$PY $F $R ZERO y8 0 1 2 3
for p in policy_mean_g21 policy_best; do
 $PY $F $R evidence/T-005/round16/$p.json $p 0 1 2 3
 $PY $F $R evidence/T-005/round16/$p.json ${p}H 4 5 6 7
done
