"""Shared pipeline plumbing: paths, BayTM@v1 CRS, tile indexing, hashing, cached fetch.

Everything here is deterministic. Network access happens only inside `cached_fetch` /
`network_allowed()` guarded blocks; `--offline` (or KAIJU_GEO_OFFLINE=1) turns any socket
connect into a hard error so a second run provably does no network I/O (T-003 C15).
"""
from __future__ import annotations

import datetime as _dt
import hashlib
import json
import math
import os
import socket
import subprocess
import sys
import time
import urllib.error
import urllib.request
from dataclasses import dataclass
from pathlib import Path

# ---------------------------------------------------------------- paths
PIPE = Path(__file__).resolve().parent.parent          # Pipelines/geo
PROJECT = PIPE.parent.parent                            # kaiju/
# git-ignored raw downloads; KAIJU_GEO_CACHE points a task worktree at the integration checkout's cache (T-102b; no symlinks)
CACHE = Path(os.environ["KAIJU_GEO_CACHE"]).resolve() if os.environ.get("KAIJU_GEO_CACHE") else PIPE / "cache"
CONFIG = PIPE / "config"
# Baked, committed geo_tile@v1 output (outside the cache; readable by the Unity importer and
# shipped with players): see README "Where the baked data lives".
OUT_ROOT = PROJECT / "Assets" / "StreamingAssets" / "Geo"
TILES_ROOT = OUT_ROOT / "tiles" / "v1"
GLOBAL_ROOT = OUT_ROOT / "global" / "v1"
CRS_DIR = OUT_ROOT / "crs"

SPEC = "geo_tile@v1"
TILE_SIZE_M = 2048.0
SAMPLES = 2049


def load_config(name: str) -> dict:
    with open(CONFIG / name, "r", encoding="utf-8") as f:
        return json.load(f)


# ---------------------------------------------------------------- CRS (R-004 §2.1)
BAYTM_PROJ = ("+proj=tmerc +lat_0=37.75 +lon_0=-122.3 +k=1 +x_0=70912 +y_0=69632 "
              "+ellps=GRS80 +units=m +no_defs")
BAYTM_NAME = "kaiju BayTM v1 (NAD83(2011))"
BAYTM_ID = "kaiju:BayTM@v1"

_crs_cache: dict = {}


def baytm_crs():
    """Projected BayTM@v1 on NAD83(2011) (EPSG:6318), k = 1 (R-004 §2.1)."""
    if "h" not in _crs_cache:
        from pyproj import CRS
        from pyproj.crs import ProjectedCRS
        from pyproj.crs.coordinate_operation import TransverseMercatorConversion
        conv = TransverseMercatorConversion(
            latitude_natural_origin=37.75, longitude_natural_origin=-122.3,
            false_easting=70912.0, false_northing=69632.0, scale_factor_natural_origin=1.0)
        _crs_cache["h"] = ProjectedCRS(conv, geodetic_crs=CRS("EPSG:6318"), name=BAYTM_NAME)
    return _crs_cache["h"]


def baytm_compound_crs():
    """BayTM@v1 + NAVD88 height (EPSG:5703): the stored CRS of every raster."""
    if "c" not in _crs_cache:
        from pyproj import CRS
        from pyproj.crs import CompoundCRS
        _crs_cache["c"] = CompoundCRS(name=BAYTM_NAME + " + NAVD88 height",
                                      components=[baytm_crs(), CRS("EPSG:5703")])
    return _crs_cache["c"]


def transformer(src, dst):
    from pyproj import Transformer
    key = ("t", str(src), str(dst))
    if key not in _crs_cache:
        _crs_cache[key] = Transformer.from_crs(src, dst, always_xy=True)
    return _crs_cache[key]


def lonlat_to_baytm(lon, lat):
    """NAD83(2011) lon/lat -> BayTM x (east), z (north). OSM WGS84 coords are treated as
    NAD83 (R-004 §2.1 datum caveat: ±1.5 m carried as uncertainty)."""
    return transformer("EPSG:6318", baytm_crs()).transform(lon, lat)


def baytm_to_lonlat(x, z):
    return transformer(baytm_crs(), "EPSG:6318").transform(x, z)


# ---------------------------------------------------------------- tiles (R-004 §3.1)
@dataclass(frozen=True)
class Tile:
    level: int
    i: int
    j: int

    @property
    def size(self) -> float:
        return TILE_SIZE_M * (2 ** self.level)

    @property
    def id(self) -> str:
        return f"L{self.level}_{self.i:02d}_{self.j:02d}"

    @property
    def folder(self) -> str:
        return f"L{self.level}/{self.i:02d}_{self.j:02d}"

    @property
    def x0(self) -> float:
        return self.i * self.size

    @property
    def z0(self) -> float:
        return self.j * self.size

    @property
    def bounds(self):
        return (self.x0, self.z0, self.x0 + self.size, self.z0 + self.size)

    @property
    def spacing(self) -> float:
        return self.size / (SAMPLES - 1)

    def contains(self, x: float, z: float) -> bool:
        """Half-open ownership rule [x0, x1) x [z0, z1): every point has exactly one owner."""
        x0, z0, x1, z1 = self.bounds
        return x0 <= x < x1 and z0 <= z < z1

    def bounds_lonlat(self, margin_m: float = 0.0):
        x0, z0, x1, z1 = self.bounds
        xs = [x0 - margin_m, x1 + margin_m, x1 + margin_m, x0 - margin_m]
        zs = [z0 - margin_m, z0 - margin_m, z1 + margin_m, z1 + margin_m]
        # densify edges so the lon/lat box really contains the projected square
        lons, lats = [], []
        for t in [k / 16 for k in range(17)]:
            for (ax, az), (bx, bz) in zip(zip(xs, zs), list(zip(xs, zs))[1:] + [(xs[0], zs[0])]):
                lo, la = baytm_to_lonlat(ax + (bx - ax) * t, az + (bz - az) * t)
                lons.append(lo); lats.append(la)
        return (min(lons), min(lats), max(lons), max(lats))


def tile_of(x: float, z: float, level: int = 0) -> Tile:
    s = TILE_SIZE_M * (2 ** level)
    return Tile(level, int(math.floor(x / s)), int(math.floor(z / s)))


def parse_tile(tile_id: str) -> Tile:
    try:
        l, i, j = tile_id.split("_")
        assert l[0] == "L"
        return Tile(int(l[1:]), int(i), int(j))
    except Exception as e:  # noqa: BLE001
        raise SystemExit(f"bad tile id {tile_id!r}, expected L<level>_<ii>_<jj>") from e


# ---------------------------------------------------------------- hashing / json
def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def sha256_bytes(b: bytes) -> str:
    return hashlib.sha256(b).hexdigest()


def canonical_json(obj, indent=None) -> str:
    return json.dumps(obj, sort_keys=True, ensure_ascii=False, indent=indent,
                      separators=(",", ":") if indent is None else (",", ": "))


def write_if_changed(path: Path, data: bytes) -> bool:
    """Atomic write; returns False (and leaves mtime alone) when bytes are unchanged."""
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists() and path.read_bytes() == data:
        return False
    tmp = path.with_suffix(path.suffix + ".tmp")
    tmp.write_bytes(data)
    os.replace(tmp, path)
    return True


def utc_now() -> str:
    return _dt.datetime.now(_dt.timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def git_head() -> dict:
    try:
        rev = subprocess.run(["git", "rev-parse", "HEAD"], cwd=PIPE, capture_output=True, text=True, check=True).stdout.strip()
        dirty = subprocess.run(["git", "status", "--porcelain", "--", "."], cwd=PIPE, capture_output=True, text=True).stdout
        dirty = any(not l[3:].startswith("cache/") for l in dirty.splitlines())
        return {"git": rev, "git_dirty_pipeline": dirty}
    except Exception:  # noqa: BLE001
        return {"git": "unknown", "git_dirty_pipeline": True}


# ---------------------------------------------------------------- network guard
_OFFLINE = os.environ.get("KAIJU_GEO_OFFLINE") == "1"
_real_connect = socket.socket.connect


class NetworkForbidden(RuntimeError):
    pass


def set_offline(flag: bool = True) -> None:
    """After this, any socket connect raises: proves a cached run does no network I/O."""
    global _OFFLINE
    _OFFLINE = flag
    if flag:
        def _deny(self, *a, **k):  # noqa: ANN001
            raise NetworkForbidden(f"network access attempted in offline mode: {a!r}")
        socket.socket.connect = _deny  # type: ignore[assignment]
        os.environ["GDAL_DISABLE_READDIR_ON_OPEN"] = "EMPTY_DIR"
    else:
        socket.socket.connect = _real_connect  # type: ignore[assignment]


def is_offline() -> bool:
    return _OFFLINE


# ---------------------------------------------------------------- cached fetch
def meta_path(dest: Path) -> Path:
    return dest.with_name(dest.name + ".meta.json")


def read_meta(dest: Path) -> dict | None:
    mp = meta_path(dest)
    if dest.exists() and mp.exists():
        return json.loads(mp.read_text())
    return None


def up_to_date(dest: Path, label: str) -> dict | None:
    m = read_meta(dest)
    if m is not None:
        print(f"[{label}] up to date: {dest.relative_to(PIPE)} (sha256 {m['sha256'][:12]}, fetched {m['fetched_utc']})")
    return m


def finish_fetch(dest: Path, tmp: Path, url: str, extra: dict | None = None) -> dict:
    """Validate-then-rename: a failed fetch never leaves a partial cache entry."""
    if not tmp.exists() or tmp.stat().st_size == 0:
        raise RuntimeError(f"fetch produced no data for {url}")
    os.replace(tmp, dest)
    meta = {"url": url, "fetched_utc": utc_now(), "sha256": sha256_file(dest), "bytes": dest.stat().st_size}
    meta.update(extra or {})
    meta_path(dest).write_text(canonical_json(meta, indent=1) + "\n")
    return meta


def http_get(url: str, dest: Path, label: str, retries: int = 4, timeout: int = 120,
             expect: str | None = None) -> dict:
    """Download url -> dest unless cached. Raises (non-zero exit) on any failure."""
    m = up_to_date(dest, label)
    if m is not None:
        return m
    if _OFFLINE:
        raise NetworkForbidden(f"[{label}] {dest.name} missing from cache and --offline is set")
    dest.parent.mkdir(parents=True, exist_ok=True)
    tmp = dest.with_name(dest.name + ".part")
    last = None
    for attempt in range(retries):
        try:
            req = urllib.request.Request(url, headers={"User-Agent": "kaiju-geo-pipeline/1 (open-data GIS bake)"})
            with urllib.request.urlopen(req, timeout=timeout) as r, open(tmp, "wb") as f:
                hdr = {"etag": r.headers.get("ETag"), "last_modified": r.headers.get("Last-Modified"),
                       "content_length": r.headers.get("Content-Length")}
                n = 0
                t0 = time.time()
                for chunk in iter(lambda: r.read(1 << 20), b""):
                    f.write(chunk); n += len(chunk)
                    if n % (64 << 20) < (1 << 20):
                        print(f"[{label}] {n / 1e6:.0f} MB ({n / 1e6 / max(time.time() - t0, 1e-3):.1f} MB/s)", flush=True)
            if hdr["content_length"] and int(hdr["content_length"]) != tmp.stat().st_size:
                raise RuntimeError(f"short read {tmp.stat().st_size} of {hdr['content_length']}")
            if expect and expect not in tmp.read_bytes()[:4096].decode("latin-1"):
                raise RuntimeError(f"unexpected payload (no {expect!r}) from {url}")
            meta = finish_fetch(dest, tmp, url, {k: v for k, v in hdr.items() if v})
            print(f"[{label}] fetched {dest.relative_to(PIPE)} ({meta['bytes'] / 1e6:.2f} MB)")
            return meta
        except (urllib.error.URLError, OSError, RuntimeError) as e:
            last = e
            print(f"[{label}] attempt {attempt + 1}/{retries} failed: {e}", file=sys.stderr)
            if tmp.exists():
                tmp.unlink()
            time.sleep(3 * (attempt + 1))
    raise RuntimeError(f"[{label}] source unreachable after {retries} attempts: {url}: {last}")


def http_json(url: str, label: str, timeout: int = 120, retries: int = 4):
    """Uncached JSON GET (used only inside fetch steps that cache their own result)."""
    if _OFFLINE:
        raise NetworkForbidden(f"[{label}] network needed for {url} but --offline is set")
    last = None
    for attempt in range(retries):
        try:
            req = urllib.request.Request(url, headers={"User-Agent": "kaiju-geo-pipeline/1 (open-data GIS bake)"})
            with urllib.request.urlopen(req, timeout=timeout) as r:
                return json.loads(r.read().decode("utf-8"))
        except (urllib.error.URLError, OSError, ValueError) as e:
            last = e
            print(f"[{label}] attempt {attempt + 1}/{retries} failed: {e}", file=sys.stderr)
            time.sleep(3 * (attempt + 1))
    raise RuntimeError(f"[{label}] source unreachable after {retries} attempts: {url}: {last}")


def std_args(parser):
    parser.add_argument("--offline", action="store_true",
                        help="forbid all network access (cache must be complete)")
    return parser


def apply_std(args):
    if getattr(args, "offline", False):
        set_offline(True)
