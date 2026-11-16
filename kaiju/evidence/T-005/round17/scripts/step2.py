"""Step 2: lateral y_ref = stance-foot centre in single support (qp_r17_yref_ss), alone vs control, on the control plant and on the Y8 plant. 8 seeds (0-3 train, 4-7 held-out), isolated."""
import sys; sys.path.insert(0, '/Users/kuanw/Me/code/pacific-rim/.claude/worktrees/fable-orchestrator-prompt-83a0a8/kaiju/.wt/T-005-r17/kaiju/Tools/GaitLearn')
from common import *
named = {"ctl": CONTROL, "ctl_yr1": {**CONTROL, "qp_r17_yref_ss": 1.0, "qp_lateral": 1}, "ctl_yr05": {**CONTROL, "qp_r17_yref_ss": 0.5},
         "y8": Y8, "y8_yr1": {**Y8, "qp_r17_yref_ss": 1.0}, "y8_yr05": {**Y8, "qp_r17_yref_ss": 0.5}}
out = eval_configs(named, list(range(8)), "step2", sys.argv[1])
for lab, rows in out.items(): print(fmt(lab, rows), flush=True)
