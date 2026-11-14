"""Tile indexing and ownership (R-004 §3.1; T-003 C16)."""
import pytest

from kaiju_geo.common import Tile, parse_tile, tile_of


def test_m0_tile_index_and_bounds():
    t = tile_of(62375.6, 74051.1)
    assert t.id == "L0_30_36"
    assert t.bounds == (61440.0, 73728.0, 63488.0, 75776.0)
    assert t.folder == "L0/30_36"


def test_m0_wgs84_corners_match_r004():
    w, s, e, n = Tile(0, 30, 36).bounds_lonlat()
    assert abs(w - -122.407558) < 2e-6 and abs(s - 37.786854) < 2e-6
    assert abs(e - -122.384281) < 2e-6 and abs(n - 37.805325) < 2e-6


@pytest.mark.parametrize("x,z,tid", [(61440.0, 73728.0, "L0_30_36"), (63487.999, 75775.999, "L0_30_36"),
                                     (63488.0, 75000.0, "L0_31_36"), (61439.999, 75000.0, "L0_29_36"),
                                     (62000.0, 73727.9, "L0_30_35")])
def test_half_open_ownership(x, z, tid):
    t = tile_of(x, z)
    assert t.id == tid and t.contains(x, z)
    for di in (-1, 0, 1):
        for dj in (-1, 0, 1):
            if di or dj:
                assert not Tile(0, t.i + di, t.j + dj).contains(x, z)


def test_samples_and_spacing_per_level():
    for lvl, sp in [(0, 1.0), (1, 2.0), (2, 4.0), (4, 16.0)]:
        t = Tile(lvl, 1, 1)
        assert t.spacing == sp and t.size == 2048.0 * 2 ** lvl


def test_origins_are_exact_multiples_of_2048():
    for i in range(0, 68):
        t = Tile(0, i, 67 - i)
        assert t.x0 % 2048 == 0 and t.z0 % 2048 == 0
        assert float(t.x0) == float(int(t.x0))


def test_world_box_fits_68_by_68_grid():
    from kaiju_geo.common import lonlat_to_baytm
    for lon, lat in [(-123.05, 37.15), (-121.55, 38.35), (-123.05, 38.35), (-121.55, 37.15)]:
        t = tile_of(*lonlat_to_baytm(lon, lat))
        assert 0 <= t.i <= 67 and 0 <= t.j <= 67


def test_parse_tile_roundtrip():
    assert parse_tile("L0_30_36") == Tile(0, 30, 36)
    assert parse_tile("L2_03_07").id == "L2_03_07"
    with pytest.raises(SystemExit):
        parse_tile("30_36")
