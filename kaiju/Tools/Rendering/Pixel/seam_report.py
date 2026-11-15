#!/usr/bin/env python
"""T-192 seam report on moving layered capture sequences (run with the kaiju conda env).

Input: a capture root holding one folder per configuration (M0StyleFrame = single snap, far lift off; M3HazeFarLift = single, lift on;
M3Layered = layerSnap slabs, lift off; M3LayeredFarLift = slabs, lift on), each with <path_id>/x4/sequence.json written by
Game.Tools.Capture.CaptureCli (style_capture@v1). Nothing here changes a capture or a threshold.

Per (path, configuration) it reports, over every frame of the clip:
  seam purity   C6 ground seam purity (pixel_checks.seam_purity): share of seam texels in 4x4 windows of two lightness-neighbour indices
  tear rate     share of same-surface neighbour pairs (|d ln z| < 0.12, both not sky) that straddle a slab boundary and differ by two or more
                lightness ranks; the same pairs on the single render give the base rate (a haze band edge sits on the slab boundary by design)
  blink rate    share of boundary-zone texels (radial within +-4 % of a slab boundary) whose palette index goes A, B, A over three frames
                (frame-to-frame shimmer) against the same share in the rest of the frame at the same radial range
  band tears    same as tear rate for pairs straddling any index change on a continuous surface at least 1 texel from a slab boundary (haze band seams)
Tear and blink figures are compared layered against single (the difference is the slab's own contribution).
"""
import argparse, json, math, os, sys
import numpy as np
from PIL import Image
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import pixel_checks as pc
sys.path.insert(0, os.path.join(HERE, "..", "..", "Metrics"))
from kaiju_metrics.style import measures as M
C1_EDGES = [0, 50, 150, 400, 1000, 2500, 6000]   # registry M-003@v1.1 depth_bands_m, band_min_share 0.01, sky_far_fraction 0.99, min_bands 3

ZONE = 0.04   # +-4 % of a slab boundary radial distance
SAME_SURFACE = 0.12   # |d ln z| of two neighbouring texels of one surface (a ground row at 340 m seen from 10 m height steps by 0.08)


def load(d):
    meta, frames = pc.load_seq(d)
    return frames


def radial(f, d):
    z, sky = pc.depth_m(d, f)
    c = f["camera"]; H, W = z.shape
    ys, xs = np.mgrid[0:H, 0:W]
    tv = math.tan(math.radians(c["vfov_deg"]) / 2); th = math.tan(math.radians(c["hfov_deg"]) / 2)
    ray = np.sqrt(1 + ((xs + 0.5) / W * 2 - 1) ** 2 * th * th + ((ys + 0.5) / H * 2 - 1) ** 2 * tv * tv)
    return z * ray, z, sky


def ground_mask(z, sky, f):
    """Ground texels with the ground height estimated per frame (the Bay ground is not at y = 0 and a layered pose is snapped to a 1.6 m grid, so
    pixel_checks.ground_mask's fixed 0 +- 0.5 m slab misses the plane): the mode of the reconstructed height of the lower half of the frame, +-0.5 m."""
    c = f["camera"]; H, W = z.shape
    ys = np.arange(H)[:, None]
    tv = math.tan(math.radians(c["vfov_deg"]) / 2); pitch = math.radians(-c["yaw_pitch_roll_deg"][1])
    height = c["position_local_m"][1] + z * (math.sin(pitch) + (1 - (ys + 0.5) / H * 2) * tv * math.cos(pitch))
    low = (~sky) & (np.arange(H)[:, None] > H * 0.55) & (z < 2000)
    if low.sum() < 200: return np.zeros(z.shape, bool), height
    hist, edges = np.histogram(height[low], bins=np.arange(-30, 60, 0.25)); k = int(hist.argmax()); g0 = 0.5 * (edges[k] + edges[k + 1])
    g = (~sky) & (np.abs(height - g0) < 0.5)
    g[1:, :] &= g[:-1, :].copy()
    return g, height


def rank_map(idx, L, sky):
    used = np.unique(idx[~sky]); m = {int(i): float(L[(idx == i) & ~sky].mean()) for i in used}
    order = sorted(m, key=lambda i: m[i]); r = np.full(256, -1, np.int32); r[order] = np.arange(len(order)); return r


def pair_stats(idx, z, rad, sky, rank, bounds):
    """Counts of same-surface neighbour pairs: total, differing, hard (>= 2 ranks), split into straddling a slab boundary and not."""
    out = {"straddle": [0, 0, 0], "zone": [0, 0, 0], "other_far": [0, 0, 0]}   # pairs, differing, hard
    for axis in (0, 1):
        a = [slice(None)] * 2; b = [slice(None)] * 2
        a[axis] = slice(0, -1); b[axis] = slice(1, None); a = tuple(a); b = tuple(b)
        ok = (~sky[a]) & (~sky[b]) & (np.abs(np.log(z[a] / z[b])) < SAME_SURFACE)
        ra, rb = rad[a], rad[b]
        lo, hi = np.minimum(ra, rb), np.maximum(ra, rb)
        strad = np.zeros_like(ok)
        for B in bounds: strad |= (lo < B) & (hi >= B)
        inzone = np.zeros_like(ok)
        for B in bounds: inzone |= (np.abs(np.log(ra / B)) < ZONE) & (np.abs(np.log(rb / B)) < ZONE)
        far = ok & (np.minimum(ra, rb) > 250)
        diff = idx[a] != idx[b]; hard = np.abs(rank[idx[a]] - rank[idx[b]]) >= 2
        for key, m in (("straddle", ok & strad), ("zone", ok & inzone & ~strad), ("other_far", far & ~inzone)):
            out[key][0] += int(m.sum()); out[key][1] += int((m & diff).sum()); out[key][2] += int((m & hard).sum())
    return out


def block_share(out, ox, oy, k, mask):
    """Blocks of k x k output px on the grid with origin (ox, oy) that lie wholly inside mask: (uniform, total)."""
    H, W = out.shape[:2]; h, w = (H - oy) // k, (W - ox) // k
    o = out[oy:oy + h * k, ox:ox + w * k].reshape(h, k, w, k, -1).transpose(0, 2, 1, 3, 4).reshape(h, w, k * k, -1)
    m = mask[oy:oy + h * k, ox:ox + w * k].reshape(h, k, w, k).transpose(0, 2, 1, 3).reshape(h, w, k * k).all(axis=2)
    uni = (o == o[:, :, :1, :]).all(axis=(2, 3))
    return int((uni & m).sum()), int(m.sum())


def grid_regions(d, f, rad, sky, k=4):
    """Per slab-owned region (radial from the sidecar boundaries, 5 % margin): uniform-block share at the declared output offset and at the
    best offset of that region. The sidecar declares one offset; in a layered frame each slab is shifted by its own output-pixel amount."""
    lay = f.get("layers")
    if not lay: return None
    out = np.array(Image.open(os.path.join(d, f["files"]["color_output"])))[:, :, :3]
    B = lay["boundaries_radial_m"]; R = np.kron(rad, np.ones((k, k))); SK = np.kron(sky, np.ones((k, k), bool))
    decl = f["resolution"]["output_block_offset_px"]
    regs = {"near": (R < B[0] * 0.95) & ~SK, "main": (R > B[0] * 1.05) & (R < B[1] * 0.95) & ~SK, "far": (R > B[1] * 1.05) & ~SK}
    res = {}
    for name, m in regs.items():
        ud, nd = block_share(out, decl[0], decl[1], k, m)
        best = max(((block_share(out, ox, oy, k, m), (ox, oy)) for ox in range(k) for oy in range(k)), key=lambda t: (t[0][0] / max(1, t[0][1]), t[0][1]))
        (ub, nb), off = best
        res[name] = {"blocks": nd, "declared_uniform": ud, "own_uniform": ub, "own_offset": list(off)}
    res["declared_offset"] = list(decl)
    return res


def analyse(d, bounds_fallback):
    frames = load(d)
    tot = {"straddle": [0, 0, 0], "zone": [0, 0, 0], "other_far": [0, 0, 0]}
    purity = {"seam": 0, "impure": 0, "three": 0.0, "ground": 0}; blink = {"zone": [0, 0], "rest": [0, 0]}
    prev = []; layered = 0
    c1 = {"evaluated": 0, "pass": 0, "drop_fail": 0, "rho_fail": 0, "span_fail": 0, "worst_drop": 0.0, "min_rho": 1.0, "min_span": 1e9}
    grid = {"near": [0, 0, 0], "main": [0, 0, 0], "far": [0, 0, 0]}; offsets_differ = 0; grid_frames = 0
    for n, f in enumerate(frames):
        rad, z, sky = radial(f, d); idx = pc.img(d, f, "palette_index"); L = pc.lightness(pc.img(d, f, "color_internal"))
        lay = f.get("layers"); bounds = lay["boundaries_radial_m"] if lay else bounds_fallback
        layered += 1 if lay else 0
        rank = rank_map(idx, L, sky)
        ps = pair_stats(idx, z, rad, sky, rank, bounds)
        for k in tot:
            for i in range(3): tot[k][i] += ps[k][i]
        g, _ = ground_mask(z, sky, f); sp = pc.seam_purity(idx, L, g, rad)
        purity["seam"] += sp.get("seam_texels", 0); purity["impure"] += sp.get("impure_seam_texels", 0); purity["ground"] += sp.get("ground_texels", 0)
        purity["three"] += sp.get("three_or_more_index_window_share_of_ground", 0.0)
        db = M.depth_banding(L, z, f["camera"]["far_m"], C1_EDGES, 0.01, 0.99, 3)
        if "rho" in db:
            c1["evaluated"] += 1; dr, rh, sp = db["max_drop"], db["rho"], db["span"]
            ok = (dr <= 2.0) and (rh >= 0.9) and (sp >= 15.0); c1["pass"] += int(ok)
            c1["drop_fail"] += int(dr > 2.0); c1["rho_fail"] += int(rh < 0.9); c1["span_fail"] += int(sp < 15.0)
            c1["worst_drop"] = max(c1["worst_drop"], dr); c1["min_rho"] = min(c1["min_rho"], rh); c1["min_span"] = min(c1["min_span"], sp)
        if lay and n % 5 == 0:
            gr = grid_regions(d, f, rad, sky)
            if gr:
                grid_frames += 1
                for nm in grid: grid[nm][0] += gr[nm]["declared_uniform"]; grid[nm][1] += gr[nm]["own_uniform"]; grid[nm][2] += gr[nm]["blocks"]
                offs = {tuple(gr[nm]["own_offset"]) for nm in grid if gr[nm]["blocks"] > 200}
                if len(offs) > 1: offsets_differ += 1
        prev.append((idx, z, rad, sky, bounds))
        if len(prev) > 3: prev.pop(0)
        if len(prev) == 3:
            (i0, z0, r0, s0, _), (i1, z1, r1, s1, _), (i2, z2, r2, s2, bb) = prev
            same = (~s0) & (~s1) & (~s2) & (np.abs(np.log(z0 / z2)) < 0.01) & (np.abs(np.log(z1 / z2)) < 0.01)
            toggled = same & (i0 == i2) & (i1 != i2)
            zone = np.zeros_like(same)
            for B in bb: zone |= np.abs(np.log(r2 / B)) < ZONE
            rest = same & ~zone & (r2 > 250)
            zn = same & zone
            blink["zone"][0] += int(zn.sum()); blink["zone"][1] += int((toggled & zn).sum())
            blink["rest"][0] += int(rest.sum()); blink["rest"][1] += int((toggled & rest).sum())
    def rate(a): return a[1] / a[0] if a[0] else None
    res = {"frames": len(frames), "layered_frames": layered, "bounds_m": bounds_fallback,
           "pairs_straddle": tot["straddle"][0], "straddle_diff_rate": rate(tot["straddle"]), "straddle_hard_rate": (tot["straddle"][2] / tot["straddle"][0]) if tot["straddle"][0] else None,
           "pairs_zone": tot["zone"][0], "zone_diff_rate": rate(tot["zone"]), "zone_hard_rate": (tot["zone"][2] / tot["zone"][0]) if tot["zone"][0] else None,
           "pairs_other_far": tot["other_far"][0], "other_diff_rate": rate(tot["other_far"]), "other_hard_rate": (tot["other_far"][2] / tot["other_far"][0]) if tot["other_far"][0] else None,
           "seam_texels": purity["seam"], "impure_seam_texels": purity["impure"], "pure_share": 1.0 - purity["impure"] / max(1, purity["seam"]),
           "three_index_window_share_mean": purity["three"] / max(1, len(frames)),
           "c1_per_frame": c1,
           "grid_frames_sampled": grid_frames, "grid_frames_with_different_slab_offsets": offsets_differ,
           "grid": {nm: {"blocks": v[2], "declared_offset_uniform_share": (v[0] / v[2]) if v[2] else None, "own_offset_uniform_share": (v[1] / v[2]) if v[2] else None} for nm, v in grid.items()},
           "blink_zone_texels": blink["zone"][0], "blink_zone_rate": rate(blink["zone"]), "blink_rest_texels": blink["rest"][0], "blink_rest_rate": rate(blink["rest"])}
    return res


def main():
    ap = argparse.ArgumentParser(); ap.add_argument("cap_root"); ap.add_argument("--out", required=True)
    ap.add_argument("--configs", default="M0StyleFrame,M3HazeFarLift,M3Layered,M3LayeredFarLift")
    ap.add_argument("--bounds", default=None, help="fallback slab boundaries (radial m) for single-render sets, taken from the layered set of the same path when absent")
    a = ap.parse_args(); cfgs = a.configs.split(",")
    paths = sorted(p for p in os.listdir(os.path.join(a.cap_root, cfgs[0])) if os.path.isdir(os.path.join(a.cap_root, cfgs[0], p)))
    out = {"configs": cfgs, "zone_half_width": ZONE, "paths": {}}
    for p in paths:
        # boundaries of the layered run (first frame) are the reference for single-render sets
        lay_dir = os.path.join(a.cap_root, "M3Layered", p, "x4"); fr = load(lay_dir)
        b0 = fr[0]["layers"]["boundaries_radial_m"] if fr and fr[0].get("layers") else None
        out["paths"][p] = {}
        for c in cfgs:
            d = os.path.join(a.cap_root, c, p, "x4")
            if not os.path.isdir(d): continue
            out["paths"][p][c] = analyse(d, b0 or [338.3, 2601.2]); print(p, c, "done", flush=True)
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    json.dump(out, open(a.out, "w"), indent=1)


if __name__ == "__main__":
    main()
