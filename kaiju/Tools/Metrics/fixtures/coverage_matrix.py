"""Write evidence/T-002/coverage_matrix.md: sub-measure ID -> known-answer / boundary / negative tests -> fixtures.

Usage (project root): /opt/miniconda3/envs/kaiju/bin/python Tools/Metrics/fixtures/coverage_matrix.py"""
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
METRICS = os.path.dirname(HERE)
PROJECT = os.path.dirname(os.path.dirname(METRICS))
sys.path.insert(0, METRICS)
sys.path.insert(0, os.path.join(METRICS, "tests"))

from test_registry import SPEC_IDS  # noqa: E402

FIXTURES = {"M-001": "gen_gait.py (spec_walk/start/stop/estop/turn/curve)", "M-002": "gen_gait.py (slips, roll, sink, flags, ext_f)",
            "M-003": "gen_style.py (m003_gameplay_frames, m003_haze_cal, repaint)", "M-004": "gen_style.py (m004_sequence, m004_set)"}


def keys(sid):
    m, rest = sid[:5], sid[6:]
    base = m.replace("-", "")
    r = rest.replace("-", "_").replace(".", "_")
    out = [f"{base}_{r}", sid]
    if m == "M-003":
        out += [f"{base}_{rest.split('.')[0].replace('-', '_')}_"]
        if rest.startswith("a."):
            out += ["M003_a_known", "M003_a_controls"]
    if m == "M-004":
        g = rest.split(".")[-1]
        out += {"S0": ["M004_S0", "M004_pass_set_known", "M004_screen_set_known"],
                "Y": ["M004_pass_set_known", "M004_screen_set_known", "negative_fails_only_its_class-Y"],
                "T12": ["M004_pass_set_known", "M004_screen_set_known", "M004_S_px_translation_boundary[T2",
                        "negative_fails_only_its_class-T12"],
                "T3": ["M004_pass_set_known", "M004_screen_set_known", "M004_S_px_translation_boundary[T3",
                       "negative_fails_only_its_class-T3"]}.get(g, [])
    return out


def main():
    r = subprocess.run([sys.executable, "-m", "pytest", "--collect-only", "-q", "-p", "no:cacheprovider",
                        os.path.join(METRICS, "tests")], capture_output=True, text=True, cwd=PROJECT)
    ids = [l.strip() for l in r.stdout.splitlines() if "::" in l]
    L = ["# Coverage matrix (sub-measure -> tests)", "",
         "Generated from `pytest --collect-only`; known = known-answer / control reproduction, boundary = 98 % / 102 % "
         "pair (boundary tests also assert the measured value against construction truth), negative = fails that ID (only that ID where the fixture allows), flip = registry flip test.", "",
         "| Sub-measure | known | boundary | negative | flip | fixtures |", "|---|---|---|---|---|---|"]
    gaps = []
    for m, sids in SPEC_IDS.items():
        for sid in sids:
            ks = keys(sid)
            hit = [i for i in ids if any(re.search(re.escape(k) + r"(?![0-9])", i) for k in ks)]
            cat = {"known": [], "boundary": [], "negative": [], "flip": []}
            for i in hit:
                n = i.split("::")[-1]
                if "flip" in n:
                    cat["flip"].append(n)
                elif "boundary" in n:
                    cat["boundary"].append(n)
                elif "negative" in n or "controls" in n or "R002" in n:
                    cat["negative"].append(n)
                else:
                    cat["known"].append(n)
            cell = lambda v: "<br>".join(sorted(set(v))[:4]) + (f" (+{len(set(v)) - 4})" if len(set(v)) > 4 else "") if v else "-"
            L.append(f"| {sid} | {cell(cat['known'])} | {cell(cat['boundary'])} | {cell(cat['negative'])} | "
                     f"{len(cat['flip'])} | {FIXTURES[m]} |")
            if not cat["known"] and not cat["negative"] and not cat["boundary"]:
                gaps.append(sid)
    L += ["", f"Collected tests: {len(ids)}. Sub-measures without a named known or negative test: "
              f"{', '.join(gaps) if gaps else 'none'}."]
    out = os.path.join(PROJECT, "evidence", "T-002", "coverage_matrix.md")
    open(out, "w").write("\n".join(L) + "\n")
    print(out, len(ids), gaps)


if __name__ == "__main__":
    main()
