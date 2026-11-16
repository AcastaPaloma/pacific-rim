"""M-122 store assets (T-195): Steam capsules, library art, logotype and icons built reproducibly from the pixel-look stills in docs/store/sources.

Rules: art is integer nearest-neighbour oversize then CROP (never resampled, R-120 section 5); the logotype is drawn from the hand-defined 5x7 pixel font below
at one integer scale per asset. No franchise names, no AI-generated pixels. The title text and the logotype are TEMPORARY placeholders (manifest says so).
Deterministic: same sources + same code = same bytes (PNG optimize, JPEG fixed quality)."""
from __future__ import annotations

import hashlib
import json
import os

from PIL import Image

TITLE = "KAIJU"  # TEMPORARY working title (productName KaijuGame); replace with the founders' name + logotype art
FONT = {  # 5x7 glyphs, hand-defined for this project
    "K": ["X...X", "X..X.", "X.X..", "XX...", "X.X..", "X..X.", "X...X"],
    "A": [".XXX.", "X...X", "X...X", "XXXXX", "X...X", "X...X", "X...X"],
    "I": ["XXXXX", "..X..", "..X..", "..X..", "..X..", "..X..", "XXXXX"],
    "J": ["..XXX", "...X.", "...X.", "...X.", "...X.", "X..X.", ".XX.."],
    "U": ["X...X", "X...X", "X...X", "X...X", "X...X", "X...X", ".XXX."],
}
FILL, EDGE = (244, 232, 214, 255), (24, 20, 32, 255)
SRC_W, SRC_H = 480, 270
HERO_SRC, CAPSULE_SRC, ICON_SRC = "warm_amber_haze", "lavender_peach", "slate_teal"
CAPS = {"header_capsule": (920, 430), "small_capsule": (462, 174), "main_capsule": (1232, 706), "vertical_capsule": (748, 896), "library_capsule": (600, 900)}
LOGO_FRAC = {"header_capsule": 0.62, "small_capsule": 0.86, "main_capsule": 0.55, "vertical_capsule": 0.78, "library_capsule": 0.78}
SAFE_MARGIN = 0.05  # logo bbox must stay >= 5 % from every edge (capsules and icons)
MAX_BYTES = 3_000_000


def logo_cells(text=TITLE):
    """Logo as a 1-px cell mask (fill + outline), size (w, h) in cells."""
    cols = len(text) * 5 + (len(text) - 1)
    w, h = cols + 2, 9
    fill = set()
    for i, ch in enumerate(text):
        for y, row in enumerate(FONT[ch]):
            for x, c in enumerate(row):
                if c == "X":
                    fill.add((1 + i * 6 + x, 1 + y))
    edge = {(x + dx, y + dy) for x, y in fill for dx in (-1, 0, 1) for dy in (-1, 0, 1)} - fill
    return fill, edge, w, h


def render_logo(k, canvas=None, text=TITLE):
    """RGBA logotype at integer scale k, centred on a transparent canvas (default tight)."""
    fill, edge, w, h = logo_cells(text)
    cw, ch = canvas or (w * k, h * k)
    im = Image.new("RGBA", (cw, ch), (0, 0, 0, 0))
    ox, oy = (cw - w * k) // 2, (ch - h * k) // 2
    px = im.load()
    for cells, col in ((edge, EDGE), (fill, FILL)):
        for x, y in cells:
            for yy in range(k):
                for xx in range(k):
                    px[ox + x * k + xx, oy + y * k + yy] = col
    return im


def load_src(src_dir, name):
    return Image.open(os.path.join(src_dir, name + ".png")).convert("RGB")


def cover(src, w, h):
    """Smallest integer nearest-neighbour scale that covers (w, h), then a centre crop."""
    k = max(-(-w // src.width), -(-h // src.height))
    big = src.resize((src.width * k, src.height * k), Image.NEAREST)
    l, t = (big.width - w) // 2, (big.height - h) // 2
    return big.crop((l, t, l + w, t + h)), k


def with_logo(bg, logo_frac, dy=0):
    w, h = bg.size
    fill, edge, lw, lh = logo_cells()
    k = max(1, int(w * logo_frac) // lw)
    while lh * k > h * 0.6 and k > 1:
        k -= 1
    logo = render_logo(k)
    out = bg.convert("RGBA")
    out.alpha_composite(logo, ((w - logo.width) // 2, (h - logo.height) // 2 + dy))
    return out.convert("RGB"), logo.size, k


def build(src_dir, out_dir):
    os.makedirs(os.path.join(out_dir, "screenshots"), exist_ok=True)
    man = {"schema": "store_assets@v1", "status": "TEMPORARY", "title_text": TITLE,
           "title_note": "TEMPORARY placeholder title and logotype: hand-defined 5x7 pixel font in capture_tools/store.py; replace with the founders' name and art before a public store page.",
           "ip": "no franchise names/designs; no AI-generated pixels (sources are engine captures of the pixel pipeline; font is hand-defined)", "assets": {}}

    def save(name, im, fmt="PNG", sub="", meta=None):
        p = os.path.join(out_dir, sub, name)
        kw = {"optimize": True} if fmt == "PNG" else ({"quality": 95, "subsampling": 0, "optimize": True} if fmt == "JPEG" else {})
        im.save(p, fmt, **kw)
        data = open(p, "rb").read()
        key = os.path.join(sub, name)
        man["assets"][key] = dict(meta or {}, size=list(im.size), bytes=len(data), sha256=hashlib.sha256(data).hexdigest())

    for n, (w, h) in CAPS.items():
        bg, k = cover(load_src(src_dir, CAPSULE_SRC), w, h)
        im, lsz, lk = with_logo(bg, LOGO_FRAC[n])
        save(n + ".png", im, meta={"source": CAPSULE_SRC, "bg_int_scale": k, "logo_px": list(lsz), "logo_scale": lk, "text": "title only", "status": "TEMPORARY"})
    hero, k = cover(load_src(src_dir, HERO_SRC), 3840, 1240)
    save("library_hero.png", hero, meta={"source": HERO_SRC, "bg_int_scale": k, "text": "none (artwork only)", "status": "art from engine capture; final hero pending real gameplay stills"})
    fill, edge, lw, lh = logo_cells()
    kk = 1280 // lw
    logo = render_logo(kk, canvas=(1280, lh * kk))
    save("library_logo.png", logo, meta={"logo_scale": kk, "text": TITLE, "status": "TEMPORARY"})
    for n, (px, fmt, ext) in {"client_icon": (184, "JPEG", ".jpg"), "community_icon": (256, "PNG", ".png")}.items():
        bg, k = cover(load_src(src_dir, ICON_SRC), px, px)
        k_g = int(px * (1 - 2 * SAFE_MARGIN - 0.1)) // 9
        glyph = render_logo(k_g, text="K")
        out = bg.convert("RGBA")
        out.alpha_composite(glyph, ((px - glyph.width) // 2, (px - glyph.height) // 2))
        save(n + ext, out.convert("RGB"), fmt, meta={"source": ICON_SRC, "bg_int_scale": k, "glyph": "K", "glyph_px": list(glyph.size), "status": "TEMPORARY"})
    ico = render_logo(max(1, 200 // 9), text="K")
    sc = Image.new("RGBA", (256, 256), (0, 0, 0, 0))
    sc.alpha_composite(ico, ((256 - ico.width) // 2, (256 - ico.height) // 2))
    save("shortcut_icon.png", sc, meta={"glyph": "K", "status": "TEMPORARY"})
    shots = []
    for n in ("lavender_peach", "slate_crimson", "slate_teal", "warm_amber_haze", "warm_rust_sepia"):
        shots.append((f"tile_{n}.png", load_src(src_dir, n).resize((1920, 1080), Image.NEAREST), n + " (x4 nearest of the 480x270 tile test-scene still)"))
    for n in ("v3_gate", "v4_street", "v5_skyline"):
        shots.append((f"hud_{n}.png", Image.open(os.path.join(src_dir, f"hud_slice_{n}.png")).convert("RGB"), "T-191 HUD composite (not live IMGUI)"))
    for n, im, src in shots:
        save(n, im, sub="screenshots", meta={"source": src, "status": "TEMPORARY: tile test scene / composite, replace with demo-build gameplay captures"})
    with open(os.path.join(out_dir, "manifest.json"), "w") as f:
        json.dump(man, f, indent=1, sort_keys=True)
    return man


def safe_zone_report(out_dir):
    """Logo bbox (from the manifest geometry) must lie inside the canvas with SAFE_MARGIN on every side."""
    man = json.load(open(os.path.join(out_dir, "manifest.json")))
    rows = []
    for n in list(CAPS):
        a = man["assets"][n + ".png"]
        (w, h), (lw, lh) = a["size"], a["logo_px"]
        mx, my = (w - lw) / 2 / w, (h - lh) / 2 / h
        rows.append((n, min(mx, my) >= SAFE_MARGIN, round(min(mx, my), 3), lw / w))
    for n, ext in (("client_icon", ".jpg"), ("community_icon", ".png")):
        a = man["assets"][n + ext]
        (w, h), (lw, lh) = a["size"], a["glyph_px"]
        mx, my = (w - lw) / 2 / w, (h - lh) / 2 / h
        rows.append((n, min(mx, my) >= SAFE_MARGIN, round(min(mx, my), 3), lw / w))
    return rows


if __name__ == "__main__":
    import sys
    here = os.path.dirname(os.path.abspath(__file__))
    root = os.path.abspath(os.path.join(here, "..", "..", ".."))
    build(sys.argv[1] if len(sys.argv) > 1 else os.path.join(root, "docs", "store", "sources"), sys.argv[2] if len(sys.argv) > 2 else os.path.join(root, "docs", "store", "assets"))
