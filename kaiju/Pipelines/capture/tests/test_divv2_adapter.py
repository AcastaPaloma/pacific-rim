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


def test_presented_labels_follow_shader_rule_with_whole_texel_shifts():
    """Each slab shifted by its own whole output pixels (incl. >= k): the presented label is the slab the shader samples."""
    sc = copy.deepcopy(SC)
    sc["layers"]["shift_output_px"] = [[5, -2], [-3, 1], [0, 4]]
    h, w = 24, 40
    lab_int = np.full((h, w), 1, np.int16); lab_int[:, :10] = 0; lab_int[:, 30:] = 2   # near | main | far columns
    lab = A.presented_labels(lab_int, sc, (w * K, h * K))
    # slab 0 (sx=5): output col c samples texel floor((c+5)/K); owned (label 0) while that is < 10
    assert lab[0, 0] == 0 and lab[0, 10 * K - 5 - 1] == 0 and lab[0, 10 * K - 5] != 0
    # main (sx=-3) takes over where slab 0 is not owned: texel floor((c-3)/K) in [10,30)
    c = 10 * K - 5
    assert lab[0, c] == (1 if 10 <= (c - 3) // K < 30 else -1)
    # far (sx=0) owns from col 30*K; slab 1 does not own there
    assert lab[8, 30 * K + 6] == 2 and lab[8, w * K - 20] == 2
    # no pixel is mislabelled against the shader rule on the interior rows
    ty = lambda r, sy: (r - sy) // K
    for r in (8, 20, 40):
        for cc in range(0, w * K, 7):
            exp = -1
            for i, (sx, sy) in enumerate(sc["layers"]["shift_output_px"]):
                t, u = (cc + sx) // K, ty(r, sy)
                if 0 <= t < w and 0 <= u < h and lab_int[u, t] == i: exp = i; break
                if 0 <= t < w and 0 <= u < h and lab_int[u, t] < i: break
            if r < h * K and lab[r, cc] != -1:
                assert lab[r, cc] == exp


def test_gap_fill_ambiguity_only_in_clip_overlap_at_label_edges():
    h, w = 20, 30
    lab = np.ones((h, w), np.int16); lab[:, :15] = 0
    b0 = SC["layers"]["boundaries_radial_m"][0]
    lo1, hi0 = SC["layers"]["slab_clip_eye_m"][1][0], SC["layers"]["slab_clip_eye_m"][0][1]
    assert lo1 < b0 < hi0                                         # the real clip ranges overlap around the boundary
    rad_in = np.full((h, w), (lo1 + hi0) / 2 * 1.01)               # inside the overlap everywhere
    cam = dict(CAM, hfov_deg=1.0, vfov_deg=1.0)                   # rays ~ 1: eye ~ radial
    amb = A.gap_fill_ambiguous(lab, rad_in, SC, cam)
    assert amb[:, 14].all() and amb[:, 15].all() and not amb[:, 5].any() and not amb[:, 25].any()
    far = np.full((h, w), hi0 * 2.0)                              # outside the overlap: nothing ambiguous
    assert not A.gap_fill_ambiguous(lab, far, SC, cam).any()


def test_style_metric_ok_rule_t196e():
    from capture_tools import style
    rep = {"submeasures": [{"id": "M-003.d-iv", "kind": "gating", "status": "FAIL"}, {"id": "M-003.a.output", "kind": "gating", "status": "PASS"}]}
    m = {"exit": 1, "report": rep}
    assert not style.metric_ok(m)                                   # unlayered: v1 verdict stands
    assert style.metric_ok({**m, "d_iv_v2": {"ok": True}})          # layered, v2 ok, only d-iv@v1 failing
    assert not style.metric_ok({**m, "d_iv_v2": {"ok": False}})
    rep["submeasures"][1]["status"] = "FAIL"
    assert not style.metric_ok({**m, "d_iv_v2": {"ok": True}})      # another gate failing: unchanged
    assert style.metric_ok({"exit": 0, "report": rep})


def test_layered_nn_match_t196f():
    """M-123 relation for layered frames (T-196f): every decidable pixel is a nearest-neighbour sample of the internal image under a slab shift; a
    filtered frame is not; a single present_shift would not describe the frame."""
    sc = copy.deepcopy(SC); sc["layers"]["shift_output_px"] = [[-2, 1], [-1, -1], [0, -3]]
    ci = _tex(5)
    depth = _depth_png([60.0, 800.0, 20000.0])
    lab = A.adapt(sc, depth)["labels"]
    out = np.zeros((OH, OW, 3), np.uint8)
    for i, (sx, sy) in enumerate(sc["layers"]["shift_output_px"]):
        ty = np.clip(np.floor_divide(np.arange(OH) - sy, K), 0, IH - 1)
        tx = np.clip(np.floor_divide(np.arange(OW) + sx, K), 0, IW - 1)
        out[lab == i] = ci[ty][:, tx][lab == i]
    m, own, undec = A.layered_nn_match(sc, depth, ci, out)
    assert m == 1.0 and own == 1.0 and 0.0 <= undec < 0.05
    assert scale.matches_shifted_nn_of(out, ci, K, (-1, -1)) < 1.0
    soft = out.astype(np.float32); soft = ((soft + np.roll(soft, 1, 1) + np.roll(soft, 1, 0)) / 3).astype(np.uint8)
    assert A.layered_nn_match(sc, depth, ci, soft)[0] < 0.9


def test_slab_id_labels_decode_t196g():
    """Slab-id channel code: 1 + slab, +4 gap rule, +8 clamped at the guard band; 255 / 0 = no information."""
    code = np.array([[1, 2, 3, 255, 0], [5, 6, 7, 1 + 8, 3 + 4 + 8]], np.uint8)
    lab, gap, clamped = A.slab_id_labels(code)
    assert lab.tolist() == [[0, 1, 2, -1, -1], [0, 1, 2, 0, 2]]
    assert gap.tolist() == [[False] * 5, [True, True, True, False, True]]
    assert clamped.tolist() == [[False] * 5, [False, False, False, True, True]]


def test_gap_fill_block_is_named_not_guessed_t196g():
    """T-196f finding: a gap-filled texel shows one slab's own colour where the composite shows another, so it matches no internal nearest-neighbour sample
    and the depth-derived label cannot say why. With the renderer's slab-id channel the block is named (gap) and excluded from the M-123 comparison, every
    other pixel still compared exactly; without the channel the same frame fails, and a stretched frame still fails with it (negative control)."""
    sc = copy.deepcopy(SC); sc["layers"]["shift_output_px"] = [[-2, 1], [-1, -1], [0, -3]]
    ci = _tex(5)
    depth = _depth_png([60.0, 800.0, 20000.0])
    lab = A.adapt(sc, depth)["labels"]
    out = np.zeros((OH, OW, 3), np.uint8)
    for i, (sx, sy) in enumerate(sc["layers"]["shift_output_px"]):
        ty = np.clip(np.floor_divide(np.arange(OH) - sy, K), 0, IH - 1)
        tx = np.clip(np.floor_divide(np.arange(OW) + sx, K), 0, IW - 1)
        out[lab == i] = ci[ty][:, tx][lab == i]
        if i == 1: out[lab == -1] = ci[ty][:, tx][lab == -1]          # pixels the depth guess left undecided: the renderer says slab 1
    code = np.where(lab >= 0, lab + 1, 255).astype(np.uint8)
    code[lab == -1] = 1 + 1                                           # undecided-by-depth pixels named as slab 1 (exact labels everywhere)
    ys, xs = np.nonzero(lab == 0); y0, x0 = ys[len(ys) // 2], xs[len(xs) // 2]
    y0, x0 = y0 - y0 % K, x0 - x0 % K
    out[y0:y0 + 3, x0:x0 + 4] = (out[y0, x0] ^ 0x5A)                  # a 4x3 texel no slab's nearest-neighbour sample reproduces
    code[y0:y0 + 3, x0:x0 + 4] = 1 + 0 + 4                            # the renderer says: gap fill, slab 0
    assert A.layered_nn_match(sc, depth, ci, out)[0] < 1.0
    m, own, undec = A.layered_nn_match(sc, depth, ci, out, slab_id=code)
    assert m == 1.0 and undec > 11 / (OH * OW)
    soft = out.astype(np.float32); soft = ((soft + np.roll(soft, 1, 1) + np.roll(soft, 1, 0)) / 3).astype(np.uint8)
    assert A.layered_nn_match(sc, depth, ci, soft, slab_id=code)[0] < 0.9


def test_slab_id_channel_gives_exact_labels_to_divv2_t196g():
    """With the channel, evaluate_frame uses the renderer's labels: no pixel is undecided, so the seam is only block-boundary pixels."""
    sc = copy.deepcopy(SC); sc["layers"]["shift_output_px"] = [[0, 0], [0, 0], [0, 0]]
    depth = _depth_png([60.0, 800.0, 20000.0])
    out = np.repeat(np.repeat(_tex(9), K, 0), K, 1)[:OH, :OW]
    code = np.full((OH, OW), 1 + 0, np.uint8)
    a = A.adapt(sc, depth, (OW, OH), slab_id=code)
    assert (a["labels"] == 0).all() and not a["gap"].any()
    r = A.evaluate_frame(sc, depth, out, slab_id=code)
    assert r["version"] == "v2" and r["frame"] >= 0.995
