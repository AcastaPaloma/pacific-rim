"""M-102 LOD pixel error and no popping (research/R-101.md section 9).

Input: a directory with `m102_set.json` (`lod_capture@v1`) next to the PNGs it names (paths relative to the directory):
  {"format": "lod_capture", "schema": 1, "output_height_px": 1080,
   "pairs": [ {"id", "switch": "T1".."T4"|"B1"|"B2", "viewpoint", "yaw_deg", "k": 4|6, "distance_factor": 0.98|1.02,
               "ref": "L0 colour PNG (RGB, output resolution)", "test": "level-n colour PNG, same pose and snapped phase",
               "ref_labels": "uint8 PNG, depth-band index of the reference (0 = sky, other ids = terrain / building classes)",
               "test_labels": "same for the level-n frame"} ],
   "sweeps": [ {"id", "switch", "speed_mps": 2|10|30, "frames": ["f000.png", ... 240], "level_change_masks": ["m000.png", ...]} ],
   "m004": {"lod": "report.json of m004 on the LOD sequences", "pinned": "report.json of m004 on the L0-pinned run"} | null }
(a) silhouette contour = pixels whose 4-neighbourhood holds another label (sky vs non-sky and class edges); the displacement is the RMS of the
bidirectional nearest-contour distance, in output px (both directions pooled). (b) CIEDE2000 between ref and test colour; the share of pixels at or
below delta_e_max. (c) a pop event is a frame whose share of output pixels changed (against the previous frame) inside the mask of tiles whose level
changed that frame exceeds max(pop_floor, factor x median of the previous 10 shares). (d) per M-004 sequence id in both reports, S_px and C_px of the
LOD run are at most the pinned run + margin, and the LOD report is not FAIL. Coverage: >= 12 viewpoints x 3 yaws x k in {4, 6} x switches T1..T4, B1, B2
x {inside, outside}; sweeps at 2, 10, 30 m/s of 240 frames per switch; anything missing makes the verdict INCOMPLETE, never PASS."""
from __future__ import annotations

import json
import math
import os

import numpy as np

from ..colour import ciede2000, srgb_u8_to_lab
from ..common import ErrorList, InvalidInput, S_NOT_RUN, load_json_file, sha256_file
from ..png import read_png
from ..report import part

SET_FILE = "m102_set.json"


def _isnum(v):
    return isinstance(v, (int, float)) and not isinstance(v, bool) and math.isfinite(v)


def load_set(path):
    d = path if os.path.isdir(path) else os.path.dirname(path)
    f = os.path.join(d, SET_FILE) if os.path.isdir(path) else path
    el = ErrorList(f)
    if not os.path.isfile(f):
        el.add("file", f"{SET_FILE} missing", SET_FILE, f)
        raise InvalidInput(el.errors)
    try:
        s = load_json_file(f)
    except Exception as e:  # noqa: BLE001
        el.add("file", f"not valid JSON ({e})", SET_FILE)
        raise InvalidInput(el.errors)
    if not isinstance(s, dict) or s.get("format") != "lod_capture" or s.get("schema") != 1:
        el.add("envelope", "not lod_capture@v1", 'format "lod_capture", schema 1')
        raise InvalidInput(el.errors)
    pairs = s.get("pairs")
    if not isinstance(pairs, list) or not pairs:
        el.add("pairs", "non-empty list required", ">= 1 pair")
    else:
        for i, p in enumerate(pairs):
            w = f"pairs[{i}]"
            if not isinstance(p, dict):
                el.add(w, "must be an object")
                continue
            for k in ("id", "switch", "viewpoint", "ref", "test", "ref_labels", "test_labels"):
                if not isinstance(p.get(k), str) or not p[k]:
                    el.add(f"{w}.{k}", "missing or empty", "string", str(p.get(k)))
            for k in ("yaw_deg", "k", "distance_factor"):
                if not _isnum(p.get(k)):
                    el.add(f"{w}.{k}", "missing or not a number", "number", str(p.get(k)))
            for k in ("ref", "test", "ref_labels", "test_labels"):
                if isinstance(p.get(k), str) and not os.path.isfile(os.path.join(d, p[k])):
                    el.add(f"{w}.{k}", "file not found", p[k], os.path.join(d, p[k]))
    sweeps = s.get("sweeps", [])
    if not isinstance(sweeps, list):
        el.add("sweeps", "must be a list", "list")
    else:
        for i, sw in enumerate(sweeps):
            w = f"sweeps[{i}]"
            if not isinstance(sw, dict) or not isinstance(sw.get("frames"), list) or not isinstance(sw.get("level_change_masks"), list) \
                    or len(sw["frames"]) != len(sw["level_change_masks"]) or len(sw["frames"]) < 2 or not _isnum(sw.get("speed_mps")) \
                    or not isinstance(sw.get("switch"), str):
                el.add(w, "needs switch, speed_mps and equally long frames / level_change_masks lists (>= 2 frames)", "sweep")
                continue
            for fn in sw["frames"] + sw["level_change_masks"]:
                if not os.path.isfile(os.path.join(d, fn)):
                    el.add(w, "file not found", fn, os.path.join(d, fn))
                    break
    if el:
        raise InvalidInput(el.errors)
    return s, d, f


def contour(labels):
    """Boolean mask of the one-pixel-thick contour: pixels whose lower or right neighbour carries another label (the same convention on
    both frames, so a pure shift of s px displaces the contour by exactly s px)."""
    c = np.zeros(labels.shape, dtype=bool)
    c[:-1, :] |= labels[:-1, :] != labels[1:, :]
    c[:, :-1] |= labels[:, :-1] != labels[:, 1:]
    return c


def contour_rms(lab_ref, lab_test):
    """(rms px or None when neither frame has a contour, ref contour px, test contour px)."""
    from scipy.ndimage import distance_transform_edt
    a, b = contour(lab_ref), contour(lab_test)
    na, nb = int(a.sum()), int(b.sum())
    if na == 0 and nb == 0:
        return None, na, nb
    if na == 0 or nb == 0:
        return float("inf"), na, nb
    d_to_b = distance_transform_edt(~b)
    d_to_a = distance_transform_edt(~a)
    sq = np.concatenate([d_to_b[a] ** 2, d_to_a[b] ** 2])
    return float(math.sqrt(sq.mean())), na, nb


def de_share(ref_rgb, test_rgb, limit):
    ref = np.asarray(ref_rgb)[..., :3].astype(np.uint8)
    tst = np.asarray(test_rgb)[..., :3].astype(np.uint8)
    de = ciede2000(srgb_u8_to_lab(ref), srgb_u8_to_lab(tst))
    return float((de <= limit).mean()), float(de.max())


def pop_events(frames_changed_share, floor, factor, window):
    """Indices of frames whose in-mask changed share exceeds max(floor, factor x median of the previous `window` shares)."""
    pops = []
    for i, s in enumerate(frames_changed_share):
        prev = frames_changed_share[max(0, i - window):i]
        thr = max(floor, factor * float(np.median(prev))) if prev else floor
        if s > thr:
            pops.append(i)
    return pops


def sweep_shares(d, sw):
    shares, prev = [], None
    for fn, mn in zip(sw["frames"], sw["level_change_masks"]):
        fr = read_png(os.path.join(d, fn))
        mk = read_png(os.path.join(d, mn))
        if mk.ndim == 3:
            mk = mk[..., 0]
        if prev is None:
            shares.append(float(0))
        else:
            changed = np.any(fr != prev, axis=-1) if fr.ndim == 3 else fr != prev
            shares.append(float((changed & (mk > 0)).sum()) / changed.size)
        prev = fr
    return shares


def run(rep, inputs, reg, ratings=None, out_dir=None, images=False, options=None):
    if len(inputs) != 1:
        el = ErrorList("")
        el.add("arguments", "exactly one capture-set directory (or m102_set.json) is required", "1 input", str(len(inputs)))
        raise InvalidInput(el.errors)
    s, d, f = load_set(inputs[0])
    rep.add_input(f, sha256_file(f), "lod_capture@v1")
    pairs, sweeps = s["pairs"], s.get("sweeps", [])
    de_lim, share_min = reg.t("M-102.b", "delta_e_max"), reg.t("M-102.b", "share_min")
    # coverage
    need_sw = [str(x) for x in reg.p("switches")]
    ks = [int(x) for x in reg.p("integer_scales")]
    fin, fout = reg.p("inside_factor"), reg.p("outside_factor")
    missing = []
    vps = {p["viewpoint"] for p in pairs}
    if len(vps) < reg.p("min_viewpoints"):
        missing.append(f"{len(vps)} viewpoints (< {reg.p('min_viewpoints')})")
    for sw in need_sw:
        for k in ks:
            for fac, nm in ((fin, "inside"), (fout, "outside")):
                if not any(p["switch"] == sw and int(p["k"]) == k and math.isclose(p["distance_factor"], fac) for p in pairs):
                    missing.append(f"{sw} k={k} {nm}")
    yaws = {}
    for p in pairs:
        yaws.setdefault(p["viewpoint"], set()).add(round(p["yaw_deg"], 3))
    short_yaw = [v for v, y in yaws.items() if len(y) < reg.p("yaws_per_viewpoint")]
    if short_yaw:
        missing.append(f"{len(short_yaw)} viewpoints with < {reg.p('yaws_per_viewpoint')} yaws")
    for sw in need_sw:
        for sp in reg.p("sweep_speeds_mps"):
            if not any(x["switch"] == sw and math.isclose(x["speed_mps"], sp) and len(x["frames"]) >= reg.p("sweep_frames") for x in sweeps):
                missing.append(f"sweep {sw} {sp:g} m/s x {reg.p('sweep_frames')} frames")
    rep.coverage = {"required": [f">= {reg.p('min_viewpoints')} viewpoints x {reg.p('yaws_per_viewpoint')} yaws x k {ks} x {need_sw} x inside/outside; sweeps"],
                    "provided": [f"{len(pairs)} pairs, {len(vps)} viewpoints, {len(sweeps)} sweeps"], "missing": missing[:40], "optional_not_provided": []}
    # (a), (b)
    pa, pb = [], []
    for p in pairs:
        lr, lt = read_png(os.path.join(d, p["ref_labels"])), read_png(os.path.join(d, p["test_labels"]))
        cr, ct = read_png(os.path.join(d, p["ref"])), read_png(os.path.join(d, p["test"]))
        for nm, a, b in (("ref/test labels", lr, lt), ("ref/test colour", cr, ct)):
            if a.shape[:2] != b.shape[:2]:
                el = ErrorList(f)
                el.add(p["id"], f"{nm} differ in size", str(a.shape[:2]), str(b.shape[:2]))
                raise InvalidInput(el.errors)
        rms, na, nb = contour_rms(lr, lt)
        nm = f"{p['id']} ({p['switch']} {p['viewpoint']} yaw {p['yaw_deg']:g} k{int(p['k'])} x{p['distance_factor']})"
        if rms is None:
            pa.append({"name": nm, "value": None, "status": S_NOT_RUN, "op": "<=", "threshold": reg.t("M-102.a", "contour_rms_px_max"),
                       "margin": None, "margin_rel": None, "unit": "output px", "note": "no contour in either frame: nothing to compare"})
        else:
            pa.append(part(nm, rms, "<=", reg.t("M-102.a", "contour_rms_px_max"), unit="output px", contour_px=[na, nb]))
        share, dmax = de_share(cr, ct, de_lim)
        pb.append(part(nm, share, ">=", share_min, unit="share", delta_e_max_seen=dmax))
    rep.add("M-102.a", parts=pa, diagnostics={"pairs": len(pairs)})
    rep.add("M-102.b", parts=pb, diagnostics={"delta_e_limit": de_lim})
    # (c)
    floor, factor, win = reg.p("pop_floor_share"), reg.p("pop_median_factor"), int(reg.p("pop_median_window"))
    pc, pop_detail = [], {}
    for sw in sweeps:
        shares = sweep_shares(d, sw)
        pops = pop_events(shares, floor, factor, win)
        pop_detail[sw.get("id", sw["switch"])] = {"pops": pops, "max_share": max(shares)}
        pc.append(part(f"{sw.get('id', sw['switch'])} ({sw['switch']} {sw['speed_mps']:g} m/s)", len(pops), "<=",
                       reg.t("M-102.c", "pop_events_max"), unit="count", max_share=max(shares)))
    rep.add("M-102.c", parts=pc or None, diagnostics={"sweeps": len(sweeps)})
    # (d)
    m4 = s.get("m004")
    pdm = []
    if isinstance(m4, dict) and m4.get("lod") and m4.get("pinned"):
        def rd(k):
            pth = os.path.join(d, m4[k])
            rep.add_input(pth, sha256_file(pth), f"m004_report_{k}")
            return load_json_file(pth)
        lod, pin = rd("lod"), rd("pinned")
        marg = reg.t("M-102.d", "margin_over_pinned")
        pdm.append(part("M-004 verdict of the LOD run is not FAIL", 1 if lod.get("verdict") == "FAIL" else 0, "<=", 0, unit="count"))
        seq_p = {q["sequence"]: q for q in pin.get("extra", {}).get("sequences", [])}
        for q in lod.get("extra", {}).get("sequences", []):
            o = seq_p.get(q["sequence"])
            if o is None:
                continue
            for key, lab in (("S", "S_px"), ("C", "C_px")):
                if q.get(key) is None or o.get(key) is None:
                    continue
                pdm.append(part(f"{q['sequence']} {lab}", q[key] - o[key], "<=", marg, unit="share", lod=q[key], pinned=o[key]))
    rep.add("M-102.d", parts=pdm or None, note=None if pdm else "no M-004 LOD / pinned reports given")
    rep.extra["m102"] = {"pairs": len(pairs), "viewpoints": len(vps), "sweeps": pop_detail}
