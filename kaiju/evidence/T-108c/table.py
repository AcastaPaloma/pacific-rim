"""Build evidence/T-108c/before_after.csv from the baked manifests (cache) and evidence/T-108b/before_after.csv."""
import json, glob, csv
before = {r['tile']: r for r in csv.DictReader(open('evidence/T-108b/before_after.csv'))}
print("tile,tris_T108_unfiltered,tris_T108b_prefilter,tris_T108c_ground,cap150k_T108b,cap150k_T108c,thr_for_cap_m_T108b,thr_for_cap_m_T108c,replaced_pct,footprint_kept_pct,err_rms_T108b,err_rms_T108c_all,err_rms_T108c_outside_zone,err_max_T108b,err_max_T108c_outside_zone,err_max_T108c_inside_zone,max_drop_m,b1_tris,b1_within_100k")
for f in sorted(glob.glob('Pipelines/geo/cache/tiles/v1/L*/*/manifest.json')):
    m = json.load(open(f)); t = m['tile']; T = m['terrain']; g = T['ground']; e = m['errors']; z = e['terrain_ground_zone']
    b = before.get(t, {}); b1 = e['buildings']['forms'].get('b1', {})
    print(f"{t},{T['rtin_triangles_unfiltered']},{T['rtin_triangles_before_ground']},{T['rtin_triangles']},{b.get('cap150k_after','')},{T['rtin_triangles']<=150000},{b.get('thr_for_cap_m_after','')},{T['rtin_threshold_for_cap_m']},{g['replaced_pct']},{g['footprint_nodes_kept_pct']},{b.get('err_rms_after','')},{e['terrain']['rms_m']},{z['outside']['rms_m']},{b.get('err_max_after','')},{z['outside']['max_m']},{z['inside']['max_m']},{g['max_drop_m']},{b1.get('triangles','')},{b1.get('within_cap','')}")
