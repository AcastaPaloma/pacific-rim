"""R-002: M-004@v1 (pixel stability) procedure, validated on the synthetic ray-caster with naive vs snapped cameras.
Metric = depth+pose reprojection of frame t-1 (and t-2) into frame t at internal resolution, then palette-index change
rate (shimmer), A->B->A reversal rate (flicker) and the change rate restricted to dithered pixels (dither crawl).
Writes research/R-002/m004_validation.json."""
import json, os
import numpy as np
from common import OUT
from synth import make_scene, render, quantise, load_ramp, cam_basis, rays, W, H, HFOV, FAR
from m003 import dither_metrics
from scipy.ndimage import uniform_filter

PITCH = np.radians(-4)
D_REF = 600.0                                   # snap reference depth (m): the giant's distance
PX_WORLD = 2 * D_REF * np.tan(HFOV / 2) / W     # world size of one internal pixel at D_REF
PX_ANGLE = np.arctan(2 * np.tan(HFOV / 2) / W)  # yaw per internal pixel at screen centre
N = 60


def project(Pw, pos, yaw):
    r, u, f = cam_basis(yaw, PITCH)
    v = Pw - pos
    z = v @ f; x = v @ r; y = v @ u
    t = np.tan(HFOV / 2)
    px = (x / z / t + 1) / 2 * W - 0.5
    py = (-y / z / (t * H / W) + 1) / 2 * H - 0.5
    return px, py, z


def reproject(src_idx, src_depth, src_pose, dst_depth, dst_pose):
    """Sample src frame at the location each dst pixel had in src (nearest pixel). Sky pixels reproject by direction."""
    (pd, yd), (ps, ys) = dst_pose, src_pose
    d, f = rays(yd, PITCH); d = d.reshape(-1, 3); z = dst_depth.reshape(-1)
    sky = z >= 0.99 * FAR
    Pw = np.where(sky[:, None], ps + d * 1e7, pd + d * (z / (d @ f))[:, None])
    px, py, zs = project(Pw, np.asarray(ps), ys)
    ix, iy = np.round(px).astype(int), np.round(py).astype(int)
    inb = (ix >= 0) & (ix < W) & (iy >= 0) & (iy < H) & (zs > 0)
    out = np.full(len(z), -1); valid = np.zeros(len(z), bool)
    sd = src_depth[iy[inb], ix[inb]]
    occl_ok = np.where(sky[inb], sd >= 0.99 * FAR, np.abs(sd - zs[inb]) <= 0.05 * zs[inb])
    sel = np.where(inb)[0][occl_ok]
    out[sel] = src_idx[iy[sel], ix[sel]]; valid[sel] = True
    return out.reshape(H, W), valid.reshape(H, W)


def poses(motion, speed, snapped):
    """speed in internal px/frame (at D_REF for translation, at screen centre for yaw)."""
    out = []
    for t in range(N):
        if motion == "static":
            pos, yaw, k = np.array([0.0, 60.0, 0.0]), 0.0, 0
        elif motion == "translate":
            s = speed * t * PX_WORLD
            k = int(np.floor(speed * t)) if snapped else 0
            if snapped: s = k * PX_WORLD
            r, _, _ = cam_basis(0.0, PITCH); pos, yaw = np.array([0.0, 60.0, 0.0]) + r * s, 0.0
        else:  # yaw
            a = speed * t * PX_ANGLE
            k = int(np.floor(speed * t)) if snapped else 0
            if snapped: a = k * PX_ANGLE
            pos, yaw = np.array([0.0, 60.0, 0.0]), a
        out.append((pos, yaw, k))
    return out


def sequence(boxes, ramp, motion, speed, snapped, dither_mode, cache):
    frames = []
    for t, (pos, yaw, k) in enumerate(poses(motion, speed, snapped)):
        key = (round(float(pos[0]), 6), round(float(yaw), 9))
        if key not in cache: cache[key] = render(boxes, pos, yaw)
        p, depth = cache[key]
        if dither_mode == "bayer_locked":     # pattern offset follows the whole-pixel shift at D_REF / screen centre
            q = quantise(p, len(ramp), "bayer", offset=(k, 0))
        elif dither_mode == "bayer_direction_locked":  # sphere-mapped: follows rotation only (Pope 2017 idea)
            q = quantise(p, len(ramp), "bayer", offset=(k if motion == "yaw" else 0, 0))
        elif dither_mode == "bayer_world_anchored":  # idealised surface-anchored pattern: index by frame-0 projection
            pos0, yaw0, _ = poses(motion, speed, snapped)[0]
            d, f = rays(yaw, PITCH); d = d.reshape(-1, 3); z = depth.reshape(-1); sky = z >= 0.99 * FAR
            Pw = np.where(sky[:, None], pos0 + d * 1e7, pos + d * (z / (d @ f))[:, None])
            px0, py0, _ = project(Pw, pos0, yaw0)
            from common import bayer
            thr = bayer(4)[np.round(py0).astype(int) % 4, np.round(px0).astype(int) % 4].reshape(H, W)
            q = np.clip(np.floor(p * (len(ramp) - 1) + thr), 0, len(ramp) - 1).astype(np.int32)
        elif dither_mode == "temporal_noise":
            q = quantise(p, len(ramp), "noise", seed=t)
        else:
            q = quantise(p, len(ramp), "bayer")
        frames.append((q, depth, (pos, yaw)))
    return frames


def stability(frames, ramp, lag=1):
    """lag = frames per 1 internal px of camera motion (ceil(1/speed)); S_lag compares frame t with t-lag reprojected."""
    S, R, Sd, Sf, SL, CL = [], [], [], [], [], []
    for t in range(lag, len(frames)):
        q, dep, pose = frames[t]
        qL, vL = reproject(frames[t - lag][0], frames[t - lag][1], frames[t - lag][2], dep, pose)
        SL.append(((q != qL) & vL).sum() / max(vL.sum(), 1))
        dmk = interleaved_mask(q, ramp) & vL
        CL.append(((q != qL) & dmk).sum() / max(dmk.sum(), 1))
    for t in range(1, len(frames)):
        q, dep, pose = frames[t]
        q1, v1 = reproject(frames[t - 1][0], frames[t - 1][1], frames[t - 1][2], dep, pose)
        ch = (q != q1) & v1
        S.append(ch.sum() / max(v1.sum(), 1))
        dm_mask = interleaved_mask(q, ramp)
        flat = (uniform_filter((q == np.roll(q, 1, 1)).astype(float), 3) == 1.0) & ~dm_mask
        Sd.append(ch[dm_mask & v1].mean() if (dm_mask & v1).any() else 0.0)
        Sf.append(ch[flat & v1].mean() if (flat & v1).any() else 0.0)
        if t >= 2:
            q2, v2 = reproject(frames[t - 2][0], frames[t - 2][1], frames[t - 2][2], dep, pose)
            rv = v1 & v2 & (q2 == q) & (q1 != q)
            R.append(rv.sum() / max((v1 & v2).sum(), 1))
    return dict(lag=lag, shimmer_per_px=round(float(np.mean(SL)), 4), crawl_per_px=round(float(np.mean(CL)), 4), shimmer=round(float(np.mean(S)), 4), shimmer_p95=round(float(np.percentile(S, 95)), 4),
                flicker_reversal=round(float(np.mean(R)), 4), crawl_dithered=round(float(np.mean(Sd)), 4),
                change_flat=round(float(np.mean(Sf)), 4))


def interleaved_mask(q, ramp):
    Hh, Ww = q.shape; Pp = np.pad(q, 1, mode="edge"); diff = np.zeros(q.shape)
    for dy, dx in ((0, 1), (2, 1), (1, 0), (1, 2)):
        diff += (Pp[dy:dy + Hh, dx:dx + Ww] != q)
    return uniform_filter(diff / 4, 4) >= 0.35


def main():
    ramp, _ = load_ramp("slate_teal")
    boxes = make_scene(); cache = {}
    res = {}
    cfgs = [("static", 0, False, "bayer_screen"), ("static", 0, False, "temporal_noise")]
    for motion in ("translate", "yaw"):
        for sp in (0.1, 0.25, 1.0):
            cfgs += [(motion, sp, False, "bayer_screen"), (motion, sp, True, "bayer_screen"), (motion, sp, True, "bayer_locked"),
                     (motion, sp, True, "bayer_direction_locked"), (motion, sp, True, "bayer_world_anchored")]
    for motion, sp, snapped, dm in cfgs:
        key = f"{motion}@{sp}px/f|{'snapped' if snapped else 'continuous'}|{dm}"
        lag = 1 if sp == 0 else int(np.ceil(1 / sp - 1e-9))
        res[key] = stability(sequence(boxes, ramp, motion, sp, snapped, dm, cache), ramp, lag)
        print(key, res[key], flush=True)
    meta = dict(internal_res=[W, H], hfov_deg=60, d_ref_m=D_REF, px_world_m=round(PX_WORLD, 3),
                px_angle_deg=round(float(np.degrees(PX_ANGLE)), 4), frames=N)
    json.dump(dict(meta=meta, results=res), open(os.path.join(OUT, "m004_validation.json"), "w"), indent=1)


if __name__ == "__main__":
    main()
