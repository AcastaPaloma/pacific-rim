"""Step 1 diagnosis: same control config + seeds in different batch orders; compare fall times and gait-log hashes (ms field removed)."""
import sys, hashlib, json
sys.path.insert(0, __import__('os').path.join(__import__('os').path.dirname(__file__), '../../../../Tools/GaitLearn'))
from common import *
def run(tag, order, cfg=CONTROL, suffix=""):
    lines = [vline(f"d{k}", cfg, k) for k in order]
    res, o = batch(lines, tag)
    out = {}
    for k in order:
        f = f"{o}/out/gait/walk_fr010@d{k}.gait.jsonl"
        h = hashlib.sha256(re.sub(r'"ms":[-\d.eE+]+,?', '', open(f).read()).encode()).hexdigest()[:12] if os.path.exists(f) else None
        out[k] = (res.get(f"d{k}"), h)
    return out
if __name__ == "__main__":
    allr = {}
    for tag, order in [("A", [0,1,2,3]), ("B", [3,2,1,0]), ("C0", [0]), ("C3", [3])]:
        allr[tag] = run("det_" + tag, order); print(tag, order, allr[tag], flush=True)
    json.dump(allr, open(sys.argv[1], "w"))
