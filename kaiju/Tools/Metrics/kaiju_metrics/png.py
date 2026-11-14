"""Lossless PNG I/O with explicit bit-depth / colour-type checks (read from the IHDR chunk)."""
from __future__ import annotations

import struct
import zlib

import numpy as np

_SIG = b"\x89PNG\r\n\x1a\n"
COLOUR_TYPES = {0: "grey", 2: "rgb", 3: "palette", 4: "grey+alpha", 6: "rgba"}


def png_header(path):
    """Return dict(width, height, bit_depth, colour_type) or raise ValueError when not a PNG."""
    with open(path, "rb") as f:
        head = f.read(33)
    if len(head) < 33 or head[:8] != _SIG or head[12:16] != b"IHDR":
        raise ValueError("not a PNG file (bad signature or missing IHDR)")
    w, h, bd, ct = struct.unpack(">IIBB", head[16:26])
    return {"width": w, "height": h, "bit_depth": bd, "colour_type": COLOUR_TYPES.get(ct, f"unknown({ct})")}


def png_integrity(path):
    """Complete structural check without decoding pixels: every chunk complete with a matching CRC-32, IHDR first,
    at least one IDAT, IEND last. Returns None when sound, else a message (truncated / corrupt)."""
    with open(path, "rb") as f:
        b = f.read()
    if b[:8] != _SIG:
        return "not a PNG file (bad signature)"
    pos, seen, last = 8, [], None
    while pos < len(b):
        if pos + 12 > len(b):
            return "PNG truncated (incomplete chunk header)"
        n = struct.unpack(">I", b[pos:pos + 4])[0]
        t = b[pos + 4:pos + 8]
        if pos + 12 + n > len(b):
            return f"PNG truncated inside chunk {t.decode('latin-1')}"
        crc = struct.unpack(">I", b[pos + 8 + n:pos + 12 + n])[0]
        if zlib.crc32(t + b[pos + 8:pos + 8 + n]) & 0xFFFFFFFF != crc:
            return f"PNG corrupt: CRC mismatch in chunk {t.decode('latin-1')}"
        seen.append(t)
        last = t
        pos += 12 + n
        if t == b"IEND":
            break
    if not seen or seen[0] != b"IHDR":
        return "PNG corrupt: IHDR is not the first chunk"
    if b"IDAT" not in seen:
        return "PNG corrupt: no IDAT chunk"
    if last != b"IEND":
        return "PNG truncated (no IEND chunk)"
    return None


def read_png(path):
    """Decode a PNG without conversion. RGB returned in RGB order; grey as 2-D. dtype uint8 or uint16.
    A file that cannot be decoded is a contract violation (InvalidInput), never a tool error."""
    import cv2  # local import: keeps package import light
    a = cv2.imread(path, cv2.IMREAD_UNCHANGED)
    if a is None:
        from .common import ContractError, InvalidInput, rel_path
        raise InvalidInput([ContractError(rel_path(path), "png", "PNG could not be decoded (truncated or corrupt)",
                                          "a complete lossless PNG", "undecodable file")])
    if a.ndim == 3 and a.shape[2] == 3:
        a = a[..., ::-1]
    elif a.ndim == 3 and a.shape[2] == 4:
        a = a[..., [2, 1, 0, 3]]
    return np.ascontiguousarray(a)


def write_png(path, arr):
    """Deterministic PNG writer (zlib level 9, no ancillary chunks, no timestamps)."""
    a = np.ascontiguousarray(arr)
    if a.dtype == np.uint8 and a.ndim == 2:
        ct, bd, ch = 0, 8, 1
    elif a.dtype == np.uint8 and a.ndim == 3 and a.shape[2] == 3:
        ct, bd, ch = 2, 8, 3
    elif a.dtype == np.uint8 and a.ndim == 3 and a.shape[2] == 4:
        ct, bd, ch = 6, 8, 4
    elif a.dtype == np.uint16 and a.ndim == 2:
        ct, bd, ch = 0, 16, 1
    else:
        raise ValueError(f"unsupported array for PNG: {a.dtype} {a.shape}")
    h, w = a.shape[:2]
    data = a.astype(">u2").tobytes() if bd == 16 else a.tobytes()
    stride = w * ch * (bd // 8)
    raw = b"".join(b"\x00" + data[y * stride:(y + 1) * stride] for y in range(h))

    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)

    with open(path, "wb") as f:
        f.write(_SIG + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, bd, ct, 0, 0, 0))
                + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))
