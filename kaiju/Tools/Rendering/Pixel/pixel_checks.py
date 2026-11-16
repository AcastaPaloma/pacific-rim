#!/usr/bin/env python
"""T-004 evidence checks that the T-002 metric tools do not cover (run with the kaiju conda env).

Every check reads style_capture@v1 capture sets written by the Unity capture tool and prints / stores numbers; nothing
here changes a capture. Sub-commands:
  smooth <seq_on> <seq_off>      C11 (M-011): an object edge in color_output follows the commanded camera motion
  cut <capture_set>              C13: the first frame after a cut equals a static capture of the new pose
  identical <dirA> <dirB>        C19: two capture runs are bit-identical (every PNG; sidecars apart from nothing)
  grid <sequence_dir>            C2 control: grid integrity with the declared block offset and with it ignored
  scale <sequence_dir>           C7: marker and 80 m block legibility in the scale view
  layers <sequence_dir>          C6: layers at 1, 3, 10, 30 km, sky seam, near field darkest, seam pixels
  depth <sequence_dir>           C19: depth PNG decodes to the known scene distances within 1 %
  blocks <sequence_dir>          C7: each 80 m block against its background over every frame of a sequence
All commands take --out <json> to store the result.
"""
import argparse, glob, hashlib, json, math, os, sys
import numpy as np
from PIL import Image
from skimage.color import rgb2lab


def load_seq(d):
    meta = json.load(open(os.path.join(d, "sequence.json")))
    frames = [json.load(open(os.path.join(d, n))) for n in meta["frames"]]
    return meta, frames


def img(d, f, key):
    return np.array(Image.open(os.path.join(d, f["files"][key])))


def depth_m(d, f):
    v = img(d, f, "depth").astype(np.float64)
    c = f["camera"]
    z = c["near_m"] * (c["far_m"] / c["near_m"]) ** (v / 65535.0)
    return z, v >= 65535


def lightness(rgb):
    return rgb2lab(rgb[..., :3] / 255.0)[..., 0]


def quat_rotate_inv(q, v):
    x, y, z, w = q
    # inverse rotation: conjugate
    qv = np.array([-x, -y, -z]); t = 2 * np.cross(qv, v)
    return v + w * t + np.cross(qv, t)


def euler_basis(yaw_deg, pitch_down_deg):
    """Right, up, forward for Unity Euler (pitch about +X positive down, yaw about +Y)."""
    y, p = math.radians(yaw_deg), math.radians(-pitch_down_deg)
    f = np.array([math.sin(y) * math.cos(p), math.sin(p), math.cos(y) * math.cos(p)])
    r = np.array([math.cos(y), 0.0, -math.sin(y)])
    return r, np.cross(f, r), f


def project_wanted(f, world):
    """Output-pixel x (top-left origin) of a world point seen from the WANTED (continuous) pose of a frame."""
    c = f["camera"]; res = f["resolution"]
    r, u, fw = euler_basis(c["wanted_yaw_pitch_roll_deg"][0], c["wanted_yaw_pitch_roll_deg"][1])
    v = np.asarray(world, float) - np.asarray(c["wanted_position_local_m"], float)
    x, y, z = v @ r, v @ u, v @ fw
    th = math.tan(math.radians(c["hfov_deg"]) / 2); tv = math.tan(math.radians(c["vfov_deg"]) / 2)
    W, H = res["output_wh"]
    return (x / z / th * 0.5 + 0.5) * W, (0.5 - y / z / tv * 0.5) * H


TOWER_EDGE = (310.0, 120.0, 440.0)   # left silhouette edge of Tower_600m seen from the eye70_tower viewpoint (its far-left corner), 120 m up


DEPTH_CLASS_POINTS = {"near": (-15.0, 60.0, -10.0), "reference": TOWER_EDGE, "far": (4000.0, 700.0, 9400.0)}
DEPTH_EDGE_SEARCH_TEXELS = 24
DEPTH_EDGE_MIN_REL_JUMP = 0.03   # a depth discontinuity is a >3 % change of eye depth between horizontally adjacent texels


def depth_edge_output_x(d, f, world):
    """x (output px) of the depth discontinuity nearest to the projected wanted position of `world`, along its row, from the depth PNG
    (internal resolution): scale * texel boundary - present_shift_output_px.x. Returns (measured_x, projected_x) or None when the point or
    every discontinuity within the search window is off-screen / absent."""
    k = f["resolution"]["integer_scale"]; W, H = f["resolution"]["output_wh"]
    px, py = project_wanted(f, world)
    if not (0 <= px < W and 0 <= py < H) or euler_basis(*f["camera"]["wanted_yaw_pitch_roll_deg"][:2])[2] @ (np.asarray(world, float) - np.asarray(f["camera"]["wanted_position_local_m"], float)) <= 0: return None
    z, sky = depth_m(d, f)
    z = np.where(sky, f["camera"]["far_m"] * 4.0, z)
    row = min(max(int(py // k), 0), z.shape[0] - 1); cx = px / k
    x0, x1 = max(int(cx) - DEPTH_EDGE_SEARCH_TEXELS, 1), min(int(cx) + DEPTH_EDGE_SEARCH_TEXELS + 1, z.shape[1])
    if x1 <= x0: return None
    r = z[row]; rel = np.abs(r[x0:x1] - r[x0 - 1:x1 - 1]) / np.minimum(r[x0:x1], r[x0 - 1:x1 - 1])
    cand = np.where(rel > DEPTH_EDGE_MIN_REL_JUMP)[0] + x0     # boundary i lies between texels i-1 and i
    if len(cand) == 0: return None
    i = cand[np.argmin(np.abs(cand - cx))]
    shift = f["camera"]["snap"].get("present_shift_output_px", [0, 0])[0]
    return float(k * i - shift), float(px)


def edge_series(d):
    """Measured x (output px) of the tower's left silhouette edge in color_output, per frame, and the commanded x."""
    meta, frames = load_seq(d)
    meas, cmd = [], []
    dseries = {k: {"meas": [], "cmd": []} for k in DEPTH_CLASS_POINTS}; skips = {k: 0 for k in DEPTH_CLASS_POINTS}
    for f in frames:
        out = img(d, f, "color_output"); L = lightness(out)
        px, py = project_wanted(f, TOWER_EDGE)
        y0, y1 = int(py) - 40, int(py) + 40
        x0, x1 = int(px) - 48, int(px) + 48
        if x0 < 0 or x1 > L.shape[1] or y0 < 0 or y1 > L.shape[0]: raise SystemExit("tower edge is outside the frame in " + d)
        band = L[y0:y1, x0:x1]
        # Column profile over 80 rows (averages any dither), then the sub-pixel position where it crosses the midpoint
        # between the background plateau on the left and the tower plateau on the right.
        prof = band.mean(axis=0)
        lo, hi = np.median(prof[-16:]), np.median(prof[:16])
        mid = 0.5 * (lo + hi)
        below = np.where(prof < mid)[0]
        i = int(below[0]) if len(below) else len(prof) - 1
        edge = i - (mid - prof[i]) / (prof[i - 1] - prof[i]) if i > 0 and prof[i - 1] != prof[i] else float(i)
        meas.append(x0 + float(edge)); cmd.append(px)
        for cls, w in DEPTH_CLASS_POINTS.items():
            dm = depth_edge_output_x(d, f, w)
            if dm is None: skips[cls] += 1
            else: dseries[cls]["meas"].append(dm[0]); dseries[cls]["cmd"].append(dm[1])
    meta = dict(meta); meta["_depth_classes"] = {"series": {k: {"meas": np.array(v["meas"]), "cmd": np.array(v["cmd"])} for k, v in dseries.items()}, "skipped_frames": skips}
    return np.array(meas), np.array(cmd), meta


def cmd_smooth(a):
    res = {}
    for label, d in (("subpixel_on", a.seq_on), ("subpixel_off", a.seq_off)):
        m, c, meta = edge_series(d)
        dev = (m - m[0]) - (c - c[0])            # measured minus commanded displacement, output px
        step_err = np.diff(m) - np.diff(c)       # per-frame displacement error
        res[label] = {"sequence": os.path.relpath(d), "motion": meta["motion"], "frames": len(m),
                      "commanded_px_per_frame": float(np.mean(np.diff(c))), "measured_px_per_frame": float(np.mean(np.diff(m))),
                      "max_abs_displacement_error_px": float(np.abs(dev).max()),
                      "max_abs_step_error_px": float(np.abs(step_err).max()),
                      "max_step_px": float(np.abs(np.diff(m)).max()),
                      "pass_within_1px": bool(np.abs(dev).max() <= 1.0 + 0.5 and np.abs(step_err).max() <= 1.0)}
        dc = meta["_depth_classes"]
        for cls in ("near", "reference", "far"):
            sr = dc["series"][cls]
            if len(sr["meas"]) == 0:
                res[label]["depth_class_" + cls] = {"frames_measured": 0, "frames_skipped": dc["skipped_frames"][cls], "pass_within_1px": None}; continue
            err = np.abs(sr["meas"] - sr["cmd"])
            res[label]["depth_class_" + cls] = {"frames_measured": int(len(err)), "frames_skipped": dc["skipped_frames"][cls], "max_abs_error_px": float(err.max()), "p95_abs_error_px": float(np.percentile(err, 95)), "pass_within_1px": bool(err.max() <= 1.0)}
        res[label]["pass_all_depth_classes_within_1px"] = bool(all(res[label]["depth_class_" + c]["pass_within_1px"] for c in ("near", "reference", "far")))
    res["depth_class_criterion"] = "DEF-T004-style-3: per depth class, output x of the nearest depth discontinuity (scale * texel boundary - present_shift_output_px) against the projected wanted position of the near (-15,60,-10), reference (TOWER_EDGE) and far (4000,700,9400) points, max |error| <= 1 output px; frames where the point is off-screen or no discontinuity lies within %d texels are skipped and counted" % DEPTH_EDGE_SEARCH_TEXELS
    res["criterion"] = "C11 / M-011: per-frame color_output displacement of a scene edge matches the commanded motion within +-1 output px (cumulative deviation from the first frame allowed 1.5 px: 1 px plus the half-pixel of edge rounding)"
    return res


def cmd_cut(a):
    d1, d2 = os.path.join(a.capture_set, "moving_then_cut"), os.path.join(a.capture_set, "static_at_new_pose")
    _, f1 = load_seq(d1); _, f2 = load_seq(d2)
    last, static = f1[-1], f2[0]
    res = {"cut_frame": os.path.join(os.path.relpath(d1), "f%06d" % last["frame"]["index"]), "static_frame": os.path.relpath(d2), "cut_flag": last["camera"]["snap"]["cut"]}
    same = True
    for k in ("color_internal", "color_output", "palette_index", "depth"):
        eq = bool(np.array_equal(img(d1, last, k), img(d2, static, k)))
        res["identical_" + k] = eq; same &= eq
    res["identical_pose"] = last["camera"]["position_local_m"] == static["camera"]["position_local_m"] and last["camera"]["rotation_xyzw"] == static["camera"]["rotation_xyzw"]
    res["identical_offsets"] = last["resolution"] == static["resolution"] and last["camera"]["snap"]["dither_origin_texels"] == static["camera"]["snap"]["dither_origin_texels"]
    # The frame before the cut must differ (so the comparison is not trivially true).
    res["differs_from_previous_frame"] = not np.array_equal(img(d1, f1[-2], "color_internal"), img(d1, last, "color_internal"))
    res["pass"] = bool(same and res["identical_pose"] and res["identical_offsets"] and res["differs_from_previous_frame"])
    return res


def cmd_identical(a):
    """Every PNG and sidecar of the re-run (dir_b) must exist byte-identical in the first run (dir_a)."""
    pngs = sorted(glob.glob(os.path.join(a.dir_b, "**", "*.png"), recursive=True))
    jsons = sorted(glob.glob(os.path.join(a.dir_b, "**", "*.json"), recursive=True))
    diff = []
    for p in pngs + jsons:
        rel = os.path.relpath(p, a.dir_b); q = os.path.join(a.dir_a, rel)
        if not os.path.isfile(q) or hashlib.sha256(open(p, "rb").read()).digest() != hashlib.sha256(open(q, "rb").read()).digest(): diff.append(rel)
    return {"first_run": os.path.relpath(a.dir_a), "second_run": os.path.relpath(a.dir_b), "png_files": len(pngs), "json_files": len(jsons), "differing": len(diff), "first_differing": diff[:5],
            "pass": bool(len(pngs) > 0 and not diff), "criterion": "C19: two runs of the same capture are bit-identical (images, sidecars and sequence.json)"}


def grid_share(out, s, ox, oy):
    H, W = out.shape[:2]
    x0 = ox % s; y0 = oy % s
    nx, ny = (W - x0) // s, (H - y0) // s
    b = out[y0:y0 + ny * s, x0:x0 + nx * s, :3].reshape(ny, s, nx, s, 3).astype(int)
    rng = b.max(axis=(1, 3)) - b.min(axis=(1, 3))
    return float((rng.max(axis=-1) <= 1).mean())


def cmd_grid(a):
    _, frames = load_seq(a.sequence_dir); rows = []
    for f in frames:
        out = img(a.sequence_dir, f, "color_output"); r = f["resolution"]; s = r["integer_scale"]; ox, oy = r["output_block_offset_px"]
        rows.append({"frame": f["frame"]["index"], "scale": s, "declared_offset": [ox, oy], "uniform_blocks_declared": grid_share(out, s, ox, oy),
                     "uniform_blocks_offset_ignored": grid_share(out, s, 0, 0), "subpixel_offset_internal_px": r["subpixel_offset_internal_px"]})
    return {"sequence": os.path.relpath(a.sequence_dir), "frames": rows, "pass": bool(all(x["uniform_blocks_declared"] >= 0.995 for x in rows)),
            "criterion": "M-003 d-iv@v1: >= 99.5 % of s x s output blocks uniform at the declared offset",
            "d_iv_v2": "not computed here (layered frames need capture depth + sidecar slabs): see Pipelines/capture/capture_tools/divv2.py; the capture style path (capture_tools.style.divv2_set) computes it automatically for layered sequences; this command stays the v1 reading"}


def project_rendered(f, world):
    c = f["camera"]; res = f["resolution"]
    v = quat_rotate_inv(c["rotation_xyzw"], np.asarray(world, float) - np.asarray(c["position_local_m"], float))
    th = math.tan(math.radians(c["hfov_deg"]) / 2); tv = math.tan(math.radians(c["vfov_deg"]) / 2)
    W, H = res["internal_wh"]
    return (v[0] / v[2] / th * 0.5 + 0.5) * W, (0.5 - v[1] / v[2] / tv * 0.5) * H, v[2]


def cmd_scale(a):
    _, frames = load_seq(a.sequence_dir); f = frames[0]; d = a.sequence_dir
    rgb = img(d, f, "color_internal"); L = lightness(rgb); z, sky = depth_m(d, f); idx = img(d, f, "palette_index")
    mx, my, mz = project_rendered(f, (-60.0, 0.9, 447.0))        # Marker1p8_Far
    x0, y0 = int(round(mx)) - 6, int(round(my)) - 6
    win_z = z[y0:y0 + 12, x0:x0 + 12]; win_L = L[y0:y0 + 12, x0:x0 + 12]; win_i = idx[y0:y0 + 12, x0:x0 + 12]
    # Marker pixels: the pixels of the window whose depth is the marker's (in front of the ground behind it).
    ground_z = np.median(win_z)
    marker = np.abs(win_z - mz) < 0.02 * mz
    marker &= np.abs(win_L - np.median(win_L)) > 3
    n = int(marker.sum())
    res = {"frame": os.path.relpath(d), "marker_distance_m": float(mz), "marker_pixels": n}
    if n:
        surround = ~marker
        res["marker_L"] = float(win_L[marker].mean()); res["surround_L"] = float(np.median(win_L[surround]))
        res["marker_dL"] = float(abs(res["marker_L"] - res["surround_L"]))
        res["marker_index"] = [int(v) for v in np.unique(win_i[marker])]; res["surround_index"] = [int(v) for v in np.unique(win_i[surround])][:6]
        ys = np.where(marker.any(axis=1))[0]; res["marker_height_px"] = int(ys.max() - ys.min() + 1)
    bx, by_top, bz = project_rendered(f, (220.0, 80.0, 850.0)); _, by_bot, _ = project_rendered(f, (220.0, 0.0, 850.0))
    res["block80_distance_m"] = float(bz); res["block80_height_px"] = float(by_bot - by_top)
    blk = (np.abs(z - bz) < 0.03 * bz) & (np.abs(np.arange(z.shape[1])[None, :] - bx) < 5) & (np.arange(z.shape[0])[:, None] > by_top + 2) & (np.arange(z.shape[0])[:, None] < by_bot - 2)
    ring = (np.abs(np.arange(z.shape[1])[None, :] - bx) < 16) & (np.arange(z.shape[0])[:, None] > by_top + 2) & (np.arange(z.shape[0])[:, None] < by_bot - 8) & ~(np.abs(z - bz) < 0.03 * bz) & ((z > bz * 1.1) | sky)
    if blk.any() and ring.any():
        res["block80_L"] = float(L[blk].mean()); res["block80_background_L"] = float(L[ring].mean()); res["block80_dL"] = float(abs(res["block80_L"] - res["block80_background_L"]))
    res["pass"] = bool(1 <= n and res.get("marker_height_px", 0) in (1, 2) and res.get("marker_dL", 0) >= 10 and res.get("block80_height_px", 0) >= 10 + res.get("marker_height_px", 0))
    res["criterion"] = "C7: the 1.8 m marker at 600 m is 1-2 internal pixels with dL* >= 10 to its surround; the 80 m block at about 1 km is at least 10 px taller than the marker (block contrast is judged over twelve views by the `blocks` check)"
    return res


def ground_mask(z, sky, f):
    """Texels of the ground plane (reconstructed height within 0.5 m of zero, and the texel above too)."""
    c = f["camera"]; H, W = z.shape
    ys = np.arange(H)[:, None]
    tv = math.tan(math.radians(c["vfov_deg"]) / 2)
    pitch = math.radians(-c["yaw_pitch_roll_deg"][1])
    height = c["position_local_m"][1] + z * (math.sin(pitch) + (1 - (ys + 0.5) / H * 2) * tv * math.cos(pitch))
    g = (~sky) & (np.abs(height) < 0.5)
    g[1:, :] &= g[:-1, :].copy()
    return g, height


def seam_purity(idx, L, ground, dist):
    """Seam purity on the ground (DEF-T004-style-1): every 4x4 window that lies wholly on the ground must hold at most
    two palette indices, and two only if they are neighbours in the lightness order of the indices the ground uses.
    Also: the most common index per distance bin must not get darker with distance (DEF-T004-style-6)."""
    from numpy.lib.stride_tricks import sliding_window_view
    res = {"ground_texels": int(ground.sum())}
    if ground.sum() < 200: return res
    used = np.unique(idx[ground])
    meanL = {int(i): float(L[ground & (idx == i)].mean()) for i in used}
    order = sorted(meanL, key=lambda i: meanL[i]); rank = np.full(256, -1); rank[order] = np.arange(len(order))
    w_idx = sliding_window_view(idx, (4, 4)).reshape(idx.shape[0] - 3, idx.shape[1] - 3, 16)
    w_gnd = sliding_window_view(ground, (4, 4)).reshape(idx.shape[0] - 3, idx.shape[1] - 3, 16).all(axis=2)
    srt = np.sort(w_idx, axis=2); distinct = 1 + (np.diff(srt, axis=2) != 0).sum(axis=2)
    r = rank[w_idx]; rmin, rmax = r.min(axis=2), r.max(axis=2)
    mixed = w_gnd & (distinct >= 2)
    bad = w_gnd & ((distinct >= 3) | ((distinct == 2) & (rmax - rmin != 1)))
    def cover(win):   # texels covered by at least one flagged window
        m = np.zeros(idx.shape, bool)
        for dy in range(4):
            for dx in range(4): m[dy:dy + win.shape[0], dx:dx + win.shape[1]] |= win
        return m
    seam_tex, bad_tex = cover(mixed) & ground, cover(bad) & ground
    res["seam_texels"] = int(seam_tex.sum()); res["seam_share_of_ground"] = float(seam_tex.sum() / ground.sum())
    res["impure_seam_texels"] = int(bad_tex.sum())
    res["pure_share_of_seam_texels"] = float(1.0 - bad_tex.sum() / max(1, seam_tex.sum()))
    res["three_or_more_index_window_share_of_ground"] = float((cover(w_gnd & (distinct >= 3)) & ground).sum() / ground.sum())
    # Most common index per log-distance bin (20 bins from 30 m to 30 km); its lightness must not fall with distance.
    edges = np.logspace(math.log10(30), math.log10(30000), 21); modal = []
    for k in range(20):
        sel = ground & (dist >= edges[k]) & (dist < edges[k + 1])
        if sel.sum() >= 30:
            vals, cnt = np.unique(idx[sel], return_counts=True); modal.append(int(vals[cnt.argmax()]))
    ml = [meanL[i] for i in modal]
    res["modal_index_by_distance"] = modal; res["modal_lightness_never_falls"] = bool(all(b >= a - 0.01 for a, b in zip(ml[:-1], ml[1:])))
    return res


def cmd_layers(a):
    _, frames = load_seq(a.sequence_dir); d = a.sequence_dir; rows = []
    for f in frames[: a.max_frames]:
        rgb = img(d, f, "color_internal"); L = lightness(rgb); z, sky = depth_m(d, f); idx = img(d, f, "palette_index")
        c = f["camera"]; H, W = z.shape
        ys, xs = np.mgrid[0:H, 0:W]
        tv = math.tan(math.radians(c["vfov_deg"]) / 2); th = math.tan(math.radians(c["hfov_deg"]) / 2)
        ray = np.sqrt(1 + ((xs + 0.5) / W * 2 - 1) ** 2 * th * th + ((ys + 0.5) / H * 2 - 1) ** 2 * tv * tv)
        dist = z * ray
        # World height of each pixel, to pick the upright layer faces (not the ground in front of them).
        pitch = math.radians(-c["yaw_pitch_roll_deg"][1])
        vy = (1 - (ys + 0.5) / H * 2) * tv
        height = c["position_local_m"][1] + z * (math.sin(pitch) + vy * math.cos(pitch))
        upright = (~sky) & (height > 25)
        layers = {}
        for name, lo, hi in (("near_150m", 100, 220), ("1km", 850, 1250), ("3km", 2600, 3500), ("10km", 9000, 11500), ("30km", 27000, 33000)):
            sel = upright & (dist >= lo) & (dist < hi)
            if sel.sum() >= 20:
                layers[name] = {"pixels": int(sel.sum()), "mean_L": float(L[sel].mean()), "L_p10_p90": float(np.percentile(L[sel], 90) - np.percentile(L[sel], 10)),
                                "indices": [int(v) for v in np.unique(idx[sel])]}
        row = {"frame": f["frame"]["index"], "layers": layers}
        order = [k for k in ("near_150m", "1km", "3km", "10km", "30km") if k in layers]
        means = [layers[k]["mean_L"] for k in order]
        row["successively_lighter"] = bool(all(b > a_ for a_, b in zip(means[:-1], means[1:])))
        # The horizon: sky pixels in the rows just above the highest far-layer pixel.
        if "30km" in layers:
            sel30 = upright & (dist >= 27000) & (dist < 33000)
            hz_rows = np.where(sel30.any(axis=1))[0]
            horizon_sky = sky & (ys >= hz_rows.min() - 4) & (ys <= hz_rows.max())
            if horizon_sky.any():
                row["sky_at_horizon_L"] = float(L[horizon_sky].mean()); row["far_layer_minus_sky_L"] = float(layers["30km"]["mean_L"] - row["sky_at_horizon_L"])
                row["far_layer_indices_subset_of_horizon_sky"] = bool(set(layers["30km"]["indices"]) <= set(int(v) for v in np.unique(idx[horizon_sky])))
        near = (~sky) & (dist < 150)
        if near.any():
            row["near_field_mean_L"] = float(L[near].mean()); row["rest_min_band_mean_L"] = float(min(means[1:])) if len(means) > 1 else None
            row["near_field_darkest"] = bool(row["near_field_mean_L"] <= min(means[1:]) if len(means) > 1 else True)
        gmask, _ = ground_mask(z, sky, f)
        row["seams"] = seam_purity(idx, L, gmask, dist)
        rows.append(row)
    seam_tex = sum(r["seams"].get("seam_texels", 0) for r in rows); impure = sum(r["seams"].get("impure_seam_texels", 0) for r in rows)
    pure = 1.0 - impure / max(1, seam_tex)
    return {"sequence": os.path.relpath(d), "frames": rows, "seam_texels": seam_tex, "impure_seam_texels": impure, "pure_share_of_seam_texels": pure,
            "modal_lightness_never_falls_frames": sum(1 for r in rows if r["seams"].get("modal_lightness_never_falls", True)),
            "pass": bool(all(r["successively_lighter"] and r.get("near_field_darkest", True) and r["seams"].get("modal_lightness_never_falls", True) for r in rows) and pure >= 0.99),
            "criterion": "C6: upright layers at 1, 3, 10 and 30 km are successively lighter; the farthest matches the sky at the horizon; the near field is darkest; at least 99 % of ground seam texels lie in 4x4 windows of two palette indices that are lightness neighbours (DEF-T004-style-1); the most common ground index never gets darker with distance (DEF-T004-style-6)"}


def cmd_depth(a):
    _, frames = load_seq(a.sequence_dir); d = a.sequence_dir; f = frames[min(a.frame, len(frames) - 1)]
    z, sky = depth_m(d, f); rows = []
    # Known scene points (StyleTestScenes): points on box faces that are visible from the central eye70 station.
    for name, pt in (("Block80_Near front face", (0.0, 60.0, -10.0)), ("Block80_Far front face", (220.0, 60.0, 840.0)), ("Layer_3km_B front face", (900.0, 300.0, 3000.0)),
                     ("Layer_10km_A front face", (3900.0, 750.0, 9400.0)), ("Layer_30km_B front face", (9000.0, 4000.0, 28500.0))):
        x, y, ez = project_rendered(f, pt)
        xi, yi = int(x), int(y)
        if 0 <= xi < z.shape[1] and 0 <= yi < z.shape[0] and ez > 0:
            got = float(z[yi, xi]); rows.append({"point": name, "expected_eye_depth_m": float(ez), "decoded_m": got, "relative_error": abs(got - ez) / ez})
    return {"frame": os.path.relpath(d) + "/f%06d" % f["frame"]["index"], "points": rows, "sky_pixels": int(sky.sum()), "pass": bool(len(rows) >= 4 and all(r["relative_error"] <= 0.01 for r in rows)),
            "criterion": "C19: depth PNG16 (log) decodes to known scene distances within 1 %"}


def block_contrast(L, z, sky, f, top, bottom, half_width_m=15.0):
    """Mean L* of an 80 m block and of what is seen beside it at a greater distance (farther surfaces and sky), or None if out of view."""
    bx, by_top, bz = project_rendered(f, top); _, by_bot, _ = project_rendered(f, bottom)
    H, W = z.shape
    if bz <= 0: return None
    hw = half_width_m / bz / math.tan(math.radians(f["camera"]["hfov_deg"]) / 2) * 0.5 * W   # half width of the block in texels
    if bx - hw < 30 or bx + hw > W - 30 or by_top < 2 or min(by_bot, H) - by_top < 8: return None
    xs = np.arange(W)[None, :]; ys = np.arange(H)[:, None]
    rows_ = (ys > by_top + 2) & (ys < min(by_bot, H) - 2)
    on = np.abs(z - bz) < 0.06 * bz
    blk = on & (np.abs(xs - bx) < max(1.0, hw - 2)) & rows_
    farther = (z > bz * 1.15) | sky
    ring = (np.abs(xs - bx) > hw + 1) & (np.abs(xs - bx) < hw + 25) & rows_ & farther
    ring3 = (np.abs(xs - bx) > hw + 0.5) & (np.abs(xs - bx) < hw + 3.5) & rows_ & farther   # three texels beside the silhouette
    if blk.sum() < 10 or ring.sum() < 10 or ring3.sum() < 6: return None
    return {"distance_m": float(bz), "height_px": float(by_bot - by_top), "block_L": float(L[blk].mean()), "background_L": float(L[ring].mean()), "dL": float(abs(L[blk].mean() - L[ring].mean())),
            "background3_L": float(L[ring3].mean()), "dL_ring3": float(abs(L[blk].mean() - L[ring3].mean()))}


def cmd_blocks(a):
    """C7: each 80 m block against what is behind it, over every frame of a sequence where it is in view."""
    _, frames = load_seq(a.sequence_dir); d = a.sequence_dir; out = {"near_150m": [], "far_1km": []}
    for f in frames:
        L = lightness(img(d, f, "color_internal")); z, sky = depth_m(d, f)
        for key, top, bottom in (("near_150m", (0.0, 80.0, -10.0), (0.0, 0.0, -10.0)), ("far_1km", (220.0, 80.0, 840.0), (220.0, 0.0, 840.0))):
            r = block_contrast(L, z, sky, f, top, bottom)
            if r: r["frame"] = f["frame"]["index"]; out[key].append(r)
    res = {"sequence": os.path.relpath(d)}
    ok = True
    for key, rows in out.items():
        dl = [r["dL_ring3"] for r in rows]; wide = [r["dL"] for r in rows]
        res[key] = {"frames_in_view": len(rows), "dL_min": min(dl) if dl else None, "dL_median": float(np.median(dl)) if dl else None, "frames_dL_ge_4": sum(1 for v in dl if v >= 4.0),
                    "wide_ring_dL_min": min(wide) if wide else None, "wide_ring_dL_median": float(np.median(wide)) if wide else None, "wide_ring_frames_dL_ge_4": sum(1 for v in wide if v >= 4.0), "frames": rows}
        ok &= bool(dl) and min(dl) >= 4.0
    res["pass"] = bool(ok)
    res["criterion"] = "C7: the 80 m block differs from its background by at least one value band at about 150 m and 1 km; one value band is read as 4 L* (M-003 d-i merges levels closer than 4 L*). dL = block face against the three texels beside its silhouette that show strictly farther surfaces or sky (the evaluator's measure); wide_ring = the same over 25 texels"
    return res


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("smooth"); p.add_argument("seq_on"); p.add_argument("seq_off")
    p = sub.add_parser("cut"); p.add_argument("capture_set")
    p = sub.add_parser("identical"); p.add_argument("dir_a"); p.add_argument("dir_b")
    p = sub.add_parser("grid"); p.add_argument("sequence_dir")
    p = sub.add_parser("scale"); p.add_argument("sequence_dir")
    p = sub.add_parser("layers"); p.add_argument("sequence_dir"); p.add_argument("--max-frames", type=int, default=12)
    p = sub.add_parser("depth"); p.add_argument("sequence_dir"); p.add_argument("--frame", type=int, default=5)
    p = sub.add_parser("blocks"); p.add_argument("sequence_dir")
    for q in sub.choices.values(): q.add_argument("--out")
    a = ap.parse_args()
    res = {"smooth": cmd_smooth, "cut": cmd_cut, "identical": cmd_identical, "grid": cmd_grid, "scale": cmd_scale, "layers": cmd_layers, "depth": cmd_depth, "blocks": cmd_blocks}[a.cmd](a)
    res["tool"] = "Tools/Rendering/Pixel/pixel_checks.py " + a.cmd
    text = json.dumps(res, indent=1)
    if a.out:
        os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
        open(a.out, "w").write(text + "\n")
    summary = {k: v for k, v in res.items() if k in ("pass", "png_files", "json_files", "differing")} if a.cmd != "smooth" else {k: (v["pass_within_1px"], round(v["max_abs_displacement_error_px"], 2), round(v["max_abs_step_error_px"], 2), {c: (round(v["depth_class_" + c].get("max_abs_error_px", float("nan")), 2), v["depth_class_" + c]["pass_within_1px"]) for c in ("near", "reference", "far")}) for k, v in res.items() if isinstance(v, dict)}
    print(a.cmd, summary)
    return 0


if __name__ == "__main__":
    sys.exit(main())
