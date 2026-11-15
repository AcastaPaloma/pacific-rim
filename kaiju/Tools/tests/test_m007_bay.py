"""M-007@v2 tool: synthetic brain_log@v2 traces that pass and fail each clause. Run:
/opt/miniconda3/envs/kaiju/bin/python -m pytest kaiju/Tools/tests/test_m007_bay.py -q"""
import copy
import json
import math
import os
import sys

import pytest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import m007_bay as mb  # noqa: E402

SS, PSTEPS = 0.1, 10      # one tick = 1 s


def small_cfg(**kv):
    """Test config: only the *minimum sample counts* are reduced so one synthetic run can reach them; no pass threshold changes."""
    c = copy.deepcopy(mb.CFG)
    c["T"].update({"C3.min_switches": 5, "P2.min_changes": 2, "P3.min_events": 1, "C6.seeds": 200})
    c["T"].update(kv)
    c["bootstrap_resamples"] = 200
    return c


class L:
    """brain_log@v2 builder. Ticks are 1 s; step = tick * 10."""

    def __init__(self, brain, scenario="hunt", comp="LMH", seed=1):
        self.rows = [{"kind": "header", "schema": mb.SCHEMA, "run_id": f"{brain}-{seed}", "seed": seed, "step_seconds": SS,
                      "brain_period_steps": PSTEPS, "brain_id": brain, "scenario": "m007_bay_" + scenario,
                      "composition": comp, "config_hash": "0", "call_range_m": 5000.0}]

    def _b(self, k, tick, **kw):
        r = {"kind": k, "step": tick * PSTEPS, "t": float(tick)}
        r.update(kw)
        self.rows.append(r)

    def dec(self, tick, k, x, y, sp="L", role="Flanker", goal="hunt", state=1, prev=1, target=101, reason=0, pack=1,
            vis=1, age=0.0, src="own", hp=1.0, alive=1, heading=0.0, speed=1.0):
        self._b("decision", tick, ent=f"ent:{k}", pack=pack, species=sp, role=role, goal=goal, alive=alive, x=x, y=y,
                heading_rad=heading, speed=speed, hp=hp, state=state, prev_state=prev, reason=reason,
                target=f"ent:{target}", tgt_visible=vis, tgt_mem_age_s=age, board_age_s=0.5, board_source=src, claimed_by=None)

    def mech(self, tick, m, x, y, alive=1, hp=1.0, heading=0.0):
        self._b("target_sample", tick, ent=f"ent:{m}", tkind="mech", alive=alive, x=x, y=y, heading_rad=heading, hp=hp)

    def infra(self, tick, k, cls, hp, hp_max=10.0, x=30000.0, y=0.0):
        self._b("infra", tick, ent=f"ent:{k}", id=f"i{k}", **{"class": cls}, hp=hp, hp_max=hp_max, x=x, y=y)

    def hit(self, tick, a, v, dmg):
        self._b("hit", tick, attacker=f"ent:{a}", victim=f"ent:{v}", damage=dmg, victim_hp_after=0.0)

    def death(self, tick, k):
        self._b("death", tick, ent=f"ent:{k}")

    def call(self, tick, **kw):
        self._b("call", tick, sender="ent:1", delivered_to=["ent:2"], delay_s=1.0, lost=False, dist_m=100.0, range_m=5000.0, **kw)

    def rolech(self, tick, m, a, b, reason="bid"):
        self._b("role_change", tick, member=f"ent:{m}", **{"from": a, "to": b}, reason=reason)

    def goal(self, tick, pack, goal, target=None):
        self._b("goal_change", tick, pack=pack, goal=goal, target=None if target is None else f"ent:{target}")

    def aim(self, tick, mech, k):
        self._b("mech_target", tick, mech=f"ent:{mech}", target=f"ent:{k}")

    def runs(self):
        return self.rows


def pos(b, r=250.0):
    return r * math.cos(math.radians(b)), r * math.sin(math.radians(b))


def to_run(lg, tmp_path, name="a"):
    p = tmp_path / f"{name}.jsonl"
    lg.rows[1:] = sorted(lg.rows[1:], key=lambda r: r["step"])   # the harness writes in step order
    p.write_text("\n".join(json.dumps(r) for r in lg.rows) + "\n")
    return mb.load_run(str(p))


def ev(logs, tmp_path, cfg=None):
    runs = [to_run(lg, tmp_path, f"r{i}") for i, lg in enumerate(logs)]
    rep = mb.evaluate(runs, cfg or small_cfg())
    return {c["clause"]: c for c in rep["clauses"]}, rep


CAND = "scriptedpack@1"


def engage(brain, bearings, targets=None, commit=True, scenario="hunt", seed=1, comp="LMH", roles=None, species="LMH"):
    """3 kaiju around mech 101 (tick 0 approach, tick 1 kaiju 1 enters Charge, tick 2 hold). 2 mechs for focus."""
    lg = L(brain, scenario, comp, seed)
    targets = targets or [101] * len(bearings)
    roles = roles or ["Flanker"] * len(bearings)
    for tick in range(3):
        lg.mech(tick, 101, 0.0, 0.0)
        lg.mech(tick, 102, 0.0, 100.0)
        for k, b in enumerate(bearings, 1):
            x, y = pos(b)
            st = 3 if (commit and tick >= 1 and k == 1) else 1
            prev = 1 if (tick == 1 and k == 1) else (3 if st == 3 else 1)
            lg.dec(tick, k, x, y, sp=species[(k - 1) % len(species)], role=roles[k - 1], state=st, prev=prev, target=targets[k - 1])
    return lg


# ------------------------------------------------------------------ units and loader
def test_units():
    assert mb.spread_deg([0, 120, 240]) == pytest.approx(240.0)
    assert mb.spread_deg([0, 10, 20]) == pytest.approx(20.0)
    assert mb.ff_chance(2, 2) == pytest.approx(0.75)
    assert mb.wilson(50, 100)[0] < 0.5 < mb.wilson(50, 100)[1]
    assert mb.roles_covered(["Anchor", "Flanker", "Bait"], "hunt")
    assert not mb.roles_covered(["Flanker", "Flanker", "Flanker"], "hunt")
    assert mb.roles_covered(["Flanker", "Bait"], "hunt") and not mb.roles_covered(["Bait", "Bait"], "hunt")
    assert mb.roles_covered(["Raider"], "raid") and not mb.roles_covered(["Raider", "Flanker", "Flanker"], "raid")


def test_loader_rejects_v1_and_missing_fields(tmp_path):
    p = tmp_path / "v1.jsonl"
    p.write_text(json.dumps({"kind": "header", "schema": "brain_log@v1"}) + "\n")
    with pytest.raises(mb.InputError):
        mb.load_run(str(p))
    lg = L(CAND)
    lg.rows.append({"kind": "decision", "step": 0, "t": 0.0})
    with pytest.raises(mb.InputError) as e:
        to_run(lg, tmp_path)
    assert "missing" in str(e.value)


def test_cli_exit_codes(tmp_path, capsys):
    lg = engage(CAND, [0, 120, 240])
    (tmp_path / "x.jsonl").write_text("\n".join(json.dumps(r) for r in lg.rows) + "\n")
    assert mb.main([str(tmp_path), "--out", str(tmp_path / "rep.json")]) == 1   # most clauses NOT_RUN: not all pass
    assert json.loads((tmp_path / "rep.json").read_text())["spec"] == "M-007@v2"
    (tmp_path / "bad.jsonl").write_text("{}\n")
    assert mb.main([str(tmp_path / "bad.jsonl")]) == 2


# ------------------------------------------------------------------ C1
def test_c1_pass_and_fail(tmp_path):
    good = ev([engage(CAND, [0, 120, 240]), engage("directcharge@1", [0, 10, 20])], tmp_path)[0]["C1"]
    assert good["status"] == "PASS", good
    bad = ev([engage(CAND, [0, 10, 20]), engage("directcharge@1", [0, 10, 20])], tmp_path)[0]["C1"]
    assert bad["status"] == "FAIL"
    # null as good as the candidate: ratio-to-null clause fails even if fr_commit passes
    same = ev([engage(CAND, [0, 120, 240]), engage("directcharge@1", [0, 120, 240])], tmp_path)[0]["C1"]
    assert same["status"] == "FAIL"


def test_c1_uses_species_engagement_radius(tmp_path):
    # kaiju 1,2 are Light (300 m) at 350 m: not engaged, so no multi-commit
    lg = L(CAND)
    for tick in range(3):
        lg.mech(tick, 101, 0, 0)
        for k, b in enumerate([0, 180], 1):
            x, y = pos(b, 350.0)
            lg.dec(tick, k, x, y, sp="L", state=3 if tick else 1, prev=1 if tick == 1 else 3 if tick else 1)
    c = ev([lg], tmp_path)[0]["C1"]
    assert c["status"] == "NOT_RUN"
    lg2 = L(CAND)
    for tick in range(3):
        lg2.mech(tick, 101, 0, 0)
        for k, b in enumerate([0, 180], 1):
            x, y = pos(b, 350.0)
            lg2.dec(tick, k, x, y, sp="M", state=3 if tick else 1, prev=1 if tick == 1 else 3 if tick else 1)
    assert ev([lg2], tmp_path)[0]["C1"]["measures"][0]["value"] == 1.0   # Mid 500 m: engaged (no null control: clause NOT_RUN)


def test_composition_guard_fails_a_bad_composition():
    runs = [dict(comp="LL", commits_flanked=0, commits_multi=1) for _ in range(200)] + \
           [dict(comp="MH", commits_flanked=1, commits_multi=1) for _ in range(200)]
    bad, tab = mb.wil_guard(runs, "commits_flanked", "commits_multi", ">=", 0.6)
    assert bad == ["LL"] and "MH" in tab
    assert mb.ratio(runs, "commits_flanked", "commits_multi") == pytest.approx(0.5)


# ------------------------------------------------------------------ C2
def test_c2_focus(tmp_path):
    focus = engage(CAND, [0, 120, 240], targets=[101, 101, 101])
    nofoc = engage("nofocus@1", [0, 120, 240], targets=[101, 102, 101])
    c = ev([focus, nofoc], tmp_path)[0]["C2"]
    assert c["status"] == "PASS", c
    same = ev([engage(CAND, [0, 120, 240], targets=[101, 102, 101]), engage("nofocus@1", [0, 120, 240], targets=[101, 102, 101])], tmp_path)[0]["C2"]
    assert same["status"] == "FAIL"
    assert ev([focus], tmp_path)[0]["C2"]["status"] == "NOT_RUN"      # no independent control


# ------------------------------------------------------------------ C3
def churn(brain, period, n_ticks=600):
    lg = L(brain, "churn")
    for tick in range(n_ticks):
        lg.mech(tick, 101, 0, 0)
        lg.mech(tick, 102, 0, 50)
        tg = 101 + (tick // period) % 2 if period < 10 ** 6 else 101
        sw = tick > 0 and tick % period == 0
        lg.dec(tick, 1, 100.0, 0.0, target=tg, reason=11 if sw else 0)
    return lg


def test_c3_thrash_fails_and_calm_passes(tmp_path):
    bad = ev([churn(CAND, 5)], tmp_path)[0]["C3"]
    assert bad["status"] == "FAIL"
    calm = ev([churn(CAND, 100)], tmp_path)[0]["C3"]     # 5 switches in 10 kaiju-min, no A-B-A within 12 s
    assert calm["status"] == "PASS", calm


def test_c3_not_run_below_min_switches(tmp_path):
    assert ev([churn(CAND, 100)], tmp_path, mb.CFG)[0]["C3"]["status"] == "NOT_RUN"


# ------------------------------------------------------------------ C4
def test_c4_leaks(tmp_path):
    ok = engage(CAND, [0, 120, 240])
    assert ev([ok], tmp_path)[0]["C4"]["status"] == "PENDING_CONTROL"      # candidate clean, omniscient control missing
    leak = engage(CAND, [0, 120, 240])
    leak.rows[-1].update(tgt_visible=0, tgt_mem_age_s=9.0)
    assert ev([leak], tmp_path)[0]["C4"]["status"] == "FAIL"
    prov = engage(CAND, [0, 120, 240])
    prov.rows[-1]["board_source"] = "omniscient"
    assert ev([prov], tmp_path)[0]["C4"]["status"] == "FAIL"
    late = engage(CAND, [0, 120, 240])
    late.call(2, )
    late.rows[-1]["delay_s"] = 0.0
    late.rows[-1]["dist_m"] = 3430.0
    assert ev([late], tmp_path)[0]["C4"]["status"] == "FAIL"
    beyond = engage(CAND, [0, 120, 240])
    beyond.call(2, )
    beyond.rows[-1]["dist_m"] = 9000.0
    beyond.rows[-1]["delay_s"] = 30.0
    assert ev([beyond], tmp_path)[0]["C4"]["status"] == "FAIL"


def test_c4_omniscient_must_be_flagged(tmp_path):
    om = engage("omniscient@1", [0, 120, 240])
    om.rows[-1]["board_source"] = "omniscient"
    c, rep = ev([engage(CAND, [0, 120, 240]), om], tmp_path)
    assert c["C4"]["status"] == "PASS" and rep["informational"]["runs"]
    blind = engage("omniscient@1", [0, 120, 240])
    assert ev([engage(CAND, [0, 120, 240]), blind], tmp_path)[0]["C4"]["status"] == "NON_DISCRIMINATING"


# ------------------------------------------------------------------ C5 panel
def ratings(cheap_cand=2, smart_cand=6, cheap_om=6, smart_solo=3):
    out = []
    for r in range(12):
        j = r % 3
        for arm, sm, ch in (("candidate", smart_cand + (j == 0) * 0, cheap_cand + j % 2), ("solo@1", smart_solo + j % 2, 3),
                            ("omniscient@1", 5, cheap_om + j % 2), ("directcharge@1", 2, 4)):
            for clip in range(2):
                out.append({"rater": r, "clip": clip, "arm": arm, "smart": sm, "cheap": ch})
    return out


def test_c5_panel_states():
    cfg = small_cfg()
    assert mb.panel_clause(None, cfg)["status"] == "PENDING"
    assert mb.panel_clause(ratings(), cfg)["status"] == "PASS"
    assert mb.panel_clause(ratings(smart_cand=4), cfg)["status"] == "FAIL"
    assert mb.panel_clause(ratings(cheap_cand=4), cfg)["status"] == "FAIL"
    assert mb.panel_clause(ratings(cheap_om=1), cfg)["status"] == "FAIL"        # cheat not detected: instrument void
    assert mb.panel_clause([r for r in ratings() if r["rater"] < 5], cfg)["status"] == "NOT_RUN"


# ------------------------------------------------------------------ C6 win rate
def _pairs(wins, n, policy="policy@1"):
    pr = []
    for s in range(n):
        for b, sc in ((policy, 1.0 if s < wins else 0.0), (CAND, 0.5)):
            pr.append({"brain": b, "scenario": "m007_bay_hunt", "comp": "LMH", "seed": s, "S": sc})
    return pr


def test_c6_win_rate():
    cfg = small_cfg()
    assert mb.winrate_clause([], cfg)["status"] == "PENDING"
    assert "T-041" in mb.winrate_clause([], cfg)["note"]
    assert mb.winrate_clause(_pairs(150, 200), cfg)["status"] == "PASS"
    assert mb.winrate_clause(_pairs(100, 200), cfg)["status"] == "FAIL"
    assert mb.winrate_clause(_pairs(112, 200), cfg)["status"] == "FAIL"        # 0.56 but Wilson lower bound <= 0.50 fails? see below
    assert mb.winrate_clause(_pairs(50, 100), cfg)["status"] == "NOT_RUN"


def test_c6_ties_count_half():
    pr = _pairs(0, 200)
    for r in pr:
        if r["brain"] == "policy@1":
            r["S"] = 0.5
    assert mb.winrate_clause(pr, small_cfg())["status"] == "FAIL"


def test_c6_pending_in_evaluate(tmp_path):
    assert ev([engage(CAND, [0, 120, 240])], tmp_path)[0]["C6"]["status"] == "PENDING"


# ------------------------------------------------------------------ P1 role coverage
def pack_run(brain, roles, species="LMH", ticks=5, scenario="hunt", seed=1, goal="hunt"):
    lg = L(brain, scenario, "LMH", seed)
    for t in range(ticks):
        lg.mech(t, 101, 0, 0)
        for k, r in enumerate(roles, 1):
            x, y = pos(120 * k)
            lg.dec(t, k, x, y, sp=species[k - 1], role=r, goal=goal)
    return lg


def test_p1_coverage_and_fit(tmp_path):
    good = ev([pack_run(CAND, ["Bait", "Flanker", "Anchor"])], tmp_path)[0]["P1"]
    assert good["status"] in ("PASS", "PENDING_CONTROL"), good
    nocover = ev([pack_run(CAND, ["Flanker", "Flanker", "Bait"])], tmp_path)[0]["P1"]
    assert nocover["status"] == "FAIL"             # no Anchor
    badfit = ev([pack_run(CAND, ["Anchor", "Flanker", "Bait"])], tmp_path)[0]["P1"]   # L Anchor .1, M Flanker .8, H Bait 0
    assert badfit["status"] == "FAIL" and badfit["composition_fail"]


def test_p1_non_discriminating_when_randomrole_is_as_good(tmp_path):
    c = ev([pack_run(CAND, ["Bait", "Flanker", "Anchor"]), pack_run("randomrole@1", ["Bait", "Flanker", "Anchor"])], tmp_path)[0]["P1"]
    assert c["status"] == "NON_DISCRIMINATING"
    c2 = ev([pack_run(CAND, ["Bait", "Flanker", "Anchor"]), pack_run("randomrole@1", ["Flanker", "Flanker", "Flanker"])], tmp_path)[0]["P1"]
    assert c2["status"] == "PASS"


# ------------------------------------------------------------------ P2 role stability
def test_p2_role_stability(tmp_path):
    ok = pack_run(CAND, ["Bait", "Flanker", "Anchor"], ticks=600)
    ok.rolech(100, 1, "Bait", "Flanker")
    ok.rolech(300, 2, "Flanker", "Anchor")
    ok.rolech(500, 3, "Anchor", "Flanker")
    assert ev([ok], tmp_path)[0]["P2"]["status"] == "PASS"
    th = pack_run(CAND, ["Bait", "Flanker", "Anchor"], ticks=120)
    for i in range(6):
        th.rolech(10 + i * 10, 1, "Bait" if i % 2 == 0 else "Flanker", "Flanker" if i % 2 == 0 else "Bait")
    c = ev([th], tmp_path)[0]["P2"]
    assert c["status"] == "FAIL"
    forced = pack_run(CAND, ["Bait", "Flanker", "Anchor"])
    forced.rolech(2, 1, "Bait", "Flanker", reason="death")
    assert ev([forced], tmp_path)[0]["P2"]["status"] == "NOT_RUN"        # forced changes are exempt and do not count
    assert ev([ok], tmp_path, mb.CFG)[0]["P2"]["status"] == "NOT_RUN"    # 3 < 100 under the real minimum


# ------------------------------------------------------------------ P3 re-allocation
def loss_run(survivor_roles, delay):
    lg = L(CAND, "hunt", "LMH")
    for t in range(40):
        lg.mech(t, 101, 0, 0)
        for k, r in enumerate(["Anchor", "Flanker", "Bait"], 1):
            x, y = pos(120 * k)
            if k == 1 and t >= 5:
                lg.dec(t, 1, x, y, role="Anchor", alive=0, state=5)
                continue
            role = r
            if k > 1:
                role = survivor_roles[k - 2] if t < 5 + delay else ("Flanker" if k == 2 else "Bait")
            lg.dec(t, k, x, y, role=role)
        if t == 5:
            lg.death(5, 1)
    return lg


def test_p3_reallocation(tmp_path):
    fast = ev([loss_run(["Flanker", "Bait"], 0)], tmp_path)[0]["P3"]
    assert fast["status"] == "PASS", fast
    slow = ev([loss_run(["Bait", "Bait"], 20)], tmp_path)[0]["P3"]
    assert slow["status"] == "FAIL" and slow["info"]["median_Lr_s"] == pytest.approx(20.0)
    assert ev([fast and loss_run(["Flanker", "Bait"], 0)], tmp_path, mb.CFG)[0]["P3"]["status"] == "NOT_RUN"


# ------------------------------------------------------------------ P4 bait-and-strike
def bait_run(brain, strike_bearing, roles=("Bait", "Flanker")):
    lg = L(brain, "hunt", "LMH")
    for t in range(8):
        lg.mech(t, 101, 0, 0)
        x1, y1 = pos(0)
        lg.dec(t, 1, x1, y1, role=roles[0])
        x2, y2 = pos(strike_bearing)
        st, pv = (3, 1) if t == 4 else ((3, 3) if t > 4 else (1, 1))
        lg.dec(t, 2, x2, y2, role=roles[1], state=st, prev=pv)
        if t < 5:
            lg.aim(t, 101, 1)
    return lg


def test_p4_bait_and_strike(tmp_path):
    good = ev([bait_run(CAND, 180), bait_run("nobait@1", 20, roles=("Flanker", "Flanker"))], tmp_path)[0]["P4"]
    assert good["status"] == "PASS", good
    nostrike = ev([bait_run(CAND, 20), bait_run("nobait@1", 20, roles=("Flanker", "Flanker"))], tmp_path)[0]["P4"]
    assert nostrike["status"] == "FAIL"
    equal = ev([bait_run(CAND, 180), bait_run("nobait@1", 180, roles=("Flanker", "Flanker"))], tmp_path)[0]["P4"]
    assert equal["status"] == "FAIL"        # not >= 2 x nobait
    short = bait_run(CAND, 180)
    short.rows = [r for r in short.rows if not (r["kind"] == "mech_target" and r["t"] >= 2)]
    assert ev([short], tmp_path)[0]["P4"]["status"] == "NOT_RUN"         # aimed < 3 s: not an opportunity


# ------------------------------------------------------------------ P5 synergy
def kx_run(brain, seed, mech_dmg, kaiju_dmg):
    lg = pack_run(brain, ["Bait", "Flanker", "Anchor"], ticks=3, seed=seed)
    lg.mech(0, 102, 0, 10)
    lg.hit(1, 2, 101, mech_dmg)
    lg.hit(1, 101, 3, kaiju_dmg)
    return lg


def test_p5_synergy(tmp_path):
    good = ev([kx_run(CAND, s, 0.6, 0.1) for s in range(1, 4)] + [kx_run("noroles@1", s, 0.3, 0.1) for s in range(1, 4)], tmp_path)[0]["P5"]
    assert good["status"] == "PASS", good
    bad = ev([kx_run(CAND, s, 0.3, 0.1) for s in range(1, 4)] + [kx_run("noroles@1", s, 0.3, 0.1) for s in range(1, 4)], tmp_path)[0]["P5"]
    assert bad["status"] == "FAIL"


# ------------------------------------------------------------------ P6 participation
def part_run(brain, hider):
    lg = L(brain, "hunt", "LMH")
    for t in range(20):
        lg.mech(t, 101, 0, 0)
        for k in (1, 2, 3):
            x, y = pos(120 * k, 5000.0 if (hider and k == 3) else 250.0)
            lg.dec(t, k, x, y, sp="LMH"[k - 1])
    return lg


def test_p6_participation(tmp_path):
    assert ev([part_run(CAND, False)], tmp_path)[0]["P6"]["status"] == "PASS"
    hid = ev([part_run(CAND, True)], tmp_path)[0]["P6"]
    assert hid["status"] == "FAIL" and hid["measures"][0]["value"] == pytest.approx(2 / 3)


# ------------------------------------------------------------------ I1 raid completion, I3 efficiency
def raid_run(brain, hp_left, seed=1, raider=True, scenario="raid"):
    lg = L(brain, scenario, "LMH", seed)
    lg.goal(0, 1, "raid", target=501)
    for t in range(10):
        lg.infra(t, 501, "bridge", 10.0 if t < 5 else hp_left, x=3000.0)
        lg.infra(t, 502, "crane", 10.0, x=9000.0)
        lg.dec(t, 1, 2500.0, 0.0, role="Raider" if raider else "Flanker", goal="raid", target=501)
    return lg


def test_i1_raid_completion(tmp_path):
    ok = ev([raid_run(CAND, 1.0)], tmp_path)[0]["I1"]
    assert ok["status"] == "PASS" and ok["info"]["median_time_to_first_destruction_s"] == 5.0
    assert ev([raid_run(CAND, 8.0)], tmp_path)[0]["I1"]["status"] == "FAIL"        # only 20 % destroyed
    assert ev([raid_run(CAND, 1.0, raider=False)], tmp_path)[0]["I1"]["status"] == "NOT_RUN"


def test_i3_raid_efficiency(tmp_path):
    good = ev([raid_run(CAND, 1.0, s) for s in (1, 2)] + [raid_run("nearestinfra@1", 6.0, s) for s in (1, 2)], tmp_path)[0]["I3"]
    assert good["status"] == "PASS", good
    bad = ev([raid_run(CAND, 6.0, s) for s in (1, 2)] + [raid_run("nearestinfra@1", 6.0, s) for s in (1, 2)], tmp_path)[0]["I3"]
    assert bad["status"] == "FAIL"


# ------------------------------------------------------------------ I2 arbitration
def mixed_run(abandon, idle, comp="LMH"):
    lg = L(CAND, "mixed", comp)
    lg.goal(0, 1, "hunt")
    for t in range(150):
        near = t >= 130
        mx = 500.0 if near else 20000.0
        lg.mech(t, 101, mx, 0.0)
        for k in (1, 2):
            x = 10.0 * k + (0 if idle else 10.0 * t if not near else 0)
            if near:
                lg.dec(t, k, 10.0 * k, 0.0, goal="raid" if abandon else "hunt", heading=math.pi, speed=5.0,
                       state=1 if abandon else 3, prev=1 if abandon else 3)
            else:
                lg.dec(t, k, x, 0.0, goal="hunt", state=1)
    return lg


def test_i2_abandonment_and_idle(tmp_path):
    good = ev([mixed_run(False, False)], tmp_path)[0]["I2"]
    assert good["status"] == "PASS", good
    ab = ev([mixed_run(True, False)], tmp_path)[0]["I2"]
    assert ab["status"] == "FAIL" and ab["measures"][0]["value"] == 1.0
    idle = ev([mixed_run(False, True)], tmp_path)[0]["I2"]
    assert idle["status"] == "FAIL" and idle["measures"][1]["value"] == 1.0


# ------------------------------------------------------------------ F1 formation
def emerge_run(packs, spread=1000.0, size_extra=0):
    lg = L(CAND, "emerge", "LMH")
    n = len(packs)
    for t in range(700):
        for k, p in enumerate(packs, 1):
            lg.dec(t, k, spread * k, 0.0, pack=p, role="Free", goal="free", state=0, target=0)
    return lg


def test_f1_formation(tmp_path):
    assert ev([emerge_run([1, 1, 1])], tmp_path)[0]["F1"]["status"] == "PASS"
    one = ev([emerge_run([1, 1, 0])], tmp_path)[0]["F1"]            # one kaiju left solo: 1/3 valid pack share
    assert one["status"] == "FAIL"
    over = ev([emerge_run([1] * 6)], tmp_path)[0]["F1"]
    assert over["status"] == "FAIL" and over["measures"][1]["value"] == 1
    lg = emerge_run([1, 1, 1])
    for r in lg.rows:       # kaiju 3 joins pack 2 (members 1,2 relabelled) from 9 km away at tick 300
        if r["kind"] == "decision" and r["ent"] in ("ent:1", "ent:2") and r["t"] >= 300:
            r["pack"] = 2
        if r["kind"] == "decision" and r["ent"] == "ent:3" and r["t"] >= 300:
            r["pack"] = 2
            r["x"] = 12000.0
    far = ev([lg], tmp_path)[0]["F1"]
    assert far["status"] == "FAIL" and far["measures"][2]["value"] >= 1


# ------------------------------------------------------------------ I4 and informational, instruments
def test_i4_is_informational_and_species_table(tmp_path):
    c, rep = ev([part_run(CAND, False)], tmp_path)
    assert c["I4"]["status"] == "INFORMATIONAL"
    assert set(rep["informational"]["per_species"]) == {"L", "M", "H"}
    assert rep["informational"]["per_species"]["L"]["engaged_decisions"] > 0


def test_omniscient_gap_reported(tmp_path):
    a = kx_run(CAND, 1, 0.3, 0.1)
    b = kx_run("omniscient@1", 1, 0.6, 0.1)
    _, rep = ev([a, b], tmp_path)
    assert rep["informational"]["omniscient_gap_delta_S"]["value"] > 0


def test_thresholds_match_r103():
    T = mb.CFG["T"]
    assert (T["C1.fr_commit"], T["C2.fe"], T["C3.thrash"], T["C3.rate"], T["C3.min_switches"]) == (0.60, 0.50, 0.10, 3.0, 200)
    assert (T["C6.win"], T["C6.seeds"], T["P1.cov"], T["P3.share"], T["P4.rate"], T["P5.ratio"]) == (0.55, 200, 0.90, 0.90, 0.40, 1.25)
    assert (T["I1.rate"], T["I2.ab"], T["I2.id"], T["I3.ratio"], T["F1.share"], T["P2.min_changes"], T["P3.min_events"]) == (0.80, 0.10, 0.20, 1.20, 0.90, 100, 30)
