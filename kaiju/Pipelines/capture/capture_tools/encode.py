"""Lossless-looking encode path: scale in RGB with nearest neighbour (never in YUV), then H.264 with dither-safe settings.
R-121 section 5 command; falls back to a documented command string when ffmpeg is missing."""
from __future__ import annotations

import os
import shutil
import subprocess

import numpy as np

from .scale import upscale_nn
from ._metrics import read_png

DEFAULTS = {"crf": 14, "pix_fmt": "yuv420p", "tune": "animation", "preset": "slow"}


def ffmpeg_command(frames_dir, out, fps=60, audio=None, crf=None, pix_fmt=None, tune=None, preset=None, scale_to=None,
                   pattern="%05d.png"):
    o = {**DEFAULTS, **{k: v for k, v in dict(crf=crf, pix_fmt=pix_fmt, tune=tune, preset=preset).items() if v is not None}}
    cmd = ["ffmpeg", "-y", "-framerate", str(fps), "-i", os.path.join(frames_dir, pattern)]
    if audio:
        cmd += ["-i", audio]
    vf = []
    if scale_to:  # only when the master is not already at the target: neighbour in RGB, before format conversion
        vf.append(f"scale={scale_to[0]}:{scale_to[1]}:flags=neighbor")
    vf.append(f"scale=out_color_matrix=bt709:out_range=tv:flags=accurate_rnd+full_chroma_int,format={o['pix_fmt']}")
    cmd += ["-vf", ",".join(vf), "-c:v", "libx264", "-preset", o["preset"], "-crf", str(o["crf"])]
    if o["tune"] and o["tune"] != "none":
        cmd += ["-tune", o["tune"]]
    cmd += ["-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709", "-color_range", "tv"]
    if audio:
        cmd += ["-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-shortest"]
    cmd += ["-movflags", "+faststart", out]
    return cmd


def encode(frames_dir, out, dry_run=False, **kw):
    cmd = ffmpeg_command(frames_dir, out, **kw)
    if dry_run or not shutil.which("ffmpeg"):
        return {"ran": False, "command": " ".join(cmd), "note": "ffmpeg not run (dry run or not installed)"}
    subprocess.run(cmd, check=True, capture_output=True)
    return {"ran": True, "command": " ".join(cmd)}


def upscale_dir(src_dir, dst_dir, scale, pattern="*.png"):
    """Nearest-neighbour integer upscale of every PNG in a directory (pixel-exact; cv2 writer, lossless)."""
    import cv2
    import glob
    os.makedirs(dst_dir, exist_ok=True)
    n = 0
    for p in sorted(glob.glob(os.path.join(src_dir, pattern))):
        a = upscale_nn(read_png(p), scale)
        cv2.imwrite(os.path.join(dst_dir, os.path.basename(p)), np.ascontiguousarray(a[..., ::-1]))
        n += 1
    return n
