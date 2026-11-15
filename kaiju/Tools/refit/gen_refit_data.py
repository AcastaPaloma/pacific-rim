#!/usr/bin/env python
"""Generates Assets/Game/Config/Refit/parts@v1.json and chassis@v1.json (T-121, research/R-104.md sections 2, 5.4, 9, 10.4).

The two JSON files are the committed data; this script is the calibration record. It states every mass, offset and rating, solves the actuator
(drive-class) mass of each actuated part from the torque rule and the radiator / coolant masses from the heat rule, makes the torso the residual so
that each default build has exactly the class tonnage of economy_prices@v1, and checks the three default builds against the section 9 limits with a
float64 model that mirrors RefitPhysics.cs. Exit code 1 when a default build is out of limits or its tonnage differs from the price table.
Run:  /opt/miniconda3/envs/kaiju/bin/python Tools/refit/gen_refit_data.py [--write]
"""
import json, math, sys, os, copy

G = 9.81
CLASSES = ["structure", "armour", "titanium", "composite", "drive", "reactor", "weapons"]
SLOT_TYPES = ["torso", "head", "reactor_bay", "back_mount", "shoulder", "upper_arm", "forearm", "hip_leg", "foot"]
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Game", "Config", "Refit")
MW = 1.0e6
TORQUE_TARGET = 1.06          # actuator sized to 106% of the demand of its default build (so heavier arms or legs fail: the refit tradeoff)

PHYS = dict(
    frame_height_m=80.0, com_height_frac=[0.45, 0.55], com_lateral_m=1.5, com_fore_aft_m=[-2.0, 3.0], tilt_min_deg=12.0,
    reactor_w_per_kg=200.0, buffer_j=1.0e9, buffer_window_s=20.0, cot=0.34, walk_speed_ms=2.5,
    passive_heat_w=30.0e6, radiator_w_per_kg=200.0, coolant_j_per_kg_k=4200.0, coolant_delta_k=60.0, burst_s=600.0,
    torque_nm_per_kg=2000.0, torque_derate=0.8, ground_pressure_pa=700000.0,
)

B, H, L = "balanced", "heavy_brawler", "basic_balanced"
CHASSIS_DEFS = [  # id, label, stance half width m, alpha rad/s2, mass cap kg, foot area m2
    (B, "Balanced", 12.0, 1.0, 9_900_000, 140.0),
    (H, "Heavy Brawler", 15.0, 1.2, 13_000_000, 190.0),
    (L, "Basic Balanced (city reserve hull)", 12.0, 1.0, 9_900_000, 140.0),
]
LIMBS = {"arm_l": ["shoulder_l", "upper_arm_l", "forearm_l"], "arm_r": ["shoulder_r", "upper_arm_r", "forearm_r"]}
DEFAULT_PART = {  # slot type -> part id, per chassis
    B: dict(torso="torso_balanced", head="head_sensor", reactor_bay="reactor_balanced", back_mount="back_thruster", shoulder="shoulder_standard",
            upper_arm="upper_arm_standard", forearm="forearm_cannon", hip_leg="hip_leg_standard", foot="foot_standard"),
    H: dict(torso="torso_heavy", head="head_sensor_heavy", reactor_bay="reactor_heavy", back_mount="back_thruster_heavy", shoulder="shoulder_heavy",
            upper_arm="upper_arm_heavy", forearm="forearm_fist", hip_leg="hip_leg_heavy", foot="foot_heavy"),
    L: dict(torso="torso_basic", head="head_basic", reactor_bay="reactor_basic", back_mount="back_basic", shoulder="shoulder_basic",
            upper_arm="upper_arm_basic", forearm="forearm_cannon_light", hip_leg="hip_leg_basic", foot="foot_basic"),
}


def slot_layout(stance, torso_y):
    """The one modular frame (R-104 section 9). anchor = nominal part CoM, pivot = joint axis of an actuated slot. x lateral (+ right), y up, z
    forward, origin at the ankle midpoint on the ground. R-104's text says 13 slots but its own enumeration (head, torso, 2 shoulders, 2 upper
    arms, 2 forearms, 2 hip-legs, 2 feet, back mount, reactor bay) is 14; the enumeration is used."""
    s = stance / 12.0
    out = []

    def add(name, typ, parent, anchor, pivot=None, mirror=False, actuated=False, moves_self=False):
        out.append(dict(name=name, type=typ, parent=parent, anchor=anchor, pivot=pivot, mirror=mirror, actuated=actuated, moves_self=moves_self))
    add("torso", "torso", "", [0, torso_y, 0])
    add("head", "head", "torso", [0, 74, 1])
    add("reactor_bay", "reactor_bay", "torso", [0, 38, -3])
    add("back_mount", "back_mount", "torso", [0, 56, -8])
    for side, sg in (("l", -1), ("r", 1)):
        add("shoulder_" + side, "shoulder", "torso", [sg * 14, 62, 0], [sg * 14, 62, 0], sg < 0, True, False)
        add("upper_arm_" + side, "upper_arm", "shoulder_" + side, [sg * 17, 53, 1], [sg * 17, 44, 2], sg < 0, True, False)
        add("forearm_" + side, "forearm", "upper_arm_" + side, [sg * 18, 36, 4], None, sg < 0, False, False)
    for side, sg in (("l", -1), ("r", 1)):
        add("hip_leg_" + side, "hip_leg", "torso", [sg * 12 * s, 20, 0], [sg * 12 * s, 40, 0], sg < 0, True, True)
        add("foot_" + side, "foot", "hip_leg_" + side, [sg * 12 * s, 2, 4], None, sg < 0, False, False)
    return out


# ---- parts -----------------------------------------------------------------------------------------------------------------------------
PARTS = {}


def P(pid, slot, chassis, tier, comp, size, off=(0, 0, 0), power=0.0, pulse=False, heat=None, heat_peak=None, radiator_t=0.0, coolant_t=0.0, bonus=0,
      rare="", src="R-104 s9 (A)", note="", assumed=True):
    PARTS[pid] = dict(id=pid, slot_type=slot, chassis=list(chassis), tier=tier, comp={k: float(v) for k, v in comp.items()}, size=list(size), off=list(off),
                      power=power, pulse=pulse, heat=heat, heat_peak=heat_peak, radiator_t=radiator_t, coolant_t=coolant_t, bonus=bonus, rare=rare,
                      src=src, note=note, assumed=assumed)


def define_parts():
    PARTS.clear()
    # reactor bays: all the reactor-class mass; rated power is 0.2 MW per tonne of reactor-class mass (R-104 s9)
    P("reactor_balanced", "reactor_bay", [B], "standard", dict(reactor=900), (20, 20, 14))
    P("reactor_balanced_uprated", "reactor_bay", [B], "advanced", dict(reactor=1000, structure=40), (22, 22, 14), note="more power, more mass and heat")
    P("reactor_heavy", "reactor_bay", [H], "standard", dict(reactor=1300), (24, 22, 16))
    P("reactor_basic", "reactor_bay", [L], "basic", dict(reactor=800), (20, 20, 14))
    # heads
    P("head_sensor", "head", [B], "standard", dict(structure=30, armour=40, composite=20, weapons=60), (10, 10, 10), power=8 * MW, heat=1 * MW, heat_peak=2 * MW)
    P("head_longrange", "head", [B], "advanced", dict(structure=30, armour=40, composite=20, weapons=100), (10, 12, 12), (0, 0, 1), power=14 * MW, heat=2 * MW, heat_peak=4 * MW)
    P("head_sensor_heavy", "head", [H], "standard", dict(structure=40, armour=80, weapons=60), (12, 12, 12), power=8 * MW, heat=1 * MW, heat_peak=2 * MW)
    P("head_basic", "head", [L], "basic", dict(structure=30, armour=40, weapons=20), (10, 10, 10), power=4 * MW, heat=0.5 * MW, heat_peak=1 * MW)
    # back mounts
    P("back_thruster", "back_mount", [B], "standard", dict(structure=70, armour=20, drive=70), (20, 20, 8), power=100 * MW, heat=2 * MW, heat_peak=12 * MW,
      radiator_t=60, src="R-104 s9: unweighting 20% of the weight about 100 MW for at most 20 s (Balanced)")
    P("back_thruster_heavy", "back_mount", [H], "standard", dict(structure=90, armour=30, drive=100), (22, 22, 9), power=128 * MW, heat=2.6 * MW, heat_peak=15 * MW,
      radiator_t=60, src="R-104 s9: the 100 MW Balanced burst scaled by weight 11,500/9,000 (A)")
    P("back_radiator", "back_mount", [B, H], "standard", dict(structure=120, armour=20), (24, 20, 8), radiator_t=110, note="extra radiators, no burst power")
    P("back_launcher", "back_mount", [B], "advanced", dict(structure=60, armour=20, drive=20, weapons=140), (20, 14, 10), power=40 * MW, pulse=True, heat=1 * MW, heat_peak=10 * MW,
      radiator_t=30, note="missile rack")
    P("back_overdrive", "back_mount", [B, H], "advanced", dict(structure=90, armour=20, drive=110), (20, 20, 8), power=190 * MW, heat=3 * MW, heat_peak=20 * MW, radiator_t=60,
      note="oversized thruster: more burst power than the reactor plant and buffer carry on most builds")
    P("back_magazine", "back_mount", [B, H], "advanced", dict(structure=100, armour=100, weapons=600), (22, 20, 14), (0, -2, -6), radiator_t=0,
      note="siege magazine: a back-mounted ammunition store, heavy and far aft")
    P("back_basic", "back_mount", [L], "basic", dict(structure=80, armour=20), (16, 16, 6), radiator_t=50, note="light radiator pack, city reserve hull")
    # shoulders / upper arms / hips: actuated, drive solved
    P("shoulder_standard", "shoulder", [B], "standard", dict(structure=60, armour=60, drive=0), (10, 10, 10))
    P("shoulder_pod", "shoulder", [B], "advanced", dict(structure=60, armour=60, weapons=80, drive=0), (12, 10, 12), (0, 1, 0), power=20 * MW, pulse=True, note="shoulder weapon pod")
    P("shoulder_heavy", "shoulder", [H], "standard", dict(structure=90, armour=120, drive=0), (12, 12, 12))
    P("shoulder_basic", "shoulder", [L], "basic", dict(structure=80, armour=80, drive=0), (10, 10, 10))
    P("upper_arm_standard", "upper_arm", [B], "standard", dict(structure=80, armour=90, titanium=20, drive=0), (6, 18, 6))
    P("upper_arm_titanium", "upper_arm", [B], "advanced", dict(structure=40, armour=90, titanium=40, drive=0), (6, 18, 6), note="titanium frame: lighter, dearer")
    P("upper_arm_osteoderm", "upper_arm", [B], "rare", dict(structure=80, armour=58, titanium=20, drive=0), (6, 18, 6), rare="osteoderm_plate",
      src="R-104 s5.4: osteoderm specific strength 1.56x HSLA, equal-strength armour mass x 0.64 (bone density (A))",
      note="kaiju bone plate: armour 90 t x 0.64 = 58 t; one salvage set consumed on fitting")
    P("upper_arm_heavy", "upper_arm", [H], "standard", dict(structure=110, armour=160, titanium=30, drive=0), (8, 18, 8))
    P("upper_arm_basic", "upper_arm", [L], "basic", dict(structure=120, armour=90, drive=0), (6, 18, 6))
    P("hip_leg_standard", "hip_leg", [B], "standard", dict(structure=100, armour=40, titanium=40, drive=0), (10, 40, 10))
    P("hip_leg_titanium", "hip_leg", [B], "advanced", dict(structure=50, armour=40, titanium=70, drive=0), (10, 40, 10), note="titanium frame: lighter, dearer")
    P("hip_leg_fibre", "hip_leg", [B], "rare", dict(structure=100, armour=40, titanium=40, drive=0), (10, 40, 10), bonus=1500, rare="actuator_fibre",
      src="R-104 s5.4: tendon fibre torque density bonus, capped at +15% (A, no sourced figure)",
      note="kaiju tendon fibre bundle in the hip actuator: +15% torque, so a lighter actuator; one salvage set consumed on fitting")
    P("hip_leg_heavy", "hip_leg", [H], "standard", dict(structure=140, armour=80, titanium=50, drive=0), (12, 40, 12))
    P("hip_leg_basic", "hip_leg", [L], "basic", dict(structure=140, armour=60, drive=0), (10, 40, 10))
    # forearms (weapon bearing; weapons class includes ammunition, D-019 limited ammo)
    P("forearm_cannon", "forearm", [B], "standard", dict(structure=30, armour=40, composite=20, weapons=200, drive=20), (6, 18, 10), (0, 0, 1), power=30 * MW, pulse=True, heat=1.5 * MW,
      heat_peak=8 * MW, src="R-104 s9: heavy arm cannon pulse 30 MW; 80 t weapon plus ammunition")
    P("forearm_cannon_light", "forearm", [B, L], "basic", dict(structure=55, armour=60, weapons=90, drive=20), (6, 16, 8), (0, 0, 1), power=15 * MW, pulse=True, heat=0.8 * MW, heat_peak=4 * MW)
    P("forearm_blade", "forearm", [B, H, L], "basic", dict(structure=40, armour=60, weapons=70, drive=20), (6, 18, 8), (0, 0, 1), power=22 * MW, pulse=True, heat=1.0 * MW, heat_peak=5 * MW,
      note="melee pulse blade")
    P("forearm_cannon_heavy", "forearm", [H], "standard", dict(structure=60, armour=150, weapons=140, drive=30), (8, 20, 12), (0, 0, 1), power=34 * MW, pulse=True, heat=1.8 * MW, heat_peak=9 * MW)
    P("forearm_siege_cannon", "forearm", [B, H], "advanced", dict(structure=60, armour=80, weapons=380, drive=40), (8, 20, 14), (0, 0, 2), power=48 * MW, pulse=True, heat=2.4 * MW, heat_peak=12 * MW,
      note="siege cannon: very heavy, over-torques most shoulders")
    P("forearm_fist", "forearm", [H], "standard", dict(structure=60, armour=150, weapons=40, drive=30), (8, 18, 10), (0, 0, 1), power=24 * MW, pulse=True, heat=1.2 * MW, heat_peak=6 * MW,
      note="brawler fist")
    # feet
    P("foot_standard", "foot", [B], "standard", dict(structure=15, armour=10, titanium=5), (12, 4, 20))
    P("foot_heavy", "foot", [H], "standard", dict(structure=20, armour=15, titanium=5), (14, 4, 22))
    P("foot_basic", "foot", [L], "basic", dict(structure=20, armour=10), (12, 4, 20))
    # torsos: residuals, filled by solve()
    P("torso_balanced", "torso", [B], "standard", dict(), (24, 22, 14))
    P("torso_heavy", "torso", [H], "standard", dict(), (28, 24, 16))
    P("torso_basic", "torso", [L], "basic", dict(), (24, 22, 14))


# ---- physics model (mirrors RefitPhysics.cs) --------------------------------------------------------------------------------------------

def kg(p):
    return {k: int(round(v * 1000)) for k, v in p["comp"].items() if round(v * 1000) != 0}


def part_mass(p):
    return float(sum(kg(p).values()))


def evaluate(ch, build):
    slots = ch["slots"]
    n = len(slots)
    by_name = {s["name"]: i for i, s in enumerate(slots)}
    Ps = [PARTS[build[s["name"]]] for s in slots]
    m = [part_mass(p) for p in Ps]
    com = []
    for i, s in enumerate(slots):
        o = Ps[i]["off"]
        com.append((s["anchor"][0] + (-o[0] if s["mirror"] else o[0]), s["anchor"][1] + o[1], s["anchor"][2] + o[2]))
    M = sum(m)
    cx = sum(m[i] * com[i][0] for i in range(n)) / M
    cy = sum(m[i] * com[i][1] for i in range(n)) / M
    cz = sum(m[i] * com[i][2] for i in range(n)) / M
    r = {"M": M, "com": (cx, cy, cz)}
    reactor_kg = sum(kg(p).get("reactor", 0) for p in Ps)
    rated = ch["reactor_w_per_kg"] * reactor_kg
    walk = ch["cot"] * M * G * ch["walk_speed_ms"]
    cont = sum(p["power"] for p in Ps if not p["pulse"])
    pulse = max([p["power"] for p in Ps if p["pulse"]] + [0.0])
    r["power"] = (walk + cont + pulse, rated, ch["buffer_j"] / ch["buffer_window_s"])
    qgen = sum(p["heat"] or 0.0 for p in Ps)
    qrej = ch["passive_heat_w"] + ch["radiator_w_per_kg"] * sum(p["radiator_t"] * 1000 for p in Ps)
    r["heat"] = (qgen, qrej)
    cap = sum(p["coolant_t"] * 1000 for p in Ps) * ch["coolant_j_per_kg_k"] * ch["coolant_delta_k"]
    need = max(0.0, sum(p["heat_peak"] or 0.0 for p in Ps) - qrej) * ch["burst_s"]
    r["storage"] = (need, cap)
    desc = {s["name"]: [] for s in slots}
    for s in slots:
        par = s["parent"]
        while par:
            desc[par].append(s["name"])
            par = slots[by_name[par]]["parent"]
    tq = {}
    for i, s in enumerate(slots):
        if not s["actuated"]:
            continue
        moved = list(desc[s["name"]]) + ([s["name"]] if s["moves_self"] else [])
        piv = s["pivot"]
        static = dyn = 0.0
        for nm in moved:
            k = by_name[nm]
            dx, dy, dz = com[k][0] - piv[0], com[k][1] - piv[1], com[k][2] - piv[2]
            rr = math.sqrt(dx * dx + dy * dy + dz * dz)
            ioth = m[k] * max(Ps[k]["size"]) ** 2 / 12.0
            static += m[k] * G * rr
            dyn += (m[k] * rr * rr + ioth) * ch["alpha_rad_s2"]
        demand = static + dyn
        rated_t = ch["torque_nm_per_kg"] * kg(Ps[i]).get("drive", 0) * ch["torque_derate"] * (1 + Ps[i]["bonus"] / 10000.0)
        tq[s["name"]] = (demand, rated_t, rated_t / demand if demand > 0 else 1e9)
    r["torque"] = tq
    h = ch["frame_height_m"]
    r["com_h"] = (cy, ch["com_height_frac"][0] * h, ch["com_height_frac"][1] * h)
    w = ch["stance_half_width_m"]
    tilts = [math.degrees(math.atan2(w - abs(cx), cy))]
    for ln, members in LIMBS.items():
        keep = [i for i, s in enumerate(slots) if s["name"] not in members]
        Mk = sum(m[i] for i in keep)
        tilts.append(math.degrees(math.atan2(w - abs(sum(m[i] * com[i][0] for i in keep) / Mk), sum(m[i] * com[i][1] for i in keep) / Mk)))
    r["tilt"] = (min(tilts), ch["tilt_min_deg"])
    r["class_t"] = {c: sum(kg(p).get(c, 0) for p in Ps) / 1000.0 for c in CLASSES}
    return r


def violations(ch, r):
    out = []
    if r["M"] > ch["mass_cap_kg"]: out.append("mass %.0f t > cap" % (r["M"] / 1000))
    if r["power"][0] - r["power"][1] > r["power"][2]: out.append("power peak %.1f MW vs rated %.1f + %.0f" % (r["power"][0] / MW, r["power"][1] / MW, r["power"][2] / MW))
    if r["heat"][0] > r["heat"][1]: out.append("heat gen %.1f > rej %.1f MW" % (r["heat"][0] / MW, r["heat"][1] / MW))
    if r["storage"][0] > r["storage"][1]: out.append("burst %.1f GJ > storage %.1f GJ" % (r["storage"][0] / 1e9, r["storage"][1] / 1e9))
    for nm, (d, rt, ra) in r["torque"].items():
        if ra < 1.0: out.append("torque %s ratio %.3f" % (nm, ra))
    if not (r["com_h"][1] <= r["com_h"][0] <= r["com_h"][2]): out.append("com height %.2f not in [%.1f, %.1f]" % r["com_h"])
    if abs(r["com"][0]) > ch["com_lateral_m"]: out.append("com lateral %.2f" % r["com"][0])
    if not (ch["com_fore_aft_m"][0] <= r["com"][2] <= ch["com_fore_aft_m"][1]): out.append("com fore-aft %.2f" % r["com"][2])
    if r["tilt"][0] < r["tilt"][1]: out.append("tilt %.2f < %.1f" % r["tilt"])
    return out


def build_of(cid, over=None):
    ch_map = DEFAULT_PART[cid]
    b = {}
    for s in make_chassis(cid)["slots"]:
        b[s["name"]] = ch_map[s["type"]]
    if over:
        for slot_type, pid in over.items():
            for s in make_chassis(cid)["slots"]:
                if s["type"] == slot_type:
                    b[s["name"]] = pid
    return b


_CH = {}
TORSO_Y = {B: 42.0, H: 42.0, L: 42.0}


def make_chassis(cid):
    for (i, label, stance, alpha, cap, area) in CHASSIS_DEFS:
        if i == cid:
            ch = dict(PHYS)
            ch.update(id=i, label=label, stance_half_width_m=stance, alpha_rad_s2=alpha, mass_cap_kg=cap, foot_area_m2=area,
                      slots=slot_layout(stance, TORSO_Y[i]), limbs=LIMBS)
            return ch
    raise KeyError(cid)


def solve():
    define_parts()
    econ = json.load(open(os.path.join(ROOT, "Assets", "Game", "Config", "Economy", "economy_prices@v1.json")))
    econ_t = {c["id"]: {k: float(v) for k, v in c["tonnes"].items()} for c in econ["chassis"]}
    # 1. actuator masses: each actuated part solved in the context of the default build of its first chassis
    for it in range(40):
        changed = False
        for pid, p in PARTS.items():
            if p["slot_type"] not in ("upper_arm", "shoulder", "hip_leg"):
                continue
            cid = p["chassis"][0]
            ch = make_chassis(cid)
            b = build_of(cid, {p["slot_type"]: pid})
            # torso needs placeholder mass so evaluate() does not divide by zero; it is not part of any moved set
            for t in ("torso_balanced", "torso_heavy", "torso_basic"):
                if not PARTS[t]["comp"]:
                    PARTS[t]["comp"] = dict(structure=1)
            r = evaluate(ch, b)
            slot = next(s["name"] for s in ch["slots"] if s["type"] == p["slot_type"])
            demand = r["torque"][slot][0]
            per_t = ch["torque_nm_per_kg"] * 1000 * ch["torque_derate"] * (1 + p["bonus"] / 10000.0)
            need_t = math.ceil(demand * TORQUE_TARGET / per_t)
            if abs(need_t - p["comp"].get("drive", 0)) > 0.5:
                p["comp"]["drive"] = float(need_t); changed = True
        if not changed:
            break
    # 2. torso residual per chassis (class tonnage = economy_prices@v1) and heat parts
    for cid, tname in ((B, "torso_balanced"), (H, "torso_heavy"), (L, "torso_basic")):
        tot = dict(econ_t[cid])
        used = {}
        for st, pid in DEFAULT_PART[cid].items():
            if st == "torso":
                continue
            mult = 1 if st in ("head", "reactor_bay", "back_mount") else 2
            for k, v in PARTS[pid]["comp"].items():
                used[k] = used.get(k, 0.0) + v * mult
        res = {}
        for k in CLASSES:
            v = tot.get(k, 0.0) - used.get(k, 0.0)
            if v < -1e-9:
                print("FAIL residual negative", cid, k, v); return False
            if v > 0: res[k] = v
        PARTS[tname]["comp"] = res
    plated = dict(PARTS["torso_balanced"]["comp"]); plated["armour"] = plated.get("armour", 0.0) + 1200
    P("torso_balanced_plated", "torso", [B], "advanced", plated, (24, 22, 14), note="heavily plated torso: over the ground-pressure mass cap on a standard build")
    # 3. heat: drive-class parts heat 0.01 MW/t (peak 0.03), reactor 0.04 (peak 0.06); torso radiators and coolant solved on the default build
    for pid, p in PARTS.items():
        d = p["comp"].get("drive", 0.0)
        rt = p["comp"].get("reactor", 0.0)
        if p["heat"] is None:
            p["heat"] = 0.01 * MW * d + 0.04 * MW * rt
            p["heat_peak"] = 0.03 * MW * d + 0.06 * MW * rt
        else:
            p["heat"] += 0.01 * MW * d
            p["heat_peak"] += 0.03 * MW * d
    for cid, tname in ((B, "torso_balanced"), (H, "torso_heavy"), (L, "torso_basic")):
        ch = make_chassis(cid)
        b = build_of(cid)
        PARTS[tname]["radiator_t"] = 0.0; PARTS[tname]["coolant_t"] = 0.0
        r = evaluate(ch, b)
        q = r["heat"][0]
        rad_need = max(0.0, q * 1.04 - r["heat"][1]) / ch["radiator_w_per_kg"] / 1000.0
        PARTS[tname]["radiator_t"] = float(math.ceil(rad_need))
        r = evaluate(ch, b)
        cool = math.ceil(r["storage"][0] * 1.05 / (ch["coolant_j_per_kg_k"] * ch["coolant_delta_k"]) / 1000.0)
        PARTS[tname]["coolant_t"] = float(cool)
    return True


def main():
    ok = solve()
    econ = json.load(open(os.path.join(ROOT, "Assets", "Game", "Config", "Economy", "economy_prices@v1.json")))
    econ_t = {c["id"]: {k: float(v) for k, v in c["tonnes"].items()} for c in econ["chassis"]}
    chassis = []
    for (cid, label, stance, alpha, cap, area) in CHASSIS_DEFS:
        ch = make_chassis(cid)
        b = build_of(cid)
        ch["default_build"] = b
        ch["foot_area_m2"] = area
        gp = ch["ground_pressure_pa"] * area / G
        if cap > gp:
            print("FAIL", cid, "mass cap above ground pressure bound", cap, gp); ok = False
        r = evaluate(ch, b)
        v = violations(ch, r)
        print("%-15s M=%7.0f t com=(%.2f, %.2f, %.2f) power %.1f/%.1f (+%.0f MW) heat %.1f/%.1f MW storage %.1f/%.1f GJ tilt %.1f deg" % (
            cid, r["M"] / 1000, *r["com"], r["power"][0] / MW, r["power"][1] / MW, r["power"][2] / MW, r["heat"][0] / MW, r["heat"][1] / MW,
            r["storage"][0] / 1e9, r["storage"][1] / 1e9, r["tilt"][0]))
        for nm, (d, rt, ra) in sorted(r["torque"].items()):
            if nm.endswith("_l"):
                continue
            print("    torque %-12s demand %8.1f MN m rated %8.1f ratio %.3f" % (nm, d / 1e6, rt / 1e6, ra))
        want = econ_t[cid]
        got = {k: v for k, v in r["class_t"].items() if v}
        if got != want:
            print("FAIL class tonnage", cid, got, want); ok = False
        if v:
            print("FAIL", cid, v); ok = False
        chassis.append(ch)
    # every part: sub-masses must fit inside the part
    for pid, p in PARTS.items():
        m = part_mass(p)
        if (p["radiator_t"] + p["coolant_t"]) * 1000 > m:
            print("FAIL sub-mass exceeds part", pid); ok = False
    print("parts", len(PARTS), "torso residuals", {k: PARTS[k]["comp"] for k in ("torso_balanced", "torso_heavy", "torso_basic")})
    if "--write" in sys.argv and ok:
        os.makedirs(OUT, exist_ok=True)
        plist = []
        for pid, p in PARTS.items():
            plist.append(dict(id=pid, slot_type=p["slot_type"], chassis=p["chassis"], tier=p["tier"], composition_kg=kg(p), size_m=p["size"], com_offset_m=p["off"],
                              power_peak_w=p["power"], power_pulse=p["pulse"], heat_w=round(p["heat"], 3), heat_peak_w=round(p["heat_peak"], 3),
                              radiator_kg=int(round(p["radiator_t"] * 1000)), coolant_kg=int(round(p["coolant_t"] * 1000)), torque_bonus_bps=p["bonus"],
                              rare_part=p["rare"], source=p["src"], assumed=p["assumed"], note=p["note"]))
        parts_doc = dict(schema="parts@v1", source="research/R-104.md sections 2, 5.4, 9, 10.4 (T-121); generated by Tools/refit/gen_refit_data.py",
                         classes=CLASSES, slot_types=SLOT_TYPES, parts=plist)
        chassis_doc = dict(schema="chassis@v1", source="research/R-104.md section 9 (T-121); generated by Tools/refit/gen_refit_data.py", chassis=chassis)
        with open(os.path.join(OUT, "parts@v1.json"), "w", newline="\n") as f:
            f.write(json.dumps(parts_doc, indent=1) + "\n")
        with open(os.path.join(OUT, "chassis@v1.json"), "w", newline="\n") as f:
            f.write(json.dumps(chassis_doc, indent=1) + "\n")
        print("written", OUT)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
