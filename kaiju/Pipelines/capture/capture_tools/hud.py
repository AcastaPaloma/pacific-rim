"""HUD-on pass (T-191). The slice HUD is IMGUI text drawn over the presented frame (SliceBootstrap.OnGUI): it is not part of the pixel pipeline and a batch editor cannot
screen-capture it (T-189). So the HUD-on frames are world frames from the T-147 driver (real tile, shipping pixel pipeline, UI off) with the HUD text composited at the
same place, from the same strings: the composite is exact in layout and text, NOT a live IMGUI render (font rasteriser differs) and its state values are scene definitions
(docs/store/hud_scenes.json), not a running simulation. Every output sidecar says so."""
from __future__ import annotations

import hashlib
import json
import os
import re

from PIL import Image, ImageDraw, ImageFont

FONT_CANDIDATES = ("/System/Library/Fonts/Supplemental/Arial.ttf", "/Library/Fonts/Arial.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf")
RIG_CS = os.path.join(os.path.dirname(__file__), "..", "..", "..", "Assets", "Game", "Slice", "Runtime", "SliceFeelRig.cs")
STATES = ("Idle", "Approach", "Flank", "Charge", "Retreat", "Withdrawn")   # Game.AI BrainState


def help_text():
    """SliceFeelRig.Help, read from the source so the composite cannot drift from the game."""
    m = re.search(r'public const string Help = "([^"]*)"', open(RIG_CS, encoding="utf-8").read())
    if not m:
        raise RuntimeError("SliceFeelRig.Help not found in " + RIG_CS)
    return m.group(1)


def hud_lines(v, help_=None):
    """The text SliceBootstrap.OnGUI builds for the feelcheck config (line 1: title + fps + destruction; 2: help + kaiju + mech hp; 3: SliceFeelRig.HudLine; 4: last message)."""
    help_ = help_ or help_text()
    l1 = ("Kaiju feel check (TEMPORARY: capsule mech and kaiju; mech body awaits T-005)  %d fps   buildings destroyed %d  damaged events %d"
          % (v.get("fps", 60), v.get("destroyed", 0), v.get("damaged_events", 0)))
    l2 = "%s   kaiju %s d=%d m   mech hp %.2f" % (help_, v.get("kaiju_state", "Approach"), v.get("kaiju_distance_m", 340), v.get("mech_hp", 1.0))
    l3 = ("ammo %d/%d   kaiju hp %d%%%s   shots %d hits k%d/b%d" % (v.get("ammo", 20), v.get("max_ammo", 20), round(v.get("kaiju_hp_frac", 1.0) * 100),
                                                                    " (down)" if v.get("kaiju_down") else "", v.get("shots", 0), v.get("hits_kaiju", 0), v.get("hits_building", 0))
          + "   hangar cycles %d" % v.get("hangar_cycles", 0))
    return [l1, l2, l3, v.get("message", "")]


def _font(px):
    for p in FONT_CANDIDATES:
        if os.path.exists(p):
            return ImageFont.truetype(p, px), p
    return ImageFont.load_default(), "PIL default"


def compose(frame_png, lines, out_png, font_px=12, origin=(10, 10), line_gap=3):
    im = Image.open(frame_png).convert("RGB")
    f, fpath = _font(font_px)
    d = ImageDraw.Draw(im)
    x, y = origin
    for ln in lines:
        d.text((x, y), ln, font=f, fill=(255, 255, 255))
        y += font_px + line_gap
    im.save(out_png)
    return {"font": os.path.basename(fpath), "font_px": font_px, "origin_px": list(origin), "size": list(im.size)}


def _sha(p):
    return hashlib.sha256(open(p, "rb").read()).hexdigest()


def run(captures_dir, scenes_json, out_dir, scale="x4"):
    """For each scene {id, path_id, frame_index, values}: take <captures>/<path_id>/<scale>/f%06d_color_output.png, composite, write <out>/<id>.png and <id>.json."""
    os.makedirs(out_dir, exist_ok=True)
    spec = json.load(open(scenes_json))
    rows = []
    for sc in spec["scenes"]:
        src = os.path.join(captures_dir, sc["path_id"], scale, "f%06d_color_output.png" % sc.get("frame_index", 0))
        row = {"id": sc["id"], "source": src, "ok": False}
        if not os.path.exists(src):
            row["error"] = "source frame missing"
            rows.append(row)
            continue
        lines = hud_lines(sc.get("values", {}))
        out_png = os.path.join(out_dir, sc["id"] + ".png")
        meta = compose(src, lines, out_png, spec.get("font_px", 12))
        side = {"schema": "hud_frame@v1", "id": sc["id"], "composite": True, "live_imgui": False,
                "note": "HUD text composited over a driver frame; strings follow SliceBootstrap.OnGUI (feelcheck); state values are scene definitions, not a running simulation",
                "source_frame": os.path.relpath(src, captures_dir).replace("\\", "/"), "source_sha256": _sha(src), "output_sha256": _sha(out_png),
                "hud_lines": lines, **meta}
        json.dump(side, open(os.path.join(out_dir, sc["id"] + ".json"), "w"), indent=1)
        row.update(ok=True, out=out_png, size=meta["size"], hud_lines=len(lines))
        rows.append(row)
    return {"check": "hud", "scenes": rows, "pass": bool(rows) and all(r["ok"] for r in rows)}
