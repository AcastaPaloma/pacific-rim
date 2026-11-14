"""M-004@v1 Pixel stability (research/R-002.md §5).

Reprojection (§5.1): each pixel of frame t is back-projected with its depth and the rendered (snapped) pose,
projected into frame t-k, nearest pixel; valid when in bounds and |z_src - z_expected| <= 5 % (sky by direction),
and not animated in either frame. S_px: share of valid pixels whose palette index changed between t and t-k,
k = ceil(1 / speed), mean over t. C_px: S_px on interleaved (dithered, M-003 b) pixels of frame t. F: share of
pixels valid in both t-1 and t-2 with index A -> B -> A. S0 (static): plain frame difference.
Poses: world = origin_offset_m + local (float64; differences formed offset-first to keep precision)."""
from __future__ import annotations

import math
import os

import numpy as np

from ..colour import index_colour_mismatch, nearest_palette
from ..common import ContractError, InvalidInput, S_NA, S_NOT_RUN, rel_path
from ..contracts.palette_set import load_palette_set
from ..contracts.style_capture import decode_depth, load_image
from ..gait.segment import quat_to_mat
from ..report import part
from . import measures as M
from .m003 import load_sets

GROUP_IDS = {"S0": ("M-004.S0", "M-004.C_px.S0", "M-004.F.S0")}
for _g in ("Y", "T12", "T3"):
    GROUP_IDS[_g] = (f"M-004.S_px.{_g}", f"M-004.C_px.{_g}", f"M-004.F.{_g}")


class Pose:
    def __init__(self, cam, W, H):
        sn = cam["snap"]
        loc = sn["snapped_position_local_m"] if sn.get("enabled") else cam["position_local_m"]
        self.off = np.asarray(cam["origin_offset_m"], np.float64)
        self.loc = np.asarray(loc, np.float64)
        self.R = quat_to_mat(np.asarray(cam["rotation_xyzw"], np.float64))
        self.th = math.tan(math.radians(cam["hfov_deg"]) / 2)
        self.tv = math.tan(math.radians(cam["vfov_deg"]) / 2)
        self.W, self.H = W, H
        xs = (2 * (np.arange(W) + 0.5) / W - 1) * self.th
        ys = (1 - 2 * (np.arange(H) + 0.5) / H) * self.tv
        X, Y = np.meshgrid(xs, ys)
        self.ray_cam = np.stack([X, Y, np.ones_like(X)], -1)      # camera-space ray with z = 1

    def delta(self, other):
        """World position of self minus other, offset-first (float64 precision at large offsets)."""
        return (self.off - other.off) + (self.loc - other.loc)


def reproject(dst_pose, dst_z, src_pose, src_z, occl, sky_lim):
    """For every dst pixel: (row, col) of the source pixel and validity."""
    H, W = dst_z.shape
    sky = ~np.isfinite(dst_z) | (dst_z >= sky_lim)
    zc = np.where(sky, 1.0, dst_z)
    pc = dst_pose.ray_cam * zc[..., None]
    pw = pc @ dst_pose.R.T                                       # rotated, relative to the dst camera
    dirw = dst_pose.ray_cam @ dst_pose.R.T
    rel = pw + dst_pose.delta(src_pose)                          # relative to the src camera
    v = np.where(sky[..., None], dirw, rel)
    q = v @ src_pose.R                                           # = R_src^T v
    qz = q[..., 2]
    with np.errstate(divide="ignore", invalid="ignore"):
        u = (q[..., 0] / (qz * src_pose.th) + 1) / 2 * W
        w = (1 - q[..., 1] / (qz * src_pose.tv)) / 2 * H
    ok = (qz > 0) & np.isfinite(u) & np.isfinite(w)
    iu = np.floor(np.where(ok, u, -1)).astype(np.int64)
    iv = np.floor(np.where(ok, w, -1)).astype(np.int64)
    ok &= (iu >= 0) & (iu < W) & (iv >= 0) & (iv < H)
    iu = np.clip(iu, 0, W - 1); iv = np.clip(iv, 0, H - 1)
    zs = src_z[iv, iu]
    src_sky = ~np.isfinite(zs) | (zs >= sky_lim)
    occ_ok = np.where(sky, src_sky, (~src_sky) & (np.abs(zs - qz) <= occl * qz))
    return iv, iu, ok & occ_ok


class FrameCache:
    def __init__(self, seq, prof, interleave_p, tol):
        self.seq, self.prof, self.ip, self.tol = seq, prof, interleave_p, tol
        self.c = {}

    def get(self, t):
        if t not in self.c:
            fr = self.seq.frames[t]
            pi = load_image(fr, "palette_index")
            ci = load_image(fr, "color_internal")
            if pi is None:
                _, pi = nearest_palette(ci, self.prof.rgb)
            elif index_colour_mismatch(ci, self.prof.rgb, pi, self.tol):
                raise InvalidInput([ContractError(rel_path(fr.sidecar_path), "files.palette_index",
                                                  "palette_index and color_internal disagree (DEF-T002-arch-3)",
                                                  "consistent index and colour", "mismatching pixels")])
            idx = pi.astype(np.int32)
            cam = fr.meta["camera"]
            z = decode_depth(load_image(fr, "depth"), cam["near_m"], cam["far_m"])
            anim = load_image(fr, "mask_animated") > 0
            Lp = self.prof.lab[:, 0]
            L = Lp[np.clip(idx, 0, len(Lp) - 1)]
            inter, _ = M.interleaved(idx, L, *self.ip)
            H, W = idx.shape
            self.c[t] = (idx, z, anim, Pose(cam, W, H), inter, cam)
            for k in [k for k in self.c if k < t - 12]:
                del self.c[k]
        return self.c[t]


def measured_speed(seq, kind, ref_depth):
    f0, f1 = seq.frames[0].meta, seq.frames[-1].meta
    n = len(seq.frames) - 1
    W = f0["resolution"]["internal_wh"][0]
    th = math.tan(math.radians(f0["camera"]["hfov_deg"]) / 2)
    if n <= 0:
        return 0.0, 0.0
    d = (np.asarray(f1["camera"]["origin_offset_m"]) - np.asarray(f0["camera"]["origin_offset_m"])) + \
        (np.asarray(f1["camera"]["position_local_m"]) - np.asarray(f0["camera"]["position_local_m"]))
    trans_px = float(np.linalg.norm(d)) / n / (2 * ref_depth * th / W)
    dyaw = (f1["camera"]["yaw_pitch_roll_deg"][0] - f0["camera"]["yaw_pitch_roll_deg"][0] + 180) % 360 - 180
    yaw_px = abs(dyaw) / n / math.degrees(math.atan(2 * th / W))
    return trans_px, yaw_px


def classify(seq, reg, E):
    mo = seq.motion
    typ, sp = mo["type"], float(mo["speed_internal_px_per_frame"])
    if typ in reg.get("no_verdict_types"):
        return None
    tol = reg.p("class_speed_tol")
    best = None
    for name, c in reg.get("classes").items():
        if c["type"] != typ:
            continue
        nom = c["speed"]
        ok = (sp == 0) if nom == 0 else abs(sp - nom) <= tol * nom
        if ok:
            best = name
    if best is None:
        E.append(ContractError(rel_path(os.path.join(seq.dir, "sequence.json")), "motion.speed_internal_px_per_frame",
                               "speed matches no M-004 class within +-10 %",
                               ", ".join(f"{k} {v['type']} {v['speed']}" for k, v in reg.get("classes").items()),
                               f"{typ} {sp}"))
    return best


def sequence_metrics(seq, prof, reg, cls):
    ip = (reg.p("window_px"), reg.p("top_colour_max"), reg.p("distinct_max"), reg.p("neighbour_diff_min"),
          reg.p("amp_min"))
    fc = FrameCache(seq, prof, ip, reg.p("index_colour_tol_de00", 2.0))
    n = len(seq.frames)
    occl = reg.p("occlusion_rel")
    sp = float(seq.motion["speed_internal_px_per_frame"])
    static = cls == "S0"
    lag = 1 if static or sp <= 0 else max(1, math.ceil(1.0 / sp - 1e-9))
    S, C, F, V = [], [], [], []
    n_inter = n_valid = 0
    change_sum = None
    blank = False
    for t in range(n):
        idx, z, anim, pose, inter, cam = fc.get(t)
        sky_lim = reg.p("sky_far_fraction") * cam["far_m"]
        if len(np.unique(idx[~anim])) <= 1:
            blank = True
        if change_sum is None:
            change_sum = np.zeros(idx.shape)
        if static:
            if t >= 1:
                i1, _, a1, _, _, _ = fc.get(t - 1)
                v = ~anim & ~a1
                ch = (idx != i1) & v
                S.append(ch.sum() / max(v.sum(), 1)); V.append(v.mean())
                vi = v & inter
                n_inter += int(vi.sum()); n_valid += int(v.sum())
                C.append(((idx != i1) & vi).sum() / max(vi.sum(), 1))
                change_sum += ch
            if t >= 2:
                i1, _, a1, _, _, _ = fc.get(t - 1)
                i2, _, a2, _, _, _ = fc.get(t - 2)
                v = ~anim & ~a1 & ~a2
                F.append(((idx == i2) & (idx != i1) & v).sum() / max(v.sum(), 1))
            continue
        if t >= lag:
            si, sz, sa, sp_, _, _ = fc.get(t - lag)
            r, c, ok = reproject(pose, z, sp_, sz, occl, sky_lim)
            ok &= ~anim & ~sa[r, c]
            ch = (idx != si[r, c]) & ok
            S.append(ch.sum() / max(ok.sum(), 1)); V.append(ok.mean())
            vi = ok & inter
            n_inter += int(vi.sum()); n_valid += int(ok.sum())
            C.append(((idx != si[r, c]) & vi).sum() / max(vi.sum(), 1))
            change_sum += ch
        if t >= 2:
            i1, z1, a1, p1, _, _ = fc.get(t - 1)
            i2, z2, a2, p2, _, _ = fc.get(t - 2)
            r1, c1, o1 = reproject(pose, z, p1, z1, occl, sky_lim)
            r2, c2, o2 = reproject(pose, z, p2, z2, occl, sky_lim)
            both = o1 & o2 & ~anim & ~a1[r1, c1] & ~a2[r2, c2]
            F.append(((i2[r2, c2] == idx) & (i1[r1, c1] != idx) & both).sum() / max(both.sum(), 1))
    res = {"sequence": seq.id, "class": cls, "lag": lag, "frames": n,
           "S": float(np.mean(S)) if S else float("nan"),
           "C": ((float(np.mean(C)) if C else float("nan"))
                 if n_inter >= reg.p("crawl_min_interleaved_share", 0.01) * max(n_valid, 1) and n_inter else None),
           "interleaved_px": n_inter, "interleaved_share": n_inter / max(n_valid, 1),
           "F": float(np.mean(F)) if F else float("nan"), "valid_share": float(np.mean(V)) if V else 0.0,
           "S_pair_p95": float(np.percentile(S, 95)) if S else float("nan"),
           "S_pair_max": float(np.max(S)) if S else float("nan"), "blank": blank,
           "change_map": change_sum / max(len(S), 1) if change_sum is not None else None}
    return res


def _seq_job(a):
    seq, prof, reg_path, cls = a
    from ..registry import Registry
    return sequence_metrics(seq, prof, Registry(reg_path), cls)


def run(report, inputs, reg, ratings=None, out_dir=None, images=True, options=None):
    options = options or {}
    pal = load_palette_set(options.get("palettes"))
    report.add_input(pal.path, pal.sha256, "palette_set@v1")
    sets = load_sets(inputs, pal)
    for cs in sets:
        for p, h in cs.input_files:
            report.add_input(p, h, "style_capture@v1")
    seqs = [sq for cs in sets for sq in cs.sequences if sq.purpose == "M-004"]
    E = []
    fixture = bool(options.get("fixture"))
    min_frames = reg.p("min_frames_fixture") if fixture else reg.p("min_frames")
    jobs, nov, short = [], [], []
    for sq in seqs:
        cls = classify(sq, reg, E)
        if cls is None:
            if sq.motion["type"] in reg.get("no_verdict_types"):
                nov.append(sq)
            continue
        tr, yw = measured_speed(sq, sq.motion["type"], reg.p("ref_depth_m"))
        decl = float(sq.motion["speed_internal_px_per_frame"])
        meas = tr if sq.motion["type"] == "translate" else yw if sq.motion["type"] == "yaw" else max(tr, yw)
        where = rel_path(os.path.join(sq.dir, "sequence.json"))
        if cls == "S0":
            if max(tr, yw) > reg.p("static_motion_max"):
                E.append(ContractError(where, "camera poses", "static sequence but the camera moves",
                                       f"<= {reg.p('static_motion_max')} px/frame", f"{max(tr, yw):.4f} px/frame"))
        elif abs(meas - decl) > reg.p("speed_check_tol") * decl:
            E.append(ContractError(where, "motion.speed_internal_px_per_frame",
                                   "declared speed differs from the camera motion measured from the sidecar poses "
                                   "by more than 10 %", f"{decl}", f"{meas:.4f} px/frame"))
        need = reg.p("static_min_frames") if cls == "S0" else min_frames
        if len(sq.frames) < need:
            short.append(f"{sq.id}: {len(sq.frames)} frames (< {need})")
            continue
        if cls != "S0" and decl * (len(sq.frames) - 1) >= 1.0:
            key = "palette_index" if sq.frames[0].paths.get("palette_index") else "color_internal"
            a = load_image(sq.frames[0], key)
            if all(np.array_equal(a, load_image(f, key)) for f in sq.frames[1:]):
                E.append(ContractError(where, "frames", "frozen capture: every frame identical to frame 0 although "
                                       "the camera moves >= 1 px", "changing frames", "identical"))
        jobs.append((sq, pal.profiles[sq.profile_id], reg.path, cls))
    if E:
        raise InvalidInput(E)
    nj = int(options.get("jobs") or 1)
    if nj > 1 and len(jobs) > 1:
        import multiprocessing as mp
        with mp.get_context("spawn").Pool(min(nj, len(jobs))) as pool:
            results = pool.map(_seq_job, jobs)
    else:
        results = [sequence_metrics(sq, prof, reg, cls) for sq, prof, _, cls in jobs]
    for (sq, _, _, _), r in zip(jobs, results):
        r["profile"], r["viewpoint"] = sq.profile_id, sq.viewpoint
        if r["blank"]:
            raise InvalidInput([ContractError(rel_path(os.path.join(sq.dir, "sequence.json")), "frames",
                                              "blank frame (a single palette index over all non-animated pixels)",
                                              "rendered content", "blank")])
    # coverage
    req_p = options.get("profiles").split(",") if options.get("profiles") else None
    req_v = options.get("viewpoints").split(",") if options.get("viewpoints") else None
    groups = {}
    for r in results:
        groups.setdefault((r["profile"], r["viewpoint"]), set()).add(r["class"])
    all_cls = set(reg.get("classes"))
    complete = {k for k, v in groups.items() if v >= all_cls}
    prof_ok = {}
    for (p, v) in complete:
        prof_ok.setdefault(p, set()).add(v)
    missing = list(short)
    if req_p:
        for p in req_p:
            have = prof_ok.get(p, set())
            need_v = req_v or []
            if req_v:
                missing += [f"{p}/{v}: incomplete class set" for v in req_v if v not in have]
            elif len(have) < reg.get("required_viewpoints"):
                missing.append(f"{p}: {len(have)} complete viewpoints (< {reg.get('required_viewpoints')})")
    else:
        good = [p for p, vs in prof_ok.items() if (all(v in vs for v in req_v) if req_v else
                                                   len(vs) >= reg.get("required_viewpoints"))]
        if len(good) < reg.get("required_profiles"):
            missing.append(f"{len(good)} profiles with complete S0/T1/T2/T3/Y1/Y2 sets on the required viewpoints "
                           f"(< {reg.get('required_profiles')})")
    report.coverage = {"required": [f"{reg.get('required_profiles')} profiles x {reg.get('required_viewpoints')} "
                                    "viewpoints x S0,T1,T2,T3,Y1,Y2" if not req_p else
                                    f"{','.join(req_p)} x {','.join(req_v) if req_v else 'any viewpoints'}"],
                       "provided": sorted(f"{p}/{v}: {','.join(sorted(c))}" for (p, v), c in groups.items()),
                       "missing": missing, "optional_not_provided": []}
    # sub-measures per threshold group, worst sequence
    tg = reg.get("threshold_group")
    vmin = reg.p("valid_min")
    for grp, ids in GROUP_IDS.items():
        rs = [r for r in results if tg[r["class"]] == grp]
        for sid, key in zip(ids, ("S", "C", "F")):
            if not rs:
                report.add(sid, S_NOT_RUN, note=f"no {grp} sequence")
                continue
            ps = []
            for r in rs:
                nm = f"{r['sequence']} ({r['class']}, {r['profile']}/{r['viewpoint']}, lag {r['lag']})"
                if r["valid_share"] < vmin:
                    ps.append({"name": nm, "value": None, "status": S_NOT_RUN, "op": "<=", "threshold": None,
                               "margin": None, "margin_rel": None, "unit": "share",
                               "note": f"valid-pixel share {r['valid_share']:.3f} < {vmin}"})
                    continue
                if key == "C" and r["C"] is None:
                    ps.append({"name": nm, "value": None, "status": S_NA, "op": "<=", "threshold": reg.t(sid, "max"),
                               "margin": None, "margin_rel": None, "unit": "share",
                               "note": f"dithered (interleaved) pixels {r['interleaved_share']:.4f} of valid pixels "
                                       "(< crawl_min_interleaved_share): crawl not measurable"})
                    report.warn(f"{sid}: {r['sequence']} has no dithered pixels to measure crawl on "
                                f"({r['interleaved_px']} px); C_px NOT_APPLICABLE for it")
                    continue
                ps.append(part(nm, r[key], "<=", reg.t(sid, "max"), unit="share", valid_share=r["valid_share"],
                               pair_p95=r["S_pair_p95"] if key == "S" else None))
            worst = max((p for p in ps if p["value"] is not None), key=lambda p: p["value"], default=None)
            report.add(sid, parts=ps, diagnostics={"where": worst["name"] if worst else None})
    report.extra["sequences"] = [{k: v for k, v in r.items() if k != "change_map"} for r in results]
    report.extra["no_verdict_sequences"] = [{"sequence": sq.id, "motion": sq.motion["type"],
                                             "note": "validated and reported without a verdict"} for sq in nov]
    if fixture:
        report.warn("fixture mode: 60-frame sequences accepted; not a gating run")
    if images and out_dir and results:
        from ..diagnostics import heat_map
        w = max(results, key=lambda r: (r["S"] if np.isfinite(r["S"]) else -1))
        if w["change_map"] is not None:
            name = heat_map(out_dir, "diag_M-004_shimmer_map.png", w["change_map"], 1.0)
            report.images.append(name)
            report.extra["shimmer_map"] = {"image": name, "sequence": w["sequence"]}
