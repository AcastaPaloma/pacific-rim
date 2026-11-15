"""Re-uses the negative-control definitions of the test-suite for the evidence builder (single source of truth)."""
import os
import sys

TESTS = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "tests")
if TESTS not in sys.path:
    sys.path.insert(0, TESTS)


def NEG001():
    import test_m001 as T
    return {sid: T._neg(sid) for sid in sorted(T.NEG, key=lambda s: int(s.split(".")[1]))}


def NEG002():
    import test_m002 as T
    from fixtures import gen_gait as G
    out = {}
    for sid, o in T.NEG.items():
        o = dict(o)
        if sid == "M-002.3":
            sp = G.spec_walk(0.2, strides=5)
            sp.dt = 0.025
            o["walk_fr020"] = sp
        out[sid] = o
    return out


def m001_rat(path, build):
    from gaitkit import m001_ratings
    return m001_ratings(path, build)
