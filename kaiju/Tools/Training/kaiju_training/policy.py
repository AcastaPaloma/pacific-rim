"""Masked factored actor (target, maneuver, speed) + critic (R-028 sections 3, 4, 10).

The maneuver head is masked conditionally on the sampled target (target-needing maneuvers are illegal with target 0),
mirroring `PolicyLayout.ManeuverAllowed` in C#. Parameter sharing: one actor for every kaiju; the ego-centric encoding
carries no agent id. The critic here sees the local observation (IPPO); the MAPPO centralised critic is R-028 4.2."""
from __future__ import annotations

import numpy as np
import torch
from torch import nn

# Maneuver order = C# Game.AI.Policy.Maneuver: Hold, Approach, FlankLeft, FlankRight, Charge, Retreat, Regroup
NEEDS_TARGET = np.array([False, True, True, True, True, False, False])
NEG = -1e9


def mlp(sizes, act=nn.Tanh):
    layers = []
    for a, b in zip(sizes[:-1], sizes[1:]):
        layers += [nn.Linear(a, b), act()]
    return nn.Sequential(*layers)


class ActorCritic(nn.Module):
    def __init__(self, obs_dim, n_targets, n_maneuvers, n_speeds, hidden=(128, 128), critic_hidden=(256, 256)):
        super().__init__()
        self.n_targets, self.n_maneuvers, self.n_speeds = n_targets, n_maneuvers, n_speeds
        self.actor = mlp([obs_dim, *hidden])
        self.head = nn.Linear(hidden[-1], n_targets + n_maneuvers + n_speeds)
        self.critic = nn.Sequential(mlp([obs_dim, *critic_hidden]), nn.Linear(critic_hidden[-1], 1))
        nn.init.orthogonal_(self.head.weight, 0.01)
        nn.init.zeros_(self.head.bias)
        self.register_buffer("needs_target", torch.as_tensor(NEEDS_TARGET[:n_maneuvers]))

    def actor_macs(self) -> int:
        return sum(m.in_features * m.out_features for m in [*self.actor.modules(), self.head] if isinstance(m, nn.Linear))

    def _split(self, obs):
        z = self.head(self.actor(obs))
        nt, nm = self.n_targets, self.n_maneuvers
        return z[:, :nt], z[:, nt:nt + nm], z[:, nt + nm:]

    def masks(self, mask, target):
        nt, nm = self.n_targets, self.n_maneuvers
        mt = mask[:, :nt]
        mm = mask[:, nt:nt + nm] & ~(self.needs_target[None, :] & (target[:, None] == 0))
        ms = mask[:, nt + nm:]
        return mt, mm, ms

    def value(self, obs):
        return self.critic(obs).squeeze(-1)

    @torch.no_grad()
    def act(self, obs, mask, generator=None, greedy=False):
        lt, lm, ls = self._split(obs)
        mt = mask[:, :self.n_targets]
        t = _pick(lt.masked_fill(~mt, NEG), generator, greedy)
        _, mm, ms = self.masks(mask, t)
        m = _pick(lm.masked_fill(~mm, NEG), generator, greedy)
        s = _pick(ls.masked_fill(~ms, NEG), generator, greedy)
        a = torch.stack([t, m, s], dim=1)
        logp, _ = self.evaluate_logits(lt, lm, ls, mask, a)
        return a, logp, self.value(obs)

    def evaluate_logits(self, lt, lm, ls, mask, a):
        mt, mm, ms = self.masks(mask, a[:, 0])
        logp, ent = 0.0, 0.0
        for logits, msk, idx in ((lt, mt, a[:, 0]), (lm, mm, a[:, 1]), (ls, ms, a[:, 2])):
            lg = torch.log_softmax(logits.masked_fill(~msk, NEG), dim=-1)
            logp = logp + lg.gather(1, idx[:, None]).squeeze(1)
            p = lg.exp()
            ent = ent - (p * lg.masked_fill(~msk, 0.0)).sum(-1)
        return logp, ent

    def evaluate(self, obs, mask, a):
        lt, lm, ls = self._split(obs)
        logp, ent = self.evaluate_logits(lt, lm, ls, mask, a)
        return logp, ent, self.value(obs)


def _pick(logits, generator, greedy):
    if greedy:
        return logits.argmax(-1)
    p = torch.softmax(logits, dim=-1)
    return torch.multinomial(p, 1, generator=generator).squeeze(1)


def random_actions(mask: np.ndarray, rng: np.random.Generator, n_targets: int, n_maneuvers: int) -> np.ndarray:
    """Uniform masked baseline (same semantics as C# MaskedRandomPolicyNetwork, different RNG)."""
    out = np.zeros((mask.shape[0], 3), dtype=np.int32)
    for i, m in enumerate(mask):
        t = rng.choice(np.flatnonzero(m[:n_targets]))
        mm = m[n_targets:n_targets + n_maneuvers] & ~(NEEDS_TARGET[:n_maneuvers] & (t == 0))
        out[i] = (t, rng.choice(np.flatnonzero(mm)), rng.choice(np.flatnonzero(m[n_targets + n_maneuvers:])))
    return out
