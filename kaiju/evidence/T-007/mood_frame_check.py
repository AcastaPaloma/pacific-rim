"""C28 mood-frame style check (T-007, A §2).

Usage: <project conda env python, see environment.yml> evidence/T-007/mood_frame_check.py <frame.png> <out_dir> [cut_fraction]
(cut_fraction: rows below this fraction of the height are excluded from the mech search, so the near
pier is not merged with the legs; default 0.86)
Writes <out_dir>/mood_frame_check.json and <out_dir>/mood_frame_histogram.png.

Method
- k-means, k = 8, in CIELAB on a 1/4 downscale (seeded, deterministic).
- Each cluster centre is assigned a hue family from its LCh: chroma < 10 -> "neutral";
  chroma < 4 -> "neutral grey"; otherwise by hue angle: 30-115 deg "warm cream/brown",
  115-260 "slate-teal/grey-green", else "red/magenta". (The frame is low-chroma throughout, C* 6-12,
  so the family is decided by the hue angle of the cluster centre.)
- Accent = pixels with chroma >= 30 and hue 20-75 deg (saturated warm), measured per pixel at full size,
  because an accent that small never gets its own cluster.
- Mech mask = largest connected component of L* < 60 above the water surface, plus the off-white
  shoulder blocks (L* > 88) inside the mech's bounding box. Sky = rows above the far shore, outside the
  dilated mech mask.
- Water line: rows where the leg components end at the water. Because the soles are hidden, height is
  estimated from the design's leg proportion (leg length 0.48 H, DESIGN_NOTE.md): with the crotch at
  y_c, the top (shoulder-block / head top = H) at y_t and the water at y_w, the hidden depth d solves
  (y_w - y_c + d) / (y_w - y_t + d) = 0.48. Water height above sole = d / (y_w - y_t + d) * H.
  The same is reported for leg proportions 0.46 and 0.50 (the design range).
"""
import json
import sys

import numpy as np
from PIL import Image
from scipy import ndimage
from skimage.color import lab2lch, rgb2lab
from sklearn.cluster import KMeans
import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt

H_M = 80.0


def family(L, C, h):
    if C < 4:
        return "neutral grey"
    if 30 <= h < 115:
        return "warm cream/brown"
    if 115 <= h < 260:
        return "slate-teal/grey-green"
    return "red/magenta"


def main(path, out_dir, cut_frac=0.86):
    rgb = np.asarray(Image.open(path).convert("RGB")).astype(np.float64) / 255.0
    Hh, W, _ = rgb.shape
    lab = rgb2lab(rgb)
    lch = lab2lch(lab)
    L, C, hdeg = lch[..., 0], lch[..., 1], np.degrees(lch[..., 2])

    # k-means on 1/4 downscale
    small = np.asarray(Image.open(path).convert("RGB").resize((W // 4, Hh // 4), Image.BOX)) / 255.0
    X = rgb2lab(small).reshape(-1, 3)
    km = KMeans(n_clusters=8, n_init=10, random_state=0).fit(X)
    counts = np.bincount(km.labels_, minlength=8) / len(km.labels_)
    centres = km.cluster_centers_
    clch = lab2lch(centres[None, :, :])[0]
    clusters = []
    fam_share = {}
    for i in np.argsort(-counts):
        Lc, Cc, hc = clch[i, 0], clch[i, 1], np.degrees(clch[i, 2])
        fam = family(Lc, Cc, hc)
        fam_share[fam] = fam_share.get(fam, 0) + counts[i]
        srgb = np.clip(
            __import__("skimage.color", fromlist=["lab2rgb"]).lab2rgb(centres[i][None, None, :])[0, 0], 0, 1
        )
        clusters.append({
            "share": round(float(counts[i]), 4),
            "lab": [round(float(v), 1) for v in centres[i]],
            "rgb": [int(round(v * 255)) for v in srgb],
            "chroma": round(float(Cc), 1),
            "hue_deg": round(float(hc), 1),
            "family": fam,
        })

    accent = (C >= 30) & (hdeg >= 20) & (hdeg < 75)
    accent_share = float(accent.mean())
    ys, xs = np.nonzero(accent)
    accent_centroid = [int(xs.mean()), int(ys.mean())] if len(xs) else None

    fams_sorted = sorted(fam_share.items(), key=lambda kv: -kv[1])
    top3 = sum(v for _, v in fams_sorted[:3])
    in_families_plus_accent = min(1.0, top3 + accent_share)

    # water surface row: first row (from the top, below mid-frame) where the open water band starts,
    # found as the bright flat band under the mech; then leg components.
    dark = L < 60
    cut = int(Hh * cut_frac)  # above the near pier
    lab_cc, n = ndimage.label(dark[:cut])
    sizes = ndimage.sum(np.ones_like(lab_cc), lab_cc, range(1, n + 1))
    mech_cc = np.zeros_like(dark)
    mech_cc[:cut] = lab_cc == (1 + int(np.argmax(sizes)))
    yy, xx = np.nonzero(mech_cc)
    x0, x1, y0, y1 = xx.min(), xx.max(), yy.min(), yy.max()
    # off-white blocks: bright components of >= 800 px touching the dark body
    bright_cc, nb = ndimage.label(L > 85)
    near = ndimage.binary_dilation(mech_cc, iterations=4)
    blocks = np.zeros_like(dark)
    for i in range(1, nb + 1):
        comp = bright_cc == i
        cy = np.nonzero(comp)[0]
        if 800 <= comp.sum() <= 0.02 * comp.size and cy.min() >= y0 - 40 and (comp & near).any():
            blocks |= comp
    mech = mech_cc | blocks
    myy, mxx = np.nonzero(mech)
    # top = first row where the mech is at least 12 px wide (ignores thin rods on the head and blocks)
    row_w = mech.sum(1)
    top_y = int(np.nonzero(row_w >= 12)[0].min())

    # leg bottoms and crotch: column-wise lowest mech pixel
    lowest = np.full(W, -1)
    for x in range(x0, x1 + 1):
        col = np.nonzero(mech[:, x])[0]
        if len(col):
            lowest[x] = col.max()
    # legs end where they enter the water: the lower legs are the columns whose lowest mech pixel is in the
    # bottom quarter of the mech; the water line is the median of their bottoms
    lo = lowest[lowest >= 0]
    deep = lowest >= np.percentile(lo, 90) - 40
    water_y = int(np.median(lowest[deep]))
    leg_cols = np.nonzero(deep)[0]
    # gap columns between the two legs: between the leftmost and rightmost leg columns,
    # columns whose lowest mech pixel is well above the water
    legs_l, legs_r = leg_cols.min(), leg_cols.max()
    mid = [x for x in range(legs_l, legs_r + 1) if 0 <= lowest[x] < water_y - 60]
    crotch_y = int(np.median(sorted(lowest[mid])[: max(1, len(mid) // 3)])) if mid else None

    est = {}
    if crotch_y is not None:
        for leg in (0.46, 0.48, 0.50):
            a = water_y - crotch_y
            b = water_y - top_y
            d = max(0.0, (leg * b - a) / (1 - leg))
            est[str(leg)] = {
                "hidden_px": round(d, 1),
                "water_height_m_above_sole": round(d / (b + d) * H_M, 2),
                "water_height_fraction_of_H": round(d / (b + d), 3),
            }

    # sky: rows above the mech's knee level, outside dilated mech mask, above the far shore
    sky_region = np.zeros_like(dark)
    sky_region[: int(water_y - 0.12 * (water_y - top_y))] = True
    sky = sky_region & ~ndimage.binary_dilation(mech, iterations=12)
    sky_L = float(L[sky].mean())
    mech_L = float(L[mech].mean())
    mech_dark_L = float(L[mech_cc].mean())

    result = {
        "frame": path, "cut_fraction": cut_frac,
        "image_size": [W, Hh],
        "kmeans": {"k": 8, "space": "CIELAB", "downscale": 4, "random_state": 0, "clusters": clusters},
        "hue_family_share": {k: round(float(v), 4) for k, v in fams_sorted},
        "families_used": len(fams_sorted),
        "share_in_top3_families_plus_accent": round(in_families_plus_accent, 4),
        "accent": {"rule": "chroma>=30 and hue 20-75 deg", "share": round(accent_share, 5),
                   "centroid_xy": accent_centroid},
        "luminance_Lstar": {"sky_mean": round(sky_L, 2), "mech_mean_incl_blocks": round(mech_L, 2),
                            "mech_mean_dark_body": round(mech_dark_L, 2),
                            "sky_above_mech": sky_L > mech_L},
        "geometry_px": {"mech_top_y": top_y, "crotch_y": crotch_y, "water_line_y": water_y,
                        "mech_bbox_x": [int(x0), int(x1)], "visible_mech_height_px": water_y - top_y},
        "water_line_estimate": est,
        "water_line_target": "ankle (~3-4 m) to mid-shin (~10-12 m) above the sole",
    }
    c = result
    c["checks"] = {
        "families_le_3_plus_accent_ge_90pct": c["share_in_top3_families_plus_accent"] >= 0.90,
        "accent_le_10pct": accent_share <= 0.10,
        "sky_lighter_than_mech": sky_L > mech_L,
        "water_line_ankle_to_midshin": bool(est) and 3.0 <= est["0.48"]["water_height_m_above_sole"] <= 12.0,
    }
    json.dump(result, open(f"{out_dir}/mood_frame_check.json", "w"), indent=2)

    # histogram figure
    fig, ax = plt.subplots(1, 2, figsize=(11, 4), gridspec_kw={"width_ratios": [3, 2]})
    shares = [cl["share"] for cl in clusters]
    cols = [np.array(cl["rgb"]) / 255 for cl in clusters]
    labels = [f"{cl['family']}\nL{cl['lab'][0]:.0f} C{cl['chroma']:.0f} h{cl['hue_deg']:.0f}" for cl in clusters]
    ax[0].bar(range(8), [s * 100 for s in shares], color=cols, edgecolor="black")
    ax[0].set_xticks(range(8))
    ax[0].set_xticklabels(labels, fontsize=6, rotation=30, ha="right")
    ax[0].set_ylabel("% of pixels")
    ax[0].set_title("8-cluster k-means (CIELAB), mood frame v2")
    fams = list(fam_share.keys()) + ["accent (per pixel)"]
    vals = [fam_share[k] * 100 for k in fam_share] + [accent_share * 100]
    ax[1].barh(fams, vals, color="grey")
    for i, v in enumerate(vals):
        ax[1].text(v, i, f" {v:.2f}%", va="center", fontsize=8)
    ax[1].set_xlabel("% of pixels")
    ax[1].set_title(f"sky L* {sky_L:.1f} > mech L* {mech_L:.1f}")
    fig.tight_layout()
    fig.savefig(f"{out_dir}/mood_frame_histogram.png", dpi=110)

    # debug mask overlay for the evaluator
    ov = (rgb * 255).astype(np.uint8).copy()
    ov[mech] = (ov[mech] * 0.4 + np.array([255, 0, 255]) * 0.6).astype(np.uint8)
    ov[sky] = (ov[sky] * 0.7 + np.array([0, 120, 255]) * 0.3).astype(np.uint8)
    for y, colr in ((top_y, (255, 255, 0)), (crotch_y, (0, 255, 0)), (water_y, (255, 0, 0))):
        if y is not None:
            ov[max(0, y - 1) : y + 2] = colr
    Image.fromarray(ov).save(f"{out_dir}/mood_frame_masks.png")
    print(json.dumps({k: result[k] for k in ("hue_family_share", "accent", "luminance_Lstar", "geometry_px",
                                             "water_line_estimate", "checks")}, indent=1))


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], float(sys.argv[3]) if len(sys.argv) > 3 else 0.86)
