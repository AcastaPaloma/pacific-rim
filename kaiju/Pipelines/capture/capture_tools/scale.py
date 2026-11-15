"""Integer-scale rules (Q-234) and nearest-neighbour upscale. Pure numpy; dither is preserved bit-exactly."""
from __future__ import annotations

import numpy as np

ALLOWED_OUTPUTS = ((1920, 1080), (3840, 2160))


def upscale_nn(rgb: np.ndarray, s: int) -> np.ndarray:
    """(H,W,C) -> (H*s,W*s,C) by pixel replication."""
    if not isinstance(s, int) or s < 1:
        raise ValueError(f"scale must be a positive integer, got {s!r}")
    return np.repeat(np.repeat(rgb, s, 0), s, 1)


def scale_for(internal_wh, output_wh):
    """Integer scale s with output == internal*s, else None."""
    (iw, ih), (ow, oh) = internal_wh, output_wh
    if iw > 0 and ih > 0 and ow % iw == 0 and oh % ih == 0 and ow // iw == oh // ih:
        return ow // iw
    return None


def check_resolution(internal_wh, output_wh, gameplay=True):
    """Returns a list of problems (empty = ok)."""
    errs = []
    s = scale_for(internal_wh, output_wh)
    if s is None:
        errs.append(f"output {tuple(output_wh)} is not an integer multiple of internal {tuple(internal_wh)}")
    if gameplay and tuple(output_wh) not in ALLOWED_OUTPUTS:
        errs.append(f"output {tuple(output_wh)} not in {ALLOWED_OUTPUTS} (Q-234)")
    return errs


def blocks_uniform(out: np.ndarray, s: int, offset=(0, 0)) -> float:
    """Share of s x s output blocks that are single-colour (1.0 for a true nearest-neighbour integer upscale).
    Offset (ox, oy) is the output_block_offset_px of the sidecar (np.roll of the upscale); partial edge blocks are
    excluded."""
    ox, oy = offset
    a = np.roll(out, (-oy, -ox), (0, 1)) if (ox or oy) else out
    H, W = a.shape[0] // s * s, a.shape[1] // s * s
    if H == 0 or W == 0:
        return 0.0
    b = a[:H, :W].reshape(H // s, s, W // s, s, -1)
    same = (b == b[:, :1, :, :1, :]).all((1, 3, 4))
    return float(same.mean())


def matches_nn_of(out: np.ndarray, internal: np.ndarray, s: int, offset=(0, 0)) -> float:
    """Share of output pixels equal to the nearest-neighbour upscale of `internal` (the stretch detector)."""
    exp = upscale_nn(internal, s)
    ox, oy = offset
    if ox or oy:
        exp = np.roll(exp, (oy, ox), (0, 1))
    if exp.shape != out.shape:
        return 0.0
    return float((exp == out).all(-1).mean())


def matches_shifted_nn_of(out: np.ndarray, internal: np.ndarray, s: int, shift=(0, 0)) -> float:
    """Like matches_nn_of for a frame the present stage shifted by `shift` = present_shift_output_px (sx, sy) of the sidecar
    (bottom-left origin, sub-pixel compensation): output pixel (x, y in PNG rows) shows texel (floor((y - sy) / s), floor((x + sx) / s)).
    Pixels whose texel lies outside the cropped internal image (the guard band) are not compared. Returns the share of compared pixels that match."""
    sx, sy = shift
    H, W = out.shape[:2]
    ih, iw = internal.shape[:2]
    if (H, W) != (ih * s, iw * s):
        return 0.0
    ty = (np.arange(H) - sy) // s
    tx = (np.arange(W) + sx) // s
    vy, vx = (ty >= 0) & (ty < ih), (tx >= 0) & (tx < iw)
    exp = internal[np.clip(ty, 0, ih - 1)][:, np.clip(tx, 0, iw - 1)]
    eq = (exp == out).all(-1)[np.ix_(vy, vx)]
    return float(eq.mean()) if eq.size else 0.0
