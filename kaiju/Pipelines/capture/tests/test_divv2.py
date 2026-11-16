import json
import os

import numpy as np
from PIL import Image

from capture_tools import afc2, divv2, scale

S = 4
H, W = 384, 96
BOUNDS = [(0, 100), (100, 1000), (1000, 1e5)]
DEPTHS = [50.0, 500.0, 5000.0]
OFFS = {0: (0, 0), 1: (2, 1), 2: (1, 3)}


def _depth(rows=None):
    d = np.zeros((H, W))
    rows = rows or [(0, 128), (128, 256), (256, 384)]
    for k, (a, b) in enumerate(rows):
        d[a:b] = DEPTHS[k]
    return d


def _tex(k, seed=0):
    rng = np.random.default_rng(100 * seed + k)
    pal = np.random.default_rng(7).integers(0, 256, (16, 3), dtype=np.uint8)   # limited palette, as in the real style
    return pal[rng.integers(0, 16, (H // S + 2, W // S + 2))]


def _slab_img(k, off, seed=0):
    up = scale.upscale_nn(_tex(k, seed), S)[:H, :W]
    return np.roll(up, (off[1], off[0]), (0, 1))


def _frame(offs, labels, seed=0, per_slab=lambda k, im: im):
    out = np.zeros((H, W, 3), np.uint8)
    for k in range(3):
        out[labels == k] = per_slab(k, _slab_img(k, offs[k], seed))[labels == k]
    return out


def _labels():
    return divv2.slab_labels(_depth(), BOUNDS)


def test_labels_from_depth():
    lab = _labels()
    assert (lab[:128] == 0).all() and (lab[128:256] == 1).all() and (lab[256:] == 2).all()


def test_two_lattices_v1_low_v2_perfect():
    lab = _labels(); out = _frame(OFFS, lab)
    assert scale.blocks_uniform(out, S, (0, 0)) < 0.6          # v1 with one offset
    r = divv2.frame_ok(out, S, lab, OFFS)
    assert r["version"] == "v2" and r["frame"] == 1.0 and r["ok"], r["failures"]
    assert all(v["score"] == 1.0 and v["blind_estimate_ok"] for v in r["slabs"].values())
    assert r["eval_area"] >= 0.95 and r["seam_adherence"] == 1.0


def test_bilinear_slab_fails():
    lab = _labels()
    def blur(k, im):
        if k != 1: return im
        f = im.astype(float); g = (f + np.roll(f, 1, 1) + np.roll(f, 1, 0) + np.roll(np.roll(f, 1, 0), 1, 1)) / 4
        return g.astype(np.uint8)
    out = _frame(OFFS, lab, per_slab=blur)
    r = divv2.frame_ok(out, S, lab, OFFS)
    assert not r["ok"] and r["slabs"][1]["score"] < 0.995 and r["slabs"][0]["score"] == 1.0


def test_wrong_declared_offset_fails_blind_estimate():
    lab = _labels(); out = _frame(OFFS, lab)
    bad = dict(OFFS); bad[1] = (0, 0)
    r = divv2.frame_ok(out, S, lab, bad)
    assert not r["ok"] and not r["slabs"][1]["blind_estimate_ok"]
    assert r["slabs"][1]["best_offsets"] == [(2, 1)]
    assert any("blind estimate" in f for f in r["failures"])


def test_seam_area_cap():
    stripes = [(i, i + 6) for i in range(0, 384, 6)]
    d = np.zeros((H, W))
    for i, (a, b) in enumerate(stripes): d[a:b] = DEPTHS[i % 3]
    lab = divv2.slab_labels(d, BOUNDS); out = _frame(OFFS, lab)
    r = divv2.frame_ok(out, S, lab, OFFS)
    assert r["eval_area"] < 0.95 and not r["ok"] and any("seam cap" in f for f in r["failures"])


def test_renderer_labels_must_agree_with_capture():
    lab = _labels(); out = _frame(OFFS, lab)
    rl = lab.copy(); rl[40:60] = 2                       # renderer mislabels a mid-slab band
    assert not divv2.frame_ok(out, S, lab, OFFS, renderer_labels=rl)["ok"]
    rl = lab.copy(); rl[126:130] = rl[130]               # within one block of the boundary: tolerated
    assert divv2.frame_ok(out, S, lab, OFFS, renderer_labels=rl)["ok"]


def test_seam_grid_tearing_fails():
    lab = _labels(); out = _frame(OFFS, lab)
    res = divv2.d_iv_v2(out, S, lab, OFFS)
    seam = ~res["_cover"] & ~divv2._interior(lab, S)   # seam at the slab edges (the image-edge strips are slab content, not a seam)
    assert seam.any()
    torn = out.copy(); torn[seam] = (255, 0, 255)         # seam pixels not from either slab's palette
    r = divv2.frame_ok(torn, S, lab, OFFS)
    assert r["seam_adherence"] < 1.0 and not r["ok"] and any("grid tearing" in f for f in r["failures"])
    assert r["frame"] == 1.0                              # only the seam check catches it


def test_unlayered_frame_is_v1_whole_frame():
    out = scale.upscale_nn(_tex(0)[:H // S, :W // S], S)
    r = divv2.d_iv_v2(out, S)
    assert r["version"] == "v1" and r["frame"] == scale.blocks_uniform(out, S) == 1.0 and r["ok"]
    bl = (out.astype(float) + np.roll(out.astype(float), 1, 1)).__truediv__(2).astype(np.uint8)
    assert not divv2.d_iv_v2(bl, S)["ok"]


def test_negative_control_nearest_vs_resampled_layered():
    lab = _labels(); good = _frame(OFFS, lab)
    f = good.astype(float)
    shifted = ((f + np.roll(f, 1, 1)) / 2).astype(np.uint8)    # whole-frame resample
    assert divv2.frame_ok(good, S, lab, OFFS)["ok"] and not divv2.frame_ok(shifted, S, lab, OFFS)["ok"]


def test_clip_value_is_min_over_frames_and_slabs():
    lab = _labels(); a = divv2.frame_ok(_frame(OFFS, lab, 0), S, lab, OFFS); b = divv2.frame_ok(_frame(OFFS, lab, 1), S, lab, OFFS)
    c = divv2.clip_value([a, b]); assert c["ok"] and c["gate_value"] == 1.0
    assert divv2.METRIC["threshold"] == 0.995


def test_afc2_pairs(tmp_path):
    A, B = tmp_path / "a", tmp_path / "b"; A.mkdir(); B.mkdir()
    for d, v in ((A, 10), (B, 200)):
        Image.fromarray(np.full((8, 8, 3), v, np.uint8)).save(d / "f1.png")
    Image.fromarray(np.zeros((8, 8, 3), np.uint8)).save(A / "only_a.png")
    key = afc2.render_pairs(str(A), str(B), str(tmp_path / "o"), seed=3, zoom=2)
    assert list(key) == ["f1.png"]
    im = np.array(Image.open(tmp_path / "o" / "f1.png")); assert im.shape == (16, 40, 3)
    left = key["f1.png"]["left"]; assert im[0, 0, 0] == (10 if left == str(A) else 200)
    assert json.load(open(tmp_path / "o" / "key.json"))["seed"] == 3
