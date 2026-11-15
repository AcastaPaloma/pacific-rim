"""Regenerates the PyTorch<->C# parity fixture (DotnetTests/AI.Tests/Fixtures). Run from Tools/Training:
   /opt/miniconda3/envs/kaiju/bin/python -m tests.make_parity_fixture"""
import json
import pathlib

import numpy as np
import torch

from kaiju_training.export import export_kpw, logits_numpy
from kaiju_training.policy import ActorCritic

OUT = pathlib.Path(__file__).resolve().parents[3] / "DotnetTests/AI.Tests/Fixtures"


def main():
    torch.manual_seed(7)
    m = ActorCritic(313, 9, 7, 3)
    with torch.no_grad():
        m.head.weight.mul_(400.0)  # init head is ~0.01 scaled; make logits informative
        m.head.bias.normal_(0, 0.3)
    OUT.mkdir(parents=True, exist_ok=True)
    macs = export_kpw(m, str(OUT / "mlp_parity.kpw"), "parity@1")
    rng = np.random.default_rng(3)
    obs = rng.normal(0, 1.0, (16, 313)).astype(np.float32)
    lg = logits_numpy(m, obs)
    (OUT / "mlp_parity.json").write_text(json.dumps({"macs": macs, "obs": obs.tolist(), "logits": lg.tolist()}))
    print("macs", macs, "max|logit|", float(np.abs(lg).max()))


if __name__ == "__main__":
    main()
