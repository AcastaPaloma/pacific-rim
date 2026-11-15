"""Client of the headless C# bridge (R-028 section 5.2). Little-endian binary lockstep protocol.

bridge -> python  hello: magic, protocol, layout, obs_dim, mask_dim, ev_dim, nt, na, n_maneuvers, n_speeds, n_envs (int32 x 11)
bridge -> python  batch: int32 nrows, rows of (int32 env, agent, kind, episode, f32[ev_dim], f32[obs_dim], u8[mask_dim])
python -> bridge  reply: int32 n (= number of kind 0 rows, -1 = quit), then int32[3 n] (target, maneuver, speed) in row order
kind 0 = decide (events accumulated since this agent's previous decision), kind 2 = episode end for that agent."""
from __future__ import annotations

import json
import os
import struct
import subprocess
from dataclasses import dataclass

import numpy as np

MAGIC = 0x4B4A4252
PROTOCOL = 1
KIND_DECIDE, KIND_END = 0, 2
EV_NAMES = ("dmg_dealt", "dmg_taken", "kills_mine", "team_kills", "ally_deaths", "i_died", "win", "loss", "timeout",
            "voluntary_switch", "elapsed_s", "commit_flanked", "commit_multi")

TRAINING_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BRIDGE_DIR = os.path.join(TRAINING_DIR, "bridge")
BRIDGE_DLL = os.path.join(BRIDGE_DIR, "bin", "Release", "net11.0", "KaijuBridge.dll")


@dataclass(frozen=True)
class Layout:
    layout_version: int
    obs_dim: int
    mask_dim: int
    ev_dim: int
    nt: int
    na: int
    n_maneuvers: int
    n_speeds: int
    n_envs: int

    @property
    def n_targets(self):
        return self.nt + 1


@dataclass
class Batch:
    env: np.ndarray      # int32 [n]
    agent: np.ndarray
    kind: np.ndarray
    episode: np.ndarray
    ev: np.ndarray       # float32 [n, ev_dim]
    obs: np.ndarray      # float32 [n, obs_dim]
    mask: np.ndarray     # bool [n, mask_dim]

    @property
    def decide(self):
        return self.kind == KIND_DECIDE


def row_dtype(layout: Layout) -> np.dtype:
    return np.dtype([("env", "<i4"), ("agent", "<i4"), ("kind", "<i4"), ("episode", "<i4"),
                     ("ev", "<f4", (layout.ev_dim,)), ("obs", "<f4", (layout.obs_dim,)), ("mask", "u1", (layout.mask_dim,))])


def parse_hello(buf: bytes) -> Layout:
    v = struct.unpack("<11i", buf)
    if v[0] != MAGIC:
        raise RuntimeError(f"bad bridge magic {v[0]:#x}")
    if v[1] != PROTOCOL:
        raise RuntimeError(f"bridge protocol {v[1]} != {PROTOCOL}")
    return Layout(*v[2:])


def parse_rows(buf: bytes, layout: Layout, n: int) -> Batch:
    a = np.frombuffer(buf, dtype=row_dtype(layout), count=n)
    return Batch(a["env"].copy(), a["agent"].copy(), a["kind"].copy(), a["episode"].copy(), a["ev"].copy(),
                 a["obs"].copy(), a["mask"].astype(bool))


def pack_actions(actions: np.ndarray) -> bytes:
    actions = np.ascontiguousarray(actions, dtype="<i4").reshape(-1, 3)
    return struct.pack("<i", actions.shape[0]) + actions.tobytes()


def build_bridge(force: bool = False) -> str:
    """dotnet build -c Release once (incremental); returns the dll path."""
    if force or not os.path.isfile(BRIDGE_DLL) or os.environ.get("KAIJU_BRIDGE_REBUILD") == "1":
        subprocess.run(["dotnet", "build", "-c", "Release", "--nologo", "-v", "q", BRIDGE_DIR], check=True,
                       stdout=subprocess.DEVNULL)
    return BRIDGE_DLL


class BridgeProcess:
    """Spawns `dotnet KaijuBridge.dll --config <json>`; one object per vectorised env set."""

    def __init__(self, cfg: dict, work_dir: str, build: bool = True):
        os.makedirs(work_dir, exist_ok=True)
        self.cfg_path = os.path.join(work_dir, "bridge.json")
        with open(self.cfg_path, "w") as f:
            json.dump(cfg, f, indent=1, sort_keys=True)
        dll = build_bridge() if build else BRIDGE_DLL
        self.stderr_path = os.path.join(work_dir, "bridge.stderr.log")
        self._err = open(self.stderr_path, "wb")
        self.proc = subprocess.Popen(["dotnet", dll, "--config", self.cfg_path], stdin=subprocess.PIPE,
                                     stdout=subprocess.PIPE, stderr=self._err, bufsize=0)
        self.layout = parse_hello(self._read(44))
        self._rowsize = row_dtype(self.layout).itemsize
        self._expect = 0
        self._awaiting = False  # True after send(): the bridge is writing the next batch

    def _read(self, n: int) -> bytes:
        chunks, got = [], 0
        while got < n:
            c = self.proc.stdout.read(n - got)
            if not c:
                raise RuntimeError(f"bridge closed the pipe (exit {self.proc.poll()}); see {self.stderr_path}")
            chunks.append(c)
            got += len(c)
        return b"".join(chunks)

    def recv(self) -> Batch:
        (n,) = struct.unpack("<i", self._read(4))
        b = parse_rows(self._read(n * self._rowsize), self.layout, n)
        self._expect = int(b.decide.sum())
        self._awaiting = False
        return b

    def send(self, actions: np.ndarray):
        actions = np.asarray(actions).reshape(-1, 3)
        if actions.shape[0] != self._expect:
            raise ValueError(f"expected {self._expect} actions, got {actions.shape[0]}")
        self.proc.stdin.write(pack_actions(actions))
        self.proc.stdin.flush()
        self._awaiting = True

    def close(self) -> int:
        try:
            if self.proc.poll() is None:
                if self._awaiting:  # drain the batch in flight, else a batch larger than the pipe buffer deadlocks
                    self.recv()
                self.proc.stdin.write(struct.pack("<i", -1))
                self.proc.stdin.flush()
                self.proc.wait(timeout=30)
        except (BrokenPipeError, subprocess.TimeoutExpired):
            self.proc.kill()
            self.proc.wait()
        self._err.close()
        return self.proc.returncode

    def __enter__(self):
        return self

    def __exit__(self, *exc):
        self.close()
