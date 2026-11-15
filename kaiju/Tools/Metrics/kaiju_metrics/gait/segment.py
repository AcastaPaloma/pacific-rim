"""Shared gait procedures: contact hysteresis, smoothing, strides, heading projections (R-001 §5, §9).

Conventions (gait_log@v1): Unity world, left-handed, +y up; yaw psi = atan2(fwd.x, fwd.z), clockwise from above;
forward = (sin psi, 0, cos psi), right = (cos psi, 0, -sin psi)."""
from __future__ import annotations

from dataclasses import dataclass, field

import numpy as np


@dataclass
class Contact:
    foot_index: int
    foot_id: str
    b: int                  # first sample with F_n >= F_on (index into the log arrays)
    e: int                  # first sample after the contact (exclusive); == N when still on at log end
    truncated_start: bool
    truncated_end: bool
    tags: list = field(default_factory=list)
    slip_m: float = float("nan")
    slip_point: int = -1
    slip_t: float = float("nan")
    excluded: bool = False
    contact_id: object = None       # emitter's contact_id when a contact_begin event matches


def hysteresis(fn, f_on, f_off):
    """Contact state: starts off; on at the first sample with fn >= F_on; off at the first sample with fn < F_off."""
    on = np.zeros(len(fn), bool)
    s = False
    for i, f in enumerate(fn):
        if not s and f >= f_on:
            s = True
        elif s and f < f_off:
            s = False
        on[i] = s
    return on


def contacts_from_state(state, foot_index, foot_id, fn, f_on):
    out = []
    N = len(state)
    k = 0
    while k < N:
        if state[k]:
            b = k
            while k < N and state[k]:
                k += 1
            out.append(Contact(foot_index, foot_id, b, k, truncated_start=(b == 0), truncated_end=(k == N)))
        else:
            k += 1
    for c in out:
        if c.truncated_start:
            c.tags.append("truncated_start")
        if c.truncated_end:
            c.tags.append("truncated_end")
    return out


def spec_contacts(log, f_on, f_off):
    cs = []
    states = []
    for i, f in enumerate(log.feet):
        st = hysteresis(f["fn"], f_on, f_off)
        states.append(st)
        cs += contacts_from_state(st, i, f["id"], f["fn"], f_on)
    begins = {}
    for ev in log.events:
        if ev.get("type") == "contact_begin":
            begins[(ev["n"], ev["foot"])] = ev["contact_id"]
    n0 = int(log.n[0])
    for c in cs:
        c.contact_id = begins.get((n0 + c.b, c.foot_id))
    cs.sort(key=lambda c: (c.b, c.foot_index))
    return cs, states


def odd_width(width_s, dt):
    w = width_s / dt
    return max(1, 2 * int(round((w - 1) / 2)) + 1)


def moving_average(x, n):
    """Centred moving average over n (odd) samples; windows shrink symmetrically-truncated at the ends."""
    x = np.asarray(x, dtype=np.float64)
    if n <= 1:
        return x.copy()
    h = n // 2
    ax0 = x.shape[0]
    c = np.concatenate([np.zeros((1,) + x.shape[1:]), np.cumsum(x, axis=0)], axis=0)
    i = np.arange(ax0)
    lo = np.clip(i - h, 0, ax0)
    hi = np.clip(i + h + 1, 0, ax0)
    cnt = (hi - lo).reshape((-1,) + (1,) * (x.ndim - 1))
    return (c[hi] - c[lo]) / cnt


def cdiff(x, dt):
    """Central difference (one-sided at the two ends)."""
    x = np.asarray(x, dtype=np.float64)
    d = np.empty_like(x)
    d[1:-1] = (x[2:] - x[:-2]) / (2 * dt)
    d[0] = (x[1] - x[0]) / dt
    d[-1] = (x[-1] - x[-2]) / dt
    return d


def heading_axes(yaw):
    fwd = np.stack([np.sin(yaw), np.zeros_like(yaw), np.cos(yaw)], -1)
    right = np.stack([np.cos(yaw), np.zeros_like(yaw), -np.sin(yaw)], -1)
    return fwd, right


def horizontal_speed(com_v):
    return np.hypot(com_v[:, 0], com_v[:, 2])


@dataclass
class Stride:
    foot_index: int
    b: int
    e: int
    T: float
    v_bar: float
    stance_s: float
    cmd_const: bool
    steady: bool = False


def strides(log, contacts, g, steady_dv_gT):
    """Same-foot contact_begin to contact_begin; truncated-start contacts do not open a stride.
    v_bar = horizontal CoM displacement / stride time. Steady: cmd_speed constant over two consecutive strides
    and |v_bar_i - v_bar_(i-1)| < steady_dv_gT * g * T_i; both strides of such a pair are steady."""
    dt = log.dt
    out = []
    for fi in range(len(log.feet)):
        cs = [c for c in contacts if c.foot_index == fi and not c.truncated_start]
        fs = []
        for a, b in zip(cs[:-1], cs[1:]):
            T = (b.b - a.b) * dt
            d = log.com_p[b.b] - log.com_p[a.b]
            vb = float(np.hypot(d[0], d[2]) / T)
            cmd = log.cmd_speed[a.b:b.b + 1]
            stance = (a.e - a.b) * dt if not a.truncated_end else float("nan")
            fs.append(Stride(fi, a.b, b.b, T, vb, stance, bool(np.all(cmd == cmd[0]))))
        for i in range(1, len(fs)):
            p, q = fs[i - 1], fs[i]
            same_cmd = p.cmd_const and q.cmd_const and log.cmd_speed[p.b] == log.cmd_speed[q.b]
            if same_cmd and abs(q.v_bar - p.v_bar) < steady_dv_gT * g * q.T:
                p.steady = q.steady = True
        out += fs
    return out


def quat_to_mat(q):
    """[x, y, z, w] quaternions (..., 4) -> rotation matrices (..., 3, 3)."""
    q = np.asarray(q, dtype=np.float64)
    q = q / np.linalg.norm(q, axis=-1, keepdims=True)
    x, y, z, w = q[..., 0], q[..., 1], q[..., 2], q[..., 3]
    R = np.empty(q.shape[:-1] + (3, 3))
    R[..., 0, 0] = 1 - 2 * (y * y + z * z); R[..., 0, 1] = 2 * (x * y - z * w); R[..., 0, 2] = 2 * (x * z + y * w)
    R[..., 1, 0] = 2 * (x * y + z * w); R[..., 1, 1] = 1 - 2 * (x * x + z * z); R[..., 1, 2] = 2 * (y * z - x * w)
    R[..., 2, 0] = 2 * (x * z - y * w); R[..., 2, 1] = 2 * (y * z + x * w); R[..., 2, 2] = 1 - 2 * (x * x + y * y)
    return R


def stride_of_sample(strides_, k, foot_index=0):
    for s in strides_:
        if s.foot_index == foot_index and s.b <= k < s.e:
            return [int(s.b), int(s.e)]
    return None
