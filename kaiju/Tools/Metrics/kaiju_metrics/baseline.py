"""Reference-panel baseline (C17): the still-applicable M-003 measures on the 11 reference panels.

Panels, boxes and caption / logo exclusions come from research/R-002/panels.json; ref_06A's soft alpha matte is
eroded by 4 px (R-002 scripts/common.py: load_panel). De-noising follows R-002 §2: a periodic pixel grid (mean
resultant length R of strong-edge positions, periods 2..12 px) is area-resampled to the native grid; otherwise a
3 x 3 median filter and 2x decimation. Measures: (a) A_T of the raw pixels against the panel's own palette and of
the de-noised pixels against its district profile, (b) DIG and O on the de-noised pixels mapped to the panel
palette, (d-i) value levels, (d-ii) hue families, (d-iii) accent share, (e) silhouette contrast. Not applicable to
stills: a.internal, c1, c2 (no depth / haze scene), d-iv (resampled re-encodes), e.sky / e.horizon (no depth / pose),
f, all of M-004. The baseline verdict is NOT_APPLICABLE (not a capture set); panels that fail a gate stay FAIL."""
from __future__ import annotations

import os

import numpy as np

from .colour import nearest_lab, nearest_lab_rgb, srgb_u8_to_lab
from .common import PROJECT_ROOT, dump_json, load_json_file, rel_path, sha256_file
from .registry import load_registry
from .report import judge
from .style import measures as M
from .style.m003 import DITHER_KEYS

PANELS_JSON = os.path.join(PROJECT_ROOT, "research", "R-002", "panels.json")
PALETTES_JSON = os.path.join(PROJECT_ROOT, "research", "R-002", "palettes.json")
VALIDATION = os.path.join(PROJECT_ROOT, "research", "R-002", "m003_validation.json")
REFS = os.path.join(PROJECT_ROOT, "art", "style_refs")
GRID_R, GRID_R_WEAK = 0.08, 0.05          # R-002 §2 grid detection thresholds (research procedure constants)
ERODE = {"ref_06A": 4}


def load_panel(pid, info):
    import cv2
    from scipy.ndimage import binary_erosion
    a = cv2.imread(os.path.join(REFS, info["file"]), cv2.IMREAD_UNCHANGED)
    if a.shape[2] == 3:
        a = np.dstack([a, np.full(a.shape[:2], 255, np.uint8)])
    a = a[..., [2, 1, 0, 3]]
    x0, y0, x1, y1 = info["box_xyxy"]
    rgb = a[y0:y1, x0:x1, :3].copy()
    mask = a[y0:y1, x0:x1, 3] == 255
    for ex0, ey0, ex1, ey1 in info["excluded_boxes"]:
        mask[max(ey0 - y0, 0):max(ey1 - y0, 0), max(ex0 - x0, 0):max(ex1 - x0, 0)] = False
    if pid in ERODE:
        mask = binary_erosion(mask, iterations=ERODE[pid])
    return rgb, mask


def grid_period(gray, mask, axis, pmin=2.0, pmax=12.0):
    d = np.abs(np.diff(gray, axis=axis))
    mm = (mask[:, 1:] & mask[:, :-1]) if axis == 1 else (mask[1:] & mask[:-1])
    thr = np.percentile(d[mm], 95)
    ys, xs = np.where((d > thr) & mm)
    pos = (xs if axis == 1 else ys) + 0.5
    ps = np.arange(pmin, pmax + 1e-9, 0.01)
    R = np.empty(len(ps))
    for i in range(0, len(ps), 50):
        ph = np.exp(2j * np.pi * pos[None, :] / ps[i:i + 50, None])
        R[i:i + 50] = np.abs(ph.mean(1))
    cand = [j for j in range(len(ps)) if R[j] >= 0.85 * R.max()
            and (j == 0 or R[j] >= R[j - 1]) and (j == len(R) - 1 or R[j] >= R[j + 1])]
    j = cand[-1]
    return float(ps[j]), float(R[j])


def denoise(rgb, mask):
    import cv2
    g = rgb.astype(float).mean(2)
    px, rx = grid_period(g, mask, 1)
    py, ry = grid_period(g, mask, 0)
    detected = min(rx, ry) >= GRID_R
    weak = (not detected) and max(rx, ry) >= GRID_R_WEAK and abs(px - py) / min(px, py) < 0.1 and max(px, py) <= 8.0
    if detected or weak:
        p = 0.5 * (px + py)
        h, w = mask.shape
        nw, nh = int(round(w / p)), int(round(h / p))
        small = cv2.resize(rgb, (nw, nh), interpolation=cv2.INTER_AREA)
        sm = cv2.resize(mask.astype(np.float32), (nw, nh), interpolation=cv2.INTER_AREA) > 0.99
    else:
        p = None
        small = cv2.medianBlur(rgb, 3)[::2, ::2]
        sm = mask[::2, ::2]
    return {"px": px, "rx": rx, "py": py, "ry": ry, "detected": bool(detected), "weak": bool(weak), "p": p}, small, sm


def panel_measures(pid, info, pal_json, reg):
    rgb, mask = load_panel(pid, info)
    gi, small, sm = denoise(rgb, mask)
    lab_small = srgb_u8_to_lab(small)
    ppal = pal_json["panels"][pid]
    plab = np.array([c["lab"] for c in ppal["palette"]])
    prof_id = next((k for k, v in pal_json["profiles"].items() if pid in v["members"]), None)
    out = {"panel": pid, "file": info["file"], "box_xyxy": info["box_xyxy"], "profile": prof_id, "grid": gi}
    # (a) raw vs own palette, de-noised vs district profile
    raw = rgb[mask]
    d_raw, _ = nearest_lab_rgb(raw, plab)
    out["raw_vs_own_palette"] = {str(t): float((d_raw <= t).mean()) for t in (2, 3, 5)}
    if prof_id:
        prlab = np.array([c["lab"] for c in pal_json["profiles"][prof_id]["colours"]])
        d_pr, _ = nearest_lab(lab_small[sm], prlab)
        out["denoised_vs_profile"] = {str(t): float((d_pr <= t).mean()) for t in (2, 3, 5)}
        d_rawp, _ = nearest_lab_rgb(raw, prlab)
        out["raw_vs_profile"] = {str(t): float((d_rawp <= t).mean()) for t in (2, 3, 5)}
    # (b) dither on de-noised pixels mapped to the panel palette
    _, idx = nearest_lab(lab_small.reshape(-1, 3), plab)
    idx = idx.reshape(lab_small.shape[:2])
    P = {k: reg.p(k) for k in DITHER_KEYS}
    dm, _ = M.dither_metrics(idx, plab[:, 0][idx], P)
    out["dither"] = dm
    share = np.array([c["share"] for c in ppal["palette"]])
    out["value_levels"] = M.value_levels(plab[:, 0], share, reg.p("level_share_min"), reg.p("level_merge_dL"))
    nf, fam = M.hue_families(lab_small[sm], reg.p("hue_cmin"), reg.p("hue_sigma_deg"), reg.p("hue_mass_min"))
    out["hue_families"], out["hue_peaks"] = nf, fam
    out["accent_share"] = ppal["accent"]["share"] if ppal.get("accent") else None
    L = lab_small[..., 0]
    lo, hi = np.percentile(L, [10, 90])
    out["silhouette_contrast"] = float(hi - lo)
    return out


def _status(v, op, t, t2=None):
    return judge(v, op, t, t2)[0]


def baseline(out_dir, registry=None):
    reg = load_registry("M-003", registry)
    pj = load_json_file(PANELS_JSON)["panels"]
    pal = load_json_file(PALETTES_JSON)
    import json
    with open(VALIDATION) as f:          # research output: contains NaN tokens (not a contract file)
        val = json.load(f)["references"]
    os.makedirs(out_dir, exist_ok=True)
    rows = []
    for pid, info in pj.items():
        m = panel_measures(pid, info, pal, reg)
        sm = {
            "M-003.a.output": {"status": _status(m.get("raw_vs_profile", m["raw_vs_own_palette"])["2"], ">=",
                                                 reg.t("M-003.a.output", "min_share")),
                               "value": m.get("raw_vs_profile", m["raw_vs_own_palette"])["2"],
                               "note": "raw panel pixels vs the district profile (own palette when no profile)"},
            "M-003.a.internal": {"status": "NOT_APPLICABLE", "note": "no internal (pre-upscale) buffer for a still"},
            "M-003.b.DIG": {"status": _status(m["dither"]["DIG"], ">=", reg.t("M-003.b.DIG", "dig_min")),
                            "value": m["dither"]["DIG"], "note": "single still: per-frame DIG >= 0.15"},
            "M-003.b.DIG_max": {"status": _status(m["dither"]["DIG"], "<=", reg.t("M-003.b.DIG_max", "dig_max")),
                                "value": m["dither"]["DIG"]},
            "M-003.b.O": {"status": "REPORTED", "value": m["dither"]["O"]},
            "M-003.c1": {"status": "NOT_APPLICABLE", "note": "no depth for a still"},
            "M-003.c2": {"status": "NOT_APPLICABLE", "note": "no haze_cal scene"},
            "M-003.d-i": {"status": _status(m["value_levels"], "<=", reg.t("M-003.d-i", "levels_max")),
                          "value": m["value_levels"]},
            "M-003.d-ii": {"status": _status(m["hue_families"], "<=", reg.t("M-003.d-ii", "families_max")),
                           "value": m["hue_families"]},
            "M-003.d-iii": ({"status": _status(m["accent_share"], "in", reg.t("M-003.d-iii", "share_lo"),
                                               reg.t("M-003.d-iii", "share_hi")), "value": m["accent_share"]}
                            if m["accent_share"] else {"status": "NOT_APPLICABLE", "note": "no accent"}),
            "M-003.d-iv": {"status": "NOT_APPLICABLE", "note": "references are resampled re-encodes (R-002 §4 d-iv)"},
            "M-003.e.sky": {"status": "NOT_APPLICABLE", "note": "no depth"},
            "M-003.e.horizon": {"status": "NOT_APPLICABLE", "note": "no camera pose"},
            "M-003.e.contrast": {"status": _status(m["silhouette_contrast"], ">=", reg.t("M-003.e.contrast", "min")),
                                 "value": m["silhouette_contrast"]},
            "M-003.f": {"status": "NOT_APPLICABLE", "note": "panel ratings are for game captures"},
            "M-004": {"status": "NOT_APPLICABLE", "note": "stills have no motion"},
        }
        r002 = val.get(pid, {})
        ref = {"DIG": r002.get("dither", {}).get("DIG"), "O": r002.get("dither", {}).get("O"),
               "value_levels": r002.get("value_levels"), "hue_families": r002.get("hue_families"),
               "silhouette_contrast": r002.get("composition", {}).get("silhouette_dL_10_90"),
               "raw_vs_own_palette_2": pj[pid]["adherence_raw"]["2"], "raw_vs_own_palette_5": pj[pid]["adherence_raw"]["5"],
               "denoised_vs_profile": (pal["profiles"][m["profile"]]["member_adherence"][pid][0] if m["profile"] else None)}
        rec = {"report_schema": "baseline_panel@v1", "panel": pid, "verdict": "NOT_APPLICABLE: not a capture set",
               "registry": reg.describe(), "input": {"path": rel_path(os.path.join(REFS, info["file"])),
                                                     "sha256": sha256_file(os.path.join(REFS, info["file"]))},
               "measures": m, "submeasures": sm, "r002": ref}
        with open(os.path.join(out_dir, f"{pid}.json"), "w") as f:
            f.write(dump_json(rec))
        rows.append(rec)
    table = comparison_table(rows)
    with open(os.path.join(out_dir, "comparison.md"), "w") as f:
        f.write(table)
    return rows, table


TOL = {"DIG": 0.03, "O": 0.03, "raw2": 0.03, "raw5": 0.02, "prof": 0.03, "contrast": 1.0}


def comparison_table(rows):
    L = ["# Reference-panel baseline vs R-002 (tolerances: DIG / O +-0.03, raw A_2 +-0.03, raw A_5 +-0.02, "
         "member adherence +-0.03, value levels and hue families equal, contrast +-1 L*)", "",
         "| Panel | profile | DIG (R-002) | O (R-002) | raw A_2 own (R-002) | raw A_5 own (R-002) | A_2/A_3/A_5 de-noised vs profile (R-002) | levels (R-002) | hue fam. (R-002) | contrast (R-002) | a.output | b.DIG | d-i | d-ii | d-iii | within tol |",
         "|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|"]
    for r in rows:
        m, ref, s = r["measures"], r["r002"], r["submeasures"]
        ok = []
        ok.append(abs(m["dither"]["DIG"] - ref["DIG"]) <= TOL["DIG"])
        ok.append(abs(m["dither"]["O"] - ref["O"]) <= TOL["O"])
        ok.append(abs(m["raw_vs_own_palette"]["2"] - ref["raw_vs_own_palette_2"]) <= TOL["raw2"])
        ok.append(abs(m["raw_vs_own_palette"]["5"] - ref["raw_vs_own_palette_5"]) <= TOL["raw5"])
        ok.append(m["value_levels"] == ref["value_levels"])
        ok.append(m["hue_families"] == ref["hue_families"])
        ok.append(abs(m["silhouette_contrast"] - ref["silhouette_contrast"]) <= TOL["contrast"])
        prof = "-"
        if m.get("denoised_vs_profile"):
            d = m["denoised_vs_profile"]; rp = ref["denoised_vs_profile"]
            prof = f"{d['2']:.2f}/{d['3']:.2f}/{d['5']:.2f} ({rp['2']:.2f}/{rp['3']:.2f}/{rp['5']:.2f})"
            ok.append(all(abs(d[k] - rp[k]) <= TOL["prof"] for k in ("2", "3", "5")))
        L.append(f"| {r['panel']} | {m['profile'] or '-'} | {m['dither']['DIG']:.3f} ({ref['DIG']}) | "
                 f"{m['dither']['O']:.3f} ({ref['O']}) | {m['raw_vs_own_palette']['2']:.3f} ({ref['raw_vs_own_palette_2']:.3f}) | "
                 f"{m['raw_vs_own_palette']['5']:.3f} ({ref['raw_vs_own_palette_5']:.3f}) | {prof} | "
                 f"{m['value_levels']} ({ref['value_levels']}) | {m['hue_families']} ({ref['hue_families']}) | "
                 f"{m['silhouette_contrast']:.1f} ({ref['silhouette_contrast']}) | {s['M-003.a.output']['status']} | "
                 f"{s['M-003.b.DIG']['status']} | {s['M-003.d-i']['status']} | {s['M-003.d-ii']['status']} | "
                 f"{s['M-003.d-iii']['status']} | {'yes' if all(ok) else 'NO (' + str(sum(not x for x in ok)) + ' off)'} |")
    L += ["", "Verdict for every panel: NOT_APPLICABLE (not a capture set). Not applicable to stills: a.internal, c1, c2, "
          "d-iv, e.sky, e.horizon, f, M-004. FAIL statuses above are kept as measured; no threshold was changed."]
    return "\n".join(L) + "\n"


def baseline_cli(args):
    rows, table = baseline(args.out, args.registry)
    print(table, end="")
    return 0
