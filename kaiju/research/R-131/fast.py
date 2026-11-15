"""Fast ROI evaluator for parameter searches: C7 block/ring masks (identical to pixel_checks.block_contrast) and C6 layer windows are
pre-extracted per frame, so a candidate curve costs a few thousand pixel evaluations. Finalists are re-checked with evalc.py (full frames)."""
from evalc import *

def block_masks(fd, top, bottom, half_width_m=15.0):
    f = fd.f; z, sky = fd.z, fd.sky
    bx, by_top, bz = pc.project_rendered(f, top); _, by_bot, _ = pc.project_rendered(f, bottom)
    H, W = z.shape
    if bz <= 0: return None
    hw = half_width_m / bz / math.tan(math.radians(f["camera"]["hfov_deg"]) / 2) * 0.5 * W
    if bx - hw < 30 or bx + hw > W - 30 or by_top < 2 or min(by_bot, H) - by_top < 8: return None
    xs = np.arange(W)[None, :]; ys = np.arange(H)[:, None]
    rows_ = (ys > by_top + 2) & (ys < min(by_bot, H) - 2)
    on = np.abs(z - bz) < 0.06 * bz
    blk = on & (np.abs(xs - bx) < max(1.0, hw - 2)) & rows_
    farther = (z > bz * 1.15) | sky
    ring3 = (np.abs(xs - bx) > hw + 0.5) & (np.abs(xs - bx) < hw + 3.5) & rows_ & farther
    ring = (np.abs(xs - bx) > hw + 1) & (np.abs(xs - bx) < hw + 25) & rows_ & farther
    if blk.sum() < 10 or ring.sum() < 10 or ring3.sum() < 6: return None
    return blk, ring3

class Roi:
    def __init__(self, S, which="mid", s_form=S0_FORM, s_ground=S0_GROUND):
        self.S = S; P = S.P; self.P = P; self.fr = []
        self.s_tab, _, self.L_tab = rung_table(P)
        gi = int(round(s_ground * (GRID - 1))); fi = int(round(s_form * (GRID - 1)))
        for fd in S.fds:
            masks = {}
            for key, top, bottom in BLOCKS:
                m = block_masks(fd, top, bottom)
                if m: masks[key] = m
            up = (~fd.sky) & (fd.height > 25)
            win = {k: up & (fd.dist >= lo) & (fd.dist < hi) for k, (lo, hi) in zip(("1", "3", "10", "30"), ((850, 1250), (2600, 3500), (9000, 11500), (27000, 33000)))}
            near = (~fd.sky) & (fd.dist < 150)
            layers_ok = all(w.sum() >= 20 for w in win.values())
            used = np.zeros(fd.z.shape, bool)
            for v in masks.values(): used |= v[0] | v[1]
            if layers_ok:
                for w in win.values(): used |= w
                used |= near
            idx = np.flatnonzero(used.ravel()); pos = np.full(used.size, -1); pos[idx] = np.arange(len(idx))
            i = {"lo": fd.lo, "hi": fd.hi, "mid": (fd.lo + fd.hi) // 2}[which].ravel()[idx]
            s0 = S0[i]
            neu = fd.neutral.ravel()[idx]; gr = fd.ground.ravel()[idx]
            lo_, hi_ = fd.lo.ravel()[idx], fd.hi.ravel()[idx]
            cg = neu & gr & (lo_ <= gi) & (gi <= hi_); cf = neu & ~gr & (lo_ <= fi) & (fi <= hi_)
            s0 = np.where(cg, s_ground, np.where(cf, s_form, s0))
            sub = lambda m: pos[np.flatnonzero(m.ravel())]
            e = dict(s0=s0, dist=fd.dist.ravel()[idx], neu=neu, Lobs=fd.L.ravel()[idx], blocks={k: (sub(v[0]), sub(v[1])) for k, v in masks.items()},
                     win={k: sub(w) for k, w in win.items()} if layers_ok else None, near=sub(near) if layers_ok else None, sky=fd.sky.ravel()[idx], z=fd.z.ravel()[idx])
            self.fr.append(e)
    def L(self, e, a):
        s = e["s0"] + (self.P.t["airlightValue"] - e["s0"]) * a
        Ls = self.L_tab[np.minimum(np.round(np.clip(s, 0, 1) * (len(self.s_tab) - 1)).astype(int), len(self.s_tab) - 1)]
        return np.where(e["neu"], Ls, e["Lobs"])
    def run(self, amount_fn):
        c7 = {"near_150m": [], "far_1km": []}; c6 = []; nearok = []
        for e in self.fr:
            L = self.L(e, amount_fn(e["dist"]))
            for k, (b, r) in e["blocks"].items(): c7[k].append(abs(L[b].mean() - L[r].mean()))
            if e["win"] is not None:
                means = [L[e["win"][k]].mean() for k in ("1", "3", "10", "30")]
                c6.append(float(np.diff(means).min())); nearok.append(L[e["near"]].mean() <= min(means) if len(e["near"]) else True)
        return {"far_pass": int(sum(v >= 4 for v in c7["far_1km"])), "far_n": len(c7["far_1km"]), "far_min": min(c7["far_1km"]) if c7["far_1km"] else None,
                "near_pass": int(sum(v >= 4 for v in c7["near_150m"])), "near_n": len(c7["near_150m"]),
                "c6_ok": int(sum(m > 0 for m in c6)), "c6_n": len(c6), "c6_minstep": min(c6) if c6 else None, "near_dark": int(sum(nearok))}
