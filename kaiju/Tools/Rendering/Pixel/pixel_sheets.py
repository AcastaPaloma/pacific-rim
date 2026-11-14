#!/usr/bin/env python
"""T-004 evidence images built from captures (kaiju conda env). Captures are only tiled, labelled and enlarged with
nearest-neighbour sampling; no pixel of a capture is altered.
  strip <out.png> <label=sequence_dir> ...   stage A/B strip: first frame of each sequence, labelled
  contact <out.png> <sequence_dir> [--every n] [--cols c]   contact sheet of a sequence
  swatches <out.png> <palette_set.json> <captures_m003_stills>   per profile: palette, haze ramp, 30 km depth-ramp strip
  board <out.png> <style_refs_dir> <label=frame.png> ...   reference panels beside captures (for the panel)
"""
import argparse, glob, json, math, os, sys
import numpy as np
from PIL import Image, ImageDraw


def first_internal(d):
    meta = json.load(open(os.path.join(d, "sequence.json")))
    f = json.load(open(os.path.join(d, meta["frames"][0])))
    return Image.open(os.path.join(d, f["files"]["color_internal"])).convert("RGB")


def label(im, text, h=14):
    out = Image.new("RGB", (im.width, im.height + h), (24, 24, 24))
    out.paste(im, (0, h)); ImageDraw.Draw(out).text((3, 1), text, fill=(235, 235, 235))
    return out


def tile(images, cols, pad=4, bg=(40, 40, 40)):
    w = max(i.width for i in images); h = max(i.height for i in images); rows = (len(images) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * (w + pad) + pad, rows * (h + pad) + pad), bg)
    for n, im in enumerate(images): sheet.paste(im, (pad + (n % cols) * (w + pad), pad + (n // cols) * (h + pad)))
    return sheet


def cmd_strip(a):
    ims = []
    for item in a.items:
        name, d = item.split("=", 1)
        im = first_internal(d)
        if im.width > 480: im = im.resize((480, 270), Image.NEAREST)   # the low-res-off frame is shown reduced; the file itself is in the capture set
        ims.append(label(im, name))
    tile(ims, a.cols).save(a.out)


def cmd_contact(a):
    meta = json.load(open(os.path.join(a.sequence_dir, "sequence.json")))
    ims = []
    for n in meta["frames"][:: a.every]:
        f = json.load(open(os.path.join(a.sequence_dir, n)))
        im = Image.open(os.path.join(a.sequence_dir, f["files"]["color_internal"])).convert("RGB")
        ims.append(label(im.resize((im.width // 2, im.height // 2), Image.NEAREST), "f%06d" % f["frame"]["index"], 12))
    tile(ims, a.cols, 2).save(a.out)


def cmd_swatches(a):
    pal = json.load(open(a.palette_set))["profiles"]; rows = []
    for pid, p in pal.items():
        sw = 22; n = len(p["colours"])
        im = Image.new("RGB", (24 * sw + 8 + 8 * sw + 8 + 420, 3 * sw + 16), (30, 30, 30)); d = ImageDraw.Draw(im)
        d.text((2, 1), pid + "  palette (accent underlined) | haze ramp ramp_uniform_L | depth ramp 10 m - 30 km from a capture", fill=(230, 230, 230))
        for i, c in enumerate(p["colours"]):
            d.rectangle([i * sw, 14, i * sw + sw - 2, 14 + sw - 2], fill=c["hex"])
            if c["role"] == "accent": d.rectangle([i * sw, 14 + sw, i * sw + sw - 2, 14 + sw + 2], fill=(255, 255, 255))
            if c["role"].startswith("ramp"): d.rectangle([i * sw, 14 + sw + 4, i * sw + sw - 2, 14 + sw + 5], fill=(120, 120, 120))
        x0 = 24 * sw + 8
        for i, c in enumerate(p["ramp_uniform_L"]): d.rectangle([x0 + i * sw, 14, x0 + i * sw + sw - 2, 14 + sw - 2], fill=c["hex"])
        # Depth-ramp strip: the most common colour of ground pixels in 60 log-distance bins, read from the eye70 stills.
        seq = os.path.join(a.stills, pid + "_eye70"); x1 = x0 + 8 * sw + 8
        if os.path.isdir(seq):
            meta = json.load(open(os.path.join(seq, "sequence.json"))); bins = [dict() for _ in range(60)]
            for nme in meta["frames"]:
                f = json.load(open(os.path.join(seq, nme))); c = f["camera"]
                rgb = np.array(Image.open(os.path.join(seq, f["files"]["color_internal"])).convert("RGB"))
                v = np.array(Image.open(os.path.join(seq, f["files"]["depth"]))).astype(np.float64)
                z = c["near_m"] * (c["far_m"] / c["near_m"]) ** (v / 65535.0); ok = v < 65535
                b = np.clip(((np.log10(np.maximum(z, 10.0)) - 1.0) / (math.log10(30000.0) - 1.0) * 60).astype(int), 0, 59)
                for bi in range(60):
                    sel = ok & (b == bi)
                    if sel.any():
                        cols, cnt = np.unique(rgb[sel], axis=0, return_counts=True)
                        for col, k in zip(cols, cnt): bins[bi][tuple(col)] = bins[bi].get(tuple(col), 0) + int(k)
            for bi in range(60):
                if bins[bi]:
                    ranked = sorted(bins[bi].items(), key=lambda t: -t[1]); total = sum(bins[bi].values()); y = 14
                    for col, k in ranked[:3]:   # up to three colours, heights by share: shows the dithered mixes
                        hgt = max(1, int(round((2 * sw) * k / total)))
                        y_end = min(14 + 2 * sw, y + hgt)
                        if y_end > y: d.rectangle([x1 + bi * 7, y, x1 + bi * 7 + 6, y_end], fill=tuple(int(v) for v in col))
                        y = y_end
        rows.append(im)
    tile(rows, 1).save(a.out)


def cmd_board(a):
    refs = sorted(glob.glob(os.path.join(a.refs, "*.png")) + glob.glob(os.path.join(a.refs, "*.jpg")))
    seen, ims = set(), []
    for r in refs:
        im = Image.open(r).convert("RGB"); key = im.resize((16, 16)).tobytes()
        if key in seen: continue   # ref_01 and ref_02 are identical (Q-022)
        seen.add(key); im.thumbnail((480, 480)); ims.append(label(im, "reference (style only): " + os.path.basename(r)))
    for item in a.items:
        name, p = item.split("=", 1)
        im = Image.open(p).convert("RGB")
        if im.width > 960: im = im.resize((im.width // 4, im.height // 4), Image.NEAREST)
        ims.append(label(im, "capture: " + name))
    tile(ims, a.cols).save(a.out)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("strip"); p.add_argument("out"); p.add_argument("items", nargs="+"); p.add_argument("--cols", type=int, default=4)
    p = sub.add_parser("contact"); p.add_argument("out"); p.add_argument("sequence_dir"); p.add_argument("--every", type=int, default=10); p.add_argument("--cols", type=int, default=6)
    p = sub.add_parser("swatches"); p.add_argument("out"); p.add_argument("palette_set"); p.add_argument("stills")
    p = sub.add_parser("board"); p.add_argument("out"); p.add_argument("refs"); p.add_argument("items", nargs="+"); p.add_argument("--cols", type=int, default=3)
    a = ap.parse_args()
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    {"strip": cmd_strip, "contact": cmd_contact, "swatches": cmd_swatches, "board": cmd_board}[a.cmd](a)
    print("wrote", a.out)


if __name__ == "__main__":
    sys.exit(main())
