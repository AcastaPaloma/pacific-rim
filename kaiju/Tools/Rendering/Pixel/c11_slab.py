#!/usr/bin/env python
"""T-182: C11 slab / depthShift analysis (run with the kaiju conda env: /opt/miniconda3/envs/kaiju/bin/python).

Reads the c11_slab@v1 sequences written by the Unity job `c11slab` (Assets/Game/Tools/StyleCapture/Editor/C11SlabJobs.cs):
per frame, the log-coded eye depth of the presented output along the row of each class point (near 124 m, reference 663 m,
far 10.4 km), as rendered by the shipped present stage (single snap, depthShift, or layered slabs). From that it measures,
per class, the output-pixel x of the tracked depth discontinuity in every frame and reports the R-131 figure

    error(frame) = (x(frame) - x(first)) - (commanded(frame) - commanded(first))      [output px]
    per phase (one start offset) = max over frames of |error|;  worst-phase = max over phases, median-phase = median over phases

next to the R-131 model figures (research/R-131/c11_sim.py logic, re-evaluated here at the snapped depth of each configuration)
and the any-depth model figure. The absolute error |x - commanded| (pixel_checks.py depth_class criterion) is reported too.
Nothing here changes a threshold: the C11 gate stays 1 px (Q-260 is open, so both main depths are reported).

  c11_slab.py <captures_root> [--out results.json] [--md table.md]
  c11_slab.py --selftest
"""
import argparse, base64, glob, gzip, json, math, os, sys
import numpy as np

CLASSES = ("near", "reference", "far")
CLASS_DEPTH_M = {"near": 124.2, "reference": 663.0, "far": 10353.0}     # model classes (R-131 section 4.1)
EDGE_MIN_REL_JUMP = 0.03          # same discontinuity rule as pixel_checks.py (relative eye-depth change between neighbours)
SKY_CODE = 16777215
MIN_FRAMES = 100                 # a sequence counts for a class when the edge is tracked over at least this many of the 180 frames
GATE_PX = 1.0                     # C11 (unchanged): displacement error within 1 px
K = 4                             # output pixels per texel in the model (1080p)


# ---- depth code ------------------------------------------------------------------------------------------------------

def decode_depth(b64):
    """24-bit codes -> eye depth (m), inf for sky. v = round(16777214 * ln(z / 0.1) / ln(1e7))."""
    raw = np.frombuffer(base64.b64decode(b64), dtype=np.uint8).reshape(-1, 3).astype(np.int64)
    v = raw[:, 0] * 65536 + raw[:, 1] * 256 + raw[:, 2]
    z = 0.1 * np.power(1e7, v / 16777214.0)
    return np.where(v >= SKY_CODE, np.inf, z)


def encode_depth(z):
    """Inverse of decode_depth (selftest and tests)."""
    z = np.asarray(z, float)
    v = np.where(np.isfinite(z), np.floor(np.clip(np.log(np.maximum(z, 0.1) / 0.1) / math.log(1e7), 0, 1) * 16777214.0 + 0.5), SKY_CODE).astype(np.int64)
    return np.stack([v // 65536, (v // 256) % 256, v % 256], axis=1).astype(np.uint8).tobytes()


def edge_candidates(z, x0):
    """Output-pixel boundaries i (between pixel i-1 and i) with a relative depth jump above the threshold; sky counts as a jump."""
    a, b = z[:-1], z[1:]
    lo = np.minimum(a, b)
    with np.errstate(invalid="ignore", divide="ignore"):
        rel = np.abs(a - b) / lo
    rel = np.where(np.isinf(a) != np.isinf(b), np.inf, np.where(np.isinf(a) & np.isinf(b), 0.0, rel))
    return np.where(rel > EDGE_MIN_REL_JUMP)[0] + 1 + x0


# ---- one sequence ----------------------------------------------------------------------------------------------------

def load_sequence(d):
    meta = json.load(open(os.path.join(d, "sequence.json")))
    with gzip.open(os.path.join(d, meta["files"]["frames"]), "rt") as f:
        frames = json.load(f)
    return meta, frames


def track_class(frames, cls_index):
    """Tracks the edge of one class over the frames. Returns (frame indices, measured x, commanded px) as arrays; frames without a visible edge are skipped."""
    idx, meas, cmd = [], [], []
    offset = None   # measured - commanded of the first measured frame: the tracker looks for the discontinuity nearest to commanded + offset
    for fr in frames:
        c = fr["classes"][cls_index]
        if not c["visible"] or "depth_code_b64" not in c: continue
        z = decode_depth(c["depth_code_b64"])
        cand = edge_candidates(z, c["x0"])
        if len(cand) == 0: continue
        target = c["px"] + (offset if offset is not None else 0.0)
        i = cand[np.argmin(np.abs(cand - target))]
        if offset is None: offset = float(i) - c["px"]
        elif abs(i - target) > 24 * K: continue   # a different object entered the window; not the tracked edge
        idx.append(fr["i"]); meas.append(float(i)); cmd.append(c["px"])
    return np.array(idx), np.array(meas), np.array(cmd)


def sequence_errors(frames):
    """Per class: max |displacement error| over frames (the R-131 per-phase figure), max absolute error, frames measured."""
    out = {}
    for ci, name in enumerate(CLASSES):
        idx, m, c = track_class(frames, ci)
        if len(idx) < 2:
            out[name] = {"frames": int(len(idx)), "max_disp_error_px": None, "max_abs_error_px": None}; continue
        disp = (m - m[0]) - (c - c[0])
        out[name] = {"frames": int(len(idx)), "max_disp_error_px": float(np.abs(disp).max()), "max_abs_error_px": float(np.abs(m - c).max()),
                     "max_step_error_px": float(np.abs(np.diff(m) - np.diff(c)).max())}
    return out


# ---- the R-131 model (research/R-131/c11_sim.py), re-evaluated at the snapped depth of each configuration ---------------

PH = (np.arange(16) + 0.37) / 16.0


def model_stats(scheme, z_snap, z_surface, z_layer=None, frames=180):
    """Model worst-phase / median-phase max |error| (output px) for a surface at z_surface.
    scheme: base | depthshift | layersnap. z_snap: depth of the (main) snap grid; z_layer: depth of the layer the surface is drawn in (layersnap).
    Units are texels at z_snap; the camera moves 0.0902 texel/frame of the 664.874 m grid (T1: 0.1 texel/frame of the 600 m grid, as R-131 calibrated it)."""
    fr = np.arange(frames)
    c = 0.0902 * 664.874 / z_snap * fr + 0.6153   # R-131 uses 0.0902 texel/frame at 664.874 m (the sidecars' measured speed); same metres per frame at any snap depth
    rho = z_snap / z_surface
    worst = []
    for phi in PH:
        xs, cm = [], []
        for ci in c:
            if scheme == "base":
                S = np.ceil(ci); rem = ci - S; u = rho * (0 - S) + phi; shift = np.round(K * rem)
            elif scheme == "depthshift":
                S = np.ceil(ci); rem = ci - S; u = rho * (0 - S) + phi; shift = np.round(K * rho * rem)
            else:
                r = z_snap / z_layer; Sl = np.ceil(ci * r) / r; rem = ci - Sl; u = rho * (0 - Sl) + phi; shift = np.round(K * r * rem)
            xs.append(K * np.round(u) - shift); cm.append(K * rho * (0 - ci))
        xs = np.array(xs); cm = np.array(cm)
        worst.append(np.abs((xs - xs[0]) - (cm - cm[0])).max())
    return float(max(worst)), float(np.median(worst))


def model_for(mode, snap_depth, surfaces, layers=None):
    """surfaces: class -> surface depth (m). layers: [near, main, far] layer depths (layersnap). snap_depth: the single snap depth, or the main layer depth."""
    out = {}
    for i, name in enumerate(CLASSES):
        z = surfaces[name]
        if mode == "layersnap": w, m = model_stats("layersnap", snap_depth, z, z_layer=layers[i])
        else: w, m = model_stats(mode, snap_depth, z)
        out[name] = {"worst_phase_px": w, "median_phase_px": m}
    return out


def check_model_against_r131(path):
    """The re-evaluated model must reproduce research/R-131/out_c11_sim.json at 664.874 m (guards the replication)."""
    ref = json.load(open(path)); bad = []
    for mode, key in (("base", "baseline_uniform_shift"), ("depthshift", "depth_aware_shift_single_snap")):
        mine = model_for(mode, 664.874, CLASS_DEPTH_M)
        for name in CLASSES:
            w, m = ref[key][name]
            if abs(mine[name]["worst_phase_px"] - w) > 0.05 or abs(mine[name]["median_phase_px"] - m) > 0.05: bad.append((mode, name, (w, m), mine[name]))
    return bad


# ---- the report -------------------------------------------------------------------------------------------------------

def config_dirs(root):
    cfgs = {}
    for d in sorted(glob.glob(os.path.join(root, "c11slab_*", "[AB]*"))):
        cfgs.setdefault(os.path.basename(os.path.dirname(d))[len("c11slab_"):], []).append(d)
    return cfgs


def analyse(root, r131_json=None):
    cfgs = config_dirs(root); res = {"schema": "c11_slab_results@v1", "gate_px": GATE_PX, "configs": {}}
    for name, dirs in cfgs.items():
        metas, per_phase = [], []
        for d in dirs:
            meta, frames = load_sequence(d); metas.append(meta); per_phase.append(sequence_errors(frames))
        m0 = metas[0]
        entry = {"mode": m0["mode"], "main_depth_m": m0["main_depth_m"], "snap_reference_depth_m": m0["snap_reference_depth_m"], "sequences": len(dirs), "groups": sorted({m["group"] for m in metas}),
                 "frames_per_phase": m0["frames"], "layered_frames_min": min(m["layered_frames"] for m in metas), "layer_rejections": sorted({m["layer_rejection"] for m in metas if m["layer_rejection"]}), "classes": {}}
        for cls in CLASSES:
            # A class is measured only over the start groups where its edge is a clean silhouette (the capture job's group_classes); frames below MIN_FRAMES are dropped
            use = [p for p, m in zip(per_phase, metas) if cls in m["group_classes"] and p[cls]["frames"] >= MIN_FRAMES]
            disp = [p[cls]["max_disp_error_px"] for p in use if p[cls]["max_disp_error_px"] is not None]
            absn = [p[cls]["max_abs_error_px"] for p in use if p[cls]["max_abs_error_px"] is not None]
            nfr = [p[cls]["frames"] for p in use] or [0]
            entry["classes"][cls] = {"phases_measured": len(disp), "frames_median": float(np.median(nfr)), "frames_min": int(min(nfr)), "sequences_dropped": int(len([1 for m in metas if cls in m["group_classes"]]) - len(use)),
                                     "worst_phase_px": max(disp) if disp else None, "median_phase_px": float(np.median(disp)) if disp else None,
                                     "abs_worst_phase_px": max(absn) if absn else None, "abs_median_phase_px": float(np.median(absn)) if absn else None,
                                     "pass_1px_worst": bool(disp and max(disp) <= GATE_PX), "pass_1px_median": bool(disp and float(np.median(disp)) <= GATE_PX),
                                     "starts_within_1px": int(sum(1 for d in disp if d <= GATE_PX)), "share_starts_within_1px": (sum(1 for d in disp if d <= GATE_PX) / len(disp)) if disp else None}
        if "and_depthshift" not in name:
            mode = m0["mode"]; snap = m0["main_depth_m"] if mode == "layersnap" else m0["snap_reference_depth_m"]
            geo = dict(zip(CLASSES, m0["class_depths_geometric_m"]))
            entry["class_depths_geometric_m"] = geo
            entry["model"] = model_for(mode, snap, CLASS_DEPTH_M, m0["layer_depths_m"])          # surfaces at the R-131 class depths (124.2 / 663.0 / 10353)
            entry["model_geometric"] = model_for(mode, snap, geo, m0["layer_depths_m"])          # surfaces at the geometric depths of the capture points
        res["configs"][name] = entry
    # depthShift is ignored under layerSnap: edge tracks must equal those of layerSnap alone, frame by frame
    both, alone = "ref663_layersnap_and_depthshift", "ref663_layersnap"
    if both in cfgs and alone in cfgs:
        _, fa = load_sequence(cfgs[both][0]); _, fb = load_sequence(next(d for d in cfgs[alone] if os.path.basename(d) == "B00"))
        same = all(np.array_equal(track_class(fa, i)[1], track_class(fb, i)[1]) for i in (1, 2))
        res["depthshift_ignored_under_layersnap"] = {"edge_tracks_identical": bool(same), "frames": len(fa)}
    if r131_json:
        any_depth = json.load(open(r131_json))["layers_log_spaced"]
        res["any_depth_model_r131"] = {k: v for k, v in any_depth.items()}
        res["model_replication_mismatches"] = check_model_against_r131(r131_json)
    return res


def fmt(v): return "n/a" if v is None else "%.2f" % v


def markdown(res):
    L = ["# C11 slab / depthShift captures (T-182)", "",
         "Measured through the shipped present stage (output-resolution edge positions). Figures are max |displacement error| in output px over the frames of T1 where the edge is tracked (180; near about 130), per start offset; worst = max over start offsets, median = median over start offsets, plus how many start offsets meet 1 px. A start offset that puts the edge phase exactly on a texel rounding boundary flips by one texel (4 px) even when the lock is exact, so worst is a property of the number of starts tried; the median and the share are the stable figures. Model = R-131 `c11_sim.py` logic re-evaluated at the snapped depth of the configuration (16 edge phases). Gate: 1 px, unchanged.", ""]
    for cls in CLASSES:
        L += ["## " + cls + " class (R-131 depth %.1f m)" % CLASS_DEPTH_M[cls], "", "| configuration | starts | frames (min) | measured worst | measured median | starts within 1 px | R-131 model worst / median | model at geometric depth worst / median | abs worst / median | gate 1 px (worst) |", "|---|---|---|---|---|---|---|---|---|---|"]
        for name, e in res["configs"].items():
            c = e["classes"][cls]; m = e.get("model", {}).get(cls); g = e.get("model_geometric", {}).get(cls)
            L.append("| %s | %d | %d | %s | %s | %s | %s | %s | %s | %s |" % (name, c["phases_measured"], c["frames_min"], fmt(c["worst_phase_px"]), fmt(c["median_phase_px"]),
                                                                       "%d / %d" % (c["starts_within_1px"], c["phases_measured"]),
                                                                       "%s / %s" % (fmt(m["worst_phase_px"]), fmt(m["median_phase_px"])) if m else "-", "%s / %s" % (fmt(g["worst_phase_px"]), fmt(g["median_phase_px"])) if g else "-",
                                                                       "%s / %s" % (fmt(c["abs_worst_phase_px"]), fmt(c["abs_median_phase_px"])), "pass" if c["pass_1px_worst"] else "fail"))
        L.append("")
    if "any_depth_model_r131" in res:
        L += ["## Any depth (R-131 model, per-layer snap with N log-spaced layers, worst over all surface depths)", ""] + ["- %s: worst %.1f px, median %.1f px" % (k, v["worst_px_any_depth"], v["median_px"]) for k, v in res["any_depth_model_r131"].items()] + [""]
    if "depthshift_ignored_under_layersnap" in res: L += ["depthShift ignored under layerSnap: edge tracks identical = %s" % res["depthshift_ignored_under_layersnap"]["edge_tracks_identical"], ""]
    L += ["Layered frames (min over start offsets): " + ", ".join("%s %d/%d" % (n, e["layered_frames_min"], e["frames_per_phase"]) for n, e in res["configs"].items() if e["mode"] == "layersnap"), ""]
    return "\n".join(L)


# ---- selftest ----------------------------------------------------------------------------------------------------------

def selftest():
    z = np.array([5000.0, 120.0, np.inf, 664.0, 0.5])
    back = decode_depth(base64.b64encode(encode_depth(z)).decode())
    assert np.all(np.isinf(back) == np.isinf(z)) and np.allclose(back[np.isfinite(back)], z[np.isfinite(z)], rtol=2e-6), back
    # synthetic sequence: a near box edge (120 m) over a far wall (5 km), tracked over 40 frames, one jitter of 3 px at frame 20
    frames = []
    for i in range(40):
        px = 400.0 - 2.5 * i; true = int(round(px)) + (3 if i >= 20 else 0)
        x0 = int(px) - 128; n = 257; zz = np.where(np.arange(x0, x0 + n) >= true, 120.0, 5000.0)
        frames.append({"i": i, "classes": [{"visible": True, "px": px, "py": 10.0, "x0": x0, "n": n, "depth_code_b64": base64.b64encode(encode_depth(zz)).decode()}] + [{"visible": False}] * 2})
    idx, m, c = track_class(frames, 0)
    disp = (m - m[0]) - (c - c[0])
    assert len(idx) == 40 and abs(np.abs(disp).max() - 3.5) < 1.0, (len(idx), np.abs(disp).max())
    assert model_stats("layersnap", 664.874, 124.2, 124.2)[0] < 1.0
    assert not check_model_against_r131(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "research", "R-131", "out_c11_sim.json"))
    print("selftest ok")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("root", nargs="?"); ap.add_argument("--out"); ap.add_argument("--md"); ap.add_argument("--selftest", action="store_true")
    ap.add_argument("--r131", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "research", "R-131", "out_c11_sim.json"))
    a = ap.parse_args()
    if a.selftest: return selftest()
    if not a.root: ap.error("captures root required")
    res = analyse(a.root, a.r131 if os.path.isfile(a.r131) else None)
    if a.out: json.dump(res, open(a.out, "w"), indent=1)
    md = markdown(res)
    if a.md: open(a.md, "w").write(md + "\n")
    print(md)


if __name__ == "__main__":
    main()
