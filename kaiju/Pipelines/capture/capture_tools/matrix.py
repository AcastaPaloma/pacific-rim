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


def run(internal_dir, n_frames=8, scales=SCALES, pix_fmts=PIX_FMTS, crfs=CRFS, tunes=TUNES, preset="medium", fps=60, work=None, pattern="*_color_internal.png"):
    """internal_dir: frames at internal resolution (searched recursively). Returns the matrix report; `pass` = a cell was found."""
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
            masters = _write_frames(frames, scale, fdir)
            h, w = masters[0].shape[:2]
            for pix, crf, tune in itertools.product(pix_fmts, crfs, tunes):
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
                cells.append(cell)
                os.remove(mp4) if os.path.exists(mp4) else None
            shutil.rmtree(fdir, ignore_errors=True)
    finally:
        if work is None:
            shutil.rmtree(tmp, ignore_errors=True)
    good = [c for c in cells if c.get("ok")]
    best = min(good, key=lambda c: c["bytes"]) if good else None
    return {"check": "matrix", "gate": f"share <= dE00 2.0 >= {SHARE_MIN:.0%} (never moved); selection margin {MARGIN:.1%}",
            "source_frames": len(frames), "internal_wh": [frames[0].shape[1], frames[0].shape[0]], "preset": preset, "cells": cells,
            "selected": best, "pass": best is not None,
            "reason": None if best else "no cell met the selection margin"}


def settings_json(report):
    """The docs/store/encode_settings.json body for a matrix report with a selected cell."""
    b = report["selected"]
    return {"schema": "encode_settings@v1", "master_scale": b["scale"], "pix_fmt": b["pix_fmt"], "crf": b["crf"], "tune": b["tune"],
            "preset": report["preset"], "selected_min_share": b["min_share"], "bytes_for_frames": b["bytes"], "frames": b["frames"],
            "source": "capture.py matrix", "authoritative": report["source_frames"] >= 60}
