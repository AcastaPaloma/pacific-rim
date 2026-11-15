"""Smoke train (T-041): bridge -> masked PPO -> brain_log@v1, end to end. Proves the loop, not a policy.

  /opt/miniconda3/envs/kaiju/bin/python -m kaiju_training.smoke_train --config configs/smoke.yaml --out runs/smoke
  (run from Tools/Training). `--policy random` skips learning (uniform masked baseline, throughput reference)."""
from __future__ import annotations

import argparse
import glob
import hashlib
import json
import os
import sys
import time

import numpy as np
import torch
import yaml

from . import reward as reward_mod
from .bridge import BridgeProcess
from .policy import ActorCritic, random_actions
from .ppo import Rollout, ppo_update

TRAINING_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
METRICS_DIR = os.path.join(os.path.dirname(TRAINING_DIR), "Metrics")


def validate_logs(log_dir: str) -> dict:
    """Load every brain_log with the M-007 tool's strict loader and compute its per-run primitives."""
    sys.path.insert(0, METRICS_DIR)
    from kaiju_metrics.brain.brain_log import load_brain_log  # noqa: E402
    from kaiju_metrics.brain import m007  # noqa: E402
    from kaiju_metrics.registry import load_registry  # noqa: E402

    reg = load_registry("M-007")
    out = {"files": 0, "valid": 0, "errors": [], "brain_ids": set(), "decisions": 0, "primitives": {}}
    for p in sorted(glob.glob(os.path.join(log_dir, "*.jsonl"))):
        out["files"] += 1
        log, errs = load_brain_log(p)
        if errs or log is None:
            out["errors"] += [str(e) for e in errs[:3]]
            continue
        out["valid"] += 1
        out["brain_ids"].add(log.brain_id)
        out["decisions"] += len(log.decisions)
        prim = m007.compute_run(log, reg)
        prim.pop("diag", None)
        out["primitives"][os.path.basename(p)] = prim
    out["brain_ids"] = sorted(out["brain_ids"])
    return out


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("--config", default=os.path.join(TRAINING_DIR, "configs", "smoke.yaml"))
    ap.add_argument("--out", default=os.path.join(TRAINING_DIR, "runs", "smoke"))
    ap.add_argument("--policy", choices=["ppo", "random"], default="ppo")
    ap.add_argument("--updates", type=int, default=None)
    args = ap.parse_args(argv)
    with open(args.config) as f:
        cfg = yaml.safe_load(f)
    pc, rc = cfg["ppo"], cfg["reward"]
    if args.updates is not None:
        pc["updates"] = args.updates
    seed = int(cfg["seed"])
    torch.manual_seed(seed)
    torch.set_num_threads(int(pc.get("torch_threads", 4)))
    torch.use_deterministic_algorithms(True)
    gen = torch.Generator().manual_seed(seed)
    rng = np.random.default_rng(seed)
    out = os.path.abspath(args.out)
    log_dir = os.path.join(out, "brain_logs")
    for old in glob.glob(os.path.join(log_dir, "*.jsonl")):
        os.remove(old)
    bcfg = dict(cfg["bridge"], log_dir=log_dir)

    report = {"config": args.config, "policy": args.policy, "updates": [], "seed": seed}
    t0 = time.perf_counter()
    with BridgeProcess(bcfg, out) as br:
        L = br.layout
        report["layout"] = L.__dict__
        model = ActorCritic(L.obs_dim, L.n_targets, L.n_maneuvers, L.n_speeds, tuple(cfg["policy"]["hidden"]),
                            tuple(cfg["policy"]["critic_hidden"]))
        opt = torch.optim.Adam(model.parameters(), lr=float(pc["lr"]), eps=1e-5)
        report["actor_macs"] = model.actor_macs()
        report["actor_params"] = sum(p.numel() for p in [*model.actor.parameters(), *model.head.parameters()])
        ro = Rollout(float(pc["gamma"]), float(pc["lam"]))
        rows = decisions = 0
        t_env = t_learn = 0.0
        n_updates = int(pc["updates"])
        for u in range(n_updates):
            progress = u / max(1, n_updates)
            ts = time.perf_counter()
            while ro.count < int(pc["rollout_rows"]):
                b = br.recv()
                rows += b.kind.shape[0]
                d = b.decide
                decisions += int(d.sum())
                obs = torch.as_tensor(b.obs[d])
                mask = torch.as_tensor(b.mask[d])
                if args.policy == "random":
                    act = random_actions(b.mask[d], rng, L.n_targets, L.n_maneuvers)
                    logp = np.zeros(act.shape[0], np.float32)
                    value = np.zeros(act.shape[0], np.float32)
                else:
                    a, lp, v = model.act(obs, mask, generator=gen)
                    act, logp, value = a.numpy().astype(np.int32), lp.numpy(), v.numpy()
                br.send(act)
                ro.ingest(b, reward_mod.compute(b.ev, rc, progress), act, logp, value)
            t_env += time.perf_counter() - ts
            data = ro.drain()
            tl = time.perf_counter()
            st = ppo_update(model, opt, data, pc, gen) if args.policy == "ppo" else {}
            t_learn += time.perf_counter() - tl
            fin = np.asarray(ro.finished) if ro.finished else np.zeros((0, 5))
            st.update({"update": u, "transitions": int(data["obs"].shape[0]), "mean_return_target":
                       float(np.mean(data["ret"])) if data["obs"].shape[0] else 0.0,
                       "agent_episodes_finished": int(fin.shape[0]),
                       "win_frac": float(fin[:, 1].mean()) if fin.shape[0] else None,
                       "mean_agent_return": float(fin[:, 0].mean()) if fin.shape[0] else None})
            report["updates"].append(st)
            print(json.dumps(st), flush=True)
        report["rc"] = None
    report["rc"] = br.proc.returncode
    wall = time.perf_counter() - t0
    report.update({"rows": rows, "decisions": decisions, "wall_s": wall, "env_s": t_env, "learn_s": t_learn,
                   "decisions_per_s_env": decisions / t_env if t_env else None,
                   "bridge_stderr": open(br.stderr_path).read().strip()[-500:]})
    report["brain_logs"] = validate_logs(log_dir)
    torch.save(model.state_dict(), os.path.join(out, "actor_critic.pt"))
    h = hashlib.sha256()
    for k, v in sorted(model.state_dict().items()):
        h.update(k.encode()); h.update(v.numpy().tobytes())
    report["weights_sha256"] = h.hexdigest()
    with open(os.path.join(out, "smoke_report.json"), "w") as f:
        json.dump(report, f, indent=1, sort_keys=True, default=str)
    ok = report["rc"] == 0 and report["brain_logs"]["valid"] > 0 and not report["brain_logs"]["errors"]
    print(f"smoke: decisions={decisions} wall={wall:.1f}s logs={report['brain_logs']['valid']}/{report['brain_logs']['files']} "
          f"rc={report['rc']} ok={ok}")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
