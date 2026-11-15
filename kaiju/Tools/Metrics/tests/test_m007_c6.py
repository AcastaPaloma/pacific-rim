"""M-007 C6 (R-028 section 11, T-041b O4): duplicate-format pairing, outcome order, Wilson bound, edge-time diagnostic."""
import json
import math

from kaiju_metrics.brain import c6
from kaiju_metrics.brain.brain_log import load_brain_log
from kaiju_metrics.runner import run_metric

STEP_S = 0.02


def write_run(path, brain_id, seed, killed, kaiju_alive=3, dmg_per_kill=100.0, t_last=100.0, mechs=3, x=0.0, scenario="m007_3v4"):
    rows = [{"kind": "header", "schema": "brain_log@v1", "run_id": f"{scenario}_{seed}", "seed": seed, "step_seconds": STEP_S,
             "brain_period_steps": 10, "brain_id": brain_id, "config_hash": "0", "scenario": scenario,
             "size_class": {"H_m": 80, "mass_t": 10000}, "d_eng_m": 400}]
    for m in range(mechs):
        rows.append({"kind": "target_sample", "step": 0, "t": 0.0, "ent": f"ent:{101 + m}", "tkind": "mech", "alive": 1, "x": 0.0, "y": 0.0, "heading_rad": 0.0, "hp": 1.0})
    for k in range(3):
        rows.append({"kind": "decision", "step": 0, "t": 0.0, "ent": f"ent:{k + 1}", "pack": 1, "role": "wing", "alive": 1, "x": x, "y": 0.0,
                     "heading_rad": 0.0, "speed": 1.0, "hp": 1.0, "state": 1, "prev_state": 0, "reason": 0, "target": "ent:101", "tgt_visible": 1,
                     "tgt_mem_age_s": 0.0, "i_heading_rad": 0.0, "i_speed_frac": 0.6, "i_target": "ent:101", "i_action": 0, "score_cur": 0, "score_best": 0})
    for m in range(killed):
        step = int(t_last / STEP_S) - (killed - 1 - m) * 10
        rows.append({"kind": "hit", "step": step, "attacker": "ent:1", "victim": f"ent:{101 + m}", "damage": dmg_per_kill, "victim_hp_after": 0.0})
        rows.append({"kind": "death", "step": step, "ent": f"ent:{101 + m}"})
    for k in range(3 - kaiju_alive):
        rows.append({"kind": "death", "step": 5, "ent": f"ent:{k + 1}"})
    rows.sort(key=lambda r: r.get("step", 0))
    path.write_text("\n".join(json.dumps(r) for r in rows) + "\n")


def load(p):
    lg, e = load_brain_log(str(p)); assert lg is not None, e
    return lg


def test_outcome_order(tmp_path):
    a, b, c, d = (tmp_path / n for n in "abcd")
    write_run(a, "x", 1, 3, t_last=80.0); write_run(b, "x", 1, 3, t_last=90.0)
    write_run(c, "x", 1, 3, kaiju_alive=2, t_last=50.0); write_run(d, "x", 1, 2, t_last=10.0)
    oa, ob, oc, od = (c6.outcome(load(p)) for p in (a, b, c, d))
    assert oa["key"] > ob["key"] > oc["key"] > od["key"]      # faster win > slower win > fewer kaiju alive > fewer mechs killed
    assert (oa["all_dead"], od["all_dead"]) == (1, 0) and abs(oa["t_win_s"] - 80.0) < 0.1


def test_wilson_known_values():
    assert c6.wilson_lower(0, 0) == 0.0
    assert abs(c6.wilson_lower(110, 200) - 0.4812) < 5e-4     # 0.55 observed over 200 is not enough (R-028 gate)
    assert c6.wilson_lower(125, 200) > 0.5 > c6.wilson_lower(110, 200)
    assert abs(c6.wilson_lower(8, 10) - 0.4902) < 5e-4


def _dirs(tmp_path, n, cand_wins, ties=0):
    d = tmp_path / "logs"; d.mkdir()
    for s in range(1, n + 1):
        write_run(d / f"s{s}.jsonl", "scripted@1", s, 2, t_last=100.0)
        if s <= ties:
            write_run(d / f"c{s}.jsonl", "ppo@1", s, 2, t_last=100.0)
        elif s <= ties + cand_wins:
            write_run(d / f"c{s}.jsonl", "ppo@1", s, 3, t_last=100.0)
        else:
            write_run(d / f"c{s}.jsonl", "ppo@1", s, 1, t_last=100.0)
    return d


def sm(rep, sid):
    return next(s for s in rep["submeasures"] if s["id"] == sid)


def _run(tmp_path, d):
    return run_metric("m007", [str(d)], out_dir=str(tmp_path / "out"), images=False, options={"candidate": "ppo@1"})[0]


def test_c6_pass_and_fail(tmp_path):
    rep = _run(tmp_path, _dirs(tmp_path, 200, 130))
    s = sm(rep, "M-007.C6"); assert s["status"] == "PASS", s
    assert abs(s["diagnostics"]["pair_win_rate"] - 0.65) < 1e-9 and s["diagnostics"]["pairs"] == 200
    (tmp_path / "w").mkdir()
    rep = _run(tmp_path / "w", _dirs(tmp_path / "w", 200, 110))   # 0.55 exactly, Wilson LB 0.48: FAIL on the bound
    s = sm(rep, "M-007.C6"); assert s["status"] == "FAIL", s
    assert [p["status"] for p in s["parts"]] == ["PASS", "FAIL"]


def test_c6_not_run_cases(tmp_path):
    assert sm(_run(tmp_path, _dirs(tmp_path, 50, 40)), "M-007.C6")["status"] == "NOT_RUN"          # < 200 pairs
    (tmp_path / "t").mkdir()
    assert sm(_run(tmp_path / "t", _dirs(tmp_path / "t", 200, 40, ties=110)), "M-007.C6")["status"] == "NOT_RUN"  # > 50 % ties


def test_edge_time_diagnostic(tmp_path):
    write_run(tmp_path / "e.jsonl", "x", 1, 0, x=1950.0)
    write_run(tmp_path / "m.jsonl", "x", 2, 0, x=0.0)
    r = c6.edge_time([load(tmp_path / "e.jsonl"), load(tmp_path / "m.jsonl")])
    assert r["decisions"] == 6 and r["at_edge"] == 3 and abs(r["fraction"] - 0.5) < 1e-12


def test_without_candidate_c6_stays_not_applicable(tmp_path):
    d = _dirs(tmp_path, 3, 2)
    rep = run_metric("m007", [str(d / "s1.jsonl")], out_dir=str(tmp_path / "o2"), images=False)[0]
    assert sm(rep, "M-007.C6")["status"] == "NOT_APPLICABLE"
