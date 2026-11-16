"""M-003.d-iv@v2 (T-196b, proposal P-1 of research/R-M3-DIV-review.md): per-slab block uniformity for layered frames.

ADDITIVE and ADVISORY: reported next to d-iv@v1 (scale.blocks_uniform / M-003.d-iv), never replacing it, and no gate or
spec verdict is changed by this module. Threshold 0.995 and the 1-code-value block tolerance are the v1 values.

Hardening (review section 5):
 (a) blind estimate: per slab the best (ox, oy) in 0..s-1 by uniformity must reach the score of the declared offset
     (declared must be among the maxima; ties on flat content are flagged `ambiguous`, not failed);
 (b) excluded seam area is capped: evaluated area >= min_eval_area (0.95) of the non-UI pixels;
 (c) slab labels come from the capture (`depth_linear` + sidecar slab bounds); optional renderer labels must agree except
     within one block of a slab boundary;
 (d) gate per slab AND for the whole frame at 0.995; clip value = min over frames.
Frames without layers: `d_iv_v2` returns the v1 whole-frame reading (version "v1", same 0.995 gate).
Seam tearing (A2-style palette check on the excluded seam pixels) is reported as `seam_adherence` and gated at 1.000
in `frame_ok`; a blended/resampled seam produces colours that exist in neither neighbouring slab.
"""
from __future__ import annotations

import numpy as np

METRIC = {"id": "M-003.d-iv@v2", "status": "advisory (additive; v1 stays the gate)", "threshold": 0.995,
          "block_tol_code": 1, "min_eval_area": 0.95, "seam_adherence_min": 1.0,
          "source": "research/R-M3-DIV-review.md section 5 (P-1)"}
THRESHOLD = METRIC["threshold"]


def slab_labels(depth, bounds):
    """Label per pixel from the capture depth (metres) and sidecar slab bounds [(near, far), ...]: index of the slab with
    near <= d < far (last slab closed at far), -1 if none."""
    lab = np.full(depth.shape, -1, np.int16)
    for i, (lo, hi) in enumerate(bounds):
        m = (depth >= lo) & ((depth < hi) | (depth <= hi if i == len(bounds) - 1 else False))
        lab[m & (lab < 0)] = i
    return lab


def _blocks(a, s, off):
    ox, oy = off
    r = a[oy:, ox:]   # crop (not roll): the partial strips left of/above the lattice and the partial edge blocks are excluded
    H, W = r.shape[0] // s * s, r.shape[1] // s * s
    return r[:H, :W].reshape(H // s, s, W // s, s, *r.shape[2:])


def _slab_blocks(out, lab, ui, s, off, k, tol):
    """(n_uniform, n_blocks, block_mask_full) for blocks fully inside slab k and outside UI at offset off."""
    L = _blocks(np.where(ui, -2, lab), s, off)
    inside = (L == k).all((1, 3))
    b = _blocks(out, s, off).astype(np.int16)
    spread = (b.max((1, 3)) - b.min((1, 3))).max(-1)
    return int((inside & (spread <= tol)).sum()), int(inside.sum()), inside


def _coverage(inside, s, off, shape):
    """Per-pixel boolean (rolled back) of pixels in counted blocks."""
    m = np.zeros(shape, bool)
    ix = np.kron(inside, np.ones((s, s), bool))
    ox, oy = off
    m[oy:oy + ix.shape[0], ox:ox + ix.shape[1]] = ix
    return m


def d_iv_v2(out, s, labels=None, declared=None, ui=None, tol=1, renderer_labels=None, min_eval_area=0.95,
            slab_min_pixels=1, offset=(0, 0)):
    """out (H,W,3) uint8 output frame; labels (H,W) capture-derived slab ids (None -> unlayered, v1 reading);
    declared {slab: (ox, oy)} per-slab output_block_offset; ui (H,W) bool mask. Returns a dict (see keys)."""
    H, W = out.shape[:2]
    ui = np.zeros((H, W), bool) if ui is None else ui
    if labels is None or declared is None:
        d, n, _ = _slab_blocks(out, np.zeros((H, W), np.int16), ui, s, offset, 0, tol)
        v = d / n if n else 0.0
        return {"version": "v1", "frame": v, "slabs": {}, "ok": v >= THRESHOLD, "eval_area": 1.0, "failures": [] if v >= THRESHOLD else ["frame"]}
    fails, slabs, tot_u, tot_n = [], {}, 0, 0
    cover = np.zeros((H, W), bool)
    for k in sorted(declared):
        if int((labels == k).sum()) < slab_min_pixels:
            continue
        dec = tuple(int(v) % s for v in declared[k])
        scores = {}
        for oy in range(s):
            for ox in range(s):
                u, n, _ = _slab_blocks(out, labels, ui, s, (ox, oy), k, tol)
                scores[(ox, oy)] = u / n if n else 0.0
        _, n_dec, ins = _slab_blocks(out, labels, ui, s, dec, k, tol)
        u_dec = int(round(scores[dec] * n_dec))
        best = max(scores.values())
        top = sorted(scores.values(), reverse=True)
        est = [o for o, v in scores.items() if abs(v - best) < 1e-12]
        ok_blind = scores[dec] >= best - 1e-12
        slabs[k] = {"declared": dec, "score": scores[dec], "blocks": n_dec, "best_offsets": est, "best_score": best,
                    "margin_vs_second": top[0] - next((v for v in top if v < top[0] - 1e-12), top[0]),
                    "blind_estimate_ok": ok_blind, "ambiguous": len(est) > 1 and ok_blind and len(est) == s * s}
        if not ok_blind: fails.append(f"slab {k}: blind estimate {est[0]} != declared {dec}")
        if scores[dec] < THRESHOLD: fails.append(f"slab {k}: score {scores[dec]:.4f} < {THRESHOLD}")
        tot_u += u_dec; tot_n += n_dec
        cover |= _coverage(ins, s, dec, (H, W))
    eval_area = float(cover.sum()) / max(1, int((~ui).sum()))
    if eval_area < min_eval_area: fails.append(f"evaluated area {eval_area:.3f} < {min_eval_area} (seam cap)")
    if renderer_labels is not None:
        bad = (renderer_labels != labels) & ~ui
        edge = np.zeros((H, W), bool)
        for sh in range(1, s + 1):
            for ax in (0, 1):
                for sg in (1, -1):
                    r = np.roll(labels, sg * sh, ax)
                    edge |= r != labels
        if (bad & ~edge).any(): fails.append("renderer labels disagree with capture labels away from slab boundaries")
    frame = tot_u / tot_n if tot_n else 0.0
    if frame < THRESHOLD: fails.append(f"frame {frame:.4f} < {THRESHOLD}")
    return {"version": "v2", "frame": frame, "slabs": slabs, "eval_area": eval_area, "ok": not fails, "failures": fails,
            "_cover": cover}


def seam_adherence(out, res, labels, ui=None):
    """A2-style seam check: share of non-UI pixels outside counted blocks (the seam zone) whose colour occurs inside the
    counted blocks of the frame. Resampled/blended seams create new colours -> < 1.0."""
    cover = res["_cover"]
    ui = np.zeros(cover.shape, bool) if ui is None else ui
    seam = ~cover & ~ui
    if not seam.any(): return 1.0
    pal = {tuple(c) for c in np.unique(out[cover].reshape(-1, out.shape[-1]), axis=0)}
    sc = out[seam].reshape(-1, out.shape[-1])
    return float(np.mean([tuple(c) in pal for c in sc]))


def frame_ok(out, s, labels, declared, **kw):
    r = d_iv_v2(out, s, labels, declared, **kw)
    if r["version"] == "v2":
        r["seam_adherence"] = seam_adherence(out, r, labels, kw.get("ui"))
        if r["seam_adherence"] < METRIC["seam_adherence_min"]:
            r["ok"] = False; r["failures"].append(f"seam adherence {r['seam_adherence']:.4f} < 1.000 (grid tearing)")
    r.pop("_cover", None)
    return r


def clip_value(frames):
    """Min over frames of the whole-frame value and of every slab (the gated numbers), plus all failures."""
    vals = [f["frame"] for f in frames] + [v["score"] for f in frames for v in f["slabs"].values()]
    return {"gate_value": min(vals) if vals else None, "ok": all(f["ok"] for f in frames),
            "failures": [x for f in frames for x in f["failures"]]}
