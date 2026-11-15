// T-123: economy_tuning@v1 (campaign-simulation overlay). New file; EconomyTests.cs is untouched and keeps reading the untuned R-104 base table.
using System;
using System.IO;
using NUnit.Framework;
using Game.Meta.Economy;

namespace Game.Meta.Economy.Tests
{
    public class EconomyTuningTests
    {
        static string TuningText() => File.ReadAllText(Path.Combine(Kit.Root(), "Assets", "Game", "Config", "Economy", "economy_tuning@v1.json"));

        [Test]
        public void CommittedFile_Parses_AndEveryValueIsMarkedAssumed()
        {
            var t = EconomyTuning.Parse(TuningText());
            Assert.AreEqual(860, t.FeeBps); Assert.AreEqual(1000, t.AdvanceBps); Assert.AreEqual(600, t.FloorLossLevyBps);
            Assert.IsTrue(t.FeeAssumed && t.AdvanceAssumed && t.LevyAssumed, "no tuned value has a source yet: all stay Assumed");
        }

        [Test]
        public void ApplyTo_ChangesOnlyFeeAndAdvance_AndPayoutFollowsTheFee()
        {
            var baseTable = Kit.Prices();
            var tuned = Kit.Prices();
            EconomyTuning.Parse(TuningText()).ApplyTo(tuned);
            Assert.AreEqual(600, baseTable.FeeBps); Assert.AreEqual(860, tuned.FeeBps);
            Assert.AreEqual(baseTable.PayoutCapUsd, tuned.PayoutCapUsd); Assert.AreEqual(baseTable.FailureFloorBps, tuned.FailureFloorBps);
            Assert.AreEqual(baseTable.ChassisPriceUsd("balanced"), tuned.ChassisPriceUsd("balanced"));
            // R-104 typical contract: V = $1.2B, 75% saved: 0.086 x 0.9B = $77.4M (base table: $54M)
            Assert.AreEqual(54_000_000L, baseTable.Payout(1_200_000_000L, 300_000_000L));
            Assert.AreEqual(77_400_000L, tuned.Payout(1_200_000_000L, 300_000_000L));
            Assert.LessOrEqual(tuned.Payout(6_450_000_000L, 0), tuned.PayoutCapUsd, "the cap still binds");
        }

        [Test]
        public void FloorLossLevy_IsAShareOfTheFloorChassisPrice()
        {
            var t = EconomyTuning.Parse(TuningText());
            var prices = Kit.Prices();
            Assert.AreEqual(331_400_000L * 600 / 10000, t.FloorLossLevyUsd(prices));
            Assert.AreEqual(19_884_000L, t.FloorLossLevyUsd(prices));
        }

        [Test]
        public void Levy_ThroughPayUpkeep_ShortfallBecomesArrears_FundsStayNonNegative()
        {
            var prices = Kit.Prices();
            var t = EconomyTuning.Parse(TuningText());
            var e = new EconomyService(prices, null, 5_000_000L);
            long levy = t.FloorLossLevyUsd(prices);
            long paid = e.PayUpkeep(levy, 1);
            Assert.AreEqual(5_000_000L, paid); Assert.AreEqual(0, e.Funds); Assert.AreEqual(levy - paid, e.Arrears);
            e.Credit(100_000_000L, TransactionReason.ContractPayout, 2);   // later income clears the arrears first
            Assert.AreEqual(0, e.Arrears); Assert.AreEqual(100_000_000L - (levy - paid), e.Funds);
            Assert.AreEqual(e.TotalCredited - e.TotalDebited, e.Funds);
        }

        [Test]
        public void Parse_RejectsMissingFlagsSourcesAndRange()
        {
            string good = TuningText();
            Assert.Throws<FormatException>(() => EconomyTuning.Parse("{}"));
            Assert.Throws<FormatException>(() => EconomyTuning.Parse(good.Replace("\"floor_loss_levy_bps\"", "\"levy\"")));
            Assert.Throws<FormatException>(() => EconomyTuning.Parse(good.Replace("\"floor_loss_levy_bps\": { \"value\": 600", "\"floor_loss_levy_bps\": { \"value\": 9000")));
            Assert.Throws<FormatException>(() => EconomyTuning.Parse(good.Replace("\"advance_bps\": { \"value\": 1000, \"assumed\": true,", "\"advance_bps\": { \"value\": 1000,")));
            Assert.Throws<ArgumentNullException>(() => EconomyTuning.Parse(null));
        }
    }
}
