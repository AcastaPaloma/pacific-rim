"""R-002: reference implementations (research notes) of the M-003@v1 sub-measures, validated on the reference
panels and on synthetic positive/negative controls. Writes research/R-002/m003_validation.json."""
import json, os
import numpy as np, cv2
from scipy.ndimage import gaussian_filter, uniform_filter
from scipy.stats import spearmanr
from common import *
from analyse_panels import denoise
from synth import make_scene, render, quantise, ramp_lab, load_ramp, contrast_targets, W, H

# ---------- (a) palette adherence ----------
def adherence(lab_img, palette_lab, mask=None, tol=2.0):
    x = lab_img.reshape(-1, 3) if mask is None else lab_img[mask]
    d, _ = nearest_de00(x, palette_lab)
    return float((d <= tol).mean())

# ---------- (b) dithering in gradients ----------
def dither_metrics(idx, Lpal, win=4, g_lo=0.25, g_hi=4.0):
    """idx: HxW palette-index image at internal resolution; Lpal: L* per palette entry.
    interleaved window = not flat (top colour < 90 %), <= 4 distinct indices, >= 35 % of 4-neighbour pairs differ,
    and mean |L* - box4(L*)| >= 1 (an actual palette step, not a continuous ramp).
    DIG = share of gradient pixels (low-passed L* slope in [g_lo, g_hi] L*/px) inside an interleaved window.
    O = (rho_lag2 - rho_lag1) of the de-screened L* residual inside interleaved windows:
        Bayer/checker > 0.5, white noise ~ 0, clustered pixel masses < 0."""
    Hh, Ww = idx.shape
    K = int(idx.max()) + 1
    cnt = np.stack([uniform_filter((idx == k).astype(np.float32), win, mode="nearest") * win * win for k in range(K)], 0)
    top1 = cnt.max(0) / (win * win)
    ncol = (cnt > 0.5).sum(0)
    P = np.pad(idx, 1, mode="edge")
    diff = np.zeros(idx.shape, np.float32)
    for dy, dx in ((0, 1), (2, 1), (1, 0), (1, 2)):
        diff += (P[dy:dy + Hh, dx:dx + Ww] != idx)
    Tw = uniform_filter(diff / 4.0, win, mode="nearest")
    L = Lpal[idx].astype(np.float64)
    r = L - uniform_filter(L, win, mode="nearest")
    amp = uniform_filter(np.abs(r), win, mode="nearest")
    inter = (top1 < 0.9) & (ncol <= 4) & (Tw >= 0.35) & (amp >= 1.0)
    Lb = gaussian_filter(L, 3.0)
    gy, gx = np.gradient(Lb); g = np.hypot(gx, gy)
    grad = (g >= g_lo) & (g <= g_hi)
    DIG = float(inter[grad].mean()) if grad.any() else 0.0
    def rho(dy, dx):
        a = r[: Hh - dy, : Ww - dx]; b = r[dy:, dx:]; mk = inter[: Hh - dy, : Ww - dx] & inter[dy:, dx:]
        if mk.sum() < 50: return 0.0
        a, b = a[mk], b[mk]
        return float(np.corrcoef(a, b)[0, 1]) if a.std() > 0 and b.std() > 0 else 0.0
    O = 0.5 * ((rho(0, 2) - rho(0, 1)) + (rho(2, 0) - rho(1, 0)))
    return dict(DIG=round(DIG, 3), O=round(O, 3), interleaved_share=round(float(inter.mean()), 3),
                banded_gradient_share=round(float((grad & ~inter & (top1 >= 0.5)).mean() / max(grad.mean(), 1e-9)), 3),
                gradient_share=round(float(grad.mean()), 3))

# ---------- (c) value banding by depth ----------
BANDS = [0, 50, 150, 400, 1000, 2500, 6000, 1e9]
def depth_banding(Limg, depth, far=20000.0, min_share=0.01):
    """Geometry only (sky = depth >= 0.99 far is excluded). Per log-spaced depth band: mean L* and local contrast
    C = P90 of |dL*| between same-surface neighbours (depth ratio < 1.25) on de-screened (4x4 box) L*. Pass: no band-to-band drop of mean L* > 2, Spearman rho_L >= 0.9 and
    far-near span >= 15 L*. rho_C is reported (advisory): on gameplay frames it is content-dependent."""
    geo = depth < 0.99 * far
    Ld = uniform_filter(Limg.astype(np.float64), 4, mode="nearest")
    # same-surface contrast: |dL*| between 4-neighbours whose depths differ by < 25 % (haze attenuates it with distance)
    pairs = []
    for a, b, da, db in ((Ld[:, :-1], Ld[:, 1:], depth[:, :-1], depth[:, 1:]), (Ld[:-1], Ld[1:], depth[:-1], depth[1:])):
        ok = (np.maximum(da, db) / np.minimum(da, db) < 1.25)
        pairs.append((np.abs(a - b), ok, np.minimum(da, db)))
    meanL, C, used = [], [], []
    for i in range(len(BANDS) - 1):
        s = geo & (depth >= BANDS[i]) & (depth < BANDS[i + 1])
        if s.mean() >= min_share:
            meanL.append(float(Limg[s].mean()))
            v = np.concatenate([d[ok & (z >= BANDS[i]) & (z < BANDS[i + 1]) & (z < 0.99 * far)] for d, ok, z in pairs])
            C.append(float(np.percentile(v, 90)) if len(v) else 0.0); used.append(i)
    dL = np.diff(meanL)
    rhoL = spearmanr(used, meanL)[0] if len(used) > 2 else np.nan
    rhoC = spearmanr(used, C)[0] if len(used) > 2 else np.nan
    return dict(bands=used, meanL=np.round(meanL, 1).tolist(), C=np.round(C, 2).tolist(),
                max_drop_L=round(float(-dL.min()) if len(dL) else 0.0, 2), rho_L=round(float(rhoL), 3),
                rho_C=round(float(rhoC), 3),
                span_L=round(float(meanL[-1] - meanL[0]) if meanL else 0.0, 1),
                pass_=bool(len(dL) > 1 and dL.min() >= -2.0 and rhoL >= 0.9 and meanL[-1] - meanL[0] >= 15.0))

def target_contrast(ramp, haze_on=True, invert=False):
    """Calibration-scene check (content-independent): apparent dL* of each dark/light pair must fall with distance
    (Koschmieder: C = C0 exp(-d/D)) and the pair's mean L* must rise."""
    boxes, tg = contrast_targets()
    p, depth, ids = render(boxes, (0, 60, 0), 0.0, haze_on=haze_on, return_ids=True)
    q = quantise(1 - p if invert else p, len(ramp), "bayer"); L = ramp[q][..., 0]
    rows = []
    for dk, lt, d in tg:
        a, b = L[ids == dk], L[ids == lt]
        rows.append((d, float(np.mean(b) - np.mean(a)) if len(a) and len(b) else np.nan, float(np.mean(np.r_[a, b])) if len(a) else np.nan, int(len(a) + len(b))))
    d, C, M, n = map(np.array, zip(*rows))
    ok = np.isfinite(C) & (C > 0.5)
    D_fit = float(-1 / np.polyfit(d[ok], np.log(C[ok]), 1)[0]) if ok.sum() >= 3 else float("nan")
    return dict(d=d.tolist(), dL=np.round(C, 2).tolist(), meanL=np.round(M, 1).tolist(), px=n.tolist(), D_fit_m=round(D_fit, 0),
                pass_=bool(np.all(np.isfinite(C)) and np.all(np.diff(C) <= 0.5) and C[-1] <= 0.5 * C[0] and np.all(np.diff(M) >= -2.0)))

# ---------- (d) value levels, hue families, accent ----------
def value_levels(pal_lab, share, min_share=0.005, dl=4.0):
    L = np.sort(np.asarray(pal_lab)[np.asarray(share) >= min_share][:, 0])
    levels = 1 + int((np.diff(L) >= dl).sum()) if len(L) else 0
    return levels

def hue_families(lab_px, min_mass=0.10, cmin=8.0):
    C = np.hypot(lab_px[:, 1], lab_px[:, 2]); Hh = np.degrees(np.arctan2(lab_px[:, 2], lab_px[:, 1])) % 360
    c = C >= cmin
    hist = np.bincount(Hh[c].astype(int) % 360, weights=C[c], minlength=360)
    hs = gaussian_filter(np.concatenate([hist] * 3), 15)[360:720]
    hs /= hs.sum() + 1e-9
    # peaks with circular basins: assign each degree to its uphill peak, count peaks with >= min_mass
    nxt = np.arange(360)
    for _ in range(360):
        l = (nxt - 1) % 360; r = (nxt + 1) % 360
        best = np.where(hs[l] > hs[nxt], l, nxt); best = np.where(hs[r] > hs[best], r, best)
        if (best == nxt).all(): break
        nxt = best
    mass = np.bincount(nxt, weights=hs, minlength=360)
    fam = [(int(h), round(float(m), 3)) for h, m in enumerate(mass) if m >= min_mass]
    return len(fam), fam, float(c.mean())

# ---------- (d) pixel-grid integrity ----------
def grid_integrity(out_rgb, s, offset=(0, 0)):
    ox, oy = offset
    a = out_rgb[oy:, ox:].astype(np.int16)
    h, w = (a.shape[0] // s) * s, (a.shape[1] // s) * s
    a = a[:h, :w].reshape(h // s, s, w // s, s, 3)
    uni = (a.max((1, 3)) - a.min((1, 3))).max(-1) <= 1        # <=1 code value per channel (8-bit rounding)
    return float(uni.mean())

# ---------- (e) composition (advisory) ----------
def composition(Limg, sky_mask=None):
    if sky_mask is None:  # reference images have no depth: sky = top-connected low-texture region near top-row L*
        sd = np.sqrt(np.maximum(uniform_filter(Limg ** 2, 5) - uniform_filter(Limg, 5) ** 2, 0))
        topL = np.median(Limg[:3]); cand = (sd < 3.0) & (np.abs(Limg - topL) < 12)
        n, lab = cv2.connectedComponents(cand.astype(np.uint8), connectivity=4)
        top = np.unique(lab[0][cand[0]]); sky_mask = np.isin(lab, top) & cand
    rows = sky_mask.mean(1)
    below = np.where(rows < 0.5)[0]
    horizon = float(below[0] / len(rows)) if len(below) else 1.0      # fraction from top where sky stops dominating
    lo, hi = np.percentile(Limg, [10, 90])
    return dict(sky_share=round(float(sky_mask.mean()), 3), horizon_from_bottom=round(1 - horizon, 3),
                silhouette_dL_10_90=round(float(hi - lo), 1))


def refs():
    pj = json.load(open(os.path.join(OUT, "palettes.json")))
    out = {}
    for pid in PANELS:
        rgb, mask = load_panel(pid)
        info, small, sm = denoise(rgb, mask, pid)
        lab = srgb_to_lab(small)
        pal = np.array([c["lab"] for c in pj["panels"][pid]["palette"]]); share = [c["share"] for c in pj["panels"][pid]["palette"]]
        _, idx = nearest_de00(lab.reshape(-1, 3), pal); idx = idx.reshape(lab.shape[:2])
        dm = dither_metrics(idx, pal[:, 0])
        nf, fam, chroma_share = hue_families(lab[sm])
        acc = pj["panels"][pid]["accent"]
        comp = composition(lab[..., 0])
        gi = grid_integrity(rgb, int(round(info["p"])) if info["p"] else 4)
        out[pid] = dict(grid_detected=info["detected"], grid_weak=info["weak"], dither=dm, value_levels=value_levels(pal, share),
                        hue_families=nf, hue_family_peaks=fam, chromatic_share=round(chroma_share, 3),
                        accent_share=(acc["share"] if acc else None), composition=comp, grid_integrity_raw=round(gi, 3))
        print(pid, out[pid])
    return out


def synthetic():
    ramp, full = load_ramp("slate_teal")
    boxes = make_scene()
    p, depth = render(boxes, (0, 60, 0), 0.0)
    n = len(ramp); res = {}
    variants = {
        "bayer4": quantise(p, n, "bayer"),
        "no_dither_posterised": quantise(p, n, "none"),
        "white_noise_dither": quantise(p, n, "noise"),
    }
    cont = ramp_lab(p, ramp)                                   # unquantised continuous render (negative control)
    for k, q in variants.items():
        lab = ramp[q]
        res[k] = dict(adherence_T2=adherence(lab, full), dither=dither_metrics(q, ramp[:, 0]),
                      depth=depth_banding(lab[..., 0], depth))
    # continuous: quantise to a fine 64-level ramp for the dither detector (no palette structure)
    fine = np.clip(np.round(p * 63), 0, 63).astype(int); fine_ramp = ramp_lab(np.linspace(0, 1, 64), ramp)
    res["continuous_unquantised"] = dict(adherence_T2=adherence(cont, full), adherence_T5=adherence(cont, full, tol=5.0),
                                         dither=dither_metrics(fine, fine_ramp[:, 0]), depth=depth_banding(cont[..., 0], depth))
    # depth negative control: inverted haze (far darker than near)
    inv = quantise(1 - p, n, "bayer"); res["inverted_haze"] = dict(depth=depth_banding(ramp[inv][..., 0], depth))
    pn, _ = render(boxes, (0, 60, 0), 0.0, haze_on=False)
    res["no_haze"] = dict(depth=depth_banding(ramp[quantise(pn, n, "bayer")][..., 0], depth))
    res["targets_haze"] = target_contrast(ramp); res["targets_no_haze"] = target_contrast(ramp, haze_on=False)
    res["targets_inverted"] = target_contrast(ramp, invert=True)
    # pixel-grid integrity at output resolution (scale 6): nearest vs bilinear upscaling
    rgb = lab_to_srgb_u8(ramp[variants["bayer4"]])
    near = cv2.resize(rgb, (W * 6, H * 6), interpolation=cv2.INTER_NEAREST)
    bil = cv2.resize(rgb, (W * 6, H * 6), interpolation=cv2.INTER_LINEAR)
    shifted = np.roll(near, (2, 3), (0, 1))                     # sub-pixel-compensated output: blocks offset by (3,2)
    res["grid_integrity"] = dict(nearest=grid_integrity(near, 6), bilinear=grid_integrity(bil, 6),
                                 offset_wrong=grid_integrity(shifted, 6), offset_declared=grid_integrity(shifted, 6, (3, 2)))
    # palette adherence at output resolution after bilinear upscale (a "sloppy present" failure)
    res["output_bilinear_adherence_T2"] = adherence(srgb_to_lab(bil), full)
    res["composition_synth"] = composition(ramp[variants["bayer4"]][..., 0], sky_mask=depth >= 19999)
    for k, v in res.items(): print(k, v)
    return res


if __name__ == "__main__":
    out = dict(references=refs(), synthetic=synthetic())
    json.dump(out, open(os.path.join(OUT, "m003_validation.json"), "w"), indent=1, default=float)
