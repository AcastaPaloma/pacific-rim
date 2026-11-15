#!/usr/bin/env python3
"""T-143: render the SteamPipe VDF templates. Fills ${PLACEHOLDERS} from --set KEY=VALUE or the environment and refuses
to write anything if a placeholder is unset or a value is empty/unsafe. This script never uploads: it imports no
network or process modules and only writes two small text files per platform build.

Required values: APP_ID, DEPOT_ID_MAC, DEPOT_ID_WIN (positive integers from Steamworks), BUILD_DESC, BUILD_OUTPUT,
CONTENT_ROOT. Outputs in --out-dir: app_build_<APP_ID>.vdf, depot_build_<DEPOT_ID_MAC>.vdf, depot_build_<DEPOT_ID_WIN>.vdf.

Usage:  python -I render_vdf.py --out-dir <dir> [--set APP_ID=480 ...]   (anything not given is read from the environment)
"""
import argparse
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PLACEHOLDER = re.compile(r"\$\{([A-Z0-9_]+)\}")
REQUIRED = ["APP_ID", "DEPOT_ID_MAC", "DEPOT_ID_WIN", "BUILD_DESC", "BUILD_OUTPUT", "CONTENT_ROOT"]
NUMERIC = ["APP_ID", "DEPOT_ID_MAC", "DEPOT_ID_WIN"]


class RenderError(Exception):
    pass


def placeholders(text):
    return sorted(set(PLACEHOLDER.findall(text)))


def fill(text, values):
    missing = [k for k in placeholders(text) if k not in values]
    if missing:
        raise RenderError("unset placeholder(s): " + ", ".join(missing))
    return PLACEHOLDER.sub(lambda m: values[m.group(1)], text)


def validate(values):
    for k in REQUIRED:
        v = values.get(k)
        if v is None or not v.strip():
            raise RenderError("%s is unset or empty" % k)
        if any(c in v for c in '"\n\r\t\\'):
            raise RenderError("%s contains a quote, backslash, tab or newline (unsafe in VDF)" % k)
    for k in NUMERIC:
        if not re.fullmatch(r"[1-9][0-9]*", values[k]):
            raise RenderError("%s must be a positive integer (got %r)" % (k, values[k]))
    if len({values[k] for k in NUMERIC}) != len(NUMERIC):
        raise RenderError("APP_ID, DEPOT_ID_MAC and DEPOT_ID_WIN must all differ")


def render(values, template_dir=HERE):
    """Return {filename: text}. Raises RenderError before producing anything if inputs are incomplete."""
    validate(values)
    with open(os.path.join(template_dir, "app_build.vdf.template"), encoding="utf-8") as f:
        app_t = f.read()
    with open(os.path.join(template_dir, "depot_build.vdf.template"), encoding="utf-8") as f:
        depot_t = f.read()
    out = {"app_build_%s.vdf" % values["APP_ID"]: fill(app_t, values)}
    for plat, key in (("mac", "DEPOT_ID_MAC"), ("win", "DEPOT_ID_WIN")):
        v = dict(values, DEPOT_ID=values[key], DEPOT_SUBDIR=plat)
        out["depot_build_%s.vdf" % values[key]] = fill(depot_t, v)
    for name, text in out.items():
        left = placeholders(text)
        if left:
            raise RenderError("%s still has placeholder(s): %s" % (name, ", ".join(left)))
    return out


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--out-dir", required=True)
    ap.add_argument("--set", action="append", default=[], metavar="KEY=VALUE")
    a = ap.parse_args(argv)
    values = {k: os.environ[k] for k in REQUIRED if k in os.environ}
    for kv in a.set:
        k, sep, v = kv.partition("=")
        if not sep or k not in REQUIRED:
            print("RENDER FAIL: --set expects KEY=VALUE with KEY in %s (got %r)" % (", ".join(REQUIRED), kv), file=sys.stderr)
            return 2
        values[k] = v
    try:
        files = render(values)
    except (RenderError, OSError) as e:
        print("RENDER FAIL: %s" % e, file=sys.stderr)
        return 1
    os.makedirs(a.out_dir, exist_ok=True)
    for name, text in sorted(files.items()):
        with open(os.path.join(a.out_dir, name), "w", encoding="utf-8", newline="\n") as f:
            f.write(text)
        print("wrote " + os.path.join(a.out_dir, name))
    print("Nothing was uploaded. See README.md for the founder-run upload step.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
