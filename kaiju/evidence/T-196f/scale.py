import json, os, sys
sys.path.insert(0, "/Users/kuanw/Me/code/pacific-rim/.claude/worktrees/fable-orchestrator-prompt-83a0a8/.wt/T-196f/kaiju/Pipelines/capture")
from capture_tools import style
C = "/private/tmp/claude-501/-Users-kuanw-Me-code-pacific-rim--claude-worktrees-fable-orchestrator-prompt-83a0a8/783d9ab8-fde5-48e3-b851-e65ddc3ac6c1/scratchpad/cap"
arm, clip = sys.argv[1:3]
r = style.scale_check_set(f"{C}/{arm}/{clip}")
bad = [x for x in r["rows"] if not x["ok"]]
os.makedirs(f"/private/tmp/t196f_run/scale/{arm}", exist_ok=True)
json.dump({"arm": arm, "clip": clip, "frames": r["frames"], "pass": r["pass"], "n_bad": len(bad), "bad": bad[:5],
           "min_match": min((x["match"] for x in r["rows"] if x["match"] is not None), default=None)}, open(f"/private/tmp/t196f_run/scale/{arm}/{clip}.json", "w"))
