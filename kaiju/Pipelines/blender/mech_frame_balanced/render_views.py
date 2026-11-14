"""Render orthographic views of the greybox (Workbench, headless).

Run:
  /Applications/Blender.app/Contents/MacOS/Blender -b art/models/mech_frame_balanced/mech_frame_balanced.blend \
      --python Pipelines/blender/mech_frame_balanced/render_views.py -- [--out DIR] [--only turnaround|silhouette|flat]

Outputs (DIR defaults to evidence/T-007/raw):
  turnaround_{front,side,back,three_quarter}.png  1920x1080, palette colours, 1.8 m human at the left toe
  silhouette_{front,side,three_quarter}.png        black on white, 25 px/m, no markers
  flat_front.png                                   flat palette colours (no shading), 25 px/m
  views.json                                       camera centre / px-per-metre so markers can be drawn exactly
Markers (10 m grid, 67 m deck line, 80 m line) are drawn by compose_evidence.py from views.json.
"""
import json
import math
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mf_common as C  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.join(C.EVIDENCE, "raw")
ONLY = None
for i, a in enumerate(argv):
    if a == "--out":
        OUT = argv[i + 1]
    if a == "--only":
        ONLY = argv[i + 1]
os.makedirs(OUT, exist_ok=True)

# view name -> direction FROM the mech TO the camera, in frame coords (x right, y up, z forward)
VIEWS = {
    "front": (0, 0, 1),
    "side": (-1, 0, 0),            # mech's left side
    "back": (0, 0, -1),
    "three_quarter": (-math.sqrt(0.5), 0, math.sqrt(0.5)),   # front-left
}
Y_CENTRE = 41.0


def add_human():
    """1.8 m human scale figure (simple capsule body + head) standing in front of the left toe"""
    import bmesh
    me = bpy.data.meshes.new("scale_human")
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=10, radius1=0.22, radius2=0.22, depth=1.45,
                          matrix=__import__("mathutils").Matrix.Translation((0, 0, 0.725)))
    bmesh.ops.create_uvsphere(bm, u_segments=10, v_segments=6, radius=0.17,
                              matrix=__import__("mathutils").Matrix.Translation((0, 0, 1.63)))
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new("scale_human", me)
    ob.location = Vector(C.f2b((-13.0, 0.0, 9.0)))
    m = bpy.data.materials.new("scale_human_mat")
    m.diffuse_color = (0.8, 0.05, 0.05, 1)
    me.materials.append(m)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def setup(scene, w, h, mode):
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.render.resolution_x, scene.render.resolution_y = w, h
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = False
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGB"
    try:
        scene.view_settings.view_transform = "Standard"
    except Exception:
        pass
    sh = scene.display.shading
    if mode == "silhouette":
        sh.light = "FLAT"
        sh.color_type = "SINGLE"
        sh.single_color = (0, 0, 0)
        sh.show_object_outline = False
        sh.show_cavity = False
        bg = (1, 1, 1)
    elif mode == "flat":
        sh.light = "FLAT"
        sh.color_type = "MATERIAL"
        sh.show_object_outline = False
        sh.show_cavity = False
        bg = (1, 1, 1)
    else:
        sh.light = "STUDIO"
        sh.color_type = "MATERIAL"
        sh.show_object_outline = True
        sh.object_outline_color = (0.05, 0.05, 0.05)
        sh.show_cavity = False
        sh.show_shadows = False
        bg = (0.93, 0.93, 0.92)
    sh.background_type = "WORLD"
    if scene.world is None:
        scene.world = bpy.data.worlds.new("bg")
    scene.world.color = bg
    try:
        scene.display.render_aa = "OFF" if mode == "silhouette" else "8"
    except Exception:
        pass


def camera(scene, view, ortho_w, y_c):
    cam = bpy.data.objects.get("evcam")
    if cam is None:
        cam = bpy.data.objects.new("evcam", bpy.data.cameras.new("evcam"))
        scene.collection.objects.link(cam)
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = ortho_w
    cam.data.clip_start, cam.data.clip_end = 1, 1000
    d = Vector(VIEWS[view])
    pos_f = Vector((0, y_c, 0)) + d * 300
    cam.location = Vector(C.f2b(pos_f))
    tgt = Vector(C.f2b((0, y_c, 0)))
    cam.rotation_euler = (tgt - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    return cam


def render(path):
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def main():
    scene = bpy.context.scene
    meta = {"y_centre": Y_CENTRE, "views": {}}
    human = add_human()
    if ONLY in (None, "turnaround"):
        W, H = 1920, 1080
        ortho_w = 92.0 * W / H
        setup(scene, W, H, "turnaround")
        for v in VIEWS:
            camera(scene, v, ortho_w, Y_CENTRE)
            render(os.path.join(OUT, f"turnaround_{v}.png"))
            meta["views"][f"turnaround_{v}"] = {"w": W, "h": H, "px_per_m": W / ortho_w, "dir": VIEWS[v]}
    human.hide_render = True
    PXM = 25.0
    W, H = 2400, 2100
    ortho_w = W / PXM
    if ONLY in (None, "silhouette"):
        setup(scene, W, H, "silhouette")
        for v in ("front", "side", "three_quarter"):
            camera(scene, v, ortho_w, Y_CENTRE)
            render(os.path.join(OUT, f"silhouette_{v}.png"))
            meta["views"][f"silhouette_{v}"] = {"w": W, "h": H, "px_per_m": PXM, "dir": VIEWS[v]}
    if ONLY in (None, "flat"):
        setup(scene, W, H, "flat")
        camera(scene, "front", ortho_w, Y_CENTRE)
        render(os.path.join(OUT, "flat_front.png"))
        meta["views"]["flat_front"] = {"w": W, "h": H, "px_per_m": PXM, "dir": VIEWS["front"]}
    p = os.path.join(OUT, "views.json")
    old = json.load(open(p)) if os.path.exists(p) else {"views": {}}
    old["views"].update(meta["views"])
    old["y_centre"] = Y_CENTRE
    json.dump(old, open(p, "w"), indent=1)


main()
