"""R-002: group panels into district palette profiles, build 8-24 colour profiles with a haze ramp,
write research/R-002/palettes.json (palette_set@v1) and palettes.png. Run after analyse_panels.py."""
import json, os
import numpy as np
from PIL import Image, ImageDraw
from scipy.cluster.hierarchy import linkage, fcluster
from sklearn.cluster import KMeans
from common import *
from analyse_panels import merge_palette, TOLS

RNG = np.random.default_rng(7)
PROFILES = {  # per GAME_BREAKDOWN §9 hue families; membership checked against the palette-distance clustering below
    "warm_rust_sepia": ["ref_01A", "ref_03A", "ref_03C"],
    "warm_amber_haze": ["ref_05A"],
    "slate_teal": ["ref_01B", "ref_03B"],
    "lavender_peach": ["ref_01C"],
    "slate_crimson": ["ref_03D", "ref_04A"],
}
EXCLUDED = {"ref_04B": "sunset gradient strip of ref_04 (crimson accents); candidate warm-dusk variant, not a v1 profile",
            "ref_06A": "saturated blue/cream outlier; used only as an ordered-dither reference"}
RAMP_N = 8; BODY_K = 24; BODY_MERGE = 5.0; MAX_COLOURS = 24


def palette_distance(pa, wa, pb, wb):
    """Symmetric share-weighted mean nearest-neighbour CIEDE2000 between two palettes."""
    D = de00(pa[:, None], pb[None])
    return 0.5 * (np.average(D.min(1), weights=wa) + np.average(D.min(0), weights=wb))


def build_profile(name, members, pooled, panels):
    sample = np.concatenate([pooled[m][RNG.choice(len(pooled[m]), min(30000, len(pooled[m])), replace=False)]
                             for m in members])
    C = np.hypot(sample[:, 1], sample[:, 2]); H = np.degrees(np.arctan2(sample[:, 2], sample[:, 1])) % 360
    chrom = C >= 8
    z = (C[chrom] * np.exp(1j * np.radians(H[chrom]))).sum(); hdom = np.degrees(np.angle(z)) % 360
    dh = np.abs((H - hdom + 180) % 360 - 180)
    base = sample[(C < 8) | (dh < 60)]            # ramp from dominant-hue + neutral pixels only
    edges = np.percentile(base[:, 0], np.linspace(1, 99, RAMP_N + 1))
    ramp = []
    for i in range(RAMP_N):
        sel = (base[:, 0] >= edges[i]) & (base[:, 0] <= edges[i + 1])
        ramp.append(np.median(base[sel], axis=0))
    ramp = np.array(ramp)
    km = KMeans(BODY_K, n_init=2, random_state=0).fit(sample)
    _, idx = nearest_de00(sample, km.cluster_centers_)
    w = np.bincount(idx, minlength=BODY_K) / len(idx)
    body, bw = merge_palette(km.cluster_centers_[w > 0.002], w[w > 0.002], BODY_MERGE)
    keep = de00(body[:, None], ramp[None]).min(1) >= BODY_MERGE
    body, bw = body[keep], bw[keep]
    accents = [panels[m]["accent"] for m in members if panels[m]["accent"]]
    acc = max(accents, key=lambda a: a["chroma"]) if accents else None
    roles = ["ramp_%d" % i for i in range(RAMP_N)]
    cols = list(ramp)
    order = np.argsort(-bw)
    for i in order:
        if len(cols) >= MAX_COLOURS - (1 if acc else 0): break
        cols.append(body[i]); roles.append("body")
    if acc:
        cols.append(np.array(acc["lab"])); roles.append("accent")
    cols = np.array(cols); cols[:, 0] = np.clip(cols[:, 0], 0, 100)
    return cols, roles, float(hdom), sample


def adherence(lab, pal):
    d, _ = nearest_de00(lab, pal)
    return {str(t): round(float((d <= t).mean()), 4) for t in TOLS}, round(float(np.percentile(d, 95)), 2)


def main():
    pj = json.load(open(os.path.join(OUT, "panels.json")))
    panels = pj["panels"]
    pooled = dict(np.load(os.path.join(CACHE, "_pooled_lab.npz")))
    ids = list(panels)
    P = {k: (np.array(v["palette_lab"]), np.array(v["palette_share"])) for k, v in panels.items()}
    D = np.array([[palette_distance(*P[a], *P[b]) for b in ids] for a in ids])
    cond = D[np.triu_indices(len(ids), 1)]
    Z = linkage(cond, "average"); lab5 = fcluster(Z, 5, "maxclust")
    print("palette distance matrix (dE00):")
    print("        " + " ".join(i[-3:] for i in ids))
    for i, a in enumerate(ids): print(a, " ".join("%3.0f" % x for x in D[i]))
    print("average-linkage 5 clusters (cluster ids arbitrary):", dict(zip(ids, lab5.tolist())))
    profiles = {}
    for name, members in PROFILES.items():
        cols, roles, hdom, sample = build_profile(name, members, pooled, panels)
        member_adh = {m: adherence(pooled[m], cols) for m in members}
        # renderer-ready alternative: the same ramp curve re-sampled at uniform L* steps (see M-NEW-b)
        rl = np.array([c for c, r in zip(cols, roles) if r.startswith("ramp")]); Ls = np.linspace(rl[0, 0], rl[-1, 0], RAMP_N)
        uni = np.array([[np.interp(L, rl[:, 0], rl[:, j]) for j in range(3)] for L in Ls])
        dL = np.diff(rl[:, 0]); cv = float(dL.std() / dL.mean())
        profiles[name] = dict(members=members, dominant_hue_deg=round(hdom, 1), size=len(cols),
                              colours=[dict(role=r, hex=h, lab=np.round(c, 2).tolist())
                                       for r, h, c in zip(roles, hexs(lab_to_srgb_u8(cols)), cols)],
                              member_adherence=member_adh, ramp_dL_cv=round(cv, 2),
                              ramp_uniform_L=[dict(hex=h, lab=np.round(c, 2).tolist()) for h, c in zip(hexs(lab_to_srgb_u8(uni)), uni)])
        print(name, len(cols), {m: a for m, a in member_adh.items()})
    # cross-profile adherence at 5 dE00 (discrimination check)
    cross = {}
    for pid in ids:
        cross[pid] = {n: adherence(pooled[pid], np.array([c["lab"] for c in p["colours"]]))[0]["5"]
                      for n, p in profiles.items()}
    print("cross adherence @5:"); [print(k, v) for k, v in cross.items()]
    out = dict(schema="palette_set@v1", generated_by="research/R-002/scripts/profiles.py (after analyse_panels.py)",
               colour_space=dict(rgb="sRGB IEC 61966-2-1, 8-bit, hex", lab="CIELAB, D65 / 2 deg (skimage rgb2lab)"),
               delta_e="CIEDE2000 (kL=kC=kH=1)", panel_merge_de00=pj["merge_de00"], profile_body_merge_de00=BODY_MERGE,
               panels={k: dict(label=v["label"], source=v["file"], box_xyxy=v["box_xyxy"], palette=[
                   dict(hex=h, lab=l, share=s) for h, l, s in zip(v["palette_hex"], v["palette_lab"], v["palette_share"])],
                   accent=v["accent"]) for k, v in panels.items()},
               profiles=profiles, excluded_from_profiles=EXCLUDED,
               palette_distance_de00=dict(ids=ids, matrix=np.round(D, 2).tolist()),
               cross_profile_adherence_at_5=cross)
    json.dump(out, open(os.path.join(OUT, "palettes.json"), "w"), indent=1)
    # swatch image
    sw, rows = 22, []
    rows += [(k, [c for c in v["palette_hex"]], None) for k, v in panels.items()]
    rows += [("P:" + n, [c["hex"] for c in p["colours"]], [c["role"] for c in p["colours"]]) for n, p in profiles.items()]
    W = 150 + sw * max(len(r[1]) for r in rows); Hh = 8 + rows.__len__() * (sw + 6) + 10
    im = Image.new("RGB", (W, Hh), (255, 255, 255)); dr = ImageDraw.Draw(im)
    y = 8
    for name, cs, roles in rows:
        dr.text((4, y + 5), name, fill=(0, 0, 0))
        for i, c in enumerate(cs):
            x = 150 + i * sw
            dr.rectangle([x, y, x + sw - 2, y + sw], fill=c)
            if roles and roles[i] == "accent": dr.rectangle([x, y + sw - 3, x + sw - 2, y + sw], fill=(0, 0, 0))
        y += sw + 6
    im.save(os.path.join(OUT, "palettes.png"))


if __name__ == "__main__":
    main()
