"""M-122 store asset spec compliance. Table from R-120 section 5, version-stamped 2026-11-15 (docs/store/capture_plan.md)."""
from __future__ import annotations

import glob
import json
import os
import shutil
import subprocess

SPEC_VERSION = "2026-11-15"
# name -> (allowed (w,h) tuples or None, extensions, alpha required, required?)
ASSETS = {
    "header_capsule": dict(sizes=[(920, 430)], required=True),
    "small_capsule": dict(sizes=[(462, 174)], required=True),
    "main_capsule": dict(sizes=[(1232, 706)], required=True),
    "vertical_capsule": dict(sizes=[(748, 896)], required=True),
    "library_capsule": dict(sizes=[(600, 900)], required=True),
    "library_hero": dict(sizes=[(3840, 1240)], required=True),
    "library_logo": dict(sizes=[(1280, None), (None, 720)], alpha=True, required=True),
    "client_icon": dict(sizes=[(184, 184)], exts=(".jpg", ".jpeg"), required=True),
    "community_icon": dict(sizes=[(256, 256)], required=True),
    "page_background": dict(sizes=[(1438, 810)], required=False),
}
SHOT_MIN_COUNT, SHOT_TARGET, DEMO_SHOT_MIN = 5, 8, 5
SHOT_MIN_WH = (1920, 1080)
TRAILER_FPS = (30.0, 29.97, 60.0, 59.94)
TRAILER_MIN_KBPS = 5000
TRAILER_MAX_WH = (1920, 1080)
AUDIO_RATES = (44100, 48000)


def image_info(path):
    from PIL import Image
    with Image.open(path) as im:  # header only
        return {"w": im.size[0], "h": im.size[1], "mode": im.mode, "format": im.format,
                "alpha": im.mode in ("RGBA", "LA", "PA") or "transparency" in im.info}


def _row(name, ok, detail):
    return {"name": name, "ok": bool(ok), "detail": detail}


def _size_ok(w, h, sizes):
    for sw, sh in sizes:
        if (sw is None or sw == w) and (sh is None or sh == h):
            return True
    return False


def check_assets(asset_dir, demo=False):
    rows = []
    for name, rule in ASSETS.items():
        exts = rule.get("exts", (".png", ".jpg", ".jpeg"))
        hits = [p for e in exts for p in glob.glob(os.path.join(asset_dir, f"{name}{e}"))]
        if not hits:
            if rule["required"]:
                rows.append(_row(name, False, "missing"))
            continue
        info = image_info(hits[0])
        ok = _size_ok(info["w"], info["h"], rule["sizes"])
        det = f'{info["w"]}x{info["h"]} {info["format"]}'
        if rule.get("alpha") and not info["alpha"]:
            ok, det = False, det + " no alpha channel"
        if not ok and "alpha" not in det:
            det += f" (want {rule['sizes']})"
        rows.append(_row(name, ok, det))
    shots = sorted(glob.glob(os.path.join(asset_dir, "screenshots", "*.png")) +
                   glob.glob(os.path.join(asset_dir, "screenshots", "*.jpg")))
    bad = []
    for p in shots:
        i = image_info(p)
        if i["w"] < SHOT_MIN_WH[0] or i["h"] < SHOT_MIN_WH[1] or i["w"] * 9 != i["h"] * 16:
            bad.append(f'{os.path.basename(p)} {i["w"]}x{i["h"]}')
    rows.append(_row("screenshots_count", len(shots) >= SHOT_MIN_COUNT,
                     f"{len(shots)} found, min {SHOT_MIN_COUNT}, target {SHOT_TARGET}"))
    rows.append(_row("screenshots_size", not bad, "all >= 1920x1080 and exactly 16:9" if not bad else "bad: " + "; ".join(bad)))
    if demo:
        n = len(glob.glob(os.path.join(asset_dir, "demo_screenshots", "*.png")))
        rows.append(_row("demo_screenshots_count", n >= DEMO_SHOT_MIN, f"{n} found, min {DEMO_SHOT_MIN}"))
    return rows


def probe_trailer(path):
    if not shutil.which("ffprobe"):
        return None
    out = subprocess.run(["ffprobe", "-v", "error", "-print_format", "json", "-show_format", "-show_streams", path],
                         capture_output=True, text=True)
    return json.loads(out.stdout) if out.returncode == 0 else None


def _fps(s):
    try:
        n, d = s.split("/")
        return float(n) / float(d)
    except Exception:
        return 0.0


def check_trailer_probe(pr, ext=".mp4"):
    """pr = ffprobe JSON. Pure, so it is testable without ffmpeg."""
    rows = []
    v = next((s for s in pr.get("streams", []) if s.get("codec_type") == "video"), None)
    a = next((s for s in pr.get("streams", []) if s.get("codec_type") == "audio"), None)
    rows.append(_row("trailer_container", ext == ".mp4" and "mp4" in pr.get("format", {}).get("format_name", ""),
                     pr.get("format", {}).get("format_name", "?")))
    if not v:
        return rows + [_row("trailer_video", False, "no video stream")]
    w, h = int(v["width"]), int(v["height"])
    rows.append(_row("trailer_codec", v.get("codec_name") == "h264", v.get("codec_name")))
    rows.append(_row("trailer_size", w <= TRAILER_MAX_WH[0] and h <= TRAILER_MAX_WH[1] and w * 9 == h * 16, f"{w}x{h}"))
    fps = _fps(v.get("avg_frame_rate", "0/1"))
    rows.append(_row("trailer_fps", any(abs(fps - f) < 0.02 for f in TRAILER_FPS), f"{fps:.3f}"))
    br = float(v.get("bit_rate") or pr.get("format", {}).get("bit_rate") or 0) / 1000
    rows.append(_row("trailer_bitrate", br >= TRAILER_MIN_KBPS, f"{br:.0f} kbps (min {TRAILER_MIN_KBPS})"))
    rows.append(_row("trailer_audio", bool(a) and a.get("codec_name") == "aac" and int(a.get("sample_rate", 0)) in AUDIO_RATES,
                     f'{a.get("codec_name")} {a.get("sample_rate")} Hz' if a else "no audio stream"))
    return rows


def run(asset_dir, trailer=None, demo=False):
    rows = check_assets(asset_dir, demo)
    if trailer:
        pr = probe_trailer(trailer)
        rows += check_trailer_probe(pr, os.path.splitext(trailer)[1].lower()) if pr else [
            _row("trailer_probe", False, "ffprobe unavailable or failed")]
    return {"check": "M-122", "spec_version": SPEC_VERSION, "rows": rows, "pass": all(r["ok"] for r in rows)}
