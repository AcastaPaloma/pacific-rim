"""Colour maths: sRGB (IEC 61966-2-1) 8-bit -> CIELAB (D65 / 2 deg) and CIEDE2000 (Sharma, Wu, Dalal 2005).

Own vectorised implementation (float64). Verified against the Sharma 34-pair table (tests/test_colour.py)."""
from __future__ import annotations

import re

import numpy as np

# sRGB primaries -> XYZ (D65), IEC 61966-2-1
_M = np.array([[0.412453, 0.357580, 0.180423],
               [0.212671, 0.715160, 0.072169],
               [0.019334, 0.119193, 0.950227]], dtype=np.float64)
_WHITE_D65 = np.array([0.95047, 1.0, 1.08883], dtype=np.float64)
_HEX = re.compile(r"^#[0-9a-f]{6}$")


def is_hex(s) -> bool:
    return isinstance(s, str) and bool(_HEX.match(s))


def hex_to_rgb(h: str) -> np.ndarray:
    return np.array([int(h[1:3], 16), int(h[3:5], 16), int(h[5:7], 16)], dtype=np.uint8)


def hexes_to_rgb(hs) -> np.ndarray:
    return np.stack([hex_to_rgb(h) for h in hs]).astype(np.uint8)


def rgb_to_hex(rgb) -> str:
    r, g, b = (int(v) for v in rgb)
    return f"#{r:02x}{g:02x}{b:02x}"


def srgb_u8_to_lab(rgb) -> np.ndarray:
    """(..., 3) uint8 sRGB -> (..., 3) float64 CIELAB D65."""
    c = np.asarray(rgb, dtype=np.float64) / 255.0
    lin = np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
    xyz = lin @ _M.T / _WHITE_D65
    eps = (6.0 / 29.0) ** 3
    f = np.where(xyz > eps, np.cbrt(xyz), xyz / (3 * (6.0 / 29.0) ** 2) + 4.0 / 29.0)
    L = 116.0 * f[..., 1] - 16.0
    a = 500.0 * (f[..., 0] - f[..., 1])
    b = 200.0 * (f[..., 1] - f[..., 2])
    return np.stack([L, a, b], axis=-1)


def ciede2000(lab1, lab2) -> np.ndarray:
    """CIEDE2000 colour difference (kL = kC = kH = 1), broadcasting over leading axes."""
    lab1 = np.asarray(lab1, dtype=np.float64)
    lab2 = np.asarray(lab2, dtype=np.float64)
    L1, a1, b1 = lab1[..., 0], lab1[..., 1], lab1[..., 2]
    L2, a2, b2 = lab2[..., 0], lab2[..., 1], lab2[..., 2]
    C1 = np.hypot(a1, b1)
    C2 = np.hypot(a2, b2)
    Cbar = 0.5 * (C1 + C2)
    G = 0.5 * (1 - np.sqrt(Cbar ** 7 / (Cbar ** 7 + 25.0 ** 7)))
    a1p, a2p = (1 + G) * a1, (1 + G) * a2
    C1p, C2p = np.hypot(a1p, b1), np.hypot(a2p, b2)
    h1p = np.where((a1p == 0) & (b1 == 0), 0.0, np.degrees(np.arctan2(b1, a1p)) % 360.0)
    h2p = np.where((a2p == 0) & (b2 == 0), 0.0, np.degrees(np.arctan2(b2, a2p)) % 360.0)
    dLp = L2 - L1
    dCp = C2p - C1p
    zero = (C1p * C2p) == 0
    dh = h2p - h1p
    dh = np.where(dh > 180, dh - 360, np.where(dh < -180, dh + 360, dh))
    dh = np.where(zero, 0.0, dh)
    dHp = 2 * np.sqrt(C1p * C2p) * np.sin(np.radians(dh) / 2)
    Lbp = 0.5 * (L1 + L2)
    Cbp = 0.5 * (C1p + C2p)
    hsum = h1p + h2p
    hbp = np.where(zero, hsum,
                   np.where(np.abs(h1p - h2p) <= 180, 0.5 * hsum,
                            np.where(hsum < 360, 0.5 * (hsum + 360), 0.5 * (hsum - 360))))
    T = (1 - 0.17 * np.cos(np.radians(hbp - 30)) + 0.24 * np.cos(np.radians(2 * hbp))
         + 0.32 * np.cos(np.radians(3 * hbp + 6)) - 0.20 * np.cos(np.radians(4 * hbp - 63)))
    dtheta = 30 * np.exp(-(((hbp - 275) / 25) ** 2))
    RC = 2 * np.sqrt(Cbp ** 7 / (Cbp ** 7 + 25.0 ** 7))
    SL = 1 + 0.015 * (Lbp - 50) ** 2 / np.sqrt(20 + (Lbp - 50) ** 2)
    SC = 1 + 0.045 * Cbp
    SH = 1 + 0.015 * Cbp * T
    RT = -np.sin(np.radians(2 * dtheta)) * RC
    return np.sqrt((dLp / SL) ** 2 + (dCp / SC) ** 2 + (dHp / SH) ** 2 + RT * (dCp / SC) * (dHp / SH))


def nearest_palette(rgb_img, pal_rgb):
    """Nearest palette entry by CIEDE2000 for an (H, W, 3) uint8 image.

    Works on the unique colours of the image (quantised frames hold few), so it is exact and fast.
    Returns (dE00 per pixel (H, W) float64, index per pixel (H, W) int32)."""
    rgb = np.asarray(rgb_img, dtype=np.uint8)
    shp = rgb.shape[:-1]
    flat = rgb.reshape(-1, 3)
    key = (flat[:, 0].astype(np.uint32) << 16) | (flat[:, 1].astype(np.uint32) << 8) | flat[:, 2]
    uk, inv = np.unique(key, return_inverse=True)
    ucol = np.stack([(uk >> 16) & 255, (uk >> 8) & 255, uk & 255], axis=1).astype(np.uint8)
    lab_u = srgb_u8_to_lab(ucol)
    lab_p = srgb_u8_to_lab(np.asarray(pal_rgb, dtype=np.uint8))
    d_best = np.full(len(ucol), np.inf)
    i_best = np.zeros(len(ucol), dtype=np.int32)
    step = 65536
    for s in range(0, len(ucol), step):
        d = ciede2000(lab_u[s:s + step, None, :], lab_p[None, :, :])
        i_best[s:s + step] = d.argmin(1)
        d_best[s:s + step] = d.min(1)
    return d_best[inv].reshape(shp), i_best[inv].reshape(shp)


def nearest_lab(lab_px, pal_lab, chunk=200000):
    """Nearest palette entry for N x 3 Lab pixels (used where pixels are not 8-bit, e.g. denoised references)."""
    lab_px = np.asarray(lab_px, dtype=np.float64).reshape(-1, 3)
    P = np.asarray(pal_lab, dtype=np.float64)
    out_d = np.empty(len(lab_px))
    out_i = np.empty(len(lab_px), dtype=np.int32)
    for s in range(0, len(lab_px), chunk):
        d = ciede2000(lab_px[s:s + chunk, None, :], P[None, :, :])
        out_i[s:s + chunk] = d.argmin(1)
        out_d[s:s + chunk] = d.min(1)
    return out_d, out_i


def nearest_lab_rgb(rgb_px, pal_lab):
    """Nearest Lab palette entry for N x 3 uint8 pixels, computed once per unique colour (exact, fast)."""
    flat = np.asarray(rgb_px, dtype=np.uint8).reshape(-1, 3)
    key = (flat[:, 0].astype(np.uint32) << 16) | (flat[:, 1].astype(np.uint32) << 8) | flat[:, 2]
    uk, inv = np.unique(key, return_inverse=True)
    ucol = np.stack([(uk >> 16) & 255, (uk >> 8) & 255, uk & 255], axis=1).astype(np.uint8)
    d, i = nearest_lab(srgb_u8_to_lab(ucol), pal_lab)
    return d[inv], i[inv]
