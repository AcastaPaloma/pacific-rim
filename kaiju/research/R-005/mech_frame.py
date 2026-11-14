"""R-005 derivations: reference mech frame (mech_frame@v1) and the numbers quoted in research/R-005.md.

Run with the kaiju env:  /opt/miniconda3/envs/kaiju/bin/python research/R-005/mech_frame.py
Writes research/R-005/mech_frame_balanced.json and prints every derived figure used in R-005.md.
Optional args: --height H --mass M_kg (scaling rule of R-005 section 1.4) --out path.json

Frame: Unity world axes, x right, y up, z forward (Unity is left-handed); origin on the ground midway
between the feet; metres, kilograms, seconds. Rest pose: standing straight, arms hanging, feet parallel.
"""
import argparse, json, math

G = 9.81
REF_H, REF_M = 80.0, 1.0e7

# de Leva (1996) male, joint-centre based (Duarte BMC data file BSPmale_ZdeLeva.txt):
# mass fraction, CoM fraction from proximal end, radii of gyration / length (sagittal = about the
# antero-posterior axis, transverse = about the medio-lateral axis, longitudinal = long axis).
DE_LEVA = {
    "head": (0.0694, 0.5976, 0.362, 0.376, 0.312),
    "trunk": (0.4346, 0.4486, 0.372, 0.347, 0.191),
    "upper_trunk": (0.1596,), "middle_trunk": (0.1633,), "lower_trunk": (0.1117,),
    "upper_arm": (0.0271, 0.5772, 0.285, 0.269, 0.158),   # SJC-EJC
    "forearm": (0.0162, 0.4574, 0.276, 0.265, 0.121),     # EJC-WJC
    "hand": (0.0061, 0.3624, 0.288, 0.235, 0.184),        # WJC-DAC3 (wrist to fingertip)
    "thigh": (0.1416, 0.4095, 0.329, 0.329, 0.149),       # HJC-KJC
    "shank": (0.0433, 0.4395, 0.251, 0.246, 0.102),       # KJC-AJC
    "foot": (0.0137, 0.4415, 0.257, 0.245, 0.124),        # HEEL-TTIP
}

# Heroic armoured frame mass fractions (R-005 section 1.2 justifies each change). Sum = 1 exactly.
MASS_FRAC = {"pelvis": 0.110, "torso": 0.330, "head": 0.020,
             "upper_arm": 0.032, "forearm": 0.028, "hand": 0.010,
             "thigh": 0.115, "shank": 0.055, "foot": 0.030}

# Landmarks for the 80 m reference, as fractions of height (y) or of height (x, z).
LM = dict(head_top=1.0, neck=71.0/80, shoulder=66.0/80, elbow=51.0/80, wrist=38.0/80, fingertip=30.5/80,
          waist=48.5/80, pelvis_bottom=39.0/80, hip=42.0/80, knee=23.0/80, ankle=4.0/80,
          hip_x=6.5/80, arm_x=13.5/80, heel_z=-5.25/80, toe_z=9.75/80, foot_w=10.0/80)
# Representative envelope (width x, depth z) as fractions of height; boxes for pelvis/torso/head/foot.
DIMS = dict(pelvis=(16.0/80, 9.0/80), torso=(18.0/80, 10.0/80), head=(5.0/80, 6.0/80),
            upper_arm=(5.0/80, 5.0/80), forearm=(4.8/80, 4.8/80), hand=(2.5/80, 5.0/80),
            thigh=(6.5/80, 7.0/80), shank=(5.5/80, 6.0/80))
# Hull fill factor (solid fraction of the bounding box) for the density check.
FILL = dict(pelvis=0.70, torso=0.70, head=0.70, foot=0.50, upper_arm=math.pi/4, forearm=math.pi/4,
            hand=0.70, thigh=math.pi/4, shank=math.pi/4)


def box_I(m, w, h, d):  # principal inertia of a solid cuboid about its centre: (Ixx, Iyy, Izz)
    return (m * (h*h + d*d) / 12, m * (w*w + d*d) / 12, m * (w*w + h*h) / 12)


def build(H=REF_H, M=REF_M):
    s = H  # lengths scale with height (geometric similarity)
    y = {k: v * s for k, v in LM.items()}
    segs, joints = [], []

    def add(sid, kind, parent, prox, dist, dims, com, inertia, method, side=None, L=None):
        m = MASS_FRAC[kind] * M
        L = math.dist(prox, dist) if L is None else L
        w, h, d = dims
        vol = w * h * d * FILL[kind]
        segs.append(dict(id=sid, kind=kind, side=side, parent=parent,
                         proximal_joint=[round(c, 4) for c in prox], distal_end=[round(c, 4) for c in dist],
                         length_m=round(L, 4), width_x_m=round(w, 4), height_y_m=round(h, 4), depth_z_m=round(d, 4),
                         mass_kg=round(m, 1), com=[round(c, 4) for c in com],
                         inertia_principal_kgm2=[float(f"{i:.6g}") for i in inertia],
                         inertia_axes="world x,y,z at rest pose (principal axes aligned with world)",
                         inertia_method=method, hull_volume_m3=round(vol, 2),
                         hull_density_kg_m3=round(m / vol, 1)))

    # Pelvis (root): box from pelvis_bottom to waist.
    pb, wa = y["pelvis_bottom"], y["waist"]
    w, d = DIMS["pelvis"][0]*s, DIMS["pelvis"][1]*s
    h = wa - pb
    m = MASS_FRAC["pelvis"] * M
    add("pelvis", "pelvis", None, (0, pb, 0), (0, wa, 0), (w, h, d), (0, (pb+wa)/2, 0), box_I(m, w, h, d), "solid box")
    # Torso: waist to neck.
    nk = y["neck"]; w, d = DIMS["torso"][0]*s, DIMS["torso"][1]*s; h = nk - wa; m = MASS_FRAC["torso"]*M
    add("torso", "torso", "pelvis", (0, wa, 0), (0, nk, 0), (w, h, d), (0, (wa+nk)/2, 0), box_I(m, w, h, d), "solid box")
    # Head (incl. neck housing): neck to top.
    ht = y["head_top"]; w, d = DIMS["head"][0]*s, DIMS["head"][1]*s; h = ht - nk; m = MASS_FRAC["head"]*M
    add("head", "head", "torso", (0, nk, 0), (0, ht, 0), (w, h, d), (0, (nk+ht)/2, 0), box_I(m, w, h, d), "solid box")

    def limb(sid, kind, parent, x, ytop, ybot, side):
        m = MASS_FRAC[kind] * M
        _, cf, rsag, rtr, rlon = DE_LEVA[kind]
        L = ytop - ybot
        com = (x, ytop - cf * L, 0.0)
        I = (m*(rtr*L)**2, m*(rlon*L)**2, m*(rsag*L)**2)  # x: medio-lateral (flexion), y: long, z: AP
        w, d = DIMS[kind][0]*s, DIMS[kind][1]*s
        add(sid, kind, parent, (x, ytop, 0), (x, ybot, 0), (w, L, d), com, I,
            "de Leva 1996 radii of gyration x segment length; CoM fraction from proximal joint", side)

    for side, sg in (("L", -1), ("R", 1)):
        ax, hx = sg * y["arm_x"], sg * y["hip_x"]
        limb(f"upper_arm_{side}", "upper_arm", "torso", ax, y["shoulder"], y["elbow"], side)
        limb(f"forearm_{side}", "forearm", f"upper_arm_{side}", ax, y["elbow"], y["wrist"], side)
        limb(f"hand_{side}", "hand", f"forearm_{side}", ax, y["wrist"], y["fingertip"], side)
        limb(f"thigh_{side}", "thigh", "pelvis", hx, y["hip"], y["knee"], side)
        limb(f"shank_{side}", "shank", f"thigh_{side}", hx, y["knee"], y["ankle"], side)
        # Foot: box, sole on the ground, ankle joint at the top surface, heel behind, toe ahead.
        m = MASS_FRAC["foot"] * M
        fl = (y["toe_z"] - y["heel_z"]); fw = y["foot_w"]; fh = y["ankle"]
        cz = (y["toe_z"] + y["heel_z"]) / 2
        add(f"foot_{side}", "foot", f"shank_{side}", (hx, y["ankle"], 0), (hx, 0.0, y["toe_z"]), (fw, fh, fl),
            (hx, fh/2, cz), box_I(m, fw, fh, fl), "solid box (length_m = sole length heel to toe; distal_end = toe tip)", side, L=fl)

    # --- joints ---------------------------------------------------------------------------
    # dof: name, axis (world, rest pose), unity drive slot, positive anatomical motion, sign of the Unity
    # rotation about +axis that produces it (Unity: +x takes +y to +z; +y takes +z to +x; +z takes +x to +y),
    # ROM [min, max] deg of the anatomical motion, m0 status.
    def J(jid, parent, child, pos, jtype, dofs, note=""):
        joints.append(dict(id=jid, parent=parent, child=child, position=[round(c, 4) for c in pos],
                           unity_joint_type=jtype, dofs=dofs, note=note))

    def dof(name, axis, slot, pos_motion, sign, rom, m0="active"):
        return dict(name=name, axis=axis, unity_drive=slot, positive=pos_motion, unity_sign=sign,
                    rom_deg=rom, m0=m0)

    J("waist", "pelvis", "torso", (0, wa, 0), "SphericalJoint", [
        dof("flexion", [1, 0, 0], "xDrive(twist)", "torso leans forward", +1, [-20, 40]),
        dof("axial_rotation", [0, 1, 0], "yDrive(swingY)", "torso turns left (nose to -x)", -1, [-30, 30]),
        dof("lateral_bend", [0, 0, 1], "zDrive(swingZ)", "torso bends to the right (+x)", -1, [-20, 20])])
    J("neck", "torso", "head", (0, nk, 0), "FixedJoint (M0); SphericalJoint later", [
        dof("flexion", [1, 0, 0], "xDrive(twist)", "head nods forward", +1, [-30, 40], "locked"),
        dof("rotation", [0, 1, 0], "yDrive(swingY)", "head turns left", -1, [-70, 70], "locked")])
    for side, sg in (("L", -1), ("R", 1)):
        ax, hx = sg * y["arm_x"], sg * y["hip_x"]
        # abduction/rotation signs mirror between sides
        J(f"shoulder_{side}", "torso", f"upper_arm_{side}", (ax, y["shoulder"], 0), "SphericalJoint", [
            dof("flexion", [1, 0, 0], "xDrive(twist)", "arm swings forward", -1, [-50, 160]),
            dof("internal_rotation", [0, 1, 0], "yDrive(swingY)", "forearm turns toward the chest", sg * -1, [-60, 70]),
            dof("abduction", [0, 0, 1], "zDrive(swingZ)", "arm raised sideways", sg * 1, [-10, 150])])
        J(f"elbow_{side}", f"upper_arm_{side}", f"forearm_{side}", (ax, y["elbow"], 0), "RevoluteJoint", [
            dof("flexion", [1, 0, 0], "xDrive", "hand comes forward/up", -1, [0, 145])])
        J(f"wrist_{side}", f"forearm_{side}", f"hand_{side}", (ax, y["wrist"], 0), "FixedJoint (M0); SphericalJoint later", [
            dof("flexion", [1, 0, 0], "xDrive(twist)", "palm side forward", -1, [-60, 60], "locked"),
            dof("ulnar_deviation", [0, 0, 1], "zDrive(swingZ)", "hand tilts outward", sg * 1, [-20, 30], "locked")])
        J(f"hip_{side}", "pelvis", f"thigh_{side}", (hx, y["hip"], 0), "SphericalJoint", [
            dof("flexion", [1, 0, 0], "xDrive(twist)", "thigh swings forward", -1, [-20, 120]),
            dof("internal_rotation", [0, 1, 0], "yDrive(swingY)", "toes turn inward", sg * -1, [-40, 35]),
            dof("abduction", [0, 0, 1], "zDrive(swingZ)", "leg moves outward", sg * 1, [-25, 40])])
        J(f"knee_{side}", f"thigh_{side}", f"shank_{side}", (hx, y["knee"], 0), "RevoluteJoint", [
            dof("flexion", [1, 0, 0], "xDrive", "shank swings back", +1, [0, 135])])
        J(f"ankle_{side}", f"shank_{side}", f"foot_{side}", (hx, y["ankle"], 0),
          "SphericalJoint with swingY locked", [
            dof("plantarflexion", [1, 0, 0], "xDrive(twist)", "toes down", +1, [-20, 40]),
            dof("inversion", [0, 0, 1], "zDrive(swingZ)", "sole turns inward", sg * -1, [-15, 25])])

    tot_m = sum(sg["mass_kg"] for sg in segs)
    com = [sum(sg["mass_kg"] * sg["com"][i] for sg in segs) / tot_m for i in range(3)]
    vol = sum(sg["hull_volume_m3"] for sg in segs)
    foot = next(sg for sg in segs if sg["id"] == "foot_R")
    frame = dict(
        schema="mech_frame@v1", id="mech_frame_balanced", source="research/R-005.md (R-005); generator research/R-005/mech_frame.py",
        frame=dict(axes="x right, y up, z forward (Unity world, left-handed)", origin="ground, midway between the feet",
                   units="m, kg, s, deg", pose="rest: standing straight, arms hanging, feet parallel, knees straight"),
        total_height_m=H, total_mass_kg=round(tot_m, 1), com_rest=[round(c, 4) for c in com],
        hull_volume_m3=round(vol, 1), mean_hull_density_kg_m3=round(tot_m / vol, 1),
        stance=dict(neutral_stance_width_m=round(2 * y["hip_x"], 4),
                    note="lateral distance between foot centrelines; feet directly under the hip joints"),
        foot_sole=dict(length_m=round(y["toe_z"] - y["heel_z"], 4), width_m=round(y["foot_w"], 4),
                       area_m2=round((y["toe_z"] - y["heel_z"]) * y["foot_w"], 3),
                       heel_z_m=round(y["heel_z"], 4), toe_z_m=round(y["toe_z"], 4), ankle_z_m=0.0,
                       ankle_height_m=round(y["ankle"], 4), shape="rectangle (corners may be chamfered by art if area stays within 2%)"),
        segments=segs, joints=joints,
        scaling_rule="lengths x H/80; masses x M/1e7; inertia x (M/1e7)(H/80)^2; joint positions x H/80; ROM unchanged",
        m0_active_dof=sum(1 for j in joints for d in j["dofs"] if d["m0"] == "active"))
    return frame


def whole_body(frame):
    segs = frame["segments"]; M = frame["total_mass_kg"]; c = frame["com_rest"]
    I = [0.0, 0.0, 0.0]
    for sg in segs:
        r = [sg["com"][i] - c[i] for i in range(3)]
        Ip = sg["inertia_principal_kgm2"]; m = sg["mass_kg"]
        I[0] += Ip[0] + m * (r[1]**2 + r[2]**2)
        I[1] += Ip[1] + m * (r[0]**2 + r[2]**2)
        I[2] += Ip[2] + m * (r[0]**2 + r[1]**2)
    return I


def subtree_I_about(frame, ids, pivot, axis):
    """Inertia of the listed segments about an axis (0=x,1=y,2=z) through pivot (rest pose)."""
    tot = 0.0
    for sg in frame["segments"]:
        if sg["id"] in ids:
            r = [sg["com"][i] - pivot[i] for i in range(3)]
            perp = sum(r[i]**2 for i in range(3) if i != axis)
            tot += sg["inertia_principal_kgm2"][axis] + sg["mass_kg"] * perp
    return tot


def report(frame):
    M = frame["total_mass_kg"]; W = M * G; c = frame["com_rest"]
    print(f"total mass {M:.4g} kg, CoM {c}, CoM/H = {c[1]/frame['total_height_m']:.4f}")
    print(f"hull volume {frame['hull_volume_m3']} m3, mean density {frame['mean_hull_density_kg_m3']} kg/m3")
    for sg in frame["segments"]:
        print(f"  {sg['id']:12s} m={sg['mass_kg']/1e3:8.1f} t L={sg['length_m']:6.2f} com={sg['com']} "
              f"I={['%.3g' % v for v in sg['inertia_principal_kgm2']]} rho={sg['hull_density_kg_m3']}")
    Iw = whole_body(frame)
    print("whole-body I about CoM (x pitch, y yaw, z roll):", ["%.4g" % v for v in Iw])
    # -- effective inertias for gains
    seg = {s["id"]: s for s in frame["segments"]}
    jpos = {j["id"]: j["position"] for j in frame["joints"]}
    allids = set(seg)
    cases = {
        "hip_swing(flex)": (["thigh_R", "shank_R", "foot_R"], jpos["hip_R"], 0),
        "knee_swing": (["shank_R", "foot_R"], jpos["knee_R"], 0),
        "ankle_swing": (["foot_R"], jpos["ankle_R"], 0),
        "shoulder(flex)": (["upper_arm_R", "forearm_R", "hand_R"], jpos["shoulder_R"], 0),
        "elbow": (["forearm_R", "hand_R"], jpos["elbow_R"], 0),
        "waist(flex)": (["torso", "head", "upper_arm_L", "upper_arm_R", "forearm_L", "forearm_R", "hand_L", "hand_R"], jpos["waist"], 0),
        "waist(yaw)": (["torso", "head", "upper_arm_L", "upper_arm_R", "forearm_L", "forearm_R", "hand_L", "hand_R"], jpos["waist"], 1),
        "stance_ankle(pitch, all but stance foot)": (list(allids - {"foot_R"}), jpos["ankle_R"], 0),
        "stance_knee(pitch, all but stance shank+foot)": (list(allids - {"foot_R", "shank_R"}), jpos["knee_R"], 0),
        "stance_hip(pitch, upper body + swing leg)": (list(allids - {"foot_R", "shank_R", "thigh_R"}), jpos["hip_R"], 0),
        "stance_hip(yaw, upper body + swing leg)": (list(allids - {"foot_R", "shank_R", "thigh_R"}), jpos["hip_R"], 1),
    }
    print("\nPD gains: k = I wn^2, d = 2 zeta I wn (zeta = 1)")
    out = {}
    for name, (ids, piv, ax) in cases.items():
        I = subtree_I_about(frame, ids, piv, ax)
        row = [I]
        for f in (0.5, 1.0):
            wn = 2 * math.pi * f
            row += [I * wn**2, 2 * I * wn]
        out[name] = row
        print(f"  {name:45s} I={I:.3e}  f=0.5Hz k={row[1]:.2e} d={row[2]:.2e} | f=1Hz k={row[3]:.2e} d={row[4]:.2e}")
    # gravity torque on a swing leg / arm held horizontal-ish
    leg = ["thigh_R", "shank_R", "foot_R"]
    mleg = sum(seg[i]["mass_kg"] for i in leg)
    hip = jpos["hip_R"]
    rcm = sum(seg[i]["mass_kg"] * (hip[1] - seg[i]["com"][1]) for i in leg) / mleg
    print(f"\nleg mass {mleg/1e3:.0f} t, leg CoM {rcm:.2f} m below hip; gravity torque at 30 deg flexion {mleg*G*rcm*0.5:.3e} N m; at 90 deg {mleg*G*rcm:.3e}")
    arm = ["upper_arm_R", "forearm_R", "hand_R"]; marm = sum(seg[i]["mass_kg"] for i in arm)
    sh = jpos["shoulder_R"]; ra = sum(seg[i]["mass_kg"] * (sh[1] - seg[i]["com"][1]) for i in arm) / marm
    print(f"arm mass {marm/1e3:.0f} t, arm CoM {ra:.2f} m from shoulder; torque held horizontal {marm*G*ra:.3e} N m")
    # foot-limited ankle torque
    fs = frame["foot_sole"]
    print(f"\nfoot-limited ankle torque (single stance, CoP at toe) {W*fs['toe_z_m']:.3e}, heel {W*-fs['heel_z_m']:.3e}, side {W*fs['width_m']/2:.3e} N m")
    # Froude walking
    Lleg = jpos["hip_R"][1]
    v = math.sqrt(0.25 * G * Lleg)
    print(f"leg {Lleg} m, Fr 0.25 speed {v:.3f} m/s")
    # scaling of human joint data (per kg) to the mech: torque x (L/Lh), power x sqrt(L/Lh)
    Lh = 0.93  # human hip-joint height, m (0.53 x 1.75 m)
    rl = Lleg / Lh
    print(f"length ratio {rl:.2f}, sqrt {math.sqrt(rl):.3f}")
    for jn, (tmin, tmax, pmin, pmax) in {"ankle": (1.4, 1.7, 2.5, 3.5), "knee": (0.5, 0.9, 0.8, 1.0), "hip": (0.8, 1.35, 0.7, 1.45)}.items():
        print(f"  {jn:5s} peak torque {tmin*rl*M:.2e}-{tmax*rl*M:.2e} N m, peak power {pmin*math.sqrt(rl)*M/1e6:.0f}-{pmax*math.sqrt(rl)*M/1e6:.0f} MW")
    for cot in (0.05, 0.075, 0.10):
        print(f"  mech CoT {cot}: mean positive mech power {cot*W*v/1e6:.1f} MW")
    # brace stance
    knee = jpos["knee_R"][1] - jpos["ankle_R"][1]
    for flex in (20, 40):
        a = math.radians(flex / 2)
        m_above = M - 2 * (seg["shank_R"]["mass_kg"] + seg["foot_R"]["mass_kg"])
        off = knee * math.sin(a)
        print(f"  brace knee flex {flex}: knee torque per leg {(m_above*G/2)*off:.3e} N m (offset {off:.2f} m)")
    # ground pressure
    A = fs["area_m2"]
    print(f"\nfoot area {A} m2; double stance {W/(2*A)/1e3:.0f} kPa; single {W/A/1e3:.0f} kPa; x1.2 {1.2*W/A/1e3:.0f}; x1.5 {1.5*W/A/1e3:.0f}")
    for name, psf in (("clay/silt", 1500), ("sand", 2000), ("sandy gravel", 3000), ("sedimentary rock", 4000), ("crystalline bedrock", 12000)):
        print(f"  IBC 1806.2 {name:20s} {psf} psf = {psf*47.88/1e3:.1f} kPa allowable")
    # falls
    print("\nfall energetics")
    print(f"  m g h_cm = {M*G*c[1]:.3e} J = {M*G*c[1]/4.184e9:.2f} t TNT")
    h_lie = frame["segments"][1]["depth_z_m"] / 2  # torso half depth: CoM height when lying prone (approx)
    dE = M * G * (c[1] - h_lie)
    print(f"  released to prone (CoM {h_lie:.1f} m): {dE:.3e} J = {dE/4.184e9:.2f} t TNT")
    for pivot_name, pz in (("forward over toes", fs["toe_z_m"]), ("backward over heels", fs["heel_z_m"])):
        r2 = (c[1])**2 + (c[2] - pz)**2
        Ip = Iw[0] + M * r2
        r = math.sqrt(r2)
        lam = math.sqrt(M * G * r / Ip)
        # energy: CoM falls from r (above pivot) to h_lie
        w_imp = math.sqrt(2 * M * G * (r - h_lie) / Ip)
        # time to fall from tilt th0 to horizontal (integrate theta'' = lam^2 sin theta)
        def tfall(th0):
            th, om, t, dt = th0, 0.0, 0.0, 1e-4
            while th < math.radians(90 - math.degrees(math.asin(min(1, h_lie / r)))):
                al = lam**2 * math.sin(th); om += al * dt; th += om * dt; t += dt
            return t, om
        t1, o1 = tfall(math.radians(1)); t5, o5 = tfall(math.radians(5)); t15, o15 = tfall(math.radians(15))
        print(f"  pivot {pivot_name}: r={r:.2f} m Ipivot={Ip:.3e} lambda={lam:.3f} 1/s; impact omega {w_imp:.3f} rad/s; "
              f"head-top speed {w_imp*math.hypot(frame['total_height_m'], 0):.1f} m/s, CoM speed {w_imp*r:.1f} m/s; "
              f"t_fall from 1/5/15 deg = {t1:.2f}/{t5:.2f}/{t15:.2f} s")
    # free-fall integrator bias (semi-implicit Euler) for the drop test
    for dt in (1/50, 1/60, 1/120):
        for t in (1.0, 2.0):
            print(f"  drop test dt={dt:.4f} t={t}: expected relative energy error dt/t = {dt/t:.4f}")
    # friction need
    for a in (0.25, 0.5, 1.0, 2.0):
        print(f"  mu needed for horizontal CoM accel {a} m/s2 (double support, flat): {a/G:.3f}; + gait shear 0.2 -> {0.2 + a/G:.3f}")
    # float32 resolution
    for x in (1e3, 1e4, 5e4, 1e5):
        e = math.floor(math.log2(x)); print(f"  float32 ulp at {x:.0e} m: {2.0**(e-23):.2e} m")


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--height", type=float, default=REF_H)
    ap.add_argument("--mass", type=float, default=REF_M)
    ap.add_argument("--out", default="research/R-005/mech_frame_balanced.json")
    ap.add_argument("--no-write", action="store_true")
    a = ap.parse_args()
    fr = build(a.height, a.mass)
    assert abs(sum(MASS_FRAC.values()) + sum(MASS_FRAC[k] for k in ("upper_arm", "forearm", "hand", "thigh", "shank", "foot")) - 1.0) < 1e-9
    assert abs(fr["total_mass_kg"] - a.mass) < 1.0
    if not a.no_write:
        with open(a.out, "w") as f:
            json.dump(fr, f, indent=1)
        print("wrote", a.out)
    report(fr)
