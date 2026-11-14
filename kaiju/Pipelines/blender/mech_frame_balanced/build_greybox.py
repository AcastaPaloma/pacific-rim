"""Build the Balanced-chassis mech greybox (T-007 Phase B) from mech_frame@v1.

Run (headless):
  /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup \
      --python Pipelines/blender/mech_frame_balanced/build_greybox.py

Writes art/models/mech_frame_balanced/mech_frame_balanced.{blend,fbx}.
15 rigid parts (one per mech_frame@v1 segment, named MechBalanced_<segment id>), each a
single closed manifold shell made by boolean union of convex primitives, origin at its
proximal joint centre, plus named socket empties. Geometry is authored in the mech frame
(x right, y up, z forward) relative to the joint positions read from the JSON; see
mf_common.py for the axis mapping and DESIGN_NOTE.md for the forms.
"""
import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mf_common as C  # noqa: E402

FRAME = C.load_frame()
SEG = C.segments_by_id(FRAME)
PAL = C.load_palette()

# ---------------------------------------------------------------- joints from the JSON
def pj(seg_id):
    return SEG[seg_id]["proximal_joint"]

AX_ARM = abs(pj("upper_arm_L")[0])      # 13.5 shoulder/elbow/wrist |x|
AX_LEG = abs(pj("thigh_L")[0])          # 6.5 hip/knee/ankle |x|
Y_ROOT = pj("pelvis")[1]                # 39.0
Y_WAIST = pj("torso")[1]                # 48.5
Y_NECK = pj("head")[1]                  # 71.0
Y_TOP = SEG["head"]["distal_end"][1]    # 80.0
Y_SH = pj("upper_arm_L")[1]             # 66.0
Y_EL = pj("forearm_L")[1]               # 51.0
Y_WR = pj("hand_L")[1]                  # 38.0
Y_HAND_END = SEG["hand_L"]["distal_end"][1]  # 30.5
Y_HIP = pj("thigh_L")[1]                # 42.0
Y_KN = pj("shank_L")[1]                 # 23.0
Y_AN = pj("foot_L")[1]                  # 4.0
SOLE = FRAME["foot_sole"]               # 15 x 10, heel -5.25, toe 9.75
STANCE_HALF = FRAME["stance"]["neutral_stance_width_m"] / 2.0  # 6.5

GAP = 0.35          # joint clearance between a housing and the neighbouring part
N_CYL = 12          # facets on drums and columns (silhouette-first, low poly)

# housing radii (design choices, see EVIDENCE.md C15/C21)
R_SHOULDER_BALL = 2.3
R_ELBOW_DRUM = 2.4
R_WRIST_DRUM = 1.9
R_HIP_BALL = 3.8
R_KNEE_DRUM = 2.8
R_ANKLE_DRUM = 2.4
R_WAIST_BALL = 3.0


# ---------------------------------------------------------------- primitives (frame coords)
def box(x0, x1, y0, y1, z0, z1):
    return [(x, y, z) for x in (x0, x1) for y in (y0, y1) for z in (z0, z1)]


def ring_x(x, yc, zc, r, n=N_CYL):
    return [(x, yc + r * math.cos(2 * math.pi * (i + 0.5) / n), zc + r * math.sin(2 * math.pi * (i + 0.5) / n)) for i in range(n)]


def cyl_x(yc, zc, r, x0, x1, n=N_CYL):
    return ring_x(x0, yc, zc, r, n) + ring_x(x1, yc, zc, r, n)


def cyl_y(xc, zc, r, y0, y1, n=N_CYL):
    pts = []
    for y in (y0, y1):
        pts += [(xc + r * math.cos(2 * math.pi * (i + 0.5) / n), y, zc + r * math.sin(2 * math.pi * (i + 0.5) / n)) for i in range(n)]
    return pts


def sphere(c, r, nseg=12, nring=7):
    pts = [(c[0], c[1] + r, c[2]), (c[0], c[1] - r, c[2])]
    for j in range(1, nring):
        phi = math.pi * j / nring
        for i in range(nseg):
            th = 2 * math.pi * (i + 0.5) / nseg
            pts.append((c[0] + r * math.sin(phi) * math.cos(th), c[1] + r * math.cos(phi), c[2] + r * math.sin(phi) * math.sin(th)))
    return pts


def xr(s, a, b):
    """x interval from |x| magnitudes a..b on side s (-1 = L, +1 = R)"""
    v = sorted((s * a, s * b))
    return v[0], v[1]


def chamfer_box(x0, x1, y0, y1, z0, z1, c):
    """box with every edge chamfered by c (convex hull of three inset boxes)"""
    return (box(x0 + c, x1 - c, y0, y1, z0 + c, z1 - c) + box(x0, x1, y0 + c, y1 - c, z0 + c, z1 - c)
            + box(x0 + c, x1 - c, y0 + c, y1 - c, z0, z1))


# ---------------------------------------------------------------- part recipes
# each recipe returns (pieces, cutters); piece = (points, role); cutter = points
def recipe_pelvis():
    pieces = [
        # central core between the thighs (keel of the chest continued down), bottom edges chamfered
        (box(-2.15, 2.15, Y_ROOT + 1.6, Y_WAIST, -4.7, 4.5) + box(-1.3, 1.3, Y_ROOT, Y_ROOT + 0.01, -4.0, 3.8), "primary"),
        # back plate (hip actuator housing behind the thigh balls, clear of hip flexion)
        (chamfer_box(-8.0, 8.0, Y_HIP - 2.5, Y_WAIST, -9.4, -4.55, 0.4), "primary"),
    ]
    cutters = [sphere((0, Y_WAIST, 0), R_WAIST_BALL + GAP, 16, 9)]
    for s in (-1, 1):
        cutters.append(sphere((s * AX_LEG, Y_HIP, 0), R_HIP_BALL + GAP, 16, 9))
    return pieces, cutters


def recipe_torso():
    yb, yt = 53.0, 69.5   # chest wedge bottom / top (shoulder line)
    chest = [(-10, yt, -5), (10, yt, -5), (10, yt, 3), (-10, yt, 3), (0, yt, 6),
             (-5, yb, -4), (5, yb, -4), (5, yb, 2), (-5, yb, 2), (0, yb, 4)]
    pieces = [
        (chest, "primary"),
        (cyl_y(0, 0, R_WAIST_BALL, Y_WAIST, yb + 0.5), "joint"),            # waist column
        (sphere((0, Y_WAIST, 0), R_WAIST_BALL), "joint"),                   # waist ball (housing)
        (cyl_y(0, -0.3, 3.0, yt - 0.5, Y_NECK + 2.8), "joint"),            # neck collar ring
        (chamfer_box(-10.0, 10.0, yt - 0.5, 72.0, -5.5, -1.5, 0.3), "primary"),  # back yoke joining the shoulder blocks
    ]
    for s in (-1, 1):
        a = xr(s, 8.0, AX_ARM - R_SHOULDER_BALL - 0.2 + 0.0)
        pieces.append((cyl_x(Y_SH, 0, 1.8, a[0], a[1]), "joint"))           # shoulder axle stub
        p = xr(s, 9.0, 12.0)
        pieces.append((box(p[0], p[1], yt - 1.0, 71.7, -4.2, -1.8), "primary"))  # block post
        b = xr(s, 9.8, AX_ARM + R_SHOULDER_BALL)
        pieces.append((chamfer_box(b[0], b[1], 71.5, 77.0, -5.5, -0.5, 0.35), "secondary"))  # shoulder block
    # accent: one small panel on the mech's left (-x) chest face, ~2.2 x 2.2 m
    y = 64.0
    t = (y - yb) / (yt - yb)
    zk, w, zs = 4 + 2 * t, 5 + 5 * t, 2 + 1 * t
    d = Vector((-w, 0, zs - zk)).normalized()
    n = Vector((-d.z, 0, d.x))
    if n.z < 0:
        n = -n
    c = Vector((0, y, zk)) + d * (0.55 * Vector((-w, 0, zs - zk)).length)
    acc = []
    for a in (-1.1, 1.1):
        for b in (-1.1, 1.1):
            for e in (-0.4, 0.2):
                q = c + d * a + Vector((0, b, 0)) + n * e
                acc.append(tuple(q))
    pieces.append((acc, "accent"))
    cutters = [cyl_y(0, 0, 1.6 + GAP, yt - 0.01, Y_NECK + 3.5)]   # neck hole through the collar
    for s_ in (-1, 1):   # bevel the outer-bottom edge of each shoulder block: clearance for 150 deg abduction
        cutters.append([(s_ * x, y, z) for x, y in ((13.4, 71.0), (17.0, 71.0), (17.0, 76.4)) for z in (-6.0, 0.0)])
    return pieces, cutters


def recipe_head():
    pieces = [
        (cyl_y(0, 0, 1.6, Y_NECK - 0.9, 75.0), "joint"),   # neck (housing of the neck pivot)
        ([(-2.6, 74.6, -2.4), (2.6, 74.6, -2.4), (2.6, 74.6, 1.8), (-2.6, 74.6, 1.8),
          (-3.2, 75.6, -3.2), (3.2, 75.6, -3.2), (3.2, 75.6, 2.45), (-3.2, 75.6, 2.45),
          (-3.2, 78.6, -3.2), (3.2, 78.6, -3.2), (3.2, 78.6, 2.1), (-3.2, 78.6, 2.1),
          (-2.8, Y_TOP, -2.8), (2.8, Y_TOP, -2.8), (2.8, Y_TOP, 0.9), (-2.8, Y_TOP, 0.9)], "primary"),  # flat sensor cap
        (box(-2.4, 2.4, 76.6, 77.6, 1.9, 2.6), "joint"),   # one horizontal sensor slit band
    ]
    return pieces, []


def recipe_upper_arm(s):
    ax = AX_ARM
    bx = xr(s, ax - 2.3, ax + 2.1)
    pieces = [
        (sphere((s * ax, Y_SH, 0), R_SHOULDER_BALL), "joint"),
        (chamfer_box(bx[0], bx[1], Y_EL + R_ELBOW_DRUM + GAP, Y_SH - 2.0, -3.5, 1.5, 0.3), "primary"),
    ]
    for a, b in ((ax - 1.9, ax - 0.7), (ax + 0.4, ax + 1.6)):   # elbow rams, upper half (back side)
        r = xr(s, a, b)
        pieces.append((box(r[0], r[1], Y_EL + 3.0, Y_EL + 11.0, -4.8, -3.3), "joint"))
    return pieces, []


def recipe_forearm(s):
    ax = AX_ARM
    P = C.FOREARM_PLATE
    bx = xr(s, ax - 2.85, P["x_in"])
    pieces = [
        (cyl_x(Y_EL, 0, R_ELBOW_DRUM, bx[0], bx[1]), "joint"),
        (chamfer_box(bx[0], bx[1], Y_WR + R_WRIST_DRUM + GAP, Y_EL, -4.2, 0.6, 0.3), "primary"),
        # hardpoint plate on the outer face (off-white), slightly proud
        (box(*xr(s, P["x_in"] - 0.2, P["x_out"]), Y_EL + P["y0_rel"], Y_EL + P["y1_rel"], P["z0"], P["z1"]), "secondary"),
    ]
    for a, b in ((ax - 1.9, ax - 0.7), (ax + 0.4, ax + 1.6)):   # elbow rams, lower half spanning the joint line
        r = xr(s, a, b)
        pieces.append((box(r[0], r[1], Y_WR + 5.0, Y_EL + 1.5, -5.2, -4.0), "joint"))
    return pieces, []


def recipe_hand(s):
    ax = AX_ARM
    fx = xr(s, ax - 2.5, ax + 2.1)
    dx = xr(s, ax - 2.2, ax + 1.8)
    fist = (box(fx[0], fx[1], Y_HAND_END + 0.5, Y_WR - 1.5, -3.1, 1.3)
            + box(fx[0] + 0.4, fx[1] - 0.4, Y_HAND_END, Y_HAND_END + 0.5, -2.7, 0.9))   # flat knuckle face down
    pieces = [
        (cyl_x(Y_WR, 0, R_WRIST_DRUM, dx[0], dx[1]), "joint"),   # wrist ring
        (fist, "primary"),
    ]
    return pieces, []


def recipe_thigh(s):
    ax = AX_LEG
    bx = xr(s, ax - 4.2, ax + 3.8)
    sx = xr(s, ax - 3.6, ax + 3.4)
    pieces = [
        (sphere((s * ax, Y_HIP, 0), R_HIP_BALL), "joint"),
        (chamfer_box(bx[0], bx[1], Y_KN + R_KNEE_DRUM + GAP, Y_HIP - 1.5, -3.5, 4.5, 0.4), "primary"),
        (box(sx[0], sx[1], Y_KN + 10.5, Y_HIP - 2.0, 4.3, 5.1), "secondary"),   # off-white front slab
    ]
    for a, b in ((ax - 2.7, ax - 1.3), (ax + 1.1, ax + 2.5)):   # knee rams, upper rods (front)
        r = xr(s, a, b)
        pieces.append((box(r[0], r[1], Y_KN + 2.3, Y_KN + 10.6, 4.4, 5.8), "joint"))
    # hamstring channel up the back (same reason as the calf channel)
    cx = xr(s, ax - 2.0, ax + 2.0)
    # bevel the medial-front vertical edge: clearance for the waist ball at 120 deg hip flexion
    m = ax - 4.2
    bev = [(s * x, y, z) for x, z in ((m - 0.5, 4.5 - 1.6), (m - 0.5, 6.0), (m + 1.6 + 0.6, 6.0)) for y in (Y_KN, Y_HIP + 1)]
    return pieces, [box(cx[0], cx[1], Y_KN + 4.5, Y_HIP - 4.5, -3.7, -0.7), bev]


def recipe_shank(s):
    ax = AX_LEG
    bx = xr(s, ax - 3.4, ax + 3.4)
    pieces = [
        (cyl_x(Y_KN, 0, R_KNEE_DRUM, bx[0], bx[1]), "joint"),
        (chamfer_box(bx[0], bx[1], Y_AN + R_ANKLE_DRUM + 0.4, Y_KN, -1.2, 5.2, 0.3), "primary"),
    ]
    for a, b in ((ax - 2.7, ax - 1.3), (ax + 1.1, ax + 2.5)):   # knee rams, lower cylinders (front)
        r = xr(s, a, b)
        pieces.append((box(r[0], r[1], Y_KN - 11.0, Y_KN + 1.8, 5.0, 6.4), "joint"))
    # calf channel: a recess up the back keeps the 6.4 m column silhouette (front and side) at the
    # R-005 mass fraction (C12 widths vs C19 volume fraction)
    cx = xr(s, ax - 2.2, ax + 2.2)
    cutters = [box(cx[0], cx[1], Y_AN + R_ANKLE_DRUM + 2.5, Y_KN - R_KNEE_DRUM - 0.6, -1.5, 2.1)]
    return pieces, cutters


def recipe_foot(s):
    ax = AX_LEG
    hw = SOLE["width_m"] / 2.0
    heel, toe, ch = SOLE["heel_z_m"], SOLE["toe_z_m"], 1.2
    def outline(inset):
        a, b = ax - hw + inset, ax + hw - inset
        h, t = heel + inset, toe - inset
        pts = [(a, h), (b, h), (b, t - ch), (b - ch, t), (a + ch, t), (a, t - ch)]
        return [(s * x, z) for x, z in pts]
    slab = []
    for y, inset in ((0.0, 0.0), (1.2, 0.0), (1.7, 0.5)):
        slab += [(x, y, z) for x, z in outline(inset)]
    dx = xr(s, ax - 3.4, ax + 3.4)
    pieces = [
        (slab, "primary"),
        (cyl_x(Y_AN, 0, R_ANKLE_DRUM, dx[0], dx[1]), "joint"),
    ]
    return pieces, []


RECIPES = {"pelvis": recipe_pelvis, "torso": recipe_torso, "head": recipe_head}
for _side, _s in (("L", -1), ("R", 1)):
    for _k, _f in (("upper_arm", recipe_upper_arm), ("forearm", recipe_forearm), ("hand", recipe_hand),
                   ("thigh", recipe_thigh), ("shank", recipe_shank), ("foot", recipe_foot)):
        RECIPES[f"{_k}_{_side}"] = (lambda f, s: (lambda: f(s)))(_f, _s)


# ---------------------------------------------------------------- Blender helpers
def make_materials():
    mats = {}
    for role in C.ROLES:
        m = bpy.data.materials.new(C.MAT_PREFIX + role)
        col = C.hex_to_linear(PAL["roles"][role]["hex"]) + (1.0,)
        m.diffuse_color = col
        m.roughness = 0.9
        m.metallic = 0.0
        try:
            m.use_nodes = True
        except Exception:
            pass
        if m.node_tree:
            bsdf = next((n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
            if bsdf:
                bsdf.inputs["Base Color"].default_value = col
                bsdf.inputs["Roughness"].default_value = 0.9
        mats[role] = m
    return mats


def hull_object(name, pts_frame, mat, coll):
    bm = bmesh.new()
    vs = [bm.verts.new(C.f2b(p)) for p in pts_frame]
    res = bmesh.ops.convex_hull(bm, input=vs)
    bmesh.ops.delete(bm, geom=list(set(res["geom_interior"]) | set(res["geom_unused"])), context="VERTS")
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    if mat is not None:
        me.materials.append(mat)
    ob = bpy.data.objects.new(name, me)
    coll.objects.link(ob)
    return ob


def build_part(seg_id, mats, scratch):
    pieces, cutters = RECIPES[seg_id]()
    name = C.part_name(seg_id)
    objs = [hull_object(f"{name}__p{i}", p, mats[r], scratch) for i, (p, r) in enumerate(pieces)]
    base = objs[0]
    # base mesh must carry all four slots so TRANSFER keeps role names stable
    if len(objs) > 1:
        ops = bpy.data.collections.new(name + "__ops")
        for o in objs[1:]:
            scratch.objects.unlink(o)
            ops.objects.link(o)
        m = base.modifiers.new("union", "BOOLEAN")
        m.operation = "UNION"
        m.solver = "EXACT"
        m.operand_type = "COLLECTION"
        m.collection = ops
        m.material_mode = "TRANSFER"
    if cutters:
        cut = bpy.data.collections.new(name + "__cut")
        for i, p in enumerate(cutters):
            o = hull_object(f"{name}__c{i}", p, None, scratch)
            scratch.objects.unlink(o)
            cut.objects.link(o)
        m = base.modifiers.new("cut", "BOOLEAN")
        m.operation = "DIFFERENCE"
        m.solver = "EXACT"
        m.operand_type = "COLLECTION"
        m.collection = cut
        m.material_mode = "TRANSFER"
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(base.evaluated_get(dg))
    me.name = name
    # clean: weld, drop degenerate, merge coplanar triangles into flat ngons
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-3)
    bmesh.ops.dissolve_degenerate(bm, edges=bm.edges, dist=1e-4)
    bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(0.5), verts=bm.verts, edges=bm.edges,
                             delimit={"MATERIAL"})
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    # origin at the proximal joint centre
    j = Vector(C.f2b(SEG[seg_id]["proximal_joint"]))
    bmesh.ops.translate(bm, verts=bm.verts, vec=-j)
    bm.to_mesh(me)
    bm.free()
    # material slots in canonical role order, remap indices
    old = [m.name if m else None for m in me.materials]
    face_idx = [p.material_index for p in me.polygons]   # read before clear(): clear() resets indices
    used = sorted({C.MAT_PREFIX + r for r in C.ROLES if C.MAT_PREFIX + r in old},
                  key=lambda n: C.ROLES.index(n[len(C.MAT_PREFIX):]))
    remap = {i: used.index(n) for i, n in enumerate(old) if n in used}
    me.materials.clear()
    for n in used:
        me.materials.append(bpy.data.materials[n])
    for p, k in zip(me.polygons, face_idx):
        p.material_index = remap.get(k, 0)
    ob = bpy.data.objects.new(name, me)
    ob.location = j
    return ob


def mirror_part(src, seg_id):
    """R part = exact mirror (frame x -> -x, i.e. Blender x -> -x) of the built L part."""
    name = C.part_name(seg_id)
    me = src.data.copy()
    me.name = name
    bm = bmesh.new()
    bm.from_mesh(me)
    for v in bm.verts:
        v.co.x = -v.co.x
    bmesh.ops.reverse_faces(bm, faces=bm.faces)
    bm.normal_update()
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    ob.location = Vector(C.f2b(SEG[seg_id]["proximal_joint"]))
    return ob


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.unit_settings.system = "METRIC"
    sc.unit_settings.scale_length = 1.0
    mats = make_materials()
    scratch = bpy.data.collections.new("scratch")
    sc.collection.children.link(scratch)
    parts = bpy.data.collections.new("MechBalanced")
    sc.collection.children.link(parts)

    objs = {}
    for seg in FRAME["segments"]:
        if seg["side"] == "R":   # exact mirror of the L part (C18: L/R mirrored within 1 cm)
            ob = mirror_part(objs[seg["id"][:-2] + "_L"], seg["id"])
        else:
            ob = build_part(seg["id"], mats, scratch)
        parts.objects.link(ob)
        objs[seg["id"]] = ob
        print("built", ob.name, len(ob.data.polygons), "faces")

    # remove scratch geometry
    for o in list(scratch.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for c in list(bpy.data.collections):
        if c.name.startswith("MechBalanced_") or c.name == "scratch":
            for o in list(c.objects):
                bpy.data.objects.remove(o, do_unlink=True)
            bpy.data.collections.remove(c)
    for m in list(bpy.data.meshes):
        if m.users == 0:
            bpy.data.meshes.remove(m)

    # kinematic tree from the JSON parent field
    for seg in FRAME["segments"]:
        if seg["parent"]:
            ch, pa = objs[seg["id"]], objs[seg["parent"]]
            ch.parent = pa
            ch.matrix_parent_inverse.identity()
            ch.location = Vector(C.f2b(seg["proximal_joint"])) - Vector(C.f2b(SEG[seg["parent"]]["proximal_joint"]))

    # sockets: named helper empties parented to their part
    for nm, parent, pos, _dir in C.socket_specs(FRAME):
        e = bpy.data.objects.new(C.SOCKET_PREFIX + nm, None)
        e.empty_display_type = "ARROWS"
        e.empty_display_size = 2.0
        parts.objects.link(e)
        pa = objs[parent]
        e.parent = pa
        e.matrix_parent_inverse.identity()
        e.location = Vector(C.f2b(pos)) - Vector(C.f2b(SEG[parent]["proximal_joint"]))

    os.makedirs(C.MODEL_DIR, exist_ok=True)
    bpy.context.preferences.filepaths.save_version = 0   # no .blend1 backups next to the LFS asset
    bpy.ops.wm.save_as_mainfile(filepath=C.BLEND_PATH, compress=False)
    export_fbx(parts)
    tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs.values())
    print("TOTAL_TRIS", tris)


def export_fbx(parts):
    bpy.ops.object.select_all(action="DESELECT")
    for o in parts.objects:
        o.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=C.FBX_PATH, use_selection=True, object_types={"MESH", "EMPTY"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", global_scale=1.0,
        bake_space_transform=True, axis_forward="-Z", axis_up="Y",
        mesh_smooth_type="FACE", use_mesh_modifiers=False, use_triangles=False,
        add_leaf_bones=False, bake_anim=False, use_custom_props=False,
        path_mode="STRIP", embed_textures=False, use_tspace=False)


if __name__ == "__main__":
    main()
