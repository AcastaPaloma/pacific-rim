// T-123: economy tuning overlay, floor-hull rules (no wreck credit, loss levy) and the scrap path, over the real data files. Engine-free (dotnet and Unity EditMode).
using System;
using System.Collections.Generic;
using System.IO;
using Game.Composition;
using Game.Core;
using Game.Hangar;
using Game.Meta.Economy;
using Game.Meta.Roster;
using Game.Meta.Salvage;
using NUnit.Framework;

namespace Game.Composition.Tests
{
    public class MetaTuningTests
    {
        static readonly PlayerSlot S0 = PlayerSlot.FromIndex(0);

        sealed class Errors : ICoreErrorSink
        {
            public readonly List<CoreError> List = new List<CoreError>();
            public void Report(CoreError e) => List.Add(e);
        }

        sealed class Record : IBodyDamageRecord
        {
            public readonly List<PartDamage> Parts = new List<PartDamage>();
            public Record(PlayerSlot slot, params (string id, double integrity)[] parts)
            { Slot = slot; foreach (var p in parts) Parts.Add(new PartDamage { PartId = p.id, Integrity = p.integrity, MaxIntegrity = 100 }); }
            public PlayerSlot Slot { get; }
            public void CopyParts(List<PartDamage> into) { into.Clear(); into.AddRange(Parts); }
            public void ApplyParts(IReadOnlyList<PartDamage> parts) { Parts.Clear(); foreach (var p in parts) Parts.Add(p); }
        }

        static string ConfigRoot()
        {
            foreach (var start in new[] { TestContext.CurrentContext.TestDirectory, Environment.CurrentDirectory })
            {
                var d = new DirectoryInfo(start);
                while (d != null && !File.Exists(Path.Combine(d.FullName, "Assets", "Game", "Config", "FeatureFlags", "Meta.Refit.json"))) d = d.Parent;
                if (d != null) return Path.Combine(d.FullName, "Assets", "Game", "Config");
            }
            Assert.Fail("repo root not found");
            return null;
        }

        static FeatureFlagSnapshot AllOn() => new FeatureFlagSnapshot(new List<KeyValuePair<string, bool>>
        {
            new KeyValuePair<string, bool>("meta.economy", true), new KeyValuePair<string, bool>("meta.salvage", true),
            new KeyValuePair<string, bool>("meta.permadeath", true), new KeyValuePair<string, bool>("meta.refit", true),
        });

        sealed class World
        {
            public ServiceRegistry Reg = new ServiceRegistry(); public EventBus Bus; public SaveSystem Saves = new SaveSystem("t123");
            public Errors Err = new Errors(); public HangarService Hangar = new HangarService(HangarSites.CrissyField); public MetaHandle Meta;
            public World(Func<string, string> read = null, long funds = 1_000_000_000_000L)
            {
                Bus = new EventBus(Err, 1000); Reg.Register<IEventBus>(Bus);
                var inputs = MetaInputs.FromDirectory(ConfigRoot(), funds);
                if (read != null) inputs.ReadConfig = read;
                Meta = MetaInstaller.Install(Reg, AllOn(), Err, inputs, Saves, Hangar);
            }
        }

        static string ReadFile(string rel) => File.ReadAllText(Path.Combine(ConfigRoot(), rel));
        static Record Wreck() => new Record(S0, ("body", 0), ("cockpit_torso", 0), ("reactor", 0));

        [Test]
        public void TuningFile_IsAppliedOnInstall_AndKeepsBaseFile()
        {
            var w = new World();
            Assert.That(w.Err.List, Is.Empty);
            Assert.That(w.Meta.Tuning, Is.Not.Null);
            Assert.That(w.Meta.Prices.FeeBps, Is.EqualTo(w.Meta.Tuning.FeeBps));
            Assert.That(w.Meta.Prices.FeeBps, Is.Not.EqualTo(PriceTable.Parse(ReadFile(MetaInputs.PricesPath)).FeeBps), "the tuned fee differs from the R-104 draft value in the base file");
            Assert.That(w.Meta.Tuning.FeeAssumed && w.Meta.Tuning.AdvanceAssumed && w.Meta.Tuning.LevyAssumed, Is.True, "every tuned value stays marked Assumed until sourced");
        }

        [Test]
        public void MissingTuningFile_IsNotAnError_BaseValuesStay()
        {
            var w = new World(rel => rel == MetaInputs.TuningPath ? throw new FileNotFoundException(rel) : ReadFile(rel));
            Assert.That(w.Err.List, Is.Empty);
            Assert.That(w.Meta.Tuning, Is.Null);
            Assert.That(w.Meta.Prices.FeeBps, Is.EqualTo(PriceTable.Parse(ReadFile(MetaInputs.PricesPath)).FeeBps));
        }

        [Test]
        public void MalformedTuningFile_IsReported_BaseValuesStay()
        {
            var w = new World(rel => rel == MetaInputs.TuningPath ? "{\"schema\":\"economy_tuning@v1\",\"parameters\":{}}" : ReadFile(rel));
            Assert.That(w.Err.List.Count, Is.EqualTo(1));
            Assert.That(w.Meta.Tuning, Is.Null);
            Assert.That(w.Meta.Economy, Is.Not.Null);
        }

        [Test]
        public void Parse_RejectsBadValues()
        {
            string good = ReadFile(MetaInputs.TuningPath);
            Assert.DoesNotThrow(() => EconomyTuning.Parse(good));
            Assert.Throws<FormatException>(() => EconomyTuning.Parse(good.Replace("economy_tuning@v1", "economy_tuning@v9")));
            Assert.Throws<FormatException>(() => EconomyTuning.Parse(good.Replace("\"fee_bps\": { \"value\": 860", "\"fee_bps\": { \"value\": 0")));
            Assert.Throws<FormatException>(() => EconomyTuning.Parse(good.Replace("\"fee_bps\": { \"value\": 860", "\"fee_bps\": { \"value\": 20000")));
            Assert.Throws<FormatException>(() => EconomyTuning.Parse(good.Replace("\"assumed\": true", "\"assumed\": 1")));
        }

        [Test]
        public void FloorHull_Loss_PaysNoWreckCredit_AndIsBilledTheLevyOnce()
        {
            var w = new World();
            var b = w.Meta.Bridge;
            b.CreateMech(S0, "balanced", 0);
            var first = b.OnFightEnd(Wreck(), 10);
            Assert.That(first.Roster.Lost && first.Roster.FloorGranted, Is.True);
            Assert.That(first.Salvage, Is.Not.Null, "a purchased mech's wreck still pays its (capped) credit");
            long fundsBefore = w.Meta.Economy.Funds;
            long levy = w.Meta.Tuning.FloorLossLevyUsd(w.Meta.Prices);
            Assert.That(levy, Is.EqualTo(w.Meta.Prices.ChassisPriceUsd(w.Meta.Prices.FloorChassis) * w.Meta.Tuning.FloorLossLevyBps / 10000));
            Assert.That(levy, Is.GreaterThan(0));

            var floorId = w.Meta.Roster.ActiveMech(S0);
            Assert.That(w.Meta.Roster.Get(floorId).IsFloor, Is.True);
            var second = b.OnFightEnd(Wreck(), 20);
            Assert.That(second.Roster.Lost, Is.True);
            Assert.That(second.Salvage, Is.Null, "the free hull is city property: no wreck credit");
            Assert.That(w.Meta.Salvage.IsClaimed(second.Roster.WreckId), Is.False);
            Assert.That(w.Meta.Economy.Funds, Is.EqualTo(fundsBefore - levy));
            Assert.That(w.Meta.Roster.FlyableCount(S0), Is.EqualTo(1), "the next floor hull is granted regardless of funds");
            Assert.That(w.Meta.Economy.Arrears, Is.EqualTo(0));
            Assert.That(w.Meta.Roster.ActiveMech(S0), Is.Not.EqualTo(floorId), "the lost hull is replaced by a new floor hull, the levy was billed for the lost one only");
        }

        [Test]
        public void FloorHull_Loss_WithEmptyTreasury_BecomesArrears_NeverBlocksTheGrant()
        {
            var w = new World(funds: 0);
            var b = w.Meta.Bridge;
            b.CreateMech(S0, "balanced", 0);
            b.OnFightEnd(Wreck(), 10);                               // purchased mech lost, a capped credit lands, floor granted
            long credit = w.Meta.Salvage.TotalCreditUsd;
            var second = b.OnFightEnd(Wreck(), 20);                  // floor hull lost
            long levy = w.Meta.Tuning.FloorLossLevyUsd(w.Meta.Prices);
            Assert.That(w.Meta.Economy.Funds, Is.GreaterThanOrEqualTo(0));
            Assert.That(w.Meta.Economy.Funds + w.Meta.Economy.Arrears, Is.EqualTo(Math.Max(0, credit - levy) + Math.Max(0, levy - credit)));
            Assert.That(w.Meta.Roster.FlyableCount(S0), Is.EqualTo(1));
            Assert.That(second.Roster.FloorGranted, Is.True);
        }

        [Test]
        public void ScrapMech_PaysAtMostTheCap_FloorCannotBeScrapped_ReplayChangesNothing()
        {
            var w = new World();
            var b = w.Meta.Bridge;
            var m = b.CreateMech(S0, "balanced", 0);
            long price = w.Meta.Prices.ChassisPriceUsd("balanced");
            long fundsBefore = w.Meta.Economy.Funds;
            var healthy = new Record(S0, ("body", 100), ("cockpit_torso", 100), ("reactor", 100));
            var r = b.ScrapMech(m.MechId, healthy, 5);
            Assert.That(r.Roster.Lost, Is.True); Assert.That(r.Roster.Cause, Is.EqualTo(LossCause.Scrapped));
            Assert.That(r.Roster.FloorGranted, Is.True, "scrapping the last flyable mech grants the floor hull (Q-206)");
            Assert.That(r.Salvage, Is.Not.Null);
            long credit = w.Meta.Economy.Funds - fundsBefore;
            Assert.That(credit, Is.EqualTo(r.Salvage.CreditUsd));
            Assert.That(credit, Is.LessThanOrEqualTo(price * 12 / 100), "scrapping returns at most the 12% wreck cap: never worth it");
            Assert.That(credit, Is.GreaterThan(0));
            long after = w.Meta.Economy.Funds;
            var again = b.ScrapMech(m.MechId, healthy, 6);
            Assert.That(again.Roster.Replay, Is.True); Assert.That(w.Meta.Economy.Funds, Is.EqualTo(after));

            var floorId = w.Meta.Roster.ActiveMech(S0);
            Assert.Throws<InvalidOperationException>(() => b.ScrapMech(floorId, healthy, 7), "the floor hull cannot be scrapped");
            Assert.Throws<ArgumentNullException>(() => b.ScrapMech(floorId, null, 7));
        }

        [Test]
        public void ScrapMech_UsesZeroFallEnergy_SoItPaysAtLeastAWreckThatFell()
        {
            var w = new World();
            var m = w.Meta.Bridge.CreateMech(S0, "balanced", 0);
            var healthy = new Record(S0, ("body", 100), ("cockpit_torso", 100), ("reactor", 100));
            var spec = w.Meta.Bridge.BuildWreck(w.Meta.Roster.Get(m.MechId), healthy, 77, SalvageLocation.Land, 0.0);
            var fell = w.Meta.Bridge.BuildWreck(w.Meta.Roster.Get(m.MechId), healthy, 78, SalvageLocation.Land);
            Assert.That(spec.FallEnergyJPerKg, Is.EqualTo(0.0));
            Assert.That(fell.FallEnergyJPerKg, Is.GreaterThan(0.0));
            Assert.That(w.Meta.Salvage.Assess(spec).CreditUsd, Is.GreaterThanOrEqualTo(w.Meta.Salvage.Assess(fell).CreditUsd));
        }
    }
}
