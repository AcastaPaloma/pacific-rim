"""T-005 r7: check ladder rows against the round-7 hard limits (CoM-height dip, CoP window, Fz <= 1.5 W, omega T < 1.5, front-foot load before switch).
Usage: python row_limits.py <ladder.json>"""
import json, sys, math
import numpy as np
f = sys.argv[1] if len(sys.argv) > 1 else 'Assets/Game/Config/Mech/gait_reference_qp_ladder_v4.json'
d = json.load(open(f))
for r in d['rows']:
    nd = r['nodes']; N = len(nd); W = r['body_mass_kg'] * 9.81; T = r['step_time_s']; L = r['step_length_m']
    heel, toe = r['heel_x_m'], r['toe_x_m']
    comz = np.array([n['com'][1] for n in nd]); pz = np.array([n['pelvis_z_m'] for n in nd])
    f4 = np.array([n['f4x2'] for n in nd]) / W         # [ft,fn]*4 in W
    fn = f4[:, [1, 3, 5, 7]]; tot = fn.sum(1)
    om = math.sqrt(9.81 / comz.mean())
    # CoP of each foot in the foot frame: use pts x of heel/toe points; leg A heel pt0 toe pt1, leg B heel pt2 toe pt3
    pts = np.array([n['pts'] for n in nd])   # x,z of 4 points
    cop = []
    for leg in (0, 1):
        h, t = fn[:, 2 * leg], fn[:, 2 * leg + 1]; xh, xt = pts[:, 4 * leg], pts[:, 4 * leg + 2]
        c = np.where(h + t > 1e-3, (h * xh + t * xt) / np.maximum(h + t, 1e-9), np.nan)
        # position relative to ankle: ankle x = heel x + 5.25 on a flat foot; use fractions along the sole instead
        frac = np.where(h + t > 1e-3, t / np.maximum(h + t, 1e-9), np.nan)   # toe load share
        cop.append(((c - xh) / np.maximum(xt - xh, 1e-6), (h + t)))
    # CoP position from heel in m (flat foot): frac_toe x sole length (toe-heel)
    sole = toe - heel
    copm = [np.nanmax(((c[0]) * sole)) for c in cop]; copl = [np.nanmin(((c[0]) * sole)) for c in cop]
    # load before switch: front foot (leg touching down at node 0 is leg index with fn>0 at node 0 while previous...)
    ldsfr = [n['stance']['heel_fn_n'] + n['stance']['toe_fn_n'] for n in nd]
    print(f"Fr {r['fr']:.2f} T {T:.2f} s L {L:.1f} m wT {om*T:.2f} comz {comz.min():.2f}..{comz.max():.2f} (dip {comz.max()-comz.min():.2f}) pelvz {pz.min():.2f}..{pz.max():.2f} "
          f"Fz/W max {tot.max():.2f} min {tot.min():.2f} copHeel-offset(min..max over feet) {min(copl):.1f}..{max(copm):.1f} of sole {sole:.1f}")
