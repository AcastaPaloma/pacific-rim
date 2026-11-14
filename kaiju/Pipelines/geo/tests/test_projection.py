"""BayTM@v1 projection maths (R-004 §2; T-003 C7)."""
import math

import numpy as np
import pytest
from pyproj import Geod, Proj, Transformer

from kaiju_geo.common import BAYTM_PROJ, baytm_crs, baytm_to_lonlat, lonlat_to_baytm

GEOD = Geod(ellps="GRS80")
# R-004 §5 table: (lat, lon) -> BayTM (x, z) to 0.1 m
REF = {"salesforce": (37.789775, -122.396914, 62375.6, 74051.1),
       "transamerica": (37.795166, -122.402786, 61859.0, 74650.0),
       "w2": (37.790753, -122.385823, 63352.6, 74158.7),
       "coit": (37.802377, -122.405834, 61591.4, 75450.7),
       "gg_south": (37.814014, -122.477891, 55248.0, 76752.0),
       "diablo": (37.881695, -121.914147, 104856.9, 84319.4)}


@pytest.mark.parametrize("name", sorted(REF))
def test_landmarks_match_r004_table(name):
    lat, lon, x, z = REF[name]
    gx, gz = lonlat_to_baytm(lon, lat)
    assert abs(gx - x) <= 0.06 and abs(gz - z) <= 0.06, (name, gx, gz)


def test_round_trip_is_sub_millimetre():
    xs = np.linspace(4300, 137500, 25)
    zs = np.linspace(3300, 136500, 25)
    X, Z = np.meshgrid(xs, zs)
    lon, lat = baytm_to_lonlat(X.ravel(), Z.ravel())
    x2, z2 = lonlat_to_baytm(lon, lat)
    assert np.max(np.hypot(x2 - X.ravel(), z2 - Z.ravel())) < 1e-6


def test_false_origin_puts_tile_corner_on_grid():
    # origin 37.75 N 122.30 W maps to (70912, 69632) = (34.625*2048, 34*2048)
    x, z = lonlat_to_baytm(-122.3, 37.75)
    assert abs(x - 70912) < 1e-6 and abs(z - 69632) < 1e-6
    assert 70912 / 2048 == 34.625 and 69632 / 2048 == 34


def test_point_scale_within_0_to_55_ppm_over_world_box():
    p = Proj(BAYTM_PROJ)
    lons, lats = np.meshgrid(np.linspace(-123.05, -121.55, 31), np.linspace(37.15, 38.35, 25))
    k = p.get_factors(lons.ravel(), lats.ravel()).meridional_scale
    ppm = (np.asarray(k) - 1) * 1e6
    assert ppm.min() >= -0.01 and ppm.max() <= 55.5, (ppm.min(), ppm.max())


@pytest.mark.parametrize("a,b", [("salesforce", "transamerica"), ("gg_south", "diablo"), ("coit", "w2")])
def test_pair_distance_scale_le_60ppm(a, b):
    la, oa, _, _ = REF[a]
    lb, ob, _, _ = REF[b]
    xa, za = lonlat_to_baytm(oa, la)
    xb, zb = lonlat_to_baytm(ob, lb)
    d_game = math.hypot(xb - xa, zb - za)
    d_geo = GEOD.inv(oa, la, ob, lb)[2]
    assert abs(d_game / d_geo - 1) <= 60e-6
    if d_geo < 2000:
        assert abs(d_game - d_geo) <= 0.1


def test_utm_would_fail_the_scale_gate():
    """Guards against the UTM-vs-BayTM confusion (R-004 §2.1)."""
    t = Transformer.from_crs("EPSG:6318", "EPSG:26910", always_xy=True)
    la, oa, _, _ = REF["gg_south"]
    lb, ob, _, _ = REF["diablo"]
    xa, ya = t.transform(oa, la)
    xb, yb = t.transform(ob, lb)
    assert abs(math.hypot(xb - xa, yb - ya) / GEOD.inv(oa, la, ob, lb)[2] - 1) > 150e-6


def test_crs_is_nad83_2011_tm_k1():
    c = baytm_crs()
    assert "NAD83(2011)" in c.geodetic_crs.name
    wkt = c.to_wkt()
    assert "Transverse Mercator" in wkt and "UTM" not in wkt
