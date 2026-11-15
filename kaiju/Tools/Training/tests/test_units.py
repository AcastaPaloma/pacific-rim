"""T-041 unit tests: protocol framing, reward, masked policy, rollout/GAE (no bridge process)."""
import struct

import numpy as np
import pytest
import torch

from kaiju_training import bridge, reward
from kaiju_training.bridge import KIND_DECIDE, KIND_END, EV_NAMES, Layout, parse_hello, parse_rows, row_dtype
from kaiju_training.policy import NEEDS_TARGET, ActorCritic, random_actions
from kaiju_training.ppo import Rollout

L = Layout(1, 313, 19, len(EV_NAMES), 8, 7, 7, 3, 2)


def test_hello_and_rows_roundtrip():
    hello = struct.pack("<11i", bridge.MAGIC, bridge.PROTOCOL, 1, 313, 19, 13, 8, 7, 7, 3, 2)
    assert parse_hello(hello) == L
    with pytest.raises(RuntimeError):
        parse_hello(struct.pack("<11i", 0, *([1] * 10)))
    a = np.zeros(3, dtype=row_dtype(L))
    a["env"] = [0, 1, 1]; a["agent"] = [2, 0, 3]; a["kind"] = [KIND_DECIDE, KIND_END, KIND_DECIDE]
    a["obs"][1, 5] = 0.25; a["mask"][2, 4] = 1; a["ev"][0, 6] = 1.0
    assert row_dtype(L).itemsize == 16 + 4 * 13 + 4 * 313 + 19
    b = parse_rows(a.tobytes(), L, 3)
    assert b.decide.tolist() == [True, False, True]
    assert b.obs[1, 5] == pytest.approx(0.25) and b.mask[2, 4] and b.ev[0, 6] == 1.0
    acts = bridge.pack_actions(np.array([[1, 4, 2], [0, 5, 0]]))
    assert struct.unpack("<7i", acts) == (2, 1, 4, 2, 0, 5, 0)


def test_reward_terms_and_annealing():
    ev = np.zeros((3, len(EV_NAMES)), np.float32)
    ix = reward.IDX
    ev[0, ix["win"]] = 1; ev[0, ix["dmg_dealt"]] = 0.5
    ev[1, ix["i_died"]] = 1; ev[1, ix["elapsed_s"]] = 10
    ev[2, ix["commit_multi"]] = 2; ev[2, ix["commit_flanked"]] = 1
    w = {"win": 5.0, "dmg_dealt": 1.0, "i_died": -1.0, "time_per_s": -0.002,
         "shaping": {"commit_flanked": 0.3, "commit_unflanked": -0.1, "anneal_frac": 0.5}}
    r0 = reward.compute(ev, w, progress=0.0)
    assert r0[0] == pytest.approx(5.5) and r0[1] == pytest.approx(-1.02) and r0[2] == pytest.approx(0.2)
    assert reward.compute(ev, w, progress=0.25)[2] == pytest.approx(0.1)
    assert reward.compute(ev, w, progress=0.6)[2] == 0.0  # shaping gone after anneal_frac
    assert reward.compute(ev, w, progress=0.6)[0] == pytest.approx(5.5)  # outcome terms permanent


def _mask(n, usable):
    m = np.zeros((n, 19), bool)
    m[:, 0] = True
    m[:, 1:1 + usable] = True
    m[:, 9:16] = True if usable else NEEDS_TARGET == False  # noqa: E712
    m[:, 16:] = True
    return m


def test_masked_policy_respects_conditional_mask_and_logp():
    torch.manual_seed(0)
    model = ActorCritic(313, 9, 7, 3)
    assert model.actor_macs() <= 100_000  # R-028 section 10 bound (D8)
    obs = torch.randn(512, 313)
    mask = torch.as_tensor(np.concatenate([_mask(256, 3), _mask(256, 0)]))
    a, logp, v = model.act(obs, mask, generator=torch.Generator().manual_seed(1))
    an = a.numpy(); mn = mask.numpy()
    assert all(mn[i, an[i, 0]] for i in range(512))
    assert (an[256:, 0] == 0).all()
    assert not NEEDS_TARGET[an[an[:, 0] == 0, 1]].any()  # no target -> only Hold / Retreat / Regroup
    lp2, ent, v2 = model.evaluate(obs, mask, a)
    assert torch.allclose(logp, lp2, atol=1e-5) and torch.allclose(v, v2)
    assert (ent > 0).all()
    a2, _, _ = model.act(obs, mask, generator=torch.Generator().manual_seed(1))
    assert torch.equal(a, a2)  # seeded sampling is deterministic
    ra = random_actions(mn[:64], np.random.default_rng(3), 9, 7)
    assert all(mn[i, ra[i, 0]] and mn[i, 9 + ra[i, 1]] for i in range(64))


def _batch(rows):
    n = len(rows)
    b = bridge.Batch(np.array([r[0] for r in rows], np.int32), np.array([r[1] for r in rows], np.int32),
                     np.array([r[2] for r in rows], np.int32), np.zeros(n, np.int32), np.zeros((n, 13), np.float32),
                     np.zeros((n, 4), np.float32), np.ones((n, 19), bool))
    for i, r in enumerate(rows):
        for k, v in r[3].items():
            b.ev[i, reward.IDX[k]] = v
    return b


def test_rollout_completes_on_next_row_and_splits_episodes():
    ro = Rollout(gamma=0.5, lam=1.0)
    act = np.zeros((1, 3), np.int64)
    # agent (0,0): decide, decide (reward 1), end (timeout = truncated, reward 2), new episode decide, end (win)
    seq = [(0, 0, KIND_DECIDE, {}), (0, 0, KIND_DECIDE, {"elapsed_s": 1}), (0, 0, KIND_END, {"timeout": 1, "elapsed_s": 2}),
           (0, 0, KIND_DECIDE, {}), (0, 0, KIND_END, {"win": 1})]
    for s in seq:
        b = _batch([s])
        r = reward.compute(b.ev, {"time_per_s": 1.0, "win": 10.0})
        nd = int(b.decide.sum())
        ro.ingest(b, r, act[:nd], np.zeros(nd), np.full(nd, 4.0))
    assert ro.count == 3 and len(ro.finished) == 2
    d = ro.drain()
    # t0: r=1, next v=4 (same episode): delta = 1 + 0.5*4 - 4 = -1; t1 (truncated): r=2, bootstrap own v=4: delta=2+2-4=0
    # GAE chain t0 <- t1 (lam 1): adv0 = -1 + 0.5*0 = -1 ; episode 2 (terminal win): delta = 10 - 4 = 6
    assert d["adv"].tolist() == pytest.approx([-1.0, 0.0, 6.0])
    assert d["ret"].tolist() == pytest.approx([3.0, 4.0, 10.0])
    assert ro.count == 0 and not ro.pending
