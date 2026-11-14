"""style_capture@v1 (research/R-002.md §6): validation and loading of capture sets.

Layout: <capture_set_dir>/<sequence_id>/sequence.json + f<index:06d>.json (sidecar) + the images each sidecar names.
The validator accepts a capture-set directory, a sequence directory, or one sidecar file, and lists all errors.

Tool-side extensions (recorded in SPEC_READINGS.md, proposed for style_capture@v1.1):
- sequence.json `viewpoint` (optional string) groups M-004 sequences by viewpoint (default "default").
- haze_cal frame sidecars carry `haze_targets`: [{distance_m, dark_px: [x0,y0,x1,y1], light_px: [...]}] in
  internal-resolution pixels (end-exclusive), so M-003 c2 knows where the dark/light pairs are.
- sequence.json `frames` entries may be {"sidecar": name, "sha256": hex}; listed hashes are verified.
"""
from __future__ import annotations

import math
import os
import re
from dataclasses import dataclass, field

import numpy as np

from ..common import ErrorList, InvalidInput, load_json_file, rel_path, sha256_file
from ..png import png_header, read_png

SCHEMA = "style_capture@v1"
PURPOSES = ("M-003", "M-004", "haze_cal")
MOTION_TYPES = ("static", "translate", "yaw", "pitch", "dolly", "scripted")
DITHER_ANCHORS = ("screen", "direction", "surface")
FILE_KEYS = ("color_internal", "color_output", "palette_index", "depth", "mask_animated", "mask_ui", "motion_vectors")
REQUIRED_FILES = ("color_internal", "color_output", "depth", "mask_animated")
# (resolution, colour type, bit depth)
FILE_FORMAT = {"color_internal": ("internal", "rgb", 8), "color_output": ("output", "rgb", 8),
               "palette_index": ("internal", "grey", 8), "depth": ("internal", "grey", 16),
               "mask_animated": ("internal", "grey", 8), "mask_ui": ("output", "grey", 8)}
FORBIDDEN_FLAG = re.compile(r"(taa|temporal.?anti|motion.?blur|bloom)", re.I)
SIDECAR_RE = re.compile(r"^f(\d{6})\.json$")


def _num(x):
    return isinstance(x, (int, float)) and not isinstance(x, bool) and math.isfinite(x)


def _nvec(x, n):
    return isinstance(x, list) and len(x) == n and all(_num(v) for v in x)


def _ivec(x, n):
    return isinstance(x, list) and len(x) == n and all(isinstance(v, int) and not isinstance(v, bool) for v in x)


@dataclass
class Frame:
    index: int
    sidecar_path: str
    meta: dict
    paths: dict                      # file key -> absolute path or None

    @property
    def profile_id(self):
        return self.meta["style"]["palette_profile_id"]


@dataclass
class Sequence:
    id: str
    dir: str
    meta: dict
    frames: list = field(default_factory=list)

    @property
    def purpose(self):
        return self.meta["purpose"]

    @property
    def motion(self):
        return self.meta["motion"]

    @property
    def viewpoint(self):
        return self.meta.get("viewpoint", "default")

    @property
    def profile_id(self):
        return self.frames[0].profile_id if self.frames else None


@dataclass
class CaptureSet:
    path: str
    id: str
    sequences: list
    input_files: list                # (path, sha256) of every file read


def _check_sidecar(sc, E: ErrorList, w, seq_meta, palettes, idx_expected):
    if not isinstance(sc, dict):
        E.add(w, "sidecar must be a JSON object", "object", type(sc).__name__)
        return False
    ok = True
    if sc.get("schema") != SCHEMA:
        E.add(f"{w}.schema", "wrong schema string", SCHEMA, repr(sc.get("schema")))
        ok = False
    for k in ("capture_set_id", "sequence_id"):
        if seq_meta is not None and sc.get(k) != seq_meta.get(k):
            E.add(f"{w}.{k}", f"{k} differs from sequence.json", seq_meta.get(k), repr(sc.get(k)))
    for sec in ("frame", "build", "style", "resolution", "camera", "conventions", "motion", "files", "encodings"):
        if not isinstance(sc.get(sec), dict):
            E.add(f"{w}.{sec}", "missing section", "object", repr(sc.get(sec))[:40])
            ok = False
    if not ok:
        return False
    fr = sc["frame"]
    if not isinstance(fr.get("index"), int) or isinstance(fr.get("index"), bool):
        E.add(f"{w}.frame.index", "index must be an integer", "int", repr(fr.get("index")))
    elif idx_expected is not None and fr["index"] != idx_expected:
        E.add(f"{w}.frame.index", "frame index differs from the sidecar file name", idx_expected, fr["index"])
    if seq_meta is not None and fr.get("sequence_length") != seq_meta.get("frame_count"):
        E.add(f"{w}.frame.sequence_length", "differs from sequence.json frame_count", seq_meta.get("frame_count"),
              repr(fr.get("sequence_length")))
    if not _num(fr.get("dt_s")) or fr.get("dt_s", 0) <= 0:
        E.add(f"{w}.frame.dt_s", "dt_s must be > 0", "> 0", repr(fr.get("dt_s")))
    if not _num(fr.get("sim_time_s")):
        E.add(f"{w}.frame.sim_time_s", "missing sim_time_s", "number", repr(fr.get("sim_time_s")))
    if not isinstance(fr.get("sim_paused"), bool):
        E.add(f"{w}.frame.sim_paused", "sim_paused must be a boolean", "bool", repr(fr.get("sim_paused")))
    b = sc["build"]
    for k in ("git_commit", "unity_version", "render_pipeline"):
        if not isinstance(b.get(k), str) or not b.get(k):
            E.add(f"{w}.build.{k}", "missing", "string", repr(b.get(k)))
    ff = b.get("feature_flags")
    if not isinstance(ff, dict):
        E.add(f"{w}.build.feature_flags", "missing feature_flags", "object", repr(ff))
    else:
        for k, v in ff.items():
            if FORBIDDEN_FLAG.search(k) and v not in (False, None, 0, "off", "none", "false"):
                E.add(f"{w}.build.feature_flags.{k}", "TAA, motion blur and bloom are forbidden on color_internal "
                      "(style_capture rule 2)", "false/off", repr(v))
    st = sc["style"]
    if st.get("palette_set") != "palette_set@v1":
        E.add(f"{w}.style.palette_set", "unsupported palette set", "palette_set@v1", repr(st.get("palette_set")))
    prof = None
    pid = st.get("palette_profile_id")
    if palettes is not None:
        if pid not in palettes.profiles:
            E.add(f"{w}.style.palette_profile_id", "unknown profile", sorted(palettes.profiles), repr(pid))
        else:
            prof = palettes.profiles[pid]
            if st.get("palette_sha256") != prof.sha256:
                E.add(f"{w}.style.palette_sha256", "does not match the canonical hash of the declared profile",
                      prof.sha256, repr(st.get("palette_sha256")))
    for k in ("district_id", "time_of_day"):
        if not isinstance(st.get(k), str):
            E.add(f"{w}.style.{k}", "missing", "string", repr(st.get(k)))
    rs = sc["resolution"]
    good_res = True
    for k in ("internal_wh", "output_wh", "output_block_offset_px"):
        if not _ivec(rs.get(k), 2):
            E.add(f"{w}.resolution.{k}", "must be two integers", "[a, b]", repr(rs.get(k)))
            good_res = False
    s = rs.get("integer_scale")
    if not isinstance(s, int) or isinstance(s, bool) or s < 1:
        E.add(f"{w}.resolution.integer_scale", "must be an integer >= 1", ">= 1", repr(s))
        good_res = False
    if not _nvec(rs.get("subpixel_offset_internal_px"), 2):
        E.add(f"{w}.resolution.subpixel_offset_internal_px", "must be two numbers", "[dx, dy]",
              repr(rs.get("subpixel_offset_internal_px")))
    if good_res:
        iw, ih = rs["internal_wh"]
        if iw <= 0 or ih <= 0:
            E.add(f"{w}.resolution.internal_wh", "must be positive", "> 0", rs["internal_wh"])
            good_res = False
        elif rs["output_wh"] != [iw * s, ih * s]:
            E.add(f"{w}.resolution.output_wh", "output must be internal x integer_scale", [iw * s, ih * s],
                  rs["output_wh"])
        ox, oy = rs["output_block_offset_px"]
        if not (0 <= ox < s and 0 <= oy < s):
            E.add(f"{w}.resolution.output_block_offset_px", "offset must lie in [0, integer_scale)", f"[0, {s})",
                  rs["output_block_offset_px"])
    cam = sc["camera"]
    if cam.get("projection") != "perspective":
        E.add(f"{w}.camera.projection", "only perspective cameras are supported", "perspective",
              repr(cam.get("projection")))
    for k in ("hfov_deg", "vfov_deg", "near_m", "far_m"):
        if not _num(cam.get(k)) or cam.get(k) <= 0:
            E.add(f"{w}.camera.{k}", "must be a number > 0", "> 0", repr(cam.get(k)))
    if _num(cam.get("near_m")) and _num(cam.get("far_m")) and cam["far_m"] <= cam["near_m"]:
        E.add(f"{w}.camera.far_m", "far must exceed near", f"> {cam['near_m']}", cam["far_m"])
    if good_res and _num(cam.get("hfov_deg")) and _num(cam.get("vfov_deg")) and 0 < cam["hfov_deg"] < 180:
        iw, ih = rs["internal_wh"]
        exp = math.degrees(2 * math.atan(math.tan(math.radians(cam["hfov_deg"]) / 2) * ih / iw))
        if abs(exp - cam["vfov_deg"]) > 0.01 * exp:
            E.add(f"{w}.camera.vfov_deg", "vfov inconsistent with hfov and the aspect ratio", round(exp, 3),
                  cam["vfov_deg"])
    for k in ("origin_offset_m", "position_local_m", "yaw_pitch_roll_deg"):
        if not _nvec(cam.get(k), 3):
            E.add(f"{w}.camera.{k}", "must be three numbers", "[x, y, z]", repr(cam.get(k)))
    q = cam.get("rotation_xyzw")
    if not _nvec(q, 4):
        E.add(f"{w}.camera.rotation_xyzw", "must be a quaternion [x, y, z, w]", "4 numbers", repr(q))
    elif abs(math.sqrt(sum(v * v for v in q)) - 1) > 1e-3:
        E.add(f"{w}.camera.rotation_xyzw", "quaternion not unit length", "|q| = 1 +- 1e-3",
              round(math.sqrt(sum(v * v for v in q)), 6))
    sn = cam.get("snap")
    if not isinstance(sn, dict):
        E.add(f"{w}.camera.snap", "missing snap block", "object", repr(sn))
    else:
        if not isinstance(sn.get("enabled"), bool):
            E.add(f"{w}.camera.snap.enabled", "must be a boolean", "bool", repr(sn.get("enabled")))
        for k in ("snapped_position_local_m", "snapped_yaw_pitch_roll_deg"):
            if not _nvec(sn.get(k), 3):
                E.add(f"{w}.camera.snap.{k}", "must be three numbers", "[x, y, z]", repr(sn.get(k)))
        if sn.get("dither_anchor") not in DITHER_ANCHORS:
            E.add(f"{w}.camera.snap.dither_anchor", "unknown dither anchor", DITHER_ANCHORS,
                  repr(sn.get("dither_anchor")))
    cv = sc["conventions"]
    for k in ("handedness", "up", "forward", "pixel_origin"):
        if not isinstance(cv.get(k), str):
            E.add(f"{w}.conventions.{k}", "missing", "string", repr(cv.get(k)))
    if isinstance(cv.get("handedness"), str) and not cv["handedness"].lower().startswith("left"):
        E.add(f"{w}.conventions.handedness", "Unity world is left-handed", "left (Unity)", cv["handedness"])
    mo = sc["motion"]
    if mo.get("type") not in MOTION_TYPES:
        E.add(f"{w}.motion.type", "unknown motion type", MOTION_TYPES, repr(mo.get("type")))
    if not _num(mo.get("speed_internal_px_per_frame")) or mo.get("speed_internal_px_per_frame", -1) < 0:
        E.add(f"{w}.motion.speed_internal_px_per_frame", "must be a number >= 0", ">= 0",
              repr(mo.get("speed_internal_px_per_frame")))
    if seq_meta is not None and mo != seq_meta.get("motion"):
        E.add(f"{w}.motion", "motion differs from sequence.json motion", "identical", "different")
    fl = sc["files"]
    for k in FILE_KEYS:
        if k not in fl:
            E.add(f"{w}.files.{k}", "key missing (use null when absent)", "file name | null", "absent")
    for k in REQUIRED_FILES:
        if not fl.get(k):
            E.add(f"{w}.files.{k}", "required file missing", "file name", repr(fl.get(k)))
    enc = sc["encodings"]
    dep = enc.get("depth")
    if isinstance(dep, dict) and dep.get("format") not in (None, "png16_log"):
        E.add(f"{w}.encodings.depth.format", "this tool version reads png16_log depth only", "png16_log",
              repr(dep.get("format")))
    if seq_meta is not None and seq_meta.get("purpose") == "haze_cal":
        _check_haze_targets(sc, E, w)
    return True


def _check_haze_targets(sc, E, w):
    tg = sc.get("haze_targets")
    if not isinstance(tg, list) or len(tg) < 2:
        E.add(f"{w}.haze_targets", "haze_cal frames must list the dark/light target pairs",
              "[{distance_m, dark_px, light_px}, ...] (>= 2)", repr(tg)[:60])
        return
    wh = sc.get("resolution", {}).get("internal_wh", [0, 0])
    for i, t in enumerate(tg):
        if not isinstance(t, dict) or not _num(t.get("distance_m")):
            E.add(f"{w}.haze_targets[{i}]", "needs distance_m", "number", repr(t)[:60])
            continue
        for k in ("dark_px", "light_px"):
            b = t.get(k)
            if not _ivec(b, 4) or not (0 <= b[0] < b[2] <= wh[0] and 0 <= b[1] < b[3] <= wh[1]):
                E.add(f"{w}.haze_targets[{i}].{k}", "box must be [x0, y0, x1, y1] inside internal_wh",
                      f"0 <= x0 < x1 <= {wh[0]}, 0 <= y0 < y1 <= {wh[1]}", repr(b))


def _check_files(sc, E, w, d, palettes, inputs):
    rs = sc["resolution"]
    paths = {}
    idx = sc["frame"].get("index")
    for k in FILE_KEYS:
        name = sc["files"].get(k)
        if not name:
            paths[k] = None
            continue
        p = os.path.join(d, name)
        paths[k] = p
        if isinstance(idx, int) and not os.path.basename(name).startswith(f"f{idx:06d}"):
            E.add(f"{w}.files.{k}", "file name must start with the frame index (same frame for all files)",
                  f"f{idx:06d}...", name)
        if not os.path.isfile(p):
            E.add(f"{w}.files.{k}", "file not found", rel_path(p), "missing")
            continue
        if k == "motion_vectors":
            inputs.append((p, sha256_file(p)))
            continue
        if not name.lower().endswith(".png"):
            E.add(f"{w}.files.{k}", "captures must be lossless PNG", ".png", name)
            continue
        try:
            hd = png_header(p)
        except ValueError as e:
            E.add(f"{w}.files.{k}", str(e), "PNG", name)
            continue
        res, ct, bd = FILE_FORMAT[k]
        if hd["colour_type"] != ct or hd["bit_depth"] != bd:
            E.add(f"{w}.files.{k}", "wrong PNG format", f"{bd}-bit {ct}", f"{hd['bit_depth']}-bit {hd['colour_type']}")
        if _ivec(rs.get(f"{res}_wh"), 2) and [hd["width"], hd["height"]] != rs[f"{res}_wh"]:
            E.add(f"{w}.files.{k}", f"resolution differs from resolution.{res}_wh", rs[f"{res}_wh"],
                  [hd["width"], hd["height"]])
        inputs.append((p, sha256_file(p)))
        if k == "palette_index" and palettes is not None and not E.errors_for(w):
            pid = sc["style"].get("palette_profile_id")
            if pid in palettes.profiles:
                try:
                    a = read_png(p)
                except ValueError as e:
                    E.add(f"{w}.files.{k}", str(e), "decodable PNG", name)
                    continue
                n = len(palettes.profiles[pid].hexes)
                bad = (a >= n) & (a != 255)
                if bad.any():
                    E.add(f"{w}.files.palette_index", "index outside the profile (or 255 = not quantised)",
                          f"0..{n - 1} or 255", f"{int(bad.sum())} pixels, max {int(a[bad].max())}")
    return paths


class _E(ErrorList):
    def errors_for(self, prefix):
        return [e for e in self.errors if e.where.startswith(prefix)]


def _load_sequence(d, palettes, E, inputs):
    sp = os.path.join(d, "sequence.json")
    w0 = rel_path(sp)
    if not os.path.isfile(sp):
        E.add(w0, "sequence.json not found", "sequence.json in each sequence directory", "missing")
        return None
    try:
        meta = load_json_file(sp)
    except Exception as e:  # noqa: BLE001
        E.add(w0, "sequence.json is not valid JSON", "JSON object", str(e)[:80])
        return None
    inputs.append((sp, sha256_file(sp)))
    if not isinstance(meta, dict):
        E.add(w0, "sequence.json must be an object", "object", type(meta).__name__)
        return None
    bad = False
    if meta.get("schema") != SCHEMA:
        E.add(f"{w0}.schema", "wrong schema string", SCHEMA, repr(meta.get("schema")))
        bad = True
    for k in ("capture_set_id", "sequence_id"):
        if not isinstance(meta.get(k), str) or not meta.get(k):
            E.add(f"{w0}.{k}", "missing", "string", repr(meta.get(k)))
            bad = True
    if isinstance(meta.get("sequence_id"), str) and meta["sequence_id"] != os.path.basename(os.path.normpath(d)):
        E.add(f"{w0}.sequence_id", "must equal the sequence directory name", os.path.basename(os.path.normpath(d)),
              meta["sequence_id"])
    if meta.get("purpose") not in PURPOSES:
        E.add(f"{w0}.purpose", "unknown purpose", PURPOSES, repr(meta.get("purpose")))
        bad = True
    fc = meta.get("frame_count")
    if not isinstance(fc, int) or isinstance(fc, bool) or fc < 1:
        E.add(f"{w0}.frame_count", "must be an integer >= 1", ">= 1", repr(fc))
        bad = True
    if not isinstance(meta.get("motion"), dict):
        E.add(f"{w0}.motion", "missing motion", "object", repr(meta.get("motion")))
        bad = True
    if not isinstance(meta.get("renderer_settings_hash"), str):
        E.add(f"{w0}.renderer_settings_hash", "missing", "string", repr(meta.get("renderer_settings_hash")))
    fl = meta.get("frames")
    if not isinstance(fl, list):
        E.add(f"{w0}.frames", "missing list of frame sidecars", "list", repr(fl)[:40])
        bad = True
    if bad:
        return None
    seq = Sequence(meta["sequence_id"], d, meta)
    names = []
    for i, it in enumerate(fl):
        nm = it.get("sidecar") if isinstance(it, dict) else it
        if not isinstance(nm, str) or not SIDECAR_RE.match(nm):
            E.add(f"{w0}.frames[{i}]", "sidecar name must be f<index:06d>.json", "f000000.json", repr(nm))
            continue
        names.append((nm, it.get("sha256") if isinstance(it, dict) else None))
    on_disk = sorted(f for f in os.listdir(d) if SIDECAR_RE.match(f))
    if sorted(n for n, _ in names) != on_disk:
        E.add(f"{w0}.frames", "listed sidecars differ from the sidecars on disk", f"{len(names)} listed",
              f"{len(on_disk)} on disk")
    if len(names) != fc:
        E.add(f"{w0}.frame_count", "frame_count differs from the number of listed sidecars", len(names), fc)
    idxs = [int(SIDECAR_RE.match(n).group(1)) for n, _ in names]
    if idxs != list(range(len(idxs))):
        E.add(f"{w0}.frames", "frame indices must run 0..frame_count-1 in order (no gaps)",
              f"0..{len(idxs) - 1}", _gaps(idxs))
    for (nm, sha), idx in zip(names, idxs):
        p = os.path.join(d, nm)
        w = rel_path(p)
        if not os.path.isfile(p):
            E.add(w, "sidecar file not found", nm, "missing")
            continue
        h = sha256_file(p)
        inputs.append((p, h))
        if sha is not None and sha != h:
            E.add(w, "sha256 mismatch with sequence.json", sha, h)
        try:
            sc = load_json_file(p)
        except Exception as e:  # noqa: BLE001
            E.add(w, "sidecar is not valid JSON", "JSON object", str(e)[:80])
            continue
        if not _check_sidecar(sc, E, w, meta, palettes, idx):
            continue
        paths = _check_files(sc, E, w, d, palettes, inputs)
        seq.frames.append(Frame(idx, p, sc, paths))
    if seq.frames:
        p0 = {f.meta["style"].get("palette_profile_id") for f in seq.frames}
        if len(p0) > 1:
            E.add(f"{w0}", "all frames of a sequence must share one palette profile", "one profile", sorted(map(str, p0)))
        r0 = {str(f.meta["resolution"]) for f in seq.frames}
        if len(r0) > 1:
            E.add(f"{w0}", "all frames of a sequence must share one resolution block", "identical", f"{len(r0)} variants")
    return seq


def _gaps(idxs):
    s = set(idxs)
    miss = [i for i in range(max(idxs) + 1) if i not in s] if idxs else []
    return f"missing {miss[:5]}" if miss else f"order {idxs[:5]}..."


def load_capture_set(path, palettes, collect_only=False):
    """Load a capture set directory (or one sequence directory). Raises InvalidInput with every error."""
    E = _E("")
    inputs = []
    seqs = []
    if os.path.isfile(path) and path.endswith(".json") and os.path.basename(path) != "sequence.json":
        d = os.path.dirname(os.path.abspath(path))
        s = _load_sequence(d, palettes, E, inputs)
        if s:
            s.frames = [f for f in s.frames if os.path.abspath(f.sidecar_path) == os.path.abspath(path)]
            seqs.append(s)
        set_id = s.meta.get("capture_set_id") if s else None
    elif os.path.isdir(path) and os.path.isfile(os.path.join(path, "sequence.json")):
        s = _load_sequence(path, palettes, E, inputs)
        seqs = [s] if s else []
        set_id = s.meta.get("capture_set_id") if s else None
    elif os.path.isdir(path):
        subs = sorted(x for x in os.listdir(path) if os.path.isdir(os.path.join(path, x)))
        if not subs:
            E.add(rel_path(path), "capture set holds no sequence directories", "<set>/<sequence_id>/sequence.json", "none")
        for x in subs:
            s = _load_sequence(os.path.join(path, x), palettes, E, inputs)
            if s:
                seqs.append(s)
        ids = {s.meta.get("capture_set_id") for s in seqs}
        if len(ids) > 1:
            E.add(rel_path(path), "sequences disagree on capture_set_id", "one id", sorted(map(str, ids)))
        set_id = os.path.basename(os.path.normpath(path))
        if seqs and seqs[0].meta.get("capture_set_id") != set_id:
            E.add(rel_path(path), "capture_set_id must equal the capture-set directory name", set_id,
                  seqs[0].meta.get("capture_set_id"))
    else:
        E.add(rel_path(path), "not found or not a capture set / sequence / sidecar", "directory", path)
        set_id = None
    if E:
        if collect_only:
            return None, E.errors
        raise InvalidInput(E.errors)
    cs = CaptureSet(path, set_id, seqs, inputs)
    return (cs, []) if collect_only else cs


def validate_path(path, palettes):
    _, errs = load_capture_set(path, palettes, collect_only=True)
    return errs


# ---------------------------------------------------------------- image access helpers (used by M-003 / M-004)
def decode_depth(v16, near, far):
    """PNG16-log decode: z = near * (far/near) ** (v / 65535); v = 65535 is sky (returned as +inf)."""
    v = v16.astype(np.float64)
    z = near * (far / near) ** (v / 65535.0)
    return np.where(v16 == 65535, np.inf, z)


def load_image(frame: Frame, key):
    p = frame.paths.get(key)
    return None if p is None else read_png(p)
