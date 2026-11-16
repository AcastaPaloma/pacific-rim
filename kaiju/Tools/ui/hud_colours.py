"""T-150: build / check the sha-checked `hud_colours` data table (R-122 4.2) and CVD dE helper.
Usage: python hud_colours.py build  <kaiju root>   -> writes Assets/Game/Config/UI/hud_colours.json
       python hud_colours.py check  <kaiju root>   -> verifies table sha + profile shas against PaletteProfiles/*.asset
       python hud_colours.py de HEX HEX            -> prints dE76 normal + protan/deutan/tritan (parity helper)
Fills come from research/R-122/out_cvd_palette.json (best4_hex); panel = darkest, text = lightest profile colour."""
import sys, os, json, hashlib, itertools
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', '..', 'research', 'R-122'))
import numpy as np
import cvd_palette as C

def hx(c): return '%02x%02x%02x' % tuple(int(v) for v in c)
def unhex(s): return np.array([int(s[i:i+2], 16) for i in (0, 2, 4)], float)

def pair_de(a, b):
    """dE76 for (normal, protan, deutan, tritan) between two hex colours; same maths as C# CvdMath."""
    A, B = unhex(a), unhex(b)
    return [C.de(A, B)] + [C.de(C.sim(A, k), C.sim(B, k)) for k in ('protan', 'deutan', 'tritan')]

def worst_pair_de(fills):
    return min(min(pair_de(a, b)) for a, b in itertools.combinations(fills, 2))

def profile_sha(cols):
    return hashlib.sha256('\n'.join(sorted(hx(c) for c in cols)).encode()).hexdigest()

def canonical(profiles):
    lines = []
    for n in sorted(profiles):
        p = profiles[n]
        lines.append('%s:%s:%s:%s:%s' % (n, p['panel'], p['text'], ','.join(p['fills']), p['profile_sha256']))
    return '\n'.join(lines)

def build(root):
    prof = C.load(root)
    best = json.load(open(os.path.join(root, 'research/R-122/out_cvd_palette.json')))
    out = {}
    for n, cols in prof.items():
        cs = [np.array(c, float) for c in cols]
        L = [C.lab(c)[0] for c in cs]
        fills = best[n]['best4_hex']
        out[n] = {'panel': hx(cs[int(np.argmin(L))]), 'text': hx(cs[int(np.argmax(L))]), 'fills': fills,
                  'profile_sha256': profile_sha(cols), 'fills_min_de': round(worst_pair_de(fills), 4)}
    doc = {'schema': 1, 'source': 'research/R-122/out_cvd_palette.json', 'profiles': out,
           'table_sha256': hashlib.sha256(canonical(out).encode()).hexdigest()}
    return doc

def check(root):
    path = os.path.join(root, 'Assets/Game/Config/UI/hud_colours.json')
    doc = json.load(open(path)); errs = []
    if hashlib.sha256(canonical(doc['profiles']).encode()).hexdigest() != doc['table_sha256']: errs.append('table sha')
    prof = C.load(root)
    for n, p in doc['profiles'].items():
        if n not in prof: errs.append('unknown profile ' + n); continue
        if profile_sha(prof[n]) != p['profile_sha256']: errs.append('profile sha ' + n)
        have = {hx(c) for c in prof[n]}
        for c in [p['panel'], p['text']] + p['fills']:
            if c not in have: errs.append('%s: %s not a profile entry' % (n, c))
    return errs

if __name__ == '__main__':
    cmd = sys.argv[1]
    if cmd == 'build':
        root = sys.argv[2]
        json.dump(build(root), open(os.path.join(root, 'Assets/Game/Config/UI/hud_colours.json'), 'w'), indent=2); print('written')
    elif cmd == 'check':
        e = check(sys.argv[2]); print('OK' if not e else e); sys.exit(1 if e else 0)
    elif cmd == 'de':
        print([round(v, 4) for v in pair_de(sys.argv[2], sys.argv[3])])
