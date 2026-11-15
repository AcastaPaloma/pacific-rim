"""Asynchronous-agent rollout buffer + PPO update (R-028 section 4).

Kaiju decide at staggered physics steps, so the bridge delivers one agent decision per env per batch. A transition of agent
(env, agent) is completed by that agent's NEXT row: its events are the reward, its observation gives the bootstrap value.
An end row closes the trajectory (terminal on win / wipe / own death, truncated on timeout). GAE runs per agent key."""
from __future__ import annotations

from collections import defaultdict

import numpy as np
import torch

from .bridge import KIND_END
from .reward import IDX


class Rollout:
    def __init__(self, gamma: float, lam: float):
        self.gamma, self.lam = gamma, lam
        self.pending = {}
        self.done = defaultdict(list)
        self.count = 0
        self.ep_returns = defaultdict(float)
        self.finished = []  # (return, win, loss, timeout, died) per agent-episode

    def ingest(self, batch, rewards, act, logp, value):
        """Process one bridge batch in row order. act/logp/value are aligned with the decide rows (in order)."""
        j = 0
        for i in range(batch.kind.shape[0]):
            key = (int(batch.env[i]), int(batch.agent[i]))
            r = float(rewards[i])
            if batch.kind[i] == KIND_END:
                ev = batch.ev[i]
                p = self.pending.pop(key, None)
                if p is not None:
                    died = ev[IDX["i_died"]] > 0
                    terminal = bool(died or ev[IDX["win"]] > 0 or ev[IDX["loss"]] > 0)
                    self._complete(key, p, r, terminal, 0.0 if terminal else p["value"], end=True)
                self.ep_returns[key] += r
                self.finished.append((self.ep_returns.pop(key), float(ev[IDX["win"]]), float(ev[IDX["loss"]]),
                                      float(ev[IDX["timeout"]]), float(ev[IDX["i_died"]])))
                continue
            p = self.pending.get(key)
            if p is not None:
                self._complete(key, p, r, False, float(value[j]), end=False)
                self.ep_returns[key] += r
            self.pending[key] = {"obs": batch.obs[i], "mask": batch.mask[i], "act": act[j], "logp": float(logp[j]),
                                 "value": float(value[j])}
            j += 1

    def _complete(self, key, p, r, terminal, next_value, end):
        self.done[key].append((p, r, terminal, next_value, end))
        self.count += 1

    def drain(self):
        """GAE per key over completed transitions; returns flat arrays and clears the completed lists."""
        obs, mask, act, logp, adv, ret, val = [], [], [], [], [], [], []
        for key, seq in self.done.items():
            a_next = 0.0
            advs = np.zeros(len(seq))
            for t in range(len(seq) - 1, -1, -1):
                p, r, terminal, nv, end = seq[t]
                delta = r + self.gamma * nv * (0.0 if terminal else 1.0) - p["value"]
                # chain only to the next transition of the same trajectory (an end row, terminal or truncated, ends it)
                cont = 0.0 if (end or t == len(seq) - 1) else 1.0
                a_next = delta + self.gamma * self.lam * cont * a_next
                advs[t] = a_next
            for t, (p, r, terminal, nv, end) in enumerate(seq):
                obs.append(p["obs"]); mask.append(p["mask"]); act.append(p["act"]); logp.append(p["logp"])
                val.append(p["value"]); adv.append(advs[t]); ret.append(advs[t] + p["value"])
        self.done.clear()
        self.count = 0
        return {"obs": np.asarray(obs, np.float32), "mask": np.asarray(mask, bool), "act": np.asarray(act, np.int64),
                "logp": np.asarray(logp, np.float32), "adv": np.asarray(adv, np.float32),
                "ret": np.asarray(ret, np.float32), "val": np.asarray(val, np.float32)}


def ppo_update(model, opt, data, cfg, generator: torch.Generator, device="cpu"):
    n = data["obs"].shape[0]
    t = {k: torch.as_tensor(v, device=device) for k, v in data.items()}
    adv = (t["adv"] - t["adv"].mean()) / (t["adv"].std() + 1e-8)
    stats = defaultdict(list)
    for _ in range(int(cfg["epochs"])):
        perm = torch.randperm(n, generator=generator)
        for s in range(0, n, int(cfg["minibatch"])):
            idx = perm[s:s + int(cfg["minibatch"])].to(device)
            logp, ent, v = model.evaluate(t["obs"][idx], t["mask"][idx], t["act"][idx])
            ratio = torch.exp(logp - t["logp"][idx])
            a = adv[idx]
            pg = -torch.min(ratio * a, torch.clamp(ratio, 1 - cfg["clip"], 1 + cfg["clip"]) * a).mean()
            vf = 0.5 * ((v - t["ret"][idx]) ** 2).mean()
            loss = pg + cfg["vf_coef"] * vf - cfg["ent_coef"] * ent.mean()
            opt.zero_grad()
            loss.backward()
            torch.nn.utils.clip_grad_norm_(model.parameters(), cfg["max_grad_norm"])
            opt.step()
            with torch.no_grad():
                stats["pg_loss"].append(pg.item()); stats["vf_loss"].append(vf.item()); stats["entropy"].append(ent.mean().item())
                stats["approx_kl"].append((t["logp"][idx] - logp).mean().item())
                stats["clip_frac"].append(((ratio - 1).abs() > cfg["clip"]).float().mean().item())
    return {k: float(np.mean(v)) for k, v in stats.items()}
