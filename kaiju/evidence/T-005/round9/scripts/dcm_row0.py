#!/usr/bin/env python
"""T-005 r9: offline row-0 DCM test. For each ladder row: reference DCM offset b_ref = (x_com + v/omega) - x_stance_heel at the lift-off node (k_ds),
and the time a standing mech needs to reach it with the CoP held at the rear limit of the stance sole (LIPM, xi - c = (xi0 - c) exp(omega t)).
The CoM proxy is the pelvis (planar model hip); omega = sqrt(g / z). Usage: dcm_row0.py <ladder.json> ..."""
import json, sys, math
G = 9.81


def row_test(r, cop_heel_m=1.5):
    nodes = r['nodes']; n = len(nodes); sch = r['schedule']; T = r['step_time_s']
    dt = nodes[1]['t_s'] - nodes[0]['t_s']
    k = sch['k_ds']; x = [nd['pelvis_x_m'] for nd in nodes]; z = nodes[k]['pelvis_z_m']
    om = math.sqrt(G / z); v = (x[k + 1] - x[k - 1]) / (2 * dt)
    heel = r['heel_x_m']; toe = r['toe_x_m']   # relative to the stance ankle; pelvis_x_rel_m is the pelvis relative to it
    xi = nodes[k]['pelvis_x_rel_m'] + v / om; b = xi - heel
    xc = heel + cop_heel_m; x0 = 0.5 * (heel + toe); e0 = x0 - xc; e1 = xi - xc
    tl = math.log(e1 / e0) / om if e1 > e0 > 0 else float('nan')
    return dict(fr=r['fr'], v_ref=round(v, 2), omega=round(om, 3), dcm_off_m=round(b, 2), sole_len=toe - heel,
                toe_margin=round(toe - xi, 2), t_launch_s=round(tl, 2), T=T, ratio_launch_to_T=round(tl / T, 2))


if __name__ == '__main__':
    for f in sys.argv[1:]:
        d = json.load(open(f)); print('#', f)
        for r in d['rows']:
            try: print(row_test(r))
            except Exception as e: print('row', r.get('fr'), 'ERR', repr(e))
