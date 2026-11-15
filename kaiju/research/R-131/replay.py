"""Per-pixel replay of the committed T-004 stills under an arbitrary haze-amount curve.
Method: every non-sky, neutral-palette pixel of a committed still is inverted to the scene albedo interval [lo, hi] (sRGB grey)
that reproduces its observed ladder rung under the CURRENT haze curve (pixmodel port); mid = interval centre. A candidate curve
then re-renders the pixel from (albedo, new haze amount). Sky and accent pixels keep their observed value. Bounds: lo / mid / hi
(all pixels take the low / centre / high edge of their interval) give an uncertainty range for every pass count."""
import os, sys, math, pickle
import numpy as np
from common import *
from pixmodel import *

CACHE = os.environ.get("R131_CACHE", "/tmp/r131_cache"); os.makedirs(CACHE, exist_ok=True)
GRID = 2001
S0 = np.linspace(0, 1, GRID)
VPS = ["eye70", "elevated300", "street"]

def rung_table(P, n=4001):
    """Nearest-rung index and rung L* as a function of a post-haze grey s (sRGB)."""
    s = np.linspace(0, 1, n)
    L, Lmix, r = P.quantise_grey(s)
    return s, r.astype(int), L

class FrameData:
    pass

def load_frame(d, f):
    fd = FrameData()
    fd.f = f; fd.dir = d
    fd.L = pc.lightness(pc.img(d, f, "color_internal"))
    fd.z, fd.sky = pc.depth_m(d, f)
    fd.idx = pc.img(d, f, "palette_index")
    c = f["camera"]; H, W = fd.z.shape
    ys, xs = np.mgrid[0:H, 0:W]
    tv = math.tan(math.radians(c["vfov_deg"]) / 2); th = math.tan(math.radians(c["hfov_deg"]) / 2)
    ray = np.sqrt(1 + ((xs + 0.5) / W * 2 - 1) ** 2 * th * th + ((ys + 0.5) / H * 2 - 1) ** 2 * tv * tv)
    fd.dist = fd.z * ray
    pitch = math.radians(-c["yaw_pitch_roll_deg"][1]); vy = (1 - (ys + 0.5) / H * 2) * tv
    fd.height = c["position_local_m"][1] + fd.z * (math.sin(pitch) + vy * math.cos(pitch))
    fd.ground, _ = pc.ground_mask(fd.z, fd.sky, f)
    return fd

class Set:
    """One profile: all frames of one sequence plus the inverted albedo intervals."""
    def __init__(self, pid, vp, tuning=None, d=None):
        self.P = Profile(pid, tuning); self.pid = pid; self.vp = vp
        d = d or stills(pid, vp); meta, frames = pc.load_seq(d)
        self.fds = [load_frame(d, f) for f in frames]
        self._invert()
    def _invert(self):
        P = self.P; s_tab, r_tab, L_tab = rung_table(P)
        rung_of_idx = {int(i): k for k, i in enumerate(P.rungs)}
        air = P.t["airlightValue"]
        # F table per band: rung index vs s0 grid
        B = P.bands
        Fb = []
        for b in range(B):
            a = b / (B - 1); s = S0 + (air - S0) * a
            Fb.append(np.interp(s, s_tab, r_tab).round().astype(int) if False else r_tab[np.minimum(np.round(s * (len(s_tab) - 1)).astype(int), len(s_tab) - 1)])
        self.Fb = np.array(Fb)
        for fd in self.fds:
            neutral = np.zeros(fd.idx.shape, bool); robs = np.full(fd.idx.shape, -1)
            for i, k in rung_of_idx.items():
                m = fd.idx == i; neutral |= m; robs[m] = k
            fd.neutral = neutral & ~fd.sky
            lvl = P.level(fd.dist); band = np.clip(np.floor(lvl), 0, B - 1).astype(int)
            lo = np.zeros(fd.idx.shape, int); hi = np.zeros(fd.idx.shape, int); ok = np.ones(fd.idx.shape, bool)
            for b in range(B):
                m = fd.neutral & (band == b)
                if not m.any(): continue
                r = robs[m]; F = self.Fb[b]
                l = np.searchsorted(F, r, "left"); h = np.searchsorted(F, r, "right") - 1
                bad = l > h                               # no albedo gives this rung at this band (seam dither pixel): take the nearest edge
                l2 = np.clip(l, 0, GRID - 1); h2 = np.clip(h, 0, GRID - 1)
                mid_bad = np.clip(np.where(bad, np.minimum(l, GRID - 1), 0), 0, GRID - 1)
                l = np.where(bad, mid_bad, l2); h = np.where(bad, mid_bad, h2)
                ok[m] = ~bad
                lo[m] = l; hi[m] = h
            fd.lo, fd.hi, fd.ok = lo, hi, ok
    # ---- replay with an amount function a(dist) -> [0,1]
    def render(self, fd, amount_fn, which="mid", albedo_override=None):
        P = self.P; s_tab, r_tab, L_tab = rung_table(P)
        a = amount_fn(fd.dist)
        i = {"lo": fd.lo, "hi": fd.hi, "mid": (fd.lo + fd.hi) // 2}[which]
        s0 = S0[i]
        if albedo_override is not None: s0 = np.where(fd.ground, albedo_override, s0)
        s = s0 + (P.t["airlightValue"] - s0) * a
        Ls = L_tab[np.minimum(np.round(np.clip(s, 0, 1) * (len(s_tab) - 1)).astype(int), len(s_tab) - 1)]
        return np.where(fd.neutral, Ls, fd.L)

def current_amount(P):
    return lambda dist: P.amount(P.level(dist))
