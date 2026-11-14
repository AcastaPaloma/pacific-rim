"""M-003@v1 Matches the style references (research/R-002.md §4): sub-measures a (output, internal, A3, A5),
b (DIG gate, DIG_max advisory, O informational), c1, c2 (haze_cal scene), d-i .. d-iv, e (advisory), f (panel).
Every gate uses the worst frame; per-frame values are in the diagnostics."""
from __future__ import annotations

import os

import numpy as np

from ..colour import ciede2000, index_colour_mismatch, nearest_palette, srgb_u8_to_lab
from ..common import ContractError, InvalidInput, S_NA, S_NOT_RUN, S_REPORTED, rel_path
from ..contracts.palette_set import load_palette_set
from ..contracts.style_capture import decode_depth, load_capture_set, load_image
from ..report import part
from . import measures as M

DITHER_KEYS = ("grad_sigma_px", "grad_lo", "grad_hi", "window_px", "top_colour_max", "distinct_max",
               "neighbour_diff_min", "amp_min", "o_min_pairs")


def load_sets(inputs, palettes):
    sets, errs = [], []
    for p in inputs:
        cs, e = load_capture_set(p, palettes, collect_only=True)
        errs += e
        if cs:
            sets.append(cs)
    if errs:
        raise InvalidInput(errs)
    return sets


def frame_data(fr, prof, need_output=True):
    """Decode a frame: internal RGB / Lab / index / L per pixel, depth, masks, output RGB."""
    cam = fr.meta["camera"]
    ci = load_image(fr, "color_internal")
    lab = srgb_u8_to_lab(ci)
    pi = load_image(fr, "palette_index")
    de_i, nn_i = nearest_palette(ci, prof.rgb)
    idx = pi.astype(np.int32) if pi is not None else nn_i.astype(np.int32)
    Lpal = prof.lab[:, 0]
    L_idx = np.where(idx == 255, lab[..., 0], Lpal[np.clip(idx, 0, len(Lpal) - 1)])
    z = decode_depth(load_image(fr, "depth"), cam["near_m"], cam["far_m"])
    s = fr.meta["resolution"]["integer_scale"]
    ui = load_image(fr, "mask_ui")
    ui_int = None
    if ui is not None:
        H, W = ci.shape[:2]
        ui_int = (ui[:H * s, :W * s].reshape(H, s, W, s) > 0).any((1, 3))
    d = {"ci": ci, "lab": lab, "idx": idx, "has_index": pi is not None, "L_idx": L_idx, "de_i": de_i, "z": z,
         "ui": ui, "ui_int": ui_int, "cam": cam}
    if need_output:
        co = load_image(fr, "color_output")
        d["co"] = co
        d["de_o"], _ = nearest_palette(co, prof.rgb)
    return d


def run(report, inputs, reg, ratings=None, out_dir=None, images=True, options=None):
    options = options or {}
    pal = load_palette_set(options.get("palettes"))
    report.add_input(pal.path, pal.sha256, "palette_set@v1")
    sets = load_sets(inputs, pal)
    for cs in sets:
        for p, h in cs.input_files:
            report.add_input(p, h, "style_capture@v1")
    gp, hz = [], []
    for cs in sets:
        for sq in cs.sequences:
            for fr in sq.frames:
                (hz if sq.purpose == "haze_cal" else gp if sq.purpose == "M-003" else []).append((cs, sq, fr))
    req_prof = (options.get("profiles").split(",") if options.get("profiles") else
                sorted(pal.profiles) if reg.get("required_profiles") == "all" else list(reg.get("required_profiles")))
    nmin = int(reg.get("min_frames_per_profile"))
    counts = {}
    for _, _, fr in gp:
        counts[fr.profile_id] = counts.get(fr.profile_id, 0) + 1
    missing = [f"{p}: {counts.get(p, 0)} gameplay frames (< {nmin})" for p in req_prof if counts.get(p, 0) < nmin]
    if not hz:
        missing.append("haze_cal sequence (M-003 c2)")
    report.coverage = {"required": [f"{p} >= {nmin} frames" for p in req_prof] + ["haze_cal"],
                       "provided": [f"{k}: {v} frames" for k, v in sorted(counts.items())] +
                                   (["haze_cal"] if hz else []),
                       "missing": missing, "optional_not_provided": []}
    P = {k: reg.p(k) for k in DITHER_KEYS}
    tol_o, tol_i = reg.t("M-003.a.output", "tol_de00"), reg.t("M-003.a.internal", "tol_de00")
    t3, t5 = reg.t("M-003.a.A3", "tol_de00"), reg.t("M-003.a.A5", "tol_de00")
    rows = []
    worst_img = {}
    for cs, sq, fr in gp:
        prof = pal.profiles[fr.profile_id]
        d = frame_data(fr, prof)
        tag = f"{cs.id}/{sq.id}/f{fr.index:06d}"
        r = {"frame": tag, "profile": prof.id}
        ui = d["ui"] > 0 if d["ui"] is not None else None
        keep_o = ~ui if ui is not None else None
        keep_i = ~d["ui_int"] if d["ui_int"] is not None else None
        r["A2_out"] = M.adherence(d["de_o"], keep_o, tol_o)
        r["A3_out"] = M.adherence(d["de_o"], keep_o, t3)
        r["A5_out"] = M.adherence(d["de_o"], keep_o, t5)
        r["A2_int"] = M.adherence(d["de_i"], keep_i, tol_i)
        r["off_palette_px_out"] = int(((d["de_o"] > tol_o) & (keep_o if keep_o is not None else True)).sum())
        if d["has_index"]:
            bad = index_colour_mismatch(d["ci"], prof.rgb, d["idx"], tol_i)
            if bad:
                raise InvalidInput([ContractError(rel_path(fr.sidecar_path), "files.palette_index",
                                                  "palette_index and color_internal disagree (index names a palette "
                                                  "entry the colour is not, or 255 on a palette colour)",
                                                  "0 inconsistent pixels", f"{bad} pixels")])
        dm, inter = M.dither_metrics(d["idx"], d["L_idx"], P)
        r["gradient_share"] = dm["gradient_share"]
        r["dither_evaluated"] = dm["gradient_share"] >= reg.p("min_gradient_share")
        r["DIG"], r["O"] = dm["DIG"], dm["O"]
        db = M.depth_banding(d["lab"][..., 0], d["z"], d["cam"]["far_m"], reg.p("depth_bands_m"),
                             reg.p("band_min_share"), reg.p("sky_far_fraction"), int(reg.p("min_bands")))
        r["depth"] = db
        n = len(prof.hexes)
        sh = np.bincount(d["idx"][d["idx"] < n].ravel(), minlength=n) / d["idx"].size
        r["levels"] = M.value_levels(prof.lab[:, 0], sh, reg.p("level_share_min"), reg.p("level_merge_dL"))
        labm = d["lab"][keep_i] if keep_i is not None else d["lab"]
        r["hue_families"], r["hue_peaks"] = M.hue_families(labm, reg.p("hue_cmin"), reg.p("hue_sigma_deg"),
                                                           reg.p("hue_mass_min"))
        r["accent_share"] = float(sh[prof.accent_index]) if prof.accent_index is not None else None
        res = fr.meta["resolution"]
        gi, nonuni = M.grid_integrity(d["co"], res["integer_scale"], res["output_block_offset_px"],
                                      reg.p("block_tol_code"), d["ui"])
        r["grid_uniform"] = gi
        ypr = d["cam"]["snap"]["snapped_yaw_pitch_roll_deg"] if d["cam"]["snap"].get("enabled") else d["cam"]["yaw_pitch_roll_deg"]
        r["composition"] = M.composition(d["lab"][..., 0], d["z"], d["cam"]["far_m"], reg.p("sky_far_fraction"),
                                         ypr[1], d["cam"]["vfov_deg"])
        rows.append(r)
        for key, val, lower in (("a", r["A2_out"], True), ("b", r["DIG"] if r["dither_evaluated"] else None, True),
                                ("d4", gi, True)):
            if val is None:
                continue
            if key not in worst_img or (val < worst_img[key][0]):
                worst_img[key] = (val, tag, d, inter, nonuni, prof)
    report.extra["frames"] = rows
    _add_a(report, reg, rows)
    _add_b(report, reg, rows)
    _add_c1(report, reg, rows)
    _add_c2(report, reg, hz, pal)
    _add_d(report, reg, rows)
    _add_e(report, reg, rows)
    from ..ratings import score_m003
    score_m003(report, reg, ratings, sorted(counts))
    if images and out_dir and worst_img:
        _images(report, out_dir, worst_img, tol_o)


def _worst(rows, key, lower=True, pred=None):
    rs = [r for r in rows if (pred is None or pred(r)) and r.get(key) is not None and np.isfinite(r[key])]
    if not rs:
        return None
    return min(rs, key=lambda r: r[key]) if lower else max(rs, key=lambda r: r[key])


def _per_frame(rows, key):
    return {r["frame"]: r.get(key) for r in rows}


def _nan_parts(rows, key, name, op, thr, why):
    """NOT_RUN parts for frames whose value could not be computed (a fully masked frame is not hidden)."""
    return [{"name": f"{name} {r['frame']}", "value": None, "status": S_NOT_RUN, "op": op, "threshold": thr,
             "margin": None, "margin_rel": None, "unit": "share", "note": why}
            for r in rows if r.get(key) is None or not np.isfinite(r[key])]


def _profiles(rows):
    return sorted({r["profile"] for r in rows})


def _add_a(rep, reg, rows):
    if not rows:
        for s in ("M-003.a.output", "M-003.a.internal", "M-003.a.A3", "M-003.a.A5"):
            rep.add(s, S_NOT_RUN, note="no gameplay frames")
        return
    for sid, key, lab in (("M-003.a.output", "A2_out", "output"), ("M-003.a.internal", "A2_int", "internal")):
        thr = reg.t(sid, "min_share")
        ps = _nan_parts(rows, key, f"A_2 {lab}", ">=", thr, "no unmasked pixel (vacuous: not a pass)")
        w = _worst(rows, key)
        if w is not None:
            extra = {"off_palette_px": w["off_palette_px_out"]} if key == "A2_out" else {}
            ps.insert(0, part(f"A_2 {lab} worst frame {w['frame']}", w[key], ">=", thr, unit="share", **extra))
        where = (f"{w['frame']} ({w['off_palette_px_out']} output px beyond dE00 {reg.t(sid, 'tol_de00')})"
                 if (w is not None and key == "A2_out") else (w["frame"] if w is not None else None))
        rep.add(sid, parts=ps, diagnostics={"where": where, "per_frame": _per_frame(rows, key)})
    for sid, k in (("M-003.a.A3", "A3_out"), ("M-003.a.A5", "A5_out")):
        w = _worst(rows, k)
        if w is None:
            rep.add(sid, S_NOT_RUN, note="no unmasked pixel")
        else:
            rep.add(sid, S_REPORTED, value=w[k], unit="share", diagnostics={"worst_frame": w["frame"]})


def _add_b(rep, reg, rows):
    """DIG gate per profile: a profile with no evaluated frame is NOT_RUN (it cannot hide behind another profile)."""
    ev = [r for r in rows if r["dither_evaluated"] and r["DIG"] is not None and np.isfinite(r["DIG"])]
    dmin = reg.t("M-003.b.DIG", "dig_min")
    fmin = reg.t("M-003.b.DIG", "frame_share_min")
    if not rows:
        for s in ("M-003.b.DIG", "M-003.b.DIG_max", "M-003.b.O"):
            rep.add(s, S_NOT_RUN, note="no gameplay frames")
        return
    parts, low = [], []
    for pr in _profiles(rows):
        rp = [r for r in rows if r["profile"] == pr]
        ep = [r for r in ev if r["profile"] == pr]
        if not ep:
            parts.append({"name": f"{pr}: share of evaluated frames with DIG >= dig_min", "value": None,
                          "status": S_NOT_RUN, "op": ">=", "threshold": fmin, "margin": None, "margin_rel": None,
                          "unit": "share of frames", "note": f"no evaluated frame (all {len(rp)} below the "
                                                             "gradient-share floor): vacuous, not a pass"})
            continue
        ok = [r for r in ep if r["DIG"] >= dmin]
        parts.append(part(f"{pr}: share of evaluated frames with DIG >= dig_min", len(ok) / len(ep), ">=", fmin,
                          unit="share of frames", evaluated=len(ep), skipped=len(rp) - len(ep), dig_min=dmin))
        low += [r for r in ep if r["DIG"] < dmin]
    low.sort(key=lambda r: r["DIG"])
    rep.add("M-003.b.DIG", parts=parts, diagnostics={"per_frame_DIG": _per_frame(ev, "DIG"),
                                                     "where": "; ".join(f"{r['frame']} DIG {r['DIG']:.3f}"
                                                                        for r in low[:3])})
    if not ev:
        rep.add("M-003.b.DIG_max", S_NOT_RUN, note="no evaluated frame")
        rep.add("M-003.b.O", S_NOT_RUN, note="no evaluated frame")
        return
    w = _worst(ev, "DIG", lower=False)
    rep.add("M-003.b.DIG_max", parts=[part(f"max DIG {w['frame']}", w["DIG"], "<=", reg.t("M-003.b.DIG_max", "dig_max"),
                                           unit="DIG")])
    os_ = [r["O"] for r in ev if r["O"] is not None]
    if not os_:
        rep.add("M-003.b.O", S_NOT_RUN, note="O undefined: no frame has enough interleaved windows (R-002: '-')",
                diagnostics={"per_frame_O": _per_frame(ev, "O")})
        return
    o = float(np.median(os_))
    rep.add("M-003.b.O", S_REPORTED, value=o, unit="O",
            diagnostics={"per_frame_O": _per_frame(ev, "O"), "note": "Bayer/checker > 0.5, white noise ~ 0, "
                                                                     "clustered < 0 (advisory until Q-015)"})
    rep.warn(f"ADVISORY M-003.b.O reported without a threshold until Q-015: median O {o:.3f} "
             "(Bayer/checker > 0.5, white noise ~ 0, clustered < 0; the references are -0.34..+0.15)")


def _add_c1(rep, reg, rows):
    """Worst evaluated frame per profile; a profile without any frame of >= min_bands bands is NOT_RUN."""
    if not rows:
        rep.add("M-003.c1", S_NOT_RUN, note="no gameplay frames")
        return
    ps_all, where = [], []
    for pr in _profiles(rows):
        rp = [r for r in rows if r["profile"] == pr]
        ev = [r for r in rp if "rho" in r["depth"]]
        if not ev:
            ps_all.append({"name": f"{pr}: depth banding", "value": None, "status": S_NOT_RUN, "op": ">=",
                           "threshold": reg.t("M-003.c1", "rho_min"), "margin": None, "margin_rel": None,
                           "unit": "rho", "note": f"no frame with >= {reg.p('min_bands')} depth bands"})
            continue
        best = None
        for r in ev:
            db = r["depth"]
            ps = [part("max band-to-band drop", db["max_drop"], "<=", reg.t("M-003.c1", "max_drop_L"), unit="L*"),
                  part("Spearman rho(band, L*)", db["rho"], ">=", reg.t("M-003.c1", "rho_min"), unit="rho"),
                  part("far - near span", db["span"], ">=", reg.t("M-003.c1", "span_min_L"), unit="L*")]
            m = min([p["margin_rel"] for p in ps if p["margin_rel"] is not None] or [-np.inf])
            if best is None or m < best[0]:
                best = (m, r, ps)
        _, r, ps = best
        for p in ps:
            p["name"] = f"{r['frame']}: {p['name']}"
        ps_all += ps
        where.append(f"{r['frame']} bands {r['depth']['bands']} mean L* {[round(x, 1) for x in r['depth']['meanL']]}")
    rep.add("M-003.c1", parts=ps_all, diagnostics={"where": "; ".join(where),
                                                   "frames_evaluated": sum(1 for r in rows if "rho" in r["depth"]),
                                                   "frames_skipped": sum(1 for r in rows if "rho" not in r["depth"])})


def _add_c2(rep, reg, hz, pal):
    if not hz:
        rep.add("M-003.c2", S_NOT_RUN, note="no haze_cal sequence in the capture set")
        return
    need = reg.p("haze_distances_m")
    best = None
    info = []
    for cs, sq, fr in hz:
        prof = pal.profiles[fr.profile_id]
        ci = load_image(fr, "color_internal")
        L = srgb_u8_to_lab(ci)[..., 0]
        rows = M.haze_pairs(L, fr.meta["haze_targets"])
        ds = [x["distance_m"] for x in rows]
        tag = f"{cs.id}/{sq.id}/f{fr.index:06d}"
        if not all(any(abs(d - n) < 1e-6 for d in ds) for n in need[:1] + need[-1:]):
            info.append({"frame": tag, "note": "200 m and 8 km pairs required"})
            continue
        dL = [x["dL"] for x in rows]
        mL = [x["meanL"] for x in rows]
        rise = max(np.diff(dL)) if len(dL) > 1 else 0.0
        far = dL[-1] - reg.t("M-003.c2", "far_ratio_max") * dL[0]
        drop = max(0.0, -min(np.diff(mL))) if len(mL) > 1 else 0.0
        ps = [part("max rise of signed dL* between distances", float(rise), "<=",
                   reg.t("M-003.c2", "rise_allowance_L"), unit="L*"),
              part("dL*(8 km) - ratio x dL*(200 m)", float(far), "<=", 0.0, unit="L*"),
              part("max drop of pair mean L*", float(drop), "<=", reg.t("M-003.c2", "meanL_drop_allowance"), unit="L*")]
        absd = [abs(x) for x in dL]
        info.append({"frame": tag, "dL_signed": dL, "meanL": mL, "dL_abs_reading": absd,
                     "abs_reading_non_increasing": bool(all(b - a <= reg.t("M-003.c2", "rise_allowance_L")
                                                            for a, b in zip(absd[:-1], absd[1:]))),
                     "abs_reading_far_ok": bool(absd[-1] <= reg.t("M-003.c2", "far_ratio_max") * absd[0])})
        m = min(p["margin_rel"] for p in ps)
        if best is None or m < best[0]:
            best = (m, tag, ps)
    if best is None:
        rep.add("M-003.c2", S_NOT_RUN, note="haze_cal frames lack the 200 m / 8 km pairs", info={"frames": info})
        return
    _, tag, ps = best
    for p in ps:
        p["name"] = f"{tag}: {p['name']}"
    rep.add("M-003.c2", parts=ps, info={"frames": info, "note": "signed reading gates (R-002 as written); the "
                                                                "absolute-value reading (expect-B) is informational"},
            diagnostics={"where": tag})


def _add_d(rep, reg, rows):
    if not rows:
        for s in ("M-003.d-i", "M-003.d-ii", "M-003.d-iii", "M-003.d-iv"):
            rep.add(s, S_NOT_RUN, note="no gameplay frames")
        return
    w = _worst(rows, "levels", lower=False)
    rep.add("M-003.d-i", parts=[part(f"value levels {w['frame']}", w["levels"], "<=", reg.t("M-003.d-i", "levels_max"),
                                     unit="levels")], diagnostics={"per_frame": _per_frame(rows, "levels")})
    w = _worst(rows, "hue_families", lower=False)
    rep.add("M-003.d-ii", parts=[part(f"hue families {w['frame']}", w["hue_families"], "<=",
                                      reg.t("M-003.d-ii", "families_max"), unit="families", peaks=w["hue_peaks"])],
            diagnostics={"per_frame": _per_frame(rows, "hue_families")})
    acc = [r for r in rows if r["accent_share"]]
    if acc:
        lo, hi = reg.t("M-003.d-iii", "share_lo"), reg.t("M-003.d-iii", "share_hi")
        ps = [part(f"accent share {r['frame']}", r["accent_share"], "in", lo, hi, unit="share") for r in acc]
        w = min(ps, key=lambda p: p["margin_rel"])
        rep.add("M-003.d-iii", parts=[w], diagnostics={"per_frame": _per_frame(acc, "accent_share")})
    else:
        rep.add("M-003.d-iii", S_NA, note="no accent present in any frame (applies only when present)",
                spec_clause="R-002 §4 d-iii: the accent share is checked when the accent is present")
    w = _worst(rows, "grid_uniform")
    thr = reg.t("M-003.d-iv", "min_share")
    ps = _nan_parts(rows, "grid_uniform", "uniform blocks", ">=", thr, "every block touches the UI mask")
    if w is not None:
        ps.insert(0, part(f"uniform blocks {w['frame']}", w["grid_uniform"], ">=", thr, unit="share of blocks"))
    rep.add("M-003.d-iv", parts=ps, diagnostics={"where": w["frame"] if w else None,
                                                 "per_frame": _per_frame(rows, "grid_uniform")})


def _add_e(rep, reg, rows):
    if not rows:
        for s in ("M-003.e.sky", "M-003.e.horizon", "M-003.e.contrast"):
            rep.add(s, S_NOT_RUN, note="no gameplay frames")
        return
    for sid, key, op, a, b in (("M-003.e.sky", "sky_share", "in", "lo", "hi"),
                               ("M-003.e.horizon", "horizon", "<=", "max", None),
                               ("M-003.e.contrast", "contrast", ">=", "min", None)):
        ps = [part(f"{key} {r['frame']}", r["composition"][key], op, reg.t(sid, a), reg.t(sid, b) if b else None,
                   unit="") for r in rows]
        rep.add(sid, parts=[min(ps, key=lambda p: p["margin_rel"])],
                diagnostics={"per_frame": {r["frame"]: r["composition"][key] for r in rows}})


def _images(rep, out_dir, worst, tol):
    from ..diagnostics import heat_map, mask_image
    if "a" in worst:
        _, tag, d, _, _, prof = worst["a"]
        de = d["de_o"]
        name = heat_map(out_dir, "diag_M-003a_dE00_heatmap.png", de, 2 * tol)
        co = d["co"].reshape(-1, 3)
        bad = (de.reshape(-1) > tol)
        offenders = []
        if bad.any():
            cols, cnt = np.unique(co[bad], axis=0, return_counts=True)
            order = np.argsort(-cnt)[:5]
            for i in order:
                c = cols[i]
                dd = float(ciede2000(srgb_u8_to_lab(c), prof.lab).min())
                offenders.append({"hex": "#%02x%02x%02x" % tuple(int(v) for v in c), "pixels": int(cnt[i]),
                                  "dE00_to_nearest": dd})
        rep.sms["M-003.a.output"]["diagnostics"].update(heat_map=name, worst_offenders=offenders, frame=tag)
        rep.images.append(name)
    if "b" in worst:
        _, tag, d, inter, _, _ = worst["b"]
        name = mask_image(out_dir, "diag_M-003b_dither_mask.png", d["ci"], inter)
        rep.sms["M-003.b.DIG"]["diagnostics"].update(dither_mask=name, frame=tag)
        rep.images.append(name)
    if "d4" in worst:
        _, tag, d, _, nonuni, _ = worst["d4"]
        name = heat_map(out_dir, "diag_M-003d4_nonuniform_blocks.png", nonuni.astype(float), 1.0)
        rep.sms["M-003.d-iv"]["diagnostics"].update(block_map=name, frame=tag,
                                                    nonuniform_blocks=int(nonuni.sum()))
        rep.images.append(name)
