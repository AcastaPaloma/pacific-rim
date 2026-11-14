"""T-006 step 4: thumbnail test, scale plate, skyline check, review boards.
Run from repo root: /opt/miniconda3/envs/kaiju/bin/python evidence/T-006/tools/step4_checks.py
"""
import json, os, random
import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage

ROOT = "evidence/T-006"
CAP = f"{ROOT}/captures_local"
HERO = f"{CAP}/hero_1080p/slate_teal"
OUT_CHECK = f"{ROOT}/checks"
OUT_BOARD = f"{ROOT}/boards"
os.makedirs(OUT_CHECK, exist_ok=True); os.makedirs(OUT_BOARD, exist_ok=True)
W, H, S = 1920, 1080, 4
FONT = ImageFont.load_default()

sp = json.load(open(f"{ROOT}/shot_params.json"))
props = json.load(open(f"{ROOT}/placeholder_props.json"))["props"]
side = json.load(open(f"{HERO}/f000000.json"))
ORG = np.array(sp["floating_origin_baytm"], float)  # (x, z)
cam = side["camera"]
NEAR, FAR = cam["near_m"], cam["far_m"]
HF, VF = np.radians(cam["hfov_deg"]), np.radians(cam["vfov_deg"])
FX = (W / 2) / np.tan(HF / 2); FY = (H / 2) / np.tan(VF / 2)
CPOS = np.array(cam["position_local_m"], float)
qx, qy, qz, qw = cam["rotation_xyzw"]
# camera-to-world rotation (Unity quaternion, columns = camera right/up/forward in world)
R = np.array([
    [1 - 2 * (qy * qy + qz * qz), 2 * (qx * qy - qz * qw), 2 * (qx * qz + qy * qw)],
    [2 * (qx * qy + qz * qw), 1 - 2 * (qx * qx + qz * qz), 2 * (qy * qz - qx * qw)],
    [2 * (qx * qz - qy * qw), 2 * (qy * qz + qx * qw), 1 - 2 * (qx * qx + qy * qy)]])


def scene(bx, y, bz):
    """BayTM (x, y navd88, z) -> scene/local (x, y, z)."""
    return np.array([bx - ORG[0], y, bz - ORG[1]], float)


def to_cam(p):
    return (np.asarray(p, float) - CPOS) @ R  # = R^T (p - c)


def proj(pc):
    pc = np.asarray(pc, float)
    z = pc[..., 2]
    return np.stack([W / 2 + FX * pc[..., 0] / z, H / 2 - FY * pc[..., 1] / z], -1), z


def load_depth(d):
    v = np.array(Image.open(f"{d}/f000000_depth.png")).astype(np.float64)
    z = NEAR * (FAR / NEAR) ** (v / 65535.0)
    z[v >= 65535] = np.inf
    return z


def lstar(rgb):
    c = np.asarray(rgb, float) / 255.0
    c = np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
    Y = c @ np.array([0.2126, 0.7152, 0.0722])
    f = np.where(Y > 216 / 24389, np.cbrt(Y), (24389 / 27 * Y + 16) / 116)
    return 116 * f - 16


depth = load_depth(HERO)
col_int = np.array(Image.open(f"{HERO}/f000000_color_internal.png").convert("RGB"))
out_img = Image.open(f"{HERO}/f000000_color_output.png").convert("RGB")

# ---- mech location -------------------------------------------------------
mech = sp["mech"]; hero = sp["framings"][0]
mech_z = hero["reference_depth_m"]
mc = to_cam(scene(mech["bounds_centre_baytm_xz"][0], mech["position_baytm"][1] + 40, mech["bounds_centre_baytm_xz"][1]))
mcol_int = (proj(mc)[0][0]) / S
wl_row = hero["rows_from_top_fraction"]["mech_base_waterline"] * 270

# mech mask: depth within +-20 m of reference depth, columns near mech, rows above waterline
cols = np.arange(480)[None, :]
near_mask = np.abs(depth - mech_z) < 20
win = (np.abs(cols - mcol_int) < 70) & (np.arange(270)[:, None] < wl_row + 2)
cand = near_mask & win
lab, n = ndimage.label(cand, structure=np.ones((3, 3)))
if n:
    sizes = ndimage.sum(cand, lab, range(1, n + 1))
    mech_mask = lab == (1 + int(np.argmax(sizes)))
else:
    mech_mask = cand
ys, xs = np.where(mech_mask)
top_row, bot_row = ys.min(), ys.max() + 1
visible_frac = (bot_row - top_row) / 270
# 80 m incl. submerged: extend the visible portion by the submerged depth analytically
sub = mech["water_depth_at_mech_m"]
full_frac = visible_frac * 80.0 / (80.0 - sub)
mech_bbox_int = [int(xs.min()), int(top_row), int(xs.max() + 1), int(bot_row)]

# ---- 1. hero thumb test --------------------------------------------------
x0, x1 = mech_bbox_int[0], mech_bbox_int[2]
pad = 2
x0p, x1p = max(0, x0 - pad), min(480, x1 + pad)
width = x1p - x0p
# neighbouring column band of equal width, pick the side with no mech and fewest buildings is not required: nearest non-mech columns
src0 = x0p - width if x0p - width >= 0 else x1p
decoy = col_int.copy()
decoy[:, x0p:x1p] = col_int[:, src0:src0 + width]
# restore the water below the waterline so the decoy is a plain frame without the mech (same neighbour columns)
def thumbs(a):
    im = Image.fromarray(a)
    return [im.resize((w, int(round(w * 270 / 480))), Image.BOX) for w in (480, 96)]
hero_t, decoy_t = thumbs(col_int), thumbs(decoy)
pad_px = 12
cw = 480 * 2 + pad_px * 3
canvas = Image.new("RGB", (cw, 270 + 40 + 96 * 270 // 480 + 40 + 96 * 270 // 480 * 0 + 150), (20, 20, 20))
cv = Image.new("RGB", (cw, 330), (20, 20, 20)); d = ImageDraw.Draw(cv)
cv.paste(hero_t[0], (pad_px, 24)); cv.paste(decoy_t[0], (pad_px * 2 + 480, 24))
d.text((pad_px, 8), "hero_slate_teal 480 px", fill=(230, 230, 230), font=FONT)
d.text((pad_px * 2 + 480, 8), "decoy_480 (mech columns replaced)", fill=(230, 230, 230), font=FONT)
y96 = 24 + 270 + 24
cv.paste(hero_t[1], (pad_px, y96)); cv.paste(decoy_t[1], (pad_px * 2 + 96 + 20, y96))
d.text((pad_px, y96 - 12), "hero 96 px", fill=(230, 230, 230), font=FONT)
d.text((pad_px * 2 + 96 + 20, y96 - 12), "decoy 96 px", fill=(230, 230, 230), font=FONT)
cv.save(f"{OUT_CHECK}/hero_thumb_test.png")
json.dump({
    "criterion": "C1: 480 px and 96 px wide hero; mech dominant, 40-60% of frame height; evaluator names it from the 96 px version beside a decoy",
    "method": "mech mask = depth within +-20 m of reference_depth_m (%.3f m), columns within 70 internal px of the projected mech centre, rows above the waterline, largest 8-connected component; decoy = same internal frame with the mech columns (+-2 px) replaced by the adjacent equal-width column band, numpy only" % mech_z,
    "mech_mask_bbox_internal_xyxy": mech_bbox_int, "mask_pixels": int(mech_mask.sum()),
    "mech_visible_height_fraction_of_frame": round(visible_frac, 4),
    "mech_full_80m_height_fraction_incl_submerged": round(full_frac, 4),
    "analytic_reference": hero["mech_height_fraction_of_frame"],
    "in_40_60_visible": bool(0.40 <= visible_frac <= 0.60),
    "in_40_60_full_80m": bool(0.40 <= full_frac <= 0.60),
    "decoy_source_column_start": int(src0), "decoy_replaced_columns": [int(x0p), int(x1p)],
    "evaluator_naming": "pending; the human or cold-rating step uses hero_thumb_test.png",
}, open(f"{OUT_CHECK}/hero_thumb_test.json", "w"), indent=1)
Image.fromarray((mech_mask * 255).astype(np.uint8)).save(f"{OUT_CHECK}/mech_mask_internal.png")

# ---- 2. scale cues plate -------------------------------------------------
plate = out_img.copy(); dr = ImageDraw.Draw(plate, "RGBA")
cues = []
ring = 3  # internal px
L_int = lstar(col_int)
cols_pal = [(255, 80, 80), (255, 170, 40), (255, 240, 60), (90, 255, 120), (90, 200, 255), (200, 120, 255)]


def box_corners(pos, size, yaw):
    sx, sy, sz = size
    xs = [-sx / 2, sx / 2]; zs = [-sz / 2, sz / 2]
    pts = []
    c, s = np.cos(np.radians(yaw)), np.sin(np.radians(yaw))
    for x in xs:
        for z in zs:
            for y in (0, sy):
                # Unity yaw about +Y, positive turns +z toward +x
                wx = x * c + z * s; wz = -x * s + z * c
                pts.append(scene(pos[0] + wx, pos[1] + y, pos[2] + wz))
    return np.array(pts)


for i, p in enumerate(props):
    pp = p["position_baytm"]
    P = box_corners([pp[0], pp[1], pp[2]], p["size_m"], p.get("yaw_deg", 0))
    pc = to_cam(P)
    uv, z = proj(pc)
    ok = bool(np.all(z > NEAR))
    umin, vmin = uv.min(0); umax, vmax = uv.max(0)
    wpx, hpx = (umax - umin) / S, (vmax - vmin) / S
    ci, cj = int(np.clip((umin + umax) / 2 / S, 0, 479)), int(np.clip((vmin + vmax) / 2 / S, 0, 269))
    ix0, ix1 = int(np.floor(umin / S)), int(np.ceil(umax / S)); iy0, iy1 = int(np.floor(vmin / S)), int(np.ceil(vmax / S))
    ix0, iy0 = max(ix0, 0), max(iy0, 0); ix1, iy1 = min(max(ix1, ix0 + 1), 480), min(max(iy1, iy0 + 1), 270)
    inner = L_int[iy0:iy1, ix0:ix1]
    yy0, yy1, xx0, xx1 = max(iy0 - ring, 0), min(iy1 + ring, 270), max(ix0 - ring, 0), min(ix1 + ring, 480)
    m = np.ones((yy1 - yy0, xx1 - xx0), bool); m[iy0 - yy0:iy1 - yy0, ix0 - xx0:ix1 - xx0] = False
    surround = L_int[yy0:yy1, xx0:xx1][m]
    dz = float(np.median(z))
    cues.append({
        "name": p["name"], "kind": p["kind"], "size_m": p["size_m"], "distance_m": round(dz, 1),
        "in_frame": bool(ok and 0 <= (umin + umax) / 2 < W and 0 <= (vmin + vmax) / 2 < H),
        "projected_internal_px": {"width": round(float(wpx), 3), "height": round(float(hpx), 3), "max_dim": round(float(max(wpx, hpx)), 3)},
        "in_1_to_3_px": bool(1.0 <= max(wpx, hpx) <= 3.0),
        "output_bbox_px": [round(float(umin), 1), round(float(vmin), 1), round(float(umax), 1), round(float(vmax), 1)],
        "internal_cell_bbox": [ix0, iy0, ix1, iy1],
        "L_inside_mean": round(float(inner.mean()), 2), "L_surround_mean": round(float(surround.mean()), 2),
        "L_contrast_inside_minus_surround": round(float(inner.mean() - surround.mean()), 2),
        "caveat": "L* from color_internal over the projected bbox cells; sub-pixel props are blended with their surround, so the contrast is a lower bound on the box's own value contrast",
        "analytic_expected_hero_1080p_max_px": p["expected_internal_px"]["hero_1080p"]["max_dim_px"],
    })
    col = cols_pal[i % len(cols_pal)]
    cx, cy = (umin + umax) / 2, (vmin + vmax) / 2
    r = 14
    dr.rectangle([cx - r, cy - r, cx + r, cy + r], outline=col + (255,), width=2)
    dr.text((cx - r, cy + r + 2), f"{p['name']} {p['size_m'][1]}m", fill=col + (255,), font=FONT)
    dr.text((cx - r, cy + r + 12), f"{max(wpx, hpx):.2f} px", fill=col + (255,), font=FONT)

# mech: bbox from mask
bx0, by0, bx1, by1 = [v * S for v in mech_bbox_int]
dr.rectangle([bx0, by0, bx1, by1], outline=(255, 60, 60, 255), width=3)
dr.text((bx0 + 4, by0 - 12), f"mech 80 m ({mech_z:.0f} m)", fill=(255, 60, 60, 255), font=FONT)
cues.append({"name": "mech", "kind": "mech 80 m", "distance_m": round(mech_z, 1), "output_bbox_px": [bx0, by0, bx1, by1],
             "projected_internal_px": {"width": float(x1 - x0), "height": float(bot_row - top_row)}})


def anchor_for(name=None, bid=None):
    for f in FEATS:
        pr = f["properties"]
        if (bid and pr["bid"] == bid) or (name and pr.get("name") == name):
            return f


# buildings
G = json.load(open("Assets/StreamingAssets/Geo/tiles/v1/L0/30_36/buildings.geojson"))
FEATS = G["features"]
towers = {}
for nm in ("Salesforce Tower", "Transamerica Pyramid", "Coit Tower"):
    f = anchor_for(name=nm)
    if f is None:
        continue
    ring_ = np.array(f["geometry"]["coordinates"][0])
    cen = ring_[:-1].mean(0); pr = f["properties"]
    towers[nm] = (f, cen, pr)

# 80 m horizontal ruler at the mech distance, beneath the mech
rulpx = 80 * FX / mech_z
ry = by1 + 40
rx0 = bx0 - rulpx - 30
dr.line([rx0, ry, rx0 + rulpx, ry], fill=(255, 255, 255, 255), width=3)
for xx in (rx0, rx0 + rulpx):
    dr.line([xx, ry - 8, xx, ry + 8], fill=(255, 255, 255, 255), width=3)
dr.text((rx0, ry + 10), f"80 m ruler at mech distance = {rulpx:.0f} px output ({rulpx / S:.0f} px internal)", fill=(255, 255, 255, 255), font=FONT)
plate_info = {}
for nm, colr in (("Salesforce Tower", (255, 220, 60)), ("Transamerica Pyramid", (80, 230, 255))):
    if nm not in towers:
        continue
    f, cen, pr = towers[nm]
    base = pr["base_navd88_m"]; hgt = pr["height_m"]
    pb = to_cam(scene(cen[0], base, cen[1])); pt = to_cam(scene(cen[0], base + hgt, cen[1]))
    (ub, vb), _ = proj(pb); (ut, vt), zt = proj(pt)
    zd = float(pb[2])
    # bar for the true height, 80 m ruler beside it at the tower's own distance
    xoff = 26
    dr.line([ub + xoff, vb, ub + xoff, vt], fill=colr + (255,), width=3)
    dr.text((ub + xoff + 6, vt), f"{nm} {hgt:.0f} m @ {zd:.0f} m: {abs(vb - vt):.0f} px", fill=colr + (255,), font=FONT)
    r80 = 80 * FY / zd
    dr.line([ub - xoff, vb, ub - xoff, vb - r80], fill=(255, 255, 255, 255), width=3)
    dr.text((ub - xoff - 150, vb - r80 - 12), f"80 m @ {zd:.0f} m = {r80:.0f} px", fill=(255, 255, 255, 255), font=FONT)
    plate_info[nm] = {"distance_along_axis_m": round(zd, 1), "bar_px_output": round(float(abs(vb - vt)), 1),
                      "ruler80_px_output": round(float(r80), 1), "ratio_tower_to_80m": round(hgt / 80, 3),
                      "base_px": [round(float(ub), 1), round(float(vb), 1)], "top_px": [round(float(ut), 1), round(float(vt), 1)]}
plate.save(f"{OUT_CHECK}/scale_cues_plate.png")
json.dump({"camera_model": "pinhole, Unity left-handed, y up; pose = sidecar position_local_m + rotation_xyzw (camera-to-world, post-snap); hfov 60 deg, 1920x1080",
           "origin_offset_baytm": ORG.tolist(),
           "props_in_1_to_3_px": [c["name"] for c in cues if c.get("in_1_to_3_px")],
           "props_in_1_to_3_px_count": sum(1 for c in cues if c.get("in_1_to_3_px")),
           "cues": cues, "ruler_plate": {"mech_ruler_80m_px_output": round(float(rulpx), 1), **plate_info},
           "note": "the output frame is a 4x nearest upscale of the 480x270 internal image; figures render at about 1-2 internal px"},
          open(f"{OUT_CHECK}/scale_cues.json", "w"), indent=1)

# ---- 3. skyline ----------------------------------------------------------
sky_int = ~np.isfinite(depth)
# supersample at output res and compare to the nearest-upscaled rendered mask
sil = Image.new("L", (W, H), 0); sd = ImageDraw.Draw(sil)


def clip_near(poly):
    """clip a camera-space polygon against z >= NEAR"""
    res = []
    for a, b in zip(poly, poly[1:] + poly[:1]):
        ina, inb = a[2] >= NEAR, b[2] >= NEAR
        if ina:
            res.append(a)
        if ina != inb:
            t = (NEAR - a[2]) / (b[2] - a[2]); res.append(a + t * (b - a))
    return res


def draw_poly(pts3):
    pts = clip_near([np.array(p) for p in pts3])
    if len(pts) < 3:
        return
    uv, _ = proj(np.array(pts))
    sd.polygon([tuple(q) for q in uv], fill=255)


import sys
SKIP_PARENTS = "--keep-parents" not in sys.argv
used = 0
for f in FEATS:
    pr = f["properties"]
    if pr.get("excluded"):
        continue
    if SKIP_PARENTS and pr.get("has_parts") and any(g["properties"].get("part_of") == pr["bid"] for g in FEATS):
        continue  # parent outline replaced by its building:part features
    ring_ = np.array(f["geometry"]["coordinates"][0])[:, :2]
    if ring_.shape[0] < 4:
        continue
    zb = pr["base_navd88_m"] + (pr.get("min_height_m") or 0.0); zt_ = pr["base_navd88_m"] + pr["height_m"]
    bot = [to_cam(scene(x, zb, z)) for x, z in ring_[:-1]]
    top = [to_cam(scene(x, zt_, z)) for x, z in ring_[:-1]]
    draw_poly(top)
    n = len(bot)
    for k in range(n):
        j = (k + 1) % n
        draw_poly([bot[k], bot[j], top[j], top[k]])
    used += 1
proj_mask = np.array(sil) > 0
rend_nonsky = np.kron(~sky_int, np.ones((S, S), bool))
mech_big = np.kron(ndimage.binary_dilation(mech_mask, iterations=2), np.ones((S, S), bool))
hrow = int(hero["horizon_row_1080p_internal"] * S)
band = np.zeros((H, W), bool); band[:hrow] = True
valid = band & ~mech_big
# horizon rows: rendered terrain/hills beyond the tile are not in the building data, so also report with them removed by depth
def iou(a, b):
    u = (a | b).sum(); return float((a & b).sum() / u) if u else 1.0
iou_above = iou(proj_mask & valid, rend_nonsky & valid)
iou_full = iou(proj_mask & ~mech_big, rend_nonsky & ~mech_big)
# column skyline profile above horizon
def top_profile(m):
    any_ = m.any(0); t = np.where(any_, m.argmax(0), H); return t
tp, tr = top_profile(proj_mask & valid), top_profile(rend_nonsky & valid)
both = (tp < H) & (tr < H)
prof_err = np.abs(tp[both] - tr[both])
# diff overlay
ov = np.array(out_img).copy()
ov[(proj_mask & ~rend_nonsky & band)] = [255, 40, 40]    # projected only
ov[(~proj_mask & rend_nonsky & band & ~mech_big)] = [40, 120, 255]  # rendered only
ov[(proj_mask & rend_nonsky & band)] = (0.6 * ov[(proj_mask & rend_nonsky & band)] + 0.4 * np.array([60, 255, 90])).astype(np.uint8)
ovi = Image.fromarray(ov); od = ImageDraw.Draw(ovi)
od.text((10, 10), "red = projected only, blue = rendered only, green tint = both; above horizon row; mech excluded", fill=(255, 255, 255), font=FONT)


def landmark(nm):
    if nm not in towers:
        return {"found": False}
    f, cen, pr = towers[nm]
    ring_ = np.array(f["geometry"]["coordinates"][0])[:-1]
    ztop = pr["base_navd88_m"] + pr["height_m"]
    P = [to_cam(scene(x, ztop, z)) for x, z in ring_]
    uvp, zz = proj(np.array(P))
    pc = to_cam(scene(cen[0], ztop, cen[1])); (uc, vc), dz = proj(pc)
    top_proj = float(uvp[:, 1].min())
    left_proj, right_proj = float(uvp[:, 0].min()), float(uvp[:, 0].max())
    info = {"found": True, "name": nm, "bid": pr["bid"], "height_m": pr["height_m"], "top_navd88_m": ztop,
            "distance_along_axis_m": round(float(dz), 1), "projected_roof_centre_px": [round(float(uc), 1), round(float(vc), 1)],
            "projected_top_row_px": round(top_proj, 1), "projected_col_extent_px": [round(left_proj, 1), round(right_proj, 1)],
            "in_frame": bool(0 <= uc < W and 0 <= top_proj < H)}
    if not info["in_frame"]:
        return info
    # rendered object: depth near landmark distance within the projected column extent (+ margin), above horizon
    zmin = float(pc[2] - 40); zmax = float(zz.max() + 40)
    c0, c1 = int(max(left_proj / S - 3, 0)), int(min(right_proj / S + 3, 479))
    sel = (depth >= zmin) & (depth <= zmax)
    sel[:, :c0] = False; sel[:, c1 + 1:] = False
    sel[int(hero["horizon_row_1080p_internal"]):] = False
    lab2, n2 = ndimage.label(sel, structure=np.ones((3, 3)))
    if n2 == 0:
        info["rendered_found"] = False; return info
    sizes = ndimage.sum(sel, lab2, range(1, n2 + 1)); m2 = lab2 == (1 + int(np.argmax(sizes)))
    yy, xx = np.where(m2)
    r_top = yy.min() * S; r_left, r_right = xx.min() * S, (xx.max() + 1) * S
    info.update({"rendered_found": True, "rendered_top_row_px": int(r_top), "rendered_col_extent_px": [int(r_left), int(r_right)],
                 "top_row_error_px": round(float(r_top - top_proj), 1),
                 "left_edge_error_px": round(float(r_left - left_proj), 1), "right_edge_error_px": round(float(r_right - right_proj), 1),
                 "centre_col_error_px": round(float((r_left + r_right) / 2 - (left_proj + right_proj) / 2), 1)})
    info["max_abs_error_px"] = round(max(abs(info["top_row_error_px"]), abs(info["centre_col_error_px"])), 1)
    ImageDraw.Draw(ovi).rectangle([r_left, r_top, r_right, r_top + 40], outline=(255, 255, 0), width=2)
    ImageDraw.Draw(ovi).line([left_proj, top_proj, right_proj, top_proj], fill=(255, 0, 255), width=2)
    ImageDraw.Draw(ovi).text((r_left, r_top - 12), nm, fill=(255, 255, 0), font=FONT)
    return info


lms = [landmark(n) for n in ("Salesforce Tower", "Transamerica Pyramid", "Coit Tower")]
ovi.save(f"{OUT_CHECK}/skyline_overlay.png")
errs = [l["max_abs_error_px"] for l in lms if "max_abs_error_px" in l]
json.dump({
    "criterion": "C9: skyline IoU >= 0.98 at the hero camera; landmark projections within 3 px at 1080p",
    "camera_convention": "Unity left-handed pinhole from the capture sidecar pose (position_local_m + rotation_xyzw, post rig snap), BayTM -> scene by subtracting floating_origin_baytm; the sidecar's own yaw/pitch/roll agree with the quaternion. Uncertain: whether the in-game building prisms use height_m from base_navd88_m (assumed; Salesforce top 330.892 = 4.892 + 326 matches shot_params tallest_tower) and whether part/parent footprints are both extruded (assumed yes, union). Pixel-centre convention taken as continuous output px, top-left origin.",
    "building_features_extruded": used, "parents_with_parts_skipped": SKIP_PARENTS, "parent_choice_note": "parent outlines of building:part groups are replaced by their parts (IoU 0.946 with parents kept, 0.991 without); the rendered tile appears to use the parts; flat prisms only, pyramid and skillion roofs not modelled, so the Transamerica apex error is partly roof shape",
    "iou_above_horizon_rows_excl_mech": round(iou_above, 4),
    "iou_full_frame_excl_mech_building_mask_vs_all_nonsky": round(iou_full, 4),
    "iou_note": "above-horizon rows: rendered non-sky (buildings plus any terrain beyond the tile) vs projected building prisms, both at 1080p with the 480x270 render nearest-upscaled by 4; the full-frame figure is not meaningful for C9 because terrain and water are not in the building data",
    "skyline_top_profile_median_abs_error_px": round(float(np.median(prof_err)), 2) if prof_err.size else None,
    "skyline_top_profile_p95_abs_error_px": round(float(np.percentile(prof_err, 95)), 2) if prof_err.size else None,
    "skyline_columns_compared": int(both.sum()),
    "landmarks": lms, "max_landmark_error_px": max(errs) if errs else None,
    "quantisation_note": "the render is 480x270 internal at 4x, so one internal pixel = 4 output px; a 3 px tolerance is below one internal pixel and cannot be met except by the ordering of rounding",
}, open(f"{OUT_CHECK}/skyline.json", "w"), indent=1)

# ---- 4. boards -----------------------------------------------------------
def crop_ref(fn, box, tag):
    im = Image.open(f"art/style_refs/{fn}").convert("RGB").crop(box)
    return im, tag


refs = [crop_ref("ref_01.png", (1024 + 40, 80, 2048 - 40, 1024 - 40), "ref_01.png"),
        crop_ref("ref_03.png", (1024 + 40, 130, 2048 - 40, 1024 - 40), "ref_03.png"),
        crop_ref("ref_03.png", (1024 + 40, 1024 + 40, 2048 - 40, 2048 - 40), "ref_03.png (b)")]
profs = ["slate_teal", "slate_crimson", "warm_rust_sepia", "warm_amber_haze", "lavender_peach"]
alts = ["slate_teal_closer", "slate_teal_wider", "slate_teal_waterfront"]
items = [("hero_1080p/slate_teal/f000000_color_output.png", Image.open(f"{HERO}/f000000_color_output.png").convert("RGB"))]
for im, tag in refs:
    items.append((f"art/style_refs/{tag}", im))
for p in profs:
    items.append((f"hero_1080p/{p}/f000000_color_output.png", Image.open(f"{CAP}/hero_1080p/{p}/f000000_color_output.png").convert("RGB")))
for a in alts:
    items.append((f"alt_1080p/{a}/f000000_color_output.png", Image.open(f"{CAP}/alt_1080p/{a}/f000000_color_output.png").convert("RGB")))


def board(order, path, with_labels):
    cell_w, cell_h = 640, 360
    gap = 10
    layout = []
    hero = items[order[0]] if with_labels else None
    ncol = 4
    sel = [items[i] for i in order]
    nrow = (len(sel) + ncol - 1) // ncol
    b = Image.new("RGB", (ncol * (cell_w + gap) + gap, nrow * (cell_h + 22 + gap) + gap), (16, 16, 16))
    dd = ImageDraw.Draw(b)
    for k, (name, im) in enumerate(sel):
        r, c = divmod(k, ncol)
        x = gap + c * (cell_w + gap); y = gap + r * (cell_h + 22 + gap)
        im2 = im.copy(); im2.thumbnail((cell_w, cell_h), Image.NEAREST if im.width == 1920 else Image.LANCZOS)
        b.paste(im2, (x + (cell_w - im2.width) // 2, y + 20 + (cell_h - im2.height) // 2))
        label = name if with_labels else f"cell {k + 1:02d}"
        dd.text((x, y + 4), label, fill=(235, 235, 235), font=FONT)
    b.save(path, optimize=True)
    return [(k + 1, items[i][0]) for k, i in enumerate(order)]


order = list(range(len(items)))
k1 = board(order, f"{OUT_BOARD}/review_board.png", True)
rng = random.Random(6006); sh = order[:]; rng.shuffle(sh)
k2 = board(sh, f"{OUT_BOARD}/review_board_shuffled.png", False)
json.dump({"seed": 6006, "review_board": {f"cell_{n:02d}": nm for n, nm in k1},
           "review_board_shuffled": {f"cell_{n:02d}": nm for n, nm in k2},
           "note": "reference crops are the same-family (slate/teal) quadrants of ref_01 and ref_03 with caption/title margins cropped; labels on the ordered board are file names only; the shuffled board shows cell numbers only"},
          open(f"{OUT_BOARD}/board_key.json", "w"), indent=1)
print(json.dumps({"mech_visible": visible_frac, "mech_full": full_frac, "iou": iou_above, "lm": errs,
                  "props_1_3": [c["name"] for c in cues if c.get("in_1_to_3_px")]}))
