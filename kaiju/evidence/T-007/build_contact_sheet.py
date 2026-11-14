"""Rebuild evidence/T-007/concept_contact_sheet.png (E-1): every kept concept, grouped by layer,
labelled with its job ID, plus the generation count.

Usage: <project conda env python, see environment.yml> evidence/T-007/build_contact_sheet.py
"""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
K = ROOT / "art/concepts/mech_frame_balanced"
OUT = ROOT / "evidence/T-007/concept_contact_sheet.png"
GEN_COUNT = "15 of 16 generations used"

ROWS = [
    ("Layer 1 - silhouette explorations", [
        ("layer1_silhouettes-A_030477f2-cd72-437d-9a46-c1352f153159_0.jpg", "silhouettes-A  job 030477f2  img 0"),
        ("layer1_silhouettes-B_030477f2-cd72-437d-9a46-c1352f153159_2.jpg", "silhouettes-B  job 030477f2  img 2"),
    ]),
    ("Layer 2 - orthographic turnaround", [
        ("layer2_turnaround-final_2fa84a4e-3d89-445e-84a3-bebe971a8b3c_2.jpg", "turnaround-final  job 2fa84a4e  img 2"),
        ("layer2_turnaround_4d4eda80-57f4-4f41-8a02-a1c23192d6a5_1.jpg", "turnaround  job 4d4eda80  img 1"),
    ]),
    ("Layer 3 - plate breakup and joints", [
        ("layer3_joints_b1c655d7-811f-4413-85da-dbd5d55c2346_3.jpg", "joints  job b1c655d7  img 3"),
        ("layer3_plate-breakup_77faf417-aabb-4415-afb6-788705e1c8f4_2.jpg", "plate-breakup  job 77faf417  img 2"),
    ]),
    ("Layer 4 - mood frame v2 (current; fix cycle 2)", [
        ("layer4_mood-v2_af67b340-b21d-4861-b15d-57f351cebe0c_0.png",
         "mood-v2  job af67b340  img 0  (CURRENT; masts not to be followed)"),
    ]),
    ("Layer 4 - mood frame v1 (superseded by v2, kept as history)", [
        ("layer4_mood-cold_6ba8d2aa-8808-4122-8f68-af97d7ed2be4_1.png", "mood-cold  job 6ba8d2aa  img 1  (SUPERSEDED)"),
        ("layer4_mood-warm_c3ce3ab4-7bd4-45bf-9d83-49b30fe05d97_2.png", "mood-warm  job c3ce3ab4  img 2  (SUPERSEDED)"),
    ]),
]

W, PAD, TILE_W, LABEL_H, HEAD_H = 1328, 16, 640, 26, 34


def font(size):
    for f in ("/System/Library/Fonts/Helvetica.ttc", "/System/Library/Fonts/Supplemental/Arial.ttf"):
        try:
            return ImageFont.truetype(f, size)
        except OSError:
            pass
    return ImageFont.load_default()


def main():
    f_title, f_head, f_lab = font(22), font(18), font(13)
    blocks = []
    for head, items in ROWS:
        tiles = []
        for name, label in items:
            im = Image.open(K / name).convert("RGB")
            tw = TILE_W if len(items) > 1 else W - 2 * PAD
            th = round(im.height * tw / im.width)
            tiles.append((im.resize((tw, th), Image.LANCZOS), label))
        blocks.append((head, tiles, max(t[0].height for t in tiles)))
    H = PAD + 40 + sum(HEAD_H + h + LABEL_H + PAD for _, _, h in blocks)
    sheet = Image.new("RGB", (W, H), "white")
    d = ImageDraw.Draw(sheet)
    d.text((PAD, PAD), f"T-007 Balanced chassis - Midjourney concept layers (AI-generated, 2026-11-14) - {GEN_COUNT}",
           fill="black", font=f_title)
    y = PAD + 40
    for head, tiles, h in blocks:
        d.text((PAD, y + 6), head, fill="black", font=f_head)
        y += HEAD_H
        x = PAD
        for im, label in tiles:
            sheet.paste(im, (x, y))
            d.text((x, y + im.height + 5), label, fill="black", font=f_lab)
            x += im.width + PAD
        y += h + LABEL_H + PAD
    sheet.save(OUT)
    print(OUT, sheet.size)


if __name__ == "__main__":
    main()
