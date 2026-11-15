"""Candidate haze-amount curves. Each factory returns amount_fn(dist_m) -> haze amount in [0, 1] (what the shader lerps toward the airlight).
A 'band' count B quantises the amount to B equal steps exactly as PixelHaze.shader does (amount = floor(level)/(B-1))."""
import math, numpy as np

def q(a, B):                       # posterise an amount in [0,1] into B bands (the last band = pure airlight only when a reaches 1)
    return np.clip(np.floor(a * (B - 1) + 1e-9), 0, B - 1) / (B - 1)

def log_curve(start, full, B, gamma=1.0):
    def f(d):
        u = np.clip(np.log(np.maximum(d, start) / start) / math.log(full / start), 0, 1)
        return q(u ** gamma, B)
    return f

def knee_curve(start, knee, full, w, g1, g2, B):
    """Piecewise log: fraction w of the haze range is spent between start and knee (shape g1), the rest slowly to full (shape g2)."""
    def f(d):
        d = np.maximum(d, start)
        u1 = np.clip(np.log(d / start) / math.log(knee / start), 0, 1) ** g1
        u2 = np.clip(np.log(np.maximum(d, knee) / knee) / math.log(full / knee), 0, 1) ** g2
        return q(w * u1 + (1 - w) * np.where(d > knee, u2, 0.0), B)
    return f

def beer_two(start, L1, L2, m, B, scale=1.0):
    """Beer-Lambert extinction with a two-coefficient (Mie short, Rayleigh long) transmittance: a = scale * (1 - m e^{-(d-start)/L1} - (1-m) e^{-(d-start)/L2}).
    Per-channel extinction does not change anything here: the neutral ladder only reads lightness (see R-131 section 4)."""
    def f(d):
        x = np.maximum(d - start, 0.0)
        a = scale * (1 - m * np.exp(-x / L1) - (1 - m) * np.exp(-x / L2))
        return q(np.clip(a, 0, 1), B)
    return f

def staircase(log_thresholds, amounts):
    """Table-driven haze: amount[k] for ln(d) in [t_k, t_{k+1}); amount 0 below t_0. Monotone by construction."""
    t = np.asarray(log_thresholds, float); A = np.asarray(amounts, float)
    def f(d):
        k = np.searchsorted(t, np.log(np.maximum(d, 1e-3)), "right") - 1
        return np.where(k >= 0, A[np.clip(k, 0, len(A) - 1)], 0.0)
    return f
