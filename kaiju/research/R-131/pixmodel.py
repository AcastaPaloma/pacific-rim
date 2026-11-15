"""Offline port of the Kaiju pixel pipeline for NEUTRAL (grey) scene surfaces: haze bands -> value grade -> ladder rungs.
Ports PaletteLutBaker.Ladder / Grade / ProjectRatio and PixelHaze.shader / HazeMath for the neutral-cell path only (the
test scene is all neutral greys; the accent path is not needed for C6/C7). Read-only on research/R-002/palettes.json and the
committed tuning JSON."""
import json, math, os
import numpy as np
from common import ROOT

def srgb_to_lin(c):
    c = np.asarray(c, float)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
def lin_to_srgb(c):
    c = np.asarray(c, float)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.maximum(c, 0) ** (1 / 2.4) - 0.055)
M = np.array([[0.4124564, 0.3575761, 0.1804375], [0.2126729, 0.7151522, 0.0721750], [0.0193339, 0.1191920, 0.9503041]])
WP = np.array([0.95047, 1.0, 1.08883])
def _f(t): return np.where(t > (6 / 29) ** 3, np.cbrt(t), t / (3 * (6 / 29) ** 2) + 4 / 29)
def lin_to_lab(rgb):
    xyz = np.asarray(rgb, float) @ M.T / WP
    fx, fy, fz = _f(xyz[..., 0]), _f(xyz[..., 1]), _f(xyz[..., 2])
    return np.stack([116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz)], -1)
def lab_to_lin(lab):
    lab = np.asarray(lab, float)
    fy = (lab[..., 0] + 16) / 116; fx = fy + lab[..., 1] / 500; fz = fy - lab[..., 2] / 200
    fi = lambda t: np.where(t > 6 / 29, t ** 3, 3 * (6 / 29) ** 2 * (t - 4 / 29))
    xyz = np.stack([fi(fx), fi(fy), fi(fz)], -1) * WP
    return xyz @ np.linalg.inv(M).T
def hex_rgb(h): return np.array([int(h[i:i + 2], 16) for i in (1, 3, 5)]) / 255.0
def grey_L(s): return 116 * _f(srgb_to_lin(s)) - 16     # L* of an sRGB grey (Y of grey = linear value)

class Profile:
    def __init__(self, pid, tuning_overrides=None):
        pal = json.load(open(os.path.join(ROOT, "research", "R-002", "palettes.json")))["profiles"][pid]
        tj = json.load(open(os.path.join(ROOT, "Assets", "Game", "Config", "Rendering", "PaletteProfileTuning.json")))
        t = dict(tj["defaults"]); t.update(tj["profiles"].get(pid, {}));
        if tuning_overrides: t.update(tuning_overrides)
        self.id, self.t = pid, t
        self.cols = [hex_rgb(c["hex"]) for c in pal["colours"]]
        self.roles = [c["role"] for c in pal["colours"]]
        self.lab = np.array([c["lab"] for c in pal["colours"]], float)
        self.ramp = np.array([x["lab"] for x in pal["ramp_uniform_L"]], float)
        self.accent = next((i for i, r in enumerate(self.roles) if "accent" in r), -1)
        self.rungs = self._ladder()
        self.rung_lin = srgb_to_lin(np.array([self.cols[i] for i in self.rungs]))
        self.rung_lab = np.array([lin_to_lab(l) for l in self.rung_lin])
        self.bands = int(t["hazeBands"])
    def _ladder(self):
        t = self.t; step = t["ladderMinStepL"]; ramp = self.ramp; lab = self.lab
        onhue = []
        for i, c in enumerate(lab):
            if i == self.accent: continue
            if c[0] >= ramp[-1][0]: on = ramp[-1]
            else:
                on = ramp[0]
                for j in range(len(ramp) - 1):
                    if ramp[j][0] <= c[0] <= ramp[j + 1][0]:
                        u = (c[0] - ramp[j][0]) / max(1e-9, ramp[j + 1][0] - ramp[j][0]); on = ramp[j] + u * (ramp[j + 1] - ramp[j]); break
            if math.hypot(c[1] - on[1], c[2] - on[2]) <= t["ladderHueTolerance"]: onhue.append(i)
        topL = t["ladderTopL"] if t["ladderTopL"] > 0 else ramp[-1][0]
        bottom = top = onhue[0]
        for i in onhue:
            if abs(lab[i][0] - ramp[0][0]) < abs(lab[bottom][0] - ramp[0][0]): bottom = i
            if abs(lab[i][0] - topL) < abs(lab[top][0] - topL): top = i
        onhue.sort(key=lambda i: (lab[i][0], i))
        rungs = [bottom]
        for i in onhue:
            l = lab[i][0]
            if l <= lab[bottom][0] or l >= lab[top][0]: continue
            if l - lab[rungs[-1]][0] >= step and lab[top][0] - l >= step: rungs.append(i)
        if top != bottom: rungs.append(top)
        return rungs
    # ---- haze (HazeMath / PixelHaze.shader)
    def level(self, dist, start=None, full=None, bands=None, gamma=None):
        t = self.t; start = start or t["hazeStartM"]; full = full or t["hazeFullM"]; bands = bands or self.bands; gamma = t["hazeCurveGamma"] if gamma is None else gamma
        u = np.clip(np.log(np.maximum(dist, start) / start) / math.log(full / start), 0, 1)
        return (bands - 1) * u ** gamma
    # ---- grade + quantise for grey sRGB value s (scene value after haze); returns (L*, ratio-expected L*)
    def quantise_grey(self, s, spread_dither=False):
        t = self.t; s = np.asarray(s, float)
        v = np.clip(grey_L(np.clip(s, 0, 1)) / 100.0, 0, 1)
        p = np.clip(t["valueBias"] + t["valueScale"] * v ** t["valueGamma"], 0, 1)
        n = len(self.rungs); f = p * (n - 1); i = np.minimum(np.floor(f).astype(int), n - 2); u = f - i
        tgt_lab = self.rung_lab[i] * (1 - u)[..., None] + self.rung_lab[i + 1] * u[..., None]
        tgt = lab_to_lin(tgt_lab)
        a, b = self.rung_lin[i], self.rung_lin[i + 1]
        d = b - a; ratio = np.clip(((tgt - a) * d).sum(-1) / np.maximum((d * d).sum(-1), 1e-12), 0, 1)
        La, Lb = self.rung_lab[i][..., 0], self.rung_lab[i + 1][..., 0]
        nearest = np.where(ratio > 0.5, Lb, La)
        return nearest, La * (1 - ratio) + Lb * ratio, i + (ratio > 0.5)
    def amount(self, level, mode="floor"):
        b = np.floor(level) if mode == "floor" else level
        return np.clip(b, 0, self.bands - 1) / (self.bands - 1)
    def render_grey(self, s0, dist, **kw):
        """Replay: albedo s0 (sRGB grey) at distance dist (m) through the CURRENT haze curve; returns nearest-rung L*."""
        a = self.amount(self.level(dist, **kw))
        s = s0 + (self.t["airlightValue"] - s0) * a
        return self.quantise_grey(s)[0]
