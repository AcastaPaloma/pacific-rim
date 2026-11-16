"""Step 1 diagnosis, round 2: Y8 plant (N1 body + om_y 0.8) in several batch orders / mixed with the control, hash the gait logs (ms removed)."""
import sys, hashlib, json

sys.path.insert(0, '/Users/kuanw/Me/code/pacific-rim/.claude/worktrees/fable-orchestrator-prompt-83a0a8/kaiju/.wt/T-005-r17/kaiju/Tools/GaitLearn')
from common import *
def run(tag, items, isolated=False):
    lines = [vline(n, c, k) for n, c, k in items]
    res, o = batch(lines, tag, isolated=isolated)
    out = {}
    for n, c, k in items:
        f = f"{o}/out/gait/walk_fr010@{n}.gait.jsonl"
        h = hashlib.sha256(re.sub(r'"ms":[-\d.eE+]+,?', '', open(f).read()).encode()).hexdigest()[:12] if os.path.exists(f) else None
        out[n] = (res.get(n), h)
    return out
Y = lambda k: (f"y{k}", Y8, k)
C = lambda k: (f"c{k}", CONTROL, k)
if __name__ == "__main__":
    allr = {}
    iso = "--iso" in sys.argv
    sets = [("Y_fwd", [Y(0), Y(1), Y(2), Y(3)]), ("Y_rev", [Y(3), Y(2), Y(1), Y(0)]), ("Y2alone", [Y(2)]), ("Cthen", [C(0), Y(2)]), ("Ythen", [Y(2), C(0)]), ("Cthen2", [C(2), Y(2)])]
    for tag, items in sets:
        allr[tag] = run(("det3_" if iso else "det2_") + tag, items, iso); print(tag, allr[tag], flush=True)
        json.dump(allr, open(sys.argv[1], "w"))
