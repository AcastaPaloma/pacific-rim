"""T-106c: a broken worker pool must not abandon the rest of a county (Sonoma 172/208 and Contra Costa 331/339 tiles were left unfilled)."""
import json
import sys
from concurrent.futures import Future
from concurrent.futures.process import BrokenProcessPool
from pathlib import Path
from types import SimpleNamespace

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import fetch_lidar_bay as B  # noqa: E402


class FakePool:
    """Synchronous stand-in: the first time `crash_on` is submitted the whole 'pool' breaks (every future in flight raises)."""
    crash_on, crashed, instances = None, False, 0

    def __init__(self, workers):
        FakePool.instances += 1
        self.dead = False

    def __enter__(self):
        return self

    def __exit__(self, *a):
        return False

    def submit(self, fn, arg):
        f = Future()
        if self.dead or (arg[0] == FakePool.crash_on and not FakePool.crashed):
            FakePool.crashed = self.dead = True
            f.set_exception(BrokenProcessPool("a process was terminated abruptly"))
        else:
            f.set_result({"tile": arg[0], "status": "done", "secs": 0.1, "bytes_out": 1, "satisfied": 1, "targets": 1})
        return f


def _args(**kw):
    d = dict(tile=None, county=None, no_retry_failed=False, limit=None, cell_m=128, max_run=4, threads=1, validate=False,
             workers=2, min_free_gb=0.0, max_restarts=5)
    d.update(kw)
    return SimpleNamespace(**d)


def _setup(tmp_path, monkeypatch, n=5):
    plan = {f"L0_10_{k:02d}": {"n_default": 5, "has_rind": False, "projects": ["p"], "county": "X", "area_default": 100.0 - k} for k in range(n)}
    (tmp_path / "plan.json").write_text(json.dumps({"tiles": plan}))
    monkeypatch.setattr(B, "WORK", tmp_path)
    monkeypatch.setattr(B, "PLAN", tmp_path / "plan.json")
    monkeypatch.setattr(B, "STATUS", tmp_path / "status.jsonl")
    monkeypatch.setattr(B, "ProcessPoolExecutor", FakePool)
    FakePool.crashed, FakePool.instances = False, 0
    return list(plan)


def test_pool_break_records_failed_rebuilds_pool_and_finishes_the_county(tmp_path, monkeypatch):
    ids = _setup(tmp_path, monkeypatch)
    FakePool.crash_on = ids[1]
    rc = B.cmd_run(_args())
    last = B.read_status()
    assert rc == 1  # a failure was recorded, so the chain script retries
    assert FakePool.instances == 2  # the pool was rebuilt once
    assert last[ids[1]]["status"] == "failed" and "pool broken" in last[ids[1]]["error"]
    assert all(last[t]["status"] == "done" for t in ids if t != ids[1])  # nothing after the crash was abandoned


def test_retry_run_completes_the_failed_tile(tmp_path, monkeypatch):
    ids = _setup(tmp_path, monkeypatch)
    FakePool.crash_on = ids[1]
    B.cmd_run(_args())
    assert B.cmd_run(_args()) == 0  # the failed tile is re-queued, the crash is spent
    assert all(r["status"] == "done" for r in B.read_status().values())


def test_endlessly_breaking_pool_stops_with_exit_4(tmp_path, monkeypatch):
    class Always(FakePool):
        def submit(self, fn, arg):
            f = Future(); f.set_exception(BrokenProcessPool("x")); return f
    _setup(tmp_path, monkeypatch)
    monkeypatch.setattr(B, "ProcessPoolExecutor", Always)
    assert B.cmd_run(_args(max_restarts=2)) == 4
