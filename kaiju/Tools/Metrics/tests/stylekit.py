"""Test helpers for style fixtures."""
import json
import os

import numpy as np
from PIL import Image
from skimage.color import rgb2lab

from fixtures import gen_style as GS
from kaiju_metrics.runner import run_metric


def pass_set(tmp_path, name="m003_pass", **kw):
    sd, truth = GS.m003_pass_set(str(tmp_path), set_id=name, **kw)
    return sd, truth


def run3(sd, profiles="slate_teal", palettes=None, ratings=None, label=None, out=None, images=False):
    opts = {"profiles": profiles}
    if palettes:
        opts["palettes"] = palettes
    rep, _ = run_metric("m003", [sd], out_dir=out, label=label, ratings=ratings, images=images, options=opts)
    return rep


def sm(rep, sid):
    return next(s for s in rep["submeasures"] if s["id"] == sid)


def failing(rep, kinds=("gating",)):
    return sorted(s["id"] for s in rep["submeasures"] if s["status"] == "FAIL" and s["kind"] in kinds)


def hex_L(h):
    return float(rgb2lab(GS.hex_rgb(h)[None, None, :] / 255.0)[0, 0, 0])


def frames(sd, seq="gameplay"):
    d = os.path.join(sd, seq)
    return d, sorted(int(f[1:7]) for f in os.listdir(d) if f.endswith(".json") and f.startswith("f"))


def m003_ratings(path, build, n_raters=8, mean=8.5, profile="slate_teal", n_caps=12, neg_anchor=2):
    raters = [{"rater_id": f"r{i}", "rater_type": "agent", "blind": True} for i in range(n_raters)]
    rows = []
    for i in range(n_raters):
        for c in range(n_caps):
            rows.append({"rater_id": f"r{i}", "build": build, "capture": f"c{c}", "profile": profile, "score": mean})
        for a in range(2):
            rows.append({"rater_id": f"r{i}", "build": build, "capture": f"pos{a}", "anchor": "positive", "score": 9})
            rows.append({"rater_id": f"r{i}", "build": build, "capture": f"neg{a}", "anchor": "negative",
                         "score": neg_anchor})
    with open(path, "w") as f:
        json.dump({"schema": "panel_ratings@v1", "metric": "M-003", "raters": raters, "ratings": rows}, f)
    return path


def load_png(p):
    return np.array(Image.open(p))
