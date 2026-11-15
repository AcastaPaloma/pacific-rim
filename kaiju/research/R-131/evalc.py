"""Evaluate C7 (1 km / 150 m blocks, scale marker), C6 (layer ordering, near field darkest), M-003 c1, c2 on replayed stills."""
import sys
from replay import *
sys.path.insert(0, os.path.join(ROOT, "Tools", "Metrics"))
from kaiju_metrics.style import measures as M

S0_FORM = 0.272; S0_GROUND = 0.240
BLOCKS = (("near_150m", (0.0, 80.0, -10.0), (0.0, 0.0, -10.0)), ("far_1km", (220.0, 80.0, 840.0), (220.0, 0.0, 840.0)))

def render_set(S, fd, amount_fn, which="mid", s_form=S0_FORM, s_ground=S0_GROUND, post=None):
    P = S.P
    i = {"lo": fd.lo, "hi": fd.hi, "mid": (fd.lo + fd.hi) // 2}[which]
    s0 = S0[i].copy()
    gi = int(round(s_ground * (GRID - 1))); fi = int(round(s_form * (GRID - 1)))
    cg = fd.neutral & fd.ground & (fd.lo <= gi) & (gi <= fd.hi)
    cf = fd.neutral & ~fd.ground & (fd.lo <= fi) & (fi <= fd.hi)
    s0 = np.where(cg, s_ground, np.where(cf, s_form, s0))
    a = amount_fn(fd.dist)
    if post is not None: a = post(fd, a)
    s = s0 + (P.t["airlightValue"] - s0) * a
    s_tab, r_tab, L_tab = rung_table(P)
    Ls = L_tab[np.minimum(np.round(np.clip(s, 0, 1) * (len(s_tab) - 1)).astype(int), len(s_tab) - 1)]
    return np.where(fd.neutral, Ls, fd.L)

def c7_blocks(S, amount_fn, which="mid", post=None, **kw):
    out = {}
    for key, top, bottom in BLOCKS: out[key] = []
    for fd in S.fds:
        L = render_set(S, fd, amount_fn, which, post=post, **kw)
        for key, top, bottom in BLOCKS:
            r = pc.block_contrast(L, fd.z, fd.sky, fd.f, top, bottom)
            if r: out[key].append(r)
    res = {}
    for key, rows in out.items():
        dl = [r["dL_ring3"] for r in rows]
        res[key] = {"in_view": len(rows), "pass": sum(v >= 4.0 for v in dl), "min": min(dl) if dl else None}
    return res

def c6_layers(S, amount_fn, which="mid", post=None, **kw):
    ok = 0; nf = 0; near_ok = 0; minstep = 99
    for fd in S.fds:
        L = render_set(S, fd, amount_fn, which, post=post, **kw)
        upright = (~fd.sky) & (fd.height > 25); means = []
        for lo, hi in ((850, 1250), (2600, 3500), (9000, 11500), (27000, 33000)):
            sel = upright & (fd.dist >= lo) & (fd.dist < hi)
            if sel.sum() >= 20: means.append(float(L[sel].mean()))
        if len(means) == 4:
            nf += 1; d = np.diff(means); ok += bool((d > 0).all()); minstep = min(minstep, d.min())
        near = (~fd.sky) & (fd.dist < 150)
        if near.any() and len(means) == 4: near_ok += bool(L[near].mean() <= min(means))
    return {"frames": nf, "lighter": ok, "near_darkest": near_ok, "min_step": minstep}

def c1_all(sets, amount_fn, which="mid", post=None, **kw):
    """M-003 c1 per profile over the stills of all three viewpoints (worst frame per gate)."""
    edges = [0, 50, 150, 400, 1000, 2500, 6000]
    worst = {"drop": 0.0, "rho": 9.0, "span": 99.0}; nev = 0
    for S in sets:
        for fd in S.fds:
            L = render_set(S, fd, amount_fn, which, post=post, **kw)
            db = M.depth_banding(L, np.where(fd.sky, fd.f["camera"]["far_m"], fd.z), fd.f["camera"]["far_m"], edges, 0.01, 0.99, 3)
            if "rho" in db:
                nev += 1
                worst["drop"] = max(worst["drop"], db["max_drop"]); worst["rho"] = min(worst["rho"], db["rho"]); worst["span"] = min(worst["span"], db["span"])
    worst["frames"] = nev; worst["pass"] = bool(worst["drop"] <= 2.0 and worst["rho"] >= 0.9 and worst["span"] >= 15.0)
    return worst
