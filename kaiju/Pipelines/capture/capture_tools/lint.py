"""capture_path@v1 lint (docs/store/capture_plan.md section 3). Rule ids L01..L17 are the same as the C# CapturePath.Lint
(Assets/Game/Tools/Capture/Core), so a file that passes here passes the Unity driver and the other way round.
L16 (Python only, needs the whole directory): path_id unique across files."""
from __future__ import annotations

import glob
import json
import math
import os
import re

SCHEMA = "capture_path@v1"
PROFILES = ("warm_rust_sepia", "warm_amber_haze", "slate_teal", "lavender_peach", "slate_crimson")
REQUIRES = ("walk", "kaiju", "m2_streaming", "hud", "coop", "logotype", "mech_mesh")
INTERP = ("linear", "catmull_rom", "ease_in_out")
PURPOSES = ("M-003", "M-004")
# M-004 classes (Tools/Metrics registry M-004@v1.1): id -> (type, nominal internal px/frame, frames); same table as C# CapturePath.MotionClasses
MOTION_CLASSES = {"S0": ("static", 0.0, 60), "T1": ("translate", 0.1, 180), "T2": ("translate", 0.25, 180), "T3": ("translate", 1.0, 180),
                  "Y1": ("yaw", 0.25, 180), "Y2": ("yaw", 1.0, 180)}
ID_RX, TILE_RX = re.compile(r"^[a-z0-9_]+$"), re.compile(r"^L0_[0-9]+_[0-9]+$")


def _num(v):
    return isinstance(v, (int, float)) and not isinstance(v, bool)


def _pair(v):
    return isinstance(v, list) and len(v) == 2 and all(_num(x) for x in v)


def lint_path(d) -> list:
    """Rule violations of one parsed capture_path@v1 object, as 'Lnn message' strings (empty = clean)."""
    e = []
    if not isinstance(d, dict):
        return ["L00 root is not an object"]
    if d.get("schema") != SCHEMA:
        e.append(f"L01 schema must be {SCHEMA}")
    pid = d.get("path_id")
    if not isinstance(pid, str) or not ID_RX.match(pid):
        e.append("L02 path_id must match [a-z0-9_]+")
    fps, dur = d.get("fps"), d.get("duration_s")
    if fps not in (30, 60) or isinstance(fps, bool):
        e.append("L03 fps must be 30 or 60")
    if not _num(dur) or dur <= 0:
        e.append("L04 duration_s must be > 0")
    elif fps in (30, 60) and abs(dur * fps - round(dur * fps)) > 1e-6:
        e.append("L04 duration_s * fps must be an integer")
    interp = d.get("interpolation")
    if interp not in INTERP:
        e.append("L06 interpolation must be linear, catmull_rom or ease_in_out")
    kfs = d.get("keyframes")
    if not isinstance(kfs, list):
        e.append("L05 keyframes missing")
        kfs = []
    min_keys = 4 if interp == "catmull_rom" else 2
    navd = agl = 0
    for i, k in enumerate(kfs):
        if not isinstance(k, dict):
            e.append(f"L10 keyframe {i} is not an object")
            continue
        if not _pair(k.get("pos_baytm")):
            e.append(f"L10 keyframe {i}: pos_baytm must be [east, north]")
        has_agl, has_navd = _num(k.get("alt_m")), _num(k.get("alt_navd88"))
        if has_agl == has_navd:
            e.append(f"L10 keyframe {i}: exactly one of alt_m (above ground) and alt_navd88 is required")
        else:
            navd += has_navd
            agl += has_agl
        if not all(_num(k.get(f)) for f in ("t_s", "yaw_deg", "pitch_deg")):
            e.append(f"L10 keyframe {i}: t_s, yaw_deg and pitch_deg are required numbers")
    if navd and agl:
        e.append("L10 keyframes mix alt_m and alt_navd88")
    if any(isinstance(k, dict) and _num(k.get("pitch_deg")) and abs(k["pitch_deg"]) > 89 for k in kfs):
        e.append("L10 pitch_deg must be within +-89")
    ts = [k.get("t_s") for k in kfs if isinstance(k, dict) and _num(k.get("t_s"))]
    if len(kfs) < min_keys:
        e.append(f"L05 needs at least {min_keys} keyframes for {interp}")
    elif len(ts) == len(kfs):
        if abs(ts[0]) > 1e-9:
            e.append("L05 first keyframe t_s must be 0")
        if _num(dur) and abs(ts[-1] - dur) > 1e-9:
            e.append("L05 last keyframe t_s must equal duration_s")
        if any(not ts[i] > ts[i - 1] for i in range(1, len(ts))):
            e.append("L05 keyframes must be strictly sorted by t_s")
    snap = d.get("snap") if isinstance(d.get("snap"), dict) else {}
    if snap.get("enabled") is not True:
        e.append("L07 snap.enabled must be true (unsnapped camera poses are not allowed)")
    if not d.get("non_world") and snap.get("dither_anchor") != "direction":
        e.append("L07 snap.dither_anchor must be direction")
    mode = d.get("mode")
    if mode not in ("clip", "still"):
        e.append("L09 mode must be clip or still")
    frames = round(dur * fps) if _num(dur) and fps in (30, 60) else 0
    if mode == "still":
        sf = d.get("still_frames")
        if not isinstance(sf, list) or not sf:
            e.append("L09 still mode needs still_frames")
        else:
            if any(not isinstance(x, int) or isinstance(x, bool) or x < 0 or x >= max(1, frames) for x in sf):
                e.append("L09 still_frames entries must be integers within the clip")
            elif any(sf[i] <= sf[i - 1] for i in range(1, len(sf))):
                e.append("L09 still_frames must be strictly increasing")
    outs = d.get("outputs")
    if not isinstance(outs, list) or not outs:
        e.append("L08 at least one output is required")
        outs = []
    for o in outs:
        if not (isinstance(o, dict) and _pair(o.get("internal_wh")) and _pair(o.get("output_wh")) and _num(o.get("scale"))):
            e.append("L08 output needs internal_wh, output_wh (two numbers each) and scale")
            continue
        (iw, ih), (ow, oh), sc = o["internal_wh"], o["output_wh"], o["scale"]
        if sc < 1 or sc != int(sc) or ow != iw * sc or oh != ih * sc:
            e.append(f"L08 output_wh must equal internal_wh * integer scale ({ow}x{oh} vs {iw}x{ih} x{sc})")
        if (ow, oh) not in ((1920, 1080), (3840, 2160)):
            e.append("L08 output must be 1920x1080 or 3840x2160")
        elif (ow, oh) == (3840, 2160) and mode == "clip":
            e.append("L08 3840x2160 is for stills only (clips are 1920x1080)")
    if d.get("profile_id") not in PROFILES:
        e.append("L15 profile_id must be one of the five palette profiles")
    for r in d.get("requires") or []:
        if r not in REQUIRES:
            e.append(f"L11 unknown requires tag '{r}'")
    fb = d.get("fallback_of")
    if fb is not None and not (isinstance(fb, str) and ID_RX.match(fb)):
        e.append("L11 fallback_of must be a path_id")
    seed = d.get("seed")
    if not isinstance(seed, int) or isinstance(seed, bool) or seed < 0:
        e.append("L12 seed must be a non-negative integer")
    for s in d.get("shake") or []:
        if not (isinstance(s, dict) and all(_num(s.get(f)) for f in ("t_s", "amp_internal_px", "decay_s"))
                and 0 <= s["t_s"] <= (dur if _num(dur) else math.inf) and s["amp_internal_px"] >= 0 and s["decay_s"] > 0):
            e.append("L13 shake needs 0 <= t_s <= duration_s, amp_internal_px >= 0, decay_s > 0")
            break
    vp = d.get("viewpoint") if isinstance(d.get("viewpoint"), dict) else {}
    if not _pair(vp.get("baytm")):
        e.append("L14 viewpoint.baytm must be [east, north]")
    if not isinstance(vp.get("tile"), str) or not TILE_RX.match(vp["tile"]):
        e.append("L14 viewpoint.tile must look like L0_30_36")
    if not vp.get("id"):
        e.append("L14 viewpoint.id is required")
    if not d.get("district_id"):
        e.append("L14 district_id is required")
    purpose, ms = d.get("purpose", "M-003"), d.get("motion_set")
    if purpose not in PURPOSES:
        e.append("L17 purpose must be M-003 or M-004")
    elif purpose == "M-004":
        cl = ms.get("classes") if isinstance(ms, dict) else None
        if not isinstance(cl, list) or not cl or not all(isinstance(c, str) for c in cl):
            e.append("L17 purpose M-004 needs motion_set.classes")
        else:
            if any(c not in MOTION_CLASSES for c in cl):
                e.append("L17 unknown motion class '%s'" % next(c for c in cl if c not in MOTION_CLASSES))
            if len(set(cl)) != len(cl):
                e.append("L17 motion_set.classes has duplicates")
            need = max((MOTION_CLASSES[c][2] for c in cl if c in MOTION_CLASSES), default=0)
            if frames < need:
                e.append(f"L17 duration_s * fps must cover the longest class ({need} frames)")
        if mode != "clip":
            e.append("L17 an M-004 motion set is a clip")
    elif ms is not None:
        e.append("L17 motion_set is only for purpose M-004")
    return e


def run(paths_dir: str) -> dict:
    """Lint every *.json under a directory (or a single file); L16 checks path_id uniqueness across the set."""
    files = [paths_dir] if os.path.isfile(paths_dir) else sorted(glob.glob(os.path.join(paths_dir, "*.json")))
    rows, seen = [], {}
    for f in files:
        try:
            d = json.load(open(f))
            errs = lint_path(d)
        except (OSError, ValueError) as ex:
            d, errs = None, [f"L00 not valid JSON: {ex}"]
        pid = d.get("path_id") if isinstance(d, dict) else None
        if isinstance(pid, str):
            if pid in seen:
                errs.append(f"L16 duplicate path_id {pid} (also in {seen[pid]})")
            seen[pid] = os.path.basename(f)
        rows.append({"file": os.path.basename(f), "path_id": pid, "requires": (d or {}).get("requires", []) if isinstance(d, dict) else [],
                     "errors": errs, "ok": not errs})
    return {"check": "capture_path/lint", "schema": SCHEMA, "files": len(rows), "rows": rows,
            "pass": bool(rows) and all(r["ok"] for r in rows),
            "blocked": sum(1 for r in rows if r["requires"]), "runnable": sum(1 for r in rows if not r["requires"])}
