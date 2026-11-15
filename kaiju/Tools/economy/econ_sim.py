#!/usr/bin/env python
"""Campaign simulator and M-104 economy pacing tool (T-123, research/R-104.md sections 6, 7, 8, 11).

The simulator is a line-for-line Python port of the *rules* of the shipped Meta modules (integer USD, basis points, same rounding):
  Meta.Economy  ledger (funds >= 0, arrears, conservation), payout / advance / loss, repair cost, offers (SplitMix generator), floor chassis
  Meta.Salvage  Assess (fragmentation law, reuse / scrap, wreck-credit cap)
  Meta.Roster   permadeath (core destroyed = lost, absorbing), floor grant only when no flyable mech
  Meta.Refit    part prices from parts@v1 compositions, sell-back of removed parts, city-issued parts at nothing
and reads the same committed data files (economy_prices@v1, salvage_yields@v1, parts@v1, chassis@v1). Nothing here changes a rule; the stochastic
sortie model (contract values, damage, loss probability) is the only invented part and every constant of it is listed in SORTIE_MODEL with its
source or an Assumed flag. Thresholds of M-104 are constants in this file and are never read from data or a command line.

Usage:  python econ_sim.py m104 [--seeds 30] [--sorties 80] [--out report.json]    (exit 1 when a gate fails)
        python econ_sim.py sweep --param fee_bps --values 600,860 --seeds 300 --seed0 1000 [--out f.json]   (sensitivity of gate (a), no gate)
"""
from __future__ import annotations

import argparse
import json
import math
import os
import random
import statistics
import sys
from dataclasses import dataclass, field

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
CFG = os.path.join(ROOT, "Assets", "Game", "Config")

MASK64 = (1 << 64) - 1

# ------------------------------------------------------------------------------------------------------------------------------------------
# M-104 thresholds (R-104 section 11). Constants, never read from data; "escalate rather than adjust".
# ------------------------------------------------------------------------------------------------------------------------------------------
GATE_MEDIAN_BAND = (8.0, 16.0)       # (a) median sorties to replace a lost mech (full price from net income)
GATE_P90_MAX = 25.0                  # (a) p90
GATE_LOOP_LOSS_BPS = 6000            # (c) an item-cycling loop must lose at least this share of the item price per iteration
GATE_CONTROL_SEED_SHARE = 0.90       # (c) campaign control: share of seeds in which the loss-farming control ends below honest play
GATE_CONTROL_INCOME_RATIO = 0.60     # (c) and its mean operating income per sortie is at most this share of honest play's
TRIVIAL_ASSETS_USD = (1_000_000, 5_000_000, 10_000_000, 20_000_000)   # a few buildings or one port crane ($10M, R-104 s3 [9])
SEEDS = 30
SORTIES = 80
# honest-play replacement price band context (R-104 section 8): mech loss about once per 20-30 sorties
LOSS_INTERVAL_BAND = (20.0, 30.0)


# ------------------------------------------------------------------------------------------------------------------------------------------
# Sortie model constants (the only invented part). Every row: value, source or Assumed.
# ------------------------------------------------------------------------------------------------------------------------------------------
SORTIE_MODEL = {
    "pool_assets": (10, "Assumed: assets known to the offer generator per sortie"),
    "offers_per_sortie": (3, "Assumed: contracts offered per hangar visit (R-104 s7d needs at least one)"),
    "asset_value_median_usd": (350_000_000, "calibrated so the chosen contract has median V 1.2B (R-104 s3: 0.5B-2.6B p10-p90); the split into assets is Assumed"),
    "asset_value_sigma": (0.7, "R-104 s3: lognormal sigma 0.6 per contract; per-asset sigma Assumed"),
    "damage_beta": ((2.0, 5.0), "R-104 s8 toy: beta-distributed damage; shape calibrated to R-104 s4 typical repair $10-30M per sortie and s8 loss once per 20-30 sorties"),
    "saved_beta": ((6.0, 2.0), "R-104 s3: S = 0.75 typical; beta shape Assumed"),
    "difficulty_exponent": (0.4, "Assumed: threat grows with contract value V as (V / 1.2B)^0.4, clipped 0.6..1.8"),
    "loss_p0": (0.03, "R-104 s8 toy: loss probability 0.03 + 0.12 d^3 x difficulty"),
    "loss_p1": (0.12, "R-104 s8 toy"),
    "floor_effectiveness": (0.70, "R-104 s6 rule 8 (A)"),
    "refit_gain_per_advanced_part": (0.02, "Assumed: effectiveness per catalogue upgrade, cap +0.20"),
    "refit_gain_cap": (0.20, "Assumed"),
    "refit_reserve_share": (0.25, "R-104 s8 toy: surplus above 0.25 x price is spent on refit"),
    "refit_surplus_spend_share": (1.0, "R-104 s8 toy: the whole surplus goes into refit; once the catalogue upgrades are fitted the rest is sunk in sidegrades (sell-back loss), no effectiveness gain"),
    "start_funds_usd": (100_000_000, "Assumed: campaign starting grant, about 0.18 of a Balanced price"),
    "carried_damage_weight": (0.5, "Assumed: unrepaired deficit raises the next sortie's damage by this share"),
    "location_mix": ((0.60, 0.25, 0.15), "Assumed: wreck lies on land / shallow Bay / deep channel"),
    "wreck_energy_j_per_kg_hit": ((10_000.0, 50_000.0), "R-104 s5.1: concentrated hits 10-50 kJ/kg on hit parts"),
    "debris_t_per_m2": (1.0, "Assumed: gross-floor debris tonnes per m2"),
    "debris_steel_share": (0.15, "Assumed: steel share of building debris (R-104 s5.3 by catalogue)"),
}
SLOT_EXPOSURE = {  # Assumed: probability weight that a slot is hit, per unit of mech damage d
    "torso": 0.9, "head": 0.5, "reactor_bay": 0.25, "back_mount": 0.6, "shoulder": 0.7, "upper_arm": 0.8, "forearm": 1.0, "hip_leg": 0.8, "foot": 0.9,
}


# ------------------------------------------------------------------------------------------------------------------------------------------
# Data and Meta.Economy port
# ------------------------------------------------------------------------------------------------------------------------------------------

def load_json(rel):
    with open(os.path.join(CFG, rel), encoding="utf-8") as f:
        return json.load(f)


def mul_div_floor(a, b, c):
    return (a * b) // c


def mul_div_round(a, b, c):
    return (a * b * 2 + c) // (2 * c)


LOSS_STATES = 5  # None, Slight, Moderate, Extensive, Complete


class Prices:
    """Port of PriceTable (economy_prices@v1). `overrides` replaces parameters (used by the sweep and the sensitivity runs only)."""

    def __init__(self, doc=None, overrides=None, tuned=True):
        d = doc or load_json("Economy/economy_prices@v1.json")
        self.doc = d
        self.classes = {c["id"]: c for c in d["classes"]}
        self.per_t = {k: int(v["usd_per_tonne"]) for k, v in self.classes.items()}
        self.chassis = {c["id"]: {k: int(v) for k, v in c["tonnes"].items()} for c in d["chassis"]}
        p = dict(d["parameters"])
        p.setdefault("floor_loss_levy_bps", 0)
        if tuned:
            for k, v in load_json("Economy/economy_tuning@v1.json")["parameters"].items():
                p[k] = int(v["value"])
        if overrides:
            p.update(overrides)
        self.p = p
        self.floor_levy_bps = p["floor_loss_levy_bps"]
        self.fee, self.advance_bps, self.fail_floor = p["fee_bps"], p["advance_bps"], p["failure_floor_saved_bps"]
        self.cap_bps, self.cap_ref, self.floor_chassis = p["cap_bps_of_reference"], p["cap_reference_chassis"], p["floor_chassis"]
        self.sellback_bps, self.upkeep_bps, self.overhead_bps = p["sellback_bps"], p["upkeep_bps_per_sortie"], p["repair_overhead_bps"]
        self.min_payout = p["guaranteed_min_payout_usd"]
        self.loss_bps, self.repair_cap, self.usd_m2 = p["loss_bps"], p["repair_cap_bps"], p["building_usd_per_m2"]
        self.intact, self.light, self.moderate = p["intact_min_integrity_bps"], p["light_min_integrity_bps"], p["moderate_min_integrity_bps"]

    def quote(self, cls, kg):
        return mul_div_round(kg, self.per_t[cls], 1000)

    def chassis_price(self, cid):
        return sum(t * self.per_t[c] for c, t in self.chassis[cid].items())

    @property
    def payout_cap(self):
        return mul_div_floor(self.chassis_price(self.cap_ref), self.cap_bps, 10000)

    def sell_back(self, price):
        return mul_div_floor(price, self.sellback_bps, 10000)

    def upkeep(self, mech_price):
        return mul_div_round(mech_price, self.upkeep_bps, 10000)

    def loss_usd(self, values, states):
        return sum(mul_div_floor(v, self.loss_bps[s], 10000) for v, s in zip(values, states))

    def payout(self, value, loss):
        saved = value - loss
        if saved * 10000 < self.fail_floor * value:
            return 0
        return min(mul_div_floor(saved, self.fee, 10000), self.payout_cap)

    def failed(self, value, loss):
        return self.payout(value, loss) == 0 and (value - loss) * 10000 < self.fail_floor * value

    def expected_payout(self, value):
        return self.payout(value, 0)

    def advance(self, value):
        return mul_div_floor(self.expected_payout(value), self.advance_bps, 10000)

    def repair_cost(self, part_price, integrity):
        i_bps = int(math.floor(integrity * 10000.0 + 0.5))
        if i_bps >= self.intact:
            return 0
        if i_bps == 0:
            return part_price
        raw = mul_div_floor(part_price, (10000 - i_bps) * self.overhead_bps, 100_000_000)
        cap = self.repair_cap[0] if i_bps >= self.light else self.repair_cap[1] if i_bps >= self.moderate else self.repair_cap[2]
        return min(raw, mul_div_floor(part_price, cap, 10000))


class Ledger:
    """Port of the EconomyService ledger rules (credit, purchase, upkeep, arrears, advance settle)."""

    def __init__(self, start=0):
        self.funds = self.arrears = self.credited = self.debited = 0
        self.floor_grants = 0
        if start:
            self.funds = self.credited = start

    def _add(self, a):
        self.funds += a
        self.credited += a

    def _sub(self, a):
        assert a <= self.funds, "ledger: debit exceeds funds"
        self.funds -= a
        self.debited += a

    def credit(self, amount, collect_arrears=True):
        if amount <= 0:
            return
        self._add(amount)
        if collect_arrears and self.arrears > 0:
            take = min(self.arrears, amount)
            self.arrears -= take
            self._sub(take)

    def try_purchase(self, price):
        if price > self.funds:
            return False
        if price > 0:
            self._sub(price)
        return True

    def pay_upkeep(self, amount):
        pay = min(amount, self.funds)
        if pay > 0:
            self._sub(pay)
        self.arrears += amount - pay
        return pay

    def accept(self, advance):
        self._add(advance)  # ContractAdvance: no arrears collection

    def settle(self, advance, payout):
        if payout > 0:
            self.credit(payout)
        if advance > 0:
            pay = min(advance, self.funds)
            if pay > 0:
                self._sub(pay)
            self.arrears += advance - pay

    def check(self):
        assert self.funds >= 0 and self.arrears >= 0, "negative ledger"
        assert self.credited - self.debited == self.funds, "conservation"


class SplitMix:
    def __init__(self, seed):
        self.s = seed & MASK64

    def below(self, n):
        self.s = (self.s + 0x9E3779B97F4A7C15) & MASK64
        z = self.s
        z = ((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9) & MASK64
        z = ((z ^ (z >> 27)) * 0x94D049BB133111EB) & MASK64
        return (z ^ (z >> 31)) % n


def offer_contracts(prices, seed, step, pool, count):
    """Port of EconomyService.OfferContracts: sets of 1..3 pool assets, guarantee of one offer with expected payout >= the minimum."""
    rng = SplitMix((seed & MASK64) ^ ((step * 0x9E3779B97F4A7C15) & MASK64))
    offers = []
    for _ in range(count):
        idx = list(range(len(pool)))
        take = 1 + rng.below(min(3, len(pool)))
        chosen = []
        for k in range(take):
            j = k + rng.below(len(idx) - k)
            idx[k], idx[j] = idx[j], idx[k]
            chosen.append(pool[idx[k]])
        offers.append(chosen)
    if not any(prices.expected_payout(sum(a[1] for a in o)) >= prices.min_payout for o in offers):
        order = sorted(pool, key=lambda a: (-a[1], a[0]))
        s, tot = [], 0
        for a in order:
            s.append(a)
            tot += a[1]
            if prices.expected_payout(tot) >= prices.min_payout:
                break
        offers[-1] = s
    return offers


# ------------------------------------------------------------------------------------------------------------------------------------------
# Meta.Salvage port (mech wrecks and building debris)
# ------------------------------------------------------------------------------------------------------------------------------------------

class Yields:
    def __init__(self, doc=None):
        d = doc or load_json("Salvage/salvage_yields@v1.json")
        p = d["parameters"]
        self.e_c = float(p["comminution_j_per_kg"])
        self.reuse_bps, self.reuse_min = p["reuse_bps"], p["reuse_min_integrity_bps"]
        self.cap_bps = p["credit_cap_bps_of_build_price"]
        self.eta = {l["id"]: l["eta_bps"] / 10000.0 for l in d["locations"]}
        self.cls = {c["id"]: c for c in d["classes"]}

    def frag(self, e_per_kg):
        return 1.0 - math.exp(-e_per_kg / self.e_c) if e_per_kg > 0 else 0.0


def assess_mech(y, parts, location, fall_j_per_kg, build_price):
    """Port of SalvageService.Assess for a Mech wreck. parts: (class, mass_kg, absorbed_j, integrity in 0..1, part_price). Returns (credit, raw, capped, recovered_kg)."""
    eta = y.eta[location]
    raw = 0
    rec_total = 0.0
    for cls, mass, absorbed, integ, price in parts:
        c = y.cls[cls]
        frag = y.frag(fall_j_per_kg + absorbed / mass) if c["fragments"] else 0.0
        rec = mass * (1.0 - frag) * eta
        rec_total += rec
        usd = 0
        if integ * 10000.0 > y.reuse_min and price > 0:
            usd = int(max(0.0, math.floor(price * (y.reuse_bps / 10000.0) * integ * (rec / mass))))
        elif not c["reuse_only"]:
            usd = int(max(0.0, math.floor(rec / 1000.0 * c["scrap_raw_usd_per_tonne"] * c["scrap_bps"] / 10000.0)))
        raw += usd
    cap = build_price * y.cap_bps // 10000
    return (min(raw, cap), raw, raw > cap, rec_total)


# ------------------------------------------------------------------------------------------------------------------------------------------
# Meta.Refit data port: part prices and default builds
# ------------------------------------------------------------------------------------------------------------------------------------------

class Catalogue:
    def __init__(self, prices):
        self.parts = {p["id"]: p for p in load_json("Refit/parts@v1.json")["parts"]}
        ch = load_json("Refit/chassis@v1.json")["chassis"]
        self.chassis = {c["id"]: c for c in ch}
        self.pr = prices
        self.price = {pid: sum(prices.quote(c, kg) for c, kg in p["composition_kg"].items()) for pid, p in self.parts.items()}

    def default_build(self, cid):
        return dict(self.chassis[cid]["default_build"])

    def slot_type(self, cid, slot):
        for s in self.chassis[cid]["slots"]:
            if s["name"] == slot:
                return s["type"]
        raise KeyError(slot)

    def build_price(self, cid, build):
        return sum(self.price[p] for p in build.values())

    def upgrades(self, cid, build):
        """Advanced parts for the chassis that fit a slot type, as (net price, slot, part id), cheapest first, only when the slot does not yet hold one."""
        out = []
        for s in self.chassis[cid]["slots"]:
            cur = self.parts[build[s["name"]]]
            if cur["tier"] in ("advanced", "rare"):
                continue
            for pid, p in self.parts.items():
                if p["tier"] != "advanced" or p["slot_type"] != s["type"] or cid not in p["chassis"]:
                    continue
                net = self.price[pid] - self.pr.sell_back(self.price[build[s["name"]]])
                out.append((net, s["name"], pid))
        out.sort()
        return out


# ------------------------------------------------------------------------------------------------------------------------------------------
# The campaign
# ------------------------------------------------------------------------------------------------------------------------------------------

@dataclass
class Mech:
    chassis: str
    build: dict
    issued: set                 # slots holding city-issued parts (no sell-back)
    damage: dict = field(default_factory=dict)   # slot -> integrity 0..1 (absent = 1)
    is_floor: bool = False
    upgrades: int = 0
    lost: bool = False

    def integrity(self, slot):
        return self.damage.get(slot, 1.0)


@dataclass
class Policy:
    """Honest play. The control policies below override single behaviours."""
    repair: bool = True
    refit: bool = True
    buy_replacement: bool = True
    accept: str = "best"          # best | smallest
    lose_on_purpose: bool = False


class Campaign:
    def __init__(self, seed, prices, yields, cat, policy=None, sorties=SORTIES, floor_wreck_credit=False, scrap_rule=True):
        self.seed, self.pr, self.y, self.cat = seed, prices, yields, cat
        self.rng = random.Random(seed)
        self.policy = policy or Policy()
        self.sorties = sorties
        self.floor_wreck_credit = floor_wreck_credit
        self.scrap_rule = scrap_rule
        self.led = Ledger(SORTIE_MODEL["start_funds_usd"][0])
        self.mechs = []
        self.step = 0
        self.log = []                 # per sortie
        self.losses = []              # dicts: sortie index, price, credit, replaced_after
        self.pending_loss = None
        self.capital = 0              # USD spent on replacement purchases and refit (not operating cost)
        self.violations = {"no_flyable": 0, "negative_funds": 0, "conservation": 0, "no_reachable_contract": 0}
        self.bal = prices.chassis_price("balanced")
        self.add_mech(self.new_mech("balanced", issued=False))

    # -- mechs
    def new_mech(self, cid, issued):
        b = self.cat.default_build(cid)
        return Mech(cid, b, set(b) if issued else set(), is_floor=issued)

    def add_mech(self, m):
        self.mechs.append(m)

    def flyable(self):
        return [m for m in self.mechs if not m.lost]

    def effectiveness(self, m):
        if m.is_floor:
            return SORTIE_MODEL["floor_effectiveness"][0]
        return 1.0 + min(SORTIE_MODEL["refit_gain_cap"][0], SORTIE_MODEL["refit_gain_per_advanced_part"][0] * m.upgrades)

    def best(self):
        fl = self.flyable()
        return max(fl, key=lambda m: (self.effectiveness(m), self.cat.build_price(m.chassis, m.build)))

    def mech_price(self, m):
        return self.cat.build_price(m.chassis, m.build)

    # -- repair
    def repair_queue(self, m):
        q = []
        for slot, integ in m.damage.items():
            price = self.cat.price[m.build[slot]]
            c = self.pr.repair_cost(price, integ)
            if c > 0:
                q.append((c, slot))
        return sorted(q, key=lambda t: (-m.damage[t[1]] * 0 + t[0], t[1]))

    def do_repairs(self, m):
        spent = 0
        order = sorted(self.repair_queue(m), key=lambda t: (t[1] != "torso", t[0]))   # core first, then cheapest first
        for c, slot in order:
            if self.led.try_purchase(c):
                spent += c
                del m.damage[slot]
        # a part under the intact threshold that was free stays as is (no charge, tiny wear carried)
        return spent

    def carried_deficit(self, m):
        tot = 0.0
        w = 0.0
        for s in self.cat.chassis[m.chassis]["slots"]:
            e = SLOT_EXPOSURE[s["type"]]
            w += e
            tot += e * (1.0 - m.integrity(s["name"]))
        return tot / w if w else 0.0

    # -- one sortie
    def pool(self):
        n = SORTIE_MODEL["pool_assets"][0]
        med = SORTIE_MODEL["asset_value_median_usd"][0]
        sig = SORTIE_MODEL["asset_value_sigma"][0]
        return [("a%02d" % i, max(1_000_000, int(med * math.exp(self.rng.gauss(0.0, sig))))) for i in range(n)]

    def damage_states(self, values, saved_target):
        """Hazus damage state per asset so that the expected loss share is 1 - saved_target (probabilistic rounding between the state ratios)."""
        ratios = [b / 10000.0 for b in self.pr.loss_bps]
        out = []
        for _ in values:
            r = min(1.0, max(0.0, self.rng.betavariate(1.0, max(0.05, saved_target / max(0.05, 1 - saved_target))) if saved_target < 0.999 else 0.0))
            # r has mean (1 - saved) when beta(1, s/(1-s)): E = 1 / (1 + s/(1-s)) = 1 - s
            k = max(i for i in range(LOSS_STATES) if ratios[i] <= r) if r < 1.0 else LOSS_STATES - 1
            if k < LOSS_STATES - 1 and self.rng.random() < (r - ratios[k]) / (ratios[k + 1] - ratios[k]):
                k += 1
            out.append(k)
        return out

    def sortie(self):
        i = self.step
        self.step += 1
        led = self.led
        m = self.best() if self.flyable() else None
        if m is None:
            self.violations["no_flyable"] += 1
            return
        # offers
        pool = self.pool()
        offers = offer_contracts(self.pr, self.seed * 1_000_003 + 17, i, pool, SORTIE_MODEL["offers_per_sortie"][0])
        vals = [sum(a[1] for a in o) for o in offers]
        if not any(self.pr.expected_payout(v) >= self.pr.min_payout for v in vals):
            self.violations["no_reachable_contract"] += 1
        def diff(v):
            return min(1.8, max(0.6, (v / 1.2e9) ** SORTIE_MODEL["difficulty_exponent"][0]))
        if self.policy.accept == "smallest":
            k = min(range(len(offers)), key=lambda j: vals[j])
        else:
            k = max(range(len(offers)), key=lambda j: self.pr.expected_payout(vals[j]) / diff(vals[j]))
        v = vals[k]
        advance = self.pr.advance(v)
        led.accept(advance)
        # hangar: repairs before the sortie, after the advance (R-104 s3: the advance is there so a mech can be repaired before a sortie)
        repair_spent = self.do_repairs(m) if self.policy.repair else 0
        # outcome
        eff = self.effectiveness(m)
        dif = diff(v)
        a, b = SORTIE_MODEL["damage_beta"][0]
        d = min(1.0, self.rng.betavariate(a, b) * dif / eff + SORTIE_MODEL["carried_damage_weight"][0] * self.carried_deficit(m))
        p_loss = SORTIE_MODEL["loss_p0"][0] + SORTIE_MODEL["loss_p1"][0] * d ** 3 * dif
        lost = self.policy.lose_on_purpose or self.rng.random() < p_loss
        sa, sb = SORTIE_MODEL["saved_beta"][0]
        s0 = self.rng.betavariate(sa, sb)
        saved = 1.0 - (1.0 - s0) * min(2.0, dif / eff)
        if lost:
            saved *= self.rng.uniform(0.4, 0.9)
        saved = min(1.0, max(0.0, saved))
        states = self.damage_states([a_[1] for a_ in offers[k]], saved)
        loss = self.pr.loss_usd([a_[1] for a_ in offers[k]], states)
        payout = self.pr.payout(v, loss)
        led.settle(advance, payout)
        led.pay_upkeep(self.pr.upkeep(self.mech_price(m)))
        # mech damage
        if lost:
            self.lose_mech(m, i, d)
        else:
            for s in self.cat.chassis[m.chassis]["slots"]:
                if self.rng.random() < min(1.0, d * SLOT_EXPOSURE[s["type"]]):
                    integ = max(0.0, 1.0 - d * self.rng.uniform(0.25, 1.0))
                    m.damage[s["name"]] = min(m.integrity(s["name"]), integ)
                    if s["type"] == "torso" and m.damage[s["name"]] <= 0.0:
                        m.damage[s["name"]] = 0.02    # a surviving mech keeps its core (Roster: core at 0 is the loss)
        # purchases after the sortie
        if self.policy.buy_replacement and not lost:
            self.maybe_buy_replacement(i)
        if self.policy.refit and not lost:
            self.maybe_refit(m)
        if self.policy.buy_replacement and lost:
            self.maybe_buy_replacement(i)
        led.check()
        self.log.append({"i": i, "v": v, "payout": payout, "loss": loss, "repair": repair_spent, "d": d, "lost": lost, "funds": led.funds,
                         "mech": "floor" if m.is_floor else m.chassis, "eff": eff})

    def lose_mech(self, m, i, d):
        m.lost = True
        price = self.mech_price(m)
        # wreck: core dead, other parts damaged by d, absorbed energy on hit parts
        parts = []
        lo, hi = SORTIE_MODEL["wreck_energy_j_per_kg_hit"][0]
        locs = ("land", "shallow_bay", "deep_channel")
        loc = self.rng.choices(locs, weights=SORTIE_MODEL["location_mix"][0])[0]
        for s in self.cat.chassis[m.chassis]["slots"]:
            integ = 0.0 if s["type"] == "torso" else max(0.0, 1.0 - d * self.rng.uniform(0.0, 1.2) * SLOT_EXPOSURE[s["type"]])
            integ = min(integ, m.integrity(s["name"]))
            hit = integ < 0.95
            for c, kg in self.cat.parts[m.build[s["name"]]]["composition_kg"].items():
                e = self.rng.uniform(lo, hi) * kg if hit else 0.0
                parts.append((c, kg, e, integ, self.pr.quote(c, kg)))
        credit, raw, capped, rec = assess_mech(self.y, parts, loc, 392.0, price)
        if m.is_floor and not self.floor_wreck_credit:
            credit = 0
        self.led.credit(credit)
        if m.is_floor:
            self.led.pay_upkeep(mul_div_floor(price, self.pr.floor_levy_bps, 10000))
        self.pending_loss_start(i, m, price, credit)
        # Roster floor rule: a free floor chassis only when no flyable mech remains
        if not self.flyable():
            self.led.floor_grants += 1
            self.add_mech(self.new_mech(self.pr.floor_chassis, issued=True))

    def pending_loss_start(self, i, m, price, credit):
        rec = {"sortie": i, "price": price, "credit": credit, "floor": m.is_floor, "replaced_after": None}
        self.losses.append(rec)
        if not m.is_floor and self.pending_loss is None:
            self.pending_loss = rec

    def maybe_buy_replacement(self, i):
        if self.pending_loss is None:
            return
        if any((not m.lost) and (not m.is_floor) for m in self.mechs):
            return
        price = self.pr.chassis_price("balanced")
        if self.led.try_purchase(price):
            self.capital += price
            self.add_mech(self.new_mech("balanced", issued=False))
            self.pending_loss["replaced_after"] = i - self.pending_loss["sortie"]
            self.pending_loss = None

    def maybe_refit(self, m):
        if m.is_floor or self.pending_loss is not None:
            return
        reserve = int(self.mech_price(m) * SORTIE_MODEL["refit_reserve_share"][0])
        gain = SORTIE_MODEL["refit_gain_per_advanced_part"][0]
        cap = SORTIE_MODEL["refit_gain_cap"][0]
        for _ in range(40):
            ups = self.cat.upgrades(m.chassis, m.build)
            if not ups or m.upgrades * gain >= cap:
                break
            net, slot, pid = ups[0]
            if self.led.funds - net < reserve:
                break
            if not self.led.try_purchase(net):
                break
            self.capital += net
            m.build[slot] = pid
            m.upgrades += 1
            m.issued.discard(slot)
        # remaining surplus: sidegrades (buy a standard part, the old one sells back at the sell-back share): a pure sink
        surplus = int((self.led.funds - reserve) * SORTIE_MODEL["refit_surplus_spend_share"][0])
        if surplus > 0:
            sink = surplus - self.pr.sell_back(surplus)       # what the churn costs: the part price less its sell-back
            pay = min(sink, self.led.funds)
            if self.led.try_purchase(pay):
                self.capital += pay

    def run(self, tail_cap=120):
        for _ in range(self.sorties):
            self.sortie()
        # resolve an open replacement (documented tail, max tail_cap sorties) so a late loss is not silently dropped
        extra = 0
        while self.pending_loss is not None and extra < tail_cap:
            self.sortie()
            extra += 1
        for l in self.losses:
            if l["replaced_after"] is None and not l["floor"]:
                l["censored_at"] = self.step - l["sortie"]
        return self


# ------------------------------------------------------------------------------------------------------------------------------------------
# M-104 sub-gates
# ------------------------------------------------------------------------------------------------------------------------------------------

def pct(xs, q):
    xs = sorted(xs)
    if not xs:
        return float("nan")
    k = (len(xs) - 1) * q
    lo, hi = int(math.floor(k)), int(math.ceil(k))
    return xs[lo] + (xs[hi] - xs[lo]) * (k - lo)


def honest_runs(prices, yields, cat, seeds=SEEDS, sorties=SORTIES, seed0=1, **kw):
    return [Campaign(s, prices, yields, cat, sorties=sorties, **kw).run() for s in range(seed0, seed0 + seeds)]


def gate_a(runs):
    reps, cens = [], 0
    losses_per = []
    for c in runs:
        n = 0
        for l in c.losses:
            if l["floor"]:
                continue
            n += 1
            if l["replaced_after"] is not None:
                reps.append(l["replaced_after"])
            else:
                cens += 1
        losses_per.append(n)
    all_losses = [n for n in losses_per]
    any_losses = sum(len(c.losses) for c in runs)
    sorties_total = sum(len(c.log) for c in runs)
    med, p90 = statistics.median(reps) if reps else float("nan"), pct(reps, 0.90) if reps else float("nan")
    ok = bool(reps) and GATE_MEDIAN_BAND[0] <= med <= GATE_MEDIAN_BAND[1] and p90 <= GATE_P90_MAX and cens == 0
    return {"pass": ok, "median_sorties": med, "p10": pct(reps, 0.10), "p90": p90, "replacements": len(reps), "unresolved": cens,
            "losses_per_campaign_mean": statistics.mean(all_losses), "sorties_per_loss": (SORTIES * len(runs)) / max(1, sum(all_losses)),
            "sorties_per_any_loss": sorties_total / max(1, any_losses), "loss_interval_band": list(LOSS_INTERVAL_BAND),
            "band": list(GATE_MEDIAN_BAND), "p90_max": GATE_P90_MAX, "samples": sorted(reps)}


def gate_b(runs):
    tot = {"no_flyable": 0, "negative_funds": 0, "conservation": 0, "no_reachable_contract": 0}
    for c in runs:
        for k, v in c.violations.items():
            tot[k] += v
        # flyable mech after the last step and at every sortie start is asserted by the counters; conservation and non-negativity by Ledger.check()
        assert c.led.funds >= 0 and c.led.credited - c.led.debited == c.led.funds
        if not c.flyable():
            tot["no_flyable"] += 1
    return {"pass": all(v == 0 for v in tot.values()), "violations": tot, "campaigns": len(runs), "sorties": sum(len(c.log) for c in runs)}


def net_worth(c):
    """Funds minus arrears plus the sell-back value of every purchased (non-issued) part still owned: what the player could take out."""
    v = c.led.funds - c.led.arrears
    for m in c.mechs:
        if m.lost or m.is_floor:
            continue
        for slot, pid in m.build.items():
            if slot not in m.issued:
                v += c.pr.sell_back(c.cat.price[pid])
    return v


def loops(prices, yields, cat, floor_wreck_credit, scrap_rule):
    """Section 7 guard loops, each run through the same rules. Returns rows with net per iteration (USD, + = player gains) and the verdict."""
    rows = []
    bal = prices.chassis_price("balanced")
    flr = prices.chassis_price(prices.floor_chassis)
    rng = random.Random(7)

    def wreck_credit(cid, build, integ_other=1.0, fall=392.0, loc="land", energy=0.0):
        parts = []
        for slot_name, pid in build.items():
            stype = cat.slot_type(cid, slot_name)
            integ = 0.0 if stype == "torso" else integ_other
            for c, kg in cat.parts[pid]["composition_kg"].items():
                parts.append((c, kg, energy * kg, integ, prices.quote(c, kg)))
        price = cat.build_price(cid, build)
        return assess_mech(yields, parts, loc, fall, price)[0], price

    # 1. lose a purchased mech on purpose for the wreck credit (best case: everything but the core intact, land, no extra energy)
    credit, price = wreck_credit("balanced", cat.default_build("balanced"))
    rows.append({"loop": "lose_purchased_mech_for_wreck_credit", "item_price": price, "net_per_iteration": credit - price, "kind": "item",
                 "loss_share_bps": (price - credit) * 10000 // price})
    # 2. lose the free floor chassis on purpose (the city re-grants it): the player's cost is zero, so any credit is income
    credit_f, price_f = wreck_credit(prices.floor_chassis, cat.default_build(prices.floor_chassis))
    net_f = (credit_f if floor_wreck_credit else 0) - mul_div_floor(price_f, prices.floor_levy_bps, 10000)
    rows.append({"loop": "lose_free_floor_chassis_for_wreck_credit", "item_price": price_f, "net_per_iteration": net_f, "kind": "earn", "note": "floor re-grant is free; wreck credit paid = %s" % floor_wreck_credit})
    # 3. buy a part and sell it back
    pid = max(cat.price, key=lambda k: cat.price[k] if cat.parts[k]["tier"] == "advanced" else 0)
    p = cat.price[pid]
    rows.append({"loop": "buy_part_then_sell_back", "item_price": p, "net_per_iteration": prices.sell_back(p) - p, "kind": "item", "loss_share_bps": (p - prices.sell_back(p)) * 10000 // p})
    # 4. repair a heavily damaged part, then scrap the mech (retire with the wreck rule: Assess at fall energy 0, same 12 % cap)
    b = cat.default_build("balanced")
    price = cat.build_price("balanced", b)
    rep = sum(prices.repair_cost(cat.price[pid], 0.4) for pid in b.values())
    if scrap_rule:
        parts = [(c, kg, 0.0, 1.0, prices.quote(c, kg)) for pid in b.values() for c, kg in cat.parts[pid]["composition_kg"].items()]
        scrap_credit = assess_mech(yields, parts, "land", 0.0, price)[0]
    else:
        scrap_credit = 0
    rows.append({"loop": "repair_then_scrap", "item_price": price, "net_per_iteration": scrap_credit - rep - price, "kind": "item", "repair_usd": rep, "scrap_credit_usd": scrap_credit,
                 "loss_share_bps": (price + rep - scrap_credit) * 10000 // price, "note": "scrap rule: wreck assessment at zero fall energy, capped at %d bps of price" % yields.cap_bps})
    # 5. contract-farm a trivial asset, 1000 sorties of the same tiny contract: net per iteration = payout - upkeep (no repair)
    nets = []
    for v in TRIVIAL_ASSETS_USD:
        pay = prices.payout(v, 0)
        nets.append(pay - prices.upkeep(bal))
    rows.append({"loop": "farm_trivial_asset", "item_price": bal, "net_per_iteration": max(nets), "kind": "earn", "assets_usd": list(TRIVIAL_ASSETS_USD), "break_even_asset_usd": prices.upkeep(bal) * 10000 // prices.fee})
    # 6. destroy a protected building for its debris scrap: the loss of payout vs the debris credit (best case: all debris steel, land)
    t_per_m2 = SORTIE_MODEL["debris_t_per_m2"][0]
    steel = yields.cls["debris_steel"]
    per_m2_value = prices.usd_m2
    debris_credit_per_value = t_per_m2 * steel["scrap_raw_usd_per_tonne"] * steel["scrap_bps"] / 10000.0 * yields.eta["land"] / per_m2_value
    for steel_share, tag in ((SORTIE_MODEL["debris_steel_share"][0], "assumed_share"), (1.0, "all_steel_upper_bound")):
        V = 1_000_000_000
        credit_d = int(V * debris_credit_per_value * steel_share)
        lost_pay = prices.payout(V, 0) - prices.payout(V, V)   # a Complete state on the whole asset: payout 0
        rows.append({"loop": "destroy_building_for_debris_" + tag, "item_price": V, "net_per_iteration": credit_d - lost_pay, "kind": "earn", "debris_credit": credit_d, "payout_lost": lost_pay})
    # 7. fail a contract on purpose to keep the advance
    V = 1_200_000_000
    led = Ledger(0)
    adv = prices.advance(V)
    led.accept(adv)
    loss = prices.loss_usd([V], [4])
    led.settle(adv, prices.payout(V, loss))
    rows.append({"loop": "fail_contract_keep_advance", "item_price": adv, "net_per_iteration": led.funds - led.arrears, "kind": "earn", "advance": adv})
    return rows


def gate_c(prices, yields, cat, honest, floor_wreck_credit=False, scrap_rule=True, seed0=1):
    rows = loops(prices, yields, cat, floor_wreck_credit, scrap_rule)
    honest_net = statistics.mean((c.led.funds - SORTIE_MODEL["start_funds_usd"][0]) / max(1, len(c.log)) for c in honest)
    for r in rows:
        if r["kind"] == "item":
            r["pass"] = r["loss_share_bps"] >= GATE_LOOP_LOSS_BPS
        else:
            r["pass"] = r["net_per_iteration"] <= 0
    # whole-campaign control: lose the mech on purpose every sortie. Operating income per sortie (payouts + credits - repairs - upkeep, i.e. funds change
    # plus capital spent) must stay below honest play's, seed by seed, and the control must never end ahead of honest play in net worth.
    ctl = []
    for s in range(seed0, seed0 + len(honest)):
        c = Campaign(s, prices, yields, cat, policy=Policy(lose_on_purpose=True, refit=False, buy_replacement=False), floor_wreck_credit=floor_wreck_credit).run(tail_cap=0)
        h = honest[s - seed0]
        op = lambda x: (x.led.funds - SORTIE_MODEL["start_funds_usd"][0] + x.capital) / max(1, len(x.log))
        ctl.append((op(c), op(h), net_worth(c), net_worth(h)))
    below = sum(1 for a, b, _, _ in ctl if a < b)
    ctl_row = {"loop": "lose_every_sortie_80_sorties", "kind": "campaign", "control_op_income_per_sortie_mean": statistics.mean(x[0] for x in ctl),
               "honest_op_income_per_sortie_mean": statistics.mean(x[1] for x in ctl), "control_net_worth_mean": statistics.mean(x[2] for x in ctl),
               "honest_net_worth_mean": statistics.mean(x[3] for x in ctl), "seeds_below_honest": below, "seeds": len(ctl),
               "income_ratio": statistics.mean(x[0] for x in ctl) / max(1.0, statistics.mean(x[1] for x in ctl)),
               "pass": below >= math.ceil(GATE_CONTROL_SEED_SHARE * len(ctl)) and statistics.mean(x[0] for x in ctl) <= GATE_CONTROL_INCOME_RATIO * statistics.mean(x[1] for x in ctl)}
    rows.append(ctl_row)
    return {"pass": all(r["pass"] for r in rows), "rows": rows, "honest_net_per_sortie": honest_net}


def m104(seeds=SEEDS, sorties=SORTIES, overrides=None, floor_wreck_credit=False, scrap_rule=True, seed0=1):
    prices = Prices(overrides=overrides)
    yields = Yields()
    cat = Catalogue(prices)
    runs = honest_runs(prices, yields, cat, seeds, sorties, seed0, floor_wreck_credit=floor_wreck_credit)
    a, b = gate_a(runs), gate_b(runs)
    c = gate_c(prices, yields, cat, runs, floor_wreck_credit, scrap_rule, seed0)
    return {"metric": "M-104@v1", "seeds": seeds, "seed0": seed0, "sorties": sorties, "fee_bps": prices.fee, "advance_bps": prices.advance_bps,
            "floor_wreck_credit": floor_wreck_credit, "scrap_rule": scrap_rule,
            "a_pacing": a, "b_no_unrecoverable": b, "c_no_exploit": c, "pass": a["pass"] and b["pass"] and c["pass"],
            "honest_final_funds_mean": statistics.mean(r.led.funds for r in runs),
            "repair_per_sortie_median": statistics.median([l["repair"] for r in runs for l in r.log]),
            "sortie_model": {k: {"value": v[0], "source": v[1]} for k, v in SORTIE_MODEL.items()}}


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument("cmd", choices=["m104", "sweep"])
    ap.add_argument("--seeds", type=int, default=SEEDS)
    ap.add_argument("--sorties", type=int, default=SORTIES)
    ap.add_argument("--param", default="fee_bps", help="sweep: price parameter to vary (fee_bps, advance_bps, floor_loss_levy_bps)")
    ap.add_argument("--values", default="600,700,800,860,900,1000")
    ap.add_argument("--seed0", type=int, default=1)
    ap.add_argument("--fee-bps", type=int)
    ap.add_argument("--advance-bps", type=int)
    ap.add_argument("--legacy-floor-credit", action="store_true", help="floor wrecks pay the wreck credit (behaviour before the T-123 rule)")
    ap.add_argument("--no-scrap-rule", action="store_true")
    ap.add_argument("--out")
    a = ap.parse_args(argv)
    ov = {}
    if a.fee_bps is not None:
        ov["fee_bps"] = a.fee_bps
    if a.advance_bps is not None:
        ov["advance_bps"] = a.advance_bps
    if a.legacy_floor_credit:
        ov["floor_loss_levy_bps"] = 0
    if a.cmd == "sweep":
        rows = []
        for v in [int(x) for x in a.values.split(",")]:
            o = dict(ov)
            o[a.param] = v
            prices = Prices(overrides=o)
            runs = honest_runs(prices, Yields(), Catalogue(prices), a.seeds, a.sorties, a.seed0, floor_wreck_credit=a.legacy_floor_credit)
            g = gate_a(runs)
            rows.append({a.param: v, "median": g["median_sorties"], "p10": g["p10"], "p90": g["p90"], "replacements": g["replacements"], "unresolved": g["unresolved"],
                         "sorties_per_loss": g["sorties_per_loss"], "gate_a": g["pass"]})
            print(json.dumps(rows[-1]))
        if a.out:
            os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
            with open(a.out, "w", newline="\n") as f:
                f.write(json.dumps({"param": a.param, "seed0": a.seed0, "seeds": a.seeds, "sorties": a.sorties, "rows": rows}, indent=1, default=float) + "\n")
        return 0
    r = m104(a.seeds, a.sorties, ov or None, a.legacy_floor_credit, not a.no_scrap_rule, a.seed0)
    s = json.dumps(r, indent=1, default=float)
    if a.out:
        os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
        with open(a.out, "w", newline="\n") as f:
            f.write(s + "\n")
    summ = r["a_pacing"]
    print("M-104@v1 fee %d bps advance %d bps  seeds %d x %d sorties" % (r["fee_bps"], r["advance_bps"], r["seeds"], r["sorties"]))
    print("(a) pacing: median %.1f (band %s) p10 %.1f p90 %.1f (max %.0f) replacements %d unresolved %d, %.1f sorties per loss -> %s" % (
        summ["median_sorties"], summ["band"], summ["p10"], summ["p90"], summ["p90_max"], summ["replacements"], summ["unresolved"], summ["sorties_per_loss"], "PASS" if summ["pass"] else "FAIL"))
    print("(b) invariants: %s -> %s" % (r["b_no_unrecoverable"]["violations"], "PASS" if r["b_no_unrecoverable"]["pass"] else "FAIL"))
    for row in r["c_no_exploit"]["rows"]:
        print("(c) %-52s %s %s" % (row["loop"], "PASS" if row["pass"] else "FAIL", {k: v for k, v in row.items() if k in ("net_per_iteration", "loss_share_bps", "seeds_below_honest", "control_op_income_per_sortie_mean", "honest_op_income_per_sortie_mean", "control_net_worth_mean", "honest_net_worth_mean")}))
    print("M-104@v1 %s" % ("PASS" if r["pass"] else "FAIL"))
    return 0 if r["pass"] else 1


if __name__ == "__main__":
    sys.exit(main())
