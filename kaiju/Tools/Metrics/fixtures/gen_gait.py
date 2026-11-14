"""Deterministic synthetic gait_log@v1 generator with closed-form truth (fixtures for M-001 / M-002).

Independent of kaiju_metrics: it never imports the tool. Motion is built from piecewise-constant accelerations
(speed and yaw), so speed, heading, accelerations, start / stop times, stride period, duty factor and slip are
known exactly. Unity world frame: left-handed, +y up, +x east, +z north; yaw clockwise from above,
forward = (sin psi, 0, cos psi), right = (cos psi, 0, -sin psi).

Usage: python fixtures/gen_gait.py <out_dir> [--example]   (writes the eight M-001 captures plus stomp)
"""
from __future__ import annotations

import json
import math
import os
import sys
from dataclasses import dataclass, field

import numpy as np

G = 9.81
REF = dict(H_m=80.0, leg_length_m=42.4, com_height_stand_m=44.8, mass_kg=1.0e7, I_yaw_kgm2=4.75e8,
           foot_length_m=19.6, foot_width_m=7.65, fixed_dt_s=0.02, reactor_power_W=2.0e8, T_stride_nom_s=6.60,
           fr_cruise_nom=0.20, mu_assumed=0.4)
HIP_OFFSET = 4.8          # lateral foot offset from the CoM path (0.06 H)
ORIGIN = np.array([5512.3125, 0.0, 10233.8125])   # absolute float64 world position of the run start


def alexander_T(fr, l=REF["leg_length_m"]):
    return 2.3 * fr ** -0.2 * math.sqrt(l / G)


def v_of_fr(fr, l=REF["leg_length_m"]):
    return math.sqrt(fr * G * l)


class Profile:
    """Piecewise-constant second derivative: x(t), x'(t), x''(t) in closed form."""

    def __init__(self, x0=0.0, v0=0.0, segments=()):
        self.t0, self.x0, self.v0, self.a = [0.0], [x0], [v0], []
        for dur, acc in segments:
            t, x, v = self.t0[-1], self.x0[-1], self.v0[-1]
            self.a.append(acc)
            self.t0.append(t + dur)
            self.x0.append(x + v * dur + 0.5 * acc * dur * dur)
            self.v0.append(v + acc * dur)
        self.a.append(0.0)

    def _k(self, t):
        return np.clip(np.searchsorted(np.array(self.t0), t, side="right") - 1, 0, len(self.t0) - 1)

    def x(self, t):
        t = np.asarray(t, float); k = self._k(t); tau = t - np.array(self.t0)[k]
        return np.array(self.x0)[k] + np.array(self.v0)[k] * tau + 0.5 * np.array(self.a)[k] * tau ** 2

    def v(self, t):
        t = np.asarray(t, float); k = self._k(t); tau = t - np.array(self.t0)[k]
        return np.array(self.v0)[k] + np.array(self.a)[k] * tau

    def acc(self, t):
        t = np.asarray(t, float); return np.array(self.a)[self._k(t)]

    @property
    def end(self):
        return self.t0[-1]


def yaw_quat(psi):
    psi = np.asarray(psi, float)
    return np.stack([np.zeros_like(psi), np.sin(psi / 2), np.zeros_like(psi), np.cos(psi / 2)], -1)


def wrap(a):
    a = np.mod(np.asarray(a, float) + math.pi, 2 * math.pi) - math.pi
    return np.where(a <= -math.pi, a + 2 * math.pi, a)


@dataclass
class Spec:
    """Everything that defines one synthetic capture. Units SI, angles rad."""
    capture: str
    duration_s: float
    speed: Profile                    # forward speed along the velocity direction
    yaw: Profile                      # heading psi (x = psi, v = yaw rate, a = yaw acceleration)
    vel_dir: Profile | None = None    # velocity direction (None = heading); constant != heading gives a crab walk
    sway_amp_mps: float = 0.0         # lateral velocity amplitude (sinusoid) along right(vel_dir)
    sway_period_s: float = 6.6
    stride_s: float = 6.60            # stride period while stepping (rounded to whole samples)
    duty: float = 0.66
    step_window: tuple = (0.0, 1e9)   # [start, end) of stepping; both feet planted outside
    cmd: list = field(default_factory=list)     # [(t, move, cmd_speed, cmd_yaw_rate)]
    slips: dict = field(default_factory=dict)   # {(foot_index, contact_ordinal): (dx, dz)} metres over the stance
    roll: dict = field(default_factory=dict)    # {(foot_index, contact_ordinal): max_angle_rad} heel-to-toe roll
    sink_m: dict = field(default_factory=dict)  # {(foot_index, contact_ordinal): metres} vertical sinkage
    ext_f: list = field(default_factory=list)   # [(t0, t1, newtons)]
    flag_flip_every: int = 0          # flip the emitter contact flag on every k-th step (M-002.4 control)
    header: dict = field(default_factory=dict)  # header overrides
    dt: float = 0.02
    float32_positions: bool = False
    scuff: list = field(default_factory=list)   # [(foot_index, t0, t1)] extra short contacts


def build(spec: Spec):
    """Return (records, truth). records: list of dicts in file order. truth: closed-form quantities."""
    dt = spec.dt
    N = int(round(spec.duration_s / dt)) + 1
    n = np.arange(N)
    t = n * dt
    vdir = spec.vel_dir or spec.yaw
    psi, psid = spec.yaw.x(t), spec.yaw.v(t)
    phi = vdir.x(t)
    sp = spec.speed.v(t)

    # CoM velocity and position (position integrated with an 8-substep Simpson rule per step: |err| < 1e-9 m)
    def vel(tt):
        ph = vdir.x(tt); s = spec.speed.v(tt)
        f = np.stack([np.sin(ph), np.zeros_like(ph), np.cos(ph)], -1)
        r = np.stack([np.cos(ph), np.zeros_like(ph), -np.sin(ph)], -1)
        u = spec.sway_amp_mps * np.sin(2 * math.pi * tt / spec.sway_period_s)
        return f * s[..., None] + r * u[..., None]
    com_v = vel(t)
    sub = 8
    ts = (n[:-1, None] + np.linspace(0, 1, sub + 1)[None, :]) * dt
    vs = vel(ts.reshape(-1)).reshape(N - 1, sub + 1, 3)
    wS = np.ones(sub + 1); wS[1:-1:2] = 4; wS[2:-1:2] = 2
    inc = (vs * wS[None, :, None]).sum(1) * dt / (3 * sub)
    com_p = np.vstack([np.zeros(3), np.cumsum(inc, 0)]) + ORIGIN + np.array([0, REF["com_height_stand_m"] - 0.18, 0])

    # contact schedule
    NT = int(round(spec.stride_s / dt)); Ns = int(round(spec.duty * NT))
    k0 = int(round(spec.step_window[0] / dt)); k1 = min(N, int(round(spec.step_window[1] / dt)))
    stance = np.zeros((2, N), bool)
    for i, ph in enumerate((0, NT // 2)):
        for k in range(N):
            if k < k0 or k >= k1:
                stance[i, k] = True
            else:
                stance[i, k] = ((k - k0 - ph) % NT) < Ns
    for fi, a, b in spec.scuff:   # insert a short contact inside a swing
        stance[fi, int(round(a / dt)):int(round(b / dt))] = True
    # contacts as [begin, end) intervals
    contacts = []
    for i in range(2):
        k = 0
        while k < N:
            if stance[i, k]:
                b = k
                while k < N and stance[i, k]:
                    k += 1
                contacts.append((i, b, k))
            else:
                k += 1
    contacts.sort(key=lambda c: (c[1], c[0]))
    ordinal = {}
    per_foot = [0, 0]
    for c in contacts:
        ordinal[c] = per_foot[c[0]]; per_foot[c[0]] += 1

    # foot poses
    L, Wf = REF["foot_length_m"], REF["foot_width_m"]
    sole = np.array([[0, 0, 0], [0.45 * Wf, 0, 0.45 * L], [-0.45 * Wf, 0, 0.45 * L],
                     [0.45 * Wf, 0, -0.45 * L], [-0.45 * Wf, 0, -0.45 * L]])
    fp = np.zeros((2, N, 3)); fq = np.zeros((2, N, 4)); fq[..., 3] = 1
    plant = {}
    for c in contacts:
        i, b, e = c
        km = min(N - 1, (b + e) // 2)
        side = -1.0 if i == 0 else 1.0
        ps = psi[km]
        right = np.array([math.cos(ps), 0, -math.sin(ps)])
        pos = com_p[km].copy(); pos[1] = 0.0
        pos = pos + side * HIP_OFFSET * right
        plant[c] = (pos, ps)
        m = e - b
        for j, k in enumerate(range(b, e)):
            s = j / max(m - 1, 1)
            p = pos.copy()
            q = yaw_quat(ps)
            if (i, ordinal[c]) in spec.slips:
                dx, dz = spec.slips[(i, ordinal[c])]
                p = p + np.array([dx, 0.0, dz]) * s
            if (i, ordinal[c]) in spec.sink_m:
                p = p + np.array([0.0, -spec.sink_m[(i, ordinal[c])] * s, 0.0])
            if (i, ordinal[c]) in spec.roll and s > 0.7:
                th = spec.roll[(i, ordinal[c])] * (s - 0.7) / 0.3
                # heel-up rotation R_x(th) about the toe line (local z = +0.45 L): local point l maps to
                # z_t e_z + R_x (l - z_t e_z); the foot-frame origin therefore moves by (0, z_t sin th, z_t (1 - cos th))
                zt = 0.45 * L
                lp = np.array([0.0, zt * math.sin(th), zt - zt * math.cos(th)])
                p = p + _yaw_mat(ps) @ lp
                qx = np.array([math.sin(th / 2), 0, 0, math.cos(th / 2)])
                q = _qmul(yaw_quat(ps), qx)
            fp[i, k] = p; fq[i, k] = q
    # swing interpolation between successive contacts of a foot
    for i in range(2):
        cs = [c for c in contacts if c[0] == i]
        for a, b in zip(cs[:-1], cs[1:]):
            k_lo, k_hi = a[2], b[1]
            if k_hi <= k_lo:
                continue
            p0 = fp[i, k_lo - 1]; p1 = plant[b][0]
            y0, y1 = plant[a][1], plant[b][1]
            for k in range(k_lo, k_hi):
                s = (k - k_lo + 1) / (k_hi - k_lo + 1)
                p = p0 + (p1 - p0) * s
                p[1] = p0[1] + (p1[1] - p0[1]) * s + 3.0 * math.sin(math.pi * s)
                fp[i, k] = p; fq[i, k] = yaw_quat(y0 + (y1 - y0) * s)
        if cs and cs[0][1] > 0:     # before the first contact (only when the log starts mid-swing)
            fp[i, :cs[0][1]] = plant[cs[0]][0]
        if cs and cs[-1][2] < N:
            fp[i, cs[-1][2]:] = fp[i, cs[-1][2] - 1]
    fv = np.gradient(fp, dt, axis=1)

    m = spec.header.get("mass_kg", REF["mass_kg"])
    both = stance.sum(0)
    fn = np.where(stance, m * G / np.maximum(both, 1)[None, :], 0.0)
    ext = np.zeros(N)
    for a, b, f in spec.ext_f:
        ext[(t >= a) & (t < b)] = f
    flag = stance.astype(int)
    if spec.flag_flip_every:
        idx = np.arange(0, N, spec.flag_flip_every)
        flag[0, idx] = 1 - flag[0, idx]

    # commands
    cmds = sorted(spec.cmd)
    def cmd_at(k):
        cur = ("idle", 0.0, 0.0)
        for (tc, mv, cs_, cy) in cmds:
            if t[k] + 1e-12 >= tc:
                cur = (mv, cs_, cy)
        return cur

    hdr = dict(rec="header", schema="gait_log@v1", capture=spec.capture, commit="synthetic", unit_id="mech_ref_synth",
               unit_kind="mech", H_m=REF["H_m"], leg_length_m=REF["leg_length_m"],
               com_height_stand_m=REF["com_height_stand_m"], mass_kg=REF["mass_kg"], I_yaw_kgm2=REF["I_yaw_kgm2"],
               foot_ids=["L", "R"],
               feet=[dict(id=fid, length_m=L, width_m=Wf, sole_points_local=sole.tolist()) for fid in ("L", "R")],
               fixed_dt_s=dt, physics_hz=round(1 / dt, 9), render_hz_target=60, g_mps2=G, mu_assumed=REF["mu_assumed"],
               reactor_power_W=REF["reactor_power_W"], T_stride_nom_s=REF["T_stride_nom_s"],
               fr_cruise_nom=REF["fr_cruise_nom"],
               thresholds=dict(F_on_N=0.03 * REF["mass_kg"] * G, F_off_N=0.01 * REF["mass_kg"] * G),
               camera_rig="SyntheticRig")
    hdr.update({k: v for k, v in spec.header.items()})
    if "mass_kg" in spec.header and "thresholds" not in spec.header:
        hdr["thresholds"] = dict(F_on_N=0.03 * m * G, F_off_N=0.01 * m * G)
    root_p = com_p - np.array([0, 2.9, 0])
    rq = yaw_quat(psi)
    yw = wrap(psi)
    if spec.float32_positions:
        com_p = com_p.astype(np.float32).astype(np.float64)
        root_p = root_p.astype(np.float32).astype(np.float64)
        fp = fp.astype(np.float32).astype(np.float64)

    begins = {(c[0], c[1]): (cid, c) for cid, c in enumerate(contacts)}
    ends = {(c[0], c[2]): (cid, c) for cid, c in enumerate(contacts)}
    recs = [hdr]
    prev_cmd = None
    for k in range(N):
        mv, cs_, cy = cmd_at(k)
        feet = []
        for i in range(2):
            st = bool(stance[i, k])
            feet.append(dict(p=fp[i, k].tolist(), q=fq[i, k].tolist(), v=fv[i, k].tolist(), contact=int(flag[i, k]),
                             fn=float(fn[i, k]), ft=[0.0, 0.0, 0.0], cn=[0.0, 1.0, 0.0] if st else None))
        recs.append(dict(rec="step", n=int(k), t=float(k * dt), com_p=com_p[k].tolist(), com_v=com_v[k].tolist(),
                         root_p=root_p[k].tolist(), root_q=rq[k].tolist(), yaw=float(yw[k]), yaw_rate=float(psid[k]),
                         feet=feet, ext_f=float(ext[k]),
                         intent=dict(move=mv, target=[0.0, 0.0, 0.0], effort=0.6, cmd_speed_mps=float(cs_),
                                     cmd_yaw_rate_rps=float(cy))))
        if prev_cmd is not None and (mv, cs_, cy) != prev_cmd:
            recs.append(dict(rec="event", n=int(k), t=float(k * dt), type="intent_change", move=mv,
                             cmd_speed_mps=float(cs_), cmd_yaw_rate_rps=float(cy)))
        prev_cmd = (mv, cs_, cy)
        for i in range(2):
            if (i, k) in ends and k > 0:
                cid, _ = ends[(i, k)]
                recs.append(dict(rec="event", n=int(k), t=float(k * dt), type="contact_end", foot=["L", "R"][i],
                                 contact_id=cid))
            if (i, k) in begins:
                cid, _ = begins[(i, k)]
                recs.append(dict(rec="event", n=int(k), t=float(k * dt), type="contact_begin", foot=["L", "R"][i],
                                 contact_id=cid, fn=float(fn[i, k]), foot_v=fv[i, k].tolist()))
                recs.append(dict(rec="event", n=int(k), t=float(k * dt), type="cam_impulse", foot=["L", "R"][i],
                                 contact_id=cid, amplitude_m=0.4, dir=[0.0, -1.0, 0.0], source_n=int(k)))
    truth = dict(N=N, dt=dt, stride_samples=NT, stance_samples=Ns, stride_s=NT * dt, duty=Ns / NT,
                 contacts=[(["L", "R"][c[0]], c[1], c[2]) for c in contacts], ordinal=ordinal)
    return recs, truth


def _yaw_mat(psi):
    c, s = math.cos(psi), math.sin(psi)
    return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])


def _qmul(a, b):
    ax, ay, az, aw = a; bx, by, bz, bw = b
    return np.array([aw * bx + ax * bw + ay * bz - az * by, aw * by - ax * bz + ay * bw + az * bx,
                     aw * bz + ax * by - ay * bx + az * bw, aw * bw - ax * bx - ay * by - az * bz])


def write(records, path):
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        for r in records:
            f.write(json.dumps(r, separators=(",", ":")) + "\n")
    return path


# ------------------------------------------------------------------ reference captures (all sub-measures pass)
FR_NOM = REF["fr_cruise_nom"]
VC = v_of_fr(FR_NOM)                      # 9.1203 m/s
T_NOM = REF["T_stride_nom_s"]
STEP = T_NOM / 2


def spec_walk(fr=0.20, strides=5, heading_deg=0.0, capture=None, **kw):
    v = v_of_fr(fr)
    T = alexander_T(fr)
    dur = strides * T + 1.0
    psi0 = math.radians(heading_deg)
    return Spec(capture=capture or f"walk_fr{int(round(fr * 100)):03d}", duration_s=dur,
                speed=Profile(0.0, v, [(dur + 1, 0.0)]), yaw=Profile(psi0, 0.0, [(dur + 1, 0.0)]),
                stride_s=T, duty=kw.pop("duty", 0.66), cmd=[(0.0, "walk", v, 0.0)], **kw)


def spec_start(steps_to_90=3.0, t_cmd=2.0, cruise_strides=4, **kw):
    """Constant acceleration a from 0 so that 0.9 VC is reached after `steps_to_90` steps; then cruise."""
    t90 = steps_to_90 * STEP
    a = 0.9 * VC / t90
    t_acc = VC / a
    dur = t_cmd + t_acc + cruise_strides * T_NOM + 1.0
    return Spec(capture="start", duration_s=dur, speed=Profile(0.0, 0.0, [(t_cmd, 0.0), (t_acc, a), (dur, 0.0)]),
                yaw=Profile(0.3, 0.0, [(dur + 1, 0.0)]), stride_s=T_NOM, step_window=(t_cmd, 1e9),
                cmd=[(0.0, "idle", 0.0, 0.0), (t_cmd, "walk", VC, 0.0)], **kw)


def spec_stop(steps_to_stop=2.0, cruise_strides=4, v0=VC, capture="stop", speed_floor=0.3, spike=None, **kw):
    """Cruise, then constant deceleration so that speed < 0.3 m/s is reached after `steps_to_stop` steps."""
    t_cmd = cruise_strides * T_NOM + 0.5
    t_stop = steps_to_stop * STEP
    d = (v0 - speed_floor) / t_stop
    t_dec = v0 / d
    dur = t_cmd + t_dec + 4.0
    segs = [(t_cmd, 0.0), (t_dec, -d), (dur, 0.0)]
    if spike is not None:     # (t_offset, duration, extra decel) inside the deceleration
        to, du, ex = spike
        d2 = d                     # keep total time: compensate with no change (spike shortens the stop slightly)
        segs = [(t_cmd, 0.0), (to, -d2), (du, -(d2 + ex))]
        v_after = v0 - d2 * to - (d2 + ex) * du
        segs += [(v_after / d2, -d2), (dur, 0.0)]
    return Spec(capture=capture, duration_s=dur, speed=Profile(0.0, v0, segs),
                yaw=Profile(-0.7, 0.0, [(dur + 1, 0.0)]), stride_s=T_NOM, step_window=(0.0, t_cmd + t_dec),
                cmd=[(0.0, "walk", v0, 0.0), (t_cmd, "stop" if capture == "stop" else "estop", 0.0, 0.0)], **kw)


def spec_estop(decel=2.5, **kw):
    """Emergency stop at constant deceleration `decel` (m/s^2) from cruise."""
    t_stop = (VC - 0.3) / decel
    return spec_stop(steps_to_stop=t_stop / STEP, capture="estop", **kw)


def spec_turn_inplace(peak_rate_dps=20.0, yaw_acc_dps2=3.0, total_deg=90.0, capture="turn90_inplace", **kw):
    """Trapezoidal yaw-rate profile (accelerate, coast, decelerate) turning `total_deg` in place."""
    w = math.radians(peak_rate_dps); al = math.radians(yaw_acc_dps2); tot = math.radians(total_deg)
    t_a = w / al
    ang_a = 0.5 * al * t_a ** 2
    if 2 * ang_a > tot:       # triangle profile
        t_a = math.sqrt(tot / al); t_c = 0.0
    else:
        t_c = (tot - 2 * ang_a) / w
    t0 = 2.0
    dur = t0 + 2 * t_a + t_c + 4.0
    yaw = Profile(0.25, 0.0, [(t0, 0.0), (t_a, al), (t_c, 0.0), (t_a, -al), (dur, 0.0)])
    return Spec(capture=capture, duration_s=dur, speed=Profile(0.0, 0.0, [(dur + 1, 0.0)]), yaw=yaw,
                stride_s=T_NOM, step_window=(t0, t0 + 2 * t_a + t_c),
                cmd=[(0.0, "idle", 0.0, 0.0), (t0, "turn", 0.0, w)], **kw)


def spec_curve(rate_dps=4.0, yaw_acc_dps2=1.0, strides=5, crab=False, **kw):
    """Cruise with a yaw-rate ramp up to `rate_dps`, held, ramped down. crab=True keeps the velocity direction fixed."""
    w = math.radians(rate_dps); al = math.radians(yaw_acc_dps2)
    t_a = w / al
    dur = strides * T_NOM + 1.0
    t_c = max(0.0, dur - 2 * t_a - 6.0)
    yaw = Profile(1.1, 0.0, [(3.0, 0.0), (t_a, al), (t_c, 0.0), (t_a, -al), (dur, 0.0)])
    return Spec(capture="curve", duration_s=dur, speed=Profile(0.0, VC, [(dur + 1, 0.0)]), yaw=yaw,
                vel_dir=Profile(1.1, 0.0, [(dur + 1, 0.0)]) if crab else None, stride_s=T_NOM,
                cmd=[(0.0, "walk", VC, w)], **kw)


def spec_stomp(**kw):
    """Standing with one short single-foot lift and stomp of the right foot (M-002 input)."""
    dur = 12.0
    return Spec(capture="stomp", duration_s=dur, speed=Profile(0.0, 0.0, [(dur + 1, 0.0)]),
                yaw=Profile(0.0, 0.0, [(dur + 1, 0.0)]), stride_s=T_NOM, step_window=(4.0, 4.0 + T_NOM),
                cmd=[(0.0, "idle", 0.0, 0.0), (4.0, "stomp", 0.0, 0.0)], **kw)


def reference_specs(over=None):
    """The eight M-001 captures plus stomp, all passing. `over` = {capture: Spec} replaces entries."""
    s = {
        "walk_fr010": spec_walk(0.10, strides=4),
        "walk_fr020": spec_walk(0.20, strides=5),
        "walk_fr030": spec_walk(0.30, strides=4),
        "start": spec_start(),
        "stop": spec_stop(),
        "estop": spec_estop(),
        "turn90_inplace": spec_turn_inplace(),
        "curve": spec_curve(),
        "stomp": spec_stomp(),
    }
    s.update(over or {})
    return s


def write_set(out_dir, over=None, names=None):
    paths = {}
    for name, sp in reference_specs(over).items():
        if names and name not in names:
            continue
        recs, _ = build(sp)
        paths[name] = write(recs, os.path.join(out_dir, f"{name}.gait.jsonl"))
    return paths


def example_log(path):
    """Small valid example (contract demo for emitters): 8 s of walk_fr020 at a non-axis heading, with frames."""
    sp = spec_walk(0.20, strides=1, heading_deg=33.0, capture="walk_fr020")
    sp.duration_s = 8.0
    recs, _ = build(sp)
    q = np.array([0.1, 0.38, -0.04, 0.92]); q = (q / np.linalg.norm(q)).tolist()
    steps = [r for r in recs if r["rec"] == "step"]
    frames_after = {}
    for f in range(int(sp.duration_s * 60) + 1):      # a frame is emitted after the last step it includes
        n_latest = int(math.floor(f / 60.0 / sp.dt + 1e-9))
        frames_after.setdefault(n_latest, []).append(f)
    out = []
    for i, r in enumerate(recs):
        out.append(r)
        nxt = recs[i + 1] if i + 1 < len(recs) else None
        if r["rec"] in ("step", "event") and (nxt is None or nxt["rec"] == "step"):
            n = r["n"]
            for f in frames_after.get(n, []):
                com = steps[n]["com_p"]
                out.append(dict(rec="frame", f=f, t_render=f / 60.0, n_latest=n,
                                cam_p=[com[0] - 120.0, com[1] + 51.0, com[2] - 120.0], cam_q=q,
                                cam_impulse_offset_m=0.0))
    return write(out, path)


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(2)
    if "--example" in sys.argv:
        print(example_log(sys.argv[1]))
    else:
        for k, p in write_set(sys.argv[1]).items():
            print(k, p)
