"""R-002: tiny numpy ray-caster used as a stand-in renderer for validating M-003/M-004 procedures.
Scene: ground plane + a city of boxes + one giant block (~120 m) + sky; perspective camera; depth haze along a
palette ramp; palette quantisation with ordered (Bayer) / white-noise / no dither. Units: metres, radians."""
import json, os
import numpy as np
from common import bayer, OUT

W, H = 320, 180            # internal resolution (1/6 of 1920x1080)
HFOV = np.radians(60.0)
FAR = 20000.0              # sky depth (m)
HAZE_D = 1500.0            # haze e-folding distance (m)


def make_scene(seed=3):
    rng = np.random.default_rng(seed)
    boxes = []
    for i in range(-6, 7):
        for j in range(2, 26):
            if rng.random() < 0.55:
                cx, cz = i * 90.0 + rng.uniform(-20, 20), j * 90.0
                w, d, h = rng.uniform(25, 50), rng.uniform(25, 50), rng.uniform(10, 140)
                boxes.append((cx - w / 2, 0, cz - d / 2, cx + w / 2, h, cz + d / 2, rng.uniform(0.35, 0.8)))
    boxes.append((-60, 0, 520, 60, 120, 600, 0.25))      # giant silhouette block
    for k in range(10):                                   # far ridge blocks
        boxes.append((-3000 + k * 600, 0, 4500, -2500 + k * 600, rng.uniform(150, 400), 5000, 0.6))
    return np.array(boxes, dtype=np.float64)


def cam_basis(yaw, pitch):
    f = np.array([np.sin(yaw) * np.cos(pitch), np.sin(pitch), np.cos(yaw) * np.cos(pitch)])
    r = np.array([np.cos(yaw), 0.0, -np.sin(yaw)])
    u = np.cross(f, r)
    return r, u, f


def rays(yaw, pitch):
    r, u, f = cam_basis(yaw, pitch)
    t = np.tan(HFOV / 2)
    xs = ((np.arange(W) + 0.5) / W * 2 - 1) * t
    ys = -((np.arange(H) + 0.5) / H * 2 - 1) * t * H / W
    X, Y = np.meshgrid(xs, ys)
    d = X[..., None] * r + Y[..., None] * u + f
    return d / np.linalg.norm(d, axis=-1, keepdims=True), f


def render(boxes, pos, yaw, pitch=np.radians(-4), haze_on=True, return_ids=False):
    """Returns ramp position p in [0,1] (continuous tone), eye-space linear depth (m)."""
    d, fwd = rays(yaw, pitch)
    D = d.reshape(-1, 3); o = np.asarray(pos, float)
    tbest = np.full(len(D), np.inf); shade = np.zeros(len(D)); ids = np.full(len(D), -1)
    inv = 1.0 / np.where(np.abs(D) < 1e-9, 1e-9, D)
    sun = np.array([0.5, 0.7, -0.4]); sun /= np.linalg.norm(sun)
    for bi, b in enumerate(boxes):
        lo, hi, alb = b[:3], b[3:6], b[6]
        t1 = (lo - o) * inv; t2 = (hi - o) * inv
        tmin = np.minimum(t1, t2); tmax = np.maximum(t1, t2)
        tn = tmin.max(1); tf = tmax.min(1)
        hit = (tn <= tf) & (tf > 0) & (tn > 0.1) & (tn < tbest)
        if hit.any():
            ax = tmin[hit].argmax(1)
            n = np.zeros((hit.sum(), 3)); n[np.arange(len(ax)), ax] = -np.sign(D[hit, ax])
            tbest[hit] = tn[hit]; ids[hit] = bi
            shade[hit] = alb * (0.35 + 0.65 * np.clip(n @ sun, 0, 1))
    # ground plane y=0 with 30 m tiles of varying albedo
    tg = np.where(D[:, 1] < -1e-6, -o[1] / np.where(D[:, 1] < -1e-6, D[:, 1], -1), np.inf)
    g = (tg < tbest)
    P = o + D[g] * tg[g, None]
    tile = (np.floor(P[:, 0] / 30) + np.floor(P[:, 2] / 30)) % 2
    tbest[g] = tg[g]; ids[g] = -2; shade[g] = (0.25 + 0.25 * tile) * (0.35 + 0.65 * sun[1])
    sky = ~np.isfinite(tbest)
    depth = np.where(sky, FAR, tbest * (D @ fwd))           # eye-space z (linear depth)
    haze = 1 - np.exp(-np.where(sky, FAR, tbest) / HAZE_D)
    if not haze_on: haze = np.where(sky, 1.0, 0.0)
    p_obj = 0.55 * np.clip(shade, 0, 1)                     # near objects use the dark half of the ramp
    elev = np.clip(D[:, 1], 0, 0.4) / 0.4
    p_sky = 1.0 - 0.35 * elev                               # sky: lighter near the horizon
    p = np.where(sky, p_sky, p_obj * (1 - haze) + 0.97 * haze)
    if return_ids: return p.reshape(H, W), depth.reshape(H, W), ids.reshape(H, W)
    return p.reshape(H, W), depth.reshape(H, W)


def quantise(p, n_levels, mode="bayer", offset=(0, 0), seed=0):
    """Map ramp position to palette index 0..n-1. mode: bayer | none | noise. offset shifts the Bayer pattern."""
    x = p * (n_levels - 1)
    if mode == "bayer":
        B = bayer(4); yy, xx = np.indices(p.shape)
        thr = B[(yy + offset[1]) % 4, (xx + offset[0]) % 4]
    elif mode == "noise":
        thr = np.random.default_rng(seed).random(p.shape)
    else:
        thr = 0.5
    return np.clip(np.floor(x + thr), 0, n_levels - 1).astype(np.int32)


def ramp_lab(p, ramp):
    """Continuous (unquantised) colour: piecewise-linear interpolation along the palette ramp in Lab."""
    n = len(ramp); x = np.clip(p, 0, 1) * (n - 1); i = np.minimum(np.floor(x).astype(int), n - 2); f = (x - i)[..., None]
    return ramp[i] * (1 - f) + ramp[i + 1] * f


def load_ramp(profile="slate_teal"):
    pj = json.load(open(os.path.join(OUT, "palettes.json")))
    cols = pj["profiles"][profile]["colours"]
    ramp = np.array([c["lab"] for c in cols if c["role"].startswith("ramp_")])
    return ramp[np.argsort(ramp[:, 0])], np.array([c["lab"] for c in cols])


def contrast_targets(dists=(200, 500, 1000, 2000, 4000, 8000), cam_h=60.0):
    """Calibration scene: a dark (albedo 0.1) / light (0.9) panel pair per distance, sized 0.03 d so every pair spans
    the same angle (~6 internal px). Returns boxes and the (dark_id, light_id, d) list."""
    boxes, tg = [], []
    angs = np.radians(np.linspace(-22, 22, len(dists)))
    for a, d in zip(angs, dists):
        s = 0.03 * d; cx, cz = d * np.sin(a), d * np.cos(a); y0 = 0.0
        boxes.append((cx - s, y0, cz, cx, y0 + s, cz + 1, 0.1)); boxes.append((cx, y0, cz, cx + s, y0 + s, cz + 1, 0.9))
        tg.append((len(boxes) - 2, len(boxes) - 1, d))
    return np.array(boxes), tg
