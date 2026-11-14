"""M-002@v1 No foot sliding (research/R-001.md §5, §9).

s_c = max over sole points k and loaded samples t of |Pi(p_k(t) - p_k(t_k0))|, Pi = projection on the tangent
plane of the force-weighted mean contact normal; a point is loaded when it is <= h_tol = 0.01 L_f above the lowest
sole point along that normal; t_k0 is its first loaded sample (anchor kept through unload / reload)."""
from __future__ import annotations

import os

import numpy as np

from ..common import S_PASS
from ..report import part
from .inputs import check_normalisers
from .segment import quat_to_mat, spec_contacts


def contact_slip(log, c, sole_local, L_f, h_tol_Lf):
    f = log.feet[c.foot_index]
    sl = slice(c.b, c.e)
    fn = f["fn"][sl]
    cn = f["cn"][sl]
    ok = np.isfinite(cn).all(1)
    if ok.any() and (fn[ok] > 0).any():
        nb = (fn[ok, None] * cn[ok]).sum(0)
        nb = nb / np.linalg.norm(nb)
        normal_src = "force-weighted cn"
    else:
        nb = np.array([0.0, 1.0, 0.0])
        normal_src = "world up (no cn logged during the contact)"
    R = quat_to_mat(f["q"][sl])                                       # (T, 3, 3)
    P = f["p"][sl][:, None, :] + np.einsum("tij,kj->tki", R, sole_local)   # (T, 5, 3)
    hgt = P @ nb
    hgt = hgt - hgt.min(1, keepdims=True)
    loaded = hgt <= h_tol_Lf * L_f
    best, best_k, best_t = 0.0, -1, c.b
    per_point = []
    for k in range(P.shape[1]):
        idx = np.where(loaded[:, k])[0]
        if len(idx) == 0:
            per_point.append(None)
            continue
        d = P[idx, k] - P[idx[0], k]
        d = d - np.outer(d @ nb, nb)
        s = np.linalg.norm(d, axis=1)
        j = int(np.argmax(s))
        per_point.append(float(s[j]))
        if s[j] > best:
            best, best_k, best_t = float(s[j]), k, c.b + int(idx[j])
    return best, best_k, best_t, per_point, normal_src


def run(report, logs, reg, ratings=None, out_dir=None, images=True):
    g = reg.p("g_mps2")
    check_normalisers(report, logs, reg, g)
    req = reg.get("required_captures") or []
    opt = reg.get("optional_captures") or []
    names = [lg.capture for lg in logs]
    report.coverage = {"required": req, "provided": sorted(set(names)),
                       "missing": [c for c in req if c not in names],
                       "optional_not_provided": [c for c in opt if c not in names]}
    for lg in logs:
        report.add_input(lg.path, lg.sha256, f"gait_log@v1 {lg.capture}")
    all_c = []
    agree_parts, dt_parts = [], []
    disagreements = {}
    for lg in logs:
        h = lg.header
        m = h["mass_kg"]
        f_on, f_off = reg.p("F_on_mg") * m * g, reg.p("F_off_mg") * m * g
        cs, states = spec_contacts(lg, f_on, f_off)
        T_nom = h["T_stride_nom_s"]
        ext_lim = reg.p("ext_f_mg") * m * g
        for c in cs:
            foot = h["feet"][c.foot_index]
            sole = np.asarray(foot["sole_points_local"], dtype=np.float64)
            s, k, tk, pp, nsrc = contact_slip(lg, c, sole, foot["length_m"], reg.p("h_tol_Lf"))
            c.slip_m, c.slip_point, c.slip_t = s, k, float(lg.t[min(tk, len(lg.t) - 1)])
            c.L_f = foot["length_m"]
            c.capture = lg.capture
            c.file = os.path.basename(lg.path)
            c.n_begin = int(lg.n[c.b])
            c.t_begin = float(lg.t[c.b])
            c.per_point = pp
            c.normal_src = nsrc
            if (c.e - c.b) * lg.dt < reg.p("scuff_T") * T_nom:
                c.tags.append("scuff")
            if np.any(lg.ext_f[c.b:c.e] > ext_lim):
                c.excluded = True
                c.tags.append("excluded_external_force")
        all_c += cs
        # M-002.4: emitter flag versus spec hysteresis
        tot = agree = 0
        bad = []
        for fi, st in enumerate(states):
            em = lg.feet[fi]["contact"].astype(bool)
            tot += len(st)
            agree += int((em == st).sum())
            bad += [int(lg.n[i]) for i in np.where(em != st)[0][:20]]
        share = agree / tot if tot else float("nan")
        agree_parts.append(part(f"{lg.capture}:{os.path.basename(lg.path)}", share, ">=", reg.t("M-002.4", "agree_min"),
                                unit="share of foot-steps"))
        disagreements[lg.capture] = sorted(bad)[:20]
        dt_parts.append(part(f"{lg.capture}:fixed_dt_s", lg.dt, "<=", reg.t("M-002.3", "dt_max_s"), unit="s"))
    used = [c for c in all_c if not c.excluded]
    fr = sorted(used, key=lambda c: -c.slip_m / c.L_f)
    worst = [{"capture": c.capture, "file": c.file, "foot": c.foot_id, "contact_id": c.contact_id,
              "n_begin": c.n_begin, "t_begin_s": round(c.t_begin, 4), "t_worst_s": round(c.slip_t, 4),
              "slip_m": c.slip_m, "slip_pct_Lf": 100 * c.slip_m / c.L_f, "sole_point": c.slip_point,
              "tags": c.tags} for c in fr[:int(reg.p("worst_n"))]]
    diag = {"worst_contacts": worst, "contacts_total": len(all_c), "contacts_used": len(used),
            "truncated": sum(1 for c in all_c if c.truncated_start or c.truncated_end),
            "scuffs": sum(1 for c in all_c if "scuff" in c.tags),
            "where": "; ".join(f"{w['capture']} foot {w['foot']} contact {w['contact_id']} n={w['n_begin']} "
                               f"t={w['t_begin_s']}s slip {w['slip_m']:.4f} m ({w['slip_pct_Lf']:.3f} % L_f)"
                               for w in worst[:3])}
    if used:
        mx = max(c.slip_m / c.L_f for c in used)
        report.add("M-002.1", parts=[part("max_c s_c/L_f", mx, "<", reg.t("M-002.1", "max_frac"), unit="L_f",
                                          slip_m=fr[0].slip_m)], diagnostics=diag)
        p95 = float(np.percentile([c.slip_m / c.L_f for c in used], reg.p("p95_quantile")))
        report.add("M-002.2", parts=[part("p95 s_c/L_f", p95, "<", reg.t("M-002.2", "p95_frac"), unit="L_f")])
    else:
        report.add("M-002.1", "NOT_RUN", note="zero usable contacts (vacuous: not a pass)", diagnostics=diag)
        report.add("M-002.2", "NOT_RUN", note="zero usable contacts")
    report.add("M-002.3", parts=dt_parts + [
        {"name": "every_step_logged", "value": True, "status": S_PASS, "margin": None, "margin_rel": None, "op": None,
         "unit": "", "threshold": None, "note": "n contiguous (validated: a gap is INVALID_INPUT)"},
        {"name": "float64_positions", "value": True, "status": S_PASS, "margin": None, "margin_rel": None, "op": None,
         "unit": "", "threshold": None, "note": "float32-quantised positions are INVALID_INPUT"}])
    report.add("M-002.4", parts=agree_parts, diagnostics={"disagreeing_steps_n": disagreements,
                                                          "where": "; ".join(f"{k}: n={v[:5]}" for k, v in disagreements.items() if v)})
    exc = [c for c in all_c if c.excluded]
    report.add("M-002.5", parts=[part("excluded contacts", len(exc), "<=", reg.t("M-002.5", "excluded_max"),
                                      unit="contacts")],
               diagnostics={"excluded": [{"capture": c.capture, "foot": c.foot_id, "n_begin": c.n_begin}
                                         for c in exc[:20]],
                            "where": "; ".join(f"{c.capture} foot {c.foot_id} n={c.n_begin}" for c in exc[:5])})
    report.extra["contacts"] = [{"capture": c.capture, "foot": c.foot_id, "contact_id": c.contact_id,
                                 "n_begin": c.n_begin, "samples": c.e - c.b, "slip_m": c.slip_m,
                                 "per_point_m": c.per_point, "tags": c.tags} for c in all_c]
    if images and out_dir and all_c:
        from ..diagnostics import slip_plot
        report.images.append(slip_plot(out_dir, all_c, reg.t("M-002.1", "max_frac")))
