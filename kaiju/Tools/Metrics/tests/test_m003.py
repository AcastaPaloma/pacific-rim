"""M-003@v1 (R-002 §4): known answers (C3), boundary pairs and negative controls (C4), R-002 control tables,
verdict rules (C2), diagnostics (C12). Truth: exact palette shares / painted bands and boxes (L* of the hex via
scikit-image), or the generator's own construction."""
import json
import math
import os

import numpy as np
import pytest
from PIL import Image
from skimage.color import lab2rgb, rgb2lab

from fixtures import gen_style as GS
from stylekit import failing, frames, hex_L, m003_ratings, pass_set, run3, sm

W, H, S = 160, 90, 4
SLATE, ROLES, LABS = GS.load_profile("slate_teal")


def ramp_hex(k):
    return SLATE[k]


def _frames_rows(rep):
    return {r["frame"].split("/")[-1]: r for r in rep["extra"]["frames"]}


# ------------------------------------------------------------------ pass set and verdicts
def test_M003_pass_set_headless_pass(tmp_path):
    sd, _ = pass_set(tmp_path)
    rep = run3(sd)
    assert failing(rep, ("gating", "advisory")) == [], failing(rep, ("gating", "advisory"))
    assert rep["verdict"] == "INCOMPLETE" and sm(rep, "M-003.f")["status"] == "PENDING"
    assert rep["headless_verdict"] == "PASS"


def test_M003_pass_set_with_panel_pass(tmp_path):
    sd, _ = pass_set(tmp_path)
    r = m003_ratings(str(tmp_path / "r.json"), "m003_pass")
    rep = run3(sd, ratings=r)
    assert rep["verdict"] == "PASS" and rep["exit_code"] == 0 and rep["verdict_label"].startswith("PASS (provisional")


def test_M003_too_few_frames_incomplete(tmp_path):
    sd = os.path.join(str(tmp_path), "few")
    GS.m003_gameplay_frames(sd, n_frames=6)
    GS.m003_haze_cal(sd)
    rep = run3(sd)
    assert rep["verdict"] == "INCOMPLETE" and any("slate_teal: 6" in m for m in rep["coverage"]["missing"])


def test_M003_missing_haze_cal_incomplete(tmp_path):
    sd = os.path.join(str(tmp_path), "nohaze")
    GS.m003_gameplay_frames(sd)
    rep = run3(sd)
    assert sm(rep, "M-003.c2")["status"] == "NOT_RUN" and rep["headless_verdict"] == "INCOMPLETE"


# ------------------------------------------------------------------ (a) palette adherence
def test_M003_a_known_share(tmp_path):
    sd, truth = pass_set(tmp_path, off_palette_share=0.03)
    rep = run3(sd)
    exp = 1 - round(0.03 * W * H) / (W * H)
    assert abs(sm(rep, "M-003.a.output")["value"] - exp) < 1e-9
    assert abs(sm(rep, "M-003.a.internal")["value"] - exp) < 1e-9
    assert sm(rep, "M-003.a.A3")["status"] == "REPORTED" and sm(rep, "M-003.a.A5")["value"] <= 1.0


@pytest.mark.parametrize("bad,exp", [(1 - 0.95 * 0.98, "FAIL"), (1 - 0.95 * 1.02, "PASS")])
def test_M003_a_output_boundary(tmp_path, bad, exp):
    sd, _ = pass_set(tmp_path, off_palette_share=bad)
    assert sm(run3(sd), "M-003.a.output")["status"] == exp


@pytest.mark.parametrize("f,exp", [(0.98, "PASS"), (1.02, "FAIL")])
def test_M003_a_internal_boundary(tmp_path, f, exp):
    """Threshold 0.995 share: boundary on the failing share 0.005 (0.49 % pass / 0.51 % fail)."""
    sd, _ = pass_set(tmp_path, off_palette_share=0.005 * f)
    assert sm(run3(sd), "M-003.a.internal")["status"] == exp


def test_M003_a_controls_unquantised_and_bilinear(tmp_path):
    """R-002 controls (slate_teal, synthetic scene, 320 x 180): unquantised render A_2 = 0.847 (A_5 0.998) and a
    bilinear upscale to output A_2 = 0.871. Same scene construction as the research stand-in."""
    from kaiju_metrics.colour import nearest_palette
    lab = np.array([l for l, r in zip(LABS, ROLES) if r.startswith("ramp_")])
    lab = lab[np.argsort(lab[:, 0])]
    cam = GS.Cam(GS.ORIGIN + np.array([0, 60.0, 0]), 0.0, 4.0, 320, 180)
    p, _, _ = GS.render_city(cam, GS.city_boxes(), GS.ORIGIN, sky_k=0.35)
    x = np.clip(p, 0, 1) * 7; i0 = np.minimum(np.floor(x).astype(int), 6); fr = (x - i0)[..., None]
    cont_lab = lab[i0] * (1 - fr) + lab[i0 + 1] * fr
    from kaiju_metrics.colour import ciede2000
    full = np.array(LABS)
    d = ciede2000(cont_lab.reshape(-1, 1, 3), full[None]).min(1)
    assert abs((d <= 2).mean() - 0.847) < 0.005 and abs((d <= 5).mean() - 0.998) < 0.005
    pal = np.stack([GS.hex_rgb(h) for h in SLATE])
    q = GS.quantise(p, 8, "bayer")
    rgb = pal[[i for i, r in enumerate(ROLES) if r.startswith("ramp_")]][q]
    bil = GS.upscale(rgb, 6, mode="bilinear")
    de, _ = nearest_palette(bil, pal)
    assert abs((de <= 2).mean() - 0.871) < 0.02
    rep_dir = os.path.join(str(tmp_path), "bil")
    GS.m003_gameplay_frames(rep_dir, upscale_mode="bilinear")
    GS.m003_haze_cal(rep_dir)
    rep = run3(rep_dir)
    assert sm(rep, "M-003.a.output")["status"] == "FAIL" and sm(rep, "M-003.d-iv")["status"] == "FAIL"


def test_M003_a_index_mismatch_invalid(tmp_path):
    sd, _ = pass_set(tmp_path)
    d, ks = frames(sd)
    idx = GS.read_index(d, 0)
    idx[0, 0] = 255                       # declared not-quantised, but the colour is on the palette
    Image.fromarray(idx.astype(np.uint8)).save(os.path.join(d, "f000000_index.png"))
    rep = run3(sd)
    assert rep["verdict"] == "INVALID_INPUT" and rep["exit_code"] == 2


def test_M003_a_diagnostics_name_offenders(tmp_path):
    sd, _ = pass_set(tmp_path, off_palette_share=0.08)
    rep = run3(sd, out=str(tmp_path / "out"), images=True)
    dg = sm(rep, "M-003.a.output")["diagnostics"]
    assert dg["worst_offenders"][0]["hex"] == "#00ff00"
    assert dg["worst_offenders"][0]["pixels"] == round(0.08 * W * H) * S * S
    assert os.path.isfile(tmp_path / "out" / dg["heat_map"])


# ------------------------------------------------------------------ (b) dithering
def test_M003_b_reproduces_R002_controls():
    """R-002 §4 b validation table: Bayer 0.70 / O 1.12, white noise 0.58 / 0.06, posterised 0.00, continuous 0.01."""
    from kaiju_metrics.registry import load_registry
    from kaiju_metrics.style import measures as M
    from kaiju_metrics.style.m003 import DITHER_KEYS
    reg = load_registry("M-003")
    P = {k: reg.p(k) for k in DITHER_KEYS}
    lab = np.array(sorted([l for l, r in zip(LABS, ROLES) if r.startswith("ramp_")], key=lambda v: v[0]))
    cam = GS.Cam(GS.ORIGIN + np.array([0, 60.0, 0]), 0.0, 4.0, 320, 180)
    p, z, _ = GS.render_city(cam, GS.city_boxes(), GS.ORIGIN, sky_k=0.35)
    exp = {"bayer": (0.70, 1.12), "noise": (0.58, 0.06), "none": (0.00, None)}
    for mode, (dig, o) in exp.items():
        q = GS.quantise(p, 8, mode)
        dm, _ = M.dither_metrics(q, lab[q][..., 0], P)
        assert abs(dm["DIG"] - dig) <= 0.03, (mode, dm)
        if o is not None:
            assert abs(dm["O"] - o) <= 0.03, (mode, dm)
    fine = np.clip(np.round(p * 63), 0, 63).astype(int)
    xx = np.linspace(0, 1, 64) * 7; i0 = np.minimum(np.floor(xx).astype(int), 6); f = (xx - i0)[:, None]
    fl = (lab[i0] * (1 - f) + lab[i0 + 1] * f)[:, 0]
    dm, _ = M.dither_metrics(fine, fl[fine], P)
    assert abs(dm["DIG"] - 0.01) <= 0.03


@pytest.mark.parametrize("n_none,exp_share,exp", [(2, 10 / 12, "PASS"), (3, 9 / 12, "FAIL")])
def test_M003_b_known_frame_share(tmp_path, n_none, exp_share, exp):
    """Frames are fully posterised ('none', DIG ~ 0) or dithered ('mixed', DIG >= 0.15): the gate statistic is
    known exactly. Boundary at the frame-share threshold 0.80 uses the nearest achievable counts (9 or 10 of 12)."""
    modes = ["none"] * n_none + ["mixed"] * (12 - n_none)
    sd, _ = pass_set(tmp_path, modes=modes)
    s = sm(run3(sd), "M-003.b.DIG")
    assert abs(s["value"] - exp_share) < 1e-9 and s["status"] == exp


def _dig_reference(idx, L, P):
    """Independent brute-force reference of the R-002 §4 b definition (explicit windows, rows/cols -2..+1)."""
    from scipy.ndimage import gaussian_filter
    Hh, Ww = idx.shape
    pad = lambda a, k: np.pad(a, k, mode="edge")
    P1 = pad(idx, 1)
    diff = sum((P1[1 + dy:1 + dy + Hh, 1 + dx:1 + dx + Ww] != idx).astype(float)
               for dy, dx in ((-1, 0), (1, 0), (0, -1), (0, 1))) / 4
    Ip, Lp, Dp = pad(idx, 2), pad(L, 2), pad(diff, 2)
    inter = np.zeros(idx.shape, bool)
    boxL = np.zeros(idx.shape)
    for y in range(Hh):
        for x in range(Ww):
            boxL[y, x] = Lp[y:y + 4, x:x + 4].mean()
    r = L - boxL
    Rp = pad(r, 2)
    for y in range(Hh):
        for x in range(Ww):
            win = Ip[y:y + 4, x:x + 4]
            vals, cnt = np.unique(win, return_counts=True)
            inter[y, x] = (cnt.max() / 16 < 0.9 and len(vals) <= 4 and Dp[y:y + 4, x:x + 4].mean() >= 0.35
                           and np.abs(Rp[y:y + 4, x:x + 4]).mean() >= 1.0)
    Lb = gaussian_filter(L, 3.0)
    gy, gx = np.gradient(Lb)
    g = np.hypot(gx, gy)
    grad = (g >= 0.25) & (g <= 4.0)
    return inter[grad].mean()


def _split_image(Ww, cw):
    seq = [0, 3, 5, 6, 7]
    r = np.arange(48)[:, None]; c = np.arange(Ww)[None, :]
    band = np.minimum(r // 12, 3)
    lo = np.array(seq)[band]; hi = np.array(seq)[band + 1]
    return np.where((c < cw) & ((r + c) % 2 == 1), hi, lo).astype(int)


def test_M003_b_dig_matches_brute_force_reference():
    from kaiju_metrics.registry import load_registry
    from kaiju_metrics.style import measures as M
    from kaiju_metrics.style.m003 import DITHER_KEYS
    reg = load_registry("M-003"); P = {k: reg.p(k) for k in DITHER_KEYS}
    Lp = np.array([l[0] for l in LABS])
    for cw in (0, 31, 64, 120):
        idx = _split_image(160, cw)
        dm, _ = M.dither_metrics(idx, Lp[idx], P)
        assert abs(dm["DIG"] - _dig_reference(idx, Lp[idx], P)) < 1e-12


@pytest.mark.parametrize("target,exp", [(0.15 * 0.98, False), (0.15 * 1.02, True)])
def test_M003_b_dig_boundary_per_frame(target, exp):
    """Per-frame DIG at 98 % / 102 % of 0.15: image chosen by the brute-force reference (closest column split)."""
    from kaiju_metrics.registry import load_registry
    from kaiju_metrics.style import measures as M
    from kaiju_metrics.style.m003 import DITHER_KEYS
    reg = load_registry("M-003"); P = {k: reg.p(k) for k in DITHER_KEYS}
    Lp = np.array([l[0] for l in LABS])
    best = None
    for cw in range(30, 80):
        idx = _split_image(400, cw)
        v = M.dither_metrics(idx, Lp[idx], P)[0]["DIG"]
        if best is None or abs(v - target) < abs(best[0] - target):
            best = (v, idx)
    assert abs(best[0] - target) < 0.0025
    assert (best[0] >= reg.t("M-003.b.DIG", "dig_min")) == exp


def test_M003_b_no_evaluated_frames_not_run(tmp_path):
    sd = os.path.join(str(tmp_path), "flat")
    GS.m003_gameplay_frames(sd)
    GS.m003_haze_cal(sd)
    d, ks = frames(sd)
    for k in ks:
        GS.repaint(d, k, np.full((H, W), [i for i, r in enumerate(ROLES) if r == "ramp_3"][0], np.int32))
    rep = run3(sd)
    assert sm(rep, "M-003.b.DIG")["status"] == "NOT_RUN" and rep["verdict"] != "PASS"


def test_M003_b_dig_max_advisory(tmp_path):
    sd, _ = pass_set(tmp_path, mode="bayer")
    rep = run3(sd)
    assert sm(rep, "M-003.b.DIG_max")["status"] == "FAIL" and "M-003.b.DIG_max" not in failing(rep)
    assert sm(rep, "M-003.b.O")["value"] > 0.5         # Bayer reads as ordered


# ------------------------------------------------------------------ (c1) depth banding, painted
def _paint_bands(sd, stripes, seq="gameplay"):
    """stripes: list of (depth_m, [(ramp_role_index, n_pixels_per_row), ...]) painted as horizontal stripes."""
    d, ks = frames(sd, seq)
    ramp = [i for i, r in enumerate(ROLES) if r.startswith("ramp_")]
    rows = H // len(stripes)
    for k in ks:
        idx = np.full((H, W), ramp[7], np.int32)
        z = np.full((H, W), np.inf)
        for j, (dep, mix) in enumerate(stripes):
            y0, y1 = j * rows, (j + 1) * rows if j < len(stripes) - 1 else H
            row = np.concatenate([np.full(n, ramp[c]) for c, n in mix])
            idx[y0:y1] = row[None, :W]
            z[y0:y1] = dep
        GS.repaint(d, k, idx)
        GS.save_png(os.path.join(d, f"f{k:06d}_depth.png"), GS.encode_depth(z))
    return [[(hex_L(SLATE[ramp[c]]) * n) for c, n in mix] for _, mix in stripes]


def _Lr(k):
    return hex_L(SLATE[[i for i, r in enumerate(ROLES) if r.startswith("ramp_")][k]])


def test_M003_c1_known_painted(tmp_path):
    sd, _ = pass_set(tmp_path)
    _paint_bands(sd, [(30, [(0, W)]), (100, [(2, W)]), (700, [(5, W)]), (3000, [(7, W)])])
    rep = run3(sd)
    exp = [_Lr(0), _Lr(2), _Lr(5), _Lr(7)]
    row = list(_frames_rows(rep).values())[0]["depth"]
    assert row["bands"] == [0, 1, 3, 5] and np.allclose(row["meanL"], exp, atol=1e-9)
    assert abs(row["span"] - (exp[-1] - exp[0])) < 1e-9 and row["rho"] == 1.0
    assert sm(rep, "M-003.c1")["status"] == "PASS"


def _mix_for(target, a, b, n=W):
    """Pixels of ramp a / ramp b in a row of n giving mean L closest to target."""
    La, Lb = _Lr(a), _Lr(b)
    nb = int(round((target - La) / (Lb - La) * n))
    return [(a, n - nb), (b, nb)], (La * (n - nb) + Lb * nb) / n


@pytest.mark.parametrize("f,exp", [(0.98, "FAIL"), (1.02, "PASS")])
def test_M003_c1_span_boundary(tmp_path, f, exp):
    sd, _ = pass_set(tmp_path)
    mix, got = _mix_for(_Lr(0) + f * 15.0, 4, 5)
    _paint_bands(sd, [(30, [(0, W)]), (100, [(2, W)]), (700, mix)])
    rep = run3(sd)
    assert abs(sm(rep, "M-003.c1")["parts"][2]["value"] - (got - _Lr(0))) < 1e-9
    assert sm(rep, "M-003.c1")["parts"][2]["status"] == exp


@pytest.mark.parametrize("f,exp", [(0.98, "PASS"), (1.02, "FAIL")])
def test_M003_c1_drop_boundary(tmp_path, f, exp):
    sd, _ = pass_set(tmp_path)
    mix, got = _mix_for(_Lr(3) - f * 2.0, 1, 3)
    _paint_bands(sd, [(30, [(0, W)]), (100, [(3, W)]), (300, mix), (700, [(6, W)]), (3000, [(7, W)])])
    rep = run3(sd)
    p = next(p for p in sm(rep, "M-003.c1")["parts"] if "drop" in p["name"])
    assert abs(p["value"] - (_Lr(3) - got)) < 1e-9 and p["status"] == exp


def test_M003_c1_rho_boundary_discrete(tmp_path):
    """Spearman over 7 bands is discrete: one adjacent swap gives 0.964 (pass), three give 0.893 (fail)."""
    order_pass = [0, 1, 3, 2, 4, 5, 7]
    sd, _ = pass_set(tmp_path)
    deps = [30, 100, 300, 700, 2000, 4000, 9000]
    _paint_bands(sd, [(d, [(c, W)]) for d, c in zip(deps, order_pass)])
    p = next(p for p in sm(run3(sd), "M-003.c1")["parts"] if "rho" in p["name"])
    assert abs(p["value"] - (1 - 6 * 2 / 336)) < 1e-9 and p["status"] == "PASS"


def test_M003_c1_R002_controls(tmp_path):
    """R-002 §4 c1 table: hazed passes, inverted haze (rho = -1) fails, no haze (span ~0.6) fails."""
    for name, kw, exp in (("hz", {}, "PASS"), ("inv", {"invert": True}, "FAIL"), ("nohz", {"haze": False}, "FAIL")):
        sd = os.path.join(str(tmp_path), name)
        GS.m003_gameplay_frames(sd, **kw)
        GS.m003_haze_cal(sd)
        assert sm(run3(sd), "M-003.c1")["status"] == exp, name


def test_M003_c1_too_few_bands_not_run(tmp_path):
    sd, _ = pass_set(tmp_path)
    _paint_bands(sd, [(30, [(0, W)]), (100, [(2, W)])])
    assert sm(run3(sd), "M-003.c1")["status"] == "NOT_RUN"


# ------------------------------------------------------------------ (c2) haze_cal
@pytest.mark.parametrize("mode,exp", [("haze", "PASS"), ("flat", "FAIL"), ("inverted", "FAIL")])
def test_M003_c2_known_and_controls(tmp_path, mode, exp):
    sd = os.path.join(str(tmp_path), mode)
    GS.m003_gameplay_frames(sd)
    truth = GS.m003_haze_cal(sd, dL_mode=mode)
    rep = run3(sd)
    info = sm(rep, "M-003.c2")["info"]["frames"][0]
    ramp = [i for i, r in enumerate(ROLES) if r.startswith("ramp_")]
    assert sm(rep, "M-003.c2")["status"] == exp
    exp_dL = {"haze": [(0, 5), (1, 5), (2, 5), (3, 5), (4, 5), (5, 5)], "flat": [(0, 4)] * 6,
              "inverted": [(5, 7), (4, 6), (3, 5), (2, 4), (1, 3), (0, 2)]}[mode]
    want = [hex_L(SLATE[ramp[b]]) - hex_L(SLATE[ramp[a]]) for a, b in exp_dL]
    assert np.allclose(info["dL_signed"], want, atol=1e-9)
    assert "dL_abs_reading" in info


def _paint_haze(sd, pairs):
    """pairs: per distance ((dark ramp), [(light ramp, n_px), ...]) painted into the haze_cal boxes."""
    d = os.path.join(sd, "haze_cal")
    sc = json.load(open(os.path.join(d, "f000000.json")))
    ci = np.array(Image.open(os.path.join(d, sc["files"]["color_internal"])))
    ramp = [i for i, r in enumerate(ROLES) if r.startswith("ramp_")]
    pal = np.stack([GS.hex_rgb(h) for h in SLATE])
    dLs = []
    for t, (dk, light) in zip(sc["haze_targets"], pairs):
        x0, y0, x1, y1 = t["dark_px"]; ci[y0:y1, x0:x1] = pal[ramp[dk]]
        x0, y0, x1, y1 = t["light_px"]
        flat = np.concatenate([np.full(n, ramp[c]) for c, n in light])
        ci[y0:y1, x0:x1] = pal[flat].reshape(y1 - y0, x1 - x0, 3)
        Ld = _Lr(dk)
        Ll = float(np.mean([_Lr(c) for c, n in light for _ in range(n)]))
        dLs.append(Ll - Ld)
    GS.save_png(os.path.join(d, sc["files"]["color_internal"]), ci)
    return dLs


@pytest.mark.parametrize("f,exp", [(0.98, "PASS"), (1.02, "FAIL")])
def test_M003_c2_far_ratio_boundary(tmp_path, f, exp):
    sd, _ = pass_set(tmp_path)
    n = 10 * 10        # box = 10 x 10 px at 160 px width
    d200 = _Lr(7) - _Lr(0)
    target = f * 0.5 * d200                 # dL(8 km) relative to dark ramp_3
    La, Lb = _Lr(3), _Lr(7)
    nb = int(round((target - (La - _Lr(3))) / (Lb - La) * n))
    pairs = [(0, [(7, n)]), (1, [(7, n)]), (2, [(7, n)]), (3, [(7, n)]), (3, [(7, n)]), (3, [(3, n - nb), (7, nb)])]
    dLs = _paint_haze(sd, pairs)
    rep = run3(sd)
    p = next(p for p in sm(rep, "M-003.c2")["parts"] if "8 km" in p["name"])
    assert abs(p["value"] - (dLs[-1] - 0.5 * dLs[0])) < 1e-6 and p["status"] == exp


# ------------------------------------------------------------------ (d) levels, hues, accent, grid
def _test_palettes(tmp_path):
    return GS.write_test_palettes(str(tmp_path / "test_palettes.json"))


def _paint_patches(sd, colour_indices, px_each, palettes):
    d, ks = frames(sd)
    for k in ks:
        idx = GS.read_index(d, k)
        flat = idx.reshape(-1)
        for j, c in enumerate(colour_indices):
            flat[j * px_each:(j + 1) * px_each] = c
        GS.repaint(d, k, flat.reshape(H, W), palettes=palettes)


def test_M003_d_i_known_and_negative(tmp_path):
    pals = _test_palettes(tmp_path)
    sd, _ = pass_set(tmp_path, profile="test_many", palettes=pals)
    hexes, roles, _ = GS.load_profile("test_many", pals)
    greys = [i for i, h in enumerate(hexes) if h[1:3] == h[3:5] == h[5:7]]
    _paint_patches(sd, greys, 80, pals)                 # 80 px = 0.56 % each, in the top (sky) rows
    rep = run3(sd, profiles="test_many", palettes=pals)
    rows = rep["extra"]["frames"]
    d, _ = frames(sd)
    idx = GS.read_index(d, 0)
    share = np.bincount(idx.ravel(), minlength=len(hexes)) / idx.size
    Ls = np.sort([hex_L(h) for h, s in zip(hexes, share) if s >= 0.005])
    exp = 1 + int((np.diff(Ls) >= 4).sum())
    assert rows[0]["levels"] == exp and exp > 12
    assert failing(rep) == ["M-003.d-i"]


def test_M003_d_ii_negative_four_hues(tmp_path):
    pals = _test_palettes(tmp_path)
    sd, _ = pass_set(tmp_path, profile="test_many", palettes=pals)
    hexes, _, _ = GS.load_profile("test_many", pals)
    hues = [hexes.index(h) for h in ("#c03020", "#20a040", "#2040c0")]
    _paint_patches(sd, hues, 576, pals)                 # 4 % each
    rep = run3(sd, profiles="test_many", palettes=pals)
    assert sm(rep, "M-003.d-ii")["value"] == 4
    assert failing(rep) == ["M-003.d-ii"]


def test_M003_d_ii_known_three(tmp_path):
    pals = _test_palettes(tmp_path)
    sd, _ = pass_set(tmp_path, profile="test_many", palettes=pals)
    hexes, _, _ = GS.load_profile("test_many", pals)
    _paint_patches(sd, [hexes.index("#c03020"), hexes.index("#20a040")], 576, pals)
    rep = run3(sd, profiles="test_many", palettes=pals)
    assert sm(rep, "M-003.d-ii")["value"] == 3 and sm(rep, "M-003.d-ii")["status"] == "PASS"


@pytest.mark.parametrize("share,exp", [(0.15 * 0.98, "PASS"), (0.15 * 1.02, "FAIL"), (0.001 * 0.98, "FAIL"),
                                       (0.001 * 1.02 + 0.0001, "PASS")])
def test_M003_d_iii_accent_boundary(tmp_path, share, exp):
    sd = os.path.join(str(tmp_path), "acc")
    GS.m003_gameplay_frames(sd, profile="warm_amber_haze")
    GS.m003_haze_cal(sd, profile="warm_amber_haze")
    hexes, roles, _ = GS.load_profile("warm_amber_haze")
    acc = roles.index("accent")
    n = int(round(share * W * H))
    _paint_patches(sd, [acc], n, GS.PALETTES)
    rep = run3(sd, profiles="warm_amber_haze")
    s = sm(rep, "M-003.d-iii")
    assert abs(s["value"] - n / (W * H)) < 1e-12 and s["status"] == exp
    assert "M-003.d-iii" not in failing(rep)               # advisory: never gates


@pytest.mark.parametrize("k,exp", [(70, "PASS"), (74, "FAIL")])
def test_M003_d_iv_boundary(tmp_path, k, exp):
    """k non-uniform blocks of 14400: 70 -> 0.99514 (>= 0.995, 98 % side), 74 -> 0.99486 (102 % side)."""
    sd, _ = pass_set(tmp_path)
    d, ks = frames(sd)
    for f in ks:
        p = os.path.join(d, f"f{f:06d}_color_output.png")
        a = np.array(Image.open(p))
        for b in range(k):
            y, x = (b // W) * S, (b % W) * S
            a[y, x] = np.clip(a[y, x].astype(int) + 9, 0, 255)
        GS.save_png(p, a)
    rep = run3(sd)
    s = sm(rep, "M-003.d-iv")
    assert abs(s["value"] - (1 - k / (W * H))) < 1e-12 and s["status"] == exp


def test_M003_d_iv_R002_controls(tmp_path):
    """nearest 1.000 pass, bilinear fail, offset output with offset undeclared fail, declared pass."""
    cases = (("near", {}, "PASS"), ("bil", {"upscale_mode": "bilinear"}, "FAIL"),
             ("undecl", {"offset": (3, 2), "declare_offset": False}, "FAIL"),
             ("decl", {"offset": (3, 2)}, "PASS"))
    for name, kw, exp in cases:
        sd = os.path.join(str(tmp_path), name)
        GS.m003_gameplay_frames(sd, **kw)
        GS.m003_haze_cal(sd)
        assert sm(run3(sd), "M-003.d-iv")["status"] == exp, name


# ------------------------------------------------------------------ (e) composition, advisory
def test_M003_e_known(tmp_path):
    sd, _ = pass_set(tmp_path)
    d, _ = frames(sd)
    sc = json.load(open(os.path.join(d, "f000000.json")))
    v = np.array(Image.open(os.path.join(d, sc["files"]["depth"])))
    ci = np.array(Image.open(os.path.join(d, sc["files"]["color_internal"])))
    L = rgb2lab(ci / 255.0)[..., 0]
    pitch = sc["camera"]["yaw_pitch_roll_deg"][1]
    h = 0.5 + math.tan(math.radians(pitch)) / (2 * math.tan(math.radians(sc["camera"]["vfov_deg"]) / 2))
    row = list(_frames_rows(run3(sd)).values())[0]["composition"]
    assert abs(row["sky_share"] - (v == 65535).mean()) < 1e-12
    assert abs(row["horizon"] - h) < 1e-12
    lo, hi = np.percentile(L, [10, 90])
    assert abs(row["contrast"] - (hi - lo)) < 1e-3


# ------------------------------------------------------------------ (f) panel
@pytest.mark.parametrize("mean,exp", [(8 * 0.98, "FAIL"), (8 * 1.02, "PASS")])
def test_M003_f_boundary(tmp_path, mean, exp):
    sd, _ = pass_set(tmp_path)
    r = m003_ratings(str(tmp_path / "r.json"), "m003_pass", mean=mean)
    assert sm(run3(sd, ratings=r), "M-003.f")["status"] == exp


def test_M003_f_negative_anchor_excludes_rater(tmp_path):
    sd, _ = pass_set(tmp_path)
    r = m003_ratings(str(tmp_path / "r.json"), "m003_pass", neg_anchor=6)
    s = sm(run3(sd, ratings=r), "M-003.f")
    assert s["status"] == "PENDING" and len(s["info"]["excluded_raters"]) == 8


# ------------------------------------------------------------------ one gating failure at a time (C2)
def _neg_a_output(tmp_path):
    sd, _ = pass_set(tmp_path)
    d, ks = frames(sd)
    for k in ks:
        idx = GS.read_index(d, k)
        sc = json.load(open(os.path.join(d, f"f{k:06d}.json")))
        pal = np.stack([GS.hex_rgb(h) for h in SLATE])
        co = GS.upscale(pal[idx], S)
        co[: 6 * S, :] = [0, 255, 0]                          # 6 internal rows = 6.7 % of output, whole blocks
        GS.save_png(os.path.join(d, sc["files"]["color_output"]), co)
    return sd, {}


NEG = {
    "M-003.a.output": _neg_a_output,
    "M-003.a.internal": lambda t: pass_set(t, off_palette_share=0.01),
    "M-003.b.DIG": lambda t: pass_set(t, mode="none"),
    "M-003.c1": lambda t: pass_set(t, invert=True),
    "M-003.d-iv": lambda t: pass_set(t, offset=(3, 2), declare_offset=False),
}


@pytest.mark.parametrize("sid", sorted(NEG))
def test_M003_negative_fails_only_that_id(tmp_path, sid):
    sd, _ = NEG[sid](tmp_path)
    rep = run3(sd)
    assert failing(rep) == [sid], failing(rep)
    assert rep["verdict"] == "FAIL" and rep["exit_code"] == 1


def test_M003_c2_negative_fails_only_c2(tmp_path):
    sd = os.path.join(str(tmp_path), "c2neg")
    GS.m003_gameplay_frames(sd)
    GS.m003_haze_cal(sd, dL_mode="flat")
    rep = run3(sd)
    assert failing(rep) == ["M-003.c2"]


def test_M003_f_negative_fails_only_f(tmp_path):
    sd, _ = pass_set(tmp_path)
    r = m003_ratings(str(tmp_path / "r.json"), "m003_pass", mean=7.0)
    rep = run3(sd, ratings=r)
    assert failing(rep) == ["M-003.f"] and rep["verdict"] == "FAIL"
