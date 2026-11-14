"""Compose the Unity 4-view capture and the pixel-proxy capture (T-007 C29, E-7) (kaiju env).

Run after MechFrameCapture.Run:
  /opt/miniconda3/envs/kaiju/bin/python Pipelines/blender/mech_frame_balanced/compose_unity.py

Inputs : evidence/T-007/raw/unity_{front,side,back,three_quarter}.png, raw/unity_views.json
Outputs: evidence/T-007/unity_4view.png        the four views with 10 m grid, 67 m deck and 80 m lines, bounds readout
         evidence/T-007/unity_pixel_proxy.png  1/5 downscale (nearest) + quantised to the R-002 slate_crimson profile
                                               plus the mech palette roles, shown x5 (stand-in until the T-004 renderer lands)
"""
import json
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
EV = os.path.join(PROJECT, "evidence", "T-007")
RAW = os.path.join(EV, "raw")
try:
    FONT = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial.ttf", 22)
except Exception:
    FONT = ImageFont.load_default()

v = json.load(open(os.path.join(RAW, "unity_views.json")))
W, H, pxm, yc = v["w"], v["h"], v["px_per_m"], v["y_centre"]
row = lambda y: H / 2 - (y - yc) * pxm

tiles = []
for name in v["views"]:
    im = Image.open(os.path.join(RAW, f"unity_{name}.png")).convert("RGB")
    base = im.copy()
    d = ImageDraw.Draw(im)
    for y in range(0, 91, 10):
        d.line([(0, row(y)), (W, row(y))], fill=(185, 185, 185), width=1)
        d.text((6, row(y) - 24), f"{y} m", fill=(110, 110, 110), font=FONT)
    for k in range(-5, 6):
        x = W / 2 + k * 10 * pxm
        d.line([(x, 0), (x, H)], fill=(205, 205, 205), width=1)
    im = Image.composite(base, im, Image.eval(base.convert("L"), lambda p: 255 if p < 200 else 0))
    d = ImageDraw.Draw(im)
    for x0 in range(0, W, 24):
        d.line([(x0, row(67)), (x0 + 14, row(67))], fill=(30, 90, 200), width=3)
    d.text((W - 230, row(67) - 28), "67 m deck", fill=(30, 90, 200), font=FONT)
    d.line([(0, row(80)), (W, row(80))], fill=(200, 40, 40), width=2)
    d.text((W - 230, row(80) - 28), "80 m", fill=(200, 40, 40), font=FONT)
    d.text((10, 8), f"Unity {name}", fill=(40, 40, 40), font=FONT)
    tiles.append(im)

sheet = Image.new("RGB", (W * 4, H + 60), "white")
for i, t in enumerate(tiles):
    sheet.paste(t, (i * W, 0))
d = ImageDraw.Draw(sheet)
d.text((10, H + 16), f"Unity 6000.6.9f1, prefab Assets/Game/Units/Mech/Art/Prefabs/MechFrameBalanced.prefab, orthographic. "
       f"Renderer bounds min {v['bounds_min']} max {v['bounds_max']}, height {v['height_m']} m. Red capsule = 1.8 m human.",
       fill=(40, 40, 40), font=FONT)
sheet.save(os.path.join(EV, "unity_4view.png"))

# pixel proxy: front view, 1/5 downscale, quantised to a fixed palette
pal = json.load(open(os.path.join(PROJECT, "research", "R-002", "palettes.json")))
roles = json.load(open(os.path.join(HERE, "palette_roles.json")))["roles"]
hexes = [c["hex"] for c in pal["profiles"]["slate_crimson"]["colours"]] + [r["hex"] for r in roles.values()] + ["#eeeeeb"]
P = np.array([[int(h[i:i + 2], 16) for i in (1, 3, 5)] for h in hexes], float)
im = Image.open(os.path.join(RAW, "unity_front.png")).convert("RGB")
small = np.array(im.resize((W // 5, H // 5), Image.NEAREST)).astype(float)
idx = ((small[:, :, None, :] - P[None, None]) ** 2).sum(-1).argmin(-1)
q = P[idx].astype(np.uint8)
out = Image.fromarray(q).resize((W // 5 * 5, H // 5 * 5), Image.NEAREST)
out.save(os.path.join(EV, "unity_pixel_proxy.png"))
used = np.unique(idx)
json.dump({"downscale": 5, "palette": hexes, "colours_used": [hexes[i] for i in used],
           "note": "nearest-neighbour 1/5 downscale of raw/unity_front.png, nearest-colour quantise in RGB"},
          open(os.path.join(EV, "unity_pixel_proxy.json"), "w"), indent=1)
print("ok", v["height_m"], len(used))
