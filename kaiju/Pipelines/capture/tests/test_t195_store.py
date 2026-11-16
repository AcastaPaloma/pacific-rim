import hashlib
import json
import os

from PIL import Image

from capture_tools import spec, store

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
SRC = os.path.join(ROOT, "docs", "store", "sources")
OUT = os.path.join(ROOT, "docs", "store", "assets")


def test_m122_spec_passes_on_committed_assets():
    rep = spec.run(OUT)
    assert rep["pass"], [r for r in rep["rows"] if not r["ok"]]


def test_exact_dimensions_and_formats():
    for n, (w, h) in store.CAPS.items():
        assert Image.open(os.path.join(OUT, n + ".png")).size == (w, h)
    assert Image.open(os.path.join(OUT, "library_hero.png")).size == (3840, 1240)
    lg = Image.open(os.path.join(OUT, "library_logo.png"))
    assert lg.width == 1280 and lg.mode == "RGBA" and lg.getchannel("A").getextrema()[0] == 0
    ci = Image.open(os.path.join(OUT, "client_icon.jpg"))
    assert ci.size == (184, 184) and ci.format == "JPEG"
    assert Image.open(os.path.join(OUT, "community_icon.png")).size == (256, 256)
    assert Image.open(os.path.join(OUT, "shortcut_icon.png")).size == (256, 256)


def test_file_sizes_slim():
    for dp, _, fs in os.walk(OUT):
        for f in fs:
            assert os.path.getsize(os.path.join(dp, f)) < store.MAX_BYTES, f
    assert sum(os.path.getsize(os.path.join(dp, f)) for dp, _, fs in os.walk(OUT) for f in fs) < 1_000_000


def test_safe_zones_and_small_capsule_fill():
    rows = store.safe_zone_report(OUT)
    assert all(r[1] for r in rows), rows
    small = [r for r in rows if r[0] == "small_capsule"][0]
    assert small[3] >= 0.7  # logo nearly fills the small capsule


def test_hero_has_no_logo_pixels_and_logo_pixels_only_palette():
    hero = Image.open(os.path.join(OUT, "library_hero.png")).convert("RGB")
    cols = {c for _, c in hero.getcolors(1 << 24)}
    assert store.FILL[:3] not in cols and store.EDGE[:3] not in cols
    lg = Image.open(os.path.join(OUT, "library_logo.png"))
    assert {c for _, c in lg.getcolors(1 << 24) if c[3]} <= {store.FILL, store.EDGE}


def test_integer_scale_nearest_no_resampling():
    # hero is an integer nearest upscale of the source: few colours, same as source palette
    src = {c for _, c in store.load_src(SRC, store.HERO_SRC).getcolors(1 << 20)}
    hero = {c for _, c in Image.open(os.path.join(OUT, "library_hero.png")).convert("RGB").getcolors(1 << 24)}
    assert hero <= src


def test_build_reproducible_and_matches_committed(tmp_path):
    man = store.build(SRC, str(tmp_path))
    committed = json.load(open(os.path.join(OUT, "manifest.json")))
    assert man["assets"] == committed["assets"]


def test_manifest_flags_temporary_and_no_franchise_terms():
    m = json.load(open(os.path.join(OUT, "manifest.json")))
    assert m["status"] == "TEMPORARY" and "TEMPORARY" in m["title_note"]
    assert all("TEMPORARY" in a["status"] or "pending" in a["status"] or "engine capture" in a["status"] for a in m["assets"].values())
    assert not any(t in json.dumps(m).lower() for t in ("pacific rim", "jaeger", "godzilla", "gundam"))
    assert store.TITLE.lower() not in ("pacific rim",)


def test_disclosure_draft_mentions_store_assets():
    t = open(os.path.join(ROOT, "docs", "store", "ai_disclosure_draft.md"), encoding="utf-8").read()
    assert "Store assets (T-195)" in t
