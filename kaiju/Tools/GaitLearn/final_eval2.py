"""r17 final evaluation: control, Y8, learned policy (CMA-ES mean and best-fit candidate of the last generations) on train seeds 0-7 and held-out seeds 8-15 (isolated batch, deterministic).
Usage: final_eval2.py <train_dir> <out_json>"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from train2 import *
s = json.load(open(f"{sys.argv[1]}/state.json")); hist = s["hist"]
mean_w = np.array(s["m"]); bi = max(range(len(hist)), key=lambda i: hist[i]["best_fit"]); best_w = np.array(hist[bi]["best_w"])
named = {"ctl": CONTROL, "y8": PLANT, "pol_mean": cfg_of(mean_w), "pol_best": cfg_of(best_w)}
out = {}
for split, seeds in (("train", TRAIN), ("held", HELD)):
    o = eval_configs(named, seeds, "final_" + split, f"{sys.argv[2]}.{split}.json")
    for lab, rows in o.items(): print(split, fmt(lab, rows), flush=True); out[f"{split}/{lab}"] = rows
json.dump({"mean_w": mean_w.tolist(), "best_w": best_w.tolist(), "best_gen": bi, "rows": out}, open(sys.argv[2], "w"))
