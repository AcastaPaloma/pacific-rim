"""Shared constants and helpers for the Balanced mech greybox scripts (T-007).

Coordinates
-----------
All geometry is authored in the mech_frame@v1 frame ("F"): x right, y up, z forward
(Unity world, metres), origin on the ground midway between the feet, mech's left = -x.
Blender is Z-up. The mapping used everywhere is

    Blender (bx, by, bz) = (Fx, Fz, Fy)          F = (bx, bz, by)

(a reflection: the frame is Unity's left-handed world, Blender is right-handed), so the mech
faces Blender +Y with its left at Blender -X. The FBX is written Z-up (axis_forward Y, axis_up Z)
and Unity's importer converts it with bakeAxisConversion, giving Unity world == F with identity
rotations (verified by the EditMode test and evidence/T-007/unity_hierarchy.txt).

Single source of dimensions: research/R-005/mech_frame_balanced.json (mech_frame@v1).
"""
import json
import os

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))  # kaiju/
FRAME_JSON = os.path.join(PROJECT, "research", "R-005", "mech_frame_balanced.json")
PALETTE_JSON = os.path.join(HERE, "palette_roles.json")
MODEL_DIR = os.path.join(PROJECT, "art", "models", "mech_frame_balanced")
BLEND_PATH = os.path.join(MODEL_DIR, "mech_frame_balanced.blend")
FBX_PATH = os.path.join(MODEL_DIR, "mech_frame_balanced.fbx")
EVIDENCE = os.path.join(PROJECT, "evidence", "T-007")

PREFIX = "MechBalanced_"          # part object name = PREFIX + segment id (e.g. MechBalanced_thigh_L)
SOCKET_PREFIX = PREFIX + "socket_"  # helper empties, e.g. MechBalanced_socket_shoulder_L
ROLES = ["primary", "secondary", "joint", "accent"]
MAT_PREFIX = "mech_"              # material name = mech_<role>


def load_frame():
    with open(FRAME_JSON) as f:
        d = json.load(f)
    assert d["schema"] == "mech_frame@v1", d["schema"]
    return d


def load_palette():
    with open(PALETTE_JSON) as f:
        return json.load(f)


def segments_by_id(frame):
    return {s["id"]: s for s in frame["segments"]}


def part_name(seg_id):
    return PREFIX + seg_id


def seg_of(name):
    return name[len(PREFIX):] if name.startswith(PREFIX) else None


def f2b(p):
    """frame -> Blender world"""
    return (p[0], p[2], p[1])


def b2f(p):
    """Blender world -> frame"""
    return (p[0], p[2], p[1])


def hex_to_linear(h):
    h = h.lstrip("#")
    out = []
    for i in (0, 2, 4):
        c = int(h[i:i + 2], 16) / 255.0
        out.append(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4)
    return tuple(out)


# Sockets: (name suffix, parent segment, position in frame coords as a function of the
# segment table). Orientation convention (applied in Unity): shoulder and hardpoint sockets
# face laterally outward, hip and elbow sockets face down along the limb.
def socket_specs(frame):
    segs = segments_by_id(frame)
    out = []
    for side in ("L", "R"):
        s = -1.0 if side == "L" else 1.0
        out.append(("shoulder_" + side, "torso", tuple(segs["upper_arm_" + side]["proximal_joint"]), "lateral"))
        out.append(("hip_" + side, "pelvis", tuple(segs["thigh_" + side]["proximal_joint"]), "down"))
        out.append(("elbow_" + side, "upper_arm_" + side, tuple(segs["forearm_" + side]["proximal_joint"]), "down"))
        # forearm hardpoint: centre of the outer-face plate (geometry in build_greybox.FOREARM_PLATE)
        ej = segs["forearm_" + side]["proximal_joint"]
        out.append(("hardpoint_" + side, "forearm_" + side,
                    (s * FOREARM_PLATE["x_out"], ej[1] + FOREARM_PLATE["y_mid_rel"], FOREARM_PLATE["z_mid"]), "lateral"))
    return out


# forearm outer hardpoint plate (|x| of outer face, centre height relative to elbow, centre z)
FOREARM_PLATE = {"x_out": 15.75, "x_in": 15.5, "y0_rel": -9.0, "y1_rel": -2.0, "y_mid_rel": -5.5,
                 "z0": -3.6, "z1": -0.4, "z_mid": -2.0}
