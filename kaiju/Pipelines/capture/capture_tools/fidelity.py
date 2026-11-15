"""M-124 encode fidelity: decoded frames vs master, CIEDE2000 (CIELAB D65 2 deg), >= 99 % of pixels <= 2.0 per sampled frame."""
from __future__ import annotations

import glob
import os
import shutil
import subprocess

import numpy as np

from ._metrics import ciede2000, read_png, srgb_u8_to_lab

DE_LIMIT = 2.0
SHARE_MIN = 0.99
MIN_FRAMES = 60


def delta_e_map(a: np.ndarray, b: np.ndarray, chunk_rows=270) -> np.ndarray:
    """Per-pixel CIEDE2000 between two (H,W,3) uint8 sRGB images."""
    if a.shape != b.shape:
        raise ValueError(f"shape mismatch {a.shape} vs {b.shape}")
    out = np.empty(a.shape[:2], np.float64)
    for y in range(0, a.shape[0], chunk_rows):
        out[y:y + chunk_rows] = ciede2000(srgb_u8_to_lab(a[y:y + chunk_rows]), srgb_u8_to_lab(b[y:y + chunk_rows]))
    return out


def frame_fidelity(master: np.ndarray, decoded: np.ndarray) -> dict:
    de = delta_e_map(master, decoded)
    shift = (srgb_u8_to_lab(decoded).reshape(-1, 3).mean(0) - srgb_u8_to_lab(master).reshape(-1, 3).mean(0))
    return {"share_le_2": float((de <= DE_LIMIT).mean()), "mean": float(de.mean()), "p999": float(np.percentile(de, 99.9)),
            "max": float(de.max()), "mean_shift_lab": [round(float(v), 4) for v in shift]}


def sample_indices(n_frames, cut_frames=(), every=10):
    s = set(range(0, n_frames, every)) | {c for c in cut_frames if 0 <= c < n_frames}
    return sorted(s)


def evaluate(pairs, min_frames=MIN_FRAMES, share_min=SHARE_MIN):
    """pairs: iterable of (frame_index, master_rgb, decoded_rgb)."""
    rows = []
    for i, m, d in pairs:
        r = frame_fidelity(m, d)
        r["frame"] = int(i)
        r["ok"] = r["share_le_2"] >= share_min
        rows.append(r)
    sh = [r["share_le_2"] for r in rows]
    summ = {"frames": len(rows), "min_share": min(sh) if sh else 0.0, "mean_share": float(np.mean(sh)) if sh else 0.0,
            "p999_worst": max((r["p999"] for r in rows), default=0.0)}
    ok = bool(rows) and all(r["ok"] for r in rows) and len(rows) >= min_frames
    return {"check": "M-124", "threshold": f">= {share_min:.0%} of pixels at dE00 <= {DE_LIMIT} on every sampled frame, "
                                            f">= {min_frames} frames", "summary": summ, "rows": rows, "pass": ok,
            "reason": None if ok else ("too few frames sampled" if rows and len(rows) < min_frames else "share below threshold")}


def decode_frames(video, indices, w, h):
    """Decode selected frames to RGB via ffmpeg (bt709 limited -> full-range RGB, accurate chroma upsampling)."""
    if not shutil.which("ffmpeg"):
        raise RuntimeError("ffmpeg not installed: decode with `ffmpeg -i v.mp4 -vf scale=in_color_matrix=bt709:in_range=tv:out_range=pc:"
                           "flags=accurate_rnd+full_chroma_int+bitexact -pix_fmt rgb24 d/%05d.png` and use evaluate()")
    sel = "+".join(f"eq(n\\,{i})" for i in indices)
    cmd = ["ffmpeg", "-v", "error", "-i", video, "-vf",
           f"select='{sel}',scale=in_color_matrix=bt709:in_range=tv:out_range=pc:flags=accurate_rnd+full_chroma_int+bitexact",
           "-fps_mode", "passthrough", "-pix_fmt", "rgb24", "-f", "rawvideo", "-"]
    raw = subprocess.run(cmd, capture_output=True, check=True).stdout
    n = len(raw) // (w * h * 3)
    return np.frombuffer(raw, np.uint8)[:n * w * h * 3].reshape(n, h, w, 3)


def run(master_dir, video, cut_frames=(), every=10, pattern="*.png"):
    files = sorted(glob.glob(os.path.join(master_dir, pattern)))
    if not files:
        return {"check": "M-124", "pass": False, "reason": "no master frames"}
    first = read_png(files[0])
    h, w = first.shape[:2]
    idx = sample_indices(len(files), cut_frames, every)
    dec = decode_frames(video, idx, w, h)
    if len(dec) != len(idx):
        return {"check": "M-124", "pass": False, "reason": f"decoded {len(dec)} of {len(idx)} sampled frames"}
    return evaluate((i, read_png(files[i]), dec[k]) for k, i in enumerate(idx))
