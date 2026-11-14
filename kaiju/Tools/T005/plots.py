#!/usr/bin/env python
"""T-005 plots from gait_log@v1: centre of mass against the feet (C6: the CoM leads each step and is caught by the landing foot), and speed against command.
Usage: python Tools/T005/plots.py <evidence dir> <capture> [<capture> ...]   (kaiju conda env)"""
import gzip, json, os, sys
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

def rows(path):
    if not os.path.exists(path): path += ".gz"
    op = gzip.open if path.endswith(".gz") else open
    with op(path, "rt") as f: return [json.loads(l) for l in f if l.strip()]

ev = sys.argv[1]
os.makedirs(os.path.join(ev, "plots"), exist_ok=True)
for cap in sys.argv[2:]:
    g = rows(os.path.join(ev, "gait", cap + ".gait.jsonl")); h = g[0]; st = [r for r in g if r["rec"] == "step"]
    t = [r["t"] for r in st]; mg = h["mass_kg"] * h["g_mps2"]
    fig, ax = plt.subplots(4, 1, figsize=(11, 11), sharex=True)
    ax[0].plot(t, [r["com_p"][2] for r in st], "k", label="CoM z"); ax[0].plot(t, [r["ctl"]["dcm"][1] for r in st], "g--", lw=0.8, label="DCM z")
    for i, fid in enumerate(h["foot_ids"]):
        c = "C%d" % i
        ax[0].plot(t, [r["feet"][i]["p"][2] for r in st], c, label="foot %s sole centre z" % fid)
        ax[1].plot(t, [r["feet"][i]["p"][0] for r in st], c, label="foot %s x" % fid)
        ax[2].plot(t, [r["feet"][i]["fn"] / mg for r in st], c, lw=0.7, label="foot %s Fn / (m g)" % fid)
    ax[1].plot(t, [r["com_p"][0] for r in st], "k", label="CoM x"); ax[1].plot(t, [r["ctl"]["dcm"][0] for r in st], "g--", lw=0.8, label="DCM x")
    ax[3].plot(t, [(r["com_v"][0] ** 2 + r["com_v"][2] ** 2) ** 0.5 for r in st], "k", label="CoM horizontal speed"); ax[3].plot(t, [r["intent"]["cmd_speed_mps"] for r in st], "r", label="command (limited)"); ax[3].plot(t, [min(r["intent"]["requested_speed_mps"], 12) for r in st], "r:", label="intent as received (clipped at 12 for the plot)")
    ax[0].set_ylabel("forward (m)"); ax[1].set_ylabel("lateral (m)"); ax[2].set_ylabel("normal force (body weights)"); ax[3].set_ylabel("m/s"); ax[3].set_xlabel("t (s)")
    for a in ax: a.legend(loc="upper left", fontsize=7); a.grid(alpha=0.3)
    fig.suptitle("%s  commit %s  (gait_log@v1)" % (cap, h["commit"])); fig.tight_layout()
    fig.savefig(os.path.join(ev, "plots", cap + ".com_vs_feet.png"), dpi=80); plt.close(fig)
    print("wrote", cap)
