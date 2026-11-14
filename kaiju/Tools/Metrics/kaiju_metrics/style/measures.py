"""Frame-level style measures (R-002 §4): adherence, dithering (DIG, O), depth banding, haze pairs, value levels,
hue families, accent share, grid integrity, composition. Pure functions on arrays; parameters come from the registry.

Window semantics (reading, see SPEC_READINGS.md): a pixel is "inside an interleaved window" when the 4 x 4 window
anchored at that pixel (rows / columns -2 .. +1, edge-replicated, the scipy uniform_filter(size=4) window used in
R-002's validation) meets all four conditions."""
from __future__ import annotations

import numpy as np
from scipy.ndimage import gaussian_filter, gaussian_filter1d, uniform_filter
from scipy.stats import spearmanr


def adherence(de, mask=None, tol=2.0):
    v = de if mask is None else de[mask]
    return float((v <= tol).mean()) if v.size else float("nan")


def neighbour_diff(idx):
    H, W = idx.shape
    P = np.pad(idx, 1, mode="edge")
    d = np.zeros(idx.shape, np.float64)
    for dy, dx in ((0, 1), (2, 1), (1, 0), (1, 2)):
        d += P[dy:dy + H, dx:dx + W] != idx
    return d / 4.0


def interleaved(idx, L, win=4, top_max=0.9, ncol_max=4, diff_min=0.35, amp_min=1.0):
    """Boolean map of pixels inside an interleaved window (R-002 §4 b), plus the residual r = L - box4(L)."""
    vals = np.unique(idx)
    top = np.zeros(idx.shape, np.float64)
    ncol = np.zeros(idx.shape, np.int32)
    for v in vals:
        c = uniform_filter((idx == v).astype(np.float64), win, mode="nearest")
        top = np.maximum(top, c)
        ncol += c > 0.5 / (win * win)
    tw = uniform_filter(neighbour_diff(idx), win, mode="nearest")
    r = L - uniform_filter(L, win, mode="nearest")
    amp = uniform_filter(np.abs(r), win, mode="nearest")
    inter = (top < top_max - 1e-12) & (ncol <= ncol_max) & (tw >= diff_min - 1e-12) & (amp >= amp_min)
    return inter, r


def gradient_mask(L, sigma=3.0, lo=0.25, hi=4.0):
    Lb = gaussian_filter(L, sigma)
    gy, gx = np.gradient(Lb)
    g = np.hypot(gx, gy)
    return (g >= lo) & (g <= hi)


def dither_metrics(idx, L, p):
    """DIG (share of gradient pixels inside interleaved windows) and O (ordered index). p: dict of parameters."""
    inter, r = interleaved(idx, L, p["window_px"], p["top_colour_max"], p["distinct_max"], p["neighbour_diff_min"],
                           p["amp_min"])
    grad = gradient_mask(L, p["grad_sigma_px"], p["grad_lo"], p["grad_hi"])
    gshare = float(grad.mean())
    dig = float(inter[grad].mean()) if grad.any() else float("nan")
    H, W = idx.shape

    def rho(dy, dx):
        a = r[:H - dy, :W - dx]; b = r[dy:, dx:]
        mk = inter[:H - dy, :W - dx] & inter[dy:, dx:]
        if mk.sum() < p["o_min_pairs"]:
            return 0.0
        a, b = a[mk], b[mk]
        return float(np.corrcoef(a, b)[0, 1]) if a.std() > 0 and b.std() > 0 else 0.0
    O = 0.5 * ((rho(0, 2) - rho(0, 1)) + (rho(2, 0) - rho(1, 0)))
    return {"DIG": dig, "O": O, "gradient_share": gshare, "interleaved_share": float(inter.mean())}, inter


def depth_banding(L, z, far, edges, min_share, sky_frac):
    """Mean L* per log depth band (sky z >= sky_frac * far excluded; bands with >= min_share of all pixels)."""
    geo = np.isfinite(z) & (z < sky_frac * far)
    e = list(edges) + [np.inf]
    used, meanL = [], []
    for i in range(len(e) - 1):
        s = geo & (z >= e[i]) & (z < e[i + 1])
        if s.mean() >= min_share:
            used.append(i)
            meanL.append(float(L[s].mean()))
    out = {"bands": used, "meanL": meanL}
    if len(used) >= 3:
        d = np.diff(meanL)
        out.update(max_drop=float(max(0.0, -d.min())), rho=float(spearmanr(used, meanL)[0]),
                   span=float(meanL[-1] - meanL[0]))
    return out


def haze_pairs(L, targets):
    rows = []
    for t in sorted(targets, key=lambda t: t["distance_m"]):
        x0, y0, x1, y1 = t["dark_px"]; a = L[y0:y1, x0:x1]
        x0, y0, x1, y1 = t["light_px"]; b = L[y0:y1, x0:x1]
        rows.append({"distance_m": t["distance_m"], "dL": float(b.mean() - a.mean()),
                     "meanL": float(np.concatenate([a.ravel(), b.ravel()]).mean())})
    return rows


def value_levels(L_entries, shares, share_min, merge_dL):
    Ls = np.sort(np.asarray(L_entries)[np.asarray(shares) >= share_min])
    return (1 + int((np.diff(Ls) >= merge_dL).sum())) if len(Ls) else 0


def hue_families(lab, cmin, sigma, mass_min):
    """Peaks of the chroma-weighted circular hue histogram (1 deg bins, Gaussian sigma, circular basins)."""
    lab = lab.reshape(-1, 3)
    C = np.hypot(lab[:, 1], lab[:, 2])
    Hd = np.degrees(np.arctan2(lab[:, 2], lab[:, 1])) % 360
    c = C >= cmin
    if not c.any():
        return 0, []
    hist = np.bincount(Hd[c].astype(int) % 360, weights=C[c], minlength=360)
    hs = gaussian_filter1d(np.concatenate([hist] * 3), sigma)[360:720]
    hs = hs / (hs.sum() + 1e-12)
    nxt = np.arange(360)
    for _ in range(360):
        l_, r_ = (nxt - 1) % 360, (nxt + 1) % 360
        best = np.where(hs[l_] > hs[nxt], l_, nxt)
        best = np.where(hs[r_] > hs[best], r_, best)
        if (best == nxt).all():
            break
        nxt = best
    mass = np.bincount(nxt, weights=hs, minlength=360)
    fam = [(int(h), float(m)) for h, m in enumerate(mass) if m >= mass_min]
    return len(fam), fam


def grid_integrity(out_rgb, s, offset, tol, ui_mask=None):
    """Share of complete s x s blocks (starting at the declared offset) with max - min <= tol per channel.
    Blocks touching UI-masked pixels are excluded. Returns (share, non-uniform block map)."""
    ox, oy = offset
    a = out_rgb[oy:, ox:].astype(np.int16)
    h, w = (a.shape[0] // s) * s, (a.shape[1] // s) * s
    a = a[:h, :w].reshape(h // s, s, w // s, s, 3)
    uni = (a.max((1, 3)) - a.min((1, 3))).max(-1) <= tol
    keep = np.ones(uni.shape, bool)
    if ui_mask is not None:
        m = ui_mask[oy:, ox:][:h, :w].reshape(h // s, s, w // s, s)
        keep = ~(m > 0).any((1, 3))
    return (float(uni[keep].mean()) if keep.any() else float("nan")), (~uni & keep)


def composition(L, z, far, sky_frac, pitch_deg, vfov_deg):
    sky = ~np.isfinite(z) | (z >= sky_frac * far)
    lo, hi = np.percentile(L, [10, 90])
    h = 0.5 + np.tan(np.radians(pitch_deg)) / (2 * np.tan(np.radians(vfov_deg) / 2))
    return {"sky_share": float(sky.mean()), "horizon": float(h), "contrast": float(hi - lo)}
