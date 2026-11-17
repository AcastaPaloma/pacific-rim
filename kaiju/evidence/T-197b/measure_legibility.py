"""T-197b: legibility of the destruction cue per impact. For each 'destruction fx burst' event: frame before the burst (b-2) vs burst frames.
Region = pixels whose luma changed by > 16/255 vs the pre frame, cut to the 60 % centre crop and the 4-frame-wide flash window (b+1..b+4) or the dust
window (b+5..b+40) separately. Metrics: share of frame, mean |dLuma| in region (0..255), contrast ratio (region mean luma vs 12 px ring mean luma on the
same frame, (hi+.05)/(lo+.05) with luma 0..1), and the same ring contrast on the PRE frame (so the cue's added contrast is visible).
Run: python measure_legibility.py FRAMES_DIR record.json OUT.json [fps=30]"""
import sys, json, re
import numpy as np
from PIL import Image
from scipy import ndimage
fr, rec, out = sys.argv[1:4]; fps = float(sys.argv[4]) if len(sys.argv) > 4 else 30.0
r = json.load(open(rec)); ev = r["scripted_events"]; tr = r["trace_1s"]; g0 = tr[0]["game_t"] - tr[0]["frame"] / fps
bursts = [float(re.match(r"t=([\d.]+)", e).group(1)) for e in ev if "destruction fx burst" in e]
def L(i):
    im = np.asarray(Image.open(f"{fr}/f{i:06d}.png").convert("RGB")).astype(float) / 255
    return im @ np.array([0.2126, 0.7152, 0.0722])
C = lambda a, b: (max(a, b) + .05) / (min(a, b) + .05)
def measure(pre, cur):
    H, W = pre.shape; crop = np.zeros_like(pre, bool); crop[H // 5:4 * H // 5, W // 5:4 * W // 5] = True
    m = (np.abs(cur - pre) > 16 / 255) & crop
    if m.sum() < 20: return {"share": 0.0, "dl": 0.0, "contrast": 1.0, "pre_contrast": 1.0}
    m = ndimage.binary_opening(m, iterations=1) if m.sum() > 400 else m
    ring = ndimage.binary_dilation(m, iterations=12) & ~ndimage.binary_dilation(m, iterations=2) & crop
    if not ring.any(): return {"share": float(m.mean()), "dl": 0.0, "contrast": 1.0, "pre_contrast": 1.0}
    return {"share": round(float(m.mean()), 5), "dl": round(float(np.abs(cur - pre)[m].mean() * 255), 2),
            "contrast": round(C(cur[m].mean(), cur[ring].mean()), 3), "pre_contrast": round(C(pre[m].mean(), pre[ring].mean()), 3)}
res = []
for n, t in enumerate(bursts, 1):
    b = int(round((t - g0) * fps)); pre = L(b - 2); ent = {"impact": n, "burst_frame": b}
    best = lambda idx: max((measure(pre, L(i)) | {"frame": i} for i in idx), key=lambda d: d["dl"] * d["share"] ** .5)
    ent["flash"] = best(range(b + 1, b + 5)); ent["dust"] = best(range(b + 5, b + 41, 3))
    res.append(ent)
json.dump(res, open(out, "w"), indent=1); print(json.dumps(res, indent=1))
