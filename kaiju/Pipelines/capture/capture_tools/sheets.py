"""Contact sheets: nearest-neighbour thumbnails only (no smoothing) on a grid, with a per-pillar coverage tally."""
from __future__ import annotations

import glob
import os
import re

from PIL import Image, ImageDraw


def thumb(im: Image.Image, w: int) -> Image.Image:
    h = max(1, round(im.height * w / im.width))
    return im.convert("RGB").resize((w, h), Image.NEAREST)


def contact_sheet(paths, out, cols=4, thumb_w=480, pad=8, labels=True):
    if not paths:
        raise ValueError("no frames")
    ts = [thumb(Image.open(p), thumb_w) for p in paths]
    th = max(t.height for t in ts)
    rows = -(-len(ts) // cols)
    lab = 14 if labels else 0
    sheet = Image.new("RGB", (cols * (thumb_w + pad) + pad, rows * (th + lab + pad) + pad), (24, 24, 24))
    d = ImageDraw.Draw(sheet)
    for i, (p, t) in enumerate(zip(paths, ts)):
        x, y = pad + (i % cols) * (thumb_w + pad), pad + (i // cols) * (th + lab + pad)
        sheet.paste(t, (x, y + lab))
        if labels:
            d.text((x, y), os.path.basename(p)[:60], fill=(220, 220, 220))
    sheet.save(out)
    return {"out": out, "frames": len(paths), "size": sheet.size}


def coverage(paths, pillar_re=r"(P[1-5])"):
    """Frames per pillar parsed from file names (P1..P5)."""
    tally = {f"P{i}": 0 for i in range(1, 6)}
    other = 0
    for p in paths:
        m = re.search(pillar_re, os.path.basename(p))
        if m and m.group(1) in tally:
            tally[m.group(1)] += 1
        else:
            other += 1
    return {"per_pillar": tally, "unassigned": other, "missing_pillars": [k for k, v in tally.items() if v == 0]}


def run(frames_dir, out, pattern="*.png", every=1, **kw):
    paths = sorted(glob.glob(os.path.join(frames_dir, pattern)))[::max(1, every)]
    r = contact_sheet(paths, out, **kw)
    r["coverage"] = coverage(paths)
    return r
