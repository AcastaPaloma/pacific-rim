"""T-196c: adapter from real style_capture@v1 frames (sidecar + decoded depth) to the d-iv@v2 inputs of divv2.py.

Sidecar fields used (verified on real T-196 sidecars, p1_d_strafe_t192 f000050):
  resolution.integer_scale k, resolution.internal_wh, resolution.output_block_offset_px (main slab, = (-sx mod k, sy mod k)),
  camera.near_m / far_m / hfov_deg / vfov_deg,
  layers.shift_output_px[slab] (whole output px applied per slab), layers.boundaries_radial_m (slab cut distances, RADIAL),
  layers.slab_clip_eye_m (eye-space clip ranges, overlapping; informational), layers.count, layers.slab_names.
Per-slab declared offset follows the capture writer (StyleCaptureSession): ox = (-sx) mod k, oy = sy mod k.
Depth: the capture `depth` png16_log decoded as z = near * (far/near) ** (v/65535) (eye-space, along forward); v = 65535 is sky
(labelled far slab, as the renderer's composite does). Radial distance = z * sqrt(1 + tx^2 + ty^2) with the per-pixel ray.
Labels are built per slab on that slab's OWN lattice (a texel of slab k covers output [k*i+ox_k, k*i+ox_k+k)), so a pixel
belongs to slab k iff the internal texel under slab k's lattice has radial label k; pixels claimed by no slab or by two slabs
are -1 (seam zone, excluded by divv2).
"""
from __future__ import annotations

import math

import numpy as np
from PIL import Image

from . import divv2


def decode_depth(depth_png16, camera):
    v = np.asarray(depth_png16).astype(np.float64)
    z = camera["near_m"] * (camera["far_m"] / camera["near_m"]) ** (v / 65535.0)
    return z, v >= 65535


def radial_m(z, camera):
    H, W = z.shape
    ys, xs = np.mgrid[0:H, 0:W]
    tv = math.tan(math.radians(camera["vfov_deg"]) / 2)
    th = math.tan(math.radians(camera["hfov_deg"]) / 2)
    ray = np.sqrt(1 + ((xs + 0.5) / W * 2 - 1) ** 2 * th * th + ((ys + 0.5) / H * 2 - 1) ** 2 * tv * tv)
    return z * ray


def internal_labels(rad, sky, boundaries, quantum=0.0):
    """Slab index per internal texel from the radial distance; sky = far slab. `quantum` is the relative uncertainty of the
    decoded depth (one step of the png16_log code, ln(far/near)/65535, see depth_quantum): a texel whose radial lies within
    quantum * boundary of a slab boundary cannot be assigned from the saved depth and is -1 (undecidable), not guessed."""
    lab = np.zeros(rad.shape, np.int16)
    unsure = np.zeros(rad.shape, bool)
    for b in boundaries:
        lab += (rad >= b).astype(np.int16)
        unsure |= np.abs(rad - b) <= quantum * b
    lab[unsure] = -1
    lab[sky] = len(boundaries)
    return lab


def gap_fill_ambiguous(lab, rad, sidecar, camera):
    """Texels whose slab cannot be told from the composite depth alone (T-196d). The composite (and the present) takes a gap, a texel no
    slab both covers and owns, from the covered slab with the SMALLEST radial distance, so a sample of slab i+1 inside slab i's range
    (the clip ranges overlap, layers.slab_clip_eye_m) can stand in for a slab i texel that slab i does not cover; its depth then reads
    as slab i. Only near a label edge (within one texel: a coverage difference between slab renders is sub-texel) and only inside the
    overlap of the two clip ranges can that happen: exactly those texels are undecidable (-1)."""
    from scipy import ndimage
    clips = sidecar["layers"]["slab_clip_eye_m"]
    b = sidecar["layers"]["boundaries_radial_m"]
    H, W = lab.shape
    ys, xs = np.mgrid[0:H, 0:W]
    tv = math.tan(math.radians(camera["vfov_deg"]) / 2); th = math.tan(math.radians(camera["hfov_deg"]) / 2)
    rayl = np.sqrt(1 + ((xs + 0.5) / W * 2 - 1) ** 2 * th * th + ((ys + 0.5) / H * 2 - 1) ** 2 * tv * tv)
    band = np.zeros(lab.shape, bool)
    for j, bj in enumerate(b):
        lo = clips[j + 1][0] * rayl                              # nearest radial slab j+1 can see
        hi = clips[j][1] * rayl                                  # farthest radial slab j can see
        band |= (rad >= lo) & (rad <= hi)
    mn = ndimage.minimum_filter(lab, size=3, mode="nearest"); mx = ndimage.maximum_filter(lab, size=3, mode="nearest")
    return band & ((mn != lab) | (mx != lab))


def depth_quantum(camera):
    """Relative error bound of a decoded depth: one step of the 16-bit log code (rounding is half a step; the full step also covers
    the float to code to float round trip and the radial reconstruction)."""
    return math.log(camera["far_m"] / camera["near_m"]) / 65535.0


def declared_offsets(sidecar):
    """{slab: (ox, oy)} from layers.shift_output_px; None when the frame is not layered."""
    L = sidecar.get("layers")
    if not L or L.get("count", 0) < 2:
        return None
    k = sidecar["resolution"]["integer_scale"]
    return {i: ((-sx) % k, sy % k) for i, (sx, sy) in enumerate(L["shift_output_px"])}


def output_labels(lab_int, declared, k, out_wh):
    """Per-slab own-lattice labels at output resolution; -1 where no slab or several claim the pixel."""
    W, H = out_wh
    claim = np.zeros((H, W), np.int16)
    lab = np.full((H, W), -1, np.int16)
    for s, (ox, oy) in declared.items():
        up = np.repeat(np.repeat(lab_int == s, k, 0), k, 1)          # texel i -> output [k*i, k*i+k)
        m = np.zeros((H, W), bool)
        m[:up.shape[0], :up.shape[1]] = up[:H, :W]
        m = np.roll(m, (oy, ox), (0, 1))                               # lattice starts at (ox, oy)
        lab[m] = s
        claim += m
    lab[claim != 1] = -1
    return lab


def presented_labels(lab_int, sidecar, out_wh):
    """Exact presented-frame slab labels (T-196d), from the renderer's own rule (PixelPresent.shader PixelLayeredColour).
    Output pixel (col c, image row r; the shader's o.y is bottom-up) samples slab i at internal texel
    (floor((c + sx_i) / k), floor((r - sy_i) / k)) (image rows; sx, sy = layers.shift_output_px, whole output pixels, the guard
    offset cancels because the capture internal image is the unpadded crop), and the first slab near to far that is covered and
    owned at ITS sampled texel wins. The capture depth is the zero-shift composite, whose label L(t) = first slab covered and
    owned at texel t, so: L(t_i) == i  <=> slab i is covered+owned there; L(t_i) > i proves slab i is not; L(t_i) < i (a nearer
    slab hides it) is undecidable from the composite and the pixel is left -1 (seam), as is a gap (nothing covered/owned at
    its texel, the shader's smallest-radial fallback) and a sample outside the internal image (clamped by the guard band).
    No guard, no tolerance: every presented pixel is mapped through the declared shifts."""
    k = sidecar["resolution"]["integer_scale"]
    W, H = out_wh
    h, w = lab_int.shape
    shifts = sidecar["layers"]["shift_output_px"]
    cols, rows = np.arange(W), np.arange(H)
    lab = np.full((H, W), -1, np.int16)
    undecided = np.ones((H, W), bool)
    for i, (sx, sy) in enumerate(shifts):
        tx = np.floor_divide(cols + sx, k)
        ty = np.floor_divide(rows - sy, k)
        inx = (tx >= 0) & (tx < w)
        iny = (ty >= 0) & (ty < h)
        L = lab_int[np.clip(ty, 0, h - 1)[:, None], np.clip(tx, 0, w - 1)[None, :]]
        inside = iny[:, None] & inx[None, :]
        own = undecided & inside & (L == i)
        lab[own] = i
        # a nearer slab hides slab i at its texel: later slabs' state is unknown for pixels where L < i; stop deciding them
        hidden = undecided & ~inside | undecided & (L < i)
        undecided = undecided & ~own & ~hidden
    return lab


def slab_id_labels(code):
    """T-196g: exact per-pixel slab labels from the renderer's slab-id channel (`files.slab_id`, the layered present's alpha; PixelPresent.shader).
    Code = 1 + slab, +4 when no slab was covered and owned at its own texel (the deterministic gap rule, smallest radial distance, chose), +8 when the
    chosen sample was clamped at the guard-band edge; 255 (or 0) = no slab information. Returns (labels int16, -1 = none; gap bool; clamped bool).
    Nothing is guessed from the composite depth: the label of every pixel is the slab that rendered it, gap fills included."""
    c = np.asarray(code).astype(np.int16)
    none = (c == 255) | (c == 0)
    lab = ((c & 3) - 1).astype(np.int16)
    lab[none] = -1
    return lab, ((c >> 2) & 1).astype(bool) & ~none, ((c >> 3) & 1).astype(bool) & ~none


def load_slab_id(seq_dir, sidecar):
    """Slab-id code image for a frame, or None when the capture has no slab-id channel (older captures)."""
    import os
    name = (sidecar.get("files") or {}).get("slab_id")
    if not name:
        return None
    path = os.path.join(seq_dir, name)
    return np.array(Image.open(path)) if os.path.exists(path) else None


def guard_labels(lab, guard_px):
    """Set label to -1 within guard_px (Chebyshev) of a pixel with another label: tolerance for the capture label boundary
    being displaced against the presented slab boundary (depth is not shifted with the slab's output shift)."""
    if guard_px <= 0:
        return lab
    from scipy import ndimage
    z = 2 * guard_px + 1
    lo = ndimage.minimum_filter(lab, size=z, mode="nearest"); hi = ndimage.maximum_filter(lab, size=z, mode="nearest")
    out = lab.copy(); out[(lo != lab) | (hi != lab)] = -1
    return out


def adapt(sidecar, depth_png16, out_wh=None, guard_px=0, mapping="presented", slab_id=None):
    """-> dict(labels, declared, k, lab_internal). declared None when the frame is not layered (v1 reading). With `slab_id` (the capture's
    slab-id channel, T-196g) the labels are exact and mapping/guess steps are skipped (`gap`, `clamped` masks returned too)."""
    cam = sidecar["camera"]
    k = sidecar["resolution"]["integer_scale"]
    declared = declared_offsets(sidecar)
    if declared is None:
        return {"labels": None, "declared": None, "k": k, "offset": tuple(sidecar["resolution"]["output_block_offset_px"])}
    if slab_id is not None:
        lab, gap, clamped = slab_id_labels(slab_id)
        return {"labels": guard_labels(lab, guard_px), "declared": declared, "k": k, "lab_internal": None, "gap": gap, "clamped": clamped,
                "offset": tuple(sidecar["resolution"]["output_block_offset_px"])}
    z, sky = decode_depth(depth_png16, cam)
    rad = radial_m(z, cam)
    lab_int = internal_labels(rad, sky, sidecar["layers"]["boundaries_radial_m"], depth_quantum(cam))
    if mapping == "presented":
        lab_int[gap_fill_ambiguous(lab_int, rad, sidecar, cam)] = -1
    out_wh = out_wh or tuple(sidecar["resolution"]["output_wh"])
    lab_out = output_labels(lab_int, declared, k, out_wh) if mapping == "lattice" else presented_labels(lab_int, sidecar, out_wh)
    return {"labels": guard_labels(lab_out, guard_px), "declared": declared, "k": k,
            "lab_internal": lab_int, "offset": tuple(sidecar["resolution"]["output_block_offset_px"])}


def evaluate_frame(sidecar, depth_png16, color_output, guard_px=0, mapping="presented", slab_id=None):
    """Run divv2.frame_ok on a real frame. Also returns the seam share (1 - eval_area)."""
    a = adapt(sidecar, depth_png16, (color_output.shape[1], color_output.shape[0]), guard_px, mapping, slab_id)
    if a["declared"] is None:
        return divv2.frame_ok(color_output, a["k"], None, None, offset=a["offset"])
    return divv2.frame_ok(color_output, a["k"], labels=a["labels"], declared=a["declared"])


def load_frame(seq_dir, sidecar_name):
    """(sidecar, depth uint16 array, color_output uint8) for one frame json in a capture sequence dir. The slab-id channel, when captured, is
    read with load_slab_id(seq_dir, sidecar)."""
    import json
    import os
    sc = json.load(open(os.path.join(seq_dir, sidecar_name)))
    rd = lambda key: np.array(Image.open(os.path.join(seq_dir, sc["files"][key])))
    return sc, rd("depth"), rd("color_output")[..., :3]


def layered_nn_match(sidecar, depth_png16, color_internal, color_output, slab_id=None):
    """T-196f: no-stretch (M-123) relation for a layered frame. The present stage shows slab i at its own shift, so one
    present_shift does not describe the frame (the single-shift relation reads 84-97 % on real layered clips). Slab i shows the internal
    texel (floor((c + sx_i) / k), floor((r - sy_i) / k)). Pixels whose slab is undecidable (presented_labels = -1, the d-iv@v2 seam set,
    same cap) are not compared. Every other pixel must equal the nearest-neighbour sample of the internal image under SOME slab's shift
    (a stretch or filter produces colours no slab shift reproduces); the stricter share, equal under its own label, is returned too
    (a composite label can be wrong by one texel at a boundary: seen once in 1080 frames).
    Returns (any-slab match share over decidable pixels, own-label match share, undecidable share)."""
    a = adapt(sidecar, depth_png16, (color_output.shape[1], color_output.shape[0]), slab_id=slab_id)
    k, shifts = a["k"], sidecar["layers"]["shift_output_px"]
    lab = a["labels"]
    vouch = None
    if slab_id is not None:
        # T-196g: the renderer names the slab of every pixel, so nothing about the label is guessed. What the zero-shift composite (the saved internal
        # image) can vouch for is a different question: slab i's sample at its texel appears in the internal image only where the composite also picked
        # slab i there. Excluded as undecidable (same 5 % cap, reported in the undecidable share): gap fills (shown from one slab's own target, deterministic
        # smallest-radial rule), samples clamped at the guard band (no internal texel), and texels where the composite shows a nearer slab (depth label
        # below i, or too close to a slab boundary for the depth code to tell). Every other pixel is compared exactly.
        lab = np.where(a["gap"] | a["clamped"], -1, lab).astype(np.int16)
        cam = sidecar["camera"]
        z, sky = decode_depth(depth_png16, cam)
        vouch = internal_labels(radial_m(z, cam), sky, sidecar["layers"]["boundaries_radial_m"], depth_quantum(cam))
    H, W = lab.shape
    ih, iw = color_internal.shape[:2]
    ci, co = color_internal[..., :3], color_output[..., :3]
    own = np.zeros(lab.shape, bool)
    anyslab = np.zeros(lab.shape, bool)
    for i, (sx, sy) in enumerate(shifts):
        ry, rx = np.floor_divide(np.arange(H) - sy, k), np.floor_divide(np.arange(W) + sx, k)
        ty, tx = np.clip(ry, 0, ih - 1), np.clip(rx, 0, iw - 1)
        eq = (ci[ty][:, tx] == co).all(-1)
        if vouch is not None:
            vi = (vouch[ty][:, tx] == i) & (((ry >= 0) & (ry < ih))[:, None] & ((rx >= 0) & (rx < iw))[None, :])   # guard-band texels are not in the internal image
            lab = np.where((lab == i) & ~vi, -1, lab).astype(np.int16)
        anyslab |= eq
        own |= eq & (lab == i)
    dec = lab >= 0
    if not dec.any():
        return 0.0, 0.0, 1.0
    return float(anyslab[dec].mean()), float(own[dec].mean()), float(1.0 - dec.mean())
