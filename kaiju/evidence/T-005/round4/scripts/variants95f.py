#!/usr/bin/env python
"""T-005 r4 9.5f: build a variants file (name + JSON per line) from a base dict and per-variant deltas, and validate it:
unique keys inside each JSON (JsonUtility keeps the last silently), every key a WalkTuning field (typos are silently ignored),
and every variant's effective dict different from every other. Prints the effective qp_*/imp* values per variant.
Usage: variants95f.py <set> <out.txt>   (set = name of a dict in SETS)"""
import json, re, sys, os, itertools
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '../../../..'))
src = open(os.path.join(ROOT, 'Assets/Game/Units/Mech/Contracts/MechContracts.cs')).read()
FIELDS = set()
for m in re.finditer(r'^\s*public (?:double|int|bool|string|float)\s+([^;(=]+(?:=[^;]*)?(?:,[^;]*)*);', src, re.M):
    for part in re.split(r',(?![^()]*\))', m.group(0)):
        n = re.search(r'([a-z_][a-z0-9_]*)\s*(?:=|;|$)', part.replace('public double', '').replace('public int', '').strip())
        if n: FIELDS.add(n.group(1))
FIELDS |= set(re.findall(r'\b((?:qp|imp|imp2)_[a-z0-9_]+)\b', src))
BASE = {"imp": 3, "imp2_phase_sync": 1, "qp_engage_speed_frac": 0.3, "qp_ladder": 1, "qp_match": 1, "qp_match_ss": 1, "qp_match_climb_td": 1,
        "qp_lateral": 1, "qp_om_y": 0.3, "qp_w_tau": 1, "qp_match_thr": 0.8, "qp_match_up_thr": 1.2, "qp_match_hold": 2,
        "qp_yaw_trunk": 1, "qp_om_yaw": 2.0, "qp_mu": 0.5, "qp_fr_margin": 0.95}
SETS = {
 'f1': {
  'F0base': {},
  'F1arm': {"qp_arm_ff": 1},
  'F2arm06': {"qp_arm_ff": 1, "qp_arm_ff_gain": 0.6},
  'F3arm_hz6': {"qp_arm_ff": 1, "qp_arm_ff_hz": 6.0},
  'F4budget': {"qp_yaw_budget": 1.0},
  'F5arm_budget': {"qp_arm_ff": 1, "qp_yaw_budget": 1.0},
  'F6arm_ref': {"qp_arm_ff": 1, "qp_arm_ff_ref": 1},
  'F7arm_ff0': {"qp_arm_ff": 1, "qp_yaw_ff": 0},
 },
 'f2': {
  'G0base': {},
  'G1refw1': {"qp_arm_ff": 1, "qp_arm_ff_ref_w": 1.0},
  'G2refw1g07': {"qp_arm_ff": 1, "qp_arm_ff_ref_w": 1.0, "qp_arm_ff_gain": 0.7},
  'G3refw05': {"qp_arm_ff": 1, "qp_arm_ff_ref_w": 0.5},
  'G4refw1bud': {"qp_arm_ff": 1, "qp_arm_ff_ref_w": 1.0, "qp_yaw_budget": 1.0},
  'G5bud05': {"qp_yaw_budget": 0.5, "qp_yaw_budget_floor": 0.3},
  'G6refw1ff0': {"qp_arm_ff": 1, "qp_arm_ff_ref_w": 1.0, "qp_yaw_ff": 0},
  'G7refw1hz6': {"qp_arm_ff": 1, "qp_arm_ff_ref_w": 1.0, "qp_arm_ff_hz": 6.0},
  'G8refw1g13': {"qp_arm_ff": 1, "qp_arm_ff_ref_w": 1.0, "qp_arm_ff_gain": 1.3},
 },
}
def _seeded(configs, n):
    # 'seed' = a 1e-4-scale change of qp_pd_scale (0.25 + k 1e-4): the run is deterministic, a perturbation samples the run-to-run spread of one configuration
    return {f'{cn}s{k}': dict(d, qp_pd_scale=round(0.25 + 1e-4 * k, 6)) for cn, d in configs.items() for k in range(n)}
SETS['f3'] = _seeded({
  'Hbase': {},
  'Hmeas': {"qp_arm_ff": 1, "qp_arm_ff_gain": 0.7},
  'Href': {"qp_arm_ff": 1, "qp_arm_ff_ref_w": 1.0, "qp_arm_ff_gain": 0.7},
  'Hbud': {"qp_yaw_budget": 1.0},
  'Hrefbud': {"qp_arm_ff": 1, "qp_arm_ff_ref_w": 1.0, "qp_arm_ff_gain": 0.7, "qp_yaw_budget": 1.0},
}, 4)
def load_set(name):
    return SETS[name]
def check_line(line):
    seen = set()
    def hook(pairs):
        for k, _ in pairs:
            assert k not in seen, 'DUPLICATE key ' + k
            seen.add(k)
        return dict(pairs)
    d = json.loads(line, object_pairs_hook=hook)
    for k in d: assert k in FIELDS, 'UNKNOWN key ' + k
    return d
def main():
    name, out = sys.argv[1], sys.argv[2]
    deltas = load_set(name); lines = []; eff = {}
    for vn, delta in deltas.items():
        d = dict(BASE); d.update(delta)
        # write base keys then delta keys; a delta key already in BASE replaces it, so each key appears once
        s = json.dumps(d, separators=(',', ':'))
        lines.append(vn + ' ' + s); eff[vn] = check_line(s)
    for (a, da), (b, db) in itertools.combinations(eff.items(), 2): assert da != db, f'{a} == {b}'
    open(out, 'w').write('\n'.join(lines) + '\n')
    for vn, d in eff.items(): print(vn, {k: v for k, v in d.items() if k not in BASE or BASE[k] != v})
    print('ok', len(eff), 'variants, keys unique, all known, pairwise different;', out)
main()
