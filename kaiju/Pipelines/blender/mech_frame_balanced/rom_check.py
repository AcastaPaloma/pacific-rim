"""Range-of-motion check of the greybox (T-007 C21 = AC-21 poses + B7 per-joint ranges).

Run:
  /Applications/Blender.app/Contents/MacOS/Blender -b art/models/mech_frame_balanced/mech_frame_balanced.blend \
      --python Pipelines/blender/mech_frame_balanced/rom_check.py

Writes evidence/T-007/rom_report.json and evidence/T-007/raw/pose_<name>_{front,side}.png.

Method. Parts are rigid; a pose is a set of anatomical DOF angles (mech_frame@v1 joints[].dofs:
world axis in the rest pose, unity_sign, rom_deg). Each child is rotated about its joint centre and
carries its subtree. For every pair of parts whose posed bounding boxes touch, the intersection is
computed with Blender's boolean (Manifold solver) and its volume measured.
  - adjacent pairs (parent/child): overlap volume as % of the smaller part (B7: <= 2 %)
  - non-adjacent pairs: overlap volume and penetration depth, taken as the smallest extent of the
    intersection's bounding box (AC-21: <= 0.5 m; B7: none)
B7 tests every DOF of the L-side joints, waist and neck at its R-005 range ends and mid-range
(R parts are exact mirrors). Each DOF is run twice: "isolated" (every other joint at rest) and
"cleared" (other limbs moved out of the swept space: contralateral leg abducted 40 deg and flexed
30 deg, both arms abducted 40 deg), so self-geometry limits and stance-contact limits are told apart.
"""
import json
import math
import os
import sys

import bmesh
import bpy
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mf_common as C  # noqa: E402

FRAME = C.load_frame()
SEG = C.segments_by_id(FRAME)
JOINTS = {j["id"]: j for j in FRAME["joints"]}
JOINT_OF_CHILD = {j["child"]: j for j in FRAME["joints"]}
OUT_RAW = os.path.join(C.EVIDENCE, "raw")
os.makedirs(OUT_RAW, exist_ok=True)


def rot(axis, deg):
    a = math.radians(deg)
    c, s = math.cos(a), math.sin(a)
    if axis == 0:
        return np.array([[1, 0, 0], [0, c, -s], [0, s, c]])
    if axis == 1:
        return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])
    return np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])


def local_rot(joint_id, dofs):
    """dofs: {dof name: anatomical degrees}; applied in the JSON dof order"""
    R = np.eye(3)
    for d in JOINTS[joint_id]["dofs"]:
        if d["name"] in dofs:
            R = R @ rot(int(np.argmax(np.abs(d["axis"]))), d["unity_sign"] * dofs[d["name"]])
    return R


def pose_matrices(pose):
    """pose: {joint id: {dof: deg}} -> {segment id: 4x4 world transform (frame coords)}"""
    M = {}
    for seg in FRAME["segments"]:   # parents precede children in the JSON
        sid = seg["id"]
        T = np.eye(4)
        if sid in JOINT_OF_CHILD:
            j = JOINT_OF_CHILD[sid]
            p = np.array(j["position"], float)
            R = local_rot(j["id"], pose.get(j["id"], {}))
            L = np.eye(4); L[:3, :3] = R; L[:3, 3] = p - R @ p
            T = M[seg["parent"]] @ L
        M[sid] = T
    return M


def rest_meshes():
    out = {}
    for sid in SEG:
        ob = bpy.data.objects[C.part_name(sid)]
        mw = ob.matrix_world
        v = np.array([C.b2f(mw @ x.co) for x in ob.data.vertices])
        f = [list(p.vertices) for p in ob.data.polygons]
        out[sid] = (v, f, [p.material_index for p in ob.data.polygons], list(ob.data.materials))
    return out


REST = None
COLL = None


def posed_objects(M, tag):
    objs = {}
    for sid, (v, f, mi, mats) in REST.items():
        T = M[sid]
        vw = (T[:3, :3] @ v.T).T + T[:3, 3]
        me = bpy.data.meshes.new(f"{tag}_{sid}")
        me.from_pydata([C.f2b(p) for p in vw], [], f)
        for m in mats:
            me.materials.append(m)
        for p, k in zip(me.polygons, mi):
            p.material_index = k
        ob = bpy.data.objects.new(f"{tag}_{sid}", me)
        COLL.objects.link(ob)
        objs[sid] = (ob, vw.min(0), vw.max(0))
    return objs


def clear_objects(objs):
    for ob, _a, _b in objs.values():
        me = ob.data
        bpy.data.objects.remove(ob, do_unlink=True)
        bpy.data.meshes.remove(me)


def volume(ob):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bm.transform(ob.matrix_world)
    v = bm.calc_volume(signed=False)
    bm.free()
    return v


def intersect(a, b):
    tmp = bpy.data.objects.new("isect", a.data.copy())
    COLL.objects.link(tmp)
    m = tmp.modifiers.new("i", "BOOLEAN")
    m.operation = "INTERSECT"
    m.solver = "MANIFOLD"
    m.object = b
    dg = bpy.context.evaluated_depsgraph_get()
    ev = tmp.evaluated_get(dg)
    me = ev.to_mesh()
    vol, depth = 0.0, 0.0
    if len(me.vertices):
        bm = bmesh.new()
        bm.from_mesh(me)
        vol = bm.calc_volume(signed=False)
        co = np.array([C.b2f(x.co) for x in bm.verts])
        depth = float((co.max(0) - co.min(0)).min())
        bm.free()
    ev.to_mesh_clear()
    d = tmp.data
    bpy.data.objects.remove(tmp, do_unlink=True)
    bpy.data.meshes.remove(d)
    return vol, depth


PART_VOL = {}


def adjacent(a, b):
    return SEG[a]["parent"] == b or SEG[b]["parent"] == a


def evaluate(pose, tag):
    M = pose_matrices(pose)
    objs = posed_objects(M, tag)
    ids = list(SEG)
    adj, non = [], []
    for i in range(len(ids)):
        for k in range(i + 1, len(ids)):
            a, b = ids[i], ids[k]
            oa, amin, amax = objs[a]
            ob_, bmin, bmax = objs[b]
            if np.any(amax < bmin) or np.any(bmax < amin):
                continue
            vol, depth = intersect(oa, ob_)
            if vol < 1e-3:
                continue
            row = {"a": a, "b": b, "volume_m3": round(vol, 3), "depth_m": round(depth, 3)}
            if adjacent(a, b):
                row["pct_of_smaller"] = round(100 * vol / min(PART_VOL[a], PART_VOL[b]), 3)
                adj.append(row)
            else:
                non.append(row)
    return objs, adj, non


CAM = None


def render_pose(name):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.render.resolution_x, sc.render.resolution_y = 640, 720
    sh = sc.display.shading
    sh.light, sh.color_type, sh.show_object_outline = "STUDIO", "MATERIAL", True
    if sc.world is None:
        sc.world = bpy.data.worlds.new("bg")
    sc.world.color = (0.93, 0.93, 0.92)
    sh.background_type = "WORLD"
    try:
        sc.view_settings.view_transform = "Standard"
    except Exception:
        pass
    cam = bpy.data.objects.get("romcam") or bpy.data.objects.new("romcam", bpy.data.cameras.new("romcam"))
    if cam.name not in sc.collection.objects:
        sc.collection.objects.link(cam)
    cam.data.type, cam.data.ortho_scale = "ORTHO", 110
    cam.data.clip_end = 1000
    sc.camera = cam
    from mathutils import Vector
    for view, d in (("front", (0, 0, 1)), ("side", (-1, 0, 0))):
        c = np.array([0, 45.0, 4.0])
        cam.location = Vector(C.f2b(c + 300 * np.array(d)))
        cam.rotation_euler = (Vector(C.f2b(c)) - cam.location).to_track_quat("-Z", "Y").to_euler()
        sc.render.filepath = os.path.join(OUT_RAW, f"pose_{name}_{view}.png")
        bpy.ops.render.render(write_still=True)


def hide_parts(hide):
    for sid in SEG:
        bpy.data.objects[C.part_name(sid)].hide_render = hide
    for o in bpy.data.objects:
        if o.type == "EMPTY":
            o.hide_render = hide


AC21_POSES = {
    "rest": {},
    "guard": {"shoulder_R": {"flexion": 45}, "elbow_R": {"flexion": 90}},
    "crouch": {"hip_L": {"flexion": 80}, "hip_R": {"flexion": 80}, "knee_L": {"flexion": 110}, "knee_R": {"flexion": 110},
               "ankle_L": {"plantarflexion": -20}, "ankle_R": {"plantarflexion": -20}},
    "arms_overhead": {"shoulder_L": {"flexion": 155}, "shoulder_R": {"flexion": 155}},
}
CLEARED = {"hip_R": {"abduction": 40, "flexion": 30}, "shoulder_L": {"abduction": 40}, "shoulder_R": {"abduction": 40}}
B7_JOINTS = ["waist", "neck", "shoulder_L", "elbow_L", "wrist_L", "hip_L", "knee_L", "ankle_L"]


def main():
    global REST, COLL
    REST = rest_meshes()
    COLL = bpy.data.collections.new("rom")
    bpy.context.scene.collection.children.link(COLL)
    for sid in SEG:
        PART_VOL[sid] = volume(bpy.data.objects[C.part_name(sid)])
    report = {"schema": "mech_rom_report@v1", "model": os.path.relpath(bpy.data.filepath, C.PROJECT),
              "script": "Pipelines/blender/mech_frame_balanced/rom_check.py", "method": __doc__.strip().split("\n\n", 1)[1],
              "ac21_poses": {}, "b7": []}
    hide_parts(True)
    for name, pose in AC21_POSES.items():
        objs, adj, non = evaluate(pose, "ac21_" + name)
        maxdepth = max([r["depth_m"] for r in non], default=0.0)
        report["ac21_poses"][name] = {"pose": pose, "adjacent_overlaps": adj, "non_adjacent": non,
                                      "max_non_adjacent_depth_m": maxdepth, "pass_depth_le_0_5m": maxdepth <= 0.5}
        render_pose(name)
        clear_objects(objs)
        print("AC21", name, maxdepth, [(r["a"], r["b"], r["depth_m"]) for r in non])
    for jid in B7_JOINTS:
        j = JOINTS[jid]
        for d in j["dofs"]:
            lo, hi = d["rom_deg"]
            for ang in (lo, hi, hi / 2.0 if hi > 0 else lo / 2.0):
                for variant in ("isolated", "cleared"):
                    pose = {jid: {d["name"]: ang}}
                    if variant == "cleared":
                        for k, v in CLEARED.items():
                            if k != jid:
                                pose[k] = dict(v)
                    objs, adj, non = evaluate(pose, f"b7_{jid}_{d['name']}_{ang}_{variant}")
                    clear_objects(objs)
                    own = [r for r in adj if j["child"] in (r["a"], r["b"]) and j["parent"] in (r["a"], r["b"])]
                    worst_adj = max([r["pct_of_smaller"] for r in adj], default=0.0)
                    row = {"joint": jid, "dof": d["name"], "angle_deg": ang, "range_end": ang in (lo, hi), "variant": variant,
                           "joint_pair_overlap_pct": own[0]["pct_of_smaller"] if own else 0.0,
                           "worst_adjacent_overlap_pct": worst_adj, "adjacent": adj, "non_adjacent": non,
                           "pass_adjacent_le_2pct": worst_adj <= 2.0, "pass_no_non_adjacent": len(non) == 0}
                    report["b7"].append(row)
                    print("B7", jid, d["name"], ang, variant, round(worst_adj, 2), [(r["a"], r["b"], r["depth_m"]) for r in non])
    ends = [r for r in report["b7"] if r["range_end"]]
    report["summary"] = {
        "ac21_all_pass": all(p["pass_depth_le_0_5m"] for p in report["ac21_poses"].values()),
        "b7_range_ends_adjacent_pass": sum(r["pass_adjacent_le_2pct"] for r in ends), "b7_range_ends_total": len(ends),
        "b7_range_ends_isolated_no_contact": sum(r["pass_no_non_adjacent"] for r in ends if r["variant"] == "isolated"),
        "b7_range_ends_cleared_no_contact": sum(r["pass_no_non_adjacent"] for r in ends if r["variant"] == "cleared"),
        "b7_range_ends_per_variant": len(ends) // 2,
        "b7_failures_adjacent": [(r["joint"], r["dof"], r["angle_deg"], r["variant"], r["worst_adjacent_overlap_pct"]) for r in ends if not r["pass_adjacent_le_2pct"]],
        "b7_contacts_cleared": [(r["joint"], r["dof"], r["angle_deg"], [(n["a"], n["b"], n["depth_m"]) for n in r["non_adjacent"]]) for r in ends if r["variant"] == "cleared" and r["non_adjacent"]],
    }
    with open(os.path.join(C.EVIDENCE, "rom_report.json"), "w") as f:
        json.dump(report, f, indent=1, default=lambda o: o.item() if hasattr(o, "item") else str(o))
    print("SUMMARY", json.dumps(report["summary"], default=str))


main()
