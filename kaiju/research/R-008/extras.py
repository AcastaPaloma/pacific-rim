"""R-008 extras: flat-foot cap, data-change sensitivities, perception table, lateral LIPM, power sanity.
Run from the project root: /opt/miniconda3/envs/kaiju/bin/python research/R-008/extras.py"""
import sys, os, numpy as np
sys.path.insert(0, os.path.dirname(__file__)); import model as m
from model import solve, gait, l, g, M, W, HW
print("== flat-foot gait (no heel strike pitch, no heel rise, no pelvis yaw): max Froude / step at margin 0, beta 0.64")
for kmin in (5, 15, 25):
    for ks in (1.0, 0.85):
        lo, hi = 0.002, 0.3
        for _ in range(14):
            mid = 0.5 * (lo + hi); r = solve(mid, kstep=ks, phs_max=0, fto_max=0, psi_max=0, kmin=kmin, starts=6)
            (lo := mid) if (r is not None and r.x[12] >= 0) else (hi := mid)
        v, s, T = gait(lo, ks); print(f"stance knee >= {kmin:2d} deg, step x{ks}: Fr {lo:.3f}  v {v:.2f} m/s  step {s:.1f} m ({s/l:.2f} l)")
def fr_at(margin, **kw):
    lo, hi = 0.01, 0.45
    for _ in range(12):
        mid = 0.5 * (lo + hi); r = solve(mid, flat_ss=0.75, kmin=5, starts=6, **kw)
        (lo := mid) if (r is not None and r.x[12] >= margin) else (hi := mid)
    return lo
print("== kinematic Froude ceiling (foot flat >= 75 % of single support, knee >= 5 deg) at 0 / 5 deg joint margin")
for nm, kw in (("S1 R-007 pattern bounds", {}), ("S2 human-like rockers + tilt", dict(phs_max=25, fto_max=40, tilt_max=10))):
    for ks in (1.0, 0.85):
        print(f"{nm}, step x{ks}: Fr {fr_at(0, kstep=ks, **kw):.3f} / {fr_at(5, kstep=ks, **kw):.3f}")
print("== data sensitivities at Fr 0.20, S1 bounds, nominal step, flat >= 75 %: joint margin (deg)")
base = dict(flat_ss=0.75, kmin=5, starts=8)
def marg(**kw): r = solve(0.20, **base, **kw); return r.x[12] if r is not None else float("nan")
print(f"as given: {marg():+.1f}")
for d in (-25, -30):
    m.ANK = [d, 40]; print(f"ankle dorsiflexion limit {d}: {marg():+.1f}"); m.ANK = [-20, 40]
m.HIP = [-25, 120]; print(f"hip extension limit -25: {marg():+.1f}")
m.ANK = [-25, 40]; print(f"hip -25 and ankle -25: {marg():+.1f}"); m.ANK = [-20, 40]; m.HIP = [-20, 120]
for heel, toe in ((4.0, 11.0), (3.0, 12.0), (6.5, 8.5)):
    m.HEEL, m.TOE = heel, toe; print(f"ankle {heel} m from heel (toe lever {toe} m): {marg():+.1f}")
m.HEEL, m.TOE = 5.25, 9.75
print(f"pelvic tilt allowed 10 deg (no ROM change): {marg(tilt_max=10):+.1f}; rockers 25/40 deg: {marg(phs_max=25, fto_max=40):+.1f}")
print("== perception (R-001 relations, l = 42.4 m) and the measured gait")
for Fr in (0.023, 0.05, 0.10, 0.15, 0.20):
    v, s, T = gait(Fr); print(f"Fr {Fr:.3f}: v {v:.2f} m/s ({v*3.6:.1f} km/h), natural step {s:.1f} m, stride {T:.2f} s, cadence {120/T:.1f} steps/min,"
                              f" human-equivalent speed {np.sqrt(Fr*g*0.93):.2f} m/s, time stretch vs human x{np.sqrt(l/0.93):.2f}")
v, s = 3.07, 10.5; T = 2 * s / v; Ta = 2.3 * (v**2 / (g * l)) ** -0.2 * np.sqrt(l / g)
print(f"measured: v {v} m/s, step {s} m ({s/l:.2f} l), step time {T/2:.2f} s, cadence {120/T:.1f} steps/min; natural stride at this Fr {Ta:.1f} s"
      f" -> stride ratio {T/Ta:.2f}; natural step at this Fr {1.15*(v**2/(g*l))**0.3*l:.1f} m")
print("== lateral LIPM (no double support, conservative): CoM crosses the midline at the support switch")
for Fr in (0.10, 0.20):
    v, s, T = gait(Fr); w = np.sqrt(g / 42.5)
    for p in (HW, HW - 2.9, HW - 5.0):
        print(f"Fr {Fr}: lateral CoP {p:.1f} m from midline -> peak lateral accel {w**2*p:.2f} m/s2 ({w**2*p/g:.3f} g); sway amplitude {p - p/np.cosh(w*T/4):.2f} m")
print(f"lateral CoP offset for the M-001.5 limit 0.834 m/s2: {0.834/(g/42.5):.2f} m from midline")
print("== pelvis yaw and M-001.8 (yaw accel <= 4.0 deg/s2) if measured on the pelvis")
for Fr in (0.10, 0.20):
    v, s, T = gait(Fr); w = 2 * np.pi / T
    print(f"Fr {Fr}: psi 4 deg -> {4*w**2:.1f} deg/s2, 8 deg -> {8*w**2:.1f} deg/s2, peak yaw rate 8 deg -> {8*w:.1f} deg/s")
print("== power and first step")
ke = 0.5 * M * 3.07**2; print(f"KE at 3.07 m/s {ke/1e6:.0f} MJ; over the 5.2 s start: {ke/5.2/1e6:.0f} MW mean; 2,027 MW peak = {2027e6/(ke/5.2):.0f} x that")
for Fr in (0.10, 0.20):
    v, s, T = gait(Fr); print(f"Fr {Fr}: mean mechanical power at CoT 0.05-0.10: {0.05*W*v/1e6:.0f}-{0.10*W*v/1e6:.0f} MW")
print(f"measured CoT at 3.07 m/s, 94 MW: {94e6/(W*3.07):.2f}")
