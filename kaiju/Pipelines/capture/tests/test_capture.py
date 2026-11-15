import json
import os
import shutil
import subprocess
import sys

import numpy as np
import pytest
from PIL import Image

from capture_tools import encode, fidelity, scale, sheets, spec, style
from capture_tools._metrics import METRICS_DIR

sys.path.insert(0, os.path.join(METRICS_DIR, "fixtures"))
sys.path.insert(0, METRICS_DIR)
FFMPEG = shutil.which("ffmpeg") and shutil.which("ffprobe")
PAL = np.array([[20, 40, 60], [200, 60, 40], [40, 200, 90], [240, 220, 40]], np.uint8)


def dither(w=48, h=27, seed=0):
    """Bayer-like 2-colour checker mix on saturated colours: worst case for chroma subsampling."""
    y, x = np.mgrid[0:h, 0:w]
    idx = ((x + y) % 2) + 2 * ((x // 8 + y // 8) % 2)
    return PAL[idx]


# ---- scale
def test_upscale_nn_preserves_pixels_and_blocks():
    a = dither()
    o = scale.upscale_nn(a, 4)
    assert o.shape == (108, 192, 3) and scale.blocks_uniform(o, 4) == 1.0
    assert (o[::4, ::4] == a).all() and scale.matches_nn_of(o, a, 4) == 1.0


def test_bilinear_is_detected():
    import cv2
    a = dither()
    b = cv2.resize(a, (192, 108), interpolation=cv2.INTER_LINEAR)
    assert scale.matches_nn_of(b, a, 4) < 0.9 and scale.blocks_uniform(b, 4) < 0.9


def test_offset_blocks():
    a = dither()
    o = np.roll(scale.upscale_nn(a, 4), (1, 2), (0, 1))
    assert scale.blocks_uniform(o, 4, (2, 1)) == 1.0 and scale.matches_nn_of(o, a, 4, (2, 1)) == 1.0


@pytest.mark.parametrize("i,o,ok", [((480, 270), (1920, 1080), True), ((480, 270), (3840, 2160), True),
                                    ((320, 180), (1920, 1080), True), ((400, 270), (1920, 1080), False),
                                    ((480, 270), (1280, 720), False)])
def test_resolution_rules(i, o, ok):
    assert (scale.check_resolution(i, o) == []) == ok


def test_scale_for_and_bad_scale():
    assert scale.scale_for((480, 270), (1920, 1080)) == 4 and scale.scale_for((100, 50), (250, 100)) is None
    with pytest.raises(ValueError):
        scale.upscale_nn(dither(), 0)


# ---- M-122
def _img(p, wh, mode="RGB"):
    os.makedirs(os.path.dirname(p), exist_ok=True)
    Image.new(mode, wh, (10, 20, 30, 255) if mode == "RGBA" else (10, 20, 30)).save(p)


def make_assets(d, nshots=8, shot_wh=(1920, 1080)):
    for n, r in spec.ASSETS.items():
        if n == "library_logo":
            _img(f"{d}/{n}.png", (1280, 400), "RGBA")
        else:
            _img(f"{d}/{n}.{'jpg' if n == 'client_icon' else 'png'}", r["sizes"][0])
    for i in range(nshots):
        _img(f"{d}/screenshots/s{i}.png", shot_wh)


def test_spec_pass(tmp_path):
    make_assets(str(tmp_path))
    r = spec.run(str(tmp_path))
    assert r["pass"], [x for x in r["rows"] if not x["ok"]]


def test_spec_failures(tmp_path):
    make_assets(str(tmp_path), nshots=4)
    _img(str(tmp_path / "header_capsule.png"), (900, 430))
    _img(str(tmp_path / "library_logo.png"), (1280, 400), "RGB")
    _img(str(tmp_path / "screenshots/s0.png"), (1920, 1000))
    bad = {x["name"] for x in spec.run(str(tmp_path))["rows"] if not x["ok"]}
    assert {"header_capsule", "library_logo", "screenshots_count", "screenshots_size"} <= bad


def test_spec_missing_required(tmp_path):
    r = spec.run(str(tmp_path))
    assert not r["pass"] and any(x["name"] == "main_capsule" and x["detail"] == "missing" for x in r["rows"])


def _probe(codec="h264", w=1920, h=1080, fps="60/1", kbps=8000, ac="aac", sr="48000", fmt="mov,mp4,m4a"):
    return {"format": {"format_name": fmt}, "streams": [
        {"codec_type": "video", "codec_name": codec, "width": w, "height": h, "avg_frame_rate": fps, "bit_rate": str(kbps * 1000)},
        {"codec_type": "audio", "codec_name": ac, "sample_rate": sr}]}


def test_trailer_probe_rules():
    ok = lambda **k: all(r["ok"] for r in spec.check_trailer_probe(_probe(**k)))
    assert ok() and ok(fps="60000/1001") and ok(fps="30/1")
    assert not ok(codec="hevc") and not ok(w=3840, h=2160) and not ok(fps="24/1") and not ok(kbps=3000)
    assert not ok(ac="mp3") and not ok(sr="22050") and not ok(w=1920, h=1000)


# ---- M-124
def test_fidelity_identical_passes():
    a = dither(96, 54)
    r = fidelity.evaluate(((i, a, a.copy()) for i in range(60)))
    assert r["pass"] and r["summary"]["min_share"] == 1.0


def test_fidelity_too_few_frames_and_shift_fail():
    a = dither(96, 54)
    assert not fidelity.evaluate([(0, a, a)])["pass"]
    shifted = np.clip(a.astype(int) + 12, 0, 255).astype(np.uint8)
    assert not fidelity.evaluate(((i, a, shifted) for i in range(60)))["pass"]


def test_sample_indices_include_cuts():
    s = fidelity.sample_indices(100, [7, 95, 500])
    assert 0 in s and 10 in s and 7 in s and 95 in s and 500 not in s


def test_encode_command_contents():
    c = encode.ffmpeg_command("f", "o.mp4", audio="a.wav", crf=10, scale_to=(1920, 1080))
    j = " ".join(c)
    assert "flags=neighbor" in j and "-tune animation" in j and "bt709" in j and "+faststart" in j and "-crf 10" in j
    assert j.index("flags=neighbor") < j.index("format=yuv420p")
    assert "-tune" not in encode.ffmpeg_command("f", "o.mp4", tune="none")


def test_encode_dry_run():
    assert encode.encode("f", "o.mp4", dry_run=True)["ran"] is False


def _write_frames(d, n=3, scale_=1):
    os.makedirs(d, exist_ok=True)
    a = scale.upscale_nn(dither(64, 36), scale_)
    for i in range(n):
        Image.fromarray(a).save(f"{d}/{i + 1:05d}.png")
    return a


@pytest.mark.skipif(not FFMPEG, reason="ffmpeg not installed")
def test_yuv420p_1x_dither_fails_and_lossless_444_passes(tmp_path):
    a = _write_frames(str(tmp_path / "m"))
    bad, good = str(tmp_path / "bad.mp4"), str(tmp_path / "good.mp4")
    encode.encode(str(tmp_path / "m"), bad, fps=30, crf=14)
    encode.encode(str(tmp_path / "m"), good, fps=30, crf=0, pix_fmt="yuv444p", tune="none")
    for v, expect in ((bad, False), (good, True)):
        dec = fidelity.decode_frames(v, [0, 1, 2], 64, 36)
        r = fidelity.evaluate(((i, a, dec[i]) for i in range(3)), min_frames=3)
        assert r["pass"] == expect, (v, r["summary"])


@pytest.mark.skipif(not FFMPEG, reason="ffmpeg not installed")
def test_fidelity_run_end_to_end_and_trailer_probe(tmp_path):
    _write_frames(str(tmp_path / "m"))
    v = str(tmp_path / "v.mp4")
    encode.encode(str(tmp_path / "m"), v, fps=30, crf=0, pix_fmt="yuv444p", tune="none")
    r = fidelity.run(str(tmp_path / "m"), v, every=1)
    assert r["summary"]["frames"] == 3 and r["summary"]["min_share"] >= 0.99
    rows = spec.check_trailer_probe(spec.probe_trailer(v))
    assert {x["name"]: x["ok"] for x in rows}["trailer_codec"] is True
    assert {x["name"]: x["ok"] for x in rows}["trailer_audio"] is False  # no audio track


def test_upscale_dir_lossless(tmp_path):
    _write_frames(str(tmp_path / "s"), 2)
    assert encode.upscale_dir(str(tmp_path / "s"), str(tmp_path / "d"), 4) == 2
    o = np.array(Image.open(tmp_path / "d" / "00001.png"))
    assert o.shape == (144, 256, 3) and (o[::4, ::4] == dither(64, 36)).all() and scale.blocks_uniform(o, 4) == 1.0


# ---- style (wraps Tools/Metrics)
def test_scale_check_set_pass_and_bilinear_fail(tmp_path):
    import gen_style as G
    prof = "slate_teal"
    G.m003_gameplay_frames(str(tmp_path / "ok"), profile=prof, n_frames=2)
    r = style.scale_check_set(str(tmp_path / "ok"))
    assert r["frames"] == 2 and r["pass"] is False  # 640x360 output is not a store target (Q-234)
    assert style.scale_check_set(str(tmp_path / "ok"), gameplay=False)["pass"]
    G.m003_gameplay_frames(str(tmp_path / "bil"), profile=prof, n_frames=2, upscale_mode="bilinear")
    r = style.scale_check_set(str(tmp_path / "bil"), gameplay=False)
    assert not r["pass"] and "stretched" in r["rows"][0]["errors"][0]


def test_metrics_wrapper_runs_m003(tmp_path):
    import gen_style as G
    sd, _ = G.m003_pass_set(str(tmp_path))
    r = style.run_metric("m003", sd, str(tmp_path / "out"))
    assert r["verdict"] in ("PASS", "FAIL", "INCOMPLETE") and r["report"] is not None, r["tail"]


# ---- sheets
def test_contact_sheet_nn_and_coverage(tmp_path):
    for n in ("P1-a.png", "P2-a.png", "P2-b.png", "x.png"):
        Image.fromarray(scale.upscale_nn(dither(), 4)).save(tmp_path / n)
    paths = sorted(str(p) for p in tmp_path.glob("*.png"))
    r = sheets.contact_sheet(paths, str(tmp_path / "sheet.out.png"), cols=2, thumb_w=96, labels=False)
    assert r["frames"] == 4
    sh = np.array(Image.open(r["out"]))
    cols = {tuple(c) for c in sh.reshape(-1, 3)}
    assert len(cols) <= len(PAL) + 1  # background; no interpolated colours beyond that
    cv = sheets.coverage(paths)
    assert cv["per_pillar"]["P2"] == 2 and "P3" in cv["missing_pillars"] and cv["unassigned"] == 1


def test_cli_spec_exit_codes(tmp_path):
    make_assets(str(tmp_path))
    cli = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "capture.py")
    assert subprocess.run([sys.executable, cli, "spec", "--assets", str(tmp_path)], capture_output=True).returncode == 0
    assert subprocess.run([sys.executable, cli, "spec", "--assets", str(tmp_path / "none")], capture_output=True).returncode == 2
