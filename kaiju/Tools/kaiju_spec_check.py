#!/usr/bin/env python3
"""M-103 species physical consistency (R-102 section 11.3), T-112.

Loads Assets/Game/Units/Kaiju/species/*.json (KaijuSpeciesSpec@v1) plus kaiju_tissue_v1.json, recomputes mass, limb stress,
gait, attack, water and ground numbers from the primitives, compares with declared values and sourced bounds.
Mirrored in C# by Game.Units.Kaiju.SpeciesValidator (DotnetTests/Units.Kaiju.Tests). Thresholds are constants here and are never
read from the spec (a spec cannot lower them). Usage: kaiju_spec_check.py [species_dir] [--json]. Exit 0 = PASS.
"""
import glob, json, math, os, sys

G = 9.80665
MECH_MASS = 10_000_000.0
MECH_MELEE_UPPER_J = 460e6
RHO_BAND = (850.0, 1100.0)
ELEPHANT_MASS_BAND = (3000.0, 6000.0)
HEIGHT_CLASS = {"Light": 60.0, "Mid": 100.0, "Heavy": 160.0}
SF_WALK, SF_CHARGE, UTIL_MAX = 2.5, 2.0, 0.85
FR_CRUISE_BAND = (0.10, 0.30)
STATIC_PRESSURE_MAX = 1.2e6
GRAVITY_KINDS = ("Stomp", "Slam")
INERTIAL_KINDS = ("Bite", "Rake", "Swipe", "TailSweep", "Ram")
DEFAULT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Game", "Units", "Kaiju", "species")


def frus(L, r1, r2):
    return math.pi * L / 3.0 * (r1 * r1 + r1 * r2 + r2 * r2)


def ell(a, b, c):
    return 4.0 / 3.0 * math.pi * a * b * c


def v(node, *path):
    """Value of a SpecNumber at path."""
    for p in path:
        node = node[p]
    return float(node["value"])


def walk_numbers(node, prefix=""):
    """Yield (path, SpecNumber) for every {value,unit,source,status} leaf."""
    if isinstance(node, dict):
        if "value" in node and "status" in node:
            yield prefix, node
        else:
            for k, c in node.items():
                yield from walk_numbers(c, prefix + "." + k if prefix else k)
    elif isinstance(node, list):
        for i, c in enumerate(node):
            yield from walk_numbers(c, "%s[%d]" % (prefix, i))


def volume(s):
    gm = s["geometry"]
    f = lambda n: (v(gm, n, "L"), v(gm, n, "R_top") if "R_top" in gm[n] else v(gm, n, "R"), v(gm, n, "R_bot") if "R_bot" in gm[n] else v(gm, n, "r"))
    e = lambda n: ell(v(gm, n, "a"), v(gm, n, "b"), v(gm, n, "c"))
    return (e("trunk") + e("head") + frus(*f("neck")) + frus(*f("tail"))
            + v(gm, "n_limbs") * (frus(*f("limb_upper")) + frus(*f("limb_lower"))) + v(gm, "extra_volume_m3"))


def elephant_volume(s):
    r = s["mass"]["recipe_validation"]
    f = lambda n: (v(r, n, "L"), v(r, n, "R_top") if "R_top" in r[n] else v(r, n, "R"), v(r, n, "R_bot") if "R_bot" in r[n] else v(r, n, "r"))
    e = lambda n: ell(v(r, n, "a"), v(r, n, "b"), v(r, n, "c"))
    return (e("elephant_trunk") + e("elephant_head") + frus(*f("elephant_neck")) + frus(*f("elephant_tail"))
            + 4 * (frus(*f("elephant_limb_upper")) + frus(*f("elephant_limb_lower"))) + v(r, "elephant_extra_volume_m3"))


def limb(s, mass, phi, fr):
    """Per-limb force, bone stress, muscle stress (R-102 section 2)."""
    lm = s["limb_model"]
    R = 0.5 * (v(s["geometry"], "limb_upper", "R_top") + v(s["geometry"], "limb_upper", "R_bot"))
    area = math.pi * R * R
    q = v(lm, "cortex_q")
    D = math.sqrt(4.0 * v(lm, "bone_area_share") * area / (math.pi * (1 - q * q)))
    A = math.pi / 4 * D * D * (1 - q * q)
    I = math.pi / 64 * D ** 4 * (1 - q ** 4)
    F = phi * mass * G * (v(lm, "k_a") + v(lm, "k_b") * fr)
    leg = v(s["geometry"], "hip_height_m")
    sb = F / A + F * v(lm, "e_frac") * leg * (D / 2) / I
    sm = F / v(lm, "ema") / (v(lm, "muscle_area_share") * area)
    return dict(area=area, bone_d=D, force=F, sigma_bone=sb, sigma_muscle=sm, limb_d=2 * R)


def stride(s, fr):
    g = s["gait"]
    return v(g, "stride_k") * fr ** v(g, "stride_exp") * math.sqrt(v(s["geometry"], "hip_height_m") / G)


def attack_bound(a, mass, vmax):
    """Returns (kind_class, energy_bound_J, impulse_Ns, ke_J or None)."""
    k = a["kind"]
    m, vel = v(a, "m_eff_kg"), v(a, "v_tip_ms")
    if k in INERTIAL_KINDS:
        ke = 0.5 * m * vel * vel
        return "inertial", min(ke, v(a, "muscle_mass_kg") * v(a, "work_J_kg") * v(a, "elastic_amp")), m * vel, ke
    if k in GRAVITY_KINDS:
        return "gravity", m * G * v(a, "drop_height_m"), m * vel, None
    if k == "Rush":
        t = v(a, "target_mass_kg")
        mu = m * t / (m + t)
        return "rush", 0.5 * mu * vel * vel, (1 + v(a, "restitution")) * mu * vel, 0.5 * m * vel * vel
    return "static", 0.0, 0.0, None


def rel(a, b):
    return abs(a - b) / max(abs(b), 1e-300)


def check_species(s, tissue):
    """Return list of (measure, ok, detail) for one species (all sub-measures except cross-species M-103.2)."""
    out = []
    add = lambda m, ok, d: out.append((m, bool(ok), d))
    g, gm, mt = s["gait"], s["geometry"], s["mass"]
    # M-103.1
    V = volume(s)
    rho = v(mt, "rho_eff_kg_m3")
    m = rho * V
    mdecl = v(mt, "mass_kg")
    ev = elephant_volume(s)
    emin, emax = v(mt, "recipe_validation", "elephant_mass_min_kg"), v(mt, "recipe_validation", "elephant_mass_max_kg")
    ok = (rel(m, mdecl) <= 0.01 and rel(V, v(mt, "volume_m3")) <= 0.01 and RHO_BAND[0] <= rho <= RHO_BAND[1]
          and rel(ev, v(mt, "recipe_validation", "elephant_volume_m3")) <= 0.01
          and ELEPHANT_MASS_BAND[0] <= ev * 860 and ev * 1100 <= ELEPHANT_MASS_BAND[1]
          and emin >= ELEPHANT_MASS_BAND[0] and emax <= ELEPHANT_MASS_BAND[1]
          and abs(v(gm, "height_m") - HEIGHT_CLASS[s["mass_class"]]) <= 10.0)
    iy = m * (v(gm, "trunk", "a") ** 2 + v(gm, "trunk", "b") ** 2) / 5
    ok = ok and rel(iy, v(mt, "inertia_yaw_kgm2")) <= 0.01
    add("M-103.1", ok, "mass %.0f t (declared %.0f t), rho %.0f, elephant %.1f-%.1f t" % (m / 1e3, mdecl / 1e3, rho, ev * 0.86, ev * 1.1))
    # M-103.3 / 4
    bone, mus = v(s["material"], "bone_yield_Pa"), v(s["material"], "muscle_isometric_Pa")
    lm = s["limb_model"]
    fr_w, fr_c = v(g, "fr_cruise"), v(g, "fr_charge_cap")
    phi = v(lm, "phi_peak")
    lw, lc = limb(s, mdecl, phi, fr_w), limb(s, mdecl, phi, fr_c)
    sf_w, sf_c = bone / lw["sigma_bone"], bone / lc["sigma_bone"]
    ut_c = lc["sigma_muscle"] / mus
    sf_decl_ok = v(s["material"], "bone_sf_walk_min") >= SF_WALK and v(s["material"], "bone_sf_charge_min") >= SF_CHARGE and v(s["material"], "muscle_utilisation_max") <= UTIL_MAX
    # real-material extrapolation: Campione log10 M(g) = 2.749 log10 C - 1.104, C = circumference of humerus+femur (mm); report only
    real_bone_d = real_bone_diameter(mdecl)
    real_sf = real_sf_charge(s, mdecl, real_bone_d, tissue, fr_c)
    add("M-103.3", sf_w >= SF_WALK and sf_c >= SF_CHARGE and sf_decl_ok and real_sf < 1.0,
        "SF walk %.2f, charge %.2f (bone %.0f MPa); real-bone SF %.2f (report)" % (sf_w, sf_c, lc["sigma_bone"] / 1e6, real_sf))
    ok4 = ut_c <= UTIL_MAX
    rear = ""
    if v(s["stance"], "rear_mode_allowed") > 0:
        lr = limb(s, mdecl, v(s["stance"], "rear_phi"), v(s["stance"], "rear_fr"))
        ur, sfr = lr["sigma_muscle"] / mus, bone / lr["sigma_bone"]
        ok4 = ok4 and ur <= UTIL_MAX and sfr >= SF_CHARGE and v(s["stance"], "rear_time_max_s") > 0
        rear = ", rear util %.2f SF %.2f" % (ur, sfr)
    elif v(s["stance"], "rear_time_max_s") != 0:
        ok4 = False
    add("M-103.4", ok4, "muscle util charge %.2f%s" % (ut_c, rear))
    # M-103.5
    tb, tm = v(tissue, "bone_yield_Pa"), v(tissue, "muscle_isometric_Pa")
    ok5 = (bone == tb and mus == tm and bone / v(tissue, "real_bone_yield_Pa") <= 3.5 and mus / v(tissue, "real_muscle_isometric_Pa") <= 25.0
           and set(("material.bone_yield_Pa", "material.muscle_isometric_Pa")) <= set(s["provenance"]["assumed_fields"])
           and s["material"]["bone_yield_Pa"]["status"] == "Assumed" and s["material"]["muscle_isometric_Pa"]["status"] == "Assumed")
    bad = [p for p, n in walk_numbers(s) if n["status"] not in ("Sourced", "Derived", "Assumed") or not n.get("source")]
    add("M-103.5", ok5 and not bad, "tissue %.0f MPa / %.1f MPa, ratios %.1fx / %.1fx%s" % (bone / 1e6, mus / 1e6, bone / v(tissue, "real_bone_yield_Pa"), mus / v(tissue, "real_muscle_isometric_Pa"), (", bad fields " + ",".join(bad)) if bad else ""))
    # M-103.6 (spec-side; measured gait_log@v1 half waits on T-117 bodies)
    l = v(gm, "hip_height_m")
    t_ref = lambda f: 2.3 * f ** -0.2 * math.sqrt(l / G)
    tc, tch = v(g, "stride_period_cruise_s"), v(g, "stride_period_charge_s")
    ok6 = (FR_CRUISE_BAND[0] <= fr_w <= FR_CRUISE_BAND[1] and fr_w <= fr_c <= 1.0
           and v(g, "fr_band_min") == FR_CRUISE_BAND[0] and v(g, "fr_band_max") == FR_CRUISE_BAND[1]
           and abs(tc - t_ref(fr_w)) / t_ref(fr_w) <= 0.15 and abs(tch - t_ref(fr_c)) / t_ref(fr_c) <= 0.15
           and rel(stride(s, fr_w), tc) <= 1e-6 and rel(stride(s, fr_c), tch) <= 1e-6
           and v(g, "duty_factor_min") >= 0.55 and v(g, "duty_factor_max") <= 0.90
           and v(g, "duty_factor_min") <= v(g, "duty_factor_cruise") <= v(g, "duty_factor_max")
           and rel(v(g, "cruise_speed_ms"), math.sqrt(fr_w * G * l)) <= 0.01 and rel(v(g, "max_speed_ms"), math.sqrt(fr_c * G * l)) <= 0.01
           and rel(v(g, "yaw_rate_max_dps"), 26.7 * math.sqrt(42.4 / l)) <= 0.01
           and rel(v(g, "yaw_accel_max_dps2"), 4 * 42.4 / l) <= 0.01
           and rel(v(g, "lateral_g_max"), 0.5 * (v(gm, "track_width_m") / 2) / v(gm, "com_height_m")) <= 0.01
           and rel(v(g, "accel_power_cruise_ms2"), v(g, "power_w_per_kg") / v(g, "cruise_speed_ms") - v(g, "resist_c") * G) <= 0.01)
    # stress-limited charge cap: no Fr above the cap may be declared (cap itself passes SF and utilisation, checked in 3/4)
    add("M-103.6", ok6, "Fr %.2f/%.2f, stride %.1f/%.1f s, v %.1f/%.1f m/s (gait_log@v1 measurement pending T-117)" % (fr_w, fr_c, tc, tch, v(g, "cruise_speed_ms"), v(g, "max_speed_ms")))
    # M-103.7
    ok7, worst = True, 0.0
    for a in s["attacks"]:
        cls, bound, imp, ke = attack_bound(a, mdecl, v(g, "max_speed_ms"))
        E = v(a, "E_cap_J")
        good = E <= bound * (1 + 1e-9) and (bound == 0 or rel(E, bound) <= 1e-3) and abs(v(a, "impulse_Ns") - imp) <= 1e-3 * max(imp, 1.0)
        if cls != "gravity":
            good = good and E <= 10 * MECH_MELEE_UPPER_J
        if cls == "gravity":
            good = good and v(a, "v_tip_ms") <= math.sqrt(2 * G * v(a, "drop_height_m")) * 1.01 and v(a, "drop_height_m") > 0
        if cls == "rush":
            good = good and rel(v(a, "m_eff_kg"), mdecl) <= 0.01 and rel(v(a, "v_tip_ms"), v(g, "max_speed_ms")) <= 0.01
        if cls == "inertial":
            good = good and v(a, "muscle_mass_kg") <= 0.35 * mdecl
        if cls == "static":
            fcap = UTIL_MAX * mus * v(a, "muscle_area_m2")
            good = good and 0 < v(a, "hold_force_N") <= fcap
        ok7 = ok7 and good
        worst = max(worst, E)
    add("M-103.7", ok7, "%d attacks, max E_cap %.2f GJ" % (len(s["attacks"]), worst / 1e9))
    # M-103.8
    w = s["water"]
    rw = v(w, "rho_w_kg_m3")
    floats = rho < rw
    ok8 = ((v(w, "floats") > 0) == floats and rel(v(w, "draft_fraction"), min(1.0, rho / rw)) <= 0.01
           and rel(v(w, "belly_clearance_m"), l - v(gm, "trunk", "c")) <= 0.01)
    if v(s["stance"], "swim_speed_max_ms") > 0 and not floats:
        ok8 = False
    add("M-103.8", ok8, "floats %s, draft %.2f, belly %.1f m" % (floats, min(1.0, rho / rw), l - v(gm, "trunk", "c")))
    # M-103.9
    so = s["soil"]
    ps = mdecl * G / (v(gm, "n_limbs") * v(gm, "foot_area_m2"))
    pk = mdecl * G * (v(lm, "k_a") + v(lm, "k_b") * fr_c) / (2 * v(gm, "foot_area_m2"))
    ok9 = (ps <= STATIC_PRESSURE_MAX and rel(ps, v(so, "static_pressure_Pa")) <= 0.01 and rel(pk, v(so, "peak_pressure_Pa")) <= 0.01
           and (v(so, "sinks") > 0) == (ps > v(so, "soil_ultimate_Pa")) and rel(v(gm, "foot_radius_m"), math.sqrt(v(gm, "foot_area_m2") / math.pi)) <= 0.01)
    add("M-103.9", ok9, "static %.3f MPa, peak %.2f MPa, sinks %s" % (ps / 1e6, pk / 1e6, ps > v(so, "soil_ultimate_Pa")))
    # M-103.10
    ok10 = lw["bone_d"] <= 0.9 * lw["limb_d"] and rel(lw["area"], v(lm, "limb_area_m2")) <= 0.01
    add("M-103.10", ok10, "bone D %.2f m vs limb D %.2f m" % (lw["bone_d"], lw["limb_d"]))
    # info: crippled states (not gated)
    d = s["damage"]
    sf1 = bone / limb(s, mdecl, v(d, "phi_one_limb_lost"), fr_c)["sigma_bone"]
    sf2 = bone / limb(s, mdecl, v(d, "phi_two_limbs_lost"), fr_w)["sigma_bone"]
    out.append(("info.crippled", True, "SF charge one limb lost %.2f, walk two limbs lost %.2f (R-102 section 9, not gated)" % (sf1, sf2)))
    return out


def real_bone_diameter(mass):
    """Real bone (humerus/femur) diameter, Campione and Evans trend (R-102 4.2), per bone, metres."""
    c_mm = 10 ** ((math.log10(mass * 1000.0) + 1.104) / 2.749)  # C_{H+F}: humerus + femur circumferences, mm
    return c_mm / 1000.0 / math.pi / 2.0  # per bone


def real_sf_charge(s, mass, real_d, tissue, fr):
    """SF of the real-bone limb at 200 MPa for the same posture and load (report only, must be < 1)."""
    lm = s["limb_model"]
    q = v(lm, "cortex_q")
    A = math.pi / 4 * real_d * real_d * (1 - q * q)
    I = math.pi / 64 * real_d ** 4 * (1 - q ** 4)
    F = v(lm, "phi_peak") * mass * G * (v(lm, "k_a") + v(lm, "k_b") * fr)
    sb = F / A + F * v(lm, "e_frac") * v(s["geometry"], "hip_height_m") * (real_d / 2) / I
    return v(tissue, "real_bone_yield_Pa") / sb


def check_cross(specs):
    """M-103.2 on species sorted by height."""
    ss = sorted(specs, key=lambda s: v(s["geometry"], "height_m"))
    res = []
    for a, b in zip(ss, ss[1:]):
        ratio = v(b["mass"], "mass_kg") / v(a["mass"], "mass_kg")
        iso = (v(b["geometry"], "height_m") / v(a["geometry"], "height_m")) ** 3
        res.append((a["id"] + "->" + b["id"], 0.5 * iso <= ratio <= 2.5 * iso, "mass ratio %.2f vs isometric %.2f (x%.2f)" % (ratio, iso, ratio / iso)))
    return res


def load(species_dir):
    tissue = json.load(open(os.path.join(species_dir, "kaiju_tissue_v1.json")))
    specs = []
    for p in sorted(glob.glob(os.path.join(species_dir, "*.json"))):
        if os.path.basename(p).startswith("kaiju_tissue"):
            continue
        d = json.load(open(p))
        if d.get("schema") != "KaijuSpeciesSpec@v1":
            raise ValueError("%s: schema must be KaijuSpeciesSpec@v1" % p)
        specs.append(d)
    return specs, tissue


def run(species_dir=DEFAULT_DIR):
    """Returns (passed, rows) with rows (species, measure, ok, detail)."""
    specs, tissue = load(species_dir)
    rows = []
    for s in specs:
        for m, ok, d in check_species(s, tissue):
            rows.append((s["id"], m, ok, d))
    for name, ok, d in check_cross(specs):
        rows.append((name, "M-103.2", ok, d))
    return all(r[2] for r in rows) and len(specs) >= 1, rows


def main(argv):
    args = [a for a in argv[1:] if not a.startswith("--")]
    passed, rows = run(args[0] if args else DEFAULT_DIR)
    if "--json" in argv:
        print(json.dumps({"measure": "M-103", "pass": passed, "rows": [dict(species=r[0], measure=r[1], ok=r[2], detail=r[3]) for r in rows]}, indent=1))
    else:
        for r in rows:
            print("%-22s %-13s %s  %s" % (r[0], r[1], "PASS" if r[2] else "FAIL", r[3]))
        print("M-103 %s" % ("PASS" if passed else "FAIL"))
    return 0 if passed else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
