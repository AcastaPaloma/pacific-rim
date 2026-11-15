"""T-005 r7: variant frame / body json files for the offline optimiser (R7 'frame' / 'body' keys, env R7_FRAME / R7_BODY for planar_model and the exporter); the default configs are never edited.
Usage: mkbody.py <outdir>  -> frame_toe7.json (toe lever 7 m, foot mass kept), body_ank16.json (ankle limit 1.6e9 N m)."""
import json, sys, os, copy
out = sys.argv[1]; os.makedirs(out, exist_ok=True)
J = json.load(open('Assets/Game/Config/Mech/mech_frame_balanced.json'))
f = copy.deepcopy(J); toe = 7.0; heel = f['foot_sole']['heel_z_m']; d = toe - heel; fs = f['foot_sole']
fs['area_m2'] *= d / fs['length_m']; fs['length_m'] = d; fs['toe_z_m'] = toe
for s in f['segments']:
    if s['kind'] == 'foot':
        m, w, h, d0 = s['mass_kg'], s['width_x_m'], s['height_y_m'], s['depth_z_m']
        s['depth_z_m'] = d; s['length_m'] = d; s['distal_end'][2] = toe; s['com'][2] = 0.5 * (heel + toe); s['hull_volume_m3'] *= d / d0
        s['inertia_principal_kgm2'] = [m * (h * h + d * d) / 12, m * (w * w + d * d) / 12, m * (w * w + h * h) / 12]
json.dump(f, open(os.path.join(out, 'frame_toe7.json'), 'w'), indent=1)
B = json.load(open('Assets/Game/Config/Mech/body_tuning.json'))
for k in B['joint_kinds']:
    if k['kind'] == 'ankle': k['force_limit_nm'] = 1.6e9
json.dump(B, open(os.path.join(out, 'body_ank16.json'), 'w'), indent=1)
print('toe lever', toe, 'sole', d, 'ankle limit 1.6e9')
