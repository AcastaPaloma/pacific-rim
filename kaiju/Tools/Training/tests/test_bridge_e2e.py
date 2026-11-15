"""T-041 end-to-end: real bridge process (dotnet build on first use), PPO smoke, brain_log@v1 validity, determinism."""
import json
import os

import pytest

from kaiju_training import smoke_train
from kaiju_training.bridge import BridgeProcess

pytestmark = pytest.mark.skipif(os.system("dotnet --version > /dev/null 2>&1") != 0, reason="dotnet SDK not available")


def test_hello_layout_matches_csharp(tmp_path):
    with BridgeProcess({"n_envs": 2, "base_seed": 5, "scenario": "m007_3v4", "duration_s": 20}, str(tmp_path)) as br:
        L = br.layout
        assert (L.obs_dim, L.mask_dim, L.ev_dim, L.nt, L.na, L.n_maneuvers, L.n_speeds, L.n_envs) == (313, 19, 13, 8, 7, 7, 3, 2)
        b = br.recv()
        assert b.decide.sum() == 2 and b.mask[:, 0].all()
        br.send([[0, 0, 0], [0, 0, 0]])
        b = br.recv()
        assert sorted(b.env[b.decide].tolist()) == [0, 1]
    assert br.proc.returncode == 0


def test_smoke_train_end_to_end_and_deterministic(tmp_path):
    reps = []
    for run in ("a", "b"):
        out = tmp_path / run
        assert smoke_train.main(["--updates", "5", "--out", str(out)]) == 0
        rep = json.loads((out / "smoke_report.json").read_text())
        logs = sorted((out / "brain_logs").glob("*.jsonl"))
        reps.append((rep, [p.read_bytes() for p in logs]))
    (r1, l1), (r2, l2) = reps
    bl = r1["brain_logs"]
    assert bl["valid"] == bl["files"] == 4 and not bl["errors"]
    assert bl["brain_ids"] == ["ppo-smoke@0"]
    assert all(p["leaks"] == 0 for p in bl["primitives"].values())  # C4 by construction (masked targets)
    assert r1["decisions"] >= 4800  # every env finished its 60 s episode
    assert r1["weights_sha256"] == r2["weights_sha256"]  # lockstep batching: thread timing cannot change the run
    assert l1 == l2
