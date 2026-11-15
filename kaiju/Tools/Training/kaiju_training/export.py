"""Export the actor to the flat KPW1 weights file read by C# `MlpPolicyNetwork` (and optionally ONNX).

Layout: magic "KPW1", int32 layout version, int32 id length + utf8 id, int32 layer count, then per layer int32 in, int32 out,
float32 W[out*in] (row-major), float32 b[out]. Hidden layers are followed by tanh, the last layer (the head) is raw logits."""
from __future__ import annotations

import struct
import sys

import numpy as np
import torch
from torch import nn

LAYOUT_VERSION = 1


def actor_layers(model) -> list[nn.Linear]:
    return [*(m for m in model.actor.modules() if isinstance(m, nn.Linear)), model.head]


def export_kpw(model, path: str, network_id: str) -> int:
    layers = actor_layers(model)
    ident = network_id.encode("utf-8")
    with open(path, "wb") as f:
        f.write(b"KPW1")
        f.write(struct.pack("<ii", LAYOUT_VERSION, len(ident)))
        f.write(ident)
        f.write(struct.pack("<i", len(layers)))
        for lin in layers:
            w = lin.weight.detach().cpu().numpy().astype("<f4")
            b = lin.bias.detach().cpu().numpy().astype("<f4")
            f.write(struct.pack("<ii", lin.in_features, lin.out_features))
            f.write(w.tobytes()); f.write(b.tobytes())
    return sum(l.in_features * l.out_features for l in layers)


class _ActorOnly(nn.Module):
    def __init__(self, model):
        super().__init__(); self.actor, self.head = model.actor, model.head

    def forward(self, obs):
        return self.head(self.actor(obs))


def export_onnx(model, path: str, obs_dim: int) -> None:
    """ONNX of obs -> logits (dynamic batch). Needs the optional `onnx` package for torch.onnx.export."""
    torch.onnx.export(_ActorOnly(model).eval(), torch.zeros(1, obs_dim), path, input_names=["obs"], output_names=["logits"],
                      dynamic_axes={"obs": {0: "batch"}, "logits": {0: "batch"}}, opset_version=17, dynamo=False)


def logits_numpy(model, obs: np.ndarray) -> np.ndarray:
    with torch.no_grad():
        return _ActorOnly(model).eval()(torch.as_tensor(obs, dtype=torch.float32)).numpy()


if __name__ == "__main__":
    from .policy import ActorCritic
    ck = torch.load(sys.argv[1], map_location="cpu")
    m = ActorCritic(313, 9, 7, 3); m.load_state_dict(ck["model"] if "model" in ck else ck)
    print("MACs", export_kpw(m, sys.argv[2], sys.argv[3] if len(sys.argv) > 3 else "ppo@0"))
