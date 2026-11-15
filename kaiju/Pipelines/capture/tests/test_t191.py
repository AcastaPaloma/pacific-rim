"""T-191: M-004 motion-set lint (L17), HUD composite, shots.json, motion-set discovery, matrix selection filters."""
import copy
import json
import os

import numpy as np
import pytest
from PIL import Image

from capture_tools import hud, lint, matrix, motion, shots

ROOT = os.path.join(os.path.dirname(__file__), "..", "..", "..")
DOCS = os.path.join(ROOT, "docs", "store")


def motion_path():
    return json.load(open(os.path.join(DOCS, "paths_motion", "m004_v4_slate_teal.json")))


def test_shipped_motion_and_hud_paths_lint_clean():
    for d, n in (("paths_motion", 6), ("paths_hud", 3), ("paths", 9)):
        r = lint.run(os.path.join(DOCS, d))
        assert r["pass"] and r["files"] == n, (d, [x for x in r["rows"] if not x["ok"]])


@pytest.mark.parametrize("edit", [
    lambda d: d["motion_set"].update(classes=["S0", "T9"]),
    lambda d: d["motion_set"].update(classes=[]),
    lambda d: d["motion_set"].update(classes=["S0", "S0"]),
    lambda d: d.update(purpose="M-005"),
    lambda d: d.update(duration_s=2.0, keyframes=[dict(d["keyframes"][0], t_s=0), dict(d["keyframes"][1], t_s=2.0)]),   # 120 frames < 180
    lambda d: d.update(mode="still", still_frames=[0]),
    lambda d: d.pop("motion_set"),
    lambda d: (d.pop("purpose"), None)[1],   # motion_set without the purpose
])
def test_l17_fires(edit):
    d = copy.deepcopy(motion_path())
    edit(d)
    assert any(e.startswith("L17") for e in lint.lint_path(d)), lint.lint_path(d)


def test_motion_class_table_matches_registry():
    import re
    reg = open(os.path.join(ROOT, "Tools", "Metrics", "registry", "M-004@v1.1.yaml")).read()
    for cid, (typ, sp, _) in lint.MOTION_CLASSES.items():
        m = re.search(r"%s: \{type: (\w+), speed: ([0-9.]+)\}" % cid, reg)
        assert m and m.group(1) == typ and float(m.group(2)) == sp, cid


def test_motion_sets_found_by_purpose(tmp_path):
    for d, purpose in (("a", "M-004"), ("b", "M-003")):
        s = tmp_path / d / "V4_T1"
        s.mkdir(parents=True)
        (s / "sequence.json").write_text(json.dumps({"purpose": purpose}))
    assert [os.path.basename(x) for x in motion.motion_sets(str(tmp_path))] == ["a"]


def test_hud_lines_follow_the_game_strings():
    ln = hud.hud_lines({"ammo": 14, "kaiju_hp_frac": 0.62, "shots": 6, "hits_kaiju": 5, "hits_building": 1, "kaiju_state": "Charge", "kaiju_distance_m": 88, "mech_hp": 0.8})
    assert ln[0].startswith("Kaiju feel check (TEMPORARY") and "60 fps" in ln[0]
    assert "H hangar (repair + redeploy)" in ln[1] and "kaiju Charge d=88 m   mech hp 0.80" in ln[1]
    assert ln[2] == "ammo 14/20   kaiju hp 62%   shots 6 hits k5/b1   hangar cycles 0"
    assert hud.help_text() in ln[1]


def test_hud_composite_changes_only_the_hud_corner(tmp_path):
    src = tmp_path / "set" / "p" / "x4"
    src.mkdir(parents=True)
    base = np.full((1080, 1920, 3), (30, 60, 90), np.uint8)
    Image.fromarray(base).save(src / "f000000_color_output.png")
    scenes = tmp_path / "s.json"
    scenes.write_text(json.dumps({"font_px": 12, "scenes": [{"id": "h1", "path_id": "p", "frame_index": 0, "values": {}}]}))
    r = hud.run(str(tmp_path / "set"), str(scenes), str(tmp_path / "out"))
    assert r["pass"]
    out = np.asarray(Image.open(tmp_path / "out" / "h1.png"))
    diff = np.argwhere((out != base).any(axis=2))
    assert len(diff) > 100 and diff[:, 0].max() < 90 and diff[:, 1].max() < 1200   # text sits in the top-left box only
    side = json.load(open(tmp_path / "out" / "h1.json"))
    assert side["composite"] is True and side["live_imgui"] is False and len(side["source_sha256"]) == 64


def test_shots_json_from_the_shipped_plan(tmp_path):
    geo = os.path.join(ROOT, "Assets", "StreamingAssets", "Geo", "tiles", "v1", "L0")
    r = shots.run(os.path.join(DOCS, "capture_plan.md"), os.path.join(DOCS, "paths"), geo, str(tmp_path / "shots.json"),
                  [os.path.join(DOCS, "paths_hud"), os.path.join(DOCS, "paths_motion")])
    assert r["shots"] == 22 and sum(r["tally"].values()) == 22
    j = json.load(open(tmp_path / "shots.json"))
    by = {s["id"]: s for s in j["shots"]}
    assert by["P1-a"]["status"] == "BLOCKED" and "B-kaiju" in by["P1-a"]["blockers"]
    assert by["P4-d"]["status"] == "NOW_RUNNABLE" and by["P4-b"]["status"] == "NOW_UNPATHED"
    assert len(by["P4-a"]["paths"]) == 2
    assert len(j["extra_paths"]) == 9


def test_matrix_selection_filters(tmp_path):
    import cv2
    rng = np.random.default_rng(5)
    for i in range(4):
        a = (rng.integers(0, 2, (16, 32, 1)) * np.array([[[200, 40, 40]]])).astype(np.uint8) + 20
        cv2.imwrite(str(tmp_path / f"f{i}_color_internal.png"), np.ascontiguousarray(a[..., ::-1]))
    r = matrix.run(str(tmp_path), 4, scales=(1, 2), pix_fmts=("yuv420p", "yuv444p"), crfs=(10, 23), tunes=("none",), preset="ultrafast", select_pix_fmt="yuv444p", select_scale=2)
    assert r["select_pix_fmt"] == "yuv444p" and set(r["by_pix_fmt"]) == {"yuv420p", "yuv444p"} and len(r["cells"]) == 8
    if r["selected"]:
        assert r["selected"]["pix_fmt"] == "yuv444p" and r["selected"]["scale"] == 2
        s = matrix.settings_json(r)
        assert s["schema"] == "encode_settings@v1" and s["master_scale"] == 2 and s["estimated_bitrate_kbps_on_matrix_frames"] > 0
