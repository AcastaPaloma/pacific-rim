"""Deterministic synthetic style_capture@v1 generator with construction-known truth (fixtures for M-003 / M-004).

Independent of kaiju_metrics (never imports it): own camera model (Unity conventions), own tiny ray-caster, own
quantiser and dither masks, PIL for PNG writing, skimage only to turn Lab ramps into sRGB for unquantised controls.
Truth is either exact by construction (palette shares, integer pixel shifts, painted depth bands, painted haze
targets) or computed from the generator's own analytic scene geometry, and written beside the fixture.

Camera: camera-to-world R = R_y(yaw) R_x(pitch) (Unity Euler order Z, X, Y; positive pitch looks down);
forward = (sin y cos p, -sin p, cos y cos p), right = (cos y, 0, -sin y). Pixel (i, j) centre is (i + 0.5, j + 0.5)
from the top-left; ndc x = 2 (i + 0.5) / W - 1, ndc y = 1 - 2 (j + 0.5) / H.
"""
from __future__ import annotations

import hashlib
import json
import math
import os
from dataclasses import dataclass

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
PALETTES = os.path.join(PROJECT, "research", "R-002", "palettes.json")
NEAR, FAR = 0.3, 20000.0
BAYER4 = (np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]) + 0.5) / 16.0


def load_profile(pid, path=PALETTES):
    with open(path) as f:
        pj = json.load(f)
    cols = pj["profiles"][pid]["colours"]
    return [c["hex"] for c in cols], [c["role"] for c in cols], [c["lab"] for c in cols]


def canonical_sha(hexes):
    return hashlib.sha256(json.dumps([h.lower() for h in hexes], separators=(",", ":")).encode()).hexdigest()


def hex_rgb(h):
    return np.array([int(h[i:i + 2], 16) for i in (1, 3, 5)], np.uint8)


def quat_yp(yaw_deg, pitch_deg):
    y, p = math.radians(yaw_deg) / 2, math.radians(pitch_deg) / 2
    qy = np.array([0, math.sin(y), 0, math.cos(y)])
    qx = np.array([math.sin(p), 0, 0, math.cos(p)])
    ax, ay, az, aw = qy; bx, by, bz, bw = qx
    return [aw * bx + ax * bw + ay * bz - az * by, aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw, aw * bw - ax * bx - ay * by - az * bz]


@dataclass
class Cam:
    pos: np.ndarray           # world position (float64, absolute)
    yaw_deg: float
    pitch_deg: float
    W: int
    H: int
    hfov_deg: float = 60.0

    @property
    def vfov_deg(self):
        return math.degrees(2 * math.atan(math.tan(math.radians(self.hfov_deg) / 2) * self.H / self.W))

    def basis(self):
        y, p = math.radians(self.yaw_deg), math.radians(self.pitch_deg)
        f = np.array([math.sin(y) * math.cos(p), -math.sin(p), math.cos(y) * math.cos(p)])
        r = np.array([math.cos(y), 0.0, -math.sin(y)])
        u = np.array([math.sin(p) * math.sin(y), math.cos(p), math.sin(p) * math.cos(y)])
        return r, u, f

    def rays(self):
        r, u, f = self.basis()
        th, tv = math.tan(math.radians(self.hfov_deg) / 2), math.tan(math.radians(self.vfov_deg) / 2)
        x = (2 * (np.arange(self.W) + 0.5) / self.W - 1) * th
        y = (1 - 2 * (np.arange(self.H) + 0.5) / self.H) * tv
        X, Y = np.meshgrid(x, y)
        return X[..., None] * r + Y[..., None] * u + f       # not normalised: camera-z component is 1

    def project(self, P):
        """World points (..., 3) -> continuous pixel coords (u, v) (top-left origin) and eye depth z."""
        r, u, f = self.basis()
        d = P - self.pos
        z = d @ f
        th, tv = math.tan(math.radians(self.hfov_deg) / 2), math.tan(math.radians(self.vfov_deg) / 2)
        xn = (d @ r) / z / th
        yn = (d @ u) / z / tv
        return (xn + 1) / 2 * self.W, (1 - yn) / 2 * self.H, z

    def project_dir(self, D):
        r, u, f = self.basis()
        z = D @ f
        th, tv = math.tan(math.radians(self.hfov_deg) / 2), math.tan(math.radians(self.vfov_deg) / 2)
        return ((D @ r) / z / th + 1) / 2 * self.W, (1 - (D @ u) / z / tv) / 2 * self.H, z


# ------------------------------------------------------------------------------------------------ scenes
def city_boxes(seed=3):
    rng = np.random.default_rng(seed)
    b = []
    for i in range(-6, 7):
        for j in range(2, 26):
            if rng.random() < 0.55:
                cx, cz = i * 90.0 + rng.uniform(-20, 20), j * 90.0
                w, d, h = rng.uniform(25, 50), rng.uniform(25, 50), rng.uniform(10, 140)
                b.append((cx - w / 2, 0, cz - d / 2, cx + w / 2, h, cz + d / 2, rng.uniform(0.35, 0.8)))
    b.append((-60, 0, 520, 60, 120, 600, 0.25))
    for k in range(10):
        b.append((-3000 + k * 600, 0, 4500, -2500 + k * 600, rng.uniform(150, 400), 5000, 0.6))
    return np.array(b, float)


def render_city(cam: Cam, boxes, origin, haze=True, haze_d=1500.0, sky_k=0.12):
    """Tone p in [0, 1] (dark near objects, light far / sky), eye depth z (inf = sky), world hit points."""
    R = cam.rays()
    D = R.reshape(-1, 3)
    Dn = D / np.linalg.norm(D, axis=1, keepdims=True)
    o = cam.pos - origin
    tb = np.full(len(D), np.inf); shade = np.zeros(len(D))
    inv = 1.0 / np.where(np.abs(Dn) < 1e-12, 1e-12, Dn)
    sun = np.array([0.5, 0.7, -0.4]); sun /= np.linalg.norm(sun)
    for b in boxes:
        t1 = (b[:3] - o) * inv; t2 = (b[3:6] - o) * inv
        tn = np.minimum(t1, t2).max(1); tf = np.maximum(t1, t2).min(1)
        hit = (tn <= tf) & (tn > 0.1) & (tn < tb)
        if hit.any():
            ax = np.minimum(t1, t2)[hit].argmax(1)
            n = np.zeros((hit.sum(), 3)); n[np.arange(len(ax)), ax] = -np.sign(Dn[hit, ax])
            tb[hit] = tn[hit]; shade[hit] = b[6] * (0.35 + 0.65 * np.clip(n @ sun, 0, 1))
    tg = np.where(Dn[:, 1] < -1e-9, -o[1] / np.where(Dn[:, 1] < -1e-9, Dn[:, 1], -1), np.inf)
    g = tg < tb
    Pg = o + Dn[g] * tg[g, None]
    tile = (np.floor(Pg[:, 0] / 30) + np.floor(Pg[:, 2] / 30)) % 2
    tb[g] = tg[g]; shade[g] = (0.25 + 0.25 * tile) * (0.35 + 0.65 * sun[1])
    sky = ~np.isfinite(tb) | (tb > 0.98 * FAR)
    z = np.where(sky, np.inf, tb * (Dn @ cam.basis()[2]))
    hz = 1 - np.exp(-np.where(sky, FAR, tb) / haze_d) if haze else np.where(sky, 1.0, 0.0)
    elev = np.clip(Dn[:, 1], 0, 0.4) / 0.4
    p = np.where(sky, 1.0 - sky_k * elev, 0.55 * np.clip(shade, 0, 1) * (1 - hz) + 0.97 * hz)
    Pw = np.where(sky[:, None], np.nan, cam.pos + Dn * tb[:, None])
    return p.reshape(cam.H, cam.W), z.reshape(cam.H, cam.W), Pw.reshape(cam.H, cam.W, 3)


def render_wall(cam: Cam, wall_z_world, p_value=0.5):
    """Frontal wall: the plane z_world = wall_z (camera must look along +z, yaw = pitch = 0)."""
    R = cam.rays()
    t = (wall_z_world - cam.pos[2]) / R[..., 2]
    Pw = cam.pos + R * t[..., None]
    z = np.full((cam.H, cam.W), wall_z_world - cam.pos[2])
    return np.full((cam.H, cam.W), p_value), z, Pw


def quantise(p, n, mode="bayer", thr=None, seed=0):
    x = np.clip(p, 0, 1) * (n - 1)
    if mode == "bayer":
        yy, xx = np.indices(p.shape)
        thr = BAYER4[yy % 4, xx % 4]
    elif mode == "noise":
        thr = np.random.default_rng(seed).random(p.shape)
    elif mode == "none":
        thr = 0.5
    elif mode == "custom":
        pass
    return np.clip(np.floor(x + thr), 0, n - 1).astype(np.int32)


# ------------------------------------------------------------------------------------------------ writers
def save_png(path, a):
    a = np.ascontiguousarray(a)
    if a.dtype == np.uint16:
        Image.fromarray(a).save(path, format="PNG")
    else:
        Image.fromarray(a).save(path, format="PNG")


def encode_depth(z):
    fin = np.isfinite(z)
    zc = np.clip(np.where(fin, z, NEAR), NEAR, FAR)
    v = np.minimum(np.round(65535 * np.log(zc / NEAR) / math.log(FAR / NEAR)), 65534)
    return np.where(fin, v, 65535).astype(np.uint16)


def upscale(rgb, s, offset=(0, 0), mode="nearest"):
    import cv2
    h, w = rgb.shape[:2]
    if mode == "nearest":
        out = np.repeat(np.repeat(rgb, s, 0), s, 1)
    else:
        out = cv2.resize(rgb, (w * s, h * s), interpolation=cv2.INTER_LINEAR)
    ox, oy = offset
    if ox or oy:
        out = np.roll(out, (oy, ox), (0, 1))
    return out


def sidecar(set_id, seq_id, idx, n_frames, cam: Cam, profile_id, hexes, scale, files, motion, origin,
            snapped_pos=None, snapped_ypr=None, dither_anchor="screen", offset=(0, 0), flags=None, extra=None,
            pos_cont=None, ypr_cont=None):
    pos_local = (pos_cont if pos_cont is not None else cam.pos) - origin
    sp = (snapped_pos if snapped_pos is not None else cam.pos) - origin
    ypr = ypr_cont if ypr_cont is not None else [cam.yaw_deg, cam.pitch_deg, 0.0]
    sc = {
        "schema": "style_capture@v1", "capture_set_id": set_id, "sequence_id": seq_id,
        "frame": {"index": idx, "sequence_length": n_frames, "sim_time_s": round(idx / 60.0, 9), "dt_s": 1 / 60.0,
                  "sim_paused": True},
        "build": {"git_commit": "synthetic", "unity_version": "none", "render_pipeline": "synthetic",
                  "feature_flags": flags if flags is not None else {"Rendering.Pixel": True}},
        "style": {"palette_set": "palette_set@v1", "palette_profile_id": profile_id,
                  "palette_sha256": canonical_sha(hexes), "district_id": "synthetic", "time_of_day": "fixed_v1"},
        "resolution": {"internal_wh": [cam.W, cam.H], "output_wh": [cam.W * scale, cam.H * scale],
                       "integer_scale": scale, "output_block_offset_px": list(offset),
                       "subpixel_offset_internal_px": [0.0, 0.0]},
        "camera": {"rig": "SyntheticRig", "projection": "perspective", "hfov_deg": cam.hfov_deg,
                   "vfov_deg": cam.vfov_deg, "near_m": NEAR, "far_m": FAR, "origin_offset_m": list(map(float, origin)),
                   "position_local_m": list(map(float, pos_local)), "rotation_xyzw": quat_yp(cam.yaw_deg, cam.pitch_deg),
                   "yaw_pitch_roll_deg": list(map(float, ypr)),
                   "snap": {"enabled": snapped_pos is not None, "reference_depth_m": 600.0,
                            "position_grid_m": 2 * 600 * math.tan(math.radians(cam.hfov_deg) / 2) / cam.W,
                            "yaw_step_deg": math.degrees(math.atan(2 * math.tan(math.radians(cam.hfov_deg) / 2) / cam.W)),
                            "pitch_step_deg": math.degrees(math.atan(2 * math.tan(math.radians(cam.hfov_deg) / 2) / cam.W)),
                            "snapped_position_local_m": list(map(float, sp)),
                            "snapped_yaw_pitch_roll_deg": [cam.yaw_deg, cam.pitch_deg, 0.0],
                            "dither_anchor": dither_anchor}},
        "conventions": {"handedness": "left (Unity)", "up": "+Y", "forward": "+Z",
                        "pixel_origin": "top-left; pixel centre at (x+0.5, y+0.5)"},
        "motion": motion,
        "files": files,
        "encodings": {"depth": {"format": "png16_log"}},
    }
    if extra:
        sc.update(extra)
    return sc


def write_frame(seq_dir, set_id, seq_id, idx, n_frames, cam, profile_id, hexes, index_img, z, motion, origin,
                scale=4, offset=(0, 0), rgb_internal=None, rgb_output=None, mask_anim=None, mask_ui=None,
                write_index=True, upscale_mode="nearest", **kw):
    """Write one frame. index_img indexes `hexes`; rgb_internal overrides the colour image (controls)."""
    os.makedirs(seq_dir, exist_ok=True)
    pal = np.stack([hex_rgb(h) for h in hexes])
    ci = rgb_internal if rgb_internal is not None else pal[np.clip(index_img, 0, len(hexes) - 1)]
    co = rgb_output if rgb_output is not None else upscale(ci, scale, offset, upscale_mode)
    pre = f"f{idx:06d}"
    files = {"color_internal": f"{pre}_color_internal.png", "color_output": f"{pre}_color_output.png",
             "palette_index": f"{pre}_index.png" if write_index else None, "depth": f"{pre}_depth.png",
             "mask_animated": f"{pre}_mask_animated.png", "mask_ui": f"{pre}_mask_ui.png" if mask_ui is not None else None,
             "motion_vectors": None}
    save_png(os.path.join(seq_dir, files["color_internal"]), ci.astype(np.uint8))
    save_png(os.path.join(seq_dir, files["color_output"]), co.astype(np.uint8))
    if write_index:
        save_png(os.path.join(seq_dir, files["palette_index"]), index_img.astype(np.uint8))
    save_png(os.path.join(seq_dir, files["depth"]), encode_depth(z))
    save_png(os.path.join(seq_dir, files["mask_animated"]),
             (mask_anim if mask_anim is not None else np.zeros(index_img.shape, np.uint8)).astype(np.uint8))
    if mask_ui is not None:
        save_png(os.path.join(seq_dir, files["mask_ui"]), mask_ui.astype(np.uint8))
    sc = sidecar(set_id, seq_id, idx, n_frames, cam, profile_id, hexes, scale, files, motion, origin,
                 offset=offset, **kw)
    with open(os.path.join(seq_dir, f"{pre}.json"), "w") as f:
        json.dump(sc, f, indent=1)
    return sc


def write_sequence_json(seq_dir, set_id, seq_id, purpose, n_frames, motion, viewpoint=None):
    os.makedirs(seq_dir, exist_ok=True)
    meta = {"schema": "style_capture@v1", "capture_set_id": set_id, "sequence_id": seq_id, "purpose": purpose,
            "frame_count": n_frames, "motion": motion, "frames": [f"f{i:06d}.json" for i in range(n_frames)],
            "renderer_settings_hash": "synthetic"}
    if viewpoint:
        meta["viewpoint"] = viewpoint
    with open(os.path.join(seq_dir, "sequence.json"), "w") as f:
        json.dump(meta, f, indent=1)


def motion_block(typ, speed, axis="camera_right"):
    return {"type": typ, "axis": axis, "speed_internal_px_per_frame": speed, "speed_si": {}, "scripted": True}


# ------------------------------------------------------------------------------------------------ M-003 sets
ORIGIN = np.array([-2715000.0, 0.0, 4185000.0])


def m003_gameplay_frames(set_dir, profile="slate_teal", n_frames=12, W=160, H=90, scale=4, mode="mixed",
                         seq_id="gameplay", haze=True, invert=False, off_palette_share=0.0, palettes=PALETTES,
                         upscale_mode="nearest", offset=(0, 0), declare_offset=True, unquantised=False, modes=None):
    """City frames from three viewpoints. mode: 'mixed' (Bayer in the sky, posterised geometry), 'bayer',
    'none', 'noise'. off_palette_share injects whole-block #00ff00 pixels (exact share, internal and output)."""
    hexes, roles, labs = load_profile(profile, palettes)
    ramp = [i for i, r in enumerate(roles) if r.startswith("ramp_")]
    set_id = os.path.basename(os.path.normpath(set_dir))
    d = os.path.join(set_dir, seq_id)
    motion = motion_block("static", 0.0)
    write_sequence_json(d, set_id, seq_id, "M-003", n_frames, motion)
    boxes = city_boxes()
    views = [((0, 60.0, 0), 0.0, -2.5), ((0, 70.0, -200), 4.0, -2.5), ((20.0, 90.0, -400), -6.0, -2.0)]
    truth = []
    for k in range(n_frames):
        (x, y, z0), yaw, pitch = views[k % 3]
        cam = Cam(ORIGIN + np.array([x + 5.0 * k, y, z0]), yaw + 0.5 * k, pitch, W, H)
        p, zz, _ = render_city(cam, boxes, ORIGIN, haze=haze)
        if invert:
            p = np.where(np.isfinite(zz), 1 - p, p)
        sky = ~np.isfinite(zz)
        mode_k = modes[k] if modes else mode
        if mode_k == "mixed":
            band = sky & (np.indices(p.shape)[0] >= int(0.15 * H))     # dither the lower sky only
            q = np.where(band, quantise(p, 8, "bayer"), quantise(p, 8, "none"))
        else:
            q = quantise(p, 8, mode_k, seed=k)
        idx = np.array(ramp)[q]
        ci = None
        if unquantised:
            from skimage.color import lab2rgb
            lab = np.array([labs[i] for i in ramp])
            xx = np.clip(p, 0, 1) * 7; i0 = np.minimum(np.floor(xx).astype(int), 6); fr = (xx - i0)[..., None]
            ci = np.clip(np.round(lab2rgb(lab[i0] * (1 - fr) + lab[i0 + 1] * fr) * 255), 0, 255).astype(np.uint8)
        if off_palette_share > 0:
            n_bad = int(round(off_palette_share * W * H))
            if ci is None:
                pal = np.stack([hex_rgb(h) for h in hexes]); ci = pal[idx]
            flat = ci.reshape(-1, 3); flat[:n_bad] = [0, 255, 0]
            idx = idx.copy(); idx.reshape(-1)[:n_bad] = 255
        off = offset if declare_offset else (0, 0)
        sc = write_frame(d, set_id, seq_id, k, n_frames, cam, profile, hexes, idx, zz, motion, ORIGIN, scale=scale,
                         offset=off, rgb_internal=ci,
                         rgb_output=upscale(ci if ci is not None else np.stack([hex_rgb(h) for h in hexes])[np.clip(idx, 0, len(hexes) - 1)],
                                            scale, offset, upscale_mode),
                         write_index=not unquantised)
        truth.append({"frame": k, "off_palette_share": n_bad / (W * H) if off_palette_share > 0 else 0.0})
    return truth


def m003_haze_cal(set_dir, profile="slate_teal", W=160, H=90, scale=4, dL_mode="haze", seq_id="haze_cal",
                  palettes=PALETTES, n_frames=1):
    """Calibration scene painted directly: six dark/light target pairs at 200..8000 m, each pair a box pair of
    ramp colours. dL_mode: 'haze' (contrast falls, mean rises), 'flat' (no haze: constant pair), 'inverted'.
    Truth = exact signed light-minus-dark L* (from the profile Lab) per distance."""
    hexes, roles, labs = load_profile(profile, palettes)
    ramp = [i for i, r in enumerate(roles) if r.startswith("ramp_")]
    set_id = os.path.basename(os.path.normpath(set_dir))
    d = os.path.join(set_dir, seq_id)
    motion = motion_block("static", 0.0)
    write_sequence_json(d, set_id, seq_id, "haze_cal", n_frames, motion)
    dists = [200, 500, 1000, 2000, 4000, 8000]
    pairs = {"haze": [(0, 5), (1, 5), (2, 5), (3, 5), (4, 5), (5, 5)],
             "flat": [(0, 4)] * 6,
             "inverted": [(5, 7), (4, 6), (3, 5), (2, 4), (1, 3), (0, 2)]}[dL_mode]
    idx = np.full((H, W), ramp[7], np.int32)
    z = np.full((H, W), np.inf)
    targets = []
    bw = max(4, W // 16)
    for i, (dist, (a, b)) in enumerate(zip(dists, pairs)):
        x0 = 2 + i * (W // 6); y0 = H // 2 - bw // 2
        dark = [x0, y0, x0 + bw, y0 + bw]; light = [x0 + bw, y0, x0 + 2 * bw, y0 + bw]
        idx[dark[1]:dark[3], dark[0]:dark[2]] = ramp[a]; idx[light[1]:light[3], light[0]:light[2]] = ramp[b]
        z[y0:y0 + bw, x0:x0 + 2 * bw] = float(dist)
        targets.append({"distance_m": float(dist), "dark_px": dark, "light_px": light})
    cam = Cam(ORIGIN + np.array([0, 60.0, 0]), 0.0, 0.0, W, H)
    for k in range(n_frames):
        write_frame(d, set_id, seq_id, k, n_frames, cam, profile, hexes, idx, z, motion, ORIGIN, scale=scale,
                    extra={"haze_targets": targets})
    L = [labs[ramp[b]][0] - labs[ramp[a]][0] for a, b in pairs]
    M = [(labs[ramp[b]][0] + labs[ramp[a]][0]) / 2 for a, b in pairs]
    return {"dL": L, "meanL": M, "distances": dists}


def m003_pass_set(root, set_id="m003_pass", profile="slate_teal", **kw):
    sd = os.path.join(root, set_id)
    t = m003_gameplay_frames(sd, profile=profile, **kw)
    h = m003_haze_cal(sd, profile=profile, **({"palettes": kw["palettes"]} if "palettes" in kw else {}))
    return sd, {"gameplay": t, "haze_cal": h}


# ------------------------------------------------------------------------------------------------ M-004 sequences
def px_world(W, hfov=60.0, d_ref=600.0):
    return 2 * d_ref * math.tan(math.radians(hfov) / 2) / W


def px_angle_deg(W, hfov=60.0):
    return math.degrees(math.atan(2 * math.tan(math.radians(hfov) / 2) / W))


def m004_sequence(set_dir, seq_id, motion_type, speed, anchor="screen", n_frames=60, W=160, H=90, scene="wall",
                  profile="slate_teal", snapped=True, viewpoint="eye", pattern="checker", declared_speed=None,
                  freeze=False, blank=False, mask_all=False, palettes=PALETTES, seed=0, origin=ORIGIN):
    """One M-004 sequence. scene 'wall' (frontal wall at the 600 m reference depth; translation only) or 'sky'
    (all sky; yaw) or 'city'. anchor: screen | direction | surface | temporal_noise | none.
    pattern 'checker' gives a two-level checker on every pixel (all interleaved); 'stripes' gives
    a 50 % checker region (left half) and a flat region (right half).
    Returns truth computed from the generator's own geometry: S_px, C_px (checker pixels), F."""
    hexes, roles, _ = load_profile(profile, palettes)
    ramp = [i for i, r in enumerate(roles) if r.startswith("ramp_")]
    set_id = os.path.basename(os.path.normpath(set_dir))
    d = os.path.join(set_dir, seq_id)
    decl = speed if declared_speed is None else declared_speed
    motion = motion_block(motion_type, decl, "camera_right" if motion_type == "translate" else "world_up")
    write_sequence_json(d, set_id, seq_id, "M-004", n_frames, motion, viewpoint=viewpoint)
    pw, pa = px_world(W), px_angle_deg(W)
    base = origin + np.array([0.0, 70.0, 0.0])
    boxes = city_boxes() if scene == "city" else None
    frames = []
    cam0 = Cam(base.copy(), 0.0, 0.0, W, H)
    for t in range(n_frames):
        ft = 0 if freeze else t
        k = math.floor(speed * ft + 1e-9) if snapped else speed * ft
        pos_c = base.copy(); yaw_c = 0.0
        pos = base.copy(); yaw = 0.0
        if motion_type == "translate":
            pos_c = base + np.array([pw * speed * t, 0, 0]); pos = base + np.array([pw * k, 0, 0])
        elif motion_type == "yaw":
            yaw_c = pa * speed * t; yaw = pa * k
        cam = Cam(pos, yaw, 0.0, W, H)
        if scene == "wall":
            p, z, Pw = render_wall(cam, base[2] + 600.0)
        elif scene == "sky":
            R = cam.rays(); z = np.full((H, W), np.inf); Pw = None; p = np.full((H, W), 0.5)
        else:
            p, z, Pw = render_city(cam, boxes, origin)
        # pattern parity field
        if anchor in ("screen", "none", "temporal_noise"):
            yy, xx = np.indices((H, W)); par = (xx + yy) % 2
        elif anchor == "direction":
            Dd = cam.rays().reshape(-1, 3)
            u0, v0, _ = cam0.project_dir(Dd)
            par = ((np.floor(u0) + np.floor(v0)) % 2).reshape(H, W).astype(int)
        elif anchor == "surface":
            if Pw is None:
                raise ValueError("surface anchor needs geometry")
            u0, v0, _ = cam0.project(Pw.reshape(-1, 3))
            par = ((np.floor(u0) + np.floor(v0)) % 2).reshape(H, W).astype(int)
        lo, hi = ramp[2], ramp[4]
        if anchor == "none":
            q = np.full((H, W), lo)
        elif anchor == "temporal_noise":
            rng = np.random.default_rng(seed * 100003 + t)
            q = np.where(rng.random((H, W)) < 0.5, lo, hi)
        else:
            q = np.where(par == 1, hi, lo)
        if pattern == "stripes":
            q = np.where(np.indices((H, W))[1] < W // 2, q, ramp[6])
        if blank:
            q = np.full((H, W), lo)
        ma = np.full((H, W), 255, np.uint8) if mask_all else None
        sc = write_frame(d, set_id, seq_id, t, n_frames, cam, profile, hexes, q.astype(np.int32), z, motion, origin,
                         scale=2, mask_anim=ma, snapped_pos=pos if snapped else None, pos_cont=pos_c,
                         ypr_cont=[yaw_c, 0.0, 0.0],
                         dither_anchor={"temporal_noise": "screen", "none": "screen"}.get(anchor, anchor))
        frames.append((cam, q, z, Pw))
    return _truth_m004(frames, motion_type, speed, W, H, pattern)


def _truth_m004(frames, motion_type, speed, W, H, pattern):
    """Expected S_px / C_px / F from exact scene geometry (nearest-pixel reprojection by the generator itself)."""
    if motion_type == "static" or speed == 0:
        ch = [np.mean(frames[t][1] != frames[t - 1][1]) for t in range(1, len(frames))]
        rv = [np.mean((frames[t][1] == frames[t - 2][1]) & (frames[t][1] != frames[t - 1][1])) for t in range(2, len(frames))]
        return {"S_px": float(np.mean(ch)), "C_px": float(np.mean(ch)) if pattern == "checker" else None,
                "F": float(np.mean(rv)), "lag": 1}
    lag = max(1, math.ceil(1 / speed - 1e-9))

    def src(t, k):
        cam_t, q_t, z_t, P_t = frames[t]
        cam_s, q_s, z_s, P_s = frames[t - k]
        if P_t is None:   # sky: by direction
            u, v, zz = cam_s.project_dir(cam_t.rays().reshape(-1, 3))
        else:
            u, v, zz = cam_s.project(P_t.reshape(-1, 3))
        iu, iv = np.floor(u).astype(int), np.floor(v).astype(int)
        ok = (iu >= 0) & (iu < W) & (iv >= 0) & (iv < H) & (zz > 0)
        out = np.full(W * H, -1)
        out[ok] = q_s[iv[ok], iu[ok]]
        return out.reshape(H, W), ok.reshape(H, W)

    S, C, F = [], [], []
    for t in range(lag, len(frames)):
        qs, ok = src(t, lag)
        q = frames[t][1]
        S.append(((q != qs) & ok).sum() / max(ok.sum(), 1))
        if pattern == "checker":
            C.append(S[-1])
    for t in range(2, len(frames)):
        q1, o1 = src(t, 1); q2, o2 = src(t, 2); q = frames[t][1]
        both = o1 & o2
        F.append((both & (q2 == q) & (q1 != q)).sum() / max(both.sum(), 1))
    return {"S_px": float(np.mean(S)), "C_px": float(np.mean(C)) if C else None, "F": float(np.mean(F)), "lag": lag}


def m004_set(root, set_id="m004_pass", anchor_t="surface", anchor_y="direction", anchor_s="screen", n_frames=60,
             W=160, H=90, viewpoints=("eye",), profiles=("slate_teal",), static_noise=False):
    """The six required classes per profile and viewpoint (S0, T1, T2, T3, Y1, Y2)."""
    sd = os.path.join(root, set_id)
    truth = {}
    for pr in profiles:
        for vp in viewpoints:
            pre = f"{pr}_{vp}"
            truth[f"{pre}_S0"] = m004_sequence(sd, f"{pre}_S0", "static", 0.0,
                                               anchor="temporal_noise" if static_noise else anchor_s,
                                               n_frames=n_frames, W=W, H=H, profile=pr, viewpoint=vp)
            for name, sp in (("T1", 0.1), ("T2", 0.25), ("T3", 1.0)):
                truth[f"{pre}_{name}"] = m004_sequence(sd, f"{pre}_{name}", "translate", sp, anchor=anchor_t,
                                                       n_frames=n_frames, W=W, H=H, profile=pr, viewpoint=vp)
            for name, sp in (("Y1", 0.25), ("Y2", 1.0)):
                truth[f"{pre}_{name}"] = m004_sequence(sd, f"{pre}_{name}", "yaw", sp, anchor=anchor_y, scene="sky",
                                                       n_frames=n_frames, W=W, H=H, profile=pr, viewpoint=vp)
    return sd, truth


def example_capture_set(root):
    """Small valid example for emitters: a 2-frame M-003 sequence, a haze_cal frame and a 3-frame M-004 yaw."""
    sd = os.path.join(root, "example_set")
    m003_gameplay_frames(sd, n_frames=2, W=96, H=54, scale=4, seq_id="gameplay")
    m003_haze_cal(sd, W=96, H=54, scale=4)
    m004_sequence(sd, "yaw_0p25", "yaw", 0.25, anchor="direction", scene="sky", n_frames=3, W=96, H=54)
    return sd


if __name__ == "__main__":
    import sys
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "out")
    print(example_capture_set(out))


# ------------------------------------------------------------------------------------------------ editing helpers
def read_index(seq_dir, k):
    return np.array(Image.open(os.path.join(seq_dir, f"f{k:06d}_index.png"))).astype(np.int32)


def repaint(seq_dir, k, index_img, palettes=PALETTES, out_override=None):
    """Rewrite one frame's palette_index, color_internal and color_output (nearest upscale at the declared
    offset) consistently from an index image (255 = off-palette #00ff00)."""
    with open(os.path.join(seq_dir, f"f{k:06d}.json")) as f:
        sc = json.load(f)
    hexes, _, _ = load_profile(sc["style"]["palette_profile_id"], palettes)
    pal = np.vstack([np.stack([hex_rgb(h) for h in hexes]), np.zeros((256 - len(hexes), 3), np.uint8)])
    pal[255] = [0, 255, 0]
    ci = pal[index_img]
    s = sc["resolution"]["integer_scale"]
    co = upscale(ci, s, tuple(sc["resolution"]["output_block_offset_px"])) if out_override is None else out_override
    save_png(os.path.join(seq_dir, sc["files"]["palette_index"]), index_img.astype(np.uint8))
    save_png(os.path.join(seq_dir, sc["files"]["color_internal"]), ci.astype(np.uint8))
    save_png(os.path.join(seq_dir, sc["files"]["color_output"]), co.astype(np.uint8))
    return ci, co


def write_test_palettes(path, base=PALETTES):
    """palette_set@v1 copy plus profile `test_many`: the slate_teal ramp, 13 neutral greys outside the ramp L* range
    13 neutral greys and 3 saturated hues (red, green, blue). Used to build d-i / d-ii negatives."""
    from skimage.color import rgb2lab
    with open(base) as f:
        pj = json.load(f)
    cols = [c for c in pj["profiles"]["slate_teal"]["colours"] if c["role"].startswith("ramp_")]
    greys = ["#%02x%02x%02x" % (v, v, v) for v in (7, 24, 36, 121, 134, 147, 160, 174, 187, 201, 215, 232, 249)]
    hues = ["#c03020", "#20a040", "#2040c0"]
    for h in greys + hues:
        lab = rgb2lab(hex_rgb(h)[None, None, :] / 255.0)[0, 0]
        cols.append({"role": "body", "hex": h, "lab": [round(float(v), 2) for v in lab]})
    pj["profiles"]["test_many"] = {"members": [], "dominant_hue_deg": 0.0, "size": len(cols), "colours": cols,
                                   "member_adherence": {}, "ramp_dL_cv": 0.0}
    with open(path, "w") as f:
        json.dump(pj, f)
    return path
