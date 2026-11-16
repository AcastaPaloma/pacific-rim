"""Standalone-batch evaluation of a weight vector: usage final_eval.py <out_dir> <weights.json|ZERO|CONTROL> <label> <k0> <k1>... ; one Unity batch, gait logs kept for metrics."""
import sys, subprocess
sys.path.insert(0, '/Users/kuanw/Me/code/pacific-rim/.claude/worktrees/agent-abf9a33a7aa192d55/kaiju/Tools/GaitLearn')
from train import *
out, wf, label = sys.argv[1:4]; ks = [int(x) for x in sys.argv[4:]]
if wf == "CONTROL": extra = {k: 0 for k in ()}; base_extra = None
os.makedirs(out, exist_ok=True)
if wf == "CONTROL":
    lines = []
    for k in ks:
        d = dict(L7); d["qp_r10_diag"] = 1; d["qp_pd_scale"] = round(0.25 + 1e-4 * k, 4); lines.append(f"{label}s{k} " + json.dumps(d, separators=(",", ":")))
else:
    w = np.zeros(10) if wf == "ZERO" else np.array(json.load(open(wf))["w"])
    lines = [variant(label, w, k) for k in ks]
res, o = rollout(lines, label, "/private/tmp/claude-501/t005r16")
rows = []
for k in ks:
    t = res.get(f"{label}s{k}")
    m = subprocess.run(["/opt/miniconda3/envs/kaiju/bin/python", P + "/Tools/GaitLearn/metrics.py", f"{o}/out/gait/walk_fr010@{label}s{k}.gait.jsonl"], capture_output=True, text=True).stdout.split()
    rows.append((k, t, m))
    print(label, "seed", k, "fall/surv", t, "DS a", m[0] if m else None, "DS len", m[1] if m else None, flush=True)
json.dump(rows, open(f"{out}/{label}.json", "w"))
shutil.copy(f"{o}/out/summary.txt", f"{out}/{label}_summary.txt")
