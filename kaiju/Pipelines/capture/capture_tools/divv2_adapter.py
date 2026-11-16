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


def internal_labels(rad, sky, boundaries):
    lab = np.zeros(rad.shape, np.int16)
    for b in boundaries:
        lab += (rad >= b).astype(np.int16)
    lab[sky] = len(boundaries)
    return lab


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


def adapt(sidecar, depth_png16, out_wh=None):
    """-> dict(labels, declared, k, lab_internal). declared None when the frame is not layered (v1 reading)."""
    cam = sidecar["camera"]
    k = sidecar["resolution"]["integer_scale"]
    declared = declared_offsets(sidecar)
    if declared is None:
        return {"labels": None, "declared": None, "k": k, "offset": tuple(sidecar["resolution"]["output_block_offset_px"])}
    z, sky = decode_depth(depth_png16, cam)
    lab_int = internal_labels(radial_m(z, cam), sky, sidecar["layers"]["boundaries_radial_m"])
    out_wh = out_wh or tuple(sidecar["resolution"]["output_wh"])
    return {"labels": output_labels(lab_int, declared, k, out_wh), "declared": declared, "k": k,
            "lab_internal": lab_int, "offset": tuple(sidecar["resolution"]["output_block_offset_px"])}


def evaluate_frame(sidecar, depth_png16, color_output):
    """Run divv2.frame_ok on a real frame. Also returns the seam share (1 - eval_area)."""
    a = adapt(sidecar, depth_png16, (color_output.shape[1], color_output.shape[0]))
    if a["declared"] is None:
        return divv2.frame_ok(color_output, a["k"], None, None, offset=a["offset"])
    return divv2.frame_ok(color_output, a["k"], labels=a["labels"], declared=a["declared"])


def load_frame(seq_dir, sidecar_name):
    """(sidecar, depth uint16 array, color_output uint8) for one frame json in a capture sequence dir."""
    import json
    import os
    sc = json.load(open(os.path.join(seq_dir, sidecar_name)))
    rd = lambda key: np.array(Image.open(os.path.join(seq_dir, sc["files"][key])))
    return sc, rd("depth"), rd("color_output")[..., :3]
