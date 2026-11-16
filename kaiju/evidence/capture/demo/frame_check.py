"""T-203 frame checks on the demo player frames (PNG sequence) and the encoded videos.
Non-blank, pixel grid (T-148 scale.blocks_uniform at the integer scale), temporal motion, M-124 fidelity of each encode vs the frames.
Run: /opt/miniconda3/envs/kaiju/bin/python frame_check.py --frames DIR --videos a.mp4,b.mp4 --out checks.json [--scale 4]
"""
import argparse, glob, json, os, sys
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.abspath(os.path.join(HERE, "..", "..", "..", "Pipelines", "capture")))
from capture_tools import scale as sc            # noqa: E402
from capture_tools import fidelity as fid        # noqa: E402


def load(p):
    return np.asarray(Image.open(p).convert("RGB"))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--frames", required=True); ap.add_argument("--videos", default=""); ap.add_argument("--out", required=True)
    ap.add_argument("--no-fid", action="store_true"); ap.add_argument("--scale", type=int, default=4); ap.add_argument("--grid-every", type=int, default=30); ap.add_argument("--fid-every", type=int, default=45)
    a = ap.parse_args()
    files = sorted(glob.glob(os.path.join(a.frames, "f*.png")))
    n = len(files); res = {"frames": n, "scale": a.scale}
    first = load(files[min(n - 1, 30)]); H, W = first.shape[:2]; res["size"] = [W, H]
    # grid offset from one frame: best of s x s offsets
    best = max(((sc.blocks_uniform(first, a.scale, (ox, oy)), ox, oy) for ox in range(a.scale) for oy in range(a.scale)))
    off = (best[1], best[2]); res["grid_offset"] = list(off)
    blank, rows, diffs, prev = [], [], [], None
    lum = []
    for i, f in enumerate(files):
        im = load(f)
        l = im.astype(np.float32) @ np.array([0.2126, 0.7152, 0.0722], np.float32)
        std = float(l.std()); uniq = int(len(np.unique(im.reshape(-1, 3)[::7], axis=0)))
        lum.append(float(l.mean()))
        if std < 3.0: blank.append(i)   # palette-limited look: few colours is normal, so only a flat frame counts as blank
        if prev is not None: diffs.append(float((im != prev).any(-1).mean()))
        prev = im
        if i % a.grid_every == 0:
            bo = max((sc.blocks_uniform(im, a.scale, (ox, oy)), ox, oy) for ox in range(a.scale) for oy in range(a.scale))   # the present shift differs per frame: best of s*s offsets
            rows.append({"frame": i, "blocks_uniform": bo[0], "offset": [bo[1], bo[2]], "blocks_uniform_fixed_offset": sc.blocks_uniform(im, a.scale, off), "luma_mean": round(float(l.mean()), 2), "luma_std": round(std, 2), "unique_colours_sample": uniq})
    res["non_blank"] = {"blank_frames": blank, "pass": not blank, "luma_mean_min": round(min(lum), 2), "luma_mean_max": round(max(lum), 2)}
    bu = [r["blocks_uniform"] for r in rows]
    res["pixel_grid"] = {"check": "share of %dx%d blocks that are one colour (1.0 = true integer nearest-neighbour)" % (a.scale, a.scale), "sampled": len(rows),
                         "min": min(bu), "mean": float(np.mean(bu)), "pass_ge_0.999": min(bu) >= 0.999, "rows": rows}
    d = np.array(diffs)
    res["motion"] = {"mean_changed_pixel_share": float(d.mean()), "frames_identical_to_previous": int((d == 0).sum()), "p95": float(np.percentile(d, 95)), "max": float(d.max())}
    res["fidelity"] = {}
    idx = list(range(0, n, a.fid_every))
    for v in [x for x in a.videos.split(",") if x and not a.no_fid]:
        dec = fid.decode_frames(v, idx, W, H)
        pairs = [(i, load(files[i]), dec[k]) for k, i in enumerate(idx)]
        r = fid.evaluate(pairs, min_frames=min(60, len(pairs)))
        res["fidelity"][os.path.basename(v)] = {"summary": r["summary"], "pass_M124": r["pass"], "reason": r["reason"], "frames_sampled": len(pairs)}
    json.dump(res, open(a.out, "w"), indent=1)
    print(json.dumps({k: (v if k in ("frames", "size", "grid_offset") else {kk: vv for kk, vv in v.items() if kk != "rows"} if isinstance(v, dict) else v) for k, v in res.items()}, indent=1)[:3500])


if __name__ == "__main__":
    main()
