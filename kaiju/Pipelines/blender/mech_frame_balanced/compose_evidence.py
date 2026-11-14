"""Compose T-007 evidence images and pixel-scale checks from the Blender renders (kaiju env).

Run after render_views.py and rom_check.py:
  /opt/miniconda3/envs/kaiju/bin/python Pipelines/blender/mech_frame_balanced/compose_evidence.py

Inputs : evidence/T-007/raw/{turnaround_*,silhouette_*,flat_front,pose_*}.png, raw/views.json
Outputs (evidence/T-007/):
  turnaround_{front,side,back,three_quarter}.png  markers: 10 m grid, 67 m deck line, 80 m line, 1.8 m human
  turnaround_sheet.png                             the four views side by side (downscaled)
  silhouette_{front,side,three_quarter}_{full,180px,48px,24px}.png   nearest-neighbour, H = N px
  silhouettes_x8_{180,48,24}px.png                 each size shown x8; 24/48 px sheets include 2 decoys (blind labels)
  silhouette_decoy_key.json                        which label is the mech (kept apart for blind looks)
  flat_front_180px.png, flat_front_180px_x4.png    flat palette colours at H = 180 px (value-band count in report)
  overlay_human_iou.png, overlay_concept_vs_greybox.png
  rom_contact_sheet.png                            AC-21 poses, front and side
  pixel_checks.json                                gaps / bands / IoU numbers for C6-C9, C27
"""
import glob
import json
import os
import random

import scipy  # noqa: F401  (ndimage, used in concept_masks)

import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
EV = os.path.join(PROJECT, "evidence", "T-007")
RAW = os.path.join(EV, "raw")
CONCEPT = glob.glob(os.path.join(PROJECT, "art", "concepts", "mech_frame_balanced", "layer2_turnaround-final_*.jpg"))[0]
H_M = 80.0
FONT = ImageFont.load_default()
try:
    FONT = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial.ttf", 22)
except Exception:
    pass

checks = {}
views = json.load(open(os.path.join(RAW, "views.json")))
Y_C = views["y_centre"]


# ------------------------------------------------------------------ turnarounds with markers
def world_to_px(v, y):
    return v["h"] / 2 - (y - Y_C) * v["px_per_m"]


def turnarounds():
    ims = []
    for name in ("front", "side", "back", "three_quarter"):
        v = views["views"][f"turnaround_{name}"]
        im = Image.open(os.path.join(RAW, f"turnaround_{name}.png")).convert("RGB")
        base = im.copy()
        d = ImageDraw.Draw(im)
        pxm, W = v["px_per_m"], v["w"]
        for y in range(0, 91, 10):                          # 10 m grid: horizontal lines
            py = world_to_px(v, y)
            d.line([(0, py), (W, py)], fill=(185, 185, 185), width=1)
            d.text((8, py - 24), f"{y} m", fill=(120, 120, 120), font=FONT)
        for k in range(-9, 10):                             # vertical lines every 10 m about the centre
            px = W / 2 + k * 10 * pxm
            d.line([(px, 0), (px, v["h"])], fill=(205, 205, 205), width=1)
        im = Image.composite(base, im, Image.eval(base.convert("L"), lambda p: 255 if p < 200 else 0))  # grid behind the mech
        d = ImageDraw.Draw(im)
        py = world_to_px(v, 67.0)
        for x0 in range(0, W, 24):
            d.line([(x0, py), (x0 + 14, py)], fill=(30, 90, 200), width=3)
        d.text((W - 330, py - 28), "67 m bridge deck", fill=(30, 90, 200), font=FONT)
        py = world_to_px(v, 80.0)
        d.line([(0, py), (W, py)], fill=(200, 40, 40), width=2)
        d.text((W - 330, py - 28), "80 m (H)", fill=(200, 40, 40), font=FONT)
        d.text((12, v["h"] - 34), f"T-007 Balanced greybox, {name} (orthographic). Red figure at the left toe = 1.8 m human.",
               fill=(60, 60, 60), font=FONT)
        im.quantize(colors=96, method=Image.Quantize.MEDIANCUT).save(os.path.join(EV, f"turnaround_{name}.png"), optimize=True)
        ims.append(im.crop((W // 2 - 330, 0, W // 2 + 330, v["h"])))
    sheet = Image.new("RGB", (660 * 4, 1080), "white")
    for i, im in enumerate(ims):
        sheet.paste(im, (i * 660, 0))
    sheet.thumbnail((2000, 2000))
    sheet.save(os.path.join(EV, "turnaround_sheet.png"))


# ------------------------------------------------------------------ silhouettes
def mask_of(path):
    a = np.array(Image.open(path).convert("L"))
    return a < 128


def crop_to_bbox(m):
    rows, cols = np.where(m.any(1))[0], np.where(m.any(0))[0]
    return m[rows[0]:rows[-1] + 1, cols[0]:cols[-1] + 1]


def nn_resize(m, h):
    """nearest-neighbour downscale so that the figure height is exactly h px"""
    H, W = m.shape
    w = max(1, int(round(W * h / H)))
    yi = ((np.arange(h) + 0.5) * H / h).astype(int)
    xi = ((np.arange(w) + 0.5) * W / w).astype(int)
    return m[yi][:, xi]


def to_img(m, scale=1, pad=0):
    im = Image.fromarray(np.where(m, 0, 255).astype(np.uint8))
    if pad:
        im2 = Image.new("L", (im.width + 2 * pad, im.height + 2 * pad), 255)
        im2.paste(im, (pad, pad))
        im = im2
    if scale > 1:
        im = im.resize((im.width * scale, im.height * scale), Image.NEAREST)
    return im


def human_silhouette(h_px):
    """front silhouette of a standing 1.8 m human scaled to h_px (Drillis-Contini / Winter fractions)"""
    S = 8
    H = h_px * S
    W = int(H * 0.5)
    im = Image.new("L", (W, H), 255)
    d = ImageDraw.Draw(im)
    cx = W / 2
    f = lambda y: H - y * H     # height fraction -> pixel row
    d.ellipse([cx - 0.065 * H, f(1.0), cx + 0.065 * H, f(0.87)], fill=0)                 # head
    d.rectangle([cx - 0.03 * H, f(0.88), cx + 0.03 * H, f(0.82)], fill=0)                 # neck
    d.polygon([(cx - 0.13 * H, f(0.82)), (cx + 0.13 * H, f(0.82)), (cx + 0.095 * H, f(0.53)),
               (cx - 0.095 * H, f(0.53))], fill=0)                                           # trunk
    for s in (-1, 1):
        d.polygon([(cx + s * 0.13 * H, f(0.818)), (cx + s * 0.175 * H, f(0.80)), (cx + s * 0.165 * H, f(0.48)),
                   (cx + s * 0.135 * H, f(0.39)), (cx + s * 0.105 * H, f(0.39)), (cx + s * 0.115 * H, f(0.6)),
                   (cx + s * 0.11 * H, f(0.76))], fill=0)                                    # arm and hand
        d.polygon([(cx + s * 0.005 * H, f(0.53)), (cx + s * 0.095 * H, f(0.53)), (cx + s * 0.075 * H, f(0.04)),
                   (cx + s * 0.02 * H, f(0.04))], fill=0)                                    # leg
        xa, xb = sorted((cx + s * 0.02 * H, cx + s * 0.085 * H))
        d.rectangle([xa, f(0.04), xb, f(0.0)], fill=0)       # foot (front)
    m = np.array(im) < 128
    return crop_to_bbox(m)


def decoy_crane(h):
    S = 8
    H = h * S
    im = Image.new("L", (int(H * 0.9), H), 255)
    d = ImageDraw.Draw(im)
    W = im.width
    for x in (0.25, 0.55):
        d.rectangle([x * W, 0.25 * H, x * W + 0.05 * H, H], fill=0)          # legs
    d.rectangle([0.25 * W, 0.55 * H, 0.55 * W + 0.05 * H, 0.6 * H], fill=0)  # portal beam
    d.rectangle([0.0, 0.2 * H, W, 0.27 * H], fill=0)                          # boom
    d.rectangle([0.3 * W, 0.05 * H, 0.5 * W, 0.2 * H], fill=0)                # machinery house
    d.line([(0.4 * W, 0.05 * H), (0.95 * W, 0.2 * H)], fill=0, width=int(0.02 * H))
    return crop_to_bbox(np.array(im) < 128)


def decoy_tower(h):
    S = 8
    H = h * S
    im = Image.new("L", (int(H * 0.45), H), 255)
    d = ImageDraw.Draw(im)
    W = im.width
    d.rectangle([0.1 * W, 0.15 * H, 0.9 * W, H], fill=0)                      # block
    d.rectangle([0.0, 0.55 * H, W, 0.62 * H], fill=0)                         # setback ledge
    d.rectangle([0.3 * W, 0.0, 0.7 * W, 0.15 * H], fill=0)                    # crown
    d.rectangle([0.25 * W, 0.8 * H, 0.75 * W, H], fill=255)                   # portal opening
    d.rectangle([0.35 * W, 0.8 * H, 0.65 * W, H], fill=0)
    return crop_to_bbox(np.array(im) < 128)


def runs(row):
    r = np.diff(np.concatenate([[0], row.astype(int), [0]]))
    s, e = np.where(r == 1)[0], np.where(r == -1)[0]
    return list(zip(s, e))


def silhouette_checks(m48, m24, side48):
    """pixel tests on the front 48 / 24 px masks (rows counted from the top)"""
    H = m48.shape[0]
    mpp = H_M / H
    y_of = lambda r: H_M - (r + 0.5) * mpp
    out = {}
    # arm separation: rows between shoulder (66 m) and wrist (38 m) with >= 3 runs (arm | torso | arm)
    arm_rows = [r for r in range(H) if 30.5 <= y_of(r) <= 66.0]
    sep = [r for r in arm_rows if len(runs(m48[r])) >= 3]
    out["48px_arm_rows"] = len(arm_rows)
    out["48px_arm_rows_separated"] = len(sep)
    out["48px_arm_separated_fraction_of_arm_length"] = round(len(sep) / len(arm_rows), 3)
    # crotch gap: rows between 4 m and 38 m with 2+ runs in the central third
    c0, c1 = m48.shape[1] // 3, 2 * m48.shape[1] // 3
    leg_rows = [r for r in range(H) if 5.0 <= y_of(r) <= 37.0]
    out["48px_leg_rows_with_crotch_gap"] = sum(1 for r in leg_rows if len(runs(m48[r, c0:c1])) >= 2)
    out["48px_leg_rows"] = len(leg_rows)
    # head: top rows narrower than the shoulder row and separated from the shoulder blocks
    top = [r for r in range(H) if y_of(r) >= 72.0]
    out["48px_top_rows_runs"] = [len(runs(m48[r])) for r in top]
    widths = [int(m48[r].sum()) for r in range(H)]
    out["48px_widest_row_m"] = round(y_of(int(np.argmax(widths))), 1)
    # feet wider than shanks
    foot_rows = [r for r in range(H) if y_of(r) <= 1.6]
    shank_rows = [r for r in range(H) if 9 <= y_of(r) <= 20]
    fw = max(max(e - s for s, e in runs(m48[r])) for r in foot_rows)
    sw = max(max(e - s for s, e in runs(m48[r])) for r in shank_rows)
    out["48px_foot_width_px"], out["48px_shank_width_px"] = int(fw), int(sw)
    # 24 px: head bump narrower than shoulders, shoulders widest, legs separate
    H2 = m24.shape[0]
    y2 = lambda r: H_M - (r + 0.5) * H_M / H2
    w2 = [int(m24[r].sum()) for r in range(H2)]
    out["24px_row_widths_top_to_bottom"] = w2
    out["24px_widest_row_m"] = round(y2(int(np.argmax(w2))), 1)
    out["24px_top_row_width_px"] = w2[0]
    out["24px_leg_rows_with_gap"] = sum(1 for r in range(H2) if 5 <= y2(r) <= 37 and len(runs(m24[r, m24.shape[1] // 3: 2 * m24.shape[1] // 3])) >= 2)
    # side 48: toe and heel project past the shank
    Hs = side48.shape[0]
    ys = lambda r: H_M - (r + 0.5) * H_M / Hs
    fr = [r for r in range(Hs) if ys(r) <= 1.6]
    sr = [r for r in range(Hs) if 9 <= ys(r) <= 20]
    fcols = np.where(side48[fr].any(0))[0]
    scols = np.where(side48[sr].any(0))[0]
    out["side48_foot_cols"] = [int(fcols[0]), int(fcols[-1])]
    out["side48_shank_cols"] = [int(scols[0]), int(scols[-1])]
    return out


def silhouettes():
    masks = {}
    for v in ("front", "side", "three_quarter"):
        m = crop_to_bbox(mask_of(os.path.join(RAW, f"silhouette_{v}.png")))
        masks[v] = m
        to_img(m).save(os.path.join(EV, f"silhouette_{v}_full.png"))
        for n in (180, 48, 24):
            mm = nn_resize(m, n)
            masks[(v, n)] = mm
            to_img(mm, pad=2).save(os.path.join(EV, f"silhouette_{v}_{n}px.png"))
    rng = random.Random(7)
    key = {}
    for n in (180, 48, 24):
        sheets = {"": [(v, masks[(v, n)]) for v in ("front", "side", "three_quarter")]}
        if n in (48, 24):
            dec = [("decoy_crane", nn_resize(decoy_crane(n), n)), ("decoy_tower", nn_resize(decoy_tower(n), n)),
                   ("mech_front", masks[("front", n)])]
            rng.shuffle(dec)
            key[f"{n}px"] = {chr(65 + i): k for i, (k, _) in enumerate(dec)}
            sheets["_blind"] = [(chr(65 + i), m) for i, (k, m) in enumerate(dec)]
        for suffix, items in sheets.items():
            ims = [to_img(m, 8, pad=2) for _, m in items]
            slot = max(max(i.width for i in ims), 260)
            sheet = Image.new("L", (slot * len(ims) + 40 * (len(ims) + 1), max(i.height for i in ims) + 70), 255)
            d = ImageDraw.Draw(sheet)
            for k, ((lab, _), im) in enumerate(zip(items, ims)):
                x = 40 + k * (slot + 40)
                sheet.paste(im, (x, 50))
                d.text((x, 10), f"{lab} ({n} px, shown x8)", fill=0, font=FONT)
            sheet.save(os.path.join(EV, f"silhouettes_x8_{n}px{suffix}.png"))
    json.dump({"note": "blind labels used in silhouettes_x8_48px_blind.png / _24px_blind.png (A-C); only the evaluator opens this after answering",
               "key": key}, open(os.path.join(EV, "silhouette_decoy_key.json"), "w"), indent=1)
    checks["silhouette"] = silhouette_checks(masks[("front", 48)], masks[("front", 24)], masks[("side", 48)])
    return masks


def iou(a, b):
    return float((a & b).sum() / max(1, (a | b).sum()))


def place(m, H, W):
    """paste mask m bottom-centred (by horizontal centroid) into an H x W canvas"""
    c = np.zeros((H, W), bool)
    h, w = m.shape
    cx = int(round(np.where(m)[1].mean()))
    x0 = W // 2 - cx
    c[H - h:H, max(0, x0):min(W, x0 + w)] = m[:, max(0, -x0):max(0, -x0) + min(W, x0 + w) - max(0, x0)]
    return c


def overlay_img(a, b, scale):
    rgb = np.full(a.shape + (3,), 255, np.uint8)
    rgb[a & ~b] = (40, 90, 200)
    rgb[b & ~a] = (220, 80, 60)
    rgb[a & b] = (60, 60, 60)
    im = Image.fromarray(rgb)
    return im.resize((im.width * scale, im.height * scale), Image.NEAREST)


def human_iou(masks):
    out = {}
    for n in (48, 180):
        g = masks[("front", n)]
        h = nn_resize(human_silhouette(n), n)
        W = max(g.shape[1], h.shape[1]) + 10
        A, B = place(g, n, W), place(h, n, W)
        out[f"{n}px"] = round(iou(A, B), 3)
        if n == 48:
            im = overlay_img(A, B, 8)
            d = ImageDraw.Draw(im)
            d.text((5, 5), f"48 px: greybox (blue) vs human at equal H (red), IoU {out['48px']}", fill=(0, 0, 0), font=FONT)
            im.save(os.path.join(EV, "overlay_human_iou.png"))
    checks["human_iou"] = out


def concept_masks():
    """front and middle (side/3-4) figures of the chosen concept turnaround, as masks.
    Figure = dark armour (luminance < 125) or off-white blocks (> 225) on the light grey gradient
    background; closed, hole-filled, largest connected component (drops ground shadows and specks)."""
    from scipy import ndimage as nd
    lum = np.array(Image.open(CONCEPT).convert("RGB")).astype(float).mean(2)
    res = {}
    for name, (x0, x1) in {"front": (0, 500), "middle": (500, 930)}.items():
        sub = lum[:, x0:x1]
        m = (sub < 125) | (sub > 225)
        m = nd.binary_fill_holes(nd.binary_closing(m, iterations=2))
        lab, n = nd.label(m)
        k = int(np.argmax(nd.sum(m, lab, range(1, n + 1)))) + 1
        res[name] = crop_to_bbox(lab == k)
    return res


def concept_overlay(masks):
    cm = concept_masks()
    out = {}
    tiles = []
    for cname, gname in (("front", "front"), ("middle", "side")):
        c = cm[cname]
        N = 240
        cN = nn_resize(c, N)
        gN = nn_resize(masks[gname], N)
        W = max(cN.shape[1], gN.shape[1]) + 20
        A, B = place(gN, N, W), place(cN, N, W)
        out[f"{gname}_iou"] = round(iou(A, B), 3)
        tiles.append((overlay_img(A, B, 3), f"{gname}: greybox blue, concept red, IoU {out[gname + '_iou']}"))
    W = sum(t.width for t, _ in tiles) + 60
    sheet = Image.new("RGB", (W, max(t.height for t, _ in tiles) + 50), "white")
    d = ImageDraw.Draw(sheet)
    x = 20
    for t, lab in tiles:
        sheet.paste(t, (x, 45))
        d.text((x, 10), lab, fill=(0, 0, 0), font=FONT)
        x += t.width + 20
    sheet.save(os.path.join(EV, "overlay_concept_vs_greybox.png"))
    out["concept_file"] = os.path.relpath(CONCEPT, PROJECT)
    out["note"] = "concept middle figure is a 3/4-side perspective view, the greybox is a true orthographic side; heights equalised (bbox), bottom-aligned, horizontally centroid-aligned"
    checks["concept_overlay"] = out


def flat_bands():
    im = Image.open(os.path.join(RAW, "flat_front.png")).convert("RGB")
    a = np.array(im)
    fig = (a.astype(int).sum(2) < 740)
    rows, cols = np.where(fig.any(1))[0], np.where(fig.any(0))[0]
    crop = a[rows[0]:rows[-1] + 1, cols[0]:cols[-1] + 1]
    m = fig[rows[0]:rows[-1] + 1, cols[0]:cols[-1] + 1]
    h = 180
    H, W = m.shape
    w = int(round(W * h / H))
    yi = ((np.arange(h) + 0.5) * H / h).astype(int)
    xi = ((np.arange(w) + 0.5) * W / w).astype(int)
    small = crop[yi][:, xi]
    sm = m[yi][:, xi]
    small[~sm] = 255
    Image.fromarray(small).save(os.path.join(EV, "flat_front_180px.png"))
    Image.fromarray(small).resize((w * 4, h * 4), Image.NEAREST).save(os.path.join(EV, "flat_front_180px_x4.png"))
    px = small[sm].reshape(-1, 3)
    lum = (0.2126 * px[:, 0] + 0.7152 * px[:, 1] + 0.0722 * px[:, 2]).round().astype(int)
    vals, counts = np.unique(lum // 8, return_counts=True)       # 8-level luminance bins
    share = counts / counts.sum()
    bands = [(int(v * 8), round(float(s), 4)) for v, s in zip(vals, share) if s >= 0.01]
    checks["flat_180px"] = {"value_bands_ge_1pct": bands, "n_bands": len(bands),
                            "note": "flat Workbench colours (no lighting); bands = luminance bins of 8 levels holding >= 1 % of figure pixels"}


def rom_sheet():
    poses = ["rest", "guard", "crouch", "arms_overhead"]
    ims = []
    for p in poses:
        for v in ("front", "side"):
            f = os.path.join(RAW, f"pose_{p}_{v}.png")
            if os.path.exists(f):
                ims.append((Image.open(f).convert("RGB").resize((320, 360)), f"{p} / {v}"))
    sheet = Image.new("RGB", (320 * 4, 400 * 2), "white")
    d = ImageDraw.Draw(sheet)
    for i, (im, lab) in enumerate(ims):
        x, y = (i // 2) * 320, (i % 2) * 400
        sheet.paste(im, (x, y + 36))
        d.text((x + 6, y + 6), lab, fill=(0, 0, 0), font=FONT)
    sheet.save(os.path.join(EV, "rom_contact_sheet.png"))


if __name__ == "__main__":
    turnarounds()
    masks = silhouettes()
    human_iou(masks)
    concept_overlay(masks)
    flat_bands()
    rom_sheet()
    json.dump(checks, open(os.path.join(EV, "pixel_checks.json"), "w"), indent=1)
    print(json.dumps({k: (v if k != "silhouette" else {a: b for a, b in v.items() if "row_widths" not in a}) for k, v in checks.items()}, indent=0)[:3000])
