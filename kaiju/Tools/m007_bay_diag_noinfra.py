"""T-113b DIAGNOSTIC ONLY (not the M-007 evaluator, never a gate): Tools/m007_bay.py with infrastructure ids removed from the decision-row
`target` field (treated as no target), so FF (C2) and Engaged (P1/P6) count mech targets only, as R-024 3.1-3.2 define them.
Used to attribute the C2-LL and P1-LL failures to the evaluator counting a raiding pack's infrastructure target as a combat target.
Usage: m007_bay_diag_noinfra.py <log dirs...> --out report.json [--jobs 12]"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import m007_bay as M  # noqa: E402
import m007_bay_par as P  # noqa: E402


def _one(arg):
    f, _rev = arg
    r = M.load_run(f)
    infra = {M.eid(x["ent"]) for x in r.infra}
    for x in r.dec:
        if M.eid(x["target"]) in infra:
            x["target"] = ""
    return M.compute_run(r, dict(M.CFG, rev=1))


if __name__ == "__main__":
    P._one = _one
    sys.exit(P.main(sys.argv[1:]))
