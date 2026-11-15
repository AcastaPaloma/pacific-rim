using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Game.Core;
using Game.Meta.Economy;

namespace Game.Meta.Economy.Tests
{
    public class EconomyTests
    {
        static readonly PlayerSlot S0 = PlayerSlot.FromIndex(0), S1 = PlayerSlot.FromIndex(1);

        // ---------------------------------------------------------------- flag, data file, module

        [Test] public void Flag_FileDefaultsOff()
        {
            string t = File.ReadAllText(Path.Combine(Kit.Root(), "Assets", "Game", "Config", "FeatureFlags", "Meta.json"));
            StringAssert.Contains("\"meta.economy\": false", t);
            Assert.AreEqual(1, System.Text.RegularExpressions.Regex.Matches(t, "\"meta\\.").Count);
            var snap = FeatureFlagLoader.Parse(t, "Meta.json", new CollectingErrorSink());
            Assert.IsFalse(snap.IsOn(EconomyFlags.Economy));
        }

        [Test] public void Install_OnlyWhenFlagOn()
        {
            var reg = new ServiceRegistry();
            Assert.IsNull(EconomyModule.Install(reg, FeatureFlagSnapshot.AllOff, Kit.Prices()));
            Assert.IsFalse(reg.IsRegistered<IEconomyService>());
            var on = new FeatureFlagSnapshot(new[] { new KeyValuePair<string, bool>(EconomyFlags.Economy, true) });
            var svc = EconomyModule.Install(reg, on, Kit.Prices());
            Assert.IsNotNull(svc); Assert.AreSame(svc, reg.Resolve<IEconomyService>());
        }

        [Test] public void PriceFile_EveryRowHasSourceAndAssumedFlag()
        {
            var t = Kit.Prices();
            foreach (var c in t.Classes.Values) { Assert.IsNotEmpty(c.Source); }
            foreach (var a in t.FixedAssets.Values) Assert.IsNotEmpty(a.Source);
            Assert.AreEqual(7, t.Classes.Count);
        }

        [Test] public void PriceFile_BadInputsRejected()
        {
            string good = Kit.PricesText();
            Assert.Throws<FormatException>(() => PriceTable.Parse("{}"));
            Assert.Throws<FormatException>(() => PriceTable.Parse(good.Replace("economy_prices@v1\",\n  \"source\"", "economy_prices@v2\",\n  \"source\"")));
            Assert.Throws<FormatException>(() => PriceTable.Parse(good.Replace("\"fee_bps\": 600", "\"fee_bps\": 60000")));
            Assert.Throws<FormatException>(() => PriceTable.Parse(good.Replace("[0, 200, 1000, 5000, 10000]", "[0, 200, 100, 5000, 10000]")));
            Assert.Throws<FormatException>(() => PriceTable.Parse(good.Replace("\"floor_chassis\": \"basic_balanced\"", "\"floor_chassis\": \"nope\"")));
            Assert.Throws<FormatException>(() => PriceTable.Parse(good.Replace("\"structure\": 3200", "\"unobtainium\": 3200")));
        }

        // ---------------------------------------------------------------- price function (R-104 section 2)

        [Test] public void Prices_ChassisMatchR104Headline()
        {
            var t = Kit.Prices();
            Assert.AreEqual(550_800_000L, t.ChassisPriceUsd("balanced"));      // R-104: about $551M
            Assert.AreEqual(562_800_000L, t.ChassisPriceUsd("heavy_brawler")); // about $563M
            Assert.AreEqual(331_400_000L, t.ChassisPriceUsd("basic_balanced"));// about $331M
            Assert.AreEqual(9000, t.ChassisMassTonnes("balanced"));
            Assert.AreEqual(11500, t.ChassisMassTonnes("heavy_brawler"));
            Assert.AreEqual(8300, t.ChassisMassTonnes("basic_balanced"));
            Assert.AreEqual(192_780_000L, t.PayoutCapUsd);                      // 35% of Balanced, R-104: about $193M
        }

        [Test] public void Prices_PartIsMassTimesClassIndex()
        {
            var t = Kit.Prices();
            var q = t.QuotePart("reactor", 900_000);   // 900 t
            Assert.AreEqual(180_000_000L, q.PriceUsd); Assert.AreEqual(200_000L, q.UsdPerTonne); Assert.IsFalse(q.Assumed);
            Assert.AreEqual(0L, t.QuotePart("armour", 0).PriceUsd);
            Assert.AreEqual(10_000L, t.QuotePart("structure", 2500).PriceUsd);   // 2.5 t x 4000
            Assert.Throws<ArgumentException>(() => t.QuotePart("mithril", 1));
            // linearity: price(a) + price(b) == price(a+b) when masses are whole tonnes
            for (long a = 0; a < 5000; a += 1000) Assert.AreEqual(t.QuotePart("titanium", a).PriceUsd + t.QuotePart("titanium", 7000).PriceUsd, t.QuotePart("titanium", a + 7000).PriceUsd);
            Assert.AreEqual(4000L, t.QuotePart("structure", 1000).PriceUsd);
        }

        [Test] public void Prices_SellBackAndUpkeep()
        {
            var t = Kit.Prices();
            Assert.AreEqual(220_320_000L, t.SellBackUsd(t.ChassisPriceUsd("balanced")));   // 40%
            Assert.AreEqual(2_203_200L, t.UpkeepPerSortieUsd(t.ChassisPriceUsd("balanced"))); // 0.4%, R-104: about $2.2M
            Assert.AreEqual(6000L * 1200 * 10, t.BuildingValueUsd(1200, 10));
        }

        [Test] public void Prices_RepairCost()
        {
            var t = Kit.Prices();
            const long P = 100_000_000;
            Assert.AreEqual(0L, t.RepairCostUsd(P, 1.0));
            Assert.AreEqual(0L, t.RepairCostUsd(P, 0.95));                   // intact boundary
            Assert.AreEqual(15_000_000L, t.RepairCostUsd(P, 0.9));           // 1.5 x 0.1
            Assert.AreEqual(50_000_000L, t.RepairCostUsd(P, 0.66));          // light: 1.5 x 0.34 = 0.51 -> cap 0.5
            Assert.AreEqual(75_000_000L, t.RepairCostUsd(P, 0.5));           // moderate: 1.5 x 0.5, under the 0.9 cap
        }

        [Test] public void Prices_RepairCostCapsAndReplacement()
        {
            var t = Kit.Prices();
            const long P = 1_000_000;
            Assert.AreEqual(P, t.RepairCostUsd(P, 0.0));                     // destroyed = replace
            Assert.AreEqual(P, t.RepairCostUsd(P, 0.2));                     // heavy: 1.5 x 0.8 = 1.2 -> cap 1.0
            Assert.AreEqual(750_000L, t.RepairCostUsd(P, 0.5));              // moderate: 1.5 x 0.5, under cap 0.9
            Assert.AreEqual(900_000L, t.RepairCostUsd(P, 0.4));              // 1.5 x 0.6 = 0.9 = cap
            for (int k = 0; k <= 100; k++)
            {
                long c = t.RepairCostUsd(P, k / 100.0);
                Assert.LessOrEqual(c, P); Assert.GreaterOrEqual(c, 0);
                if (k < 100) Assert.GreaterOrEqual(c, t.RepairCostUsd(P, (k + 1) / 100.0), "cost never rises with integrity at " + k);
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => t.RepairCostUsd(P, 1.1));
            Assert.Throws<ArgumentOutOfRangeException>(() => t.RepairCostUsd(P, double.NaN));
        }

        // ---------------------------------------------------------------- payout model vs R-104 section 3 worked examples

        [Test] public void Payout_TypicalContract_About55M()
        {
            var t = Kit.Prices();
            // V = $1.2B, 75% saved (R-104): payout = 0.06 x 0.9B = $54M ("about $55M"), under the cap.
            Assert.AreEqual(54_000_000L, t.Payout(1_200_000_000L, 300_000_000L));
            // Median value and the p10/p90 of the quoted band: V 0.5B, 2.6B at S = 0.75 -> $22.5M / $117M
            Assert.AreEqual(22_500_000L, t.Payout(500_000_000L, 125_000_000L));
            Assert.AreEqual(117_000_000L, t.Payout(2_600_000_000L, 650_000_000L));
        }

        [Test] public void Payout_CapAndFailureFloor()
        {
            var t = Kit.Prices();
            Assert.AreEqual(192_780_000L, t.Payout(6_450_000_000L, 0));                  // Bay Bridge: 0.06 x 6.45B = 387M -> capped
            Assert.AreEqual(0L, t.Payout(1_000_000_000L, 760_000_000L));                 // saved 24% < 25%
            Assert.IsTrue(t.Failed(1_000_000_000L, 760_000_000L));
            Assert.AreEqual(15_000_000L, t.Payout(1_000_000_000L, 750_000_000L));        // saved exactly 25% pays 0.06 x 250M
            Assert.IsFalse(t.Failed(1_000_000_000L, 750_000_000L));
            Assert.AreEqual(0L, t.Payout(1_000_000_000L, 1_000_000_000L));
            Assert.Throws<ArgumentOutOfRangeException>(() => t.Payout(10, 11));
        }

        [Test] public void Payout_MonotoneInSavedValue()
        {
            var t = Kit.Prices();
            long prev = long.MaxValue;
            for (long loss = 0; loss <= 1_000_000_000L; loss += 5_000_000L)
            {
                long p = t.Payout(1_000_000_000L, loss);
                Assert.LessOrEqual(p, prev); prev = p;
            }
        }

        [Test] public void Payout_LossFromDamageStates()
        {
            var t = Kit.Prices();
            var a = new[] { new ContractAsset("x", 1_000_000_000L), new ContractAsset("y", 200_000_000L) };
            Assert.AreEqual(0L, t.LossUsd(a, new[] { AssetDamageState.None, AssetDamageState.None }));
            Assert.AreEqual(20_000_000L + 100_000_000L, t.LossUsd(a, new[] { AssetDamageState.Slight, AssetDamageState.Extensive }));
            Assert.AreEqual(1_200_000_000L, t.LossUsd(a, new[] { AssetDamageState.Complete, AssetDamageState.Complete }));
            Assert.Throws<ArgumentException>(() => t.LossUsd(a, new[] { AssetDamageState.None }));
        }

        [Test] public void Payout_Distribution_MatchesR104Band()
        {
            // 400 contracts: V lognormal-ish (sigma 0.6, median 1.2B), saved share from a skewed generator; median payout must sit in the quoted 25M-117M band.
            var t = Kit.Prices(); var r = new Kit.Rng(2024);
            var pays = new List<long>();
            for (int i = 0; i < 400; i++)
            {
                double z = 0; for (int k = 0; k < 12; k++) z += r.Unit(); z -= 6.0;               // ~N(0,1)
                long v = (long)(1_200_000_000.0 * Math.Exp(0.6 * z));
                double saved = 0.55 + 0.4 * Math.Sqrt(r.Unit());                                   // 0.55..0.95, mean about 0.82
                long loss = (long)(v * (1 - saved));
                pays.Add(t.Payout(v, loss));
            }
            pays.Sort();
            long median = pays[pays.Count / 2];
            Assert.That(median, Is.InRange(25_000_000L, 117_000_000L), "median payout " + median);
            Assert.LessOrEqual(pays[pays.Count - 1], t.PayoutCapUsd);
        }

        // ---------------------------------------------------------------- contract lifecycle

        [Test] public void Contract_AcceptAdvanceThenCompletePaysNet()
        {
            var t = Kit.Prices(); var bus = Kit.NewBus(); var h = new Kit.Collect(); bus.Subscribe<ContractCompleted>(h);
            var e = new EconomyService(t, bus);
            var offers = e.OfferContracts(7, 10, new[] { new ContractAsset("gg", 1_000_000_000L), new ContractAsset("cr", 10_000_000L) }, 1);
            var c = offers[0];
            Assert.AreEqual(ContractState.Offered, c.State);
            long expected = t.ExpectedPayout(c.ValueUsd);
            e.Accept(c.Id, S0);
            Assert.AreEqual(t.AdvanceUsd(c.ValueUsd), e.Funds);                         // 10% of the expected payout
            Assert.AreEqual(expected / 10, e.Funds);
            var done = e.Complete(c.Id, new ContractOutcome(Kit.States(c, AssetDamageState.None), 20));
            Assert.AreEqual(ContractState.Completed, done.State);
            Assert.AreEqual(expected, done.PayoutUsd);
            Assert.AreEqual(expected, e.Funds);                                        // advance already received is repaid from the payout: net equals the payout
            Assert.AreEqual(0L, e.Arrears);
            Assert.AreEqual(1, h.Seen.Count);
            Assert.AreEqual(c.Id, h.Seen[0].ContractId); Assert.AreEqual(S0, h.Seen[0].Slot); Assert.AreEqual((byte)ContractResult.Completed, h.Seen[0].Outcome); Assert.AreEqual(expected, h.Seen[0].PayoutUsd);
            Assert.Throws<InvalidOperationException>(() => e.Complete(c.Id, new ContractOutcome(Kit.States(c, AssetDamageState.None), 21)));
            Assert.AreEqual(1, h.Seen.Count, "event published exactly once");
            Assert.Throws<InvalidOperationException>(() => e.Accept(c.Id, S1));
        }

        [Test] public void Contract_FailedForfeitsAdvanceNetZero()
        {
            var t = Kit.Prices(); var e = new EconomyService(t, Kit.NewBus());
            var c = e.OfferContracts(1, 1, new[] { new ContractAsset("gg", 1_000_000_000L) }, 1)[0];
            e.Accept(c.Id, S0);
            long adv = e.Funds; Assert.Greater(adv, 0);
            var done = e.Complete(c.Id, new ContractOutcome(new[] { AssetDamageState.Complete }, 5));
            Assert.AreEqual(ContractState.Failed, done.State); Assert.AreEqual(0L, done.PayoutUsd);
            Assert.AreEqual(0L, e.Funds); Assert.AreEqual(0L, e.Arrears);               // net 0, loop is not a source of money
        }

        [Test] public void Contract_AdvanceSpentThenFailed_BecomesArrearsCollectedFromNextIncome()
        {
            var t = Kit.Prices(); var e = new EconomyService(t, Kit.NewBus());
            var c = e.OfferContracts(1, 1, new[] { new ContractAsset("gg", 1_000_000_000L) }, 1)[0];
            e.Accept(c.Id, S0);
            long adv = e.Funds;
            Assert.IsTrue(e.TryPurchase(adv, TransactionReason.Repair, 2));              // the advance buys a repair
            e.Complete(c.Id, new ContractOutcome(new[] { AssetDamageState.Complete }, 5));
            Assert.AreEqual(0L, e.Funds); Assert.AreEqual(adv, e.Arrears);
            e.Credit(adv + 1000, TransactionReason.SalvageCredit, 6);
            Assert.AreEqual(0L, e.Arrears); Assert.AreEqual(1000L, e.Funds);
            AssertConserved(e);
        }

        [Test] public void Contract_Decline_NoObligation()
        {
            var e = new EconomyService(Kit.Prices());
            var c = e.OfferContracts(3, 1, new[] { new ContractAsset("cr", 10_000_000L) }, 1)[0];
            e.Decline(c.Id);
            Assert.AreEqual(ContractState.Declined, e.GetContract(c.Id).State);
            Assert.AreEqual(0L, e.Funds);
            Assert.Throws<InvalidOperationException>(() => e.Accept(c.Id, S0));
            Assert.Throws<ArgumentException>(() => e.Accept(999, S0));
        }

        [Test] public void Contract_OutcomeMustAlignWithAssets()
        {
            var e = new EconomyService(Kit.Prices());
            var c = e.OfferContracts(3, 1, new[] { new ContractAsset("a", 10_000_000L), new ContractAsset("b", 10_000_000L) }, 1)[0];
            e.Accept(c.Id, S0);
            Assert.Throws<ArgumentException>(() => e.Complete(c.Id, new ContractOutcome(new[] { AssetDamageState.None }, 1)));
            Assert.AreEqual(ContractState.Accepted, e.GetContract(c.Id).State);
        }

        [Test] public void Offers_DeterministicAndGuaranteeOneWorthPlaying()
        {
            var t = Kit.Prices(); var pool = Kit.Pool(t);
            var a = new EconomyService(t).OfferContracts(99, 4, pool, 4);
            var b = new EconomyService(t).OfferContracts(99, 4, pool, 4);
            Assert.AreEqual(4, a.Count);
            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual(a[i].ValueUsd, b[i].ValueUsd);
                Assert.AreEqual(a[i].Assets.Length, b[i].Assets.Length);
                for (int k = 0; k < a[i].Assets.Length; k++) Assert.AreEqual(a[i].Assets[k].AssetId, b[i].Assets[k].AssetId);
            }
            // guarantee over many seeds, including a pool of only small assets
            var small = new List<ContractAsset>(); for (int i = 0; i < 6; i++) small.Add(new ContractAsset("s" + i, 150_000_000L));
            for (long seed = 0; seed < 200; seed++)
            {
                foreach (var p in new[] { pool, small })
                {
                    var offers = new EconomyService(t).OfferContracts(seed, seed, p, 3);
                    bool any = false; foreach (var o in offers) if (t.ExpectedPayout(o.ValueUsd) >= t.GuaranteedMinPayoutUsd) any = true;
                    Assert.IsTrue(any, "seed " + seed);
                }
            }
        }

        [Test] public void Offers_NewBatchWithdrawsOldOffers()
        {
            var t = Kit.Prices(); var e = new EconomyService(t); var pool = Kit.Pool(t);
            var first = e.OfferContracts(1, 1, pool, 2);
            var second = e.OfferContracts(2, 2, pool, 2);
            Assert.AreEqual(ContractState.Declined, e.GetContract(first[0].Id).State);
            Assert.AreEqual(ContractState.Offered, e.GetContract(second[0].Id).State);
            Assert.AreNotEqual(first[0].Id, second[0].Id);
            Assert.Throws<ArgumentException>(() => e.OfferContracts(1, 1, new[] { new ContractAsset("a", 5), new ContractAsset("a", 6) }, 1));
        }

        // ---------------------------------------------------------------- ledger invariants

        static void AssertConserved(EconomyService e)
        {
            Assert.GreaterOrEqual(e.Funds, 0, "funds never negative");
            Assert.GreaterOrEqual(e.Arrears, 0);
            Assert.AreEqual(e.TotalCredited - e.TotalDebited, e.Funds, "conservation: funds == credited - debited");
        }

        [Test] public void Ledger_RandomOperationsKeepInvariants()
        {
            for (ulong seed = 1; seed <= 5; seed++)
            {
                var t = Kit.Prices(); var e = new EconomyService(t, Kit.NewBus(), 50_000_000L); var r = new Kit.Rng(seed); var pool = Kit.Pool(t);
                var accepted = new List<long>();
                for (int i = 0; i < 1500; i++)
                {
                    long step = i;
                    switch (r.Below(7))
                    {
                        case 0: { long amt = r.Below(40_000_000); e.Credit(amt, TransactionReason.SalvageCredit, step); break; }
                        case 1: { long amt = r.Below(120_000_000); long before = e.Funds; bool ok = e.TryPurchase(amt, TransactionReason.Purchase, step); Assert.AreEqual(amt <= before, ok); Assert.AreEqual(ok ? before - amt : before, e.Funds); break; }
                        case 2: { long before = e.Funds, amt = r.Below(60_000_000); long paid = e.PayUpkeep(amt, step); Assert.AreEqual(Math.Min(amt, before), paid); break; }
                        case 3: { var offers = e.OfferContracts((long)r.Next(), step, pool, 3); var c = offers[r.Below(offers.Count)]; e.Accept(c.Id, S0); accepted.Add(c.Id); break; }
                        case 4: if (accepted.Count > 0) { int k = r.Below(accepted.Count); var c = e.GetContract(accepted[k]); accepted.RemoveAt(k);
                                var st = new AssetDamageState[c.Assets.Length]; for (int j = 0; j < st.Length; j++) st[j] = (AssetDamageState)r.Below(5);
                                e.Complete(c.Id, new ContractOutcome(st, step)); } break;
                        case 5: { try { e.TryPurchase(e.Funds + 1, TransactionReason.Purchase, step); } catch (Exception) { Assert.Fail("TryPurchase must not throw"); } break; }
                        case 6: e.TryGrantFloorChassis(S0, r.Below(2), step); break;
                    }
                    AssertConserved(e);
                }
            }
        }

        [Test] public void Ledger_NoNegativeFundsWithoutDebtRule()
        {
            var e = new EconomyService(Kit.Prices(), null, 1000);
            Assert.IsFalse(e.TryPurchase(1001, TransactionReason.Purchase, 1)); Assert.AreEqual(1000L, e.Funds);
            Assert.IsTrue(e.TryPurchase(1000, TransactionReason.Purchase, 2)); Assert.AreEqual(0L, e.Funds);
            Assert.AreEqual(0L, e.PayUpkeep(500, 3)); Assert.AreEqual(500L, e.Arrears); Assert.AreEqual(0L, e.Funds);
            e.Credit(200, TransactionReason.Sale, 4); Assert.AreEqual(300L, e.Arrears); Assert.AreEqual(0L, e.Funds);
            Assert.Throws<ArgumentOutOfRangeException>(() => e.Credit(-1, TransactionReason.Sale, 5));
            Assert.Throws<ArgumentOutOfRangeException>(() => e.TryPurchase(-1, TransactionReason.Purchase, 5));
            AssertConserved(e);
        }

        [Test] public void Ledger_TransactionRingKeepsLast256()
        {
            var e = new EconomyService(Kit.Prices());
            for (int i = 0; i < 600; i++) e.Credit(1, TransactionReason.Other, i);
            var tx = new List<KeyValuePair<long, long>>(); e.CopyTransactions(tx);
            Assert.AreEqual(256, tx.Count); Assert.AreEqual(600L, tx[255].Key); Assert.AreEqual(345L, tx[0].Key);
            Assert.AreEqual(600L, e.Funds);
        }

        // ---------------------------------------------------------------- floor chassis rule (Q-206)

        [Test] public void Floor_GrantedOnlyWhenNoFlyableMech_FreeAndNotStacked()
        {
            var e = new EconomyService(Kit.Prices());
            var none = e.TryGrantFloorChassis(S0, 1, 1);
            Assert.IsFalse(none.Granted); Assert.AreEqual(0L, e.FloorGrantCount(S0));
            var g = e.TryGrantFloorChassis(S0, 0, 2);
            Assert.IsTrue(g.Granted); Assert.AreEqual("basic_balanced", g.ChassisId); Assert.AreEqual(331_400_000L, g.ValueUsd); Assert.AreEqual(1L, g.GrantCount);
            Assert.AreEqual(0L, e.Funds, "free: no funds are spent or needed");
            Assert.IsFalse(e.TryGrantFloorChassis(S0, 1, 3).Granted, "not granted while the slot owns a flyable mech");
            Assert.AreEqual(1L, e.FloorGrantCount(S0));
            Assert.IsTrue(e.TryGrantFloorChassis(S0, 0, 4).Granted); Assert.AreEqual(2L, e.FloorGrantCount(S0));
            Assert.AreEqual(0L, e.FloorGrantCount(S1), "per slot");
            Assert.Throws<ArgumentOutOfRangeException>(() => e.TryGrantFloorChassis(S0, -1, 5));
        }

        [Test] public void Floor_CampaignNeverDeadEnds()
        {
            // 30 campaigns x 80 sorties: the mech is lost at random; the player may be broke. After each loss the floor rule must leave a flyable mech
            // and a contract worth playing must exist; funds never go negative.
            for (ulong seed = 1; seed <= 30; seed++)
            {
                var t = Kit.Prices(); var e = new EconomyService(t, Kit.NewBus(), 0); var r = new Kit.Rng(seed); var pool = Kit.Pool(t);
                int flyable = 1;
                for (int sortie = 0; sortie < 80; sortie++)
                {
                    var offers = e.OfferContracts((long)seed, sortie, pool, 3);
                    bool playable = false; foreach (var o in offers) if (t.ExpectedPayout(o.ValueUsd) >= t.GuaranteedMinPayoutUsd) playable = true;
                    Assert.IsTrue(playable, "contract worth playing at sortie " + sortie);
                    var c = offers[r.Below(offers.Count)]; e.Accept(c.Id, S0);
                    var st = new AssetDamageState[c.Assets.Length]; for (int j = 0; j < st.Length; j++) st[j] = (AssetDamageState)r.Below(4);
                    e.Complete(c.Id, new ContractOutcome(st, sortie));
                    if (r.Unit() < 0.15) flyable = 0;                                  // mech lost
                    var g = e.TryGrantFloorChassis(S0, flyable, sortie);
                    if (g.Granted) flyable = 1;
                    Assert.GreaterOrEqual(flyable, 1, "no state without a flyable mech (seed " + seed + ", sortie " + sortie + ")");
                    AssertConserved(e);
                }
            }
        }
    }
}
