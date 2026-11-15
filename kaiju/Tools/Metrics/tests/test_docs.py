"""C18: every command in README's quick-start block runs from the project root with the stated exit code;
README, MODULE.md and SPEC_READINGS.md exist."""
import os
import re
import shlex
import subprocess
import sys

from conftest import METRICS, PROJECT


def _quick_start():
    txt = open(os.path.join(METRICS, "README.md")).read()
    block = txt.split("## Quick start", 1)[1].split("```sh", 1)[1].split("```", 1)[0]
    cmds = []
    for line in block.strip().splitlines():
        m = re.match(r"^\$PY (.*?)\s+# exit (\d+)\s*$", line)
        assert m, line
        cmds.append((m.group(1), int(m.group(2))))
    return cmds


def test_docs_exist():
    for f in ("README.md", "MODULE.md", "SPEC_READINGS.md"):
        assert os.path.getsize(os.path.join(METRICS, f)) > 1000


def test_readme_quick_start_commands(tmp_path):
    cmds = _quick_start()
    assert len(cmds) >= 12
    for args, code in cmds:
        argv = [sys.executable] + shlex.split(args.replace("$TMP", str(tmp_path)))
        r = subprocess.run(argv, capture_output=True, text=True, cwd=PROJECT, env=dict(os.environ, MPLBACKEND="Agg"))
        assert r.returncode == code, (args, r.returncode, r.stdout[-600:], r.stderr[-600:])
        assert "Traceback" not in r.stderr, args
