"""Rebuild the T-002 evidence in evidence/T-002/ (reports, transcripts, timing, determinism, exit codes, images).

Usage (from the project root): /opt/miniconda3/envs/kaiju/bin/python Tools/Metrics/fixtures/make_evidence.py [--skip-timing]
Fixture data is generated in a scratch directory (not committed); reports, summaries and transcripts are written to
evidence/T-002/. Every command is logged with its exit code in evidence/T-002/commands.log."""
from __future__ import annotations

import json
import math
import os
import shutil
import subprocess
import sys
import tempfile
import time

HERE = os.path.dirname(os.path.abspath(__file__))
METRICS = os.path.dirname(HERE)
PROJECT = os.path.dirname(os.path.dirname(METRICS))
sys.path.insert(0, METRICS)
EV = os.path.join(PROJECT, "evidence", "T-002")
PY = sys.executable
CLI = os.path.join("Tools", "Metrics", "metrics.py")

from fixtures import gen_gait as G  # noqa: E402
from fixtures import gen_style as GS  # noqa: E402

LOG = []


def rel(p):
    p = os.path.abspath(p)
    return os.path.relpath(p, PROJECT) if p.startswith(PROJECT) else p


def sh(args, name=None, env=None):
    t0 = time.time()
    r = subprocess.run([PY] + args, capture_output=True, text=True, cwd=PROJECT, env=env)
    dt = time.time() - t0
    shown = " ".join(a.replace(SCR, "$SCRATCH").replace(PROJECT + os.sep, "") for a in args)
    LOG.append(f"[exit {r.returncode}] [{dt:6.2f} s] $PY {shown}")
    if name:
        with open(os.path.join(EV, name), "w") as f:
            f.write(f"$ $PY {shown}\n# exit {r.returncode}, {dt:.2f} s\n{r.stdout}{r.stderr}")
    return r, dt


def metric(cmd, inputs, name, extra=(), keep_report=True):
    out = os.path.join(EV, "reports", name)
    if os.path.isdir(out):
        shutil.rmtree(out)
    r, dt = sh([CLI, cmd] + list(inputs) + ["--out", out] + list(extra))
    if not keep_report:
        os.remove(os.path.join(out, "report.json"))
    return r.returncode, dt, out


def main():
    global SCR
    skip_timing = "--skip-timing" in sys.argv
    SCR = tempfile.mkdtemp(prefix="t002_ev_")
    os.makedirs(os.path.join(EV, "reports"), exist_ok=True)
    rows = []
    # ------------------------------------------------ gait: reference set, negatives, boundaries
    from fixtures.tests_shim import NEG001, NEG002, m001_rat
    ref = os.path.join(SCR, "gait_ref")
    G.write_set(ref)
    rat = m001_rat(os.path.join(SCR, "m001_ratings.json"), "gait_ref")
    for cmd, name, extra in (("m001", "M-001_reference_no_panel", []),
                             ("m001", "M-001_reference_with_panel", ["--ratings", rat]),
                             ("m002", "M-002_reference", [])):
        code, dt, _ = metric(cmd, [ref], name, extra)
        rows.append((name, code))
    for sid, over in NEG001().items():
        d = os.path.join(SCR, f"neg_{sid}")
        G.write_set(d, over=over)
        r = m001_rat(os.path.join(SCR, f"r_{sid}.json"), f"neg_{sid}")
        code, _, _ = metric("m001", [d], f"{sid}_negative", ["--ratings", r])
        rows.append((f"{sid}_negative", code))
    for sid, over in NEG002().items():
        d = os.path.join(SCR, f"neg_{sid}")
        G.write_set(d, over=over)
        code, _, out = metric("m002", [d], f"{sid}_negative")
        rows.append((f"{sid}_negative", code))
    for sid, f, sp in boundary_gait():
        d = os.path.join(SCR, f"bd_{sid}_{f}")
        G.write_set(d, over={sp.capture: sp}, names=[sp.capture])
        code, _, out = metric("m001" if sid.startswith("M-001") else "m002", [d], f"{sid}_boundary_{f}")
        st = next(x for x in json.load(open(os.path.join(out, "report.json")))["submeasures"] if x["id"] == sid)
        rows.append((f"{sid}_boundary_{f}", f"{code} ({sid} {st['status']}, value {st.get('value')}, "
                                            f"{st.get('op')} {st.get('threshold')})"))
    # ------------------------------------------------ style: pass sets, negatives, controls
    st = os.path.join(SCR, "style")
    sd3, _ = GS.m003_pass_set(st)
    code, _, _ = metric("m003", [sd3], "M-003_pass_set", ["--profiles", "slate_teal"]); rows.append(("M-003_pass_set", code))
    for name, kw, hz in (("M-003.a_unquantised_offpalette", {"off_palette_share": 0.08}, "haze"),
                         ("M-003.a.internal_negative", {"off_palette_share": 0.01}, "haze"),
                         ("M-003.b_posterised_negative", {"mode": "none"}, "haze"),
                         ("M-003.c1_inverted_negative", {"invert": True}, "haze"),
                         ("M-003.c1_nohaze_negative", {"haze": False}, "haze"),
                         ("M-003.c2_flat_negative", {}, "flat"),
                         ("M-003.d-iv_bilinear_negative", {"upscale_mode": "bilinear"}, "haze"),
                         ("M-003.d-iv_offset_undeclared_negative", {"offset": (3, 2), "declare_offset": False}, "haze")):
        d = os.path.join(SCR, name)
        GS.m003_gameplay_frames(d, **kw)
        GS.m003_haze_cal(d, dL_mode=hz)
        code, _, _ = metric("m003", [d], name, ["--profiles", "slate_teal"])
        rows.append((name, code))
    sd4, _ = GS.m004_set(st)
    code, _, _ = metric("m004", [sd4], "M-004_pass_set", ["--profiles", "slate_teal", "--viewpoints", "eye", "--fixture"])
    rows.append(("M-004_pass_set", code))
    sd4s, _ = GS.m004_set(st, set_id="m004_screen", anchor_t="screen", anchor_y="screen", static_noise=True)
    code, _, _ = metric("m004", [sd4s], "M-004_screen_dither_and_temporal_noise_negative",
                        ["--profiles", "slate_teal", "--viewpoints", "eye", "--fixture"])
    rows.append(("M-004_screen_dither_and_temporal_noise_negative", code))
    with open(os.path.join(EV, "fixture_reports_index.md"), "w") as f:
        f.write("| Fixture report (evidence/T-002/reports/<name>/) | exit (detail) | failing gating IDs |\n|---|---|---|\n")
        for n, c in rows:
            rj = os.path.join(EV, "reports", n, "report.json")
            fl = json.load(open(rj))["failing_gating"] if os.path.isfile(rj) else []
            f.write(f"| {n} | {c} | {', '.join(fl)} |\n")
    # ------------------------------------------------ validators: good examples and mutants
    vlog = []
    for args in (["validate", "gait_log", "Tools/Metrics/examples/gait_log_example.gait.jsonl"],
                 ["validate", "style_capture", "Tools/Metrics/examples/example_set"],
                 ["validate", "palette_set", "research/R-002/palettes.json"],
                 ["validate", "palette_set", "Tools/Metrics/examples/palette_set_example.json"]):
        r, _ = sh([CLI] + args)
        vlog.append(f"$ $PY {CLI} {' '.join(args)}\n# exit {r.returncode}\n{r.stdout}")
    for name, mut in mutants():
        p = os.path.join(SCR, f"mut_{name}.gait.jsonl")
        mut(p)
        r, _ = sh([CLI, "validate", "gait_log", p])
        vlog.append(f"$ $PY {CLI} validate gait_log <mutant {name}>\n# exit {r.returncode}\n" +
                    r.stdout.replace(SCR, "$SCRATCH"))
    with open(os.path.join(EV, "validator_runs.txt"), "w") as f:
        f.write("\n".join(vlog))
    # ------------------------------------------------ rescore transcripts
    rp = os.path.join(EV, "reports", "M-002_reference", "report.json")
    sh([CLI, "rescore", rp], "rescore_same_registry.txt")
    import yaml
    reg = yaml.safe_load(open(os.path.join(METRICS, "registry", "M-002@v1.yaml")))
    reg["revision"] = "v1.1-demo"; reg["submeasures"]["M-002.1"]["thresholds"]["max_frac"]["value"] = 1e-9
    p11 = os.path.join(SCR, "M-002@v1.1.yaml"); yaml.safe_dump(reg, open(p11, "w"), sort_keys=False)
    sh([CLI, "rescore", rp, "--registry", p11], "rescore_other_registry_refused.txt")
    sh([CLI, "rescore", rp, "--registry", p11, "--allow-different-registry"], "rescore_other_registry_explicit.txt")
    # ------------------------------------------------ registry flip demo (no code change)
    gd = os.path.join(SCR, "flip"); G.write_set(gd, names=["estop"])
    reg1 = yaml.safe_load(open(os.path.join(METRICS, "registry", "M-001@v1.yaml")))
    reg1["submeasures"]["M-001.4"]["thresholds"]["a_max_g"]["value"] = 0.24
    pf = os.path.join(SCR, "M-001@v1.yaml"); yaml.safe_dump(reg1, open(pf, "w"), sort_keys=False)
    out = []
    for lab, extra in (("registry v1.0 (a_max_g 0.30)", []), ("temp registry (a_max_g 0.24)", ["--registry", pf])):
        r, _ = sh([CLI, "m001", gd, "--out", os.path.join(SCR, "flip_out" + str(len(out))), "--no-images"] + extra)
        line = [l for l in r.stdout.splitlines() if "M-001.4" in l]
        out.append(f"{lab}: exit {r.returncode}; {line[0].strip() if line else ''}")
    with open(os.path.join(EV, "registry_flip_demo.txt"), "w") as f:
        f.write("estop capture at 2.5 m/s^2 peak deceleration (0.255 g); only the registry value changes:\n" +
                "\n".join(out) + "\nThe full flip test over every threshold key is tests/test_registry.py::"
                "test_registry_flip_gait / test_registry_flip_style (see pytest_full.txt).\n")
    # ------------------------------------------------ literal scan
    r = subprocess.run([PY, "-m", "pytest", "-q", "Tools/Metrics/tests/test_registry.py::test_no_threshold_literals_in_code",
                        "-p", "no:cacheprovider"], capture_output=True, text=True, cwd=PROJECT)
    with open(os.path.join(EV, "literal_scan.txt"), "w") as f:
        f.write("$ $PY -m pytest Tools/Metrics/tests/test_registry.py::test_no_threshold_literals_in_code\n"
                f"# exit {r.returncode}\n{r.stdout[-800:]}\nAllow-list with reasons: tests/test_registry.py ALLOW, "
                "ALLOW_FILES (formula constants of sRGB / CIEDE2000, plot styling).\n")
    # ------------------------------------------------ determinism
    det = []
    env_a = dict(os.environ, OMP_NUM_THREADS="1", LC_ALL="C", LANG="C"); env_a.pop("DISPLAY", None)
    env_b = dict(os.environ, OMP_NUM_THREADS="8", LC_ALL="de_DE.UTF-8", LANG="de_DE.UTF-8"); env_b.pop("DISPLAY", None)
    from kaiju_metrics.common import dump_json, sha256_bytes
    from kaiju_metrics.report import strip_run
    for cmd, inp, extra in (("m001", ref, ["--ratings", rat]), ("m002", ref, []), ("m003", sd3, ["--profiles", "slate_teal"]),
                            ("m004", sd4, ["--profiles", "slate_teal", "--viewpoints", "eye", "--fixture"])):
        hs = []
        for k, env in enumerate((env_a, env_b)):
            o = os.path.join(SCR, f"det_{cmd}_{k}")
            sh([CLI, cmd, inp, "--out", o] + extra, env=env)
            hs.append(sha256_bytes(dump_json(strip_run(json.load(open(os.path.join(o, "report.json"))))).encode()))
        det.append(f"{cmd}: run A (OMP 1, LC_ALL=C) {hs[0][:16]}  run B (OMP 8, de_DE) {hs[1][:16]}  "
                   f"{'IDENTICAL' if hs[0] == hs[1] else 'DIFFERENT'}")
    with open(os.path.join(EV, "determinism.txt"), "w") as f:
        f.write("SHA-256 of report.json with the declared run block stripped; DISPLAY unset in both runs.\n" +
                "\n".join(det) + "\nFixture regeneration bit-identity: tests/test_tools.py::"
                "test_fixtures_regenerate_bit_identically.\n")
    # ------------------------------------------------ exit-code table (live)
    table = exit_codes(ref, rat, sd3, sd4)
    with open(os.path.join(EV, "exit_codes.md"), "w") as f:
        f.write(table)
    # ------------------------------------------------ calibration on synthetic ratings
    calib()
    # ------------------------------------------------ diagnostics images
    diag_images(sd4s)
    # ------------------------------------------------ timing
    if not skip_timing:
        timing(ref)
    with open(os.path.join(EV, "commands.log"), "w") as f:
        f.write("\n".join(LOG) + "\n")
    shutil.rmtree(SCR, ignore_errors=True)


def boundary_gait():
    g, VC, STEP = G.G, G.VC, G.STEP
    out = []
    for f in (0.98, 1.02):
        out.append(("M-001.4", f, G.spec_estop(decel=f * 0.30 * g)))
        out.append(("M-001.10", f, G.spec_start(steps_to_90=2 * f)))
        out.append(("M-001.12", f, G.spec_estop(decel=(VC - 0.3) / (f * VC / (0.30 * g)))))
        s = f * 0.005 * G.REF["foot_length_m"]
        out.append(("M-002.1", f, G.spec_walk(0.2, strides=5, slips={(1, 1): (0.6 * s, 0.8 * s)})))
    return out


def mutants():
    src = os.path.join(METRICS, "examples", "gait_log_example.gait.jsonl")
    recs = [json.loads(l) for l in open(src)]
    steps = [i for i, r in enumerate(recs) if r["rec"] == "step"]

    def w(fn):
        def go(p):
            rr = json.loads(json.dumps(recs)); fn(rr)
            open(p, "w").write("".join(json.dumps(r) + "\n" for r in rr))
        return go

    def gap(r): del r[steps[5]]
    def schema(r): r[0]["schema"] = "gait_log@v0"
    def quat(r): r[steps[0]]["root_q"] = [0, 0.5, 0, 0.5]
    def g_units(r): r[0]["g_mps2"] = 32.2
    def source(r): next(x for x in r if x.get("type") == "cam_impulse")["source_n"] = 99999
    def kmh(r):
        for x in r:
            if x["rec"] == "step": x["com_v"] = [v * 3.6 for v in x["com_v"]]
    def trunc(p):
        txt = open(src).read(); open(p, "w").write(txt[: len(txt) // 2])
    return [("gap_in_n", w(gap)), ("wrong_schema", w(schema)), ("non_unit_quaternion", w(quat)),
            ("g_in_ft_s2", w(g_units)), ("dangling_cam_impulse", w(source)), ("com_v_in_kmh", w(kmh)),
            ("truncated_file", trunc)]


def exit_codes(ref, rat, sd3, sd4):
    L = ["| Metric | PASS (0) | FAIL (1) | INVALID_INPUT (2) | INCOMPLETE (3) |", "|---|---|---|---|---|"]
    bad = os.path.join(SCR, "ec_bad"); G.write_set(bad, over={"walk_fr020": G.spec_walk(0.2, strides=5, slips={(0, 2): (0.2, 0.0)})})
    part = os.path.join(SCR, "ec_part"); G.write_set(part, names=["walk_fr020"])
    broken = os.path.join(SCR, "ec_broken"); os.makedirs(broken, exist_ok=True)
    open(os.path.join(broken, "x.gait.jsonl"), "w").write('{"rec":"step"}\n')
    cases = {
        "m001": [([ref, "--ratings", rat]), ([os.path.join(SCR, "neg_M-001.12"), "--ratings",
                                              os.path.join(SCR, "r_M-001.12.json")]), ([broken]), ([ref])],
        "m002": [([ref]), ([bad]), ([broken]), ([part])],
        "m003": [([sd3, "--profiles", "slate_teal", "--ratings", m003_rat(os.path.basename(sd3))]),
                 ([os.path.join(SCR, "M-003.c1_inverted_negative"), "--profiles", "slate_teal"]),
                 ([broken]), ([sd3, "--profiles", "slate_teal"])],
        "m004": [([sd4, "--profiles", "slate_teal", "--viewpoints", "eye", "--fixture"]),
                 ([os.path.join(SCR, "style", "m004_screen"), "--profiles", "slate_teal", "--viewpoints", "eye", "--fixture"]),
                 ([broken]), ([sd4, "--profiles", "slate_teal", "--viewpoints", "eye"])],
    }
    for cmd, cs in cases.items():
        cells = []
        for k, args in enumerate(cs):
            r, _ = sh([CLI, cmd] + args + ["--out", os.path.join(SCR, f"ec_{cmd}_{k}"), "--quiet"])
            cells.append(f"{r.returncode}: {r.stdout.strip()[:70]}")
        L.append(f"| {cmd} | " + " | ".join(cells) + " |")
    return "\n".join(L) + "\n\nINCOMPLETE examples: m001 without ratings (M-001.13 PENDING), m002 with captures missing, " \
                          "m003 without ratings (M-003 f PENDING), m004 60-frame sequences without --fixture.\n"


def m003_rat(build):
    p = os.path.join(SCR, "m003_ratings.json")
    raters = [{"rater_id": f"r{i}", "rater_type": "agent", "blind": True} for i in range(8)]
    rows = []
    for i in range(8):
        rows += [{"rater_id": f"r{i}", "build": build, "capture": f"c{c}", "profile": "slate_teal", "score": 8.5}
                 for c in range(12)]
        rows += [{"rater_id": f"r{i}", "build": build, "capture": f"p{a}", "anchor": "positive", "score": 9} for a in range(2)]
        rows += [{"rater_id": f"r{i}", "build": build, "capture": f"n{a}", "anchor": "negative", "score": 2} for a in range(2)]
    json.dump({"schema": "panel_ratings@v1", "metric": "M-003", "raters": raters, "ratings": rows}, open(p, "w"))
    return p


def m001_rat_bad(p, build):
    from fixtures.tests_shim import m001_rat
    return m001_rat(p, build)


def calib():
    L = G.REF["foot_length_m"]
    slips = [0.005, 0.01, 0.015, 0.02, 0.03, 0.035, 0.045, 0.05, 0.06, 0.07]
    rd = os.path.join(SCR, "cal_reports")
    for i, s in enumerate(slips):
        d = os.path.join(SCR, f"cal{i}")
        sl = {(f, k): (s, 0.0) for f in range(2) for k in range(12)}
        G.write_set(d, over={"walk_fr020": G.spec_walk(0.2, strides=5, slips=sl)}, names=["walk_fr020"])
        sh([CLI, "m002", d, "--out", os.path.join(rd, f"cap{i}"), "--label", f"cap{i}", "--no-images"])
    raters = [{"rater_id": f"founder_{x}", "rater_type": "human", "blind": True} for x in "ab"]
    rows = []
    for r in raters:
        for i, s in enumerate(slips):
            rows.append({"rater_id": r["rater_id"], "build": "cal", "capture": f"cap{i}", "score": 9 if s < 0.04 else 5})
        rows.append({"rater_id": r["rater_id"], "build": "cal", "capture": "posA", "anchor": "positive", "score": 9})
        rows.append({"rater_id": r["rater_id"], "build": "cal", "capture": "negA", "anchor": "negative", "score": 2})
    os.makedirs(os.path.join(EV, "calibration"), exist_ok=True)
    rp = os.path.join(EV, "calibration", "synthetic_ratings.json")
    json.dump({"schema": "panel_ratings@v1", "metric": "M-002", "raters": raters, "ratings": rows}, open(rp, "w"), indent=1)
    sh([CLI, "calibrate", "--ratings", rp, "--reports", rd, "--out", os.path.join(EV, "calibration")],
       "calibration/calibrate_run.txt")
    with open(os.path.join(EV, "calibration", "README.txt"), "w") as f:
        f.write(f"Synthetic ratings: capture i has uniform slip s_i on every contact (m): {slips}; rated 9 iff s_i < "
                f"0.04 m else 5. Known cut on M-002.2 (p95 slip / L_f) lies in ({0.035 / L:.6f}, {0.045 / L:.6f}).\n")


def diag_images(screen_set):
    os.makedirs(os.path.join(EV, "diagnostics"), exist_ok=True)
    notes = []
    d = os.path.join(SCR, "diag_slip"); G.write_set(d, over={"walk_fr020": G.spec_walk(0.2, strides=5, slips={(1, 3): (0.09, -0.12)})})
    _, _, out = metric("m002", [d], "diag_M-002_slip")
    shutil.copy(os.path.join(out, "diag_slip.png"), os.path.join(EV, "diagnostics", "slip_plot.png"))
    notes.append("slip_plot.png: injected 0.15 m slip on foot R, 4th contact of walk_fr020 (M-002.1 negative); "
                 "the annotated worst contact is that one (reports/diag_M-002_slip/summary.txt 'where').")
    shutil.copy(os.path.join(EV, "reports", "M-001_reference_no_panel", "diag_gait.png"),
                os.path.join(EV, "diagnostics", "gait_traces.png"))
    notes.append("gait_traces.png: a_fore, a_lat, yaw rate of the reference set with the M-001.4/.5/.7 limits.")
    o = os.path.join(EV, "reports", "M-003.a_unquantised_offpalette")
    shutil.copy(os.path.join(o, "diag_M-003a_dE00_heatmap.png"), os.path.join(EV, "diagnostics", "dE00_heatmap.png"))
    notes.append("dE00_heatmap.png: 8 % #00ff00 pixels injected in the first rows of each frame; they are the bright "
                 "band; worst offenders listed in that report (M-003.a.output diagnostics.worst_offenders).")
    o = os.path.join(EV, "reports", "M-003_pass_set")
    shutil.copy(os.path.join(o, "diag_M-003b_dither_mask.png"), os.path.join(EV, "diagnostics", "dither_mask.png"))
    notes.append("dither_mask.png: magenta = interleaved pixels; the pass-set renderer dithers only the lower sky.")
    o = os.path.join(EV, "reports", "M-003.d-iv_bilinear_negative")
    shutil.copy(os.path.join(o, "diag_M-003d4_nonuniform_blocks.png"), os.path.join(EV, "diagnostics", "nonuniform_blocks.png"))
    notes.append("nonuniform_blocks.png: bilinear upscale; bright = s x s blocks that are not uniform (edges).")
    o = os.path.join(EV, "reports", "M-004_screen_dither_and_temporal_noise_negative")
    shutil.copy(os.path.join(o, "diag_M-004_shimmer_map.png"), os.path.join(EV, "diagnostics", "shimmer_map.png"))
    notes.append("shimmer_map.png: screen-fixed checker; bright = pixels changing after reprojection (the world-anchored "
                 "bars stay dark).")
    with open(os.path.join(EV, "diagnostics", "README.txt"), "w") as f:
        f.write("\n".join(notes) + "\n")


def timing(ref):
    L = ["| Metric | Input | Wall time (s) | Limit |", "|---|---|---|---|"]
    _, dt = sh([CLI, "m001", ref, "--out", os.path.join(SCR, "t1"), "--no-images"]); L.append(f"| M-001 | 8 captures + stomp (synthetic, 50 Hz) | {dt:.1f} | 60 |")
    _, dt = sh([CLI, "m002", ref, "--out", os.path.join(SCR, "t2"), "--no-images"]); L.append(f"| M-002 | 9 captures | {dt:.1f} | 60 |")
    big3 = os.path.join(SCR, "t3")
    GS.m003_gameplay_frames(os.path.join(big3, "m003_480"), W=480, H=270, scale=4)
    GS.m003_haze_cal(os.path.join(big3, "m003_480"), W=480, H=270, scale=4)
    _, dt = sh([CLI, "m003", os.path.join(big3, "m003_480"), "--profiles", "slate_teal", "--out", os.path.join(SCR, "t3o")])
    L.append(f"| M-003 | 12 frames 480x270 internal, 1920x1080 output, + haze_cal | {dt:.1f} | 60 |")
    big4 = os.path.join(SCR, "t4")
    GS.m004_set(big4, set_id="m004_one", n_frames=180, W=480, H=270)
    _, dt = sh([CLI, "m004", os.path.join(big4, "m004_one"), "--profiles", "slate_teal", "--viewpoints", "eye", "--out",
                os.path.join(SCR, "t4o"), "--no-images"])
    L.append(f"| M-004 | one profile-viewpoint set: 6 sequences x 180 frames, 480x270, serial | {dt:.1f} | 60 |")
    GS.m004_set(big4, set_id="m004_full", n_frames=180, W=480, H=270, profiles=("slate_teal", "warm_amber_haze",
                                                                               "slate_crimson"), viewpoints=("eye", "street"))
    _, dt = sh([CLI, "m004", os.path.join(big4, "m004_full"), "--out", os.path.join(SCR, "t4f"), "--no-images", "--jobs", "8"])
    L.append(f"| M-004 | full set: 36 sequences x 180 frames, 480x270, --jobs 8 | {dt:.1f} | 600 |")
    with open(os.path.join(EV, "timing.md"), "w") as f:
        f.write("\n".join(L) + f"\n\nMachine: {os.cpu_count()} cores; kaiju env; headless.\n")


SCR = ""

if __name__ == "__main__":
    main()
