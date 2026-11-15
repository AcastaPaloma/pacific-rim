"""T-123: campaign simulator (M-104) and Python refit oracle (M-105). The simulator is a port of the shipped Meta rules, so the first tests pin the ported
numbers to the values the C# tests assert (EconomyTests, EconomyTuningTests, MetaTuningTests); then ledger invariants, determinism, the exploit regression
(the legacy floor rules must FAIL M-104 (c), the shipped ones must pass), the gate itself, and the registry files."""
import json
import math
import os
import random
import sys

import pytest
import yaml

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, os.path.join(ROOT, "Tools", "economy"))

import econ_sim as e  # noqa: E402
import m105_refit as m105  # noqa: E402


@pytest.fixture(scope="module")
def world():
    pr = e.Prices()
    return pr, e.Yields(), e.Catalogue(pr)


# ---------------------------------------------------------------- parity with the C# rules
def test_prices_match_the_csharp_tests():
    base = e.Prices(tuned=False)
    assert base.chassis_price("balanced") == 550_800_000 and base.chassis_price("heavy_brawler") == 562_800_000 and base.chassis_price("basic_balanced") == 331_400_000
    assert base.payout_cap == 192_780_000
    assert base.payout(1_200_000_000, 300_000_000) == 54_000_000            # EconomyTests.Payout_TypicalContract_About55M
    assert base.payout(6_450_000_000, 0) == 192_780_000                    # Bay Bridge capped
    assert base.payout(1_000_000_000, 750_000_000) == 15_000_000           # saved exactly 25% pays
    assert base.payout(1_000_000_000, 750_000_001) == 0                    # just under pays nothing
    t = e.Prices()
    assert t.fee == 860 and t.payout(1_200_000_000, 300_000_000) == 77_400_000      # EconomyTuningTests
    assert t.floor_levy_bps == 600 and e.mul_div_floor(t.chassis_price("basic_balanced"), t.floor_levy_bps, 10000) == 19_884_000


def test_repair_cost_rules():
    p = e.Prices()
    assert p.repair_cost(100, 0.96) == 0 and p.repair_cost(100, 0.0) == 100
    assert p.repair_cost(1_000_000, 0.8) == 300_000                         # 1.5 x 0.2, under the light cap 0.5
    assert p.repair_cost(1_000_000, 0.2) == 1_000_000                       # 1.5 x 0.8 = 1.2 -> capped at 1.0 (replace)
    assert p.repair_cost(1_000_000, 0.5) == 750_000 and p.repair_cost(1_000_000, 0.5) <= 900_000


def test_splitmix_is_the_core_generator():
    r = e.SplitMix(0)
    assert [r.below(1 << 62) for _ in range(1)] == [0xE220A8397B1DCDAF % (1 << 62)]


def test_offers_guarantee_and_determinism(world):
    pr = world[0]
    pool = [("a%d" % i, 1_000_000 * (i + 1)) for i in range(10)]               # all trivial: the guarantee must build one big enough set
    a = e.offer_contracts(pr, 5, 3, pool, 3)
    assert a == e.offer_contracts(pr, 5, 3, pool, 3)
    assert any(pr.expected_payout(sum(x[1] for x in o)) >= pr.min_payout or len(o) == len(pool) for o in a)
    rich = [("b%d" % i, 900_000_000) for i in range(6)]
    assert any(pr.expected_payout(sum(x[1] for x in o)) >= pr.min_payout for o in e.offer_contracts(pr, 9, 1, rich, 3))


def test_salvage_headline_and_cap(world):
    pr, y, cat = world
    assert abs((1 - y.frag(392.0)) * y.eta["land"] - 0.8654) < 0.001        # salvage_yields headline 0.86
    assert abs((1 - y.frag(392.0)) * y.eta["shallow_bay"] - 0.673) < 0.001
    b = cat.default_build("balanced")
    parts = [(c, kg, 0.0, 1.0, pr.quote(c, kg)) for pid in b.values() for c, kg in cat.parts[pid]["composition_kg"].items()]
    price = cat.build_price("balanced", b)
    credit, raw, capped, rec = e.assess_mech(y, parts, "land", 392.0, price)
    assert capped and credit == price * 12 // 100 and raw > credit


def test_ledger_never_negative_and_conserves():
    led = e.Ledger(10_000_000)
    rng = random.Random(3)
    for _ in range(2000):
        k = rng.random()
        if k < 0.3:
            led.credit(rng.randint(0, 50_000_000))
        elif k < 0.6:
            led.try_purchase(rng.randint(0, 80_000_000))
        elif k < 0.8:
            led.pay_upkeep(rng.randint(0, 30_000_000))
        else:
            adv = rng.randint(0, 20_000_000)
            led.accept(adv)
            led.settle(adv, rng.choice([0, rng.randint(0, 90_000_000)]))
        led.check()
    assert led.funds >= 0 and led.arrears >= 0


# ---------------------------------------------------------------- the campaign and the gate
def test_campaign_is_deterministic(world):
    pr, y, cat = world
    a = e.Campaign(7, pr, y, cat).run()
    b = e.Campaign(7, pr, y, cat).run()
    assert a.log == b.log and a.led.funds == b.led.funds and a.losses == b.losses


def test_floor_grant_only_when_no_flyable_mech(world):
    pr, y, cat = world
    runs = e.honest_runs(pr, y, cat, 12)
    for c in runs:
        assert c.violations["no_flyable"] == 0 and c.flyable()
        # never stacked: at any time at most one floor hull flyable when no purchased mech exists
        assert sum(1 for m in c.mechs if m.is_floor and not m.lost) <= 1


def test_m104_gate_passes_and_reports_its_model():
    r = e.m104()
    assert r["pass"], json.dumps({k: r[k]["pass"] for k in ("a_pacing", "b_no_unrecoverable", "c_no_exploit")})
    assert e.GATE_MEDIAN_BAND[0] <= r["a_pacing"]["median_sorties"] <= e.GATE_MEDIAN_BAND[1] and r["a_pacing"]["p90"] <= e.GATE_P90_MAX
    assert r["a_pacing"]["unresolved"] == 0 and r["a_pacing"]["replacements"] >= 30
    assert r["b_no_unrecoverable"]["sorties"] >= 30 * 80
    assert all(v["source"] for v in r["sortie_model"].values())
    assert json.dumps(r, default=float) == json.dumps(e.m104(), default=float), "same seeds, same report"


def test_held_out_seeds_agree_with_the_gate_seeds():
    r = e.m104(seeds=100, seed0=5000)
    assert r["a_pacing"]["pass"] and r["b_no_unrecoverable"]["pass"] and r["c_no_exploit"]["pass"]


def test_legacy_floor_rules_fail_the_exploit_gate():
    """Regression for the T-123 finding: a floor wreck that pays credit (and no levy) is an income source."""
    r = e.m104(overrides={"floor_loss_levy_bps": 0}, floor_wreck_credit=True)
    rows = {x["loop"]: x for x in r["c_no_exploit"]["rows"]}
    assert not rows["lose_free_floor_chassis_for_wreck_credit"]["pass"] and rows["lose_free_floor_chassis_for_wreck_credit"]["net_per_iteration"] > 30_000_000
    assert not rows["lose_every_sortie_80_sorties"]["pass"] and not r["pass"]


def test_credit_rule_alone_is_not_enough_levy_is_needed():
    r = e.m104(overrides={"floor_loss_levy_bps": 0}, floor_wreck_credit=False, seeds=100, seed0=5000)
    ctl = r["c_no_exploit"]["rows"][-1]
    assert not ctl["pass"], "without the levy, throwing the free hull away is almost as good as honest play"
    assert ctl["income_ratio"] > e.GATE_CONTROL_INCOME_RATIO


def test_item_loops_lose_at_least_the_gate_share(world):
    pr, y, cat = world
    rows = {r["loop"]: r for r in e.loops(pr, y, cat, False, True)}
    for k in ("lose_purchased_mech_for_wreck_credit", "buy_part_then_sell_back", "repair_then_scrap"):
        assert rows[k]["loss_share_bps"] >= e.GATE_LOOP_LOSS_BPS, k
    assert rows["buy_part_then_sell_back"]["loss_share_bps"] == 10000 - pr.sellback_bps
    assert rows["lose_purchased_mech_for_wreck_credit"]["loss_share_bps"] >= 10000 - 1200 - 1
    for k, r in rows.items():
        if r["kind"] == "earn":
            assert r["net_per_iteration"] <= 0, k


def test_no_scrap_rule_would_be_cheaper_for_the_player_never_better(world):
    pr, y, cat = world
    with_rule = {r["loop"]: r for r in e.loops(pr, y, cat, False, True)}["repair_then_scrap"]
    assert with_rule["scrap_credit_usd"] <= with_rule["item_price"] * 12 // 100


def test_thresholds_in_the_registry_files_equal_the_constants():
    d = yaml.safe_load(open(os.path.join(ROOT, "Tools", "economy", "registry", "M-104@v1.yaml")))
    sm = d["submeasures"]
    t = lambda sid, k: sm[sid]["thresholds"][k]["value"]
    assert t("M-104.pacing", "median_sorties_min") == e.GATE_MEDIAN_BAND[0] and t("M-104.pacing", "median_sorties_max") == e.GATE_MEDIAN_BAND[1]
    assert t("M-104.pacing", "p90_sorties_max") == e.GATE_P90_MAX
    assert t("M-104.item_loops", "loss_share_bps_min") == e.GATE_LOOP_LOSS_BPS
    assert t("M-104.loss_farming_campaign", "seeds_below_honest_share_min") == e.GATE_CONTROL_SEED_SHARE
    assert t("M-104.loss_farming_campaign", "income_ratio_max") == e.GATE_CONTROL_INCOME_RATIO
    d5 = yaml.safe_load(open(os.path.join(ROOT, "Tools", "economy", "registry", "M-105@v1.yaml")))
    assert all(x["thresholds"][k]["value"] == 0 for x in d5["submeasures"].values() for k in x["thresholds"])


def test_tuning_file_keeps_values_assumed_and_sourced():
    d = json.load(open(os.path.join(ROOT, "Assets", "Game", "Config", "Economy", "economy_tuning@v1.json")))
    for k, v in d["parameters"].items():
        assert v["assumed"] is True and len(v["source"]) > 40, k
        assert isinstance(v["value"], int)


# ---------------------------------------------------------------- M-105 python oracle
def test_m105_default_builds_valid_and_corpus_deterministic(tmp_path):
    parts, chassis = m105.load()
    for cid, ch in chassis.items():
        first, mar, _ = m105.judge(ch, parts, ch["default_build"])
        assert first is None, (cid, first)
    a, b = tmp_path / "a.json", tmp_path / "b.json"
    ra = m105.run(60, 9, str(a))
    rb = m105.run(60, 9, str(b))
    assert ra["pass"] and a.read_bytes() == b.read_bytes()


def test_m105_oracle_rejects_a_known_bad_build_with_the_right_first_violation():
    parts, chassis = m105.load()
    ch = chassis["balanced"]
    bad = dict(ch["default_build"])
    for s in ch["slots"]:
        if s["type"] == "back_mount":
            bad[s["name"]] = "back_magazine"          # 800 t of ammunition on the back: heavier than the mass cap or the CoM box allows
    first, mar, tot = m105.judge(ch, parts, bad)
    assert first is not None and mar[first][0] < 0
    assert first == next(k for k in m105.ORDER if mar[k][0] < 0), "first violation follows the check order"


def test_m105_spec_mass_is_sum_of_part_masses():
    parts, chassis = m105.load()
    for cid, ch in chassis.items():
        _, _, tot = m105.judge(ch, parts, ch["default_build"])
        want = sum(sum(parts[p]["composition_kg"].values()) for p in ch["default_build"].values())
        assert abs(tot["mass_kg"] - want) <= 1e-9 * want
