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


TOWER_EDGE = (310.0, 120.0, 400.0)   # near-left vertical edge of Tower_600m (StyleTestScenes), 120 m up: against the sky


def edge_series(d):
    """Measured x (output px) of the tower's left silhouette edge in color_output, per frame, and the commanded x."""
    meta, frames = load_seq(d)
    meas, cmd = [], []
    for f in frames:
        out = img(d, f, "color_output"); L = lightness(out)
        px, py = project_wanted(f, TOWER_EDGE)
        y0, y1 = int(py) - 40, int(py) + 40
        x0, x1 = int(px) - 60, int(px) + 60
        band = L[y0:y1, x0:x1]
        # The tower is darker than the sky behind it: threshold halfway between the two sides of the window.
        thr = 0.5 * (np.median(band[:, :20]) + np.median(band[:, -20:]))
        dark = band < thr
        first = np.array([np.argmax(row) if row.any() else -1 for row in dark])
        first = first[first >= 0]
        meas.append(x0 + float(np.median(first))); cmd.append(px)
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
    def table(root):
        t = {}
        for p in sorted(glob.glob(os.path.join(root, "**", "*.png"), recursive=True)):
            t[os.path.relpath(p, root)] = hashlib.sha256(open(p, "rb").read()).hexdigest()
        return t
    ta, tb = table(a.dir_a), table(a.dir_b)
    diff = [k for k in ta if tb.get(k) != ta[k]] + [k for k in tb if k not in ta]
    sc = 0; scdiff = []
    for p in sorted(glob.glob(os.path.join(a.dir_a, "**", "f??????.json"), recursive=True)):
        rel = os.path.relpath(p, a.dir_a); q = os.path.join(a.dir_b, rel); sc += 1
        if not os.path.isfile(q) or open(p, "rb").read() != open(q, "rb").read(): scdiff.append(rel)
    return {"dir_a": os.path.relpath(a.dir_a), "dir_b": os.path.relpath(a.dir_b), "png_files": len(ta), "png_differing": len(diff), "first_differing": diff[:5],
            "sidecars": sc, "sidecars_differing": len(scdiff), "pass": bool(len(ta) > 0 and not diff and not scdiff)}


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
            "criterion": "M-003 d-iv: >= 99.5 % of s x s output blocks uniform at the declared offset"}


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
    res["pass"] = bool(1 <= n and res.get("marker_height_px", 0) in (1, 2) and res.get("marker_dL", 0) >= 10 and res.get("block80_height_px", 0) >= 10 + res.get("marker_height_px", 0) and res.get("block80_dL", 0) >= 4)
    res["criterion"] = "C7: 1.8 m marker at 600 m is 1-2 internal pixels with dL* >= 10 to its surround; the 80 m block at about 1 km is at least 10 px taller and at least one value band (>= 4 L*) from its background"
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
        # Seams: ground pixels in the dithered zone between haze levels k and k+1 use only indices of the two bands.
        st, fu, nb, sw = a.haze_start, a.haze_full, a.haze_bands, a.haze_seam
        level = (nb - 1) * np.clip(np.log(np.maximum(dist, st) / st) / math.log(fu / st), 0, 1)
        ground = (~sky) & (height < 3)
        seam_ok, seam_n = 0, 0
        for k in range(nb - 1):
            pure_a = ground & (level > k + sw / 2 - 0.5 + 0.02) & (level < k + 1 - sw / 2 - 0.02) if sw < 1 else ground & (np.abs(level - (k + 0.5)) < 0.02)
            pure_b = ground & (level > k + 1 + sw / 2 - 0.5 + 0.02 - 0.5 + 0.5) & (level < k + 2 - sw / 2 - 0.02)
            seam = ground & (level >= k + 1 - sw / 2 + 0.02) & (level <= k + 1 + sw / 2 - 0.02)
            if pure_a.sum() < 30 or pure_b.sum() < 30 or seam.sum() < 30: continue
            allowed = set(int(v) for v in np.unique(idx[pure_a])) | set(int(v) for v in np.unique(idx[pure_b]))
            used = set(int(v) for v in np.unique(idx[seam])); seam_n += 1; seam_ok += int(used <= allowed)
        row["ground_seams_checked"] = seam_n; row["ground_seams_only_adjacent_bands"] = seam_ok
        rows.append(row)
    return {"sequence": os.path.relpath(d), "frames": rows,
            "pass": bool(all(r["successively_lighter"] and r.get("near_field_darkest", True) and r["ground_seams_checked"] == r["ground_seams_only_adjacent_bands"] for r in rows)),
            "criterion": "C6: upright layers at 1, 3, 10 and 30 km are successively lighter; the farthest matches the sky at the horizon; the near field is darkest; ground pixels in a haze seam use only the indices of the two adjacent bands"}


def cmd_depth(a):
    _, frames = load_seq(a.sequence_dir); d = a.sequence_dir; f = frames[0]
    z, sky = depth_m(d, f); rows = []
    # Known scene points (StyleTestScenes): face centres of boxes, taken a little inside the face.
    for name, pt in (("Block80_Near front face", (0.0, 60.0, -10.0)), ("Tower_600m front face", (330.0, 120.0, 400.0)), ("Layer_3km_A front face", (-900.0, 200.0, 2700.0)),
                     ("Layer_10km_A front face", (1500.0, 600.0, 9400.0)), ("Layer_30km_A front face", (-2000.0, 2500.0, 29000.0))):
        x, y, ez = project_rendered(f, pt)
        xi, yi = int(x), int(y)
        if 0 <= xi < z.shape[1] and 0 <= yi < z.shape[0] and ez > 0:
            got = float(z[yi, xi]); rows.append({"point": name, "expected_eye_depth_m": float(ez), "decoded_m": got, "relative_error": abs(got - ez) / ez})
    return {"frame": os.path.relpath(d), "points": rows, "sky_pixels": int(sky.sum()), "pass": bool(len(rows) >= 3 and all(r["relative_error"] <= 0.01 for r in rows)),
            "criterion": "C19: depth PNG16 (log) decodes to known scene distances within 1 %"}


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("smooth"); p.add_argument("seq_on"); p.add_argument("seq_off")
    p = sub.add_parser("cut"); p.add_argument("capture_set")
    p = sub.add_parser("identical"); p.add_argument("dir_a"); p.add_argument("dir_b")
    p = sub.add_parser("grid"); p.add_argument("sequence_dir")
    p = sub.add_parser("scale"); p.add_argument("sequence_dir")
    p = sub.add_parser("layers"); p.add_argument("sequence_dir"); p.add_argument("--max-frames", type=int, default=12)
    p.add_argument("--haze-start", type=float, default=60.0); p.add_argument("--haze-full", type=float, default=20000.0); p.add_argument("--haze-bands", type=int, default=8); p.add_argument("--haze-seam", type=float, default=0.75)
    p = sub.add_parser("depth"); p.add_argument("sequence_dir")
    for q in sub.choices.values(): q.add_argument("--out")
    a = ap.parse_args()
    res = {"smooth": cmd_smooth, "cut": cmd_cut, "identical": cmd_identical, "grid": cmd_grid, "scale": cmd_scale, "layers": cmd_layers, "depth": cmd_depth}[a.cmd](a)
    res["tool"] = "Tools/Rendering/Pixel/pixel_checks.py " + a.cmd
    text = json.dumps(res, indent=1)
    if a.out:
        os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
        open(a.out, "w").write(text + "\n")
    summary = {k: v for k, v in res.items() if k in ("pass", "png_files", "png_differing")} if a.cmd != "smooth" else {k: (v["pass_within_1px"], round(v["max_abs_displacement_error_px"], 2), round(v["max_abs_step_error_px"], 2)) for k, v in res.items() if isinstance(v, dict)}
    print(a.cmd, summary)
    return 0


if __name__ == "__main__":
    sys.exit(main())
