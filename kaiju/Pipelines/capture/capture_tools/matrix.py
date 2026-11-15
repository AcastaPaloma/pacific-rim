"""Control matrix (capture_plan.md section 5, R-121 section 6): master scale x pix_fmt x CRF x tune on a short frame set,
each cell encoded with the same ffmpeg command as `encode`, decoded, scored with the M-124 rule (CIEDE2000, share of pixels <= 2.0).
The cheapest cell (smallest file) whose worst sampled frame meets MARGIN is recommended; the gate itself (99 %) is never moved."""
from __future__ import annotations

import glob
import itertools
import json
import os
import shutil
import subprocess
import tempfile

import numpy as np

from ._metrics import read_png
from .encode import ffmpeg_command
from .fidelity import SHARE_MIN, decode_frames, evaluate
from .scale import upscale_nn

MARGIN = 0.995
SCALES, PIX_FMTS, CRFS, TUNES = (1, 2, 3, 4), ("yuv420p", "yuv444p"), (10, 14, 18, 23), ("none", "animation")


def _write_frames(frames, scale, dst):
    import cv2
    os.makedirs(dst, exist_ok=True)
    out = []
    for i, f in enumerate(frames):
        a = upscale_nn(f, scale) if scale > 1 else f
        cv2.imwrite(os.path.join(dst, f"{i + 1:05d}.png"), np.ascontiguousarray(a[..., ::-1]))
        out.append(a)
    return out


def _cell(args):
    """One matrix cell (module level so a process pool can run it): encode the scale's master frames, decode, score with the M-124 rule."""
    fdir, tmp, scale, pix, crf, tune, preset, fps, n = args
    from ._metrics import read_png as _read
    masters = [_read(os.path.join(fdir, f"{i + 1:05d}.png")) for i in range(n)]
    h, w = masters[0].shape[:2]
    mp4 = os.path.join(tmp, f"s{scale}_{pix}_crf{crf}_{tune}.mp4")
    cmd = ffmpeg_command(fdir, mp4, fps=fps, crf=crf, pix_fmt=pix, tune=tune, preset=preset)
    p = subprocess.run(cmd, capture_output=True, text=True)
    cell = {"scale": scale, "size_wh": [w, h], "pix_fmt": pix, "crf": crf, "tune": tune}
    if p.returncode != 0:
        cell.update(ok=False, error=p.stderr[-200:])
    else:
        dec = decode_frames(mp4, list(range(len(masters))), w, h)
        ev = evaluate(((i, masters[i], dec[i]) for i in range(min(len(dec), len(masters)))), min_frames=1)
        cell.update(bytes=os.path.getsize(mp4), min_share=ev["summary"]["min_share"], mean_share=ev["summary"]["mean_share"],
                    p999_worst=ev["summary"]["p999_worst"], frames=ev["summary"]["frames"], gate_pass=ev["pass"], ok=ev["summary"]["min_share"] >= MARGIN)
    if os.path.exists(mp4):
        os.remove(mp4)
    return cell


def run(internal_dir, n_frames=8, scales=SCALES, pix_fmts=PIX_FMTS, crfs=CRFS, tunes=TUNES, preset="medium", fps=60, work=None, pattern="*_color_internal.png",
        select_pix_fmt=None, select_scale=None, progress=None, jobs=1):
    """internal_dir: frames at internal resolution (searched recursively). Returns the matrix report; `pass` = a cell was found.
    select_pix_fmt / select_scale restrict the cell that is *selected* (the whole grid is still measured and reported): the encode profile (4:4:4) and the
    delivery master scale (4 = 1920x1080) are decisions taken outside the matrix; the gate and the margin are never moved. jobs > 1 measures cells in parallel."""
    if not shutil.which("ffmpeg"):
        return {"check": "matrix", "pass": False, "reason": "ffmpeg not installed"}
    files = sorted(glob.glob(os.path.join(internal_dir, "**", pattern), recursive=True))[:n_frames]
    if not files:
        return {"check": "matrix", "pass": False, "reason": f"no frames matching {pattern} under {internal_dir}"}
    frames = [read_png(p) for p in files]
    cells, tmp = [], work or tempfile.mkdtemp(prefix="capture_matrix_")
    try:
        for scale in scales:
            fdir = os.path.join(tmp, f"s{scale}")
            _write_frames(frames, scale, fdir)
            tasks = [(fdir, tmp, scale, pix, crf, tune, preset, fps, len(frames)) for pix, crf, tune in itertools.product(pix_fmts, crfs, tunes)]
            if jobs > 1:
                from concurrent.futures import ProcessPoolExecutor
                with ProcessPoolExecutor(max_workers=jobs) as ex:
                    for cell in ex.map(_cell, tasks):
                        cells.append(cell)
                        if progress:
                            progress(cell)
            else:
                for t in tasks:
                    cells.append(_cell(t))
                    if progress:
                        progress(cells[-1])
            shutil.rmtree(fdir, ignore_errors=True)
    finally:
        if work is None:
            shutil.rmtree(tmp, ignore_errors=True)
    good = [c for c in cells if c.get("ok")]
    unrestricted = min(good, key=lambda c: c["bytes"]) if good else None
    eligible = [c for c in good if (select_pix_fmt is None or c["pix_fmt"] == select_pix_fmt) and (select_scale is None or c["scale"] == select_scale)]
    best = min(eligible, key=lambda c: c["bytes"]) if eligible else None
    by_pix = {}
    for c in cells:
        if c.get("min_share") is not None:
            g = by_pix.setdefault(c["pix_fmt"], {"cells": 0, "passing_margin": 0, "best_min_share": 0.0, "best_min_share_cell": None})
            g["cells"] += 1
            g["passing_margin"] += bool(c["ok"])
            if c["min_share"] >= g["best_min_share"]:
                g["best_min_share"], g["best_min_share_cell"] = c["min_share"], {k: c[k] for k in ("scale", "crf", "tune")}
    return {"check": "matrix", "gate": f"share <= dE00 2.0 >= {SHARE_MIN:.0%} (never moved); selection margin {MARGIN:.1%}",
            "source_frames": len(frames), "internal_wh": [frames[0].shape[1], frames[0].shape[0]], "preset": preset, "cells": cells,
            "selected": best, "unrestricted_best": unrestricted, "select_pix_fmt": select_pix_fmt, "select_scale": select_scale, "by_pix_fmt": by_pix,
            "pass": best is not None, "reason": None if best else "no cell met the selection margin"}


def settings_json(report, fps=60):
    """The docs/store/encode_settings.json body for a matrix report with a selected cell."""
    b = report["selected"]
    seconds = b["frames"] / fps
    out = {"schema": "encode_settings@v1", "master_scale": b["scale"], "master_wh": b["size_wh"], "pix_fmt": b["pix_fmt"], "crf": b["crf"], "tune": b["tune"],
           "preset": report["preset"], "selected_min_share": b["min_share"], "selected_mean_share": b.get("mean_share"), "bytes_for_frames": b["bytes"], "frames": b["frames"],
           "estimated_bitrate_kbps_on_matrix_frames": round(b["bytes"] * 8 / seconds / 1000.0, 1),
           "source": "capture.py matrix", "source_frames": report["source_frames"], "internal_wh": report["internal_wh"], "authoritative": report["source_frames"] >= 60,
           "gate": report["gate"], "selection_restricted_to": {"pix_fmt": report.get("select_pix_fmt"), "scale": report.get("select_scale")}}
    u = report.get("unrestricted_best")
    if u and (u["pix_fmt"], u["scale"]) != (b["pix_fmt"], b["scale"]):
        out["cheapest_unrestricted"] = {k: u[k] for k in ("scale", "pix_fmt", "crf", "tune", "bytes", "min_share")}
    out["by_pix_fmt"] = report.get("by_pix_fmt")
    return out
