import struct, sys
from pathlib import Path
import numpy as np
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import export_bay_softness as e


def test_class_scale_monotone():
    o = ["NM", "VL", "L", "M", "H", "VH"]
    v = [e.CLASS_SOFT[c] for c in o]
    assert v == sorted(v) and v[-1] == 1.0 and v[0] == 0.0


def test_read_bin_roundtrip(tmp_path):
    nx = ny = 2
    p = tmp_path / "t.bin"
    p.write_bytes(b"BCF1" + struct.pack("<iifff", nx, ny, 0.0, 0.0, 256.0) + np.arange(4, dtype="<u2").tobytes() + bytes([0, 10, 20, 255]))
    r = e.read_bin(p)
    assert r[:5] == (2, 2, 0.0, 0.0, 256.0) and list(r[6]) == [0, 10, 20, 255] and list(r[5]) == [0, 1, 2, 3]
