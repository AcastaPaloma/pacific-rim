"""Step 2: lateral y_ref = stance-foot centre in single support (qp_r17_yref_ss), alone vs control, on the control plant and on the Y8 plant. 8 seeds (0-3 train, 4-7 held-out), isolated."""
import sys; sys.path.insert(0, '/Users/kuanw/Me/code/pacific-rim/.claude/worktrees/fable-orchestrator-prompt-83a0a8/kaiju/.wt/T-005-r17/kaiju/Tools/GaitLearn')
from common import *
named = {"y8_yr025": {**Y8, "qp_r17_yref_ss": 0.25}, "y8_yr075": {**Y8, "qp_r17_yref_ss": 0.75}, "ctl_yr025": {**CONTROL, "qp_r17_yref_ss": 0.25}, "y8b_yr1_omy12": {**Y8, "qp_om_y": 1.2, "qp_r17_yref_ss": 1.0}}
out = eval_configs(named, list(range(8)), "step2b", sys.argv[1])
for lab, rows in out.items(): print(fmt(lab, rows), flush=True)
