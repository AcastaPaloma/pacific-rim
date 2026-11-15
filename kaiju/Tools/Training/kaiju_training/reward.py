"""Reward from the bridge event vector (R-028 section 6). Weights live in the run config, not in C#.

Outcome terms (win / loss / timeout / kills / damage / deaths) are permanent. Behaviour shaping from the M-007 primitives
(flanked vs unflanked multi-kaiju commits) is annealed linearly to 0 at `anneal_frac` of training, so the final policy is
optimised for winning only; M-007 C1-C4 stay gates measured on logs, never rewards (anti-Goodhart, R-028 6.3)."""
from __future__ import annotations

import numpy as np

from .bridge import EV_NAMES

IDX = {n: i for i, n in enumerate(EV_NAMES)}
OUTCOME = {  # config key -> event name
    "dmg_dealt": "dmg_dealt", "kill_mine": "kills_mine", "team_kill": "team_kills", "dmg_taken": "dmg_taken",
    "i_died": "i_died", "ally_death": "ally_deaths", "win": "win", "loss": "loss", "timeout": "timeout",
    "voluntary_switch": "voluntary_switch", "time_per_s": "elapsed_s",
}


def shaping_scale(progress: float, anneal_frac: float) -> float:
    if anneal_frac <= 0:
        return 0.0
    return float(max(0.0, 1.0 - progress / anneal_frac))


def compute(ev: np.ndarray, weights: dict, progress: float = 0.0) -> np.ndarray:
    """ev [n, ev_dim] -> reward [n]. progress in [0, 1] = fraction of the training budget used."""
    ev = np.asarray(ev, dtype=np.float64)
    r = np.zeros(ev.shape[0])
    for key, name in OUTCOME.items():
        w = float(weights.get(key, 0.0))
        if w:
            r += w * ev[:, IDX[name]]
    sh = weights.get("shaping", {}) or {}
    s = shaping_scale(progress, float(sh.get("anneal_frac", 0.0)))
    if s > 0:
        flanked = ev[:, IDX["commit_flanked"]]
        unflanked = np.maximum(ev[:, IDX["commit_multi"]] - flanked, 0.0)
        r += s * (float(sh.get("commit_flanked", 0.0)) * flanked + float(sh.get("commit_unflanked", 0.0)) * unflanked)
    return r.astype(np.float32)
