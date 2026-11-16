"""2AFC pair tool (P-3 of R-M3-DIV-review): side-by-side per-slab vs shared-phase frames for later human viewing.
Usage: python -m capture_tools.afc2 <dirA> <dirB> <out_dir> [--seed N] [--zoom Z]
Pairs frames by file name (png) present in both dirs; for each pair writes <name>.png with A/B randomly placed
left/right (nearest-neighbour zoom) and key.json mapping left/right -> dir. Viewer must not see key.json."""
from __future__ import annotations

import argparse
import json
import os
import random

import numpy as np
from PIL import Image


def pair_names(a, b):
    sa = {f for f in os.listdir(a) if f.lower().endswith(".png")}
    return sorted(sa & {f for f in os.listdir(b) if f.lower().endswith(".png")})


def render_pairs(dir_a, dir_b, out_dir, seed=0, zoom=1, gap=8):
    os.makedirs(out_dir, exist_ok=True)
    rng = random.Random(seed)
    key = {}
    for n in pair_names(dir_a, dir_b):
        ia, ib = (np.array(Image.open(os.path.join(d, n)).convert("RGB")) for d in (dir_a, dir_b))
        if ia.shape != ib.shape:
            raise ValueError(f"{n}: frame sizes differ {ia.shape} vs {ib.shape}")
        swap = rng.random() < 0.5
        l, r = (ib, ia) if swap else (ia, ib)
        if zoom > 1:
            l, r = (np.repeat(np.repeat(x, zoom, 0), zoom, 1) for x in (l, r))
        canvas = np.full((l.shape[0], l.shape[1] * 2 + gap, 3), 128, np.uint8)
        canvas[:, :l.shape[1]] = l; canvas[:, l.shape[1] + gap:] = r
        Image.fromarray(canvas).save(os.path.join(out_dir, n))
        key[n] = {"left": dir_b if swap else dir_a, "right": dir_a if swap else dir_b}
    with open(os.path.join(out_dir, "key.json"), "w") as f:
        json.dump({"seed": seed, "pairs": key}, f, indent=1)
    return key


def main():
    ap = argparse.ArgumentParser(); ap.add_argument("a"); ap.add_argument("b"); ap.add_argument("out")
    ap.add_argument("--seed", type=int, default=0); ap.add_argument("--zoom", type=int, default=1)
    x = ap.parse_args(); print(len(render_pairs(x.a, x.b, x.out, x.seed, x.zoom)), "pairs")


if __name__ == "__main__":
    main()
