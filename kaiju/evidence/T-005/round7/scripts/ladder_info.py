import json, sys
f = sys.argv[1] if len(sys.argv) > 1 else 'Assets/Game/Config/Mech/gait_reference_qp_ladder_v4.json'
d = json.load(open(f))
print(d['notes'][:1200]); print(d['conventions'])
for r in d['rows']:
    print({k: (round(v, 3) if isinstance(v, float) else v) for k, v in r.items() if k not in ('nodes', 'source_file', 'source')})
n = d['rows'][0]['nodes'][0]
print({k: (v if not isinstance(v, dict) else list(v.keys())) for k, v in n.items()})
