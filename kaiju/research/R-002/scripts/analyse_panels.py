"""R-002: panel inventory + palette extraction + self-adherence + district profiles.
Usage: /opt/miniconda3/envs/kaiju/bin/python analyse_panels.py   (writes research/R-002/*.json, palettes.png)"""
import json, os
import numpy as np, cv2
from scipy.ndimage import gaussian_filter1d
from scipy.signal import find_peaks
from sklearn.cluster import KMeans
from common import *

RNG = np.random.default_rng(2)
MERGE_DE = 3.0          # merge palette entries closer than this (CIEDE2000)
MIN_SHARE = 0.001       # drop clusters below 0.1 % of pixels
TOLS = [1, 2, 3, 5, 7, 10]


def grid_period(gray, mask, axis, pmin=2.0, pmax=12.0):
    """Pixel-grid period by circular statistics of strong-edge positions (mean resultant length R)."""
    d = np.abs(np.diff(gray, axis=axis))
    mm = (mask[:, 1:] & mask[:, :-1]) if axis == 1 else (mask[1:] & mask[:-1])
    thr = np.percentile(d[mm], 95)
    ys, xs = np.where((d > thr) & mm)
    pos = (xs if axis == 1 else ys) + 0.5
    ps = np.arange(pmin, pmax + 1e-9, 0.01)
    R = np.array([np.abs(np.exp(2j * np.pi * pos / p).mean()) for p in ps])
    # prefer the largest period whose R is within 85 % of the max (sub-harmonics p/2, p/3 also score)
    cand = [j for j in range(len(ps)) if R[j] >= 0.85 * R.max()
            and (j == 0 or R[j] >= R[j - 1]) and (j == len(R) - 1 or R[j] >= R[j + 1])]
    j = cand[-1]
    return float(ps[j]), float(R[j])


GRID_R = 0.08       # R threshold for "grid detected" (posterised/no-grid panels score 0.01-0.045)
GRID_R_WEAK = 0.05  # 0.05-0.08: weak/likely grid, still used for de-noising


def denoise(rgb, mask, pid):
    g = rgb.astype(float).mean(2)
    px, rx = grid_period(g, mask, 1)
    py, ry = grid_period(g, mask, 0)
    detected = min(rx, ry) >= GRID_R
    weak = (not detected) and max(rx, ry) >= GRID_R_WEAK and abs(px - py) / min(px, py) < 0.1
    if detected or weak:
        p = 0.5 * (px + py)
        h, w = mask.shape
        nw, nh = int(round(w / p)), int(round(h / p))
        small = cv2.resize(rgb, (nw, nh), interpolation=cv2.INTER_AREA)
        sm = cv2.resize(mask.astype(np.float32), (nw, nh), interpolation=cv2.INTER_AREA) > 0.99
        native = (nw, nh)
    else:
        p = None
        small = cv2.medianBlur(rgb, 3)[::2, ::2]
        sm = mask[::2, ::2]
        native = None
    return dict(px=px, rx=rx, py=py, ry=ry, detected=bool(detected), weak=bool(weak), p=p, native=native), small, sm


def kneedle(ks, dist):
    x = (ks - ks[0]) / (ks[-1] - ks[0]); y = (dist - dist.min()) / (dist.max() - dist.min())
    return int(ks[np.argmax((1 - x) - y)])  # decreasing convex curve: max distance below the chord


def merge_palette(lab, w, thr=MERGE_DE):
    lab = [np.array(c) for c in lab]; w = list(w)
    while len(lab) > 1:
        P = np.array(lab); D = de00(P[:, None], P[None]); np.fill_diagonal(D, 1e9)
        i, j = np.unravel_index(D.argmin(), D.shape)
        if D[i, j] >= thr: break
        lab[i] = (lab[i] * w[i] + lab[j] * w[j]) / (w[i] + w[j]); w[i] += w[j]
        del lab[j]; del w[j]
    return np.array(lab), np.array(w)


def extract_palette(lab_px, kmax=24, kover=64):
    """Dominant-colour count = Kneedle knee (Satopaa 2011) of k-means distortion for k=2..kmax.
    Palette = k-means over-segmentation (k=kover, CIELAB Euclidean) -> drop <0.1 % -> agglomerative merge under dE00<3."""
    sample = lab_px[RNG.choice(len(lab_px), min(60000, len(lab_px)), replace=False)]
    s2 = sample[:20000]
    ks = np.arange(2, kmax + 1); dist = []
    for k in ks:
        dist.append(KMeans(k, n_init=1, random_state=0, max_iter=100).fit(s2).inertia_ / len(s2))
    dist = np.array(dist); kstar = kneedle(ks, dist)
    km = KMeans(kover, n_init=1, random_state=0, max_iter=300).fit(sample)
    lab_c = km.cluster_centers_; lab_c[:, 0] = np.clip(lab_c[:, 0], 0, 100)
    _, idx = nearest_de00(lab_px, lab_c)
    w = np.bincount(idx, minlength=len(lab_c)) / len(idx)
    keep = w >= MIN_SHARE
    pal, pw = merge_palette(lab_c[keep], w[keep])
    order = np.argsort(pal[:, 0]); return pal[order], pw[order] / pw.sum(), kstar, dist.tolist()


def value_bands(L):
    h, _ = np.histogram(L, bins=101, range=(0, 101)); h = gaussian_filter1d(h / h.sum(), 1.5)
    pk, _ = find_peaks(h, prominence=0.002, distance=4)
    return len(pk), [int(p) for p in pk]


def hue_stats(pal, w):
    C = np.hypot(pal[:, 1], pal[:, 2]); H = np.degrees(np.arctan2(pal[:, 2], pal[:, 1])) % 360
    fam = {}
    for c, h, ww in zip(C, H, w):
        key = "neutral" if c < 8 else "h%03d" % (int(h // 30) * 30)
        fam[key] = fam.get(key, 0) + ww
    dom = sorted([(k, v) for k, v in fam.items() if v >= 0.10], key=lambda t: -t[1])
    chrom = C >= 8
    if chrom.any():
        z = (w[chrom] * np.exp(1j * np.radians(H[chrom]))).sum(); hdom = np.degrees(np.angle(z)) % 360
    else:
        hdom = 0.0
    medC = np.average(C, weights=w)
    best, score = None, 0
    for i in range(len(pal)):
        dh = abs((H[i] - hdom + 180) % 360 - 180)
        if 0.001 <= w[i] <= 0.20 and C[i] >= 12 and (C[i] >= 1.5 * medC or dh >= 60):
            s = C[i] * max(dh, 1) * 1.0
            if s > score: best, score = i, s
    return dom, float(hdom), (int(best) if best is not None else None), C, H


def self_adherence(lab_px, pal):
    d, _ = nearest_de00(lab_px, pal)
    return {str(t): float((d <= t).mean()) for t in TOLS}, float(np.percentile(d, 95)), float(np.median(d))


def main():
    os.makedirs(OUT, exist_ok=True)
    res = {}; pooled = {}
    for pid, (fn, box, excl, label) in PANELS.items():
        rgb, mask = load_panel(pid)
        info, small, sm = denoise(rgb, mask, pid)
        lab_s = srgb_to_lab(small)[sm]
        pal, w, kstar, dist = extract_palette(lab_s)
        lab_full = srgb_to_lab(rgb)[mask]
        sub = lab_full[RNG.choice(len(lab_full), min(300000, len(lab_full)), replace=False)]
        adh_dn, p95_dn, med_dn = self_adherence(lab_s, pal)
        adh_raw, p95_raw, med_raw = self_adherence(sub, pal)
        nb, peaks = value_bands(lab_s[:, 0])
        dom, hdom, acc, C, H = hue_stats(pal, w)
        uniq_raw = len(np.unique(rgb[mask].reshape(-1, 3), axis=0))
        res[pid] = dict(file=fn, box_xyxy=box, excluded_boxes=excl, label=label, grid=info,
                        unique_colours_raw=uniq_raw, kmeans_k_knee=kstar, palette_size=len(pal),
                        palette_hex=hexs(lab_to_srgb_u8(pal)), palette_lab=np.round(pal, 2).tolist(),
                        palette_share=np.round(w, 4).tolist(),
                        adherence_denoised=adh_dn, adherence_raw=adh_raw, de95_denoised=p95_dn, de95_raw=p95_raw,
                        de50_denoised=med_dn, de50_raw=med_raw,
                        value_band_count=nb, value_band_peaks_L=peaks,
                        dominant_hue_families=[(k, round(v, 3)) for k, v in dom], dominant_hue_deg=round(hdom, 1),
                        accent=(None if acc is None else dict(hex=hexs(lab_to_srgb_u8(pal[acc]))[0],
                                lab=np.round(pal[acc], 2).tolist(), share=round(float(w[acc]), 4),
                                hue_deg=round(float(H[acc]), 1), chroma=round(float(C[acc]), 1))))
        pooled[pid] = lab_s
        print(pid, "grid", info["detected"], round(info["px"], 2), round(info["rx"], 3), "k*", kstar, "pal", len(pal),
              "bands", nb, "adh_dn", {k: round(v, 3) for k, v in adh_dn.items()}, "adh_raw@5", round(adh_raw["5"], 3),
              "dom", dom, "acc", res[pid]["accent"] and res[pid]["accent"]["hex"])
    np.savez_compressed(os.path.join(OUT, "scripts", "_pooled_lab.npz"), **pooled)
    with open(os.path.join(OUT, "panels.json"), "w") as f:
        json.dump(dict(schema="panel_inventory@v1", generated_by="research/R-002/scripts/analyse_panels.py",
                       merge_de00=MERGE_DE, min_share=MIN_SHARE, grid_R_threshold=GRID_R, panels=res), f, indent=1)


if __name__ == "__main__":
    main()
