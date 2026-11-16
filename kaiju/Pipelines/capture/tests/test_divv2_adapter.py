import copy
import json
import os

import numpy as np

from capture_tools import divv2_adapter as A, scale

FIX = os.path.join(os.path.dirname(__file__), "fixtures", "real_sidecar_p1_d_strafe_f50.json")
SC = json.load(open(FIX))   # real sidecar (p1_d_strafe_t192 frame 50, M3LayeredSharedPhase)
CAM = SC["camera"]; K = SC["resolution"]["integer_scale"]
IW, IH = SC["resolution"]["internal_wh"]; OW, OH = SC["resolution"]["output_wh"]


def _depth_png(z_rows):
    """png16_log depth with the real encoding: three horizontal bands at eye depths z_rows (m, forward axis)."""
    v = np.zeros((IH, IW))
    n = len(z_rows)
    for i, z in enumerate(z_rows):
        v[i * IH // n:(i + 1) * IH // n] = 65535 * np.log(z / CAM["near_m"]) / np.log(CAM["far_m"] / CAM["near_m"])
    return np.round(v).astype(np.uint16)


def test_real_sidecar_fields_and_declared():
    assert SC["layers"]["count"] == 3 and len(SC["layers"]["boundaries_radial_m"]) == 2
    d = A.declared_offsets(SC)
    assert d == {0: (1, 3), 1: (1, 3), 2: (1, 3)}                       # shift (-1,-1) -> ((-sx)%4, sy%4)
    assert d[1] == tuple(SC["resolution"]["output_block_offset_px"])    # main slab == the sidecar's frame offset


def test_decode_depth_roundtrip():
    z, sky = A.decode_depth(_depth_png([50.0, 500.0, 5000.0]), CAM)
    assert abs(z[0, 0] - 50.0) / 50 < 1e-3 and abs(z[-1, 0] - 5000.0) / 5000 < 1e-3 and not sky.any()
    z2, sky2 = A.decode_depth(np.full((2, 2), 65535, np.uint16), CAM)
    assert sky2.all()


def test_labels_follow_radial_boundaries_and_sky():
    b = SC["layers"]["boundaries_radial_m"]
    lab = A.adapt(SC, _depth_png([60.0, 800.0, 20000.0]))["lab_internal"]
    assert lab[0, IW // 2] == 0 and lab[IH // 2, IW // 2] == 1 and lab[-1, IW // 2] == 2
    sky = np.full((IH, IW), 65535, np.uint16)
    assert (A.adapt(SC, sky)["lab_internal"] == 2).all() and b[0] < b[1]


def _frame(sc, textures):
    out = np.zeros((OH, OW, 3), np.uint8)
    a = A.adapt(sc, _depth_png([60.0, 800.0, 20000.0]))
    for s, (ox, oy) in a["declared"].items():
        up = np.roll(scale.upscale_nn(textures[s], K), (oy, ox), (0, 1))
        m = a["labels"] == s
        out[m] = up[m]
        if s == 1:   # seam/gap pixels (label -1) show a real palette colour of the main slab, as a real frame does
            out[a["labels"] < 0] = up[a["labels"] < 0]
    return out, a


def _tex(seed):
    pal = np.random.default_rng(7).integers(0, 256, (16, 3), dtype=np.uint8)
    return pal[np.random.default_rng(seed).integers(0, 16, (IH, IW))]


def test_layered_frame_with_different_slab_offsets_passes_per_slab():
    sc = copy.deepcopy(SC); sc["layers"]["shift_output_px"] = [[-2, 1], [-1, -1], [0, -3]]
    out, a = _frame(sc, [_tex(1), _tex(2), _tex(3)])
    assert a["declared"] == {0: (2, 1), 1: (1, 3), 2: (0, 1)}
    r = A.evaluate_frame(sc, _depth_png([60.0, 800.0, 20000.0]), out)
    assert r["version"] == "v2" and r["ok"], r["failures"]
    assert min(v["score"] for v in r["slabs"].values()) >= 0.995 and r["eval_area"] > 0.95


def test_bilinear_slab_fails_through_adapter():
    sc = copy.deepcopy(SC)
    out, a = _frame(sc, [_tex(1), _tex(2), _tex(3)])
    m = a["labels"] == 1
    soft = out.astype(np.float32); soft = (soft + np.roll(soft, 1, 1) + np.roll(soft, 1, 0)) / 3
    out[m] = soft.astype(np.uint8)[m]
    r = A.evaluate_frame(sc, _depth_png([60.0, 800.0, 20000.0]), out)
    assert not r["ok"] and r["slabs"][1]["score"] < 0.995


def test_unlayered_sidecar_uses_v1_reading():
    sc = copy.deepcopy(SC); sc["layers"] = None
    out = np.repeat(np.repeat(_tex(5), K, 0), K, 1)
    out = np.roll(out, (sc["resolution"]["output_block_offset_px"][1], sc["resolution"]["output_block_offset_px"][0]), (0, 1))
    r = A.evaluate_frame(sc, _depth_png([60.0]), out)
    assert r["version"] == "v1" and r["ok"]
