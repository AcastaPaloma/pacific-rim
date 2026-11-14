"""M-001@v1 Feels heavy (research/R-001.md §9), sub-measures M-001.1 .. M-001.13.

Procedure (R-001 §9): CoM acceleration = central difference of com_v, then a centred moving average of width
0.05 T_nom; yaw rate = central difference of the unwrapped yaw, same filter; yaw acceleration = the same filter on
the central difference of the (unfiltered) yaw rate. Strides: same-foot contact_begin to contact_begin (spec
hysteresis on fn). Steady: constant cmd_speed and |dv_bar| between strides < 0.02 g T. Limits use header values
(l, h, W_f, m, P); readings of undefined terms are listed in Tools/Metrics/SPEC_READINGS.md."""
from __future__ import annotations

import math
import os

import numpy as np

from ..common import S_NOT_RUN, S_PENDING
from ..report import part
from .inputs import check_normalisers
from .segment import (cdiff, heading_axes, horizontal_speed, moving_average, odd_width, spec_contacts,
                      stride_of_sample, strides)


class Cap:
    """Per-capture derived signals."""

    def __init__(self, lg, reg, g):
        h = lg.header
        self.lg, self.h, self.name, self.file = lg, h, lg.capture, os.path.basename(lg.path)
        self.dt = lg.dt
        self.l = h["leg_length_m"]
        self.hc = h["com_height_stand_m"]
        self.m = h["mass_kg"]
        self.T_nom = h["T_stride_nom_s"]
        self.W_f = min(f["width_m"] for f in h["feet"])
        self.P = h["reactor_power_W"]
        self.g = g
        self.v_cruise = math.sqrt(h["fr_cruise_nom"] * g * self.l)
        w = odd_width(reg.p("smoothing_width_T") * self.T_nom, self.dt)
        self.w = w
        a_raw = cdiff(lg.com_v, self.dt)
        a_s = moving_average(a_raw, w)
        psi = np.unwrap(lg.yaw)
        fwd, right = heading_axes(psi)
        self.a_fore_raw = (a_raw * fwd).sum(1)
        self.a_lat_raw = (a_raw * right).sum(1)
        self.a_fore = (a_s * fwd).sum(1)
        self.a_lat = (a_s * right).sum(1)
        yr_raw = cdiff(psi, self.dt)
        self.yaw_rate = moving_average(yr_raw, w)
        self.yaw_acc = moving_average(cdiff(yr_raw, self.dt), w)
        self.yaw_rate_raw = yr_raw
        self.speed = horizontal_speed(lg.com_v)
        self.v_bar_t = moving_average(self.speed, odd_width(self.T_nom, self.dt))     # stride-averaged speed
        f_on, f_off = reg.p("F_on_mg") * self.m * g, reg.p("F_off_mg") * self.m * g
        self.contacts, _ = spec_contacts(lg, f_on, f_off)
        self.strides = strides(lg, self.contacts, g, reg.p("steady_dv_gT"))
        self.steady = [s for s in self.strides if s.steady]

    def steady_count(self):
        """Steady strides of the foot with the fewest (each foot must show the steady state)."""
        if not self.lg.feet:
            return 0
        return min(sum(1 for s in self.steady if s.foot_index == fi) for fi in range(len(self.lg.feet)))

    def fr_steady(self):
        v = np.mean([s.v_bar for s in self.steady]) if self.steady else float("nan")
        return v * v / (self.g * self.l), v

    def loc(self, k):
        return {"capture": self.name, "file": self.file, "n": int(self.lg.n[k]), "t_s": round(float(self.lg.t[k]), 4),
                "stride_samples": stride_of_sample(self.strides, k)}

    def command_time(self, kind):
        """Index of the start / stop command: first change of cmd_speed (up for start, down for stops)."""
        cs = self.lg.cmd_speed
        for k in range(1, len(cs)):
            if kind == "start" and cs[k] > cs[k - 1]:
                return k
            if kind == "stop" and cs[k] < cs[k - 1]:
                return k
        return None


def _peak(sig, caps, attr, raw_attr=None):
    best = None
    for c in caps:
        s = np.abs(getattr(c, attr))
        k = int(np.argmax(s))
        if best is None or s[k] > best[0]:
            best = (float(s[k]), c, k)
    return best


def run(report, logs, reg, ratings=None, out_dir=None, images=True):
    g = reg.p("g_mps2")
    check_normalisers(report, logs, reg, g)
    req = reg.get("required_captures")
    names = [lg.capture for lg in logs]
    report.coverage = {"required": req, "provided": sorted(set(names)), "missing": [c for c in req if c not in names],
                       "optional_not_provided": []}
    for lg in logs:
        report.add_input(lg.path, lg.sha256, f"gait_log@v1 {lg.capture}")
    caps = [Cap(lg, reg, g) for lg in logs]
    by = {}
    for c in caps:
        by.setdefault(c.name, []).append(c)
    min_steady = int(reg.p("min_steady_strides"))
    steady_caps = reg.get("steady_captures") or reg.get("walk_captures")
    # minimum evidence per capture (DEF-T002-arch-1): every capture needs contacts on every foot
    from .m002 import vacuous_feet
    nmin = int(reg.p("min_contacts_per_foot", 1))
    vac = []
    for c in caps:
        short = vacuous_feet(c.lg, c.contacts, nmin)
        if short:
            vac.append(f"{c.name}: no contact on foot {', '.join(short)} (>= {nmin} per foot required)")
        elif c.name in steady_caps and c.steady_count() < min_steady:
            vac.append(f"{c.name}: {c.steady_count()} steady strides per foot (< {min_steady})")
    if vac:
        report.coverage["missing"] = report.coverage["missing"] + vac
    # ---------------- M-001.1 Froude band
    parts1, steady_info = [], {}
    fr_lo, fr_hi = reg.t("M-001.1", "fr_lo"), reg.t("M-001.1", "fr_hi")
    for c in caps:
        n_st = c.steady_count()
        fr, vb = c.fr_steady()
        steady_info[f"{c.name}:{c.file}"] = {"steady_strides_min_per_foot": n_st, "Fr": fr, "v_bar_mps": vb}
        if c.name in steady_caps or n_st >= min_steady:
            if n_st < min_steady:
                parts1.append({"name": f"{c.name}:Fr band", "value": None, "status": S_NOT_RUN, "op": "in",
                               "threshold": fr_lo, "threshold_hi": fr_hi, "margin": None, "margin_rel": None,
                               "unit": "Fr", "note": f"only {n_st} steady strides per foot (< {min_steady})"})
            else:
                parts1.append(part(f"{c.name}:Fr band", fr, "in", fr_lo, fr_hi, unit="Fr"))
    for c in by.get(reg.get("cruise_capture"), []):
        fr, _ = c.fr_steady()
        if c.steady_count() >= min_steady:
            tol = reg.t("M-001.1", "cruise_tol")
            nom = c.h["fr_cruise_nom"]
            parts1.append(part(f"{c.name}:cruise vs fr_cruise_nom", fr, "in", nom - tol, nom + tol, unit="Fr"))
    all_str = [(s.v_bar ** 2 / (g * c.l), c, s) for c in caps for s in c.strides]
    if all_str:
        mx = max(all_str, key=lambda x: x[0])
        parts1.append(part("max stride Fr, all captures", mx[0], "<=", reg.t("M-001.1", "fr_never_above"), unit="Fr",
                           where=f"{mx[1].name} stride n={int(mx[1].lg.n[mx[2].b])}..{int(mx[1].lg.n[mx[2].e])}"))
    if parts1:
        report.add("M-001.1", parts=parts1, diagnostics={"steady": steady_info})
    else:
        report.add("M-001.1", S_NOT_RUN, note="no walk capture", diagnostics={"steady": steady_info})

    # ---------------- M-001.2 stride period vs Alexander, M-001.3 duty factor
    parts2, parts3 = [], []
    a_c, a_e = reg.p("alexander_coef"), reg.p("alexander_exp")
    for c in caps:
        if c.steady_count() < min_steady:
            if c.name in steady_caps:
                nr = {"value": None, "status": S_NOT_RUN, "margin": None, "margin_rel": None, "op": "in", "unit": "",
                      "note": f"< {min_steady} steady strides per foot"}
                parts2.append(dict(nr, name=f"{c.name}:T ratio", threshold=reg.t("M-001.2", "ratio_lo"),
                                   threshold_hi=reg.t("M-001.2", "ratio_hi")))
                parts3.append(dict(nr, name=f"{c.name}:duty", threshold=reg.t("M-001.3", "duty_lo"),
                                   threshold_hi=reg.t("M-001.3", "duty_hi")))
            continue
        fr, _ = c.fr_steady()
        T_meas = float(np.median([s.T for s in c.steady]))
        T_pred = a_c * fr ** a_e * math.sqrt(c.l / g)
        parts2.append(part(f"{c.name}:T ratio", T_meas / T_pred, "in", reg.t("M-001.2", "ratio_lo"),
                           reg.t("M-001.2", "ratio_hi"), unit="-", T_meas_s=T_meas, T_alexander_s=T_pred))
        for fi, fid in enumerate(c.h["foot_ids"]):
            bs = [s.stance_s / s.T for s in c.steady if s.foot_index == fi and np.isfinite(s.stance_s)]
            if bs:
                b = float(np.mean(bs))
                parts3.append(part(f"{c.name}:duty foot {fid}", b, "in", reg.t("M-001.3", "duty_lo"),
                                   reg.t("M-001.3", "duty_hi"), unit="-", double_support=2 * b - 1))
    for sid, ps in (("M-001.2", parts2), ("M-001.3", parts3)):
        if ps:
            report.add(sid, parts=ps)
        else:
            report.add(sid, S_NOT_RUN, note="no capture with >= 3 steady strides")

    # ---------------- M-001.4 / .5 CoM accelerations (all captures)
    g_ = g
    p4, p5 = [], []
    for c in caps:
        k4 = int(np.argmax(np.abs(c.a_fore)))
        p4.append(part(f"{c.name}:peak |a_fore|", float(abs(c.a_fore[k4])), "<=", reg.t("M-001.4", "a_max_g") * g_,
                       unit="m/s^2", unsmoothed_peak=float(np.max(np.abs(c.a_fore_raw))), at=c.loc(k4)))
        k5 = int(np.argmax(np.abs(c.a_lat)))
        lim5 = reg.t("M-001.5", "a_max_g") * g_ * ((c.W_f / 2) / c.hc) / reg.p("ref_half_width_over_h")
        p5.append(part(f"{c.name}:peak |a_lat|", float(abs(c.a_lat[k5])), "<=", lim5, unit="m/s^2",
                       unsmoothed_peak=float(np.max(np.abs(c.a_lat_raw))), at=c.loc(k5)))
    for sid, ps in (("M-001.4", p4), ("M-001.5", p5)):
        worst = _worst(ps)
        report.add(sid, parts=ps, diagnostics={"where": f"{worst['name']} at {worst['at']}",
                                               "unsmoothed_peak_m_s2": worst["unsmoothed_peak"]})

    # ---------------- M-001.7 / .8 yaw rate and acceleration (all captures)
    p7, p8 = [], []
    for c in caps:
        k7 = int(np.argmax(np.abs(c.yaw_rate)))
        lim7 = reg.t("M-001.7", "rate_max_dps") * math.sqrt(reg.p("ref_leg_length_m") / c.l)
        p7.append(part(f"{c.name}:peak |yaw rate|", math.degrees(abs(c.yaw_rate[k7])), "<=", lim7, unit="deg/s",
                       unsmoothed_peak=math.degrees(float(np.max(np.abs(c.yaw_rate_raw)))), at=c.loc(k7)))
        k8 = int(np.argmax(np.abs(c.yaw_acc)))
        lim8 = reg.t("M-001.8", "acc_max_dps2") * reg.p("ref_leg_length_m") / c.l
        p8.append(part(f"{c.name}:peak |yaw acc|", math.degrees(abs(c.yaw_acc[k8])), "<=", lim8, unit="deg/s^2",
                       at=c.loc(k8)))
    for sid, ps in (("M-001.7", p7), ("M-001.8", p8)):
        worst = _worst(ps)
        report.add(sid, parts=ps, diagnostics={"where": f"{worst['name']} at {worst.get('at')}"})

    # ---------------- M-001.9 yaw rate while v_bar > 0.5 v_cruise: |yaw rate| * v_bar <= a_lat
    p9 = []
    for c in caps:
        sel = c.v_bar_t > reg.p("cruise_fraction_yaw") * c.v_cruise
        if not sel.any():
            if c.name == "curve":
                p9.append(_nr("curve:|yaw rate| * v_bar", "curve never exceeds 0.5 v_cruise (vacuous: not a pass)"))
            continue
        a_lat = reg.t("M-001.9", "a_lat_g") * g_ * ((c.W_f / 2) / c.hc) / reg.p("ref_half_width_over_h")
        req_a = np.abs(c.yaw_rate) * c.v_bar_t
        req_a = np.where(sel, req_a, -np.inf)
        k = int(np.argmax(req_a))
        p9.append(part(f"{c.name}:|yaw rate| * v_bar", float(req_a[k]), "<=", a_lat, unit="m/s^2",
                       yaw_rate_dps=math.degrees(abs(c.yaw_rate[k])), limit_dps=math.degrees(a_lat / c.v_bar_t[k]),
                       v_bar_mps=float(c.v_bar_t[k]), at=c.loc(k)))
    if p9:
        worst = _worst(p9)
        report.add("M-001.9", parts=p9, diagnostics={"where": f"{worst['name']} at {worst.get('at')}"})
    else:
        report.add("M-001.9", S_NOT_RUN, note="no capture with v_bar > 0.5 v_cruise")

    # ---------------- M-001.6 / .10 start
    p6, p10 = [], []
    for c in by.get("start", []):
        k0 = c.command_time("start")
        if k0 is None:
            p6.append(_nr("start:net accel", "no start command (cmd_speed never rises)"))
            continue
        tgt = reg.p("start_fraction") * c.v_cruise
        ks = np.where((np.arange(len(c.speed)) >= k0) & (c.speed >= tgt))[0]
        if len(ks) == 0:
            p6.append(_nr("start:net accel", "never reaches 90 % of cruise"))
            p10.append(_nr("start:steps", "never reaches 90 % of cruise"))
            continue
        k90 = int(ks[0])
        dt_s = (k90 - k0) * c.dt
        a_net = (c.speed[k90] - c.speed[k0]) / dt_s
        p6.append(part("start:net accel to 90 % cruise", float(a_net), "<=", reg.t("M-001.6", "a_net_max_g") * g_,
                       unit="m/s^2", t_cmd_s=float(c.lg.t[k0]), t90_s=float(c.lg.t[k90])))
        cc = reg.t("M-001.6", "c_mech_cost")
        v = c.speed
        ok = v > 0
        lim = np.where(ok, (c.P - cc * c.m * g_ * v) / (c.m * np.where(ok, v, 1.0)), np.inf)
        exc = c.a_fore - lim
        kk = int(np.argmax(np.where(ok, exc, -np.inf)))
        p6.append(part("start:a_fore - power limit (max over v)", float(exc[kk]), "<=", 0.0, unit="m/s^2",
                       a_fore=float(c.a_fore[kk]), power_limit=float(lim[kk]), v_mps=float(v[kk]), at=c.loc(kk)))
        steps = dt_s / (c.T_nom / 2)
        p10.append(part("start:steps to 90 % cruise", steps, "in", reg.t("M-001.10", "steps_lo"),
                        reg.t("M-001.10", "steps_hi"), unit="steps", seconds=dt_s))
    _add_or_nr(report, "M-001.6", p6, "no start capture")
    _add_or_nr(report, "M-001.10", p10, "no start capture")

    # ---------------- M-001.11 normal stop, M-001.12 emergency stop
    p11, p12 = [], []
    variants = []
    for name, sid in (("stop", "M-001.11"), ("estop", "M-001.12")):
        for c in by.get(name, []):
            k0 = c.command_time("stop")
            tgt = reg.p("stop_speed_mps")
            if k0 is None:
                (p11 if sid == "M-001.11" else p12).append(_nr(f"{name}:time", "no stop command (cmd_speed never drops)"))
                continue
            ks = np.where((np.arange(len(c.speed)) >= k0) & (c.speed < tgt))[0]
            if len(ks) == 0:
                (p11 if sid == "M-001.11" else p12).append(_nr(f"{name}:time", "speed never drops below 0.3 m/s"))
                continue
            t_stop = (int(ks[0]) - k0) * c.dt
            if sid == "M-001.11":
                p11.append(part("stop:steps", t_stop / (c.T_nom / 2), "in", reg.t(sid, "steps_lo"),
                                reg.t(sid, "steps_hi"), unit="steps", seconds=t_stop))
                p11.append(part("stop:t*", t_stop / math.sqrt(c.l / g_), "in", reg.t(sid, "tstar_lo"),
                                reg.t(sid, "tstar_hi"), unit="t/sqrt(l/g)", seconds=t_stop))
            else:
                a_ref = reg.t(sid, "a_ref_g") * g_
                v0 = float(c.speed[k0])
                f_act = c.h.get("F_act_max_N")
                f_brake = min(a_ref * c.m, f_act) if isinstance(f_act, (int, float)) else a_ref * c.m
                floor = max(c.v_cruise / a_ref, c.m * v0 / f_brake)
                p12.append(part("estop:time to stop", t_stop, ">=", floor, unit="s", v0_mps=v0,
                                floor_cruise_s=c.v_cruise / a_ref, floor_brake_s=c.m * v0 / f_brake))
                variants.append((c.m, t_stop / math.sqrt(c.l / g_), c.file))
    _add_or_nr(report, "M-001.11", p11, "no stop capture")
    masses = sorted({m for m, _, _ in variants})
    var_info = {"status": S_PENDING if len(masses) < 2 else "REPORTED",
                "note": "mass-variant clause (>= 2 mass variants) is non-gating until variant logs exist",
                "variants": [{"mass_kg": m, "t_star": t, "file": f} for m, t, f in sorted(variants)]}
    if len(masses) >= 2:
        ts = [t for _, t, _ in sorted(variants)]
        var_info["t_star_non_decreasing_with_mass"] = bool(all(b >= a for a, b in zip(ts[:-1], ts[1:])))
    _add_or_nr(report, "M-001.12", p12, "no estop capture", info={"mass_variants": var_info})

    # ---------------- M-001.13 panel
    from ..ratings import score_m001
    score_m001(report, reg, ratings)
    report.extra["smoothing_window_samples"] = {c.file: c.w for c in caps}
    if images and out_dir:
        from ..diagnostics import gait_plot
        report.images.append(gait_plot(out_dir, caps, report))


def _worst(ps):
    judged = [p for p in ps if p.get("margin_rel") is not None]
    return min(judged, key=lambda p: p["margin_rel"]) if judged else ps[0]


def _nr(name, note):
    return {"name": name, "value": None, "status": S_NOT_RUN, "op": None, "threshold": None, "margin": None,
            "margin_rel": None, "unit": "", "note": note}


def _add_or_nr(report, sid, ps, note, info=None):
    if ps:
        report.add(sid, parts=ps, info=info)
    else:
        report.add(sid, S_NOT_RUN, note=note, info=info)
