"""T-030c: compares the client's interpolated view of its own body with the host's truth at the same host step (M-056 shape, in metres)."""
import csv, json, sys, statistics as st, collections
d = sys.argv[1]
truth = collections.defaultdict(dict)
for r in csv.DictReader(open(f"{d}/host_truth.csv")):
    truth[int(r["slot"])][int(r["step"])] = (float(r["x"]), float(r["z"]), float(r["speed"]))
errs = {1: [], 2: []}; stale = 0; rows = 0
for r in csv.DictReader(open(f"{d}/client_view.csv")):
    ph = int(r["phase"]); rs = float(r["renderHostStep"]); x = float(r["x"]); z = float(r["z"]); rows += 1
    stale += int(r["stale"])
    t = truth[1]
    a = int(rs)
    if a not in t or a + 1 not in t: continue
    f = rs - a
    tx = t[a][0] * (1 - f) + t[a + 1][0] * f; tz = t[a][1] * (1 - f) + t[a + 1][1] * f
    errs[ph].append(((x - tx) ** 2 + (z - tz) ** 2) ** 0.5)
def pct(v, q): v = sorted(v); return v[min(len(v) - 1, int(q * len(v)))] if v else float("nan")
host = json.load(open(f"{d}/host_summary.json")); cl = json.load(open(f"{d}/client_summary.json"))
print("two-process co-op run (host and client are separate OS processes, real UDP on 127.0.0.1)")
print(f"host: steps {host['steps']}, peak slots {host['peakOccupiedSlots']}, final {host['finalOccupiedSlots']}, accepts {host['accepts']}, refuses {host['refuses']}, slot releases {host['slotReleases']}, late>5ms steps {host['stepsLateOver5ms']}, max late {host['maxLateMs']} ms")
print(f"client: slot phase1 {cl['slotPhase1']}, phase2 {cl['slotPhase2']}, same slot on rejoin: {cl['sameSlotOnRejoin']}, late>5ms {cl['stepsLateOver5ms']}, max late {cl['maxLateMs']} ms")
print(f"client view rows {rows}, stale rows {stale}")
for ph in (1, 2):
    e = errs[ph]
    if e: print(f"phase {ph}: remote-body error vs host truth (render-time matched): n={len(e)} p50 {pct(e,.5):.3f} m, p95 {pct(e,.95):.3f} m, max {max(e):.3f} m")
