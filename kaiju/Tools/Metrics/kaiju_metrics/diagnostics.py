"""Diagnostic images, written headless (Agg) with deterministic names and no timestamps."""
from __future__ import annotations

import os

import numpy as np

from .png import write_png


def _plt():
    import matplotlib
    matplotlib.use("Agg", force=True)
    import matplotlib.pyplot as plt
    return plt


def _save(fig, path):
    fig.savefig(path, dpi=90, metadata={"Software": None})


def slip_plot(out_dir, contacts, max_frac):
    plt = _plt()
    fig, ax = plt.subplots(figsize=(9, 3.6))
    caps = sorted({c.capture for c in contacts})
    x = np.arange(len(contacts))
    order = sorted(contacts, key=lambda c: (caps.index(c.capture), c.b, c.foot_index))
    v = np.array([100 * c.slip_m / c.L_f for c in order])
    col = ["#c0392b" if (100 * c.slip_m / c.L_f) >= 100 * max_frac else ("#7f8c8d" if c.excluded else "#2c7fb8")
           for c in order]
    ax.bar(x, v, color=col, width=0.9)
    ax.axhline(100 * max_frac, color="#c0392b", lw=1, ls="--", label=f"M-002.1 limit {100 * max_frac:.2f} % L_f")
    if len(order):
        w = int(np.argmax(v))
        c = order[w]
        ax.annotate(f"worst: {c.capture} {c.foot_id} id={c.contact_id} t={c.t_begin:.2f}s {c.slip_m:.3f} m",
                    (w, v[w]), textcoords="offset points", xytext=(5, 5), fontsize=7)
    ax.set_ylabel("slip s_c (% of L_f)")
    ax.set_xlabel("contacts (grouped by capture, time order)")
    ax.legend(fontsize=7, loc="upper right")
    fig.tight_layout()
    name = "diag_slip.png"
    _save(fig, os.path.join(out_dir, name))
    plt.close(fig)
    return name


def gait_plot(out_dir, caps, report):
    plt = _plt()
    fig, axes = plt.subplots(3, 1, figsize=(9, 6.5), sharex=False)
    for c in caps:
        t = c.lg.t
        axes[0].plot(t, c.a_fore, lw=0.8, label=c.name)
        axes[1].plot(t, c.a_lat, lw=0.8)
        axes[2].plot(t, np.degrees(c.yaw_rate), lw=0.8)
    for ax, sid, ylab in ((axes[0], "M-001.4", "a_fore (m/s^2)"), (axes[1], "M-001.5", "a_lat (m/s^2)"),
                          (axes[2], "M-001.7", "yaw rate (deg/s)")):
        d = report.sms.get(sid)
        if d and d.get("threshold") is not None:
            ax.axhline(d["threshold"], color="#c0392b", ls="--", lw=0.8)
            ax.axhline(-d["threshold"], color="#c0392b", ls="--", lw=0.8)
        ax.set_ylabel(ylab, fontsize=8)
    axes[0].legend(fontsize=6, ncol=4)
    axes[2].set_xlabel("t (s)")
    fig.tight_layout()
    name = "diag_gait.png"
    _save(fig, os.path.join(out_dir, name))
    plt.close(fig)
    return name


def heat_map(out_dir, name, values, vmax, mask=None, marks=()):
    """Grey-to-red heat map PNG (0 -> black, vmax -> red/yellow); masked pixels blue."""
    v = np.clip(np.asarray(values, float) / max(vmax, 1e-9), 0, 1)
    rgb = np.stack([np.clip(2 * v, 0, 1), np.clip(2 * v - 1, 0, 1), np.zeros_like(v)], -1)
    if mask is not None:
        rgb[mask] = [0.1, 0.2, 0.6]
    img = (rgb * 255).astype(np.uint8)
    for (x0, y0, x1, y1) in marks:
        img[y0:y1, [x0, max(x1 - 1, x0)]] = [0, 255, 255]
        img[[y0, max(y1 - 1, y0)], x0:x1] = [0, 255, 255]
    write_png(os.path.join(out_dir, name), img)
    return name


def mask_image(out_dir, name, base_rgb, mask, colour=(255, 0, 255)):
    img = (np.asarray(base_rgb, float) * 0.5).astype(np.uint8)
    img[mask] = colour
    write_png(os.path.join(out_dir, name), img)
    return name
