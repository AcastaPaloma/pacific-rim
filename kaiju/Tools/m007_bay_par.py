"""T-113b: parallel driver for Tools/m007_bay.py (no metric logic here). compute_run per log file in a process pool, then m007_bay.evaluate,
exactly as m007_bay.main does serially. Usage: m007_bay_par.py <log dirs...> --out report.json [--jobs 12]"""
import json
import math
import os
import sys
from multiprocessing import Pool

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import m007_bay as M  # noqa: E402


def _one(f):
    return M.compute_run(M.load_run(f), M.CFG)


def main(argv):
    out, jobs, ins = None, 12, []
    i = 0
    while i < len(argv):
        if argv[i] == "--out":
            out = argv[i + 1]; i += 2
        elif argv[i] == "--jobs":
            jobs = int(argv[i + 1]); i += 2
        else:
            ins.append(argv[i]); i += 1
    files = M.expand(ins)
    with Pool(jobs) as p:
        prims = p.map(_one, files, chunksize=2)
    rep = M.evaluate([], M.CFG, None, prims=prims)
    text = json.dumps(rep, indent=1, default=lambda o: None if isinstance(o, float) and math.isnan(o) else str(o))
    if out:
        open(out, "w").write(text)
    print(json.dumps(rep["summary"]))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
