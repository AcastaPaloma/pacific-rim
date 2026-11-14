"""Measure the greybox and write evidence/T-007/dimensions.json (T-007 C10-C20, C23, C24).

Run (headless):
  /Applications/Blender.app/Contents/MacOS/Blender -b art/models/mech_frame_balanced/mech_frame_balanced.blend \
      --python Pipelines/blender/mech_frame_balanced/measure.py

All reported coordinates are in the mech_frame@v1 frame (x right, y up, z forward, metres).
Widths are measured on a 0.05 m raster of the front orthographic silhouette (rest pose).
"""
import json
import math
import os
import subprocess
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mf_common as C  # noqa: E402

FRAME = C.load_frame()
SEG = C.segments_by_id(FRAME)
G = 9.81
RES = 0.05  # raster cell (m)


def git_head():
    try:
        return subprocess.check_output(["git", "-C", C.PROJECT, "rev-parse", "--short", "HEAD"], text=True).strip()
    except Exception:
        return None


def parts():
    return {C.seg_of(o.name): o for o in bpy.data.objects if o.type == "MESH" and C.seg_of(o.name) in SEG}


def world_bm(ob):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bm.transform(ob.matrix_world)
    return bm


def frame_verts(bm):
    return np.array([C.b2f(v.co) for v in bm.verts])


def tri_frame(bm):
    """triangles in frame coords, plus material index per triangle"""
    tris, mats = [], []
    for f in bm.faces:
        vs = [C.b2f(v.co) for v in f.verts]
        for i in range(1, len(vs) - 1):
            tris.append((vs[0], vs[i], vs[i + 1]))
            mats.append(f.material_index)
    return np.array(tris), mats


def volume_centroid(tris):
    v = 0.0
    c = np.zeros(3)
    for a, b, d in tris:
        dv = np.dot(a, np.cross(b, d)) / 6.0
        v += dv
        c += dv * (a + b + d) / 4.0
    return abs(v), c / v


def raster_front(tris_list, x0=-25, x1=25, y0=-1, y1=82, axis=0):
    nx, ny = int((x1 - x0) / RES), int((y1 - y0) / RES)
    img = np.zeros((ny, nx), bool)
    xs = x0 + (np.arange(nx) + 0.5) * RES
    ys = y0 + (np.arange(ny) + 0.5) * RES
    for tris in tris_list:
        for t in tris:
            p = t[:, [axis, 1]]
            mn, mx = p.min(0), p.max(0)
            i0, i1 = np.searchsorted(xs, mn[0]), np.searchsorted(xs, mx[0])
            j0, j1 = np.searchsorted(ys, mn[1]), np.searchsorted(ys, mx[1])
            if i1 <= i0 or j1 <= j0:
                continue
            X, Y = np.meshgrid(xs[i0:i1], ys[j0:j1])
            (ax, ay), (bx, by), (cx, cy) = p
            den = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
            if abs(den) < 1e-12:
                continue
            l1 = ((by - cy) * (X - cx) + (cx - bx) * (Y - cy)) / den
            l2 = ((cy - ay) * (X - cx) + (ax - cx) * (Y - cy)) / den
            l3 = 1 - l1 - l2
            img[j0:j1, i0:i1] |= (l1 >= -1e-9) & (l2 >= -1e-9) & (l3 >= -1e-9)
    return img, xs, ys


def band_extent(img, xs, ys, ya, yb):
    rows = img[(ys >= ya) & (ys <= yb)]
    cols = np.where(rows.any(0))[0]
    if len(cols) == 0:
        return None
    return float(xs[cols[0]] - RES / 2), float(xs[cols[-1]] + RES / 2)


def row_widths(img, xs, ys, ya, yb):
    out = []
    for j in np.where((ys >= ya) & (ys <= yb))[0]:
        c = np.where(img[j])[0]
        if len(c):
            out.append((float(ys[j]), float((c[-1] - c[0] + 1) * RES)))
    return out


def inside(bvh, p):
    """ray-parity point-in-mesh test (frame point)"""
    q = Vector(C.f2b(p))
    hits, o, d = 0, q.copy(), Vector((0.3137, 0.1219, 0.9415)).normalized()
    for _ in range(64):
        loc, nor, idx, dist = bvh.ray_cast(o, d)
        if loc is None:
            break
        hits += 1
        o = loc + d * 1e-4
    return hits % 2 == 1


def main():
    P = parts()
    assert len(P) == 15, sorted(P)
    out = {"schema": "mech_dimensions@v1", "model": os.path.relpath(bpy.data.filepath, C.PROJECT),
           "config": os.path.relpath(C.FRAME_JSON, C.PROJECT), "frame": FRAME["frame"]["axes"],
           "script": "Pipelines/blender/mech_frame_balanced/measure.py", "commit_at_run": git_head(),
           "parts": {}, "joints": [], "whole": {}, "ratios": {}, "feet": {}, "checks": {}}
    bms, tris, vols, cents, bvhs = {}, {}, {}, {}, {}
    total_tris = 0
    for sid, ob in P.items():
        bm = world_bm(ob)
        bms[sid] = bm
        t, mats = tri_frame(bm)
        tris[sid] = t
        v, c = volume_centroid(t)
        vols[sid], cents[sid] = v, c
        bvhs[sid] = BVHTree.FromBMesh(bm)
        nonman = sum(1 for e in bm.edges if not e.is_manifold)
        bound = sum(1 for e in bm.edges if e.is_boundary)
        # convex hull ratio
        hb = bmesh.new()
        for bv in bm.verts:
            hb.verts.new(bv.co)
        res = bmesh.ops.convex_hull(hb, input=hb.verts)
        bmesh.ops.delete(hb, geom=list(set(res["geom_interior"]) | set(res["geom_unused"])), context="VERTS")
        bmesh.ops.recalc_face_normals(hb, faces=hb.faces)
        hv = hb.calc_volume(signed=False)
        hb.free()
        fv = frame_verts(bm)
        ntri = len(t)
        total_tris += ntri
        org = C.b2f(ob.matrix_world.translation)
        seg = SEG[sid]
        dev = float(np.linalg.norm(np.array(org) - np.array(seg["proximal_joint"])))
        out["parts"][ob.name] = {
            "segment_id": sid, "parent": C.part_name(seg["parent"]) if seg["parent"] else None,
            "bbox_min": [round(float(x), 3) for x in fv.min(0)], "bbox_max": [round(float(x), 3) for x in fv.max(0)],
            "origin": [round(x, 4) for x in org], "config_proximal_joint": seg["proximal_joint"],
            "origin_deviation_m": round(dev, 4),
            "closed_manifold": nonman == 0 and bound == 0, "non_manifold_edges": nonman,
            "volume_m3": round(v, 2), "centroid": [round(float(x), 3) for x in c],
            "convex_hull_ratio": round(hv / v, 3), "triangles": ntri,
            "materials": [m.name for m in ob.data.materials],
        }
    Vt = sum(vols.values())
    com = sum(vols[s] * cents[s] for s in vols) / Vt

    # joints: origin of child vs config, and segment lengths joint-to-joint
    for j in FRAME["joints"]:
        ch = P[j["child"]]
        org = np.array(C.b2f(ch.matrix_world.translation))
        cfg = np.array(j["position"])
        out["joints"].append({"id": j["id"], "parent": C.part_name(j["parent"]), "child": ch.name,
                              "config": j["position"], "measured": [round(float(x), 4) for x in org],
                              "deviation_m": round(float(np.linalg.norm(org - cfg)), 4),
                              "pivot_inside": [C.part_name(s) for s in P if inside(bvhs[s], cfg)]})
    seglen = {}
    for sid, seg in SEG.items():
        kids = [s for s in SEG if SEG[s]["parent"] == sid]
        o = np.array(C.b2f(P[sid].matrix_world.translation))
        bb0, bb1 = out["parts"][P[sid].name]["bbox_min"], out["parts"][P[sid].name]["bbox_max"]
        kind = seg["kind"]
        if kind in ("upper_arm", "forearm", "thigh", "shank"):
            child = [k for k in kids][0]
            meas = float(np.linalg.norm(np.array(C.b2f(P[child].matrix_world.translation)) - o)); how = "joint-to-joint"
        elif kind == "torso":
            meas = float(C.b2f(P["head"].matrix_world.translation)[1] - o[1]); how = "waist to neck joint"
        elif kind == "pelvis":
            meas = float(C.b2f(P["torso"].matrix_world.translation)[1] - o[1]); how = "root to waist joint"
        elif kind == "head":
            meas = float(bb1[1] - o[1]); how = "neck joint to head top"
        elif kind == "hand":
            meas = float(o[1] - bb0[1]); how = "wrist joint to knuckle face"
        else:  # foot: sole length
            meas = float(bb1[2] - bb0[2]); how = "sole length heel to toe"
        cfg = seg["length_m"]
        seglen[sid] = {"measured_m": round(meas, 3), "config_m": cfg, "deviation_pct": round(100 * (meas - cfg) / cfg, 2), "method": how}
        out["parts"][P[sid].name]["segment_length"] = seglen[sid]

    # mirror symmetry (vertex sets) and origins
    mirror = {}
    for sid in SEG:
        if sid.endswith("_L"):
            r = sid[:-2] + "_R"
            a = frame_verts(bms[sid]) * [-1, 1, 1]
            b = frame_verts(bms[r])
            d_ab = np.sqrt(((a[:, None, :] - b[None, :, :]) ** 2).sum(-1))
            vd = float(max(d_ab.min(1).max(), d_ab.min(0).max()))   # symmetric Hausdorff over vertices
            oa = np.array(C.b2f(P[sid].matrix_world.translation)) * [-1, 1, 1]
            ob_ = np.array(C.b2f(P[r].matrix_world.translation))
            mirror[sid[:-2]] = {"origin_mirror_dev_m": round(float(np.linalg.norm(oa - ob_)), 4), "vertex_mirror_max_dev_m": vd}

    # front silhouette rasters
    allimg, xs, ys = raster_front([tris[s] for s in P])
    def img_of(sids):
        return raster_front([tris[s] for s in sids])[0]
    H = float(max(out["parts"][P[s].name]["bbox_max"][1] for s in P))
    sole_y = float(min(out["parts"][P[s].name]["bbox_min"][1] for s in P))
    top_set = ["torso"]   # C11 ruling (b), spec v1.1: shoulder width on torso and shoulder blocks only
    shoulder_ext = band_extent(img_of(top_set), xs, ys, Y_SH_BAND0, H)
    hip_ext = band_extent(img_of(["pelvis", "thigh_L", "thigh_R"]), xs, ys, SEG["thigh_L"]["proximal_joint"][1] - 4, SEG["thigh_L"]["proximal_joint"][1] + 4)
    shoulder_w = shoulder_ext[1] - shoulder_ext[0]
    hip_w = hip_ext[1] - hip_ext[0]

    def part_w(sid, ya=-1, yb=82):
        e = band_extent(img_of([sid]), xs, ys, ya, yb)
        return e[1] - e[0]
    ua_w, fa_w, fist_w = part_w("upper_arm_L"), part_w("forearm_L"), part_w("hand_L")
    head_v = frame_verts(bms["head"])
    head_w = float(head_v[:, 0].max() - head_v[:, 0].min())
    # head height: the cap (head part above the torso collar top)
    torso_v = frame_verts(bms["torso"])
    collar_top = float(torso_v[np.abs(torso_v[:, 0]) <= 4.5][:, 1].max())   # top of the neck cowl
    head_h = H - collar_top
    # chest shell top (shoulder line): primary-material torso vertices with |x| <= 8.5
    pm = P["torso"].data.materials
    prim = [i for i, m in enumerate(pm) if m.name == C.MAT_PREFIX + "primary"]
    # shoulder line = top of the chest shell: first torso hit of a downward ray at x = +-4.8 m, z = +1 m
    # (between the neck cowl and the shoulder-block pedestals)
    hits = []
    for sx in (-4.8, 4.8):
        loc, _n, _i, _d = bvhs["torso"].ray_cast(Vector(C.f2b((sx, 120.0, 1.0))), Vector(C.f2b((0, -1, 0))))
        hits.append(C.b2f(loc)[1])
    chest_top = max(hits)
    # legs
    kn_y, hip_y, an_y = SEG["shank_L"]["proximal_joint"][1], SEG["thigh_L"]["proximal_joint"][1], SEG["foot_L"]["proximal_joint"][1]
    th_rows = row_widths(img_of(["thigh_L"]), xs, ys, kn_y + 3.6, hip_y - 2.0)
    sh_rows = row_widths(img_of(["shank_L"]), xs, ys, an_y + 3.4, kn_y - 0.5)
    side_th = row_widths(raster_front([tris["thigh_L"]], axis=2)[0], xs, ys, kn_y + 3.6, hip_y - 2.0)
    side_sh = row_widths(raster_front([tris["shank_L"]], axis=2)[0], xs, ys, an_y + 3.4, kn_y - 0.5)
    th_rows = [(y, min(w, dict(side_th).get(y, w))) for y, w in th_rows]   # narrower of front and side view
    sh_rows = [(y, min(w, dict(side_sh).get(y, w))) for y, w in sh_rows]
    th_min = min(w for _, w in th_rows)
    sh_min = min(w for _, w in sh_rows)
    th_mid = dict(th_rows)[min(dict(th_rows), key=lambda y: abs(y - (hip_y + kn_y) / 2))]
    sh_mid = dict(sh_rows)[min(dict(sh_rows), key=lambda y: abs(y - (kn_y + an_y) / 2))]
    knee_drum_w = part_w("shank_L", kn_y - 0.5, kn_y + 0.5)
    hip_ball_w = part_w("thigh_L", hip_y - 0.5, hip_y + 0.5)
    ankle_w = part_w("foot_L", an_y - 0.5, an_y + 0.5)
    foot_v = frame_verts(bms["foot_L"])
    foot_len = float(foot_v[:, 2].max() - foot_v[:, 2].min())
    foot_w = float(foot_v[:, 0].max() - foot_v[:, 0].min())
    fl, fr = frame_verts(bms["foot_L"]), frame_verts(bms["foot_R"])
    stance = float(max(fl[:, 0].max(), fr[:, 0].max()) - min(fl[:, 0].min(), fr[:, 0].min()))

    # feet: sole polygons (faces pointing down at y ~ 0)
    for sid in ("foot_L", "foot_R"):
        bm = bms[sid]
        area, cx, cz, ys_ = 0.0, 0.0, 0.0, []
        for f in bm.faces:
            n = C.b2f(f.normal)
            if n[1] < -0.999:
                pts = [C.b2f(v.co) for v in f.verts]
                ys_ += [p[1] for p in pts]
                a = f.calc_area()
                cc = C.b2f(f.calc_center_median())
                # polygon area centroid
                tri_c = np.zeros(3); tri_a = 0.0
                for i in range(1, len(pts) - 1):
                    ta = 0.5 * np.linalg.norm(np.cross(np.subtract(pts[i], pts[0]), np.subtract(pts[i + 1], pts[0])))
                    tri_a += ta; tri_c += ta * (np.array(pts[0]) + pts[i] + pts[i + 1]) / 3
                area += a; cx += (tri_c / tri_a)[0] * a; cz += (tri_c / tri_a)[2] * a
        cx /= area; cz /= area
        ank = SEG[sid]["proximal_joint"]
        off = math.hypot(ank[0] - cx, ank[2] - cz)
        fv = frame_verts(bm)
        L = float(fv[:, 2].max() - fv[:, 2].min()); W = float(fv[:, 0].max() - fv[:, 0].min())
        out["feet"][sid] = {"sole_area_m2": round(area, 3), "sole_y_min": round(min(ys_), 4), "sole_y_max": round(max(ys_), 4),
                            "sole_centroid_xz": [round(cx, 3), round(cz, 3)], "ankle_xz": [ank[0], ank[2]],
                            "ankle_offset_m": round(off, 3), "ankle_offset_pct_foot_length": round(100 * off / L, 2),
                            "length_m": round(L, 3), "width_m": round(W, 3), "length_over_width": round(L / W, 3),
                            "static_pressure_full_mass_one_foot_kPa": round(FRAME["total_mass_kg"] * G / area / 1000, 1),
                            "static_pressure_two_feet_kPa": round(FRAME["total_mass_kg"] * G / (2 * area) / 1000, 1)}
    aL, aR = out["feet"]["foot_L"]["sole_area_m2"], out["feet"]["foot_R"]["sole_area_m2"]

    # fist striking face: hand faces pointing down (knuckle face in rest pose)
    knuckle = sum(f.calc_area() for f in bms["hand_L"].faces if C.b2f(f.normal)[1] < -0.999)

    # volumes and fractions
    vol_rows = {}
    for sid in SEG:
        frac = vols[sid] / Vt
        mfrac = SEG[sid]["mass_kg"] / FRAME["total_mass_kg"]
        vol_rows[sid] = {"volume_m3": round(vols[sid], 1), "volume_fraction": round(frac, 4), "mass_fraction": round(mfrac, 4),
                         "ratio": round(frac / mfrac, 3), "exempt_under_2pct": mfrac < 0.02,
                         "within_30pct": abs(frac / mfrac - 1) <= 0.30}
    left = sum(vols[s] for s in SEG if s.endswith("_L"))
    right = sum(vols[s] for s in SEG if s.endswith("_R"))
    torso_cx = cents["torso"][0]
    rho = FRAME["mean_hull_density_kg_m3"]

    out["whole"] = {
        "height_H_m": round(H, 3), "sole_min_y_m": round(sole_y, 4),
        "shoulder_line_chest_top_m": round(float(chest_top), 3), "deck_marker_m": 67.0,
        "deck_marker_crosses_torso": out["parts"][P["torso"].name]["bbox_min"][1] < 67 < chest_top,
        "head_bottom_m": round(float(head_v[:, 1].min()), 3),
        "shoulder_width_m": round(shoulder_w, 3), "shoulder_band_y_from": Y_SH_BAND0,
        "hip_width_m": round(hip_w, 3), "stance_outer_m": round(stance, 3),
        "head_height_m": round(head_h, 3), "head_width_m": round(head_w, 3), "neck_collar_top_m": round(collar_top, 3),
        "upper_arm_max_width_m": round(ua_w, 3), "forearm_max_width_m": round(fa_w, 3), "fist_width_m": round(fist_w, 3),
        "fist_knuckle_face_m2": round(knuckle, 2),
        "leg_length_sole_to_hip_m": hip_y - sole_y,
        "thigh_min_width_m": round(th_min, 3), "thigh_mid_width_m": round(th_mid, 3), "hip_ball_width_m": round(hip_ball_w, 3),
        "shank_min_width_m": round(sh_min, 3), "shank_mid_width_m": round(sh_mid, 3), "knee_drum_width_m": round(knee_drum_w, 3),
        "ankle_drum_width_m": round(ankle_w, 3),
        "foot_length_m": round(foot_len, 3), "foot_width_m": round(foot_w, 3),
        "total_volume_m3": round(Vt, 1), "volume_centroid": [round(float(x), 3) for x in com],
        "config_com": FRAME["com_rest"], "lr_volume_diff_pct": round(100 * abs(left - right) / (0.5 * (left + right)), 3),
        "torso_centroid_x_m": round(float(torso_cx), 3),
        "implied_mass_t": {"config_density_%.1f" % rho: round(Vt * rho / 1000, 1), "rho_1000": round(Vt / 1, 1), "rho_2500": round(Vt * 2.5, 1)},
        "triangles_total": total_tris, "material_slots": sorted({m.name for o in P.values() for m in o.data.materials}),
        "textures": len(bpy.data.images),
        "human_height_m": 1.8, "foot_length_over_human": round(foot_len / 1.8, 2),
    }
    # side-view depth of the chest shell vs the pelvis (eval-style C16 companion metric), arm stand-off from the thigh
    chest_band = torso_v[(torso_v[:, 1] >= 50.0) & (torso_v[:, 1] <= chest_top)]
    pel_v = frame_verts(bms["pelvis"])
    out["whole"]["chest_side_depth_m"] = round(float(chest_band[:, 2].max() - chest_band[:, 2].min()), 3)
    out["whole"]["pelvis_side_depth_m"] = round(float(pel_v[:, 2].max() - pel_v[:, 2].min()), 3)
    out["ratios"]["chest_side_depth_over_H"] = round(out["whole"]["chest_side_depth_m"] / H, 4)
    arm_in = min(float(np.abs(frame_verts(bms[s_])[:, 0]).min()) for s_ in ("forearm_L", "hand_L"))
    thigh_out = float(np.abs(frame_verts(bms["thigh_L"])[:, 0]).max())
    out["whole"]["arm_to_thigh_clearance_m"] = round(arm_in - thigh_out, 3)
    out["checks"]["C16_chest_deeper_than_pelvis_and_ge_0_20H"] = (out["whole"]["chest_side_depth_m"] >= 0.20 * H
                                                                 and out["whole"]["chest_side_depth_m"] > out["whole"]["pelvis_side_depth_m"])
    out["volume_fractions"] = vol_rows
    out["mirror"] = mirror
    out["ratios"] = {
        "shoulder_width_over_H": round(shoulder_w / H, 4), "shoulder_over_hip": round(shoulder_w / hip_w, 3),
        "head_height_over_H": round(head_h / H, 4), "head_width_over_shoulder": round(head_w / shoulder_w, 3),
        "forearm_over_upper_arm_width": round(fa_w / ua_w, 3), "forearm_over_fist_width": round(fa_w / fist_w, 3),
        "fist_width_over_H": round(fist_w / H, 4), "leg_length_over_H": round((hip_y - sole_y) / H, 4),
        "thigh_min_over_H": round(th_min / H, 4), "shank_min_over_H": round(sh_min / H, 4),
        "foot_length_over_H": round(foot_len / H, 4), "foot_width_over_H": round(foot_w / H, 4),
        "foot_width_over_shank": round(foot_w / sh_min, 3), "stance_over_shoulder": round(stance / shoulder_w, 3),
        "com_height_over_H": round(float(com[1]) / H, 4), "com_minus_config_over_H": round((float(com[1]) - FRAME["com_rest"][1]) / H, 4),
    }
    out["checks"] = {
        "C10_height": abs(H - 80) <= 0.5, "C10_shoulder_line": 62 <= chest_top <= 70, "C10_foot_ge_7_humans": foot_len >= 7 * 1.8,
        "C11_shoulder_over_H": 0.30 <= shoulder_w / H <= 0.40, "C11_shoulder_over_hip": 1.4 <= shoulder_w / hip_w <= 2.0,
        "C11_head_h": 0.05 <= head_h / H <= 0.09, "C11_head_w": head_w / shoulder_w <= 0.30,
        "C11_forearm": fa_w >= 1.1 * ua_w and fa_w >= fist_w, "C11_fist": fist_w / H >= 0.055 and knuckle >= 6,
        "C12_leg": 0.44 <= (hip_y - sole_y) / H <= 0.53, "C12_thigh_min": th_min >= 0.10 * H, "C12_shank_min": sh_min >= 0.08 * H,
        "C12_no_hourglass": th_mid >= min(th_min, hip_ball_w) - 1e-6 and sh_mid >= max(knee_drum_w, ankle_w) - 0.05,
        "C12_foot": 0.16 <= foot_len / H <= 0.21 and 0.10 <= foot_w / H <= 0.14 and foot_w >= 1.2 * sh_min,
        "C12_stance": 0.70 <= stance / shoulder_w <= 0.90,
        "C13_com": abs(float(com[1]) - FRAME["com_rest"][1]) <= 0.03 * H, "C13_midplane": 100 * abs(left - right) / (0.5 * (left + right)) < 1,
        "C14_area": all(abs(out["feet"][f]["sole_area_m2"] - 150) <= 15 for f in out["feet"]) and abs(aL - aR) / aL <= 0.01,
        "C14_flat": all(abs(out["feet"][f]["sole_y_max"]) <= 0.05 for f in out["feet"]),
        "C14_ratio": all(1.3 <= out["feet"][f]["length_over_width"] <= 2.2 for f in out["feet"]),
        "C14_ankle": all(out["feet"][f]["ankle_offset_pct_foot_length"] <= 15 for f in out["feet"]),
        "C18_joints": all(j["deviation_m"] <= 0.25 for j in out["joints"]) and all(p["origin_deviation_m"] <= 0.25 for p in out["parts"].values()),
        "C18_lengths": all(abs(s["deviation_pct"]) <= 2 for s in seglen.values()),
        "C18_mirror": all(m["origin_mirror_dev_m"] <= 0.01 and m["vertex_mirror_max_dev_m"] is not None and m["vertex_mirror_max_dev_m"] <= 0.01 for m in mirror.values()),
        "C19_closed": all(p["closed_manifold"] for p in out["parts"].values()),
        "C19_volume": 5000 <= Vt <= 10000, "C19_mass": 6000 <= Vt * rho / 1000 <= 15000,
        "C19_fractions": all(r["within_30pct"] or r["exempt_under_2pct"] for r in vol_rows.values()),
        "C20_parts": len(P) == 15,
        "C23_hull": all(p["convex_hull_ratio"] <= 1.6 for p in out["parts"].values()),
        "C24_tris": total_tris <= 20000, "C24_slots": len(out["whole"]["material_slots"]) <= 4 and len(bpy.data.images) == 0,
        "C15_pivots_housed": all(len(j["pivot_inside"]) >= 1 for j in out["joints"]),
    }
    os.makedirs(C.EVIDENCE, exist_ok=True)
    with open(os.path.join(C.EVIDENCE, "dimensions.json"), "w") as f:
        json.dump(out, f, indent=1, default=lambda o: o.item() if hasattr(o, "item") else str(o))
    print("CHECKS", json.dumps({k: bool(v) for k, v in out["checks"].items()}))
    print("RATIOS", json.dumps(out["ratios"]))
    print("WHOLE", json.dumps({k: out["whole"][k] for k in ("height_H_m", "shoulder_width_m", "hip_width_m", "total_volume_m3", "triangles_total", "head_height_m", "chest_side_depth_m", "pelvis_side_depth_m", "arm_to_thigh_clearance_m", "forearm_max_width_m", "upper_arm_max_width_m", "fist_width_m")}))
    print("FRAC", json.dumps({k: v["ratio"] for k, v in vol_rows.items()}))


Y_SH_BAND0 = 60.0  # shoulder band: widest extent of torso + upper arms + head above 60 m

if __name__ == "__main__":
    main()
