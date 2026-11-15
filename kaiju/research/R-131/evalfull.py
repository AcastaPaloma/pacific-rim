"""Full-frame evaluation of a candidate (per profile): C7 blocks (all albedo scenarios), C6 layers, M-003 c1 (15 stills sets), c2 (haze_cal), C7 scale marker."""
from evalc import *

FORMS = (0.266, 0.272, 0.279); GROUNDS = (0.2275, 0.240, 0.2525)

def vote(intervals):
    votes = np.zeros(GRID + 1)
    for lo, hi in intervals:
        d = np.zeros(GRID + 1); np.add.at(d, lo, 1); np.add.at(d, hi + 1, -1); votes += np.cumsum(d)
    best = votes[:GRID].max(); sel = np.flatnonzero(votes[:GRID] >= best)
    return float(S0[sel].mean()), best

class Cal:
    """Haze_cal (c2) and scale-detail frames of one profile, with panel/marker albedos inferred by voting across distances."""
    def __init__(self, pid):
        self.pid = pid
        self.hz = Set(pid, "haze_cal", d=os.path.join(CAP, "haze_cal", pid)); fd = self.hz.fds[0]
        self.targets = fd.f["haze_targets"]
        iv = {"dark": [], "light": []}
        for t in self.targets:
            for k in ("dark", "light"):
                x0, y0, x1, y1 = t[k + "_px"]; m = np.zeros(fd.z.shape, bool); m[y0:y1, x0:x1] = True; m &= fd.neutral
                iv[k].append((fd.lo[m], fd.hi[m]))
        self.s_dark, _ = vote([(l, h) for a, b in iv["dark"] for l, h in zip(a, b)]); self.s_light, _ = vote([(l, h) for a, b in iv["light"] for l, h in zip(a, b)])
        self.sc = Set(pid, "scale", d=os.path.join(CAP, "m003_details", pid + "_detail_scale"))
    def c2(self, amount_fn):
        fd = self.hz.fds[0]; P = self.hz.P
        s0 = np.zeros(fd.z.shape)
        L = fd.L.copy(); s_tab, r_tab, L_tab = rung_table(P)
        for t in self.targets:
            for k, sv in (("dark", self.s_dark), ("light", self.s_light)):
                x0, y0, x1, y1 = t[k + "_px"]
                a = amount_fn(fd.dist[y0:y1, x0:x1]); s = sv + (P.t["airlightValue"] - sv) * a
                L[y0:y1, x0:x1] = L_tab[np.minimum(np.round(np.clip(s, 0, 1) * (len(s_tab) - 1)).astype(int), len(s_tab) - 1)]
        rows = M.haze_pairs(L, self.targets)
        dL = [r["dL"] for r in rows]; mL = [r["meanL"] for r in rows]
        rise = max(np.diff(dL)); far = dL[-1] - 0.5 * dL[0]; drop = max(0.0, -min(np.diff(mL)))
        return {"rise": float(rise), "far": float(far), "drop": float(drop), "pass": bool(rise <= 0.5 and far <= 0.0 and drop <= 0.0), "dL": [round(x, 1) for x in dL], "meanL": [round(x, 1) for x in mL]}
    def scale(self, amount_fn, s_ground=S0_GROUND):
        S = self.sc; fd = S.fds[0]; f = fd.f; L = render_set(S, fd, amount_fn, "mid", s_ground=s_ground)
        z, sky = fd.z, fd.sky
        mx, my, mz = pc.project_rendered(f, (-60.0, 0.9, 447.0)); x0, y0 = int(round(mx)) - 6, int(round(my)) - 6
        win_z = z[y0:y0 + 12, x0:x0 + 12]; win_L = L[y0:y0 + 12, x0:x0 + 12]
        marker = (np.abs(win_z - mz) < 0.02 * mz) & (np.abs(win_L - np.median(win_L)) > 3)
        if not marker.any(): return {"pass": False, "note": "marker not found"}
        dl = abs(win_L[marker].mean() - np.median(win_L[~marker]))
        return {"marker_dL": float(dl), "pass": bool(dl >= 10)}

def c1_sets(pid):
    return [Set(pid, vp) for vp in VPS]

def full(pid, amount_fn, sets=None, cal=None, scenarios=True):
    sets = sets or c1_sets(pid); cal = cal or Cal(pid); S = sets[0]
    res = {"c7": {}, "c6": c6_layers(S, amount_fn)}
    worst_far = 99; worst_near = 99; scen = []
    forms = FORMS if scenarios else (S0_FORM,); grounds = GROUNDS if scenarios else (S0_GROUND,)
    for sf in forms:
        for sg in grounds:
            r = c7_blocks(S, amount_fn, "mid", s_form=sf, s_ground=sg); scen.append((r["far_1km"]["pass"], r["near_150m"]["pass"]))
    res["c7"] = {"far_pass_min_over_albedo": min(a for a, b in scen), "far_pass_max": max(a for a, b in scen), "near_pass_min": min(b for a, b in scen), "far_n": r["far_1km"]["in_view"], "far_min": r["far_1km"]["min"]}
    res["c1"] = c1_all(sets, amount_fn); res["c2"] = cal.c2(amount_fn); res["scale"] = cal.scale(amount_fn)
    return res
