"""T-154 HUD frame check on a 1920x1080 player frame: grid uniformity (scale.blocks_uniform at k=4, whole frame and HUD regions),
palette adherence of the HUD pixels (exact hud_colours entries), no clipping at k=4 (HUD rect blocks inside the frame).
Run: /opt/miniconda3/envs/kaiju/bin/python hud_check.py FRAME.png [--profile warm_amber_haze] [--out checks.json]"""
import argparse, json, os, sys
import numpy as np
from PIL import Image
HERE = os.path.dirname(os.path.abspath(__file__)); ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, os.path.join(ROOT, "Pipelines", "capture"))
from capture_tools import scale as sc  # noqa: E402

ap = argparse.ArgumentParser(); ap.add_argument("frame"); ap.add_argument("--profile", default="warm_amber_haze"); ap.add_argument("--out", default=os.path.join(HERE, "checks.json")); ap.add_argument("--k", type=int, default=4)
a = ap.parse_args(); k = a.k
im = np.asarray(Image.open(a.frame).convert("RGB")); H, W = im.shape[:2]
tab = json.load(open(os.path.join(ROOT, "Assets/Game/Config/UI/hud_colours.json")))["profiles"][a.profile]
hud_cols = {tuple(int(h[i:i + 2], 16) for i in (0, 2, 4)) for h in [tab["panel"], tab["text"]] + tab["fills"]}
lay = {e["id"]: e for e in json.load(open(os.path.join(ROOT, "Assets/Game/Config/UI/hud_layout.json")))["elements"]}
res = {"frame": os.path.basename(a.frame), "size": [W, H], "k": k, "profile": a.profile}
best = max((sc.blocks_uniform(im, k, (ox, oy)), ox, oy) for ox in range(k) for oy in range(k))
res["blocks_uniform_whole_frame_best_offset"] = {"value": best[0], "offset": [best[1], best[2]]}
res["blocks_uniform_whole_frame_offset00"] = sc.blocks_uniform(im, k)
OX, OY = best[1], best[2]   # the present stage shifts the world by whole pixels; the HUD blocks start at the same offset
# HUD regions (the layout rects inflated by the 2 px backdrop), per element
regs = {}
for eid, e in lay.items():
    x0, y0, x1, y1 = (e["x"] - 2) * k + OX, (e["y"] - 2) * k + OY, (e["x"] + e["w"] + 2) * k + OX, (e["y"] + e["h"] + 2) * k + OY
    if x0 < 0 or y0 < 0 or x1 > W or y1 > H: regs[eid] = {"outside_after_offset": True}; continue
    sub = im[y0:y1, x0:x1]
    regs[eid] = {"blocks_uniform_offset00": sc.blocks_uniform(np.roll(sub, 0, 0), k), "unique_colours": int(len(np.unique(sub.reshape(-1, 3), axis=0)))}
res["regions"] = regs
# HUD pixels: pixels equal to the HUD panel colour or other HUD colours inside any layout rect that are NOT world colours are hard to separate,
# so test the rects where a backdrop exists: every pixel in a drawn element's rect must be a HUD colour (backdrop covers the rect).
checked = bad = 0; offenders = []
drawn = []
panel = tuple(int(tab["panel"][i:i + 2], 16) for i in (0, 2, 4))
for eid, e in lay.items():
    x0, y0, x1, y1 = e["x"] * k + OX, e["y"] * k + OY, (e["x"] + e["w"]) * k + OX, (e["y"] + e["h"]) * k + OY
    sub = im[y0:y1, x0:x1].reshape(-1, 3)
    if not (sub == np.array(panel)).all(-1).any():
        continue   # not drawn this frame (unavailable data)
    if e["kind"] == "text":
        drawn.append(eid); continue   # label backdrops cover the text box only, not the whole rect: palette of text is covered by the dotnet raster test
    drawn.append(eid); checked += len(sub)
    ok = np.zeros(len(sub), bool)
    for c in hud_cols: ok |= (sub == np.array(c)).all(-1)
    nb = int((~ok).sum()); bad += nb
    if nb: offenders.append(eid)
res["drawn_elements"] = drawn
res["palette"] = {"hud_colours": sorted("%02x%02x%02x" % c for c in hud_cols), "pixels_checked": checked, "off_palette": bad, "offenders": offenders, "pass": bad == 0 and checked > 0}
hu = [v["blocks_uniform_offset00"] for v in regs.values() if "blocks_uniform_offset00" in v]
res["hud_grid"] = {"min_region_blocks_uniform": min(hu) if hu else None}
infl = lambda e: (e["x"] - 2 >= 0 and e["y"] - 2 >= 0 and e["x"] + e["w"] + 2 <= 480 and e["y"] + e["h"] + 2 <= 270)
clip = [i for i in drawn if not infl(lay[i])]
res["clipping"] = {"drawn_elements_whose_backdrop_leaves_the_480x270_frame": clip, "pass": not clip}
json.dump(res, open(a.out, "w"), indent=1); print(json.dumps({k2: v for k2, v in res.items() if k2 != "regions"}, indent=1))
