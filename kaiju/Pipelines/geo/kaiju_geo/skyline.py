"""Offline skyline references for M-005.7 (R-004 §6.7).

R-src: exact profile of the same geo_tile@v1 data (building prisms projected edge by edge, terrain +
water heightfield ray-marched per column). R-ind: ray-march of an independent first-return lidar DSM.
Camera: pinhole, level, hFOV 60 deg, W x H; heading is a compass bearing (0 = +z grid north, 90 = +x).
Profiles are continuous row coordinates (0 = top edge); a pixel (v, u) is non-sky if v + 0.5 > profile[u].
"""
from __future__ import annotations

import math

import numpy as np


class Cam:
    def __init__(self, x, z, eye, heading_deg, w=1024, h=576, hfov_deg=60.0):
        self.x, self.z, self.eye, self.w, self.h = x, z, eye, w, h
        a = math.radians(heading_deg)
        self.fwd = np.array([math.sin(a), math.cos(a)])
        self.right = np.array([math.cos(a), -math.sin(a)])
        self.f = (w / 2) / math.tan(math.radians(hfov_deg) / 2)
        self.cx, self.cy = w / 2, h / 2

    def project(self, X, Y, Z):
        dx, dz = X - self.x, Z - self.z
        zc = dx * self.fwd[0] + dz * self.fwd[1]
        xc = dx * self.right[0] + dz * self.right[1]
        with np.errstate(divide="ignore", invalid="ignore"):
            u = self.cx + self.f * xc / zc
            v = self.cy - self.f * (Y - self.eye) / zc
        return u, v, zc


def profile_heightfield(cam: Cam, sample, t_max=4000.0, step=0.5, t_min=1.0):
    """Topmost row per column of a heightfield sample(x[], z[]) -> h[] (NaN = no surface)."""
    cols = (np.arange(cam.w) + 0.5 - cam.cx) / cam.f
    best = np.full(cam.w, np.inf)
    ts = np.arange(t_min, t_max, step)
    for k in range(0, len(ts), 500):
        t = ts[k:k + 500][:, None]
        X = cam.x + t * (cam.fwd[0] + cols[None, :] * cam.right[0])
        Z = cam.z + t * (cam.fwd[1] + cols[None, :] * cam.right[1])
        H = sample(X.ravel(), Z.ravel()).reshape(X.shape)
        v = cam.cy - cam.f * (H - cam.eye) / t
        v = np.where(np.isfinite(v), v, np.inf)
        best = np.minimum(best, v.min(axis=0))
    return best


def profile_prisms(cam: Cam, edges, near=0.5):
    """edges: (N, 6) array of 3D segments (x0, y0, z0, x1, y1, z1) = roof outlines (+ apex edges).
    Returns the upper envelope row per column (inf where none)."""
    best = np.full(cam.w, np.inf)
    e = np.asarray(edges, dtype=np.float64)
    if len(e) == 0:
        return best
    _, _, z0 = cam.project(e[:, 0], e[:, 1], e[:, 2])
    _, _, z1 = cam.project(e[:, 3], e[:, 4], e[:, 5])
    keep = (z0 > near) | (z1 > near)
    e, z0, z1 = e[keep], z0[keep], z1[keep]
    # clip to the near plane
    for a, b, za, zb in ((slice(0, 3), slice(3, 6), z0, z1), (slice(3, 6), slice(0, 3), z1, z0)):
        bad = za <= near
        if np.any(bad):
            s = (near + 1e-6 - za[bad]) / (zb[bad] - za[bad])
            e[bad, a] = e[bad, a] + s[:, None] * (e[bad, b] - e[bad, a])
    u0, v0, _ = cam.project(e[:, 0], e[:, 1], e[:, 2])
    u1, v1, _ = cam.project(e[:, 3], e[:, 4], e[:, 5])
    for i in range(len(e)):
        ua, ub, va, vb = u0[i], u1[i], v0[i], v1[i]
        if ua > ub:
            ua, ub, va, vb = ub, ua, vb, va
        c0 = max(int(math.ceil(ua - 0.5)), 0)
        c1 = min(int(math.floor(ub - 0.5)), cam.w - 1)
        if c1 < c0:
            # segment within one column (vertical in screen): take its top
            c = int(math.floor(ua))
            if 0 <= c < cam.w:
                best[c] = min(best[c], va, vb)
            continue
        uc = np.arange(c0, c1 + 1) + 0.5
        t = (uc - ua) / (ub - ua) if ub > ua else np.zeros_like(uc)
        v = va + t * (vb - va)
        best[c0:c1 + 1] = np.minimum(best[c0:c1 + 1], v)
    return best


def first_rows(profile):
    """Continuous profile -> first non-sky pixel row per column (H when none)."""
    return np.ceil(profile - 0.5)


def mask_profile(mask: np.ndarray):
    """Binary mask (H, W; True = non-sky) -> first non-sky row per column (H when none)."""
    h = mask.shape[0]
    any_ = mask.any(axis=0)
    first = np.argmax(mask, axis=0).astype(np.float64)
    first[~any_] = h
    return first


def compare(game_rows, ref_rows, h, horizon_row):
    """IoU of the column-filled masks on the band [min ref row, horizon) and profile MAE (px)."""
    g = np.clip(game_rows, 0, h)
    r = np.clip(ref_rows, 0, h)
    top = int(max(0, np.min(r)))
    hr = int(horizon_row)
    if hr <= top:
        return {"iou": None, "mae_px": None, "band": [top, hr]}
    rows = np.arange(top, hr)[:, None]
    G = rows >= g[None, :]
    R = rows >= r[None, :]
    inter = np.logical_and(G, R).sum()
    union = np.logical_or(G, R).sum()
    gb = np.clip(g, top, hr)
    rb = np.clip(r, top, hr)
    return {"iou": float(inter / union) if union else 1.0, "mae_px": float(np.mean(np.abs(gb - rb))),
            "band": [top, hr], "columns": int(len(g))}
