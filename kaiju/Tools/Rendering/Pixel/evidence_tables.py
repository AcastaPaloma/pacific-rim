#!/usr/bin/env python
"""T-004: turns the stored metric reports (evidence/T-004/metrics) into the markdown tables of EVIDENCE.md.
Reads only report.json files written by the T-002 tools and the JSON written by pixel_checks.py; computes nothing new
except per-profile minima / maxima of values the reports already hold. Usage: evidence_tables.py [metrics_dir] > tables.md
"""
import glob, json, os, statistics, sys

M = sys.argv[1] if len(sys.argv) > 1 else "evidence/T-004/metrics"


def load(p):
    return json.load(open(p)) if os.path.isfile(p) else None


def fmt(v):
    if v is None: return "-"
    if isinstance(v, bool): return "yes" if v else "no"
    if isinstance(v, (int,)): return str(v)
    return ("%.4f" % v).rstrip("0").rstrip(".") if abs(v) < 1000 else "%.0f" % v


def sub_table(r, title):
    print("\n**%s** — verdict `%s`, registry %s (sha256 %s), failing gating: %s\n" % (title, r["headless_verdict"], r["registry"]["revision"], r["registry"]["sha256"][:12], ", ".join(r["failing_gating"]) or "none"))
    print("| Sub-measure | Kind | Value | Rule | Status | Decisive frame / sequence |"); print("|---|---|---|---|---|---|")
    for x in r["submeasures"]:
        where = (x.get("decisive_part") or (x.get("diagnostics") or {}).get("where") or x.get("note") or "")
        print("| %s | %s | %s %s | %s %s | %s | %s |" % (x["id"], x["kind"], fmt(x.get("value")), x.get("unit") or "", x.get("op") or "", fmt(x.get("threshold")), x["status"], str(where)[:110]))


def m003_by_profile(r):
    frames = r["extra"]["frames"]; profs = sorted({f["profile"] for f in frames})
    print("\n| Profile | Frames | a: min A₂ internal | a: min A₂ output | b: frames DIG ≥ 0.15 / evaluated | b: DIG min / max | b: median O | c1: max drop L* | c1: min ρ | c1: min span L* | d-i: max levels | d-ii: max hue families | d-iii: accent share min / max | d-iv: min uniform blocks | e: sky share min / max | e: horizon max | e: contrast min |")
    print("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|")
    for p in profs:
        fs = [f for f in frames if f["profile"] == p]
        ev = [f for f in fs if f.get("dither_evaluated")]; dig = [f["DIG"] for f in ev if f.get("DIG") is not None]
        o = [f["O"] for f in ev if f.get("O") is not None]
        dep = [f["depth"] for f in fs if f.get("depth")]
        acc = [f["accent_share"] for f in fs if f.get("accent_share") is not None]
        comp = [f["composition"] for f in fs if f.get("composition")]
        print("| %s | %d | %s | %s | %d / %d | %s / %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s |" % (
            p, len(fs), fmt(min(f["A2_int"] for f in fs)), fmt(min(f["A2_out"] for f in fs)), sum(1 for d in dig if d >= 0.15), len(ev),
            fmt(min(dig)) if dig else "-", fmt(max(dig)) if dig else "-", fmt(statistics.median(o)) if o else "-",
            fmt(max(d["max_drop"] for d in dep)) if dep else "-", fmt(min(d["rho"] for d in dep)) if dep else "-", fmt(min(d["span"] for d in dep)) if dep else "-",
            fmt(max(f["levels"] for f in fs)), fmt(max(f["hue_families"] for f in fs)),
            (fmt(min(acc)) + " / " + fmt(max(acc))) if acc else "no accent", fmt(min(f["grid_uniform"] for f in fs)),
            (fmt(min(c["sky_share"] for c in comp)) + " / " + fmt(max(c["sky_share"] for c in comp))) if comp else "-",
            fmt(max(c["horizon"] for c in comp)) if comp else "-", fmt(min(c["contrast"] for c in comp)) if comp else "-"))


def m004_sequences(r, by_profile=True):
    seqs = sorted(r["extra"]["sequences"], key=lambda q: (q["profile"], q["viewpoint"], q["class"]))
    print("\n| Profile | Viewpoint | Sequence | Lag | S_px | C_px | F | Valid share | S pair p95 |"); print("|---|---|---|---|---|---|---|---|---|")
    for q in seqs:
        print("| %s | %s | %s | %d | %s | %s | %s | %s | %s |" % (q["profile"], q["viewpoint"], q["class"], q["lag"], fmt(q["S"]), fmt(q.get("C")), fmt(q["F"]), fmt(q["valid_share"]), fmt(q.get("S_pair_p95"))))


def m004_groups(seqs):
    """Worst S, C, F per threshold group."""
    g = {"S0": ["S0"], "Y": ["Y1", "Y2"], "T12": ["T1", "T2"], "T3": ["T3"]}; out = {}
    for name, classes in g.items():
        qs = [q for q in seqs if q["class"] in classes]
        if qs: out[name] = (max(q["S"] for q in qs), max((q.get("C") or 0) for q in qs), max(q["F"] for q in qs))
    return out


LIMITS = {"S0": (0, 0, 0), "Y": (0.01, 0.01, 0.001), "T12": (0.12, 0.15, 0.005), "T3": (0.12, 0.15, 0.08)}


def main():
    r = load(os.path.join(M, "m003", "report.json"))
    if r:
        print("### M-003@v1 on the 180 stills (5 profiles × 3 viewpoints × 12 frames) and the haze calibration set")
        sub_table(r, "M-003@v1, `m003_stills` + `haze_cal`"); m003_by_profile(r)
    for name, title in (("m003_details", "detail views (sky gradient, gradient-lit surface, scale view), 3 frames per profile: fewer than the 12 the metric requires, so the tool's verdict is about coverage; the values are what C4 asks for individually"),
                        ("m003_1440p", "2560×1440 output (k = 5, 512×288 internal), one frame per profile"), ("m003_2160p", "3840×2160 output (k = 6, 640×360 internal), slate_teal, reported only")):
        r = load(os.path.join(M, name, "report.json"))
        if r:
            print("\n### M-003@v1, %s" % title); sub_table(r, name)
            fr = r["extra"].get("frames") or []
            if name == "m003_details" and fr:
                print("\n| Frame | DIG | O | gradient share | A₂ internal | grid uniform |"); print("|---|---|---|---|---|---|")
                for f in fr: print("| %s | %s | %s | %s | %s | %s |" % (f["frame"], fmt(f.get("DIG")), fmt(f.get("O")), fmt(f.get("gradient_share")), fmt(f["A2_int"]), fmt(f["grid_uniform"])))
    print("\n### Negative controls on the same tool (slate_teal, eye70, 12 frames each): the tests can fail")
    print("\n| Control | Verdict | Failing gating sub-measures | a.internal | a.output | b.DIG share | c1 value | d-iv |"); print("|---|---|---|---|---|---|---|---|")
    for p in sorted(glob.glob(os.path.join(M, "controls", "*", "report.json"))):
        r = load(p); v = {x["id"]: x for x in r["submeasures"]}
        g = lambda k: fmt(v[k].get("value")) + " (" + v[k]["status"] + ")" if k in v else "-"
        print("| %s | %s | %s | %s | %s | %s | %s | %s |" % (os.path.basename(os.path.dirname(p)), r["headless_verdict"], ", ".join(r["failing_gating"]) or "none", g("M-003.a.internal"), g("M-003.a.output"), g("M-003.b.DIG"), g("M-003.c1"), g("M-003.d-iv")))
    r = load(os.path.join(M, "m004", "report.json"))
    if r:
        print("\n### M-004@v1, five profiles × two viewpoints, configured default anchoring (`hybrid`), position snap and rotation quantisation on")
        sub_table(r, "M-004@v1, `m004_<profile>`"); m004_sequences(r)
        print("\nWorst value per profile and threshold group (S_px / C_px / F; limits: S0 0 / 0 / 0, Y 0.01 / 0.01 / 0.001, T12 0.12 / 0.15 / 0.005, T3 0.12 / 0.15 / 0.08):\n")
        print("| Profile | S0 | Y1–Y2 | T1–T2 | T3 | Sub-measures passed |"); print("|---|---|---|---|---|---|")
        for p in sorted({q["profile"] for q in r["extra"]["sequences"]}):
            g = m004_groups([q for q in r["extra"]["sequences"] if q["profile"] == p]); ok = 0; cells = []
            for name in ("S0", "Y", "T12", "T3"):
                s, c, f = g[name]; lim = LIMITS[name]; ok += (s <= lim[0]) + (c <= lim[1]) + (f <= lim[2])
                cells.append("%s / %s / %s" % (fmt(s), fmt(c), fmt(f)))
            print("| %s | %s | %d of 12 |" % (p, " | ".join(cells), ok))
    print("\n### M-004@v1 A/B: every anchoring mode and each stabiliser off (slate_teal, eye70; C12)")
    print("\nWorst S_px / C_px / F per group. `snap` = position snap, `rotq` = rotation quantisation.\n")
    print("| Variant | S0 | Y1–Y2 | T1–T2 | T3 | Sub-measures passed |"); print("|---|---|---|---|---|---|")
    for p in sorted(glob.glob(os.path.join(M, "m004ab", "*", "report.json"))):
        r = load(p); seqs = r["extra"].get("sequences") or []; g = m004_groups(seqs); cells = []; ok = 0; n = 0
        for name in ("S0", "Y", "T12", "T3"):
            if name in g:
                s, c, f = g[name]; lim = LIMITS[name]; ok += (s <= lim[0]) + (c <= lim[1]) + (f <= lim[2]); n += 3
                cells.append("%s / %s / %s" % (fmt(s), fmt(c), fmt(f)))
            else: cells.append("-")
        print("| %s | %s | %d of %d |" % (os.path.basename(os.path.dirname(p)), " | ".join(cells), ok, n))
    r = load(os.path.join(M, "m004_dolly", "report.json"))
    if r and r["extra"].get("no_verdict_sequences") is not None:
        print("\n### Dolly toward the 10 km layer (reported, B15; the tool gives dolly sequences no verdict)\n")
        print("```"); print(json.dumps(r["extra"].get("no_verdict_sequences"))[:600]); print("```")
    print("\n### Checks outside the metric tools (`Tools/Rendering/Pixel/pixel_checks.py`)\n")
    print("| Check | File | Pass | Key values |"); print("|---|---|---|---|")
    for p in sorted(glob.glob(os.path.join(M, "checks", "*.json"))):
        r = load(p); name = os.path.basename(p)
        if "subpixel_on" in r:
            on, off = r["subpixel_on"], r["subpixel_off"]
            key = "compensation on: max displacement error %s px, max step error %s px (pass %s); off: %s px, %s px (pass %s); commanded %s px/frame" % (fmt(on["max_abs_displacement_error_px"]), fmt(on["max_abs_step_error_px"]), fmt(on["pass_within_1px"]), fmt(off["max_abs_displacement_error_px"]), fmt(off["max_abs_step_error_px"]), fmt(off["pass_within_1px"]), fmt(on["commanded_px_per_frame"]))
            ok = on["pass_within_1px"] and not off["pass_within_1px"]
        else:
            ok = r.get("pass"); key = ""
            if "points" in r: key = "; ".join("%s: %s m vs %s m (%.3f %%)" % (q["point"].split()[0], fmt(q["decoded_m"]), fmt(q["expected_eye_depth_m"]), 100 * q["relative_error"]) for q in r["points"])
            elif "near_150m" in r:
                key = "; ".join("%s: in view in %d frames, dL* min %s, median %s, frames with dL* >= 4: %d" % (k, r[k]["frames_in_view"], fmt(r[k]["dL_min"]), fmt(r[k]["dL_median"]), r[k]["frames_dL_ge_4"]) for k in ("near_150m", "far_1km"))
            elif "marker_pixels" in r: key = "marker %s px, height %s px, dL* %s; 80 m block height %s px at %s m, dL* to background %s" % (r.get("marker_pixels"), r.get("marker_height_px"), fmt(r.get("marker_dL")), fmt(r.get("block80_height_px")), fmt(r.get("block80_distance_m")), fmt(r.get("block80_dL")))
            elif "png_files" in r: key = "%d PNG and %d JSON files compared, %d differ" % (r["png_files"], r["json_files"], r["differing"])
            elif "identical_color_internal" in r: key = "internal, output, index, depth, pose and offsets identical: %s; differs from the frame before the cut: %s" % (fmt(all(r[k] for k in r if k.startswith("identical_"))), fmt(r["differs_from_previous_frame"]))
            elif "frames" in r and r["frames"] and "uniform_blocks_declared" in r["frames"][0]:
                key = "uniform blocks at the declared offset %s: min %s; with the offset ignored: min %s" % (r["frames"][0]["declared_offset"], fmt(min(f["uniform_blocks_declared"] for f in r["frames"])), fmt(min(f["uniform_blocks_offset_ignored"] for f in r["frames"])))
            elif "frames" in r and r["frames"] and "layers" in r["frames"][0]:
                fr = r["frames"]; f0 = fr[0]; L = f0["layers"]
                key = "frame 0 mean L*: " + ", ".join("%s %s" % (k, fmt(v["mean_L"])) for k, v in L.items()) + "; successively lighter in %d of %d frames; far layer minus sky at horizon %s L*; near field darkest in %d of %d; ground seams within adjacent bands %d of %d" % (
                    sum(1 for f in fr if f["successively_lighter"]), len(fr), fmt(f0.get("far_layer_minus_sky_L")), sum(1 for f in fr if f.get("near_field_darkest", True)), len(fr), sum(f["ground_seams_only_adjacent_bands"] for f in fr), sum(f["ground_seams_checked"] for f in fr))
        print("| %s | `metrics/checks/%s` | %s | %s |" % (name.split(".")[0], name, fmt(bool(ok)), key))
    t = load(os.path.join(os.path.dirname(M), "timing", "timing_summary.json"))
    if t:
        print("\n### Frame timing (development Mac: relative regression only; M-008 is not verified)\n")
        print("Hardware: %s, %s, %s, Unity %s, batch mode. Method: %s. %d frames per configuration.\n" % (t["hardware"]["gpu"], t["hardware"]["graphics_api"], t["hardware"]["os"], t["hardware"]["unity"], t["method"], t["frames_per_configuration"]))
        print("| Output | Internal | Item | mean ms | p50 ms | p95 ms | max ms |"); print("|---|---|---|---|---|---|---|")
        for res in t["resolutions"]:
            for k, v in list(res["frame_ms"].items()) + [("cost: " + k, v) for k, v in res["paired_cost_ms"].items()]:
                print("| %s | %s (k = %d) | %s | %s | %s | %s | %s |" % (res["output"], res["internal"], res["integer_scale"], k, fmt(v["mean"]), fmt(v["p50"]), fmt(v["p95"]), fmt(v["max"])))
        print("\nGC.Alloc samples over 100 frames: " + "; ".join("%s: %s" % (res["output"], json.dumps(res["gc_alloc_samples_per_100_frames"])) for res in t["resolutions"]))


if __name__ == "__main__":
    main()
