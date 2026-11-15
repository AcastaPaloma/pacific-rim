import struct

import numpy as np
import pytest
import torch

from kaiju_training.export import export_kpw, export_onnx, logits_numpy
from kaiju_training.policy import ActorCritic


def read_kpw(path):
    b = open(path, "rb").read(); assert b[:4] == b"KPW1"
    ver, n = struct.unpack_from("<ii", b, 4); off = 12; ident = b[off:off + n].decode(); off += n
    (nl,) = struct.unpack_from("<i", b, off); off += 4; layers = []
    for _ in range(nl):
        i, o = struct.unpack_from("<ii", b, off); off += 8
        w = np.frombuffer(b, "<f4", i * o, off).reshape(o, i); off += 4 * i * o
        bias = np.frombuffer(b, "<f4", o, off); off += 4 * o; layers.append((w, bias))
    assert off == len(b)
    return ver, ident, layers


def test_kpw_roundtrip_matches_torch(tmp_path):
    torch.manual_seed(1); m = ActorCritic(313, 9, 7, 3)
    with torch.no_grad(): m.head.weight.mul_(300)
    p = str(tmp_path / "w.kpw"); macs = export_kpw(m, p, "x@1")
    ver, ident, layers = read_kpw(p)
    assert (ver, ident, macs) == (1, "x@1", 58880 == macs and macs)
    obs = np.random.default_rng(0).normal(size=(4, 313)).astype(np.float32)
    h = obs
    for k, (w, b) in enumerate(layers):
        h = h @ w.T + b
        if k < len(layers) - 1: h = np.tanh(h)
    assert np.abs(h - logits_numpy(m, obs)).max() < 1e-4


def test_onnx_export(tmp_path):
    onnx = pytest.importorskip("onnx")
    m = ActorCritic(313, 9, 7, 3); p = str(tmp_path / "a.onnx")
    export_onnx(m, p, 313); onnx.checker.check_model(onnx.load(p))
