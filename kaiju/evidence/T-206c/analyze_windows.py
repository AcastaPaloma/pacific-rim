import sys, json, re, glob, os
import numpy as np
from PIL import Image
sys.path.insert(0,"/Users/kuanw/Me/code/pacific-rim/.claude/worktrees/fable-orchestrator-prompt-83a0a8/kaiju/Pipelines/capture")
from capture_tools import scale as sc
fr, rec, out = sys.argv[1:4]
r = json.load(open(rec)); ev = r["scripted_events"]
cuts = [float(re.match(r"t=([\d.]+) destruction cam: cut", e).group(1)) for e in ev if "destruction cam: cut to" in e]
# game_t -> frame via trace (frame, game_t)
tr = r["trace_1s"]; g0 = tr[0]["game_t"] - tr[0]["frame"]/30.0
def fidx(t): return int(round((t - g0)*30))
L = lambda i: np.asarray(Image.open(f"{fr}/f{i:06d}.png").convert("RGB"))
res = {"cuts": [], "events": [e for e in ev if "destruction cam" in e]}
for n, ct in enumerate(cuts, 1):
    c = fidx(ct); idxs = [c + int(k*30*4.5/7) for k in range(8)]
    ims = [L(i) for i in idxs]
    sheet = np.concatenate([np.concatenate([Image.fromarray(a).resize((480,270)) and np.asarray(Image.fromarray(a).resize((480,270))) for a in ims[:4]],1), np.concatenate([np.asarray(Image.fromarray(a).resize((480,270))) for a in ims[4:]],1)],0)
    Image.fromarray(sheet).save(f"{out}/impact{n}_window.png")
    b, a_ = L(c + 40), L(c + 135)   # before the hit (~1.3 s after cut) vs end of hold (~4.5 s)
    ch = float((np.abs(b.astype(int) - a_.astype(int)).max(-1) > 16).mean())
    H,W=b.shape[:2]; m=(np.abs(b.astype(int) - a_.astype(int)).max(-1) > 16); cc=float(m[H//5:4*H//5, W//4:3*W//4].mean())
    gs = []
    for i in range(c, c + 6*10, 10):
        im = L(i); gs.append(round(max(sc.blocks_uniform(im, 4, (ox, oy)) for ox in range(4) for oy in range(4)), 4))
    res["cuts"].append({"impact": n, "cut_t": ct, "cut_frame": c, "grid_share_per_frame": gs, "changed_pixel_share_hit_window": round(ch, 4), "changed_share_center_crop": round(cc, 4)})
json.dump(res, open(f"{out}/cut_frame_checks.json", "w"), indent=1)
print(json.dumps(res["cuts"]))
