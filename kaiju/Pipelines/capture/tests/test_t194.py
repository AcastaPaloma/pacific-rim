"""T-194: cross-process determinism comparison tool."""
import os

from capture_tools import xproc


def seq(root, name, files):
    d = os.path.join(root, name)
    os.makedirs(d)
    with open(os.path.join(d, "hashes.sha256"), "w") as fh:
        for n, h in files.items():
            fh.write("%s  %s\n" % (h, n))


def test_identical_runs_pass(tmp_path):
    f = {"f000000_color_internal.png": "aa", "f000000_index.png": "bb"}
    seq(str(tmp_path / "a"), "p/x4", f)
    seq(str(tmp_path / "b"), "p/x4", dict(f))
    r = xproc.run(str(tmp_path / "a"), str(tmp_path / "b"))
    assert r["pass"] and r["files"] == 2 and r["rows"] == []


def test_difference_names_frame_and_buffer(tmp_path):
    seq(str(tmp_path / "a"), "p/x4", {"f000002_color_internal.png": "aa", "f000002_index.png": "bb", "f000003_depth.png": "cc"})
    seq(str(tmp_path / "b"), "p/x4", {"f000002_color_internal.png": "aa", "f000002_index.png": "XX", "f000003_depth.png": "cc"})
    r = xproc.run(str(tmp_path / "a"), str(tmp_path / "b"))
    assert not r["pass"] and r["differing_files"] == 1
    assert r["rows"][0]["frames"] == ["f000002"] and r["rows"][0]["buffers"] == {"index": 1}


def test_missing_sequence_fails(tmp_path):
    seq(str(tmp_path / "a"), "p/x4", {"f000000_index.png": "aa"})
    os.makedirs(tmp_path / "b")
    assert not xproc.run(str(tmp_path / "a"), str(tmp_path / "b"))["pass"]
