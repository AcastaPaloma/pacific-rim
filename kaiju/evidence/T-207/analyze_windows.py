"""T-207: window sheets, per-impact changed-pixel share, pixel-grid check and palette check on a record run.
Run: python analyze_windows.py FRAMES_DIR record.json OUT_DIR [palette_asset]"""
import sys, json, re, os
import numpy as np
from PIL import Image
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "Pipelines", "capture"))
from capture_tools import scale as sc
fr, rec, out = sys.argv[1:4]
pal_asset = sys.argv[4] if len(sys.argv) > 4 else os.path.join(HERE, "..", "..", "Assets/Game/Config/Rendering/PaletteProfiles/warm_amber_haze.asset")
r = json.load(open(rec)); ev = r["scripted_events"]
cuts = [float(re.match(r"t=([\d.]+) destruction cam: cut", e).group(1)) for e in ev if "destruction cam: cut to" in e]
bursts = [(float(re.match(r"t=([\d.]+)", e).group(1)), e) for e in ev if "destruction fx burst" in e]
tr = r["trace_1s"]; g0 = tr[0]["game_t"] - tr[0]["frame"] / 30.0
fidx = lambda t: int(round((t - g0) * 30))
L = lambda i: np.asarray(Image.open(f"{fr}/f{i:06d}.png").convert("RGB"))
# palette (rgba packed uint32; try ABGR little-endian: r = v & 255)
pal = set()
txt = open(pal_asset).read(); sec = txt.split("colours:")[1].split("roles:")[0] if "roles:" in txt else txt.split("colours:")[1]
for m in re.finditer(r"rgba: (\d+)", sec):
    v = int(m.group(1)); pal.add(((v & 255), (v >> 8) & 255, (v >> 16) & 255))
def pal_share(im):
    f = im.reshape(-1, 3); k = (f[:, 0].astype(np.int64) << 16) | (f[:, 1].astype(np.int64) << 8) | f[:, 2]
    pk = {(a << 16) | (b << 8) | c for a, b, c in pal}; u, cnt = np.unique(k, return_counts=True)
    inn = sum(int(c) for x, c in zip(u, cnt) if int(x) in pk); return inn / k.size, len(u)
def chg(a, b):
    m = np.abs(a.astype(int) - b.astype(int)).max(-1) > 16; H, W = m.shape
    return float(m.mean()), float(m[H // 5:4 * H // 5, W // 4:3 * W // 4].mean())
def sheet(idxs, path):
    ims = [np.asarray(Image.fromarray(L(i)).resize((480, 270))) for i in idxs]
    Image.fromarray(np.concatenate([np.concatenate(ims[:4], 1), np.concatenate(ims[4:8], 1)], 0)).save(path)
res = {"palette_colours": len(pal), "impacts": []}
for n, ct in enumerate(cuts, 1):
    c = fidx(ct); sheet([c + int(k * 30 * 4.5 / 7) for k in range(8)], f"{out}/impact{n}_window.png")
    ent = {"impact": n, "cut_frame": c}
    # burst of this impact: first burst after the cut
    bs = [b for b in bursts if b[0] >= ct]
    if bs:
        bt, be = bs[0]; b = fidx(bt); ent["burst_frame"] = b; ent["burst_event"] = be
        sheet([b - 3] + [b + k for k in (3, 6, 9, 15, 24, 36)] + [b + 54], f"{out}/impact{n}_fx_window.png")
        before = L(b - 2)
        peak = max(range(b + 2, b + 20, 2), key=lambda i: chg(before, L(i))[0])
        ent["fx_peak_frame"] = peak; ent["changed_peak_whole"], ent["changed_peak_centre"] = [round(x, 4) for x in chg(before, L(peak))]
        ent["changed_end_whole"], ent["changed_end_centre"] = [round(x, 4) for x in chg(before, L(b + 60))]
        ent["changed_mid_whole"], ent["changed_mid_centre"] = [round(x, 4) for x in chg(before, L(b + 20))]
        gs = [round(max(sc.blocks_uniform(L(i), 4, (ox, oy)) for ox in range(4) for oy in range(4)), 4) for i in (b - 2, b + 3, b + 9, b + 20, b + 40)]
        ent["grid_share_burst_frames"] = gs
        ps = [pal_share(L(i)) for i in (b - 2, b + 3, b + 9, b + 20)]; ent["palette_share_burst_frames"] = [round(p[0], 5) for p in ps]; ent["distinct_colours"] = [p[1] for p in ps]
    res["impacts"].append(ent)
json.dump(res, open(f"{out}/cut_frame_checks.json", "w"), indent=1)
print(json.dumps(res["impacts"], indent=0))
