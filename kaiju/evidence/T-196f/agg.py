import json, os, sys, glob
R = "/private/tmp/t196f_run/out"
rows = []
for arm in ("perslab", "shared"):
    for c in sorted(os.listdir(f"{R}/{arm}")):
        d = f"{R}/{arm}/{c}"
        try: out = json.load(open(f"{d}/stdout.json"))
        except Exception as e: out = None
        ex = open(f"{d}/exit.txt").read().strip() if os.path.exists(f"{d}/exit.txt") else "running"
        v2 = json.load(open(f"{d}/m003/div_v2.json")) if os.path.exists(f"{d}/m003/div_v2.json") else None
        rep = json.load(open(f"{d}/m003/report.json")) if os.path.exists(f"{d}/m003/report.json") else None
        fails = [s["id"] for s in rep["submeasures"] if s["status"] == "FAIL"] if rep else None
        div = next((s for s in rep["submeasures"] if s["id"] == "M-003.d-iv"), {}) if rep else {}
        rows.append(dict(arm=arm, clip=c, exit=ex, pass_=out and out.get("pass"), scale_pass=out and out["scale"]["pass"], frames=out and out["scale"].get("frames"),
                         v2=v2, v1=div.get("value"), v1_status=div.get("status"), m003_fails=fails, m003_exit=rep and rep.get("exit_code")))
json.dump(rows, open(sys.argv[1], "w"), indent=1)
f = lambda x: "-" if x is None else f"{x:.4f}"
L = ["| arm | clip | layered frames | d-iv@v2 clip value | v2 ok | d-iv@v1 (whole-frame, gating m003) | v1 status | v1_min over layered | seam share max | A2 seam adherence min | other FAIL ids (m003) | style pass |", "|---|---|---|---|---|---|---|---|---|---|---|---|"]
for r in rows:
    v = r["v2"] or {}
    other = [x for x in (r["m003_fails"] or []) if x != "M-003.d-iv"]
    L.append(f"| {r['arm']} | {r['clip']} | {v.get('layered_frames','-')} | {f(v.get('gate_value'))} | {v.get('ok','n/a (unlayered: v1 rule)')} | {f(r['v1'])} | {r['v1_status']} | {f(v.get('v1_min'))} | {f(v.get('seam_share_max'))} | {f(v.get('seam_adherence_min'))} | {', '.join(other) or '-'} | {r['pass_']} |")
print("\n".join(L))
