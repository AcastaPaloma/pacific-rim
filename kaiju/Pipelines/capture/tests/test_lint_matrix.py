import copy
import json
import os
import shutil

import numpy as np
import pytest

from capture_tools import lint, matrix

PATHS = os.path.join(os.path.dirname(__file__), "..", "..", "..", "docs", "store", "paths")


def good():
    return json.load(open(os.path.join(PATHS, "p4_a_sunrise_v5.json")))


def test_shipped_paths_lint_clean():
    r = lint.run(PATHS)
    assert r["pass"], [x for x in r["rows"] if not x["ok"]]
    assert r["files"] >= 9 and r["runnable"] >= 4 and r["blocked"] >= 3


@pytest.mark.parametrize("edit,rule", [
    (lambda d: d.update(fps=50), "L03"),
    (lambda d: d.update(duration_s=6.003), "L04"),
    (lambda d: d["snap"].update(dither_anchor="screen"), "L07"),
    (lambda d: d["snap"].update(enabled=False), "L07"),
    (lambda d: d["outputs"][0].update(output_wh=[1900, 1080]), "L08"),
    (lambda d: d["outputs"].append({"internal_wh": [480, 270], "output_wh": [3840, 2160], "scale": 8}), "L08"),   # 4k in a clip
    (lambda d: d.update(profile_id="nope"), "L15"),
    (lambda d: d.update(schema="capture_path@v2"), "L01"),
    (lambda d: d.update(path_id="Bad Id"), "L02"),
    (lambda d: d.update(requires=["warp"]), "L11"),
    (lambda d: d["viewpoint"].update(tile="x"), "L14"),
    (lambda d: d["keyframes"][1].update(t_s=5.0), "L05"),
    (lambda d: d["keyframes"][0].update(alt_m=2.0), "L10"),
    (lambda d: d.update(interpolation="catmull_rom"), "L05"),
    (lambda d: d.update(mode="still"), "L09"),
    (lambda d: d.update(seed=-1), "L12"),
    (lambda d: d.update(shake=[{"t_s": 1, "amp_internal_px": 1, "decay_s": 0}]), "L13"),
])
def test_each_rule_fires(edit, rule):
    d = copy.deepcopy(good())
    edit(d)
    errs = lint.lint_path(d)
    assert any(e.startswith(rule) for e in errs), errs


def test_still_with_frames_and_x8_is_clean():
    d = json.load(open(os.path.join(PATHS, "p4_a_still_v5.json")))
    assert lint.lint_path(d) == []


def test_duplicate_path_id_across_files(tmp_path):
    for n in ("a.json", "b.json"):
        shutil.copy(os.path.join(PATHS, "p4_a_sunrise_v5.json"), tmp_path / n)
    r = lint.run(str(tmp_path))
    assert not r["pass"] and any(e.startswith("L16") for row in r["rows"] for e in row["errors"])


def test_matrix_on_synthetic_frames(tmp_path):
    cv2 = pytest.importorskip("cv2")
    if not shutil.which("ffmpeg"):
        pytest.skip("ffmpeg not installed")
    rng = np.random.default_rng(3)
    for i in range(3):
        a = np.zeros((18, 32, 3), np.uint8)
        a[...] = (40, 90, 120)
        a[:, 16:] = (200, 160, 90)
        a[rng.random((18, 32)) < 0.1] = (250, 250, 250)
        cv2.imwrite(str(tmp_path / f"f{i:06d}_color_internal.png"), a[..., ::-1])
    r = matrix.run(str(tmp_path), n_frames=3, scales=(1, 2), pix_fmts=("yuv444p",), crfs=(10,), tunes=("none",), preset="ultrafast")
    assert r["check"] == "matrix" and len(r["cells"]) == 2 and all("bytes" in c for c in r["cells"])
    if r["selected"]:
        assert matrix.settings_json(r)["schema"] == "encode_settings@v1"


def test_shifted_nn_match_is_exact_for_the_present_shift():
    from capture_tools.scale import matches_shifted_nn_of, upscale_nn
    rng = np.random.default_rng(1)
    internal = rng.integers(0, 255, (9, 16, 3), dtype=np.uint8)
    k, sx, sy = 4, 1, -1
    H, W = 9 * k, 16 * k
    ty, tx = (np.arange(H) - sy) // k, (np.arange(W) + sx) // k
    out = internal[np.clip(ty, 0, 8)][:, np.clip(tx, 0, 15)]   # what the present stage shows, guard band clamped here
    assert matches_shifted_nn_of(out, internal, k, (sx, sy)) == 1.0
    assert matches_shifted_nn_of(upscale_nn(internal, k), internal, k, (sx, sy)) < 1.0   # an unshifted image does not match the shifted relation
    bad = out.copy()
    bad[10:20, 10:20] = 0
    assert matches_shifted_nn_of(bad, internal, k, (sx, sy)) < 1.0


def test_style_run_finds_the_path_sets_of_a_tree(tmp_path):
    from capture_tools import style
    for path in ("a", "b"):
        d = tmp_path / path / "x4"
        d.mkdir(parents=True)
        (d / "sequence.json").write_text("{}")
    assert [os.path.basename(s) for s in style._capture_sets(str(tmp_path))] == ["a", "b"]
    assert style._capture_sets(str(tmp_path / "a")) == [str(tmp_path / "a")]
