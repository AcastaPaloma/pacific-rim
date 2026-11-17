import json
import os
import stat
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
import smoke  # noqa: E402

GOOD = {"tiles": 15, "buildings_registered": 4000, "load_seconds": 3.2}


def test_evaluate_pass():
    assert smoke.evaluate(0, "x [SliceSmoke] {...}", GOOD) == []


def test_evaluate_failures():
    f = smoke.evaluate(3, "[Slice] Core is not booted", None)
    joined = " ".join(f)
    assert "exit code 3" in joined and "Core is not booted" in joined and "smoke JSON missing" in joined and "[SliceSmoke]" in joined
    assert any("no tile" in x for x in smoke.evaluate(0, "[SliceSmoke]", {"tiles": 0, "buildings_registered": 1}))
    assert any("no building" in x for x in smoke.evaluate(0, "[SliceSmoke]", {"tiles": 1, "buildings_registered": 0}))


def fake_app(tmp_path, body):
    app = tmp_path / "G.app"
    macos = app / "Contents" / "MacOS"
    macos.mkdir(parents=True)
    exe = macos / "G"
    exe.write_text("#!/bin/sh\n" + body)
    exe.chmod(exe.stat().st_mode | stat.S_IXUSR)
    return str(app)


SCRIPT = r'''
while [ $# -gt 0 ]; do case "$1" in -sliceSmokeOut) OUT=$2;; -logFile) LOG=$2;; esac; shift; done
echo '[SliceSmoke] {}' > "$LOG"
echo '{"tiles": 15, "buildings_registered": 10}' > "$OUT"
exit %d
'''


def test_run_pass_and_fail(tmp_path):
    r = smoke.run(fake_app(tmp_path, SCRIPT % 0), str(tmp_path / "o1"), timeout=20)
    assert r["pass"] and r["tiles"] == 15
    assert json.load(open(tmp_path / "o1" / "smoke_result.json"))["pass"] is True
    tmp2 = tmp_path / "b"
    tmp2.mkdir()
    r = smoke.run(fake_app(tmp2, SCRIPT % 3), str(tmp_path / "o2"), timeout=20)
    assert not r["pass"] and r["exit_code"] == 3


def test_run_timeout(tmp_path):
    r = smoke.run(fake_app(tmp_path, "sleep 30\n"), str(tmp_path / "o"), timeout=1)
    assert not r["pass"] and "timeout" in str(r["exit_code"])


def test_player_binary_windows_layouts(tmp_path):
    d = tmp_path / "win"
    d.mkdir()
    (d / "UnityCrashHandler64.exe").write_text("x")
    (d / "KaijuGame.exe").write_text("x")
    assert smoke.player_binary(str(d)).endswith("KaijuGame.exe")
    assert smoke.player_binary(str(d / "KaijuGame.exe")).endswith("KaijuGame.exe")
    (d / "Other.exe").write_text("x")
    try:
        smoke.player_binary(str(d))
        assert False, "two exes must be refused"
    except RuntimeError:
        pass
